// ============================================================================
//  Homestead progression — ARCHER / FIELD FLETCHING I tests (T026, US4).
// ----------------------------------------------------------------------------
//  Exercises the engine-free T026 Archer node-2 vertical slice (link-compiled
//  from ../src): the pure BushcraftRecipeProvider that
//    * derives whether the Field Fletching I *Character Effect* is currently
//      ACTIVE for a caller — a personal, purchased, refundable-on-revocation
//      node whose active/dormant status is re-derived through the shipped T004
//      DerivedActivationView (purchase record AND an active relationship to this
//      Stone; no second "active effects" ledger — AT-NO-ACTIVE-LEDGER), and
//    * while (and only while) that effect is active, EXPOSES the UNCHANGED
//      vanilla Wood Arrow recipe through Bushcraft (station-free craftability).
//      The provider gates EXPOSURE only: it authors and mutates NOTHING about the
//      recipe's inputs, yield, or authority (spec line 160 "exposes unchanged
//      Wood Arrows through Bushcraft while active"; contracts.md §Archer
//      "BushcraftRecipeProvider: active Field Fletching I exposes unchanged Wood
//      Arrows through Bushcraft"; research.md "Field Fletching I = unchanged Wood
//      Arrow recipe").
//
//  Named acceptance closed here (tasks.md T026):
//    AT-FIELD-FLETCHING  an active Field Fletching I Character Effect exposes the
//                        unchanged vanilla Wood Arrow recipe through Bushcraft
//                        (station-free), preserving the ordinary recipe inputs,
//                        yield, and authority; a dormant/unpurchased effect
//                        exposes nothing, and no code path here mutates the recipe.
//
//  Contrast with T025 Practice Range: Practice Range is a Stone-owned LOCAL Effect
//  gated by the Settlement Local policy AND ordinary build Permission, and it ships
//  NEW authored content (the ArrowPractice item, 100-for-8). Field Fletching I is a
//  PERSONAL Character Effect gated by purchase + relationship, and it authors NO new
//  content — it only makes the EXISTING vanilla Wood Arrow recipe station-free while
//  active. Neither a Local policy nor build Permission is a conjunct here.
// ============================================================================

using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Archer;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimFieldFletchingTests
    {
        private readonly WorldId _world = new WorldId("uid:ff-026");
        private readonly StoneId _stone;

        private readonly AccountId _account = new AccountId("acct-archer");
        private readonly CharacterId _character = new CharacterId("char-archer");
        private readonly CharacterId _sibling = new CharacterId("char-sibling");

        private static readonly VersionedId Archer = HomesteadProgressionCatalog.ArcherTree;
        private static readonly VersionedId FieldFletching = new VersionedId("FieldFletchingI", 1);
        private static readonly VersionedId PracticeRange = new VersionedId("PracticeRange", 1);

        private readonly HomesteadProgressionCatalog _catalog = new HomesteadProgressionCatalog();
        private readonly BushcraftRecipeProvider _provider;

        public NiflheimFieldFletchingTests()
        {
            _stone = StoneId.FromHostZone(_world, 7, 3);
            _provider = new BushcraftRecipeProvider(_catalog);
        }

        // A Stone with Field Fletching I (Archer, personal) developed + Offered so a purchase can
        // derive Active/Dormant. Optional developed flag drives the undeveloped-node branch.
        private StoneProgressionAggregate BuildStone(bool fieldFletchingDeveloped = true, bool offered = true)
        {
            var committed = new List<CommittedTreeRecord>
            {
                new CommittedTreeRecord(HomesteadProgressionCatalog.MartialFacetId,
                    Archer, "seed-commit-archer", _character.Value, 1, 0)
            };

            var development = new List<NodeDevelopmentRecord>();
            if (fieldFletchingDeveloped)
                development.Add(new NodeDevelopmentRecord(FieldFletching, 1, 1, true, offered, "seed-dev-ff"));

            return new StoneProgressionAggregate(_stone, revision: 5, historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "c", updatedProvenance: "u",
                mirroredStoneAp: 3, lastAppliedReceiptId: "r",
                committedTrees: committed, nodeDevelopment: development);
        }

        // The caller's character with an optional Field Fletching I purchase at this Stone.
        private CharacterProgressionAggregate BuildCharacter(CharacterId character, bool withPurchase)
        {
            NodePurchaseRecord[]? purchases = withPurchase
                ? new[]
                {
                    new NodePurchaseRecord(Archer, FieldFletching, "ap:personal",
                        "CharacterEffect", VersionedId.None, "op-buy-ff")
                }
                : null;

            var stoneRecord = new CharacterStoneRecord(_stone, 3, 3, 1, purchases, null);
            return new CharacterProgressionAggregate(_account, character,
                "world-scope", 1, 2, 2, "receipt", new[] { stoneRecord });
        }

        // Authority index: keyed (account, stone). activeCharacter, when set, holds an active Bond.
        private AccountStoneAuthorityIndex BuildAuthority(CharacterId? activeCharacter)
        {
            var idx = AccountStoneAuthorityIndex.Vacant(_account, _stone);
            if (activeCharacter.HasValue)
                idx = idx.WithReservationAdded(
                    new AuthorityReservation(activeCharacter.Value, RelationshipKind.Bond,
                        "rel-ff", "relreceipt:seed"), 1);
            return idx;
        }

        // ── AT-FIELD-FLETCHING ─────────────────────────────────────────────────

        [Fact]
        public void ActiveEffect_ExposesUnchangedWoodArrowRecipeThroughBushcraft()
        {
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: true);
            var authority = BuildAuthority(_character);

            var cap = _provider.Resolve(stone, character, authority);

            Assert.True(cap.EffectActive);
            Assert.True(cap.WoodArrowRecipeExposed);

            // The exposed recipe is the EXACT unchanged vanilla Wood Arrow — station-free (Bushcraft).
            Assert.Equal(BushcraftRecipeContent.WoodArrowItem, cap.WoodArrowRecipe.OutputItem);
            Assert.True(cap.WoodArrowRecipe.StationFree);
            // Exposure only: the provider carries NO authored inputs/yield override — the recipe's
            // ordinary vanilla inputs, yield, and authority are untouched.
            Assert.True(cap.WoodArrowRecipe.PreservesVanillaInputsYieldAuthority);
        }

        [Fact]
        public void PurchasedButNoRelationship_EffectDormant_ExposesNothing()
        {
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: true);
            // No active reservation → relationship dormant.
            var authority = BuildAuthority(null);

            var cap = _provider.Resolve(stone, character, authority);

            Assert.False(cap.EffectActive);
            Assert.False(cap.WoodArrowRecipeExposed);
        }

        [Fact]
        public void RelationshipButNoPurchase_ExposesNothing()
        {
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: false);
            var authority = BuildAuthority(_character);

            var cap = _provider.Resolve(stone, character, authority);

            Assert.False(cap.EffectActive);
            Assert.False(cap.WoodArrowRecipeExposed);
        }

        [Fact]
        public void UndevelopedNode_EvenWithPurchaseAndRelationship_ExposesNothing()
        {
            // Field Fletching I not developed on the Stone → no derived row, effect cannot be active.
            var stone = BuildStone(fieldFletchingDeveloped: false);
            var character = BuildCharacter(_character, withPurchase: true);
            var authority = BuildAuthority(_character);

            var cap = _provider.Resolve(stone, character, authority);

            Assert.False(cap.EffectActive);
            Assert.False(cap.WoodArrowRecipeExposed);
        }

        [Fact]
        public void SiblingCharacterActive_DoesNotLeakExposureToUnpurchasedCaller()
        {
            // The sibling holds the reservation; the caller has the purchase but not the active
            // relationship — the purchased caller is dormant and exposes nothing. (Personal effects
            // are per-character: another character's active reservation never activates this caller.)
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: true);
            var authority = BuildAuthority(_sibling);

            var cap = _provider.Resolve(stone, character, authority);

            Assert.False(cap.EffectActive);
            Assert.False(cap.WoodArrowRecipeExposed);
        }

        [Fact]
        public void RelationshipLossThenRestore_FlipsExposureWithNoWrites()
        {
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: true);

            // Active → exposed.
            Assert.True(_provider.Resolve(stone, character, BuildAuthority(_character)).WoodArrowRecipeExposed);
            // Relationship released → dormant, not exposed. Same persisted purchase, zero writes.
            Assert.False(_provider.Resolve(stone, character, BuildAuthority(null)).WoodArrowRecipeExposed);
            // Rejoin → active again. Pure re-derivation.
            Assert.True(_provider.Resolve(stone, character, BuildAuthority(_character)).WoodArrowRecipeExposed);
        }

        [Fact]
        public void ExposesOnlyWoodArrow_NotThePracticeRangeOrOtherRecipes()
        {
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: true);
            var authority = BuildAuthority(_character);

            var cap = _provider.Resolve(stone, character, authority);

            // Field Fletching I exposes exactly one recipe — the vanilla Wood Arrow — and never the
            // Practice Range / Practice Arrow content (that is a separate Local node, T025).
            Assert.Equal("ArrowWood", cap.WoodArrowRecipe.OutputItem);
            Assert.NotEqual(PracticeRangeContent.PracticeArrowItem, cap.WoodArrowRecipe.OutputItem);
        }

        [Fact]
        public void ExposedRecipeContent_IsStationFreeAndUnchanged()
        {
            // Static content assertion — independent of any caller. The Bushcraft exposure target is the
            // unchanged vanilla Wood Arrow recipe made station-free; it carries no authored inputs/yield.
            var recipe = BushcraftRecipeContent.WoodArrowRecipe;
            Assert.Equal("ArrowWood", recipe.OutputItem);
            Assert.True(recipe.StationFree);
            Assert.True(recipe.PreservesVanillaInputsYieldAuthority);
        }

        [Fact]
        public void NoneCapability_IsInert()
        {
            var none = BushcraftRecipeCapability.None;
            Assert.False(none.EffectActive);
            Assert.False(none.WoodArrowRecipeExposed);
        }
    }
}
