// ============================================================================
//  Homestead progression — COOKING: Savor the Hearth provider tests (T016, US4).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free T016 Cooking adapter/provider surface
//  (link-compiled from ../src):
//    * SavorTheHearthProvider — the derived food-timer drain-factor provider
//      that reads the T014 LocalEffectActivationView active-state for the Savor
//      Local node and answers the vanilla drain factor (0.5 active / 1.0 not),
//      with NO item/stat mutation and NO retroactive duration.
//
//  Named acceptance closed here (tasks.md T016 / plan.md US4):
//    AT-SAVOR-AREA-EXIT  a policy-eligible occupant inside the Stone Area with an
//                        active Savor Local node drains active food timers at
//                        factor 0.5; stepping OUTSIDE the Area (or losing policy
//                        eligibility / governance) restores factor 1 IMMEDIATELY
//                        on the next derivation, with zero writes and no
//                        retroactive refund/clawback of already-consumed time.
//
//  Honesty: these are REAL executions of the shipped provider + the shipped
//  LocalEffectActivationView derivation (both engine-free, link-compiled into the
//  net8 host). They prove the pure delivery grammar; they do NOT prove a joined
//  Valheim client sees the factor in-world — that is the node's joined-client
//  artifact (docs/v2/evidence/homestead-progression/tracer-5-cooking/).
// ============================================================================

using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Cooking;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimSavorTheHearthTests
    {
        private readonly WorldId _world = new WorldId("uid:savor-016");
        private readonly StoneId _stone;

        private readonly AccountId _owner = new AccountId("acct-owner");
        private readonly CharacterId _ownerChar = new CharacterId("char-owner");
        private readonly AccountId _guest = new AccountId("acct-guest");
        private readonly AccountId _stranger = new AccountId("acct-stranger");

        private static readonly VersionedId Cooking = HomesteadProgressionCatalog.CookingTree;
        private static readonly VersionedId Warrior = HomesteadProgressionCatalog.WarriorTree;
        private static readonly VersionedId Savor = new VersionedId("SavorTheHearth", 1);
        private static readonly VersionedId Twig = new VersionedId("TwigTraining", 1);

        private readonly HomesteadProgressionCatalog _catalog = new HomesteadProgressionCatalog();
        private readonly SavorTheHearthProvider _provider = new SavorTheHearthProvider();

        public NiflheimSavorTheHearthTests()
        {
            _stone = StoneId.FromHostZone(_world, 4, 11);
        }

        // A Stone with Savor (Cooking) + T.W.I.G. (Warrior) developed as Stone-owned Local state, both
        // Trees committed, at Active/Historical Level 2. Mirrors the T014 fixture builder.
        private StoneProgressionAggregate BuildStone(SettlementLocalPolicy? policy = null,
            bool cookingCommitted = true, int activeLevel = 2, bool savorDeveloped = true)
        {
            var committed = new List<CommittedTreeRecord>();
            if (cookingCommitted)
                committed.Add(new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                    Cooking, "seed-commit-cook", _ownerChar.Value, 1, 0));
            committed.Add(new CommittedTreeRecord(HomesteadProgressionCatalog.MartialFacetId,
                Warrior, "seed-commit-war", _ownerChar.Value, 1, 0));

            var development = new List<NodeDevelopmentRecord>
            {
                new NodeDevelopmentRecord(Savor, 1, 1, savorDeveloped, false, "seed-dev-savor"),
                new NodeDevelopmentRecord(Twig, 1, 1, true, false, "seed-dev-twig"),
            };

            return new StoneProgressionAggregate(_stone, revision: 5,
                historicalStoneLevel: 2, activeStoneLevel: activeLevel,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "created", updatedProvenance: "seed",
                mirroredStoneAp: 0, lastAppliedReceiptId: "r-seed",
                committedTrees: committed, nodeDevelopment: development,
                localPolicy: policy);
        }

        private LocalEffectActivationView DeriveFor(StoneProgressionAggregate stone, AccountId occupant,
            bool isOwner, bool hasRelationship, bool inside, bool governorPresent)
            => LocalEffectActivationView.Derive(stone, _catalog, occupant, isOwner,
                hasRelationship, inside, governorPresent);

        // ============================================================================
        //  AT-SAVOR-AREA-EXIT — factor 0.5 inside the Area, factor 1 on exit, immediate.
        // ============================================================================

        [Fact]
        public void Eligible_occupant_inside_area_drains_at_half_factor()
        {
            var stone = BuildStone(); // default Everyone policy
            var inside = DeriveFor(stone, _owner, isOwner: true, hasRelationship: true, inside: true, governorPresent: true);

            Assert.True(_provider.IsSlowing(inside));
            Assert.Equal(0.5, _provider.DrainFactor(inside));
        }

        [Fact]
        public void Stepping_outside_area_restores_full_factor_immediately()
        {
            var stone = BuildStone();
            // Same occupant, same Stone — only the server-observed "inside Area" fact flips. The provider
            // carries no state, so re-deriving with inside:false flips 0.5→1 on the very next evaluation.
            var inside = DeriveFor(stone, _owner, isOwner: true, hasRelationship: true, inside: true, governorPresent: true);
            var outside = DeriveFor(stone, _owner, isOwner: true, hasRelationship: true, inside: false, governorPresent: true);

            Assert.Equal(0.5, _provider.DrainFactor(inside));
            Assert.Equal(1.0, _provider.DrainFactor(outside));
            Assert.False(_provider.IsSlowing(outside));
        }

        [Fact]
        public void Policy_loss_restores_full_factor_even_inside_area()
        {
            // Attuned policy: an unrelated stranger inside the Area is NOT policy-eligible, so no slow.
            var stone = BuildStone(policy: SettlementLocalPolicy.Default.With(LocalBeneficiaryMode.Attuned, null));
            var stranger = DeriveFor(stone, _stranger, isOwner: false, hasRelationship: false, inside: true, governorPresent: true);

            Assert.False(_provider.IsSlowing(stranger));
            Assert.Equal(1.0, _provider.DrainFactor(stranger));
        }

        [Fact]
        public void Governance_dormancy_restores_full_factor()
        {
            var stone = BuildStone();
            // No authorized Governor ⇒ every Local Effect dormant ⇒ full factor, even inside + eligible.
            var noGov = DeriveFor(stone, _owner, isOwner: true, hasRelationship: true, inside: true, governorPresent: false);

            Assert.Equal(1.0, _provider.DrainFactor(noGov));
        }

        [Fact]
        public void Undeveloped_savor_never_slows()
        {
            var stone = BuildStone(savorDeveloped: false);
            var view = DeriveFor(stone, _owner, isOwner: true, hasRelationship: true, inside: true, governorPresent: true);

            Assert.False(_provider.IsSlowing(view));
            Assert.Equal(1.0, _provider.DrainFactor(view));
        }

        // ============================================================================
        //  No mutation / no retroactive duration — the provider only scales the slice
        //  it is handed; already-consumed time is never refunded or clawed back.
        // ============================================================================

        [Fact]
        public void Consume_elapsed_scales_only_the_current_slice()
        {
            var stone = BuildStone();
            var inside = DeriveFor(stone, _owner, isOwner: true, hasRelationship: true, inside: true, governorPresent: true);
            var outside = DeriveFor(stone, _owner, isOwner: true, hasRelationship: true, inside: false, governorPresent: true);

            // 10s inside consumes 5s of food timer (half). 10s after exit consumes the full 10s.
            Assert.Equal(5.0, _provider.ConsumeElapsed(inside, 10.0));
            Assert.Equal(10.0, _provider.ConsumeElapsed(outside, 10.0));
        }

        [Fact]
        public void Exit_does_not_retroactively_refund_previously_slowed_time()
        {
            var stone = BuildStone();
            var inside = DeriveFor(stone, _owner, isOwner: true, hasRelationship: true, inside: true, governorPresent: true);
            var outside = DeriveFor(stone, _owner, isOwner: true, hasRelationship: true, inside: false, governorPresent: true);

            // Simulate a food timer consumed across an in-area slice then a post-exit slice. The two
            // slices are independent — the post-exit slice does not re-scale the already-slowed slice.
            double consumedInside = _provider.ConsumeElapsed(inside, 20.0);   // 10s
            double consumedAfterExit = _provider.ConsumeElapsed(outside, 20.0); // 20s
            Assert.Equal(10.0, consumedInside);
            Assert.Equal(20.0, consumedAfterExit);

            // The aggregate is untouched by any provider evaluation (no item/stat mutation surface exists).
            Assert.Equal(5, stone.Revision);
        }

        [Fact]
        public void Non_positive_elapsed_consumes_nothing()
        {
            var stone = BuildStone();
            var inside = DeriveFor(stone, _owner, isOwner: true, hasRelationship: true, inside: true, governorPresent: true);
            Assert.Equal(0.0, _provider.ConsumeElapsed(inside, 0.0));
            Assert.Equal(0.0, _provider.ConsumeElapsed(inside, -3.0));
        }

        [Fact]
        public void Provider_is_stateless_across_repeated_evaluations()
        {
            var stone = BuildStone();
            var inside = DeriveFor(stone, _owner, isOwner: true, hasRelationship: true, inside: true, governorPresent: true);
            var outside = DeriveFor(stone, _owner, isOwner: true, hasRelationship: true, inside: false, governorPresent: true);

            // Interleave inside/outside evaluations — each answer depends ONLY on the view handed in,
            // proving no hysteresis / no carried state.
            Assert.Equal(0.5, _provider.DrainFactor(inside));
            Assert.Equal(1.0, _provider.DrainFactor(outside));
            Assert.Equal(0.5, _provider.DrainFactor(inside));
            Assert.Equal(1.0, _provider.DrainFactor(outside));
        }

        [Fact]
        public void Guest_gains_slow_when_relationship_and_policy_admit()
        {
            var stone = BuildStone(policy: SettlementLocalPolicy.Default.With(LocalBeneficiaryMode.Attuned, null));
            var guest = DeriveFor(stone, _guest, isOwner: false, hasRelationship: true, inside: true, governorPresent: true);
            Assert.Equal(0.5, _provider.DrainFactor(guest));
        }
    }
}
