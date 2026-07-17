using System;
using System.Collections.Generic;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using StoneContent.Workbench.Core.Model;

namespace StoneContent.Workbench.Tests
{
    // Task 1 — the current-authority snapshot adapter. It reads the SHIPPED production catalogs
    // (HomesteadProgressionCatalog / FoundationalPieceCatalog / StoneFacetPalette) through their
    // PUBLIC interfaces and normalizes them into the same StoneContentDocument the declarative asset
    // loads into. No source parsing, no copied second catalog — the parity comparison is therefore
    // against the real executable authority.
    //
    // T012 note: TreeTuning is a HELD review branch (wt/t_c7313d0f), NOT on current main. This adapter
    // reads only the three current-main axes. The Tree tuning it emits is the POC's authored proposal,
    // flagged by the parity reporter as "held-branch reference / current-main NOT APPLICABLE" — it is
    // never read back from a production TreeTuningCatalog that does not exist on main.
    internal static class CurrentCatalogSnapshotAdapter
    {
        // The POC-authored Tree tuning proposal (matches held-T012 wt/t_c7313d0f: step +1/node,
        // Level 2 at cumulative 3 BP). Held-branch reference only; not sourced from current main.
        private static TreeTuningDef HeldTuningReference() =>
            new TreeTuningDef(InitialLevel: 1, UnlockCostStep: 1, CumulativeBpThresholds: new[] { 3 });

        public static StoneContentDocument Build()
        {
            var catalog = new HomesteadProgressionCatalog();
            var foundational = FoundationalPieceCatalog.CurrentBuild;
            var palette = StoneFacetPalette.Current;

            var facets = palette.Facets
                .Select(f => new FacetDef(
                    f.FacetId,
                    f.Category.ToString(),
                    f.Candidates.Select(c => c.Key).ToList()))
                .ToList();

            // Trees, in authored candidate order (Profession then Martial, each palette's order).
            var trees = new List<TreeDef>();
            foreach (var f in palette.Facets)
                foreach (var c in f.Candidates)
                    trees.Add(new TreeDef(c.Key, c.Version, f.Category.ToString(), HeldTuningReference()));

            var nodes = catalog.Nodes.Select(ToNodeDef).ToList();

            return new StoneContentDocument(
                FormatVersion: 1,
                AssetId: "niflheim.homestead-stone.progression",
                Family: catalog.Family,
                Variant: catalog.Variant,
                Versions: new VersionPins(
                    ContentRegistry: catalog.ContentRegistryVersion,
                    FoundationalCatalog: foundational.CatalogVersion,
                    FacetPalette: palette.PaletteVersion,
                    TreeTuning: TreeTuningCatalogReferenceVersion),
                Foundational: new FoundationalSection(
                    new VersionedRef(catalog.FoundationalTree.Key, catalog.FoundationalTree.Version),
                    new FoundationalCatalogDef(
                        foundational.CatalogId,
                        foundational.CatalogVersion,
                        foundational.CatalogVersionTag,
                        foundational.Members.ToList(),
                        foundational.Exclusions.ToList())),
                Facets: facets,
                Trees: trees,
                Nodes: nodes);
        }

        // Held-T012 tuning version (wt/t_c7313d0f TreeTuningCatalog.CurrentTuningVersion == 1).
        public const int TreeTuningCatalogReferenceVersion = 1;

        private static NodeDef ToNodeDef(NodeDefinition n) =>
            new NodeDef(
                Id: n.Node.Key,
                Version: n.Node.Version,
                TreeId: n.Tree.Key,
                TreeLevel: n.TreeLevel,
                DisplayLabel: n.DisplayLabel,
                OutcomeType: n.Outcome.ToString(),
                Ownership: n.Ownership.ToString(),
                FirstBuildStatus: n.Status.ToString(),
                Pricing: new NodePricingDef(n.Pricing.DevelopmentBpPrice, n.Pricing.PurchaseApPrice),
                Requirements: new NodeRequirementsDef(
                    n.Requirements.RequiresCommittedTree,
                    n.Requirements.RequiresCurrentContentVersion,
                    n.Requirements.MinActiveStoneLevel,
                    n.Requirements.MinTreeLevel,
                    n.Requirements.RequiresActiveAttunement,
                    n.Requirements.RequiresOfferedStatus,
                    n.Requirements.RequiresDevelopmentAuthority,
                    n.Requirements.RequiresResponsibilityRange,
                    n.Requirements.PriorOfferedSet.Select(p => p.Key).ToList()));
    }
}
