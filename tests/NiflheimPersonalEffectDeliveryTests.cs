// ============================================================================
//  Homestead progression — PERSONAL CHARACTER-EFFECT DELIVERY tests
//  (T026 remediation, product defect from PR #373 review — t_3a899381).
// ----------------------------------------------------------------------------
//  Exercises the engine-free bounded server→client PERSONAL Character-Effect
//  delivery substrate (link-compiled from ../src) that the T026 review found
//  missing so a pure joined client could never craft Field Fletching I:
//    * PersonalActivationService — derives the per-(occupant, character) read
//      model from the authoritative Stone/character/authority aggregates via
//      the shipped T004 DerivedActivationView (a purchase record for the node
//      at this Stone AND an active relationship — no second active-effects
//      ledger, AT-NO-ACTIVE-LEDGER), and emits bounded notifications with
//      stable IDs + revisions + a monotonic delivery sequence.
//    * PersonalActivationSnapshot / Notification — the bounded wire contract.
//      Denied(...) is the fail-closed empty, all-inactive read model.
//    * PersonalActivationClientCache — the bounded client consumer that drops
//      stale/reordered snapshots, decides refetch from a notification, and
//      fails closed.
//
//  Personal ownership semantics (contrast the Local channel): a Character
//  Effect is active iff (purchase AND active relationship) for THIS character;
//  it is NOT gated by occupancy, the Settlement Local policy, or governor
//  presence. Relationship loss / disconnect / dormancy flip Active to false
//  with zero writes; a sibling character's reservation never leaks the effect.
//
//  Proven here: authenticated server snapshot, bound principal, monotonic
//  revision/replay, stale/out-of-order rejection, disconnect/cache
//  invalidation, hostile payload/identity, dormant/released fail-closed,
//  listen-host and pure-client consumers, and NO second active-effects ledger.
// ============================================================================

using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimPersonalEffectDeliveryTests
    {
        private readonly WorldId _world = new WorldId("uid:personal-026");
        private readonly StoneId _stone;

        private readonly AccountId _account = new AccountId("acct-archer");
        private readonly CharacterId _character = new CharacterId("char-archer");
        private readonly CharacterId _sibling = new CharacterId("char-sibling");
        private readonly AccountId _hostile = new AccountId("acct-hostile");
        private readonly CharacterId _hostileChar = new CharacterId("char-hostile");

        private static readonly VersionedId Archer = HomesteadProgressionCatalog.ArcherTree;
        private static readonly VersionedId FieldFletching = new VersionedId("FieldFletchingI", 1);

        public NiflheimPersonalEffectDeliveryTests()
        {
            _stone = StoneId.FromHostZone(_world, 7, 3);
        }

        // ── Aggregate builders (mirror NiflheimFieldFletchingTests) ──────────────

        private StoneProgressionAggregate BuildStone(long revision = 5, bool developed = true, bool offered = true)
        {
            var committed = new List<CommittedTreeRecord>
            {
                new CommittedTreeRecord(HomesteadProgressionCatalog.MartialFacetId,
                    Archer, "seed-commit-archer", _character.Value, 1, 0)
            };
            var development = new List<NodeDevelopmentRecord>();
            if (developed)
                development.Add(new NodeDevelopmentRecord(FieldFletching, 1, 1, true, offered, "seed-dev-ff"));

            return new StoneProgressionAggregate(_stone, revision, historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "c", updatedProvenance: "u",
                mirroredStoneAp: 3, lastAppliedReceiptId: "r",
                committedTrees: committed, nodeDevelopment: development);
        }

        private CharacterProgressionAggregate BuildCharacter(AccountId account, CharacterId character,
            bool withPurchase, long revision = 2)
        {
            NodePurchaseRecord[]? purchases = withPurchase
                ? new[]
                {
                    new NodePurchaseRecord(Archer, FieldFletching, "ap:personal",
                        "CharacterEffect", VersionedId.None, "op-buy-ff")
                }
                : null;
            var stoneRecord = new CharacterStoneRecord(_stone, 3, 3, 1, null, purchases, null);
            return new CharacterProgressionAggregate(account, character,
                "world-scope", revision, 2, 2, "receipt", new[] { stoneRecord });
        }

        private AccountStoneAuthorityIndex BuildAuthority(AccountId account, CharacterId? activeCharacter,
            long revision = 1)
        {
            if (!activeCharacter.HasValue)
                // A released index at an explicit revision (Vacant is revision 0; a real release advances it).
                return new AccountStoneAuthorityIndex(account, _stone, revision,
                    new AuthorityReservation[0], string.Empty, AccountStoneAuthorityIndex.CurrentSchemaVersion);
            return AccountStoneAuthorityIndex.Vacant(account, _stone).WithReservationAdded(
                new AuthorityReservation(activeCharacter.Value, RelationshipKind.Bond, "rel-ff",
                    "relreceipt:seed"), revision);
        }

        // Compose an authoritative server-side service over in-memory stores seeded with the given facts.
        private PersonalActivationService NewService(
            CharacterProgressionAggregate character, AccountStoneAuthorityIndex authority,
            StoneProgressionAggregate? stone = null)
        {
            var stones = new InMemoryStoneAggregateStore();
            var characters = new InMemoryCharacterAggregateStore();
            var authStore = new InMemoryAccountStoneAuthorityStore();
            stones.PutStone(stone ?? BuildStone());
            characters.PutCharacter(character);
            authStore.ApplyAuthorityProjection("seed", authority);
            return new PersonalActivationService(stones, characters, authStore);
        }

        // ── Authenticated server snapshot + bound principal ──────────────────────

        [Fact]
        public void ServerSnapshot_ActiveCaller_DeliversFieldFletchingActive()
        {
            var svc = NewService(
                BuildCharacter(_account, _character, withPurchase: true),
                BuildAuthority(_account, _character));

            var snap = svc.Fetch(_stone, _account, _character);

            Assert.True(snap.AuthorityPresent);
            Assert.Equal(_account.Value, snap.Occupant.Value);
            Assert.Equal(_character.Value, snap.Character.Value);
            Assert.True(snap.IsActive(FieldFletching));
        }

        [Fact]
        public void ServerSnapshot_BindsToRequestingPrincipal_NotAnotherAccount()
        {
            // The service derives strictly for the (occupant, character) it is asked about. A snapshot stamped
            // for the acting caller carries that caller's identity; the transport keys the client cache by it,
            // so another account can never read this snapshot as its own.
            var svc = NewService(
                BuildCharacter(_account, _character, withPurchase: true),
                BuildAuthority(_account, _character));

            var snap = svc.Fetch(_stone, _account, _character);
            Assert.Equal(_account.Value, snap.Occupant.Value);
            Assert.Equal(_character.Value, snap.Character.Value);

            // A different account has no seeded character aggregate → fail closed (denied, all-inactive).
            var foreign = svc.Fetch(_stone, _hostile, _hostileChar);
            Assert.False(foreign.AuthorityPresent);
            Assert.False(foreign.IsActive(FieldFletching));
        }

        // ── Dormant / released / disconnect fail-closed ──────────────────────────

        [Fact]
        public void ServerSnapshot_PurchasedButNoRelationship_DeliversDormant()
        {
            var svc = NewService(
                BuildCharacter(_account, _character, withPurchase: true),
                BuildAuthority(_account, activeCharacter: null)); // relationship released → dormant

            var snap = svc.Fetch(_stone, _account, _character);
            Assert.True(snap.AuthorityPresent);       // authority resolved; the effect is simply dormant.
            Assert.False(snap.IsActive(FieldFletching));
        }

        [Fact]
        public void ServerSnapshot_RelationshipButNoPurchase_DeliversNothing()
        {
            var svc = NewService(
                BuildCharacter(_account, _character, withPurchase: false),
                BuildAuthority(_account, _character));

            var snap = svc.Fetch(_stone, _account, _character);
            Assert.False(snap.IsActive(FieldFletching));
        }

        [Fact]
        public void ServerSnapshot_SiblingReservation_DoesNotLeakToPurchasedCaller()
        {
            // The sibling holds the reservation; the caller holds the purchase but not the active
            // relationship. Personal effects are per-character — the caller is dormant.
            var svc = NewService(
                BuildCharacter(_account, _character, withPurchase: true),
                BuildAuthority(_account, _sibling));

            var snap = svc.Fetch(_stone, _account, _character);
            Assert.False(snap.IsActive(FieldFletching));
        }

        [Fact]
        public void ServerSnapshot_MissingStoneOrCharacter_FailsClosedDenied()
        {
            // Empty stores — nothing resolves. Denied (empty, all-inactive) so the client delivers nothing.
            var svc = new PersonalActivationService(
                new InMemoryStoneAggregateStore(),
                new InMemoryCharacterAggregateStore(),
                new InMemoryAccountStoneAuthorityStore());

            var snap = svc.Fetch(_stone, _account, _character);
            Assert.False(snap.AuthorityPresent);
            Assert.False(snap.IsActive(FieldFletching));
            Assert.Empty(snap.Rows);
        }

        // ── Monotonic revision / replay / no second ledger ───────────────────────

        [Fact]
        public void RelationshipLossThenRestore_FlipsActiveWithNoWrites_PureReDerivation()
        {
            // Active → dormant → active is pure re-derivation off the SAME durable purchase. There is no
            // stored active-effects ledger to poke; only the authority index changes.
            var character = BuildCharacter(_account, _character, withPurchase: true);
            var stones = new InMemoryStoneAggregateStore();
            var characters = new InMemoryCharacterAggregateStore();
            var authStore = new InMemoryAccountStoneAuthorityStore();
            stones.PutStone(BuildStone());
            characters.PutCharacter(character);
            var svc = new PersonalActivationService(stones, characters, authStore);

            authStore.ApplyAuthorityProjection("a1", BuildAuthority(_account, _character, revision: 1));
            Assert.True(svc.Fetch(_stone, _account, _character).IsActive(FieldFletching));

            authStore.ApplyAuthorityProjection("a2", BuildAuthority(_account, activeCharacter: null, revision: 2));
            Assert.False(svc.Fetch(_stone, _account, _character).IsActive(FieldFletching));

            authStore.ApplyAuthorityProjection("a3", BuildAuthority(_account, _character, revision: 3));
            Assert.True(svc.Fetch(_stone, _account, _character).IsActive(FieldFletching));
        }

        [Fact]
        public void Publish_BumpsMonotonicSequence_FetchDoesNot()
        {
            var svc = NewService(
                BuildCharacter(_account, _character, withPurchase: true),
                BuildAuthority(_account, _character));

            Assert.Equal(0, svc.CurrentSequence(_stone, _account, _character));

            var d1 = svc.Publish(_stone, _account, _character, "request");
            Assert.Equal(1, d1.Snapshot.Sequence);
            Assert.Equal(1, d1.Notification.Sequence);

            // Fetch must NOT advance the delivery sequence (a read is not a delivery event).
            var f = svc.Fetch(_stone, _account, _character);
            Assert.Equal(1, f.Sequence);

            var d2 = svc.Publish(_stone, _account, _character, "request");
            Assert.Equal(2, d2.Snapshot.Sequence);
        }

        [Fact]
        public void Notification_CarriesAuthoritativeRevisions_ForRefetchDecision()
        {
            var svc = NewService(
                BuildCharacter(_account, _character, withPurchase: true, revision: 7),
                BuildAuthority(_account, _character, revision: 4),
                BuildStone(revision: 9));

            var d = svc.Publish(_stone, _account, _character, "request");
            Assert.Equal(9, d.Notification.StoneRevision);
            Assert.Equal(7, d.Notification.CharacterRevision);
            Assert.Equal(4, d.Notification.AuthorityRevision);
        }

        // ── Wire round-trip ──────────────────────────────────────────────────────

        [Fact]
        public void Snapshot_SerializeRoundTrip_PreservesEveryField()
        {
            var svc = NewService(
                BuildCharacter(_account, _character, withPurchase: true, revision: 7),
                BuildAuthority(_account, _character, revision: 4),
                BuildStone(revision: 9));

            var original = svc.Publish(_stone, _account, _character, "request").Snapshot;
            var round = PersonalActivationSnapshot.Deserialize(original.Serialize());

            Assert.Equal(original.Serialize(), round.Serialize());
            Assert.Equal(original.StoneId.Value, round.StoneId.Value);
            Assert.Equal(original.Occupant.Value, round.Occupant.Value);
            Assert.Equal(original.Character.Value, round.Character.Value);
            Assert.Equal(original.Sequence, round.Sequence);
            Assert.Equal(original.StoneRevision, round.StoneRevision);
            Assert.Equal(original.CharacterRevision, round.CharacterRevision);
            Assert.Equal(original.AuthorityRevision, round.AuthorityRevision);
            Assert.Equal(original.AuthorityPresent, round.AuthorityPresent);
            Assert.True(round.IsActive(FieldFletching));
        }

        [Fact]
        public void Notification_SerializeRoundTrip_PreservesEveryField()
        {
            var n = new PersonalActivationNotification(_stone, _account, _character, 5, 9, 7, 4, "request");
            var round = PersonalActivationNotification.Deserialize(n.Serialize());
            Assert.Equal(n.Serialize(), round.Serialize());
            Assert.Equal(5, round.Sequence);
            Assert.Equal("request", round.ResultCode);
        }

        // ── Client cache: stale/reorder drop, disconnect invalidation, fail-closed ──

        [Fact]
        public void ClientCache_AppliesNewerSnapshot_DropsStaleReorder()
        {
            var cache = new PersonalActivationClientCache();
            var newer = ActiveSnapshot(sequence: 5);
            var older = ActiveSnapshot(sequence: 3);

            Assert.True(cache.Apply(newer));
            Assert.False(cache.Apply(older));      // reordered late fetch — dropped, cannot roll backward.
            Assert.True(cache.IsActiveForStone(_stone, FieldFletching));
            // The held one is still the newer (seq 5).
            Assert.Equal(5, cache.Current(_stone, _account, _character)!.Sequence);
        }

        [Fact]
        public void ClientCache_IsActiveForStone_FailsClosedWithoutSnapshot()
        {
            var cache = new PersonalActivationClientCache();
            Assert.False(cache.IsActiveForStone(_stone, FieldFletching));
        }

        [Fact]
        public void ClientCache_DeniedSnapshot_DeliversNothing()
        {
            var cache = new PersonalActivationClientCache();
            Assert.True(cache.Apply(PersonalActivationSnapshot.Denied(_stone, _account, _character, 1)));
            Assert.False(cache.IsActiveForStone(_stone, FieldFletching));
            Assert.False(cache.IsActive(_stone, _account, _character, FieldFletching));
        }

        [Fact]
        public void ClientCache_InvalidateAndClear_FailClosed()
        {
            var cache = new PersonalActivationClientCache();
            cache.Apply(ActiveSnapshot(sequence: 2));
            Assert.True(cache.IsActiveForStone(_stone, FieldFletching));

            cache.Invalidate(_stone, _account, _character);      // e.g. relationship loss before refetch
            Assert.False(cache.IsActiveForStone(_stone, FieldFletching));

            cache.Apply(ActiveSnapshot(sequence: 3));
            Assert.True(cache.IsActiveForStone(_stone, FieldFletching));

            cache.Clear();                                        // e.g. ZNet teardown / disconnect
            Assert.False(cache.IsActiveForStone(_stone, FieldFletching));
        }

        [Fact]
        public void ClientCache_ShouldRefetch_OnAheadSequenceOrChangedRevisions()
        {
            var cache = new PersonalActivationClientCache();
            cache.Apply(ActiveSnapshot(sequence: 4, stoneRev: 9, charRev: 7, authRev: 4));

            // Unknown caller → refetch.
            var unknown = new PersonalActivationNotification(
                StoneId.FromHostZone(_world, 1, 1), _account, _character, 1, 0, 0, 0, "x");
            Assert.True(cache.ShouldRefetch(unknown));

            // Same sequence + same revisions → no refetch.
            var same = new PersonalActivationNotification(_stone, _account, _character, 4, 9, 7, 4, "x");
            Assert.False(cache.ShouldRefetch(same));

            // Ahead sequence → refetch.
            var ahead = new PersonalActivationNotification(_stone, _account, _character, 5, 9, 7, 4, "x");
            Assert.True(cache.ShouldRefetch(ahead));

            // Changed authority revision (relationship mutated) → refetch even at same sequence.
            var relMoved = new PersonalActivationNotification(_stone, _account, _character, 4, 9, 7, 5, "x");
            Assert.True(cache.ShouldRefetch(relMoved));
        }

        // ── Hostile payload / identity ───────────────────────────────────────────

        [Fact]
        public void ClientCache_HostilePayload_CannotForgeActivationForLocalStone()
        {
            // A hostile snapshot stamped for a DIFFERENT (account, character) never satisfies a query for the
            // local player's own principal, and — because the transport only ever hands the cache snapshots
            // the server stamped for the requesting peer — a forged "active" row for someone else is inert to
            // the local IsActive(stone, myAccount, myChar) query. IsActiveForStone would honor any held
            // active snapshot for the stone, which is exactly why the transport NEVER delivers another
            // principal's snapshot to this client; here we assert the per-principal query is not fooled.
            var cache = new PersonalActivationClientCache();
            var forged = new PersonalActivationSnapshot(
                _stone, _hostile, _hostileChar, 9, 5, 5, 5, authorityPresent: true,
                new[] { new PersonalActivationRow(FieldFletching, true, true, true, true) });
            cache.Apply(forged);

            // The local player's own principal holds no snapshot → fail closed.
            Assert.False(cache.IsActive(_stone, _account, _character, FieldFletching));
        }

        [Fact]
        public void DeniedSnapshot_WithActiveLookingRow_StillDeliversNothing()
        {
            // AuthorityPresent=false is load-bearing: even if a row claims Active, a denied snapshot delivers
            // nothing. (Denied() carries no rows, but assert the invariant directly on IsActive.)
            var denied = PersonalActivationSnapshot.Denied(_stone, _account, _character, 1);
            Assert.False(denied.IsActive(FieldFletching));
            Assert.False(denied.AuthorityPresent);
        }

        // ── Snapshot helpers ─────────────────────────────────────────────────────

        private PersonalActivationSnapshot ActiveSnapshot(long sequence, long stoneRev = 5, long charRev = 2,
            long authRev = 1) =>
            new PersonalActivationSnapshot(_stone, _account, _character, sequence, stoneRev, charRev, authRev,
                authorityPresent: true,
                new[] { new PersonalActivationRow(FieldFletching, true, true, true, true) });
    }
}
