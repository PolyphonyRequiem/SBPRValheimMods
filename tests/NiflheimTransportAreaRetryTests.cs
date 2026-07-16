// ============================================================================
//  Homestead progression — T009R4 transport/area/retry security tests.
// ----------------------------------------------------------------------------
//  Covers the five live blockers the T009R3 adversarial review found, all
//  against the SHIPPED engine-free types (link-compiled from ../src):
//
//    Blocker 1 — Stone Area lifecycle: StoneAreaRegistrar reconciles the
//                server-owned membership from resident Stone facts (add / move /
//                remove / idempotent), so placements inside resolve and
//                outside/unknown remain rejected. No test-only prepopulation.
//    Blocker 2 — transport spoof: a forged sender identity cannot redirect
//                authority; creator binds to the CHARACTER subject (s_playerID),
//                the account is a distinct platform subject.
//    Blocker 3 — stable reconnect + operation-id binding: the deterministic
//                provisioning op id binds every material field; exact retries
//                replay, changed bindings differ.
//    Blocker 4 — normalized admin gate: VanillaAdminIdentity.ListContainsId
//                mirrors ZNet.ListContainsId (platform-qualified OR bare user).
//    Blocker 5 — pending revalidation queue: success after delayed ZDO,
//                timeout writes no credit, duplicate converges, spam bound,
//                restart never scans.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Activities;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimTransportAreaRetryTests : IDisposable
    {
        private readonly string _durableDir;
        private readonly WorldId _world = new WorldId("uid:t009r4");
        private readonly StoneId _stone;
        private readonly StoneId _stoneB;
        private readonly AccountId _account = new AccountId("acct:steam-abc");
        private readonly CharacterId _character = new CharacterId("player:424242");
        private const long PlayerId = 424242L;
        private const double StoneX = 100.0;
        private const double StoneZ = 100.0;

        public NiflheimTransportAreaRetryTests()
        {
            _durableDir = Path.Combine(Path.GetTempPath(), "niflheim-t009r4-" + Guid.NewGuid().ToString("N"));
            _stone = StoneId.FromHostZone(_world, 7, 3);
            _stoneB = StoneId.FromHostZone(_world, 40, 40);
        }

        public void Dispose()
        {
            if (Directory.Exists(_durableDir)) Directory.Delete(_durableDir, recursive: true);
        }

        // ══ Blocker 1 — Stone Area lifecycle ═════════════════════════════════════

        [Fact]
        public void AreaRegistrar_RegistersResidentStones_InsideResolvesOutsideRejected()
        {
            var membership = new StoneAreaMembership();
            var facts = new[]
            {
                new StoneAreaRegistrar.StoneAreaFact(_stone, StoneX, StoneZ, 20.0),
                new StoneAreaRegistrar.StoneAreaFact(_stoneB, 500.0, 500.0, 20.0),
            };

            var result = StoneAreaRegistrar.Reconcile(membership, facts);

            Assert.Equal(2, result.Registered);
            Assert.Equal(2, result.Total);
            Assert.True(membership.TryResolve(StoneX + 5, StoneZ + 5, out var inside));
            Assert.Equal(_stone.Value, inside.Value);
            Assert.False(membership.TryResolve(-9999, -9999, out _));   // outside every Area
        }

        [Fact]
        public void AreaRegistrar_Idempotent_SameFactsNoChange()
        {
            var membership = new StoneAreaMembership();
            var facts = new[] { new StoneAreaRegistrar.StoneAreaFact(_stone, StoneX, StoneZ, 20.0) };

            StoneAreaRegistrar.Reconcile(membership, facts);
            var second = StoneAreaRegistrar.Reconcile(membership, facts);

            Assert.Equal(0, second.Registered);
            Assert.Equal(0, second.Updated);
            Assert.Equal(0, second.Unregistered);
            Assert.Equal(1, second.Total);
        }

        [Fact]
        public void AreaRegistrar_MovedStone_UpdatesCenter()
        {
            var membership = new StoneAreaMembership();
            StoneAreaRegistrar.Reconcile(membership,
                new[] { new StoneAreaRegistrar.StoneAreaFact(_stone, StoneX, StoneZ, 20.0) });

            var moved = StoneAreaRegistrar.Reconcile(membership,
                new[] { new StoneAreaRegistrar.StoneAreaFact(_stone, StoneX + 200, StoneZ + 200, 20.0) });

            Assert.Equal(1, moved.Updated);
            Assert.False(membership.TryResolve(StoneX, StoneZ, out _));           // old center no longer inside
            Assert.True(membership.TryResolve(StoneX + 200, StoneZ + 200, out _)); // new center inside
        }

        [Fact]
        public void AreaRegistrar_RemovedStone_Unregisters()
        {
            var membership = new StoneAreaMembership();
            StoneAreaRegistrar.Reconcile(membership, new[]
            {
                new StoneAreaRegistrar.StoneAreaFact(_stone, StoneX, StoneZ, 20.0),
                new StoneAreaRegistrar.StoneAreaFact(_stoneB, 500.0, 500.0, 20.0),
            });

            // _stoneB is no longer resident (its ZDO was reaped).
            var after = StoneAreaRegistrar.Reconcile(membership,
                new[] { new StoneAreaRegistrar.StoneAreaFact(_stone, StoneX, StoneZ, 20.0) });

            Assert.Equal(1, after.Unregistered);
            Assert.Equal(1, after.Total);
            Assert.False(membership.TryResolve(500.0, 500.0, out _));
        }

        [Fact]
        public void AreaRegistrar_ThenPlacementInside_EarnsReceipt_NoTestPrepopulation()
        {
            // The membership starts EMPTY (production reality). Only the registrar populates it — from facts
            // the net48 layer would read off resident Stone ZDOs. After reconcile, a placement inside credits.
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore, registerArea: false);
            Assert.Equal(0, server.StoneAreas.Count);   // no test-only prepopulation

            StoneAreaRegistrar.Reconcile(server.StoneAreas,
                new[] { new StoneAreaRegistrar.StoneAreaFact(_stone, StoneX, StoneZ, 20.0) });
            SeedAndAttune(server);

            var obs = new FoundationalPlacementObservation(
                _stone, _account.Value, _character.Value, "foundation_wood_floor", "1:1",
                insideStoneArea: server.StoneAreas.TryResolve(StoneX, StoneZ, out _),
                placementSucceeded: true, foundationalCatalogVersion: "v1");
            Assert.Equal(RuntimePlacementDisposition.Earned, server.Runtime.Observe(obs).Disposition);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(_stone));
        }

        // ══ Blocker 2 — transport spoof cannot redirect authority ════════════════

        [Fact]
        public void Spoof_ForgedSenderCharacter_CannotClaimAnothersPiece()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            SeedAndAttune(server);

            // A piece genuinely created by the victim character (player:424242).
            var source = new FakeInstanceSource();
            source.Put("1:2", "wood_floor", _character.Value, StoneX, StoneZ);

            // An attacker whose transport-authenticated character subject is player:999 tries to claim it by
            // passing the victim's ACCOUNT string. The ingress binds creator == the SENDER's character
            // subject, so the attacker's own character (999) is what's compared — mismatch, no credit.
            var attackerCharacter = "player:999";
            var outcome = server.CreateDedicatedIngress(source)
                .Ingest("acct:attacker", attackerCharacter, "1:2");

            Assert.False(outcome.Routed);
            Assert.Equal(DedicatedIngressRejection.CreatorMismatch, outcome.Rejection);
            Assert.Equal(0, stoneStore.GetMirroredStoneAp(_stone));
        }

        [Fact]
        public void Spoof_AccountIsNeverComparedToCreator_OnlyCharacterIs()
        {
            // Even if an attacker supplies the VICTIM's account as their own, credit binds to the character
            // subject (the ZDO creator), which the attacker cannot forge server-side. A sender whose
            // character subject != the ZDO creator is rejected regardless of the account passed.
            var server = NewServer();
            SeedAndAttune(server);
            var source = new FakeInstanceSource();
            source.Put("1:3", "wood_floor", _character.Value, StoneX, StoneZ);   // creator = victim character

            var outcome = server.CreateDedicatedIngress(source)
                .Ingest(_account.Value /* victim account */, "player:hacker", "1:3");

            Assert.Equal(DedicatedIngressRejection.CreatorMismatch, outcome.Rejection);
        }

        [Fact]
        public void Binder_TransportFacts_AccountFromPlatform_CharacterFromPlayerId()
        {
            // The binder splits the two transport-derived facts correctly: account = platform subject,
            // character = player:<s_playerID>. They are distinct spaces and neither is the other.
            var facts = new AuthenticatedSenderCharacter(PlayerId, "acct:steam-abc");
            Assert.True(AuthenticatedSenderBinder.TryBind(facts, out string account, out string character));
            Assert.Equal("acct:steam-abc", account);
            Assert.Equal("player:424242", character);
            Assert.NotEqual(account, character);
        }

        // ══ Blocker 3 — stable reconnect + operation-id binding ══════════════════

        [Fact]
        public void OpBinding_ExactSameFields_SameId_ChangedFields_DifferentId()
        {
            string a = ProvisioningOperationBinding.OperationId(
                _account.Value, _character.Value, _stone, RelationshipCommandType.CreateAttunement, "", "world/1");
            string aRetry = ProvisioningOperationBinding.OperationId(
                _account.Value, _character.Value, _stone, RelationshipCommandType.CreateAttunement, "", "world/1");
            Assert.Equal(a, aRetry);   // exact retry replays

            // Each changed material field yields a DISTINCT op id (intentional conflict, not silent dup).
            Assert.NotEqual(a, ProvisioningOperationBinding.OperationId(
                _account.Value, _character.Value, _stoneB, RelationshipCommandType.CreateAttunement, "", "world/1"));
            Assert.NotEqual(a, ProvisioningOperationBinding.OperationId(
                _account.Value, _character.Value, _stone, RelationshipCommandType.CreateBond, "Homestead:All", "world/1"));
            Assert.NotEqual(a, ProvisioningOperationBinding.OperationId(
                _account.Value, "player:other", _stone, RelationshipCommandType.CreateAttunement, "", "world/1"));
            Assert.NotEqual(a, ProvisioningOperationBinding.OperationId(
                _account.Value, _character.Value, _stone, RelationshipCommandType.CreateAttunement, "", "world/2"));
        }

        [Fact]
        public void Reconnect_StableCharacterSubject_PreservesAuthorization()
        {
            // Establish an Attunement, then simulate reconnect: the live character ZDOID would change but the
            // stable character subject (player:<s_playerID>) does not, so the SAME relationship authorizes a
            // post-reconnect placement — authorization is not orphaned.
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            server.CreateRelationshipProvisioningIngress().Provision(
                new AuthoritativeSubject(_account, _character), _stone,
                RelationshipCommandType.CreateAttunement,
                ProvisioningOperationBinding.OperationId(_account.Value, _character.Value, _stone, RelationshipCommandType.CreateAttunement, "", "world/1"),
                ProvisioningOperationBinding.RelationshipId(_character.Value, RelationshipCommandType.CreateAttunement),
                "t009r4/trailborne");

            // "Reconnect": a fresh observation from the SAME stable character subject still credits.
            var post = server.Runtime.Observe(new FoundationalPlacementObservation(
                _stone, _account.Value, _character.Value, "foundation_wood_floor", "1:30",
                insideStoneArea: true, placementSucceeded: true, foundationalCatalogVersion: "v1"));
            Assert.Equal(RuntimePlacementDisposition.Earned, post.Disposition);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(_stone));
        }

        // ══ Blocker 4 — normalized admin gate ════════════════════════════════════

        [Fact]
        public void Admin_PlatformQualifiedCandidate_MatchesBareListEntry()
        {
            var list = new List<string> { "76561198000000000" };   // bare user id on adminlist.txt
            Assert.True(VanillaAdminIdentity.ListContainsId(list, "Steam:76561198000000000", "Steam"));
        }

        [Fact]
        public void Admin_BareCandidate_MatchesPlatformQualifiedListEntry()
        {
            var list = new List<string> { "Steam:76561198000000000" };
            Assert.True(VanillaAdminIdentity.ListContainsId(list, "76561198000000000", "Steam"));
        }

        [Fact]
        public void Admin_NonAdmin_DoesNotMatch()
        {
            var list = new List<string> { "Steam:76561198000000000" };
            Assert.False(VanillaAdminIdentity.ListContainsId(list, "76561198999999999", "Steam"));
            Assert.False(VanillaAdminIdentity.ListContainsId(list, "", "Steam"));
            Assert.False(VanillaAdminIdentity.ListContainsId(new List<string>(), "76561198000000000", "Steam"));
        }

        [Fact]
        public void Admin_CrossPlatformCandidate_OnlyMatchesFullForm()
        {
            var list = new List<string> { "76561198000000000" };
            // A candidate on a DIFFERENT platform than the server never matches a bare (server-platform) entry.
            Assert.False(VanillaAdminIdentity.ListContainsId(list, "Xbox:76561198000000000", "Steam"));
            Assert.True(VanillaAdminIdentity.ListContainsId(
                new List<string> { "Xbox:abc" }, "Xbox:abc", "Steam"));
        }

        // ══ Blocker 5 — pending revalidation queue ═══════════════════════════════

        private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

        [Fact]
        public void Pending_SuccessAfterDelayedZdo_CreditsOnce()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            SeedAndAttune(server);
            var source = new FakeInstanceSource();   // ZDO NOT present yet (still replicating)
            var ingress = server.CreateDedicatedIngress(source);
            var queue = new PendingRevalidationQueue(Deadline);

            long t0 = 0;
            Assert.Equal(PendingRevalidationQueue.EnqueueResult.Enqueued,
                queue.Enqueue(_account.Value, _character.Value, "1:40", t0));

            // First pump: ZDO still absent, within deadline → kept, no credit.
            var r1 = queue.Pump(t0 + 1, (a, c, k) => ingress.Ingest(a, c, k));
            Assert.Empty(r1);
            Assert.Equal(1, queue.Count);
            Assert.Equal(0, stoneStore.GetMirroredStoneAp(_stone));

            // ZDO replicates; next pump revalidates and credits, then removes the entry.
            source.Put("1:40", "wood_floor", _character.Value, StoneX, StoneZ);
            var r2 = queue.Pump(t0 + 2, (a, c, k) => ingress.Ingest(a, c, k));
            Assert.Single(r2);
            Assert.Equal(RuntimePlacementDisposition.Earned, r2[0].Runtime.Disposition);
            Assert.Equal(0, queue.Count);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(_stone));
        }

        [Fact]
        public void Pending_Timeout_WritesNoCredit()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            SeedAndAttune(server);
            var ingress = server.CreateDedicatedIngress(new FakeInstanceSource());   // ZDO never appears
            var queue = new PendingRevalidationQueue(Deadline);

            queue.Enqueue(_account.Value, _character.Value, "1:41", 0);

            // Pump past the deadline with the ZDO still absent → dropped, no credit, no lingering entry.
            var resolved = queue.Pump(Deadline.Ticks + 1, (a, c, k) => ingress.Ingest(a, c, k));
            Assert.Empty(resolved);
            Assert.Equal(0, queue.Count);
            Assert.Equal(0, stoneStore.GetMirroredStoneAp(_stone));
        }

        [Fact]
        public void Pending_DuplicateNotice_ConvergesOnOneEntry_OneCredit()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            SeedAndAttune(server);
            var source = new FakeInstanceSource();
            source.Put("1:42", "wood_floor", _character.Value, StoneX, StoneZ);
            var ingress = server.CreateDedicatedIngress(source);
            var queue = new PendingRevalidationQueue(Deadline);

            Assert.Equal(PendingRevalidationQueue.EnqueueResult.Enqueued,
                queue.Enqueue(_account.Value, _character.Value, "1:42", 0));
            // Duplicate / replayed notice for the same (character, instance) converges — no second entry.
            Assert.Equal(PendingRevalidationQueue.EnqueueResult.Converged,
                queue.Enqueue(_account.Value, _character.Value, "1:42", 0));
            Assert.Equal(1, queue.Count);

            var resolved = queue.Pump(1, (a, c, k) => ingress.Ingest(a, c, k));
            Assert.Single(resolved);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(_stone));
        }

        [Fact]
        public void Pending_SpamBound_RefusesNewKeysWhenFull()
        {
            var queue = new PendingRevalidationQueue(Deadline, capacity: 2);
            Assert.Equal(PendingRevalidationQueue.EnqueueResult.Enqueued,
                queue.Enqueue(_account.Value, _character.Value, "k1", 0));
            Assert.Equal(PendingRevalidationQueue.EnqueueResult.Enqueued,
                queue.Enqueue(_account.Value, _character.Value, "k2", 0));
            // Third distinct key is refused — an attacker cannot exhaust memory by flooding notices.
            Assert.Equal(PendingRevalidationQueue.EnqueueResult.RejectedFull,
                queue.Enqueue(_account.Value, _character.Value, "k3", 0));
            // But an existing key still converges (does not count against capacity).
            Assert.Equal(PendingRevalidationQueue.EnqueueResult.Converged,
                queue.Enqueue(_account.Value, _character.Value, "k1", 0));
            Assert.Equal(2, queue.Count);
        }

        [Fact]
        public void Pending_InvalidIdentity_Refused()
        {
            var queue = new PendingRevalidationQueue(Deadline);
            Assert.Equal(PendingRevalidationQueue.EnqueueResult.RejectedInvalid,
                queue.Enqueue("", _character.Value, "k", 0));
            Assert.Equal(PendingRevalidationQueue.EnqueueResult.RejectedInvalid,
                queue.Enqueue(_account.Value, "", "k", 0));
            Assert.Equal(PendingRevalidationQueue.EnqueueResult.RejectedInvalid,
                queue.Enqueue(_account.Value, _character.Value, "", 0));
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void Pending_RestartNeverScans_FreshQueueIsEmpty()
        {
            // The queue is purely in-memory. A "restart" is a brand-new queue — it holds nothing and can
            // therefore never re-credit old loaded pieces (the notice-driven distinction).
            var queue = new PendingRevalidationQueue(Deadline);
            Assert.Equal(0, queue.Count);
            var resolved = queue.Pump(0, (a, c, k) => throw new Exception("must not be called on an empty queue"));
            Assert.Empty(resolved);
        }

        [Fact]
        public void Pending_CreatorMismatchOnResolvedZdo_IsTerminal_NotRetried()
        {
            // Once the ZDO resolves but its creator != the captured sender character, the entry is terminal
            // (dropped), NOT retried until timeout — a foreign piece can never be credited by polling.
            var server = NewServer();
            SeedAndAttune(server);
            var source = new FakeInstanceSource();
            source.Put("1:43", "wood_floor", "player:someone-else", StoneX, StoneZ);
            var ingress = server.CreateDedicatedIngress(source);
            var queue = new PendingRevalidationQueue(Deadline);

            queue.Enqueue(_account.Value, _character.Value, "1:43", 0);
            var resolved = queue.Pump(1, (a, c, k) => ingress.Ingest(a, c, k));

            Assert.Single(resolved);
            Assert.Equal(DedicatedIngressRejection.CreatorMismatch, resolved[0].Rejection);
            Assert.Equal(0, queue.Count);   // removed, not left polling
        }

        // ── fixtures ────────────────────────────────────────────────────────────

        private sealed class FixedFamilyResolver : IStoneFamilyResolver
        {
            private readonly HashSet<string> _keys;
            public FixedFamilyResolver(params StoneId[] stones)
            {
                _keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var s in stones) _keys.Add(s.Value);
            }
            public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
            {
                if (_keys.Contains(stoneId.Value)) { family = "Settlement"; variant = "Homestead"; return true; }
                family = variant = string.Empty; return false;
            }
        }

        private sealed class HomesteadBondPolicy : IBondAuthorityPolicy
        {
            public bool TryAuthorizeBond(StoneId stoneId, string requestedResponsibilityRange,
                out string grantedRange, out string grantedRole)
            {
                grantedRange = requestedResponsibilityRange ?? string.Empty;
                grantedRole = "Governor";
                return string.Equals(requestedResponsibilityRange, "Homestead:All", StringComparison.Ordinal);
            }
        }

        private sealed class FakeInstanceSource : IServerPlacedInstanceSource
        {
            private readonly Dictionary<string, ServerPlacedInstanceFacts> _byKey =
                new Dictionary<string, ServerPlacedInstanceFacts>(StringComparer.Ordinal);

            public void Put(string key, string prefabName, string creatorPrincipal, double x, double z) =>
                _byKey[key] = new ServerPlacedInstanceFacts(key, prefabName, creatorPrincipal, x, z, exists: true);

            public bool TryResolve(string instanceKey, out ServerPlacedInstanceFacts facts)
            {
                if (instanceKey != null && _byKey.TryGetValue(instanceKey, out facts)) return true;
                facts = ServerPlacedInstanceFacts.Absent(instanceKey ?? string.Empty);
                return false;
            }
        }

        private FoundationalProgressionServer NewServer(IMirroredStoneApStore? stoneStore = null, bool registerArea = true)
        {
            var server = FoundationalProgressionServer.Create(
                _durableDir,
                accountIdForPlatform: null,
                familyResolver: new FixedFamilyResolver(_stone, _stoneB),
                bondAuthority: new HomesteadBondPolicy(),
                stoneApStore: stoneStore ?? new InMemoryMirroredStoneApStore());
            if (registerArea) server.StoneAreas.Register(_stone, StoneX, StoneZ, radius: 20.0);
            return server;
        }

        private void SeedAndAttune(FoundationalProgressionServer server)
        {
            ((InMemoryCharacterAggregateStore)server.Characters).PutCharacter(
                new CharacterProgressionAggregate(_account, _character,
                    worldProductScope: "t009r4/trailborne", revision: 0,
                    bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                    stoneRecords: new[] { new CharacterStoneRecord(_stone, 0, 0, 0, null, null) }));
            var res = server.Relationships.Handle(new RelationshipCommand(
                new OperationId("op-att-r4"), RelationshipCommandType.CreateAttunement, _stone,
                new AuthenticatedConnection(_account.Value, _character.Value), default, "rel-att-r4"));
            Assert.Equal(RelationshipCommandOutcome.Applied, res.Outcome);
        }
    }
}
