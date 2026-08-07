// ============================================================================
//  Homestead progression — COOKING / FIELD PREP tests (T017, US4).
// ----------------------------------------------------------------------------
//  Exercises the engine-free T017 Cooking node-2 vertical slice (link-compiled
//  from ../src): the shared Cooking-aware Bushcraft policy's first consumer, the
//  pure CookingCraftPolicy that
//    * derives whether the Field Prep *Character Effect* is currently ACTIVE for
//      a caller — a personal, purchased, refundable-on-revocation node whose
//      active/dormant status is re-derived through the shipped T004
//      DerivedActivationView (purchase record AND an active relationship to this
//      Stone; no second "active effects" ledger — AT-NO-ACTIVE-LEDGER), and
//    * while (and only while) that effect is active, EXPOSES the UNCHANGED
//      vanilla Boar Jerky and Queen's Jam recipes through Bushcraft (station-free
//      craftability) with NORMAL Cooking XP/speed/bonus behavior. The policy
//      gates EXPOSURE only: it authors and mutates NOTHING about the recipes'
//      inputs, yield, authority, or the ordinary Cooking mechanics that run when
//      they are crafted (spec §US4 line 148-149 "Field Prep exposes unchanged Boar
//      Jerky and Queen's Jam recipes through the shared Cooking-aware Bushcraft
//      policy"; contracts.md §Cooking "CookingCraftPolicy: Field Prep eligibility
//      plus normal Cooking skill XP, speed, and bonus-output behavior for unchanged
//      Boar Jerky/Queen's Jam recipes through Bushcraft").
//
//  Named acceptance closed here (tasks.md T017):
//    AT-FIELD-PREP-COOKING-POLICY  an active Field Prep Character Effect exposes the
//                        unchanged vanilla Boar Jerky and Queen's Jam recipes through
//                        Bushcraft (station-free), preserving the ordinary recipe
//                        inputs, yield, authority, AND normal Cooking XP/speed/bonus;
//                        a dormant/unpurchased effect exposes nothing, no other recipe
//                        is exposed, and no code path here mutates the recipe or the
//                        Cooking mechanics.
//
//  Contrast with T016 Savor the Hearth: Savor is a Stone-owned LOCAL Effect gated by
//  the Settlement Local policy + governance + in-Area occupancy, delivering a food-
//  timer drain factor. Field Prep is a PERSONAL Character Effect gated by purchase +
//  relationship, and it authors NO new content and delivers NO stat/timer effect — it
//  only makes the EXISTING vanilla Boar Jerky/Queen's Jam recipes station-free while
//  active. Neither a Local policy nor build Permission is a conjunct here. It is the
//  structural twin of the Archer T026 BushcraftRecipeProvider (personal recipe
//  exposure), differing only in node (Field Prep) and recipe set (two Cooking
//  recipes, not one Wood Arrow).
// ============================================================================

using SBPR.Niflheim.HomesteadStones.Adapters.Cooking;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimFieldPrepTests
    {
        private readonly WorldId _world = new WorldId("uid:fp-017");
        private readonly StoneId _stone;

        private readonly AccountId _account = new AccountId("acct-cook");
        private readonly CharacterId _character = new CharacterId("char-cook");
        private readonly CharacterId _sibling = new CharacterId("char-sibling");

        private static readonly VersionedId Cooking = HomesteadProgressionCatalog.CookingTree;
        private static readonly VersionedId FieldPrep = new VersionedId("FieldPrep", 1);
        private static readonly VersionedId SavorTheHearth = new VersionedId("SavorTheHearth", 1);

        private readonly HomesteadProgressionCatalog _catalog = new HomesteadProgressionCatalog();
        private readonly CookingCraftPolicy _policy;

        public NiflheimFieldPrepTests()
        {
            _stone = StoneId.FromHostZone(_world, 4, 2);
            _policy = new CookingCraftPolicy(_catalog);
        }

        // A Stone with Field Prep (Cooking, personal) developed + Offered so a purchase can derive
        // Active/Dormant. Optional developed flag drives the undeveloped-node branch.
        private StoneProgressionAggregate BuildStone(bool fieldPrepDeveloped = true, bool offered = true)
        {
            var committed = new List<CommittedTreeRecord>
            {
                new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                    Cooking, "seed-commit-cooking", _character.Value, 1, 0)
            };

            var development = new List<NodeDevelopmentRecord>();
            if (fieldPrepDeveloped)
                development.Add(new NodeDevelopmentRecord(FieldPrep, 1, 1, true, offered, "seed-dev-fp"));

            return new StoneProgressionAggregate(_stone, revision: 5, historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "c", updatedProvenance: "u",
                mirroredStoneAp: 3, lastAppliedReceiptId: "r",
                committedTrees: committed, nodeDevelopment: development);
        }

        // The caller's character with an optional Field Prep purchase at this Stone.
        private CharacterProgressionAggregate BuildCharacter(CharacterId character, bool withPurchase)
        {
            NodePurchaseRecord[]? purchases = withPurchase
                ? new[]
                {
                    new NodePurchaseRecord(Cooking, FieldPrep, "ap:personal",
                        "CharacterEffect", VersionedId.None, "op-buy-fp")
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
                        "rel-fp", "relreceipt:seed"), 1);
            return idx;
        }

        // ── AT-FIELD-PREP-COOKING-POLICY ───────────────────────────────────────

        [Fact]
        public void ActiveEffect_ExposesUnchangedBoarJerkyAndQueensJamThroughBushcraft()
        {
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: true);
            var authority = BuildAuthority(_character);

            var cap = _policy.Resolve(stone, character, authority);

            Assert.True(cap.EffectActive);
            Assert.True(cap.CookingRecipesExposed);

            // Exactly the two vanilla Cooking recipes are exposed — Boar Jerky and Queen's Jam.
            var items = cap.Recipes.Select(r => r.OutputItem).ToArray();
            Assert.Equal(2, items.Length);
            Assert.Contains(BushcraftCookingContent.BoarJerkyItem, items);
            Assert.Contains(BushcraftCookingContent.QueensJamItem, items);

            // Each exposed recipe is station-free (Bushcraft), unchanged inputs/yield/authority, and
            // preserves normal Cooking XP/speed/bonus.
            foreach (var recipe in cap.Recipes)
            {
                Assert.True(recipe.StationFree);
                Assert.True(recipe.PreservesVanillaInputsYieldAuthority);
                Assert.True(recipe.PreservesNormalCookingXpSpeedBonus);
            }

            // Per-item exposure convenience: both are exposed by name.
            Assert.True(cap.ExposesRecipeFor(BushcraftCookingContent.BoarJerkyItem));
            Assert.True(cap.ExposesRecipeFor(BushcraftCookingContent.QueensJamItem));
        }

        [Fact]
        public void PurchasedButNoRelationship_EffectDormant_ExposesNothing()
        {
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: true);
            // No active reservation → relationship dormant.
            var authority = BuildAuthority(null);

            var cap = _policy.Resolve(stone, character, authority);

            Assert.False(cap.EffectActive);
            Assert.False(cap.CookingRecipesExposed);
            Assert.False(cap.ExposesRecipeFor(BushcraftCookingContent.BoarJerkyItem));
            Assert.False(cap.ExposesRecipeFor(BushcraftCookingContent.QueensJamItem));
        }

        [Fact]
        public void RelationshipButNoPurchase_ExposesNothing()
        {
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: false);
            var authority = BuildAuthority(_character);

            var cap = _policy.Resolve(stone, character, authority);

            Assert.False(cap.EffectActive);
            Assert.False(cap.CookingRecipesExposed);
        }

        [Fact]
        public void UndevelopedNode_EvenWithPurchaseAndRelationship_ExposesNothing()
        {
            // Field Prep not developed on the Stone → no derived row, effect cannot be active.
            var stone = BuildStone(fieldPrepDeveloped: false);
            var character = BuildCharacter(_character, withPurchase: true);
            var authority = BuildAuthority(_character);

            var cap = _policy.Resolve(stone, character, authority);

            Assert.False(cap.EffectActive);
            Assert.False(cap.CookingRecipesExposed);
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

            var cap = _policy.Resolve(stone, character, authority);

            Assert.False(cap.EffectActive);
            Assert.False(cap.CookingRecipesExposed);
        }

        [Fact]
        public void RelationshipLossThenRestore_FlipsExposureWithNoWrites()
        {
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: true);

            // Active → exposed.
            Assert.True(_policy.Resolve(stone, character, BuildAuthority(_character)).CookingRecipesExposed);
            // Relationship released → dormant, not exposed. Same persisted purchase, zero writes.
            Assert.False(_policy.Resolve(stone, character, BuildAuthority(null)).CookingRecipesExposed);
            // Rejoin → active again. Pure re-derivation.
            Assert.True(_policy.Resolve(stone, character, BuildAuthority(_character)).CookingRecipesExposed);
        }

        [Fact]
        public void ExposesOnlyFieldPrepRecipes_NotSavorOrOtherItems()
        {
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: true);
            var authority = BuildAuthority(_character);

            var cap = _policy.Resolve(stone, character, authority);

            // Field Prep exposes exactly Boar Jerky + Queen's Jam and never an arbitrary other item
            // (e.g. a raw meat, a Wood Arrow, or Savor's food-timer effect — those are other nodes).
            Assert.True(cap.ExposesRecipeFor("BoarJerky"));
            Assert.True(cap.ExposesRecipeFor("QueensJam"));
            Assert.False(cap.ExposesRecipeFor("RawMeat"));
            Assert.False(cap.ExposesRecipeFor("ArrowWood"));
            Assert.False(cap.ExposesRecipeFor("SwiftPreparation"));
        }

        [Fact]
        public void ExposedRecipeContent_IsStationFreeUnchangedAndNormalCooking()
        {
            // Static content assertion — independent of any caller. The Bushcraft exposure targets are
            // the unchanged vanilla Boar Jerky / Queen's Jam recipes made station-free with normal
            // Cooking XP/speed/bonus; they carry no authored inputs/yield.
            foreach (var recipe in BushcraftCookingContent.FieldPrepRecipes)
            {
                Assert.True(recipe.StationFree);
                Assert.True(recipe.PreservesVanillaInputsYieldAuthority);
                Assert.True(recipe.PreservesNormalCookingXpSpeedBonus);
            }
            Assert.Equal("BoarJerky", BushcraftCookingContent.BoarJerkyRecipe.OutputItem);
            Assert.Equal("QueensJam", BushcraftCookingContent.QueensJamRecipe.OutputItem);
            Assert.True(BushcraftCookingContent.IsFieldPrepRecipeItem("BoarJerky"));
            Assert.True(BushcraftCookingContent.IsFieldPrepRecipeItem("QueensJam"));
            Assert.False(BushcraftCookingContent.IsFieldPrepRecipeItem("RawMeat"));
        }

        [Fact]
        public void DormantEffect_ExposesRecipeFor_ReturnsFalseEvenForFieldPrepItems()
        {
            // ExposesRecipeFor is the AND of "is a Field Prep item" and "effect active". A dormant
            // effect never exposes even its own recipes.
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: true);
            var cap = _policy.Resolve(stone, character, BuildAuthority(null));

            Assert.False(cap.ExposesRecipeFor("BoarJerky"));
            Assert.False(cap.ExposesRecipeFor("QueensJam"));
        }

        [Fact]
        public void NoneCapability_IsInert()
        {
            var none = CookingCraftCapability.None;
            Assert.False(none.EffectActive);
            Assert.False(none.CookingRecipesExposed);
            Assert.False(none.ExposesRecipeFor("BoarJerky"));
        }
    }
}
