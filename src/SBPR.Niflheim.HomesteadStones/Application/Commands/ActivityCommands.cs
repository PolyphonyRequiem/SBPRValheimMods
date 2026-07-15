using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;

namespace SBPR.Niflheim.HomesteadStones.Application.Commands
{
    // T012 — recoverable RecordAlignedActivity command handler (contracts.md §"RecordAlignedActivity";
    // data-model.md §"Credit and spend BP on node development"). This is the mutation authority for
    // crediting one personal Stone-wide BP balance from an eligible aligned activity: it authenticates
    // the principal, requires an ACTIVE Bond whose Responsibility Range covers the associated Committed
    // Tree, runs the PURE BondPower.Credit transition, and commits the resulting character aggregate
    // under ONE durable, replayable receipt.
    //
    // BP is credited to the BONDED CHARACTER's one Stone-wide balance (contracts.md: "BP to a bonded
    // character: N to that character's one Stone-wide personal BP balance"). Because the balance is
    // keyed per (account, character, Stone), a different Governor — even a sibling on the same account —
    // is a separate balance (AT-BP-NOT-SHARED); this handler only ever credits the authenticated
    // acting character.
    //
    // Recovery model mirrors FacetCommands/RelationshipCommands: an append-only, per-boundary-fsync'd
    // journal IS the transaction. The character aggregate store is an idempotent projection of the
    // journal. Re-submitting the same operationId returns the recorded terminal result (Replayed); a
    // conflicting binding/payload under a committed op rejects OperationConflict with no mutation.
    //
    // net48 audit: FileStream(.Flush(true)), BinaryReader/Writer, SHA256, Encoding.UTF8, engine-free
    // domain types only. Link-compiles into the net8 test project.

    public enum ActivityCommandOutcome
    {
        Applied,
        Replayed,
        Rejected
    }

    /// <summary>Result of a RecordAlignedActivity command. On rejection nothing was journaled or
    /// committed.</summary>
    public readonly struct ActivityCommandResult
    {
        public ActivityCommandResult(ActivityCommandOutcome outcome, string resultCode,
            string receiptId, int bpDelta, int resultingBp, long characterRevision)
        {
            Outcome = outcome;
            ResultCode = resultCode;
            ReceiptId = receiptId;
            BpDelta = bpDelta;
            ResultingBp = resultingBp;
            CharacterRevision = characterRevision;
        }

        public ActivityCommandOutcome Outcome { get; }
        public string ResultCode { get; }
        public string ReceiptId { get; }
        public int BpDelta { get; }
        public int ResultingBp { get; }
        public long CharacterRevision { get; }
    }

    /// <summary>A RecordAlignedActivity command envelope. The transport attaches the server-observed
    /// <see cref="Connection"/>; <see cref="Claim"/> is untrusted payload compared but never trusted.
    /// The adapter supplies the server-observed Committed Tree context + BP award + evidence digest.</summary>
    public readonly struct RecordAlignedActivityCommand
    {
        public RecordAlignedActivityCommand(
            OperationId operationId,
            StoneId stoneId,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            VersionedId committedTreeContext,
            int bpAward,
            string evidenceDigest,
            long? expectedCharacterRevision = null)
        {
            OperationId = operationId;
            StoneId = stoneId;
            Connection = connection;
            Claim = claim;
            CommittedTreeContext = committedTreeContext;
            BpAward = bpAward;
            EvidenceDigest = evidenceDigest ?? string.Empty;
            ExpectedCharacterRevision = expectedCharacterRevision;
        }

        public OperationId OperationId { get; }
        public StoneId StoneId { get; }
        public AuthenticatedConnection Connection { get; }
        public ClaimedPrincipal Claim { get; }
        public VersionedId CommittedTreeContext { get; }
        public int BpAward { get; }
        public string EvidenceDigest { get; }
        public long? ExpectedCharacterRevision { get; }
    }

    public sealed class ActivityCommandHandler
    {
        private readonly string _journalPath;
        private readonly PrincipalResolver _resolver;
        private readonly ICharacterAggregateStore _characterStore;
        private readonly IAccountStoneAuthorityStore _authorityStore;
        private readonly IResponsibilityRangePolicy _rangePolicy;

        public ActivityCommandHandler(
            string journalPath,
            PrincipalResolver resolver,
            ICharacterAggregateStore characterStore,
            IAccountStoneAuthorityStore authorityStore,
            IResponsibilityRangePolicy rangePolicy)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _characterStore = characterStore ?? throw new ArgumentNullException(nameof(characterStore));
            _authorityStore = authorityStore ?? throw new ArgumentNullException(nameof(authorityStore));
            // No permissive fallback: every caller injects a server-owned Responsibility-Range policy.
            _rangePolicy = rangePolicy ?? throw new ArgumentNullException(nameof(rangePolicy));

            RehydrateFromJournal();
        }

        public string JournalPath => _journalPath;

        public ActivityCommandResult Handle(RecordAlignedActivityCommand command)
        {
            var resolution = _resolver.Resolve(command.Connection, command.Claim, out var principal);
            if (resolution == PrincipalResolution.UnauthenticatedPeer)
                return Reject("Unauthenticated");
            if (resolution == PrincipalResolution.PrincipalMismatch)
                return Reject("PrincipalMismatch");

            string opId = command.OperationId.Value;

            string bindingDigest = Digest(string.Join("|", new[]
            {
                opId,
                command.StoneId.Value,
                principal.Account.Value,
                principal.Character.Value,
                command.CommittedTreeContext.ToString()
            }));

            string payloadDigest = Digest(string.Join("|", new[]
            {
                command.BpAward.ToString(CultureInfo.InvariantCulture),
                command.EvidenceDigest,
                command.ExpectedCharacterRevision?.ToString(CultureInfo.InvariantCulture) ?? "-"
            }));

            var existing = FindCommitted(opId);
            if (existing != null)
            {
                if (!string.Equals(existing.BindingDigest, bindingDigest, StringComparison.Ordinal)
                    || !string.Equals(existing.PayloadDigest, payloadDigest, StringComparison.Ordinal))
                    return Reject("OperationConflict");
                ApplyProjection(opId, existing);
                return new ActivityCommandResult(ActivityCommandOutcome.Replayed, existing.ResultCode,
                    Receipt(opId), existing.BpDelta, existing.ResultingBp, existing.CharacterRevision);
            }

            if (HasConflictingPartialIntent(opId, bindingDigest, payloadDigest))
                return Reject("OperationConflict");

            var character = _characterStore.GetCharacter(principal.Account, principal.Character);
            if (character == null)
                return Reject("CharacterNotFound");
            var authority = _authorityStore.GetAuthority(principal.Account, command.StoneId);

            // BP credit requires an ACTIVE Bond (a bonded character) whose Responsibility Range covers
            // the associated Committed Tree (data-model.md: "in the Governor's Responsibility Range").
            var bond = FindActiveBond(character, authority, command.StoneId);
            if (bond == null)
                return Reject("Unauthorized");
            if (!_rangePolicy.CoversTree(command.StoneId, bond.ResponsibilityRange, bond.OwnerGovernorRole,
                    command.CommittedTreeContext))
                return Reject("OutsideResponsibilityRange");

            var transition = BondPower.Credit(character, command.StoneId, command.BpAward,
                command.ExpectedCharacterRevision);
            if (!transition.Accepted)
                return Reject(transition.ResultCode);

            var record = new CommittedActivity
            {
                OperationId = opId,
                BindingDigest = bindingDigest,
                PayloadDigest = payloadDigest,
                AccountId = principal.Account.Value,
                CharacterId = principal.Character.Value,
                StoneId = command.StoneId.Value,
                ResultCode = "Applied",
                BpDelta = command.BpAward,
                ResultingBp = transition.ResultingBp,
                CharacterRevision = transition.Character.Revision,
                CharacterSnapshot = transition.Character.Serialize()
            };

            Append(Record(ActivityBoundary.IntentJournaled, record));
            Append(Record(ActivityBoundary.Committed, record));

            ApplyProjection(opId, record);

            return new ActivityCommandResult(ActivityCommandOutcome.Applied, "Applied",
                Receipt(opId), record.BpDelta, record.ResultingBp, record.CharacterRevision);
        }

        private static RelationshipRecord? FindActiveBond(
            CharacterProgressionAggregate character, AccountStoneAuthorityIndex authority, StoneId stoneId)
        {
            var reservation = authority.ReservationFor(character.Character);
            if (reservation == null || reservation.Kind != RelationshipKind.Bond)
                return null;
            foreach (var sr in character.StoneRecords)
            {
                if (!sr.StoneId.Equals(stoneId)) continue;
                foreach (var rel in sr.Relationships)
                {
                    if (rel.IsActive && rel.Kind == RelationshipKind.Bond
                        && string.Equals(rel.RelationshipId, reservation.RelationshipId, StringComparison.Ordinal))
                        return rel;
                }
            }
            return null;
        }

        private void ApplyProjection(string operationId, CommittedActivity record)
        {
            _characterStore.ApplyCharacterProjection(operationId,
                CharacterProgressionAggregate.Deserialize(record.CharacterSnapshot));
        }

        private static ActivityCommandResult Reject(string code) =>
            new ActivityCommandResult(ActivityCommandOutcome.Rejected, code, string.Empty, 0, 0, 0);

        private static string Receipt(string opId) => Digest("activityreceipt|" + opId);

        // ---- Journal ----

        private enum ActivityBoundary
        {
            IntentJournaled = 1,
            Committed = 2
        }

        private sealed class CommittedActivity
        {
            public string OperationId = string.Empty;
            public string BindingDigest = string.Empty;
            public string PayloadDigest = string.Empty;
            public string AccountId = string.Empty;
            public string CharacterId = string.Empty;
            public string StoneId = string.Empty;
            public string ResultCode = string.Empty;
            public int BpDelta;
            public int ResultingBp;
            public long CharacterRevision;
            public string CharacterSnapshot = string.Empty;
        }

        private CommittedActivity? FindCommitted(string operationId)
        {
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null || rec.Value.OperationId != operationId) continue;
                if (rec.Value.Boundary == ActivityBoundary.Committed)
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
                if (rec.Value.Boundary != ActivityBoundary.IntentJournaled) continue;
                if (!string.Equals(rec.Value.Record.BindingDigest, bindingDigest, StringComparison.Ordinal)
                    || !string.Equals(rec.Value.Record.PayloadDigest, payloadDigest, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private void RehydrateFromJournal()
        {
            var committedByOp = new Dictionary<string, CommittedActivity>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null) continue;
                if (rec.Value.Boundary != ActivityBoundary.Committed) continue;
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
            public ActivityBoundary Boundary;
            public CommittedActivity Record;
        }

        private static string Record(ActivityBoundary boundary, CommittedActivity r)
        {
            return string.Join("|", new[]
            {
                "ACTIVITYREC",
                r.OperationId,
                ((int)boundary).ToString(CultureInfo.InvariantCulture),
                r.BindingDigest,
                r.PayloadDigest,
                Encode(r.AccountId),
                Encode(r.CharacterId),
                Encode(r.StoneId),
                r.ResultCode,
                r.BpDelta.ToString(CultureInfo.InvariantCulture),
                r.ResultingBp.ToString(CultureInfo.InvariantCulture),
                r.CharacterRevision.ToString(CultureInfo.InvariantCulture),
                Encode(r.CharacterSnapshot)
            });
        }

        private static ParsedRecord? ParseRecord(string line)
        {
            var parts = line.Split('|');
            if (parts.Length != 13 || parts[0] != "ACTIVITYREC") return null;
            var rec = new CommittedActivity
            {
                OperationId = parts[1],
                BindingDigest = parts[3],
                PayloadDigest = parts[4],
                AccountId = Decode(parts[5]),
                CharacterId = Decode(parts[6]),
                StoneId = Decode(parts[7]),
                ResultCode = parts[8],
                BpDelta = int.Parse(parts[9], CultureInfo.InvariantCulture),
                ResultingBp = int.Parse(parts[10], CultureInfo.InvariantCulture),
                CharacterRevision = long.Parse(parts[11], CultureInfo.InvariantCulture),
                CharacterSnapshot = Decode(parts[12])
            };
            return new ParsedRecord
            {
                OperationId = parts[1],
                Boundary = (ActivityBoundary)int.Parse(parts[2], CultureInfo.InvariantCulture),
                Record = rec
            };
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
