using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.Content
{
    // Aggregate 4 — ContentRegistry (data-model.md §"Aggregate 4"). Immutable definitions selected by
    // the current proof build. This is the T005 authored roster: the exact 20-node first-build roster
    // (data-model.md §"Fixed first-build roster") plus the stable Tree/Facet/Foundational identities
    // AND the authored AP/BP prices and development/personal requirements (data-model.md §"Core
    // definitions": "Node definitions: stable ID/version, Tree level, outcome type, first-build status,
    // AP/BP price, development requirements, personal requirements ...").
    //
    // Cardinal rules this file encodes (data-model.md modeling rules 3 and 6):
    //   * Stable IDs + current-build versions prevent same-build misbinding. Display names are NEVER
    //     identity — the Key is the stable content key, the display label is separate metadata.
    //   * Unknown same-build references reject clearly (ContentRegistryValidator). Production content
    //     migration/grandfathering/retirement is DEFERRED; incompatible unreleased fixtures may be
    //     RESET (ProgressionStateRepair), never silently reinterpreted.
    //
    // Arithmetic invariant (data-model.md): 20 authored nodes = 13 executable + seven unavailable. Of
    // the executable nodes, 12 are Level 1 and Swift Preparation is the sole executable Level-2 node.
    //
    // PROVISIONAL proof-only prices/requirements (Daniel design call 2026-07-14, tasks.md T006 note).
    // These are explicitly configurable playtest values, NOT final balance or compatibility locks:
    //   * Every executable node has authored BP development price = 1.
    //   * Every executable PERSONAL node has authored AP purchase price = 1.
    //   * Local (Stone-cultivated) nodes have NO AP purchase price — BP-only outcomes.
    //   * Unavailable nodes have NO AP/BP price and continue rejecting development/purchase/offering.
    //   * Requirements are the already-accepted gates only: committed Tree, current content/version,
    //     Active Stone Level >= node level, Tree Level >= node level, and (personal nodes) active
    //     Attunement + Offered status. Swift Preparation additionally requires Cooking Tree Level 2,
    //     Active Stone Level 2, and acquisition of both prior-Level-1 personal Cooking Offered Nodes
    //     (Field Prep + Iron Stomach). No additional objective/key/item requirements in this build.
    //
    // net48 audit: only System / System.Collections.Generic + the engine-free snapshot codec. No net5+
    // surface, no UnityEngine/Valheim/BepInEx reference, so this link-compiles into the net8 tests.

    /// <summary>The gameplay outcome a node delivers. Authored content identity, not a runtime flag.</summary>
    public enum NodeOutcomeType
    {
        LocalEffect,      // Stone-owned Local Effect, activated by BP development, no personal purchase
        CharacterEffect,  // personal, refundable-on-revocation Character Effect
        PermanentEffect   // personal durable Permanent Effect that survives relationship loss/revocation
    }

    /// <summary>Who owns the node's outcome and how it becomes available in the first build.</summary>
    public enum NodeOwnership
    {
        StoneCultivated,   // Stone-cultivated Local Node (developed, never purchased/Offered)
        PersonalOffered,   // personal node: developed -> Offered -> eligible attuned players purchase
        NoneWhileUnavailable // authored but unavailable in the first build: no ownership yet
    }

    /// <summary>Runtime/prototype first-build status. "Unavailable" is a real authored capability held
    /// out of the first build (spec User Story 4), not a name-only placeholder.</summary>
    public enum NodeFirstBuildStatus
    {
        Executable,
        Unavailable
    }

    /// <summary>Authored AP/BP price for a node. PROVISIONAL playtest values (Daniel 2026-07-14), not
    /// final balance. A null price means "no price of that kind": Local nodes have no AP purchase price;
    /// unavailable nodes have neither an AP nor a BP price.</summary>
    public readonly struct NodePricing
    {
        public NodePricing(int? developmentBpPrice, int? purchaseApPrice)
        {
            DevelopmentBpPrice = developmentBpPrice;
            PurchaseApPrice = purchaseApPrice;
        }

        /// <summary>BP cost to develop this node (Stone side). Null when the node has no BP price.</summary>
        public int? DevelopmentBpPrice { get; }

        /// <summary>Personal AP cost to purchase this node. Null for Local/unavailable nodes that are
        /// never purchased with Personal AP.</summary>
        public int? PurchaseApPrice { get; }

        /// <summary>No price of any kind (unavailable node).</summary>
        public static readonly NodePricing None = new NodePricing(null, null);
    }

    /// <summary>Authored development/personal requirements for a node. Only the already-accepted gates
    /// (Daniel 2026-07-14): committed Tree, current content/version, Active Stone Level, Tree Level,
    /// and (personal nodes) active Attunement + Offered status, plus an explicit prior-Offered-Set for
    /// Swift Preparation. No additional objective/key/item requirements in this proof build.</summary>
    public sealed class NodeRequirements
    {
        private static readonly ReadOnlyCollection<VersionedId> EmptyPriorSet =
            new ReadOnlyCollection<VersionedId>(new List<VersionedId>());

        public NodeRequirements(
            bool requiresCommittedTree,
            bool requiresCurrentContentVersion,
            int minActiveStoneLevel,
            int minTreeLevel,
            bool requiresActiveAttunement,
            bool requiresOfferedStatus,
            bool requiresDevelopmentAuthority,
            bool requiresResponsibilityRange,
            IReadOnlyList<VersionedId>? priorOfferedSet = null)
        {
            RequiresCommittedTree = requiresCommittedTree;
            RequiresCurrentContentVersion = requiresCurrentContentVersion;
            MinActiveStoneLevel = minActiveStoneLevel;
            MinTreeLevel = minTreeLevel;
            RequiresActiveAttunement = requiresActiveAttunement;
            RequiresOfferedStatus = requiresOfferedStatus;
            RequiresDevelopmentAuthority = requiresDevelopmentAuthority;
            RequiresResponsibilityRange = requiresResponsibilityRange;
            PriorOfferedSet = priorOfferedSet == null
                ? EmptyPriorSet
                : new ReadOnlyCollection<VersionedId>(new List<VersionedId>(priorOfferedSet));
        }

        public bool RequiresCommittedTree { get; }
        public bool RequiresCurrentContentVersion { get; }
        public int MinActiveStoneLevel { get; }
        public int MinTreeLevel { get; }
        public bool RequiresActiveAttunement { get; }
        public bool RequiresOfferedStatus { get; }

        /// <summary>Development/commit of this node requires the acting Governor to hold development
        /// authority over the committed Tree (data-model.md §Commit Tree / §"Provisional first-build
        /// prices and requirements": "the relevant relationship/authority/Responsibility Range"). True
        /// for every executable node; false for unavailable nodes that author no gates. Live authority
        /// state is supplied by T007 — this flag only records that the gate applies.</summary>
        public bool RequiresDevelopmentAuthority { get; }

        /// <summary>This node's development/spend must fall within the Governor's Responsibility Range
        /// (data-model.md §"Credit and spend BP on node development"). True for every executable node;
        /// false for unavailable nodes. Finer ranges are not authored here (T007 supplies live state).</summary>
        public bool RequiresResponsibilityRange { get; }

        /// <summary>Prior-Level same-Tree personal Offered Nodes that must already be acquired before
        /// this node is eligible (Swift Preparation: Field Prep + Iron Stomach). Empty for all others.</summary>
        public IReadOnlyList<VersionedId> PriorOfferedSet { get; }

        /// <summary>Inert requirements for an unavailable node: it authors no purchasable/developable
        /// gates and rejects development/purchase/offering/activation by its Unavailable status.</summary>
        public static readonly NodeRequirements Unavailable =
            new NodeRequirements(false, false, 0, 0, false, false, false, false, null);
    }

    /// <summary>One immutable authored node definition. Identity is <see cref="Node"/> (stable key +
    /// current-build version); everything else (outcome, prices, requirements, label) is authored
    /// content, not identity.</summary>
    public sealed class NodeDefinition
    {
        public NodeDefinition(VersionedId tree, VersionedId node, int treeLevel,
            NodeOutcomeType outcome, NodeOwnership ownership, NodeFirstBuildStatus status,
            NodePricing pricing, NodeRequirements requirements, string displayLabel)
        {
            Tree = tree;
            Node = node;
            TreeLevel = treeLevel;
            Outcome = outcome;
            Ownership = ownership;
            Status = status;
            Pricing = pricing;
            Requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));
            DisplayLabel = displayLabel ?? string.Empty;
        }

        public VersionedId Tree { get; }
        public VersionedId Node { get; }
        public int TreeLevel { get; }
        public NodeOutcomeType Outcome { get; }
        public NodeOwnership Ownership { get; }
        public NodeFirstBuildStatus Status { get; }

        /// <summary>Authored AP/BP price (provisional proof values).</summary>
        public NodePricing Pricing { get; }

        /// <summary>Authored development/personal requirements (accepted gates only).</summary>
        public NodeRequirements Requirements { get; }

        /// <summary>Human label for panels/logs. NEVER contract identity (data-model.md NodeId rule).</summary>
        public string DisplayLabel { get; }

        public bool IsExecutable => Status == NodeFirstBuildStatus.Executable;
    }

    /// <summary>The immutable current-build Homestead progression registry. Constructed once and never
    /// mutated: all collections are read-only. Callers resolve stable references through the validator,
    /// never by display name.</summary>
    public sealed class HomesteadProgressionCatalog
    {
        /// <summary>Current proof content-registry version. Bumping this is what makes an older persisted
        /// fixture an "incompatible unreleased fixture" eligible for explicit reset.</summary>
        public const int CurrentContentRegistryVersion = 1;

        public const int ExpectedAuthoredNodeCount = 20;
        public const int ExpectedExecutableNodeCount = 13;
        public const int ExpectedUnavailableNodeCount = 7;

        // Stable classification + Foundational identities (data-model.md §"Core definitions").
        public string Family { get; } = "Settlement";
        public string Variant { get; } = "Homestead";
        public VersionedId FoundationalTree { get; } = new VersionedId("FoundationalTree", 1);
        public VersionedId FoundationalCatalog { get; } = new VersionedId("FoundationalCatalog", 1);

        // Authored Facets in the proof fixture: exactly one Profession and one Martial (data-model.md
        // invariants). Facets are replaceable Tree positions, not identity of the committed Tree.
        public static readonly string ProfessionFacetId = "Profession";
        public static readonly string MartialFacetId = "Martial";

        // Authored Trees (stable keys + current-build version). Cooking/Crafting are Profession-category
        // candidates; Archer/Warrior are Martial-category candidates.
        public static readonly VersionedId CookingTree = new VersionedId("Cooking", 1);
        public static readonly VersionedId CraftingTree = new VersionedId("Crafting", 1);
        public static readonly VersionedId ArcherTree = new VersionedId("Archer", 1);
        public static readonly VersionedId WarriorTree = new VersionedId("Warrior", 1);

        // Immutable snapshot exposed to callers. Backed by a ReadOnlyCollection wrapper so a caller
        // cannot downcast the exposed Nodes to List<T> and mutate the supposedly immutable registry.
        private readonly ReadOnlyCollection<NodeDefinition> _nodes;
        private readonly Dictionary<string, NodeDefinition> _byNodeKey;

        public HomesteadProgressionCatalog()
        {
            _nodes = new ReadOnlyCollection<NodeDefinition>(BuildRoster());
            _byNodeKey = new Dictionary<string, NodeDefinition>(StringComparer.Ordinal);
            foreach (var n in _nodes)
            {
                var key = NodeKey(n.Node);
                if (_byNodeKey.ContainsKey(key))
                    throw new InvalidOperationException("Duplicate authored node key in current-build roster: " + key);
                _byNodeKey[key] = n;
            }
        }

        public int ContentRegistryVersion => CurrentContentRegistryVersion;
        public IReadOnlyList<NodeDefinition> Nodes => _nodes;

        /// <summary>Resolve a stable (tree, node) reference against the current build. Returns null when
        /// the node key is unknown OR the version does not match — the caller never binds a mismatch to
        /// a "closest" definition (data-model.md: unknown same-build references reject clearly).</summary>
        public NodeDefinition? TryResolveNode(VersionedId node)
        {
            if (node.IsNone) return null;
            if (!_byNodeKey.TryGetValue(NodeKey(node), out var def)) return null;
            // Key present but version differs -> stale/unknown definition, not a rebind.
            return def.Node.Version == node.Version ? def : null;
        }

        /// <summary>True when this key exists in the current build at ANY version (used to distinguish an
        /// UNKNOWN node from a VERSION-MISMATCH on a known node).</summary>
        public bool HasNodeKey(VersionedId node) =>
            !node.IsNone && _byNodeKey.ContainsKey(NodeKey(node));

        internal static string NodeKey(VersionedId node) => node.Key;

        // ── The fixed first-build roster (data-model.md §"Fixed first-build roster") ──
        private static List<NodeDefinition> BuildRoster()
        {
            var e = NodeFirstBuildStatus.Executable;
            var u = NodeFirstBuildStatus.Unavailable;
            var local = NodeOwnership.StoneCultivated;
            var offered = NodeOwnership.PersonalOffered;
            var none = NodeOwnership.NoneWhileUnavailable;

            // Provisional proof prices (Daniel 2026-07-14): executable node BP=1; executable personal
            // node AP=1; Local nodes have no AP price; unavailable nodes have no price.
            NodePricing localPrice = new NodePricing(developmentBpPrice: 1, purchaseApPrice: null);
            NodePricing personalPrice = new NodePricing(developmentBpPrice: 1, purchaseApPrice: 1);
            NodePricing noPrice = NodePricing.None;

            // Requirement factories: accepted gates only. Every executable node additionally gates on
            // development authority + Responsibility Range (data-model.md §"Provisional first-build
            // prices and requirements"); live authority state is supplied by T007.
            NodeRequirements LocalReq(int level) =>
                new NodeRequirements(true, true, level, level, false, false, true, true, null);
            NodeRequirements PersonalReq(int level, IReadOnlyList<VersionedId>? prior = null) =>
                new NodeRequirements(true, true, level, level, true, true, true, true, prior);
            NodeRequirements unavailableReq = NodeRequirements.Unavailable;

            // Swift Preparation's prior-Level-1 personal Cooking Offered Set: Field Prep + Iron Stomach.
            var swiftPriorSet = new[] { new VersionedId("FieldPrep", 1), new VersionedId("IronStomach", 1) };

            return new List<NodeDefinition>
            {
                // Cooking
                new NodeDefinition(CookingTree, new VersionedId("SavorTheHearth", 1), 1, NodeOutcomeType.LocalEffect, local, e, localPrice, LocalReq(1), "Savor the Hearth"),
                new NodeDefinition(CookingTree, new VersionedId("FieldPrep", 1), 1, NodeOutcomeType.CharacterEffect, offered, e, personalPrice, PersonalReq(1), "Field Prep"),
                new NodeDefinition(CookingTree, new VersionedId("IronStomach", 1), 1, NodeOutcomeType.PermanentEffect, offered, e, personalPrice, PersonalReq(1), "Iron Stomach"),
                new NodeDefinition(CookingTree, new VersionedId("SwiftPreparation", 1), 2, NodeOutcomeType.CharacterEffect, offered, e, personalPrice, PersonalReq(2, swiftPriorSet), "Swift Preparation"),
                new NodeDefinition(CookingTree, new VersionedId("WatchfulCook", 1), 2, NodeOutcomeType.CharacterEffect, none, u, noPrice, unavailableReq, "Watchful Cook"),

                // Crafting
                new NodeDefinition(CraftingTree, new VersionedId("RefinedWorkshop", 1), 1, NodeOutcomeType.LocalEffect, local, e, localPrice, LocalReq(1), "Refined Workshop"),
                new NodeDefinition(CraftingTree, new VersionedId("Masterwork", 1), 1, NodeOutcomeType.CharacterEffect, offered, e, personalPrice, PersonalReq(1), "Masterwork"),
                new NodeDefinition(CraftingTree, new VersionedId("BuiltToLast", 1), 1, NodeOutcomeType.PermanentEffect, offered, e, personalPrice, PersonalReq(1), "Built to Last"),
                new NodeDefinition(CraftingTree, new VersionedId("MeasuredCuts", 1), 1, NodeOutcomeType.CharacterEffect, none, u, noPrice, unavailableReq, "Measured Cuts"),
                new NodeDefinition(CraftingTree, new VersionedId("ArtisansCounter", 1), 1, NodeOutcomeType.LocalEffect, none, u, noPrice, unavailableReq, "Artisan's Counter"),

                // Archer
                new NodeDefinition(ArcherTree, new VersionedId("PracticeRange", 1), 1, NodeOutcomeType.LocalEffect, local, e, localPrice, LocalReq(1), "Practice Range"),
                new NodeDefinition(ArcherTree, new VersionedId("FieldFletchingI", 1), 1, NodeOutcomeType.CharacterEffect, offered, e, personalPrice, PersonalReq(1), "Field Fletching I"),
                new NodeDefinition(ArcherTree, new VersionedId("FletchersHabit", 1), 1, NodeOutcomeType.PermanentEffect, offered, e, personalPrice, PersonalReq(1), "Fletcher's Habit"),
                new NodeDefinition(ArcherTree, new VersionedId("SteadyAim", 1), 1, NodeOutcomeType.CharacterEffect, none, u, noPrice, unavailableReq, "Steady Aim"),
                new NodeDefinition(ArcherTree, new VersionedId("BowyersLore", 1), 1, NodeOutcomeType.PermanentEffect, none, u, noPrice, unavailableReq, "Bowyer's Lore"),

                // Warrior
                new NodeDefinition(WarriorTree, new VersionedId("TwigTraining", 1), 1, NodeOutcomeType.LocalEffect, local, e, localPrice, LocalReq(1), "T.W.I.G. Training"),
                new NodeDefinition(WarriorTree, new VersionedId("ReadyHands", 1), 1, NodeOutcomeType.CharacterEffect, offered, e, personalPrice, PersonalReq(1), "Ready Hands"),
                new NodeDefinition(WarriorTree, new VersionedId("WeaponDiscipline", 1), 1, NodeOutcomeType.PermanentEffect, offered, e, personalPrice, PersonalReq(1), "Weapon Discipline"),
                new NodeDefinition(WarriorTree, new VersionedId("ShrugItOffI", 1), 1, NodeOutcomeType.CharacterEffect, none, u, noPrice, unavailableReq, "Shrug It Off I"),
                new NodeDefinition(WarriorTree, new VersionedId("HeavyHands", 1), 1, NodeOutcomeType.CharacterEffect, none, u, noPrice, unavailableReq, "Heavy Hands"),
            };
        }
    }
}
