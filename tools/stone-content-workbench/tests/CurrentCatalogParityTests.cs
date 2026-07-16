using System.Linq;
using StoneContent.Workbench.Core;
using StoneContent.Workbench.Core.Parity;
using StoneContent.Workbench.Core.Serialization;
using Xunit;

namespace StoneContent.Workbench.Tests
{
    // Task 5 — behavioral parity between the declarative asset and the CURRENT production C# catalogs.
    // The three current-main axes must PASS; the Tree-tuning axis is reported honestly as a held-branch
    // reference with current-main parity NOT APPLICABLE (T012 wt/t_c7313d0f is not merged).
    public sealed class CurrentCatalogParityTests
    {
        private static ParityReport Report()
        {
            var declarative = CanonicalJson.Load(TestAssets.AssetJson);
            var currentMain = CurrentCatalogSnapshotAdapter.Build();
            return ContentParityReporter.Compare(declarative, currentMain);
        }

        [Fact]
        public void Three_current_main_axes_pass()
        {
            var report = Report();
            var registry = report.Axes.Single(a => a.Axis == ContentParityReporter.ContentRegistryAxis);
            var foundational = report.Axes.Single(a => a.Axis == ContentParityReporter.FoundationalAxis);
            var facets = report.Axes.Single(a => a.Axis == ContentParityReporter.FacetPaletteAxis);

            Assert.Equal(ParityStatus.Pass, registry.Status);
            Assert.Equal(ParityStatus.Pass, foundational.Status);
            Assert.Equal(ParityStatus.Pass, facets.Status);
            Assert.True(report.CurrentMainAxesPass,
                string.Join("\n", report.CurrentMainAxes.SelectMany(a => a.Differences)));
        }

        [Fact]
        public void Tree_tuning_is_held_branch_reference_not_applicable_on_main()
        {
            var tuning = Report().Axes.Single(a => a.Axis == ContentParityReporter.TreeTuningAxis);
            Assert.Equal(AxisBacking.HeldBranchReference, tuning.Backing);
            Assert.Equal(ParityStatus.NotApplicable, tuning.Status);
            Assert.Contains(tuning.Differences, d => d.Contains("wt/t_c7313d0f"));
        }

        [Fact]
        public void Does_not_claim_four_axis_current_main_parity()
        {
            var report = Report();
            // Exactly three current-main-backed axes; tuning is excluded from the main-parity gate.
            Assert.Equal(3, report.CurrentMainAxes.Count());
            Assert.DoesNotContain(report.CurrentMainAxes, a => a.Axis == ContentParityReporter.TreeTuningAxis);
        }

        [Fact]
        public void Check_reports_clean_when_generated_matches_fresh()
        {
            var ws = new StoneContentWorkspace();
            var doc = CanonicalJson.Load(TestAssets.AssetJson);
            var fresh = ws.Generate(doc);
            var onDisk = fresh.Artifacts.ToDictionary(a => a.FileName, a => a.Content);
            var check = ws.Check(doc, onDisk);
            Assert.True(check.Ok, string.Join("\n", check.Diagnostics));
        }

        [Fact]
        public void Check_reports_drift_when_generated_differs()
        {
            var ws = new StoneContentWorkspace();
            var doc = CanonicalJson.Load(TestAssets.AssetJson);
            var fresh = ws.Generate(doc);
            var onDisk = fresh.Artifacts.ToDictionary(a => a.FileName, a => a.Content);
            onDisk["HomesteadProgressionCatalog.Data.g.cs"] += "\n// tampered\n";
            var check = ws.Check(doc, onDisk);
            Assert.False(check.Ok);
            Assert.Contains(check.Diagnostics, d => d.Code == "GENERATED_DRIFT");
        }
    }
}
