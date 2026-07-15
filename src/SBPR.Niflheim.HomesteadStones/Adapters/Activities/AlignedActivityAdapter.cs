using System;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Activities
{
    // T012 — the trusted aligned-activity evidence adapter (contracts.md §"RecordAlignedActivity";
    // data-model.md §"Credit and spend BP on node development"). Architecture decision A1 (plan.md):
    // adapters translate trusted, server-observed runtime facts into typed evidence; they never mutate
    // ledgers. This adapter's ENGINE-FREE core builds the BP-credit command + a stable evidence digest
    // from already-server-validated facts about one eligible Cooking/Crafting/Archer/Warrior activity.
    //
    // Unlike the ongoing Foundational placement source (which is always available while the relationship
    // holds), aligned-activity credit is EVIDENCE-ELIGIBLE only when the observed activity is associated
    // with a Committed Tree (committedTreeContext). An uncommitted optional candidate cannot authorize
    // activity credit (contracts.md: "Uncommitted optional candidates cannot authorize activity
    // credit"). The adapter admits only when the server observed a real, attributed activity outcome and
    // a non-empty committed-Tree context; the command handler then re-checks the live commitment +
    // Governor Responsibility Range before crediting (never trusting the adapter's context alone).
    //
    // Client claims are NEVER the source of truth: the activity outcome, source identity, and Tree
    // context passed here are server-observed. The claim on the command is only compared, never trusted.
    //
    // net48 audit: string/value objects + engine-free receipt digest only. No net5+ API, no
    // UnityEngine/Valheim reference, so this core file link-compiles into the net8 test project.

    /// <summary>Server-observed facts about one eligible aligned activity. Every field is attributed by
    /// trusted server code (contracts.md AlignedActivityEvidence); no field is taken from the client
    /// payload.</summary>
    public readonly struct AlignedActivityEvidence
    {
        public AlignedActivityEvidence(
            OperationId operationId,
            StoneId stoneId,
            VersionedId activityDefinition,
            VersionedId committedTreeContext,
            string observedEventType,
            string sourceIdentity,
            bool outcomeSucceeded,
            int bpAward)
        {
            OperationId = operationId;
            StoneId = stoneId;
            ActivityDefinition = activityDefinition;
            CommittedTreeContext = committedTreeContext;
            ObservedEventType = observedEventType ?? string.Empty;
            SourceIdentity = sourceIdentity ?? string.Empty;
            OutcomeSucceeded = outcomeSucceeded;
            BpAward = bpAward;
        }

        public OperationId OperationId { get; }
        public StoneId StoneId { get; }
        public VersionedId ActivityDefinition { get; }

        /// <summary>The Committed Tree this activity is associated with. Must be non-none; an activity
        /// with no committed-Tree context is ineligible for BP credit.</summary>
        public VersionedId CommittedTreeContext { get; }

        public string ObservedEventType { get; }
        public string SourceIdentity { get; }
        public bool OutcomeSucceeded { get; }

        /// <summary>Authored BP award for this activity (data-defined). Must be positive to earn.</summary>
        public int BpAward { get; }
    }

    public enum AlignedActivityAdmission
    {
        Admitted,
        OutcomeFailed,           // the observed activity outcome did not succeed
        MissingActivityIdentity, // no stable activity definition
        NotCommittedTreeContext, // no Committed Tree context -> ineligible for BP credit
        NoAward                  // the activity authors no positive BP award
    }

    public readonly struct AlignedActivityAdmissionResult
    {
        public AlignedActivityAdmissionResult(AlignedActivityAdmission admission, RecordAlignedActivityCommand command)
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
        /// <summary>Translate server-observed aligned-activity evidence into a BP-credit command. Admitted
        /// ONLY when the activity outcome succeeded, carries a stable activity definition and a non-none
        /// Committed Tree context, and authors a positive BP award. Any failing gate produces no command.
        /// The connection/claim identity is attributed by the transport and threaded through unchanged;
        /// the handler authenticates it and re-checks the live commitment + Responsibility Range.</summary>
        public AlignedActivityAdmissionResult Admit(
            AlignedActivityEvidence evidence,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            long? expectedCharacterRevision = null)
        {
            if (!evidence.OutcomeSucceeded)
                return Rejected(AlignedActivityAdmission.OutcomeFailed);
            if (evidence.ActivityDefinition.IsNone)
                return Rejected(AlignedActivityAdmission.MissingActivityIdentity);
            if (evidence.CommittedTreeContext.IsNone)
                return Rejected(AlignedActivityAdmission.NotCommittedTreeContext);
            if (evidence.BpAward <= 0)
                return Rejected(AlignedActivityAdmission.NoAward);

            var command = new RecordAlignedActivityCommand(
                evidence.OperationId,
                evidence.StoneId,
                connection,
                claim,
                evidence.CommittedTreeContext,
                evidence.BpAward,
                BuildEvidenceDigest(evidence),
                expectedCharacterRevision);
            return new AlignedActivityAdmissionResult(AlignedActivityAdmission.Admitted, command);
        }

        /// <summary>Stable digest of the server-observed evidence. Bound into the receipt so a replayed
        /// operationId carrying different activity facts rejects as OperationConflict.</summary>
        public static string BuildEvidenceDigest(AlignedActivityEvidence e)
        {
            string material = string.Join("|", new[]
            {
                "aligned-activity",
                e.StoneId.Value,
                e.ActivityDefinition.ToString(),
                e.CommittedTreeContext.ToString(),
                e.ObservedEventType,
                e.SourceIdentity,
                e.BpAward.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
            return OperationReceiptStore.Digest(material);
        }

        private static AlignedActivityAdmissionResult Rejected(AlignedActivityAdmission admission) =>
            new AlignedActivityAdmissionResult(admission, default);
    }
}
