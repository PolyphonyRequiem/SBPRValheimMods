// Real owner-local loopback control server (ADR-0009 §2, §3.2, §5.2, §5.3) — M2R runtime wiring.
//
// This is the ACTUAL client-role control channel: a System.Net.Sockets.TcpListener bound
// EXPLICITLY to the loopback address (127.0.0.1) only, never 0.0.0.0. It enforces the
// owner-local bind policy (loopback peer + per-session operator token), reads exactly one
// length-prefixed request frame (LoopbackFrameParser), and hands the payload to the
// helper's OWN main-thread pump for admission/execution — it NEVER touches the game from
// the socket thread, and shares no Terminal/ScriptTools/ValBridge lock (§5.2).
//
// It is engine-free (System.* only): no UnityEngine/BepInEx/Valheim reference, so it
// link-compiles into the net8 xUnit suite and a test can open a real client socket, prove
// a remote (non-loopback) bind is refused, prove a partial/oversized/wrong-token frame is
// rejected, and prove one-connection/one-request slot semantics — all without a game.
//
// Concurrency model: a single background accept thread owns the socket; it enqueues each
// well-formed request onto a thread-safe queue and BLOCKS on a per-request completion until
// the main-thread pump (PumpOnce) processes it and supplies a receipt. One connection and
// one request in flight at a time (single slot). No sleeps in the hot path; bounded reads
// with deadlines guard against a stalled peer.
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace SBPR.QaHarness.T022.Core.ControlPlane
{
    /// <summary>A request read off the loopback socket, awaiting a main-thread receipt.</summary>
    public sealed class PendingLoopbackRequest
    {
        private readonly ManualResetEventSlim _done = new(false);
        private volatile string _receiptJson = string.Empty;

        public string Payload { get; }

        public PendingLoopbackRequest(string payload) { Payload = payload ?? string.Empty; }

        /// <summary>Called by the main-thread pump with the serialized receipt; unblocks the socket thread.</summary>
        public void Complete(string receiptJson)
        {
            _receiptJson = receiptJson ?? string.Empty;
            _done.Set();
        }

        /// <summary>Block (bounded) until the pump completes this request. Returns false on timeout.</summary>
        public bool Wait(int timeoutMs) => _done.Wait(timeoutMs);

        public string ReceiptJson => _receiptJson;
    }

    /// <summary>
    /// Loopback-only TCP control server. Start binds 127.0.0.1; Stop unbinds and joins the
    /// accept thread; the owning pump drains inbound requests via <see cref="PumpOnce"/>.
    /// </summary>
    public sealed class LoopbackControlServer : IDisposable
    {
        /// <summary>Max ms to wait for the full request frame from a connected peer before dropping it.</summary>
        public const int ReadDeadlineMs = 3_000;

        /// <summary>Max ms the socket thread waits for the main-thread pump to produce a receipt.</summary>
        public const int PumpDeadlineMs = 8_000;

        private readonly LoopbackFrameParser _parser = new();
        private readonly LoopbackBindPolicy _bindPolicy;
        private readonly ConcurrentQueue<PendingLoopbackRequest> _inbound = new();
        private readonly object _lifecycle = new();

        private TcpListener? _listener;
        private Thread? _acceptThread;
        private volatile bool _running;

        public LoopbackControlServer(string operatorToken)
        {
            _bindPolicy = new LoopbackBindPolicy(operatorToken);
        }

        /// <summary>The bound loopback port (0 until started). Ephemeral port chosen when Start(0).</summary>
        public int BoundPort { get; private set; }

        /// <summary>True between a successful Start and Stop/Dispose.</summary>
        public bool IsRunning => _running;

        /// <summary>
        /// Bind 127.0.0.1:<paramref name="port"/> (0 = ephemeral) and start the accept thread.
        /// Deliberately IPAddress.Loopback — never IPAddress.Any — so no remote peer can connect.
        /// </summary>
        public void Start(int port = 0)
        {
            lock (_lifecycle)
            {
                if (_running) throw new InvalidOperationException("already running");
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
                BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _running = true;
                _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "SBPRQA-Loopback" };
                _acceptThread.Start();
            }
        }

        /// <summary>Unbind and join the accept thread. Safe to call more than once.</summary>
        public void Stop()
        {
            Thread? toJoin;
            lock (_lifecycle)
            {
                if (!_running) return;
                _running = false;
                try { _listener?.Stop(); } catch (Exception) { /* already torn down */ }
                _listener = null;
                toJoin = _acceptThread;
                _acceptThread = null;
            }
            if (toJoin != null && toJoin.IsAlive) toJoin.Join(2_000);
            // Fail any requests still waiting so their socket threads don't hang.
            while (_inbound.TryDequeue(out var pending))
                pending.Complete(string.Empty);
        }

        /// <summary>
        /// Drain at most one inbound request and hand it to <paramref name="handler"/> (the runtime,
        /// on the main thread). Returns true if a request was processed. Call every pump tick.
        /// </summary>
        public bool PumpOnce(Func<string, string> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!_inbound.TryDequeue(out var pending)) return false;
            string receipt;
            try { receipt = handler(pending.Payload) ?? string.Empty; }
            catch (Exception) { receipt = string.Empty; }
            pending.Complete(receipt);
            return true;
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient? client = null;
                try
                {
                    var listener = _listener;
                    if (listener == null) break;
                    client = listener.AcceptTcpClient();
                    HandleConnection(client);
                }
                catch (SocketException) { /* listener stopped */ }
                catch (ObjectDisposedException) { /* listener stopped */ }
                catch (Exception) { /* never let one bad peer kill the loop */ }
                finally
                {
                    try { client?.Close(); } catch (Exception) { /* ignore */ }
                }
            }
        }

        private void HandleConnection(TcpClient client)
        {
            // Bind-policy: verify the peer is loopback BEFORE reading anything. A non-loopback
            // peer (should be impossible given the loopback bind, but defense in depth) is dropped.
            string remote = "unknown";
            if (client.Client.RemoteEndPoint is IPEndPoint ep)
                remote = ep.Address.ToString();

            client.ReceiveTimeout = ReadDeadlineMs;
            client.NoDelay = true;
            using var stream = client.GetStream();

            // Frame layout: [token frame][request frame]. Both are length-prefixed. The token
            // frame carries the per-session operator token; a wrong/absent token fail-closes
            // before the request frame is ever admitted.
            if (!TryReadFrame(stream, out string tokenPayload))
            {
                WriteReject(stream, ControlPlaneReason.PartialFrame);
                return;
            }
            var bind = _bindPolicy.Admit(remote, tokenPayload);
            if (bind != ControlPlaneReason.None)
            {
                WriteReject(stream, bind);
                return;
            }

            if (!TryReadFrame(stream, out string requestPayload))
            {
                WriteReject(stream, ControlPlaneReason.PartialFrame);
                return;
            }

            // Enqueue for the main-thread pump and block (bounded) for the receipt.
            var pending = new PendingLoopbackRequest(requestPayload);
            _inbound.Enqueue(pending);
            if (!pending.Wait(PumpDeadlineMs))
            {
                WriteReject(stream, ControlPlaneReason.Timeout);
                return;
            }
            WriteFrame(stream, pending.ReceiptJson);
        }

        // Read one length-prefixed frame with a bounded total size and the socket's read deadline.
        private bool TryReadFrame(NetworkStream stream, out string payload)
        {
            payload = string.Empty;
            byte[] header = new byte[LoopbackFrameParser.HeaderBytes];
            if (!ReadExact(stream, header, header.Length)) return false;
            long declared =
                ((long)header[0] << 24) | ((long)header[1] << 16) |
                ((long)header[2] << 8) | header[3];
            if (declared <= 0 || declared > LoopbackFrameParser.MaxPayloadBytes) return false;
            byte[] body = new byte[declared];
            if (!ReadExact(stream, body, body.Length)) return false;
            // Re-run the pure parser over the reassembled frame so the wire path and the tested
            // frame logic share one definition (defensive; body already validated above).
            byte[] whole = new byte[header.Length + body.Length];
            Array.Copy(header, 0, whole, 0, header.Length);
            Array.Copy(body, 0, whole, header.Length, body.Length);
            var parsed = _parser.TryReadFrame(whole, 0, whole.Length);
            if (!parsed.Ok || parsed.Payload == null) return false;
            payload = parsed.Payload;
            return true;
        }

        private static bool ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n;
                try { n = stream.Read(buffer, read, count - read); }
                catch (Exception) { return false; }
                if (n <= 0) return false; // peer closed or timed out
                read += n;
            }
            return true;
        }

        private static void WriteFrame(NetworkStream stream, string payload)
        {
            try
            {
                byte[] frame = LoopbackFrameParser.EncodeFrame(payload ?? string.Empty);
                stream.Write(frame, 0, frame.Length);
                stream.Flush();
            }
            catch (Exception) { /* peer gone */ }
        }

        private static void WriteReject(NetworkStream stream, ControlPlaneReason reason)
            => WriteFrame(stream, "{\"outcome\":\"TransportRejected\",\"reason\":\"" + reason + "\"}");

        public void Dispose() => Stop();
    }
}
