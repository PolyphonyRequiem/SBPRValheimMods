using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;

namespace SBPR.Niflheim.HomesteadStones.Application.Commands
{
    // T014 — recoverable SetSettlementLocalPolicy command handler (contracts.md §"SetSettlementLocalPolicy";
    // spec FR-016; data-model.md §"Local state"). This is the STONE-side mutation authority for the ONE
    // Settlement-wide Local beneficiary policy: one accepted change swaps the Stone's single policy
    // (Everyone | Attuned | Private + allowlist) under ONE durable, replayable receipt. There is NO
    // per-effect override — the whole Stone shares one policy — and the Local Effect ACTIVE/DORMANT
    // projection is derived on demand (LocalEffectActivationView), never stored, so this handler never
    // writes a second active-effects ledger (spec FR-019, AT-NO-ACTIVE-LEDGER).
    //
    // Recovery model mirrors PurchaseCommands/DevelopmentCommands: an append-only, per-boundary-fsync'd
    // journal IS the transaction. The Stone store is an idempotent projection of the journal, so a crash
    // between intent and terminal cannot leave a partial policy change — recovery re-derives the Stone
    // from the one committed record and the same result is rebuilt on restart (spec FR-023,
    // AT-RELATIONSHIP-DORMANCY restart coverage). Re-submitting the same operationId returns the
    // recorded terminal result (Replayed); a conflicting binding/payload under a committed op rejects
    // OperationConflict with zero mutation.
    //
    // Authority (contracts.md §"SetSettlementLocalPolicy" Validates): the acting character must be the
    // server-validated Homestead owner. Ownership is NEVER client-authored — the client cannot claim it;
    // the server-owned IHomesteadOwnerAuthority proves it. A non-owner (even a bonded Governor or an
    // attuned player) is rejected Unauthorized. Neither relationship nor policy silently grants a build
    // ACL: this handler mutates ONLY the beneficiary policy and grants no doors/portals/build permission.
    //
    // Optimistic concurrency: the command carries both an expected Stone revision AND an expected policy
    // revision. A stale expectation on either rejects StalePolicyRevision/StaleStoneRevision with zero
    // mutation (spec §Edge cases "Two valid actors race the same Stone revision"), so a concurrent
    // policy change or a replayed-with-changed-payload attempt cannot silently clobber.
    //
    // net48 audit: FileStream(.Flush(true)), BinaryReader/Writer, SHA256, Encoding.UTF8, engine-free
    // domain types only. Link-compiles into the net8 test project.

    public enum LocalPolicyCommandOutcome
    {
        Applied,
        Replayed,
        Rejected
    }

    /// <summary>Result of a SetSettlementLocalPolicy command. On rejection nothing was journaled or
    /// committed (zero-mutation reject).</summary>
    public readonly struct LocalPolicyCommandResult
    {
        public LocalPolicyCommandResult(LocalPolicyCommandOutcome outcome, string resultCode,
            string receiptId, LocalBeneficiaryMode mode, long policyRevision, long stoneRevision)
        {
            Outcome = outcome;
            ResultCode = resultCode;
            ReceiptId = receiptId;
            Mode = mode;
            PolicyRevision = policyRevision;
            StoneRevision = stoneRevision;
        }

        public LocalPolicyCommandOutcome Outcome { get; }
        public string ResultCode { get; }
        public string ReceiptId { get; }
        public LocalBeneficiaryMode Mode { get; }
        public long PolicyRevision { get; }
        public long StoneRevision { get; }
    }

    /// <summary>Server-owned Homestead owner authority (contracts.md §"SetSettlementLocalPolicy"
    /// Validates: "Homestead owner authority"). Given the authenticated principal + Stone, proves the
    /// acting character is the validated Homestead owner. Never client-authored; no permissive fallback
    /// — a null policy is rejected at construction. Kept as a seam so the handler stays engine-free.</summary>
    public interface IHomesteadOwnerAuthority
    {
        /// <summary>True when <paramref name="principal"/> is the validated Homestead owner of
        /// <paramref name="stoneId"/> and may set its Settlement Local policy.</summary>
        bool IsOwner(AuthoritativePrincipal principal, StoneId stoneId);
    }

    /// <summary>A SetSettlementLocalPolicy command envelope (contracts.md payload: policy =
    /// Everyone|Attuned|Private, allowlist revision/list when Private). The transport attaches the
    /// server-observed <see cref="Connection"/>; <see cref="Claim"/> is compared but never trusted.</summary>
    public readonly struct SetSettlementLocalPolicyCommand
    {
        public SetSettlementLocalPolicyCommand(
            OperationId operationId,
            StoneId stoneId,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            LocalBeneficiaryMode mode,
            IReadOnlyList<string>? allowlistAccounts = null,
            long? expectedStoneRevision = null,
            long? expectedPolicyRevision = null)
        {
            OperationId = operationId;
            StoneId = stoneId;
            Connection = connection;
            Claim = claim;
            Mode = mode;
            AllowlistAccounts = allowlistAccounts ?? Array.Empty<string>();
            ExpectedStoneRevision = expectedStoneRevision;
            ExpectedPolicyRevision = expectedPolicyRevision;
        }

        public OperationId OperationId { get; }
        public StoneId StoneId { get; }
        public AuthenticatedConnection Connection { get; }
        public ClaimedPrincipal Claim { get; }
        public LocalBeneficiaryMode Mode { get; }
        public IReadOnlyList<string> AllowlistAccounts { get; }
        public long? ExpectedStoneRevision { get; }
        public long? ExpectedPolicyRevision { get; }
    }

    public sealed class LocalPolicyCommandHandler
    {
        private readonly string _journalPath;
        private readonly PrincipalResolver _resolver;
        private readonly IStoneAggregateStore _stoneStore;
        private readonly IHomesteadOwnerAuthority _ownerAuthority;

        public LocalPolicyCommandHandler(
            string journalPath,
            PrincipalResolver resolver,
            IStoneAggregateStore stoneStore,
            IHomesteadOwnerAuthority ownerAuthority)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _stoneStore = stoneStore ?? throw new ArgumentNullException(nameof(stoneStore));
            _ownerAuthority = ownerAuthority ?? throw new ArgumentNullException(nameof(ownerAuthority));

            RehydrateFromJournal();
        }

        public string JournalPath => _journalPath;

        public LocalPolicyCommandResult Handle(SetSettlementLocalPolicyCommand command)
        {
            var resolution = _resolver.Resolve(command.Connection, command.Claim, out var principal);
            if (resolution == PrincipalResolution.UnauthenticatedPeer)
                return Reject("Unauthenticated");
            if (resolution == PrincipalResolution.PrincipalMismatch)
                return Reject("PrincipalMismatch");

            string opId = command.OperationId.Value;

            // Normalize the intended allowlist exactly as the policy will store it (Private-only,
            // deduplicated, ordinal-sorted) so the payload digest is stable and a semantically-equal
            // resubmit replays instead of conflicting.
            var normalizedPolicy = SettlementLocalPolicy.Default.With(command.Mode, command.AllowlistAccounts);

            string bindingDigest = Digest(string.Join("|", new[]
            {
                opId,
                command.StoneId.Value,
                principal.Account.Value,
                principal.Character.Value
            }));

            string payloadDigest = Digest(string.Join("|", new[]
            {
                ((int)command.Mode).ToString(CultureInfo.InvariantCulture),
                string.Join(",", normalizedPolicy.AllowlistAccounts),
                command.ExpectedStoneRevision?.ToString(CultureInfo.InvariantCulture) ?? "-",
                command.ExpectedPolicyRevision?.ToString(CultureInfo.InvariantCulture) ?? "-"
            }));

            var existing = FindCommitted(opId);
            if (existing != null)
            {
                if (!string.Equals(existing.BindingDigest, bindingDigest, StringComparison.Ordinal)
                    || !string.Equals(existing.PayloadDigest, payloadDigest, StringComparison.Ordinal))
                    return Reject("OperationConflict");
                ApplyProjection(opId, existing);
                return Terminal(LocalPolicyCommandOutcome.Replayed, existing);
            }

            if (HasConflictingPartialIntent(opId, bindingDigest, payloadDigest))
                return Reject("OperationConflict");

            var stone = _stoneStore.GetStone(command.StoneId);
            if (stone == null)
                return Reject("StoneNotFound");

            // Authority: only the validated Homestead owner may set the Settlement Local policy. A
            // non-owner (bonded Governor / attuned player / stranger) is rejected with zero mutation.
            if (!_ownerAuthority.IsOwner(principal, command.StoneId))
                return Reject("Unauthorized");

            // Optimistic concurrency on BOTH the Stone revision and the policy revision. A stale
            // expectation on either rejects with zero mutation so a concurrent policy change or a
            // replay of stale intent cannot clobber a newer policy.
            if (command.ExpectedStoneRevision.HasValue
                && command.ExpectedStoneRevision.Value != stone.Revision)
                return Reject("StaleStoneRevision");
            if (command.ExpectedPolicyRevision.HasValue
                && command.ExpectedPolicyRevision.Value != stone.LocalPolicy.Revision)
                return Reject("StalePolicyRevision");

            // Pure policy transition: increment the policy revision from the Stone's CURRENT policy, then
            // produce the next Stone with the new policy (revision incremented, provenance stamped).
            var nextPolicy = stone.LocalPolicy.With(command.Mode, command.AllowlistAccounts);
            var nextStone = stone.WithLocalPolicy(nextPolicy, "localpolicy:" + opId);

            var record = new CommittedLocalPolicy
            {
                OperationId = opId,
                BindingDigest = bindingDigest,
                PayloadDigest = payloadDigest,
                StoneId = command.StoneId.Value,
                ResultCode = "Applied",
                Mode = (int)nextPolicy.Mode,
                PolicyRevision = nextPolicy.Revision,
                StoneRevision = nextStone.Revision,
                StoneSnapshot = nextStone.Serialize()
            };

            Append(Record(LocalPolicyBoundary.IntentJournaled, record));
            Append(Record(LocalPolicyBoundary.Committed, record));

            ApplyProjection(opId, record);

            return Terminal(LocalPolicyCommandOutcome.Applied, record);
        }

        private static LocalPolicyCommandResult Terminal(LocalPolicyCommandOutcome outcome, CommittedLocalPolicy r) =>
            new LocalPolicyCommandResult(outcome, r.ResultCode, Receipt(r.OperationId),
                (LocalBeneficiaryMode)r.Mode, r.PolicyRevision, r.StoneRevision);

        private void ApplyProjection(string operationId, CommittedLocalPolicy record)
        {
            _stoneStore.ApplyStoneProjection(operationId,
                StoneProgressionAggregate.Deserialize(record.StoneSnapshot));
        }

        private static LocalPolicyCommandResult Reject(string code) =>
            new LocalPolicyCommandResult(LocalPolicyCommandOutcome.Rejected, code, string.Empty,
                LocalBeneficiaryMode.Everyone, 0, 0);

        private static string Receipt(string opId) => Digest("localpolicyreceipt|" + opId);

        // ---- Journal ----

        private enum LocalPolicyBoundary
        {
            IntentJournaled = 1,
            Committed = 2
        }

        private sealed class CommittedLocalPolicy
        {
            public string OperationId = string.Empty;
            public string BindingDigest = string.Empty;
            public string PayloadDigest = string.Empty;
            public string StoneId = string.Empty;
            public string ResultCode = string.Empty;
            public int Mode;
            public long PolicyRevision;
            public long StoneRevision;
            public string StoneSnapshot = string.Empty;
        }

        private CommittedLocalPolicy? FindCommitted(string operationId)
        {
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null || rec.Value.OperationId != operationId) continue;
                if (rec.Value.Boundary == LocalPolicyBoundary.Committed)
                    return rec.Value.Record;
            }
            return null;
        }

        private bool HasConflictingPartialIntent(string operationId, string bindingDigest, string payloadDigest)
        {
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null || rec.Value.OperationId != operationId) continue;
                if (rec.Value.Boundary != LocalPolicyBoundary.IntentJournaled) continue;
                if (!string.Equals(rec.Value.Record.BindingDigest, bindingDigest, StringComparison.Ordinal)
                    || !string.Equals(rec.Value.Record.PayloadDigest, payloadDigest, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private void RehydrateFromJournal()
        {
            var committedByOp = new Dictionary<string, CommittedLocalPolicy>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null) continue;
                if (rec.Value.Boundary != LocalPolicyBoundary.Committed) continue;
                if (!committedByOp.ContainsKey(rec.Value.OperationId))
                    order.Add(rec.Value.OperationId);
                committedByOp[rec.Value.OperationId] = rec.Value.Record;
            }
            foreach (var opId in order)
                ApplyProjection(opId, committedByOp[opId]);
        }

        private struct ParsedRecord
        {
            public string OperationId;
            public LocalPolicyBoundary Boundary;
            public CommittedLocalPolicy Record;
        }

        private static string Record(LocalPolicyBoundary boundary, CommittedLocalPolicy r)
        {
            // Delimiter-safe framing invariant (ADO #127, mirroring RelationshipCommands.cs / PR #351):
            // the record is pipe-delimited, so EVERY free-text field is base64-encoded before it enters
            // the frame — never written raw. The OperationId in particular is a caller-composed value
            // that legitimately embeds '|' (a StoneId is "world|zoneX|zoneZ" by construction, e.g.
            // "uid:-898655635|3|2"); writing it unencoded exploded an 11-field record into more and the
            // strict parser rejected EVERY frame — and the journal IS the save. Encoding it (and the
            // ResultCode) here, and decoding symmetrically in ParseRecord, keeps the field count
            // exactly 11 for ANY operation id. Digest fields are hex and integer fields are numeric, so
            // neither can contain '|' — they stay raw.
            return string.Join("|", new[]
            {
                "LOCALPOLICYREC",
                Encode(r.OperationId),
                ((int)boundary).ToString(CultureInfo.InvariantCulture),
                r.BindingDigest,
                r.PayloadDigest,
                Encode(r.StoneId),
                Encode(r.ResultCode),
                r.Mode.ToString(CultureInfo.InvariantCulture),
                r.PolicyRevision.ToString(CultureInfo.InvariantCulture),
                r.StoneRevision.ToString(CultureInfo.InvariantCulture),
                Encode(r.StoneSnapshot)
            });
        }

        private static ParsedRecord? ParseRecord(string line)
        {
            var parts = line.Split('|');
            // Delimiter-safe framing (ADO #127): every free-text field is base64-encoded on write, so no
            // raw '|' can appear inside a field and the field count is a reliable structural check. A
            // torn or malformed frame is rejected honestly as null — never partially applied.
            if (parts.Length != 11 || parts[0] != "LOCALPOLICYREC") return null;
            try
            {
                string operationId = Decode(parts[1]);
                var rec = new CommittedLocalPolicy
                {
                    OperationId = operationId,
                    BindingDigest = parts[3],
                    PayloadDigest = parts[4],
                    StoneId = Decode(parts[5]),
                    ResultCode = Decode(parts[6]),
                    Mode = int.Parse(parts[7], CultureInfo.InvariantCulture),
                    PolicyRevision = long.Parse(parts[8], CultureInfo.InvariantCulture),
                    StoneRevision = long.Parse(parts[9], CultureInfo.InvariantCulture),
                    StoneSnapshot = Decode(parts[10])
                };
                return new ParsedRecord
                {
                    OperationId = operationId,
                    Boundary = (LocalPolicyBoundary)int.Parse(parts[2], CultureInfo.InvariantCulture),
                    Record = rec
                };
            }
            catch (FormatException)
            {
                return null;   // not valid base64 / not a well-formed number — reject honestly.
            }
            catch (OverflowException)
            {
                return null;   // a revision field that overflowed long — malformed, reject honestly.
            }
        }

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
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
