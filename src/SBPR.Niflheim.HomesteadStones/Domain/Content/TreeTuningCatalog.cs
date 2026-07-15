using System;
using System.Collections.Generic;

namespace SBPR.Niflheim.HomesteadStones.Domain.Content
{
    // T012 — data-defined Tree tuning: cumulative BP thresholds + escalating unlock-cost policy
    // (data-model.md §"Core definitions": "Tree definitions: category, levels, cumulative BP
    // thresholds, escalating unlock-cost policy"; contracts.md ApplyBPToNode "the provisional
    // successive-unlock cost step"; §"Credit and spend BP on node development": "Crossing the
    // configurable cumulative threshold may advance Tree Level if Active Stone Level permits it.
    // Successive unlock costs may increase under the provisional data-defined curve.").
    //
    // This is the CONFIGURABLE tuning that the pure TreeDevelopment transition consumes. Every value
    // here is a provisional, explicitly configurable playtest number (Daniel design call), NOT a final
    // balance or compatibility lock. It is authored per Tree so:
    //   * AT-ESCALATING-COST-CONFIG — the Nth developed node in a Tree costs
    //     basePrice + unlockCostStep * (priorDevelopedNodesInThisTree). A different data-defined step
    //     yields a different curve; the transition reads it here rather than hard-coding it.
    //   * AT-TREE-ADVANCE-1-2 — a Tree advances to Level L exactly when cumulative qualifying BP
    //     investment crosses the authored Level-L threshold AND Active Stone Level permits it (the cap).
    //   * AT-NO-DIRECT-LEVEL-METER — Tree Level is a PURE FUNCTION of cumulative investment via these
    //     thresholds; there is no separate level meter, spend, or command. LevelForCumulative below is
    //     the only place a Level is decided, and it takes only cumulative investment + the Stone cap.
    //
    // net48 audit: only System / System.Collections.Generic. No net5+ surface, no UnityEngine/Valheim/
    // BepInEx reference, so this file link-compiles into the net8 test project.

    /// <summary>Authored tuning for one Tree: the cumulative BP investment thresholds that gate each
    /// Tree Level and the escalating successive-unlock cost step. Immutable current-build content.</summary>
    public sealed class TreeTuning
    {
        /// <summary>Cumulative-investment thresholds for Levels 2..N, ascending. Index i is the
        /// cumulative BP required to reach Level (i + 2). Level 1 is the initial authored level and
        /// requires no investment. An empty list means the Tree never advances past Level 1.</summary>
        private readonly int[] _levelThresholds;

        public TreeTuning(string treeKey, int initialTreeLevel, int unlockCostStep,
            IReadOnlyList<int> levelThresholds)
        {
            TreeKey = treeKey ?? throw new ArgumentNullException(nameof(treeKey));
            if (initialTreeLevel < 1) throw new ArgumentOutOfRangeException(nameof(initialTreeLevel));
            if (unlockCostStep < 0) throw new ArgumentOutOfRangeException(nameof(unlockCostStep));
            InitialTreeLevel = initialTreeLevel;
            UnlockCostStep = unlockCostStep;
            var src = levelThresholds ?? Array.Empty<int>();
            _levelThresholds = new int[src.Count];
            for (int i = 0; i < src.Count; i++)
            {
                _levelThresholds[i] = src[i];
                // Thresholds must be strictly ascending so LevelForCumulative is monotonic.
                if (i > 0 && _levelThresholds[i] <= _levelThresholds[i - 1])
                    throw new ArgumentException("Tree level thresholds must be strictly ascending: " + treeKey);
            }
        }

        public string TreeKey { get; }

        /// <summary>The authored initial Tree Level a freshly committed Tree starts at (Level 1).</summary>
        public int InitialTreeLevel { get; }

        /// <summary>Extra BP added to a node's authored base development price for each node ALREADY
        /// developed in this Tree (the successive-unlock cost step). Zero = flat authored price.</summary>
        public int UnlockCostStep { get; }

        /// <summary>Effective BP cost to develop a node whose authored base development price is
        /// <paramref name="baseNodePrice"/> when <paramref name="priorDevelopedInTree"/> nodes are
        /// already developed in this Tree. Pure, data-defined escalation (AT-ESCALATING-COST-CONFIG).</summary>
        public int EffectiveDevelopmentCost(int baseNodePrice, int priorDevelopedInTree)
        {
            if (baseNodePrice < 0) throw new ArgumentOutOfRangeException(nameof(baseNodePrice));
            if (priorDevelopedInTree < 0) throw new ArgumentOutOfRangeException(nameof(priorDevelopedInTree));
            return baseNodePrice + UnlockCostStep * priorDevelopedInTree;
        }

        /// <summary>The Tree Level implied by <paramref name="cumulativeBpInvested"/>, capped by
        /// <paramref name="activeStoneLevelCap"/>. This is the ONLY place a Tree Level is decided
        /// (AT-NO-DIRECT-LEVEL-METER): Level is a pure function of cumulative investment crossing the
        /// authored thresholds, then clamped so a Tree never exceeds the Active Stone Level.</summary>
        public int LevelForCumulative(int cumulativeBpInvested, int activeStoneLevelCap)
        {
            int level = InitialTreeLevel;
            for (int i = 0; i < _levelThresholds.Length; i++)
            {
                if (cumulativeBpInvested >= _levelThresholds[i])
                    level = InitialTreeLevel + i + 1;
                else
                    break;
            }
            if (level > activeStoneLevelCap) level = activeStoneLevelCap;
            if (level < InitialTreeLevel) level = InitialTreeLevel;
            return level;
        }

        /// <summary>The authored cumulative threshold for <paramref name="treeLevel"/>, or null when
        /// that level is not authored (Level 1 returns 0). Exposed for read models/tests.</summary>
        public int? CumulativeThresholdForLevel(int treeLevel)
        {
            if (treeLevel <= InitialTreeLevel) return 0;
            int idx = treeLevel - InitialTreeLevel - 1;
            if (idx < 0 || idx >= _levelThresholds.Length) return null;
            return _levelThresholds[idx];
        }
    }

    /// <summary>The immutable current-build Tree tuning registry. Provisional proof values (explicitly
    /// configurable, not a final balance lock): every Tree escalates successive unlock costs by +1 BP
    /// per already-developed node, and advances to Level 2 once cumulative qualifying investment
    /// reaches 3 BP (subject to the Active Stone Level cap). Bumping <see cref="CurrentTuningVersion"/>
    /// is the drift pin that makes an older command's tuningVersion stale.</summary>
    public sealed class TreeTuningCatalog
    {
        /// <summary>Current authored tuning version. A command carrying a stale tuningVersion rejects
        /// (ContentVersionMismatch), the same drift guard the content-registry/palette versions provide.</summary>
        public const int CurrentTuningVersion = 1;

        private readonly Dictionary<string, TreeTuning> _byTreeKey;

        private TreeTuningCatalog(IEnumerable<TreeTuning> tunings)
        {
            _byTreeKey = new Dictionary<string, TreeTuning>(StringComparer.Ordinal);
            foreach (var t in tunings)
                _byTreeKey[t.TreeKey] = t;
        }

        public int TuningVersion => CurrentTuningVersion;

        /// <summary>Resolve a Tree's tuning by stable key, or null when the Tree has no authored tuning
        /// in the current build.</summary>
        public TreeTuning? TryGetTuning(string treeKey)
        {
            if (treeKey == null) return null;
            return _byTreeKey.TryGetValue(treeKey, out var t) ? t : null;
        }

        /// <summary>The authored current-build tuning: one entry per authored Tree candidate. Provisional
        /// proof curve — step +1/node, Level 2 at cumulative 3.</summary>
        public static TreeTuningCatalog Current { get; } = new TreeTuningCatalog(new[]
        {
            NewTuning(HomesteadProgressionCatalog.CookingTree.Key),
            NewTuning(HomesteadProgressionCatalog.CraftingTree.Key),
            NewTuning(HomesteadProgressionCatalog.ArcherTree.Key),
            NewTuning(HomesteadProgressionCatalog.WarriorTree.Key),
        });

        private static TreeTuning NewTuning(string treeKey) =>
            new TreeTuning(treeKey, initialTreeLevel: 1, unlockCostStep: 1,
                levelThresholds: new[] { 3 }); // Level 2 at cumulative 3 BP
    }
}
