using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;

namespace SBPR.Niflheim.HomesteadStones.Persistence.Recovery
{
    // Operator-facing recovery for the Foundational AP receipt journal (T002, Gate A;
    // AT-P0-RECOVERY-REPORT lineage). It states ONLY what the durable journal proves and never
    // invents a repair: an ambiguous (partial, non-terminal) state is reported QUARANTINE for an
    // operator to decide, not auto-guessed (data-model.md validation-and-recovery).
    //
    // net48 audit: StringBuilder / Dictionary / value objects only. No net5+ API, no UnityEngine /
    // Valheim reference, so this file link-compiles into the net8 test project.

    public enum RecoveryStatus
    {
        Clean,        // no durable record for this op — it never began
        Recoverable,  // terminal result durable; replay converges to the recorded balances
        Quarantine    // partial durable state, no terminal — operator must decide (never guessed)
    }

    public readonly struct OperationRecoveryState
    {
        public OperationRecoveryState(string operationId, RecoveryStatus status,
            ReceiptBoundary lastBoundary, int personalAp, int cumulativeAp, int mirroredStoneAp)
        {
            OperationId = operationId;
            Status = status;
            LastBoundary = lastBoundary;
            PersonalAp = personalAp;
            CumulativeAp = cumulativeAp;
            MirroredStoneAp = mirroredStoneAp;
        }

        public string OperationId { get; }
        public RecoveryStatus Status { get; }
        public ReceiptBoundary LastBoundary { get; }
        public int PersonalAp { get; }
        public int CumulativeAp { get; }
        public int MirroredStoneAp { get; }
    }

    public sealed class ReceiptRecovery
    {
        private readonly OperationReceiptStore _receipts;

        public ReceiptRecovery(OperationReceiptStore receipts)
        {
            _receipts = receipts ?? throw new ArgumentNullException(nameof(receipts));
        }

        /// <summary>Classify one operation's durable state from the journal only.</summary>
        public OperationRecoveryState Inspect(string operationId)
        {
            var view = _receipts.InspectJournal(operationId);
            RecoveryStatus status;
            if (view.HasTerminal) status = RecoveryStatus.Recoverable;
            else if (!view.SawAnyRecord) status = RecoveryStatus.Clean;
            else status = RecoveryStatus.Quarantine;

            return new OperationRecoveryState(operationId, status, view.LastPhase,
                view.Projection.PersonalAp, view.Projection.CumulativeAp, view.Projection.MirroredStoneAp);
        }

        /// <summary>Classify every operation present in the durable journal.</summary>
        public IReadOnlyList<OperationRecoveryState> InspectAll()
        {
            var states = new List<OperationRecoveryState>();
            foreach (var opId in _receipts.DurableOperationIds())
                states.Add(Inspect(opId));
            return states;
        }

        /// <summary>Human-readable operator report. Reports the truncated torn-tail byte count and
        /// re-derived balances; never claims to repair anything.</summary>
        public string BuildReport(string operationId)
        {
            long tornTail;
            var durable = _receipts.ReadDurable(out tornTail);
            var state = Inspect(operationId);

            var sb = new StringBuilder();
            sb.AppendLine("=== Gate-A Recovery Report ===");
            sb.AppendLine("journal: " + _receipts.JournalPath);
            sb.AppendLine("operationId: " + operationId);
            sb.AppendLine("durable_records_total: " + durable.Count.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("torn_tail_bytes_truncated: " + tornTail.ToString(CultureInfo.InvariantCulture)
                + (tornTail > 0 ? "  (partial write from process death; ignored, not repaired)" : ""));
            sb.AppendLine("last_durable_boundary: " + state.LastBoundary);
            sb.AppendLine("recovery_status: " + Describe(state.Status));
            sb.AppendLine("re-derived balances (from journal only):");
            sb.AppendLine("  PersonalAP:      " + state.PersonalAp.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("  CumulativeAP:    " + state.CumulativeAp.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("  MirroredStoneAP: " + state.MirroredStoneAp.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static string Describe(RecoveryStatus status)
        {
            switch (status)
            {
                case RecoveryStatus.Recoverable: return "RECOVERABLE (terminal result durable; replay converges)";
                case RecoveryStatus.Clean: return "CLEAN (no record; operation never durably began)";
                default: return "QUARANTINE (partial durable state, no terminal result; operator must decide - not auto-guessed)";
            }
        }
    }
}
