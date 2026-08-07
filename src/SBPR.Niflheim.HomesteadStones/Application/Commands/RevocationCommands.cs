using System;
using System.Collections.Generic;
using System.Globalization;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;

namespace SBPR.Niflheim.HomesteadStones.Application.Commands
{
    // T033 / ADO #137 — the recoverable RevokeTree command handler (contracts.md §"RevokeTree";
    // data-model.md §"Revoke Tree"; spec US5 scenarios 3-5). This is the mutation authority for
    // revoking a Committed Tree, and it is a TWO-STEP act by design (ADO #106 decision 4):
    //
    //   STEP ONE  — PreviewRevocation. Authenticates the principal, enforces the SAME Governor
    //               authority + CAS gates the confirm step enforces, and returns what will be
    //               destroyed: Tree Level, cumulative Bond Power, per-node development progress, the
    //               nodes themselves, and the Personal-AP refunds each household member will receive.
    //               It writes NOTHING — no journal append, no projection, no cancellation. Abandoning
    //               step one is not a rollback; there is simply nothing to roll back
    //               (AT-REVOKE-TWO-STEP).
    //
    //   STEP TWO  — Handle. Re-validates from scratch (the preview is advisory, never a token the
    //               client can present as authority), then commits the whole fan-out under ONE durable
    //               replayable operation.
    //
    // ATOMICITY / AT-REVOKE-ATOMIC. The fan-out reverses N purchases across M characters, which is
    // physically several appends across two journals. It is externally ONE convergent operation
    // because the committed revocation record carries the COMPLETE reversal set, decided before any
    // of it is written. The commit order is: journal intent -> journal terminal (which names every
    // reversal) -> apply the Stone projection -> append each cancellation. A crash anywhere replays
    // from the terminal record and re-derives exactly the same set, and cancellation is idempotent by
    // construction (reversals collect into a SET in the spend derivation, never a sum), so the refund
    // converges to exactly once. Partial revocation is never exposed as success: a rejection returns
    // before the intent record and changes nothing durable.
    //
    // NO BOND POWER REFUND (AT-REVOKE-NO-BP-REFUND). Node development is deleted by the pure
    // TreeRevocation transition and nothing here credits a personal BP balance.
    //
    // AP REFUND (AT-REVOKE-AP-REFUND). Each reversed refundable Character-Effect purchase returns its
    // FULL AP value as ordinary Stone-wide Personal AP, via PurchaseCommandHandler's cancellation
    // primitive. That primitive is `internal` and THIS handler is its one production caller (ADO #137
    // constraint 2): the authority check, the receipt, and the validation that the named purchases
    // exist and belong to this Stone's revoked Tree all live here, in front of it.
    //
    // DURABLE OUTCOMES SURVIVE (AT-DURABLE-OUTCOMES-SURVIVE). Permanent Effects and Progression Keys
    // are never in the reversal set (the discovery filters to CharacterEffect), and NO purchase record
    // is ever removed — the journal is append-only and is what crash recovery replays.
    //
    // REPLACEMENT BUYS NOTHING (AT-REPLACEMENT-NO-AUTOBUY). There is no restore branch here or in the
    // pure transition; a replacement commitment goes through the ordinary CommitTreeToFacet path onto
    // a vacated Facet with zero Stone-owned progress.
    //
    // OUT OF SCOPE (ADO #136/#131): telling a returning player what happened. This handler is that
    // surface's first PRODUCER — the committed record carries everything such a surface would need —
    // but it owns no notification system and emits none.
    //
    // net48 audit: engine-free domain types only; durable framing via CommandJournalFraming. No net5+
    // surface, no UnityEngine/Valheim/BepInEx reference. Link-compiles into the net8 test project.

    public enum RevocationCommandOutcome
    {
        Applied,
        Replayed,
        Rejected
    }

    /// <summary>The Personal-AP refund one household member receives from a revocation. Composed for
    /// the step-one warning and re-reported on commit.</summary>
    public readonly struct RevocationRefund
    {
        public RevocationRefund(AccountId account, CharacterId character, VersionedId node, int apValue)
        {
            Account = account;
            Character = character;
            Node = node;
            ApValue = apValue;
        }

        public AccountId Account { get; }
        public CharacterId Character { get; }

        /// <summary>The purchased node whose Character Effect is being reversed.</summary>
        public VersionedId Node { get; }

        /// <summary>Full AP value returned — no fee, no partial return (ADO #106 decision 1).</summary>
        public int ApValue { get; }
    }

    /// <summary>STEP ONE result: exactly what confirming would destroy, and what would be refunded.
    /// Nothing was mutated to produce this. On rejection <see cref="Accepted"/> is false and the loss
    /// fields are empty — a refused preview is not a zero loss, and the caller must not present it as
    /// one.</summary>
    public sealed class RevocationPreviewResult
    {
        internal RevocationPreviewResult(bool accepted, string resultCode, TreeRevocationLoss? loss,
            IReadOnlyList<RevocationRefund> refunds, long stoneRevision)
        {
            Accepted = accepted;
            ResultCode = resultCode ?? string.Empty;
            Loss = loss;
            Refunds = refunds ?? Array.Empty<RevocationRefund>();
            StoneRevision = stoneRevision;
        }

        public bool Accepted { get; }
        public string ResultCode { get; }

        /// <summary>Stone-owned development and Bond Power that will be destroyed and NOT refunded.
        /// Null on rejection.</summary>
        public TreeRevocationLoss? Loss { get; }

        /// <summary>Per-character Personal-AP refunds that will be issued on confirm.</summary>
        public IReadOnlyList<RevocationRefund> Refunds { get; }

        /// <summary>The Stone revision this preview was computed against. A confirm that passes it as
        /// <c>expectedStoneRevision</c> fails closed (StaleStoneRevision) if the Stone moved in
        /// between, so a Governor can never confirm a warning that has gone stale.</summary>
        public long StoneRevision { get; }

        /// <summary>Total Personal AP that will be returned across every affected household member.</summary>
        public int TotalApRefunded
        {
            get
            {
                int total = 0;
                foreach (var r in Refunds) total += r.ApValue;
                return total;
            }
        }
    }

    /// <summary>Result of a confirmed RevokeTree. On rejection nothing was journaled or committed.</summary>
    public sealed class RevocationCommandResult
    {
        internal RevocationCommandResult(RevocationCommandOutcome outcome, string resultCode,
            string receiptId, string facetId, long stoneRevision, int bondPowerDestroyed,
            int apRefunded, int purchasesReversed)
        {
            Outcome = outcome;
            ResultCode = resultCode ?? string.Empty;
            ReceiptId = receiptId ?? string.Empty;
            FacetId = facetId ?? string.Empty;
            StoneRevision = stoneRevision;
            BondPowerDestroyed = bondPowerDestroyed;
            ApRefunded = apRefunded;
            PurchasesReversed = purchasesReversed;
        }

        public RevocationCommandOutcome Outcome { get; }
        public string ResultCode { get; }
        public string ReceiptId { get; }
        public string FacetId { get; }
        public long StoneRevision { get; }

        /// <summary>Bond Power destroyed and NOT refunded (AT-REVOKE-NO-BP-REFUND).</summary>
        public int BondPowerDestroyed { get; }

        /// <summary>Personal AP returned in full across every reversed purchase.</summary>
        public int ApRefunded { get; }

        public int PurchasesReversed { get; }
    }

    /// <summary>A RevokeTree command envelope (contracts.md payload: facetId, expected treeId/version,
    /// revocation reason code). The transport attaches the server-observed <see cref="Connection"/>;
    /// <see cref="Claim"/> is compared but never trusted.</summary>
    public readonly struct RevokeTreeCommand
    {
        public RevokeTreeCommand(
            OperationId operationId,
            StoneId stoneId,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            string facetId,
            string treeId,
            int treeVersion,
            string reasonCode = "",
            long? expectedStoneRevision = null)
        {
            OperationId = operationId;
            StoneId = stoneId;
            Connection = connection;
            Claim = claim;
            FacetId = facetId ?? string.Empty;
            TreeId = treeId ?? string.Empty;
            TreeVersion = treeVersion;
            ReasonCode = reasonCode ?? string.Empty;
            ExpectedStoneRevision = expectedStoneRevision;
        }

        public OperationId OperationId { get; }
        public StoneId StoneId { get; }
        public AuthenticatedConnection Connection { get; }
        public ClaimedPrincipal Claim { get; }
        public string FacetId { get; }
        public string TreeId { get; }
        public int TreeVersion { get; }
        public string ReasonCode { get; }
        public long? ExpectedStoneRevision { get; }

        public VersionedId Tree => new VersionedId(TreeId, TreeVersion);
    }

    public sealed class RevocationCommandHandler
    {
        private readonly string _journalPath;
        private readonly PrincipalResolver _resolver;
        private readonly IStoneAggregateStore _stoneStore;
        private readonly ICharacterAggregateStore _characterStore;
        private readonly IAccountStoneAuthorityStore _authorityStore;
        private readonly IGovernorAuthorityPolicy _governorAuthority;
        private readonly PurchaseCommandHandler _purchases;
        private readonly StoneFacetPalette _palette;
        private readonly HomesteadProgressionCatalog _catalog;

        public RevocationCommandHandler(
            string journalPath,
            PrincipalResolver resolver,
            IStoneAggregateStore stoneStore,
            ICharacterAggregateStore characterStore,
            IAccountStoneAuthorityStore authorityStore,
            IGovernorAuthorityPolicy governorAuthority,
            PurchaseCommandHandler purchases,
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
            // The purchase handler owns the durable purchase journal AND the cancellation primitive.
            // Revocation never opens that journal itself — one writer, one authority.
            _purchases = purchases ?? throw new ArgumentNullException(nameof(purchases));
            _palette = palette ?? StoneFacetPalette.Current;
            _catalog = catalog ?? new HomesteadProgressionCatalog();

            RehydrateFromJournal();
        }

        public string JournalPath => _journalPath;

        /// <summary>STEP ONE (AT-REVOKE-TWO-STEP). Compute and return the loss + refunds without
        /// mutating anything. Every authority/CAS gate the confirm step applies is applied here too, so
        /// an unauthorized or stale caller is refused at the warning rather than being shown a loss they
        /// could never confirm. This method performs NO journal append, NO projection write, and NO
        /// cancellation — it cannot destroy anything even if the Governor never returns.</summary>
        public RevocationPreviewResult PreviewRevocation(RevokeTreeCommand command)
        {
            var gate = Authorize(command, out _, out var stone);
            if (gate != null)
                return new RevocationPreviewResult(false, gate, null, Array.Empty<RevocationRefund>(), 0);

            var preview = TreeRevocation.Preview(stone!, _catalog, command.FacetId, command.Tree,
                command.ExpectedStoneRevision);
            if (!preview.Accepted)
                return new RevocationPreviewResult(false, MapRevocationResult(preview.Result), null,
                    Array.Empty<RevocationRefund>(), stone!.Revision);

            var refunds = DiscoverRefunds(command.StoneId, command.Tree);
            return new RevocationPreviewResult(true, "Preview", preview.Loss, refunds, stone!.Revision);
        }

        /// <summary>STEP TWO. Re-validate from current authoritative state and commit the whole
        /// revocation — Stone teardown plus the Personal-AP refund fan-out — under one durable
        /// replayable operation. The step-one preview is never trusted as input: a client cannot present
        /// a stale or forged warning as authority to destroy anything.</summary>
        public RevocationCommandResult Handle(RevokeTreeCommand command)
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
                command.FacetId,
                command.TreeId,
                command.TreeVersion.ToString(CultureInfo.InvariantCulture)
            }));

            string payloadDigest = Digest(string.Join("|", new[]
            {
                command.ReasonCode,
                command.ExpectedStoneRevision?.ToString(CultureInfo.InvariantCulture) ?? "-"
            }));

            // Idempotency: a committed record for this op returns the ONE recorded terminal result and
            // re-converges its effects (projection + cancellations), never a second teardown.
            var existing = FindCommitted(opId);
            if (existing != null)
            {
                if (!string.Equals(existing.BindingDigest, bindingDigest, StringComparison.Ordinal)
                    || !string.Equals(existing.PayloadDigest, payloadDigest, StringComparison.Ordinal))
                    return Reject("OperationConflict");
                Converge(opId, existing);
                return Terminal(RevocationCommandOutcome.Replayed, existing);
            }

            if (HasConflictingPartialIntent(opId, bindingDigest, payloadDigest))
                return Reject("OperationConflict");

            var authGate = Authorize(command, out _, out var stone);
            if (authGate != null)
                return Reject(authGate);

            // The pure teardown transition. Validates CAS, Foundational protection, commitment, and
            // exact Facet/Tree/version; produces the next Stone and the SAME loss the preview reported.
            var transition = TreeRevocation.RevokeTree(stone!, _catalog, command.FacetId, command.Tree,
                opId, command.ExpectedStoneRevision);
            if (!transition.Accepted)
                return Reject(MapRevocationResult(transition.Result));

            // Decide the COMPLETE reversal set BEFORE writing any of it. This is what makes a physically
            // multi-append fan-out externally one convergent operation: the terminal record names every
            // reversal, so replay after a crash mid-fan-out reproduces exactly this set.
            var refundable = _purchases.FindRefundablePurchases(command.StoneId, command.Tree);
            var reversedOps = new List<string>(refundable.Count);
            int apRefunded = 0;
            foreach (var p in refundable)
            {
                reversedOps.Add(p.OperationId);
                apRefunded += p.ApValue;
            }

            var loss = transition.Loss!;
            var record = new CommittedRevocation
            {
                OperationId = opId,
                BindingDigest = bindingDigest,
                PayloadDigest = payloadDigest,
                AccountId = principal.Account.Value,
                CharacterId = principal.Character.Value,
                StoneId = command.StoneId.Value,
                ResultCode = "Applied",
                FacetId = loss.FacetId,
                TreeKey = loss.Tree.Key,
                TreeVersion = loss.Tree.Version,
                BondPowerDestroyed = loss.TotalBondPowerDestroyed,
                ApRefunded = apRefunded,
                ReversedOperationIds = reversedOps,
                StoneRevision = transition.NextStone.Revision,
                StoneSnapshot = transition.NextStone.Serialize()
            };

            Append(Record(RevocationBoundary.IntentJournaled, record));
            Append(Record(RevocationBoundary.Committed, record));

            Converge(opId, record);

            return Terminal(RevocationCommandOutcome.Applied, record);
        }

        /// <summary>Apply a committed revocation's effects. Idempotent in both halves: the Stone
        /// projection is a revision-guarded set-to-state, and appending an identical cancellation twice
        /// refunds ONCE because the spend derivation collects reversals into a SET. Called on commit, on
        /// replay, and on boot rehydration, so a crash between the terminal record and any individual
        /// cancellation converges on the next call rather than losing a refund.</summary>
        private void Converge(string operationId, CommittedRevocation record)
        {
            _stoneStore.ApplyStoneProjection(operationId,
                StoneProgressionAggregate.Deserialize(record.StoneSnapshot));

            foreach (var reversedOp in record.ReversedOperationIds)
                _purchases.AppendPurchaseCancellation(reversedOp, operationId);
        }

        /// <summary>Authenticate the caller and enforce Governor authority + Responsibility Range for
        /// this Facet, exactly as CommitTreeToFacet does — revoking a commitment is at least as
        /// authoritative an act as making one. Returns null when authorized; otherwise the reject code.
        /// Runs BEFORE any journal write, so a refusal changes nothing durable.</summary>
        private string? Authorize(RevokeTreeCommand command, out AuthoritativePrincipal principal,
            out StoneProgressionAggregate? stone)
        {
            stone = null;

            var resolution = _resolver.Resolve(command.Connection, command.Claim, out principal);
            if (resolution == PrincipalResolution.UnauthenticatedPeer)
                return "Unauthenticated";
            if (resolution == PrincipalResolution.PrincipalMismatch)
                return "PrincipalMismatch";

            var current = _stoneStore.GetStone(command.StoneId);
            if (current == null)
                return "StoneNotFound";
            var character = _characterStore.GetCharacter(principal.Account, principal.Character);
            if (character == null)
                return "CharacterNotFound";

            var authority = _authorityStore.GetAuthority(principal.Account, command.StoneId);
            var bond = FindActiveBond(character, authority, command.StoneId);
            if (bond == null)
                return "Unauthorized";

            var facetDef = _palette.TryGetFacet(command.FacetId);
            var category = facetDef != null ? facetDef.Category : FacetCategory.None;
            if (!_governorAuthority.CanCommit(command.StoneId, bond.ResponsibilityRange,
                    bond.OwnerGovernorRole, command.FacetId, category))
                return "OutsideResponsibilityRange";

            stone = current;
            return null;
        }

        private List<RevocationRefund> DiscoverRefunds(StoneId stoneId, VersionedId tree)
        {
            var refundable = _purchases.FindRefundablePurchases(stoneId, tree);
            var refunds = new List<RevocationRefund>(refundable.Count);
            foreach (var p in refundable)
                refunds.Add(new RevocationRefund(p.Account, p.Character, p.Node, p.ApValue));
            return refunds;
        }

        /// <summary>The acting character's ACTIVE Bond record at this Stone, or null. Mirrors
        /// FacetCommands: both the character-owned relationship record and the authoritative index must
        /// agree the reservation is an active Bond. Attunement grants no cultivation authority.</summary>
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

        private static string MapRevocationResult(TreeRevocationResult r)
        {
            switch (r)
            {
                case TreeRevocationResult.StaleStoneRevision: return "StaleStoneRevision";
                case TreeRevocationResult.TreeNotCommitted: return "TreeNotCommitted";
                case TreeRevocationResult.TreeMismatch: return "TreeNotEligible";
                case TreeRevocationResult.ContentVersionMismatch: return "ContentVersionMismatch";
                case TreeRevocationResult.ProtectedTree: return "ProtectedTree";
                default: return "Rejected";
            }
        }

        private static RevocationCommandResult Terminal(RevocationCommandOutcome outcome, CommittedRevocation r) =>
            new RevocationCommandResult(outcome, r.ResultCode, Receipt(r.OperationId), r.FacetId,
                r.StoneRevision, r.BondPowerDestroyed, r.ApRefunded, r.ReversedOperationIds.Count);

        private static RevocationCommandResult Reject(string code) =>
            new RevocationCommandResult(RevocationCommandOutcome.Rejected, code, string.Empty,
                string.Empty, 0, 0, 0, 0);

        private static string Receipt(string opId) => Digest("revokereceipt|" + opId);

        // ---- Journal ----

        private enum RevocationBoundary
        {
            IntentJournaled = 1,
            Committed = 2
        }

        private sealed class CommittedRevocation
        {
            public string OperationId = string.Empty;
            public string BindingDigest = string.Empty;
            public string PayloadDigest = string.Empty;
            public string AccountId = string.Empty;
            public string CharacterId = string.Empty;
            public string StoneId = string.Empty;
            public string ResultCode = string.Empty;
            public string FacetId = string.Empty;
            public string TreeKey = string.Empty;
            public int TreeVersion;
            public int BondPowerDestroyed;
            public int ApRefunded;
            public List<string> ReversedOperationIds = new List<string>();
            public long StoneRevision;
            public string StoneSnapshot = string.Empty;
        }

        private CommittedRevocation? FindCommitted(string operationId)
        {
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null || rec.Value.OperationId != operationId) continue;
                if (rec.Value.Boundary == RevocationBoundary.Committed)
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
                if (rec.Value.Boundary != RevocationBoundary.IntentJournaled) continue;
                if (!string.Equals(rec.Value.Record.BindingDigest, bindingDigest, StringComparison.Ordinal)
                    || !string.Equals(rec.Value.Record.PayloadDigest, payloadDigest, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>Replay every committed revocation at construction (server boot). Only committed
        /// operations converge; a partial intent is quarantined, never applied. Re-appending the
        /// recorded cancellations here is what closes the crash-mid-fan-out window, and it is safe
        /// because cancellation is idempotent by construction.</summary>
        private void RehydrateFromJournal()
        {
            var committedByOp = new Dictionary<string, CommittedRevocation>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null) continue;
                if (rec.Value.Boundary != RevocationBoundary.Committed) continue;
                if (!committedByOp.ContainsKey(rec.Value.OperationId))
                    order.Add(rec.Value.OperationId);
                committedByOp[rec.Value.OperationId] = rec.Value.Record;
            }
            foreach (var opId in order)
                Converge(opId, committedByOp[opId]);
        }

        private struct ParsedRecord
        {
            public string OperationId;
            public RevocationBoundary Boundary;
            public CommittedRevocation Record;
        }

        private static string Record(RevocationBoundary boundary, CommittedRevocation r)
        {
            // Delimiter-safe framing invariant (ADO #127, as in FacetCommands/PurchaseCommands): the
            // record is pipe-delimited, so EVERY free-text field is base64-encoded before it enters the
            // frame. Operation ids legitimately embed '|' (a StoneId is "world|zoneX|zoneZ" by
            // construction), so an unencoded id would explode the record and the strict parser would
            // reject EVERY frame — and the journal IS the save. The reversal LIST is joined with '\n'
            // (which cannot appear in an operation id and is not the frame delimiter) and the whole
            // joined string is then encoded, so the field count stays exactly 17 for any fan-out size.
            return string.Join("|", new[]
            {
                "REVOKEREC",
                Encode(r.OperationId),
                ((int)boundary).ToString(CultureInfo.InvariantCulture),
                r.BindingDigest,
                r.PayloadDigest,
                Encode(r.AccountId),
                Encode(r.CharacterId),
                Encode(r.StoneId),
                Encode(r.ResultCode),
                Encode(r.FacetId),
                Encode(r.TreeKey),
                r.TreeVersion.ToString(CultureInfo.InvariantCulture),
                r.BondPowerDestroyed.ToString(CultureInfo.InvariantCulture),
                r.ApRefunded.ToString(CultureInfo.InvariantCulture),
                Encode(string.Join("\n", r.ReversedOperationIds.ToArray())),
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
            if (parts.Length != 17 || parts[0] != "REVOKEREC") return null;
            try
            {
                string operationId = Decode(parts[1]);
                string joined = Decode(parts[14]);
                var reversed = new List<string>();
                if (joined.Length > 0)
                    reversed.AddRange(joined.Split('\n'));

                var rec = new CommittedRevocation
                {
                    OperationId = operationId,
                    BindingDigest = parts[3],
                    PayloadDigest = parts[4],
                    AccountId = Decode(parts[5]),
                    CharacterId = Decode(parts[6]),
                    StoneId = Decode(parts[7]),
                    ResultCode = Decode(parts[8]),
                    FacetId = Decode(parts[9]),
                    TreeKey = Decode(parts[10]),
                    TreeVersion = int.Parse(parts[11], CultureInfo.InvariantCulture),
                    BondPowerDestroyed = int.Parse(parts[12], CultureInfo.InvariantCulture),
                    ApRefunded = int.Parse(parts[13], CultureInfo.InvariantCulture),
                    ReversedOperationIds = reversed,
                    StoneRevision = long.Parse(parts[15], CultureInfo.InvariantCulture),
                    StoneSnapshot = Decode(parts[16])
                };
                return new ParsedRecord
                {
                    OperationId = operationId,
                    Boundary = (RevocationBoundary)int.Parse(parts[2], CultureInfo.InvariantCulture),
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
        // The frame format (length prefix, CRC32, fsync'd append, truncate-at-first-damage read) lives
        // in CommandJournalFraming, shared with the other progression handlers. THIS HANDLER OWNS ITS
        // OWN JOURNAL FILE — shared code, INDEPENDENT durable state. It does NOT own the purchase
        // journal: cancellations are written through PurchaseCommandHandler, which is that journal's
        // single writer.

        private void Append(string text) => CommandJournalFraming.Append(_journalPath, text);

        private List<string> ReadDurable() => CommandJournalFraming.ReadDurable(_journalPath);

        private static string Encode(string s) => CommandJournalFraming.Encode(s);

        private static string Decode(string s) => CommandJournalFraming.Decode(s);

        private static string Digest(string s) => CommandJournalFraming.Digest(s);
    }
}
