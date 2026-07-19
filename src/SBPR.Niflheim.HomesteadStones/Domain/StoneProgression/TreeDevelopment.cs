using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.StoneProgression
{
    // T012 — BP-driven node development + Tree advancement pure transitions (contracts.md §"ApplyBPToNode";
    // data-model.md §"Credit and spend BP on node development"; spec FR-011/FR-020, SC-003). This is the
    // Stone-side PURE transition: given the current Stone aggregate + the authored content registry + a
    // configurable development policy, it validates the accepted development contract and PRODUCES the
    // next Stone aggregate with:
    //   * the node's development advanced by the applied delta (progress toward its authored/escalated
    //     total cost; completion at cost, not the first partial spend);
    //   * the SAME delta added to that committed Tree's cumulative qualifying investment (data-model.md:
    //     "every accepted BP delta advances node progress and the same delta in cumulative owning-Tree
    //     investment");
    //   * ordinary Tree Level advancement when cumulative qualifying investment crosses the CONFIGURABLE
    //     threshold AND Active Stone Level permits it (spec SC-003, FR-011). There is NO separate
    //     Tree-level meter, wallet, or direct-invest command — the ONLY way TreeLevel changes is this
    //     threshold crossing (AT-NO-DIRECT-LEVEL-METER).
    //
    // The BP DEBIT itself is applied to the character aggregate by the pure BondPower transition; the
    // application command layer (DevelopmentCommands.ApplyBPToNode) commits BOTH the character BP debit
    // and this Stone node/investment/level delta under ONE durable receipt. This transition never
    // mutates its input, never journals, never touches a personal balance/purchase, and never advances
    // Tree Level above the Active Stone Level cap.
    //
    // Successive-unlock cost escalation and the Tree-Level thresholds are CONFIGURABLE playtest data
    // (spec §"configurable", FR-011: "Successive unlock costs and Tree-level thresholds MUST remain
    // configurable playtest data"). TreeDevelopmentConfig carries them; the provisional defaults live in
    // TreeDevelopmentConfig.Default. A node's total BP cost is FROZEN at first spend from the escalating
    // curve (based on how many nodes are already developed in that Tree), so a partial development
    // sequence has a stable target.
    //
    // net48 audit: engine-free (System.Collections.Generic + value objects + content catalog). No net5+
    // surface, no UnityEngine/Valheim/BepInEx reference: link-compiles into the net8 test project.

    /// <summary>Configurable successive-unlock cost curve + Tree-Level thresholds (spec FR-011: these are
    /// configurable playtest data, not fixed proof values). All fields are data; changing them changes
    /// costs/advancement (AT-ESCALATING-COST-CONFIG). Engine-free value object.</summary>
    public sealed class TreeDevelopmentConfig
    {
        public TreeDevelopmentConfig(int unlockCostStep, IReadOnlyDictionary<int, int>? cumulativeThresholdByTargetLevel)
        {
            if (unlockCostStep < 0) throw new ArgumentOutOfRangeException(nameof(unlockCostStep));
            UnlockCostStep = unlockCostStep;
            var map = new Dictionary<int, int>();
            if (cumulativeThresholdByTargetLevel != null)
                foreach (var kv in cumulativeThresholdByTargetLevel) map[kv.Key] = kv.Value;
            _thresholds = map;
        }

        private readonly Dictionary<int, int> _thresholds;

        /// <summary>Escalation step added per already-developed node in the same Tree. Provisional
        /// default 1: the Nth node an authored base cost of B costs B + step*(developed-so-far).</summary>
        public int UnlockCostStep { get; }

        /// <summary>The escalated TOTAL BP cost to develop a node whose authored base cost is
        /// <paramref name="authoredBaseCost"/> when <paramref name="developedNodeCountInTree"/> nodes
        /// are already developed (completed) in that Tree. Provisional linear curve: base + step*count.</summary>
        public int NodeUnlockCost(int authoredBaseCost, int developedNodeCountInTree)
        {
            if (authoredBaseCost < 0) throw new ArgumentOutOfRangeException(nameof(authoredBaseCost));
            if (developedNodeCountInTree < 0) throw new ArgumentOutOfRangeException(nameof(developedNodeCountInTree));
            long cost = (long)authoredBaseCost + (long)UnlockCostStep * developedNodeCountInTree;
            return cost > int.MaxValue ? int.MaxValue : (int)cost;
        }

        /// <summary>Cumulative qualifying BP investment required to advance a Tree to
        /// <paramref name="targetLevel"/>, or null when no threshold is authored for that level (the
        /// Tree cannot advance to it in this build). Data-defined and configurable.</summary>
        public int? CumulativeThresholdForLevel(int targetLevel)
        {
            return _thresholds.TryGetValue(targetLevel, out var t) ? t : (int?)null;
        }

        /// <summary>Provisional proof defaults (spec §"configurable playtest data", not a balance lock):
        /// unlock cost step 1, and Tree Level 2 requires cumulative qualifying investment of 3. Only the
        /// 1→2 threshold is authored in this proof build (Level 3+ is out of scope, spec exclusions).</summary>
        public static readonly TreeDevelopmentConfig Default = new TreeDevelopmentConfig(
            unlockCostStep: 1,
            cumulativeThresholdByTargetLevel: new Dictionary<int, int> { { 2, 3 } });
    }

    public enum NodeDevelopmentResult
    {
        Applied = 0,
        StaleStoneRevision = 1,   // expected Stone revision did not match (optimistic concurrency)
        NodeNotFound = 2,         // node key/version not in the current build
        ContentVersionMismatch = 3, // known node key but wrong version
        TreeMismatch = 4,         // node does not belong to the requested Tree
        NodeUnavailable = 5,      // authored-unavailable node rejects all development
        TreeNotCommitted = 6,     // the owning Tree is not committed on this Stone
        TreeLevelTooLow = 7,      // committed Tree Level below the node's required level
        ActiveStoneLevelTooLow = 8, // Active Stone Level below the node's required level
        AlreadyDeveloped = 9,     // node already completed; no further development
        BpDeltaInvalid = 10       // delta non-positive or exceeds remaining node cost
    }

    /// <summary>Result of a pure ApplyBPToNode transition. On rejection <see cref="NextStone"/> is the
    /// UNCHANGED input aggregate. On acceptance it carries the exact applied delta, the node's post
    /// state, whether the node just completed, and whether the Tree Level advanced.</summary>
    public readonly struct NodeDevelopmentTransition
    {
        private NodeDevelopmentTransition(NodeDevelopmentResult result, StoneProgressionAggregate nextStone,
            int appliedDelta, bool nodeCompleted, bool nodeOffered, int newTreeLevel, bool treeLevelAdvanced)
        {
            Result = result;
            NextStone = nextStone;
            AppliedDelta = appliedDelta;
            NodeCompleted = nodeCompleted;
            NodeOffered = nodeOffered;
            NewTreeLevel = newTreeLevel;
            TreeLevelAdvanced = treeLevelAdvanced;
        }

        public NodeDevelopmentResult Result { get; }
        public bool Accepted => Result == NodeDevelopmentResult.Applied;
        public StoneProgressionAggregate NextStone { get; }
        public int AppliedDelta { get; }

        /// <summary>True when this delta completed the node (progress reached its total cost).</summary>
        public bool NodeCompleted { get; }

        /// <summary>True when completion made a personal node Offered (Local nodes complete but are never
        /// Offered).</summary>
        public bool NodeOffered { get; }

        public int NewTreeLevel { get; }
        public bool TreeLevelAdvanced { get; }

        public static NodeDevelopmentTransition Reject(NodeDevelopmentResult result, StoneProgressionAggregate stone) =>
            new NodeDevelopmentTransition(result, stone, 0, false, false, 0, false);

        public static NodeDevelopmentTransition Accept(StoneProgressionAggregate next, int appliedDelta,
            bool nodeCompleted, bool nodeOffered, int newTreeLevel, bool treeLevelAdvanced) =>
            new NodeDevelopmentTransition(NodeDevelopmentResult.Applied, next, appliedDelta,
                nodeCompleted, nodeOffered, newTreeLevel, treeLevelAdvanced);
    }

    /// <summary>Pure BP-driven node development + Tree advancement transitions over the Stone aggregate.</summary>
    public static class TreeDevelopment
    {
        /// <summary>ApplyBPToNode (contracts.md). Validates the node is a developable current-build node
        /// of the committed Tree within Tree/Stone level caps and the requested delta is positive and no
        /// greater than the node's remaining (escalated) cost, then produces the next Stone with the
        /// node's development advanced by <paramref name="bpDelta"/>, the SAME delta added to the Tree's
        /// cumulative qualifying investment, and — when that cumulative crosses the configured threshold
        /// and Active Stone Level permits — the Tree Level advanced by one. It never mutates its input,
        /// never journals, never advances Tree Level above Active Stone Level, and never touches a
        /// personal balance/purchase (the BP debit is applied to the character aggregate by the caller).</summary>
        public static NodeDevelopmentTransition ApplyBPToNode(
            StoneProgressionAggregate stone,
            HomesteadProgressionCatalog catalog,
            TreeDevelopmentConfig config,
            VersionedId tree,
            VersionedId node,
            int bpDelta,
            string sourceOperationId,
            long? expectedStoneRevision = null)
        {
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (config == null) throw new ArgumentNullException(nameof(config));

            // Optimistic concurrency (CAS) first: a losing concurrent client changes nothing.
            if (expectedStoneRevision.HasValue && expectedStoneRevision.Value != stone.Revision)
                return NodeDevelopmentTransition.Reject(NodeDevelopmentResult.StaleStoneRevision, stone);

            // Resolve the node against the current build. Unknown key -> NodeNotFound; known key wrong
            // version -> ContentVersionMismatch (never a "closest" rebind).
            var def = catalog.TryResolveNode(node);
            if (def == null)
            {
                return catalog.HasNodeKey(node)
                    ? NodeDevelopmentTransition.Reject(NodeDevelopmentResult.ContentVersionMismatch, stone)
                    : NodeDevelopmentTransition.Reject(NodeDevelopmentResult.NodeNotFound, stone);
            }

            // The node must belong to the requested Tree (payload treeId/version must match its owner).
            if (!def.Tree.Equals(tree))
                return NodeDevelopmentTransition.Reject(NodeDevelopmentResult.TreeMismatch, stone);

            // Authored-unavailable nodes reject every development path (spec FR-018).
            if (!def.IsExecutable)
                return NodeDevelopmentTransition.Reject(NodeDevelopmentResult.NodeUnavailable, stone);

            // The owning Tree must be committed on this Stone.
            int committedIndex = FindCommittedTreeIndex(stone, def.Tree);
            if (committedIndex < 0)
                return NodeDevelopmentTransition.Reject(NodeDevelopmentResult.TreeNotCommitted, stone);
            var committed = stone.CommittedTrees[committedIndex];

            // Level caps: developing a node requires the committed Tree Level AND the Active Stone Level
            // to be at least the node's authored level (data-model.md; Swift Preparation needs Level 2).
            if (committed.TreeLevel < def.TreeLevel)
                return NodeDevelopmentTransition.Reject(NodeDevelopmentResult.TreeLevelTooLow, stone);
            if (stone.ActiveStoneLevel < def.TreeLevel)
                return NodeDevelopmentTransition.Reject(NodeDevelopmentResult.ActiveStoneLevelTooLow, stone);

            // Requested delta must be strictly positive.
            if (bpDelta <= 0)
                return NodeDevelopmentTransition.Reject(NodeDevelopmentResult.BpDeltaInvalid, stone);

            // Find the node's current development record (or a fresh one). The total cost is FROZEN at
            // first spend from the escalating curve, based on how many nodes are already developed
            // (completed) in this Tree at that moment.
            int devIndex = FindNodeDevelopmentIndex(stone, def.Node);
            NodeDevelopmentRecord? existing = devIndex >= 0 ? stone.NodeDevelopment[devIndex] : null;

            if (existing != null && existing.Developed)
                return NodeDevelopmentTransition.Reject(NodeDevelopmentResult.AlreadyDeveloped, stone);

            int authoredBase = def.Pricing.DevelopmentBpPrice ?? 0;
            int totalCost = existing != null
                ? existing.BpCost
                : config.NodeUnlockCost(authoredBase, CountDevelopedInTree(stone, catalog, def.Tree));

            int currentProgress = existing != null ? existing.BpProgress : 0;
            int remaining = totalCost - currentProgress;
            if (remaining < 0) remaining = 0;

            // Delta must not exceed the node's remaining cost (contracts.md: "delta ≤ remaining cost").
            if (bpDelta > remaining)
                return NodeDevelopmentTransition.Reject(NodeDevelopmentResult.BpDeltaInvalid, stone);

            int newProgress = currentProgress + bpDelta;
            bool completed = newProgress >= totalCost;
            // Completing a personal node makes it Offered; a Local node completes Stone-owned state and
            // is never Offered (data-model.md: Local Nodes never enter Offered Sets).
            bool offered = completed && def.Ownership == NodeOwnership.PersonalOffered;

            var newDevRecord = new NodeDevelopmentRecord(def.Node, newProgress, totalCost, completed, offered,
                sourceOperationId ?? string.Empty);

            var newDevList = new List<NodeDevelopmentRecord>(stone.NodeDevelopment.Count + 1);
            for (int i = 0; i < stone.NodeDevelopment.Count; i++)
                if (i != devIndex) newDevList.Add(stone.NodeDevelopment[i]);
            newDevList.Add(newDevRecord);

            // Add the SAME delta to the committed Tree's cumulative qualifying investment, then evaluate
            // ordinary Tree Level advancement against the configured threshold + Active Stone Level cap.
            int newCumulative = committed.CumulativeBpInvested + bpDelta;
            int newTreeLevel = committed.TreeLevel;
            bool advanced = false;
            int targetLevel = committed.TreeLevel + 1;
            int? threshold = config.CumulativeThresholdForLevel(targetLevel);
            if (threshold.HasValue
                && newCumulative >= threshold.Value
                && stone.ActiveStoneLevel >= targetLevel)
            {
                newTreeLevel = targetLevel;
                advanced = true;
            }

            var newCommitted = new List<CommittedTreeRecord>(stone.CommittedTrees.Count);
            for (int i = 0; i < stone.CommittedTrees.Count; i++)
            {
                if (i == committedIndex)
                    newCommitted.Add(new CommittedTreeRecord(committed.FacetId, committed.Tree,
                        committed.CommitOperationId, committed.CommitActor, newTreeLevel, newCumulative));
                else
                    newCommitted.Add(stone.CommittedTrees[i]);
            }

            var next = new StoneProgressionAggregate(
                stone.StoneId,
                stone.Revision + 1,
                stone.HistoricalStoneLevel,
                stone.ActiveStoneLevel,
                stone.FoundationalTree,
                stone.FoundationalCatalog,
                stone.ContentRegistryVersion,
                stone.CreatedProvenance,
                updatedProvenance: "develop:" + (sourceOperationId ?? string.Empty),
                stone.MirroredStoneAp,
                stone.LastAppliedReceiptId,
                newCommitted,
                newDevList,
                stone.Family,
                stone.Variant,
                stone.SchemaVersion);

            return NodeDevelopmentTransition.Accept(next, bpDelta, completed, offered, newTreeLevel, advanced);
        }

        private static int FindCommittedTreeIndex(StoneProgressionAggregate stone, VersionedId tree)
        {
            for (int i = 0; i < stone.CommittedTrees.Count; i++)
                if (string.Equals(stone.CommittedTrees[i].Tree.Key, tree.Key, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        private static int FindNodeDevelopmentIndex(StoneProgressionAggregate stone, VersionedId node)
        {
            for (int i = 0; i < stone.NodeDevelopment.Count; i++)
                if (string.Equals(stone.NodeDevelopment[i].Node.Key, node.Key, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        /// <summary>Count of nodes already DEVELOPED (completed) in <paramref name="tree"/> on this
        /// Stone. Drives the escalating successive-unlock cost curve.</summary>
        private static int CountDevelopedInTree(StoneProgressionAggregate stone,
            HomesteadProgressionCatalog catalog, VersionedId tree)
        {
            int count = 0;
            foreach (var dev in stone.NodeDevelopment)
            {
                if (!dev.Developed) continue;
                var def = catalog.TryResolveNode(dev.Node);
                if (def != null && string.Equals(def.Tree.Key, tree.Key, StringComparison.Ordinal))
                    count++;
            }
            return count;
        }
    }
}
