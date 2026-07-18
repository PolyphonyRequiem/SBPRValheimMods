using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery
{
    // RD-T002 (Gate A) — the complete-contributor iff-rule and strongest-link-once selection
    // (spec RD-005 / data-model Aggregate 3 invariants / contracts §ReconcileStoneContribution step 2-3).
    //
    // POLICY (spec RD-005, verbatim intent):
    //   An account contributes at a Stone IF AND ONLY IF it has
    //     (a) an active Bond OR Attunement to that Stone,
    //     (b) at least one QUALIFYING Connection at that Stone (Active + has-sources; a grace-only,
    //         reset, or sourceless Connection does not qualify), and
    //     (c) nonzero current participation (1× or 2×).
    //   It contributes AT MOST ONCE, using its STRONGEST applicable maturity multiplier across all its
    //   qualifying Connections at that Stone. Several links / several Governors never multiply an
    //   account more than once. A solo Bond (no counterpart) contributes nothing because it has no
    //   qualifying Connection; both currently participating sides of a Bonded↔Attuned or Bonded↔Bonded
    //   pair contribute independently.
    //
    // This file is PURE selection over already-resolved snapshots. It does not read Unity state, does
    // not integrate elapsed time (that is IntervalReconciler), and does not decide relationship
    // qualification (that is Tracer 1). It answers exactly: "given this account's relationship flag,
    // participation tier, and its candidate Connections at server time, does it contribute, and with
    // what single effective multiplier?"
    //
    // net48 audit: System.Collections.Generic + value objects. Engine-free; link-compiles into net8 tests.

    /// <summary>Current participation tier at a Stone (data-model Aggregate 2 §Derived participation).
    /// The integer factor is the exact whole multiplier the tier contributes.</summary>
    public enum ParticipationTier
    {
        /// <summary>Weekly upkeep expired/missing — contributes nothing (spec RD-006 <c>0×</c>).</summary>
        None = 0,
        /// <summary>Weekly upkeep current, daily practice not current — <c>1×</c>.</summary>
        Weekly = 1,
        /// <summary>Weekly upkeep current AND daily practice current — <c>2×</c>.</summary>
        WeeklyAndDaily = 2
    }

    public static class ParticipationTiers
    {
        /// <summary>The exact whole participation factor for a tier (0, 1, or 2).</summary>
        public static int Factor(this ParticipationTier tier) => (int)tier;
    }

    /// <summary>Why an account does or does not contribute at a Stone, with the single effective
    /// maturity multiplier selected once when it does. Immutable result value.</summary>
    public readonly struct ContributionEligibility
    {
        private ContributionEligibility(bool contributes, string reasonCode,
            ParticipationTier tier, MaturityMultiplier maturity, string chosenConnectionKey)
        {
            Contributes = contributes;
            ReasonCode = reasonCode;
            Tier = tier;
            Maturity = maturity;
            ChosenConnectionKey = chosenConnectionKey ?? string.Empty;
        }

        /// <summary>True iff the complete iff-rule is satisfied.</summary>
        public bool Contributes { get; }

        /// <summary>Stable reason code. "Applied" when contributing; otherwise the first failed
        /// requirement (contracts §rejection vocabulary): "NoStoneRelationship",
        /// "ConnectionSourceNotQualifying", or "WeeklyUpkeepRequired".</summary>
        public string ReasonCode { get; }

        /// <summary>The participation tier used (None when not contributing on that ground).</summary>
        public ParticipationTier Tier { get; }

        /// <summary>The single strongest maturity multiplier selected across all qualifying Connections
        /// (1.0× default when not contributing).</summary>
        public MaturityMultiplier Maturity { get; }

        /// <summary>The canonical key of the Connection whose maturity was chosen (empty when none).</summary>
        public string ChosenConnectionKey { get; }

        internal static ContributionEligibility Deny(string reason) =>
            new ContributionEligibility(false, reason, ParticipationTier.None,
                ConnectionMaturity.Band0, string.Empty);

        internal static ContributionEligibility Allow(ParticipationTier tier, MaturityMultiplier maturity,
            string chosenKey) =>
            new ContributionEligibility(true, "Applied", tier, maturity, chosenKey);
    }

    public static class ContributionRule
    {
        /// <summary>Evaluate the complete-contributor iff-rule for one account at one Stone and select
        /// its strongest maturity multiplier ONCE (spec RD-005).</summary>
        /// <param name="hasActiveStoneRelationship">Does the account have an active Bond OR Attunement
        /// to this Stone? (Resolved upstream from the authority index / relationship aggregates.)</param>
        /// <param name="tier">The account's current participation tier at this Stone.</param>
        /// <param name="candidateConnections">Every Connection the account is a member of that is scoped
        /// to a source AT THIS STONE. Non-qualifying (grace/reset/sourceless) entries are ignored; the
        /// strongest qualifying one is chosen. Connections not touching this Stone must be filtered by
        /// the caller (see <paramref name="stoneId"/>).</param>
        /// <param name="stoneId">The Stone under evaluation; a candidate qualifies only if it carries an
        /// Active source at this Stone.</param>
        /// <param name="serverTimeSeconds">Current server time for live-age/maturity evaluation.</param>
        public static ContributionEligibility Evaluate(
            bool hasActiveStoneRelationship,
            ParticipationTier tier,
            IReadOnlyList<ConnectionAggregate> candidateConnections,
            StoneId stoneId,
            long serverTimeSeconds)
        {
            // (a) active Bond/Attunement to the Stone.
            if (!hasActiveStoneRelationship)
                return ContributionEligibility.Deny("NoStoneRelationship");

            // (b) at least one QUALIFYING Connection with a source at this Stone. Select the strongest
            //     maturity across all qualifying candidates — once. A solo Bond reaches here with no
            //     qualifying Connection and is denied (spec RD-005 "solo Bond ... contribute nothing").
            bool anyQualifying = false;
            MaturityMultiplier strongest = ConnectionMaturity.Band0;
            string chosenKey = string.Empty;
            long strongestAgeSeconds = -1;

            if (candidateConnections != null)
            {
                foreach (var conn in candidateConnections)
                {
                    if (conn == null) continue;
                    if (!conn.IsContributionQualifying) continue;
                    if (!HasSourceAtStone(conn, stoneId)) continue;

                    // "Strongest" is the greatest maturity multiplier. Because bands are monotonic in
                    // age, comparing the live age selects the strongest band deterministically; ties
                    // resolve to the lexicographically-lower canonical key for replay stability.
                    long age = conn.LiveAgeSeconds(serverTimeSeconds);
                    if (!anyQualifying
                        || age > strongestAgeSeconds
                        || (age == strongestAgeSeconds &&
                            string.CompareOrdinal(conn.Id.CanonicalKey, chosenKey) < 0))
                    {
                        anyQualifying = true;
                        strongestAgeSeconds = age;
                        strongest = conn.MaturityAt(serverTimeSeconds);
                        chosenKey = conn.Id.CanonicalKey;
                    }
                }
            }

            if (!anyQualifying)
                return ContributionEligibility.Deny("ConnectionSourceNotQualifying");

            // (c) nonzero participation.
            if (tier.Factor() <= 0)
                return ContributionEligibility.Deny("WeeklyUpkeepRequired");

            return ContributionEligibility.Allow(tier, strongest, chosenKey);
        }

        private static bool HasSourceAtStone(ConnectionAggregate conn, StoneId stoneId)
        {
            foreach (var s in conn.Sources)
                if (s.StoneId.Equals(stoneId)) return true;
            return false;
        }
    }
}
