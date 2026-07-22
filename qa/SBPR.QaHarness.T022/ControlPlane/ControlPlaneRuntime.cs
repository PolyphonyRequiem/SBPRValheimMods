// The engine-free control-plane brain (ADR-0009 §3.2, §5.1, §5.2, §6) — M2R runtime wiring.
//
// This is the single place that turns a raw inbound control payload (already past the
// transport gates — frame parsing + loopback/token bind on the client channel, or
// delivering-peer + generation validation on the server channel) into a primitive
// receipt. It composes the reviewed M1 fail-closed admission gate (RequestAdmission)
// with the M2 single-slot dispatcher (ControlDispatcher) and the M2R envelope codec.
//
// It is CHANNEL-AGNOSTIC and ENGINE-FREE (System.* only): the loopback TcpListener and
// the per-peer ZRpc responder are thin engine-bound shells that each do their own
// transport admission, then hand the payload here. That keeps every game-touching line
// out of this class so it link-compiles into the headless xUnit suite unchanged.
//
// M2R EXECUTION SCOPE: only status/ping/reject. An admitted mutating/observing verb is
// acknowledged NotImplementedInMilestone (the slot is taken and freed, no game I/O) —
// fixtures/actions/observation wiring is a later, separately-reviewed card. Until then
// the runtime performs ZERO game mutation even when fully armed.
using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core.ControlPlane
{
    /// <summary>
    /// Composes admission + dispatch + receipt for one armed run on one channel. Not
    /// thread-safe: the owning channel drives it from a single main-thread pump tick.
    /// </summary>
    public sealed class ControlPlaneRuntime
    {
        // The verbs the M2R milestone can actually execute (status / ping / lifecycle-noop).
        // Everything else in the catalog admits but reports NotImplementedInMilestone.
        private static readonly HashSet<string> ExecutableInM2R =
            new(StringComparer.Ordinal) { "Ping", "Disarm" };

        private readonly ArmedState _armed;
        private readonly RequestAdmission _admission;
        private readonly ControlDispatcher _dispatcher;
        private readonly long _requestTimeoutMs;

        // Receipt idempotency cache (requestId -> receipt): a genuine replay returns the
        // exact prior receipt, never a re-execution (mirrors RequestAdmission's replay cache).
        private readonly Dictionary<string, ControlReceipt> _receipts = new(StringComparer.Ordinal);

        public ControlPlaneRuntime(ArmedState armed, long requestTimeoutMs = 5_000)
        {
            _armed = armed ?? throw new ArgumentNullException(nameof(armed));
            _admission = new RequestAdmission(armed);
            _dispatcher = new ControlDispatcher(maxQueueDepth: 0); // strict single-slot in M2R
            _requestTimeoutMs = requestTimeoutMs > 0 ? requestTimeoutMs : 5_000;
        }

        /// <summary>The armed run this runtime serves.</summary>
        public ArmedState Armed => _armed;

        /// <summary>Advance the dispatcher clock (freeing an expired slot). Called each pump tick.</summary>
        public void Tick(long nowUnixMs) => _dispatcher.Poll(nowUnixMs, _requestTimeoutMs);

        /// <summary>
        /// Handle one already-transport-admitted payload. Returns a serialized receipt.
        /// Fail-closed at every stage; never throws on bad input.
        /// </summary>
        public ControlReceipt Handle(string? payload, long nowUnixMs)
        {
            var decode = EnvelopeCodec.Decode(payload);
            if (!decode.Ok || decode.Envelope == null)
                return Transport("", "", "MalformedFrame", nowUnixMs);

            var env = decode.Envelope!;
            string reqId = env.RequestId ?? string.Empty;
            string verb = env.Verb ?? string.Empty;

            // Receipt-level idempotency: a fully-formed replay returns the cached receipt.
            if (!string.IsNullOrEmpty(reqId) && _receipts.TryGetValue(reqId, out var cached))
                return cached;

            // M1 fail-closed admission (nonce/role/world/verb/manifest/args/expiry/seq/hmac).
            var admit = _admission.Admit(env, nowUnixMs);
            if (admit.IsReplay)
            {
                // Admission saw this id before but we have no receipt (e.g. transport-only prior);
                // echo the original reason as a rejection receipt without re-executing.
                return Remember(reqId, new ControlReceipt(
                    reqId, verb, ControlOutcome.Rejected, admit.Reason.ToString(),
                    RoleToken(_armed.Role), _armed.World.WorldUid, env.Seq, nowUnixMs, "replay"));
            }
            if (!admit.Admitted)
            {
                return Remember(reqId, new ControlReceipt(
                    reqId, verb, ControlOutcome.Rejected, admit.Reason.ToString(),
                    RoleToken(_armed.Role), _armed.World.WorldUid, env.Seq, nowUnixMs, "rejected"));
            }

            // Single-slot dispatch: a second concurrent primitive is shed BUSY (not cached —
            // the caller may retry once the slot frees).
            var offer = _dispatcher.Offer(reqId, nowUnixMs, _requestTimeoutMs);
            if (!offer.Accepted)
            {
                return new ControlReceipt(
                    reqId, verb, ControlOutcome.TransportRejected, offer.Reason.ToString(),
                    RoleToken(_armed.Role), _armed.World.WorldUid, env.Seq, nowUnixMs, "busy");
            }

            // Execute the (bounded) M2R primitive, then free the slot.
            ControlReceipt receipt;
            if (ExecutableInM2R.Contains(verb))
            {
                receipt = new ControlReceipt(
                    reqId, verb, ControlOutcome.Ok, RejectReason.None.ToString(),
                    RoleToken(_armed.Role), _armed.World.WorldUid, env.Seq, nowUnixMs,
                    verb == "Ping" ? "pong" : "disarm-ack");
            }
            else
            {
                receipt = new ControlReceipt(
                    reqId, verb, ControlOutcome.NotImplementedInMilestone, RejectReason.None.ToString(),
                    RoleToken(_armed.Role), _armed.World.WorldUid, env.Seq, nowUnixMs, "not-implemented-m2r");
            }
            _dispatcher.Complete(reqId, nowUnixMs, _requestTimeoutMs);
            return Remember(reqId, receipt);
        }

        private ControlReceipt Remember(string reqId, ControlReceipt receipt)
        {
            if (!string.IsNullOrEmpty(reqId)) _receipts[reqId] = receipt;
            return receipt;
        }

        private ControlReceipt Transport(string reqId, string verb, string reason, long nowUnixMs)
            => new(reqId, verb, ControlOutcome.TransportRejected, reason,
                   RoleToken(_armed.Role), _armed.World.WorldUid, 0, nowUnixMs, "transport-rejected");

        private static string RoleToken(HarnessRole role) => role == HarnessRole.Server ? "Server" : "Client";
    }
}
