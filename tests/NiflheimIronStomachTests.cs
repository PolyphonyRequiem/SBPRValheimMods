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
//                        preserved untouched.
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

        [Fact]
        public void NoneCapability_IsVanillaBaselineAndInert()
        {
            var none = FoodRefreshCapability.None;
            Assert.False(none.Acquired);
            Assert.Equal(FoodRefreshThresholdProvider.VanillaBaselineThreshold, none.Threshold);
            Assert.Equal(3, none.FoodSlots);
            Assert.True(none.PreservesNormalDebitStatsDuration);
        }

        // ── EatFood INNER-GUARD disposition (the shipped defect's fix) ──────────────────────────────────
        //
        //  These pin the engine-free decision the net48 Player.EatFood PREFIX delegates to. The shipped
        //  T018 only rescued the OUTER Player.CanEat; vanilla Player.EatFood then independently re-checks
        //  Food.CanEatAgain() (remaining < 0.5) inside its same-food branch and refuses the refresh above
        //  50% remaining — so a durable Iron Stomach at 60% remaining had CanEat=True but EatFood=False,
        //  and Humanoid.ConsumeItem debited the item anyway (no-loss violation). DecideEat says exactly when
        //  the seam must perform the refresh in place of that refused vanilla branch. Control at 40% (below
        //  0.5) must PASS THROUGH — vanilla already refreshes there, the seam must never double-handle it.

        [Fact]
        public void DecideEat_InRaisedBand_WithIronStomach_RescuesTheRefresh()
        {
            // The exact live-QA failure: 60% remaining, durable Iron Stomach, matching food already in a
            // slot. Vanilla EatFood refuses (0.60 !< 0.5); the seam must rescue and perform the refresh so
            // the debited item is not lost.
            var withIron = BuildCharacter(ironStomachAcquired: true);

            var d = _provider.DecideEat(withIron, matchingFoodPresent: true, remainingFraction: 0.60);

            Assert.Equal(IronStomachEatDisposition.RescueSameFoodRefresh, d);
        }

        [Fact]
        public void DecideEat_InRaisedBand_WithoutIronStomach_PassesThroughToVanilla()
        {
            // Fail-closed: no durable Iron Stomach ⇒ the seam must NOT rescue; vanilla's 0.5 refusal stands
            // (and CanEat was never rescued, so ConsumeItem never debits — no loss).
            var vanilla = BuildCharacter(ironStomachAcquired: false);

            var d = _provider.DecideEat(vanilla, matchingFoodPresent: true, remainingFraction: 0.60);

            Assert.Equal(IronStomachEatDisposition.PassThroughToVanilla, d);
        }

        [Fact]
        public void DecideEat_BelowVanillaBaseline_PassesThroughToVanilla_NoDoubleRefresh()
        {
            // The CONTROL from the QA log: 40% remaining. Vanilla EatFood ALREADY refreshes below 0.5, so
            // the seam must pass through and let vanilla handle it — never a second refresh, never a second
            // debit. Holds with or without Iron Stomach.
            foreach (var acquired in new[] { true, false })
            {
                var character = BuildCharacter(ironStomachAcquired: acquired);
                var d = _provider.DecideEat(character, matchingFoodPresent: true, remainingFraction: 0.40);
                Assert.Equal(IronStomachEatDisposition.PassThroughToVanilla, d);
            }
        }

        [Fact]
        public void DecideEat_AboveIronStomachThreshold_PassesThroughToVanilla_VanillaDenies()
        {
            // Above 0.75 the food is too fresh: the outer CanEat is NOT rescued, so CanConsumeItem denies
            // and no item is debited. The EatFood prefix must also pass through so vanilla's deny stands —
            // no rescue, no mutation.
            var withIron = BuildCharacter(ironStomachAcquired: true);

            var d = _provider.DecideEat(withIron, matchingFoodPresent: true, remainingFraction: 0.90);

            Assert.Equal(IronStomachEatDisposition.PassThroughToVanilla, d);
        }

        [Fact]
        public void DecideEat_BandBoundaries_AreExact()
        {
            var withIron = BuildCharacter(ironStomachAcquired: true);

            // At exactly the vanilla baseline (0.5): vanilla's Food.CanEatAgain() is STRICT (m_time <
            // burn/2), so at exactly 0.5 vanilla does NOT refresh — yet the outer CanEat postfix DID rescue
            // it (0.5 <= 0.75). To avoid a no-loss gap the seam must own this boundary and rescue it.
            Assert.Equal(IronStomachEatDisposition.RescueSameFoodRefresh,
                _provider.DecideEat(withIron, true, 0.50));
            // Just above baseline: the seam owns it.
            Assert.Equal(IronStomachEatDisposition.RescueSameFoodRefresh,
                _provider.DecideEat(withIron, true, 0.5000001));
            // Just below baseline: vanilla already refreshes — pass through, no double-handling.
            Assert.Equal(IronStomachEatDisposition.PassThroughToVanilla,
                _provider.DecideEat(withIron, true, 0.4999999));
            // At exactly the Iron Stomach threshold (0.75): inclusive — the seam rescues ("refresh at 75%").
            Assert.Equal(IronStomachEatDisposition.RescueSameFoodRefresh,
                _provider.DecideEat(withIron, true, 0.75));
            // Just above 0.75: too fresh — pass through, vanilla denies.
            Assert.Equal(IronStomachEatDisposition.PassThroughToVanilla,
                _provider.DecideEat(withIron, true, 0.7500001));
        }

        [Fact]
        public void DecideEat_NoMatchingFood_PassesThroughToVanilla_SlotsUntouched()
        {
            // No matching food already in a slot ⇒ this is vanilla's new-food / three-slot path, which the
            // seam NEVER touches (three slots preserved, no fourth). Even with Iron Stomach in the raised
            // band, the disposition is pass-through.
            var withIron = BuildCharacter(ironStomachAcquired: true);

            var d = _provider.DecideEat(withIron, matchingFoodPresent: false, remainingFraction: 0.60);

            Assert.Equal(IronStomachEatDisposition.PassThroughToVanilla, d);
        }

        [Fact]
        public void DecideEat_RescueBand_SurvivesRelationshipLoss_AndRestart()
        {
            // The rescue is a Permanent-Effect decision: it keys on the durable purchase alone. It holds
            // after a serialized restart round-trip (durability is structural), matching the provider's
            // Resolve durability guarantees — so the inner-guard fix inherits the same durability.
            var character = BuildCharacter(ironStomachAcquired: true);
            var restored = CharacterProgressionAggregate.Deserialize(character.Serialize());

            Assert.Equal(IronStomachEatDisposition.RescueSameFoodRefresh,
                _provider.DecideEat(restored, matchingFoodPresent: true, remainingFraction: 0.60));
        }

        [Fact]
        public void DecideEat_CapabilityOverload_AgreesWithCharacterOverload()
        {
            // The capability-first overload (what the seam uses when it already resolved the capability) and
            // the character-first convenience overload must agree for the same inputs.
            var withIron = BuildCharacter(ironStomachAcquired: true);
            var cap = _provider.Resolve(withIron);

            foreach (var frac in new[] { 0.30, 0.50, 0.60, 0.75, 0.90 })
            {
                Assert.Equal(
                    _provider.DecideEat(cap, true, frac),
                    _provider.DecideEat(withIron, true, frac));
            }
        }

        // ── EXACT-BOUNDARY: non-acquirer vs acquirer at 0.5 (the R2 remediation focus) ──────────────────
        //
        //  Load-bearing regression: BOTH the outer Player.CanEat postfix and the inner Player.EatFood prefix
        //  now route through DecideEat (single authority) so they agree at the vanilla baseline boundary. A
        //  NON-acquirer at EXACTLY 0.5 must PRESERVE vanilla's STRICT refusal (Food.CanEatAgain is m_time <
        //  burn/2 → false at exactly 0.5). The earlier defect: the postfix used the raw INCLUSIVE
        //  FoodRefreshCapability.CanRefresh (None.Threshold == 0.5, CanRefresh is <=), which wrongly rescued a
        //  non-acquirer at 0.5 to TRUE — CanConsumeItem would then debit the item with no refresh. DecideEat
        //  returns PassThroughToVanilla for every non-acquirer at every fraction, closing that gap.

        [Fact]
        public void DecideEat_NonAcquirer_AtExactBaseline050_PassesThrough_PreservesVanillaStrictRefusal()
        {
            // The exact remediation boundary: a NON-acquirer at exactly 0.5. Vanilla refuses strictly; the
            // seam must NOT rescue (no debit-without-refresh). This is the case the raw inclusive CanRefresh
            // path got wrong.
            var vanilla = BuildCharacter(ironStomachAcquired: false);

            Assert.Equal(IronStomachEatDisposition.PassThroughToVanilla,
                _provider.DecideEat(vanilla, matchingFoodPresent: true, remainingFraction: 0.50));
        }

        [Fact]
        public void DecideEat_NonAcquirer_AtEveryBandFraction_AlwaysPassesThrough()
        {
            // Fail-closed across the whole range: without a durable Iron Stomach the seam NEVER rescues,
            // regardless of remaining fraction. Vanilla's own 0.5 threshold is the only authority, and the
            // outer CanEat is never rescued so no item is debited above 0.5.
            var vanilla = BuildCharacter(ironStomachAcquired: false);

            foreach (var frac in new[] { 0.4999999, 0.50, 0.5000001, 0.60, 0.75, 0.7500001, 0.90 })
                Assert.Equal(IronStomachEatDisposition.PassThroughToVanilla,
                    _provider.DecideEat(vanilla, matchingFoodPresent: true, frac));
        }

        [Fact]
        public void DecideEat_Acquirer_AtExactBaseline050_Rescues_InclusiveLowerBound()
        {
            // The mirror of the above: an ACQUIRED owner at exactly 0.5 gets the inclusive raised band and
            // MUST rescue (the outer CanEat DID rescue at 0.5 <= 0.75; the inner guard must complete the
            // refresh so the single debit is not wasted). Together with the non-acquirer test this pins the
            // full acceptance clause: non-acquirer strict-refuse at 0.5, acquirer inclusive 0.5..0.75.
            var withIron = BuildCharacter(ironStomachAcquired: true);

            Assert.Equal(IronStomachEatDisposition.RescueSameFoodRefresh,
                _provider.DecideEat(withIron, matchingFoodPresent: true, remainingFraction: 0.50));
        }

        [Fact]
        public void FoodRefreshCapability_None_CanRefreshAt050_IsInclusive_ButDecideEatGuardsIt()
        {
            // Documents WHY the outer postfix must NOT call FoodRefreshCapability.CanRefresh directly: None's
            // inclusive CanRefresh is TRUE at exactly 0.5 (threshold 0.5, <=), which would rescue a
            // non-acquirer. DecideEat is the guard that gates on Acquired FIRST, so it stays pass-through.
            var none = FoodRefreshCapability.None;
            Assert.True(none.CanRefresh(0.50));   // inclusive — the trap the postfix must not fall into.

            Assert.Equal(IronStomachEatDisposition.PassThroughToVanilla,
                _provider.DecideEat(none, matchingFoodPresent: true, remainingFraction: 0.50));
        }
    }
}
