using System;
using System.Collections.Generic;
using System.Linq;
using StoneContent.Workbench.Core.Model;

namespace StoneContent.Workbench.Core.Parity
{
    /// <summary>Per-axis parity outcome. NotApplicable is a first-class, honest state: it is what the
    /// Tree-tuning axis reports while T012 remains a HELD review branch and is not on current main.</summary>
    public enum ParityStatus
    {
        Pass,
        Fail,
        NotApplicable
    }

    /// <summary>Whether a catalog axis is backed by current main or is only a held-branch reference.</summary>
    public enum AxisBacking
    {
        CurrentMain,
        HeldBranchReference
    }

    /// <summary>One catalog axis's parity result. For a held-branch axis, <see cref="Status"/> is
    /// NotApplicable against current main; the reporter states this explicitly rather than claiming
    /// four-axis current-main parity.</summary>
    public sealed record AxisParity(
        string Axis,
        AxisBacking Backing,
        ParityStatus Status,
        IReadOnlyList<string> Differences);

    /// <summary>The full parity report across all four axes. <see cref="CurrentMainAxesPass"/> is true
    /// only when every current-main-backed axis passes; held-branch axes are excluded from that gate.</summary>
    public sealed class ParityReport
    {
        public ParityReport(IReadOnlyList<AxisParity> axes)
        {
            Axes = axes;
        }

        public IReadOnlyList<AxisParity> Axes { get; }

        public bool CurrentMainAxesPass =>
            Axes.Where(a => a.Backing == AxisBacking.CurrentMain).All(a => a.Status == ParityStatus.Pass);

        public IEnumerable<AxisParity> CurrentMainAxes => Axes.Where(a => a.Backing == AxisBacking.CurrentMain);
    }

    /// <summary>Normalizes the declarative document and the current-C# snapshot to the same semantic
    /// shape and compares field-by-field, per axis. Byte-equal text is neither required nor sufficient;
    /// this is behavioral parity. The Tree-tuning axis is reported as a held-branch reference with
    /// current-main parity NOT APPLICABLE until T012 (wt/t_c7313d0f) merges.</summary>
    public static class ContentParityReporter
    {
        public const string ContentRegistryAxis = "contentRegistry";
        public const string FoundationalAxis = "foundationalCatalog";
        public const string FacetPaletteAxis = "facetPalette";
        public const string TreeTuningAxis = "treeTuning";

        /// <param name="declarative">The authored asset's document.</param>
        /// <param name="currentMain">The normalized snapshot of the current production C# catalogs.</param>
        public static ParityReport Compare(StoneContentDocument declarative, StoneContentDocument currentMain)
        {
            if (declarative == null) throw new ArgumentNullException(nameof(declarative));
            if (currentMain == null) throw new ArgumentNullException(nameof(currentMain));

            return new ParityReport(new List<AxisParity>
            {
                CompareNodes(declarative, currentMain),
                CompareFoundational(declarative, currentMain),
                CompareFacets(declarative, currentMain),
                TuningHeldReference(declarative),
            });
        }

        private static AxisParity CompareNodes(StoneContentDocument a, StoneContentDocument b)
        {
            var diffs = new List<string>();
            if (a.Versions.ContentRegistry != b.Versions.ContentRegistry)
                diffs.Add($"contentRegistry pin {a.Versions.ContentRegistry} != {b.Versions.ContentRegistry}");

            var aById = a.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
            var bById = b.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
            foreach (var id in bById.Keys.Where(k => !aById.ContainsKey(k)))
                diffs.Add($"node '{id}' present in current-main, absent in asset");
            foreach (var id in aById.Keys.Where(k => !bById.ContainsKey(k)))
                diffs.Add($"node '{id}' present in asset, absent in current-main");
            foreach (var id in aById.Keys.Where(bById.ContainsKey))
                if (!NodesEqual(aById[id], bById[id]))
                    diffs.Add($"node '{id}' differs between asset and current-main");

            // Ordering parity too (authored order is contract).
            if (a.Nodes.Select(n => n.Id).SequenceEqual(b.Nodes.Select(n => n.Id)) == false)
                diffs.Add("node authored order differs between asset and current-main");

            return new AxisParity(ContentRegistryAxis, AxisBacking.CurrentMain,
                diffs.Count == 0 ? ParityStatus.Pass : ParityStatus.Fail, diffs);
        }

        private static bool NodesEqual(NodeDef x, NodeDef y) =>
            x.Version == y.Version && x.TreeId == y.TreeId && x.TreeLevel == y.TreeLevel &&
            x.DisplayLabel == y.DisplayLabel && x.OutcomeType == y.OutcomeType &&
            x.Ownership == y.Ownership && x.FirstBuildStatus == y.FirstBuildStatus &&
            x.Pricing == y.Pricing &&
            x.Requirements.RequiresCommittedTree == y.Requirements.RequiresCommittedTree &&
            x.Requirements.RequiresCurrentContentVersion == y.Requirements.RequiresCurrentContentVersion &&
            x.Requirements.MinActiveStoneLevel == y.Requirements.MinActiveStoneLevel &&
            x.Requirements.MinTreeLevel == y.Requirements.MinTreeLevel &&
            x.Requirements.RequiresActiveAttunement == y.Requirements.RequiresActiveAttunement &&
            x.Requirements.RequiresOfferedStatus == y.Requirements.RequiresOfferedStatus &&
            x.Requirements.RequiresDevelopmentAuthority == y.Requirements.RequiresDevelopmentAuthority &&
            x.Requirements.RequiresResponsibilityRange == y.Requirements.RequiresResponsibilityRange &&
            x.Requirements.PriorOfferedNodeIds.SequenceEqual(y.Requirements.PriorOfferedNodeIds);

        private static AxisParity CompareFoundational(StoneContentDocument a, StoneContentDocument b)
        {
            var diffs = new List<string>();
            var ac = a.Foundational.Catalog;
            var bc = b.Foundational.Catalog;
            if (a.Versions.FoundationalCatalog != b.Versions.FoundationalCatalog)
                diffs.Add($"foundationalCatalog pin {a.Versions.FoundationalCatalog} != {b.Versions.FoundationalCatalog}");
            if (!a.Foundational.Tree.Equals(b.Foundational.Tree))
                diffs.Add("foundational tree identity differs");
            if (ac.Id != bc.Id) diffs.Add($"catalog id '{ac.Id}' != '{bc.Id}'");
            if (ac.VersionTag != bc.VersionTag) diffs.Add($"catalog versionTag '{ac.VersionTag}' != '{bc.VersionTag}'");
            if (!ac.Members.SequenceEqual(bc.Members)) diffs.Add("foundational members differ");
            if (!ac.Exclusions.SequenceEqual(bc.Exclusions)) diffs.Add("foundational exclusions differ");
            return new AxisParity(FoundationalAxis, AxisBacking.CurrentMain,
                diffs.Count == 0 ? ParityStatus.Pass : ParityStatus.Fail, diffs);
        }

        private static AxisParity CompareFacets(StoneContentDocument a, StoneContentDocument b)
        {
            var diffs = new List<string>();
            if (a.Versions.FacetPalette != b.Versions.FacetPalette)
                diffs.Add($"facetPalette pin {a.Versions.FacetPalette} != {b.Versions.FacetPalette}");
            if (a.Facets.Count != b.Facets.Count)
                diffs.Add($"facet count {a.Facets.Count} != {b.Facets.Count}");
            else
                for (int i = 0; i < a.Facets.Count; i++)
                {
                    var af = a.Facets[i];
                    var bf = b.Facets[i];
                    if (af.Id != bf.Id || af.Category != bf.Category ||
                        !af.CandidateTreeIds.SequenceEqual(bf.CandidateTreeIds))
                        diffs.Add($"facet '{af.Id}' differs from current-main");
                }
            return new AxisParity(FacetPaletteAxis, AxisBacking.CurrentMain,
                diffs.Count == 0 ? ParityStatus.Pass : ParityStatus.Fail, diffs);
        }

        // Tree tuning is HELD on wt/t_c7313d0f and is NOT on current main. We do not compare it against
        // a production TreeTuningCatalog that main does not have — that would fabricate a fourth
        // current-main axis. Instead we report it honestly as a held-branch reference, N/A on main.
        private static AxisParity TuningHeldReference(StoneContentDocument a)
        {
            var notes = new List<string>
            {
                "Tree tuning is a held review branch (wt/t_c7313d0f); not on current main.",
                $"Asset carries a treeTuning pin of {a.Versions.TreeTuning} as a held-branch reference.",
                "Current-main parity is NOT APPLICABLE until T012 merges.",
            };
            return new AxisParity(TreeTuningAxis, AxisBacking.HeldBranchReference,
                ParityStatus.NotApplicable, notes);
        }
    }
}
