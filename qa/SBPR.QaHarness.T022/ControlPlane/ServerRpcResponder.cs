// Server-role control responder (ADR-0009 §2, §3.2, §5.1) — M2R runtime wiring.
//
// The dedicated server exposes NO host listener. Server-only control verbs arrive over the
// authenticated per-peer ZRpc that a Harmony postfix on ZNet.OnNewConnection registers on
// each peer's m_rpc (see the engine-bound QaServerRpcBridge). This class is the ENGINE-FREE
// brain that the ZRpc handler delegates to: given the ACTUAL delivering peer id (from the
// transport, never the claimed identity), the claimed connection generation, and an
// execution-time admin/owner recheck result, it validates the delivering-peer/generation
// state (DeliveringPeerState) and then runs the payload through the shared ControlPlaneRuntime.
//
// Engine-free (System.* only) so a test can substitute a fake delivering peer, prove peer
// substitution and stale-generation rejection, and prove the server owns no listener — all
// without a game. The real ZRpc/ZNetPeer binding lives in the thin engine-bound bridge.
using System;

namespace SBPR.QaHarness.T022.Core.ControlPlane
{
    /// <summary>An execution-time authority (admin/owner) recheck, evaluated at the moment a verb runs.</summary>
    public interface IServerAuthorityRecheck
    {
        /// <summary>True iff the delivering peer currently holds admin/owner authority on this server.</summary>
        bool IsAuthorized(string deliveringPeerId);
    }

    /// <summary>A fixed-yes/no admin recheck for tests.</summary>
    public sealed class FakeServerAuthorityRecheck : IServerAuthorityRecheck
    {
        private readonly bool _authorized;
        public FakeServerAuthorityRecheck(bool authorized) { _authorized = authorized; }
        public bool IsAuthorized(string deliveringPeerId) => _authorized;
    }

    /// <summary>
    /// Server-role responder. Binds the actual delivering peer per connection, validates
    /// generation + admin authority at execution, and composes the shared runtime. One
    /// instance per armed server run; the engine-bound bridge feeds it real ZRpc facts.
    /// </summary>
    public sealed class ServerRpcResponder
    {
        private readonly DeliveringPeerState _peerState = new();
        private readonly ControlPlaneRuntime _runtime;
        private readonly IServerAuthorityRecheck _authority;

        public ServerRpcResponder(ArmedState armed, IServerAuthorityRecheck authority, long requestTimeoutMs = 5_000)
        {
            if (armed == null) throw new ArgumentNullException(nameof(armed));
            if (armed.Role != HarnessRole.Server)
                throw new ArgumentException("ServerRpcResponder requires a Server-role armed state", nameof(armed));
            _runtime = new ControlPlaneRuntime(armed, requestTimeoutMs);
            _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        }

        /// <summary>The current bound peer/generation (for tests + audit). Null when unbound.</summary>
        public PeerBinding? BoundPeer => _peerState.Current;

        /// <summary>The current connection generation (0 = never bound).</summary>
        public long Generation => _peerState.Generation;

        /// <summary>
        /// A peer connected (ZNet.OnNewConnection postfix). Bind the ACTUAL delivering peer,
        /// advancing the connection generation. Returns the new generation the runner must echo.
        /// </summary>
        public long OnPeerConnected(string actualPeerId)
        {
            var binding = _peerState.Bind(actualPeerId);
            return binding.Generation;
        }

        /// <summary>A peer disconnected — drop the binding. The next connect advances the generation.</summary>
        public void OnPeerDisconnected() => _peerState.Unbind();

        /// <summary>Advance the dispatcher clock each server pump tick.</summary>
        public void Tick(long nowUnixMs) => _runtime.Tick(nowUnixMs);

        /// <summary>
        /// Handle one inbound server-channel request. <paramref name="deliveringPeerId"/> is the
        /// ACTUAL peer the ZRpc transport delivered from (authoritative). The claimed connection
        /// generation is NOT a caller argument — it is decoded from the authenticated envelope in
        /// <paramref name="payload"/> (part of the HMAC input), so a caller cannot inject a
        /// current-generation value that would make the stale-generation branch unreachable.
        /// Fail-closed order: envelope decode, then peer/generation binding, then execution-time
        /// admin recheck, then the shared admission+dispatch runtime. Every receipt is stamped
        /// with the server's CURRENT generation so the runner can form its next request.
        /// </summary>
        public ControlReceipt Handle(
            string? deliveringPeerId, string? payload, long nowUnixMs)
        {
            long current = _peerState.Generation;

            // 0. Decode the authenticated envelope to recover its CLAIMED generation. A frame
            //    that carries no valid positive generation is malformed (fail-closed).
            var decode = EnvelopeCodec.Decode(payload);
            if (!decode.Ok || decode.Envelope == null)
                return TransportReject(ControlPlaneReason.MalformedFrame, nowUnixMs, status: "malformed-frame")
                    .WithGeneration(current);

            long claimedGeneration = decode.Envelope.ConnectionGeneration;

            // 1. Delivering-peer + generation binding (rejects substitution + post-reconnect replay).
            //    The claimed generation comes from the signed envelope, never a caller argument.
            var admit = _peerState.Validate(deliveringPeerId, claimedGeneration);
            if (!admit.Ok)
                return TransportReject(admit.Reason, nowUnixMs).WithGeneration(current);

            // 2. Execution-time admin/owner recheck (ADR-0009 §5.1 — not just at arm).
            if (!_authority.IsAuthorized(deliveringPeerId!))
                return TransportReject(ControlPlaneReason.PeerUnbound, nowUnixMs, status: "admin-recheck-failed")
                    .WithGeneration(current);

            // 3. Shared fail-closed admission + single-slot dispatch + receipt. The HMAC re-check
            //    inside admission covers the generation too (it is part of the canonical input),
            //    so a generation tampered without re-signing rejects as BadHmac.
            return _runtime.Handle(payload, nowUnixMs).WithGeneration(current);
        }

        private ControlReceipt TransportReject(ControlPlaneReason reason, long nowUnixMs, string status = "transport-rejected")
            => new("", "", ControlOutcome.TransportRejected, reason.ToString(),
                   "Server", _runtime.Armed.World.WorldUid, 0, nowUnixMs, status);
    }
}
