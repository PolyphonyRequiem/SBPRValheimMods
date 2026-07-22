// ============================================================================
//  QA-M4 evidence/adversarial reject taxonomy (ADR-0009 §4, §6, §10) — M4.
// ----------------------------------------------------------------------------
//  M1 RejectReason (Core/RejectReason.cs) covers the arming gate + per-request
//  admission; M2 ControlPlaneReason covers the transport layer. This enum is a
//  SEPARATE, additive taxonomy for the M4 evidence/action layer: the bounded
//  tamper policy, item-fingerprint continuity, and the receipt firewall. Keeping
//  it separate leaves every shipped M1/M2/M3 contract file byte-identical (no
//  earlier gate is weakened or edited), the same build-on-reviewed-head rule the
//  M2/M3 cards followed.
//
//  Every negative decision names exactly WHY it failed so a receipt / test can
//  assert the precise gate that fired. Fail-closed: None is the only accept.
// ============================================================================
namespace SBPR.QaHarness.T022.Core.Evidence
{
    /// <summary>Why an M4 action/observation/tamper primitive was refused. <see cref="None"/> is the only accepting value.</summary>
    public enum EvidenceReason
    {
        /// <summary>Not a rejection — the primitive was accepted.</summary>
        None = 0,

        // ── Bounded tamper policy (ADR-0009 §4, threat T5, PR408 §3.9) ────────
        /// <summary>Tamper targeted an item that is not a tracked run-scoped throwaway (never a legit item/store).</summary>
        TamperItemNotThrowaway,
        /// <summary>The tamper field is not in the static tamperable allowlist.</summary>
        TamperFieldNotAllowlisted,
        /// <summary>The operation would ADD or COPY a field/signature (only replace/remove of an EXISTING key is permitted).</summary>
        TamperWouldAddSignature,
        /// <summary>The tamper field is not currently present, so replace/remove would actually be an add.</summary>
        TamperFieldNotPresent,

        // ── Item-fingerprint continuity (M3 transfer/upgrade, M4 asserts) ────
        /// <summary>The post-hop item is not the same logical tracked item (continuity key changed).</summary>
        ContinuityBroken,
        /// <summary>A transfer's giver and receiver aliases are identical — a self-transfer is not a transfer.</summary>
        SelfTransfer,
        /// <summary>An upgrade did not bump quality by exactly one from the source, or the mapping was otherwise invalid.</summary>
        InvalidUpgradeMapping,

        // ── Receipt firewall / connection-generation (ADR-0009 §6, §10 M4) ───
        /// <summary>A receipt would smuggle a product PASS/FAIL verdict — the helper emits primitive facts only.</summary>
        HelperVerdictForbidden,
        /// <summary>A receipt/cache entry was minted on a connection generation older than the current one (post-reconnect replay).</summary>
        StaleConnectionGeneration,
        /// <summary>A raw signature/token value would leak into evidence (values are digested/redacted, never surfaced raw).</summary>
        RawSecretWouldLeak,
    }
}
