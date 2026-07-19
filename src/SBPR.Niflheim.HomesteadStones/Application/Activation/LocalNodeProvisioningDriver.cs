using System;
using System.Collections.Generic;
using System.Globalization;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Application.Activation
{
    // T016 shared runtime substrate — the LEGITIMATE isolated-QA provisioning path (task scope bullet 4).
    // T016/T021/T025/T029 QA needs a developed/committed Local node to prove joined-client entry/exit, but
    // there must be NO hardcoded production grant and NO parallel provisional ledger. This driver reaches a
    // developed Local node using ONLY the accepted, receipt-backed command handlers on a
    // LocalProgressionServer, in the exact order the spec's state machine requires:
    //
    //   1. CommitTreeToFacet   — commit the node's owning Tree into its Facet (Governor authority).
    //   2. RecordAlignedActivity — credit the Governor enough Stone-wide BP to fund development.
    //   3. ApplyBPToNode (xN)  — develop the Local node to completion (Stone-owned developed state).
    //   4. (optional) SetSettlementLocalPolicy — set the Settlement Local beneficiary policy.
    //
    // Every step is the SAME command a real session would issue; the only thing "test-only/operator" about
    // this class is that it SEQUENCES them from a server-derived subject on demand, gated by the caller
    // (the net48 admin seam behind an explicit config flag + Valheim-admin authority, mirroring
    // RelationshipProvisioningAdmin — see Features/Progression/LocalProgressionProvisioningAdmin.cs). It
    // never writes a projection directly, never fabricates development, and any handler rejection surfaces
    // verbatim, so a QA run that "provisions" a node has provably crossed the real gates.
    //
    // The acting Governor's Bond must already exist (via RelationshipProvisioningIngress); this driver only
    // does the Facet→BP→development→policy sequence a bonded Governor is authorized for.
    //
    // net48 audit: engine-free (value objects + the shipped command handlers). Link-compiles into the net8
    // test project and is fully unit-tested.
    public sealed class LocalNodeProvisioningDriver
    {
        private readonly LocalProgressionServer _server;

        public LocalNodeProvisioningDriver(LocalProgressionServer server)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
        }

        /// <summary>Provision one Stone-cultivated Local node to Developed for QA using only accepted
        /// commands. <paramref name="governor"/> must already hold an active Bond with a Responsibility
        /// Range covering the node's Tree. <paramref name="opPrefix"/> makes every derived operation id
        /// deterministic so re-running the same provisioning replays idempotently through the handlers.
        /// Returns a structured result naming the first failing step (if any) so a QA harness can assert the
        /// real gate that blocked it rather than a fabricated success.</summary>
        public LocalNodeProvisioningResult Provision(
            AuthoritativeSubject governor,
            StoneId stoneId,
            VersionedId localNode,
            string opPrefix)
        {
            if (string.IsNullOrEmpty(opPrefix))
                return LocalNodeProvisioningResult.Failed("MissingOpPrefix", "prefix");
            if (string.IsNullOrEmpty(governor.Account.Value) || string.IsNullOrEmpty(governor.Character.Value))
                return LocalNodeProvisioningResult.Failed("Unauthenticated", "subject");

            var def = _server.Catalog.TryResolveNode(localNode);
            if (def == null)
                return LocalNodeProvisioningResult.Failed("NodeNotFound", "resolve");
            if (def.Ownership != NodeOwnership.StoneCultivated)
                return LocalNodeProvisioningResult.Failed("NotALocalNode", "resolve");

            var connection = new AuthenticatedConnection(governor.Account.Value, governor.Character.Value);
            var tree = def.Tree;
            string facetId = FacetForTree(tree);

            // 1. Commit the owning Tree into its Facet (skips cleanly if already committed).
            var stone = _server.Stones.GetStone(stoneId);
            if (stone == null)
                return LocalNodeProvisioningResult.Failed("StoneNotFound", "commit");
            if (!IsTreeCommitted(stone, tree))
            {
                var commit = _server.Facets.Handle(new CommitTreeToFacetCommand(
                    new OperationId(opPrefix + "-commit"), stoneId, connection, default,
                    facetId, tree.Key, tree.Version, paletteVersion: 1));
                if (commit.Outcome == FacetCommandOutcome.Rejected)
                    return LocalNodeProvisioningResult.Failed(commit.ResultCode, "commit");
            }

            // 2-3. Credit BP then spend it on the node until Developed. Each ApplyBPToNode debits the
            // authored cost and advances development; we credit exactly the authored cost per step and stop
            // when the handler reports the node completed. Bounded to a small step budget to fail closed
            // rather than loop on an unexpected rejection.
            int authoredCost = def.Pricing.DevelopmentBpPrice ?? 0;
            if (authoredCost <= 0)
                return LocalNodeProvisioningResult.Failed("NodeHasNoDevelopmentPrice", "develop");

            const int MaxSteps = 16;
            for (int step = 0; step < MaxSteps; step++)
            {
                var current = _server.Stones.GetStone(stoneId);
                if (current == null)
                    return LocalNodeProvisioningResult.Failed("StoneNotFound", "develop");
                if (NodeDeveloped(current, localNode))
                    return LocalNodeProvisioningResult.Developed(localNode, tree, step);

                string s = step.ToString(CultureInfo.InvariantCulture);

                // Credit enough BP for exactly one development step.
                var credit = _server.Activities.Handle(new RecordAlignedActivityCommand(
                    new OperationId(opPrefix + "-bp-" + s), stoneId, connection, default,
                    tree, authoredCost, evidenceDigest: opPrefix + "-ev-" + s));
                if (credit.Outcome == ActivityCommandOutcome.Rejected)
                    return LocalNodeProvisioningResult.Failed(credit.ResultCode, "credit");

                // Spend it on the node.
                var develop = _server.Development.Handle(new ApplyBPToNodeCommand(
                    new OperationId(opPrefix + "-dev-" + s), stoneId, connection, default,
                    tree.Key, tree.Version, localNode.Key, localNode.Version, authoredCost));
                if (develop.Outcome == DevelopmentCommandOutcome.Rejected)
                    return LocalNodeProvisioningResult.Failed(develop.ResultCode, "develop");
            }

            var final = _server.Stones.GetStone(stoneId);
            if (final != null && NodeDeveloped(final, localNode))
                return LocalNodeProvisioningResult.Developed(localNode, tree, MaxSteps);
            return LocalNodeProvisioningResult.Failed("DevelopmentBudgetExhausted", "develop");
        }

        /// <summary>Set the single Settlement Local policy through the accepted owner-only handler. The
        /// caller-supplied <paramref name="owner"/> must be the validated Homestead owner (the injected
        /// IHomesteadOwnerAuthority proves it). Returns the handler's result code.</summary>
        public string SetPolicy(AuthoritativeSubject owner, StoneId stoneId, LocalBeneficiaryMode mode,
            IReadOnlyList<string>? allowlist, string opId)
        {
            var connection = new AuthenticatedConnection(owner.Account.Value, owner.Character.Value);
            var result = _server.LocalPolicy.Handle(new SetSettlementLocalPolicyCommand(
                new OperationId(opId), stoneId, connection, default, mode, allowlist));
            return result.ResultCode;
        }

        private static string FacetForTree(VersionedId tree)
        {
            // Cooking/Crafting are Profession trees; Archer/Warrior are Martial (StoneFacetPalette).
            if (string.Equals(tree.Key, HomesteadProgressionCatalog.CookingTree.Key, StringComparison.Ordinal)
                || string.Equals(tree.Key, HomesteadProgressionCatalog.CraftingTree.Key, StringComparison.Ordinal))
                return HomesteadProgressionCatalog.ProfessionFacetId;
            return HomesteadProgressionCatalog.MartialFacetId;
        }

        private static bool IsTreeCommitted(StoneProgressionAggregate stone, VersionedId tree)
        {
            foreach (var c in stone.CommittedTrees)
                if (string.Equals(c.Tree.Key, tree.Key, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool NodeDeveloped(StoneProgressionAggregate stone, VersionedId node)
        {
            foreach (var d in stone.NodeDevelopment)
                if (string.Equals(d.Node.Key, node.Key, StringComparison.Ordinal) && d.Developed) return true;
            return false;
        }
    }

    /// <summary>Structured outcome of a QA provisioning attempt. On failure it names the accepted-command
    /// step and the handler's verbatim result code, so a QA harness proves which real gate blocked it.</summary>
    public readonly struct LocalNodeProvisioningResult
    {
        private LocalNodeProvisioningResult(bool developed, VersionedId node, VersionedId tree, int steps,
            string resultCode, string failedStep)
        {
            IsDeveloped = developed;
            Node = node;
            Tree = tree;
            Steps = steps;
            ResultCode = resultCode ?? string.Empty;
            FailedStep = failedStep ?? string.Empty;
        }

        public bool IsDeveloped { get; }
        public VersionedId Node { get; }
        public VersionedId Tree { get; }
        public int Steps { get; }
        public string ResultCode { get; }
        public string FailedStep { get; }

        internal static LocalNodeProvisioningResult Developed(VersionedId node, VersionedId tree, int steps) =>
            new LocalNodeProvisioningResult(true, node, tree, steps, "Developed", string.Empty);

        internal static LocalNodeProvisioningResult Failed(string resultCode, string step) =>
            new LocalNodeProvisioningResult(false, VersionedId.None, VersionedId.None, 0, resultCode, step);
    }
}
