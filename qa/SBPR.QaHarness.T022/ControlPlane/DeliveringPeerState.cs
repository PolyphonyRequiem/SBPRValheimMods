// Delivering-peer / connection-generation state model (ADR-0009 §2, §3.2, §5.1) — M2.
//
// The dedicated server exposes NO host listener; server-only fixture verbs arrive over
// authenticated per-peer ZRpc, and the server binds the ACTUAL delivering peer (not a
// claimed identity) and validates against the current connection generation. This file
// is the engine-free bookkeeping for that: which peer is bound, what generation the
// current connection is, and whether an inbound request's claimed generation is current
// or stale (a post-reconnect replay). The live ZRpc/ZNetPeer wiring is a later slice —
// here we model the state and the accept/reject decision only, so tests can prove
// peer-substitution and stale-generation rejection without a game.
//
// "Generation" = a monotonically increasing counter bumped every time a peer (re)binds.
// A request captured on connection N and replayed after the peer reconnected as N+1 is
// rejected StaleGeneration even if its envelope HMAC still verifies, because the bound
// context moved. This is the transport-layer complement to RequestAdmission's
// nonce/sequence idempotency (which guards replays *within* one armed run).
using System;

namespace SBPR.QaHarness.T022.Core.ControlPlane
{
    /// <summary>The immutable identity of a bound delivering peer at a point in time.</summary>
    public sealed class PeerBinding
    {
        /// <summary>The actual delivering peer id the transport observed (never the claimed one).</summary>
        public string PeerId { get; }

        /// <summary>The connection generation this binding belongs to (monotonic, bumped on every (re)bind).</summary>
        public long Generation { get; }

        public PeerBinding(string peerId, long generation)
        {
            PeerId = peerId ?? throw new ArgumentNullException(nameof(peerId));
            Generation = generation;
        }
    }

    /// <summary>The outcome of validating an inbound request against the peer/generation state.</summary>
    public sealed class PeerAdmit
    {
        public ControlPlaneReason Reason { get; }
        public bool Ok => Reason == ControlPlaneReason.None;

        /// <summary>The bound peer the request was validated against, when Ok.</summary>
        public PeerBinding? Bound { get; }

        private PeerAdmit(ControlPlaneReason reason, PeerBinding? bound)
        {
            Reason = reason;
            Bound = bound;
        }

        public static PeerAdmit Accept(PeerBinding bound) => new(ControlPlaneReason.None, bound);
        public static PeerAdmit Reject(ControlPlaneReason reason) => new(reason, null);
    }

    /// <summary>
    /// Single-connection peer/generation state. NOT thread-safe by itself — the dispatcher
    /// owns a single-slot main-thread queue, so all mutation is serialized there. One
    /// instance models one server-side control context (one armed run's peer channel).
    /// </summary>
    public sealed class DeliveringPeerState
    {
        private PeerBinding? _bound;

        // Monotonic generation counter. Starts at 0 (nothing bound); the first bind is
        // generation 1. Never decreases; every (re)bind takes the next value.
        private long _generation;

        /// <summary>The currently bound peer, or null when nothing is bound.</summary>
        public PeerBinding? Current => _bound;

        /// <summary>The current connection generation (0 = never bound).</summary>
        public long Generation => _generation;

        /// <summary>
        /// Bind (or rebind) the actual delivering peer the transport observed. Every call
        /// advances the generation, so a request captured under the previous generation
        /// becomes stale. Returns the new binding.
        /// </summary>
        public PeerBinding Bind(string actualPeerId)
        {
            if (string.IsNullOrEmpty(actualPeerId))
                throw new ArgumentException("actualPeerId must be non-empty", nameof(actualPeerId));
            _generation = checked(_generation + 1);
            _bound = new PeerBinding(actualPeerId!, _generation);
            return _bound;
        }

        /// <summary>Drop the current binding (peer disconnected). Does NOT advance the generation; the next Bind does.</summary>
        public void Unbind() => _bound = null;

        /// <summary>
        /// Validate an inbound request. <paramref name="deliveringPeerId"/> is the ACTUAL
        /// peer the transport delivered from (authoritative); <paramref name="claimedGeneration"/>
        /// is the generation the request envelope asserts. Fail-closed:
        ///   • nothing bound                         => PeerUnbound
        ///   • delivering peer != bound peer         => PeerUnbound (substitution)
        ///   • claimed generation != current         => StaleGeneration
        /// The claimed identity in the request is deliberately ignored; only the delivering
        /// peer the transport observed is trusted (ADR-0009 §5.1 delivering-peer binding).
        /// </summary>
        public PeerAdmit Validate(string? deliveringPeerId, long claimedGeneration)
        {
            if (_bound == null) return PeerAdmit.Reject(ControlPlaneReason.PeerUnbound);
            if (string.IsNullOrEmpty(deliveringPeerId)) return PeerAdmit.Reject(ControlPlaneReason.PeerUnbound);
            if (!string.Equals(deliveringPeerId, _bound.PeerId, StringComparison.Ordinal))
                return PeerAdmit.Reject(ControlPlaneReason.PeerUnbound);
            if (claimedGeneration != _bound.Generation)
                return PeerAdmit.Reject(ControlPlaneReason.StaleGeneration);
            return PeerAdmit.Accept(_bound);
        }
    }
}
