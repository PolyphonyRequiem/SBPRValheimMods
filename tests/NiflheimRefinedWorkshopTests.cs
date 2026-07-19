// ============================================================================
//  Homestead progression — REFINED WORKSHOP real-versus-effective station level
//  (T021, US4, Crafting Tracer 6).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free T021 slice (link-compiled from ../src):
//    * Adapters/Crafting/EffectiveStationLevelProvider.cs — the pure real-vs-
//      effective station-level policy for Refined Workshop.
//  Composed over the already-shipped shared grammar (the pure
//  LocalEffectActivationView that decides whether the Refined Workshop Local
//  Effect is currently ACTIVE for an occupant) so the +1 inherits every accepted
//  activation/dormancy gate with no second ledger.
//
//  Named acceptance closed here (tasks.md T021 / plan.md Tracer 6):
//    AT-REFINED-REAL-VS-EFFECTIVE
//        Refined Workshop grants +1 EFFECTIVE station level only for eligible
//        portable-item production/upgrade/repair inside the active Homestead; a
//        qualifying real Level-2 station reaches effective Level 3, the same real
//        station without the active Local Effect does not; the REAL observed level
//        is never mutated and stays visible; the +1 never applies to structure
//        production or build placement and never conjures a station; and the bonus
//        re-derives away with zero writes on policy loss / relationship release /
//        area exit / missing Governor / Tree or Stone-Level dormancy.
// ============================================================================

using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Crafting;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimRefinedWorkshopTests
    {
        private readonly WorldId _world = new WorldId("uid:rw-021");
        private readonly StoneId _stone;

        private readonly AccountId _owner = new AccountId("acct-owner");
        private readonly CharacterId _ownerChar = new CharacterId("char-owner");
        private readonly AccountId _guest = new AccountId("acct-guest");
        private readonly AccountId _stranger = new AccountId("acct-stranger");

        private static readonly VersionedId Crafting = HomesteadProgressionCatalog.CraftingTree;
        private static readonly VersionedId Refined = new VersionedId("RefinedWorkshop", 1); // Local L1 Crafting
        private static readonly VersionedId ArtisansCounter = new VersionedId("ArtisansCounter", 1); // unavailable Local Crafting

        private readonly HomesteadProgressionCatalog _catalog = new HomesteadProgressionCatalog();

        public NiflheimRefinedWorkshopTests()
        {
            _stone = StoneId.FromHostZone(_world, 4, 11);
        }

        // A Stone with Refined Workshop developed as Stone-owned Local state, the Crafting Tree committed,
        // at Active/Historical Level 2 (so the L1 Refined Workshop node's level gate is satisfied).
        private StoneProgressionAggregate BuildStone(bool craftingCommitted = true, int activeLevel = 2,
            bool refinedDeveloped = true, SettlementLocalPolicy? policy = null)
        {
            var committed = new List<CommittedTreeRecord>();
            if (craftingCommitted)
                committed.Add(new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                    Crafting, "seed-commit-craft", _ownerChar.Value, 1, 0));

            var development = new List<NodeDevelopmentRecord>();
            if (refinedDeveloped)
                development.Add(new NodeDevelopmentRecord(Refined, 1, 1, true, false, "seed-dev-refined"));

            return new StoneProgressionAggregate(_stone, revision: 5,
                historicalStoneLevel: 2, activeStoneLevel: activeLevel,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "created", updatedProvenance: "seed",
                mirroredStoneAp: 0, lastAppliedReceiptId: "r-seed",
                committedTrees: committed, nodeDevelopment: development,
                localPolicy: policy);
        }

        private LocalEffectActivationView DeriveFor(StoneProgressionAggregate stone,
            AccountId occupant, bool isOwner, bool hasRelationship, bool inside, bool governorPresent)
            => LocalEffectActivationView.Derive(stone, _catalog, occupant, isOwner,
                hasRelationship, inside, governorPresent);

        // The default-active projection for the OWNER, everything satisfied (Everyone policy default).
        private LocalEffectActivationView ActiveOwnerView(StoneProgressionAggregate? stone = null)
            => DeriveFor(stone ?? BuildStone(), _owner, isOwner: true, hasRelationship: false,
                inside: true, governorPresent: true);

        // ============================================================================
        //  AT-REFINED-REAL-VS-EFFECTIVE — the +1 policy, its exclusions, and dormancy.
        // ============================================================================

        [Fact]
        public void Active_refined_workshop_makes_real_level2_effective_level3_for_portable_production()
        {
            var view = ActiveOwnerView();
            // Precondition: the Refined Workshop Local Effect is actually active for this occupant.
            Assert.True(view.StatusFor(Refined).Active);

            var r = EffectiveStationLevelProvider.Resolve(view, Refined, realStationLevel: 2,
                CraftingOperationKind.PortableItemProduction, itemIsEligiblePortable: true);

            Assert.True(r.BonusApplied);
            Assert.Equal(3, r.EffectiveStationLevelValue); // effective Level 3
            Assert.Equal(2, r.RealStationLevel);           // real level UNCHANGED and visible
        }

        [Theory]
        [InlineData(CraftingOperationKind.PortableItemProduction)]
        [InlineData(CraftingOperationKind.PortableItemUpgrade)]
        [InlineData(CraftingOperationKind.PortableItemRepair)]
        public void Plus_one_applies_to_all_three_eligible_portable_operations(CraftingOperationKind op)
        {
            var view = ActiveOwnerView();
            var r = EffectiveStationLevelProvider.Resolve(view, Refined, realStationLevel: 2, op,
                itemIsEligiblePortable: true);
            Assert.True(r.BonusApplied);
            Assert.Equal(3, r.EffectiveStationLevelValue);
        }

        [Fact]
        public void Same_real_station_without_active_local_effect_gets_no_bonus()
        {
            // Refined Workshop NOT developed on this Stone -> not active -> no +1, real level preserved.
            var stoneNoEffect = BuildStone(refinedDeveloped: false);
            var view = DeriveFor(stoneNoEffect, _owner, isOwner: true, hasRelationship: false,
                inside: true, governorPresent: true);
            Assert.False(view.StatusFor(Refined).Active);

            var r = EffectiveStationLevelProvider.Resolve(view, Refined, realStationLevel: 2,
                CraftingOperationKind.PortableItemProduction, itemIsEligiblePortable: true);

            Assert.False(r.BonusApplied);
            Assert.Equal(2, r.EffectiveStationLevelValue); // effective == real, no Level 3
            Assert.Equal(2, r.RealStationLevel);
        }

        [Theory]
        [InlineData(CraftingOperationKind.StructureProduction)]
        [InlineData(CraftingOperationKind.BuildPlacement)]
        public void Structure_and_build_operations_never_receive_the_bonus_even_when_active(CraftingOperationKind op)
        {
            var view = ActiveOwnerView();
            var r = EffectiveStationLevelProvider.Resolve(view, Refined, realStationLevel: 2, op,
                itemIsEligiblePortable: true);
            // Does not affect structure production / does not unlock building pieces/permissions.
            Assert.False(r.BonusApplied);
            Assert.Equal(2, r.EffectiveStationLevelValue);
            Assert.Equal(2, r.RealStationLevel);
        }

        [Fact]
        public void Ineligible_non_portable_item_gets_no_bonus_even_for_a_production_operation()
        {
            var view = ActiveOwnerView();
            var r = EffectiveStationLevelProvider.Resolve(view, Refined, realStationLevel: 2,
                CraftingOperationKind.PortableItemProduction, itemIsEligiblePortable: false);
            Assert.False(r.BonusApplied);
            Assert.Equal(2, r.EffectiveStationLevelValue);
        }

        [Fact]
        public void Bonus_never_conjures_a_station_when_no_real_station_present()
        {
            // Real level 0 == no station. The +1 augments an existing station; it must not create one.
            var view = ActiveOwnerView();
            var r = EffectiveStationLevelProvider.Resolve(view, Refined, realStationLevel: 0,
                CraftingOperationKind.PortableItemProduction, itemIsEligiblePortable: true);
            Assert.False(r.BonusApplied);
            Assert.Equal(0, r.EffectiveStationLevelValue);
            Assert.Equal(0, r.RealStationLevel);
        }

        [Fact]
        public void Real_level_is_reported_and_never_mutated_across_repeated_resolutions()
        {
            var view = ActiveOwnerView();
            var a = EffectiveStationLevelProvider.Resolve(view, Refined, realStationLevel: 1,
                CraftingOperationKind.PortableItemUpgrade, itemIsEligiblePortable: true);
            var b = EffectiveStationLevelProvider.Resolve(view, Refined, realStationLevel: 1,
                CraftingOperationKind.StructureProduction, itemIsEligiblePortable: true);
            // The eligible op sees effective 2; the structure op sees the same real 1 with no bonus.
            Assert.Equal(1, a.RealStationLevel);
            Assert.Equal(2, a.EffectiveStationLevelValue);
            Assert.Equal(1, b.RealStationLevel);
            Assert.Equal(1, b.EffectiveStationLevelValue);
        }

        // ---- Dormancy: the bonus re-derives away with zero writes on every gate loss ----

        [Fact]
        public void Bonus_dormant_when_occupant_exits_the_stone_area()
        {
            var stone = BuildStone();
            var outside = DeriveFor(stone, _owner, isOwner: true, hasRelationship: false,
                inside: false, governorPresent: true);
            Assert.False(outside.StatusFor(Refined).Active);
            var r = EffectiveStationLevelProvider.Resolve(outside, Refined, realStationLevel: 2,
                CraftingOperationKind.PortableItemProduction, itemIsEligiblePortable: true);
            Assert.False(r.BonusApplied);
            Assert.Equal(2, r.EffectiveStationLevelValue);
        }

        [Fact]
        public void Bonus_dormant_when_no_authorized_governor_present()
        {
            var stone = BuildStone();
            var noGov = DeriveFor(stone, _owner, isOwner: true, hasRelationship: false,
                inside: true, governorPresent: false);
            var r = EffectiveStationLevelProvider.Resolve(noGov, Refined, realStationLevel: 2,
                CraftingOperationKind.PortableItemProduction, itemIsEligiblePortable: true);
            Assert.False(r.BonusApplied);
            Assert.Equal(2, r.EffectiveStationLevelValue);
        }

        [Fact]
        public void Bonus_dormant_when_crafting_tree_not_committed()
        {
            var stone = BuildStone(craftingCommitted: false);
            var view = DeriveFor(stone, _owner, isOwner: true, hasRelationship: false,
                inside: true, governorPresent: true);
            var r = EffectiveStationLevelProvider.Resolve(view, Refined, realStationLevel: 2,
                CraftingOperationKind.PortableItemProduction, itemIsEligiblePortable: true);
            Assert.False(r.BonusApplied);
            Assert.Equal(2, r.EffectiveStationLevelValue);
        }

        [Fact]
        public void Bonus_dormant_when_active_stone_level_below_node_level()
        {
            // Active Stone Level 0 is below the Refined Workshop node's authored Level 1 -> dormant.
            var stone = BuildStone(activeLevel: 0);
            var view = DeriveFor(stone, _owner, isOwner: true, hasRelationship: false,
                inside: true, governorPresent: true);
            var r = EffectiveStationLevelProvider.Resolve(view, Refined, realStationLevel: 2,
                CraftingOperationKind.PortableItemProduction, itemIsEligiblePortable: true);
            Assert.False(r.BonusApplied);
            Assert.Equal(2, r.EffectiveStationLevelValue);
        }

        [Fact]
        public void Attuned_policy_excludes_unrelated_occupant_from_the_bonus_but_keeps_real_level()
        {
            var stone = BuildStone(policy: new SettlementLocalPolicy(LocalBeneficiaryMode.Attuned, 1, null));

            // An unrelated stranger inside the Area is policy-ineligible -> no bonus.
            var stranger = DeriveFor(stone, _stranger, isOwner: false, hasRelationship: false,
                inside: true, governorPresent: true);
            var rs = EffectiveStationLevelProvider.Resolve(stranger, Refined, realStationLevel: 2,
                CraftingOperationKind.PortableItemProduction, itemIsEligiblePortable: true);
            Assert.False(rs.BonusApplied);
            Assert.Equal(2, rs.EffectiveStationLevelValue);

            // An attuned guest DOES receive the bonus under the same policy.
            var guest = DeriveFor(stone, _guest, isOwner: false, hasRelationship: true,
                inside: true, governorPresent: true);
            var rg = EffectiveStationLevelProvider.Resolve(guest, Refined, realStationLevel: 2,
                CraftingOperationKind.PortableItemProduction, itemIsEligiblePortable: true);
            Assert.True(rg.BonusApplied);
            Assert.Equal(3, rg.EffectiveStationLevelValue);
        }

        [Fact]
        public void Rejoining_the_area_re_derives_the_bonus_with_no_writes()
        {
            var stone = BuildStone();
            // Exit -> dormant.
            var outside = DeriveFor(stone, _owner, isOwner: true, hasRelationship: false,
                inside: false, governorPresent: true);
            Assert.False(EffectiveStationLevelProvider.Resolve(outside, Refined, 2,
                CraftingOperationKind.PortableItemProduction, true).BonusApplied);
            // Re-enter -> active again from the SAME persisted Stone state, zero writes.
            var inside = DeriveFor(stone, _owner, isOwner: true, hasRelationship: false,
                inside: true, governorPresent: true);
            Assert.True(EffectiveStationLevelProvider.Resolve(inside, Refined, 2,
                CraftingOperationKind.PortableItemProduction, true).BonusApplied);
        }

        [Fact]
        public void Unavailable_crafting_local_node_is_never_active_and_never_grants_a_bonus()
        {
            // Artisan's Counter is an authored-but-unavailable Local Crafting node: it is never developed,
            // so it can never be active, and asking the provider for its "+1" yields nothing.
            var view = ActiveOwnerView();
            Assert.False(view.StatusFor(ArtisansCounter).Active);
            var r = EffectiveStationLevelProvider.Resolve(view, ArtisansCounter, realStationLevel: 2,
                CraftingOperationKind.PortableItemProduction, itemIsEligiblePortable: true);
            Assert.False(r.BonusApplied);
            Assert.Equal(2, r.EffectiveStationLevelValue);
        }

        [Fact]
        public void Is_portable_operation_classifies_only_the_three_portable_kinds()
        {
            Assert.True(EffectiveStationLevelProvider.IsPortableOperation(CraftingOperationKind.PortableItemProduction));
            Assert.True(EffectiveStationLevelProvider.IsPortableOperation(CraftingOperationKind.PortableItemUpgrade));
            Assert.True(EffectiveStationLevelProvider.IsPortableOperation(CraftingOperationKind.PortableItemRepair));
            Assert.False(EffectiveStationLevelProvider.IsPortableOperation(CraftingOperationKind.StructureProduction));
            Assert.False(EffectiveStationLevelProvider.IsPortableOperation(CraftingOperationKind.BuildPlacement));
        }
    }
}
