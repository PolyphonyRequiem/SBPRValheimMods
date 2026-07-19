// ============================================================================
//  Homestead progression — ARCHER / PRACTICE RANGE tests (T025, US4).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free T025 Archer vertical slice (link-compiled
//  from ../src): the pure PracticeRangeProvider that
//    * derives whether the Practice Range Local Effect is currently ACTIVE for an
//      occupant (reusing the T014 LocalEffectActivationView — one Settlement Local
//      policy + relationship/governance/level dormancy, no second ledger), and
//    * exposes the two Local placement/recipe CAPABILITIES (the exact vanilla
//      Archery Target placement and the Practice Arrow recipe) ONLY when that
//      effect is active AND the occupant independently holds ordinary build
//      Permission (spec FR-016 final sentence — policy never grants the build ACL),
//    * carries the authored Practice Arrow content: 100 arrows for 8 Wood with 0
//      ammo damage, retaining the bow's own draw damage, and
//    * makes the DETERMINISTIC vanilla target-return decision (a practice arrow
//      that terminally impacts the Archery Target is returned exactly once, no
//      roll) — the hook a later Fletcher's Habit roll (T027) must yield to.
//
//  Named acceptance closed here (tasks.md T025):
//    AT-PRACTICE-RANGE        active Local policy + ordinary Permission unlock the
//                             exact Archery Target placement and Practice Arrow
//                             recipe capability inside the active Homestead; policy
//                             eligibility alone or build Permission alone is not
//                             enough; dormancy suppresses both.
//    AT-PRACTICE-ARROW-DAMAGE the Practice Arrow recipe is exactly 100 for 8 Wood;
//                             ammo damage is 0; the fired projectile retains the
//                             bow's own draw damage (effective == bow damage).
//    AT-TARGET-RETURN         a practice arrow terminally impacting the Archery
//                             Target is deterministically returned exactly once
//                             (probability 1, same result every evaluation) and
//                             flags that target-return won; a non-target terminal
//                             impact returns nothing and does not win.
// ============================================================================

using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Archer;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimPracticeRangeTests
    {
        private readonly WorldId _world = new WorldId("uid:pr-025");
        private readonly StoneId _stone;

        private readonly AccountId _owner = new AccountId("acct-owner");
        private readonly CharacterId _ownerChar = new CharacterId("char-owner");
        private readonly AccountId _guest = new AccountId("acct-guest");
        private readonly AccountId _stranger = new AccountId("acct-stranger");

        private static readonly VersionedId Archer = HomesteadProgressionCatalog.ArcherTree;
        private static readonly VersionedId PracticeRange = new VersionedId("PracticeRange", 1);

        private readonly HomesteadProgressionCatalog _catalog = new HomesteadProgressionCatalog();
        private readonly PracticeRangeProvider _provider;

        public NiflheimPracticeRangeTests()
        {
            _stone = StoneId.FromHostZone(_world, 4, 11);
            _provider = new PracticeRangeProvider(_catalog);
        }

        // A Stone with Practice Range (Archer) developed as Stone-owned Local state, the Archer Tree
        // committed, at Active Level 2. Optional overrides drive the dormancy branches.
        private StoneProgressionAggregate BuildStone(long revision = 5, SettlementLocalPolicy? policy = null,
            bool archerCommitted = true, int activeLevel = 2, bool practiceRangeDeveloped = true)
        {
            var committed = new List<CommittedTreeRecord>();
            if (archerCommitted)
                committed.Add(new CommittedTreeRecord(HomesteadProgressionCatalog.MartialFacetId,
                    Archer, "seed-commit-archer", _ownerChar.Value, 1, 0));

            var development = new List<NodeDevelopmentRecord>();
            if (practiceRangeDeveloped)
                development.Add(new NodeDevelopmentRecord(PracticeRange, 1, 1, true, false, "seed-dev-pr"));

            return new StoneProgressionAggregate(_stone, revision,
                historicalStoneLevel: 2, activeStoneLevel: activeLevel,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "created", updatedProvenance: "seed",
                mirroredStoneAp: 0, lastAppliedReceiptId: "r-seed",
                committedTrees: committed, nodeDevelopment: development,
                localPolicy: policy);
        }

        private PracticeRangeCapability ResolveFor(StoneProgressionAggregate stone, AccountId occupant,
            bool isOwner, bool hasRelationship, bool inside, bool governorPresent, bool buildPermission)
            => _provider.Resolve(stone, occupant, isOwner, hasRelationship, inside, governorPresent, buildPermission);

        // ============================================================================
        //  AT-PRACTICE-RANGE — capability = active Local Effect AND ordinary build Permission.
        // ============================================================================

        [Fact]
        public void Active_local_and_build_permission_unlock_both_placement_and_recipe()
        {
            var stone = BuildStone(); // default Everyone policy
            var cap = ResolveFor(stone, _owner, isOwner: true, hasRelationship: false,
                inside: true, governorPresent: true, buildPermission: true);

            Assert.True(cap.EffectActive);
            Assert.True(cap.CanPlaceArcheryTarget);
            Assert.True(cap.CanCraftPracticeArrows);
        }

        [Fact]
        public void Policy_eligible_but_no_build_permission_cannot_place_or_craft()
        {
            var stone = BuildStone();
            var cap = ResolveFor(stone, _owner, isOwner: true, hasRelationship: false,
                inside: true, governorPresent: true, buildPermission: false);

            Assert.True(cap.EffectActive);            // the effect IS active for them
            Assert.False(cap.CanPlaceArcheryTarget);  // but policy never grants the build ACL
            Assert.False(cap.CanCraftPracticeArrows);
        }

        [Fact]
        public void Build_permitted_but_outside_policy_cannot_place_or_craft()
        {
            // Attuned policy: an unrelated stranger is not a beneficiary even with build Permission.
            var stone = BuildStone(policy: new SettlementLocalPolicy(LocalBeneficiaryMode.Attuned, 1, null));
            var cap = ResolveFor(stone, _stranger, isOwner: false, hasRelationship: false,
                inside: true, governorPresent: true, buildPermission: true);

            Assert.False(cap.EffectActive);
            Assert.False(cap.CanPlaceArcheryTarget);
            Assert.False(cap.CanCraftPracticeArrows);
        }

        [Fact]
        public void Attuned_guest_with_permission_gets_capability()
        {
            var stone = BuildStone(policy: new SettlementLocalPolicy(LocalBeneficiaryMode.Attuned, 1, null));
            var cap = ResolveFor(stone, _guest, isOwner: false, hasRelationship: true,
                inside: true, governorPresent: true, buildPermission: true);

            Assert.True(cap.EffectActive);
            Assert.True(cap.CanPlaceArcheryTarget);
            Assert.True(cap.CanCraftPracticeArrows);
        }

        [Fact]
        public void Outside_stone_area_suppresses_capability_even_with_permission()
        {
            var stone = BuildStone();
            var cap = ResolveFor(stone, _owner, isOwner: true, hasRelationship: false,
                inside: false, governorPresent: true, buildPermission: true);

            Assert.False(cap.EffectActive);
            Assert.False(cap.CanPlaceArcheryTarget);
            Assert.False(cap.CanCraftPracticeArrows);
        }

        [Fact]
        public void Missing_authorized_governor_dormants_capability()
        {
            var stone = BuildStone();
            var cap = ResolveFor(stone, _owner, isOwner: true, hasRelationship: false,
                inside: true, governorPresent: false, buildPermission: true);

            Assert.False(cap.EffectActive);
            Assert.False(cap.CanPlaceArcheryTarget);
        }

        [Fact]
        public void Archer_tree_not_committed_dormants_capability()
        {
            var stone = BuildStone(archerCommitted: false);
            var cap = ResolveFor(stone, _owner, isOwner: true, hasRelationship: false,
                inside: true, governorPresent: true, buildPermission: true);

            Assert.False(cap.EffectActive);
            Assert.False(cap.CanCraftPracticeArrows);
        }

        [Fact]
        public void Active_stone_level_below_node_level_dormants_capability()
        {
            var stone = BuildStone(activeLevel: 0);
            var cap = ResolveFor(stone, _owner, isOwner: true, hasRelationship: false,
                inside: true, governorPresent: true, buildPermission: true);

            Assert.False(cap.EffectActive);
        }

        [Fact]
        public void Undeveloped_practice_range_never_grants_capability()
        {
            var stone = BuildStone(practiceRangeDeveloped: false);
            var cap = ResolveFor(stone, _owner, isOwner: true, hasRelationship: false,
                inside: true, governorPresent: true, buildPermission: true);

            Assert.False(cap.EffectActive);
            Assert.False(cap.CanPlaceArcheryTarget);
            Assert.False(cap.CanCraftPracticeArrows);
        }

        [Fact]
        public void Capability_exposes_exact_vanilla_archery_target_and_practice_arrow_recipe()
        {
            var stone = BuildStone();
            var cap = ResolveFor(stone, _owner, true, false, true, true, true);

            // The placement capability is the EXACT vanilla Archery Target piece prefab.
            Assert.Equal(PracticeRangeContent.ArcheryTargetPrefab, cap.ArcheryTargetPrefab);
            // Pin the CORRECTED vanilla prefab id (capital A/T). Verified against the running build's
            // StreamingAssets/SoftRef/manifest_extended + the decompiled ArcheryTarget component; the
            // earlier `piece_archery_target` was wrong and orphaned the runtime piece binding.
            Assert.Equal("piece_ArcheryTarget", PracticeRangeContent.ArcheryTargetPrefab);
            Assert.Equal("ArrowPractice", PracticeRangeContent.PracticeArrowItem);
            // The recipe capability is the authored Practice Arrow recipe: 100 for 8 Wood.
            Assert.Equal(PracticeRangeContent.PracticeArrowRecipe.OutputItem, cap.PracticeArrowRecipe.OutputItem);
            Assert.Equal(100, cap.PracticeArrowRecipe.OutputCount);
            Assert.Equal(8, cap.PracticeArrowRecipe.WoodCost);
        }

        // ============================================================================
        //  AT-PRACTICE-ARROW-DAMAGE — 100 for 8 Wood, 0 ammo damage, bow damage retained.
        // ============================================================================

        [Fact]
        public void Practice_arrow_recipe_is_exactly_100_for_8_wood()
        {
            var recipe = PracticeRangeContent.PracticeArrowRecipe;
            Assert.Equal(100, recipe.OutputCount);
            Assert.Equal(8, recipe.WoodCost);
        }

        [Fact]
        public void Practice_arrow_ammo_damage_is_zero()
        {
            Assert.Equal(0.0, PracticeRangeContent.PracticeArrowAmmoDamage);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(22.0)]
        [InlineData(47.5)]
        public void Fired_practice_arrow_retains_bow_damage_with_zero_ammo_contribution(double bowDamage)
        {
            var profile = PracticeRangeProvider.ResolvePracticeArrowDamage(bowDamage);
            Assert.Equal(bowDamage, profile.BowDamage);
            Assert.Equal(0.0, profile.AmmoDamage);            // 0 ammo damage
            Assert.Equal(bowDamage, profile.EffectiveDamage); // bow damage fully retained
        }

        // ============================================================================
        //  AT-TARGET-RETURN — deterministic vanilla return, no roll.
        // ============================================================================

        [Fact]
        public void Practice_arrow_impacting_archery_target_is_deterministically_returned_once()
        {
            var decision = PracticeRangeProvider.ResolveTargetReturn(TerminalImpactSurface.ArcheryTarget);
            Assert.True(decision.TargetReturnWon);
            Assert.True(decision.ArrowReturned);
            Assert.Equal(1, decision.ReturnedCount);
            Assert.True(decision.Deterministic);
        }

        [Fact]
        public void Target_return_is_stable_across_repeated_evaluations()
        {
            // No RNG: the same surface yields an identical decision every time.
            var a = PracticeRangeProvider.ResolveTargetReturn(TerminalImpactSurface.ArcheryTarget);
            var b = PracticeRangeProvider.ResolveTargetReturn(TerminalImpactSurface.ArcheryTarget);
            Assert.Equal(a.TargetReturnWon, b.TargetReturnWon);
            Assert.Equal(a.ReturnedCount, b.ReturnedCount);
        }

        [Theory]
        [InlineData(TerminalImpactSurface.Ground)]
        [InlineData(TerminalImpactSurface.Water)]
        [InlineData(TerminalImpactSurface.Creature)]
        [InlineData(TerminalImpactSurface.LostOrExpired)]
        public void Non_target_terminal_impact_returns_nothing_and_does_not_win(TerminalImpactSurface surface)
        {
            var decision = PracticeRangeProvider.ResolveTargetReturn(surface);
            Assert.False(decision.TargetReturnWon);
            Assert.False(decision.ArrowReturned);
            Assert.Equal(0, decision.ReturnedCount);
        }
    }
}
