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
    // T013 — recoverable PurchaseNode command handler (contracts.md §"PurchaseNode"; data-model.md
    // §"Purchase personal node"). This is the CHARACTER-side mutation authority: one accepted purchase
    //   * DEBITS the caller's Stone-wide Personal AP once, and
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
    // net48 audit: engine-free domain types only. Since ADO #128 the durable framing
    // primitives (FileStream/.Flush(true), BinaryReader/Writer, SHA256, Encoding.UTF8) live in
    // CommandJournalFraming rather than in this file; this handler still owns its own journal
    // FILE.
    // Link-compiles into the net8 test project.

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

        // T022 split-ledger fix: the AUTHORITATIVE Personal-AP EARN ledger (the same receipt-derived
        // ICharacterApStore that OperationReceiptStore.SubmitFoundationalAp credits on every valid
        // Foundational placement). Personal AP has exactly one earning authority; purchasing must observe
        // it. The character aggregate's CharacterStoneRecord.PersonalAp is NOT an independent balance —
        // when this store is supplied the spendable balance is DERIVED as
        //   available = earned(ICharacterApStore) − PersonalAP already spent (this purchase journal),
        // both idempotent receipt projections, so no second synchronization ledger and no double-credit.
        // Null only on the legacy pure-domain test seam, which seeds PersonalAp directly on the aggregate.
        private readonly ICharacterApStore? _apStore;

        public PurchaseCommandHandler(
            string journalPath,
            PrincipalResolver resolver,
            IStoneAggregateStore stoneStore,
            ICharacterAggregateStore characterStore,
            IAccountStoneAuthorityStore authorityStore,
            HomesteadProgressionCatalog? catalog = null,
            ICharacterApStore? apStore = null)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _stoneStore = stoneStore ?? throw new ArgumentNullException(nameof(stoneStore));
            _characterStore = characterStore ?? throw new ArgumentNullException(nameof(characterStore));
            _authorityStore = authorityStore ?? throw new ArgumentNullException(nameof(authorityStore));
            _catalog = catalog ?? new HomesteadProgressionCatalog();
            _apStore = apStore;

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

            // T022 split-ledger fix: overlay the AUTHORITATIVE spendable Personal AP onto the caller's
            // Stone record before the pure transition. Personal AP is earned on ONE authority (the
            // receipt-derived ICharacterApStore that Foundational placement credits), never on the
            // character aggregate's stored PersonalAp field. Spendable balance is derived as
            //   earned(ICharacterApStore) − Personal-AP already spent by this caller's committed purchases,
            // both idempotent receipt/journal projections — no second synchronization ledger, no
            // double-credit, and fail-closed (a caller with no earn ledger reads 0). When no ApStore is
            // wired (legacy pure-domain test seam) the aggregate's PersonalAp is used verbatim.
            if (_apStore != null)
                character = WithAuthoritativePersonalAp(character, principal, command.StoneId);

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
                PaymentSource = "PersonalAP",
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
                case NodePurchaseResult.PaymentSourceRetired: return "PaymentSourceRetired";
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

        /// <summary>T022 split-ledger fix — return a copy of <paramref name="character"/> whose Stone
        /// record for <paramref name="stoneId"/> carries the AUTHORITATIVE spendable Personal AP:
        /// earned(ICharacterApStore) − Personal-AP already spent by this caller's committed purchases at
        /// this Stone. Both terms are idempotent receipt/journal projections, so this is a pure derived
        /// read (no stored second balance). Never negative: a spent total exceeding earned clamps to 0,
        /// which fail-closes an over-spend to InsufficientPersonalAP in the pure transition. Only the
        /// PersonalAp field is overwritten; every other balance/record/field and the aggregate revision
        /// are preserved so the CAS/idempotency/purchase-record invariants are untouched.</summary>
        private CharacterProgressionAggregate WithAuthoritativePersonalAp(
            CharacterProgressionAggregate character, AuthoritativePrincipal principal, StoneId stoneId)
        {
            int earned = _apStore!.GetPersonalAp(principal.Account, principal.Character, stoneId);
            int spent = SpentPersonalAp(principal, stoneId);
            int available = earned - spent;
            if (available < 0) available = 0;

            var newRecords = new List<CharacterStoneRecord>(character.StoneRecords.Count + 1);
            bool found = false;
            foreach (var sr in character.StoneRecords)
            {
                if (!sr.StoneId.Equals(stoneId))
                {
                    newRecords.Add(sr);
                    continue;
                }
                found = true;
                newRecords.Add(new CharacterStoneRecord(sr.StoneId, available, sr.CumulativeAp, sr.PersonalBp,
                    sr.Purchases, sr.Relationships, sr.SkillCapChoices));
            }
            if (!found)
                newRecords.Add(new CharacterStoneRecord(stoneId, available, 0, 0));

            return new CharacterProgressionAggregate(
                character.Account, character.Character, character.WorldProductScope,
                character.Revision, character.BondSlots, character.AttunementSlots,
                character.LastAppliedReceiptId, newRecords, character.SchemaVersion);
        }

        /// <summary>Sum the Personal-AP already spent by this principal at this Stone across every COMMITTED
        /// purchase in the durable journal, MINUS every purchase reversed by a committed cancellation entry.
        /// Read straight off the same durable records the rehydration and idempotency paths use, so it
        /// converges to exactly one debit per committed operation regardless of crash point (a partial/
        /// non-terminal intent contributes nothing).
        ///
        /// <para>REFUND MODEL (ADO #106 decision 2 / #132). A Tree-revocation refund is NOT a removed
        /// purchase row — the journal is append-only and is the source of truth crash recovery replays.
        /// A refund appends a cancellation entry NAMING the purchase operation it reverses; this
        /// derivation excludes the named purchase, so the caller's spendable Stone-wide Personal AP rises
        /// by exactly the refunded amount with no stored balance and no second ledger. Two properties
        /// this shape must hold, both asserted in the test suite:</para>
        /// <list type="bullet">
        /// <item>DETERMINISTIC REPLAY — replaying the journal twice yields the same balance. Held because
        /// this is a pure fold over durable records with no accumulator outside the call.</item>
        /// <item>IDEMPOTENT CANCELLATION — the same cancellation appended twice refunds ONCE. Held because
        /// reversals are collected into a SET of reversed operation ids, not summed.</item>
        /// </list>
        /// <para>Purchases whose PaymentSource is the RETIRED "FacetCredit" string are excluded, exactly as
        /// before: they never debited Personal AP, so they must never refund it either. Those records
        /// persist forever in pre-existing worlds and replay on every boot — this exclusion is why the
        /// retired payment-source value must keep being understood (see PurchasePaymentSource).</para></summary>
        private int SpentPersonalAp(AuthoritativePrincipal principal, StoneId stoneId)
        {
            var reversed = ReversedOperationIds();
            var counted = new HashSet<string>(StringComparer.Ordinal);
            int spent = 0;
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null || rec.Value.Boundary != PurchaseBoundary.Committed) continue;
                var r = rec.Value.Record;
                if (!counted.Add(r.OperationId)) continue; // one debit per committed op
                if (reversed.Contains(r.OperationId)) continue; // refunded by a cancellation entry
                if (!string.Equals(r.AccountId, principal.Account.Value, StringComparison.Ordinal)) continue;
                if (!string.Equals(r.CharacterId, principal.Character.Value, StringComparison.Ordinal)) continue;
                if (!string.Equals(r.StoneId, stoneId.Value, StringComparison.Ordinal)) continue;
                if (!string.Equals(r.PaymentSource, "PersonalAP", StringComparison.Ordinal)) continue;
                spent += r.ApDebited;
            }
            return spent;
        }

        /// <summary>Every purchase operation id reversed by a committed cancellation entry in this journal.
        /// A SET, never a count — appending the identical cancellation twice reverses the purchase once.</summary>
        private HashSet<string> ReversedOperationIds()
        {
            var reversed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in ReadDurable())
            {
                var c = ParseCancellation(line);
                if (c != null) reversed.Add(c);
            }
            return reversed;
        }

        /// <summary>Append a cancellation entry reversing <paramref name="reversedOperationId"/> — the
        /// durable refund primitive Tree revocation will use (ADO #106 decision 2). It removes nothing:
        /// the reversed purchase record stays in the journal forever and the derivation simply stops
        /// counting it as spent, so the refund lands as ordinary Stone-wide Personal AP. Appending the
        /// same reversal again is a no-op on the derived balance.</summary>
        public void AppendPurchaseCancellation(string reversedOperationId, string cancellationOperationId)
        {
            if (string.IsNullOrEmpty(reversedOperationId))
                throw new ArgumentNullException(nameof(reversedOperationId));
            Append(string.Join("|", new[]
            {
                "PURCHASECANCELREC",
                Encode(cancellationOperationId ?? string.Empty),
                Encode(reversedOperationId)
            }));
        }

        /// <summary>The purchase operation id a cancellation line reverses, or null when the line is not
        /// a cancellation record.</summary>
        private static string? ParseCancellation(string line)
        {
            var parts = line.Split('|');
            if (parts.Length != 3 || parts[0] != "PURCHASECANCELREC") return null;
            return Decode(parts[2]);
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
            // Delimiter-safe framing invariant (ADO #127, mirroring RelationshipCommands.cs / PR #351):
            // the record is pipe-delimited, so EVERY free-text field is base64-encoded before it enters
            // the frame — never written raw. The OperationId in particular is a caller-composed value
            // that legitimately embeds '|' (a StoneId is "world|zoneX|zoneZ" by construction, e.g.
            // "uid:-898655635|3|2"); writing it unencoded exploded a 15-field record into more and the
            // strict parser rejected EVERY frame — and the journal IS the save. Encoding it (and the
            // ResultCode) here, and decoding symmetrically in ParseRecord, keeps the field count
            // exactly 15 for ANY operation id. Digest fields are hex and integer fields are numeric, so
            // neither can contain '|' — they stay raw.
            return string.Join("|", new[]
            {
                "PURCHASEREC",
                Encode(r.OperationId),
                ((int)boundary).ToString(CultureInfo.InvariantCulture),
                r.BindingDigest,
                r.PayloadDigest,
                Encode(r.AccountId),
                Encode(r.CharacterId),
                Encode(r.StoneId),
                Encode(r.ResultCode),
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
            // Delimiter-safe framing (ADO #127): every free-text field is base64-encoded on write, so no
            // raw '|' can appear inside a field and the field count is a reliable structural check. A
            // torn or malformed frame is rejected honestly as null — never partially applied.
            if (parts.Length != 15 || parts[0] != "PURCHASEREC") return null;
            try
            {
                string operationId = Decode(parts[1]);
                var rec = new CommittedPurchase
                {
                    OperationId = operationId,
                    BindingDigest = parts[3],
                    PayloadDigest = parts[4],
                    AccountId = Decode(parts[5]),
                    CharacterId = Decode(parts[6]),
                    StoneId = Decode(parts[7]),
                    ResultCode = Decode(parts[8]),
                    ApDebited = int.Parse(parts[9], CultureInfo.InvariantCulture),
                    PaymentSource = Decode(parts[10]),
                    OfferedSetKey = Decode(parts[11]),
                    OfferedSetVersion = int.Parse(parts[12], CultureInfo.InvariantCulture),
                    CharacterRevision = long.Parse(parts[13], CultureInfo.InvariantCulture),
                    CharacterSnapshot = Decode(parts[14])
                };
                return new ParsedRecord
                {
                    OperationId = operationId,
                    Boundary = (PurchaseBoundary)int.Parse(parts[2], CultureInfo.InvariantCulture),
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
        // NOTE: this file declares TWO handlers (PurchaseCommandHandler and
        // WeaponDisciplineCommandHandler) and they keep SEPARATE journal files, as before.
        //
        // The record layout above deliberately stays here: it is domain-specific, and the
        // ADO #127 delimiter-safety invariant is enforced at that layer via Encode. The
        // framing layer below is delimiter-agnostic.

        private void Append(string text) => CommandJournalFraming.Append(_journalPath, text);

        private List<string> ReadDurable() => CommandJournalFraming.ReadDurable(_journalPath);

        private static string Encode(string s) => CommandJournalFraming.Encode(s);

        private static string Decode(string s) => CommandJournalFraming.Decode(s);

        public static string Digest(string s) => CommandJournalFraming.Digest(s);
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
            // Delimiter-safe framing invariant (ADO #127, mirroring RelationshipCommands.cs / PR #351):
            // the record is pipe-delimited, so EVERY free-text field is base64-encoded before it enters
            // the frame — never written raw. The OperationId in particular is a caller-composed value
            // that legitimately embeds '|' (a StoneId is "world|zoneX|zoneZ" by construction, e.g.
            // "uid:-898655635|3|2"); writing it unencoded exploded a 14-field record into more and the
            // strict parser rejected EVERY frame — and the journal IS the save, so a PERMANENT Weapon
            // Discipline choice would be lost. Encoding it (and the ResultCode) here, and decoding
            // symmetrically in ParseRecord, keeps the field count exactly 14 for ANY operation id.
            // Digest fields are hex and integer fields are numeric, so neither can contain '|'.
            return string.Join("|", new[]
            {
                "WEAPDISCREC",
                Encode(r.OperationId),
                ((int)boundary).ToString(CultureInfo.InvariantCulture),
                r.BindingDigest,
                r.PayloadDigest,
                Encode(r.AccountId),
                Encode(r.CharacterId),
                Encode(r.StoneId),
                Encode(r.ResultCode),
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
            // Delimiter-safe framing (ADO #127): every free-text field is base64-encoded on write, so no
            // raw '|' can appear inside a field and the field count is a reliable structural check. A
            // torn or malformed frame is rejected honestly as null — never partially applied.
            if (parts.Length != 14 || parts[0] != "WEAPDISCREC") return null;
            try
            {
                string operationId = Decode(parts[1]);
                var rec = new CommittedChoice
                {
                    OperationId = operationId,
                    BindingDigest = parts[3],
                    PayloadDigest = parts[4],
                    AccountId = Decode(parts[5]),
                    CharacterId = Decode(parts[6]),
                    StoneId = Decode(parts[7]),
                    ResultCode = Decode(parts[8]),
                    ChoiceId = Decode(parts[9]),
                    TargetSkill = Decode(parts[10]),
                    CapValue = int.Parse(parts[11], CultureInfo.InvariantCulture),
                    CharacterRevision = long.Parse(parts[12], CultureInfo.InvariantCulture),
                    CharacterSnapshot = Decode(parts[13])
                };
                return new ParsedRecord
                {
                    OperationId = operationId,
                    Boundary = (ChoiceBoundary)int.Parse(parts[2], CultureInfo.InvariantCulture),
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
        // NOTE: this file declares TWO handlers (PurchaseCommandHandler and
        // WeaponDisciplineCommandHandler) and they keep SEPARATE journal files, as before.
        //
        // The record layout above deliberately stays here: it is domain-specific, and the
        // ADO #127 delimiter-safety invariant is enforced at that layer via Encode. The
        // framing layer below is delimiter-agnostic.

        private void Append(string text) => CommandJournalFraming.Append(_journalPath, text);

        private List<string> ReadDurable() => CommandJournalFraming.ReadDurable(_journalPath);

        private static string Encode(string s) => CommandJournalFraming.Encode(s);

        private static string Decode(string s) => CommandJournalFraming.Decode(s);
    }
}
