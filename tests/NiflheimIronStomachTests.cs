// ============================================================================
//  Homestead progression — COOKING / IRON STOMACH tests (T018, US4).
// ----------------------------------------------------------------------------
//  Exercises the engine-free T018 Cooking node-3 vertical slice (link-compiled
//  from ../src): the pure FoodRefreshThresholdProvider that translates a
//  character's DURABLE Iron Stomach acquisition into the vanilla food
//  refresh/replacement threshold.
//
//  The load-bearing distinction from T017 Field Prep: Iron Stomach is a
//  PERMANENT Effect (data-model.md §"Fixed first-build roster": Cooking | 1 |
//  Iron Stomach | Permanent Effect | personal Offered), and per data-model.md
//  §CharacterProgression "Permanent Effects and Progression Keys survive
//  relationship loss and Tree revocation." So — unlike the Field Prep Character
//  Effect, which goes dormant the instant the caller's relationship drops — the
//  Iron Stomach threshold is keyed on the character's DURABLE purchase record
//  (outcome class PermanentEffect), independent of the current relationship,
//  the Settlement Local policy, build Permission, and even the node's current
//  development state on the Stone (Tree revocation removes development yet the
//  Permanent Effect survives). Once acquired it delivers the raised threshold
//  forever, and it re-derives identically after a process restart from the same
//  persisted purchase.
//
//  Contract (contracts.md §Cooking "FoodRefreshThresholdProvider: Iron Stomach
//  supplies threshold 0.75, highest applicable provider wins; three slots and
//  normal food debit remain"; spec §US4 sc1 "Iron Stomach permanently permits
//  food refresh/replacement at 75% remaining"): the provider supplies the
//  candidate threshold 0.75, composes with other candidate thresholds by taking
//  the HIGHEST applicable one (vanilla baseline 0.5), and answers whether a food
//  at a given remaining fraction may be refreshed under the resolved threshold.
//  It is a THRESHOLD provider ONLY — it preserves the three food slots and the
//  normal food debit/stats/duration; it authors and mutates none of them.
//
//  Named acceptance closed here (tasks.md T018):
//    AT-IRON-STOMACH-75  a durably-acquired Iron Stomach Permanent Effect raises
//                        the food refresh/replacement threshold to 0.75 (refresh
//                        permitted at 75% remaining), the highest applicable
//                        provider wins over the vanilla baseline, the raised
//                        threshold survives relationship loss and a restart, and
//                        the three slots + normal debit/stats/duration are
//                        preserved untouched. The EatFood-level refresh/DEBIT path
//                        (ShouldRefreshOnEat) is covered too: the ACTUAL in-world
//                        refresh — not just the outer CanEat gate — happens in the
//                        0.5..0.75 band for an acquirer and NEVER for a
//                        non-acquirer (so the food is never debited-without-refresh),
//                        vanilla is never lowered, and the gate and refresh path
//                        agree across the band (node-own live-QA remediation
//                        t_6b73a3de: gate-only tests could not catch the seam gap).
// ============================================================================

using SBPR.Niflheim.HomesteadStones.Adapters.Cooking;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using System.Collections.Generic;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimIronStomachTests
    {
        private readonly WorldId _world = new WorldId("uid:is-018");
        private readonly StoneId _stone;

        private readonly AccountId _account = new AccountId("acct-cook");
        private readonly CharacterId _character = new CharacterId("char-cook");

        private static readonly VersionedId Cooking = HomesteadProgressionCatalog.CookingTree;
        private static readonly VersionedId IronStomach = new VersionedId("IronStomach", 1);
        private static readonly VersionedId FieldPrep = new VersionedId("FieldPrep", 1);

        private readonly FoodRefreshThresholdProvider _provider = new FoodRefreshThresholdProvider();

        public NiflheimIronStomachTests()
        {
            _stone = StoneId.FromHostZone(_world, 4, 2);
        }

        // A character holding an optional DURABLE Iron Stomach Permanent-Effect purchase at this Stone.
        // The outcome class "PermanentEffect" is exactly what PurchaseNode stamps for a Permanent Effect
        // node (NodePurchases.OutcomeClassOf → NodeOutcomeType.PermanentEffect).
        private CharacterProgressionAggregate BuildCharacter(bool ironStomachAcquired,
            bool fieldPrepAlso = false)
        {
            var purchases = new List<NodePurchaseRecord>();
            if (ironStomachAcquired)
                purchases.Add(new NodePurchaseRecord(Cooking, IronStomach, "ap:personal",
                    "PermanentEffect", VersionedId.None, "op-buy-is"));
            if (fieldPrepAlso)
                purchases.Add(new NodePurchaseRecord(Cooking, FieldPrep, "ap:personal",
                    "CharacterEffect", VersionedId.None, "op-buy-fp"));

            var stoneRecord = new CharacterStoneRecord(_stone, 3, 3, 1, null,
                purchases.Count > 0 ? purchases.ToArray() : null, null);
            return new CharacterProgressionAggregate(_account, _character,
                "world-scope", 1, 2, 2, "receipt", new[] { stoneRecord });
        }

        // ── AT-IRON-STOMACH-75 ─────────────────────────────────────────────────

        [Fact]
        public void AcquiredIronStomach_RaisesThresholdTo075()
        {
            var character = BuildCharacter(ironStomachAcquired: true);

            var cap = _provider.Resolve(character);

            Assert.True(cap.Acquired);
            Assert.Equal(FoodRefreshThresholdProvider.IronStomachThreshold, cap.Threshold);
            Assert.Equal(0.75, cap.Threshold);
        }

        [Fact]
        public void WithoutIronStomach_ThresholdIsVanillaBaseline()
        {
            var character = BuildCharacter(ironStomachAcquired: false);

            var cap = _provider.Resolve(character);

            Assert.False(cap.Acquired);
            Assert.Equal(FoodRefreshThresholdProvider.VanillaBaselineThreshold, cap.Threshold);
            Assert.Equal(0.5, cap.Threshold);
        }

        [Fact]
        public void HighestApplicableProviderWins_IronStomachOverBaseline()
        {
            // "highest applicable provider wins" — composing the vanilla baseline (0.5) with the Iron
            // Stomach candidate (0.75) resolves to the maximum, 0.75.
            var character = BuildCharacter(ironStomachAcquired: true);

            double resolved = _provider.ResolveThreshold(character,
                FoodRefreshThresholdProvider.VanillaBaselineThreshold);

            Assert.Equal(0.75, resolved);
        }

        [Fact]
        public void HighestApplicableProviderWins_NeverLowersAStrongerBaseline()
        {
            // Composition takes the HIGHEST applicable threshold: a hypothetical stronger baseline
            // provider (0.9) is never lowered by the Iron Stomach 0.75 candidate.
            var character = BuildCharacter(ironStomachAcquired: true);

            double resolved = _provider.ResolveThreshold(character, baselineThreshold: 0.9);

            Assert.Equal(0.9, resolved);
        }

        [Fact]
        public void Compose_TakesTheMaximumCandidate()
        {
            Assert.Equal(0.75, FoodRefreshThresholdProvider.Compose(0.5, 0.75));
            Assert.Equal(0.75, FoodRefreshThresholdProvider.Compose(0.75, 0.5, 0.6));
            Assert.Equal(0.9, FoodRefreshThresholdProvider.Compose(0.5, 0.75, 0.9));
            // No candidates → the safe vanilla baseline, never a fabricated grant.
            Assert.Equal(FoodRefreshThresholdProvider.VanillaBaselineThreshold,
                FoodRefreshThresholdProvider.Compose());
        }

        [Fact]
        public void CanRefreshAt75PercentRemaining_OnlyWithIronStomach()
        {
            // A food with 0.75 of its duration remaining: vanilla (threshold 0.5) forbids a refresh,
            // Iron Stomach (threshold 0.75) permits it exactly at 75% remaining.
            var vanilla = BuildCharacter(ironStomachAcquired: false);
            var withIron = BuildCharacter(ironStomachAcquired: true);

            Assert.False(_provider.CanRefresh(vanilla, remainingFraction: 0.75));
            Assert.True(_provider.CanRefresh(withIron, remainingFraction: 0.75));
        }

        [Fact]
        public void CanRefreshAt74PercentRemaining_UnderBothThresholds()
        {
            // Below the vanilla baseline (0.5) both refresh; between the thresholds (0.5..0.75) only
            // Iron Stomach refreshes; above 0.75 neither does.
            var vanilla = BuildCharacter(ironStomachAcquired: false);
            var withIron = BuildCharacter(ironStomachAcquired: true);

            // Well below both — both can refresh.
            Assert.True(_provider.CanRefresh(vanilla, 0.30));
            Assert.True(_provider.CanRefresh(withIron, 0.30));

            // Between baseline and Iron Stomach — only Iron Stomach refreshes.
            Assert.False(_provider.CanRefresh(vanilla, 0.60));
            Assert.True(_provider.CanRefresh(withIron, 0.60));

            // Above Iron Stomach's threshold — neither refreshes (the food is too fresh).
            Assert.False(_provider.CanRefresh(vanilla, 0.90));
            Assert.False(_provider.CanRefresh(withIron, 0.90));
        }

        [Fact]
        public void ThresholdSurvivesRelationshipLoss()
        {
            // The Permanent Effect is DURABLE: the character aggregate carries the purchase with NO
            // relationship input at all. Releasing a relationship never removes the purchase record, so
            // re-deriving from the same persisted character yields the raised threshold every time —
            // this is the exact behavior that distinguishes a Permanent Effect from the Field Prep
            // Character Effect (data-model.md "Permanent Effects ... survive relationship loss").
            var character = BuildCharacter(ironStomachAcquired: true);

            // Iron Stomach's Resolve takes NO authority/relationship argument — durability is structural.
            Assert.True(_provider.Resolve(character).Acquired);
            Assert.Equal(0.75, _provider.Resolve(character).Threshold);
        }

        [Fact]
        public void ThresholdSurvivesRestart_RoundTripsThroughSerializedCharacter()
        {
            // Restart recovery: the character aggregate rehydrates from its serialized form and the
            // durable Iron Stomach purchase survives, so the provider re-derives the identical raised
            // threshold with zero writes.
            var character = BuildCharacter(ironStomachAcquired: true);
            var restored = CharacterProgressionAggregate.Deserialize(character.Serialize());

            var cap = _provider.Resolve(restored);
            Assert.True(cap.Acquired);
            Assert.Equal(0.75, cap.Threshold);
        }

        [Fact]
        public void ThresholdSurvivesTreeRevocationOfDevelopment()
        {
            // A Permanent Effect survives Tree revocation (data-model.md): the provider keys ONLY on the
            // character's durable purchase, never on the Stone's current node-development state, so a
            // revoked/undeveloped Stone does not strip the raised threshold. (Modeled directly: the
            // provider takes no Stone aggregate — there is no development conjunct to lose.)
            var character = BuildCharacter(ironStomachAcquired: true);
            Assert.True(_provider.Resolve(character).Acquired);
        }

        [Fact]
        public void OnlyPermanentEffectPurchaseCounts_NotACharacterEffect()
        {
            // A Field Prep (Character Effect) purchase must NOT be mistaken for Iron Stomach: the
            // provider matches the exact Iron Stomach node identity and never grants the threshold from
            // an unrelated node's purchase.
            var character = BuildCharacter(ironStomachAcquired: false, fieldPrepAlso: true);

            var cap = _provider.Resolve(character);
            Assert.False(cap.Acquired);
            Assert.Equal(FoodRefreshThresholdProvider.VanillaBaselineThreshold, cap.Threshold);
        }

        [Fact]
        public void PreservesThreeSlotsAndNormalDebitStatsDuration()
        {
            // The provider is a threshold provider ONLY: it never reduces the three food slots and never
            // alters the normal food debit, stats, or duration (contracts.md "three slots and normal
            // food debit remain"). These invariants hold whether or not Iron Stomach is acquired.
            foreach (var acquired in new[] { true, false })
            {
                var cap = _provider.Resolve(BuildCharacter(ironStomachAcquired: acquired));
                Assert.Equal(3, cap.FoodSlots);
                Assert.True(cap.PreservesNormalDebitStatsDuration);
            }
        }

        [Fact]
        public void RemainingFractionAtOrBelowThreshold_IsRefreshable_BoundaryInclusive()
        {
            // The threshold is inclusive at the boundary: "refresh/replacement at 75% remaining" means a
            // food AT exactly 0.75 remaining may be refreshed under Iron Stomach.
            var withIron = BuildCharacter(ironStomachAcquired: true);
            Assert.True(_provider.CanRefresh(withIron, 0.75));
            Assert.False(_provider.CanRefresh(withIron, 0.7500001));
        }

        // ── AT-IRON-STOMACH-75 refresh/DEBIT path (EatFood-level) ──────────────
        //
        // The node-own live QA (t_6b73a3de) proved that the CanEat gate + the 14
        // provider tests above only exercise the OUTER gate — never the vanilla
        // Player.EatFood refresh path, which re-checks its own hardcoded 0.5
        // Food.CanEatAgain() inner guard before it resets m_time/health/stamina/
        // eitr. In the 0.5..0.75 band that inner guard stays false, so EatFood
        // silently no-ops while Humanoid.ConsumeItem still debits the food. The
        // remediation transpiles EatFood to route that inner guard through
        // ShouldRefreshOnEat; these tests pin the EXACT decision that seam
        // delegates here — the gate and the refresh path must AGREE, and vanilla
        // must never be lowered.

        [Fact]
        public void ShouldRefreshOnEat_VanillaWouldRefresh_AlwaysTrue_NeverLowered()
        {
            // When vanilla's own guard already permits the refresh (food below the
            // vanilla 0.5 threshold), the seam must return true UNCONDITIONALLY —
            // even without Iron Stomach, and even at a fraction ABOVE the Iron
            // Stomach threshold (a nonsensical input that can only mean vanilla
            // said yes). We never lower vanilla.
            var vanilla = BuildCharacter(ironStomachAcquired: false);
            var withIron = BuildCharacter(ironStomachAcquired: true);

            Assert.True(_provider.ShouldRefreshOnEat(vanilla, 0.40, vanillaWouldRefresh: true));
            Assert.True(_provider.ShouldRefreshOnEat(withIron, 0.40, vanillaWouldRefresh: true));
            Assert.True(_provider.ShouldRefreshOnEat(vanilla, 0.99, vanillaWouldRefresh: true));
        }

        [Fact]
        public void ShouldRefreshOnEat_InBand_OnlyIronStomachRefreshes()
        {
            // The exact defect band: vanilla refused (0.5..0.75, so vanillaWouldRefresh
            // is false). Without Iron Stomach the refresh must NOT happen — this is the
            // "item debited but not refreshed" trap the seam must NOT create. WITH Iron
            // Stomach the in-band refresh IS permitted, matching the raised CanEat gate.
            var vanilla = BuildCharacter(ironStomachAcquired: false);
            var withIron = BuildCharacter(ironStomachAcquired: true);

            Assert.False(_provider.ShouldRefreshOnEat(vanilla, 0.60, vanillaWouldRefresh: false));
            Assert.True(_provider.ShouldRefreshOnEat(withIron, 0.60, vanillaWouldRefresh: false));
        }

        [Fact]
        public void ShouldRefreshOnEat_AtBoundary075_Inclusive_DeniedJustAbove()
        {
            // Boundary-inclusive at 0.75 (refresh AT 75% remaining), denied just above —
            // identical semantics to the CanEat gate so the two seams cannot disagree.
            var withIron = BuildCharacter(ironStomachAcquired: true);

            Assert.True(_provider.ShouldRefreshOnEat(withIron, 0.75, vanillaWouldRefresh: false));
            Assert.False(_provider.ShouldRefreshOnEat(withIron, 0.7500001, vanillaWouldRefresh: false));
        }

        [Fact]
        public void ShouldRefreshOnEat_AboveIronStomachThreshold_NeitherRefreshes()
        {
            // A food too fresh for even Iron Stomach (above 0.75) and above vanilla —
            // vanilla refused and the raise does not apply, so no refresh for anyone.
            var vanilla = BuildCharacter(ironStomachAcquired: false);
            var withIron = BuildCharacter(ironStomachAcquired: true);

            Assert.False(_provider.ShouldRefreshOnEat(vanilla, 0.90, vanillaWouldRefresh: false));
            Assert.False(_provider.ShouldRefreshOnEat(withIron, 0.90, vanillaWouldRefresh: false));
        }

        [Fact]
        public void ShouldRefreshOnEat_AgreesWithCanEatGate_AcrossTheBand()
        {
            // The whole point of the remediation: for an ACQUIRER, the EatFood refresh
            // decision and the raised-gate decision (CanRefresh, threshold 0.75) must
            // return the SAME answer for a food vanilla refused, so a refresh the gate
            // promised actually lands. vanillaWouldRefresh=false models "vanilla's inner
            // guard said no" (fraction at/above 0.5), which is exactly where the raise
            // does its work.
            var withIron = BuildCharacter(ironStomachAcquired: true);

            foreach (var frac in new[] { 0.50, 0.60, 0.74, 0.75, 0.7500001, 0.90 })
            {
                Assert.Equal(_provider.CanRefresh(withIron, frac),
                    _provider.ShouldRefreshOnEat(withIron, frac, vanillaWouldRefresh: false));
            }
        }

        [Fact]
        public void ShouldRefreshOnEat_NonAcquirer_NeverRefreshesWhenVanillaRefused()
        {
            // Load-bearing: unlike the provider's own CanRefresh (which uses the 0.5
            // baseline threshold and would say true at exactly 0.50), the EatFood seam
            // DEFERS to the actual vanilla verdict handed in. When vanilla refused
            // (vanillaWouldRefresh=false), a non-acquirer NEVER refreshes — this is what
            // prevents the "item debited without refresh" trap. This is precisely why the
            // seam takes vanilla's own verdict rather than recomputing the baseline.
            var vanilla = BuildCharacter(ironStomachAcquired: false);

            foreach (var frac in new[] { 0.50, 0.60, 0.74, 0.75, 0.90 })
                Assert.False(_provider.ShouldRefreshOnEat(vanilla, frac, vanillaWouldRefresh: false));
        }

        [Fact]
        public void NoneCapability_IsVanillaBaselineAndInert()
        {
            var none = FoodRefreshCapability.None;
            Assert.False(none.Acquired);
            Assert.Equal(FoodRefreshThresholdProvider.VanillaBaselineThreshold, none.Threshold);
            Assert.Equal(3, none.FoodSlots);
            Assert.True(none.PreservesNormalDebitStatsDuration);
        }
    }
}
