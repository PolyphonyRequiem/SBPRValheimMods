// ============================================================================
//  QA-M3R real fixture adapter tests (t_1572d041) — engine-free, headless.
// ----------------------------------------------------------------------------
//  Covers the M3R corrective slice: the crash-safe LedgerSnapshotStore, the
//  request->plan mapper, the gated ServerFixtureExecutor, and the responder
//  bridge — all against the SHIPPED engine-free sources + fakes, so the tested
//  logic IS the shipped logic (the engine-bound seam/authority are thin and
//  compile-only). Named coverage from the card body:
//
//    exact allowlist/bounds/radius .......... Mapper_* / Executor bounds tests
//    prefab drift ........................... Seam PrefabExists (existing) + Map unknown
//    product ID refusal ..................... Mapper_RefusesProductId (pre-allowlist)
//    additive-only component set ............ Seam_ExposesNoNonAdditiveClonePath (existing) + here
//    admin/peer/world recheck zero-side-effect  Executor_*_NoWorldEffect
//    partial spawn .......................... Executor_PartialSpawn_*
//    restart reconcile ...................... Store_RoundTrip + Executor_RestartReconcile
//    owned-only cleanup ..................... Executor_Cleanup_OwnedOnly
//    unrelated preservation ................. Executor_Cleanup_PreservesUnrelated
//    stale generation ....................... Executor_RejectsStaleGeneration_NoWorldEffect
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SBPR.QaHarness.T022.Core.ControlPlane;
using SBPR.QaHarness.T022.Core.Fixtures;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    // ── Crash-safe durable snapshot store ──────────────────────────────────
    public sealed class LedgerSnapshotStoreTests : IDisposable
    {
        private readonly string _dir;

        public LedgerSnapshotStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sbpr-qa-m3r-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private static ValidatedFixturePlan Plan(string fixtureId, string id, int count, double radius)
        {
            var allow = VanillaFixtureManifest.BuildAllowlist();
            var plan = new FixturePlan(fixtureId, new[] { new ResourceSpec(id, count, radius) });
            return FixturePlanValidator.Validate(plan, allow, VanillaFixtureManifest.Bounds).Plan!;
        }

        [Fact]
        public void Load_Absent_WhenNoFile()
        {
            var store = new LedgerSnapshotStore(Path.Combine(_dir, "nope.ledger"));
            Assert.False(store.Exists());
            Assert.Equal(SnapshotLoadStatus.Absent, store.Load().Status);
        }

        [Fact]
        public void Save_Then_Load_RoundTrips_ExactOwnedIds()
        {
            var store = new LedgerSnapshotStore(Path.Combine(_dir, "fx.ledger"));
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench", "Wood" });
            var world = new SeamFixtureWorld(seam);
            var ledger = OwnedResourceLedger.ForPlan(Plan("fx", "Wood", 3, 1.0));
            ledger.Ensure(world, TestRun.Ctx);

            store.Save(ledger);
            Assert.True(store.Exists());

            var loaded = store.Load();
            Assert.True(loaded.Ok);
            var restored = OwnedResourceLedger.FromSnapshot(loaded.Snapshot!);
            // Exact same owned ids + states survive the round-trip.
            var before = ledger.Entries.Select(e => e.Id.Canonical + ":" + e.State).OrderBy(s => s).ToArray();
            var after = restored.Entries.Select(e => e.Id.Canonical + ":" + e.State).OrderBy(s => s).ToArray();
            Assert.Equal(before, after);
        }

        [Fact]
        public void Load_Corrupt_WhenFileTruncated()
        {
            string path = Path.Combine(_dir, "corrupt.ledger");
            File.WriteAllText(path, "SBPR-QA-FIXLEDGER\t1\tfx\t"); // header claims fields but is truncated
            var store = new LedgerSnapshotStore(path);
            Assert.Equal(SnapshotLoadStatus.Corrupt, store.Load().Status);
        }

        [Fact]
        public void Save_IsAtomic_LeavesNoTempAfterSuccess()
        {
            var store = new LedgerSnapshotStore(Path.Combine(_dir, "atomic.ledger"));
            var ledger = OwnedResourceLedger.ForPlan(Plan("fx", "Wood", 1, 1.0));
            store.Save(ledger);
            store.Save(ledger); // second save exercises the File.Replace (backup) path
            Assert.True(store.Exists());
            Assert.False(File.Exists(Path.Combine(_dir, "atomic.ledger.tmp")));
        }

        [Fact]
        public void Delete_RemovesFileAndSiblings()
        {
            string path = Path.Combine(_dir, "del.ledger");
            var store = new LedgerSnapshotStore(path);
            var ledger = OwnedResourceLedger.ForPlan(Plan("fx", "Wood", 1, 1.0));
            store.Save(ledger);
            store.Save(ledger);
            store.Delete();
            Assert.False(store.Exists());
            Assert.False(File.Exists(path + ".bak"));
            Assert.False(File.Exists(path + ".tmp"));
        }
    }

    // ── Request -> plan mapping ────────────────────────────────────────────
    public sealed class FixtureRequestMapperTests
    {
        private static Dictionary<string, object?> Args(params (string k, object? v)[] kv)
        {
            var d = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (k, v) in kv) d[k] = v;
            return d;
        }

        [Fact]
        public void Map_SpawnStation_ToBoundedVanillaPlan()
        {
            var r = FixtureRequestMapper.Map("fx", "SpawnStation", Args(("prefab", "piece_workbench"), ("posRadius", 2.0)));
            Assert.True(r.Ok);
            Assert.Single(r.Plan!.Resources);
            Assert.Equal(ResourceCategory.Station, r.Plan!.Resources[0].Category);
        }

        [Fact]
        public void Map_GrantMaterials_ExpandsCount()
        {
            var r = FixtureRequestMapper.Map("fx", "GrantVanillaMaterials", Args(("itemId", "Wood"), ("qty", 4L)));
            Assert.True(r.Ok);
            Assert.Equal(4, r.Plan!.Resources.Count);
            Assert.All(r.Plan!.Resources, x => Assert.Equal(ResourceCategory.Material, x.Category));
        }

        [Theory]
        [InlineData("SBPR_Masterwork")]
        [InlineData("Attunement")]
        public void Map_RefusesProductId_PreAllowlist(string prefab)
        {
            var r = FixtureRequestMapper.Map("fx", "SpawnStation", Args(("prefab", prefab), ("posRadius", 1.0)));
            Assert.False(r.Ok);
            Assert.Equal(FixtureMapReason.ProductId, r.Reason);
        }

        [Fact]
        public void Map_RefusesNonFixtureVerb()
        {
            var r = FixtureRequestMapper.Map("fx", "Craft", Args(("recipeName", "x"), ("station", "y")));
            Assert.False(r.Ok);
            Assert.Equal(FixtureMapReason.NotAFixtureVerb, r.Reason);
        }

        [Fact]
        public void Map_RefusesUnknownPrefab_AsPlanRejected()
        {
            var r = FixtureRequestMapper.Map("fx", "SpawnStation", Args(("prefab", "not_a_vanilla_id"), ("posRadius", 1.0)));
            Assert.False(r.Ok);
            Assert.Equal(FixtureMapReason.PlanRejected, r.Reason);
            Assert.Equal(PlanRejectionReason.UnknownLogicalId, r.PlanReason);
        }

        [Fact]
        public void Map_EnforcesRealBounds_Radius()
        {
            double overRadius = VanillaFixtureManifest.Bounds.MaxRadiusMeters + 1.0;
            var r = FixtureRequestMapper.Map("fx", "SpawnStation", Args(("prefab", "forge"), ("posRadius", overRadius)));
            Assert.False(r.Ok);
            Assert.Equal(PlanRejectionReason.RadiusOutOfBounds, r.PlanReason);
        }

        [Fact]
        public void Map_EnforcesRealBounds_Count()
        {
            long overCount = VanillaFixtureManifest.Bounds.MaxCountPerResource + 1;
            var r = FixtureRequestMapper.Map("fx", "GrantVanillaMaterials", Args(("itemId", "Stone"), ("qty", overCount)));
            Assert.False(r.Ok);
            Assert.Equal(PlanRejectionReason.CountOverflow, r.PlanReason);
        }

        [Fact]
        public void Map_MissingArg_Refused()
        {
            var r = FixtureRequestMapper.Map("fx", "SpawnStation", Args(("posRadius", 1.0)));
            Assert.False(r.Ok);
            Assert.Equal(FixtureMapReason.MissingArg, r.Reason);
        }
    }

    // ── Gated, crash-safe executor ─────────────────────────────────────────
    public sealed class ServerFixtureExecutorTests : IDisposable
    {
        private readonly string _dir;

        public ServerFixtureExecutorTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sbpr-qa-m3rx-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private static Dictionary<string, object?> Args(params (string k, object? v)[] kv)
        {
            var d = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (k, v) in kv) d[k] = v;
            return d;
        }

        private (ServerFixtureExecutor exec, FakeVanillaFixtureSeam seam, FakeAuthority auth, DeliveringPeerState peers)
            Build(FakeVanillaFixtureSeam? seam = null)
        {
            seam ??= new FakeVanillaFixtureSeam(new[] { "piece_workbench", "Wood", "forge" });
            var world = new SeamFixtureWorld(seam);
            var auth = new FakeAuthority();
            var peers = new DeliveringPeerState();
            var exec = new ServerFixtureExecutor(auth, peers, world,
                id => new LedgerSnapshotStore(Path.Combine(_dir, id + ".ledger")), TestRun.Ctx);
            return (exec, seam, auth, peers);
        }

        [Fact]
        public void Ensure_HappyPath_CreatesAndPersists()
        {
            var (exec, seam, auth, peers) = Build();
            auth.Admins.Add("owner");
            var bind = peers.Bind("owner");

            var r = exec.Ensure("fx", "GrantVanillaMaterials", Args(("itemId", "Wood"), ("qty", 3L)), "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.Executed, r.Status);
            Assert.Equal(3, r.Created);
            Assert.Equal(3, seam.Live.Count);
            Assert.True(File.Exists(Path.Combine(_dir, "fx.ledger")));
        }

        [Fact]
        public void Ensure_RejectsNonAdmin_NoWorldEffect_NoSnapshot()
        {
            var (exec, seam, auth, peers) = Build();
            var bind = peers.Bind("owner"); // not admin

            var r = exec.Ensure("fx", "SpawnStation", Args(("prefab", "piece_workbench"), ("posRadius", 2.0)), "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.AuthorityRejected, r.Status);
            Assert.Equal(FixtureAuthorityReason.NotAdmin, r.AuthorityReason);
            Assert.Empty(seam.Live);
            Assert.False(File.Exists(Path.Combine(_dir, "fx.ledger")));
        }

        [Fact]
        public void Ensure_RejectsPeerSubstitution_NoWorldEffect()
        {
            var (exec, seam, auth, peers) = Build();
            auth.Admins.Add("owner");
            auth.Admins.Add("intruder");
            var bind = peers.Bind("owner");

            var r = exec.Ensure("fx", "SpawnStation", Args(("prefab", "forge"), ("posRadius", 1.0)), "intruder", bind.Generation);
            Assert.Equal(FixtureExecStatus.AuthorityRejected, r.Status);
            Assert.Equal(FixtureAuthorityReason.PeerRejected, r.AuthorityReason);
            Assert.Empty(seam.Live);
        }

        [Fact]
        public void Ensure_RejectsStaleGeneration_NoWorldEffect()
        {
            var (exec, seam, auth, peers) = Build();
            auth.Admins.Add("owner");
            peers.Bind("owner");   // gen 1
            peers.Bind("owner");   // reconnect -> gen 2

            var r = exec.Ensure("fx", "SpawnStation", Args(("prefab", "forge"), ("posRadius", 1.0)), "owner", 1);
            Assert.Equal(FixtureExecStatus.AuthorityRejected, r.Status);
            Assert.Equal(FixtureAuthorityReason.PeerRejected, r.AuthorityReason);
            Assert.Empty(seam.Live);
        }

        [Fact]
        public void Ensure_RejectsWorldNotLoaded_NoWorldEffect()
        {
            var (exec, seam, auth, peers) = Build();
            auth.WorldLoaded = false;
            auth.Admins.Add("owner");
            var bind = peers.Bind("owner");

            var r = exec.Ensure("fx", "SpawnStation", Args(("prefab", "forge"), ("posRadius", 1.0)), "owner", bind.Generation);
            Assert.Equal(FixtureAuthorityReason.WorldNotLoaded, r.AuthorityReason);
            Assert.Empty(seam.Live);
        }

        [Fact]
        public void Ensure_ProductId_MapRejected_NoGateNoWorldEffect()
        {
            var (exec, seam, auth, peers) = Build();
            auth.Admins.Add("owner");
            var bind = peers.Bind("owner");

            var r = exec.Ensure("fx", "SpawnStation", Args(("prefab", "SBPR_Masterwork"), ("posRadius", 1.0)), "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.MapRejected, r.Status);
            Assert.Equal(FixtureMapReason.ProductId, r.MapReason);
            Assert.Empty(seam.Live);
        }

        [Fact]
        public void Ensure_PartialSpawn_ThenRetry_IsIdempotent()
        {
            var failing = new FailingSeam("Wood");
            var world = new SeamFixtureWorld(failing);
            var auth2 = new FakeAuthority(); auth2.Admins.Add("owner");
            var peers2 = new DeliveringPeerState();
            var bind = peers2.Bind("owner");
            var exec2 = new ServerFixtureExecutor(auth2, peers2, world,
                id => new LedgerSnapshotStore(Path.Combine(_dir, id + ".ledger")), TestRun.Ctx);

            failing.FailGrant = true;
            var first = exec2.Ensure("fx", "GrantVanillaMaterials", Args(("itemId", "Wood"), ("qty", 2L)), "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.Executed, first.Status);
            Assert.Equal(0, first.Created);
            Assert.Equal(2, first.Failed);

            failing.FailGrant = false;
            var second = exec2.Ensure("fx", "GrantVanillaMaterials", Args(("itemId", "Wood"), ("qty", 2L)), "owner", bind.Generation);
            Assert.Equal(2, second.Created);
            Assert.Equal(2, failing.Live.Count);
        }

        [Fact]
        public void RestartReconcile_ReCreatesVanished_NoDoubleCreate()
        {
            // Run 1: create + persist.
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench" });
            var world = new SeamFixtureWorld(seam);
            var auth = new FakeAuthority(); auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            Func<string, LedgerSnapshotStore> factory = id => new LedgerSnapshotStore(Path.Combine(_dir, id + ".ledger"));

            var exec1 = new ServerFixtureExecutor(auth, peers, world, factory, TestRun.Ctx);
            var r1 = exec1.Ensure("fx", "SpawnStation", Args(("prefab", "piece_workbench"), ("posRadius", 2.0)), "owner", bind.Generation);
            Assert.Equal(1, r1.Created);

            // Simulate a crash + world wipe: the object vanished from the seam.
            foreach (var h in seam.Live.ToArray()) seam.Despawn(h);

            // Run 2: a fresh executor loads the snapshot, reconciles (downgrades vanished), re-creates.
            var peers2 = new DeliveringPeerState();
            var bind2 = peers2.Bind("owner");
            var exec2 = new ServerFixtureExecutor(auth, peers2, world, factory, TestRun.Ctx);
            var r2 = exec2.Ensure("fx", "SpawnStation", Args(("prefab", "piece_workbench"), ("posRadius", 2.0)), "owner", bind2.Generation);
            Assert.Equal(1, r2.Reconciled);   // the vanished entry was downgraded
            Assert.Equal(1, r2.Created);      // and re-created, not double-created
            Assert.Single(seam.Live);
        }

        [Fact]
        public void Cleanup_OwnedOnly_RemovesAndDeletesSnapshot()
        {
            var (exec, seam, auth, peers) = Build();
            auth.Admins.Add("owner");
            var bind = peers.Bind("owner");
            var args = Args(("prefab", "piece_workbench"), ("posRadius", 2.0));
            exec.Ensure("fx", "SpawnStation", args, "owner", bind.Generation);
            Assert.Single(seam.Live);
            Assert.True(File.Exists(Path.Combine(_dir, "fx.ledger")));

            var c = exec.Cleanup("fx", "SpawnStation", args, "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.Executed, c.Status);
            Assert.Equal(1, c.Removed);
            Assert.Empty(seam.Live);
            Assert.False(File.Exists(Path.Combine(_dir, "fx.ledger")));
        }

        [Fact]
        public void Cleanup_PreservesUnrelated()
        {
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench" });
            string unrelated = seam.SeedUnmarked("piece_workbench");
            var world = new SeamFixtureWorld(seam);
            var auth = new FakeAuthority(); auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var exec = new ServerFixtureExecutor(auth, peers, world,
                id => new LedgerSnapshotStore(Path.Combine(_dir, id + ".ledger")), TestRun.Ctx);
            var args = Args(("prefab", "piece_workbench"), ("posRadius", 2.0));

            exec.Ensure("fx", "SpawnStation", args, "owner", bind.Generation);
            exec.Cleanup("fx", "SpawnStation", args, "owner", bind.Generation);

            Assert.True(seam.IsLiveInstance(unrelated));
            Assert.Single(seam.Live);
        }

        [Fact]
        public void Cleanup_RejectedByAuthority_LeavesOwnedIntact()
        {
            var (exec, seam, auth, peers) = Build();
            auth.Admins.Add("owner");
            var bind = peers.Bind("owner");
            var args = Args(("prefab", "piece_workbench"), ("posRadius", 2.0));
            exec.Ensure("fx", "SpawnStation", args, "owner", bind.Generation);

            auth.Admins.Clear(); // owner loses admin
            var c = exec.Cleanup("fx", "SpawnStation", args, "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.AuthorityRejected, c.Status);
            Assert.Single(seam.Live); // nothing destroyed
        }
    }

    // ── Responder bridge: deterministic ids + verb routing ─────────────────
    public sealed class FixtureVerbExecutorBridgeTests : IDisposable
    {
        private readonly string _dir;
        public FixtureVerbExecutorBridgeTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sbpr-qa-m3rb-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }
        public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

        private static Dictionary<string, object?> Args(params (string k, object? v)[] kv)
        {
            var d = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (k, v) in kv) d[k] = v;
            return d;
        }

        private FixtureVerbExecutorBridge Build(out FakeVanillaFixtureSeam seam, out FakeAuthority auth, out DeliveringPeerState peers)
        {
            seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench", "Wood" });
            var world = new SeamFixtureWorld(seam);
            auth = new FakeAuthority();
            peers = new DeliveringPeerState();
            var exec = new ServerFixtureExecutor(auth, peers, world,
                id => new LedgerSnapshotStore(Path.Combine(_dir, id + ".ledger")), TestRun.Ctx);
            return new FixtureVerbExecutorBridge(exec);
        }

        [Fact]
        public void Handles_OnlyFixtureVerbsAndCleanup()
        {
            var bridge = Build(out _, out _, out _);
            Assert.True(bridge.Handles("SpawnStation"));
            Assert.True(bridge.Handles("PlaceVanillaPiece"));
            Assert.True(bridge.Handles("GrantVanillaMaterials"));
            Assert.True(bridge.Handles("Cleanup"));
            Assert.False(bridge.Handles("Craft"));
            Assert.False(bridge.Handles("Ping"));
            Assert.False(bridge.Handles(null));
        }

        [Fact]
        public void Execute_Create_Then_CleanupViaScope_RoundTrips()
        {
            var bridge = Build(out var seam, out var auth, out var peers);
            auth.Admins.Add("owner");
            var bind = peers.Bind("owner");

            var create = bridge.Execute("GrantVanillaMaterials", Args(("itemId", "Wood"), ("qty", 2L)), "owner", bind.Generation);
            Assert.True(create.Executed);
            Assert.Equal(2, seam.Live.Count);

            // Cleanup scope encodes the create verb + args so the SAME deterministic ids resolve.
            var cleanup = bridge.Execute("Cleanup",
                Args(("scope", "GrantVanillaMaterials|itemId=Wood;qty=2")), "owner", bind.Generation);
            Assert.True(cleanup.Executed);
            Assert.Empty(seam.Live);
        }

        [Fact]
        public void Execute_Cleanup_MalformedScope_Refused()
        {
            var bridge = Build(out var seam, out var auth, out var peers);
            auth.Admins.Add("owner");
            var bind = peers.Bind("owner");
            var cleanup = bridge.Execute("Cleanup", Args(("scope", "not-a-verb")), "owner", bind.Generation);
            Assert.False(cleanup.Executed);
        }
    }
}
