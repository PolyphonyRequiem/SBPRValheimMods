using System;

namespace SBPR.Niflheim.HomesteadStones.Domain.Accounts
{
    // IAP-012 Tracer 4 — configurable positive retention policy (engine-free CLEAN-side core).
    //
    // AIP-FR-023/024: closed-pilot retention uses two configured POSITIVE, BOUNDED periods:
    //   SecurityLogRetentionDays (shipped pilot default 14) and ClosedDataRetentionDays (shipped
    //   pilot default 30). Zero/negative/unbounded is invalid — it can never mean "forever"
    //   (data-model.md Retention model). Every retention policy carries the disclosure/notice version
    //   it was published under so a longer period cannot silently apply to an existing account: an
    //   INCREASE requires a NEW notice version and acknowledgement before it controls; until then the
    //   account's recorded prior policy remains controlling (AT-AIP-RETENTION-INCREASE-RENOTICE). A
    //   DECREASE may apply immediately.
    //
    // net48 audit: System.* only. No UnityEngine/Valheim/BepInEx.

    /// <summary>An immutable, validated retention policy version. Both periods are positive-day counts;
    /// zero/negative are rejected at construction so an unbounded retention can never be configured.</summary>
    public sealed class PilotRetentionPolicy
    {
        /// <summary>Shipped pilot default for authentication/security logs (AIP-FR-023).</summary>
        public const int DefaultSecurityLogRetentionDays = 14;

        /// <summary>Shipped pilot default for closed account/pilot linked data (AIP-FR-024).</summary>
        public const int DefaultClosedDataRetentionDays = 30;

        public PilotRetentionPolicy(string policyVersion, int securityLogRetentionDays, int closedDataRetentionDays)
        {
            if (string.IsNullOrEmpty(policyVersion))
                throw new ArgumentException("Retention policy version must be non-empty.", nameof(policyVersion));
            if (securityLogRetentionDays <= 0)
                throw new ArgumentOutOfRangeException(nameof(securityLogRetentionDays),
                    "SecurityLogRetentionDays must be positive; zero/negative cannot mean forever.");
            if (closedDataRetentionDays <= 0)
                throw new ArgumentOutOfRangeException(nameof(closedDataRetentionDays),
                    "ClosedDataRetentionDays must be positive; zero/negative cannot mean forever.");
            PolicyVersion = policyVersion;
            SecurityLogRetentionDays = securityLogRetentionDays;
            ClosedDataRetentionDays = closedDataRetentionDays;
        }

        public string PolicyVersion { get; }
        public int SecurityLogRetentionDays { get; }
        public int ClosedDataRetentionDays { get; }

        /// <summary>The shipped pilot default policy: 14-day security logs, 30-day closed data.</summary>
        public static PilotRetentionPolicy ShippedDefault(string policyVersion) =>
            new PilotRetentionPolicy(policyVersion, DefaultSecurityLogRetentionDays, DefaultClosedDataRetentionDays);

        /// <summary>True when <paramref name="candidate"/> lengthens EITHER period relative to this
        /// policy. An increase requires a new notice version + acknowledgement before it may control an
        /// existing account (AT-AIP-RETENTION-INCREASE-RENOTICE).</summary>
        public bool IsIncreaseOver(PilotRetentionPolicy candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            return candidate.SecurityLogRetentionDays > SecurityLogRetentionDays ||
                   candidate.ClosedDataRetentionDays > ClosedDataRetentionDays;
        }

        private const long SecondsPerDay = 86_400L;

        /// <summary>The unix-second deadline at which closed data becomes purge-eligible, measured from a
        /// closure/deletion timestamp under this policy's closed-data period.</summary>
        public long ClosedDataPurgeDueAt(long closedAtUnixSeconds) =>
            closedAtUnixSeconds + ClosedDataRetentionDays * SecondsPerDay;

        /// <summary>The unix-second deadline at which a security-log generation becomes purge-eligible.</summary>
        public long SecurityLogPurgeDueAt(long createdAtUnixSeconds) =>
            createdAtUnixSeconds + SecurityLogRetentionDays * SecondsPerDay;
    }

    /// <summary>Applies a proposed retention-policy change against an account's currently recorded
    /// policy and the notice acknowledgement the player has on file. A decrease applies immediately; an
    /// increase applies only when the proposed policy's notice version is acknowledged. This is a pure
    /// decision object — it selects nothing on the player's behalf.</summary>
    public static class RetentionPolicyChangeGate
    {
        public enum Decision { AppliesImmediately, RequiresRenotice, Applies }

        /// <summary>Decide whether <paramref name="proposed"/> may control an account whose current
        /// current controlling policy is <paramref name="current"/>, given the player's acknowledgement.
        /// Increases require an acknowledgement of the proposed policy's own notice version.</summary>
        public static Decision Evaluate(
            PilotRetentionPolicy current, PilotRetentionPolicy proposed,
            DisclosureAcknowledgement acknowledgement, string proposedNoticeVersion)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            if (proposed == null) throw new ArgumentNullException(nameof(proposed));

            if (!current.IsIncreaseOver(proposed))
                return Decision.AppliesImmediately; // decrease or equal — applies now

            // Increase: it may only control the account once the player has acknowledged the NEW notice
            // version that published the longer period.
            return acknowledgement.Satisfies(proposedNoticeVersion) ? Decision.Applies : Decision.RequiresRenotice;
        }
    }
}
