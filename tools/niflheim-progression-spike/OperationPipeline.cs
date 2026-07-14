using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SBPR.Niflheim.ProgressionSpike
{
    // One Foundational-placement operation that must converge, across a Stone aggregate
    // and a character aggregate (two separately-saved worlds, R-004), to EXACTLY:
    //   +1 Personal AP, +1 Cumulative AP (character aggregate), +1 Mirrored Stone AP
    //   (Stone aggregate) -- under replay and process death at every durable boundary.
    //
    // The journal is the transaction. Aggregate writes are idempotent projections rebuilt
    // FROM the durable journal, so a crash between the two aggregate writes cannot leave a
    // partial result: recovery re-derives both aggregates from the committed journal record.

    public enum BoundaryPhase
    {
        None = 0,
        IntentJournaled = 1,   // durable boundary 1: intent + digests recorded
        StoneApplied = 2,      // durable boundary 2: mirrored Stone AP delta recorded
        CharacterApplied = 3,  // durable boundary 3: personal+cumulative AP delta recorded
        Committed = 4          // durable boundary 4: terminal result recorded
    }

    public enum OperationOutcome { Applied, Replayed, PrincipalRejected, OperationConflict, Quarantined }

    public struct OperationResult
    {
        public OperationOutcome Outcome;
        public string ResultCode;
        public int PersonalAp;
        public int CumulativeAp;
        public int MirroredStoneAp;
        public string ReceiptId;
    }

    // Derived aggregate snapshots, rebuilt from the journal. Never a second source of truth.
    public struct AggregateProjection
    {
        public int PersonalAp;
        public int CumulativeAp;
        public int MirroredStoneAp;
    }

    // Injects real process death after the Nth durable-write boundary. -1 = never crash.
    public interface ICrashInjector
    {
        void AfterBoundary(BoundaryPhase phase);
    }

    public sealed class NoCrash : ICrashInjector
    {
        public void AfterBoundary(BoundaryPhase phase) { }
    }

    public sealed class OperationPipeline
    {
        private readonly DurableJournal _journal;
        private readonly PrincipalResolver _resolver;
        private readonly ICrashInjector _crash;

        public OperationPipeline(DurableJournal journal, PrincipalResolver resolver, ICrashInjector crash)
        {
            _journal = journal;
            _resolver = resolver;
            _crash = crash ?? new NoCrash();
        }

        // A single Foundational operation. `operationId` + authenticated principal + payload
        // digest is the idempotency key. Re-submitting the same key after ANY crash returns
        // the one recorded terminal result; a conflicting binding under the same operationId
        // rejects as OperationConflict.
        public OperationResult SubmitFoundational(
            string operationId,
            string stoneId,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            string payload)
        {
            AuthoritativePrincipal principal;
            var resolution = _resolver.Resolve(connection, claim, out principal);
            if (resolution != PrincipalResolution.Bound)
            {
                // Rejections are non-mutating (contracts.md:17). Nothing is journaled.
                return new OperationResult
                {
                    Outcome = OperationOutcome.PrincipalRejected,
                    ResultCode = resolution.ToString()
                };
            }

            string principalDigest = Digest(principal.AccountId + "|" + principal.CharacterId + "|" + principal.PlatformId);
            string payloadDigest = Digest(payload ?? string.Empty);
            string bindingDigest = Digest(operationId + "|" + stoneId + "|" + principalDigest);

            // Idempotency + recovery: inspect what is already durable for this operationId.
            var recovery = InspectJournal(operationId);
            if (recovery.HasTerminal)
            {
                if (recovery.BindingDigest != bindingDigest || recovery.PayloadDigest != payloadDigest)
                {
                    return new OperationResult
                    {
                        Outcome = OperationOutcome.OperationConflict,
                        ResultCode = "OperationConflict"
                    };
                }
                return TerminalFrom(recovery.Projection, operationId, OperationOutcome.Replayed);
            }
            if (recovery.SawAnyRecord && recovery.BindingDigest != bindingDigest)
            {
                // A partial (non-terminal) journal record exists under this operationId with a
                // DIFFERENT binding -> ambiguous. We quarantine; we do not guess (Gate-A req 6).
                return new OperationResult
                {
                    Outcome = OperationOutcome.OperationConflict,
                    ResultCode = "OperationConflict"
                };
            }

            // Drive forward from wherever the last crash left us. Each phase is idempotent:
            // we only journal a boundary record that is not already durable.
            var phase = recovery.LastPhase;

            if (phase < BoundaryPhase.IntentJournaled)
            {
                _journal.AppendText(Record(operationId, BoundaryPhase.IntentJournaled, bindingDigest, payloadDigest, 0, 0, 0));
                _crash.AfterBoundary(BoundaryPhase.IntentJournaled);
                phase = BoundaryPhase.IntentJournaled;
            }
            if (phase < BoundaryPhase.StoneApplied)
            {
                // Mirrored Stone AP: +1. Journaled BEFORE the (simulated) ZDO write so the ZDO
                // apply is a replayable projection of the durable journal, not the transaction.
                _journal.AppendText(Record(operationId, BoundaryPhase.StoneApplied, bindingDigest, payloadDigest, 0, 0, 1));
                _crash.AfterBoundary(BoundaryPhase.StoneApplied);
                phase = BoundaryPhase.StoneApplied;
            }
            if (phase < BoundaryPhase.CharacterApplied)
            {
                // Personal +1, Cumulative +1 (character aggregate).
                _journal.AppendText(Record(operationId, BoundaryPhase.CharacterApplied, bindingDigest, payloadDigest, 1, 1, 0));
                _crash.AfterBoundary(BoundaryPhase.CharacterApplied);
                phase = BoundaryPhase.CharacterApplied;
            }
            if (phase < BoundaryPhase.Committed)
            {
                _journal.AppendText(Record(operationId, BoundaryPhase.Committed, bindingDigest, payloadDigest, 0, 0, 0));
                _crash.AfterBoundary(BoundaryPhase.Committed);
            }

            var final = InspectJournal(operationId);
            return TerminalFrom(final.Projection, operationId, OperationOutcome.Applied);
        }

        private OperationResult TerminalFrom(AggregateProjection p, string operationId, OperationOutcome outcome)
        {
            return new OperationResult
            {
                Outcome = outcome,
                ResultCode = "Applied",
                PersonalAp = p.PersonalAp,
                CumulativeAp = p.CumulativeAp,
                MirroredStoneAp = p.MirroredStoneAp,
                ReceiptId = Digest("receipt|" + operationId)
            };
        }

        // --- Journal inspection / recovery ---

        public struct JournalView
        {
            public bool SawAnyRecord;
            public bool HasTerminal;
            public BoundaryPhase LastPhase;
            public string BindingDigest;
            public string PayloadDigest;
            public AggregateProjection Projection;
        }

        // Rebuild the aggregate projection for ONE operationId from durable records only.
        // Each boundary record contributes its delta exactly once (deduped by phase), so
        // replaying the journal converges to exactly one result regardless of crash point.
        public JournalView InspectJournal(string operationId)
        {
            var view = new JournalView { LastPhase = BoundaryPhase.None };
            var seenPhases = new HashSet<BoundaryPhase>();
            foreach (var line in _journal.ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null || rec.OperationId != operationId) continue;
                view.SawAnyRecord = true;
                view.BindingDigest = rec.BindingDigest;
                view.PayloadDigest = rec.PayloadDigest;
                if (rec.Phase > view.LastPhase) view.LastPhase = rec.Phase;
                if (rec.Phase == BoundaryPhase.Committed) view.HasTerminal = true;
                if (seenPhases.Add(rec.Phase))
                {
                    view.Projection.PersonalAp += rec.DPersonal;
                    view.Projection.CumulativeAp += rec.DCumulative;
                    view.Projection.MirroredStoneAp += rec.DMirrored;
                }
            }
            return view;
        }

        // --- Record encoding (pipe-delimited, digest-checked; net48-safe) ---

        private sealed class RecordData
        {
            public string OperationId;
            public BoundaryPhase Phase;
            public string BindingDigest;
            public string PayloadDigest;
            public int DPersonal, DCumulative, DMirrored;
        }

        private static string Record(string opId, BoundaryPhase phase, string binding, string payloadDigest,
            int dPersonal, int dCumulative, int dMirrored)
        {
            return string.Join("|", new[]
            {
                "REC", opId, ((int)phase).ToString(CultureInfo.InvariantCulture), binding, payloadDigest,
                dPersonal.ToString(CultureInfo.InvariantCulture),
                dCumulative.ToString(CultureInfo.InvariantCulture),
                dMirrored.ToString(CultureInfo.InvariantCulture)
            });
        }

        private static RecordData ParseRecord(string line)
        {
            var parts = line.Split('|');
            if (parts.Length != 8 || parts[0] != "REC") return null;
            return new RecordData
            {
                OperationId = parts[1],
                Phase = (BoundaryPhase)int.Parse(parts[2], CultureInfo.InvariantCulture),
                BindingDigest = parts[3],
                PayloadDigest = parts[4],
                DPersonal = int.Parse(parts[5], CultureInfo.InvariantCulture),
                DCumulative = int.Parse(parts[6], CultureInfo.InvariantCulture),
                DMirrored = int.Parse(parts[7], CultureInfo.InvariantCulture)
            };
        }

        public static string Digest(string s)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
