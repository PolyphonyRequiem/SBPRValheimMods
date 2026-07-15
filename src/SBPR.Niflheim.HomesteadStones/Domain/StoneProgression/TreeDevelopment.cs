using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.StoneProgression
{
    // T012 — the pure Stone-side ApplyBPToNode transition (contracts.md §"ApplyBPToNode"; data-model.md
    // §"Credit and spend BP on node development"). Given the current Stone aggregate and a validated
    // BP-development request, it validates the committed Tree, the developable node, Tree/Stone level
    // requirements, and the current-build definition, then PRODUCES the next Stone with:
    //   * one node-development delta (NodeDevelopmentRecord.BpProgress advanced by the applied BP), and
    //   * the SAME delta added to that Committed Tree's cumulative qualifying investment
    //     (CommittedTreeRecord.CumulativeBpInvested) — the two move ATOMICALLY in one produced state
    //     (AT-NODE-DEVELOPMENT-COUNTS-AS-INVESTMENT), and
    //   * a possibly-advanced Tree Level, computed PURELY from the new cumulative investment crossing the
    //     data-defined threshold, clamped by Active Stone Level (AT-TREE-ADVANCE-1-2). There is no
    //     separate level meter, spend, or command — Level is a function of investment
    //     (AT-NO-DIRECT-LEVEL-METER).
    //
    // The successive-unlock cost is data-defined and escalates with the number of nodes already
    // developed in this Tree (AT-ESCALATING-COST-CONFIG via TreeTuning). A node's effective cost is
    // FIXED the first time BP is applied to it (stored in NodeDevelopmentRecord.BpCost), so mid-
    // development escalation of a node already in progress does not move its own goalpost. Completing a
    // Local node marks it Developed (activates Stone-owned Local state via derivation); completing a
    // personal node additionally marks it Offered.
    //
    // The BP DEBIT itself is on the character aggregate (Domain/CharacterProgression/BondPower.cs). This
    // transition is the Stone half only; the application command layer (DevelopmentCommands.cs)
    // coordinates both under one durable receipt so the debit and the node/Tree delta commit atomically.
    //
    // net48 audit: engine-free (System.Collections.Generic + value objects + content catalog). No net5+
    // surface, no UnityEngine/Valheim/BepInEx reference: link-compiles into the net8 tests.

    /// <summary>Result of a pure ApplyBPToNode transition. On rejection <see cref="NextStone"/> is the
    /// UNCHANGED original aggregate (validation completes before commit; failure changes nothing).</summary>
    public readonly struct NodeDevelopmentTransition
    {
        private NodeDevelopmentTransition(bool accepted, string resultCode,
            StoneProgressionAggregate nextStone, int appliedBp, bool nodeCompleted,
            int newTreeLevel, bool treeLevelAdvanced)
        {
            Accepted = accepted;
            ResultCode = resultCode;
            NextStone = nextStone;
            AppliedBp = appliedBp;
            NodeCompleted = nodeCompleted;
            NewTreeLevel = newTreeLevel;
            TreeLevelAdvanced = treeLevelAdvanced;
        }

        public bool Accepted { get; }
        public string ResultCode { get; }
        public StoneProgressionAggregate NextStone { get; }

        /// <summary>BP applied to node development this operation (also the cumulative-investment delta).</summary>
        public int AppliedBp { get; }

        /// <summary>True when this application completed the node (progress reached its fixed cost).</summary>
        public bool NodeCompleted { get; }

        /// <summary>The Committed Tree's Level after this operation.</summary>
        public int NewTreeLevel { get; }

        /// <summary>True when this operation advanced the Committed Tree's Level.</summary>
        public bool TreeLevelAdvanced { get; }

        public static NodeDevelopmentTransition Reject(string code, StoneProgressionAggregate stone) =>
            new NodeDevelopmentTransition(false, code, stone, 0, false, 0, false);

        public static NodeDevelopmentTransition Accept(StoneProgressionAggregate nextStone, int appliedBp,
            bool nodeCompleted, int newTreeLevel, bool treeLevelAdvanced) =>
            new NodeDevelopmentTransition(true, "Applied", nextStone, appliedBp, nodeCompleted,
                newTreeLevel, treeLevelAdvanced);
    }

    /// <summary>Pure node-development transitions over the Stone aggregate. Every method validates the
    /// authored contract and returns the next state; none mutate their inputs, journal, or touch the
    /// character aggregate (personal BP debit lives there).</summary>
    public static class TreeDevelopment
    {
        /// <summary>ApplyBPToNode (contracts.md). Validates the current Tree commitment, a developable
        /// (not unavailable) current-build node in that Tree, Tree/Stone level requirements, expected
        /// Stone revision, and the data-defined tuning, then advances the node's development by
        /// <paramref name="bpAmount"/> and adds the same amount to the Tree's cumulative qualifying
        /// investment, possibly advancing the Tree Level under the configured threshold/cap. Governor
        /// authority + Responsibility Range and the personal BP debit are validated by the caller.</summary>
        public static NodeDevelopmentTransition ApplyBpToNode(
            StoneProgressionAggregate stone,
            HomesteadProgressionCatalog catalog,
            TreeTuning tuning,
            VersionedId tree,
            VersionedId node,
            int bpAmount,
            string sourceOperationId,
            long? expectedStoneRevision = null)
        {
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            // CAS first: a losing concurrent client changes nothing.
            if (expectedStoneRevision.HasValue && expectedStoneRevision.Value != stone.Revision)
                return NodeDevelopmentTransition.Reject("StaleStoneRevision", stone);

            if (bpAmount <= 0)
                return NodeDevelopmentTransition.Reject("EvidenceInvalid", stone);

            // The Tree must be currently committed on this Stone (TreeNotCommitted).
            CommittedTreeRecord? committed = null;
            int committedIndex = -1;
            for (int i = 0; i < stone.CommittedTrees.Count; i++)
            {
                if (string.Equals(stone.CommittedTrees[i].Tree.Key, tree.Key, StringComparison.Ordinal))
                {
                    committed = stone.CommittedTrees[i];
                    committedIndex = i;
                    break;
                }
            }
            if (committed == null)
                return NodeDevelopmentTransition.Reject("TreeNotCommitted", stone);

            // The committed Tree must be at the exact current-build version (a stale definition rejects).
            if (committed.Tree.Version != tree.Version)
                return NodeDevelopmentTransition.Reject("ContentVersionMismatch", stone);

            // The node must be a current-build definition (unknown key/version rejects clearly).
            var nodeDef = catalog.TryResolveNode(node);
            if (nodeDef == null)
            {
                return catalog.HasNodeKey(node)
                    ? NodeDevelopmentTransition.Reject("ContentVersionMismatch", stone)
                    : NodeDevelopmentTransition.Reject("RequirementNotMet", stone);
            }

            // The node must belong to the committed Tree.
            if (!string.Equals(nodeDef.Tree.Key, tree.Key, StringComparison.Ordinal))
                return NodeDevelopmentTransition.Reject("RequirementNotMet", stone);

            // Unavailable nodes reject development entirely (NodeUnavailable).
            if (nodeDef.Status == NodeFirstBuildStatus.Unavailable || nodeDef.Pricing.DevelopmentBpPrice == null)
                return NodeDevelopmentTransition.Reject("NodeUnavailable", stone);

            // Active Stone Level and Tree Level gates (data-model.md accepted gates).
            if (stone.ActiveStoneLevel < nodeDef.Requirements.MinActiveStoneLevel)
                return NodeDevelopmentTransition.Reject("ActiveStoneLevelTooLow", stone);
            if (committed.TreeLevel < nodeDef.Requirements.MinTreeLevel)
                return NodeDevelopmentTransition.Reject("TreeLevelTooLow", stone);

            // Locate any existing development record for this node.
            NodeDevelopmentRecord? existing = null;
            int existingIndex = -1;
            for (int i = 0; i < stone.NodeDevelopment.Count; i++)
            {
                if (NodeMatches(stone.NodeDevelopment[i].Node, node))
                {
                    existing = stone.NodeDevelopment[i];
                    existingIndex = i;
                    break;
                }
            }

            // An already-developed node is complete — no further BP is accepted (AlreadyAcquired guards
            // wasted spend and keeps cumulative investment honest).
            if (existing != null && existing.Developed)
                return NodeDevelopmentTransition.Reject("AlreadyAcquired", stone);

            // Effective cost is FIXED at first touch: a node already in progress keeps its recorded cost;
            // a fresh node's cost escalates with the count of nodes ALREADY developed in this Tree
            // (AT-ESCALATING-COST-CONFIG). Base price is the authored development BP price.
            int baseCost = nodeDef.Pricing.DevelopmentBpPrice.Value;
            int cost = existing != null && existing.BpCost > 0
                ? existing.BpCost
                : tuning.EffectiveDevelopmentCost(baseCost, CountDevelopedInTree(stone, catalog, tree));

            int priorProgress = existing?.BpProgress ?? 0;
            int newProgress = priorProgress + bpAmount;
            bool completed = newProgress >= cost;

            // Build the next node-development record. Local -> Developed on completion; personal ->
            // Developed AND Offered on completion (data-model.md: "Completing a Local Node activates
            // Stone-owned Local state; completing a personal node makes it Offered.").
            bool isPersonal = nodeDef.Ownership == NodeOwnership.PersonalOffered;
            var newRecord = new NodeDevelopmentRecord(
                nodeDef.Node,
                newProgress,
                cost,
                developed: completed,
                offered: completed && isPersonal,
                sourceOperationId: sourceOperationId ?? string.Empty);

            var newNodeDev = new List<NodeDevelopmentRecord>(stone.NodeDevelopment.Count + 1);
            if (existingIndex >= 0)
            {
                for (int i = 0; i < stone.NodeDevelopment.Count; i++)
                    newNodeDev.Add(i == existingIndex ? newRecord : stone.NodeDevelopment[i]);
            }
            else
            {
                newNodeDev.AddRange(stone.NodeDevelopment);
                newNodeDev.Add(newRecord);
            }

            // Cumulative qualifying Tree investment increases by exactly the applied BP, and the Tree
            // Level is recomputed PURELY from that new cumulative against the data-defined threshold,
            // clamped by Active Stone Level. No separate level meter is written.
            int newCumulative = committed.CumulativeBpInvested + bpAmount;
            int newTreeLevel = tuning.LevelForCumulative(newCumulative, stone.ActiveStoneLevel);
            bool advanced = newTreeLevel > committed.TreeLevel;
            if (newTreeLevel < committed.TreeLevel) newTreeLevel = committed.TreeLevel; // never regress

            var newCommitted = new CommittedTreeRecord(committed.FacetId, committed.Tree,
                committed.CommitOperationId, committed.CommitActor, newTreeLevel, newCumulative);

            var newCommittedTrees = new List<CommittedTreeRecord>(stone.CommittedTrees.Count);
            for (int i = 0; i < stone.CommittedTrees.Count; i++)
                newCommittedTrees.Add(i == committedIndex ? newCommitted : stone.CommittedTrees[i]);

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
                newCommittedTrees,
                newNodeDev,
                stone.Family,
                stone.Variant,
                stone.SchemaVersion);

            return NodeDevelopmentTransition.Accept(next, bpAmount, completed, newTreeLevel, advanced);
        }

        /// <summary>Count nodes ALREADY developed in a Tree (used for the escalating cost step). A node
        /// counts only when its development record is complete and its authored definition is in this
        /// Tree at the current build.</summary>
        private static int CountDevelopedInTree(StoneProgressionAggregate stone,
            HomesteadProgressionCatalog catalog, VersionedId tree)
        {
            int n = 0;
            foreach (var dev in stone.NodeDevelopment)
            {
                if (!dev.Developed) continue;
                var def = catalog.TryResolveNode(dev.Node);
                if (def != null && string.Equals(def.Tree.Key, tree.Key, StringComparison.Ordinal))
                    n++;
            }
            return n;
        }

        private static bool NodeMatches(VersionedId a, VersionedId b) =>
            string.Equals(a.Key, b.Key, StringComparison.Ordinal) && a.Version == b.Version;
    }
}
