// ============================================================================
//  RD-T001 (M0) — Resource Delivery current-truth and conformance guard tests.
// ----------------------------------------------------------------------------
//  Exercises the engine-free ResourceDeliveryConformanceGuard link-compiled from
//  ../src (see the .csproj). This is the implementation-side baseline for the
//  merged Resource Delivery spec/plan/data-model/contracts (PR #327), and closes:
//
//    AT-RD-023  First-slice Mirrored Stone AP telemetry equals the actual floored
//               Personal/Cumulative award, across replay — Resource Delivery never
//               reads/debits it (spec RD-023, data-model §Aggregate 5).
//    AT-RD-024  The proposed content/contract shapes are mechanically distinguishable
//               from the shipped 20/13/7 truth, the same-PR reconciliation obligations
//               are enumerated, and this guard enables NO Resource Delivery behavior
//               (spec RD-024, plan M0 exit).
//
//  These tests DO NOT change gameplay: they assert the shipped roster is unchanged
//  and the proposed 21/14/7 target remains ProposedNotYetImplemented. The rest of the
//  798-test Homestead suite stays green alongside them.
// ============================================================================

using SBPR.Niflheim.HomesteadStones.Domain.Content;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class ResourceDeliveryConformanceGuardTests
    {
        private static ContentRegistryValidator Validator() =>
            new ContentRegistryValidator(new HomesteadProgressionCatalog());

        // ── AT-RD-024: proposed vs shipped are mechanically distinguishable ──

        [Fact]
        public void AtRd024_ShippedRoster_IsTheLive20_13_7Truth()
        {
            var shipped = ResourceDeliveryConformanceGuard.ShippedRoster;
            Assert.Equal(20, shipped.Authored);
            Assert.Equal(13, shipped.Executable);
            Assert.Equal(7, shipped.Unavailable);
            Assert.True(shipped.IsArithmeticallyConsistent);
        }

        [Fact]
        public void AtRd024_ProposedRoster_IsThe21_14_7Target()
        {
            var proposed = ResourceDeliveryConformanceGuard.ProposedResourceDeliveryRoster;
            Assert.Equal(21, proposed.Authored);
            Assert.Equal(14, proposed.Executable);
            Assert.Equal(7, proposed.Unavailable);
            Assert.True(proposed.IsArithmeticallyConsistent);
        }

        [Fact]
        public void AtRd024_ProposedAndShipped_AreNotEqual()
        {
            // The whole point of RD-T001: the two shapes can never be confused.
            Assert.NotEqual(
                ResourceDeliveryConformanceGuard.ShippedRoster,
                ResourceDeliveryConformanceGuard.ProposedResourceDeliveryRoster);
        }

        [Fact]
        public void AtRd024_ProposedTarget_IsProposedNotYetImplemented()
        {
            Assert.Equal(
                ContentTruthState.ProposedNotYetImplemented,
                ResourceDeliveryConformanceGuard.ProposedRosterState);
        }

        [Fact]
        public void AtRd024_ProposedSupersessionShape_IsWellFormed()
        {
            // Adds exactly one authored + one executable node (Humble), holds unavailable at 7,
            // stays arithmetically consistent, and remains a proposed shape. Throws on any drift.
            ResourceDeliveryConformanceGuard.AssertProposedSupersessionShape();
        }

        [Fact]
        public void AtRd024_Humble_IsTheSingleFoundationalNodeAt1Bp()
        {
            Assert.Equal("HumbleHomesteadersBundle", ResourceDeliveryConformanceGuard.HumbleNodeKey);
            Assert.Equal(1, ResourceDeliveryConformanceGuard.HumbleAuthoredBpCost);
        }

        [Fact]
        public void AtRd024_GuardEnablesNoBehavior_LiveRosterStaysShipped()
        {
            // The guard's mere presence must not move the live roster off 20/13/7.
            ResourceDeliveryConformanceGuard.AssertShippedRosterUnchanged(Validator());
        }

        [Fact]
        public void AtRd024_ReconciliationObligations_EnumerateEveryLaterSamePrSurface()
        {
            var obligations = ResourceDeliveryConformanceGuard.ReconciliationObligations;
            Assert.NotEmpty(obligations);

            // Every obligation is named and non-empty — the boundary is enumerated, not remembered.
            foreach (var o in obligations)
            {
                Assert.False(string.IsNullOrWhiteSpace(o.Surface));
                Assert.NotNull(o.Detail);
            }

            // The load-bearing surfaces a behavior slice must move together are all present.
            var surfaces = new System.Collections.Generic.HashSet<string>();
            foreach (var o in obligations) surfaces.Add(o.Surface);
            Assert.Contains("HomesteadProgressionCatalog.roster", surfaces);
            Assert.Contains("ContentRegistryValidator.AssertRosterInvariant", surfaces);
            Assert.Contains("Mirrored Stone AP telemetry", surfaces);
        }

        // ── AT-RD-023: Mirrored telemetry == floored award ──

        [Theory]
        // baseAp, participation(0/1/2), maturityNum, maturityDen, expected floor
        [InlineData(1, 0, 10, 10, 0)]   // 0× participation -> no award
        [InlineData(1, 1, 10, 10, 1)]   // 1× * 1.0× = 1
        [InlineData(1, 2, 10, 10, 2)]   // 2× * 1.0× = 2
        [InlineData(1, 1, 11, 10, 1)]   // 1 * 1.1× = 1.1 -> floor 1
        [InlineData(1, 2, 11, 10, 2)]   // 2 * 1.1× = 2.2 -> floor 2
        [InlineData(3, 2, 15, 10, 9)]   // 3 * 2 * 1.5× = 9.0 -> 9
        [InlineData(7, 1, 13, 10, 9)]   // 7 * 1.3× = 9.1 -> floor 9
        [InlineData(5, 2, 14, 10, 14)]  // 5 * 2 * 1.4× = 14.0 -> 14
        public void AtRd023_FlooredAward_FloorsOnceAfterFullMultiplication(
            int baseAp, int participation, int maturityNum, int maturityDen, int expected)
        {
            int award = ResourceDeliveryConformanceGuard.FlooredPersonalApAward(
                baseAp, participation, maturityNum, maturityDen);
            Assert.Equal(expected, award);
        }

        [Fact]
        public void AtRd023_MirroredDelta_EqualsFlooredAward_WhenTelemetryMirrorsCorrectly()
        {
            // 3 * 2 * 1.5× = 9. Correct telemetry mirrors the floored 9.
            Assert.True(ResourceDeliveryConformanceGuard.MirroredDeltaEqualsFlooredAward(
                recordedMirroredDelta: 9, baseAp: 3, participationMultiplier: 2,
                maturityNumerator: 15, maturityDenominator: 10));
        }

        [Fact]
        public void AtRd023_MirroredDelta_RejectsPreFloorValue()
        {
            // 7 * 1.3× = 9.1; a telemetry that mirrored the PRE-floor 9.1 (rounded to 10, say,
            // or any value != the floored 9) must NOT be accepted as equal to the award.
            Assert.False(ResourceDeliveryConformanceGuard.MirroredDeltaEqualsFlooredAward(
                recordedMirroredDelta: 10, baseAp: 7, participationMultiplier: 1,
                maturityNumerator: 13, maturityDenominator: 10));
        }

        [Fact]
        public void AtRd023_MirroredDelta_RejectsDoubledTelemetry()
        {
            // Award is floor(2 * 1 * 1.1×) = 2; a doubled telemetry of 4 is not equal.
            Assert.False(ResourceDeliveryConformanceGuard.MirroredDeltaEqualsFlooredAward(
                recordedMirroredDelta: 4, baseAp: 2, participationMultiplier: 1,
                maturityNumerator: 11, maturityDenominator: 10));
        }

        [Fact]
        public void AtRd023_MirroredDelta_ForZeroParticipation_IsZero()
        {
            // 0× participation -> no award, and the telemetry mirror must be 0 too.
            Assert.True(ResourceDeliveryConformanceGuard.MirroredDeltaEqualsFlooredAward(
                recordedMirroredDelta: 0, baseAp: 5, participationMultiplier: 0,
                maturityNumerator: 15, maturityDenominator: 10));
            Assert.False(ResourceDeliveryConformanceGuard.MirroredDeltaEqualsFlooredAward(
                recordedMirroredDelta: 1, baseAp: 5, participationMultiplier: 0,
                maturityNumerator: 15, maturityDenominator: 10));
        }

        [Fact]
        public void AtRd023_FlooredAward_IsIdempotentUnderReplay()
        {
            // The award is a pure function of its inputs, so "replay" (recomputing with the
            // same snapshot inputs) yields the same floored value every time — the property
            // the receipt store relies on to return the recorded award on replay.
            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(9, ResourceDeliveryConformanceGuard.FlooredPersonalApAward(3, 2, 15, 10));
            }
        }
    }
}
