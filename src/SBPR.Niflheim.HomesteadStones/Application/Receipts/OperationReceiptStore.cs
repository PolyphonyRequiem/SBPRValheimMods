using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;

namespace SBPR.Niflheim.HomesteadStones.Application.Receipts
{
    // Durable, idempotent operation-receipt store for the Foundational AP slice (T002, Gate A).
    // This is the CLEAN-side production home of the T001 spike's selected mechanism (research.md
    // Gate A: receipt candidate 1 — append-only write-ahead journal with per-boundary fsync).
    //
    // The journal IS the transaction. The Stone-owned Mirrored AP aggregate and the character-owned
    // Personal/Cumulative AP aggregate are IDEMPOTENT PROJECTIONS rebuilt from the durable journal
    // (data-model.md OperationReceiptStore; R-004). A crash between the two separately-saved
    // aggregates therefore cannot leave a partial result: recovery re-derives both from the one
    // committed journal record. The three AP deltas "commit together" because a reader only ever
    // observes balances derived from a terminal-bearing journal.
    //
    // net48 audit: FileStream(.Flush(true)), BinaryReader/Writer, File.Exists, Path, Encoding.UTF8,
    // SHA256, unchecked CRC32 over bytes — every API exists in .NET Framework 4.8. No net5+ surface,
    // no UnityEngine/Valheim/BepInEx reference, so this file link-compiles into the net8 test project.

    /// <summary>Durable-write boundaries for one Foundational AP operation. Each is fsync'd once.</summary>
    public enum ReceiptBoundary
    {
        None = 0,
        IntentJournaled = 1,   // boundary 1: intent + digests recorded
        StoneApplied = 2,      // boundary 2: Mirrored Stone AP delta recorded
        CharacterApplied = 3,  // boundary 3: Personal + Cumulative AP delta recorded
        Committed = 4          // boundary 4: terminal result recorded
    }

    public enum ReceiptOutcome
    {
        Applied,
        Replayed,
        PrincipalRejected,
        OperationConflict,
        StaleStoneRevision,
        StaleCharacterRevision
    }

    /// <summary>The terminal result of a Foundational AP submission.</summary>
    public readonly struct ApReceiptResult
    {
        public ApReceiptResult(ReceiptOutcome outcome, string resultCode, int personalAp, int cumulativeAp,
            int mirroredStoneAp, string receiptId, long stoneRevision = 0, long characterRevision = 0)
        {
            Outcome = outcome;
            ResultCode = resultCode;
            PersonalAp = personalAp;
            CumulativeAp = cumulativeAp;
            MirroredStoneAp = mirroredStoneAp;
            ReceiptId = receiptId;
            StoneRevision = stoneRevision;
            CharacterRevision = characterRevision;
        }

        public ReceiptOutcome Outcome { get; }
        public string ResultCode { get; }
        public int PersonalAp { get; }
        public int CumulativeAp { get; }
        public int MirroredStoneAp { get; }
        public string ReceiptId { get; }

        /// <summary>Stone aggregate revision committed by (or observed at) this operation. On a
        /// stale-revision rejection this carries the current revision the caller must refetch.</summary>
        public long StoneRevision { get; }

        /// <summary>Character-at-Stone aggregate revision committed by (or observed at) this operation.</summary>
        public long CharacterRevision { get; }
    }

    /// <summary>Re-derived aggregate projection for one operation, rebuilt from durable records only.</summary>
    public readonly struct ApProjection
    {
        public ApProjection(int personalAp, int cumulativeAp, int mirroredStoneAp)
        {
            PersonalAp = personalAp;
            CumulativeAp = cumulativeAp;
            MirroredStoneAp = mirroredStoneAp;
        }

        public int PersonalAp { get; }
        public int CumulativeAp { get; }
        public int MirroredStoneAp { get; }
    }

    /// <summary>Injects real process death after the Nth durable boundary. Default never crashes.</summary>
    public interface ICrashInjector
    {
        void AfterBoundary(ReceiptBoundary boundary);
    }

    public sealed class NoCrash : ICrashInjector
    {
        public static readonly NoCrash Instance = new NoCrash();
        public void AfterBoundary(ReceiptBoundary boundary) { }
    }

    public sealed class OperationReceiptStore
    {
        private readonly string _journalPath;
        private readonly IMirroredStoneApStore _stoneStore;
        private readonly ICharacterApStore _characterStore;

        public OperationReceiptStore(string journalPath, IMirroredStoneApStore stoneStore, ICharacterApStore characterStore)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            _stoneStore = stoneStore ?? throw new ArgumentNullException(nameof(stoneStore));
            _characterStore = characterStore ?? throw new ArgumentNullException(nameof(characterStore));
        }

        public string JournalPath => _journalPath;

        /// <summary>
        /// Submit one Foundational-placement AP operation. <paramref name="operationId"/> + the
        /// authenticated principal + the payload digest is the idempotency key: re-submitting the
        /// same key after ANY crash returns the one recorded terminal result; a conflicting binding
        /// under the same operationId rejects as <c>OperationConflict</c> with no mutation.
        /// </summary>
        public ApReceiptResult SubmitFoundationalAp(
            OperationId operationId,
            StoneId stoneId,
            AuthoritativePrincipal principal,
            string evidenceDigest,
            ICrashInjector? crash = null,
            long? expectedStoneRevision = null,
            long? expectedCharacterRevision = null)
        {
            crash = crash ?? NoCrash.Instance;
            string opId = operationId.Value;
            string principalDigest = Digest(principal.Account.Value + "|" + principal.Character.Value + "|" + principal.PlatformId);
            string payloadDigest = Digest(evidenceDigest ?? string.Empty);
            string bindingDigest = Digest(opId + "|" + stoneId.Value + "|" + principalDigest);

            var view = InspectJournal(opId);

            if (view.HasTerminal)
            {
                // Idempotent replay. A conflicting binding/payload under a committed op is a conflict.
                // NOTE: an expected-revision that no longer matches is NOT a stale conflict on replay —
                // the operation already committed exactly once; we return the one recorded result so a
                // retry/reconnect converges (contracts.md: replay returns the recorded result).
                if (view.BindingDigest != bindingDigest || view.PayloadDigest != payloadDigest)
                    return Conflict(opId);
                ApplyProjections(opId, stoneId, principal, view.Projection);
                return Terminal(view.Projection, opId, ReceiptOutcome.Replayed, stoneId, principal);
            }
            if (view.SawAnyRecord && view.BindingDigest != bindingDigest)
            {
                // A partial (non-terminal) record exists under this operationId with a DIFFERENT
                // binding -> ambiguous. Reject; never guess (data-model.md idempotency invariant).
                return Conflict(opId);
            }

            // Optimistic-concurrency (CAS) gate. Validation completes BEFORE any journal write, so a
            // stale-revision command changes nothing (contracts.md: failure changes nothing). Only a
            // brand-new operation is gated; a resumed partial (same binding) has already passed CAS,
            // so re-gating it against an advanced revision would wrongly strand a recoverable op.
            if (!view.SawAnyRecord)
            {
                long currentStoneRev = _stoneStore.GetStoneRevision(stoneId);
                if (expectedStoneRevision.HasValue && expectedStoneRevision.Value != currentStoneRev)
                    return StaleStone(opId, currentStoneRev);

                long currentCharRev = _characterStore.GetCharacterRevision(principal.Account, principal.Character, stoneId);
                if (expectedCharacterRevision.HasValue && expectedCharacterRevision.Value != currentCharRev)
                    return StaleCharacter(opId, currentCharRev);
            }

            // Drive forward from wherever the last crash left us. Each phase writes only the boundary
            // record that is not already durable, so replay is idempotent.
            var phase = view.LastPhase;

            if (phase < ReceiptBoundary.IntentJournaled)
            {
                Append(Record(opId, ReceiptBoundary.IntentJournaled, bindingDigest, payloadDigest, 0, 0, 0));
                crash.AfterBoundary(ReceiptBoundary.IntentJournaled);
                phase = ReceiptBoundary.IntentJournaled;
            }
            if (phase < ReceiptBoundary.StoneApplied)
            {
                // Mirrored Stone AP: +1. Journaled BEFORE the aggregate write, so the Stone write is a
                // replayable projection of the durable journal, not the transaction itself.
                Append(Record(opId, ReceiptBoundary.StoneApplied, bindingDigest, payloadDigest, 0, 0, 1));
                crash.AfterBoundary(ReceiptBoundary.StoneApplied);
                phase = ReceiptBoundary.StoneApplied;
            }
            if (phase < ReceiptBoundary.CharacterApplied)
            {
                // Personal +1, Cumulative +1 (character aggregate).
                Append(Record(opId, ReceiptBoundary.CharacterApplied, bindingDigest, payloadDigest, 1, 1, 0));
                crash.AfterBoundary(ReceiptBoundary.CharacterApplied);
                phase = ReceiptBoundary.CharacterApplied;
            }
            if (phase < ReceiptBoundary.Committed)
            {
                Append(Record(opId, ReceiptBoundary.Committed, bindingDigest, payloadDigest, 0, 0, 0));
                crash.AfterBoundary(ReceiptBoundary.Committed);
            }

            var final = InspectJournal(opId);
            ApplyProjections(opId, stoneId, principal, final.Projection);
            return Terminal(final.Projection, opId, ReceiptOutcome.Applied, stoneId, principal);
        }

        /// <summary>Idempotently project a committed operation's re-derived balances into the
        /// server-owned Stone and character aggregates. Set-to-total, never blind increment, so
        /// replay after crash converges rather than double-counting.</summary>
        private void ApplyProjections(string operationId, StoneId stoneId, AuthoritativePrincipal principal, ApProjection projection)
        {
            _stoneStore.ApplyMirroredApProjection(stoneId, operationId, projection.MirroredStoneAp);
            _characterStore.ApplyApProjection(principal.Account, principal.Character, stoneId, operationId,
                projection.PersonalAp, projection.CumulativeAp);
        }

        private static ApReceiptResult Conflict(string opId) =>
            new ApReceiptResult(ReceiptOutcome.OperationConflict, "OperationConflict", 0, 0, 0, string.Empty);

        private static ApReceiptResult StaleStone(string opId, long currentStoneRevision) =>
            new ApReceiptResult(ReceiptOutcome.StaleStoneRevision, "StaleStoneRevision", 0, 0, 0, string.Empty,
                stoneRevision: currentStoneRevision);

        private static ApReceiptResult StaleCharacter(string opId, long currentCharacterRevision) =>
            new ApReceiptResult(ReceiptOutcome.StaleCharacterRevision, "StaleCharacterRevision", 0, 0, 0, string.Empty,
                characterRevision: currentCharacterRevision);

        /// <summary>Build the terminal result, stamping the committed aggregate revisions read back
        /// from the stores AFTER the idempotent projections were applied (so a replay reports the same
        /// revisions the original commit produced).</summary>
        private ApReceiptResult Terminal(ApProjection p, string operationId, ReceiptOutcome outcome,
            StoneId stoneId, AuthoritativePrincipal principal) =>
            new ApReceiptResult(outcome, "Applied", p.PersonalAp, p.CumulativeAp, p.MirroredStoneAp,
                Digest("receipt|" + operationId),
                _stoneStore.GetStoneRevision(stoneId),
                _characterStore.GetCharacterRevision(principal.Account, principal.Character, stoneId));

        // ---- Journal inspection / recovery ----

        public readonly struct JournalView
        {
            public JournalView(bool sawAnyRecord, bool hasTerminal, ReceiptBoundary lastPhase,
                string? bindingDigest, string? payloadDigest, ApProjection projection)
            {
                SawAnyRecord = sawAnyRecord;
                HasTerminal = hasTerminal;
                LastPhase = lastPhase;
                BindingDigest = bindingDigest;
                PayloadDigest = payloadDigest;
                Projection = projection;
            }

            public bool SawAnyRecord { get; }
            public bool HasTerminal { get; }
            public ReceiptBoundary LastPhase { get; }
            public string? BindingDigest { get; }
            public string? PayloadDigest { get; }
            public ApProjection Projection { get; }
        }

        /// <summary>Rebuild the AP projection for ONE operationId from durable records only. Each
        /// boundary contributes its delta exactly once (deduped by phase), so replaying the journal
        /// converges to exactly one result regardless of crash point.</summary>
        public JournalView InspectJournal(string operationId)
        {
            bool sawAny = false, hasTerminal = false;
            var lastPhase = ReceiptBoundary.None;
            string? binding = null, payload = null;
            int personal = 0, cumulative = 0, mirrored = 0;
            var seenPhases = new HashSet<ReceiptBoundary>();

            foreach (var line in ReadDurable(out _))
            {
                var rec = ParseRecord(line);
                if (rec == null || rec.OperationId != operationId) continue;
                sawAny = true;
                binding = rec.BindingDigest;
                payload = rec.PayloadDigest;
                if (rec.Phase > lastPhase) lastPhase = rec.Phase;
                if (rec.Phase == ReceiptBoundary.Committed) hasTerminal = true;
                if (seenPhases.Add(rec.Phase))
                {
                    personal += rec.DPersonal;
                    cumulative += rec.DCumulative;
                    mirrored += rec.DMirrored;
                }
            }

            return new JournalView(sawAny, hasTerminal, lastPhase, binding, payload,
                new ApProjection(personal, cumulative, mirrored));
        }

        /// <summary>All distinct operationIds with at least one durable record. Used by recovery.</summary>
        public IReadOnlyList<string> DurableOperationIds()
        {
            var ordered = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in ReadDurable(out _))
            {
                var rec = ParseRecord(line);
                if (rec == null) continue;
                if (seen.Add(rec.OperationId)) ordered.Add(rec.OperationId);
            }
            return ordered;
        }

        // ---- Append-only journal (candidate 1) ----

        // Record framing: [int32 payloadLen][uint32 crc32(payload)][payload bytes]. A record is
        // durable only when its full frame is present AND the crc matches. A torn tail from process
        // death is truncated on read (last fully-durable record wins) — never "repaired".
        private void Append(string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            using (var fs = new FileStream(_journalPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write(payload.Length);
                bw.Write(Crc32(payload));
                bw.Write(payload);
                bw.Flush();
                fs.Flush(true); // fsync-equivalent: the durable boundary
            }
        }

        /// <summary>Read only fully-durable records. A torn tail is reported (byte count) and ignored,
        /// not accepted or invented.</summary>
        public List<string> ReadDurable(out long tornTailBytes)
        {
            tornTailBytes = 0;
            var results = new List<string>();
            if (!File.Exists(_journalPath)) return results;

            using (var fs = new FileStream(_journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs, Encoding.UTF8))
            {
                long length = fs.Length;
                while (true)
                {
                    long recordStart = fs.Position;
                    if (recordStart + 8 > length) { tornTailBytes = length - recordStart; break; }
                    int payloadLen = br.ReadInt32();
                    uint crc = br.ReadUInt32();
                    if (payloadLen < 0 || fs.Position + payloadLen > length)
                    {
                        tornTailBytes = length - recordStart; break;
                    }
                    byte[] payload = br.ReadBytes(payloadLen);
                    if (payload.Length != payloadLen || Crc32(payload) != crc)
                    {
                        tornTailBytes = length - recordStart; break;
                    }
                    results.Add(Encoding.UTF8.GetString(payload));
                }
            }
            return results;
        }

        // ---- Record encoding (pipe-delimited, framed + crc-checked) ----

        private sealed class RecordData
        {
            public string OperationId = string.Empty;
            public ReceiptBoundary Phase;
            public string BindingDigest = string.Empty;
            public string PayloadDigest = string.Empty;
            public int DPersonal, DCumulative, DMirrored;
        }

        private static string Record(string opId, ReceiptBoundary phase, string binding, string payloadDigest,
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

        private static RecordData? ParseRecord(string line)
        {
            var parts = line.Split('|');
            if (parts.Length != 8 || parts[0] != "REC") return null;
            return new RecordData
            {
                OperationId = parts[1],
                Phase = (ReceiptBoundary)int.Parse(parts[2], CultureInfo.InvariantCulture),
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

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                table[i] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++)
                crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
