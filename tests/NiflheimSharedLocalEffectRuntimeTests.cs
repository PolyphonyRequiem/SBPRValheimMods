// ============================================================================
//  Homestead progression — SHARED LOCAL EFFECT RUNTIME tests (T016 substrate,
//  product-defect remediation t_02c13405).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free shared runtime substrate (link-compiled
//  from ../src) that the T021 investigation proved was missing:
//    * LocalProgressionServer — the composition root wiring the ACCEPTED
//      Facet/Activity/Development/LocalPolicy command handlers over the SAME
//      Stone/character/authority stores plus the LocalActivationService. No
//      parallel provisional ledger; every mutation crosses the real receipt-
//      backed handlers onto durable journals.
//    * LocalNodeProvisioningDriver — the isolated-QA path that reaches a
//      developed Local node using ONLY accepted commands (commit Tree -> credit
//      BP -> develop node), so QA can prove joined-client entry/exit without a
//      hardcoded grant.
//    * LocalActivationService — derives the per-occupant read model from the
//      authoritative Stone aggregate + observed presence via the existing
//      LocalEffectActivationView, and emits bounded notifications with stable
//      IDs + revisions + a monotonic delivery sequence.
//    * LocalActivationClientCache — the bounded client consumer that drops
//      stale/reordered snapshots, decides refetch from a notification, and
//      fails closed.
//
//  Proven here (task scope): area entry/exit, relationship/governance/policy
//  dormancy, stale/reordered notification -> refetch, reconnect/restart,
//  hostile identity, and NO second mutable active-effects ledger (derive only).
// ============================================================================

using System.Collections.Generic;
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
    public sealed class NiflheimSharedLocalEffectRuntimeTests : System.IDisposable
    {
        private readonly string _dir;
        private readonly WorldId _world = new WorldId("uid:shared-le-42");
        private readonly StoneId _stone;

        private readonly AccountId _gov = new AccountId("acct-gov");
        private readonly CharacterId _govChar = new CharacterId("char-gov");
        private readonly AccountId _guest = new AccountId("acct-guest");
        private readonly CharacterId _guestChar = new CharacterId("char-guest");
        private readonly AccountId _hostile = new AccountId("acct-hostile");
        private readonly CharacterId _hostileChar = new CharacterId("char-hostile");

        private static readonly VersionedId Savor = new VersionedId("SavorTheHearth", 1); // Cooking Local L1
        private static readonly VersionedId Cooking = HomesteadProgressionCatalog.CookingTree;

        public NiflheimSharedLocalEffectRuntimeTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "niflheim-t016-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _stone = StoneId.FromHostZone(_world, 3, 9);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        }

        // ── Composition helpers ──────────────────────────────────────────────

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
                new StubOwnerAuthority(_gov, _govChar, _stone));
        }

        // A Stone-Level-2 Homestead with the Cooking Tree NOT yet committed and NO node development — the
        // provisioning driver must reach Developed purely through accepted commands.
        private StoneProgressionAggregate BareStone(long revision) =>
            new StoneProgressionAggregate(_stone, revision, historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "created", updatedProvenance: "seed",
                mirroredStoneAp: 0, lastAppliedReceiptId: "r-seed",
                committedTrees: null, nodeDevelopment: null);

        private CharacterProgressionAggregate Governor() =>
            new CharacterProgressionAggregate(_gov, _govChar, "shared-le/trailborne",
                revision: 3, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[]
                {
                    new CharacterStoneRecord(_stone, 0, 0, 0, facetCredits: null, purchases: null,
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

        private (LocalProgressionServer server, InMemoryStoneAggregateStore stones) Provisioned()
        {
            var stones = new InMemoryStoneAggregateStore();
            var characters = new InMemoryCharacterAggregateStore();
            var authority = new InMemoryAccountStoneAuthorityStore();
            stones.PutStone(BareStone(revision: 10));
            characters.PutCharacter(Governor());
            authority.ApplyAuthorityProjection("seed-bond", BondIndex());

            var server = NewServer(stones, characters, authority);
            var driver = new LocalNodeProvisioningDriver(server);
            var result = driver.Provision(new AuthoritativeSubject(_gov, _govChar), _stone, Savor, "qa-savor");
            Assert.True(result.IsDeveloped, "QA provisioning must develop the node through accepted commands: "
                + result.FailedStep + "/" + result.ResultCode);
            return (server, stones);
        }

        private OccupantPresence Presence(AccountId occ, CharacterId ch, bool owner, bool rel, bool inside,
            bool gov) => new OccupantPresence(occ, ch, owner, rel, inside, gov);

        // ── Provisioning path: accepted commands only ────────────────────────

        [Fact]
        public void Provisioning_reaches_developed_local_node_via_accepted_commands_only()
        {
            var (server, stones) = Provisioned();
            var stone = stones.GetStone(_stone)!;

            // The node is Developed as Stone-owned state, and the Cooking Tree is committed — both via the
            // real handlers, not a direct projection poke.
            bool developed = false, committed = false;
            foreach (var d in stone.NodeDevelopment)
                if (d.Node.Key == Savor.Key && d.Developed) developed = true;
            foreach (var c in stone.CommittedTrees)
                if (c.Tree.Key == Cooking.Key) committed = true;
            Assert.True(developed);
            Assert.True(committed);
        }

        [Fact]
        public void Provisioning_is_idempotent_on_replay()
        {
            var (server, stones) = Provisioned();
            long rev = stones.GetStone(_stone)!.Revision;

            // Re-run the SAME provisioning: every accepted command replays, no double development, revision
            // unchanged beyond the first completion.
            var driver = new LocalNodeProvisioningDriver(server);
            var again = driver.Provision(new AuthoritativeSubject(_gov, _govChar), _stone, Savor, "qa-savor");
            Assert.True(again.IsDeveloped);
            Assert.Equal(rev, stones.GetStone(_stone)!.Revision);
        }

        [Fact]
        public void Provisioning_fails_closed_without_bond_authority()
        {
            var stones = new InMemoryStoneAggregateStore();
            var characters = new InMemoryCharacterAggregateStore();
            var authority = new InMemoryAccountStoneAuthorityStore();
            stones.PutStone(BareStone(revision: 10));
            // No Governor character, no bond — the accepted commands must reject; no node becomes developed.
            var server = NewServer(stones, characters, authority);
            var driver = new LocalNodeProvisioningDriver(server);
            var result = driver.Provision(new AuthoritativeSubject(_gov, _govChar), _stone, Savor, "qa-savor");
            Assert.False(result.IsDeveloped);
            foreach (var d in stones.GetStone(_stone)!.NodeDevelopment)
                Assert.False(d.Node.Key == Savor.Key && d.Developed);
        }

        // ── Activation derivation + area entry/exit ──────────────────────────

        [Fact]
        public void Occupant_inside_area_with_policy_and_governor_gets_active_effect()
        {
            var (server, _) = Provisioned();
            var snap = server.Activation.Fetch(_stone,
                Presence(_gov, _govChar, owner: true, rel: true, inside: true, gov: true));
            Assert.True(snap.AuthorityPresent);
            Assert.True(snap.IsActive(Savor));
        }

        [Fact]
        public void Area_exit_deactivates_effect_by_rederivation_no_ledger_write()
        {
            var (server, stones) = Provisioned();
            long revBefore = stones.GetStone(_stone)!.Revision;

            var inside = server.Activation.Fetch(_stone,
                Presence(_gov, _govChar, true, true, inside: true, gov: true));
            Assert.True(inside.IsActive(Savor));

            var outside = server.Activation.Fetch(_stone,
                Presence(_gov, _govChar, true, true, inside: false, gov: true));
            Assert.False(outside.IsActive(Savor));

            // Re-derivation only: the persisted Stone revision did not change when the effect deactivated.
            Assert.Equal(revBefore, stones.GetStone(_stone)!.Revision);
        }

        [Fact]
        public void Missing_governor_dormants_effect()
        {
            var (server, _) = Provisioned();
            var snap = server.Activation.Fetch(_stone,
                Presence(_gov, _govChar, true, true, inside: true, gov: false));
            Assert.False(snap.IsActive(Savor));
            Assert.False(snap.AuthorizedGovernorPresent);
        }

        [Fact]
        public void Private_policy_excludes_non_beneficiary_but_keeps_owner_active()
        {
            var (server, _) = Provisioned();
            // Owner sets Private policy with an empty allowlist through the accepted owner-only handler.
            var driver = new LocalNodeProvisioningDriver(server);
            var code = driver.SetPolicy(new AuthoritativeSubject(_gov, _govChar), _stone,
                LocalBeneficiaryMode.Private, new List<string>(), "qa-policy-private");
            Assert.Equal("Applied", code);

            var owner = server.Activation.Fetch(_stone,
                Presence(_gov, _govChar, owner: true, rel: true, inside: true, gov: true));
            Assert.True(owner.IsActive(Savor));

            var guest = server.Activation.Fetch(_stone,
                Presence(_guest, _guestChar, owner: false, rel: false, inside: true, gov: true));
            Assert.False(guest.IsActive(Savor));
            Assert.False(guest.OccupantPolicyEligible);
        }

        // ── Bounded notification: stale/reordered -> refetch ─────────────────

        [Fact]
        public void Client_drops_stale_reordered_snapshot_and_refetches_current()
        {
            var (server, _) = Provisioned();
            var cache = new LocalActivationClientCache();
            var presence = Presence(_gov, _govChar, true, true, inside: true, gov: true);

            var d1 = server.Activation.Publish(_stone, presence, "seed");     // seq 1, active
            var d2 = server.Activation.Publish(_stone, presence, "update");   // seq 2, active

            // Apply the NEWER snapshot first, then a stale reorder of the older one arrives.
            Assert.True(cache.Apply(d2.Snapshot));
            Assert.False(cache.Apply(d1.Snapshot)); // dropped as stale
            Assert.Equal(2, cache.Current(_stone, _gov)!.Sequence);

            // A notification with a sequence ahead of what we hold triggers a refetch decision.
            var d3 = server.Activation.Publish(_stone, presence, "again"); // seq 3
            Assert.True(cache.ShouldRefetch(d3.Notification));
            // The refetch fetches current truth (no sequence bump) and applies it.
            var refetched = server.Activation.Fetch(_stone, presence);
            Assert.True(cache.Apply(refetched));
            Assert.True(cache.IsActive(_stone, _gov, Savor));
        }

        [Fact]
        public void Notification_for_unknown_stone_forces_refetch()
        {
            var (server, _) = Provisioned();
            var cache = new LocalActivationClientCache();
            var presence = Presence(_gov, _govChar, true, true, inside: true, gov: true);
            var d = server.Activation.Publish(_stone, presence, "seed");
            Assert.True(cache.ShouldRefetch(d.Notification)); // nothing held yet
        }

        // ── Reconnect / restart ──────────────────────────────────────────────

        [Fact]
        public void Restart_rebuilds_identical_activation_from_durable_journals()
        {
            var (server1, stones1) = Provisioned();
            var before = server1.Activation.Fetch(_stone,
                Presence(_gov, _govChar, true, true, inside: true, gov: true));
            Assert.True(before.IsActive(Savor));

            // Simulate a full server restart: fresh stores + fresh handlers over the SAME durable directory.
            var stones2 = new InMemoryStoneAggregateStore();
            var characters2 = new InMemoryCharacterAggregateStore();
            var authority2 = new InMemoryAccountStoneAuthorityStore();
            // The Stone aggregate is persisted alongside its ZDO in production; the durable Facet/Development
            // journals rehydrate its progression. Seed only the pre-progression envelope, then reconstruct.
            stones2.PutStone(BareStone(revision: 10));
            characters2.PutCharacter(Governor());
            authority2.ApplyAuthorityProjection("seed-bond", BondIndex());
            var server2 = NewServer(stones2, characters2, authority2);

            var after = server2.Activation.Fetch(_stone,
                Presence(_gov, _govChar, true, true, inside: true, gov: true));
            Assert.True(after.IsActive(Savor));
            // Identical derived delivery: same developed Local node active for the same occupant.
            Assert.Equal(before.RowFor(Savor).Active, after.RowFor(Savor).Active);
            Assert.Equal(before.RowFor(Savor).Developed, after.RowFor(Savor).Developed);
        }

        // ── Hostile identity ─────────────────────────────────────────────────

        [Fact]
        public void Hostile_occupant_cannot_activate_or_provision()
        {
            var (server, stones) = Provisioned();

            // A stranger inside the area under default Everyone policy is policy-eligible but never gets
            // placement without ordinary build Permission.
            var snap = server.Activation.Fetch(_stone,
                Presence(_hostile, _hostileChar, owner: false, rel: false, inside: true, gov: true));
            Assert.False(snap.CanExercisePlacement(Savor, hasOrdinaryBuildPermission: false));

            // A hostile actor cannot drive the provisioning path (no bond/authority) — the accepted
            // commands reject and no state changes.
            long rev = stones.GetStone(_stone)!.Revision;
            var driver = new LocalNodeProvisioningDriver(server);
            var attempt = driver.Provision(new AuthoritativeSubject(_hostile, _hostileChar), _stone,
                new VersionedId("TwigTraining", 1), "qa-hostile");
            Assert.False(attempt.IsDeveloped);
            Assert.Equal(rev, stones.GetStone(_stone)!.Revision);
        }

        // ── No second mutable active-effects ledger ──────────────────────────

        [Fact]
        public void Activation_service_holds_no_active_effects_ledger()
        {
            var (server, stones) = Provisioned();
            var presence = Presence(_gov, _govChar, true, true, inside: true, gov: true);

            // Publishing many times bumps only the delivery SEQUENCE (delivery metadata), never the Stone
            // revision — activation is derived, not stored.
            long rev = stones.GetStone(_stone)!.Revision;
            for (int i = 0; i < 5; i++) server.Activation.Publish(_stone, presence, "x");
            Assert.Equal(rev, stones.GetStone(_stone)!.Revision);

            // Flipping presence flips Active with zero writes.
            var active = server.Activation.Fetch(_stone, presence);
            var dormant = server.Activation.Fetch(_stone,
                Presence(_gov, _govChar, true, true, inside: true, gov: false));
            Assert.True(active.IsActive(Savor));
            Assert.False(dormant.IsActive(Savor));
            Assert.Equal(rev, stones.GetStone(_stone)!.Revision);
        }

        [Fact]
        public void Denied_snapshot_fails_closed_for_unknown_stone()
        {
            var stones = new InMemoryStoneAggregateStore();
            var svc = new LocalActivationService(stones);
            var snap = svc.Fetch(_stone, Presence(_gov, _govChar, true, true, true, true));
            Assert.False(snap.AuthorityPresent);
            Assert.False(snap.IsActive(Savor));
            Assert.Empty(snap.Rows);
        }

        [Fact]
        public void Snapshot_and_notification_roundtrip_on_the_wire()
        {
            var (server, _) = Provisioned();
            var d = server.Activation.Publish(_stone,
                Presence(_gov, _govChar, true, true, inside: true, gov: true), "seed");

            var s2 = LocalActivationSnapshot.Deserialize(d.Snapshot.Serialize());
            Assert.Equal(d.Snapshot.Serialize(), s2.Serialize());
            Assert.Equal(d.Snapshot.IsActive(Savor), s2.IsActive(Savor));

            var n2 = LocalActivationNotification.Deserialize(d.Notification.Serialize());
            Assert.Equal(d.Notification.Sequence, n2.Sequence);
            Assert.Equal(d.Notification.StoneRevision, n2.StoneRevision);
        }

        // ── Stubs (server-owned authority policies) ──────────────────────────

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

        private sealed class StubOwnerAuthority : IHomesteadOwnerAuthority
        {
            private readonly AccountId _owner;
            private readonly CharacterId _ownerChar;
            private readonly StoneId _stone;
            public StubOwnerAuthority(AccountId owner, CharacterId ownerChar, StoneId stone)
            { _owner = owner; _ownerChar = ownerChar; _stone = stone; }
            public bool IsOwner(AuthoritativePrincipal principal, StoneId stoneId) =>
                stoneId.Equals(_stone) && principal.Account.Equals(_owner)
                && principal.Character.Equals(_ownerChar);
        }
    }
}
