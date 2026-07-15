using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;

namespace SBPR.Niflheim.HomesteadStones.Application.Commands
{
    // T012 — recoverable ApplyBPToNode command handler (contracts.md §"ApplyBPToNode"; data-model.md
    // §"Credit and spend BP on node development"). This is the CROSS-AGGREGATE mutation authority for
    // BP-driven node development: it authenticates the Governor, requires an active Bond covering the
    // Tree's Responsibility Range, runs the PURE BondPower.Debit (character) + TreeDevelopment.ApplyBpToNode
    // (Stone) transitions, and commits BOTH aggregates under ONE durable, replayable receipt.
    //
    // The debit and the node/Tree-investment delta are ATOMIC (AT-NODE-DEVELOPMENT-COUNTS-AS-INVESTMENT):
    // the single journal record carries both post-state snapshots, so a crash between the two writes
    // leaves no partial result — recovery re-derives both from the one committed record. Personal BP is
    // debited from the acting character's ONE Stone-wide balance (Stone-wide, no Tree binding), so BP
    // credited by a Cooking activity funds a committed Crafting node (AT-BP-STONE-WIDE); a different
    // Governor's balance is untouched (AT-BP-NOT-SHARED, enforced by the per-character balance).
    //
    // There is NO direct Tree-level meter, spend, or command (AT-NO-DIRECT-LEVEL-METER): Tree Level rides
    // along inside the pure Stone transition purely as a function of cumulative investment vs the
    // data-defined threshold, clamped by Active Stone Level (AT-TREE-ADVANCE-1-2 / AT-ESCALATING-COST-CONFIG).
    //
    // net48 audit: FileStream(.Flush(true)), BinaryReader/Writer, SHA256, Encoding.UTF8, engine-free
    // domain types only. Link-compiles into the net8 test project.

    /// <summary>Server-owned Responsibility-Range policy for BP credit/spend (data-model.md §"Credit and
    /// spend BP": "within the Governor's Responsibility Range"). Given the acting Bond's authored range +
    /// role and the target Committed Tree, confirms the Governor may develop/credit that Tree. Kept as a
    /// seam so the handlers stay engine-free; a null policy is rejected at construction (no permissive
    /// fallback). Shared by ActivityCommands (credit) and DevelopmentCommands (spend).</summary>
    public interface IResponsibilityRangePolicy
    {
        /// <summary>True when a Bond carrying <paramref name="responsibilityRange"/> and
        /// <paramref name="ownerGovernorRole"/> is authorized to develop/credit
        /// <paramref name="tree"/> on <paramref name="stoneId"/>.</summary>
        bool CoversTree(StoneId stoneId, string responsibilityRange, string ownerGovernorRole, VersionedId tree);
    }

    public enum DevelopmentCommandOutcome
    {
        Applied,
        Replayed,
        Rejected
    }

    /// <summary>Result of an ApplyBPToNode command. On rejection nothing was journaled or committed.</summary>
    public readonly struct DevelopmentCommandResult
    {
        public DevelopmentCommandResult(DevelopmentCommandOutcome outcome, string resultCode,
            string receiptId, int bpSpent, int resultingBp, bool nodeCompleted,
            int treeLevel, bool treeLevelAdvanced, int cumulativeBpInvested,
            long stoneRevision, long characterRevision)
        {
            Outcome = outcome;
            ResultCode = resultCode;
            ReceiptId = receiptId;
            BpSpent = bpSpent;
            ResultingBp = resultingBp;
            NodeCompleted = nodeCompleted;
            TreeLevel = treeLevel;
            TreeLevelAdvanced = treeLevelAdvanced;
            CumulativeBpInvested = cumulativeBpInvested;
            StoneRevision = stoneRevision;
            CharacterRevision = characterRevision;
        }

        public DevelopmentCommandOutcome Outcome { get; }
        public string ResultCode { get; }
        public string ReceiptId { get; }
        public int BpSpent { get; }
        public int ResultingBp { get; }
        public bool NodeCompleted { get; }
        public int TreeLevel { get; }
        public bool TreeLevelAdvanced { get; }
        public int CumulativeBpInvested { get; }
        public long StoneRevision { get; }
        public long CharacterRevision { get; }
    }

    /// <summary>An ApplyBPToNode command envelope (contracts.md payload: treeId/version, nodeId/version,
    /// BP amount). The transport attaches the server-observed <see cref="Connection"/>;
    /// <see cref="Claim"/> is untrusted payload compared but never trusted.</summary>
    public readonly struct ApplyBpToNodeCommand
    {
        public ApplyBpToNodeCommand(
            OperationId operationId,
            StoneId stoneId,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            VersionedId tree,
            VersionedId node,
            int bpAmount,
            int tuningVersion,
            long? expectedStoneRevision = null,
            long? expectedCharacterRevision = null)
        {
            OperationId = operationId;
            StoneId = stoneId;
            Connection = connection;
            Claim = claim;
            Tree = tree;
            Node = node;
            BpAmount = bpAmount;
            TuningVersion = tuningVersion;
            ExpectedStoneRevision = expectedStoneRevision;
            ExpectedCharacterRevision = expectedCharacterRevision;
        }

        public OperationId OperationId { get; }
        public StoneId StoneId { get; }
        public AuthenticatedConnection Connection { get; }
        public ClaimedPrincipal Claim { get; }
        public VersionedId Tree { get; }
        public VersionedId Node { get; }
        public int BpAmount { get; }
        public int TuningVersion { get; }
        public long? ExpectedStoneRevision { get; }
        public long? ExpectedCharacterRevision { get; }
    }

    public sealed class DevelopmentCommandHandler
    {
        private readonly string _journalPath;
        private readonly PrincipalResolver _resolver;
        private readonly IStoneAggregateStore _stoneStore;
        private readonly ICharacterAggregateStore _characterStore;
        private readonly IAccountStoneAuthorityStore _authorityStore;
        private readonly IResponsibilityRangePolicy _rangePolicy;
        private readonly HomesteadProgressionCatalog _catalog;
        private readonly TreeTuningCatalog _tuning;

        public DevelopmentCommandHandler(
            string journalPath,
            PrincipalResolver resolver,
            IStoneAggregateStore stoneStore,
            ICharacterAggregateStore characterStore,
            IAccountStoneAuthorityStore authorityStore,
            IResponsibilityRangePolicy rangePolicy,
            HomesteadProgressionCatalog? catalog = null,
            TreeTuningCatalog? tuning = null)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _stoneStore = stoneStore ?? throw new ArgumentNullException(nameof(stoneStore));
            _characterStore = characterStore ?? throw new ArgumentNullException(nameof(characterStore));
            _authorityStore = authorityStore ?? throw new ArgumentNullException(nameof(authorityStore));
            _rangePolicy = rangePolicy ?? throw new ArgumentNullException(nameof(rangePolicy));
            _catalog = catalog ?? new HomesteadProgressionCatalog();
            _tuning = tuning ?? TreeTuningCatalog.Current;

            RehydrateFromJournal();
        }

        public string JournalPath => _journalPath;

        public DevelopmentCommandResult Handle(ApplyBpToNodeCommand command)
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
                command.Tree.ToString(),
                command.Node.ToString()
            }));

            string payloadDigest = Digest(string.Join("|", new[]
            {
                command.BpAmount.ToString(CultureInfo.InvariantCulture),
                command.TuningVersion.ToString(CultureInfo.InvariantCulture),
                command.ExpectedStoneRevision?.ToString(CultureInfo.InvariantCulture) ?? "-",
                command.ExpectedCharacterRevision?.ToString(CultureInfo.InvariantCulture) ?? "-"
            }));

            var existing = FindCommitted(opId);
            if (existing != null)
            {
                if (!string.Equals(existing.BindingDigest, bindingDigest, StringComparison.Ordinal)
                    || !string.Equals(existing.PayloadDigest, payloadDigest, StringComparison.Ordinal))
                    return Reject("OperationConflict");
                ApplyProjections(opId, existing);
                return Replayed(existing);
            }

            if (HasConflictingPartialIntent(opId, bindingDigest, payloadDigest))
                return Reject("OperationConflict");

            // Tuning version drift guard (a stale tuningVersion is an out-of-build reference).
            if (command.TuningVersion != _tuning.TuningVersion)
                return Reject("ContentVersionMismatch");

            var stone = _stoneStore.GetStone(command.StoneId);
            if (stone == null)
                return Reject("StoneNotFound");
            var character = _characterStore.GetCharacter(principal.Account, principal.Character);
            if (character == null)
                return Reject("CharacterNotFound");
            var authority = _authorityStore.GetAuthority(principal.Account, command.StoneId);

            // Governor authority + Responsibility Range over the target Tree.
            var bond = FindActiveBond(character, authority, command.StoneId);
            if (bond == null)
                return Reject("Unauthorized");
            if (!_rangePolicy.CoversTree(command.StoneId, bond.ResponsibilityRange, bond.OwnerGovernorRole, command.Tree))
                return Reject("OutsideResponsibilityRange");

            var tuning = _tuning.TryGetTuning(command.Tree.Key);
            if (tuning == null)
                return Reject("ContentVersionMismatch");

            // Run the Stone-side node/Tree transition FIRST (validates commitment, developability,
            // levels, revision). Only if it accepts do we debit BP — so a rejected development changes
            // nothing durable, and BP is never debited for an invalid node.
            var stoneTransition = TreeDevelopment.ApplyBpToNode(stone, _catalog, tuning,
                command.Tree, command.Node, command.BpAmount, opId, command.ExpectedStoneRevision);
            if (!stoneTransition.Accepted)
                return Reject(stoneTransition.ResultCode);

            // Debit the acting character's one Stone-wide personal BP balance (never-negative invariant).
            var charTransition = BondPower.Debit(character, command.StoneId, command.BpAmount,
                command.ExpectedCharacterRevision);
            if (!charTransition.Accepted)
                return Reject(charTransition.ResultCode);

            var newCommitted = FindCommittedTree(stoneTransition.NextStone, command.Tree);

            var record = new CommittedDevelopment
            {
                OperationId = opId,
                BindingDigest = bindingDigest,
                PayloadDigest = payloadDigest,
                AccountId = principal.Account.Value,
                CharacterId = principal.Character.Value,
                StoneId = command.StoneId.Value,
                ResultCode = "Applied",
                BpSpent = command.BpAmount,
                ResultingBp = charTransition.ResultingBp,
                NodeCompleted = stoneTransition.NodeCompleted,
                TreeLevel = stoneTransition.NewTreeLevel,
                TreeLevelAdvanced = stoneTransition.TreeLevelAdvanced,
                CumulativeBpInvested = newCommitted?.CumulativeBpInvested ?? 0,
                StoneRevision = stoneTransition.NextStone.Revision,
                CharacterRevision = charTransition.Character.Revision,
                StoneSnapshot = stoneTransition.NextStone.Serialize(),
                CharacterSnapshot = charTransition.Character.Serialize()
            };

            Append(Record(DevelopmentBoundary.IntentJournaled, record));
            Append(Record(DevelopmentBoundary.Committed, record));

            ApplyProjections(opId, record);

            return new DevelopmentCommandResult(DevelopmentCommandOutcome.Applied, "Applied",
                Receipt(opId), record.BpSpent, record.ResultingBp, record.NodeCompleted,
                record.TreeLevel, record.TreeLevelAdvanced, record.CumulativeBpInvested,
                record.StoneRevision, record.CharacterRevision);
        }

        private static CommittedTreeRecord? FindCommittedTree(StoneProgressionAggregate stone, VersionedId tree)
        {
            foreach (var c in stone.CommittedTrees)
                if (string.Equals(c.Tree.Key, tree.Key, StringComparison.Ordinal))
                    return c;
            return null;
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

        private DevelopmentCommandResult Replayed(CommittedDevelopment r) =>
            new DevelopmentCommandResult(DevelopmentCommandOutcome.Replayed, r.ResultCode,
                Receipt(r.OperationId), r.BpSpent, r.ResultingBp, r.NodeCompleted, r.TreeLevel,
                r.TreeLevelAdvanced, r.CumulativeBpInvested, r.StoneRevision, r.CharacterRevision);

        private void ApplyProjections(string operationId, CommittedDevelopment record)
        {
            _stoneStore.ApplyStoneProjection(operationId,
                StoneProgressionAggregate.Deserialize(record.StoneSnapshot));
            _characterStore.ApplyCharacterProjection(operationId,
                CharacterProgressionAggregate.Deserialize(record.CharacterSnapshot));
        }

        private static DevelopmentCommandResult Reject(string code) =>
            new DevelopmentCommandResult(DevelopmentCommandOutcome.Rejected, code, string.Empty,
                0, 0, false, 0, false, 0, 0, 0);

        private static string Receipt(string opId) => Digest("developreceipt|" + opId);

        // ---- Journal ----

        private enum DevelopmentBoundary
        {
            IntentJournaled = 1,
            Committed = 2
        }

        private sealed class CommittedDevelopment
        {
            public string OperationId = string.Empty;
            public string BindingDigest = string.Empty;
            public string PayloadDigest = string.Empty;
            public string AccountId = string.Empty;
            public string CharacterId = string.Empty;
            public string StoneId = string.Empty;
            public string ResultCode = string.Empty;
            public int BpSpent;
            public int ResultingBp;
            public bool NodeCompleted;
            public int TreeLevel;
            public bool TreeLevelAdvanced;
            public int CumulativeBpInvested;
            public long StoneRevision;
            public long CharacterRevision;
            public string StoneSnapshot = string.Empty;
            public string CharacterSnapshot = string.Empty;
        }

        private CommittedDevelopment? FindCommitted(string operationId)
        {
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null || rec.Value.OperationId != operationId) continue;
                if (rec.Value.Boundary == DevelopmentBoundary.Committed)
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
                if (rec.Value.Boundary != DevelopmentBoundary.IntentJournaled) continue;
                if (!string.Equals(rec.Value.Record.BindingDigest, bindingDigest, StringComparison.Ordinal)
                    || !string.Equals(rec.Value.Record.PayloadDigest, payloadDigest, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private void RehydrateFromJournal()
        {
            var committedByOp = new Dictionary<string, CommittedDevelopment>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null) continue;
                if (rec.Value.Boundary != DevelopmentBoundary.Committed) continue;
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
            public DevelopmentBoundary Boundary;
            public CommittedDevelopment Record;
        }

        private static string Record(DevelopmentBoundary boundary, CommittedDevelopment r)
        {
            return string.Join("|", new[]
            {
                "DEVELOPREC",
                r.OperationId,
                ((int)boundary).ToString(CultureInfo.InvariantCulture),
                r.BindingDigest,
                r.PayloadDigest,
                Encode(r.AccountId),
                Encode(r.CharacterId),
                Encode(r.StoneId),
                r.ResultCode,
                r.BpSpent.ToString(CultureInfo.InvariantCulture),
                r.ResultingBp.ToString(CultureInfo.InvariantCulture),
                r.NodeCompleted ? "1" : "0",
                r.TreeLevel.ToString(CultureInfo.InvariantCulture),
                r.TreeLevelAdvanced ? "1" : "0",
                r.CumulativeBpInvested.ToString(CultureInfo.InvariantCulture),
                r.StoneRevision.ToString(CultureInfo.InvariantCulture),
                r.CharacterRevision.ToString(CultureInfo.InvariantCulture),
                Encode(r.StoneSnapshot),
                Encode(r.CharacterSnapshot)
            });
        }

        private static ParsedRecord? ParseRecord(string line)
        {
            var parts = line.Split('|');
            if (parts.Length != 19 || parts[0] != "DEVELOPREC") return null;
            var rec = new CommittedDevelopment
            {
                OperationId = parts[1],
                BindingDigest = parts[3],
                PayloadDigest = parts[4],
                AccountId = Decode(parts[5]),
                CharacterId = Decode(parts[6]),
                StoneId = Decode(parts[7]),
                ResultCode = parts[8],
                BpSpent = int.Parse(parts[9], CultureInfo.InvariantCulture),
                ResultingBp = int.Parse(parts[10], CultureInfo.InvariantCulture),
                NodeCompleted = parts[11] == "1",
                TreeLevel = int.Parse(parts[12], CultureInfo.InvariantCulture),
                TreeLevelAdvanced = parts[13] == "1",
                CumulativeBpInvested = int.Parse(parts[14], CultureInfo.InvariantCulture),
                StoneRevision = long.Parse(parts[15], CultureInfo.InvariantCulture),
                CharacterRevision = long.Parse(parts[16], CultureInfo.InvariantCulture),
                StoneSnapshot = Decode(parts[17]),
                CharacterSnapshot = Decode(parts[18])
            };
            return new ParsedRecord
            {
                OperationId = parts[1],
                Boundary = (DevelopmentBoundary)int.Parse(parts[2], CultureInfo.InvariantCulture),
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
