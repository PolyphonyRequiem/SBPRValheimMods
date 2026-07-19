using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Activities
{
    // T012 — trusted aligned-activity evidence adapter (contracts.md §"RecordAlignedActivity";
    // data-model.md §"Credit and spend BP on node development"). Architecture decision A1 (plan.md):
    // adapters translate trusted, server-observed runtime facts into typed evidence; they never mutate
    // ledgers. This adapter's ENGINE-FREE core validates that an observed Cooking/Crafting/Archer/
    // Warrior activity is eligible to award BP to a bonded Governor and builds the pipeline command +
    // evidence digest from already-server-validated facts.
    //
    // Accepted contract encoded here (contracts.md RecordAlignedActivity):
    //   * The content definition determines the award. In this proof slice an eligible aligned activity
    //     awards N BP to the acting bonded character's ONE Stone-wide personal BP balance.
    //   * An activity is eligible only when it is associated with a COMMITTED Tree on the Stone
    //     (committedTreeContext[]): "Uncommitted optional candidates cannot authorize activity credit."
    //     The adapter carries the associated Tree; the command layer confirms the Tree is committed and
    //     within the Governor's Responsibility Range and that the actor holds an active Bond.
    //   * Client claims are NEVER the source of truth: the actor, Stone, event type, and outcome passed
    //     here are server-observed; the claim carried on the command is only compared by the handler.
    //
    // net48 audit: string/value objects + engine-free receipt digest only. No net5+ API, no UnityEngine/
    // Valheim reference in this core file, so it link-compiles into the net8 test project.

    /// <summary>Server-observed facts about one eligible aligned activity event (contracts.md
    /// AlignedActivityEvidence). Every field is attributed by trusted server code; no field is taken
    /// from the client payload.</summary>
    public readonly struct AlignedActivityEvidence
    {
        public AlignedActivityEvidence(
            OperationId operationId,
            StoneId stoneId,
            string activityDefinitionId,
            int activityDefinitionVersion,
            string observedEventType,
            VersionedId associatedTree,
            int bpAward,
            bool serverAttributed)
        {
            OperationId = operationId;
            StoneId = stoneId;
            ActivityDefinitionId = activityDefinitionId;
            ActivityDefinitionVersion = activityDefinitionVersion;
            ObservedEventType = observedEventType;
            AssociatedTree = associatedTree;
            BpAward = bpAward;
            ServerAttributed = serverAttributed;
        }

        public OperationId OperationId { get; }
        public StoneId StoneId { get; }
        public string ActivityDefinitionId { get; }
        public int ActivityDefinitionVersion { get; }
        public string ObservedEventType { get; }

        /// <summary>The Committed Tree this activity is associated with (committedTreeContext). An
        /// uncommitted/optional candidate cannot authorize credit; the command layer confirms it is
        /// committed and in the Governor's Responsibility Range.</summary>
        public VersionedId AssociatedTree { get; }

        /// <summary>The authored BP the content definition awards for this event (proof slice: N BP to
        /// the bonded character's one Stone-wide balance). Must be positive to admit.</summary>
        public int BpAward { get; }

        /// <summary>True only when trusted server code attributed this event. A non-server-attributed
        /// event never authorizes credit.</summary>
        public bool ServerAttributed { get; }
    }

    public enum AlignedActivityAdmission
    {
        Admitted,
        NotServerAttributed,   // the event was not attributed by trusted server code
        MissingActivityId,     // no stable activity definition id
        NoAssociatedTree,      // no committedTreeContext Tree to associate the credit with
        NonPositiveAward       // the content definition awards no BP for this event
    }

    public readonly struct AlignedActivityAdmissionResult
    {
        public AlignedActivityAdmissionResult(AlignedActivityAdmission admission,
            RecordAlignedActivityCommand command)
        {
            Admission = admission;
            Command = command;
        }

        public AlignedActivityAdmission Admission { get; }
        public bool IsAdmitted => Admission == AlignedActivityAdmission.Admitted;

        /// <summary>Only meaningful when <see cref="IsAdmitted"/>. The command the handler processes.</summary>
        public RecordAlignedActivityCommand Command { get; }
    }

    public sealed class AlignedActivityAdapter
    {
        /// <summary>Translate server-observed aligned-activity evidence into a BP-credit command. The
        /// connection/claim identity is attributed by the transport and threaded through unchanged; the
        /// command handler authenticates it and confirms the associated Tree is committed and in the
        /// Governor's Responsibility Range. Admitted ONLY when the event is server-attributed, carries a
        /// stable activity id and an associated Tree, and the content definition awards positive BP.</summary>
        public AlignedActivityAdmissionResult Admit(
            AlignedActivityEvidence evidence,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            long? expectedCharacterRevision = null)
        {
            if (!evidence.ServerAttributed)
                return Rejected(AlignedActivityAdmission.NotServerAttributed);
            if (string.IsNullOrEmpty(evidence.ActivityDefinitionId))
                return Rejected(AlignedActivityAdmission.MissingActivityId);
            if (evidence.AssociatedTree.IsNone)
                return Rejected(AlignedActivityAdmission.NoAssociatedTree);
            if (evidence.BpAward <= 0)
                return Rejected(AlignedActivityAdmission.NonPositiveAward);

            var command = new RecordAlignedActivityCommand(
                evidence.OperationId,
                evidence.StoneId,
                connection,
                claim,
                evidence.AssociatedTree,
                evidence.BpAward,
                BuildEvidenceDigest(evidence),
                expectedCharacterRevision);
            return new AlignedActivityAdmissionResult(AlignedActivityAdmission.Admitted, command);
        }

        /// <summary>Stable digest of the server-observed evidence, bound into the receipt so a replayed
        /// operationId carrying a different activity rejects as OperationConflict.</summary>
        public static string BuildEvidenceDigest(AlignedActivityEvidence e)
        {
            string material = string.Join("|", new[]
            {
                "aligned-activity",
                e.StoneId.Value,
                e.ActivityDefinitionId ?? string.Empty,
                e.ActivityDefinitionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                e.ObservedEventType ?? string.Empty,
                e.AssociatedTree.Serialize(),
                e.BpAward.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
            return OperationReceiptStore.Digest(material);
        }

        private static AlignedActivityAdmissionResult Rejected(AlignedActivityAdmission admission) =>
            new AlignedActivityAdmissionResult(admission, default);
    }
}
