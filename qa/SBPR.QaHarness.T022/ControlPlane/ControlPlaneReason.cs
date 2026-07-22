// Transport / control-plane reject taxonomy (ADR-0009 §3.2, §5.1-§5.3) — M2.
//
// M1 (RejectReason.cs) covers the ARMING GATE and per-request ADMISSION decision
// (nonce/role/world/verb/args/hmac/sequence). This enum is a SEPARATE, additive
// taxonomy for the layer *below* admission: the owner-local loopback frame parser,
// the bind policy, the single-slot dispatcher, the bounded queue, and the
// delivering-peer / connection-generation state model. Keeping it separate leaves
// every shipped M1 contract file byte-identical (no gate is weakened or edited),
// which is the M2-prebuild rule: build ON the reviewed M1 head, never mutate it.
//
// Every negative transport decision names exactly WHY, so a receipt / test can
// assert the precise stage that fired. Fail-closed: the only accepting value is
// <see cref="None"/>.
namespace SBPR.QaHarness.T022.Core.ControlPlane
{
    /// <summary>Why a control-plane (transport-layer) operation was refused. <see cref="None"/> is the only accepting value.</summary>
    public enum ControlPlaneReason
    {
        /// <summary>Not a rejection — the transport stage accepted.</summary>
        None = 0,

        // ── Frame parsing (owner-local loopback JSON framing) ────────────────
        /// <summary>The byte stream is not yet a complete frame — more bytes are required (not an error; caller should read more).</summary>
        PartialFrame,
        /// <summary>The frame length prefix is absent, non-positive, or otherwise unparseable.</summary>
        MalformedFrame,
        /// <summary>The declared frame length exceeds the hard maximum payload cap.</summary>
        OversizedFrame,

        // ── Bind policy (owner-local channel, ADR-0009 §5.3) ─────────────────
        /// <summary>The connecting endpoint is not the loopback address (127.0.0.1 / ::1).</summary>
        NonLoopbackPeer,
        /// <summary>The per-session operator token is missing or does not match.</summary>
        BadOperatorToken,

        // ── Delivering-peer / connection-generation (ADR-0009 §3.2, §5.1) ────
        /// <summary>No delivering peer is bound for this connection (server verbs require a bound actual peer).</summary>
        PeerUnbound,
        /// <summary>The request references a connection generation older than the current one (post-reconnect replay / stale).</summary>
        StaleGeneration,

        // ── Single-slot dispatcher + bounded queue (ADR-0009 §3.2, §5.2) ─────
        /// <summary>A primitive is already in flight and the bounded queue is full — the request is shed.</summary>
        QueueFull,
        /// <summary>A second concurrent request arrived while the single slot was occupied and admission chose not to queue.</summary>
        Busy,
        /// <summary>The in-flight primitive exceeded its per-request deadline; the slot was freed.</summary>
        Timeout,
        /// <summary>The in-flight (or queued) primitive was cancelled by an explicit cancel.</summary>
        Cancelled,
        /// <summary>An operation referenced a requestId the dispatcher does not know (never accepted / already terminal).</summary>
        UnknownRequest,
    }
}
