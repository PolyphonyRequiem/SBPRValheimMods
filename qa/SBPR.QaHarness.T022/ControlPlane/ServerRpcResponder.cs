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

    /// <summary>
    /// The result of executing an admitted server fixture verb (M3R). Descriptive facts only — a
    /// short status token and the counts the runner correlates, never a product verdict. The
    /// engine-free ServerFixtureExecutor (Fixtures namespace) produces these; the responder maps
    /// them onto a control receipt status string. Kept as a tiny value type so ServerRpcResponder
    /// depends on an interface, not on the engine-bound seam/world.
    /// </summary>
    public sealed class FixtureVerbOutcome
    {
        public FixtureVerbOutcome(bool executed, string status)
        {
            Executed = executed;
            Status = status ?? string.Empty;
        }

        /// <summary>True iff the world op ran (create/cleanup); false iff a gate/map refused it (no world side effect).</summary>
        public bool Executed { get; }

        /// <summary>A bounded, descriptive status token for the receipt (e.g. "fixture-ensured:created=4").</summary>
        public string Status { get; }
    }

    /// <summary>
    /// Executes an admitted server FIXTURE verb (SpawnStation / PlaceVanillaPiece /
    /// GrantVanillaMaterials / their cleanup) through the real vanilla seam behind the
    /// execution-time authority gate. Engine-free interface: the M3R implementation
    /// (ServerFixtureExecutor) composes the crash-safe ledger + seam; the engine-bound
    /// ZNetVanillaFixtureSeam / ZNetServerAuthoritySource are injected into it by the plugin.
    /// A responder with no executor (M2R behaviour / fixture-free tests) simply reports fixtures
    /// as not-implemented, exactly as before.
    /// </summary>
    public interface IServerFixtureVerbExecutor
    {
        /// <summary>True iff <paramref name="verb"/> is a fixture verb this executor handles.</summary>
        bool Handles(string? verb);

        /// <summary>
        /// Run the admitted fixture verb. The request already passed the responder's delivering-peer
        /// + generation binding, the execution-time admin recheck, and the shared M1 admission +
        /// single-slot dispatch — so this only maps the verb to a bounded vanilla plan and drives the
        /// gated, crash-safe lifecycle. The executor re-applies its own authority gate internally
        /// (defence in depth), so a refused recheck performs NO world side effect.
        /// </summary>
        FixtureVerbOutcome Execute(
            string verb, System.Collections.Generic.IReadOnlyDictionary<string, object?> args,
            string deliveringPeerId, long claimedGeneration);
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
        private readonly DeliveringPeerState _peerState;
        private readonly ControlPlaneRuntime _runtime;
        private readonly IServerAuthorityRecheck _authority;
        private readonly IServerFixtureVerbExecutor? _fixtures;

        // Fixture-execution idempotency cache (requestId -> upgraded receipt). The shared runtime
        // caches its OWN pre-execution receipt (NotImplementedInMilestone for a fixture verb); this
        // cache holds the UPGRADED receipt so a genuine replay returns the executed result and the
        // world lifecycle never re-runs. Only populated when a fixture executor is injected.
        private readonly System.Collections.Generic.Dictionary<string, ControlReceipt> _fixtureReceipts =
            new(StringComparer.Ordinal);

        public ServerRpcResponder(ArmedState armed, IServerAuthorityRecheck authority, long requestTimeoutMs = 5_000)
            : this(armed, authority, null, null, requestTimeoutMs)
        {
        }

        public ServerRpcResponder(
            ArmedState armed, IServerAuthorityRecheck authority,
            IServerFixtureVerbExecutor? fixtures, long requestTimeoutMs = 5_000)
            : this(armed, authority, fixtures, null, requestTimeoutMs)
        {
        }

        /// <summary>
        /// Construct the responder. <paramref name="peerState"/> lets the caller SHARE the delivering-
        /// peer/generation state with the M3R fixture executor (so both gate against the same bound
        /// peer + generation); when null a fresh private state is created (M2R / fixture-free behaviour).
        /// </summary>
        public ServerRpcResponder(
            ArmedState armed, IServerAuthorityRecheck authority,
            IServerFixtureVerbExecutor? fixtures, DeliveringPeerState? peerState, long requestTimeoutMs = 5_000)
        {
            if (armed == null) throw new ArgumentNullException(nameof(armed));
            if (armed.Role != HarnessRole.Server)
                throw new ArgumentException("ServerRpcResponder requires a Server-role armed state", nameof(armed));
            _peerState = peerState ?? new DeliveringPeerState();
            _runtime = new ControlPlaneRuntime(armed, requestTimeoutMs);
            _authority = authority ?? throw new ArgumentNullException(nameof(authority));
            _fixtures = fixtures;
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
            ControlReceipt receipt = _runtime.Handle(payload, nowUnixMs).WithGeneration(current);

            // 4. M3R fixture execution. In M2R an admitted fixture verb returned
            //    NotImplementedInMilestone (the slot was taken + freed, no game I/O). With a real
            //    executor injected, that same admitted-and-dispatched fixture verb now runs the
            //    bounded, crash-safe vanilla lifecycle behind the executor's OWN execution-time
            //    authority gate (defence in depth over the recheck above). Only an ADMITTED fixture
            //    verb reaches here — a rejected/replay/busy receipt is returned unchanged, so the
            //    fixture never runs without passing every prior gate.
            if (_fixtures != null &&
                receipt.Outcome == ControlOutcome.NotImplementedInMilestone &&
                _fixtures.Handles(receipt.Verb))
            {
                // Replay: a fixture request already executed returns its cached upgraded receipt,
                // never a second world lifecycle run.
                if (!string.IsNullOrEmpty(receipt.RequestId) &&
                    _fixtureReceipts.TryGetValue(receipt.RequestId, out var prior))
                    return prior;

                var args = decode.Envelope!.Args ?? EmptyArgs;
                var outcome = _fixtures.Execute(
                    receipt.Verb, args, deliveringPeerId!, claimedGeneration);
                receipt = new ControlReceipt(
                    receipt.RequestId, receipt.Verb,
                    outcome.Executed ? ControlOutcome.Ok : ControlOutcome.Rejected,
                    receipt.Reason, receipt.Role, receipt.WorldUid, receipt.Seq, nowUnixMs,
                    outcome.Status, current);
                if (!string.IsNullOrEmpty(receipt.RequestId))
                    _fixtureReceipts[receipt.RequestId] = receipt;
            }

            return receipt;
        }

        private static readonly System.Collections.Generic.Dictionary<string, object?> EmptyArgs =
            new(StringComparer.Ordinal);

        private ControlReceipt TransportReject(ControlPlaneReason reason, long nowUnixMs, string status = "transport-rejected")
            => new("", "", ControlOutcome.TransportRejected, reason.ToString(),
                   "Server", _runtime.Armed.World.WorldUid, 0, nowUnixMs, status);
    }
}
