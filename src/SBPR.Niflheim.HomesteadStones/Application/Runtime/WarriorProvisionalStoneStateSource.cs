using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // T029 remediation — the PROVISIONAL, server-owned Stone-owned-Local-state source for the Warrior
    // T.W.I.G. Training gate. It is the direct analogue of the Foundational runtime's provisional
    // ServerHomesteadFamilyResolver / ServerHomesteadBondPolicy (FoundationalRuntimeBootstrap.cs): a small,
    // clearly-labelled proof-policy seam that supplies the Stone-owned developed Local state the shared
    // grammar needs, so a JOINED CLIENT can actually exercise the gate, until a full Stone-progression
    // command runtime (Facet/Development/LocalPolicy handlers over a durable IStoneAggregateStore) is
    // composed server-side. NONE of the pure gate/provider/view code depends on this being provisional —
    // when the real Stone runtime lands, only this class is replaced.
    //
    // What is PROVISIONAL here (and honestly so):
    //   * every resident Homestead Stone is treated as having the ONE authored Warrior T.W.I.G. Training
    //     node DEVELOPED (Stone-owned Local state), its owning Warrior Tree COMMITTED, at Active Stone
    //     Level 2 (>= the node's Tree Level 1);
    //   * the single Settlement Local policy is ATTUNED. This is the deliberate, load-bearing proof choice:
    //     under Attuned the effect is active for the owner OR an occupant holding an active Bond/Attunement
    //     — relationship state that IS composed server-side and IS admin-provisionable via the shipped
    //     sbpr_provision seam. So the joined-client proof needs no un-wired input: provision an Attunement
    //     -> the exact T.W.I.G. places (with build Permission); a non-attuned occupant is refused OUTSIDE
    //     POLICY (EffectNotActive); an attuned occupant without build Permission is refused
    //     MissingBuildPermission — the full FR-016 AND, demonstrable end-to-end.
    //   * an authorized Governor is treated as present (mirrors ServerHomesteadBondPolicy authorizing the
    //     Homestead:All Governor range). Toggleable so the governance-dormancy refusal can also be shown.
    //
    // What is NOT provisional / not weakened:
    //   * the exact-piece binding, the effect-active/policy/build-Permission AND, the no-second-ledger
    //     dormancy re-derivation, and owner/relationship membership are all the SHIPPED engine-free grammar
    //     (LocalEffectActivationView + LocalPlacementProvider). This source only supplies the developed
    //     Stone snapshot they consume; it makes no admit decision itself.
    //
    // No validated Homestead owner is resolvable without the account-owner runtime, so IsOwner is false
    // here (an occupant qualifies via Attunement, not ownership). This never GRANTS anything — it only
    // avoids a false owner claim.
    //
    // net48 audit: engine-free (System.Collections.Generic + domain value objects). Link-compiles into net8.
    public sealed class WarriorProvisionalStoneStateSource : IWarriorLocalStoneStateSource
    {
        /// <summary>Active Stone Level the provisional Stone reports. >= the T.W.I.G. node's Tree Level (1)
        /// so the level conjunct passes; a lower value would dormant the effect.</summary>
        public const int ProvisionalActiveStoneLevel = 2;

        private static readonly VersionedId TwigNode = new VersionedId("TwigTraining", 1);
        private static readonly VersionedId WarriorTree = HomesteadProgressionCatalog.WarriorTree;
        private static readonly VersionedId CookingTree = HomesteadProgressionCatalog.CookingTree;

        private readonly LocalBeneficiaryMode _policyMode;
        private readonly bool _governorPresent;

        /// <summary>Provisional source. Defaults: Attuned policy (relationship-gated, provisionable) and an
        /// authorized Governor present. Both are overridable so a playtest can also demonstrate the
        /// governance-dormancy refusal or an Everyone policy.</summary>
        public WarriorProvisionalStoneStateSource(
            LocalBeneficiaryMode policyMode = LocalBeneficiaryMode.Attuned,
            bool authorizedGovernorPresent = true)
        {
            _policyMode = policyMode;
            _governorPresent = authorizedGovernorPresent;
        }

        public bool TryGetStone(StoneId stoneId, out StoneProgressionAggregate? stone)
        {
            stone = null;
            if (string.IsNullOrEmpty(stoneId.Value)) return false;
            stone = BuildProvisionalStone(stoneId);
            return true;
        }

        /// <summary>No validated Homestead owner is resolvable at runtime without the account-owner runtime,
        /// so this provisional source never claims ownership. An occupant qualifies via the Attuned policy
        /// (an active relationship), not ownership.</summary>
        public bool IsOwner(StoneId stoneId, AccountId occupant) => false;

        public bool AuthorizedGovernorPresent(StoneId stoneId) => _governorPresent;

        private StoneProgressionAggregate BuildProvisionalStone(StoneId stoneId)
        {
            var committed = new List<CommittedTreeRecord>
            {
                // The owning Warrior Tree committed into the Martial Facet, so the T.W.I.G. node is not
                // dormant on an uncommitted Tree. A Cooking commit is included only to mirror the shared
                // test harness shape; it does not affect the Warrior node.
                new CommittedTreeRecord(HomesteadProgressionCatalog.MartialFacetId,
                    WarriorTree, "provisional-commit-warrior", "provisional", 1, 0),
                new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                    CookingTree, "provisional-commit-cooking", "provisional", 1, 0),
            };

            var development = new List<NodeDevelopmentRecord>
            {
                // The ONE authored Warrior Local node, developed as Stone-owned Local state.
                new NodeDevelopmentRecord(TwigNode, 1, 1, developed: true, offered: false, "provisional-dev-twig"),
            };

            var policy = new SettlementLocalPolicy(_policyMode, 1);

            return new StoneProgressionAggregate(
                stoneId,
                revision: 1,
                historicalStoneLevel: ProvisionalActiveStoneLevel,
                activeStoneLevel: ProvisionalActiveStoneLevel,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1,
                createdProvenance: "provisional",
                updatedProvenance: "provisional",
                mirroredStoneAp: 0,
                lastAppliedReceiptId: "provisional",
                committedTrees: committed,
                nodeDevelopment: development,
                localPolicy: policy);
        }
    }
}
