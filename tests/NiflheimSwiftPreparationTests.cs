// ============================================================================
//  Homestead progression — COOKING / SWIFT PREPARATION tests (T019, US3/US4).
// ----------------------------------------------------------------------------
//  Exercises the engine-free T019 Cooking node-4 (Tier-2) vertical slice
//  (link-compiled from ../src):
//    * MenuCraftDurationProvider — the derived provider that reads the T004
//      DerivedActivationView Active bit for the Swift Preparation personal
//      Character-Effect node and, while active, multiplies the vanilla
//      *skill-adjusted* menu-craft duration of ELIGIBLE menu-crafted food by the
//      authored factor 1/3. It applies the factor ONLY after the vanilla Cooking
//      skill adjustment (the input duration IS the already-skill-adjusted value)
//      and ONLY to eligible menu-crafted food; it NEVER completes/instant-finishes
//      a craft (AT-NO-COOKING-COMPLETION) and NEVER mutates a recipe or shared
//      prefab.
//    * The pure Tier-2 same-Tree gate (NodePurchases): Swift Preparation is the
//      sole executable Level-2 node and requires the Field Prep + Iron Stomach
//      prior-Offered Set acquired plus Cooking Tree Level 2 / Active Stone Level 2
//      (AT-COOKING-TIER2). One prior node short ⇒ ineligible; both ⇒ derived
//      same-Tree Attunement Tier Access is 2.
//
//  Named acceptance closed here (tasks.md T019 / plan.md Tracer 5):
//    AT-SWIFT-MENU-ONLY        an active Swift Preparation shortens eligible
//                              menu-crafted food to 1/3 of the vanilla
//                              skill-adjusted duration; ineligible outputs
//                              (non-food, non-Cooking station, non-menu craft)
//                              and a dormant/unpurchased effect keep the full
//                              vanilla duration; the factor is applied AFTER skill.
//    AT-COOKING-TIER2          Swift Preparation's Tier-2 prior-Offered-Set gate
//                              (Field Prep + Iron Stomach) plus Level-2 caps gate
//                              access; derived same-Tree Tier Access reaches 2 only
//                              when both priors are acquired.
//    AT-NO-COOKING-COMPLETION  the factor reduces duration but NEVER to zero /
//                              instant completion for a positive base, and never
//                              fabricates a craft from a non-positive base; Swift
//                              Preparation carries no Tree-completion state.
//
//  Honesty: these are REAL executions of the shipped provider + the shipped T004
//  DerivedActivationView / T013 purchase-tier derivations (all engine-free,
//  link-compiled into the net8 host). They prove the pure delivery + gate grammar;
//  they do NOT prove a joined Valheim client sees the shortened menu-craft timer
//  in-world — that is the node's joined-client artifact
//  (docs/v2/evidence/homestead-progression/tracer-5-cooking/).
// ============================================================================

using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Cooking;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimSwiftPreparationTests
    {
        private readonly WorldId _world = new WorldId("uid:swift-019");
        private readonly StoneId _stone;

        private readonly AccountId _account = new AccountId("acct-swift");
        private readonly CharacterId _character = new CharacterId("char-swift");
        private readonly CharacterId _sibling = new CharacterId("char-sibling");

        private static readonly VersionedId Cooking = HomesteadProgressionCatalog.CookingTree;
        private static readonly VersionedId SwiftPrep = new VersionedId("SwiftPreparation", 1);
        private static readonly VersionedId FieldPrep = new VersionedId("FieldPrep", 1);
        private static readonly VersionedId IronStomach = new VersionedId("IronStomach", 1);

        private readonly HomesteadProgressionCatalog _catalog = new HomesteadProgressionCatalog();
        private readonly MenuCraftDurationProvider _provider = new MenuCraftDurationProvider();

        public NiflheimSwiftPreparationTests()
        {
            _stone = StoneId.FromHostZone(_world, 9, 3);
        }

        // ── Aggregate builders (mirror the Field Prep / Ready Hands harnesses) ──────────────

        // A Stone with the Cooking Tree committed to Level 2 and the three personal Cooking nodes
        // developed + Offered so a purchase can derive Active/Dormant and Tier-2 access.
        private StoneProgressionAggregate BuildStone(
            int treeLevel = 2, int activeStoneLevel = 2,
            bool swiftDeveloped = true, bool swiftOffered = true,
            bool fieldPrepDeveloped = true, bool ironStomachDeveloped = true)
        {
            var committed = new List<CommittedTreeRecord>
            {
                new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                    Cooking, "seed-commit-cooking", _character.Value, treeLevel, 0)
            };

            var development = new List<NodeDevelopmentRecord>();
            if (fieldPrepDeveloped)
                development.Add(new NodeDevelopmentRecord(FieldPrep, 1, 1, true, true, "seed-dev-fp"));
            if (ironStomachDeveloped)
                development.Add(new NodeDevelopmentRecord(IronStomach, 1, 1, true, true, "seed-dev-is"));
            if (swiftDeveloped)
                development.Add(new NodeDevelopmentRecord(SwiftPrep, 1, 1, true, swiftOffered, "seed-dev-swift"));

            return new StoneProgressionAggregate(_stone, revision: 5,
                historicalStoneLevel: 2, activeStoneLevel: activeStoneLevel,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "c", updatedProvenance: "u",
                mirroredStoneAp: 3, lastAppliedReceiptId: "r",
                committedTrees: committed, nodeDevelopment: development);
        }

        // The caller's character with an optional Swift Preparation purchase at this Stone, plus optional
        // prior-set purchases (Field Prep / Iron Stomach) and a Personal AP balance.
        private CharacterProgressionAggregate BuildCharacter(
            CharacterId character,
            bool withSwift = false, bool withFieldPrep = false, bool withIronStomach = false,
            int personalAp = 5)
        {
            var purchases = new List<NodePurchaseRecord>();
            if (withFieldPrep)
                purchases.Add(new NodePurchaseRecord(Cooking, FieldPrep, "PersonalAP", "CharacterEffect", VersionedId.None, "op-buy-fp"));
            if (withIronStomach)
                purchases.Add(new NodePurchaseRecord(Cooking, IronStomach, "PersonalAP", "PermanentEffect", VersionedId.None, "op-buy-is"));
            if (withSwift)
                purchases.Add(new NodePurchaseRecord(Cooking, SwiftPrep, "PersonalAP", "CharacterEffect", VersionedId.None, "op-buy-swift"));

            var stoneRecord = new CharacterStoneRecord(_stone, personalAp, personalAp, 1,
                purchases.Count == 0 ? null : purchases.ToArray(), null);
            return new CharacterProgressionAggregate(_account, character,
                "world-scope", 1, 2, 2, "receipt", new[] { stoneRecord });
        }

        private AccountStoneAuthorityIndex BuildAuthority(CharacterId? activeCharacter)
        {
            var idx = AccountStoneAuthorityIndex.Vacant(_account, _stone);
            if (activeCharacter.HasValue)
                idx = idx.WithReservationAdded(
                    new AuthorityReservation(activeCharacter.Value, RelationshipKind.Attunement,
                        "rel-swift", "relreceipt:seed"), 1);
            return idx;
        }

        private DerivedActivationView DeriveView(
            bool withSwift, CharacterId? activeCharacter,
            bool swiftDeveloped = true, bool swiftOffered = true) =>
            DerivedActivationView.Derive(
                BuildStone(swiftDeveloped: swiftDeveloped, swiftOffered: swiftOffered),
                BuildCharacter(_character, withSwift: withSwift, withFieldPrep: true, withIronStomach: true),
                BuildAuthority(activeCharacter));

        // ── AT-SWIFT-MENU-ONLY ─────────────────────────────────────────────────────────────

        [Fact]
        public void ActiveEffect_ShortensEligibleMenuCraftedFood_ToOneThirdOfSkillAdjustedDuration()
        {
            var view = DeriveView(withSwift: true, activeCharacter: _character);
            Assert.True(_provider.IsActive(view));

            // The base handed in is ALREADY the vanilla skill-adjusted duration (the seam computes
            // base*(1 - skillFactor*maxDecrease) before calling us). Swift multiplies by 1/3 on top.
            double skillAdjusted = 6.0;
            var decision = _provider.ResolveDuration(view, MenuCraftEligibility.EligibleMenuCraftedFood, skillAdjusted);

            Assert.True(decision.Shortened);
            Assert.Equal(skillAdjusted / 3.0, decision.Duration, 10);
            Assert.Equal(MenuCraftDurationProvider.ActiveDurationFactor, 1.0 / 3.0, 10);
        }

        [Fact]
        public void FactorAppliesAfterSkill_NotBefore()
        {
            var view = DeriveView(withSwift: true, activeCharacter: _character);

            // Two different skill-adjusted inputs → each is independently multiplied by 1/3. The provider
            // never re-derives the skill adjustment; it only scales the value it is handed.
            Assert.Equal(2.0, _provider.ResolveDuration(view, MenuCraftEligibility.EligibleMenuCraftedFood, 6.0).Duration, 10);
            Assert.Equal(0.5, _provider.ResolveDuration(view, MenuCraftEligibility.EligibleMenuCraftedFood, 1.5).Duration, 10);
        }

        [Theory]
        [InlineData(MenuCraftEligibility.IneligibleNonFood)]
        [InlineData(MenuCraftEligibility.IneligibleNonCookingStation)]
        [InlineData(MenuCraftEligibility.IneligibleNotMenuCraft)]
        public void ActiveEffect_DoesNotShortenIneligibleCraft(MenuCraftEligibility eligibility)
        {
            var view = DeriveView(withSwift: true, activeCharacter: _character);
            var decision = _provider.ResolveDuration(view, eligibility, 6.0);

            Assert.False(decision.Shortened);
            Assert.Equal(6.0, decision.Duration, 10);
        }

        [Fact]
        public void PurchasedButNoRelationship_EffectDormant_KeepsFullDuration()
        {
            var view = DeriveView(withSwift: true, activeCharacter: null); // relationship dormant
            Assert.False(_provider.IsActive(view));

            var decision = _provider.ResolveDuration(view, MenuCraftEligibility.EligibleMenuCraftedFood, 6.0);
            Assert.False(decision.Shortened);
            Assert.Equal(6.0, decision.Duration, 10);
        }

        [Fact]
        public void RelationshipButNoPurchase_KeepsFullDuration()
        {
            var view = DerivedActivationView.Derive(
                BuildStone(),
                BuildCharacter(_character, withSwift: false, withFieldPrep: true, withIronStomach: true),
                BuildAuthority(_character));

            Assert.False(_provider.IsActive(view));
            Assert.Equal(6.0, _provider.ResolveDuration(view, MenuCraftEligibility.EligibleMenuCraftedFood, 6.0).Duration, 10);
        }

        [Fact]
        public void UndevelopedNode_EvenWithPurchaseAndRelationship_KeepsFullDuration()
        {
            var view = DeriveView(withSwift: true, activeCharacter: _character, swiftDeveloped: false);
            Assert.False(_provider.IsActive(view));
            Assert.Equal(6.0, _provider.ResolveDuration(view, MenuCraftEligibility.EligibleMenuCraftedFood, 6.0).Duration, 10);
        }

        [Fact]
        public void RelationshipLossThenRestore_FlipsShorteningWithNoWrites()
        {
            // Active → shortened.
            Assert.True(_provider.ResolveDuration(
                DeriveView(withSwift: true, activeCharacter: _character),
                MenuCraftEligibility.EligibleMenuCraftedFood, 6.0).Shortened);
            // Released → dormant, full duration. Same persisted purchase, zero writes.
            Assert.False(_provider.ResolveDuration(
                DeriveView(withSwift: true, activeCharacter: null),
                MenuCraftEligibility.EligibleMenuCraftedFood, 6.0).Shortened);
            // Rejoin → shortened again. Pure re-derivation.
            Assert.True(_provider.ResolveDuration(
                DeriveView(withSwift: true, activeCharacter: _character),
                MenuCraftEligibility.EligibleMenuCraftedFood, 6.0).Shortened);
        }

        [Fact]
        public void EligibilityPredicate_OnlyFoodViaCookingMenuStation()
        {
            // Food + Cooking station + menu craft → eligible.
            Assert.Equal(MenuCraftEligibility.EligibleMenuCraftedFood,
                MenuCraftDurationProvider.ClassifyCraft(outputIsFood: true,
                    stationCraftingSkill: MenuCraftDurationProvider.CookingSkill, isMenuCraft: true));
            // Not food → ineligible non-food.
            Assert.Equal(MenuCraftEligibility.IneligibleNonFood,
                MenuCraftDurationProvider.ClassifyCraft(false, MenuCraftDurationProvider.CookingSkill, true));
            // Food but a non-Cooking station skill (e.g. a Forge/Workbench recipe) → ineligible.
            Assert.Equal(MenuCraftEligibility.IneligibleNonCookingStation,
                MenuCraftDurationProvider.ClassifyCraft(true, 999, true));
            // Food + Cooking skill but not a menu craft (e.g. a world slotted-cooking timer) → ineligible.
            Assert.Equal(MenuCraftEligibility.IneligibleNotMenuCraft,
                MenuCraftDurationProvider.ClassifyCraft(true, MenuCraftDurationProvider.CookingSkill, false));
        }

        [Fact]
        public void SiblingCharacterActive_DoesNotLeakShorteningToUnpurchasedCaller()
        {
            var view = DerivedActivationView.Derive(
                BuildStone(),
                BuildCharacter(_character, withSwift: true, withFieldPrep: true, withIronStomach: true),
                BuildAuthority(_sibling)); // the sibling holds the reservation, not our caller
            Assert.False(_provider.IsActive(view));
            Assert.False(_provider.ResolveDuration(view, MenuCraftEligibility.EligibleMenuCraftedFood, 6.0).Shortened);
        }

        // ── AT-NO-COOKING-COMPLETION ─────────────────────────────────────────────────────────

        [Fact]
        public void ShortenedDuration_NeverReachesZeroOrInstantCompletion_ForPositiveBase()
        {
            var view = DeriveView(withSwift: true, activeCharacter: _character);
            foreach (var baseDuration in new[] { 0.03, 0.3, 1.5, 6.0, 30.0 })
            {
                var decision = _provider.ResolveDuration(view, MenuCraftEligibility.EligibleMenuCraftedFood, baseDuration);
                Assert.True(decision.Duration > 0.0);            // never completes instantly
                Assert.True(decision.Duration < baseDuration);    // strictly shorter than vanilla
                Assert.Equal(baseDuration / 3.0, decision.Duration, 10);
            }
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        public void NonPositiveBase_ReturnedUnchanged_NeverFabricatesACraft(double badBase)
        {
            var view = DeriveView(withSwift: true, activeCharacter: _character);
            var decision = _provider.ResolveDuration(view, MenuCraftEligibility.EligibleMenuCraftedFood, badBase);
            Assert.False(decision.Shortened);
            Assert.Equal(badBase, decision.Duration, 10);
        }

        [Fact]
        public void FactorOverload_MatchesViewOverload()
        {
            var view = DeriveView(withSwift: true, activeCharacter: _character);
            var byView = _provider.ResolveDuration(view, MenuCraftEligibility.EligibleMenuCraftedFood, 6.0);
            var byBit = _provider.ResolveDuration(swiftPreparationActive: true, MenuCraftEligibility.EligibleMenuCraftedFood, 6.0);
            Assert.Equal(byView.Duration, byBit.Duration, 10);
            Assert.Equal(byView.Shortened, byBit.Shortened);
        }

        // ── AT-COOKING-TIER2 (pure prior-Offered-Set / Tier Access gate) ──────────────────────

        [Fact]
        public void SwiftPreparation_IneligibleWithOnlyOnePriorNode_PriorOfferedSetIncomplete()
        {
            var stone = BuildStone(); // Cooking committed L2, all three developed+offered
            // Caller acquired only Field Prep (one of the two required priors) with AP available.
            var character = BuildCharacter(_character, withFieldPrep: true, withIronStomach: false);

            var t = NodePurchases.PurchaseNode(character, stone, _catalog, Cooking, SwiftPrep,
                VersionedId.None, PurchasePaymentSource.PersonalAp);

            Assert.False(t.Accepted);
            Assert.Equal(NodePurchaseResult.PriorOfferedSetIncomplete, t.Result);
        }

        [Fact]
        public void SwiftPreparation_EligibleWhenBothPriorsAcquiredAndLevel2_Purchasable()
        {
            var stone = BuildStone();
            var character = BuildCharacter(_character, withFieldPrep: true, withIronStomach: true);

            var t = NodePurchases.PurchaseNode(character, stone, _catalog, Cooking, SwiftPrep,
                VersionedId.None, PurchasePaymentSource.PersonalAp);

            Assert.True(t.Accepted);
            Assert.Equal(NodePurchaseResult.Applied, t.Result);
        }

        [Fact]
        public void SwiftPreparation_RejectedBelowLevel2_EvenWithBothPriors()
        {
            // Tree/Active-Stone level 1 → Swift (authored Level 2) is capped out regardless of priors.
            var stone = BuildStone(treeLevel: 1, activeStoneLevel: 1);
            var character = BuildCharacter(_character, withFieldPrep: true, withIronStomach: true);

            var t = NodePurchases.PurchaseNode(character, stone, _catalog, Cooking, SwiftPrep,
                VersionedId.None, PurchasePaymentSource.PersonalAp);

            Assert.False(t.Accepted);
            Assert.Contains(t.Result, new[]
            {
                NodePurchaseResult.TreeLevelTooLow,
                NodePurchaseResult.ActiveStoneLevelTooLow,
            });
        }

        [Fact]
        public void DerivedSameTreeTierAccess_Reaches2_OnlyWhenBothPriorsAcquired()
        {
            var stone = BuildStone();

            // Both priors acquired → Cooking same-Tree Attunement Tier Access is 2.
            int accessBoth = NodePurchases.DeriveSameTreeTierAccess(
                BuildCharacter(_character, withFieldPrep: true, withIronStomach: true), stone, _catalog, Cooking);
            Assert.Equal(2, accessBoth);

            // One prior short → access stays at Tier 1.
            int accessOne = NodePurchases.DeriveSameTreeTierAccess(
                BuildCharacter(_character, withFieldPrep: true, withIronStomach: false), stone, _catalog, Cooking);
            Assert.Equal(1, accessOne);
        }

        [Fact]
        public void SwiftPreparationNode_IsSoleExecutableLevel2CookingCharacterEffect()
        {
            // Registry pin: the node the provider governs is the authored Level-2 Cooking Character Effect
            // whose prior-Offered set is exactly Field Prep + Iron Stomach.
            var def = _catalog.TryResolveNode(SwiftPrep);
            Assert.NotNull(def);
            Assert.Equal(2, def!.TreeLevel);
            Assert.Equal(Cooking, def.Tree);
            var priorKeys = new HashSet<string>();
            foreach (var p in def.Requirements.PriorOfferedSet) priorKeys.Add(p.Key);
            Assert.Contains(FieldPrep.Key, priorKeys);
            Assert.Contains(IronStomach.Key, priorKeys);
            Assert.Equal(2, priorKeys.Count);
        }
    }
}
