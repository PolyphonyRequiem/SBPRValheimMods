using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;

namespace SBPR.Niflheim.HomesteadStones.Application.Commands
{
    // T007 — recoverable CreateBond / CreateAttunement / ReleaseRelationship command handler
    // (contracts.md §"Relationship commands"; data-model.md §"Form relationship"/§"Release
    // relationship"). This is the cross-aggregate mutation authority: it authenticates the principal,
    // runs the PURE relationship transition (Domain/CharacterProgression/Relationships.cs), and commits
    // the resulting character aggregate + account–Stone authority index under ONE durable, replayable
    // receipt.
    //
    // Recovery model mirrors the Gate-A OperationReceiptStore: an append-only, per-boundary-fsync'd
    // journal IS the transaction. The character/authority stores are idempotent projections of the
    // journal, so a crash between the two separate aggregate writes cannot leave a partial result —
    // recovery re-derives both from the one committed record. Re-submitting the same operationId
    // returns the recorded terminal result (Replayed); a conflicting binding under a committed op
    // rejects OperationConflict with no mutation.
    //
    // net48 audit: FileStream(.Flush(true)), BinaryReader/Writer, SHA256, Encoding.UTF8, engine-free
    // domain types only. No net5+ surface, no UnityEngine/Valheim/BepInEx reference: link-compiles
    // into the net8 test project.

    public enum RelationshipCommandType
    {
        CreateBond = 1,
        CreateAttunement = 2,
        ReleaseRelationship = 3
    }

    public enum RelationshipCommandOutcome
    {
        Applied,
        Replayed,
        Rejected
    }

    /// <summary>Result of a relationship command. On rejection nothing was journaled or committed
    /// (contracts.md: a rejection is not a receipt-bearing mutation).</summary>
    public readonly struct RelationshipCommandResult
    {
        public RelationshipCommandResult(RelationshipCommandOutcome outcome, string resultCode,
            string receiptId, string relationshipId,
            long characterRevision, long authorityRevision)
        {
            Outcome = outcome;
            ResultCode = resultCode;
            ReceiptId = receiptId;
            RelationshipId = relationshipId;
            CharacterRevision = characterRevision;
            AuthorityRevision = authorityRevision;
        }

        public RelationshipCommandOutcome Outcome { get; }
        public string ResultCode { get; }
        public string ReceiptId { get; }
        public string RelationshipId { get; }
        public long CharacterRevision { get; }
        public long AuthorityRevision { get; }
    }

    /// <summary>A relationship command envelope (contracts.md common envelope). The transport attaches
    /// the server-observed <see cref="Connection"/>; <see cref="Claim"/> is untrusted payload compared
    /// but never trusted.</summary>
    public readonly struct RelationshipCommand
    {
        public RelationshipCommand(
            OperationId operationId,
            RelationshipCommandType commandType,
            StoneId stoneId,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            string relationshipId,
            string responsibilityRange = "",
            string ownerGovernorRole = "",
            long? expectedCharacterRevision = null,
            long? expectedAuthorityRevision = null,
            RelationshipStatus expectedStatus = RelationshipStatus.Active)
        {
            OperationId = operationId;
            CommandType = commandType;
            StoneId = stoneId;
            Connection = connection;
            Claim = claim;
            RelationshipId = relationshipId ?? string.Empty;
            ResponsibilityRange = responsibilityRange ?? string.Empty;
            OwnerGovernorRole = ownerGovernorRole ?? string.Empty;
            ExpectedCharacterRevision = expectedCharacterRevision;
            ExpectedAuthorityRevision = expectedAuthorityRevision;
            ExpectedStatus = expectedStatus;
        }

        public OperationId OperationId { get; }
        public RelationshipCommandType CommandType { get; }
        public StoneId StoneId { get; }
        public AuthenticatedConnection Connection { get; }
        public ClaimedPrincipal Claim { get; }
        public string RelationshipId { get; }
        public string ResponsibilityRange { get; }
        public string OwnerGovernorRole { get; }
        public long? ExpectedCharacterRevision { get; }
        public long? ExpectedAuthorityRevision { get; }
        public RelationshipStatus ExpectedStatus { get; }
    }

    /// <summary>Supplies the authored Stone classification (family/variant) that drives the
    /// variant-authored relationship policy. Kept as a seam so the handler stays engine-free and the
    /// production wiring can source it from the Stone aggregate.</summary>
    public interface IStoneFamilyResolver
    {
        bool TryGetClassification(StoneId stoneId, out string family, out string variant);
    }

    public sealed class RelationshipCommandHandler
    {
        private readonly string _journalPath;
        private readonly PrincipalResolver _resolver;
        private readonly ICharacterAggregateStore _characterStore;
        private readonly IAccountStoneAuthorityStore _authorityStore;
        private readonly IStoneFamilyResolver _familyResolver;

        public RelationshipCommandHandler(
            string journalPath,
            PrincipalResolver resolver,
            ICharacterAggregateStore characterStore,
            IAccountStoneAuthorityStore authorityStore,
            IStoneFamilyResolver familyResolver)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _characterStore = characterStore ?? throw new ArgumentNullException(nameof(characterStore));
            _authorityStore = authorityStore ?? throw new ArgumentNullException(nameof(authorityStore));
            _familyResolver = familyResolver ?? throw new ArgumentNullException(nameof(familyResolver));

            // Rehydrate the character/authority projections from durable journal truth at construction
            // (server boot). Only committed operations project; a partial op is quarantined, never
            // applied. Idempotent set-to-state, so re-running is a no-op.
            RehydrateFromJournal();
        }

        public string JournalPath => _journalPath;

        public RelationshipCommandResult Handle(RelationshipCommand command)
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
                ((int)command.CommandType).ToString(CultureInfo.InvariantCulture),
                command.StoneId.Value,
                principal.Account.Value,
                principal.Character.Value,
                command.RelationshipId
            }));

            // Idempotency: a committed record for this op returns the one recorded terminal result; a
            // conflicting binding under a committed op is OperationConflict.
            var existing = FindCommitted(opId);
            if (existing != null)
            {
                if (!string.Equals(existing.BindingDigest, bindingDigest, StringComparison.Ordinal))
                    return Reject("OperationConflict");
                // Re-apply the recorded post-state idempotently and return the recorded result.
                ApplyProjections(opId, existing);
                return new RelationshipCommandResult(RelationshipCommandOutcome.Replayed, existing.ResultCode,
                    Receipt(opId), existing.RelationshipId, existing.CharacterRevision, existing.AuthorityRevision);
            }

            // Load current authoritative state.
            var character = _characterStore.GetCharacter(principal.Account, principal.Character);
            if (character == null)
                return Reject("CharacterNotFound");
            var authority = _authorityStore.GetAuthority(principal.Account, command.StoneId);

            if (!_familyResolver.TryGetClassification(command.StoneId, out var family, out var variant))
                return Reject("StoneNotFound");
            var policy = RelationshipPolicy.For(family, variant);

            // 3-4. Run the PURE transition. Validation completes before any journal write, so a
            // rejection changes nothing durable.
            string activationProvenance = "relreceipt:" + opId;
            RelationshipTransition transition;
            switch (command.CommandType)
            {
                case RelationshipCommandType.CreateBond:
                    transition = Relationships.CreateBond(character, authority, command.StoneId, policy,
                        command.RelationshipId, command.ResponsibilityRange, command.OwnerGovernorRole,
                        activationProvenance, command.ExpectedCharacterRevision, command.ExpectedAuthorityRevision);
                    break;
                case RelationshipCommandType.CreateAttunement:
                    transition = Relationships.CreateAttunement(character, authority, command.StoneId, policy,
                        command.RelationshipId, activationProvenance,
                        command.ExpectedCharacterRevision, command.ExpectedAuthorityRevision);
                    break;
                case RelationshipCommandType.ReleaseRelationship:
                    transition = Relationships.ReleaseRelationship(character, authority, command.StoneId,
                        command.RelationshipId, activationProvenance, command.ExpectedStatus,
                        command.ExpectedCharacterRevision, command.ExpectedAuthorityRevision);
                    break;
                default:
                    return Reject("RelationshipConflict");
            }

            if (!transition.Accepted)
                return Reject(transition.ResultCode);

            // 5-8. Commit both aggregates under one durable receipt. The journal is the transaction:
            // intent -> post-state -> terminal, each fsync'd. Recovery replays it idempotently.
            var record = new CommittedRelationship
            {
                OperationId = opId,
                BindingDigest = bindingDigest,
                ResultCode = "Applied",
                RelationshipId = transition.RelationshipId,
                CharacterRevision = transition.Character.Revision,
                AuthorityRevision = transition.Authority.Revision,
                CharacterSnapshot = transition.Character.Serialize(),
                AuthoritySnapshot = transition.Authority.Serialize()
            };

            Append(Record(RelationshipBoundary.IntentJournaled, record));
            Append(Record(RelationshipBoundary.Committed, record));

            ApplyProjections(opId, record);

            return new RelationshipCommandResult(RelationshipCommandOutcome.Applied, "Applied",
                Receipt(opId), record.RelationshipId, record.CharacterRevision, record.AuthorityRevision);
        }

        private void ApplyProjections(string operationId, CommittedRelationship record)
        {
            _characterStore.ApplyCharacterProjection(operationId,
                CharacterProgressionAggregate.Deserialize(record.CharacterSnapshot));
            _authorityStore.ApplyAuthorityProjection(operationId,
                AccountStoneAuthorityIndex.Deserialize(record.AuthoritySnapshot));
        }

        private static RelationshipCommandResult Reject(string code) =>
            new RelationshipCommandResult(RelationshipCommandOutcome.Rejected, code, string.Empty, string.Empty, 0, 0);

        private static string Receipt(string opId) => Digest("relreceipt|" + opId);

        // ---- Journal ----

        private enum RelationshipBoundary
        {
            IntentJournaled = 1,
            Committed = 2
        }

        private sealed class CommittedRelationship
        {
            public string OperationId = string.Empty;
            public string BindingDigest = string.Empty;
            public string ResultCode = string.Empty;
            public string RelationshipId = string.Empty;
            public long CharacterRevision;
            public long AuthorityRevision;
            public string CharacterSnapshot = string.Empty;
            public string AuthoritySnapshot = string.Empty;
        }

        /// <summary>Return the committed record for one operationId, or null when the operation has no
        /// durable terminal record (never committed or only a partial intent survived a crash).</summary>
        private CommittedRelationship? FindCommitted(string operationId)
        {
            CommittedRelationship? intent = null;
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null || rec.Value.OperationId != operationId) continue;
                if (rec.Value.Boundary == RelationshipBoundary.IntentJournaled)
                    intent = rec.Value.Record;
                else if (rec.Value.Boundary == RelationshipBoundary.Committed)
                    return rec.Value.Record;
            }
            // A surviving intent with no terminal is a quarantined partial: not committed, so callers
            // treat the op as absent (safe to re-run from clean current state). We do not return it.
            _ = intent;
            return null;
        }

        private void RehydrateFromJournal()
        {
            // Replay committed operations in journal order, applying each post-state snapshot
            // idempotently. Only terminal-bearing operations project.
            var committedByOp = new Dictionary<string, CommittedRelationship>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null) continue;
                if (rec.Value.Boundary != RelationshipBoundary.Committed) continue;
                if (!committedByOp.ContainsKey(rec.Value.OperationId))
                    order.Add(rec.Value.OperationId);
                committedByOp[rec.Value.OperationId] = rec.Value.Record;
            }
            foreach (var opId in order)
                ApplyProjections(opId, committedByOp[opId]);
        }

        private struct ParsedRecord
        {
            public string OperationId;
            public RelationshipBoundary Boundary;
            public CommittedRelationship Record;
        }

        private static string Record(RelationshipBoundary boundary, CommittedRelationship r)
        {
            return string.Join("|", new[]
            {
                "RELREC",
                r.OperationId,
                ((int)boundary).ToString(CultureInfo.InvariantCulture),
                r.BindingDigest,
                r.ResultCode,
                Encode(r.RelationshipId),
                r.CharacterRevision.ToString(CultureInfo.InvariantCulture),
                r.AuthorityRevision.ToString(CultureInfo.InvariantCulture),
                Encode(r.CharacterSnapshot),
                Encode(r.AuthoritySnapshot)
            });
        }

        private static ParsedRecord? ParseRecord(string line)
        {
            var parts = line.Split('|');
            if (parts.Length != 10 || parts[0] != "RELREC") return null;
            var rec = new CommittedRelationship
            {
                OperationId = parts[1],
                BindingDigest = parts[3],
                ResultCode = parts[4],
                RelationshipId = Decode(parts[5]),
                CharacterRevision = long.Parse(parts[6], CultureInfo.InvariantCulture),
                AuthorityRevision = long.Parse(parts[7], CultureInfo.InvariantCulture),
                CharacterSnapshot = Decode(parts[8]),
                AuthoritySnapshot = Decode(parts[9])
            };
            return new ParsedRecord
            {
                OperationId = parts[1],
                Boundary = (RelationshipBoundary)int.Parse(parts[2], CultureInfo.InvariantCulture),
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
                fs.Flush(true); // fsync-equivalent durable boundary
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
