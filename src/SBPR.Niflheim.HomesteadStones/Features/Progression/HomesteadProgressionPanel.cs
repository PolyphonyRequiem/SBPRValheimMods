using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    // T010 — panel/read affordances for Facet commitment (contracts.md: "command affordances are HINTS
    // ONLY; commands revalidate current state"; tasks.md T010 acceptance: "panel/read affordances remain
    // hints only"). This produces the inspectable, purely-derived view a temporary local panel / future
    // Stones UI renders for the Profession and Martial Facets: which Facet is occupied by which Committed
    // Tree, and — for an empty Facet — which candidate Trees are authored as commit hints.
    //
    // Load-bearing boundary (AT-NO-STONE-LEVEL-MUTATION / contracts.md read model): NOTHING here is a
    // client-authoritative "ready" or "can-commit" flag. The candidate list is authored content, not a
    // permission; the actual CommitTreeToFacet command re-validates Governor authority, Responsibility
    // Range, category, occupancy, palette version, Active Stone Level, and revision server-side every
    // time. This projection carries no mutable authority and is a pure function of the Stone aggregate +
    // authored palette — it can never become a second source of truth.
    //
    // net48 audit: engine-free (System.Collections.Generic + value objects + palette). No net5+ surface,
    // no UnityEngine/Valheim/BepInEx reference: link-compiles into the net8 test project.

    /// <summary>One authored candidate Tree offered as a commit HINT for an empty Facet. Authored
    /// content — never a permission or a client-authoritative ready flag.</summary>
    public readonly struct FacetCandidateHint
    {
        public FacetCandidateHint(VersionedId tree, string displayLabel)
        {
            Tree = tree;
            DisplayLabel = displayLabel ?? string.Empty;
        }

        public VersionedId Tree { get; }

        /// <summary>Human label for the panel. NEVER contract identity (data-model.md TreeId rule).</summary>
        public string DisplayLabel { get; }
    }

    /// <summary>One Facet's derived panel row: its authored id/category, whether it is occupied, the
    /// Committed Tree occupying it (when occupied), and the authored candidate commit hints (when
    /// empty). Pure projection; carries no mutable authority.</summary>
    public sealed class FacetPanelRow
    {
        public FacetPanelRow(string facetId, FacetCategory category, bool occupied,
            VersionedId committedTree, int committedTreeLevel,
            IReadOnlyList<FacetCandidateHint> candidateHints)
        {
            FacetId = facetId ?? string.Empty;
            Category = category;
            Occupied = occupied;
            CommittedTree = committedTree;
            CommittedTreeLevel = committedTreeLevel;
            CandidateHints = new ReadOnlyCollection<FacetCandidateHint>(
                new List<FacetCandidateHint>(candidateHints ?? Array.Empty<FacetCandidateHint>()));
        }

        public string FacetId { get; }
        public FacetCategory Category { get; }
        public bool Occupied { get; }

        /// <summary>The Committed Tree occupying this Facet, or <see cref="VersionedId.None"/> when empty.</summary>
        public VersionedId CommittedTree { get; }

        /// <summary>The occupying Tree's current Tree Level, or 0 when the Facet is empty.</summary>
        public int CommittedTreeLevel { get; }

        /// <summary>Authored candidate Trees offered as commit hints for an EMPTY Facet (empty when the
        /// Facet is already occupied). Hints only — the command re-validates authority server-side.</summary>
        public IReadOnlyList<FacetCandidateHint> CandidateHints { get; }
    }

    /// <summary>Derives the hints-only Facet panel view from the current Stone aggregate + authored
    /// palette + catalog. The ONLY constructor is a static factory over persisted state, so the view can
    /// never exist except as a pure function of the aggregates (never a second authority).</summary>
    public sealed class HomesteadProgressionPanel
    {
        private readonly List<FacetPanelRow> _facets;

        private HomesteadProgressionPanel(int activeStoneLevel, List<FacetPanelRow> facets)
        {
            ActiveStoneLevel = activeStoneLevel;
            _facets = facets;
        }

        /// <summary>Active Stone Level from the Stone aggregate. Rendered as an informational hint (a
        /// commit still re-checks Active Stone Level capacity server-side).</summary>
        public int ActiveStoneLevel { get; }

        public IReadOnlyList<FacetPanelRow> Facets => _facets;

        public FacetPanelRow? FacetFor(string facetId)
        {
            foreach (var row in _facets)
                if (string.Equals(row.FacetId, facetId, StringComparison.Ordinal))
                    return row;
            return null;
        }

        public static HomesteadProgressionPanel Derive(
            StoneProgressionAggregate stone,
            StoneFacetPalette palette,
            HomesteadProgressionCatalog catalog)
        {
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            // Index the current commitments by Facet.
            var committedByFacet = new Dictionary<string, CommittedTreeRecord>(StringComparer.Ordinal);
            foreach (var committed in stone.CommittedTrees)
                committedByFacet[committed.FacetId] = committed;

            var rows = new List<FacetPanelRow>();
            foreach (var facet in palette.Facets)
            {
                if (committedByFacet.TryGetValue(facet.FacetId, out var committed))
                {
                    // Occupied: no commit hints (exactly one Committed Tree per Facet).
                    rows.Add(new FacetPanelRow(facet.FacetId, facet.Category, occupied: true,
                        committedTree: committed.Tree, committedTreeLevel: committed.TreeLevel,
                        candidateHints: Array.Empty<FacetCandidateHint>()));
                }
                else
                {
                    // Empty: offer the authored candidate Trees as HINTS (labels from the catalog).
                    var hints = new List<FacetCandidateHint>();
                    foreach (var candidate in facet.Candidates)
                        hints.Add(new FacetCandidateHint(candidate, LabelForTree(catalog, candidate)));
                    // (catalog reserved for future per-Tree label metadata; see LabelForTree.)

                    rows.Add(new FacetPanelRow(facet.FacetId, facet.Category, occupied: false,
                        committedTree: VersionedId.None, committedTreeLevel: 0, candidateHints: hints));
                }
            }

            return new HomesteadProgressionPanel(stone.ActiveStoneLevel, rows);
        }

        /// <summary>A human label for a candidate Tree, sourced from the first authored node that belongs
        /// to it (the catalog carries node labels, not Tree labels). Falls back to the stable key. Label
        /// is panel metadata only, never identity.</summary>
        private static string LabelForTree(HomesteadProgressionCatalog catalog, VersionedId tree)
        {
            foreach (var node in catalog.Nodes)
                if (node.Tree.Equals(tree))
                    return tree.Key;
            return tree.Key;
        }
    }
}
