// Fail-closed reject taxonomy for the QA-harness arming gate + request admission
// (ADR-0009 §3.2, §5.1). Every negative decision names exactly WHY it failed so a
// receipt / test can assert the specific gate that fired. The gate is AND-composed:
// the decision surfaces the FIRST failing condition in a fixed, documented order so
// results are deterministic.
namespace SBPR.QaHarness.T022.Core
{
    /// <summary>
    /// Why an arm attempt or a request was refused. <see cref="None"/> is the only
    /// accepting value; everything else is a fail-closed rejection.
    /// </summary>
    public enum RejectReason
    {
        /// <summary>Not a rejection — the gate accepted.</summary>
        None = 0,

        // ── Arming gate (ADR-0009 §5.1), evaluated in this order ──────────────
        /// <summary>No explicit arm signal / enable flag was provided. Default-disabled.</summary>
        DisabledByDefault,
        /// <summary>The arm manifest was null, malformed, or failed capability parsing.</summary>
        MalformedManifest,
        /// <summary>Manifest role token was absent or not an exact <see cref="HarnessRole"/>.</summary>
        UnknownRole,
        /// <summary>Actor alias missing/blank — role and actor come from the runner's explicit signal.</summary>
        MissingActor,
        /// <summary>The observed world UID does not exactly equal the manifest world UID.</summary>
        WorldUidMismatch,
        /// <summary>The observed world name does not exactly equal the manifest world name.</summary>
        WorldNameMismatch,
        /// <summary>Hard production deny list hit (Niflheim 2456 / Heistan 2466 / prod marker) — refused even if the allowlist is misconfigured.</summary>
        ProductionWorldDenied,
        /// <summary>The world is not present in the disposable-world allowlist.</summary>
        WorldNotAllowlisted,
        /// <summary>A pinned hash (product/helper/game/BepInEx/Harmony/scenario) drifted from the manifest.</summary>
        HashManifestDrift,
        /// <summary>The run nonce was absent or empty.</summary>
        MissingNonce,
        /// <summary>The manifest expiry is at/after now — expired or non-positive TTL.</summary>
        Expired,
        /// <summary>The capability manifest enumerates no permitted verbs.</summary>
        EmptyCapability,

        // ── Per-request admission (ADR-0009 §3.2), after a successful arm ──────
        /// <summary>The harness is not armed; no mutating verb is exposed.</summary>
        NotArmed,
        /// <summary>Envelope was null or missing a required field.</summary>
        MalformedEnvelope,
        /// <summary>Request nonce does not match the armed run nonce.</summary>
        BadNonce,
        /// <summary>Request role does not match the armed role.</summary>
        RoleMismatch,
        /// <summary>Request worldUid does not match the armed world UID.</summary>
        RequestWorldMismatch,
        /// <summary>The requested verb is unknown to the catalog.</summary>
        UnknownVerb,
        /// <summary>The requested verb is not in the run's capability manifest.</summary>
        OutOfManifest,
        /// <summary>A typed argument is outside its declared bound.</summary>
        OutOfBoundsArg,
        /// <summary>The request HMAC did not verify against the envelope + run secret.</summary>
        BadHmac,
        /// <summary>The request expiry has passed.</summary>
        RequestExpired,
        /// <summary>A (requestId, seq) already seen — replay. The cached receipt is returned instead of re-execution.</summary>
        Replay,
        /// <summary>Sequence number went backwards or repeated a live requestId with different content (conflict).</summary>
        SequenceConflict,
    }
}
