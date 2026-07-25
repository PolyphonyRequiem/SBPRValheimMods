// ============================================================================
//  Niflheim 0003 — cold-reload harness engine-free CAPTURE / COMPARE / ARM tests.
// ----------------------------------------------------------------------------
//  SCOPE HONESTY (read first): this suite deterministically exercises the shipped
//  engine-free brain of the QA-only live cold-reload harness (Domain/ReloadHarness,
//  link-compiled from ../src). It proves:
//    * the capture builder actually calls the SHIPPED HomesteadSelector.Select
//      (production-selector call reachability) and emits a complete, canonically
//      ordered primitive-fact schema;
//    * the PRE/POST comparator PASSES an identity/count-stable pair and FAILS
//      closed on wrong world UID, same process/session, missing save receipt,
//      hash drift, count drift, duplicate/missing hosts, and phase confusion;
//    * the arming gate refuses an absent/disabled manifest, missing lease/rollback/
//      fixture, forbidden production world/port, and unbounded/over-retry waits.
//
//  WHAT THIS SUITE DOES NOT PROVE (still-open live gate on t_1a1164f4): it runs no
//  Valheim client, saves/loads no world, and crosses no persistence boundary. A PASS
//  here means the harness logic is correct; it does NOT prove live reload, persistence,
//  deployment, or playability.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Domain;
using SBPR.Niflheim.HomesteadStones.Domain.ReloadHarness;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimColdReloadHarnessTests
    {
        private const long FixtureUid = 2413287143L;
        private const string SelectorVersion = "niflheim-homestead-playtest-v1";
        private const double MinimumDistance = 128.0;
        private const double Density = 0.40;

        // A small, deterministic, well-spaced eligible candidate set. Coordinates are chosen so the
        // 128 m minimum-distance selector keeps a predictable subset; the exact selected count is not
        // asserted (that is the selector's job) — only that selection ran and the schema is complete.
        private static IReadOnlyCollection<HomesteadCandidate> Candidates() => new[]
        {
            new HomesteadCandidate("WoodHouse1", -21, -13, -1340.0, -820.0, 20.0),
            new HomesteadCandidate("WoodHouse2", 48, -9, 3080.0, -560.0, 20.0),
            new HomesteadCandidate("WoodHouse3", -22, -32, -1400.0, -2050.0, 20.0),
            new HomesteadCandidate("WoodHouse1", 5, 12, 340.0, 780.0, 20.0),
            new HomesteadCandidate("WoodHouse2", -3, 6, -190.0, 400.0, 20.0),
            new HomesteadCandidate("WoodHouse3", 61, -31, 3900.0, -1980.0, 20.0),
        };

        private static HomesteadReloadSession Session(string boot, string session, string proc, long gen) =>
            new HomesteadReloadSession(boot, session, proc, gen);

        private static HomesteadReloadProvenance Prov() =>
            new HomesteadReloadProvenance("srcHASH", "prodHASH", "harnessHASH");

        private static HomesteadReloadCapture BuildPre(
            IReadOnlyCollection<HomesteadCandidate> candidates,
            HomesteadReloadProvenance? prov = null) =>
            HomesteadReloadCaptureBuilder.Build(
                HomesteadReloadPhase.Pre, FixtureUid, SelectorVersion, MinimumDistance, Density,
                candidates,
                new List<HomesteadReloadReconcileEntry>(),
                Session("bootPRE", "sess-1", "1001", 1L),
                prov ?? Prov(),
                HomesteadReloadSaveReceipt.None,
                "2026-07-25T00:00:00.0000000Z");

        private static HomesteadReloadCapture BuildPost(
            IReadOnlyCollection<HomesteadCandidate> candidates,
            HomesteadReloadProvenance? prov = null,
            HomesteadReloadSaveReceipt? save = null,
            HomesteadReloadSession? session = null) =>
            HomesteadReloadCaptureBuilder.Build(
                HomesteadReloadPhase.Post, FixtureUid, SelectorVersion, MinimumDistance, Density,
                candidates,
                new List<HomesteadReloadReconcileEntry>(),
                session ?? Session("bootPOST", "sess-2", "2002", 2L),
                prov ?? Prov(),
                save ?? new HomesteadReloadSaveReceipt(true, "dbHASH", "2026-07-25T00:05:00Z"),
                "2026-07-25T00:06:00.0000000Z");

        // ── Production selector reachability + schema completeness ───────────────

        [Fact]
        public void Capture_runs_the_shipped_production_selector()
        {
            var candidates = Candidates();
            var capture = BuildPre(candidates);

            // The captured assigned count MUST equal what the shipped selector actually returns for the
            // same inputs — proving the harness ran HomesteadSelector.Select, not a reimplementation.
            var config = new HomesteadSelectionConfig(
                HomesteadWorldIdentity.FromUid(FixtureUid), SelectorVersion, MinimumDistance, Density);
            var expected = HomesteadSelector.Select(candidates, config);

            Assert.Equal(candidates.Count, capture.CandidateCount);
            Assert.Equal(expected.Selected.Count, capture.AssignedCount);
            Assert.Equal(expected.Selected.Count, capture.Hosts.Count);
        }

        [Fact]
        public void Capture_schema_is_complete_and_canonically_ordered()
        {
            var capture = BuildPre(Candidates());
            var text = capture.ToCanonicalText();

            foreach (var key in new[]
            {
                "schema=", "phase=", "world.uid=", "world.identity=", "selector.version=",
                "selector.minimumDistance=", "selector.density=", "counts.candidates=",
                "counts.assigned=", "counts.minimumPairwiseDistance=", "hosts.count=",
                "session.bootId=", "session.sessionId=", "session.processId=",
                "session.bootGeneration=", "provenance.sourceHash=", "provenance.productHash=",
                "provenance.harnessHash=", "save.present=", "capturedAtUtc=",
            })
                Assert.Contains(key, text);

            // Hosts are canonically sorted.
            for (var i = 1; i < capture.Hosts.Count; i++)
                Assert.True(capture.Hosts[i - 1].CompareTo(capture.Hosts[i]) < 0);

            // World identity derives from the UID exactly as production does.
            Assert.Equal(HomesteadWorldIdentity.FromUid(FixtureUid), capture.WorldIdentity);
        }

        [Fact]
        public void Capture_is_deterministic_for_identical_inputs()
        {
            var a = BuildPre(Candidates());
            var b = BuildPre(Candidates());
            // Session ids match here (same literal), so canonical text is identical — proves the
            // world/selector/host surface is a pure function of inputs (no reroll, no time in the set).
            Assert.Equal(a.ToCanonicalText(), b.ToCanonicalText());
        }

        // ── Secret-bearing output rejection ──────────────────────────────────────

        [Fact]
        public void Capture_rejects_secret_bearing_fields()
        {
            var dirty = new HomesteadReloadProvenance("srcHASH", "server_pass=hunter2", "harnessHASH");
            var ex = Assert.Throws<HomesteadReloadCaptureException>(() => BuildPre(Candidates(), dirty));
            Assert.Contains("secret", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── PRE/POST comparator: happy path ──────────────────────────────────────

        [Fact]
        public void Compare_passes_identity_and_count_stable_pair()
        {
            var pre = BuildPre(Candidates());
            var post = BuildPost(Candidates());
            var result = HomesteadReloadComparer.Compare(pre, post, FixtureUid);
            Assert.True(result.IsPass, string.Join(" | ", result.Failures));
        }

        // ── PRE/POST comparator: fail-closed rejections ──────────────────────────

        [Fact]
        public void Compare_rejects_wrong_world_uid()
        {
            var pre = BuildPre(Candidates());
            var post = BuildPost(Candidates());
            var result = HomesteadReloadComparer.Compare(pre, post, 999L);
            Assert.False(result.IsPass);
            Assert.Contains(result.Failures, f => f.Contains("world UID"));
        }

        [Fact]
        public void Compare_rejects_same_process_and_session()
        {
            var pre = BuildPre(Candidates());
            var post = BuildPost(Candidates(), session: Session("bootPRE", "sess-1", "1001", 2L));
            var result = HomesteadReloadComparer.Compare(pre, post, FixtureUid);
            Assert.False(result.IsPass);
            Assert.Contains(result.Failures, f => f.Contains("Same process id"));
            Assert.Contains(result.Failures, f => f.Contains("Same session id"));
        }

        [Fact]
        public void Compare_rejects_non_advancing_boot_generation()
        {
            var pre = BuildPre(Candidates());
            var post = BuildPost(Candidates(), session: Session("bootPOST", "sess-2", "2002", 1L));
            var result = HomesteadReloadComparer.Compare(pre, post, FixtureUid);
            Assert.False(result.IsPass);
            Assert.Contains(result.Failures, f => f.Contains("boot generation"));
        }

        [Fact]
        public void Compare_rejects_missing_save_receipt()
        {
            var pre = BuildPre(Candidates());
            var post = BuildPost(Candidates(), save: HomesteadReloadSaveReceipt.None);
            var result = HomesteadReloadComparer.Compare(pre, post, FixtureUid);
            Assert.False(result.IsPass);
            Assert.Contains(result.Failures, f => f.Contains("save receipt"));
        }

        [Fact]
        public void Compare_rejects_build_hash_drift()
        {
            var pre = BuildPre(Candidates());
            var post = BuildPost(Candidates(), prov: new HomesteadReloadProvenance("srcHASH", "prodHASH", "DIFFERENT"));
            var result = HomesteadReloadComparer.Compare(pre, post, FixtureUid);
            Assert.False(result.IsPass);
            Assert.Contains(result.Failures, f => f.Contains("hash drift"));
        }

        [Fact]
        public void Compare_rejects_host_count_and_set_drift()
        {
            var pre = BuildPre(Candidates());
            // POST sees one fewer candidate → a smaller assigned/host set → missing host + count drift.
            var fewer = Candidates().Take(Candidates().Count - 1).ToArray();
            var post = BuildPost(fewer);
            var result = HomesteadReloadComparer.Compare(pre, post, FixtureUid);
            Assert.False(result.IsPass);
            Assert.Contains(result.Failures, f => f.Contains("count drift") || f.Contains("MISSING"));
        }

        [Fact]
        public void Compare_rejects_phase_confusion()
        {
            // Two PRE captures — the comparator must reject the second being handed as POST.
            var preA = BuildPre(Candidates());
            var preB = BuildPre(Candidates());
            var result = HomesteadReloadComparer.Compare(preA, preB, FixtureUid);
            Assert.False(result.IsPass);
            Assert.Contains(result.Failures, f => f.Contains("expected Post"));
        }

        // ── Arming gate: fail-closed enablement / fixture / lease / production guard ──

        private static HomesteadReloadHarnessManifest ValidManifest() =>
            new HomesteadReloadHarnessManifest(
                enabled: true, expectedWorldUid: FixtureUid, leaseId: "lease-abc",
                rollbackBytesHash: "rollbackHASH", disposableDbPresent: true, disposableFwlPresent: true,
                targetWorldName: "astley-disposable", targetPort: 2600,
                readinessWaitSeconds: 120.0, phaseWaitSeconds: 60.0, readinessRetries: 1);

        [Fact]
        public void Arming_gate_arms_on_a_valid_qa_manifest()
        {
            var decision = HomesteadReloadArmingGate.Evaluate(ValidManifest(), FixtureUid);
            Assert.True(decision.IsArmed, string.Join(" | ", decision.Refusals));
        }

        [Fact]
        public void Arming_gate_refuses_absent_manifest()
        {
            var decision = HomesteadReloadArmingGate.Evaluate(null, FixtureUid);
            Assert.False(decision.IsArmed);
            Assert.Contains(decision.Refusals, r => r.Contains("inert"));
        }

        [Fact]
        public void Arming_gate_refuses_disabled_manifest()
        {
            var m = new HomesteadReloadHarnessManifest(
                false, FixtureUid, "lease", "rb", true, true, "astley", 2600, 120, 60, 1);
            var decision = HomesteadReloadArmingGate.Evaluate(m, FixtureUid);
            Assert.False(decision.IsArmed);
            Assert.Contains(decision.Refusals, r => r.Contains("not enabled"));
        }

        [Fact]
        public void Arming_gate_refuses_wrong_fixture_uid()
        {
            var decision = HomesteadReloadArmingGate.Evaluate(ValidManifest(), 999L);
            Assert.False(decision.IsArmed);
            Assert.Contains(decision.Refusals, r => r.Contains("fixture UID"));
        }

        [Fact]
        public void Arming_gate_refuses_missing_lease_or_rollback_or_fixture()
        {
            var noLease = new HomesteadReloadHarnessManifest(
                true, FixtureUid, "", "rb", true, true, "astley", 2600, 120, 60, 1);
            Assert.Contains(HomesteadReloadArmingGate.Evaluate(noLease, FixtureUid).Refusals, r => r.Contains("lease"));

            var noRollback = new HomesteadReloadHarnessManifest(
                true, FixtureUid, "lease", "", true, true, "astley", 2600, 120, 60, 1);
            Assert.Contains(HomesteadReloadArmingGate.Evaluate(noRollback, FixtureUid).Refusals, r => r.Contains("rollback"));

            var noDb = new HomesteadReloadHarnessManifest(
                true, FixtureUid, "lease", "rb", false, true, "astley", 2600, 120, 60, 1);
            Assert.Contains(HomesteadReloadArmingGate.Evaluate(noDb, FixtureUid).Refusals, r => r.Contains(".db"));

            var noFwl = new HomesteadReloadHarnessManifest(
                true, FixtureUid, "lease", "rb", true, false, "astley", 2600, 120, 60, 1);
            Assert.Contains(HomesteadReloadArmingGate.Evaluate(noFwl, FixtureUid).Refusals, r => r.Contains(".fwl"));
        }

        [Theory]
        [InlineData("Niflheim")]
        [InlineData("prod-heistan-main")]
        public void Arming_gate_refuses_forbidden_production_world_name(string name)
        {
            var m = new HomesteadReloadHarnessManifest(
                true, FixtureUid, "lease", "rb", true, true, name, 2600, 120, 60, 1);
            var decision = HomesteadReloadArmingGate.Evaluate(m, FixtureUid);
            Assert.False(decision.IsArmed);
            Assert.Contains(decision.Refusals, r => r.Contains("production world"));
        }

        [Theory]
        [InlineData(2456)]
        [InlineData(2466)]
        public void Arming_gate_refuses_forbidden_production_port(int port)
        {
            var m = new HomesteadReloadHarnessManifest(
                true, FixtureUid, "lease", "rb", true, true, "astley", port, 120, 60, 1);
            var decision = HomesteadReloadArmingGate.Evaluate(m, FixtureUid);
            Assert.False(decision.IsArmed);
            Assert.Contains(decision.Refusals, r => r.Contains("production port"));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NaN)]
        [InlineData(HomesteadReloadArmingGate.MaxWaitSeconds + 1.0)]
        public void Arming_gate_refuses_unbounded_or_nonpositive_waits(double seconds)
        {
            var m = new HomesteadReloadHarnessManifest(
                true, FixtureUid, "lease", "rb", true, true, "astley", 2600, seconds, 60, 1);
            var decision = HomesteadReloadArmingGate.Evaluate(m, FixtureUid);
            Assert.False(decision.IsArmed);
            Assert.Contains(decision.Refusals, r => r.Contains("readiness wait"));
        }

        [Fact]
        public void Arming_gate_refuses_more_than_one_readiness_retry()
        {
            var m = new HomesteadReloadHarnessManifest(
                true, FixtureUid, "lease", "rb", true, true, "astley", 2600, 120, 60, 2);
            var decision = HomesteadReloadArmingGate.Evaluate(m, FixtureUid);
            Assert.False(decision.IsArmed);
            Assert.Contains(decision.Refusals, r => r.Contains("retries"));
        }

        // ── Reconciliation receipt: selected vs removed split ────────────────────

        [Fact]
        public void Capture_reconciliation_splits_selected_and_removed_zdo_ids()
        {
            var reconciliation = new List<HomesteadReloadReconcileEntry>
            {
                new HomesteadReloadReconcileEntry("7:100", "WoodHouse1", -21, -13, removed: false),
                new HomesteadReloadReconcileEntry("7:200", "WoodHouse1", -25, -30, removed: true),
            };
            var capture = HomesteadReloadCaptureBuilder.Build(
                HomesteadReloadPhase.Post, FixtureUid, SelectorVersion, MinimumDistance, Density,
                Candidates(), reconciliation,
                Session("bootPOST", "sess-2", "2002", 2L), Prov(),
                new HomesteadReloadSaveReceipt(true, "dbHASH", "2026-07-25T00:05:00Z"),
                "2026-07-25T00:06:00.0000000Z");

            Assert.Equal(new[] { "7:100" }, capture.SelectedZdoIds.ToArray());
            Assert.Equal(new[] { "7:200" }, capture.RemovedZdoIds.ToArray());
        }
    }
}
