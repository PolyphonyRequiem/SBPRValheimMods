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
    // T010 — recoverable CommitTreeToFacet command handler (contracts.md §"CommitTreeToFacet";
    // data-model.md §"Commit Tree"). This is the mutation authority for committing one Profession and
    // one Martial Tree into the authored Stone Facets: it authenticates the Governor, validates
    // authority + Responsibility Range, runs the PURE Facet transition (Domain/StoneProgression/
    // StoneFacets.cs), and commits the resulting Stone aggregate under ONE durable, replayable receipt.
    //
    // Recovery model mirrors RelationshipCommands: an append-only, per-boundary-fsync'd journal IS the
    // transaction. The Stone aggregate store is an idempotent projection of the journal, so a crash
    // between the intent and terminal record cannot leave a partial result — recovery re-derives the
    // projection from the one committed record. Re-submitting the same operationId returns the recorded
    // terminal result (Replayed); a conflicting binding/payload under a committed op rejects
    // OperationConflict with no mutation.
    //
    // Governor authority (contracts.md CommitTreeToFacet "Validates: authenticated Governor,
    // Responsibility Range"): a commit is authorized only when the acting character holds an ACTIVE Bond
    // reservation to this Stone (Attunement grants no cultivation authority), and the server-owned
    // Governor authority policy confirms the Bond's authored Responsibility Range covers the requested
    // Facet/category. There is deliberately no permissive fallback.
    //
    // Load-bearing honesty (AT-NO-STONE-LEVEL-MUTATION): the pure transition changes no Historical/Active
    // Stone Level, personal balance, or purchase; this handler only writes the Stone aggregate snapshot
    // the transition produced, so a commit cannot mutate Stone Level or grant a purchase.
    //
    // net48 audit: FileStream(.Flush(true)), BinaryReader/Writer, SHA256, Encoding.UTF8, engine-free
    // domain types only. No net5+ surface, no UnityEngine/Valheim/BepInEx reference: link-compiles into
    // the net8 test project.

    public enum FacetCommandOutcome
    {
        Applied,
        Replayed,
        Rejected
    }

    /// <summary>Result of a Facet command. On rejection nothing was journaled or committed.</summary>
    public readonly struct FacetCommandResult
    {
        public FacetCommandResult(FacetCommandOutcome outcome, string resultCode,
            string receiptId, string facetId, long stoneRevision)
        {
            Outcome = outcome;
            ResultCode = resultCode;
            ReceiptId = receiptId;
            FacetId = facetId;
            StoneRevision = stoneRevision;
        }

        public FacetCommandOutcome Outcome { get; }
        public string ResultCode { get; }
        public string ReceiptId { get; }
        public string FacetId { get; }
        public long StoneRevision { get; }
    }

    /// <summary>A CommitTreeToFacet command envelope (contracts.md payload: facetId, treeId, treeVersion,
    /// paletteVersion). The transport attaches the server-observed <see cref="Connection"/>;
    /// <see cref="Claim"/> is untrusted payload compared but never trusted.</summary>
    public readonly struct CommitTreeToFacetCommand
    {
        public CommitTreeToFacetCommand(
            OperationId operationId,
            StoneId stoneId,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            string facetId,
            string treeId,
            int treeVersion,
            int paletteVersion,
            long? expectedStoneRevision = null)
        {
            OperationId = operationId;
            StoneId = stoneId;
            Connection = connection;
            Claim = claim;
            FacetId = facetId ?? string.Empty;
            TreeId = treeId ?? string.Empty;
            TreeVersion = treeVersion;
            PaletteVersion = paletteVersion;
            ExpectedStoneRevision = expectedStoneRevision;
        }

        public OperationId OperationId { get; }
        public StoneId StoneId { get; }
        public AuthenticatedConnection Connection { get; }
        public ClaimedPrincipal Claim { get; }
        public string FacetId { get; }
        public string TreeId { get; }
        public int TreeVersion { get; }
        public int PaletteVersion { get; }
        public long? ExpectedStoneRevision { get; }

        public VersionedId Tree => new VersionedId(TreeId, TreeVersion);
    }

    /// <summary>Server-owned Governor authority policy (contracts.md CommitTreeToFacet: "authenticated
    /// Governor, Responsibility Range"). Given the acting Bond's authored Responsibility Range + role
    /// and the requested Facet/category, confirms the Governor may commit here. Kept as a seam so the
    /// handler stays engine-free; production wiring sources it from the Stone/content policy. There is
    /// deliberately no permissive fallback — a null policy is rejected at construction.</summary>
    public interface IGovernorAuthorityPolicy
    {
        /// <summary>True when a Bond carrying <paramref name="responsibilityRange"/> and
        /// <paramref name="ownerGovernorRole"/> is authorized to commit a Tree of
        /// <paramref name="category"/> into <paramref name="facetId"/> on <paramref name="stoneId"/>.</summary>
        bool CanCommit(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
            string facetId, FacetCategory category);
    }

    public sealed class FacetCommandHandler
    {
        private readonly string _journalPath;
        private readonly PrincipalResolver _resolver;
        private readonly IStoneAggregateStore _stoneStore;
        private readonly ICharacterAggregateStore _characterStore;
        private readonly IAccountStoneAuthorityStore _authorityStore;
        private readonly IGovernorAuthorityPolicy _governorAuthority;
        private readonly StoneFacetPalette _palette;
        private readonly HomesteadProgressionCatalog _catalog;

        public FacetCommandHandler(
            string journalPath,
            PrincipalResolver resolver,
            IStoneAggregateStore stoneStore,
            ICharacterAggregateStore characterStore,
            IAccountStoneAuthorityStore authorityStore,
            IGovernorAuthorityPolicy governorAuthority,
            StoneFacetPalette? palette = null,
            HomesteadProgressionCatalog? catalog = null)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _stoneStore = stoneStore ?? throw new ArgumentNullException(nameof(stoneStore));
            _characterStore = characterStore ?? throw new ArgumentNullException(nameof(characterStore));
            _authorityStore = authorityStore ?? throw new ArgumentNullException(nameof(authorityStore));
            // No permissive fallback: every caller must inject a server-owned Governor authority policy.
            _governorAuthority = governorAuthority ?? throw new ArgumentNullException(nameof(governorAuthority));
            _palette = palette ?? StoneFacetPalette.Current;
            _catalog = catalog ?? new HomesteadProgressionCatalog();

            // Rehydrate the Stone projection from durable journal truth at construction (server boot).
            // Only committed operations project; a partial op is quarantined, never applied.
            RehydrateFromJournal();
        }

        public string JournalPath => _journalPath;

        public FacetCommandResult Handle(CommitTreeToFacetCommand command)
        {
            // 1-2. Authenticate connection principal; compare (never trust) the claim.
            var resolution = _resolver.Resolve(command.Connection, command.Claim, out var principal);
            if (resolution == PrincipalResolution.UnauthenticatedPeer)
                return Reject("Unauthenticated");
            if (resolution == PrincipalResolution.PrincipalMismatch)
                return Reject("PrincipalMismatch");

            string opId = command.OperationId.Value;

            // Binding digest = op + Stone + principal + facet + tree identity. A committed op replayed
            // with a DIFFERENT binding is OperationConflict.
            string bindingDigest = Digest(string.Join("|", new[]
            {
                opId,
                command.StoneId.Value,
                principal.Account.Value,
                principal.Character.Value,
                command.FacetId,
                command.TreeId,
                command.TreeVersion.ToString(CultureInfo.InvariantCulture)
            }));

            // Payload digest = the FULL mutable intent (paletteVersion + expected revision). A reused
            // operation ID with a CHANGED payload conflicts instead of replaying stale intent.
            string payloadDigest = Digest(string.Join("|", new[]
            {
                command.PaletteVersion.ToString(CultureInfo.InvariantCulture),
                command.ExpectedStoneRevision?.ToString(CultureInfo.InvariantCulture) ?? "-"
            }));

            // Idempotency: a committed record for this op returns the one recorded terminal result; a
            // conflicting binding OR payload under a committed op is OperationConflict.
            var existing = FindCommitted(opId);
            if (existing != null)
            {
                if (!string.Equals(existing.BindingDigest, bindingDigest, StringComparison.Ordinal)
                    || !string.Equals(existing.PayloadDigest, payloadDigest, StringComparison.Ordinal))
                    return Reject("OperationConflict");
                ApplyProjection(opId, existing);
                return new FacetCommandResult(FacetCommandOutcome.Replayed, existing.ResultCode,
                    Receipt(opId), existing.FacetId, existing.StoneRevision);
            }

            // A surviving non-terminal intent with the same op id but different intent conflicts.
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

            // Governor authority + Responsibility Range (contracts.md). The acting character must hold
            // an ACTIVE Bond to this Stone (Attunement grants no cultivation authority). The Bond's
            // authored Responsibility Range + role must authorize a commit of this Tree's category into
            // the requested Facet. Validation runs BEFORE any journal write, so a rejection changes
            // nothing durable.
            var bond = FindActiveBond(character, authority, command.StoneId);
            if (bond == null)
                return Reject("Unauthorized");

            var facetDef = _palette.TryGetFacet(command.FacetId);
            var requestedCategory = facetDef != null ? facetDef.Category : FacetCategory.None;
            if (!_governorAuthority.CanCommit(command.StoneId, bond.ResponsibilityRange,
                    bond.OwnerGovernorRole, command.FacetId, requestedCategory))
                return Reject("OutsideResponsibilityRange");

            // 3. Run the PURE transition.
            var transition = StoneFacets.CommitTreeToFacet(stone, _palette, _catalog,
                command.FacetId, command.Tree, command.PaletteVersion, opId,
                principal.Character.Value, command.ExpectedStoneRevision);

            if (!transition.Accepted)
                return Reject(transition.ResultCode);

            // 4. Commit the Stone aggregate under one durable receipt. Intent -> terminal, each fsync'd.
            var record = new CommittedFacet
            {
                OperationId = opId,
                BindingDigest = bindingDigest,
                PayloadDigest = payloadDigest,
                AccountId = principal.Account.Value,
                CharacterId = principal.Character.Value,
                StoneId = command.StoneId.Value,
                ResultCode = "Applied",
                FacetId = transition.FacetId,
                StoneRevision = transition.NextStone.Revision,
                StoneSnapshot = transition.NextStone.Serialize()
            };

            Append(Record(FacetBoundary.IntentJournaled, record));
            Append(Record(FacetBoundary.Committed, record));

            ApplyProjection(opId, record);

            return new FacetCommandResult(FacetCommandOutcome.Applied, "Applied",
                Receipt(opId), record.FacetId, record.StoneRevision);
        }

        /// <summary>The acting character's ACTIVE Bond record at this Stone, or null. A commit requires
        /// a Bond (Attunement grants no cultivation authority): both the character-owned relationship
        /// record and the authoritative index must agree the reservation is an active Bond.</summary>
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

        private void ApplyProjection(string operationId, CommittedFacet record)
        {
            _stoneStore.ApplyStoneProjection(operationId,
                StoneProgressionAggregate.Deserialize(record.StoneSnapshot));
        }

        private static FacetCommandResult Reject(string code) =>
            new FacetCommandResult(FacetCommandOutcome.Rejected, code, string.Empty, string.Empty, 0);

        private static string Receipt(string opId) => Digest("facetreceipt|" + opId);

        // ---- Journal ----

        private enum FacetBoundary
        {
            IntentJournaled = 1,
            Committed = 2
        }

        private sealed class CommittedFacet
        {
            public string OperationId = string.Empty;
            public string BindingDigest = string.Empty;
            public string PayloadDigest = string.Empty;
            public string AccountId = string.Empty;
            public string CharacterId = string.Empty;
            public string StoneId = string.Empty;
            public string ResultCode = string.Empty;
            public string FacetId = string.Empty;
            public long StoneRevision;
            public string StoneSnapshot = string.Empty;
        }

        private CommittedFacet? FindCommitted(string operationId)
        {
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null || rec.Value.OperationId != operationId) continue;
                if (rec.Value.Boundary == FacetBoundary.Committed)
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
                if (rec.Value.Boundary != FacetBoundary.IntentJournaled) continue;
                if (!string.Equals(rec.Value.Record.BindingDigest, bindingDigest, StringComparison.Ordinal)
                    || !string.Equals(rec.Value.Record.PayloadDigest, payloadDigest, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private void RehydrateFromJournal()
        {
            var committedByOp = new Dictionary<string, CommittedFacet>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null) continue;
                if (rec.Value.Boundary != FacetBoundary.Committed) continue;
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
            public FacetBoundary Boundary;
            public CommittedFacet Record;
        }

        private static string Record(FacetBoundary boundary, CommittedFacet r)
        {
            // Delimiter-safe framing invariant (ADO #127, mirroring RelationshipCommands.cs / PR #351):
            // the record is pipe-delimited, so EVERY free-text field is base64-encoded before it enters
            // the frame — never written raw. The OperationId in particular is a caller-composed value
            // that legitimately embeds '|' (a StoneId is "world|zoneX|zoneZ" by construction, e.g.
            // "uid:-898655635|3|2"); writing it unencoded exploded a 12-field record into more and the
            // strict parser rejected EVERY frame — and the journal IS the save. Encoding it (and the
            // ResultCode) here, and decoding symmetrically in ParseRecord, keeps the field count
            // exactly 12 for ANY operation id. Digest fields are hex and integer fields are numeric, so
            // neither can contain '|' — they stay raw.
            return string.Join("|", new[]
            {
                "FACETREC",
                Encode(r.OperationId),
                ((int)boundary).ToString(CultureInfo.InvariantCulture),
                r.BindingDigest,
                r.PayloadDigest,
                Encode(r.AccountId),
                Encode(r.CharacterId),
                Encode(r.StoneId),
                Encode(r.ResultCode),
                Encode(r.FacetId),
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
            if (parts.Length != 12 || parts[0] != "FACETREC") return null;
            try
            {
                string operationId = Decode(parts[1]);
                var rec = new CommittedFacet
                {
                    OperationId = operationId,
                    BindingDigest = parts[3],
                    PayloadDigest = parts[4],
                    AccountId = Decode(parts[5]),
                    CharacterId = Decode(parts[6]),
                    StoneId = Decode(parts[7]),
                    ResultCode = Decode(parts[8]),
                    FacetId = Decode(parts[9]),
                    StoneRevision = long.Parse(parts[10], CultureInfo.InvariantCulture),
                    StoneSnapshot = Decode(parts[11])
                };
                return new ParsedRecord
                {
                    OperationId = operationId,
                    Boundary = (FacetBoundary)int.Parse(parts[2], CultureInfo.InvariantCulture),
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
