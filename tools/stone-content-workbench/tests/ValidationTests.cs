using System.Collections.Generic;
using System.Linq;
using StoneContent.Workbench.Core.Model;
using StoneContent.Workbench.Core.Serialization;
using StoneContent.Workbench.Core.Validation;
using Xunit;

namespace StoneContent.Workbench.Tests
{
    // Task 3 — semantic + version validation. One diagnostic behavior per test, asserting exact
    // code + path, never prose. The canonical asset is the valid baseline.
    public sealed class ValidationTests
    {
        private static StoneContentDocument Load() => CanonicalJson.Load(TestAssets.AssetJson);
        private static readonly StoneContentValidator Validator = new();

        private static IReadOnlyList<NodeDef> Replace(IReadOnlyList<NodeDef> nodes, string id, System.Func<NodeDef, NodeDef> edit)
            => nodes.Select(n => n.Id == id ? edit(n) : n).ToList();

        [Fact]
        public void Canonical_asset_is_valid()
        {
            var report = Validator.Validate(Load());
            Assert.True(report.IsClean, string.Join("\n", report.Diagnostics));
        }

        [Fact]
        public void FieldPrep_ap_change_without_bumps_requires_version_bump()
        {
            var baseline = Load();
            var edited = baseline with
            {
                Nodes = Replace(baseline.Nodes, "FieldPrep",
                    n => n with { Pricing = n.Pricing with { PurchaseAp = 2 } })
            };
            var report = Validator.Validate(edited, baseline);
            Assert.True(report.HasCode(DiagnosticCodes.VersionBumpRequired));
            Assert.True(report.HasErrors);
        }

        [Fact]
        public void FieldPrep_ap_change_with_node_and_registry_bumps_passes()
        {
            var baseline = Load();
            var edited = baseline with
            {
                Versions = baseline.Versions with { ContentRegistry = 2 },
                Nodes = Replace(baseline.Nodes, "FieldPrep",
                    n => n with { Version = 2, Pricing = n.Pricing with { PurchaseAp = 2 } })
            };
            var report = Validator.Validate(edited, baseline);
            Assert.False(report.HasErrors, string.Join("\n", report.Diagnostics));
        }

        [Fact]
        public void DisplayLabel_only_change_needs_no_bump()
        {
            var baseline = Load();
            var edited = baseline with
            {
                Nodes = Replace(baseline.Nodes, "FieldPrep", n => n with { DisplayLabel = "Field Preparation" })
            };
            var report = Validator.Validate(edited, baseline);
            Assert.False(report.HasCode(DiagnosticCodes.VersionBumpRequired));
            Assert.False(report.HasErrors);
        }

        [Fact]
        public void Unavailable_node_with_a_price_rejects_at_the_price_path()
        {
            var baseline = Load();
            var edited = baseline with
            {
                Nodes = Replace(baseline.Nodes, "WatchfulCook",
                    n => n with { Pricing = n.Pricing with { PurchaseAp = 1 } })
            };
            var report = Validator.Validate(edited);
            var diag = Assert.Single(report.WithCode(DiagnosticCodes.UnavailableHasPrice));
            Assert.Equal("/nodes/4/pricing", diag.Path);
        }

        [Fact]
        public void Local_node_with_ap_price_rejects()
        {
            var baseline = Load();
            var edited = baseline with
            {
                Nodes = Replace(baseline.Nodes, "SavorTheHearth",
                    n => n with { Pricing = n.Pricing with { PurchaseAp = 1 } })
            };
            var report = Validator.Validate(edited);
            Assert.True(report.HasCode(DiagnosticCodes.LocalHasApPrice));
        }

        [Fact]
        public void Personal_node_missing_ap_price_rejects()
        {
            var baseline = Load();
            var edited = baseline with
            {
                Nodes = Replace(baseline.Nodes, "FieldPrep",
                    n => n with { Pricing = n.Pricing with { PurchaseAp = null } })
            };
            var report = Validator.Validate(edited);
            Assert.True(report.HasCode(DiagnosticCodes.PersonalMissingApPrice));
        }

        [Fact]
        public void Broken_swift_prerequisite_rejects_as_unknown_node_reference()
        {
            var baseline = Load();
            var edited = baseline with
            {
                Nodes = Replace(baseline.Nodes, "SwiftPreparation",
                    n => n with { Requirements = n.Requirements with { PriorOfferedNodeIds = new[] { "FieldPrep", "GhostNode" } } })
            };
            var report = Validator.Validate(edited);
            Assert.True(report.HasCode(DiagnosticCodes.UnknownNodeReference));
        }

        [Fact]
        public void Member_exclusion_overlap_rejects()
        {
            var baseline = Load();
            var cat = baseline.Foundational.Catalog;
            var edited = baseline with
            {
                Foundational = baseline.Foundational with
                {
                    Catalog = cat with { Exclusions = cat.Exclusions.Concat(new[] { "foundation_wood_floor" }).ToList() }
                }
            };
            var report = Validator.Validate(edited);
            Assert.True(report.HasCode(DiagnosticCodes.FoundationalMemberExcluded));
        }

        [Fact]
        public void Unknown_tree_reference_on_node_rejects()
        {
            var baseline = Load();
            var edited = baseline with
            {
                Nodes = Replace(baseline.Nodes, "FieldPrep", n => n with { TreeId = "Alchemy" })
            };
            var report = Validator.Validate(edited);
            Assert.True(report.HasCode(DiagnosticCodes.UnknownTree));
        }

        [Fact]
        public void Duplicate_node_id_rejects()
        {
            var baseline = Load();
            var dup = baseline.Nodes[0];
            var edited = baseline with { Nodes = baseline.Nodes.Append(dup).ToList() };
            var report = Validator.Validate(edited);
            Assert.True(report.HasCode(DiagnosticCodes.DuplicateId));
        }

        [Fact]
        public void Bad_enum_value_rejects_with_schema_enum()
        {
            var baseline = Load();
            var edited = baseline with
            {
                Nodes = Replace(baseline.Nodes, "FieldPrep", n => n with { OutcomeType = "MagicEffect" })
            };
            var report = Validator.Validate(edited);
            Assert.True(report.HasCode(DiagnosticCodes.SchemaEnum));
        }

        [Fact]
        public void Roster_arithmetic_rejects_when_node_removed()
        {
            var baseline = Load();
            var edited = baseline with { Nodes = baseline.Nodes.Where(n => n.Id != "HeavyHands").ToList() };
            var report = Validator.Validate(edited);
            Assert.True(report.HasCode(DiagnosticCodes.RosterArithmetic));
        }

        [Fact]
        public void Non_ascending_thresholds_reject()
        {
            var baseline = Load();
            var t0 = baseline.Trees[0];
            var edited = baseline with
            {
                Trees = baseline.Trees.Select((t, i) => i == 0
                    ? t with { Tuning = t.Tuning with { CumulativeBpThresholds = new[] { 5, 3 } } }
                    : t).ToList()
            };
            var report = Validator.Validate(edited);
            Assert.True(report.HasCode(DiagnosticCodes.ThresholdsNotAscending));
        }

        [Fact]
        public void Version_regression_rejects()
        {
            var baseline = Load();
            var edited = baseline with { Versions = baseline.Versions with { ContentRegistry = 0 } };
            var report = Validator.Validate(edited, baseline);
            Assert.True(report.HasCode(DiagnosticCodes.VersionRegression));
        }

        [Fact]
        public void Foundational_member_change_requires_foundational_catalog_bump()
        {
            var baseline = Load();
            var cat = baseline.Foundational.Catalog;
            var edited = baseline with
            {
                Foundational = baseline.Foundational with
                {
                    Catalog = cat with { Members = cat.Members.Append("foundation_wood_ramp").ToList() }
                }
            };
            var report = Validator.Validate(edited, baseline);
            var bump = report.WithCode(DiagnosticCodes.VersionBumpRequired).ToList();
            Assert.Contains(bump, d => d.Path == "/versions/foundationalCatalog");
        }
    }
}
