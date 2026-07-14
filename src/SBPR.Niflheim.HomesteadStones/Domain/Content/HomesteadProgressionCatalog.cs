using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.Content
{
    // Aggregate 4 — ContentRegistry (data-model.md §"Aggregate 4"). Immutable definitions selected by
    // the current proof build. This is the T005 authored roster: the exact 20-node first-build roster
    // (data-model.md §"Fixed first-build roster") plus the stable Tree/Facet/Foundational identities.
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

    /// <summary>One immutable authored node definition. Identity is <see cref="Node"/> (stable key +
    /// current-build version); everything else is authored content, not identity.</summary>
    public sealed class NodeDefinition
    {
        public NodeDefinition(VersionedId tree, VersionedId node, int treeLevel,
            NodeOutcomeType outcome, NodeOwnership ownership, NodeFirstBuildStatus status, string displayLabel)
        {
            Tree = tree;
            Node = node;
            TreeLevel = treeLevel;
            Outcome = outcome;
            Ownership = ownership;
            Status = status;
            DisplayLabel = displayLabel ?? string.Empty;
        }

        public VersionedId Tree { get; }
        public VersionedId Node { get; }
        public int TreeLevel { get; }
        public NodeOutcomeType Outcome { get; }
        public NodeOwnership Ownership { get; }
        public NodeFirstBuildStatus Status { get; }

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

            return new List<NodeDefinition>
            {
                // Cooking
                new NodeDefinition(CookingTree, new VersionedId("SavorTheHearth", 1), 1, NodeOutcomeType.LocalEffect, local, e, "Savor the Hearth"),
                new NodeDefinition(CookingTree, new VersionedId("FieldPrep", 1), 1, NodeOutcomeType.CharacterEffect, offered, e, "Field Prep"),
                new NodeDefinition(CookingTree, new VersionedId("IronStomach", 1), 1, NodeOutcomeType.PermanentEffect, offered, e, "Iron Stomach"),
                new NodeDefinition(CookingTree, new VersionedId("SwiftPreparation", 1), 2, NodeOutcomeType.CharacterEffect, offered, e, "Swift Preparation"),
                new NodeDefinition(CookingTree, new VersionedId("WatchfulCook", 1), 2, NodeOutcomeType.CharacterEffect, none, u, "Watchful Cook"),

                // Crafting
                new NodeDefinition(CraftingTree, new VersionedId("RefinedWorkshop", 1), 1, NodeOutcomeType.LocalEffect, local, e, "Refined Workshop"),
                new NodeDefinition(CraftingTree, new VersionedId("Masterwork", 1), 1, NodeOutcomeType.CharacterEffect, offered, e, "Masterwork"),
                new NodeDefinition(CraftingTree, new VersionedId("BuiltToLast", 1), 1, NodeOutcomeType.PermanentEffect, offered, e, "Built to Last"),
                new NodeDefinition(CraftingTree, new VersionedId("MeasuredCuts", 1), 1, NodeOutcomeType.CharacterEffect, none, u, "Measured Cuts"),
                new NodeDefinition(CraftingTree, new VersionedId("ArtisansCounter", 1), 1, NodeOutcomeType.LocalEffect, none, u, "Artisan's Counter"),

                // Archer
                new NodeDefinition(ArcherTree, new VersionedId("PracticeRange", 1), 1, NodeOutcomeType.LocalEffect, local, e, "Practice Range"),
                new NodeDefinition(ArcherTree, new VersionedId("FieldFletchingI", 1), 1, NodeOutcomeType.CharacterEffect, offered, e, "Field Fletching I"),
                new NodeDefinition(ArcherTree, new VersionedId("FletchersHabit", 1), 1, NodeOutcomeType.PermanentEffect, offered, e, "Fletcher's Habit"),
                new NodeDefinition(ArcherTree, new VersionedId("SteadyAim", 1), 1, NodeOutcomeType.CharacterEffect, none, u, "Steady Aim"),
                new NodeDefinition(ArcherTree, new VersionedId("BowyersLore", 1), 1, NodeOutcomeType.PermanentEffect, none, u, "Bowyer's Lore"),

                // Warrior
                new NodeDefinition(WarriorTree, new VersionedId("TwigTraining", 1), 1, NodeOutcomeType.LocalEffect, local, e, "T.W.I.G. Training"),
                new NodeDefinition(WarriorTree, new VersionedId("ReadyHands", 1), 1, NodeOutcomeType.CharacterEffect, offered, e, "Ready Hands"),
                new NodeDefinition(WarriorTree, new VersionedId("WeaponDiscipline", 1), 1, NodeOutcomeType.PermanentEffect, offered, e, "Weapon Discipline"),
                new NodeDefinition(WarriorTree, new VersionedId("ShrugItOffI", 1), 1, NodeOutcomeType.CharacterEffect, none, u, "Shrug It Off I"),
                new NodeDefinition(WarriorTree, new VersionedId("HeavyHands", 1), 1, NodeOutcomeType.CharacterEffect, none, u, "Heavy Hands"),
            };
        }
    }
}
