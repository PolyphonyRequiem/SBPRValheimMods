using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Application.Queries
{
    // Read contract `GetStoneProgressionView` (contracts.md §"Read contracts"). Produces the
    // ProgressionReadModel (data-model.md §"ProgressionReadModel"): one Stone-identity projection for
    // the temporary local panel and the future Stones UI. The projection is CALLER-SPECIFIC — the
    // per-caller balances and node statuses are filtered to the authenticated caller.
    //
    // T004 (AT-READMODEL-STONE-ID): returns the correct world-scoped Homestead identity plus a
    // caller-specific projection. Two different callers against the same Stone share the Stone-identity
    // section but get their own AP/BP/Facet-Credit and derived node statuses. The projection NEVER
    // contains a client-authoritative ready flag; command affordances are hints only and commands
    // revalidate current state (contracts.md).
    //
    // T005/T006: the projection additionally reports each authored node's EXACT price and requirements
    // from the immutable current-build registry (contracts.md §"GetStoneProgressionView": "each node's
    // exact outcome, status, price, requirements ..."; spec US1/US3 acceptance scenario 1). These
    // authored values are provisional proof-only playtest data (Daniel design call 2026-07-14), not
    // final balance, but the read model reports them verbatim so an inspector sees exact values.
    //
    // net48 audit: engine-free. Link-compiles into net8 tests.

    /// <summary>Caller-specific balances at one Stone.</summary>
    public readonly struct CallerBalances
    {
        public CallerBalances(int personalAp, int cumulativeAp, int personalBp, int totalFacetCredit)
        {
            PersonalAp = personalAp;
            CumulativeAp = cumulativeAp;
            PersonalBp = personalBp;
            TotalFacetCredit = totalFacetCredit;
        }

        public int PersonalAp { get; }
        public int CumulativeAp { get; }
        public int PersonalBp { get; }
        public int TotalFacetCredit { get; }
    }

    /// <summary>One authored node's exact registry-reported identity, status, price, and requirements.
    /// This is the current-build definition surface the read model exposes for inspection (contracts.md;
    /// spec US1/US3 acceptance scenario 1). Values are authored provisional proof data, never a
    /// client-authoritative outcome.</summary>
    public sealed class NodeCatalogEntry
    {
        public NodeCatalogEntry(
            VersionedId tree, VersionedId node, int treeLevel,
            NodeOutcomeType outcome, NodeOwnership ownership, NodeFirstBuildStatus status,
            int? developmentBpPrice, int? purchaseApPrice,
            bool requiresCommittedTree, bool requiresCurrentContentVersion,
            int minActiveStoneLevel, int minTreeLevel,
            bool requiresActiveAttunement, bool requiresOfferedStatus,
            bool requiresDevelopmentAuthority, bool requiresResponsibilityRange,
            IReadOnlyList<VersionedId> priorOfferedSet, string displayLabel)
        {
            Tree = tree;
            Node = node;
            TreeLevel = treeLevel;
            Outcome = outcome;
            Ownership = ownership;
            Status = status;
            DevelopmentBpPrice = developmentBpPrice;
            PurchaseApPrice = purchaseApPrice;
            RequiresCommittedTree = requiresCommittedTree;
            RequiresCurrentContentVersion = requiresCurrentContentVersion;
            MinActiveStoneLevel = minActiveStoneLevel;
            MinTreeLevel = minTreeLevel;
            RequiresActiveAttunement = requiresActiveAttunement;
            RequiresOfferedStatus = requiresOfferedStatus;
            RequiresDevelopmentAuthority = requiresDevelopmentAuthority;
            RequiresResponsibilityRange = requiresResponsibilityRange;
            PriorOfferedSet = priorOfferedSet;
            DisplayLabel = displayLabel ?? string.Empty;
        }

        public VersionedId Tree { get; }
        public VersionedId Node { get; }
        public int TreeLevel { get; }
        public NodeOutcomeType Outcome { get; }
        public NodeOwnership Ownership { get; }
        public NodeFirstBuildStatus Status { get; }

        // Exact authored prices (null = no price of that kind).
        public int? DevelopmentBpPrice { get; }
        public int? PurchaseApPrice { get; }

        // Exact authored requirements (accepted gates only).
        public bool RequiresCommittedTree { get; }
        public bool RequiresCurrentContentVersion { get; }
        public int MinActiveStoneLevel { get; }
        public int MinTreeLevel { get; }
        public bool RequiresActiveAttunement { get; }
        public bool RequiresOfferedStatus { get; }

        /// <summary>Development requires the acting Governor's development authority over the committed
        /// Tree (data-model.md §"Provisional first-build prices and requirements"). True for executable
        /// nodes; false for unavailable ones. Live authority state is T007 scope.</summary>
        public bool RequiresDevelopmentAuthority { get; }

        /// <summary>Development/spend must fall within the Governor's Responsibility Range. True for
        /// executable nodes; false for unavailable ones. Finer ranges are T007 scope.</summary>
        public bool RequiresResponsibilityRange { get; }

        public IReadOnlyList<VersionedId> PriorOfferedSet { get; }

        public string DisplayLabel { get; }

        internal static NodeCatalogEntry FromDefinition(NodeDefinition d) =>
            new NodeCatalogEntry(
                d.Tree, d.Node, d.TreeLevel, d.Outcome, d.Ownership, d.Status,
                d.Pricing.DevelopmentBpPrice, d.Pricing.PurchaseApPrice,
                d.Requirements.RequiresCommittedTree, d.Requirements.RequiresCurrentContentVersion,
                d.Requirements.MinActiveStoneLevel, d.Requirements.MinTreeLevel,
                d.Requirements.RequiresActiveAttunement, d.Requirements.RequiresOfferedStatus,
                d.Requirements.RequiresDevelopmentAuthority, d.Requirements.RequiresResponsibilityRange,
                d.Requirements.PriorOfferedSet, d.DisplayLabel);
    }

    public sealed class ProgressionReadModel
    {
        public ProgressionReadModel(
            StoneId stoneId,
            string family,
            string variant,
            long stoneRevision,
            int contentRegistryVersion,
            long characterRevision,
            long authorityRevision,
            int historicalStoneLevel,
            int activeStoneLevel,
            VersionedId foundationalTree,
            RelationshipKind callerRelationship,
            CallerBalances callerBalances,
            IReadOnlyList<DerivedNodeStatus> nodeStatuses,
            IReadOnlyList<NodeCatalogEntry> nodeCatalog)
        {
            StoneId = stoneId;
            Family = family;
            Variant = variant;
            StoneRevision = stoneRevision;
            ContentRegistryVersion = contentRegistryVersion;
            CharacterRevision = characterRevision;
            AuthorityRevision = authorityRevision;
            HistoricalStoneLevel = historicalStoneLevel;
            ActiveStoneLevel = activeStoneLevel;
            FoundationalTree = foundationalTree;
            CallerRelationship = callerRelationship;
            CallerBalances = callerBalances;
            NodeStatuses = nodeStatuses;
            NodeCatalog = nodeCatalog;
        }

        // World-scoped Homestead identity (never a display name, ZDOID, or minted GUID).
        public StoneId StoneId { get; }
        public string Family { get; }
        public string Variant { get; }

        // Current revisions + registry versions.
        public long StoneRevision { get; }
        public int ContentRegistryVersion { get; }
        public long CharacterRevision { get; }
        public long AuthorityRevision { get; }

        // Levels + Foundation.
        public int HistoricalStoneLevel { get; }
        public int ActiveStoneLevel { get; }
        public VersionedId FoundationalTree { get; }

        // Caller-specific projection.
        public RelationshipKind CallerRelationship { get; }
        public CallerBalances CallerBalances { get; }
        public IReadOnlyList<DerivedNodeStatus> NodeStatuses { get; }

        /// <summary>Exact authored current-build definition of every node — identity/version, outcome,
        /// status, price, and requirements (contracts.md §"GetStoneProgressionView"; spec US1/US3
        /// acceptance scenario 1). Reported verbatim from the immutable registry.</summary>
        public IReadOnlyList<NodeCatalogEntry> NodeCatalog { get; }

        // The projection never carries a client-authoritative ready flag (data-model.md). This is a
        // constant reminder in the contract surface, not a mutable field.
        public bool HasClientAuthoritativeReadyFlag => false;
    }

    public sealed class GetStoneProgressionView
    {
        private readonly HomesteadProgressionCatalog _catalog;

        /// <summary>Default: build against the current-build registry.</summary>
        public GetStoneProgressionView() : this(new HomesteadProgressionCatalog()) { }

        public GetStoneProgressionView(HomesteadProgressionCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>Build the caller-specific read model from current aggregate snapshots. Pure
        /// projection: it derives activation on the fly and stores nothing (contracts.md: "The server
        /// must revalidate commands even if the view reported an operation as available").</summary>
        public ProgressionReadModel Execute(
            StoneProgressionAggregate stone,
            CharacterProgressionAggregate caller,
            AccountStoneAuthorityIndex authority)
        {
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (authority == null) throw new ArgumentNullException(nameof(authority));

            // Caller-specific balances: sum the caller's own record at THIS Stone only.
            int personalAp = 0, cumulativeAp = 0, personalBp = 0, facetCredit = 0;
            foreach (var sr in caller.StoneRecords)
            {
                if (!sr.StoneId.Equals(stone.StoneId)) continue;
                personalAp += sr.PersonalAp;
                cumulativeAp += sr.CumulativeAp;
                personalBp += sr.PersonalBp;
                foreach (var fc in sr.FacetCredits) facetCredit += fc.Amount;
            }

            RelationshipKind callerRelationship =
                authority.ActiveCharacter.Equals(caller.Character) ? authority.ActiveKind : RelationshipKind.None;

            var view = DerivedActivationView.Derive(stone, caller, authority);

            // Exact authored node definitions (price + requirements) from the immutable current-build
            // registry, in stable roster order. Reported verbatim for inspection.
            var catalogEntries = new List<NodeCatalogEntry>(_catalog.Nodes.Count);
            foreach (var def in _catalog.Nodes)
                catalogEntries.Add(NodeCatalogEntry.FromDefinition(def));

            return new ProgressionReadModel(
                stone.StoneId,
                stone.Family,
                stone.Variant,
                stone.Revision,
                stone.ContentRegistryVersion,
                caller.Revision,
                authority.Revision,
                stone.HistoricalStoneLevel,
                stone.ActiveStoneLevel,
                stone.FoundationalTree,
                callerRelationship,
                new CallerBalances(personalAp, cumulativeAp, personalBp, facetCredit),
                view.Nodes,
                new ReadOnlyCollection<NodeCatalogEntry>(catalogEntries));
        }
    }
}
