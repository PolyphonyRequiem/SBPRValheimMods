using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.StoneProgression
{
    // T033 / ADO #137 — Tree revocation PURE transitions (contracts.md §"RevokeTree"; data-model.md
    // §"Revoke Tree"; spec US5 scenarios 3-5). This is the Stone-side half: given the current Stone
    // aggregate and an authored revocation request it either
    //   * COMPUTES THE LOSS and mutates nothing (step one), or
    //   * PRODUCES the next Stone with the commitment, its cumulative BP investment, and every node
    //     development record owned by that Tree deleted, and the Facet vacated (step two).
    //
    // TWO STEPS, NOT ONE BUTTON (ADO #106 decision 4, grilled and banked). Bond Power belongs to the
    // Stone, not to a person, so a partially-developed node can represent days of household effort;
    // the Governor is the only person whose decision that information can change. <see cref="Preview"/>
    // is therefore a first-class transition, not a convenience: it returns exactly what
    // <see cref="RevokeTree"/> will destroy, computed from the same state by the same rules, and it
    // CANNOT mutate — it never returns a next aggregate at all. Abandoning step one changes nothing
    // because step one has nothing to abandon.
    //
    // BOND POWER IS NOT REFUNDED (spec FR-021, AT-REVOKE-NO-BP-REFUND). Node development is deleted
    // outright. This transition never credits a personal BP balance, and never touches the character
    // aggregate at all: the Personal-AP refund is an APPENDED PURCHASE CANCELLATION written by the
    // application command layer (RevocationCommands over PurchaseCommandHandler), never a balance
    // written here. Permanent Effects, Progression Keys, and every purchase RECORD survive — nothing
    // in this file removes a purchase.
    //
    // A REPLACEMENT TREE BUYS NOTHING (AT-REPLACEMENT-NO-AUTOBUY): revocation deletes the Stone-owned
    // development records, so a Tree recommitted into the vacated Facet starts at the initial authored
    // Tree Level with zero cumulative BP and zero developed nodes — the ordinary CommitTreeToFacet
    // path. There is deliberately no "restore" branch anywhere in this file.
    //
    // net48 audit: engine-free (System.Collections.Generic + value objects + the content catalog). No
    // net5+ surface, no UnityEngine/Valheim/BepInEx reference: link-compiles into the net8 test project.

    public enum TreeRevocationResult
    {
        Applied = 0,
        StaleStoneRevision = 1,   // expected Stone revision did not match (optimistic concurrency)
        TreeNotCommitted = 2,     // no Committed Tree occupies the requested Facet
        TreeMismatch = 3,         // the Facet holds a DIFFERENT Tree than the caller expected
        ContentVersionMismatch = 4, // right Tree key, stale version — never a "closest" rebind
        ProtectedTree = 5         // the Foundational Tree can never be revoked
    }

    /// <summary>What a revocation WILL destroy, computed before anything is mutated (step one of the
    /// two-step act). Every field is a loss the Governor is shown; there is no next aggregate here,
    /// so a caller cannot accidentally commit a preview.</summary>
    public sealed class TreeRevocationLoss
    {
        public TreeRevocationLoss(string facetId, VersionedId tree, int treeLevel,
            int cumulativeBpInvested, int nodeDevelopmentBpProgress, int developedNodeCount,
            int partiallyDevelopedNodeCount, IReadOnlyList<VersionedId> destroyedNodes)
        {
            FacetId = facetId ?? string.Empty;
            Tree = tree;
            TreeLevel = treeLevel;
            CumulativeBpInvested = cumulativeBpInvested;
            NodeDevelopmentBpProgress = nodeDevelopmentBpProgress;
            DevelopedNodeCount = developedNodeCount;
            PartiallyDevelopedNodeCount = partiallyDevelopedNodeCount;
            DestroyedNodes = new ReadOnlyCollection<VersionedId>(
                new List<VersionedId>(destroyedNodes ?? Array.Empty<VersionedId>()));
        }

        public string FacetId { get; }
        public VersionedId Tree { get; }

        /// <summary>The Tree Level that is lost. A replacement commitment restarts at the initial
        /// authored level.</summary>
        public int TreeLevel { get; }

        /// <summary>Cumulative qualifying Bond Power invested in this Tree. NOT refunded — this is the
        /// household effort number the two-step warning exists to state (ADO #106 decision 4).</summary>
        public int CumulativeBpInvested { get; }

        /// <summary>Bond Power currently banked in per-node development progress for this Tree,
        /// completed and partial alike. Also not refunded.</summary>
        public int NodeDevelopmentBpProgress { get; }

        /// <summary>Nodes fully developed in this Tree (Local Effects live and personal nodes Offered).</summary>
        public int DevelopedNodeCount { get; }

        /// <summary>Nodes with progress banked but not yet complete — the partial development that
        /// would silently vanish without this warning.</summary>
        public int PartiallyDevelopedNodeCount { get; }

        /// <summary>Every node development record revocation will delete.</summary>
        public IReadOnlyList<VersionedId> DestroyedNodes { get; }

        /// <summary>Total Bond Power destroyed and never refunded. The single number the warning leads
        /// with; the fields above break it down.</summary>
        public int TotalBondPowerDestroyed => CumulativeBpInvested;
    }

    /// <summary>Result of a step-one loss computation. On rejection <see cref="Loss"/> is null and the
    /// caller has nothing to present — a rejected preview is not a zero loss.</summary>
    public readonly struct TreeRevocationPreview
    {
        private TreeRevocationPreview(TreeRevocationResult result, TreeRevocationLoss? loss)
        {
            Result = result;
            Loss = loss;
        }

        public TreeRevocationResult Result { get; }
        public bool Accepted => Result == TreeRevocationResult.Applied;

        /// <summary>The computed loss, or null when the preview was rejected.</summary>
        public TreeRevocationLoss? Loss { get; }

        public static TreeRevocationPreview Reject(TreeRevocationResult result) =>
            new TreeRevocationPreview(result, null);

        public static TreeRevocationPreview Accept(TreeRevocationLoss loss) =>
            new TreeRevocationPreview(TreeRevocationResult.Applied, loss);
    }

    /// <summary>Result of a step-two revocation transition. On rejection <see cref="NextStone"/> is the
    /// UNCHANGED input aggregate (contracts.md: "Validation completes before commit. Failure changes
    /// nothing."), so a caller that commits it unconditionally still writes the prior state.</summary>
    public readonly struct TreeRevocationTransition
    {
        private TreeRevocationTransition(TreeRevocationResult result, StoneProgressionAggregate nextStone,
            TreeRevocationLoss? loss)
        {
            Result = result;
            NextStone = nextStone;
            Loss = loss;
        }

        public TreeRevocationResult Result { get; }
        public bool Accepted => Result == TreeRevocationResult.Applied;
        public StoneProgressionAggregate NextStone { get; }

        /// <summary>The loss this revocation actually destroyed — byte-identical to what
        /// <see cref="TreeRevocation.Preview"/> reported for the same state, because both are computed
        /// by the same function. Null on rejection.</summary>
        public TreeRevocationLoss? Loss { get; }

        public static TreeRevocationTransition Reject(TreeRevocationResult result,
            StoneProgressionAggregate stone) =>
            new TreeRevocationTransition(result, stone, null);

        public static TreeRevocationTransition Accept(StoneProgressionAggregate next,
            TreeRevocationLoss loss) =>
            new TreeRevocationTransition(TreeRevocationResult.Applied, next, loss);
    }

    /// <summary>Pure Tree revocation transitions over the Stone aggregate. Neither method mutates its
    /// inputs, journals, touches a character aggregate, or refunds Bond Power.</summary>
    public static class TreeRevocation
    {
        /// <summary>STEP ONE (ADO #106 decision 4). Compute exactly what revoking the Tree in
        /// <paramref name="facetId"/> would destroy. Validates the same gates step two does, so a
        /// revocation that would be rejected is reported as rejected here rather than presenting a
        /// loss the Governor could never confirm. Mutates NOTHING and returns no aggregate: abandoning
        /// after this call cannot change state because this call changed none.</summary>
        public static TreeRevocationPreview Preview(
            StoneProgressionAggregate stone,
            HomesteadProgressionCatalog catalog,
            string facetId,
            VersionedId tree,
            long? expectedStoneRevision = null)
        {
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            var gate = Validate(stone, facetId, tree, expectedStoneRevision, out var committed);
            if (gate != TreeRevocationResult.Applied)
                return TreeRevocationPreview.Reject(gate);

            return TreeRevocationPreview.Accept(ComputeLoss(stone, catalog, committed!));
        }

        /// <summary>STEP TWO. Produce the next Stone with the Facet vacated: the Committed Tree record
        /// (and with it the Tree Level and cumulative BP investment) removed, and every node
        /// development record owned by that Tree deleted — completed Local Effects, Offered personal
        /// nodes, and partial progress alike. Bond Power is NOT refunded and no character aggregate is
        /// touched; the Personal-AP refund is an appended purchase cancellation written by the command
        /// layer. Every other Stone field — Historical AND Active Stone Level, Mirrored AP, foundational
        /// identities, sibling Facets, the Settlement Local policy — is preserved verbatim.</summary>
        public static TreeRevocationTransition RevokeTree(
            StoneProgressionAggregate stone,
            HomesteadProgressionCatalog catalog,
            string facetId,
            VersionedId tree,
            string revocationOperationId,
            long? expectedStoneRevision = null)
        {
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            var gate = Validate(stone, facetId, tree, expectedStoneRevision, out var committed);
            if (gate != TreeRevocationResult.Applied)
                return TreeRevocationTransition.Reject(gate, stone);

            var loss = ComputeLoss(stone, catalog, committed!);

            // Drop the Committed Tree record for this Facet. Sibling Facets are untouched.
            var newCommitted = new List<CommittedTreeRecord>(stone.CommittedTrees.Count);
            foreach (var c in stone.CommittedTrees)
            {
                if (string.Equals(c.FacetId, facetId, StringComparison.Ordinal)) continue;
                newCommitted.Add(c);
            }

            // Drop every node development record owned by the revoked Tree. A record whose node no
            // longer resolves in the current build is NOT owned by any Tree we can prove, so it is
            // preserved rather than silently swept — deletion is only ever for provably-owned nodes.
            var destroyed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in loss.DestroyedNodes) destroyed.Add(n.Key);

            var newDev = new List<NodeDevelopmentRecord>(stone.NodeDevelopment.Count);
            foreach (var d in stone.NodeDevelopment)
            {
                if (destroyed.Contains(d.Node.Key)) continue;
                newDev.Add(d);
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
                updatedProvenance: "revoke:" + (revocationOperationId ?? string.Empty),
                stone.MirroredStoneAp,
                stone.LastAppliedReceiptId,
                newCommitted,
                newDev,
                stone.Family,
                stone.Variant,
                stone.SchemaVersion,
                stone.LocalPolicy);

            return TreeRevocationTransition.Accept(next, loss);
        }

        /// <summary>The gates both steps share, so a preview can never present a loss for a revocation
        /// that would be refused. Order matters: CAS first (a losing concurrent client changes nothing
        /// and is told so), then protection, then commitment/identity.</summary>
        private static TreeRevocationResult Validate(
            StoneProgressionAggregate stone, string facetId, VersionedId tree,
            long? expectedStoneRevision, out CommittedTreeRecord? committed)
        {
            committed = null;

            if (expectedStoneRevision.HasValue && expectedStoneRevision.Value != stone.Revision)
                return TreeRevocationResult.StaleStoneRevision;

            // The Foundational Tree is present from level 1, occupies no Facet, and can NEVER be
            // revoked (data-model.md Aggregate 1 invariant). Checked before commitment lookup so the
            // refusal names the real reason rather than "not committed".
            if (!stone.FoundationalTree.IsNone
                && string.Equals(stone.FoundationalTree.Key, tree.Key, StringComparison.Ordinal))
                return TreeRevocationResult.ProtectedTree;

            CommittedTreeRecord? found = null;
            foreach (var c in stone.CommittedTrees)
            {
                if (!string.Equals(c.FacetId, facetId ?? string.Empty, StringComparison.Ordinal)) continue;
                found = c;
                break;
            }
            if (found == null)
                return TreeRevocationResult.TreeNotCommitted;

            // Exact Facet/Tree/version (contracts.md RevokeTree Validates). A different Tree key in
            // this Facet is a mismatch; the right key at a stale version is a stale content view —
            // never a "closest" rebind.
            if (!string.Equals(found.Tree.Key, tree.Key, StringComparison.Ordinal))
                return TreeRevocationResult.TreeMismatch;
            if (found.Tree.Version != tree.Version)
                return TreeRevocationResult.ContentVersionMismatch;

            committed = found;
            return TreeRevocationResult.Applied;
        }

        /// <summary>The ONE loss computation both steps use. Because step one and step two call this
        /// same function over the same state, the number the Governor was shown is the number that is
        /// destroyed — the warning cannot drift from the act it warns about.</summary>
        private static TreeRevocationLoss ComputeLoss(
            StoneProgressionAggregate stone, HomesteadProgressionCatalog catalog, CommittedTreeRecord committed)
        {
            int progress = 0;
            int developed = 0;
            int partial = 0;
            var destroyed = new List<VersionedId>();

            foreach (var d in stone.NodeDevelopment)
            {
                var def = catalog.TryResolveNode(d.Node);
                if (def == null) continue;                                   // unprovable ownership: preserved
                if (!string.Equals(def.Tree.Key, committed.Tree.Key, StringComparison.Ordinal)) continue;

                destroyed.Add(d.Node);
                progress += d.BpProgress;
                if (d.Developed) developed++;
                else if (d.BpProgress > 0) partial++;
            }

            return new TreeRevocationLoss(committed.FacetId, committed.Tree, committed.TreeLevel,
                committed.CumulativeBpInvested, progress, developed, partial, destroyed);
        }
    }
}
