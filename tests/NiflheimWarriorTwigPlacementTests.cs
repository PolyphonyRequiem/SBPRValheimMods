// ============================================================================
//  Homestead progression — WARRIOR T.W.I.G. TRAINING placement tests (T029, US4, Tracer 8).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free T029 slice (link-compiled from ../src):
//    * Adapters/Warrior/LocalPlacementProvider — the T.W.I.G. Training Local
//      placement capability. It exposes ONLY the exact unchanged vanilla T.W.I.G.
//      build piece ("TrainingDummy") and admits a placement exactly when the
//      shared T014 LocalEffectActivationView reports the effect active for the
//      occupant AND the occupant independently holds ordinary build Permission.
//
//  Named acceptance closed here (tasks.md T029 / plan.md Tracer 8):
//    AT-TWIG-LOCAL   T.W.I.G. Training exposes the exact unchanged T.W.I.G.
//                    placement, gated by the single Settlement Local policy AND
//                    ordinary build Permission (the load-bearing AND); relationship
//                    release, a missing authorized Governor, Stone/Tree dormancy,
//                    and Active Stone Level all suppress the capability with no
//                    second active-effects ledger; the node never authorizes any
//                    other piece and never overlaps another Tree's Local node.
//
//  Logs-green != playable: this is the engine-free CLEAN slice. The joined-client
//  in-world placement artifact is produced separately (tracer-8-warrior evidence).
// ============================================================================

using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Warrior;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimWarriorTwigPlacementTests
    {
        private readonly WorldId _world = new WorldId("uid:twig-529");
        private readonly StoneId _stone;

        private readonly AccountId _owner = new AccountId("acct-owner");
        private readonly CharacterId _ownerChar = new CharacterId("char-owner");
        private readonly AccountId _guest = new AccountId("acct-guest");
        private readonly AccountId _stranger = new AccountId("acct-stranger");

        private readonly InMemoryStoneAggregateStore _stones = new InMemoryStoneAggregateStore();
        private readonly HomesteadProgressionCatalog _catalog = new HomesteadProgressionCatalog();

        private static readonly VersionedId Cooking = HomesteadProgressionCatalog.CookingTree;
        private static readonly VersionedId Warrior = HomesteadProgressionCatalog.WarriorTree;
        private static readonly VersionedId Twig = new VersionedId("TwigTraining", 1);   // Local L1 Warrior
        private static readonly VersionedId Savor = new VersionedId("SavorTheHearth", 1); // Local L1 Cooking

        private readonly LocalPlacementProvider _provider = new LocalPlacementProvider();

        public NiflheimWarriorTwigPlacementTests()
        {
            _stone = StoneId.FromHostZone(_world, 7, 4);
            _stones.PutStone(BuildStone(revision: 5, policy: null));
        }

        // A Stone with Savor (Cooking) + T.W.I.G. (Warrior) developed as Stone-owned Local state, both
        // owning Trees committed, at Active/Historical Level 2. Mirrors the T014 dormancy harness so the
        // two slices exercise the SAME shared grammar.
        private StoneProgressionAggregate BuildStone(long revision, SettlementLocalPolicy? policy,
            bool warriorCommitted = true, int activeLevel = 2,
            IReadOnlyList<NodeDevelopmentRecord>? dev = null)
        {
            var committed = new List<CommittedTreeRecord>
            {
                new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                    Cooking, "seed-commit-cook", _ownerChar.Value, 1, 0),
            };
            if (warriorCommitted)
                committed.Add(new CommittedTreeRecord(HomesteadProgressionCatalog.MartialFacetId,
                    Warrior, "seed-commit-war", _ownerChar.Value, 1, 0));

            var development = dev ?? new[]
            {
                new NodeDevelopmentRecord(Savor, 1, 1, true, false, "seed-dev-savor"),
                new NodeDevelopmentRecord(Twig, 1, 1, true, false, "seed-dev-twig"),
            };

            return new StoneProgressionAggregate(_stone, revision,
                historicalStoneLevel: 2, activeStoneLevel: activeLevel,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "created", updatedProvenance: "seed",
                mirroredStoneAp: 0, lastAppliedReceiptId: "r-seed",
                committedTrees: committed, nodeDevelopment: development,
                localPolicy: policy);
        }

        private void SetStone(StoneProgressionAggregate stone) => _stones.PutStone(stone);

        private LocalEffectActivationView DeriveFor(AccountId occupant, bool isOwner,
            bool hasRelationship, bool inside, bool governorPresent)
        {
            var stone = _stones.GetStone(_stone)!;
            return LocalEffectActivationView.Derive(stone, _catalog, occupant, isOwner,
                hasRelationship, inside, governorPresent);
        }

        // ============================================================================
        //  Exact-piece grammar — the node exposes ONLY the unchanged vanilla T.W.I.G.
        // ============================================================================

        [Fact]
        public void Provider_binds_the_exact_vanilla_twig_prefab_and_authored_node()
        {
            Assert.Equal("TrainingDummy", LocalPlacementProvider.TwigPrefabName);
            Assert.Equal("TrainingDummy", _provider.PrefabName);
            Assert.Equal("TwigTraining", _provider.Node.Key);
            Assert.Equal(1, _provider.Node.Version);

            // The authored node is a Stone-cultivated Warrior Local Effect (never a purchase / Offered).
            var def = _catalog.TryResolveNode(_provider.Node);
            Assert.NotNull(def);
            Assert.Equal(Warrior.Key, def!.Tree.Key);
            Assert.Equal(NodeOutcomeType.LocalEffect, def.Outcome);
            Assert.Equal(NodeOwnership.StoneCultivated, def.Ownership);
        }

        [Fact]
        public void Active_and_permitted_occupant_may_place_the_exact_twig()
        {
            // Default policy is Everyone: any inside/build-permitted occupant benefits.
            var v = DeriveFor(_stranger, isOwner: false, hasRelationship: false, inside: true, governorPresent: true);

            var decision = _provider.Admit(v, LocalPlacementProvider.TwigPrefabName, hasOrdinaryBuildPermission: true);
            Assert.True(decision.IsAdmitted);
            Assert.Equal(WarriorPlacementAdmission.Admitted, decision.Admission);
            Assert.Equal("TrainingDummy", decision.AuthorizedPrefabName);
        }

        [Fact]
        public void Node_never_authorizes_any_other_piece()
        {
            var v = DeriveFor(_owner, isOwner: true, hasRelationship: true, inside: true, governorPresent: true);

            foreach (var other in new[] { "wood_floor", "piece_workbench", "ArcheryTarget", "trainingdummy", "" })
            {
                var d = _provider.Admit(v, other, hasOrdinaryBuildPermission: true);
                Assert.False(d.IsAdmitted);
                Assert.Equal(WarriorPlacementAdmission.NotTwigPiece, d.Admission);
                Assert.Equal(string.Empty, d.AuthorizedPrefabName);
            }
        }

        // ============================================================================
        //  The load-bearing AND — Settlement policy eligibility AND ordinary Permission.
        // ============================================================================

        [Fact]
        public void Placement_requires_both_policy_eligibility_and_build_permission()
        {
            // Attuned policy: only owner + actively-related occupants are eligible.
            SetStone(BuildStone(revision: 6,
                policy: new SettlementLocalPolicy(LocalBeneficiaryMode.Attuned, 1)));

            var eligible = DeriveFor(_guest, isOwner: false, hasRelationship: true, inside: true, governorPresent: true);
            // Eligible + build Permission => admitted.
            Assert.True(_provider.CanPlace(eligible, LocalPlacementProvider.TwigPrefabName, true));
            // Eligible but NO build Permission => rejected (policy never grants a build ACL).
            var noPerm = _provider.Admit(eligible, LocalPlacementProvider.TwigPrefabName, hasOrdinaryBuildPermission: false);
            Assert.False(noPerm.IsAdmitted);
            Assert.Equal(WarriorPlacementAdmission.MissingBuildPermission, noPerm.Admission);

            // Build-permitted but OUTSIDE the policy => rejected (Permission alone is insufficient).
            var outside = DeriveFor(_stranger, isOwner: false, hasRelationship: false, inside: true, governorPresent: true);
            var notEligible = _provider.Admit(outside, LocalPlacementProvider.TwigPrefabName, hasOrdinaryBuildPermission: true);
            Assert.False(notEligible.IsAdmitted);
            Assert.Equal(WarriorPlacementAdmission.EffectNotActive, notEligible.Admission);
        }

        [Fact]
        public void Provider_admit_matches_shared_CanExercisePlacement_for_the_exact_piece()
        {
            SetStone(BuildStone(revision: 6,
                policy: new SettlementLocalPolicy(LocalBeneficiaryMode.Private, 1, new[] { _guest.Value })));

            foreach (var (acct, owner, rel, inside, gov, perm) in new[]
            {
                (_owner,    true,  false, true,  true,  true),
                (_guest,    false, true,  true,  true,  true),
                (_guest,    false, true,  true,  true,  false),
                (_stranger, false, true,  true,  true,  true),
                (_owner,    true,  false, false, true,  true),  // outside area
                (_owner,    true,  false, true,  false, true),  // no governor
            })
            {
                var v = DeriveFor(acct, owner, rel, inside, gov);
                bool shared = v.CanExercisePlacement(Twig, perm);
                bool provider = _provider.CanPlace(v, LocalPlacementProvider.TwigPrefabName, perm);
                Assert.Equal(shared, provider);
            }
        }

        // ============================================================================
        //  Relationship / governance / level dormancy — no second active-effects ledger.
        // ============================================================================

        [Fact]
        public void Missing_authorized_governor_suppresses_placement_but_retains_development()
        {
            var v = DeriveFor(_owner, isOwner: true, hasRelationship: true, inside: true, governorPresent: false);
            var d = _provider.Admit(v, LocalPlacementProvider.TwigPrefabName, hasOrdinaryBuildPermission: true);
            Assert.False(d.IsAdmitted);
            Assert.Equal(WarriorPlacementAdmission.EffectNotActive, d.Admission);
            // Development is retained — dormancy deletes nothing.
            Assert.True(v.StatusFor(Twig).Developed);
            Assert.True(v.StatusFor(Twig).Dormant);
        }

        [Fact]
        public void Uncommitted_warrior_tree_makes_twig_dormant()
        {
            SetStone(BuildStone(revision: 6, policy: null, warriorCommitted: false));
            var v = DeriveFor(_owner, isOwner: true, hasRelationship: true, inside: true, governorPresent: true);
            Assert.False(_provider.CanPlace(v, LocalPlacementProvider.TwigPrefabName, true));
            Assert.Equal(WarriorPlacementAdmission.EffectNotActive,
                _provider.Admit(v, LocalPlacementProvider.TwigPrefabName, true).Admission);
        }

        [Fact]
        public void Active_stone_level_below_node_level_makes_twig_dormant()
        {
            // Node authored at Tree Level 1; dropping Active Stone Level below it dormants the effect.
            SetStone(BuildStone(revision: 6, policy: null, activeLevel: 0));
            var v = DeriveFor(_owner, isOwner: true, hasRelationship: true, inside: true, governorPresent: true);
            Assert.False(_provider.CanPlace(v, LocalPlacementProvider.TwigPrefabName, true));
        }

        [Fact]
        public void Outside_stone_area_suppresses_placement()
        {
            var v = DeriveFor(_owner, isOwner: true, hasRelationship: true, inside: false, governorPresent: true);
            var d = _provider.Admit(v, LocalPlacementProvider.TwigPrefabName, hasOrdinaryBuildPermission: true);
            Assert.False(d.IsAdmitted);
            Assert.Equal(WarriorPlacementAdmission.EffectNotActive, d.Admission);
        }

        [Fact]
        public void Relationship_release_then_rejoin_rederives_with_zero_writes()
        {
            SetStone(BuildStone(revision: 6,
                policy: new SettlementLocalPolicy(LocalBeneficiaryMode.Attuned, 1)));

            // Related guest can place.
            var related = DeriveFor(_guest, isOwner: false, hasRelationship: true, inside: true, governorPresent: true);
            Assert.True(_provider.CanPlace(related, LocalPlacementProvider.TwigPrefabName, true));

            // Release the relationship — same persisted Stone, re-derived view, capability gone.
            var released = DeriveFor(_guest, isOwner: false, hasRelationship: false, inside: true, governorPresent: true);
            Assert.False(_provider.CanPlace(released, LocalPlacementProvider.TwigPrefabName, true));

            // Rejoin — capability returns, no mutation anywhere.
            var rejoined = DeriveFor(_guest, isOwner: false, hasRelationship: true, inside: true, governorPresent: true);
            Assert.True(_provider.CanPlace(rejoined, LocalPlacementProvider.TwigPrefabName, true));
        }

        // ============================================================================
        //  No overlap with another Tree's Local node.
        // ============================================================================

        [Fact]
        public void Twig_provider_does_not_authorize_the_cooking_local_prefab()
        {
            // Even when the occupant is fully eligible, the T.W.I.G. provider governs ONLY the T.W.I.G.
            // piece — Savor's (or any other Tree's) Local placement is not its concern.
            var v = DeriveFor(_owner, isOwner: true, hasRelationship: true, inside: true, governorPresent: true);
            var d = _provider.Admit(v, "piece_cooking_station", hasOrdinaryBuildPermission: true);
            Assert.False(d.IsAdmitted);
            Assert.Equal(WarriorPlacementAdmission.NotTwigPiece, d.Admission);
        }
    }
}
