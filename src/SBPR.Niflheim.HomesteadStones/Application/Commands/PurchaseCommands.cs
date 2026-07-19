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
    // T013 — recoverable PurchaseNode command handler (contracts.md §"PurchaseNode"; data-model.md
    // §"Purchase personal node"). This is the CHARACTER-side mutation authority: one accepted purchase
    //   * DEBITS the caller's permitted balance once (Personal AP or matching Facet Credit), and
    //   * appends ONE purchase record carrying exact Offered-Set/version provenance,
    // under ONE durable, replayable receipt (data-model.md: "Debit the allowed balance exactly once").
    //
    // Recovery model mirrors ActivityCommands/DevelopmentCommands: an append-only, per-boundary-fsync'd
    // journal IS the transaction. The character store is an idempotent projection of the journal, so a
    // crash between intent and terminal cannot leave a partial purchase — recovery re-derives the
    // character from the one committed record. Re-submitting the same operationId returns the recorded
    // terminal result (Replayed); a conflicting binding/payload under a committed op rejects
    // OperationConflict with no mutation.
    //
    // Authority (contracts.md §"PurchaseNode"): the acting character must hold an ACTIVE ATTUNEMENT.
    // Bond alone is NOT purchase authority (spec US3: "Only an eligible actively Attuned character may
    // purchase an Offered personal node") — a bonded-but-unattuned caller is rejected RelationshipRequired.
    // All content/level/prior-Offered-Set/price gates live in the pure NodePurchases transition.
    //
    // Same-Tree Attunement Tier Access is DERIVED (NodePurchases.DeriveSameTreeTierAccess) from prior
    // same-Tree Offered purchases + Tree/Stone caps; it is never stored as Tier XP (spec FR-014).
    //
    // net48 audit: FileStream(.Flush(true)), BinaryReader/Writer, SHA256, Encoding.UTF8, engine-free
    // domain types only. Link-compiles into the net8 test project.

    public enum PurchaseCommandOutcome
    {
        Applied,
        Replayed,
        Rejected
    }

    /// <summary>Result of a PurchaseNode command. On rejection nothing was journaled/committed.</summary>
    public readonly struct PurchaseCommandResult
    {
        public PurchaseCommandResult(PurchaseCommandOutcome outcome, string resultCode, string receiptId,
            int apDebited, string paymentSource, string offeredSetKey, int offeredSetVersion,
            long characterRevision)
        {
            Outcome = outcome;
            ResultCode = resultCode;
            ReceiptId = receiptId;
            ApDebited = apDebited;
            PaymentSource = paymentSource;
            OfferedSetKey = offeredSetKey;
            OfferedSetVersion = offeredSetVersion;
            CharacterRevision = characterRevision;
        }

        public PurchaseCommandOutcome Outcome { get; }
        public string ResultCode { get; }
        public string ReceiptId { get; }
        public int ApDebited { get; }
        public string PaymentSource { get; }
        public string OfferedSetKey { get; }
        public int OfferedSetVersion { get; }
        public long CharacterRevision { get; }
    }

    /// <summary>A PurchaseNode command envelope (contracts.md payload: treeId/version, nodeId/version,
    /// expected OfferedSetId/version, payment source preference). The transport attaches the
    /// server-observed <see cref="Connection"/>; <see cref="Claim"/> is compared but never trusted.</summary>
    public readonly struct PurchaseNodeCommand
    {
        public PurchaseNodeCommand(
            OperationId operationId,
            StoneId stoneId,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            string treeId,
            int treeVersion,
            string nodeId,
            int nodeVersion,
            string expectedOfferedSetKey,
            int expectedOfferedSetVersion,
            PurchasePaymentSource paymentPreference,
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
            ExpectedOfferedSetKey = expectedOfferedSetKey ?? string.Empty;
            ExpectedOfferedSetVersion = expectedOfferedSetVersion;
            PaymentPreference = paymentPreference;
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
        public string ExpectedOfferedSetKey { get; }
        public int ExpectedOfferedSetVersion { get; }
        public PurchasePaymentSource PaymentPreference { get; }
        public long? ExpectedStoneRevision { get; }
        public long? ExpectedCharacterRevision { get; }

        public VersionedId Tree => new VersionedId(TreeId, TreeVersion);
        public VersionedId Node => new VersionedId(NodeId, NodeVersion);
        public VersionedId ExpectedOfferedSet =>
            string.IsNullOrEmpty(ExpectedOfferedSetKey)
                ? VersionedId.None
                : new VersionedId(ExpectedOfferedSetKey, ExpectedOfferedSetVersion);
    }

    public sealed class PurchaseCommandHandler
    {
        private readonly string _journalPath;
        private readonly PrincipalResolver _resolver;
        private readonly IStoneAggregateStore _stoneStore;
        private readonly ICharacterAggregateStore _characterStore;
        private readonly IAccountStoneAuthorityStore _authorityStore;
        private readonly HomesteadProgressionCatalog _catalog;

        public PurchaseCommandHandler(
            string journalPath,
            PrincipalResolver resolver,
            IStoneAggregateStore stoneStore,
            ICharacterAggregateStore characterStore,
            IAccountStoneAuthorityStore authorityStore,
            HomesteadProgressionCatalog? catalog = null)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _stoneStore = stoneStore ?? throw new ArgumentNullException(nameof(stoneStore));
            _characterStore = characterStore ?? throw new ArgumentNullException(nameof(characterStore));
            _authorityStore = authorityStore ?? throw new ArgumentNullException(nameof(authorityStore));
            _catalog = catalog ?? new HomesteadProgressionCatalog();

            RehydrateFromJournal();
        }

        public string JournalPath => _journalPath;

        public PurchaseCommandResult Handle(PurchaseNodeCommand command)
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
                command.ExpectedOfferedSet.Serialize(),
                ((int)command.PaymentPreference).ToString(CultureInfo.InvariantCulture),
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
                return Terminal(PurchaseCommandOutcome.Replayed, existing);
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

            // Authority: purchase requires an ACTIVE ATTUNEMENT. Bond alone is NOT purchase authority
            // (spec US3). A caller with no active reservation, or one whose active reservation is a Bond,
            // is rejected RelationshipRequired with zero mutation.
            if (!HasActiveAttunement(character, authority, command.StoneId))
                return Reject("RelationshipRequired");

            // Optimistic concurrency on BOTH aggregates before any mutation (CAS). Purchase mutates only
            // the character, but a stale Stone revision means the caller's Offered/level view is stale.
            if (command.ExpectedStoneRevision.HasValue
                && command.ExpectedStoneRevision.Value != stone.Revision)
                return Reject("StaleStoneRevision");
            if (command.ExpectedCharacterRevision.HasValue
                && command.ExpectedCharacterRevision.Value != character.Revision)
                return Reject("StaleCharacterRevision");

            // Pure purchase transition (validates content/Tree/Offered/level/prior-Offered-Set/price).
            var transition = NodePurchases.PurchaseNode(character, stone, _catalog,
                command.Tree, command.Node, command.ExpectedOfferedSet, command.PaymentPreference);
            if (!transition.Accepted)
                return Reject(MapPurchaseResult(transition.Result));

            var record = new CommittedPurchase
            {
                OperationId = opId,
                BindingDigest = bindingDigest,
                PayloadDigest = payloadDigest,
                AccountId = principal.Account.Value,
                CharacterId = principal.Character.Value,
                StoneId = command.StoneId.Value,
                ResultCode = "Applied",
                ApDebited = transition.ApDebited,
                PaymentSource = transition.PaymentSource == PurchasePaymentSource.PersonalAp
                    ? "PersonalAP" : "FacetCredit",
                OfferedSetKey = transition.OfferedSet.Key,
                OfferedSetVersion = transition.OfferedSet.Version,
                CharacterRevision = transition.NextCharacter.Revision,
                CharacterSnapshot = transition.NextCharacter.Serialize()
            };

            Append(Record(PurchaseBoundary.IntentJournaled, record));
            Append(Record(PurchaseBoundary.Committed, record));

            ApplyProjection(opId, record);

            return Terminal(PurchaseCommandOutcome.Applied, record);
        }

        private static PurchaseCommandResult Terminal(PurchaseCommandOutcome outcome, CommittedPurchase r) =>
            new PurchaseCommandResult(outcome, r.ResultCode, Receipt(r.OperationId), r.ApDebited,
                r.PaymentSource, r.OfferedSetKey, r.OfferedSetVersion, r.CharacterRevision);

        private static string MapPurchaseResult(NodePurchaseResult r)
        {
            switch (r)
            {
                case NodePurchaseResult.NodeNotFound: return "NodeNotFound";
                case NodePurchaseResult.ContentVersionMismatch: return "ContentVersionMismatch";
                case NodePurchaseResult.TreeMismatch: return "TreeMismatch";
                case NodePurchaseResult.NodeNotOffered: return "NodeNotOffered";
                case NodePurchaseResult.TreeNotCommitted: return "TreeNotCommitted";
                case NodePurchaseResult.TreeLevelTooLow: return "TreeLevelTooLow";
                case NodePurchaseResult.ActiveStoneLevelTooLow: return "ActiveStoneLevelTooLow";
                case NodePurchaseResult.PriorOfferedSetIncomplete: return "PriorOfferedSetIncomplete";
                case NodePurchaseResult.AlreadyAcquired: return "AlreadyAcquired";
                case NodePurchaseResult.InsufficientPersonalAP: return "InsufficientPersonalAP";
                case NodePurchaseResult.InsufficientFacetCredit: return "InsufficientFacetCredit";
                default: return "Rejected";
            }
        }

        /// <summary>The acting character holds an ACTIVE Attunement at this Stone. The reservation index
        /// is the one gate; the character-owned relationship record is the source of truth for kind.
        /// Bond reservations return false — Bond is not purchase authority.</summary>
        private static bool HasActiveAttunement(
            CharacterProgressionAggregate character, AccountStoneAuthorityIndex authority, StoneId stoneId)
        {
            var reservation = authority.ReservationFor(character.Character);
            if (reservation == null || reservation.Kind != RelationshipKind.Attunement)
                return false;

            foreach (var sr in character.StoneRecords)
            {
                if (!sr.StoneId.Equals(stoneId)) continue;
                foreach (var rel in sr.Relationships)
                {
                    if (rel.IsActive && rel.Kind == RelationshipKind.Attunement
                        && string.Equals(rel.RelationshipId, reservation.RelationshipId, StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }

        private void ApplyProjection(string operationId, CommittedPurchase record)
        {
            _characterStore.ApplyCharacterProjection(operationId,
                CharacterProgressionAggregate.Deserialize(record.CharacterSnapshot));
        }

        private static PurchaseCommandResult Reject(string code) =>
            new PurchaseCommandResult(PurchaseCommandOutcome.Rejected, code, string.Empty,
                0, string.Empty, string.Empty, 0, 0);

        private static string Receipt(string opId) => Digest("purchasereceipt|" + opId);

        // ---- Journal ----

        private enum PurchaseBoundary
        {
            IntentJournaled = 1,
            Committed = 2
        }

        private sealed class CommittedPurchase
        {
            public string OperationId = string.Empty;
            public string BindingDigest = string.Empty;
            public string PayloadDigest = string.Empty;
            public string AccountId = string.Empty;
            public string CharacterId = string.Empty;
            public string StoneId = string.Empty;
            public string ResultCode = string.Empty;
            public int ApDebited;
            public string PaymentSource = string.Empty;
            public string OfferedSetKey = string.Empty;
            public int OfferedSetVersion;
            public long CharacterRevision;
            public string CharacterSnapshot = string.Empty;
        }

        private CommittedPurchase? FindCommitted(string operationId)
        {
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null || rec.Value.OperationId != operationId) continue;
                if (rec.Value.Boundary == PurchaseBoundary.Committed)
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
                if (rec.Value.Boundary != PurchaseBoundary.IntentJournaled) continue;
                if (!string.Equals(rec.Value.Record.BindingDigest, bindingDigest, StringComparison.Ordinal)
                    || !string.Equals(rec.Value.Record.PayloadDigest, payloadDigest, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private void RehydrateFromJournal()
        {
            var committedByOp = new Dictionary<string, CommittedPurchase>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null) continue;
                if (rec.Value.Boundary != PurchaseBoundary.Committed) continue;
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
            public PurchaseBoundary Boundary;
            public CommittedPurchase Record;
        }

        private static string Record(PurchaseBoundary boundary, CommittedPurchase r)
        {
            return string.Join("|", new[]
            {
                "PURCHASEREC",
                r.OperationId,
                ((int)boundary).ToString(CultureInfo.InvariantCulture),
                r.BindingDigest,
                r.PayloadDigest,
                Encode(r.AccountId),
                Encode(r.CharacterId),
                Encode(r.StoneId),
                r.ResultCode,
                r.ApDebited.ToString(CultureInfo.InvariantCulture),
                Encode(r.PaymentSource),
                Encode(r.OfferedSetKey),
                r.OfferedSetVersion.ToString(CultureInfo.InvariantCulture),
                r.CharacterRevision.ToString(CultureInfo.InvariantCulture),
                Encode(r.CharacterSnapshot)
            });
        }

        private static ParsedRecord? ParseRecord(string line)
        {
            var parts = line.Split('|');
            if (parts.Length != 15 || parts[0] != "PURCHASEREC") return null;
            var rec = new CommittedPurchase
            {
                OperationId = parts[1],
                BindingDigest = parts[3],
                PayloadDigest = parts[4],
                AccountId = Decode(parts[5]),
                CharacterId = Decode(parts[6]),
                StoneId = Decode(parts[7]),
                ResultCode = parts[8],
                ApDebited = int.Parse(parts[9], CultureInfo.InvariantCulture),
                PaymentSource = Decode(parts[10]),
                OfferedSetKey = Decode(parts[11]),
                OfferedSetVersion = int.Parse(parts[12], CultureInfo.InvariantCulture),
                CharacterRevision = long.Parse(parts[13], CultureInfo.InvariantCulture),
                CharacterSnapshot = Decode(parts[14])
            };
            return new ParsedRecord
            {
                OperationId = parts[1],
                Boundary = (PurchaseBoundary)int.Parse(parts[2], CultureInfo.InvariantCulture),
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

    // ── T031 — ChooseWeaponDisciplineSkill (contracts.md §ChooseWeaponDisciplineSkill) ─────────────
    //
    // The CHARACTER-side mutation authority for the Weapon Discipline permanent skill-cap choice. One
    // accepted choice appends exactly ONE durable SkillCapChoiceRecord (choice + cap-provider provenance)
    // under ONE replayable receipt. It cannot be spent twice and cannot raise every melee cap — the
    // authored choice names ONE target skill (Adapters/Warrior/SkillCapProvider.Choices).
    //
    // Recovery model mirrors PurchaseCommandHandler exactly: an append-only, per-boundary-fsync'd journal
    // IS the transaction; the character store is an idempotent projection of the journal. Re-submitting
    // the same operationId returns the recorded terminal (Replayed); a conflicting binding/payload under a
    // committed op rejects OperationConflict with no mutation. All content gates (purchased/eligible, ≥2
    // authored choices, offered selection, ≤100 cap, no prior choice) live in the pure SkillCapChoices
    // transition; the catalog resolution lives in the SkillCapProvider adapter.

    public enum WeaponDisciplineCommandOutcome
    {
        Applied,
        Replayed,
        Rejected
    }

    /// <summary>Result of a ChooseWeaponDisciplineSkill command. On rejection nothing was
    /// journaled/committed.</summary>
    public readonly struct WeaponDisciplineCommandResult
    {
        public WeaponDisciplineCommandResult(WeaponDisciplineCommandOutcome outcome, string resultCode,
            string receiptId, string choiceId, string targetSkill, int capValue, long characterRevision)
        {
            Outcome = outcome;
            ResultCode = resultCode;
            ReceiptId = receiptId;
            ChoiceId = choiceId;
            TargetSkill = targetSkill;
            CapValue = capValue;
            CharacterRevision = characterRevision;
        }

        public WeaponDisciplineCommandOutcome Outcome { get; }
        public string ResultCode { get; }
        public string ReceiptId { get; }
        public string ChoiceId { get; }
        public string TargetSkill { get; }
        public int CapValue { get; }
        public long CharacterRevision { get; }
    }

    /// <summary>A ChooseWeaponDisciplineSkill command envelope (contracts.md payload: nodeId/version,
    /// selected skill stable ID, choice-catalog version). The transport attaches the server-observed
    /// <see cref="Connection"/>; <see cref="Claim"/> is compared but never trusted.</summary>
    public readonly struct ChooseWeaponDisciplineSkillCommand
    {
        public ChooseWeaponDisciplineSkillCommand(
            OperationId operationId,
            StoneId stoneId,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            string nodeId,
            int nodeVersion,
            string selectedChoiceId,
            int choiceCatalogVersion,
            long? expectedStoneRevision = null,
            long? expectedCharacterRevision = null)
        {
            OperationId = operationId;
            StoneId = stoneId;
            Connection = connection;
            Claim = claim;
            NodeId = nodeId ?? string.Empty;
            NodeVersion = nodeVersion;
            SelectedChoiceId = selectedChoiceId ?? string.Empty;
            ChoiceCatalogVersion = choiceCatalogVersion;
            ExpectedStoneRevision = expectedStoneRevision;
            ExpectedCharacterRevision = expectedCharacterRevision;
        }

        public OperationId OperationId { get; }
        public StoneId StoneId { get; }
        public AuthenticatedConnection Connection { get; }
        public ClaimedPrincipal Claim { get; }
        public string NodeId { get; }
        public int NodeVersion { get; }
        public string SelectedChoiceId { get; }
        public int ChoiceCatalogVersion { get; }
        public long? ExpectedStoneRevision { get; }
        public long? ExpectedCharacterRevision { get; }

        public VersionedId Node => new VersionedId(NodeId, NodeVersion);
    }

    public sealed class WeaponDisciplineCommandHandler
    {
        private readonly string _journalPath;
        private readonly PrincipalResolver _resolver;
        private readonly IStoneAggregateStore _stoneStore;
        private readonly ICharacterAggregateStore _characterStore;
        private readonly IAccountStoneAuthorityStore _authorityStore;
        private readonly Adapters.Warrior.SkillCapProvider _provider;

        public WeaponDisciplineCommandHandler(
            string journalPath,
            PrincipalResolver resolver,
            IStoneAggregateStore stoneStore,
            ICharacterAggregateStore characterStore,
            IAccountStoneAuthorityStore authorityStore,
            Adapters.Warrior.SkillCapProvider? provider = null)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _stoneStore = stoneStore ?? throw new ArgumentNullException(nameof(stoneStore));
            _characterStore = characterStore ?? throw new ArgumentNullException(nameof(characterStore));
            _authorityStore = authorityStore ?? throw new ArgumentNullException(nameof(authorityStore));
            _provider = provider ?? new Adapters.Warrior.SkillCapProvider();

            RehydrateFromJournal();
        }

        public string JournalPath => _journalPath;

        public WeaponDisciplineCommandResult Handle(ChooseWeaponDisciplineSkillCommand command)
        {
            var resolution = _resolver.Resolve(command.Connection, command.Claim, out var principal);
            if (resolution == PrincipalResolution.UnauthenticatedPeer)
                return Reject("Unauthenticated");
            if (resolution == PrincipalResolution.PrincipalMismatch)
                return Reject("PrincipalMismatch");

            string opId = command.OperationId.Value;

            string bindingDigest = PurchaseCommandHandler.Digest(string.Join("|", new[]
            {
                opId,
                command.StoneId.Value,
                principal.Account.Value,
                principal.Character.Value,
                command.Node.Serialize()
            }));

            string payloadDigest = PurchaseCommandHandler.Digest(string.Join("|", new[]
            {
                command.SelectedChoiceId,
                command.ChoiceCatalogVersion.ToString(CultureInfo.InvariantCulture),
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
                return Terminal(WeaponDisciplineCommandOutcome.Replayed, existing);
            }

            if (HasConflictingPartialIntent(opId, bindingDigest, payloadDigest))
                return Reject("OperationConflict");

            var stone = _stoneStore.GetStone(command.StoneId);
            if (stone == null)
                return Reject("StoneNotFound");
            var character = _characterStore.GetCharacter(principal.Account, principal.Character);
            if (character == null)
                return Reject("CharacterNotFound");

            // Optimistic concurrency on both aggregates before any mutation (CAS). The choice mutates only
            // the character, but a stale Stone revision means the caller's Offered/purchase view is stale.
            if (command.ExpectedStoneRevision.HasValue
                && command.ExpectedStoneRevision.Value != stone.Revision)
                return Reject("StaleStoneRevision");
            if (command.ExpectedCharacterRevision.HasValue
                && command.ExpectedCharacterRevision.Value != character.Revision)
                return Reject("StaleCharacterRevision");

            // Resolve the caller-selected choice against the authored catalog (adapter). A None resolution
            // (unknown id / stale catalog version) maps to the domain's ChoiceNotOffered rejection.
            var resolved = _provider.Resolve(command.SelectedChoiceId, command.ChoiceCatalogVersion);

            // Pure choice transition (validates purchased/eligible, ≥2 authored choices, offered selection,
            // ≤100 cap, no prior committed choice). The grant identity is the command's node identity.
            var transition = SkillCapChoices.Choose(character, command.StoneId, command.Node,
                resolved, _provider.ChoiceCount, opId);
            if (!transition.Accepted)
                return Reject(MapChoiceResult(transition.Result));

            var chosen = transition.Committed!;
            var record = new CommittedChoice
            {
                OperationId = opId,
                BindingDigest = bindingDigest,
                PayloadDigest = payloadDigest,
                AccountId = principal.Account.Value,
                CharacterId = principal.Character.Value,
                StoneId = command.StoneId.Value,
                ResultCode = "Applied",
                ChoiceId = chosen.ChoiceId,
                TargetSkill = chosen.TargetSkill,
                CapValue = chosen.CapValue,
                CharacterRevision = transition.NextCharacter.Revision,
                CharacterSnapshot = transition.NextCharacter.Serialize()
            };

            Append(Record(ChoiceBoundary.IntentJournaled, record));
            Append(Record(ChoiceBoundary.Committed, record));

            ApplyProjection(opId, record);

            return Terminal(WeaponDisciplineCommandOutcome.Applied, record);
        }

        private static WeaponDisciplineCommandResult Terminal(WeaponDisciplineCommandOutcome outcome,
            CommittedChoice r) =>
            new WeaponDisciplineCommandResult(outcome, r.ResultCode, Receipt(r.OperationId),
                r.ChoiceId, r.TargetSkill, r.CapValue, r.CharacterRevision);

        private static string MapChoiceResult(SkillCapChoiceResult r)
        {
            switch (r)
            {
                case SkillCapChoiceResult.NotPurchased: return "NotPurchased";
                case SkillCapChoiceResult.CatalogTooSmall: return "CatalogTooSmall";
                case SkillCapChoiceResult.ChoiceNotOffered: return "ChoiceNotOffered";
                case SkillCapChoiceResult.CapExceedsMax: return "CapExceedsMax";
                case SkillCapChoiceResult.AlreadyChosen: return "AlreadyChosen";
                default: return "Rejected";
            }
        }

        private void ApplyProjection(string operationId, CommittedChoice record)
        {
            _characterStore.ApplyCharacterProjection(operationId,
                CharacterProgressionAggregate.Deserialize(record.CharacterSnapshot));
        }

        private static WeaponDisciplineCommandResult Reject(string code) =>
            new WeaponDisciplineCommandResult(WeaponDisciplineCommandOutcome.Rejected, code, string.Empty,
                string.Empty, string.Empty, 0, 0);

        private static string Receipt(string opId) =>
            PurchaseCommandHandler.Digest("weapondisciplinereceipt|" + opId);

        // ---- Journal ----

        private enum ChoiceBoundary
        {
            IntentJournaled = 1,
            Committed = 2
        }

        private sealed class CommittedChoice
        {
            public string OperationId = string.Empty;
            public string BindingDigest = string.Empty;
            public string PayloadDigest = string.Empty;
            public string AccountId = string.Empty;
            public string CharacterId = string.Empty;
            public string StoneId = string.Empty;
            public string ResultCode = string.Empty;
            public string ChoiceId = string.Empty;
            public string TargetSkill = string.Empty;
            public int CapValue;
            public long CharacterRevision;
            public string CharacterSnapshot = string.Empty;
        }

        private CommittedChoice? FindCommitted(string operationId)
        {
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null || rec.Value.OperationId != operationId) continue;
                if (rec.Value.Boundary == ChoiceBoundary.Committed)
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
                if (rec.Value.Boundary != ChoiceBoundary.IntentJournaled) continue;
                if (!string.Equals(rec.Value.Record.BindingDigest, bindingDigest, StringComparison.Ordinal)
                    || !string.Equals(rec.Value.Record.PayloadDigest, payloadDigest, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private void RehydrateFromJournal()
        {
            var committedByOp = new Dictionary<string, CommittedChoice>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null) continue;
                if (rec.Value.Boundary != ChoiceBoundary.Committed) continue;
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
            public ChoiceBoundary Boundary;
            public CommittedChoice Record;
        }

        private static string Record(ChoiceBoundary boundary, CommittedChoice r)
        {
            return string.Join("|", new[]
            {
                "WEAPDISCREC",
                r.OperationId,
                ((int)boundary).ToString(CultureInfo.InvariantCulture),
                r.BindingDigest,
                r.PayloadDigest,
                Encode(r.AccountId),
                Encode(r.CharacterId),
                Encode(r.StoneId),
                r.ResultCode,
                Encode(r.ChoiceId),
                Encode(r.TargetSkill),
                r.CapValue.ToString(CultureInfo.InvariantCulture),
                r.CharacterRevision.ToString(CultureInfo.InvariantCulture),
                Encode(r.CharacterSnapshot)
            });
        }

        private static ParsedRecord? ParseRecord(string line)
        {
            var parts = line.Split('|');
            if (parts.Length != 14 || parts[0] != "WEAPDISCREC") return null;
            var rec = new CommittedChoice
            {
                OperationId = parts[1],
                BindingDigest = parts[3],
                PayloadDigest = parts[4],
                AccountId = Decode(parts[5]),
                CharacterId = Decode(parts[6]),
                StoneId = Decode(parts[7]),
                ResultCode = parts[8],
                ChoiceId = Decode(parts[9]),
                TargetSkill = Decode(parts[10]),
                CapValue = int.Parse(parts[11], CultureInfo.InvariantCulture),
                CharacterRevision = long.Parse(parts[12], CultureInfo.InvariantCulture),
                CharacterSnapshot = Decode(parts[13])
            };
            return new ParsedRecord
            {
                OperationId = parts[1],
                Boundary = (ChoiceBoundary)int.Parse(parts[2], CultureInfo.InvariantCulture),
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
