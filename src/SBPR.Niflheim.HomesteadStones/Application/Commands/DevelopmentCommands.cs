using System;
using System.Collections.Generic;
using System.Globalization;
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
    // §"Credit and spend BP on node development"). This is the CROSS-AGGREGATE mutation authority: one
    // accepted development simultaneously
    //   * DEBITS the acting Governor's one Stone-wide personal BP balance (character aggregate), and
    //   * ADVANCES the node's development plus the SAME delta of cumulative owning-Tree investment, and
    //     — when the configured threshold crosses and Active Stone Level permits — the Tree Level
    //     (Stone aggregate),
    // under ONE durable, replayable receipt (data-model.md: "in one mutation").
    //
    // Recovery model mirrors RelationshipCommands: an append-only, per-boundary-fsync'd journal IS the
    // transaction. The character AND Stone stores are idempotent projections of the journal, so a crash
    // between the two aggregate writes cannot leave a partial result — recovery re-derives BOTH from the
    // one committed record. Re-submitting the same operationId returns the recorded terminal result
    // (Replayed); a conflicting binding/payload under a committed op rejects OperationConflict.
    //
    // Authority (contracts.md): the acting character must hold an ACTIVE Bond, the node's owning Tree
    // must be committed, and the Governor's authored Responsibility Range must cover it. There is no
    // direct Tree-level wallet/command/meter (AT-NO-DIRECT-LEVEL-METER): Tree Level ONLY moves via the
    // cumulative-investment threshold inside the pure TreeDevelopment transition.
    //
    // net48 audit: engine-free domain types only. Since ADO #128 the durable framing
    // primitives (FileStream/.Flush(true), BinaryReader/Writer, SHA256, Encoding.UTF8) live in
    // CommandJournalFraming rather than in this file; this handler still owns its own journal
    // FILE.
    // Link-compiles into the net8 test project.

    public enum DevelopmentCommandOutcome
    {
        Applied,
        Replayed,
        Rejected
    }

    /// <summary>Result of an ApplyBPToNode command. On rejection nothing was journaled/committed.</summary>
    public readonly struct DevelopmentCommandResult
    {
        public DevelopmentCommandResult(DevelopmentCommandOutcome outcome, string resultCode,
            string receiptId, int bpDebited, int newBpBalance, bool nodeCompleted, bool nodeOffered,
            int newTreeLevel, bool treeLevelAdvanced, long stoneRevision, long characterRevision)
        {
            Outcome = outcome;
            ResultCode = resultCode;
            ReceiptId = receiptId;
            BpDebited = bpDebited;
            NewBpBalance = newBpBalance;
            NodeCompleted = nodeCompleted;
            NodeOffered = nodeOffered;
            NewTreeLevel = newTreeLevel;
            TreeLevelAdvanced = treeLevelAdvanced;
            StoneRevision = stoneRevision;
            CharacterRevision = characterRevision;
        }

        public DevelopmentCommandOutcome Outcome { get; }
        public string ResultCode { get; }
        public string ReceiptId { get; }
        public int BpDebited { get; }
        public int NewBpBalance { get; }
        public bool NodeCompleted { get; }
        public bool NodeOffered { get; }
        public int NewTreeLevel { get; }
        public bool TreeLevelAdvanced { get; }
        public long StoneRevision { get; }
        public long CharacterRevision { get; }
    }

    /// <summary>An ApplyBPToNode command envelope (contracts.md payload: treeId/version, nodeId/version,
    /// BP amount). The transport attaches the server-observed <see cref="Connection"/>;
    /// <see cref="Claim"/> is untrusted payload compared but never trusted.</summary>
    public readonly struct ApplyBPToNodeCommand
    {
        public ApplyBPToNodeCommand(
            OperationId operationId,
            StoneId stoneId,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            string treeId,
            int treeVersion,
            string nodeId,
            int nodeVersion,
            int bpAmount,
            long? expectedStoneRevision = null,
            long? expectedCharacterRevision = null)
        {
            OperationId = operationId;
            StoneId = stoneId;
            Connection = connection;
            Claim = claim;
            TreeId = treeId ?? string.Empty;
            TreeVersion = treeVersion;
            NodeId = nodeId ?? string.Empty;
            NodeVersion = nodeVersion;
            BpAmount = bpAmount;
            ExpectedStoneRevision = expectedStoneRevision;
            ExpectedCharacterRevision = expectedCharacterRevision;
        }

        public OperationId OperationId { get; }
        public StoneId StoneId { get; }
        public AuthenticatedConnection Connection { get; }
        public ClaimedPrincipal Claim { get; }
        public string TreeId { get; }
        public int TreeVersion { get; }
        public string NodeId { get; }
        public int NodeVersion { get; }
        public int BpAmount { get; }
        public long? ExpectedStoneRevision { get; }
        public long? ExpectedCharacterRevision { get; }

        public VersionedId Tree => new VersionedId(TreeId, TreeVersion);
        public VersionedId Node => new VersionedId(NodeId, NodeVersion);
    }

    public sealed class DevelopmentCommandHandler
    {
        private readonly string _journalPath;
        private readonly PrincipalResolver _resolver;
        private readonly IStoneAggregateStore _stoneStore;
        private readonly ICharacterAggregateStore _characterStore;
        private readonly IAccountStoneAuthorityStore _authorityStore;
        private readonly IGovernorDevelopmentAuthority _developmentAuthority;
        private readonly HomesteadProgressionCatalog _catalog;
        private readonly TreeDevelopmentConfig _config;

        public DevelopmentCommandHandler(
            string journalPath,
            PrincipalResolver resolver,
            IStoneAggregateStore stoneStore,
            ICharacterAggregateStore characterStore,
            IAccountStoneAuthorityStore authorityStore,
            IGovernorDevelopmentAuthority developmentAuthority,
            HomesteadProgressionCatalog? catalog = null,
            TreeDevelopmentConfig? config = null)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _stoneStore = stoneStore ?? throw new ArgumentNullException(nameof(stoneStore));
            _characterStore = characterStore ?? throw new ArgumentNullException(nameof(characterStore));
            _authorityStore = authorityStore ?? throw new ArgumentNullException(nameof(authorityStore));
            _developmentAuthority = developmentAuthority ?? throw new ArgumentNullException(nameof(developmentAuthority));
            _catalog = catalog ?? new HomesteadProgressionCatalog();
            _config = config ?? TreeDevelopmentConfig.Default;

            RehydrateFromJournal();
        }

        public string JournalPath => _journalPath;

        public DevelopmentCommandResult Handle(ApplyBPToNodeCommand command)
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
                command.Tree.Serialize(),
                command.Node.Serialize()
            }));

            string payloadDigest = Digest(string.Join("|", new[]
            {
                command.BpAmount.ToString(CultureInfo.InvariantCulture),
                command.ExpectedStoneRevision?.ToString(CultureInfo.InvariantCulture) ?? "-",
                command.ExpectedCharacterRevision?.ToString(CultureInfo.InvariantCulture) ?? "-"
            }));

            var existing = FindCommitted(opId);
            if (existing != null)
            {
                if (!string.Equals(existing.BindingDigest, bindingDigest, StringComparison.Ordinal)
                    || !string.Equals(existing.PayloadDigest, payloadDigest, StringComparison.Ordinal))
                    return Reject("OperationConflict");
                ApplyProjection(opId, existing);
                return Terminal(DevelopmentCommandOutcome.Replayed, existing);
            }

            if (HasConflictingPartialIntent(opId, bindingDigest, payloadDigest))
                return Reject("OperationConflict");

            var stone = _stoneStore.GetStone(command.StoneId);
            if (stone == null)
                return Reject("StoneNotFound");
            var character = _characterStore.GetCharacter(principal.Account, principal.Character);
            if (character == null)
                return Reject("CharacterNotFound");
            var authority = _authorityStore.GetAuthority(principal.Account, command.StoneId);

            // Authority: active Bond, then Responsibility Range over the owning Tree.
            var bond = FindActiveBond(character, authority, command.StoneId);
            if (bond == null)
                return Reject("Unauthorized");
            if (!_developmentAuthority.CanDevelop(command.StoneId, bond.ResponsibilityRange,
                    bond.OwnerGovernorRole, command.Tree))
                return Reject("OutsideResponsibilityRange");

            // Optimistic concurrency on BOTH aggregates before any mutation (CAS).
            if (command.ExpectedStoneRevision.HasValue
                && command.ExpectedStoneRevision.Value != stone.Revision)
                return Reject("StaleStoneRevision");
            if (command.ExpectedCharacterRevision.HasValue
                && command.ExpectedCharacterRevision.Value != character.Revision)
                return Reject("StaleCharacterRevision");

            // Pure Stone-side node development + Tree advancement (validates node/level/cost/delta).
            var stoneTransition = TreeDevelopment.ApplyBPToNode(stone, _catalog, _config,
                command.Tree, command.Node, command.BpAmount, opId);
            if (!stoneTransition.Accepted)
                return Reject(MapNodeResult(stoneTransition.Result));

            // Pure character-side BP debit (validates the non-negative invariant). Only after the Stone
            // side accepted, so the character's balance is exactly the developable amount.
            var bpTransition = BondPower.Debit(character, command.StoneId, command.BpAmount);
            if (!bpTransition.Accepted)
                return Reject(MapBpResult(bpTransition.Result));

            var record = new CommittedDevelopment
            {
                OperationId = opId,
                BindingDigest = bindingDigest,
                PayloadDigest = payloadDigest,
                AccountId = principal.Account.Value,
                CharacterId = principal.Character.Value,
                StoneId = command.StoneId.Value,
                ResultCode = "Applied",
                BpDebited = command.BpAmount,
                NewBpBalance = bpTransition.NewBalance,
                NodeCompleted = stoneTransition.NodeCompleted,
                NodeOffered = stoneTransition.NodeOffered,
                NewTreeLevel = stoneTransition.NewTreeLevel,
                TreeLevelAdvanced = stoneTransition.TreeLevelAdvanced,
                StoneRevision = stoneTransition.NextStone.Revision,
                CharacterRevision = bpTransition.NextCharacter.Revision,
                StoneSnapshot = stoneTransition.NextStone.Serialize(),
                CharacterSnapshot = bpTransition.NextCharacter.Serialize()
            };

            Append(Record(DevelopmentBoundary.IntentJournaled, record));
            Append(Record(DevelopmentBoundary.Committed, record));

            ApplyProjection(opId, record);

            return Terminal(DevelopmentCommandOutcome.Applied, record);
        }

        private static DevelopmentCommandResult Terminal(DevelopmentCommandOutcome outcome, CommittedDevelopment r) =>
            new DevelopmentCommandResult(outcome, r.ResultCode, Receipt(r.OperationId), r.BpDebited,
                r.NewBpBalance, r.NodeCompleted, r.NodeOffered, r.NewTreeLevel, r.TreeLevelAdvanced,
                r.StoneRevision, r.CharacterRevision);

        private static string MapNodeResult(NodeDevelopmentResult r)
        {
            switch (r)
            {
                case NodeDevelopmentResult.StaleStoneRevision: return "StaleStoneRevision";
                case NodeDevelopmentResult.NodeNotFound: return "NodeNotFound";
                case NodeDevelopmentResult.ContentVersionMismatch: return "ContentVersionMismatch";
                case NodeDevelopmentResult.TreeMismatch: return "TreeMismatch";
                case NodeDevelopmentResult.NodeUnavailable: return "NodeUnavailable";
                case NodeDevelopmentResult.TreeNotCommitted: return "TreeNotCommitted";
                case NodeDevelopmentResult.TreeLevelTooLow: return "TreeLevelTooLow";
                case NodeDevelopmentResult.ActiveStoneLevelTooLow: return "ActiveStoneLevelTooLow";
                case NodeDevelopmentResult.AlreadyDeveloped: return "AlreadyDeveloped";
                case NodeDevelopmentResult.BpDeltaInvalid: return "BpDeltaInvalid";
                default: return "Rejected";
            }
        }

        private static string MapBpResult(BondPowerResult r)
        {
            switch (r)
            {
                case BondPowerResult.InsufficientBp: return "InsufficientBp";
                case BondPowerResult.NonPositiveAmount: return "BpDeltaInvalid";
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

        private void ApplyProjection(string operationId, CommittedDevelopment record)
        {
            _stoneStore.ApplyStoneProjection(operationId,
                StoneProgressionAggregate.Deserialize(record.StoneSnapshot));
            _characterStore.ApplyCharacterProjection(operationId,
                CharacterProgressionAggregate.Deserialize(record.CharacterSnapshot));
        }

        private static DevelopmentCommandResult Reject(string code) =>
            new DevelopmentCommandResult(DevelopmentCommandOutcome.Rejected, code, string.Empty,
                0, 0, false, false, 0, false, 0, 0);

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
            public int BpDebited;
            public int NewBpBalance;
            public bool NodeCompleted;
            public bool NodeOffered;
            public int NewTreeLevel;
            public bool TreeLevelAdvanced;
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
                ApplyProjection(opId, committedByOp[opId]);
        }

        private struct ParsedRecord
        {
            public string OperationId;
            public DevelopmentBoundary Boundary;
            public CommittedDevelopment Record;
        }

        private static string Record(DevelopmentBoundary boundary, CommittedDevelopment r)
        {
            // Delimiter-safe framing invariant (ADO #127, mirroring RelationshipCommands.cs / PR #351):
            // the record is pipe-delimited, so EVERY free-text field is base64-encoded before it enters
            // the frame — never written raw. The OperationId in particular is a caller-composed value
            // that legitimately embeds '|' (a StoneId is "world|zoneX|zoneZ" by construction, e.g.
            // "uid:-898655635|3|2"); writing it unencoded exploded a 19-field record into more and the
            // strict parser rejected EVERY frame — and the journal IS the save. Encoding it (and the
            // ResultCode) here, and decoding symmetrically in ParseRecord, keeps the field count
            // exactly 19 for ANY operation id. Digest fields are hex, boolean fields are "0"/"1" and
            // integer fields are numeric, so none can contain '|' — they stay raw.
            return string.Join("|", new[]
            {
                "DEVELOPREC",
                Encode(r.OperationId),
                ((int)boundary).ToString(CultureInfo.InvariantCulture),
                r.BindingDigest,
                r.PayloadDigest,
                Encode(r.AccountId),
                Encode(r.CharacterId),
                Encode(r.StoneId),
                Encode(r.ResultCode),
                r.BpDebited.ToString(CultureInfo.InvariantCulture),
                r.NewBpBalance.ToString(CultureInfo.InvariantCulture),
                r.NodeCompleted ? "1" : "0",
                r.NodeOffered ? "1" : "0",
                r.NewTreeLevel.ToString(CultureInfo.InvariantCulture),
                r.TreeLevelAdvanced ? "1" : "0",
                r.StoneRevision.ToString(CultureInfo.InvariantCulture),
                r.CharacterRevision.ToString(CultureInfo.InvariantCulture),
                Encode(r.StoneSnapshot),
                Encode(r.CharacterSnapshot)
            });
        }

        private static ParsedRecord? ParseRecord(string line)
        {
            var parts = line.Split('|');
            // Delimiter-safe framing (ADO #127): every free-text field is base64-encoded on write, so no
            // raw '|' can appear inside a field and the field count is a reliable structural check. A
            // torn or malformed frame is rejected honestly as null — never partially applied.
            if (parts.Length != 19 || parts[0] != "DEVELOPREC") return null;
            try
            {
                string operationId = Decode(parts[1]);
                var rec = new CommittedDevelopment
                {
                    OperationId = operationId,
                    BindingDigest = parts[3],
                    PayloadDigest = parts[4],
                    AccountId = Decode(parts[5]),
                    CharacterId = Decode(parts[6]),
                    StoneId = Decode(parts[7]),
                    ResultCode = Decode(parts[8]),
                    BpDebited = int.Parse(parts[9], CultureInfo.InvariantCulture),
                    NewBpBalance = int.Parse(parts[10], CultureInfo.InvariantCulture),
                    NodeCompleted = parts[11] == "1",
                    NodeOffered = parts[12] == "1",
                    NewTreeLevel = int.Parse(parts[13], CultureInfo.InvariantCulture),
                    TreeLevelAdvanced = parts[14] == "1",
                    StoneRevision = long.Parse(parts[15], CultureInfo.InvariantCulture),
                    CharacterRevision = long.Parse(parts[16], CultureInfo.InvariantCulture),
                    StoneSnapshot = Decode(parts[17]),
                    CharacterSnapshot = Decode(parts[18])
                };
                return new ParsedRecord
                {
                    OperationId = operationId,
                    Boundary = (DevelopmentBoundary)int.Parse(parts[2], CultureInfo.InvariantCulture),
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

        // ---- Durable framing (ADO #128) ----
        //
        // The frame format (length prefix, CRC32, fsync'd append, truncate-at-first-damage
        // read) now lives in CommandJournalFraming, shared with the other five progression
        // handlers. It was extracted from six byte-for-byte identical private copies, proven
        // byte-identical by NiflheimCommandJournalFramingOracleTests before any handler moved.
        //
        // THIS HANDLER STILL OWNS ITS OWN JOURNAL FILE (_journalPath). Shared code,
        // INDEPENDENT durable state — a defect in another handler's journal cannot reach this
        // one's rehydration. Do not "simplify" this toward a shared file or shared stream.
        //
        // The record layout above (DEVELOPREC) deliberately stays here: it is domain-specific,
        // and the ADO #127 delimiter-safety invariant is enforced at that layer via Encode.
        // The framing layer below is delimiter-agnostic.

        private void Append(string text) => CommandJournalFraming.Append(_journalPath, text);

        private List<string> ReadDurable() => CommandJournalFraming.ReadDurable(_journalPath);

        private static string Encode(string s) => CommandJournalFraming.Encode(s);

        private static string Decode(string s) => CommandJournalFraming.Decode(s);

        public static string Digest(string s) => CommandJournalFraming.Digest(s);
    }
}
