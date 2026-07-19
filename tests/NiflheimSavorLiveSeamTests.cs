// ============================================================================
//  Homestead progression — COOKING: Savor the Hearth LIVE DELIVERY SEAM (T016
//  remediation). Exercises the engine-free heart of the net48 food-timer seam:
//  SavorFoodDrainResolver + SavorLocalContextIndex + SavorContextFactory.
// ----------------------------------------------------------------------------
//  These are REAL executions of the shipped resolver over the shipped
//  StoneAreaMembership, LocalEffectActivationView derivation, and
//  SavorTheHearthProvider (all link-compiled from ../src). They prove the LIVE
//  decision the net48 Player.UpdateFood prefix makes each tick — factor 0.5
//  inside an Area with an established active Savor context, 1.0 on Area exit /
//  context clear / policy loss / governance dormancy — WITHOUT any second ledger
//  and scaling ONLY the elapsed slice (no retroactive m_time rewrite).
//
//  The net48 Player.UpdateFood prefix (Features/Cooking/SavorFoodTimerObserver)
//  and the playtest admin/console seam reference Valheim and are NOT
//  link-compiled; this suite covers every gameplay decision they delegate here.
// ============================================================================

using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimSavorLiveSeamTests
    {
        private readonly WorldId _world = new WorldId("uid:savor-live-016");
        private readonly StoneId _stone;
        private readonly StoneId _otherStone;

        // The Stone Area center + a radius. The occupant position picks inside/outside deterministically.
        private const double CenterX = 100.0;
        private const double CenterZ = 200.0;
        private const double Radius = 20.0;

        private readonly SavorFoodDrainResolver _resolver = new SavorFoodDrainResolver();

        public NiflheimSavorLiveSeamTests()
        {
            _stone = StoneId.FromHostZone(_world, 4, 11);
            _otherStone = StoneId.FromHostZone(_world, 9, 9);
        }

        private StoneAreaMembership Membership()
        {
            var m = new StoneAreaMembership();
            m.Register(_stone, CenterX, CenterZ, Radius);
            return m;
        }

        private SavorLocalContextIndex ActiveContextAt(StoneId stoneId, SettlementLocalPolicy? policy = null)
        {
            var index = new SavorLocalContextIndex();
            var stone = SavorContextFactory.DevelopedSavorStone(stoneId, policy ?? SettlementLocalPolicy.Default);
            index.Set(stoneId, new SavorLocalContext(stone, authorizedGovernorPresent: true));
            return index;
        }

        private static SavorOccupant Occupant(double x, double z,
            bool isOwner = false, bool hasRelationship = false, string account = "occupant-a")
            => new SavorOccupant(new AccountId(account), isOwner, hasRelationship, x, z);

        // ── AT-SAVOR-AREA-EXIT: inside the Area with an active context → factor 0.5 ──────────────────

        [Fact]
        public void Inside_area_with_active_context_drains_at_half()
        {
            var membership = Membership();
            var contexts = ActiveContextAt(_stone);
            var occ = Occupant(CenterX, CenterZ);   // dead center → inside

            Assert.Equal(0.5, _resolver.DrainFactor(membership, contexts, occ));
            Assert.Equal(5.0, _resolver.ConsumeElapsed(membership, contexts, occ, 10.0));
        }

        [Fact]
        public void Stepping_outside_area_restores_full_factor_immediately()
        {
            var membership = Membership();
            var contexts = ActiveContextAt(_stone);

            var inside = Occupant(CenterX, CenterZ);
            var outside = Occupant(CenterX + 1000.0, CenterZ);   // far outside the radius

            Assert.Equal(0.5, _resolver.DrainFactor(membership, contexts, inside));
            Assert.Equal(1.0, _resolver.DrainFactor(membership, contexts, outside));
            // The two slices are independent — no retroactive rewrite.
            Assert.Equal(5.0, _resolver.ConsumeElapsed(membership, contexts, inside, 10.0));
            Assert.Equal(10.0, _resolver.ConsumeElapsed(membership, contexts, outside, 10.0));
        }

        [Fact]
        public void No_established_context_is_full_factor_even_inside()
        {
            var membership = Membership();
            var empty = new SavorLocalContextIndex();   // nothing established
            var occ = Occupant(CenterX, CenterZ);

            Assert.Equal(1.0, _resolver.DrainFactor(membership, empty, occ));
        }

        [Fact]
        public void Clearing_context_restores_full_factor_immediately()
        {
            var membership = Membership();
            var contexts = ActiveContextAt(_stone);
            var occ = Occupant(CenterX, CenterZ);

            Assert.Equal(0.5, _resolver.DrainFactor(membership, contexts, occ));
            contexts.Clear(_stone);
            Assert.Equal(1.0, _resolver.DrainFactor(membership, contexts, occ));
            Assert.Equal(0, contexts.Count);
        }

        [Fact]
        public void Context_established_at_a_different_stone_does_not_slow_here()
        {
            var membership = Membership();
            // Active context exists, but keyed to a Stone the occupant is NOT standing in.
            var contexts = ActiveContextAt(_otherStone);
            var occ = Occupant(CenterX, CenterZ);   // inside _stone's Area, not _otherStone

            Assert.Equal(1.0, _resolver.DrainFactor(membership, contexts, occ));
        }

        // ── Policy / governance still gate the derived active status ─────────────────────────────────

        [Fact]
        public void Attuned_policy_unrelated_occupant_is_full_factor_inside()
        {
            var membership = Membership();
            // Attuned policy: a non-owner with no relationship is not policy-eligible → factor 1.
            var contexts = ActiveContextAt(_stone,
                SettlementLocalPolicy.Default.With(LocalBeneficiaryMode.Attuned, null));
            var stranger = Occupant(CenterX, CenterZ, isOwner: false, hasRelationship: false);

            Assert.Equal(1.0, _resolver.DrainFactor(membership, contexts, stranger));
        }

        [Fact]
        public void Attuned_policy_related_occupant_gains_slow_inside()
        {
            var membership = Membership();
            var contexts = ActiveContextAt(_stone,
                SettlementLocalPolicy.Default.With(LocalBeneficiaryMode.Attuned, null));
            var guest = Occupant(CenterX, CenterZ, isOwner: false, hasRelationship: true);

            Assert.Equal(0.5, _resolver.DrainFactor(membership, contexts, guest));
        }

        [Fact]
        public void Governance_dormancy_restores_full_factor()
        {
            var membership = Membership();
            var index = new SavorLocalContextIndex();
            var stone = SavorContextFactory.DevelopedSavorStone(_stone, SettlementLocalPolicy.Default);
            // No authorized Governor present ⇒ every Local Effect dormant ⇒ factor 1 even inside + eligible.
            index.Set(_stone, new SavorLocalContext(stone, authorizedGovernorPresent: false));
            var occ = Occupant(CenterX, CenterZ);

            Assert.Equal(1.0, _resolver.DrainFactor(membership, index, occ));
        }

        // ── No-mutation / slice-only invariants ──────────────────────────────────────────────────────

        [Fact]
        public void Non_positive_elapsed_consumes_nothing()
        {
            var membership = Membership();
            var contexts = ActiveContextAt(_stone);
            var occ = Occupant(CenterX, CenterZ);

            Assert.Equal(0.0, _resolver.ConsumeElapsed(membership, contexts, occ, 0.0));
            Assert.Equal(0.0, _resolver.ConsumeElapsed(membership, contexts, occ, -4.0));
        }

        [Fact]
        public void Resolver_is_stateless_across_interleaved_evaluations()
        {
            var membership = Membership();
            var contexts = ActiveContextAt(_stone);
            var inside = Occupant(CenterX, CenterZ);
            var outside = Occupant(CenterX + 1000.0, CenterZ);

            // Each answer depends ONLY on the inputs handed in — no hysteresis.
            Assert.Equal(0.5, _resolver.DrainFactor(membership, contexts, inside));
            Assert.Equal(1.0, _resolver.DrainFactor(membership, contexts, outside));
            Assert.Equal(0.5, _resolver.DrainFactor(membership, contexts, inside));
            Assert.Equal(1.0, _resolver.DrainFactor(membership, contexts, outside));
        }

        [Fact]
        public void Empty_membership_is_always_full_factor()
        {
            var empty = new StoneAreaMembership();   // no Areas registered
            var contexts = ActiveContextAt(_stone);
            var occ = Occupant(CenterX, CenterZ);

            Assert.Equal(1.0, _resolver.DrainFactor(empty, contexts, occ));
        }
    }
}
