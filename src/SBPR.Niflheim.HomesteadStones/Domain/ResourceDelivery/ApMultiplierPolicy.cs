using System;

namespace SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery
{
    // RD-T005 (Tracer 2) — the pure multiplier-aware AP award policy (spec RD-009 / data-model
    // Aggregate 5 / contracts §RecordApActivity). Named acceptance: AT-RD-009.
    //
    // POLICY (spec RD-009, verbatim intent)
    //   For an OTHERWISE-AUTHORIZED AP-producing event, the final Personal AP award is
    //       floor(authoredBaseAP × participationFactor × strongestMaturityMultiplier)
    //   with a SINGLE final floor after the full exact rational multiplication. The multiplier does NOT
    //   widen the AP source's accepted actor/relationship authorization — that check runs first and
    //   upstream; this policy only scales an award the source already authorized. A missing qualifying
    //   Connection or missing current weekly upkeep yields NO award (participation 0× or the caller not
    //   contributing collapses the award to zero). BP is never multiplied (this policy is not applied to
    //   BP). Cumulative AP Earned records the same final award. The first behavior slice writes the same
    //   final award to the compatibility Mirrored projection as telemetry.
    //
    // EXACT ARITHMETIC (data-model modeling rule 6)
    //   Maturity is an exact rational numerator/denominator (e.g. 1.1× = 11/10). The award is computed
    //   as (base × participationFactor × maturity.Numerator) / maturity.Denominator with ONE integer
    //   floor at the very end, so there is no intermediate rounding and no floating-point drift.
    //
    // net48 audit: System only. Engine-free; link-compiles into the net8 test project.

    /// <summary>The immutable, fully-snapshotted result of one AP award computation (data-model
    /// Aggregate 5 §award receipt). The caller persists every field in the receipt so replay returns the
    /// recorded award without recomputing against later state.</summary>
    public readonly struct ApAwardResult
    {
        public ApAwardResult(long authoredBaseAp, ParticipationTier tier, MaturityMultiplier maturity,
            long finalAward, bool awarded)
        {
            AuthoredBaseAp = authoredBaseAp;
            Tier = tier;
            Maturity = maturity;
            FinalAward = finalAward;
            Awarded = awarded;
        }

        public long AuthoredBaseAp { get; }
        public ParticipationTier Tier { get; }
        public MaturityMultiplier Maturity { get; }

        /// <summary>The final whole-number award after ONE floor. Zero when not awarded.</summary>
        public long FinalAward { get; }

        /// <summary>True when the event produced a positive-or-recorded award. A 0× (no current weekly
        /// upkeep) or non-contributing account is a recorded authorized no-award result: Awarded is
        /// false and FinalAward is 0.</summary>
        public bool Awarded { get; }

        /// <summary>The Mirrored telemetry delta the first slice MUST write — equal to the final award
        /// (spec RD-009 / contracts §RecordApActivity). Never derived independently.</summary>
        public long MirroredTelemetryDelta => FinalAward;
    }

    public static class ApMultiplierPolicy
    {
        /// <summary>Compute the exact multiplier-aware AP award (spec RD-009). The caller has ALREADY
        /// run the source's own actor/relationship authorization and resolved the account's contribution
        /// eligibility (which yields the participation tier and the strongest maturity once). This method
        /// only performs the exact scaling + single floor; it never widens authorization.</summary>
        /// <param name="authoredBaseAp">The AP source's authored base award (non-negative).</param>
        /// <param name="eligibility">The contribution eligibility for this account at this Stone. When it
        /// does not contribute (no relationship, no qualifying Connection, or 0× participation) the award
        /// is a recorded no-award (FinalAward 0, Awarded false).</param>
        public static ApAwardResult Award(long authoredBaseAp, ContributionEligibility eligibility)
        {
            if (authoredBaseAp < 0) throw new ArgumentOutOfRangeException(nameof(authoredBaseAp));

            // Not contributing (0× / no qualifying Connection / no relationship): recorded no-award.
            if (!eligibility.Contributes)
                return new ApAwardResult(authoredBaseAp, ParticipationTier.None,
                    ConnectionMaturity.Band0, 0, awarded: false);

            return Award(authoredBaseAp, eligibility.Tier, eligibility.Maturity);
        }

        /// <summary>Compute the exact multiplier-aware AP award from an explicit tier + maturity. A
        /// <see cref="ParticipationTier.None"/> (0×) collapses the award to a recorded no-award.</summary>
        public static ApAwardResult Award(long authoredBaseAp, ParticipationTier tier, MaturityMultiplier maturity)
        {
            if (authoredBaseAp < 0) throw new ArgumentOutOfRangeException(nameof(authoredBaseAp));

            int participationFactor = tier.Factor();
            if (participationFactor <= 0)
                // 0× (no current weekly upkeep): recorded authorized no-award, creates no AP delta.
                return new ApAwardResult(authoredBaseAp, tier, maturity, 0, awarded: false);

            // Exact rational: (base × participation × maturityNum) / maturityDen, floored ONCE.
            long numerator = authoredBaseAp * participationFactor * maturity.Numerator;
            long finalAward = numerator / maturity.Denominator; // integer division = single floor for non-negatives

            return new ApAwardResult(authoredBaseAp, tier, maturity, finalAward, awarded: true);
        }
    }
}
