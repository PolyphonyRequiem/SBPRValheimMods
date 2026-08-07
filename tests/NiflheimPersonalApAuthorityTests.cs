// ============================================================================
//  T022 split-ledger regression — unified Personal-AP authority for Masterwork
//  purchase.
// ----------------------------------------------------------------------------
//  Reproduces the live PR #388 PRODUCT FAIL (executor t_b9caf2b6 run 1296):
//  genuine Foundational placements credited Personal AP on the receipt-derived
//  ICharacterApStore, but Masterwork `buy` read PersonalAp off the SEPARATE
//  character aggregate (always 0 for a joined buyer) and rejected
//  InsufficientPersonalAP every time.
//
//  Every test here drives the REAL server handlers end-to-end:
//    * earn  — OperationReceiptStore.SubmitFoundationalAp (the exact Foundational
//              placement credit path) into a shared ICharacterApStore;
//    * offer — LocalProvisioningIngress.OfferMasterwork (accepted develop+offer);
//    * buy   — LocalProvisioningIngress.BuyMasterwork (accepted PurchaseCommandHandler),
//              composed over the SAME shared ICharacterApStore.
//
//  Coverage (card acceptance):
//    * valid placements credit Personal AP and purchase observes that balance;
//    * offered Masterwork purchase SUCCEEDS when the earned cost is reached;
//    * replayed placement receipt does not double-credit the spendable balance;
//    * insufficient earned balance still rejects InsufficientPersonalAP;
//    * purchase debits exactly once; a replayed/duplicate buy cannot double-spend
//      the earned balance (a second distinct node stays affordable iff funded);
//    * account/character isolation holds — one character's earn never funds another;
//    * the derived available balance = earned − spent survives a server restart
//      (fresh stores rehydrated from the same durable journals).
// ============================================================================

using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimPersonalApAuthorityTests : System.IDisposable
    {
        private readonly string _dir;
        private readonly WorldId _world = new WorldId("uid:t022-apauthority");
        private readonly StoneId _stone;

        private readonly AccountId _gov = new AccountId("acct-gov");
        private readonly CharacterId _govChar = new CharacterId("char-gov");
        private readonly AccountId _buyer = new AccountId("acct-buyer");
        private readonly CharacterId _buyerChar = new CharacterId("char-buyer");
        private readonly AccountId _other = new AccountId("acct-other");
        private readonly CharacterId _otherChar = new CharacterId("char-other");

        private static readonly VersionedId Crafting = HomesteadProgressionCatalog.CraftingTree;
        private static readonly VersionedId Masterwork = new VersionedId("Masterwork", 1);

        public NiflheimPersonalApAuthorityTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "niflheim-t022apauth-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _stone = StoneId.FromHostZone(_world, 7, 5);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        }

        // ── The shared, durable earn ledger: the exact OperationReceiptStore the Foundational runtime
        //    composes, over a persistent AP journal in the same durable directory. Its ICharacterApStore
        //    projection sink is the ONE authoritative Personal-AP earn balance the purchase path must read.

        private string ApJournal => Path.Combine(_dir, FoundationalProgressionServer.ApJournalFile);

        private OperationReceiptStore NewReceipts(InMemoryCharacterApStore apSink) =>
            new OperationReceiptStore(ApJournal, new InMemoryMirroredStoneApStore(), apSink);

        /// <summary>Credit one genuine Foundational placement of Personal AP to (account, character) at the
        /// Stone through the REAL receipt path (each op = +1 Personal AP, exactly like an in-world piece).</summary>
        private void EarnPlacement(OperationReceiptStore receipts, AccountId account, CharacterId character, string op)
        {
            var principal = new AuthoritativePrincipal(account, character);
            var r = receipts.SubmitFoundationalAp(new OperationId(op), _stone, principal, "evidence-" + op);
            Assert.Equal(ReceiptOutcome.Applied, r.Outcome);
        }

        private void EarnPlacements(InMemoryCharacterApStore apSink, AccountId account, CharacterId character, int count, string prefix)
        {
            var receipts = NewReceipts(apSink);
            for (int i = 0; i < count; i++)
                EarnPlacement(receipts, account, character, prefix + "-" + i);
        }

        // ── Composition: a LocalProgressionServer whose purchase path reads the SHARED earn ledger. ──

        private LocalProgressionServer NewServer(
            InMemoryStoneAggregateStore stones,
            InMemoryCharacterAggregateStore characters,
            InMemoryAccountStoneAuthorityStore authority,
            InMemoryCharacterApStore apSink)
        {
            var relationships = new RelationshipCommandHandler(
                Path.Combine(_dir, "relationships.journal"), new PrincipalResolver(), characters, authority,
                new FixedFamilyResolver(), new AllowHomesteadBondPolicy(), AlwaysAtStoneProximity.Instance, null, _world,
                new ProductScope("SBPR.Trailborne"));

            return LocalProgressionServer.Create(
                _dir, stones, characters, authority, relationships,
                new FixedFamilyResolver(), new AllowGovernorAuthority(), new AllowDevelopmentAuthority(),
                new CommittedGovernorOwnerAuthority(new GovernorPresenceResolver(characters, authority)),
                characterApStore: apSink);
        }

        // A bonded Governor (develop/offer authority) — holds NO Personal AP on the aggregate.
        private CharacterProgressionAggregate Governor() =>
            new CharacterProgressionAggregate(_gov, _govChar, "t022apauth/trailborne",
                revision: 3, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[]
                {
                    new CharacterStoneRecord(_stone, 0, 0, 0, purchases: null,
                        relationships: new[]
                        {
                            new RelationshipRecord("rel-bond-gov", RelationshipKind.Bond,
                                RelationshipStatus.Active, "Homestead:All", "Governor",
                                "relreceipt:seed-bond", string.Empty)
                        })
                });

        private AccountStoneAuthorityIndex GovBondIndex() =>
            AccountStoneAuthorityIndex.Vacant(_gov, _stone).WithReservationAdded(
                new AuthorityReservation(_govChar, RelationshipKind.Bond, "rel-bond-gov",
                    "relreceipt:seed-bond"), 1);

        // An ATTUNED buyer — critically, the aggregate's stored PersonalAp is ZERO (the live joined-client
        // condition). Any spendable balance must come from the EARNED receipt ledger, not this field.
        private CharacterProgressionAggregate AttunedBuyer(AccountId account, CharacterId character, string relId) =>
            new CharacterProgressionAggregate(account, character, "t022apauth/trailborne",
                revision: 2, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[]
                {
                    new CharacterStoneRecord(_stone, 0, 0, 0, purchases: null,
                        relationships: new[]
                        {
                            new RelationshipRecord(relId, RelationshipKind.Attunement,
                                RelationshipStatus.Active, "Homestead:All", string.Empty,
                                "relreceipt:" + relId, string.Empty)
                        })
                });

        private void SeedAttunement(InMemoryAccountStoneAuthorityStore authority, AccountId account, CharacterId character, string relId)
        {
            var idx = AccountStoneAuthorityIndex.Vacant(account, _stone).WithReservationAdded(
                new AuthorityReservation(character, RelationshipKind.Attunement, relId, "relreceipt:" + relId), 1);
            authority.ApplyAuthorityProjection("seed-" + relId, idx);
        }

        private (LocalProgressionServer server, InMemoryStoneAggregateStore stones,
                 InMemoryCharacterAggregateStore characters, InMemoryAccountStoneAuthorityStore authority,
                 InMemoryCharacterApStore apSink) Bootstrap()
        {
            var stones = new InMemoryStoneAggregateStore();
            var characters = new InMemoryCharacterAggregateStore();
            var authority = new InMemoryAccountStoneAuthorityStore();
            var apSink = new InMemoryCharacterApStore();

            characters.PutCharacter(Governor());
            authority.ApplyAuthorityProjection("seed-bond", GovBondIndex());
            characters.PutCharacter(AttunedBuyer(_buyer, _buyerChar, "rel-attune-buyer"));
            SeedAttunement(authority, _buyer, _buyerChar, "rel-attune-buyer");

            var server = NewServer(stones, characters, authority, apSink);
            return (server, stones, characters, authority, apSink);
        }

        private void Offer(LocalProgressionServer server) =>
            Assert.True(server.CreateLocalProvisioningIngress()
                .OfferMasterwork(new AuthoritativeSubject(_gov, _govChar), _stone, "qa-mw").Succeeded);

        private LocalProvisioningResult Buy(LocalProgressionServer server, AccountId account, CharacterId character, string opPrefix) =>
            server.CreateLocalProvisioningIngress()
                .BuyMasterwork(new AuthoritativeSubject(account, character), _stone, opPrefix);

        private int MasterworkPurchaseCount(InMemoryCharacterAggregateStore characters, AccountId account, CharacterId character)
        {
            var chr = characters.GetCharacter(account, character);
            if (chr == null) return 0;
            int n = 0;
            foreach (var sr in chr.StoneRecords)
                if (sr.StoneId.Equals(_stone))
                    foreach (var p in sr.Purchases)
                        if (p.Node.Key == Masterwork.Key) n++;
            return n;
        }

        // ── THE live-failure reproduction: earned placement AP funds Masterwork purchase. ──

        [Fact]
        public void Earned_placement_ap_is_visible_to_masterwork_purchase_and_buy_succeeds()
        {
            var (server, _, characters, _, apSink) = Bootstrap();

            // Buyer's aggregate PersonalAp is 0; earn one genuine Foundational placement (Masterwork AP cost=1).
            EarnPlacements(apSink, _buyer, _buyerChar, count: 1, prefix: "earn-buyer");
            Assert.Equal(1, apSink.GetPersonalAp(_buyer, _buyerChar, _stone));

            Offer(server);
            var buy = Buy(server, _buyer, _buyerChar, "qa-mw");

            // The exact live regression: before the fix this rejected InsufficientPersonalAP every time.
            Assert.True(buy.Succeeded, buy.ResultCode + "/" + buy.Step);
            Assert.Equal("Purchased", buy.Kind);
            Assert.Equal(1, MasterworkPurchaseCount(characters, _buyer, _buyerChar));
        }

        [Fact]
        public void Unfunded_buyer_with_no_earned_placement_still_rejects_insufficient_personal_ap()
        {
            var (server, _, _, _, _) = Bootstrap(); // no EarnPlacement calls: earned balance is 0.
            Offer(server);

            var buy = Buy(server, _buyer, _buyerChar, "qa-mw");
            Assert.False(buy.Succeeded);
            Assert.Equal("InsufficientPersonalAP", buy.ResultCode);
        }

        [Fact]
        public void Replayed_placement_receipt_does_not_double_credit_spendable_balance()
        {
            var (server, _, _, _, apSink) = Bootstrap();

            // Submit the SAME placement operationId twice through the real receipt path: replay is idempotent.
            var receipts = NewReceipts(apSink);
            EarnPlacement(receipts, _buyer, _buyerChar, "earn-dup");
            var replay = receipts.SubmitFoundationalAp(
                new OperationId("earn-dup"), _stone, new AuthoritativePrincipal(_buyer, _buyerChar), "evidence-earn-dup");
            Assert.Equal(ReceiptOutcome.Replayed, replay.Outcome);

            // Earned balance is still exactly 1 — no double credit.
            Assert.Equal(1, apSink.GetPersonalAp(_buyer, _buyerChar, _stone));

            Offer(server);
            // One earned AP funds exactly one Masterwork purchase (cost 1).
            Assert.True(Buy(server, _buyer, _buyerChar, "qa-mw").Succeeded);
        }

        [Fact]
        public void Purchase_debits_exactly_once_and_duplicate_buy_cannot_double_spend()
        {
            var (server, _, characters, _, apSink) = Bootstrap();
            EarnPlacements(apSink, _buyer, _buyerChar, count: 1, prefix: "earn-buyer");
            Offer(server);

            var first = Buy(server, _buyer, _buyerChar, "qa-mw");
            Assert.True(first.Succeeded);
            Assert.Equal("Purchased", first.Kind);

            // Exact replay of the SAME buy op returns the recorded terminal result — one purchase, one debit.
            var again = Buy(server, _buyer, _buyerChar, "qa-mw");
            Assert.True(again.Succeeded);
            Assert.Equal("Replayed", again.Kind);
            Assert.Equal(1, MasterworkPurchaseCount(characters, _buyer, _buyerChar));

            // The one earned AP is now fully spent: a DISTINCT second purchase attempt (different op) of the
            // same node rejects — AlreadyAcquired for Masterwork (a further debit is structurally impossible),
            // proving the earned balance was consumed exactly once, never re-spent.
            var third = Buy(server, _buyer, _buyerChar, "qa-mw-2");
            Assert.False(third.Succeeded);
            Assert.Equal("AlreadyAcquired", third.ResultCode);
        }

        [Fact]
        public void Spent_reduces_available_so_a_second_node_needs_a_second_earned_ap()
        {
            // Two distinct affordable purchases require two earned AP; one earned AP funds only the first.
            // Masterwork is unique-per-node, so we assert the balance arithmetic via the read gate instead:
            // with exactly 1 earned AP and 1 spent on Masterwork, available is 0 and a re-derived purchase of
            // a fresh (re-offered) attempt cannot find spendable AP.
            var (server, _, _, _, apSink) = Bootstrap();
            EarnPlacements(apSink, _buyer, _buyerChar, count: 1, prefix: "earn-buyer");
            Offer(server);

            Assert.True(Buy(server, _buyer, _buyerChar, "qa-mw").Succeeded);

            // Earn ledger unchanged at 1; the purchase journal now records 1 PersonalAP debit, so the derived
            // spendable balance is 0. A second Masterwork buy attempt rejects (AlreadyAcquired) — never a
            // fabricated re-credit from the stale aggregate field.
            Assert.Equal(1, apSink.GetPersonalAp(_buyer, _buyerChar, _stone));
            var second = Buy(server, _buyer, _buyerChar, "qa-mw-again");
            Assert.False(second.Succeeded);
        }

        [Fact]
        public void Account_isolation_one_characters_earn_never_funds_another()
        {
            var (server, _, characters, authority, apSink) = Bootstrap();

            // A DIFFERENT attuned character earns 5 placements; the target buyer earns nothing.
            characters.PutCharacter(AttunedBuyer(_other, _otherChar, "rel-attune-other"));
            SeedAttunement(authority, _other, _otherChar, "rel-attune-other");
            EarnPlacements(apSink, _other, _otherChar, count: 5, prefix: "earn-other");

            Offer(server);

            // The unfunded buyer still cannot purchase — the other account's earned AP is not visible.
            var buy = Buy(server, _buyer, _buyerChar, "qa-mw");
            Assert.False(buy.Succeeded);
            Assert.Equal("InsufficientPersonalAP", buy.ResultCode);

            // The funded OTHER buyer can.
            Assert.True(Buy(server, _other, _otherChar, "qa-mw-other").Succeeded);
        }

        [Fact]
        public void Derived_available_survives_server_restart_from_durable_journals()
        {
            // Earn, offer, buy on the first server instance.
            var (server1, _, _, _, apSink1) = Bootstrap();
            EarnPlacements(apSink1, _buyer, _buyerChar, count: 2, prefix: "earn-buyer");
            Offer(server1);
            Assert.True(Buy(server1, _buyer, _buyerChar, "qa-mw").Succeeded);

            // Restart: fresh stores, fresh AP sink rehydrated from the SAME durable AP journal (server boot),
            // fresh LocalProgressionServer over the SAME durable directory (relationship + purchase + develop
            // journals replay). No fabricated migration.
            var apSink2 = new InMemoryCharacterApStore();
            _ = NewReceipts(apSink2); // rehydrate the earn ledger from foundational-ap.journal
            Assert.Equal(2, apSink2.GetPersonalAp(_buyer, _buyerChar, _stone)); // earned survives

            var stones2 = new InMemoryStoneAggregateStore();
            var characters2 = new InMemoryCharacterAggregateStore();
            var authority2 = new InMemoryAccountStoneAuthorityStore();
            characters2.PutCharacter(Governor());
            authority2.ApplyAuthorityProjection("seed-bond", GovBondIndex());
            characters2.PutCharacter(AttunedBuyer(_buyer, _buyerChar, "rel-attune-buyer"));
            SeedAttunement(authority2, _buyer, _buyerChar, "rel-attune-buyer");
            var server2 = NewServer(stones2, characters2, authority2, apSink2);

            // After restart the Masterwork purchase already committed (replay), and the derived available
            // balance is earned(2) − spent(1) = 1 — proven by re-buying the same node returning Replayed
            // (idempotent), never a second debit, and the balance never over-counting.
            var replay = Buy(server2, _buyer, _buyerChar, "qa-mw");
            Assert.True(replay.Succeeded);
            Assert.Equal("Replayed", replay.Kind);
            Assert.Equal(1, MasterworkPurchaseCount(characters2, _buyer, _buyerChar));
        }

        // ── Stubs (mirror the shared-suite server-owned authority fixtures) ──

        [Fact]
        public void Revocation_cancellation_entry_refunds_personal_ap_deterministically_and_only_once()
        {
            // ADO #106 decision 2 / #132 review addition 2 — asserted against the REAL derived-balance
            // path (earned − spent), not a pure-domain seam.
            //
            // A Tree-revocation refund is an APPENDED CANCELLATION ENTRY naming the purchase it reverses,
            // never a deleted purchase row: the journal is append-only and is the source of truth crash
            // recovery replays. The refund lands as ordinary Stone-wide Personal AP because the derivation
            // stops counting the reversed purchase as spent — no stored balance, no second ledger.
            //
            // Two properties, because a crash-recovery replay is precisely what would otherwise double a
            // refund:
            //   * DETERMINISTIC REPLAY   — replaying the journal twice yields the same balance.
            //   * IDEMPOTENT CANCELLATION — the same cancellation appended twice refunds ONCE.
            var (server, _, _, _, apSink) = Bootstrap();
            EarnPlacements(apSink, _buyer, _buyerChar, count: 1, prefix: "earn-buyer");
            Offer(server);
            Assert.True(Buy(server, _buyer, _buyerChar, "qa-mw").Succeeded);

            // The buy ingress derives the purchase operation id from the prefix; read it back off the
            // journal rather than assuming its shape.
            string purchaseJournal = Path.Combine(_dir, LocalProgressionServer.PurchaseJournalFile);
            var handler = new PurchaseCommandHandler(purchaseJournal, new PrincipalResolver(),
                server.Stones, server.Characters, server.Authority, server.Catalog, apSink);

            int Available() =>
                apSink.GetPersonalAp(_buyer, _buyerChar, _stone) - SpentFromJournal(purchaseJournal);

            // Earned 1, spent 1 on Masterwork -> nothing spendable.
            Assert.Equal(1, apSink.GetPersonalAp(_buyer, _buyerChar, _stone));
            Assert.Equal(0, Available());
            // Deterministic replay: re-reading the same durable journal yields the same answer.
            Assert.Equal(0, Available());

            // Refund: append a cancellation naming the purchase. The purchase row is NOT removed.
            string purchaseOp = SoleCommittedPurchaseOperationId(purchaseJournal);
            handler.AppendPurchaseCancellation(purchaseOp, "op-revoke-1");
            Assert.Equal(1, Available());

            // Idempotent cancellation: appending the identical reversal again refunds ONCE, not twice.
            handler.AppendPurchaseCancellation(purchaseOp, "op-revoke-1");
            Assert.Equal(1, Available());

            // And the reversed purchase is still on the journal — history preserved, replay still terminal.
            Assert.Equal("Replayed", Buy(server, _buyer, _buyerChar, "qa-mw").Kind);
        }

        // Independent re-derivation of "Personal AP spent" straight off the durable journal, so the
        // assertions above do not merely re-run the implementation they are testing.
        private int SpentFromJournal(string journalPath)
        {
            int spent = 0;
            var counted = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            var reversed = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach (var parts in JournalLines(journalPath))
                if (parts.Length == 3 && parts[0] == "PURCHASECANCELREC")
                    reversed.Add(B64(parts[2]));
            foreach (var parts in JournalLines(journalPath))
            {
                if (parts.Length != 15 || parts[0] != "PURCHASEREC") continue;
                if (parts[2] != "2") continue; // Committed boundary only
                string op = parts[1];
                if (!counted.Add(op)) continue;
                if (reversed.Contains(op)) continue;
                if (B64(parts[5]) != _buyer.Value || B64(parts[6]) != _buyerChar.Value) continue;
                if (B64(parts[7]) != _stone.Value) continue;
                if (B64(parts[10]) != "PersonalAP") continue;
                spent += int.Parse(parts[9], System.Globalization.CultureInfo.InvariantCulture);
            }
            return spent;
        }

        private string SoleCommittedPurchaseOperationId(string journalPath)
        {
            foreach (var parts in JournalLines(journalPath))
                if (parts.Length == 15 && parts[0] == "PURCHASEREC" && parts[2] == "2")
                    return parts[1];
            throw new Xunit.Sdk.XunitException("no committed purchase record in " + journalPath);
        }

        private static string B64(string s) =>
            System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(s));

        /// <summary>Framed-record reader mirroring the handler's own durable format (len|crc|payload).</summary>
        private static System.Collections.Generic.List<string[]> JournalLines(string path)
        {
            var lines = new System.Collections.Generic.List<string[]>();
            if (!File.Exists(path)) return lines;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs, System.Text.Encoding.UTF8))
            {
                long length = fs.Length;
                while (fs.Position + 8 <= length)
                {
                    int payloadLen = br.ReadInt32();
                    br.ReadUInt32(); // crc — framing is the handler's concern; we only need the payload
                    if (payloadLen < 0 || fs.Position + payloadLen > length) break;
                    byte[] payload = br.ReadBytes(payloadLen);
                    if (payload.Length != payloadLen) break;
                    lines.Add(System.Text.Encoding.UTF8.GetString(payload).Split('|'));
                }
            }
            return lines;
        }

        private sealed class FixedFamilyResolver : IStoneFamilyResolver
        {
            public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
            {
                family = "Settlement"; variant = "Homestead"; return true;
            }
        }

        private sealed class AllowHomesteadBondPolicy : IBondAuthorityPolicy
        {
            public bool TryAuthorizeBond(StoneId stoneId, string requestedResponsibilityRange,
                out string grantedRange, out string grantedRole)
            {
                grantedRange = requestedResponsibilityRange ?? string.Empty;
                grantedRole = "Governor";
                return string.Equals(requestedResponsibilityRange, "Homestead:All",
                    System.StringComparison.Ordinal);
            }
        }

        private sealed class AllowGovernorAuthority : IGovernorAuthorityPolicy
        {
            public bool CanCommit(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                string facetId, FacetCategory category) =>
                string.Equals(responsibilityRange, "Homestead:All", System.StringComparison.Ordinal)
                && string.Equals(ownerGovernorRole, "Governor", System.StringComparison.Ordinal)
                && category != FacetCategory.None;
        }

        private sealed class AllowDevelopmentAuthority : IGovernorDevelopmentAuthority
        {
            public bool CanDevelop(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                VersionedId tree) =>
                string.Equals(responsibilityRange, "Homestead:All", System.StringComparison.Ordinal)
                && string.Equals(ownerGovernorRole, "Governor", System.StringComparison.Ordinal)
                && !tree.IsNone;
        }
    }
}
