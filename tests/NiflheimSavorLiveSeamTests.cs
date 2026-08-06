// ============================================================================
//  Homestead progression — COOKING: Savor the Hearth LIVE DELIVERY SEAM (T016
//  remediation, rebased onto the merged shared Local Effect runtime PR #368).
// ----------------------------------------------------------------------------
//  Exercises the engine-free heart of the net48 food-timer seam:
//  SavorFoodDrainResolver translating the AUTHORITATIVE per-occupant
//  LocalActivationSnapshot (derived by the shared LocalActivationService from
//  the real Stone aggregate + committed relationship/governance + Settlement
//  policy + observed occupancy) into the vanilla food-timer drain factor.
//
//  These are REAL executions: each snapshot is produced by the shipped
//  LocalActivationService over a Stone whose Savor node was developed through
//  the shared LocalNodeProvisioningDriver's ACCEPTED commands — no family-local
//  ledger, no fabricated activation. They prove the LIVE decision the net48
//  Player.UpdateFood prefix makes each tick: factor 0.5 when the substrate says
//  Savor is active for the occupant, 1.0 on Area exit / policy loss / governance
//  dormancy / denied authority — scaling ONLY the elapsed slice (no retroactive
//  m_time rewrite).
//
//  The net48 Player.UpdateFood prefix + the playtest admin/console seam
//  reference Valheim and are NOT link-compiled; this suite covers the gameplay
//  decision they delegate here.
// ============================================================================

using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimSavorLiveSeamTests : System.IDisposable
    {
        private readonly string _dir;
        private readonly WorldId _world = new WorldId("uid:savor-live-016");
        private readonly StoneId _stone;

        private readonly AccountId _gov = new AccountId("acct-gov");
        private readonly CharacterId _govChar = new CharacterId("char-gov");
        private readonly AccountId _stranger = new AccountId("acct-stranger");
        private readonly CharacterId _strangerChar = new CharacterId("char-stranger");

        private static readonly VersionedId Savor = new VersionedId("SavorTheHearth", 1);

        private readonly SavorFoodDrainResolver _resolver = new SavorFoodDrainResolver();

        public NiflheimSavorLiveSeamTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "niflheim-savor-live-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _stone = StoneId.FromHostZone(_world, 4, 11);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        }

        // ── Shared-substrate composition: develop Savor through accepted commands only ────────────────

        private StoneProgressionAggregate BareStone(long revision) =>
            new StoneProgressionAggregate(_stone, revision, historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "created", updatedProvenance: "seed",
                mirroredStoneAp: 0, lastAppliedReceiptId: "r-seed",
                committedTrees: null, nodeDevelopment: null);

        private CharacterProgressionAggregate Governor() =>
            new CharacterProgressionAggregate(_gov, _govChar, "savor-live/trailborne",
                revision: 3, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[]
                {
                    new CharacterStoneRecord(_stone, 0, 0, 0, purchases: null,
                        relationships: new[]
                        {
                            new RelationshipRecord("rel-bond-gov", RelationshipKind.Bond,
                                RelationshipStatus.Active, "Homestead:All", "Governor",
                                "relreceipt:seed-bond", string.Empty)
                        })
                });

        private AccountStoneAuthorityIndex BondIndex() =>
            AccountStoneAuthorityIndex.Vacant(_gov, _stone).WithReservationAdded(
                new AuthorityReservation(_govChar, RelationshipKind.Bond, "rel-bond-gov",
                    "relreceipt:seed-bond"), 1);

        private LocalProgressionServer ProvisionedServer()
        {
            var stones = new InMemoryStoneAggregateStore();
            var characters = new InMemoryCharacterAggregateStore();
            var authority = new InMemoryAccountStoneAuthorityStore();
            stones.PutStone(BareStone(revision: 10));
            characters.PutCharacter(Governor());
            authority.ApplyAuthorityProjection("seed-bond", BondIndex());

            var relationships = new RelationshipCommandHandler(
                Path.Combine(_dir, "relationships.journal"), new PrincipalResolver(), characters, authority,
                new SavorFixedFamilyResolver(), new SavorAllowBondPolicy(), null, _world,
                new ProductScope("SBPR.Trailborne"));

            var server = LocalProgressionServer.Create(
                _dir, stones, characters, authority, relationships,
                new SavorFixedFamilyResolver(), new SavorAllowGovernorAuthority(),
                new SavorAllowDevelopmentAuthority(),
                new CommittedGovernorOwnerAuthority(new GovernorPresenceResolver(characters, authority)));

            var driver = new LocalNodeProvisioningDriver(server);
            var result = driver.Provision(new AuthoritativeSubject(_gov, _govChar), _stone, Savor, "qa-savor");
            Assert.True(result.IsDeveloped, "QA provisioning must develop Savor via accepted commands: "
                + result.FailedStep + "/" + result.ResultCode);
            // Everyone policy so an in-area occupant is eligible by default.
            driver.SetPolicy(new AuthoritativeSubject(_gov, _govChar), _stone, LocalBeneficiaryMode.Everyone, null, "qa-savor-policy");
            return server;
        }

        private static OccupantPresence Presence(AccountId occ, CharacterId ch, bool owner, bool rel,
            bool inside, bool gov) => new OccupantPresence(occ, ch, owner, rel, inside, gov);

        // ── AT-SAVOR-AREA-EXIT: inside the Area with an active derived effect → factor 0.5 ────────────

        [Fact]
        public void Inside_area_with_active_effect_drains_at_half()
        {
            var server = ProvisionedServer();
            var snap = server.Activation.Fetch(_stone,
                Presence(_gov, _govChar, owner: true, rel: true, inside: true, gov: true));

            Assert.True(snap.IsActive(Savor));
            Assert.Equal(0.5, _resolver.DrainFactor(snap));
            Assert.Equal(5.0, _resolver.ConsumeElapsed(snap, 10.0));
        }

        [Fact]
        public void Stepping_outside_area_restores_full_factor_immediately()
        {
            var server = ProvisionedServer();
            var inside = server.Activation.Fetch(_stone,
                Presence(_gov, _govChar, true, true, inside: true, gov: true));
            var outside = server.Activation.Fetch(_stone,
                Presence(_gov, _govChar, true, true, inside: false, gov: true));

            Assert.Equal(0.5, _resolver.DrainFactor(inside));
            Assert.Equal(1.0, _resolver.DrainFactor(outside));
            // The two slices are independent — no retroactive rewrite.
            Assert.Equal(5.0, _resolver.ConsumeElapsed(inside, 10.0));
            Assert.Equal(10.0, _resolver.ConsumeElapsed(outside, 10.0));
        }

        [Fact]
        public void Denied_or_null_snapshot_is_full_factor()
        {
            var denied = LocalActivationSnapshot.Denied(_stone, _gov, 0);
            Assert.Equal(1.0, _resolver.DrainFactor(denied));
            Assert.Equal(1.0, _resolver.DrainFactor(null));
            Assert.Equal(10.0, _resolver.ConsumeElapsed(null, 10.0));
        }

        // ── Policy / governance still gate the derived active status ─────────────────────────────────

        [Fact]
        public void Attuned_policy_unrelated_occupant_is_full_factor_inside()
        {
            var server = ProvisionedServer();
            // Switch to Attuned: a non-owner with no relationship is not policy-eligible → factor 1.
            new LocalNodeProvisioningDriver(server).SetPolicy(
                new AuthoritativeSubject(_gov, _govChar), _stone, LocalBeneficiaryMode.Attuned, null, "attune");

            var snap = server.Activation.Fetch(_stone,
                Presence(_stranger, _strangerChar, owner: false, rel: false, inside: true, gov: true));
            Assert.Equal(1.0, _resolver.DrainFactor(snap));
        }

        [Fact]
        public void Governance_dormancy_restores_full_factor()
        {
            var server = ProvisionedServer();
            // No authorized Governor present ⇒ every Local Effect dormant ⇒ factor 1 even inside + eligible.
            var snap = server.Activation.Fetch(_stone,
                Presence(_gov, _govChar, owner: true, rel: true, inside: true, gov: false));
            Assert.Equal(1.0, _resolver.DrainFactor(snap));
        }

        // ── No-mutation / slice-only invariants ──────────────────────────────────────────────────────

        [Fact]
        public void Non_positive_elapsed_consumes_nothing()
        {
            var server = ProvisionedServer();
            var snap = server.Activation.Fetch(_stone,
                Presence(_gov, _govChar, true, true, inside: true, gov: true));

            Assert.Equal(0.0, _resolver.ConsumeElapsed(snap, 0.0));
            Assert.Equal(0.0, _resolver.ConsumeElapsed(snap, -4.0));
        }

        [Fact]
        public void Resolver_is_stateless_across_interleaved_evaluations()
        {
            var server = ProvisionedServer();
            var inside = server.Activation.Fetch(_stone,
                Presence(_gov, _govChar, true, true, inside: true, gov: true));
            var outside = server.Activation.Fetch(_stone,
                Presence(_gov, _govChar, true, true, inside: false, gov: true));

            // Each answer depends ONLY on the snapshot handed in — no hysteresis.
            Assert.Equal(0.5, _resolver.DrainFactor(inside));
            Assert.Equal(1.0, _resolver.DrainFactor(outside));
            Assert.Equal(0.5, _resolver.DrainFactor(inside));
            Assert.Equal(1.0, _resolver.DrainFactor(outside));
        }
    }

    // Deterministic authority stubs (mirror the shared-substrate test stubs) — engine-free.
    file sealed class SavorFixedFamilyResolver : IStoneFamilyResolver
    {
        public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
        {
            family = "Settlement"; variant = "Homestead"; return true;
        }
    }

    file sealed class SavorAllowBondPolicy : IBondAuthorityPolicy
    {
        public bool TryAuthorizeBond(StoneId stoneId, string requestedResponsibilityRange,
            out string grantedRange, out string grantedRole)
        {
            grantedRange = requestedResponsibilityRange ?? string.Empty;
            grantedRole = "Governor";
            return string.Equals(requestedResponsibilityRange, "Homestead:All", System.StringComparison.Ordinal);
        }
    }

    file sealed class SavorAllowGovernorAuthority : IGovernorAuthorityPolicy
    {
        public bool CanCommit(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
            string facetId, FacetCategory category) => true;
    }

    file sealed class SavorAllowDevelopmentAuthority : IGovernorDevelopmentAuthority
    {
        public bool CanDevelop(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
            VersionedId tree) => true;
    }
}
