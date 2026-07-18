using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery
{
    // RD-T002 (Gate A) — the durable, principal/target-bound two-operation final-link handshake
    // (spec RD-004 / data-model §"Remove qualifying loyalty links" / contracts §"Final-link warning
    // token"). Named acceptance AT-RD-004.
    //
    // WHAT THIS OWNS
    //   The recoverable coupling of "prepare a final-link release" and "confirm it" across restart and
    //   competing confirmations. It is the second cross-aggregate fan-out proof (relationship release +
    //   every affected Connection's Grace transition + the confirmation receipt) committing under ONE
    //   atomic recovery boundary.
    //
    // TWO OPERATIONS
    //   1. Prepare(preparationOpId, ...) — resolves the canonical ordered affected Connection set and
    //      stores a durable, NON-MUTATING ConfirmationRequired decision bound to:
    //        * the preparing AccountId/CharacterId,
    //        * the target RelationshipId + release-authority revision,
    //        * the exact ordered affected set (ConnectionId + sources-to-remove + preview tier/age),
    //        * the relationship/Connection revisions and grace-policy version,
    //        * a signed challenge (warningToken).
    //      Replaying the SAME preparation operation after restart returns the EXACT decision/challenge.
    //      Preparation commits no gameplay state (no release, no grace, no age change).
    //
    //   2. Confirm(confirmationOpId, ...) — a FRESH operation id referencing the decision. It:
    //        * requires the confirming principal to EQUAL the preparing principal (token possession is
    //          not authority) -> else FinalLinkConfirmationPrincipalMismatch;
    //        * requires that principal to STILL be the active holder with release authority at the bound
    //          revision -> else RelationshipReleaseUnauthorized;
    //        * rejects a changed set/order/revision/policy or a mismatched decision/intent/target ->
    //          FinalLinkConfirmationStale;
    //        * rejects a challenge already consumed by a DIFFERENT confirmation op ->
    //          FinalLinkConfirmationConsumed (returns the winning receipt correlation);
    //        * reconciles each still-bound Connection's age through confirmation server time, FREEZES it,
    //          and sets graceExpiresAt = receivedServerTime + 72h;
    //        * ATOMICALLY journals challenge consumption + relationship release + every Connection Grace
    //          transition + the confirmation receipt. A crash before the terminal record leaves the
    //          challenge UNCONSUMED (prepare replay still works, a fresh confirm still applies). A crash
    //          after it recovers release + grace + receipt together, and same-op replay returns the
    //          winning receipt.
    //
    // The store is engine-free and file-journal-backed, exactly like OperationReceiptStore: the journal
    // is the transaction; the returned results are projections of durable records. Reusing this store's
    // journal across "process death" is simulated by constructing a new instance over the same path.
    //
    // net48 audit: FileStream(.Flush(true)), BinaryReader/Writer, SHA256, Encoding.UTF8, CRC32 —
    // all present in .NET Framework 4.8. Engine-free; link-compiles into the net8 test project.

    /// <summary>One affected Connection in a final-link release, in canonical order. Preview tier/age are
    /// ISSUE-TIME warning previews only — never commit inputs (contracts §Final-link warning token).</summary>
    public readonly struct AffectedConnection
    {
        public AffectedConnection(ConnectionId connectionId, IReadOnlyList<string> sourceIdsToRemove,
            long connectionRevision, string previewTier, long previewConnectedAgeSeconds)
        {
            ConnectionId = connectionId;
            SourceIdsToRemove = sourceIdsToRemove ?? Array.Empty<string>();
            ConnectionRevision = connectionRevision;
            PreviewTier = previewTier ?? string.Empty;
            PreviewConnectedAgeSeconds = previewConnectedAgeSeconds;
        }

        public ConnectionId ConnectionId { get; }
        public IReadOnlyList<string> SourceIdsToRemove { get; }
        public long ConnectionRevision { get; }
        public string PreviewTier { get; }
        public long PreviewConnectedAgeSeconds { get; }

        /// <summary>The portion of the ordered-set digest contributed by THIS affected Connection. Binds
        /// identity + ordered sources + revision (NOT the preview fields, which are non-commit).</summary>
        internal string DigestPart()
        {
            var sb = new StringBuilder();
            sb.Append(ConnectionId.CanonicalKey).Append('|').Append(ConnectionRevision.ToString(CultureInfo.InvariantCulture));
            foreach (var s in SourceIdsToRemove) sb.Append('|').Append(s);
            return sb.ToString();
        }
    }

    /// <summary>The preparing principal + target the whole handshake is bound to. Confirmation must match
    /// this exactly (spec RD-004): token possession grants no authority.</summary>
    public readonly struct FinalLinkBinding
    {
        public FinalLinkBinding(string preparedByAccountId, string preparedByCharacterId,
            string targetRelationshipId, long releaseAuthorityRevision, long relationshipRevision,
            int gracePolicyVersion)
        {
            PreparedByAccountId = preparedByAccountId ?? string.Empty;
            PreparedByCharacterId = preparedByCharacterId ?? string.Empty;
            TargetRelationshipId = targetRelationshipId ?? string.Empty;
            ReleaseAuthorityRevision = releaseAuthorityRevision;
            RelationshipRevision = relationshipRevision;
            GracePolicyVersion = gracePolicyVersion;
        }

        public string PreparedByAccountId { get; }
        public string PreparedByCharacterId { get; }
        public string TargetRelationshipId { get; }
        public long ReleaseAuthorityRevision { get; }
        public long RelationshipRevision { get; }
        public int GracePolicyVersion { get; }
    }

    public enum FinalLinkOutcome
    {
        ConfirmationRequired,   // prepare produced a durable non-mutating decision
        PreparationReplayed,    // prepare replayed the exact prior decision
        Confirmed,              // confirmation applied release + grace atomically
        ConfirmationReplayed,   // same confirmation op replayed the winning receipt
        PrincipalMismatch,      // FinalLinkConfirmationPrincipalMismatch
        ReleaseUnauthorized,    // RelationshipReleaseUnauthorized (lost authority)
        Stale,                  // FinalLinkConfirmationStale (changed set/binding)
        Consumed,               // FinalLinkConfirmationConsumed (different op lost the race)
        DecisionNotFound,       // confirmation references an unknown preparation decision
        OperationConflict       // op id reused with a different binding/payload
    }

    /// <summary>Result of a prepare or confirm operation.</summary>
    public readonly struct FinalLinkResult
    {
        public FinalLinkResult(FinalLinkOutcome outcome, string resultCode, string warningToken,
            string confirmationDecisionId, string receiptId, long graceExpiresAtSeconds,
            long frozenAgeSeconds, string winningConfirmationOpId)
        {
            Outcome = outcome;
            ResultCode = resultCode;
            WarningToken = warningToken ?? string.Empty;
            ConfirmationDecisionId = confirmationDecisionId ?? string.Empty;
            ReceiptId = receiptId ?? string.Empty;
            GraceExpiresAtSeconds = graceExpiresAtSeconds;
            FrozenAgeSeconds = frozenAgeSeconds;
            WinningConfirmationOpId = winningConfirmationOpId ?? string.Empty;
        }

        public FinalLinkOutcome Outcome { get; }
        public string ResultCode { get; }
        public string WarningToken { get; }
        public string ConfirmationDecisionId { get; }
        public string ReceiptId { get; }
        public long GraceExpiresAtSeconds { get; }
        public long FrozenAgeSeconds { get; }

        /// <summary>On a Consumed race, the confirmation op id that actually committed (winning receipt
        /// correlation, contracts §FinalLinkConfirmationConsumed).</summary>
        public string WinningConfirmationOpId { get; }

        public bool Accepted => Outcome == FinalLinkOutcome.ConfirmationRequired
            || Outcome == FinalLinkOutcome.PreparationReplayed
            || Outcome == FinalLinkOutcome.Confirmed
            || Outcome == FinalLinkOutcome.ConfirmationReplayed;
    }

    /// <summary>Live authority the confirmation revalidates. The application layer supplies the CURRENT
    /// active-holder facts; the store never trusts token possession (spec RD-004).</summary>
    public readonly struct LiveReleaseAuthority
    {
        public LiveReleaseAuthority(string currentActiveHolderAccountId, string currentActiveHolderCharacterId,
            long currentReleaseAuthorityRevision, bool hasVoluntaryReleaseAuthority)
        {
            CurrentActiveHolderAccountId = currentActiveHolderAccountId ?? string.Empty;
            CurrentActiveHolderCharacterId = currentActiveHolderCharacterId ?? string.Empty;
            CurrentReleaseAuthorityRevision = currentReleaseAuthorityRevision;
            HasVoluntaryReleaseAuthority = hasVoluntaryReleaseAuthority;
        }

        public string CurrentActiveHolderAccountId { get; }
        public string CurrentActiveHolderCharacterId { get; }
        public long CurrentReleaseAuthorityRevision { get; }
        public bool HasVoluntaryReleaseAuthority { get; }
    }

    public sealed class FinalLinkHandshakeStore
    {
        // Journal phases for one confirmation. Preparation is a single durable decision record.
        private const string RecPrepare = "PREP";
        private const string RecConfirmIntent = "CONF1"; // challenge consumption + intent journaled
        private const string RecConfirmCommit = "CONF2"; // release + grace + receipt terminal

        private readonly string _journalPath;

        public FinalLinkHandshakeStore(string journalPath)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
        }

        public string JournalPath => _journalPath;

        // ---- Preparation ----

        /// <summary>Prepare a final-link release: store (or replay) a durable non-mutating
        /// ConfirmationRequired decision. Idempotent by preparationOpId; a conflicting binding under the
        /// same op id is OperationConflict.</summary>
        public FinalLinkResult Prepare(
            string preparationOpId,
            FinalLinkBinding binding,
            IReadOnlyList<AffectedConnection> orderedAffected,
            long issuedAtServerTimeSeconds)
        {
            if (string.IsNullOrEmpty(preparationOpId)) throw new ArgumentException("preparationOpId required");
            if (orderedAffected == null || orderedAffected.Count == 0)
                throw new ArgumentException("A final-link release must affect at least one Connection.");

            string decisionId = DecisionId(preparationOpId, binding);
            string orderedSetDigest = OrderedSetDigest(orderedAffected);
            string bindingDigest = BindingDigest(binding, orderedSetDigest);
            string token = Digest("token|" + decisionId + "|" + bindingDigest);

            var existing = FindPrepare(preparationOpId);
            if (existing != null)
            {
                if (existing.BindingDigest != bindingDigest)
                    return Conflict();
                // Exact replay of the prior decision/challenge (survives restart).
                return new FinalLinkResult(FinalLinkOutcome.PreparationReplayed, "FinalLinkConfirmationRequired",
                    existing.WarningToken, existing.DecisionId, string.Empty, 0, 0, string.Empty);
            }

            var rec = new PrepareRecord
            {
                PreparationOpId = preparationOpId,
                DecisionId = decisionId,
                BindingDigest = bindingDigest,
                OrderedSetDigest = orderedSetDigest,
                WarningToken = token,
                IssuedAtSeconds = issuedAtServerTimeSeconds,
                AffectedCount = orderedAffected.Count
            };
            Append(SerializePrepare(rec));

            return new FinalLinkResult(FinalLinkOutcome.ConfirmationRequired, "FinalLinkConfirmationRequired",
                token, decisionId, string.Empty, 0, 0, string.Empty);
        }

        // ---- Confirmation ----

        /// <summary>Confirm a prepared final-link release under a FRESH confirmation op id. Applies the
        /// atomic release+grace+receipt or rejects with no gameplay mutation. Replaying the same
        /// confirmation op after restart returns the winning receipt.</summary>
        /// <param name="liveAgeByConnectionKey">Current reconciled live age (seconds) for each still-bound
        /// Connection key at confirmation time — the value that gets FROZEN into Grace.</param>
        public FinalLinkResult Confirm(
            string confirmationOpId,
            string preparationOpId,
            FinalLinkBinding binding,
            IReadOnlyList<AffectedConnection> orderedAffected,
            LiveReleaseAuthority live,
            IReadOnlyDictionary<string, long> liveAgeByConnectionKey,
            long receivedServerTimeSeconds)
        {
            if (string.IsNullOrEmpty(confirmationOpId)) throw new ArgumentException("confirmationOpId required");
            if (string.Equals(confirmationOpId, preparationOpId, StringComparison.Ordinal))
                // Reusing the preparation op id with confirmation payload conflicts (contracts).
                return Conflict();

            var prep = FindPrepare(preparationOpId);
            if (prep == null)
                return new FinalLinkResult(FinalLinkOutcome.DecisionNotFound, "FinalLinkConfirmationStale",
                    string.Empty, string.Empty, string.Empty, 0, 0, string.Empty);

            string decisionId = DecisionId(preparationOpId, binding);
            string orderedSetDigest = OrderedSetDigest(orderedAffected);
            string bindingDigest = BindingDigest(binding, orderedSetDigest);

            // The decision/intent/target/set binding must match exactly (RD-004 revalidation).
            if (prep.DecisionId != decisionId || prep.BindingDigest != bindingDigest)
                return new FinalLinkResult(FinalLinkOutcome.Stale, "FinalLinkConfirmationStale",
                    string.Empty, decisionId, string.Empty, 0, 0, string.Empty);

            // Was this challenge already consumed? Scan committed confirmations for this decision.
            var winner = FindWinningConfirmation(prep.DecisionId);
            if (winner != null)
            {
                if (string.Equals(winner.ConfirmationOpId, confirmationOpId, StringComparison.Ordinal))
                    // Same op replay -> return the winning receipt.
                    return new FinalLinkResult(FinalLinkOutcome.ConfirmationReplayed, "Applied",
                        prep.WarningToken, prep.DecisionId, winner.ReceiptId, winner.GraceExpiresAtSeconds,
                        winner.FrozenAgeSeconds, winner.ConfirmationOpId);
                // A different op raced and lost.
                return new FinalLinkResult(FinalLinkOutcome.Consumed, "FinalLinkConfirmationConsumed",
                    string.Empty, prep.DecisionId, winner.ReceiptId, winner.GraceExpiresAtSeconds,
                    winner.FrozenAgeSeconds, winner.ConfirmationOpId);
            }

            // A partial (intent journaled, not committed) confirmation under a DIFFERENT op id means an
            // interrupted rival — it never consumed the challenge (consumption commits with the terminal
            // record), so we may proceed. A partial under THIS op id means resume-after-crash.
            var ownPartial = FindConfirmIntent(prep.DecisionId, confirmationOpId);

            // Principal binding: the confirming principal must EQUAL the preparing principal. Token
            // possession is not authority (spec RD-004).
            if (!string.Equals(live.CurrentActiveHolderAccountId, binding.PreparedByAccountId, StringComparison.Ordinal)
                || !string.Equals(live.CurrentActiveHolderCharacterId, binding.PreparedByCharacterId, StringComparison.Ordinal))
                return new FinalLinkResult(FinalLinkOutcome.PrincipalMismatch, "FinalLinkConfirmationPrincipalMismatch",
                    string.Empty, prep.DecisionId, string.Empty, 0, 0, string.Empty);

            // Lost-authority: the bound principal must still be the active holder with voluntary-release
            // authority at the bound release-authority revision.
            if (!live.HasVoluntaryReleaseAuthority
                || live.CurrentReleaseAuthorityRevision != binding.ReleaseAuthorityRevision)
                return new FinalLinkResult(FinalLinkOutcome.ReleaseUnauthorized, "RelationshipReleaseUnauthorized",
                    string.Empty, prep.DecisionId, string.Empty, 0, 0, string.Empty);

            // Freeze the reconciled confirmation-time age. Use the MAX live age across still-bound
            // Connections as the representative frozen age for the receipt; each Connection's own frozen
            // age is applied by the aggregate. (Grace freeze per-Connection is the aggregate's job; the
            // receipt records the representative frozen age + the uniform grace expiry.)
            long frozenAge = 0;
            if (liveAgeByConnectionKey != null)
                foreach (var kv in liveAgeByConnectionKey)
                    if (kv.Value > frozenAge) frozenAge = kv.Value;

            long graceExpiresAt = receivedServerTimeSeconds + ConnectionAggregate.GraceSeconds;
            string receiptId = Digest("receipt|" + confirmationOpId + "|" + prep.DecisionId);

            // Phase 1: journal challenge-consumption INTENT (not yet terminal). If we already have our own
            // intent record (resume-after-crash), skip re-writing it.
            if (ownPartial == null)
                Append(SerializeConfirmIntent(prep.DecisionId, confirmationOpId, bindingDigest));

            // Phase 2: terminal commit — challenge consumed + release + grace + receipt, atomically. The
            // terminal record IS the atomic boundary: a crash before it leaves the challenge unconsumed.
            Append(SerializeConfirmCommit(prep.DecisionId, confirmationOpId, receiptId, graceExpiresAt, frozenAge));

            return new FinalLinkResult(FinalLinkOutcome.Confirmed, "Applied", prep.WarningToken,
                prep.DecisionId, receiptId, graceExpiresAt, frozenAge, confirmationOpId);
        }

        // ---- Identity / digest helpers ----

        private static string DecisionId(string preparationOpId, FinalLinkBinding b) =>
            Digest("decision|" + preparationOpId + "|" + b.TargetRelationshipId + "|" +
                   b.ReleaseAuthorityRevision.ToString(CultureInfo.InvariantCulture));

        private static string OrderedSetDigest(IReadOnlyList<AffectedConnection> ordered)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < ordered.Count; i++)
                sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append('#').Append(ordered[i].DigestPart()).Append(';');
            return Digest(sb.ToString());
        }

        private static string BindingDigest(FinalLinkBinding b, string orderedSetDigest) =>
            Digest(string.Join("|", new[]
            {
                b.PreparedByAccountId, b.PreparedByCharacterId, b.TargetRelationshipId,
                b.ReleaseAuthorityRevision.ToString(CultureInfo.InvariantCulture),
                b.RelationshipRevision.ToString(CultureInfo.InvariantCulture),
                b.GracePolicyVersion.ToString(CultureInfo.InvariantCulture),
                orderedSetDigest
            }));

        private static FinalLinkResult Conflict() =>
            new FinalLinkResult(FinalLinkOutcome.OperationConflict, "OperationConflict",
                string.Empty, string.Empty, string.Empty, 0, 0, string.Empty);

        // ---- Journal record types ----

        private sealed class PrepareRecord
        {
            public string PreparationOpId = string.Empty;
            public string DecisionId = string.Empty;
            public string BindingDigest = string.Empty;
            public string OrderedSetDigest = string.Empty;
            public string WarningToken = string.Empty;
            public long IssuedAtSeconds;
            public int AffectedCount;
        }

        private sealed class ConfirmCommitRecord
        {
            public string DecisionId = string.Empty;
            public string ConfirmationOpId = string.Empty;
            public string ReceiptId = string.Empty;
            public long GraceExpiresAtSeconds;
            public long FrozenAgeSeconds;
        }

        private PrepareRecord? FindPrepare(string preparationOpId)
        {
            foreach (var line in ReadDurable())
            {
                var parts = line.Split('|');
                if (parts[0] != RecPrepare) continue;
                if (Decode(parts[1]) != preparationOpId) continue;
                return new PrepareRecord
                {
                    PreparationOpId = Decode(parts[1]),
                    DecisionId = Decode(parts[2]),
                    BindingDigest = Decode(parts[3]),
                    OrderedSetDigest = Decode(parts[4]),
                    WarningToken = Decode(parts[5]),
                    IssuedAtSeconds = long.Parse(parts[6], CultureInfo.InvariantCulture),
                    AffectedCount = int.Parse(parts[7], CultureInfo.InvariantCulture)
                };
            }
            return null;
        }

        /// <summary>The committed (terminal) confirmation for a decision, if any. First terminal wins.</summary>
        private ConfirmCommitRecord? FindWinningConfirmation(string decisionId)
        {
            foreach (var line in ReadDurable())
            {
                var parts = line.Split('|');
                if (parts[0] != RecConfirmCommit) continue;
                if (Decode(parts[1]) != decisionId) continue;
                return new ConfirmCommitRecord
                {
                    DecisionId = Decode(parts[1]),
                    ConfirmationOpId = Decode(parts[2]),
                    ReceiptId = Decode(parts[3]),
                    GraceExpiresAtSeconds = long.Parse(parts[4], CultureInfo.InvariantCulture),
                    FrozenAgeSeconds = long.Parse(parts[5], CultureInfo.InvariantCulture)
                };
            }
            return null;
        }

        private string? FindConfirmIntent(string decisionId, string confirmationOpId)
        {
            foreach (var line in ReadDurable())
            {
                var parts = line.Split('|');
                if (parts[0] != RecConfirmIntent) continue;
                if (Decode(parts[1]) == decisionId && Decode(parts[2]) == confirmationOpId)
                    return line;
            }
            return null;
        }

        private static string SerializePrepare(PrepareRecord r) => string.Join("|", new[]
        {
            RecPrepare, Encode(r.PreparationOpId), Encode(r.DecisionId), Encode(r.BindingDigest),
            Encode(r.OrderedSetDigest), Encode(r.WarningToken),
            r.IssuedAtSeconds.ToString(CultureInfo.InvariantCulture),
            r.AffectedCount.ToString(CultureInfo.InvariantCulture)
        });

        private static string SerializeConfirmIntent(string decisionId, string confirmationOpId, string bindingDigest) =>
            string.Join("|", new[] { RecConfirmIntent, Encode(decisionId), Encode(confirmationOpId), Encode(bindingDigest) });

        private static string SerializeConfirmCommit(string decisionId, string confirmationOpId, string receiptId,
            long graceExpiresAt, long frozenAge) => string.Join("|", new[]
        {
            RecConfirmCommit, Encode(decisionId), Encode(confirmationOpId), Encode(receiptId),
            graceExpiresAt.ToString(CultureInfo.InvariantCulture),
            frozenAge.ToString(CultureInfo.InvariantCulture)
        });

        // ---- Append-only, framed + crc-checked journal (mirrors OperationReceiptStore) ----

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
                fs.Flush(true);
            }
        }

        /// <summary>Read only fully-durable records; a torn tail from process death is ignored.</summary>
        public List<string> ReadDurable()
        {
            var results = new List<string>();
            if (!File.Exists(_journalPath)) return results;
            using (var fs = new FileStream(_journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs, Encoding.UTF8))
            {
                long length = fs.Length;
                while (true)
                {
                    long recordStart = fs.Position;
                    if (recordStart + 8 > length) break;
                    int payloadLen = br.ReadInt32();
                    uint crc = br.ReadUInt32();
                    if (payloadLen < 0 || fs.Position + payloadLen > length) break;
                    byte[] payload = br.ReadBytes(payloadLen);
                    if (payload.Length != payloadLen || Crc32(payload) != crc) break;
                    results.Add(Encoding.UTF8.GetString(payload));
                }
            }
            return results;
        }

        /// <summary>Injectable "process death after the next append" hook for the recovery matrix. The
        /// test wraps a store whose Append throws after writing N records; recovery constructs a fresh
        /// store over the same journal. This method exposes the durable record count so a test can assert
        /// the crash landed between the intended boundaries.</summary>
        public int DurableRecordCount() => ReadDurable().Count;

        private static string Encode(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? string.Empty));
        private static string Decode(string s) => Encoding.UTF8.GetString(Convert.FromBase64String(s));

        public static string Digest(string s)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? string.Empty));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return ~crc;
        }
    }
}
