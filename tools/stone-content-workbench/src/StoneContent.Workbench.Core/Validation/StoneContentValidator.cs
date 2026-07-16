using System;
using System.Collections.Generic;
using System.Linq;
using StoneContent.Workbench.Core.Changes;
using StoneContent.Workbench.Core.Model;

namespace StoneContent.Workbench.Core.Validation
{
    /// <summary>Pure semantic + version validator for a Stone content document. Given a document (and
    /// optionally a baseline to diff against), it returns a ValidationReport of stable diagnostics.
    /// It never prints, writes, or mutates. Schema SHAPE (unknown/missing fields, JSON types) is
    /// enforced at load time by <see cref="Serialization.CanonicalJson"/>; this validator owns enum
    /// VALUES, cross-references, roster arithmetic, pricing/ownership contradictions, threshold order,
    /// Foundational overlap, and — with a baseline — explicit version-bump requirements.</summary>
    public sealed class StoneContentValidator
    {
        private static readonly HashSet<string> OutcomeTypes = new(StringComparer.Ordinal)
            { "LocalEffect", "CharacterEffect", "PermanentEffect" };
        private static readonly HashSet<string> Ownerships = new(StringComparer.Ordinal)
            { "StoneCultivated", "PersonalOffered", "NoneWhileUnavailable" };
        private static readonly HashSet<string> FirstBuildStatuses = new(StringComparer.Ordinal)
            { "Executable", "Unavailable" };
        private static readonly HashSet<string> FacetCategories = new(StringComparer.Ordinal)
            { "Profession", "Martial" };

        public const int ExpectedNodeCount = 20;
        public const int ExpectedExecutableCount = 13;
        public const int ExpectedUnavailableCount = 7;

        public ValidationReport Validate(StoneContentDocument document, StoneContentDocument? baseline = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var diags = new List<ContentDiagnostic>();

            ValidateEnums(document, diags);
            ValidateDuplicateIds(document, diags);
            ValidateTreeReferences(document, diags);
            ValidateNodeReferences(document, diags);
            ValidateRosterArithmetic(document, diags);
            ValidateLevelPartition(document, diags);
            ValidatePricingOwnership(document, diags);
            ValidateThresholds(document, diags);
            ValidateFoundationalOverlap(document, diags);

            if (baseline != null)
                ValidateVersionPolicy(baseline, document, diags);

            return new ValidationReport(diags);
        }

        private static void ValidateEnums(StoneContentDocument d, List<ContentDiagnostic> diags)
        {
            for (int i = 0; i < d.Facets.Count; i++)
            {
                var f = d.Facets[i];
                if (!FacetCategories.Contains(f.Category))
                    diags.Add(Enum($"/facets/{i}/category", f.Category, FacetCategories));
            }
            for (int i = 0; i < d.Trees.Count; i++)
            {
                var t = d.Trees[i];
                if (!FacetCategories.Contains(t.Category))
                    diags.Add(Enum($"/trees/{i}/category", t.Category, FacetCategories));
            }
            for (int i = 0; i < d.Nodes.Count; i++)
            {
                var n = d.Nodes[i];
                if (!OutcomeTypes.Contains(n.OutcomeType))
                    diags.Add(Enum($"/nodes/{i}/outcomeType", n.OutcomeType, OutcomeTypes));
                if (!Ownerships.Contains(n.Ownership))
                    diags.Add(Enum($"/nodes/{i}/ownership", n.Ownership, Ownerships));
                if (!FirstBuildStatuses.Contains(n.FirstBuildStatus))
                    diags.Add(Enum($"/nodes/{i}/firstBuildStatus", n.FirstBuildStatus, FirstBuildStatuses));
            }
        }

        private static ContentDiagnostic Enum(string path, string value, IEnumerable<string> allowed) =>
            new(DiagnosticCodes.SchemaEnum, DiagnosticSeverity.Error, path,
                $"'{value}' is not one of [{string.Join(", ", allowed)}].");

        private static void ValidateDuplicateIds(StoneContentDocument d, List<ContentDiagnostic> diags)
        {
            DupCheck(d.Nodes.Select(n => n.Id), "/nodes", "node id", diags);
            DupCheck(d.Trees.Select(t => t.Id), "/trees", "tree id", diags);
            DupCheck(d.Facets.Select(f => f.Id), "/facets", "facet id", diags);
        }

        private static void DupCheck(IEnumerable<string> ids, string path, string label, List<ContentDiagnostic> diags)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ids)
                if (!seen.Add(id))
                    diags.Add(new ContentDiagnostic(DiagnosticCodes.DuplicateId, DiagnosticSeverity.Error,
                        path, $"Duplicate {label} '{id}'."));
        }

        private static void ValidateTreeReferences(StoneContentDocument d, List<ContentDiagnostic> diags)
        {
            var treeIds = new HashSet<string>(d.Trees.Select(t => t.Id), StringComparer.Ordinal);
            for (int i = 0; i < d.Facets.Count; i++)
            {
                var f = d.Facets[i];
                for (int j = 0; j < f.CandidateTreeIds.Count; j++)
                    if (!treeIds.Contains(f.CandidateTreeIds[j]))
                        diags.Add(new ContentDiagnostic(DiagnosticCodes.UnknownTree, DiagnosticSeverity.Error,
                            $"/facets/{i}/candidateTreeIds/{j}",
                            $"Candidate tree '{f.CandidateTreeIds[j]}' is not an authored tree."));
            }
            for (int i = 0; i < d.Nodes.Count; i++)
                if (!treeIds.Contains(d.Nodes[i].TreeId))
                    diags.Add(new ContentDiagnostic(DiagnosticCodes.UnknownTree, DiagnosticSeverity.Error,
                        $"/nodes/{i}/treeId", $"Node references unknown tree '{d.Nodes[i].TreeId}'."));
        }

        private static void ValidateNodeReferences(StoneContentDocument d, List<ContentDiagnostic> diags)
        {
            var nodeIds = new HashSet<string>(d.Nodes.Select(n => n.Id), StringComparer.Ordinal);
            for (int i = 0; i < d.Nodes.Count; i++)
            {
                var n = d.Nodes[i];
                for (int j = 0; j < n.Requirements.PriorOfferedNodeIds.Count; j++)
                {
                    var prior = n.Requirements.PriorOfferedNodeIds[j];
                    if (!nodeIds.Contains(prior))
                        diags.Add(new ContentDiagnostic(DiagnosticCodes.UnknownNodeReference, DiagnosticSeverity.Error,
                            $"/nodes/{i}/requirements/priorOfferedNodeIds/{j}",
                            $"Prior-offered reference '{prior}' is not an authored node."));
                }
            }
        }

        private static void ValidateRosterArithmetic(StoneContentDocument d, List<ContentDiagnostic> diags)
        {
            int total = d.Nodes.Count;
            int exe = d.Nodes.Count(n => n.FirstBuildStatus == "Executable");
            int un = d.Nodes.Count(n => n.FirstBuildStatus == "Unavailable");
            if (total != ExpectedNodeCount || exe != ExpectedExecutableCount || un != ExpectedUnavailableCount)
                diags.Add(new ContentDiagnostic(DiagnosticCodes.RosterArithmetic, DiagnosticSeverity.Error,
                    "/nodes",
                    $"Roster must be {ExpectedNodeCount} = {ExpectedExecutableCount} executable + " +
                    $"{ExpectedUnavailableCount} unavailable; found {total} = {exe} + {un}."));
        }

        private static void ValidateLevelPartition(StoneContentDocument d, List<ContentDiagnostic> diags)
        {
            var executable = d.Nodes.Where(n => n.FirstBuildStatus == "Executable").ToList();
            int l1 = executable.Count(n => n.TreeLevel == 1);
            var l2 = executable.Where(n => n.TreeLevel == 2).ToList();
            bool ok = l1 == 12 && l2.Count == 1 && l2[0].Id == "SwiftPreparation";
            if (!ok)
                diags.Add(new ContentDiagnostic(DiagnosticCodes.InvalidLevelPartition, DiagnosticSeverity.Error,
                    "/nodes",
                    "Executable partition must be exactly 12 Level-1 nodes plus the sole executable " +
                    "Level-2 node 'SwiftPreparation'; found " +
                    $"{l1} L1 and [{string.Join(", ", l2.Select(n => n.Id))}] at L2."));
        }

        private static void ValidatePricingOwnership(StoneContentDocument d, List<ContentDiagnostic> diags)
        {
            for (int i = 0; i < d.Nodes.Count; i++)
            {
                var n = d.Nodes[i];
                if (n.FirstBuildStatus == "Unavailable")
                {
                    if (n.Pricing.DevelopmentBp.HasValue || n.Pricing.PurchaseAp.HasValue)
                        diags.Add(new ContentDiagnostic(DiagnosticCodes.UnavailableHasPrice, DiagnosticSeverity.Error,
                            $"/nodes/{i}/pricing",
                            $"Unavailable node '{n.Id}' must carry no price (developmentBp and purchaseAp must be null)."));
                    continue;
                }
                // Executable nodes:
                if (n.Ownership == "StoneCultivated" && n.Pricing.PurchaseAp.HasValue)
                    diags.Add(new ContentDiagnostic(DiagnosticCodes.LocalHasApPrice, DiagnosticSeverity.Error,
                        $"/nodes/{i}/pricing/purchaseAp",
                        $"Local (Stone-cultivated) node '{n.Id}' must not carry an AP purchase price."));
                if (n.Ownership == "PersonalOffered" && !n.Pricing.PurchaseAp.HasValue)
                    diags.Add(new ContentDiagnostic(DiagnosticCodes.PersonalMissingApPrice, DiagnosticSeverity.Error,
                        $"/nodes/{i}/pricing/purchaseAp",
                        $"Personal (offered) node '{n.Id}' must carry an AP purchase price."));
            }
        }

        private static void ValidateThresholds(StoneContentDocument d, List<ContentDiagnostic> diags)
        {
            for (int i = 0; i < d.Trees.Count; i++)
            {
                var th = d.Trees[i].Tuning.CumulativeBpThresholds;
                for (int j = 1; j < th.Count; j++)
                    if (th[j] <= th[j - 1])
                    {
                        diags.Add(new ContentDiagnostic(DiagnosticCodes.ThresholdsNotAscending, DiagnosticSeverity.Error,
                            $"/trees/{i}/tuning/cumulativeBpThresholds",
                            "Cumulative BP thresholds must be strictly ascending."));
                        break;
                    }
            }
        }

        private static void ValidateFoundationalOverlap(StoneContentDocument d, List<ContentDiagnostic> diags)
        {
            var exclusions = new HashSet<string>(d.Foundational.Catalog.Exclusions, StringComparer.Ordinal);
            var members = d.Foundational.Catalog.Members;
            for (int i = 0; i < members.Count; i++)
                if (exclusions.Contains(members[i]))
                    diags.Add(new ContentDiagnostic(DiagnosticCodes.FoundationalMemberExcluded, DiagnosticSeverity.Error,
                        $"/foundational/catalog/members/{i}",
                        $"Member '{members[i]}' is also listed in exclusions; a piece cannot be both."));
        }

        // ── version policy (requires a baseline) ─────────────────────────────────────────────────
        private static void ValidateVersionPolicy(StoneContentDocument baseline, StoneContentDocument edited,
            List<ContentDiagnostic> diags)
        {
            // Regressions on any pin are always an error.
            CheckRegression("contentRegistry", baseline.Versions.ContentRegistry, edited.Versions.ContentRegistry, "/versions/contentRegistry", diags);
            CheckRegression("foundationalCatalog", baseline.Versions.FoundationalCatalog, edited.Versions.FoundationalCatalog, "/versions/foundationalCatalog", diags);
            CheckRegression("facetPalette", baseline.Versions.FacetPalette, edited.Versions.FacetPalette, "/versions/facetPalette", diags);
            CheckRegression("treeTuning", baseline.Versions.TreeTuning, edited.Versions.TreeTuning, "/versions/treeTuning", diags);

            var changes = ContentChangeClassifier.Classify(baseline, edited);
            foreach (var c in changes)
            {
                switch (c.Pin)
                {
                    case RequiredPin.ContentRegistry:
                        if (edited.Versions.ContentRegistry <= baseline.Versions.ContentRegistry)
                            diags.Add(BumpRequired("/versions/contentRegistry", c));
                        if (c.RequiresNodeVersionBump)
                        {
                            var nodeId = ExtractNodeId(c.Path);
                            if (nodeId != null && !ContentChangeClassifier.NodeVersionBumped(baseline, edited, nodeId))
                                diags.Add(BumpRequired($"/nodes[{nodeId}]/version", c));
                        }
                        break;
                    case RequiredPin.FoundationalCatalog:
                        if (edited.Versions.FoundationalCatalog <= baseline.Versions.FoundationalCatalog)
                            diags.Add(BumpRequired("/versions/foundationalCatalog", c));
                        break;
                    case RequiredPin.FacetPalette:
                        if (edited.Versions.FacetPalette <= baseline.Versions.FacetPalette)
                            diags.Add(BumpRequired("/versions/facetPalette", c));
                        break;
                    case RequiredPin.TreeTuning:
                        if (edited.Versions.TreeTuning <= baseline.Versions.TreeTuning)
                            diags.Add(BumpRequired("/versions/treeTuning", c));
                        break;
                    case RequiredPin.FormatVersion:
                        break;
                }
            }
        }

        private static void CheckRegression(string name, int b, int e, string path, List<ContentDiagnostic> diags)
        {
            if (e < b)
                diags.Add(new ContentDiagnostic(DiagnosticCodes.VersionRegression, DiagnosticSeverity.Error,
                    path, $"Pin '{name}' regressed from {b} to {e}; version pins never move backward."));
        }

        private static ContentDiagnostic BumpRequired(string path, ClassifiedChange c) =>
            new(DiagnosticCodes.VersionBumpRequired, DiagnosticSeverity.Error, path, c.Detail + $" (source: {c.Path})");

        private static string? ExtractNodeId(string path)
        {
            // path shape "/nodes[<id>]"
            int lb = path.IndexOf('[');
            int rb = path.IndexOf(']');
            if (lb >= 0 && rb > lb) return path.Substring(lb + 1, rb - lb - 1);
            return null;
        }
    }
}
