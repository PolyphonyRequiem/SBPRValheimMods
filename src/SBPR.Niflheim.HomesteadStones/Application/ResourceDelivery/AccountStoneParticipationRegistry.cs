using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Application.ResourceDelivery
{
    // RD-T005 (Tracer 2) — the durable coordinator that owns per-(account, Stone) Participation and
    // multiplier-aware AP awards (spec RD-006 / RD-009 / RD-019, data-model Aggregates 2 & 5, contracts
    // §SubmitUpkeepDonation / RecordDailyPractice / RecordApActivity). Named acceptance: AT-RD-006,
    // AT-RD-009, AT-RD-019.
    //
    // WHAT THIS OWNS
    //   The recoverable Participation projection and the ordered combined placement operation. It couples
    //   the pure AccountStoneParticipationAggregate (weekly/daily windows and the durable daily cycle),
    //   the pure ContributionRule (complete-contributor iff-rule + strongest maturity once), and the pure
    //   ApMultiplierPolicy (exact floor(base × participation × maturity)) into commands that survive
    //   restart and replay.
    //
    // SCOPE (Tracer 2): participation completion/expiry and multiplier-aware AP only. It does NOT move
    // items (Gate B / Tracer 3), authenticate principals, or run the AP source's own authorization —
    // those ran upstream and hand this coordinator the resolved facts (relationship flag + the account's
    // candidate Connections at the Stone). It does NOT widen AP source authorization (spec RD-009).
    //
    // EVENT-SOURCED RECOVERY (mirrors StoneConnectionSourceRegistry / OperationReceiptStore discipline)
    //   Every accepted mutation is appended to a framed, CRC-checked, fsync'd journal keyed by
    //   operationId. The in-memory Participation projection is a pure function of replaying the committed
    //   events in journal order at their recorded server times, so a restart reconstructs the EXACT
    //   weekly/daily windows and cycle progress. A committed operationId replays its RECORDED terminal
    //   result verbatim (never a recomputation against later participation/maturity); a reused id with a
    //   different binding is an OperationConflict. This is the exact-replay discipline the parent RD-T004
    //   correction established.
    //
    // COMBINED FIFTH-PLACEMENT ORDER (spec RD-019 / contracts §RecordDailyPractice)
    //   For a qualifying Foundational placement that is ALSO an AP source, one combined terminal operation
    //   after elapsed reconciliation: (1) snapshot/compute the AP subresult from the participation tier
    //   that existed BEFORE this placement's practice mutation, then (2) apply the placement to daily
    //   progress. Therefore the fifth placement uses the prior 0×/1× tier and 2× applies only to
    //   subsequent events/elapsed time. Replay/restart returns that same ordered combined result.
    //
    // net48 audit: FileStream(.Flush(true)), BinaryReader/Writer, SHA256, Encoding.UTF8, CRC32 — all
    // present in .NET Framework 4.8. Engine-free; link-compiles into the net8 test project.

    public enum ParticipationEventKind
    {
        WeeklyUpkeepCompleted = 1,
        DailyPracticeRecorded = 2,
        CombinedPlacement = 3
    }

    public enum ParticipationOutcome
    {
        Applied,
        Replayed,
        OperationConflict
    }

    /// <summary>Result of a weekly upkeep completion.</summary>
    public readonly struct WeeklyUpkeepResult
    {
        public WeeklyUpkeepResult(ParticipationOutcome outcome, long weeklyExpiresAtSeconds)
        {
            Outcome = outcome;
            WeeklyExpiresAtSeconds = weeklyExpiresAtSeconds;
        }

        public ParticipationOutcome Outcome { get; }
        public long WeeklyExpiresAtSeconds { get; }
    }

    /// <summary>Result of a standalone daily-practice record.</summary>
    public readonly struct DailyPracticeRecordResult
    {
        public DailyPracticeRecordResult(ParticipationOutcome outcome, DailyPracticeOutcome practiceOutcome,
            int progress, ParticipationTier tierAfter)
        {
            Outcome = outcome;
            PracticeOutcome = practiceOutcome;
            Progress = progress;
            TierAfter = tierAfter;
        }

        public ParticipationOutcome Outcome { get; }
        public DailyPracticeOutcome PracticeOutcome { get; }
        public int Progress { get; }
        public ParticipationTier TierAfter { get; }
    }

    /// <summary>Result of a combined fifth-placement operation: the AP subresult computed against the
    /// PRE-practice tier, plus the practice outcome and the tier that becomes current afterward.</summary>
    public readonly struct CombinedPlacementResult
    {
        public CombinedPlacementResult(ParticipationOutcome outcome, ApAwardResult apAward,
            ParticipationTier tierBeforePractice, DailyPracticeOutcome practiceOutcome,
            ParticipationTier tierAfterPractice, int progressAfter)
        {
            Outcome = outcome;
            ApAward = apAward;
            TierBeforePractice = tierBeforePractice;
            PracticeOutcome = practiceOutcome;
            TierAfterPractice = tierAfterPractice;
            ProgressAfter = progressAfter;
        }

        public ParticipationOutcome Outcome { get; }

        /// <summary>The AP subresult, computed with the tier that existed BEFORE this placement's practice
        /// mutation (spec RD-019: the fifth placement never boosts its own AP).</summary>
        public ApAwardResult ApAward { get; }

        public ParticipationTier TierBeforePractice { get; }
        public DailyPracticeOutcome PracticeOutcome { get; }

        /// <summary>The tier current immediately AFTER practice (2× when this placement closed the cycle
        /// with weekly upkeep current).</summary>
        public ParticipationTier TierAfterPractice { get; }
        public int ProgressAfter { get; }
    }

    public sealed class AccountStoneParticipationRegistry
    {
        private const string RecWeekly = "PWKY";
        private const string RecDaily = "PDLY";
        private const string RecCombined = "PCMB";

        private readonly string _journalPath;

        // Projection: per-Participation aggregate keyed by canonical key.
        private readonly Dictionary<string, AccountStoneParticipationAggregate> _byKey =
            new Dictionary<string, AccountStoneParticipationAggregate>(StringComparer.Ordinal);
        // Committed operation binding digests (idempotency).
        private readonly Dictionary<string, string> _committedOps =
            new Dictionary<string, string>(StringComparer.Ordinal);
        // Recorded terminal result per committed op id, replayed verbatim (never recomputed).
        private readonly Dictionary<string, string> _resultByOp =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public AccountStoneParticipationRegistry(string journalPath)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            RehydrateFromJournal();
        }

        public string JournalPath => _journalPath;

        // ---- Read projection ----

        /// <summary>The current aggregate for a Participation identity, or a fresh empty one when none
        /// exists yet. Never null.</summary>
        public AccountStoneParticipationAggregate GetParticipation(ParticipationId id)
        {
            if (_byKey.TryGetValue(id.CanonicalKey, out var agg)) return agg;
            return AccountStoneParticipationAggregate.CreateEmpty(id);
        }

        /// <summary>The participation tier for an account at a Stone at <paramref name="serverTimeSeconds"/>,
        /// reconciling an expired daily window first so a lapsed 2× resolves to 1× (or 0×).</summary>
        public ParticipationTier TierAt(ParticipationId id, long serverTimeSeconds) =>
            GetParticipation(id).ReconcileTo(serverTimeSeconds).TierAt(serverTimeSeconds);

        // ---- Commands ----

        /// <summary>Record an accepted weekly upkeep completion (contracts §SubmitUpkeepDonation commit
        /// step "record weekly upkeep completion and rolling seven-day expiry"). Reconciles the elapsed
        /// span under the old state first, then refreshes the rolling seven-day expiry. Repeated
        /// completion refreshes the latest timestamp without stacking a multiplier. Idempotent by op id.</summary>
        public WeeklyUpkeepResult RecordWeeklyUpkeep(string operationId, ParticipationId id, long serverTimeSeconds)
        {
            string binding = Digest(string.Join("|", new[]
            {
                ((int)ParticipationEventKind.WeeklyUpkeepCompleted).ToString(CultureInfo.InvariantCulture),
                id.CanonicalKey, serverTimeSeconds.ToString(CultureInfo.InvariantCulture)
            }));
            if (TryReplay(operationId, binding, out var conflict, out var recorded))
            {
                if (conflict) return new WeeklyUpkeepResult(ParticipationOutcome.OperationConflict, 0);
                var rr = new SnapshotReader(recorded);
                return new WeeklyUpkeepResult(ParticipationOutcome.Replayed, rr.GetLong("wkExp"));
            }

            var current = GetParticipation(id).ReconcileTo(serverTimeSeconds);
            var next = current.RecordWeeklyCompletion(serverTimeSeconds);
            _byKey[id.CanonicalKey] = next;

            string result = new SnapshotWriter().PutLong("wkExp", next.WeeklyExpiresAtSeconds).Build();
            AppendEvent(operationId, binding, ParticipationEventKind.WeeklyUpkeepCompleted, id,
                serverTimeSeconds, string.Empty, 0, result);
            return new WeeklyUpkeepResult(ParticipationOutcome.Applied, next.WeeklyExpiresAtSeconds);
        }

        /// <summary>Record one distinct physical eligible Foundational placement toward the daily cycle
        /// (contracts §RecordDailyPractice), for a placement that is NOT also an AP source. Reconciles the
        /// elapsed span under the old completion/expiry first. Idempotent by op id.</summary>
        public DailyPracticeRecordResult RecordDailyPractice(string operationId, ParticipationId id,
            string physicalInstanceId, long serverTimeSeconds)
        {
            string binding = Digest(string.Join("|", new[]
            {
                ((int)ParticipationEventKind.DailyPracticeRecorded).ToString(CultureInfo.InvariantCulture),
                id.CanonicalKey, physicalInstanceId ?? string.Empty,
                serverTimeSeconds.ToString(CultureInfo.InvariantCulture)
            }));
            if (TryReplay(operationId, binding, out var conflict, out var recorded))
            {
                if (conflict) return new DailyPracticeRecordResult(ParticipationOutcome.OperationConflict,
                    DailyPracticeOutcome.DuplicateInstance, 0, ParticipationTier.None);
                var rr = new SnapshotReader(recorded);
                return new DailyPracticeRecordResult(ParticipationOutcome.Replayed,
                    (DailyPracticeOutcome)rr.GetInt("po"), rr.GetInt("prog"),
                    (ParticipationTier)rr.GetInt("tier"));
            }

            var current = GetParticipation(id).ReconcileTo(serverTimeSeconds);
            var applied = current.RecordDailyPractice(physicalInstanceId ?? string.Empty, serverTimeSeconds);
            _byKey[id.CanonicalKey] = applied.Aggregate;

            var tierAfter = applied.Aggregate.TierAt(serverTimeSeconds);
            int progress = applied.Aggregate.DailyProgress;
            string result = new SnapshotWriter()
                .PutInt("po", (int)applied.Outcome)
                .PutInt("prog", progress)
                .PutInt("tier", (int)tierAfter)
                .Build();
            AppendEvent(operationId, binding, ParticipationEventKind.DailyPracticeRecorded, id,
                serverTimeSeconds, physicalInstanceId ?? string.Empty, 0, result);
            return new DailyPracticeRecordResult(ParticipationOutcome.Applied, applied.Outcome, progress, tierAfter);
        }

        /// <summary>The combined terminal operation for a Foundational placement that is ALSO an AP source
        /// (spec RD-019 / contracts §RecordDailyPractice fixed order). After reconciling the elapsed span:
        /// (1) compute the AP subresult from the participation tier BEFORE this placement's practice
        /// mutation and the account's strongest current maturity, then (2) apply the placement to daily
        /// progress — atomically under one receipt. The fifth placement therefore uses the prior 0×/1×
        /// tier; only later events see 2×. Idempotent by op id; replay returns the recorded ordered
        /// result verbatim.</summary>
        /// <param name="authoredBaseAp">The AP source's authored base award; its own actor/relationship
        /// authorization already ran upstream (this call never widens it).</param>
        /// <param name="hasActiveStoneRelationship">Whether the account has an active Bond/Attunement at
        /// the Stone (resolved upstream).</param>
        /// <param name="candidateConnections">The account's candidate Connections at this Stone for
        /// strongest-maturity-once selection (resolved upstream).</param>
        public CombinedPlacementResult RecordCombinedPlacement(
            string operationId, ParticipationId id, string physicalInstanceId, long serverTimeSeconds,
            long authoredBaseAp, bool hasActiveStoneRelationship,
            IReadOnlyList<ConnectionAggregate> candidateConnections)
        {
            string binding = Digest(string.Join("|", new[]
            {
                ((int)ParticipationEventKind.CombinedPlacement).ToString(CultureInfo.InvariantCulture),
                id.CanonicalKey, physicalInstanceId ?? string.Empty,
                serverTimeSeconds.ToString(CultureInfo.InvariantCulture),
                authoredBaseAp.ToString(CultureInfo.InvariantCulture)
            }));
            if (TryReplay(operationId, binding, out var conflict, out var recorded))
            {
                if (conflict) return ConflictCombined();
                return DecodeCombined(recorded);
            }

            // Elapsed reconciliation under the prior state (closes an expired daily window).
            var current = GetParticipation(id).ReconcileTo(serverTimeSeconds);

            // STEP 1 — AP subresult from the PRE-practice tier (spec RD-019: fifth placement uses prior tier).
            var tierBefore = current.TierAt(serverTimeSeconds);
            var eligibility = ContributionRule.Evaluate(
                hasActiveStoneRelationship, tierBefore, candidateConnections, id.Stone, serverTimeSeconds);
            var apAward = ApMultiplierPolicy.Award(authoredBaseAp, eligibility);

            // STEP 2 — apply the placement to daily-cycle progress (2× becomes current AFTER the AP subresult).
            var applied = current.RecordDailyPractice(physicalInstanceId ?? string.Empty, serverTimeSeconds);
            _byKey[id.CanonicalKey] = applied.Aggregate;

            var tierAfter = applied.Aggregate.TierAt(serverTimeSeconds);
            int progressAfter = applied.Aggregate.DailyProgress;

            var result = new CombinedPlacementResult(ParticipationOutcome.Applied, apAward,
                tierBefore, applied.Outcome, tierAfter, progressAfter);

            string encoded = EncodeCombined(result);
            AppendEvent(operationId, binding, ParticipationEventKind.CombinedPlacement, id,
                serverTimeSeconds, physicalInstanceId ?? string.Empty, authoredBaseAp, encoded);
            return result;
        }

        private static CombinedPlacementResult ConflictCombined() =>
            new CombinedPlacementResult(ParticipationOutcome.OperationConflict,
                new ApAwardResult(0, ParticipationTier.None, ConnectionMaturity.Band0, 0, false),
                ParticipationTier.None, DailyPracticeOutcome.DuplicateInstance, ParticipationTier.None, 0);

        private static string EncodeCombined(CombinedPlacementResult r) => new SnapshotWriter()
            .PutLong("base", r.ApAward.AuthoredBaseAp)
            .PutInt("apTier", (int)r.ApAward.Tier)
            .PutInt("mNum", r.ApAward.Maturity.Numerator)
            .PutInt("mDen", r.ApAward.Maturity.Denominator)
            .PutLong("award", r.ApAward.FinalAward)
            .PutBool("awarded", r.ApAward.Awarded)
            .PutInt("tierBefore", (int)r.TierBeforePractice)
            .PutInt("po", (int)r.PracticeOutcome)
            .PutInt("tierAfter", (int)r.TierAfterPractice)
            .PutInt("prog", r.ProgressAfter)
            .Build();

        private static CombinedPlacementResult DecodeCombined(string s)
        {
            var r = new SnapshotReader(s);
            var award = new ApAwardResult(
                r.GetLong("base"),
                (ParticipationTier)r.GetInt("apTier"),
                new MaturityMultiplier(r.GetInt("mNum"), r.GetInt("mDen")),
                r.GetLong("award"),
                r.GetBool("awarded"));
            return new CombinedPlacementResult(ParticipationOutcome.Replayed, award,
                (ParticipationTier)r.GetInt("tierBefore"),
                (DailyPracticeOutcome)r.GetInt("po"),
                (ParticipationTier)r.GetInt("tierAfter"),
                r.GetInt("prog"));
        }

        // ---- Idempotency ----

        // Returns true when the op id is already committed. Sets conflict when the binding differs, else
        // yields the recorded terminal result string for verbatim replay.
        private bool TryReplay(string operationId, string binding, out bool conflict, out string recorded)
        {
            conflict = false;
            recorded = string.Empty;
            if (string.IsNullOrEmpty(operationId)) throw new ArgumentException("operationId required");
            if (_committedOps.TryGetValue(operationId, out var committedBinding))
            {
                if (!string.Equals(committedBinding, binding, StringComparison.Ordinal))
                {
                    conflict = true;
                    return true;
                }
                recorded = _resultByOp.TryGetValue(operationId, out var rec) ? rec : string.Empty;
                return true;
            }
            return false;
        }

        // ---- Journal ----

        private void AppendEvent(string operationId, string binding, ParticipationEventKind kind,
            ParticipationId id, long serverTimeSeconds, string instanceId, long baseAp, string result)
        {
            Append(SerializeEvent(operationId, binding, kind, id, serverTimeSeconds, instanceId, baseAp, result));
            _committedOps[operationId] = binding;
            _resultByOp[operationId] = result;
        }

        private void RehydrateFromJournal()
        {
            foreach (var line in ReadDurable())
            {
                var ev = ParseEvent(line);
                if (ev == null) continue;
                var e = ev.Value;
                if (_committedOps.ContainsKey(e.OperationId)) continue;
                _committedOps[e.OperationId] = e.Binding;
                _resultByOp[e.OperationId] = e.Result;

                var id = new ParticipationId(new WorldId(e.World), new ProductScope(e.Product),
                    new AccountId(e.Account), new StoneId(e.Stone));
                var current = GetParticipation(id).ReconcileTo(e.ServerTimeSeconds);

                switch (e.Kind)
                {
                    case ParticipationEventKind.WeeklyUpkeepCompleted:
                        _byKey[id.CanonicalKey] = current.RecordWeeklyCompletion(e.ServerTimeSeconds);
                        break;
                    case ParticipationEventKind.DailyPracticeRecorded:
                    case ParticipationEventKind.CombinedPlacement:
                        // Both replay the same practice mutation at the recorded server time; the AP
                        // subresult for a combined op is already recorded in the result string (replayed
                        // verbatim), so rehydration only needs to reconstruct the practice progress.
                        _byKey[id.CanonicalKey] =
                            current.RecordDailyPractice(e.InstanceId, e.ServerTimeSeconds).Aggregate;
                        break;
                }
            }
        }

        private struct ParsedEvent
        {
            public string OperationId;
            public string Binding;
            public ParticipationEventKind Kind;
            public string World;
            public string Product;
            public string Account;
            public string Stone;
            public long ServerTimeSeconds;
            public string InstanceId;
            public long BaseAp;
            public string Result;
        }

        private static string SerializeEvent(string operationId, string binding, ParticipationEventKind kind,
            ParticipationId id, long serverTimeSeconds, string instanceId, long baseAp, string result)
        {
            return string.Join("|", new[]
            {
                RecKind(kind),
                Encode(operationId),
                Encode(binding),
                ((int)kind).ToString(CultureInfo.InvariantCulture),
                Encode(id.World.Value),
                Encode(id.Product.Value),
                Encode(id.Account.Value),
                Encode(id.Stone.Value),
                serverTimeSeconds.ToString(CultureInfo.InvariantCulture),
                Encode(instanceId ?? string.Empty),
                baseAp.ToString(CultureInfo.InvariantCulture),
                Encode(result ?? string.Empty)
            });
        }

        private static string RecKind(ParticipationEventKind kind) =>
            kind == ParticipationEventKind.WeeklyUpkeepCompleted ? RecWeekly
            : kind == ParticipationEventKind.DailyPracticeRecorded ? RecDaily
            : RecCombined;

        private static ParsedEvent? ParseEvent(string line)
        {
            var parts = line.Split('|');
            if (parts.Length != 12) return null;
            if (parts[0] != RecWeekly && parts[0] != RecDaily && parts[0] != RecCombined) return null;
            return new ParsedEvent
            {
                OperationId = Decode(parts[1]),
                Binding = Decode(parts[2]),
                Kind = (ParticipationEventKind)int.Parse(parts[3], CultureInfo.InvariantCulture),
                World = Decode(parts[4]),
                Product = Decode(parts[5]),
                Account = Decode(parts[6]),
                Stone = Decode(parts[7]),
                ServerTimeSeconds = long.Parse(parts[8], CultureInfo.InvariantCulture),
                InstanceId = Decode(parts[9]),
                BaseAp = long.Parse(parts[10], CultureInfo.InvariantCulture),
                Result = Decode(parts[11])
            };
        }

        // ---- Append-only, framed + crc-checked journal (mirrors StoneConnectionSourceRegistry) ----

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

        private List<string> ReadDurable()
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

        private static string Encode(string s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? string.Empty));

        private static string Decode(string s) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(s));

        private static string Digest(string s)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(h.Length * 2);
                foreach (var b in h) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static readonly uint[] _crcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (var b in data)
                crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
