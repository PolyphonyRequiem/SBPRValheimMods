// Owner-local loopback frame parser + bind policy (ADR-0009 §3.2, §5.2, §5.3) — M2.
//
// The GUI-client control surface is a dedicated owner-local loopback TCP/JSON channel
// (127.0.0.1 bind), completely independent of ValBridge / Terminal / ScriptTools —
// its own single-slot dispatcher (this assembly, ControlDispatcher). This file is the
// *pure, engine-free* framing + admission-to-the-channel logic exercised by tests; the
// live socket accept loop is a later, separately-reviewed slice that will FEED bytes to
// this parser and consult this policy. Nothing here opens a socket, binds a port, or
// touches the game — it decides, given (bytes, endpoint facts, token), whether a frame
// is well-formed and whether the connection may talk to the helper at all.
//
// Wire framing is deliberately NOT a streaming JSON reader (net48, no System.Text.Json
// in this SDK-shielded assembly): a frame is a fixed 4-byte big-endian unsigned length
// prefix followed by exactly that many UTF-8 payload bytes. This makes partial/oversized/
// malformed detection unambiguous without a serializer, mirroring RequestHmac's
// "stable manual layout avoids serializer ambiguity" discipline.
using System;

namespace SBPR.QaHarness.T022.Core.ControlPlane
{
    /// <summary>The outcome of trying to read one frame from a byte buffer.</summary>
    public sealed class FrameParse
    {
        /// <summary>Why the read did not yield a complete good frame; <see cref="ControlPlaneReason.None"/> on success.</summary>
        public ControlPlaneReason Reason { get; }

        /// <summary>The decoded UTF-8 payload when <see cref="Reason"/> is None; null otherwise.</summary>
        public string? Payload { get; }

        /// <summary>Total bytes consumed from the buffer (prefix + payload) on success; 0 otherwise.</summary>
        public int BytesConsumed { get; }

        private FrameParse(ControlPlaneReason reason, string? payload, int consumed)
        {
            Reason = reason;
            Payload = payload;
            BytesConsumed = consumed;
        }

        public bool Ok => Reason == ControlPlaneReason.None;

        public static FrameParse Partial() => new(ControlPlaneReason.PartialFrame, null, 0);
        public static FrameParse Reject(ControlPlaneReason reason) => new(reason, null, 0);
        public static FrameParse Complete(string payload, int consumed) => new(ControlPlaneReason.None, payload, consumed);
    }

    /// <summary>
    /// Length-prefixed frame reader + owner-local bind policy. Stateless and pure; the
    /// caller owns the accumulating buffer and re-invokes as more bytes arrive.
    /// </summary>
    public sealed class LoopbackFrameParser
    {
        /// <summary>The fixed frame header: a 4-byte big-endian unsigned payload length.</summary>
        public const int HeaderBytes = 4;

        /// <summary>Hard maximum payload size (bytes). A frame declaring more is rejected outright (anti-DoS, ADR-0009 §5.2 bounded).</summary>
        public const int MaxPayloadBytes = 64 * 1024;

        /// <summary>
        /// Attempt to read exactly one frame from <paramref name="buffer"/> starting at
        /// <paramref name="offset"/>. Returns PartialFrame when more bytes are needed
        /// (not an error), a reject reason on a malformed/oversized header, or a complete
        /// payload with the consumed byte count. Never throws on bad input.
        /// </summary>
        public FrameParse TryReadFrame(byte[]? buffer, int offset, int count)
        {
            if (buffer == null || count < 0 || offset < 0 || offset > buffer.Length)
                return FrameParse.Reject(ControlPlaneReason.MalformedFrame);
            int available = Math.Min(count, buffer.Length - offset);

            // Need the full header before we can know the declared length.
            if (available < HeaderBytes) return FrameParse.Partial();

            long declared =
                ((long)buffer[offset] << 24) |
                ((long)buffer[offset + 1] << 16) |
                ((long)buffer[offset + 2] << 8) |
                buffer[offset + 3];

            // A zero-length payload carries no request; treat as malformed (no empty verbs).
            if (declared <= 0) return FrameParse.Reject(ControlPlaneReason.MalformedFrame);
            if (declared > MaxPayloadBytes) return FrameParse.Reject(ControlPlaneReason.OversizedFrame);

            long total = HeaderBytes + declared;
            if (available < total) return FrameParse.Partial();

            string payload;
            try
            {
                payload = System.Text.Encoding.UTF8.GetString(buffer, offset + HeaderBytes, (int)declared);
            }
            catch (Exception)
            {
                // Encoding.UTF8.GetString does not throw on invalid bytes (it substitutes
                // U+FFFD), but guard defensively so no malformed byte run can crash the pump.
                return FrameParse.Reject(ControlPlaneReason.MalformedFrame);
            }
            return FrameParse.Complete(payload, (int)total);
        }

        /// <summary>Encode a payload string as a length-prefixed frame (for the runner-side symmetry + round-trip tests).</summary>
        public static byte[] EncodeFrame(string payload)
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(payload ?? string.Empty);
            var frame = new byte[HeaderBytes + body.Length];
            uint len = (uint)body.Length;
            frame[0] = (byte)((len >> 24) & 0xFF);
            frame[1] = (byte)((len >> 16) & 0xFF);
            frame[2] = (byte)((len >> 8) & 0xFF);
            frame[3] = (byte)(len & 0xFF);
            Array.Copy(body, 0, frame, HeaderBytes, body.Length);
            return frame;
        }
    }

    /// <summary>
    /// Owner-local bind policy (ADR-0009 §5.3): the loopback channel binds 127.0.0.1 only
    /// and requires the runner's per-session operator token. A connection from any other
    /// address, or a frame carrying a wrong/absent token, is fail-closed rejected before
    /// admission ever runs. Engine-free: the caller supplies the observed remote address
    /// string and the token the frame claimed; the live accept loop wires those in later.
    /// </summary>
    public sealed class LoopbackBindPolicy
    {
        private readonly string _operatorToken;

        public LoopbackBindPolicy(string operatorToken)
        {
            // An empty operator token is a misconfiguration; treat every request as
            // unauthorized rather than silently accepting a blank secret.
            _operatorToken = operatorToken ?? string.Empty;
        }

        /// <summary>True only for the IPv4/IPv6 loopback address (no hostname resolution — exact literal match).</summary>
        public static bool IsLoopback(string? remoteAddress)
        {
            if (string.IsNullOrEmpty(remoteAddress)) return false;
            string a = remoteAddress!.Trim();
            // Strip an optional ":port" suffix on IPv4; leave bracketed IPv6 intact.
            int colon = a.IndexOf(':');
            if (colon > 0 && a.IndexOf(':', colon + 1) < 0 && a[0] != '[')
                a = a.Substring(0, colon);
            return a == "127.0.0.1" || a == "::1" || a == "[::1]";
        }

        /// <summary>
        /// Admit a connection to the channel. Loopback is checked first (a non-local peer
        /// never even gets its token compared), then the operator token in constant time.
        /// </summary>
        public ControlPlaneReason Admit(string? remoteAddress, string? presentedToken)
        {
            if (!IsLoopback(remoteAddress)) return ControlPlaneReason.NonLoopbackPeer;
            if (string.IsNullOrEmpty(_operatorToken)) return ControlPlaneReason.BadOperatorToken;
            if (presentedToken == null) return ControlPlaneReason.BadOperatorToken;
            if (!RequestHmac.Verify(_operatorToken, presentedToken))
                return ControlPlaneReason.BadOperatorToken;
            return ControlPlaneReason.None;
        }
    }
}
