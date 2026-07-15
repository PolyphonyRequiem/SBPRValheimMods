using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.StoneProgression
{
    // T010 — Stone Facets + the pure CommitTreeToFacet transition (contracts.md §"CommitTreeToFacet";
    // data-model.md §"Commit Tree" / §"Aggregate 1" Facets/Tree palette). A Facet is a replaceable
    // Tree POSITION on a Stone (data-model.md FacetId: "Replaceable Tree position, not a relationship
    // or equipment slot"). The proof fixture authors exactly one Profession and one Martial Facet, each
    // empty or occupied by exactly one candidate eligible for its category/current palette.
    //
    // This file holds:
    //   * The authored Facet PALETTE (category + eligible candidate Trees + palette version) as
    //     immutable current-build content, keyed off the stable Tree/Facet identities in
    //     HomesteadProgressionCatalog. The palette version pins the current build so a stale
    //     paletteVersion in a command rejects rather than silently rebinding (ContentVersionMismatch).
    //   * The PURE CommitTreeToFacet transition: given the current Stone aggregate and an authored
    //     commit request, it validates Facet category, occupancy, palette/version eligibility, Active
    //     Stone Level capacity, and expected Stone revision, then PRODUCES the next Stone aggregate with
    //     exactly one Committed Tree added at its initial authored Tree Level and zero cumulative BP.
    //     It never mutates its input, never changes Historical/Active Stone Level, never touches a
    //     personal balance or purchase, and never journals (the durable commit lives in the application
    //     command layer, mirroring Relationships.cs / RelationshipCommands.cs).
    //
    // Governor authority + Responsibility Range are validated in the application command layer
    // (FacetCommands.cs) because they read the account–Stone authority index + the character's Bond
    // record; this pure transition takes only what it needs about the authenticated commit actor.
    //
    // net48 audit: engine-free (System.Collections.Generic + value objects + the snapshot codec). No
    // net5+ surface, no UnityEngine/Valheim/BepInEx reference: link-compiles into the net8 tests.

    /// <summary>Authored category of a Stone Facet. A candidate Tree fits a Facet only when their
    /// categories match (contracts.md FacetCategoryMismatch).</summary>
    public enum FacetCategory
    {
        None = 0,
        Profession = 1,
        Martial = 2
    }

    /// <summary>One authored Facet position: its stable id, category, and the eligible candidate Trees
    /// of that category in the current palette. Immutable current-build content.</summary>
    public sealed class FacetDefinition
    {
        public FacetDefinition(string facetId, FacetCategory category, IReadOnlyList<VersionedId> candidates)
        {
            FacetId = facetId ?? throw new ArgumentNullException(nameof(facetId));
            Category = category;
            Candidates = new ReadOnlyCollection<VersionedId>(new List<VersionedId>(
                candidates ?? throw new ArgumentNullException(nameof(candidates))));
        }

        public string FacetId { get; }
        public FacetCategory Category { get; }

        /// <summary>Eligible candidate Trees for this Facet in the current palette (stable key + version).</summary>
        public IReadOnlyList<VersionedId> Candidates { get; }

        /// <summary>True when <paramref name="tree"/> is a candidate here at the EXACT current-build
        /// version. A key match with a different version is NOT eligible (stale definition).</summary>
        public bool HasCandidate(VersionedId tree)
        {
            foreach (var c in Candidates)
                if (c.Equals(tree)) return true;
            return false;
        }

        /// <summary>True when <paramref name="tree"/>'s KEY is a candidate here at any version (used to
        /// distinguish a version mismatch on a known candidate from an unknown/ineligible Tree).</summary>
        public bool HasCandidateKey(VersionedId tree)
        {
            foreach (var c in Candidates)
                if (string.Equals(c.Key, tree.Key, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    /// <summary>The immutable authored Facet palette for the current proof build: exactly one Profession
    /// Facet (Cooking/Crafting candidates) and one Martial Facet (Archer/Warrior candidates). The palette
    /// version pins the current build; a command carrying a stale paletteVersion rejects.</summary>
    public sealed class StoneFacetPalette
    {
        /// <summary>Current authored palette version. Bumping this makes an older command's paletteVersion
        /// stale (ContentVersionMismatch), the same drift guard the content-registry version provides.</summary>
        public const int CurrentPaletteVersion = 1;

        private readonly ReadOnlyCollection<FacetDefinition> _facets;
        private readonly Dictionary<string, FacetDefinition> _byFacetId;

        private StoneFacetPalette(List<FacetDefinition> facets)
        {
            _facets = new ReadOnlyCollection<FacetDefinition>(facets);
            _byFacetId = new Dictionary<string, FacetDefinition>(StringComparer.Ordinal);
            foreach (var f in _facets)
                _byFacetId[f.FacetId] = f;
        }

        public int PaletteVersion => CurrentPaletteVersion;
        public IReadOnlyList<FacetDefinition> Facets => _facets;

        /// <summary>The authored current-build palette (data-model.md invariants: exactly one Profession
        /// and one Martial Facet in the proof fixture).</summary>
        public static StoneFacetPalette Current { get; } = new StoneFacetPalette(new List<FacetDefinition>
        {
            new FacetDefinition(HomesteadProgressionCatalog.ProfessionFacetId, FacetCategory.Profession,
                new[] { HomesteadProgressionCatalog.CookingTree, HomesteadProgressionCatalog.CraftingTree }),
            new FacetDefinition(HomesteadProgressionCatalog.MartialFacetId, FacetCategory.Martial,
                new[] { HomesteadProgressionCatalog.ArcherTree, HomesteadProgressionCatalog.WarriorTree }),
        });

        /// <summary>Resolve an authored Facet by id, or null when the id is not an authored Facet.</summary>
        public FacetDefinition? TryGetFacet(string facetId)
        {
            if (facetId == null) return null;
            return _byFacetId.TryGetValue(facetId, out var f) ? f : null;
        }

        /// <summary>The category a candidate Tree KEY belongs to across the whole palette, or None when
        /// the Tree key is not an authored candidate anywhere. Used to distinguish a category mismatch
        /// (known candidate, wrong Facet) from an ineligible Tree (unknown candidate).</summary>
        public FacetCategory CategoryOfTreeKey(VersionedId tree)
        {
            foreach (var f in _facets)
                if (f.HasCandidateKey(tree)) return f.Category;
            return FacetCategory.None;
        }
    }

    /// <summary>Result of a pure CommitTreeToFacet transition. On rejection <see cref="NextStone"/> is
    /// the UNCHANGED original aggregate (contracts.md: "Validation completes before commit. Failure
    /// changes nothing."), so a caller that commits it unconditionally still writes the prior state.</summary>
    public readonly struct FacetCommitTransition
    {
        private FacetCommitTransition(bool accepted, string resultCode,
            StoneProgressionAggregate nextStone, string facetId)
        {
            Accepted = accepted;
            ResultCode = resultCode;
            NextStone = nextStone;
            FacetId = facetId ?? string.Empty;
        }

        public bool Accepted { get; }
        public string ResultCode { get; }
        public StoneProgressionAggregate NextStone { get; }
        public string FacetId { get; }

        public static FacetCommitTransition Reject(string code, StoneProgressionAggregate stone) =>
            new FacetCommitTransition(false, code, stone, string.Empty);

        public static FacetCommitTransition Accept(StoneProgressionAggregate nextStone, string facetId) =>
            new FacetCommitTransition(true, "Applied", nextStone, facetId);
    }

    /// <summary>Pure Facet commitment transitions over the Stone aggregate. Every method validates the
    /// authored contract and returns the next state; none mutate their inputs, journal, change Stone
    /// Level, or grant a personal purchase.</summary>
    public static class StoneFacets
    {
        /// <summary>The initial authored Tree Level a freshly committed Tree starts at (data-model.md
        /// §"Commit Tree": "initial authored Tree Level"). Level 1 in the proof build.</summary>
        public const int InitialTreeLevel = 1;

        /// <summary>CommitTreeToFacet (contracts.md). Validates matching Facet category, empty Facet,
        /// eligible candidate/current palette, Active Stone Level capacity, and expected Stone revision,
        /// then produces the next Stone with exactly one Committed Tree added at its initial authored
        /// Tree Level and zero cumulative BP. It does not change Stone Level, personal balances, or
        /// purchase state (AT-NO-STONE-LEVEL-MUTATION). Governor authority + Responsibility Range are
        /// validated by the caller (application command layer).</summary>
        public static FacetCommitTransition CommitTreeToFacet(
            StoneProgressionAggregate stone,
            StoneFacetPalette palette,
            HomesteadProgressionCatalog catalog,
            string facetId,
            VersionedId tree,
            int paletteVersion,
            string commitOperationId,
            string commitActor,
            long? expectedStoneRevision = null)
        {
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            // Optimistic concurrency (CAS) first: a losing concurrent client changes nothing
            // (contracts.md StaleStoneRevision).
            if (expectedStoneRevision.HasValue && expectedStoneRevision.Value != stone.Revision)
                return FacetCommitTransition.Reject("StaleStoneRevision", stone);

            // The requested Facet must be an authored Facet on this build.
            var facet = palette.TryGetFacet(facetId);
            if (facet == null)
                return FacetCommitTransition.Reject("FacetCategoryMismatch", stone);

            // Current palette must match: a stale/unknown paletteVersion rejects rather than binding to
            // a "closest" palette (contracts.md ContentVersionMismatch).
            if (paletteVersion != palette.PaletteVersion)
                return FacetCommitTransition.Reject("ContentVersionMismatch", stone);

            // Category match: the candidate Tree's authored category must equal the requested Facet's
            // category. A Tree that is a KNOWN candidate but of a different category is a category
            // mismatch (e.g. committing Cooking to the Martial Facet); a Tree that is not a candidate
            // ANYWHERE is ineligible.
            var treeCategory = palette.CategoryOfTreeKey(tree);
            if (treeCategory == FacetCategory.None)
                return FacetCommitTransition.Reject("TreeNotEligible", stone);
            if (treeCategory != facet.Category)
                return FacetCommitTransition.Reject("FacetCategoryMismatch", stone);

            // Eligible candidate at the EXACT current-build version. Key present but wrong version is a
            // stale definition (ContentVersionMismatch); key absent from THIS Facet's palette (but a
            // matching category elsewhere is impossible here since category already matched) is
            // TreeNotEligible.
            if (!facet.HasCandidate(tree))
            {
                return facet.HasCandidateKey(tree)
                    ? FacetCommitTransition.Reject("ContentVersionMismatch", stone)
                    : FacetCommitTransition.Reject("TreeNotEligible", stone);
            }

            // Occupancy: the Facet must be empty. Exactly one Committed Tree per Facet.
            foreach (var committed in stone.CommittedTrees)
                if (string.Equals(committed.FacetId, facetId, StringComparison.Ordinal))
                    return FacetCommitTransition.Reject("FacetOccupied", stone);

            // One TreeId cannot occupy two Facets on the same Stone (data-model.md invariant).
            foreach (var committed in stone.CommittedTrees)
                if (string.Equals(committed.Tree.Key, tree.Key, StringComparison.Ordinal))
                    return FacetCommitTransition.Reject("TreeNotEligible", stone);

            // Active Stone Level capacity: the Stone must permit a Tree at the initial Tree Level.
            if (stone.ActiveStoneLevel < InitialTreeLevel)
                return FacetCommitTransition.Reject("ActiveStoneLevelTooLow", stone);

            // Produce the next Stone. Exactly one Committed Tree added at the initial authored Tree
            // Level and zero cumulative BP, with commit provenance. Every OTHER field — Historical AND
            // Active Stone Level, Mirrored AP, foundational identities, node development, personal
            // ledgers (which live on the character aggregate, untouched here) — is preserved verbatim.
            var newCommitted = new List<CommittedTreeRecord>(stone.CommittedTrees.Count + 1);
            newCommitted.AddRange(stone.CommittedTrees);
            newCommitted.Add(new CommittedTreeRecord(facetId, tree, commitOperationId ?? string.Empty,
                commitActor ?? string.Empty, InitialTreeLevel, cumulativeBpInvested: 0));

            var next = new StoneProgressionAggregate(
                stone.StoneId,
                stone.Revision + 1,
                stone.HistoricalStoneLevel,
                stone.ActiveStoneLevel,
                stone.FoundationalTree,
                stone.FoundationalCatalog,
                stone.ContentRegistryVersion,
                stone.CreatedProvenance,
                updatedProvenance: "commit:" + (commitOperationId ?? string.Empty),
                stone.MirroredStoneAp,
                stone.LastAppliedReceiptId,
                newCommitted,
                stone.NodeDevelopment,
                stone.Family,
                stone.Variant,
                stone.SchemaVersion);

            return FacetCommitTransition.Accept(next, facetId);
        }
    }
}
