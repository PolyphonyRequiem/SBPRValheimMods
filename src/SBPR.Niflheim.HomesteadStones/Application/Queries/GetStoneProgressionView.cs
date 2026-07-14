using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
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
            IReadOnlyList<DerivedNodeStatus> nodeStatuses)
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

        // The projection never carries a client-authoritative ready flag (data-model.md). This is a
        // constant reminder in the contract surface, not a mutable field.
        public bool HasClientAuthoritativeReadyFlag => false;
    }

    public sealed class GetStoneProgressionView
    {
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
                view.Nodes);
        }
    }
}
