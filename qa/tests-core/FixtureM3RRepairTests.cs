// ============================================================================
//  QA-M3R repair (t_0e3a88bd) — hostile crash-safe ownership + additive tests.
// ----------------------------------------------------------------------------
//  These are the tests the owner review demanded before PR #414 can go to dual
//  review. They exercise the two repaired defects against the SHIPPED engine-free
//  sources + fakes (so the tested logic IS the shipped logic; the engine-bound
//  seam is thin + compile-only):
//
//   Defect 2 (crash-safe ownership via durable exact markers):
//     * crash immediately after spawn, before snapshot          -> Recovery_AdoptsCrashSurvivor_*
//     * corrupt/unreadable snapshot with one marked survivor     -> Recovery_CorruptSnapshot_*
//     * duplicate markers (two objects, same owned id)           -> Recovery_DuplicateMarker_Refuses
//     * foreign run / foreign world                              -> Recovery_ForeignRun/World_Refuses
//     * same-prefab UNMARKED preservation                        -> Recovery_PreservesUnmarked_*
//     * failed snapshot delete is observable/retryable           -> Cleanup_SnapshotDeleteFailure_*
//     * no duplicate creation after adoption                     -> Recovery_AdoptedSurvivor_NotRecreated
//     * marker write failure is a Create failure (no leak)       -> Ensure_MarkerWriteFailure_*
//     * Corrupt/IoError never treated as empty                   -> Recovery_CorruptSnapshot_NeverEmpty
//     * unexpected resource marker refuses                       -> Recovery_UnexpectedResource_Refuses
//     * malformed marker refuses                                 -> Recovery_MalformedMarker_Refuses
//
//   Defect 1 (true additive construction — ADR-0006, no clone identity):
//     * the seam exposes no clone/instantiate path + shell is additive  -> Additive_*
//     (the engine-bound shell's component set is asserted structurally here;
//      full in-game construction is M6, not verified in headless tests.)
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
    // ── Durable exact-marker crash recovery (fail-closed) ──────────────────
    public sealed class FixtureMarkerRecoveryTests : IDisposable
    {
        private readonly string _dir;

        public FixtureMarkerRecoveryTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sbpr-qa-m3r-rec-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

        private static Dictionary<string, object?> Args(params (string k, object? v)[] kv)
        {
            var d = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (k, v) in kv) d[k] = v;
            return d;
        }

        private static ValidatedFixturePlan Plan(string fixtureId, string id, int count, double radius)
        {
            var allow = VanillaFixtureManifest.BuildAllowlist();
            var plan = new FixturePlan(fixtureId, new[] { new ResourceSpec(id, count, radius) });
            return FixturePlanValidator.Validate(plan, allow, VanillaFixtureManifest.Bounds).Plan!;
        }

        private ServerFixtureExecutor Exec(FakeVanillaFixtureSeam seam, FakeAuthority auth,
            DeliveringPeerState peers, FixtureRunContext ctx)
        {
            var world = new SeamFixtureWorld(seam);
            return new ServerFixtureExecutor(auth, peers, world,
                id => new LedgerSnapshotStore(Path.Combine(_dir, id + ".ledger")), ctx);
        }

        // ── The single-resource plan's owned id (ordinal 0) is deterministic. ──
        private static OwnedResourceId Owned(string fixtureId, string logical, int ordinal = 0) =>
            new OwnedResourceId(fixtureId, logical, ordinal);

        // Crash immediately after spawn, BEFORE the snapshot was written: the object survives with
        // its exact durable marker, no snapshot file exists, and the next run adopts it (no leak,
        // no double-create).
        [Fact]
        public void Recovery_AdoptsCrashSurvivor_NoSnapshot()
        {
            var ctx = TestRun.Ctx;
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench" });
            var plan = Plan("fx", "piece_workbench", 1, 2.0);

            // A crash-before-snapshot survivor: a marked live object, but NO .ledger file on disk.
            var marker = FixtureOwnershipMarker.For(ctx, "fx", Owned("fx", "piece_workbench"));
            seam.SeedMarkedSurvivor("piece_workbench", marker.Encode());
            Assert.False(File.Exists(Path.Combine(_dir, "fx.ledger")));

            var auth = new FakeAuthority(); auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var exec = Exec(seam, auth, peers, ctx);

            var r = exec.Ensure("fx", "SpawnStation", Args(("prefab", "piece_workbench"), ("posRadius", 2.0)), "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.Executed, r.Status);
            Assert.Equal(1, r.Reconciled);   // the survivor was adopted
            Assert.Equal(0, r.Created);      // and NOT re-created (no duplicate)
            Assert.Single(seam.Live);        // still exactly one live object
        }

        // After adoption, a subsequent cleanup removes exactly the adopted survivor (owned-only).
        [Fact]
        public void Recovery_AdoptedSurvivor_CleanedUp_NotRecreated()
        {
            var ctx = TestRun.Ctx;
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench" });
            var marker = FixtureOwnershipMarker.For(ctx, "fx", Owned("fx", "piece_workbench"));
            seam.SeedMarkedSurvivor("piece_workbench", marker.Encode());

            var auth = new FakeAuthority(); auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var exec = Exec(seam, auth, peers, ctx);
            var args = Args(("prefab", "piece_workbench"), ("posRadius", 2.0));

            exec.Ensure("fx", "SpawnStation", args, "owner", bind.Generation);
            var c = exec.Cleanup("fx", "SpawnStation", args, "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.Executed, c.Status);
            Assert.Equal(1, c.Removed);
            Assert.Empty(seam.Live);
        }

        // A corrupt/unreadable snapshot is NEVER treated as empty: a marked survivor is still adopted
        // from world truth (its own marker), not orphaned.
        [Fact]
        public void Recovery_CorruptSnapshot_AdoptsSurvivor_NeverEmpty()
        {
            var ctx = TestRun.Ctx;
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench" });
            // Write a corrupt snapshot file for this fixture id.
            File.WriteAllText(Path.Combine(_dir, "fx.ledger"), "SBPR-QA-FIXLEDGER\t1\tfx\tGARBAGE");

            var marker = FixtureOwnershipMarker.For(ctx, "fx", Owned("fx", "piece_workbench"));
            seam.SeedMarkedSurvivor("piece_workbench", marker.Encode());

            var auth = new FakeAuthority(); auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var exec = Exec(seam, auth, peers, ctx);

            var r = exec.Ensure("fx", "SpawnStation", Args(("prefab", "piece_workbench"), ("posRadius", 2.0)), "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.Executed, r.Status);
            Assert.Equal(1, r.Reconciled);  // adopted from the marker despite the corrupt snapshot
            Assert.Equal(0, r.Created);
            Assert.Single(seam.Live);
        }

        // Two live objects claim the SAME owned id — ambiguous ownership. Recovery refuses (fail-closed,
        // no world side effect), so a corrupted/duplicated world does not silently pick one.
        [Fact]
        public void Recovery_DuplicateMarker_Refuses()
        {
            var ctx = TestRun.Ctx;
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench" });
            var marker = FixtureOwnershipMarker.For(ctx, "fx", Owned("fx", "piece_workbench"));
            seam.SeedMarkedSurvivor("piece_workbench", marker.Encode());
            seam.SeedMarkedSurvivor("piece_workbench", marker.Encode()); // duplicate owned id

            var auth = new FakeAuthority(); auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var exec = Exec(seam, auth, peers, ctx);
            int before = seam.Live.Count;

            var r = exec.Ensure("fx", "SpawnStation", Args(("prefab", "piece_workbench"), ("posRadius", 2.0)), "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.RecoveryRefused, r.Status);
            Assert.Contains("DuplicateMarker", r.Detail);
            Assert.Equal(before, seam.Live.Count); // no world side effect
        }

        // A survivor whose marker names a different RUN nonce is foreign — recovery refuses.
        [Fact]
        public void Recovery_ForeignRun_Refuses()
        {
            var ctx = TestRun.Ctx;
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench" });
            var foreign = new FixtureOwnershipMarker(TestRun.WorldUid, "OTHER-RUN", "fx",
                Owned("fx", "piece_workbench").Canonical);
            seam.SeedMarkedSurvivor("piece_workbench", foreign.Encode());

            var auth = new FakeAuthority(); auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var exec = Exec(seam, auth, peers, ctx);

            var r = exec.Ensure("fx", "SpawnStation", Args(("prefab", "piece_workbench"), ("posRadius", 2.0)), "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.RecoveryRefused, r.Status);
            Assert.Contains("ForeignRun", r.Detail);
        }

        // A survivor whose marker names a different WORLD uid is foreign — recovery refuses.
        [Fact]
        public void Recovery_ForeignWorld_Refuses()
        {
            var ctx = TestRun.Ctx;
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench" });
            var foreign = new FixtureOwnershipMarker(TestRun.WorldUid + 12345, TestRun.Nonce, "fx",
                Owned("fx", "piece_workbench").Canonical);
            seam.SeedMarkedSurvivor("piece_workbench", foreign.Encode());

            var auth = new FakeAuthority(); auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var exec = Exec(seam, auth, peers, ctx);

            var r = exec.Ensure("fx", "SpawnStation", Args(("prefab", "piece_workbench"), ("posRadius", 2.0)), "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.RecoveryRefused, r.Status);
            Assert.Contains("ForeignWorld", r.Detail);
        }

        // A marker for THIS world/run/fixture that names an owned id the current plan does not expect
        // is refused (a plan drift or a hostile marker), never adopted.
        [Fact]
        public void Recovery_UnexpectedResource_Refuses()
        {
            var ctx = TestRun.Ctx;
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench" });
            // Marker names an owned id (ordinal 7) the single-count plan will never expand to.
            var unexpected = FixtureOwnershipMarker.For(ctx, "fx", Owned("fx", "piece_workbench", 7));
            seam.SeedMarkedSurvivor("piece_workbench", unexpected.Encode());

            var auth = new FakeAuthority(); auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var exec = Exec(seam, auth, peers, ctx);

            var r = exec.Ensure("fx", "SpawnStation", Args(("prefab", "piece_workbench"), ("posRadius", 2.0)), "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.RecoveryRefused, r.Status);
            Assert.Contains("UnexpectedResource", r.Detail);
        }

        // A malformed (undecodable) marker payload refuses recovery, never guesses ownership.
        [Fact]
        public void Recovery_MalformedMarker_Refuses()
        {
            var ctx = TestRun.Ctx;
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench" });
            seam.SeedMarkedSurvivor("piece_workbench", "not-a-valid-marker-payload");

            var auth = new FakeAuthority(); auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var exec = Exec(seam, auth, peers, ctx);

            var r = exec.Ensure("fx", "SpawnStation", Args(("prefab", "piece_workbench"), ("posRadius", 2.0)), "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.RecoveryRefused, r.Status);
            Assert.Contains("MalformedMarker", r.Detail);
        }

        // An UNMARKED same-prefab world object is never discovered/adopted/destroyed: recovery ignores
        // it and cleanup preserves it.
        [Fact]
        public void Recovery_PreservesUnmarked_SamePrefab()
        {
            var ctx = TestRun.Ctx;
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench" });
            string unmarked = seam.SeedUnmarked("piece_workbench"); // no marker → invisible to recovery

            var auth = new FakeAuthority(); auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var exec = Exec(seam, auth, peers, ctx);
            var args = Args(("prefab", "piece_workbench"), ("posRadius", 2.0));

            // Fresh ensure creates ITS OWN marked object; the unmarked one is untouched.
            var r = exec.Ensure("fx", "SpawnStation", args, "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.Executed, r.Status);
            Assert.Equal(0, r.Reconciled);   // the unmarked object was never adopted
            Assert.Equal(1, r.Created);

            // Cleanup removes only the harness's owned object; the unmarked one survives.
            exec.Cleanup("fx", "SpawnStation", args, "owner", bind.Generation);
            Assert.True(seam.IsLiveInstance(unmarked));
            Assert.Single(seam.Live);
        }

        // A marker write failure at create time is a Create failure — the object is NOT tracked and
        // NOT left as a silent leak; the entry is Failed and a retry (with marker write restored)
        // succeeds without a duplicate.
        [Fact]
        public void Ensure_MarkerWriteFailure_IsCreateFailure_NoLeak()
        {
            var ctx = TestRun.Ctx;
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench" });
            seam.FailMarkerWrite = true;

            var auth = new FakeAuthority(); auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var exec = Exec(seam, auth, peers, ctx);
            var args = Args(("prefab", "piece_workbench"), ("posRadius", 2.0));

            var first = exec.Ensure("fx", "SpawnStation", args, "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.Executed, first.Status);
            Assert.Equal(0, first.Created);
            Assert.Equal(1, first.Failed);     // marker-write failure => Create failure
            Assert.Empty(seam.Live);           // no untracked leak

            seam.FailMarkerWrite = false;
            var second = exec.Ensure("fx", "SpawnStation", args, "owner", bind.Generation);
            Assert.Equal(1, second.Created);   // retried and created exactly once
            Assert.Single(seam.Live);
        }

        // A snapshot delete failure after full cleanup is OBSERVABLE (not swallowed): cleanup reports
        // SnapshotDeleteFailed with the removed counts so the caller can retry.
        [Fact]
        public void Cleanup_SnapshotDeleteFailure_IsObservable()
        {
            var ctx = TestRun.Ctx;
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench" });
            var auth = new FakeAuthority(); auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");

            // A store whose snapshot path is a DIRECTORY the process cannot File.Delete → delete fails.
            string fixtureId = "fx";
            var world = new SeamFixtureWorld(seam);
            var exec = new ServerFixtureExecutor(auth, peers, world,
                id => new UndeletableSnapshotStore(Path.Combine(_dir, id + ".ledger")), ctx);
            var args = Args(("prefab", "piece_workbench"), ("posRadius", 2.0));

            exec.Ensure(fixtureId, "SpawnStation", args, "owner", bind.Generation);
            Assert.Single(seam.Live);

            var c = exec.Cleanup(fixtureId, "SpawnStation", args, "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.SnapshotDeleteFailed, c.Status);
            Assert.Equal(1, c.Removed);         // the world object WAS removed
            Assert.Empty(seam.Live);            // cleanup itself succeeded
            Assert.Contains("could not be deleted", c.Detail);
        }
    }

    // A snapshot store whose Delete always reports failure (models an undeletable durable file).
    internal sealed class UndeletableSnapshotStore : LedgerSnapshotStore
    {
        public UndeletableSnapshotStore(string path) : base(path) { }
        public override bool Delete() => false;
    }

    // ── Marker codec round-trip + fail-closed decode (pure) ────────────────
    public sealed class FixtureOwnershipMarkerCodecTests
    {
        [Fact]
        public void Encode_Decode_RoundTrips()
        {
            var m = new FixtureOwnershipMarker(42, "nonce-x", "fx-1", "fx-1/piece_workbench#0");
            Assert.True(FixtureOwnershipMarker.TryDecode(m.Encode(), out var back));
            Assert.Equal(m, back);
        }

        [Fact]
        public void Decode_RoundTrips_OddCharacters()
        {
            // Fields with the framing separator / backslashes must survive escaping.
            var m = new FixtureOwnershipMarker(7, "run\\with\\slash", "fx\ttab", "id#0");
            Assert.True(FixtureOwnershipMarker.TryDecode(m.Encode(), out var back));
            Assert.Equal(m, back);
        }

        [Theory]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("SBPRQA-OWN")]           // magic only, no fields
        [InlineData("WRONG\u241F1\u241F1\u241Fn\u241Ff\u241Fr")] // bad magic
        public void Decode_Rejects_Malformed(string payload)
        {
            Assert.False(FixtureOwnershipMarker.TryDecode(payload, out _));
        }
    }

    // ── Additive construction (ADR-0006) — no clone identity path ──────────
    public sealed class FixtureAdditiveConstructionTests
    {
        // The engine-bound seam interface exposes only additive Spawn/Grant/Despawn/Discover — there
        // is NO Clone/Instantiate/Strip/Mutate method reachable, so a subtractive clone-and-strip
        // base cannot be produced through it (structural ADR-0006 guarantee).
        [Fact]
        public void Seam_ExposesNoClonePath()
        {
            var methods = typeof(IVanillaFixtureSeam).GetMethods().Select(m => m.Name).ToArray();
            Assert.Contains("SpawnPrefab", methods);
            Assert.Contains("GrantItem", methods);
            Assert.Contains("DiscoverMarked", methods);
            Assert.DoesNotContain(methods, m =>
                m.Contains("Clone") || m.Contains("Instantiate") || m.Contains("Strip") || m.Contains("Mutate"));
        }

        // The world seam Create requires an ownership marker — a spawn cannot happen without a durable
        // marker being handed to the seam (enforced by the type signature).
        [Fact]
        public void WorldCreate_RequiresMarker()
        {
            var createParams = typeof(IFixtureWorld).GetMethod("Create")!.GetParameters();
            Assert.Contains(createParams, p => p.ParameterType == typeof(FixtureOwnershipMarker));
        }
    }
}
