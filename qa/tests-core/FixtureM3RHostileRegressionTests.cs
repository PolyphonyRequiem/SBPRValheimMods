// ============================================================================
//  QA-M3R hostile regression suite (t_9b60ea0c) — the review-demanded gaps.
// ----------------------------------------------------------------------------
//  The M3R repair (PR #414) already ships broad hostile coverage in
//  FixtureM3RRepairTests / FixtureM3RAdapterTests / FixtureM3IntegrationTests.
//  This file adds the THREE hostile cases the card enumerates that those suites
//  did not yet pin, each as a RED-against-pre-repair / GREEN-against-repair
//  regression:
//
//    1. UNREADABLE (I/O-error) snapshot with one marked survivor. Distinct from a
//       Corrupt snapshot (which decodes-and-fails): here the file EXISTS but the
//       read itself throws (SnapshotLoadStatus.IoError). The pre-repair behavior
//       that treated any non-Ok load as "empty" would orphan the survivor; the
//       repair adopts it from its durable marker instead. -> Recovery_IoErrorSnapshot_*
//
//    2. Snapshot-delete failure with OBSERVABLE RETRY that then SUCCEEDS. The
//       existing Cleanup_SnapshotDeleteFailure_IsObservable proves the failure is
//       surfaced; this proves the load-bearing follow-through — a caller that
//       retries after the durable file becomes deletable gets a clean Executed
//       result with the snapshot gone, and no owned object is recreated or leaked
//       across the retry. -> Cleanup_SnapshotDeleteFailure_RetrySucceeds
//
//    3. STRUCTURAL additive-shell conformance (ADR-0006) for the engine-bound
//       ZNetVanillaFixtureSeam. The net8 headless test project cannot link-compile
//       the UnityEngine-referencing seam, so — following the repo's established
//       source-conformance convention (NiflheimRecipeDataPairAccessGuardTests) —
//       these read the SHIPPED seam source directly and assert the additive
//       guarantees: the shell starts from a NEW inactive GameObject, AddComponents
//       ONLY the allowlisted intended components, copies blueprint VALUE fields,
//       fails closed when the blueprint is missing, and NEVER Instantiates a
//       vanilla ZNetView donor (no cloned vanilla instance identity). These fail
//       RED against the pre-repair source (which Instantiate'd the vanilla prefab)
//       and GREEN against the repaired additive source. -> AdditiveShellSource_*
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SBPR.QaHarness.T022.Core.ControlPlane;
using SBPR.QaHarness.T022.Core.Fixtures;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    // ── 1 + 2: unreadable snapshot recovery + delete-failure retry ─────────
    public sealed class FixtureHostileRecoveryTests : IDisposable
    {
        private readonly string _dir;

        public FixtureHostileRecoveryTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sbpr-qa-m3r-hostile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

        private static Dictionary<string, object?> Args(params (string k, object? v)[] kv)
        {
            var d = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (k, v) in kv) d[k] = v;
            return d;
        }

        private static OwnedResourceId Owned(string fixtureId, string logical, int ordinal = 0) =>
            new OwnedResourceId(fixtureId, logical, ordinal);

        // A snapshot store whose Load ALWAYS reports an I/O fault (the file exists but the read
        // itself fails) — distinct from a Corrupt (decoded-and-rejected) snapshot. Models a
        // permission/handle/hardware read error on the durable ledger.
        private sealed class IoErrorSnapshotStore : LedgerSnapshotStore
        {
            public IoErrorSnapshotStore(string path) : base(path) { }
            public override SnapshotLoadResult Load() =>
                SnapshotLoadResult.IoError("injected unreadable-snapshot I/O fault");
        }

        // An UNREADABLE (I/O-error) snapshot is NEVER treated as empty: a marked survivor is still
        // adopted from world truth (its own durable marker), not orphaned. This is the sibling of the
        // corrupt-snapshot case for the OTHER fail-closed load status (IoError, not Corrupt).
        [Fact]
        public void Recovery_IoErrorSnapshot_AdoptsSurvivor_NeverEmpty()
        {
            var ctx = TestRun.Ctx;
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench" });
            var marker = FixtureOwnershipMarker.For(ctx, "fx", Owned("fx", "piece_workbench"));
            seam.SeedMarkedSurvivor("piece_workbench", marker.Encode());

            var auth = new FakeAuthority(); auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var world = new SeamFixtureWorld(seam);
            // Force the snapshot load to fail with IoError specifically.
            var exec = new ServerFixtureExecutor(auth, peers, world,
                id => new IoErrorSnapshotStore(Path.Combine(_dir, id + ".ledger")), ctx);

            var r = exec.Ensure("fx", "SpawnStation", Args(("prefab", "piece_workbench"), ("posRadius", 2.0)), "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.Executed, r.Status);
            Assert.Equal(1, r.Reconciled);  // adopted from the marker despite the unreadable snapshot
            Assert.Equal(0, r.Created);     // NOT re-created (would be a duplicate)
            Assert.Single(seam.Live);
        }

        // A snapshot store whose Delete fails ONLY while a latch is set (models a transient
        // undeletable durable file that becomes deletable on a later attempt).
        private sealed class LatchedUndeletableStore : LedgerSnapshotStore
        {
            public bool BlockDelete { get; set; } = true;
            public LatchedUndeletableStore(string path) : base(path) { }
            public override bool Delete() => BlockDelete ? false : base.Delete();
        }

        // Snapshot-delete failure is OBSERVABLE (SnapshotDeleteFailed) AND retryable to a clean
        // result: after the durable file becomes deletable, a retry cleanup returns Executed with the
        // snapshot gone — no owned object is recreated and none leaks across the retry.
        [Fact]
        public void Cleanup_SnapshotDeleteFailure_RetrySucceeds()
        {
            var ctx = TestRun.Ctx;
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench" });
            var auth = new FakeAuthority(); auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var world = new SeamFixtureWorld(seam);

            // One shared latched store instance per fixture id so Ensure and both Cleanups use it.
            string path = Path.Combine(_dir, "fx.ledger");
            var store = new LatchedUndeletableStore(path);
            var exec = new ServerFixtureExecutor(auth, peers, world, id => store, ctx);
            var args = Args(("prefab", "piece_workbench"), ("posRadius", 2.0));

            exec.Ensure("fx", "SpawnStation", args, "owner", bind.Generation);
            Assert.Single(seam.Live);
            Assert.True(File.Exists(path));

            // First cleanup: world object removed, but the durable snapshot delete fails -> observable.
            var first = exec.Cleanup("fx", "SpawnStation", args, "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.SnapshotDeleteFailed, first.Status);
            Assert.Equal(1, first.Removed);
            Assert.Empty(seam.Live);          // world cleanup itself succeeded
            Assert.True(File.Exists(path));   // the durable ledger still persists (the reported leak)

            // The delete condition clears; a retry cleanup now completes cleanly with the snapshot gone.
            store.BlockDelete = false;
            var second = exec.Cleanup("fx", "SpawnStation", args, "owner", bind.Generation);
            Assert.Equal(FixtureExecStatus.Executed, second.Status);
            Assert.Equal(0, second.Removed);   // nothing to remove the second time (no re-create)
            Assert.Empty(seam.Live);           // still no leak
            Assert.False(File.Exists(path));   // durable snapshot finally deleted
        }
    }

    // ── 3: structural additive-shell conformance (ADR-0006) via source scan ─
    //  The engine-bound ZNetVanillaFixtureSeam cannot be link-compiled into this
    //  net8 headless project (it references UnityEngine/Valheim), so — mirroring
    //  the repo's NiflheimRecipeDataPairAccessGuardTests source-conformance
    //  convention — these read the SHIPPED seam source and assert the additive
    //  construction guarantees against the known member shape.
    public sealed class AdditiveShellSourceConformanceTests
    {
        private const string SeamRelPath =
            "qa/SBPR.QaHarness.T022/Runtime/ZNetVanillaFixtureSeam.cs";

        private static string SeamSource()
        {
            var full = Path.Combine(RepoRoot(), SeamRelPath);
            Assert.True(File.Exists(full), "shipped seam source not found: " + full);
            return StripComments(File.ReadAllText(full));
        }

        // Strip // line comments and /* */ block comments so structural assertions target real CODE,
        // not the file's prose (which deliberately describes the FORBIDDEN pre-repair patterns).
        private static string StripComments(string src)
        {
            // Block comments first, then line comments. String literals in this seam never contain
            // "//" or "/*", so a lexer-free strip is safe here.
            src = Regex.Replace(src, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            var sb = new System.Text.StringBuilder(src.Length);
            foreach (var line in src.Split('\n'))
            {
                int c = line.IndexOf("//", StringComparison.Ordinal);
                sb.Append(c >= 0 ? line.Substring(0, c) : line).Append('\n');
            }
            return sb.ToString();
        }

        // Only the ONLY method body that constructs a shell (BuildStationShell) — so assertions about
        // "no Instantiate here" target construction, not the separate spawn-of-our-own-shell step.
        private static string BuildStationShellBody(string src)
        {
            int idx = src.IndexOf("GameObject? BuildStationShell", StringComparison.Ordinal);
            Assert.True(idx >= 0, "BuildStationShell method not found in seam source");
            // From the signature to the next top-level 'private static' member (the method boundary).
            int next = src.IndexOf("private static", idx + 40, StringComparison.Ordinal);
            if (next < 0) next = src.Length;
            return src.Substring(idx, next - idx);
        }

        // The shell starts from a NEW inactive GameObject (additive), not an Instantiate of a donor.
        [Fact]
        public void AdditiveShellSource_StartsFromNewGameObject_NotInstantiate()
        {
            var body = BuildStationShellBody(SeamSource());
            Assert.Matches(new Regex(@"new\s+GameObject\s*\("), body);
            // The pre-repair defect: BuildStationShell / the station path Instantiate'd the vanilla
            // prefab (a ZNetView donor clone). Construction must contain NO Instantiate.
            Assert.DoesNotContain("Instantiate", body);
        }

        // The shell AddComponents ONLY the allowlisted intended components (ZNetView, BoxCollider,
        // CraftingStation) — no other component types are added during construction.
        [Fact]
        public void AdditiveShellSource_AddsOnlyAllowlistedComponents()
        {
            var body = BuildStationShellBody(SeamSource());
            var allowed = new HashSet<string>(StringComparer.Ordinal)
            {
                "ZNetView", "BoxCollider", "CraftingStation",
            };
            var added = Regex.Matches(body, @"AddComponent<\s*([A-Za-z0-9_]+)\s*>")
                .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            Assert.NotEmpty(added);
            foreach (var comp in added)
                Assert.True(allowed.Contains(comp),
                    "additive shell AddComponent<" + comp + "> is not on the intended allowlist {ZNetView, BoxCollider, CraftingStation}");
            // The ZNetView networked identity must be one of the intended components.
            Assert.Contains("ZNetView", added);
        }

        // The station's blueprint VALUE fields are copied by reference-value (not inherited by cloning):
        // the shell reads m_name/m_useDistance off the blueprint's CraftingStation and assigns them to
        // its OWN component.
        [Fact]
        public void AdditiveShellSource_CopiesBlueprintValueFields()
        {
            var body = BuildStationShellBody(SeamSource());
            Assert.Matches(new Regex(@"station\.m_name\s*=\s*blueprintStation\.m_name"), body);
            Assert.Matches(new Regex(@"station\.m_useDistance\s*=\s*blueprintStation\.m_useDistance"), body);
        }

        // The shell is constructed under the INACTIVE holder (SetActive(false)) so no Awake fires
        // during construction (the product's GetHolder/TryConstructPieceShell discipline).
        [Fact]
        public void AdditiveShellSource_UsesInactiveHolder()
        {
            var src = SeamSource();
            Assert.Matches(new Regex(@"_holder\s*\.\s*SetActive\s*\(\s*false\s*\)"), src);
            var body = BuildStationShellBody(src);
            Assert.Contains("Holder()", body);   // the shell is parented under the inactive holder
        }

        // FAIL-CLOSED on missing/drifted blueprint data: the spawn path returns empty (a Create
        // failure) when the blueprint prefab cannot be read, rather than proceeding with a partial
        // shell. Asserted on the SpawnPrefab body (the blueprint read + guard).
        [Fact]
        public void AdditiveShellSource_FailsClosed_OnMissingBlueprint()
        {
            var src = SeamSource();
            int idx = src.IndexOf("public string SpawnPrefab", StringComparison.Ordinal);
            Assert.True(idx >= 0, "SpawnPrefab not found");
            int next = src.IndexOf("public string GrantItem", idx, StringComparison.Ordinal);
            string spawn = src.Substring(idx, (next < 0 ? src.Length : next) - idx);
            // A null blueprint from GetPrefab returns empty (fail-closed), never proceeds.
            Assert.Matches(new Regex(@"blueprint\s*==\s*null\s*\)\s*return\s+string\.Empty"), spawn);
            // The marker read-back gate also fails closed (no untracked leak): a failed StampMarker
            // destroys the half-built instance and returns empty.
            Assert.Contains("StampMarker", spawn);
            Assert.Matches(new Regex(@"DestroyInstance\s*\(\s*instance\s*\)\s*;\s*return\s+string\.Empty"), spawn);
        }

        // NO cloned vanilla instance identity: the seam never Instantiates the vanilla blueprint prefab
        // as a mutable base. The ONLY Instantiate call in the file is of OUR OWN registered shell
        // (Instantiate(shell, ...)), never Instantiate(prefab/blueprint, ...).
        [Fact]
        public void AdditiveShellSource_NoClonedVanillaInstanceIdentity()
        {
            var src = SeamSource();
            var instantiations = Regex.Matches(src, @"Instantiate\s*\(\s*([A-Za-z0-9_]+)")
                .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            // There is exactly one Instantiate and it targets our own additive shell, not the donor.
            Assert.All(instantiations, target =>
                Assert.True(target == "shell",
                    "Instantiate(" + target + ", ...) clones a non-shell object; the additive seam must only Instantiate its OWN 'shell', never a vanilla ZNetView donor (blueprint/prefab)."));
            Assert.Contains("shell", instantiations);
            // Defence in depth: the blueprint is only ever read via GetPrefab (an asset read, no Awake),
            // never Instantiated.
            Assert.DoesNotContain("Instantiate(blueprint", src.Replace(" ", ""));
            Assert.DoesNotContain("Instantiate(prefab", src.Replace(" ", ""));
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, SeamRelPath.Replace('/', Path.DirectorySeparatorChar))))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate repo root (" + SeamRelPath + ") from " + AppContext.BaseDirectory);
        }
    }
}
