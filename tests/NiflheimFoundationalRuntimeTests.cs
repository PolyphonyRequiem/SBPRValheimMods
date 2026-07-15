// ============================================================================
//  Homestead progression — T009 LIVE-runtime adapter tests (Tracer 2 remediation).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free live-runtime seam (link-compiled from ../src):
//  a server-observed FoundationalPlacementObservation routed through the composed
//  FoundationalProgressionServer (adapter → relationship-backed pipeline → durable
//  receipt), which before T009 was assembled only in tests. These tests prove the
//  live path IS the tested path across every required case:
//
//    eligible            an authorized, in-area, catalog-member placement earns one receipt.
//    unknown/excluded    a non-catalog / excluded prefab earns nothing (precise reason).
//    outside             a placement outside every Stone Area earns nothing.
//    failed              a failed placement earns nothing.
//    unauthorized        a placement by a character with no active relationship earns nothing.
//    retry/conflict      re-observing the SAME physical instance replays the one receipt (no dup).
//    restart/rehydration a fresh server over the same durable dir resumes the receipt + relationship.
//    repetition-suppress a DIFFERENT physical instance re-crediting an already-credited piece
//                        is suppressed; distinct instances each earn once.
// ============================================================================

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
    public sealed class NiflheimFoundationalRuntimeTests : System.IDisposable
    {
        private readonly string _durableDir;
        private readonly WorldId _world = new WorldId("uid:t009");
        private readonly StoneId _stone;
        private readonly AccountId _account = new AccountId("plat-owner");
        private readonly CharacterId _character = new CharacterId("char-owner");
        private readonly CharacterId _stranger = new CharacterId("char-stranger");

        // Stone Area centered at (100,100); placements near it are inside, far ones outside.
        private const double StoneX = 100.0;
        private const double StoneZ = 100.0;

        public NiflheimFoundationalRuntimeTests()
        {
            _durableDir = Path.Combine(Path.GetTempPath(), "niflheim-t009-" + System.Guid.NewGuid().ToString("N"));
            _stone = StoneId.FromHostZone(_world, 12, -4);
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
                    worldProductScope: "t009/trailborne", revision: 0,
                    bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                    stoneRecords: new[] { new CharacterStoneRecord(_stone, 0, 0, 0, null, null) }));
        }

        private void Attune(FoundationalProgressionServer server, CharacterId who, string opId = "op-attune")
        {
            var res = server.Relationships.Handle(new RelationshipCommand(
                new OperationId(opId), RelationshipCommandType.CreateAttunement, _stone,
                new AuthenticatedConnection(_account.Value, who.Value), default, "rel-att-1"));
            Assert.Equal(RelationshipCommandOutcome.Applied, res.Outcome);
        }

        private FoundationalPlacementObservation ObsAt(
            CharacterId who, string prefabName, string instanceId, double x, double z,
            bool succeeded = true, string versionTag = "v1")
        {
            bool inside = server_membershipHelper(x, z, out var stoneId);
            string stableId = FoundationalPrefabMap.CurrentBuild.ResolveStablePieceId(prefabName) ?? string.Empty;
            return new FoundationalPlacementObservation(
                inside ? stoneId : _stone, _account.Value, who.Value,
                stableId, instanceId, insideStoneArea: inside, placementSucceeded: succeeded,
                foundationalCatalogVersion: versionTag);
        }

        // Small helper so tests describe positions and let the membership resolve inside/outside.
        private readonly StoneAreaMembership _membership = new StoneAreaMembership();
        private bool server_membershipHelper(double x, double z, out StoneId stoneId)
        {
            if (_membership.Count == 0) _membership.Register(_stone, StoneX, StoneZ, 20.0);
            return _membership.TryResolve(x, z, out stoneId);
        }

        // ── eligible ─────────────────────────────────────────────────────────────

        [Fact]
        public void Eligible_AuthorizedInAreaMember_EarnsOneReceipt()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            Seed(server, _character);
            Attune(server, _character);

            var outcome = server.Runtime.Observe(
                ObsAt(_character, "wood_floor", "zdoid:1", StoneX, StoneZ));

            Assert.Equal(RuntimePlacementDisposition.Earned, outcome.Disposition);
            Assert.True(outcome.Credited);
            Assert.Equal(1, outcome.MirroredStoneApDelta);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(_stone));
            Assert.Equal(1, server.Runtime.Log.TotalCredited);
        }

        // ── unknown / excluded ─────────────────────────────────────────────────────

        [Fact]
        public void Unknown_NonCatalogPrefab_EarnsNothing()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);

            var outcome = server.Runtime.Observe(
                ObsAt(_character, "not_a_real_prefab", "zdoid:2", StoneX, StoneZ));

            Assert.Equal(RuntimePlacementDisposition.NotAdmitted, outcome.Disposition);
            Assert.False(outcome.Credited);
            // Unknown prefab resolves to empty stable id -> MissingPieceIdentity.
            Assert.Equal(PlacementAdmission.MissingPieceIdentity, outcome.Admission);
        }

        [Fact]
        public void Excluded_HeldOutStation_EarnsNothing()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);

            var outcome = server.Runtime.Observe(
                ObsAt(_character, "piece_workbench", "zdoid:3", StoneX, StoneZ));

            Assert.Equal(RuntimePlacementDisposition.NotAdmitted, outcome.Disposition);
            Assert.Equal(PlacementAdmission.ExcludedPiece, outcome.Admission);
        }

        // ── outside ────────────────────────────────────────────────────────────────

        [Fact]
        public void Outside_PlacementBeyondEveryStoneArea_EarnsNothing()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);

            var outcome = server.Runtime.Observe(
                ObsAt(_character, "wood_floor", "zdoid:4", StoneX + 500.0, StoneZ + 500.0));

            Assert.Equal(RuntimePlacementDisposition.NotAdmitted, outcome.Disposition);
            Assert.Equal(PlacementAdmission.OutsideStoneArea, outcome.Admission);
        }

        // ── failed ───────────────────────────────────────────────────────────────

        [Fact]
        public void Failed_UnsuccessfulPlacement_EarnsNothing()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);

            var outcome = server.Runtime.Observe(
                ObsAt(_character, "wood_floor", "zdoid:5", StoneX, StoneZ, succeeded: false));

            Assert.Equal(RuntimePlacementDisposition.NotAdmitted, outcome.Disposition);
            Assert.Equal(PlacementAdmission.PlacementFailed, outcome.Admission);
        }

        // ── unauthorized ───────────────────────────────────────────────────────────

        [Fact]
        public void Unauthorized_NoActiveRelationship_EarnsNothing()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            Seed(server, _stranger); // seeded but NEVER attuned

            var outcome = server.Runtime.Observe(
                ObsAt(_stranger, "wood_floor", "zdoid:6", StoneX, StoneZ));

            Assert.Equal(RuntimePlacementDisposition.PipelineRejected, outcome.Disposition);
            Assert.Equal("RelationshipRequired", outcome.ResultCode);
            Assert.False(outcome.Credited);
            Assert.Equal(0, stoneStore.GetMirroredStoneAp(_stone));
            Assert.DoesNotContain("op-", string.Join(",", server.Receipts.DurableOperationIds()));
        }

        // ── retry / conflict ─────────────────────────────────────────────────────

        [Fact]
        public void Retry_SamePhysicalInstanceReObserved_ReplaysOneReceiptNoDup()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            Seed(server, _character);
            Attune(server, _character);

            var first = server.Runtime.Observe(ObsAt(_character, "wood_floor", "zdoid:7", StoneX, StoneZ));
            Assert.Equal(RuntimePlacementDisposition.Earned, first.Disposition);

            // Re-observe the SAME physical piece (same instance id) — e.g. a duplicate build event, a
            // reconnect, a re-scan. Deterministic operation id => pure replay, exactly one credit.
            var replay = server.Runtime.Observe(ObsAt(_character, "wood_floor", "zdoid:7", StoneX, StoneZ));
            Assert.Equal(RuntimePlacementDisposition.Replayed, replay.Disposition);
            Assert.True(replay.Credited);
            Assert.Equal(first.OperationId.Value, replay.OperationId.Value);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(_stone));
        }

        // ── restart / rehydration ──────────────────────────────────────────────────

        [Fact]
        public void Restart_FreshServerOverSameDurableDir_ResumesReceiptAndRelationship()
        {
            // Boot 1: attune + earn exactly one receipt on a real physical instance.
            var stone1 = new InMemoryMirroredStoneApStore();
            var server1 = NewServer(stone1);
            Seed(server1, _character);
            Attune(server1, _character);
            var earn = server1.Runtime.Observe(ObsAt(_character, "wood_floor", "zdoid:8", StoneX, StoneZ));
            Assert.Equal(RuntimePlacementDisposition.Earned, earn.Disposition);
            Assert.Equal(1, stone1.GetMirroredStoneAp(_stone));

            // Boot 2: fresh stores/handlers over the SAME durable directory == restarted server.
            var stone2 = new InMemoryMirroredStoneApStore();
            var server2 = NewServer(stone2);
            Seed(server2, _character); // clean character seed; relationship rehydrates from journal
            Assert.True(server2.Authority.GetAuthority(_account, _stone).HasActive(_character),
                "attunement must survive restart");
            Assert.Equal(1, stone2.GetMirroredStoneAp(_stone));

            // Re-observing the same physical instance after restart is a pure replay: still exactly one.
            var replay = server2.Runtime.Observe(ObsAt(_character, "wood_floor", "zdoid:8", StoneX, StoneZ));
            Assert.Equal(RuntimePlacementDisposition.Replayed, replay.Disposition);
            Assert.Equal(1, stone2.GetMirroredStoneAp(_stone));
        }

        // ── physical-instance repetition suppression ────────────────────────────────

        [Fact]
        public void Repetition_DistinctInstancesEarnOnce_SameInstanceByNewOpSuppressed()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            Seed(server, _character);
            Attune(server, _character);

            // Two DISTINCT physical instances each earn once.
            Assert.Equal(RuntimePlacementDisposition.Earned,
                server.Runtime.Observe(ObsAt(_character, "wood_floor", "zdoid:9", StoneX, StoneZ)).Disposition);
            Assert.Equal(RuntimePlacementDisposition.Earned,
                server.Runtime.Observe(ObsAt(_character, "wood_wall", "zdoid:10", StoneX + 1, StoneZ + 1)).Disposition);
            Assert.Equal(2, stoneStore.GetMirroredStoneAp(_stone));

            // Re-crediting an ALREADY-credited physical instance never adds AP. Because the operation
            // id is derived deterministically from the physical-instance provenance, a re-observation of
            // instance zdoid:9 carrying DIFFERENT content resolves to the same operation id and is caught
            // as an OperationConflict at the durable receipt layer (the idempotency authority) — no
            // second credit. Either way the physical piece is credited at most once.
            var repeat = server.Runtime.Observe(ObsAt(_character, "wood_pole", "zdoid:9", StoneX, StoneZ));
            Assert.False(repeat.Credited);
            Assert.Equal(RuntimePlacementDisposition.PipelineRejected, repeat.Disposition);
            Assert.Equal("OperationConflict", repeat.ResultCode);
            Assert.Equal(2, stoneStore.GetMirroredStoneAp(_stone));
        }

        // ── operator-readable bounded log ────────────────────────────────────────────

        [Fact]
        public void OperatorLog_IsBounded_AndCountsCreditedVsTotal()
        {
            var server = FoundationalProgressionServer.Create(
                _durableDir, null, new FixedFamilyResolver(_stone), new HomesteadBondPolicy(),
                new InMemoryMirroredStoneApStore(), log: new RuntimePlacementLog(capacity: 4));
            server.StoneAreas.Register(_stone, StoneX, StoneZ, 20.0);
            Seed(server, _character);
            Attune(server, _character);

            // 10 observations, capacity 4 -> ring holds only the last 4, totals are monotonic.
            for (int i = 0; i < 10; i++)
                server.Runtime.Observe(ObsAt(_character, "wood_floor", "zdoid:log" + i, StoneX, StoneZ));

            Assert.Equal(4, server.Runtime.Log.Recent().Count);
            Assert.Equal(10, server.Runtime.Log.TotalObserved);
            Assert.Equal(10, server.Runtime.Log.TotalCredited);
            foreach (var line in server.Runtime.Log.Recent())
                Assert.Contains("foundational-live", line.ToOperatorLine());
        }

        // ── stale catalog version ────────────────────────────────────────────────────

        [Fact]
        public void StaleCatalogVersion_EarnsNothing()
        {
            var server = NewServer();
            Seed(server, _character);
            Attune(server, _character);

            var outcome = server.Runtime.Observe(
                ObsAt(_character, "wood_floor", "zdoid:11", StoneX, StoneZ, versionTag: "v0"));

            Assert.Equal(RuntimePlacementDisposition.NotAdmitted, outcome.Disposition);
            Assert.Equal(PlacementAdmission.StaleCatalogVersion, outcome.Admission);
        }
    }
}
