// ============================================================================
//  Homestead progression — T009R2 DEDICATED-server ingress + revalidation tests.
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free DedicatedPlacementIngress (link-compiled
//  from ../src): a joined dedicated-server client's build never runs
//  Player.PlacePiece on the server, so the listen-host observer cannot see it.
//  The client fires a NOTICE that only IDENTIFIES a candidate physical instance;
//  the server independently re-derives every credit-bearing fact from its OWN
//  authoritative ZDO store (here a fake IServerPlacedInstanceSource) and binds the
//  creator to the AUTHENTICATED sender before routing through the SAME shared
//  FoundationalPlacementRuntime validation core the listen-host path uses.
//
//    eligible          an authenticated sender whose created, in-area, catalog
//                      member instance resolves server-side earns one receipt.
//    fabricated key    a key that resolves to nothing earns nothing (NoSuchInstance).
//    creator mismatch  a sender pointing at a piece it did not create earns nothing.
//    payload-not-authority  a lying notice cannot override server-derived facts
//                      (unknown prefab / outside area re-derived, not from payload).
//    startup/replication  no notice => no award for old loaded pieces.
//    duplicate notice  replayed notices for one instance converge on one receipt.
//    conflicting reuse a credited instance reused under a new op rejects.
//    shared core       dedicated ingress and listen-host Observe converge on the
//                      one receipt for the same physical instance.
// ============================================================================

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
    public sealed class NiflheimDedicatedIngressTests : System.IDisposable
    {
        private readonly string _durableDir;
        private readonly WorldId _world = new WorldId("uid:t009r2");
        private readonly StoneId _stone;
        // T009R4 corrected identity split (Blockers 2/3): the ACCOUNT is the authenticated platform/socket
        // subject; the CHARACTER is the stable player:<s_playerID> subject a placed piece's ZDO s_creator
        // is stamped from. The ingress binds creator == CHARACTER (not account).
        private readonly AccountId _account = new AccountId("acct:steam-100");   // platform account subject
        private readonly CharacterId _character = new CharacterId("player:5555"); // stable s_playerID subject
        private const double StoneX = 100.0;
        private const double StoneZ = 100.0;

        public NiflheimDedicatedIngressTests()
        {
            _durableDir = Path.Combine(Path.GetTempPath(), "niflheim-t009r2-" + System.Guid.NewGuid().ToString("N"));
            _stone = StoneId.FromHostZone(_world, 7, 3);
        }

        public void Dispose()
        {
            if (Directory.Exists(_durableDir)) Directory.Delete(_durableDir, recursive: true);
        }

        // ── fixtures ────────────────────────────────────────────────────────────

        private sealed class FixedFamilyResolver : IStoneFamilyResolver
        {
            private readonly string _key;
            public FixedFamilyResolver(StoneId stone) { _key = stone.Value; }
            public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
            {
                if (string.Equals(stoneId.Value, _key, System.StringComparison.Ordinal))
                { family = "Settlement"; variant = "Homestead"; return true; }
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
                return string.Equals(requestedResponsibilityRange, "Homestead:All", System.StringComparison.Ordinal);
            }
        }

        /// <summary>A fake server-owned ZDO store. Only the instances registered here "exist"; every fact
        /// returned is server-derived, exactly like the ZDOMan-backed production source.</summary>
        private sealed class FakeInstanceSource : IServerPlacedInstanceSource
        {
            private readonly Dictionary<string, ServerPlacedInstanceFacts> _byKey =
                new Dictionary<string, ServerPlacedInstanceFacts>(System.StringComparer.Ordinal);

            public void Put(string key, string prefabName, string creatorPrincipal, double x, double z) =>
                _byKey[key] = new ServerPlacedInstanceFacts(key, prefabName, creatorPrincipal, x, z, exists: true);

            public bool TryResolve(string instanceKey, out ServerPlacedInstanceFacts facts)
            {
                if (instanceKey != null && _byKey.TryGetValue(instanceKey, out facts)) return true;
                facts = ServerPlacedInstanceFacts.Absent(instanceKey ?? string.Empty);
                return false;
            }
        }

        private FoundationalProgressionServer NewServer(IMirroredStoneApStore? stoneStore = null)
        {
            var server = FoundationalProgressionServer.Create(
                _durableDir,
                accountIdForPlatform: null,                     // candidate A: platform id as account
                familyResolver: new FixedFamilyResolver(_stone),
                bondAuthority: new HomesteadBondPolicy(),
                stoneApStore: stoneStore ?? new InMemoryMirroredStoneApStore());
            server.StoneAreas.Register(_stone, StoneX, StoneZ, radius: 20.0);
            return server;
        }

        private void Seed(FoundationalProgressionServer server, CharacterId who)
        {
            ((InMemoryCharacterAggregateStore)server.Characters).PutCharacter(
                new CharacterProgressionAggregate(_account, who,
                    worldProductScope: "t009r2/trailborne", revision: 0,
                    bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                    stoneRecords: new[] { new CharacterStoneRecord(_stone, 0, 0, 0, null, null) }));
        }

        private void Attune(FoundationalProgressionServer server, CharacterId who)
        {
            var res = server.Relationships.Handle(new RelationshipCommand(
                new OperationId("op-attune"), RelationshipCommandType.CreateAttunement, _stone,
                new AuthenticatedConnection(_account.Value, who.Value), default, "rel-att-1"));
            Assert.Equal(RelationshipCommandOutcome.Applied, res.Outcome);
        }

        // ── eligible ─────────────────────────────────────────────────────────────

        [Fact]
        public void Eligible_AuthenticatedSenderOwnInAreaMember_EarnsOneReceipt()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            Seed(server, _character);
            Attune(server, _character);

            var source = new FakeInstanceSource();
            source.Put("100:1", "wood_floor", _character.Value, StoneX, StoneZ);
            var ingress = server.CreateDedicatedIngress(source);

            var outcome = ingress.Ingest(_account.Value, _character.Value, "100:1");

            Assert.True(outcome.Routed);
            Assert.Equal(RuntimePlacementDisposition.Earned, outcome.Runtime.Disposition);
            Assert.True(outcome.Credited);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(_stone));
        }

        // ── fabricated / stale key ─────────────────────────────────────────────────

        [Fact]
        public void FabricatedKey_ResolvesToNothing_EarnsNothing()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            Seed(server, _character);
            Attune(server, _character);

            var ingress = server.CreateDedicatedIngress(new FakeInstanceSource()); // empty store
            var outcome = ingress.Ingest(_account.Value, _character.Value, "100:999");

            Assert.False(outcome.Routed);
            Assert.Equal(DedicatedIngressRejection.NoSuchInstance, outcome.Rejection);
            Assert.False(outcome.Credited);
            Assert.Equal(0, stoneStore.GetMirroredStoneAp(_stone));
        }

        [Fact]
        public void MissingInstanceKey_EarnsNothing()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);

            var ingress = server.CreateDedicatedIngress(new FakeInstanceSource());
            var outcome = ingress.Ingest(_account.Value, _character.Value, "");

            Assert.False(outcome.Routed);
            Assert.Equal(DedicatedIngressRejection.MissingInstanceKey, outcome.Rejection);
        }

        // ── creator binding: payload identity is never authority ─────────────────────

        [Fact]
        public void CreatorMismatch_SenderDidNotCreateInstance_EarnsNothing()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            Seed(server, _character);
            Attune(server, _character);

            var source = new FakeInstanceSource();
            // The ZDO was created by SOMEONE ELSE; the authenticated sender is _account.
            source.Put("100:2", "wood_floor", "player:777", StoneX, StoneZ);
            var ingress = server.CreateDedicatedIngress(source);

            var outcome = ingress.Ingest(_account.Value, _character.Value, "100:2");

            Assert.False(outcome.Routed);
            Assert.Equal(DedicatedIngressRejection.CreatorMismatch, outcome.Rejection);
            Assert.Equal(0, stoneStore.GetMirroredStoneAp(_stone));
        }

        [Fact]
        public void EmptyCreatorOnInstance_Unbindable_EarnsNothing()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);

            var source = new FakeInstanceSource();
            source.Put("100:3", "wood_floor", creatorPrincipal: "", StoneX, StoneZ);
            var ingress = server.CreateDedicatedIngress(source);

            var outcome = ingress.Ingest(_account.Value, _character.Value, "100:3");

            Assert.False(outcome.Routed);
            Assert.Equal(DedicatedIngressRejection.CreatorMismatch, outcome.Rejection);
        }

        // ── payload facts are never authority: server re-derives identity + area ─────

        [Fact]
        public void UnknownPrefabOnResolvedInstance_ReDerivedServerSide_EarnsNothing()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);

            var source = new FakeInstanceSource();
            // A resolvable, correctly-created instance — but the SERVER-observed prefab is not Foundational.
            source.Put("100:4", "not_a_real_prefab", _character.Value, StoneX, StoneZ);
            var ingress = server.CreateDedicatedIngress(source);

            var outcome = ingress.Ingest(_account.Value, _character.Value, "100:4");

            Assert.True(outcome.Routed);   // creator matched; the SHARED adapter then rejects the identity
            Assert.Equal(RuntimePlacementDisposition.NotAdmitted, outcome.Runtime.Disposition);
            Assert.Equal(PlacementAdmission.MissingPieceIdentity, outcome.Runtime.Admission);
        }

        [Fact]
        public void OutsideArea_ReDerivedFromServerPosition_EarnsNothing()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);

            var source = new FakeInstanceSource();
            // Server-owned position is far from every Stone Area — no claimed area can override it.
            source.Put("100:5", "wood_floor", _character.Value, StoneX + 500.0, StoneZ + 500.0);
            var ingress = server.CreateDedicatedIngress(source);

            var outcome = ingress.Ingest(_account.Value, _character.Value, "100:5");

            Assert.True(outcome.Routed);
            Assert.Equal(RuntimePlacementDisposition.NotAdmitted, outcome.Runtime.Disposition);
            Assert.Equal(PlacementAdmission.OutsideStoneArea, outcome.Runtime.Admission);
        }

        [Fact]
        public void ExcludedStation_ReDerivedServerSide_EarnsNothing()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);

            var source = new FakeInstanceSource();
            source.Put("100:6", "piece_workbench", _character.Value, StoneX, StoneZ);
            var ingress = server.CreateDedicatedIngress(source);

            var outcome = ingress.Ingest(_account.Value, _character.Value, "100:6");

            Assert.True(outcome.Routed);
            Assert.Equal(PlacementAdmission.ExcludedPiece, outcome.Runtime.Admission);
        }

        // ── unauthorized: no active relationship ────────────────────────────────────

        [Fact]
        public void Unauthorized_NoActiveRelationship_EarnsNothing()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            Seed(server, _character);   // seeded but NEVER attuned

            var source = new FakeInstanceSource();
            source.Put("100:7", "wood_floor", _character.Value, StoneX, StoneZ);
            var ingress = server.CreateDedicatedIngress(source);

            var outcome = ingress.Ingest(_account.Value, _character.Value, "100:7");

            Assert.True(outcome.Routed);
            Assert.Equal(RuntimePlacementDisposition.PipelineRejected, outcome.Runtime.Disposition);
            Assert.Equal("RelationshipRequired", outcome.Runtime.ResultCode);
            Assert.Equal(0, stoneStore.GetMirroredStoneAp(_stone));
        }

        // ── duplicate / replayed notice converges on one receipt ─────────────────────

        [Fact]
        public void DuplicateNotice_SameInstance_ConvergesOnOneReceipt()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            Seed(server, _character);
            Attune(server, _character);

            var source = new FakeInstanceSource();
            source.Put("100:8", "wood_floor", _character.Value, StoneX, StoneZ);
            var ingress = server.CreateDedicatedIngress(source);

            var first = ingress.Ingest(_account.Value, _character.Value, "100:8");
            Assert.Equal(RuntimePlacementDisposition.Earned, first.Runtime.Disposition);

            // A duplicate / replayed notice (client resend, reconnect) for the SAME physical instance.
            var replay = ingress.Ingest(_account.Value, _character.Value, "100:8");
            Assert.Equal(RuntimePlacementDisposition.Replayed, replay.Runtime.Disposition);
            Assert.Equal(first.Runtime.OperationId.Value, replay.Runtime.OperationId.Value);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(_stone));
        }

        // ── conflicting reuse of a credited instance rejects ─────────────────────────

        [Fact]
        public void ConflictingReuse_CreditedInstanceNewContent_Rejects()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            Seed(server, _character);
            Attune(server, _character);

            var source = new FakeInstanceSource();
            source.Put("100:9", "wood_floor", _character.Value, StoneX, StoneZ);
            var ingress = server.CreateDedicatedIngress(source);

            Assert.Equal(RuntimePlacementDisposition.Earned,
                ingress.Ingest(_account.Value, _character.Value, "100:9").Runtime.Disposition);

            // The same physical instance key is mutated to a different piece (a conflicting reuse). The
            // deterministic ZDOID-derived op id collides at the receipt layer → OperationConflict, no dup.
            source.Put("100:9", "wood_wall", _character.Value, StoneX, StoneZ);
            var conflict = ingress.Ingest(_account.Value, _character.Value, "100:9");
            Assert.False(conflict.Credited);
            Assert.Equal(RuntimePlacementDisposition.PipelineRejected, conflict.Runtime.Disposition);
            Assert.Equal("OperationConflict", conflict.Runtime.ResultCode);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(_stone));
        }

        // ── startup / replication: no notice => no award of old loaded pieces ────────

        [Fact]
        public void Startup_NoNoticeFired_AwardsNothingForLoadedPieces()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            Seed(server, _character);
            Attune(server, _character);

            // Instances exist in the server store (loaded/replicated), but NO notice is ever ingested.
            var source = new FakeInstanceSource();
            source.Put("100:10", "wood_floor", _character.Value, StoneX, StoneZ);
            _ = server.CreateDedicatedIngress(source);

            // Nothing observed => nothing credited. Ingress is notice-driven, never a ZDO scan.
            Assert.Equal(0, stoneStore.GetMirroredStoneAp(_stone));
            Assert.Equal(0, server.Runtime.Log.TotalObserved);
        }

        // ── shared core: dedicated ingress and listen-host Observe converge ──────────

        [Fact]
        public void SharedCore_ListenHostThenDedicatedNotice_OneReceiptForSamePhysicalInstance()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            Seed(server, _character);
            Attune(server, _character);

            // Listen-host path credits the instance directly (its PlacePiece ran on the server).
            var hostObs = new FoundationalPlacementObservation(
                _stone, _account.Value, _character.Value, "foundation_wood_floor", "100:11",
                insideStoneArea: true, placementSucceeded: true, foundationalCatalogVersion: "v1");
            Assert.Equal(RuntimePlacementDisposition.Earned, server.Runtime.Observe(hostObs).Disposition);

            // A late dedicated notice for the SAME physical instance is a pure replay — no second credit.
            var source = new FakeInstanceSource();
            source.Put("100:11", "wood_floor", _character.Value, StoneX, StoneZ);
            var replay = server.CreateDedicatedIngress(source).Ingest(_account.Value, _character.Value, "100:11");
            Assert.Equal(RuntimePlacementDisposition.Replayed, replay.Runtime.Disposition);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(_stone));
        }
    }
}
