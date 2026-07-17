using System;
using System.IO;
using System.Linq;
using StoneContent.Workbench.Core;
using StoneContent.Workbench.Core.Changes;
using StoneContent.Workbench.Core.Serialization;
using StoneContent.Workbench.Core.Validation;
using StoneContent.Workbench.Web;
using Xunit;

namespace StoneContent.Workbench.Tests
{
    // Web adapter contract tests (POC UI card t_e4d16b1c). They exercise WorkbenchService — the same
    // core-backed logic the HTTP endpoints call — WITHOUT spinning Kestrel, so they run fast and
    // headless in CI. Each test copies the canonical asset into a temp "granted asset root" plus a temp
    // scratch root, exactly mirroring the startup-granted-roots model, and asserts that authority stays
    // in the core: the adapter only shapes/writes, never decides.
    public sealed class WebContractTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _assetPath;
        private readonly string _scratch;
        private readonly string _canonical;
        private readonly string _hash;
        private readonly WorkbenchService _svc;

        public WebContractTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "scw-webtest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _assetPath = Path.Combine(_dir, "homestead-stone.content.json");
            _scratch = Path.Combine(_dir, "scratch");
            File.Copy(TestAssets.AssetPath, _assetPath);
            _svc = new WorkbenchService(_assetPath, _scratch);
            var doc = _svc.GetDocument();
            Assert.True(doc.Ok);
            _canonical = doc.Json!;
            _hash = doc.BaselineHash!;
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        // ── Scenario 1: load the real canonical asset → clean ─────────────────────────────────────
        [Fact]
        public void GetDocument_returns_canonical_asset_and_baseline_hash()
        {
            var doc = _svc.GetDocument();
            Assert.True(doc.Ok);
            Assert.Equal("niflheim.homestead-stone.progression", doc.AssetId);
            Assert.False(string.IsNullOrEmpty(doc.BaselineHash));
            // The served document is canonical (a re-serialization of the on-disk asset).
            Assert.Equal(_canonical, doc.Json);
        }

        [Fact]
        public void Validate_clean_baseline_has_no_diagnostics()
        {
            var res = _svc.Validate(_canonical);
            Assert.Equal("valid", res.Status);
            Assert.False(res.HasErrors);
            Assert.Empty(res.Diagnostics);
        }

        // ── Scenario 2: Field Prep AP 1→2 without a bump is blocked ────────────────────────────────
        [Fact]
        public void FieldPrep_ap_change_without_bump_is_blocked_with_targeted_diagnostics()
        {
            var edited = BumpFieldPrepAp(_canonical, registryBump: false, nodeVersionBump: false);
            var res = _svc.Validate(edited);
            Assert.Equal("invalid", res.Status);
            Assert.True(res.HasErrors);
            // Exactly the two version-bump requirements — nothing spurious (the classifier must not flag
            // every other unchanged node; that was the reference-equality bug this fix closes).
            Assert.All(res.Diagnostics, d => Assert.Equal(DiagnosticCodes.VersionBumpRequired, d.Code));
            Assert.Contains(res.Diagnostics, d => d.Path == "/versions/contentRegistry");
            Assert.Contains(res.Diagnostics, d => d.Path == "/nodes[FieldPrep]/version");
            Assert.Equal(2, res.Diagnostics.Count);

            // Generation is blocked while invalid.
            var gen = _svc.GeneratePreview(edited);
            Assert.True(gen.Blocked);
            Assert.Empty(gen.Artifacts);
        }

        // ── Scenario 3: manual node + registry bumps pass and generate the four artifacts ─────────
        [Fact]
        public void FieldPrep_ap_change_with_node_and_registry_bumps_passes_and_generates()
        {
            var edited = BumpFieldPrepAp(_canonical, registryBump: true, nodeVersionBump: true);
            var res = _svc.Validate(edited);
            Assert.Equal("valid", res.Status);
            Assert.False(res.HasErrors);

            var gen = _svc.GeneratePreview(edited);
            Assert.False(gen.Blocked);
            Assert.Equal(4, gen.Artifacts.Count);
            Assert.Contains(gen.Artifacts, a => a.FileName == "HomesteadProgressionCatalog.Data.g.cs");
        }

        // ── Scenario 4: unavailable Watchful Cook with a price → precise field diagnostic + blocked ─
        [Fact]
        public void Unavailable_watchful_cook_with_price_produces_field_diagnostic_and_blocks_generation()
        {
            var edited = SetWatchfulCookAp(_canonical, 3);
            var res = _svc.Validate(edited);
            Assert.True(res.HasErrors);
            var price = Assert.Single(res.Diagnostics, d => d.Code == DiagnosticCodes.UnavailableHasPrice);
            Assert.Equal("/nodes/4/pricing", price.Path);

            var gen = _svc.GeneratePreview(edited);
            Assert.Equal("generation-blocked", gen.Status);
            Assert.True(gen.Blocked);
        }

        // ── Scenario 6: deterministic preview — generating twice is byte-identical ────────────────
        [Fact]
        public void GeneratePreview_is_deterministic()
        {
            var a = _svc.GeneratePreview(_canonical);
            var b = _svc.GeneratePreview(_canonical);
            Assert.Equal(a.Artifacts.Count, b.Artifacts.Count);
            for (int i = 0; i < a.Artifacts.Count; i++)
            {
                Assert.Equal(a.Artifacts[i].FileName, b.Artifacts[i].FileName);
                Assert.Equal(a.Artifacts[i].Content, b.Artifacts[i].Content);
            }
        }

        // ── Export: atomic write of valid asset into the granted scratch root ─────────────────────
        [Fact]
        public void Export_valid_bumped_asset_writes_asset_and_artifacts_atomically()
        {
            var edited = BumpFieldPrepAp(_canonical, registryBump: true, nodeVersionBump: true);
            var res = _svc.Export(edited, _hash);
            Assert.True(res.Ok);
            Assert.Equal(_scratch, res.OutputDirectory);
            Assert.Contains("homestead-stone.content.json", res.Files);
            Assert.Contains("HomesteadProgressionCatalog.Data.g.cs", res.Files);
            // No leftover temp files from the atomic temp+rename.
            Assert.Empty(Directory.GetFiles(_scratch, ".*tmp-*"));
            // Exported asset re-loads and re-validates clean through the core.
            var reloaded = new StoneContentWorkspace().Load(
                File.ReadAllText(Path.Combine(_scratch, "homestead-stone.content.json")));
            Assert.True(reloaded.Ok);
        }

        [Fact]
        public void Export_invalid_asset_is_blocked_and_writes_nothing()
        {
            var edited = SetWatchfulCookAp(_canonical, 3);
            var res = _svc.Export(edited, _hash);
            Assert.False(res.Ok);
            Assert.Equal("blocked", res.Status);
            Assert.False(Directory.Exists(_scratch) && Directory.GetFiles(_scratch).Any());
        }

        // ── File safety: stale-write detection refuses to clobber an external edit ─────────────────
        [Fact]
        public void Export_refuses_when_asset_changed_on_disk_since_load()
        {
            // Simulate an external edit to the asset root after the session captured its baseline hash.
            File.AppendAllText(_assetPath, "\n");
            var edited = BumpFieldPrepAp(_canonical, registryBump: true, nodeVersionBump: true);
            var res = _svc.Export(edited, _hash);
            Assert.False(res.Ok);
            Assert.Equal("stale-baseline", res.Status);
            Assert.False(Directory.Exists(_scratch) && Directory.GetFiles(_scratch).Any());
        }

        [Fact]
        public void Malformed_document_is_surfaced_as_a_load_error_not_a_crash()
        {
            var res = _svc.Validate("{ not json");
            Assert.Equal("load-error", res.Status);
            Assert.True(res.HasErrors);
        }

        // ── Classifier regression: two independently parsed identical documents are NOT "changed" ──
        // This is the reference-equality bug the web adapter exposed: TreeTuningDef / NodeRequirementsDef
        // carry lists, so record equality fell back to reference equality and flagged every node.
        [Fact]
        public void Classifier_reports_no_change_between_two_independent_parses_of_the_same_asset()
        {
            var a = CanonicalJson.Load(_canonical);
            var b = CanonicalJson.Load(_canonical);
            var changes = ContentChangeClassifier.Classify(a, b);
            Assert.Empty(changes);
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────────────
        private static string BumpFieldPrepAp(string canonical, bool registryBump, bool nodeVersionBump)
        {
            var doc = CanonicalJson.Load(canonical);
            var nodes = doc.Nodes.Select(n =>
            {
                if (n.Id != "FieldPrep") return n;
                var version = nodeVersionBump ? n.Version + 1 : n.Version;
                return n with { Version = version, Pricing = n.Pricing with { PurchaseAp = 2 } };
            }).ToList();
            var versions = registryBump ? doc.Versions with { ContentRegistry = doc.Versions.ContentRegistry + 1 } : doc.Versions;
            return CanonicalJson.Serialize(doc with { Nodes = nodes, Versions = versions });
        }

        private static string SetWatchfulCookAp(string canonical, int ap)
        {
            var doc = CanonicalJson.Load(canonical);
            var nodes = doc.Nodes.Select(n =>
                n.Id == "WatchfulCook" ? n with { Pricing = n.Pricing with { PurchaseAp = ap } } : n).ToList();
            return CanonicalJson.Serialize(doc with { Nodes = nodes });
        }
    }
}
