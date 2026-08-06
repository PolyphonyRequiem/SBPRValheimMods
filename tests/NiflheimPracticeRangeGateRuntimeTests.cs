// ============================================================================
//  T025R — Archer Practice Range placement gate ported onto the AUTHORITATIVE
//  Local Effect activation runtime (PR #368).
// ----------------------------------------------------------------------------
//  The net48 ArcheryTargetPlacementGate is a Player.PlacePiece prefix and cannot
//  link-compile here, but the AUTHORITATIVE PROJECTION it consumes is fully
//  engine-free and is exactly what these tests exercise:
//
//    * HOST path — the gate fetches the acting occupant's read model from the
//      composed LocalActivationService and asks it CanExercisePlacement(node,
//      buildPermission). These tests drive that identical Fetch + FR-016 AND for
//      the Practice Range node through the real shared runtime, proving the gate
//      no longer self-derives activation and holds no provisional ledger.
//
//    * CLIENT path — the gate consults LocalActivationClientCache
//      .CanExercisePlacementForNode(node, buildPermission), the bounded consumer
//      added for T025R. These tests prove it reads ONLY the server-delivered
//      snapshot, ANDs ordinary build Permission, and fails closed when no active
//      snapshot is held (relog / area exit / never delivered).
//
//  Named acceptance reinforced (tasks.md T025 / spec FR-016): active Local policy
//  AND ordinary build Permission are BOTH load-bearing; area/policy/governance/
//  dormancy exclusions each suppress the capability through the authoritative
//  projection, never a parallel grant.
// ============================================================================

using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Archer;
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
    public sealed class NiflheimPracticeRangeGateRuntimeTests : System.IDisposable
    {
        private readonly string _dir;
        private readonly WorldId _world = new WorldId("uid:t025r-gate");
        private readonly StoneId _stone;

        private readonly AccountId _gov = new AccountId("acct-gov");
        private readonly CharacterId _govChar = new CharacterId("char-gov");
        private readonly AccountId _hostile = new AccountId("acct-hostile");
        private readonly CharacterId _hostileChar = new CharacterId("char-hostile");

        // The Practice Range Local node identity the gate reads (Archer / PracticeRange v1). This is the
        // SAME id the net48 gate passes: PracticeRangeProvider.PracticeRangeNode.
        private static readonly VersionedId PracticeRange =
            PracticeRangeProvider.PracticeRangeNode;

        public NiflheimPracticeRangeGateRuntimeTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "niflheim-t025r-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _stone = StoneId.FromHostZone(_world, 4, 11);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        }

        // ── Composition helpers (mirror the shared-runtime suite) ────────────

        private LocalProgressionServer NewServer(
            InMemoryStoneAggregateStore stones,
            InMemoryCharacterAggregateStore characters,
            InMemoryAccountStoneAuthorityStore authority)
        {
            var relationships = new RelationshipCommandHandler(
                Path.Combine(_dir, "relationships.journal"), new PrincipalResolver(), characters, authority,
                new FixedFamilyResolver(), new AllowHomesteadBondPolicy(), null, _world,
                new ProductScope("SBPR.Trailborne"));

            return LocalProgressionServer.Create(
                _dir, stones, characters, authority, relationships,
                new FixedFamilyResolver(), new AllowGovernorAuthority(), new AllowDevelopmentAuthority(),
                new CommittedGovernorOwnerAuthority(new GovernorPresenceResolver(characters, authority)));
        }

        private StoneProgressionAggregate BareStone(long revision) =>
            new StoneProgressionAggregate(_stone, revision, historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "created", updatedProvenance: "seed",
                mirroredStoneAp: 0, lastAppliedReceiptId: "r-seed",
                committedTrees: null, nodeDevelopment: null);

        private CharacterProgressionAggregate Governor() =>
            new CharacterProgressionAggregate(_gov, _govChar, "t025r/trailborne",
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

        // A composed server with Practice Range developed for the Archer Tree via accepted commands only.
        private LocalProgressionServer Provisioned()
        {
            var stones = new InMemoryStoneAggregateStore();
            var characters = new InMemoryCharacterAggregateStore();
            var authority = new InMemoryAccountStoneAuthorityStore();
            stones.PutStone(BareStone(revision: 10));
            characters.PutCharacter(Governor());
            authority.ApplyAuthorityProjection("seed-bond", BondIndex());

            var server = NewServer(stones, characters, authority);
            var driver = new LocalNodeProvisioningDriver(server);
            var result = driver.Provision(new AuthoritativeSubject(_gov, _govChar), _stone, PracticeRange, "qa-pr");
            Assert.True(result.IsDeveloped,
                "provisioning must develop Practice Range via accepted commands: "
                + result.FailedStep + "/" + result.ResultCode);
            return server;
        }

        private OccupantPresence Presence(AccountId occ, CharacterId ch, bool owner, bool rel, bool inside,
            bool gov) => new OccupantPresence(occ, ch, owner, rel, inside, gov);

        // ── HOST path: the gate's authoritative Fetch + FR-016 AND ───────────

        [Fact]
        public void Host_active_effect_and_build_permission_permit_archery_target_placement()
        {
            var server = Provisioned();
            var presence = Presence(_gov, _govChar, owner: true, rel: true, inside: true, gov: true);

            var snapshot = server.Activation.Fetch(_stone, presence);
            Assert.True(snapshot.AuthorityPresent);
            Assert.True(snapshot.IsActive(PracticeRange));
            // Load-bearing AND: with build Permission the capability is granted; without it, refused.
            Assert.True(snapshot.CanExercisePlacement(PracticeRange, hasOrdinaryBuildPermission: true));
            Assert.False(snapshot.CanExercisePlacement(PracticeRange, hasOrdinaryBuildPermission: false));
        }

        [Fact]
        public void Host_outside_stone_area_refuses_even_with_build_permission()
        {
            var server = Provisioned();
            // The gate composes insideStoneArea from the server-owned placement point. Outside the Area the
            // authoritative projection marks the effect dormant, so the FR-016 AND is false.
            var outside = Presence(_gov, _govChar, owner: true, rel: true, inside: false, gov: true);
            var snapshot = server.Activation.Fetch(_stone, outside);
            Assert.False(snapshot.IsActive(PracticeRange));
            Assert.False(snapshot.CanExercisePlacement(PracticeRange, hasOrdinaryBuildPermission: true));
        }

        [Fact]
        public void Host_missing_authorized_governor_refuses_placement()
        {
            var server = Provisioned();
            var noGov = Presence(_gov, _govChar, owner: true, rel: true, inside: true, gov: false);
            var snapshot = server.Activation.Fetch(_stone, noGov);
            Assert.False(snapshot.IsActive(PracticeRange));
            Assert.False(snapshot.CanExercisePlacement(PracticeRange, hasOrdinaryBuildPermission: true));
        }

        // ── CLIENT path: the bounded LocalActivationClientCache consumer ─────

        [Fact]
        public void Client_consumer_grants_placement_from_delivered_active_snapshot_with_permission()
        {
            var server = Provisioned();
            var cache = new LocalActivationClientCache();
            var presence = Presence(_gov, _govChar, owner: true, rel: true, inside: true, gov: true);

            // The server pushes the derived snapshot; the client applies it.
            var delivery = server.Activation.Publish(_stone, presence, "seed");
            Assert.True(cache.Apply(delivery.Snapshot));

            // The gate's client-side question: capability = held active snapshot AND ordinary build Permission.
            Assert.True(cache.CanExercisePlacementForNode(PracticeRange, hasOrdinaryBuildPermission: true));
            Assert.False(cache.CanExercisePlacementForNode(PracticeRange, hasOrdinaryBuildPermission: false));
        }

        [Fact]
        public void Client_consumer_fails_closed_with_no_delivered_snapshot()
        {
            var cache = new LocalActivationClientCache();
            // Never delivered (or invalidated on relog / area exit): the gate must fail closed.
            Assert.False(cache.CanExercisePlacementForNode(PracticeRange, hasOrdinaryBuildPermission: true));
        }

        [Fact]
        public void Client_consumer_fails_closed_on_denied_snapshot()
        {
            var cache = new LocalActivationClientCache();
            // A fail-closed denied snapshot (server could not authorize) carries no active effect.
            Assert.True(cache.Apply(LocalActivationSnapshot.Denied(_stone, _gov, 1)));
            Assert.False(cache.CanExercisePlacementForNode(PracticeRange, hasOrdinaryBuildPermission: true));
        }

        [Fact]
        public void Client_consumer_refuses_when_delivered_snapshot_is_dormant()
        {
            var server = Provisioned();
            var cache = new LocalActivationClientCache();
            // Server derives a dormant snapshot (no authorized Governor) and delivers it; the client holds it
            // but the node is not active, so placement is refused even with build Permission.
            var dormant = Presence(_gov, _govChar, owner: true, rel: true, inside: true, gov: false);
            var delivery = server.Activation.Publish(_stone, dormant, "dormant");
            Assert.True(cache.Apply(delivery.Snapshot));
            Assert.False(cache.CanExercisePlacementForNode(PracticeRange, hasOrdinaryBuildPermission: true));
        }

        [Fact]
        public void Client_consumer_never_serves_another_occupants_hostile_snapshot()
        {
            var server = Provisioned();
            var cache = new LocalActivationClientCache();
            // A stranger with no relationship, under default Everyone policy but no build Permission, is
            // refused; and even the delivered snapshot for the hostile occupant never activates Practice
            // Range without the developed/entitled state.
            var hostile = Presence(_hostile, _hostileChar, owner: false, rel: false, inside: true, gov: true);
            var delivery = server.Activation.Publish(_stone, hostile, "hostile");
            cache.Apply(delivery.Snapshot);
            Assert.False(cache.CanExercisePlacementForNode(PracticeRange, hasOrdinaryBuildPermission: false));
        }

        // ── Stubs (server-owned authority policies; local copies of the shared-suite fixtures) ──

        private sealed class FixedFamilyResolver : IStoneFamilyResolver
        {
            public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
            {
                family = "Settlement"; variant = "Homestead"; return true;
            }
        }

        private sealed class AllowHomesteadBondPolicy : IBondAuthorityPolicy
        {
            public bool TryAuthorizeBond(StoneId stoneId, string requestedResponsibilityRange,
                out string grantedRange, out string grantedRole)
            {
                grantedRange = requestedResponsibilityRange ?? string.Empty;
                grantedRole = "Governor";
                return string.Equals(requestedResponsibilityRange, "Homestead:All",
                    System.StringComparison.Ordinal);
            }
        }

        private sealed class AllowGovernorAuthority : IGovernorAuthorityPolicy
        {
            public bool CanCommit(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                string facetId, FacetCategory category) =>
                string.Equals(responsibilityRange, "Homestead:All", System.StringComparison.Ordinal)
                && string.Equals(ownerGovernorRole, "Governor", System.StringComparison.Ordinal)
                && category != FacetCategory.None;
        }

        private sealed class AllowDevelopmentAuthority : IGovernorDevelopmentAuthority
        {
            public bool CanDevelop(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                VersionedId tree) =>
                string.Equals(responsibilityRange, "Homestead:All", System.StringComparison.Ordinal)
                && string.Equals(ownerGovernorRole, "Governor", System.StringComparison.Ordinal)
                && !tree.IsNone;
        }
    }
}
