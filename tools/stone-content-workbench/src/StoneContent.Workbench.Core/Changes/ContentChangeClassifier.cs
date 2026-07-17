using System;
using System.Collections.Generic;
using System.Linq;
using StoneContent.Workbench.Core.Model;
using StoneContent.Workbench.Core.Validation;

namespace StoneContent.Workbench.Core.Changes
{
    /// <summary>The version pin an edit requires the author to bump. Used to emit VERSION_BUMP_REQUIRED
    /// diagnostics pointing at the exact pin path.</summary>
    public enum RequiredPin
    {
        ContentRegistry,
        FoundationalCatalog,
        FacetPalette,
        TreeTuning,
        FormatVersion
    }

    /// <summary>One classified change between a baseline document and an edited document, carrying the
    /// pin it requires and whether that pin (and, for node-semantic edits, the node version) was
    /// actually bumped. The classifier NEVER auto-bumps; it only reports what the author must do.</summary>
    public sealed record ClassifiedChange(
        RequiredPin Pin,
        string Path,
        string Detail,
        bool RequiresNodeVersionBump);

    /// <summary>Compares a baseline document to an edited one and classifies every semantic delta to the
    /// pin it requires (decision-map #4). Presentation-only edits (displayLabel) require no pin. Node
    /// semantic edits require BOTH the node version AND contentRegistry. Node add/remove/rename and Tree
    /// identity/version changes require contentRegistry; renames are remove+add (never silent rebinding).
    /// Foundational membership/exclusions/identity → foundationalCatalog; Facet ids/categories/candidates
    /// → facetPalette; Tree tuning numbers → treeTuning; authoring-shape (formatVersion) → formatVersion.</summary>
    public static class ContentChangeClassifier
    {
        public static IReadOnlyList<ClassifiedChange> Classify(StoneContentDocument baseline, StoneContentDocument edited)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            if (edited == null) throw new ArgumentNullException(nameof(edited));
            var changes = new List<ClassifiedChange>();

            ClassifyFoundational(baseline, edited, changes);
            ClassifyFacets(baseline, edited, changes);
            ClassifyTrees(baseline, edited, changes);
            ClassifyNodes(baseline, edited, changes);

            return changes;
        }

        private static void ClassifyFoundational(StoneContentDocument b, StoneContentDocument e, List<ClassifiedChange> changes)
        {
            var bc = b.Foundational.Catalog;
            var ec = e.Foundational.Catalog;
            bool changed =
                !b.Foundational.Tree.Equals(e.Foundational.Tree) ||
                bc.Id != ec.Id || bc.VersionTag != ec.VersionTag ||
                !bc.Members.SequenceEqual(ec.Members) ||
                !bc.Exclusions.SequenceEqual(ec.Exclusions);
            if (changed)
                changes.Add(new ClassifiedChange(RequiredPin.FoundationalCatalog, "/foundational",
                    "Foundational tree/catalog identity, members, or exclusions changed.", false));
        }

        private static void ClassifyFacets(StoneContentDocument b, StoneContentDocument e, List<ClassifiedChange> changes)
        {
            bool changed = b.Facets.Count != e.Facets.Count;
            if (!changed)
            {
                for (int i = 0; i < b.Facets.Count; i++)
                {
                    var bf = b.Facets[i];
                    var ef = e.Facets[i];
                    if (bf.Id != ef.Id || bf.Category != ef.Category ||
                        !bf.CandidateTreeIds.SequenceEqual(ef.CandidateTreeIds))
                    {
                        changed = true;
                        break;
                    }
                }
            }
            if (changed)
                changes.Add(new ClassifiedChange(RequiredPin.FacetPalette, "/facets",
                    "Facet ids, categories, or candidate trees changed.", false));
        }

        private static void ClassifyTrees(StoneContentDocument b, StoneContentDocument e, List<ClassifiedChange> changes)
        {
            var bById = b.Trees.ToDictionary(t => t.Id, StringComparer.Ordinal);

            // Tree identity/version change → contentRegistry (a Tree is content identity).
            if (b.Trees.Count != e.Trees.Count ||
                !b.Trees.Select(t => t.Id).SequenceEqual(e.Trees.Select(t => t.Id)))
            {
                changes.Add(new ClassifiedChange(RequiredPin.ContentRegistry, "/trees",
                    "Tree roster identity changed (add/remove/reorder).", false));
            }
            foreach (var et in e.Trees)
            {
                if (bById.TryGetValue(et.Id, out var bt))
                {
                    if (bt.Version != et.Version || bt.Category != et.Category)
                        changes.Add(new ClassifiedChange(RequiredPin.ContentRegistry, $"/trees[{et.Id}]",
                            "Tree identity/version/category changed.", false));
                    if (!TuningEquals(bt.Tuning, et.Tuning))
                        changes.Add(new ClassifiedChange(RequiredPin.TreeTuning, $"/trees[{et.Id}]/tuning",
                            "Tree tuning numbers changed.", false));
                }
            }
        }

        private static void ClassifyNodes(StoneContentDocument b, StoneContentDocument e, List<ClassifiedChange> changes)
        {
            var bById = b.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
            var eById = e.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

            // Add / remove (renames surface here as remove+add — never silent rebinding).
            foreach (var id in eById.Keys.Where(k => !bById.ContainsKey(k)))
                changes.Add(new ClassifiedChange(RequiredPin.ContentRegistry, $"/nodes[{id}]",
                    "Node added.", false));
            foreach (var id in bById.Keys.Where(k => !eById.ContainsKey(k)))
                changes.Add(new ClassifiedChange(RequiredPin.ContentRegistry, $"/nodes[{id}]",
                    "Node removed.", false));

            foreach (var en in e.Nodes)
            {
                if (!bById.TryGetValue(en.Id, out var bn)) continue;
                bool semantic = IsNodeSemanticChange(bn, en);
                if (semantic)
                    changes.Add(new ClassifiedChange(RequiredPin.ContentRegistry, $"/nodes[{en.Id}]",
                        "Node semantic field changed; requires node-version and contentRegistry bump.", true));
                // displayLabel-only change requires no pin (presentation).
            }
        }

        /// <summary>A node's SEMANTIC identity: everything except displayLabel and the node's own version
        /// (the version is the thing the author bumps IN RESPONSE to a semantic change, so comparing it
        /// here would mask the requirement).</summary>
        private static bool IsNodeSemanticChange(NodeDef b, NodeDef e) =>
            b.TreeId != e.TreeId ||
            b.TreeLevel != e.TreeLevel ||
            b.OutcomeType != e.OutcomeType ||
            b.Ownership != e.Ownership ||
            b.FirstBuildStatus != e.FirstBuildStatus ||
            b.Pricing != e.Pricing ||
            !RequirementsEqual(b.Requirements, e.Requirements);

        // TreeTuningDef and NodeRequirementsDef are records that carry an IReadOnlyList, so the compiler-
        // generated record equality falls back to REFERENCE equality on that list — two independently
        // parsed-but-identical documents would then read as different. Compare structurally instead so
        // the classifier is correct regardless of how the two documents were constructed.
        private static bool TuningEquals(TreeTuningDef a, TreeTuningDef b) =>
            a.InitialLevel == b.InitialLevel &&
            a.UnlockCostStep == b.UnlockCostStep &&
            a.CumulativeBpThresholds.SequenceEqual(b.CumulativeBpThresholds);

        private static bool RequirementsEqual(NodeRequirementsDef a, NodeRequirementsDef b) =>
            a.RequiresCommittedTree == b.RequiresCommittedTree &&
            a.RequiresCurrentContentVersion == b.RequiresCurrentContentVersion &&
            a.MinActiveStoneLevel == b.MinActiveStoneLevel &&
            a.MinTreeLevel == b.MinTreeLevel &&
            a.RequiresActiveAttunement == b.RequiresActiveAttunement &&
            a.RequiresOfferedStatus == b.RequiresOfferedStatus &&
            a.RequiresDevelopmentAuthority == b.RequiresDevelopmentAuthority &&
            a.RequiresResponsibilityRange == b.RequiresResponsibilityRange &&
            a.PriorOfferedNodeIds.SequenceEqual(b.PriorOfferedNodeIds);

        /// <summary>True when this node's own version was bumped between baseline and edited.</summary>
        public static bool NodeVersionBumped(StoneContentDocument baseline, StoneContentDocument edited, string nodeId)
        {
            var b = baseline.Nodes.FirstOrDefault(n => n.Id == nodeId);
            var e = edited.Nodes.FirstOrDefault(n => n.Id == nodeId);
            return b != null && e != null && e.Version > b.Version;
        }
    }
}
