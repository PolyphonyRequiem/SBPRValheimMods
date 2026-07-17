using System;
using System.Collections.Generic;

namespace StoneContent.Workbench.Core.Model
{
    // The canonical AUTHORED document. Plain immutable records that hold authored INTENT only
    // (decision-map #2): root identity, four explicit human-authored version pins, the Foundational
    // Tree/catalog + ordered members + explicit exclusions, the Facet palette, the Trees + held-T012
    // tuning, and the node roster. It deliberately does NOT model derived counts, lookup dictionaries,
    // helper factories, runtime state, repair/rejection policy, or UI state — those stay hand-written.
    //
    // These records are also the single semantic snapshot the parity adapter and the generator both
    // normalize to, so parity is behavioral (field-by-field) rather than formatting-dependent.

    public sealed record StoneContentDocument(
        int FormatVersion,
        string AssetId,
        string Family,
        string Variant,
        VersionPins Versions,
        FoundationalSection Foundational,
        IReadOnlyList<FacetDef> Facets,
        IReadOnlyList<TreeDef> Trees,
        IReadOnlyList<NodeDef> Nodes);

    public sealed record VersionPins(
        int ContentRegistry,
        int FoundationalCatalog,
        int FacetPalette,
        int TreeTuning);

    public sealed record VersionedRef(string Id, int Version);

    public sealed record FoundationalSection(
        VersionedRef Tree,
        FoundationalCatalogDef Catalog);

    public sealed record FoundationalCatalogDef(
        string Id,
        int Version,
        string VersionTag,
        IReadOnlyList<string> Members,
        IReadOnlyList<string> Exclusions);

    public sealed record FacetDef(
        string Id,
        string Category,
        IReadOnlyList<string> CandidateTreeIds);

    public sealed record TreeDef(
        string Id,
        int Version,
        string Category,
        TreeTuningDef Tuning);

    public sealed record TreeTuningDef(
        int InitialLevel,
        int UnlockCostStep,
        IReadOnlyList<int> CumulativeBpThresholds);

    public sealed record NodePricingDef(
        int? DevelopmentBp,
        int? PurchaseAp);

    public sealed record NodeRequirementsDef(
        bool RequiresCommittedTree,
        bool RequiresCurrentContentVersion,
        int MinActiveStoneLevel,
        int MinTreeLevel,
        bool RequiresActiveAttunement,
        bool RequiresOfferedStatus,
        bool RequiresDevelopmentAuthority,
        bool RequiresResponsibilityRange,
        IReadOnlyList<string> PriorOfferedNodeIds);

    public sealed record NodeDef(
        string Id,
        int Version,
        string TreeId,
        int TreeLevel,
        string DisplayLabel,
        string OutcomeType,
        string Ownership,
        string FirstBuildStatus,
        NodePricingDef Pricing,
        NodeRequirementsDef Requirements);
}
