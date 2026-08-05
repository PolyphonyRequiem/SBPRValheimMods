using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;

namespace SBPR.Niflheim.HomesteadStones.Application.Commands
{
    // T012 — recoverable RecordAlignedActivity command handler (contracts.md §"RecordAlignedActivity";
    // data-model.md §"Credit and spend BP on node development"). This is the mutation authority that
    // credits a bonded Governor's ONE Stone-wide personal BP balance from a trusted, server-observed
    // aligned activity associated with a Committed Tree in the Governor's Responsibility Range.
    //
    // Recovery model mirrors FacetCommands/RelationshipCommands: an append-only, per-boundary-fsync'd
    // journal IS the transaction. The character aggregate store is an idempotent projection of the
    // journal; a crash between intent and terminal leaves no partial credit. Re-submitting the same
    // operationId returns the recorded terminal result (Replayed); a conflicting binding/payload under a
    // committed op rejects OperationConflict with no mutation.
    //
    // Authority (contracts.md): credit requires the acting character to hold an ACTIVE Bond to this
    // Stone (Attunement grants no cultivation authority), the associated Tree to be COMMITTED on the
    // Stone, and the server-owned Governor development authority to confirm the Bond's authored
    // Responsibility Range covers that Tree. Uncommitted candidates cannot authorize credit.
    //
    // Load-bearing (FR-010, AT-BP-STONE-WIDE / AT-BP-NOT-SHARED): the credited BP lands on the acting
    // character's OWN aggregate keyed only by StoneId — never on the account, never on the Stone, never
    // bound to a source Tree. Different Governors therefore never share a balance.
    //
    // net48 audit: FileStream(.Flush(true)), BinaryReader/Writer, SHA256, Encoding.UTF8, engine-free
    // domain types only. Link-compiles into the net8 test project.

    public enum ActivityCommandOutcome
    {
        Applied,
        Replayed,
        Rejected
    }

    /// <summary>Server-owned Governor development authority (contracts.md RecordAlignedActivity /
    /// ApplyBPToNode: "Governor and Responsibility Range"). Given the acting Bond's authored
    /// Responsibility Range + role and the target Committed Tree, confirms the Governor may credit/spend
    /// BP on that Tree. No permissive fallback — a null policy is rejected at construction.</summary>
    public interface IGovernorDevelopmentAuthority
    {
        /// <summary>True when a Bond carrying <paramref name="responsibilityRange"/> and
        /// <paramref name="ownerGovernorRole"/> is authorized to develop <paramref name="tree"/> on
        /// <paramref name="stoneId"/>.</summary>
        bool CanDevelop(StoneId stoneId, string responsibilityRange, string ownerGovernorRole, VersionedId tree);
    }

    /// <summary>Result of a RecordAlignedActivity command. On rejection nothing was journaled/committed.</summary>
    public readonly struct ActivityCommandResult
    {
        public ActivityCommandResult(ActivityCommandOutcome outcome, string resultCode,
            string receiptId, int bpAwarded, int newBpBalance, long characterRevision)
        {
            Outcome = outcome;
            ResultCode = resultCode;
            ReceiptId = receiptId;
            BpAwarded = bpAwarded;
            NewBpBalance = newBpBalance;
            CharacterRevision = characterRevision;
        }

        public ActivityCommandOutcome Outcome { get; }
        public string ResultCode { get; }
        public string ReceiptId { get; }
        public int BpAwarded { get; }
        public int NewBpBalance { get; }
        public long CharacterRevision { get; }
    }

    /// <summary>A RecordAlignedActivity command envelope (contracts.md AlignedActivityEvidence). The
    /// transport attaches the server-observed <see cref="Connection"/>; <see cref="Claim"/> is untrusted
    /// payload compared but never trusted. Built by AlignedActivityAdapter from server-observed facts.</summary>
    public readonly struct RecordAlignedActivityCommand
    {
        public RecordAlignedActivityCommand(
            OperationId operationId,
            StoneId stoneId,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            VersionedId associatedTree,
            int bpAward,
            string evidenceDigest,
            long? expectedCharacterRevision = null)
        {
            OperationId = operationId;
            StoneId = stoneId;
            Connection = connection;
            Claim = claim;
            AssociatedTree = associatedTree;
            BpAward = bpAward;
            EvidenceDigest = evidenceDigest ?? string.Empty;
            ExpectedCharacterRevision = expectedCharacterRevision;
        }

        public OperationId OperationId { get; }
        public StoneId StoneId { get; }
        public AuthenticatedConnection Connection { get; }
        public ClaimedPrincipal Claim { get; }
        public VersionedId AssociatedTree { get; }
        public int BpAward { get; }
        public string EvidenceDigest { get; }
        public long? ExpectedCharacterRevision { get; }
    }

    public sealed class ActivityCommandHandler
    {
        private readonly string _journalPath;
        private readonly PrincipalResolver _resolver;
        private readonly IStoneAggregateStore _stoneStore;
        private readonly ICharacterAggregateStore _characterStore;
        private readonly IAccountStoneAuthorityStore _authorityStore;
        private readonly IGovernorDevelopmentAuthority _developmentAuthority;

        public ActivityCommandHandler(
            string journalPath,
            PrincipalResolver resolver,
            IStoneAggregateStore stoneStore,
            ICharacterAggregateStore characterStore,
            IAccountStoneAuthorityStore authorityStore,
            IGovernorDevelopmentAuthority developmentAuthority)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _stoneStore = stoneStore ?? throw new ArgumentNullException(nameof(stoneStore));
            _characterStore = characterStore ?? throw new ArgumentNullException(nameof(characterStore));
            _authorityStore = authorityStore ?? throw new ArgumentNullException(nameof(authorityStore));
            _developmentAuthority = developmentAuthority ?? throw new ArgumentNullException(nameof(developmentAuthority));

            RehydrateFromJournal();
        }

        public string JournalPath => _journalPath;

        public ActivityCommandResult Handle(RecordAlignedActivityCommand command)
        {
            // 1-2. Authenticate connection principal; compare (never trust) the claim.
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
                command.AssociatedTree.Serialize()
            }));

            string payloadDigest = Digest(string.Join("|", new[]
            {
                command.BpAward.ToString(CultureInfo.InvariantCulture),
                command.EvidenceDigest,
                command.ExpectedCharacterRevision?.ToString(CultureInfo.InvariantCulture) ?? "-"
            }));

            // Idempotency: a committed record returns its recorded terminal result; a conflicting
            // binding OR payload under a committed op is OperationConflict.
            var existing = FindCommitted(opId);
            if (existing != null)
            {
                if (!string.Equals(existing.BindingDigest, bindingDigest, StringComparison.Ordinal)
                    || !string.Equals(existing.PayloadDigest, payloadDigest, StringComparison.Ordinal))
                    return Reject("OperationConflict");
                ApplyProjection(opId, existing);
                return new ActivityCommandResult(ActivityCommandOutcome.Replayed, existing.ResultCode,
                    Receipt(opId), existing.BpAwarded, existing.NewBpBalance, existing.CharacterRevision);
            }

            if (HasConflictingPartialIntent(opId, bindingDigest, payloadDigest))
                return Reject("OperationConflict");

            // Load current authoritative state.
            var stone = _stoneStore.GetStone(command.StoneId);
            if (stone == null)
                return Reject("StoneNotFound");
            var character = _characterStore.GetCharacter(principal.Account, principal.Character);
            if (character == null)
                return Reject("CharacterNotFound");
            var authority = _authorityStore.GetAuthority(principal.Account, command.StoneId);

            // Authority: acting character must hold an ACTIVE Bond (Attunement grants no cultivation
            // authority). Validated BEFORE any journal write.
            var bond = FindActiveBond(character, authority, command.StoneId);
            if (bond == null)
                return Reject("Unauthorized");

            // The associated Tree must be committed on this Stone (uncommitted candidates cannot
            // authorize credit — contracts.md RecordAlignedActivity).
            if (!IsTreeCommitted(stone, command.AssociatedTree))
                return Reject("TreeNotCommitted");

            // The Bond's authored Responsibility Range must cover the associated Tree.
            if (!_developmentAuthority.CanDevelop(command.StoneId, bond.ResponsibilityRange,
                    bond.OwnerGovernorRole, command.AssociatedTree))
                return Reject("OutsideResponsibilityRange");

            // Optimistic concurrency on the character aggregate (CAS) before the pure credit.
            if (command.ExpectedCharacterRevision.HasValue
                && command.ExpectedCharacterRevision.Value != character.Revision)
                return Reject("StaleCharacterRevision");

            // Pure BP credit to the one Stone-wide balance.
            var transition = BondPower.Credit(character, command.StoneId, command.BpAward);
            if (!transition.Accepted)
                return Reject(MapBpResult(transition.Result));

            var record = new CommittedActivity
            {
                OperationId = opId,
                BindingDigest = bindingDigest,
                PayloadDigest = payloadDigest,
                AccountId = principal.Account.Value,
                CharacterId = principal.Character.Value,
                StoneId = command.StoneId.Value,
                ResultCode = "Applied",
                BpAwarded = command.BpAward,
                NewBpBalance = transition.NewBalance,
                CharacterRevision = transition.NextCharacter.Revision,
                CharacterSnapshot = transition.NextCharacter.Serialize()
            };

            Append(Record(ActivityBoundary.IntentJournaled, record));
            Append(Record(ActivityBoundary.Committed, record));

            ApplyProjection(opId, record);

            return new ActivityCommandResult(ActivityCommandOutcome.Applied, "Applied",
                Receipt(opId), record.BpAwarded, record.NewBpBalance, record.CharacterRevision);
        }

        private static string MapBpResult(BondPowerResult r)
        {
            switch (r)
            {
                case BondPowerResult.NonPositiveAmount: return "NonPositiveAward";
                case BondPowerResult.StaleRevision: return "StaleCharacterRevision";
                default: return "Rejected";
            }
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

        private static bool IsTreeCommitted(StoneProgressionAggregate stone, VersionedId tree)
        {
            foreach (var c in stone.CommittedTrees)
                if (c.Tree.Equals(tree)) return true;
            return false;
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
            public int BpAwarded;
            public int NewBpBalance;
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
            // Delimiter-safe framing invariant (ADO #127, mirroring RelationshipCommands.cs / PR #351):
            // the record is pipe-delimited, so EVERY free-text field is base64-encoded before it enters
            // the frame — never written raw. The OperationId in particular is a caller-composed value
            // that legitimately embeds '|' (a StoneId is "world|zoneX|zoneZ" by construction, e.g.
            // "uid:-898655635|3|2"); writing it unencoded exploded a 13-field record into more and the
            // strict parser rejected EVERY frame — and the journal IS the save. Encoding it (and the
            // ResultCode) here, and decoding symmetrically in ParseRecord, keeps the field count
            // exactly 13 for ANY operation id. Digest fields are hex and integer fields are numeric, so
            // neither can contain '|' — they stay raw.
            return string.Join("|", new[]
            {
                "ACTIVITYREC",
                Encode(r.OperationId),
                ((int)boundary).ToString(CultureInfo.InvariantCulture),
                r.BindingDigest,
                r.PayloadDigest,
                Encode(r.AccountId),
                Encode(r.CharacterId),
                Encode(r.StoneId),
                Encode(r.ResultCode),
                r.BpAwarded.ToString(CultureInfo.InvariantCulture),
                r.NewBpBalance.ToString(CultureInfo.InvariantCulture),
                r.CharacterRevision.ToString(CultureInfo.InvariantCulture),
                Encode(r.CharacterSnapshot)
            });
        }

        private static ParsedRecord? ParseRecord(string line)
        {
            var parts = line.Split('|');
            // Delimiter-safe framing (ADO #127): every free-text field is base64-encoded on write, so no
            // raw '|' can appear inside a field and the field count is a reliable structural check. A
            // torn or malformed frame (wrong field count, bad tag, non-base64 field, or an integer field
            // that overflows) is rejected honestly as null — never partially applied.
            if (parts.Length != 13 || parts[0] != "ACTIVITYREC") return null;
            try
            {
                string operationId = Decode(parts[1]);
                var rec = new CommittedActivity
                {
                    OperationId = operationId,
                    BindingDigest = parts[3],
                    PayloadDigest = parts[4],
                    AccountId = Decode(parts[5]),
                    CharacterId = Decode(parts[6]),
                    StoneId = Decode(parts[7]),
                    ResultCode = Decode(parts[8]),
                    BpAwarded = int.Parse(parts[9], CultureInfo.InvariantCulture),
                    NewBpBalance = int.Parse(parts[10], CultureInfo.InvariantCulture),
                    CharacterRevision = long.Parse(parts[11], CultureInfo.InvariantCulture),
                    CharacterSnapshot = Decode(parts[12])
                };
                return new ParsedRecord
                {
                    OperationId = operationId,
                    Boundary = (ActivityBoundary)int.Parse(parts[2], CultureInfo.InvariantCulture),
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
