using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SBPR.Niflheim.ProgressionSpike
{
    // Operator-readable recovery report (AT-P0-RECOVERY-REPORT). It states ONLY what the
    // durable journal proves: which boundaries are committed, whether a terminal result
    // exists, whether a torn tail was truncated, and the re-derived aggregate balances.
    // It never invents a repair; an ambiguous state is reported as QUARANTINE, not guessed.
    public static class RecoveryReport
    {
        public static string Build(DurableJournal journal, string operationId, OperationPipeline pipeline)
        {
            long tornTail;
            var durable = journal.ReadDurable(out tornTail);
            var view = pipeline.InspectJournal(operationId);

            var sb = new StringBuilder();
            sb.AppendLine("=== Gate-A Recovery Report ===");
            sb.AppendLine("journal: " + journal.Path);
            sb.AppendLine("operationId: " + operationId);
            sb.AppendLine("durable_records_total: " + durable.Count.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("torn_tail_bytes_truncated: " + tornTail.ToString(CultureInfo.InvariantCulture)
                + (tornTail > 0 ? "  (partial write from process death; ignored, not repaired)" : ""));
            sb.AppendLine("last_durable_boundary: " + view.LastPhase);
            sb.AppendLine("terminal_result_present: " + (view.HasTerminal ? "yes" : "no"));

            string status;
            if (view.HasTerminal) status = "RECOVERABLE (terminal result durable; replay converges)";
            else if (!view.SawAnyRecord) status = "CLEAN (no record; operation never durably began)";
            else status = "QUARANTINE (partial durable state, no terminal result; operator must decide - not auto-guessed)";
            sb.AppendLine("recovery_status: " + status);

            sb.AppendLine("re-derived balances (from journal only):");
            sb.AppendLine("  PersonalAP:     " + view.Projection.PersonalAp.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("  CumulativeAP:   " + view.Projection.CumulativeAp.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("  MirroredStoneAP:" + view.Projection.MirroredStoneAp.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
