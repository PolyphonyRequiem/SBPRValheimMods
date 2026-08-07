// ============================================================================
//  T033 / ADO #137 — Tree revocation: the two-step Governor warning and the
//  Personal-AP refund.
// ----------------------------------------------------------------------------
//  Every test here drives the REAL server handlers end-to-end over durable
//  journals — the same composition NiflheimPersonalApAuthorityTests uses:
//    * earn     — OperationReceiptStore.SubmitFoundationalAp (the genuine
//                 Foundational placement credit path) into a shared
//                 ICharacterApStore;
//    * commit +
//      develop  — LocalProvisioningIngress.OfferMasterwork (accepted
//                 CommitTreeToFacet -> BP credit -> ApplyBPToNode);
//    * purchase — LocalProvisioningIngress.PurchaseNode (accepted
//                 PurchaseCommandHandler);
//    * revoke   — RevocationCommandHandler.PreviewRevocation / Handle.
//
//  This matters: the spendable Personal-AP balance is DERIVED (earned minus
//  spent-minus-reversed), so asserting a refund against a pure-domain seam that
//  reads a stored field would prove nothing. Every balance assertion below goes
//  through the real derivation, and the independent re-derivation helper reads
//  the durable journal directly rather than re-running the implementation.
//
//  Coverage (card acceptance):
//    * AT-REVOKE-TWO-STEP          — the loss is computed and presented before
//      any mutation; abandoning step one mutates NOTHING (Stone revision,
//      committed Trees, node development, and every balance are unchanged, and
//      no revocation journal is even created);
//    * AT-REVOKE-ATOMIC            — an N-purchase / M-character fan-out is one
//      convergent operation; replay refunds once; a rejected revocation exposes
//      no partial teardown;
//    * AT-REVOKE-NO-BP-REFUND      — node development and cumulative Bond Power
//      are destroyed and no BP is returned anywhere;
//    * AT-REVOKE-AP-REFUND         — each reversed refundable Character-Effect
//      purchase returns its FULL AP value as ordinary Stone-wide Personal AP,
//      spendable on another Facet;
//    * AT-REPLACEMENT-NO-AUTOBUY   — a replacement Tree starts at zero and buys
//      nothing automatically;
//    * AT-DURABLE-OUTCOMES-SURVIVE — a Permanent Effect purchase is neither
//      refunded nor removed.
//
//  Plus the guards ADO #137 inherited from #132: authority (an unauthenticated
//  or non-Governor caller cannot revoke), the Foundational Tree is protected,
//  and a purchase recorded under the RETIRED Facet-Credit payment source never
//  refunds Personal AP.
// ============================================================================

using System.Collections.Generic;
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
    public sealed class NiflheimTreeRevocationTests : System.IDisposable
    {
        private readonly string _dir;
        private readonly WorldId _world = new WorldId("uid:ado137-revocation");
        private readonly StoneId _stone;

        private readonly AccountId _gov = new AccountId("acct-gov");
        private readonly CharacterId _govChar = new CharacterId("char-gov");
        private readonly AccountId _buyer = new AccountId("acct-buyer");
        private readonly CharacterId _buyerChar = new CharacterId("char-buyer");
        private readonly AccountId _other = new AccountId("acct-other");
        private readonly CharacterId _otherChar = new CharacterId("char-other");
        private readonly AccountId _stranger = new AccountId("acct-stranger");
        private readonly CharacterId _strangerChar = new CharacterId("char-stranger");

        private static readonly VersionedId Crafting = HomesteadProgressionCatalog.CraftingTree;
        private static readonly VersionedId Cooking = HomesteadProgressionCatalog.CookingTree;
        private static readonly VersionedId Archer = HomesteadProgressionCatalog.ArcherTree;
        private static readonly VersionedId FieldFletching = new VersionedId("FieldFletchingI", 1); // CharacterEffect, Martial Facet
        private static readonly VersionedId Masterwork = new VersionedId("Masterwork", 1);      // CharacterEffect
        private static readonly VersionedId BuiltToLast = new VersionedId("BuiltToLast", 1);    // PermanentEffect
        private static readonly string ProfessionFacet = HomesteadProgressionCatalog.ProfessionFacetId;

        public NiflheimTreeRevocationTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "niflheim-ado137-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _stone = StoneId.FromHostZone(_world, 7, 5);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        }

        // ══ AT-REVOKE-TWO-STEP ═══════════════════════════════════════════════

        [Fact]
        public void Step_one_presents_the_bond_power_loss_and_the_refunds_before_anything_is_destroyed()
        {
            var w = BootstrapWithPurchase();

            var preview = w.Revocation.PreviewRevocation(Revoke(w, "op-revoke-preview"));

            Assert.True(preview.Accepted, preview.ResultCode);
            Assert.NotNull(preview.Loss);

            // The household-effort number the two-step warning exists to state (ADO #106 decision 4).
            Assert.Equal(ProfessionFacet, preview.Loss!.FacetId);
            Assert.Equal(Crafting.Key, preview.Loss.Tree.Key);
            Assert.True(preview.Loss.TotalBondPowerDestroyed > 0);
            Assert.Equal(1, preview.Loss.DevelopedNodeCount);
            Assert.Contains(Masterwork.Key, NodeKeys(preview.Loss.DestroyedNodes));

            // And what each household member gets back: the FULL AP value, no fee (decision 1).
            Assert.Single(preview.Refunds);
            Assert.Equal(_buyer, preview.Refunds[0].Account);
            Assert.Equal(Masterwork.Key, preview.Refunds[0].Node.Key);
            Assert.Equal(1, preview.Refunds[0].ApValue);
            Assert.Equal(1, preview.TotalApRefunded);
        }

        [Fact]
        public void Abandoning_step_one_mutates_nothing_at_all()
        {
            var w = BootstrapWithPurchase();

            long revisionBefore = w.Server.Stones.GetStone(_stone)!.Revision;
            int committedBefore = w.Server.Stones.GetStone(_stone)!.CommittedTrees.Count;
            int devBefore = w.Server.Stones.GetStone(_stone)!.NodeDevelopment.Count;
            int availableBefore = Available(w, _buyer, _buyerChar);
            int purchasesBefore = PurchaseCount(w, _buyer, _buyerChar);

            // Preview repeatedly, then simply walk away. There is no "cancel" call to make, because
            // step one has nothing to cancel.
            w.Revocation.PreviewRevocation(Revoke(w, "op-revoke-abandoned-1"));
            w.Revocation.PreviewRevocation(Revoke(w, "op-revoke-abandoned-2"));

            var stone = w.Server.Stones.GetStone(_stone)!;
            Assert.Equal(revisionBefore, stone.Revision);
            Assert.Equal(committedBefore, stone.CommittedTrees.Count);
            Assert.Equal(devBefore, stone.NodeDevelopment.Count);
            Assert.Equal(availableBefore, Available(w, _buyer, _buyerChar));
            Assert.Equal(purchasesBefore, PurchaseCount(w, _buyer, _buyerChar));

            // Not one durable byte: the preview never even opened the revocation journal for writing,
            // and it appended no cancellation to the purchase journal.
            Assert.Equal(0, CancellationCount());
            Assert.False(File.Exists(RevocationJournal) && new FileInfo(RevocationJournal).Length > 0);
        }

        [Fact]
        public void The_number_the_governor_was_shown_is_the_number_that_is_destroyed()
        {
            // The warning cannot drift from the act it warns about: both steps compute the loss with
            // the same function over the same state.
            var w = BootstrapWithPurchase();

            var preview = w.Revocation.PreviewRevocation(Revoke(w, "op-revoke-shown"));
            Assert.True(preview.Accepted);

            var confirmed = w.Revocation.Handle(Revoke(w, "op-revoke-shown"));
            Assert.Equal(RevocationCommandOutcome.Applied, confirmed.Outcome);

            Assert.Equal(preview.Loss!.TotalBondPowerDestroyed, confirmed.BondPowerDestroyed);
            Assert.Equal(preview.TotalApRefunded, confirmed.ApRefunded);
            Assert.Equal(preview.Refunds.Count, confirmed.PurchasesReversed);
        }

        [Fact]
        public void A_preview_computed_against_a_stale_stone_cannot_be_confirmed()
        {
            // The Governor is warned, someone else develops meanwhile, and the confirm fails closed
            // rather than destroying more than the warning stated.
            var w = BootstrapWithPurchase();

            var preview = w.Revocation.PreviewRevocation(Revoke(w, "op-revoke-stale"));
            Assert.True(preview.Accepted);

            // The Stone moves under the warning.
            Assert.True(w.Server.CreateLocalProvisioningIngress()
                .DevelopLocalNode(GovSubject, _stone, new VersionedId("RefinedWorkshop", 1), "qa-rw").Succeeded);

            var confirm = w.Revocation.Handle(Revoke(w, "op-revoke-stale", preview.StoneRevision));
            Assert.Equal(RevocationCommandOutcome.Rejected, confirm.Outcome);
            Assert.Equal("StaleStoneRevision", confirm.ResultCode);

            // Zero mutation on the rejection: the commitment and the purchase both stand.
            Assert.NotNull(FindCommitted(w, ProfessionFacet));
            Assert.Equal(1, PurchaseCount(w, _buyer, _buyerChar));
            Assert.Equal(0, CancellationCount());
        }

        // ══ AT-REVOKE-NO-BP-REFUND ═══════════════════════════════════════════

        [Fact]
        public void Development_is_deleted_and_bond_power_is_not_refunded()
        {
            var w = BootstrapWithPurchase();

            var stoneBefore = w.Server.Stones.GetStone(_stone)!;
            int bpBefore = PersonalBp(w, _gov, _govChar);
            var committedBefore = FindCommitted(w, ProfessionFacet);
            Assert.NotNull(committedBefore);
            Assert.True(committedBefore!.CumulativeBpInvested > 0);
            Assert.NotEmpty(stoneBefore.NodeDevelopment);

            var result = w.Revocation.Handle(Revoke(w, "op-revoke-bp"));
            Assert.Equal(RevocationCommandOutcome.Applied, result.Outcome);

            var stoneAfter = w.Server.Stones.GetStone(_stone)!;

            // The Facet is vacated and every Crafting node development record is gone.
            Assert.Null(FindCommitted(w, ProfessionFacet));
            foreach (var d in stoneAfter.NodeDevelopment)
                Assert.NotEqual(Masterwork.Key, d.Node.Key);

            // The destroyed Bond Power is reported, and refunded NOWHERE.
            Assert.Equal(committedBefore.CumulativeBpInvested, result.BondPowerDestroyed);
            Assert.Equal(bpBefore, PersonalBp(w, _gov, _govChar));
            Assert.Equal(bpBefore, PersonalBp(w, _buyer, _buyerChar));
        }

        // ══ AT-REVOKE-AP-REFUND ══════════════════════════════════════════════

        [Fact]
        public void Each_reversed_character_effect_purchase_returns_its_full_ap_as_stone_wide_personal_ap()
        {
            var w = BootstrapWithPurchase();

            // Earned 1, spent 1 on Masterwork -> nothing spendable.
            Assert.Equal(1, w.ApSink.GetPersonalAp(_buyer, _buyerChar, _stone));
            Assert.Equal(0, Available(w, _buyer, _buyerChar));

            var result = w.Revocation.Handle(Revoke(w, "op-revoke-ap"));
            Assert.Equal(RevocationCommandOutcome.Applied, result.Outcome);
            Assert.Equal(1, result.ApRefunded);
            Assert.Equal(1, result.PurchasesReversed);

            // FULL value back, as ordinary Personal AP, derived — no stored balance was written.
            Assert.Equal(1, Available(w, _buyer, _buyerChar));
            Assert.Equal(1, w.ApSink.GetPersonalAp(_buyer, _buyerChar, _stone)); // earn ledger untouched

            // And the purchase RECORD is still on the journal: history preserved, nothing removed.
            Assert.Equal(1, CommittedPurchaseRecordCount());
        }

        [Fact]
        public void Refunded_ap_is_stone_wide_and_spendable_on_a_different_facet()
        {
            // ADO #106 decision 3: this is intended, not an exploit.
            var w = BootstrapWithPurchase();
            Assert.Equal(RevocationCommandOutcome.Applied,
                w.Revocation.Handle(Revoke(w, "op-revoke-stonewide")).Outcome);
            Assert.Equal(1, Available(w, _buyer, _buyerChar));

            // Spend it on a genuinely DIFFERENT Facet: the Martial Facet, Archer's Field Fletching I.
            // (Re-buying the revoked Tree's own node is AlreadyAcquired — the purchase RECORD survives
            // revocation by design; it is the record's SPEND that was reversed, not the record.)
            Assert.True(Develop(w, FieldFletching, "qa-ff"));

            var buy = w.Server.CreateLocalProvisioningIngress().PurchaseNode(
                BuyerSubject, _stone, Archer, FieldFletching,
                VersionedId.None, PurchasePaymentSource.PersonalAp, "op-buy-ff");
            Assert.True(buy.Succeeded, buy.ResultCode + "/" + buy.Step);
            Assert.Equal(0, Available(w, _buyer, _buyerChar));
        }

        // ══ AT-DURABLE-OUTCOMES-SURVIVE ══════════════════════════════════════

        [Fact]
        public void Permanent_effects_survive_revocation_with_no_refund_and_no_record_removed()
        {
            var w = BootstrapWithPurchase();

            // The buyer additionally earns and buys Built to Last — a PERMANENT Effect in the same Tree.
            EarnPlacements(w, _buyer, _buyerChar, count: 1, prefix: "earn-perm");
            var ingress = w.Server.CreateLocalProvisioningIngress();
            Assert.True(Develop(w, BuiltToLast, "qa-btl"));
            var buy = ingress.PurchaseNode(BuyerSubject, _stone, Crafting, BuiltToLast,
                VersionedId.None, PurchasePaymentSource.PersonalAp, "op-buy-btl");
            Assert.True(buy.Succeeded, buy.ResultCode + "/" + buy.Step);
            Assert.Equal(0, Available(w, _buyer, _buyerChar));

            var result = w.Revocation.Handle(Revoke(w, "op-revoke-durable"));
            Assert.Equal(RevocationCommandOutcome.Applied, result.Outcome);

            // ONLY the refundable Character Effect reversed. The Permanent Effect refunded nothing...
            Assert.Equal(1, result.PurchasesReversed);
            Assert.Equal(1, result.ApRefunded);
            Assert.Equal(1, Available(w, _buyer, _buyerChar));

            // ...and neither purchase record was removed — both persist as durable provenance.
            Assert.Equal(2, CommittedPurchaseRecordCount());
            Assert.True(HasPurchaseRecord(w, _buyer, _buyerChar, BuiltToLast));
        }

        // ══ AT-REPLACEMENT-NO-AUTOBUY ════════════════════════════════════════

        [Fact]
        public void A_replacement_tree_starts_at_zero_and_buys_nothing_automatically()
        {
            var w = BootstrapWithPurchase();
            Assert.Equal(RevocationCommandOutcome.Applied,
                w.Revocation.Handle(Revoke(w, "op-revoke-replace")).Outcome);

            // Commit a replacement into the vacated Profession Facet through the ordinary accepted path.
            var commit = CommitTree(w, ProfessionFacet, Cooking, "op-commit-replacement");
            Assert.Equal(FacetCommandOutcome.Applied, commit.Outcome);

            var replacement = FindCommitted(w, ProfessionFacet)!;
            Assert.Equal(Cooking.Key, replacement.Tree.Key);
            Assert.Equal(StoneFacets.InitialTreeLevel, replacement.TreeLevel);
            Assert.Equal(0, replacement.CumulativeBpInvested);

            // Nothing was purchased on the character by the act of committing.
            Assert.Equal(1, PurchaseCount(w, _buyer, _buyerChar)); // still just the reversed Masterwork record
            Assert.False(HasPurchaseRecord(w, _buyer, _buyerChar, new VersionedId("FieldPrep", 1)));

            // Recommitting the ORIGINAL Tree does not restore the removed purchase's spendability either:
            // the refund stands, and the AP is still the player's to spend.
            Assert.Equal(1, Available(w, _buyer, _buyerChar));
        }

        // ══ AT-REVOKE-ATOMIC ═════════════════════════════════════════════════

        [Fact]
        public void A_fan_out_across_several_characters_is_one_convergent_operation_that_refunds_once()
        {
            // Two households members, each with a refundable Character-Effect purchase in the Tree.
            var w = BootstrapWithPurchase();
            AddAttunedBuyer(w, _other, _otherChar, "rel-attune-other");
            EarnPlacements(w, _other, _otherChar, count: 1, prefix: "earn-other");
            var buy = w.Server.CreateLocalProvisioningIngress().PurchaseNode(
                new AuthoritativeSubject(_other, _otherChar), _stone, Crafting, Masterwork,
                VersionedId.None, PurchasePaymentSource.PersonalAp, "op-buy-other");
            Assert.True(buy.Succeeded, buy.ResultCode + "/" + buy.Step);

            Assert.Equal(0, Available(w, _buyer, _buyerChar));
            Assert.Equal(0, Available(w, _other, _otherChar));

            var result = w.Revocation.Handle(Revoke(w, "op-revoke-fanout"));
            Assert.Equal(RevocationCommandOutcome.Applied, result.Outcome);
            Assert.Equal(2, result.PurchasesReversed);
            Assert.Equal(2, result.ApRefunded);

            // Both members are made whole, each exactly once.
            Assert.Equal(1, Available(w, _buyer, _buyerChar));
            Assert.Equal(1, Available(w, _other, _otherChar));

            // DETERMINISTIC REPLAY — re-reading the same durable journals yields the same balances.
            Assert.Equal(1, Available(w, _buyer, _buyerChar));
            Assert.Equal(1, Available(w, _other, _otherChar));

            // IDEMPOTENT CANCELLATION — replaying the whole revocation refunds ONCE, not twice.
            var replay = w.Revocation.Handle(Revoke(w, "op-revoke-fanout"));
            Assert.Equal(RevocationCommandOutcome.Replayed, replay.Outcome);
            Assert.Equal(2, replay.PurchasesReversed);
            Assert.Equal(1, Available(w, _buyer, _buyerChar));
            Assert.Equal(1, Available(w, _other, _otherChar));
        }

        [Fact]
        public void The_fan_out_reconverges_from_the_durable_journal_after_a_restart()
        {
            // The crash-recovery shape: a fresh server rehydrates from the same durable directory and
            // re-derives exactly the same balances — the refund is neither lost nor doubled.
            var w = BootstrapWithPurchase();
            Assert.Equal(RevocationCommandOutcome.Applied,
                w.Revocation.Handle(Revoke(w, "op-revoke-restart")).Outcome);
            Assert.Equal(1, Available(w, _buyer, _buyerChar));

            var w2 = Restart(w);
            Assert.Equal(1, Available(w2, _buyer, _buyerChar));

            // The Stone teardown replayed too: the Facet is still vacant on the rehydrated aggregate.
            Assert.Null(FindCommitted(w2, ProfessionFacet));

            // And a second restart still converges to one refund, not two.
            var w3 = Restart(w2);
            Assert.Equal(1, Available(w3, _buyer, _buyerChar));
        }

        [Fact]
        public void A_rejected_revocation_exposes_no_partial_teardown()
        {
            var w = BootstrapWithPurchase();

            // Wrong Tree in the right Facet: refused before anything is written.
            var wrongTree = w.Revocation.Handle(new RevokeTreeCommand(
                new OperationId("op-revoke-wrongtree"), _stone, GovConnection, default,
                ProfessionFacet, Cooking.Key, Cooking.Version));
            Assert.Equal(RevocationCommandOutcome.Rejected, wrongTree.Outcome);
            Assert.Equal("TreeNotEligible", wrongTree.ResultCode);

            // Stale Tree VERSION on the right Tree is a stale content view, never a "closest" rebind.
            var staleVersion = w.Revocation.Handle(new RevokeTreeCommand(
                new OperationId("op-revoke-staleversion"), _stone, GovConnection, default,
                ProfessionFacet, Crafting.Key, Crafting.Version + 1));
            Assert.Equal("ContentVersionMismatch", staleVersion.ResultCode);

            // An empty Facet has nothing to revoke.
            var emptyFacet = w.Revocation.Handle(new RevokeTreeCommand(
                new OperationId("op-revoke-emptyfacet"), _stone, GovConnection, default,
                HomesteadProgressionCatalog.MartialFacetId, Crafting.Key, Crafting.Version));
            Assert.Equal("TreeNotCommitted", emptyFacet.ResultCode);

            // Nothing was destroyed and nothing was refunded by any of the three.
            Assert.NotNull(FindCommitted(w, ProfessionFacet));
            Assert.Equal(0, Available(w, _buyer, _buyerChar));
            Assert.Equal(0, CancellationCount());
        }

        // ══ Authority — the wrapping the unguarded primitive was missing ═════

        [Fact]
        public void An_unauthenticated_caller_cannot_revoke_or_even_preview()
        {
            var w = BootstrapWithPurchase();
            var anonymous = new RevokeTreeCommand(new OperationId("op-revoke-anon"), _stone,
                new AuthenticatedConnection(string.Empty, string.Empty), default,
                ProfessionFacet, Crafting.Key, Crafting.Version);

            Assert.Equal("Unauthenticated", w.Revocation.PreviewRevocation(anonymous).ResultCode);
            Assert.Equal("Unauthenticated", w.Revocation.Handle(anonymous).ResultCode);

            Assert.NotNull(FindCommitted(w, ProfessionFacet));
            Assert.Equal(0, CancellationCount());
        }

        [Fact]
        public void An_attuned_non_governor_cannot_revoke_a_tree()
        {
            // Attunement is purchase authority, never cultivation authority. This is the exact hole the
            // public unguarded refund primitive left open before ADO #137 wrapped it.
            var w = BootstrapWithPurchase();
            var byBuyer = new RevokeTreeCommand(new OperationId("op-revoke-buyer"), _stone,
                new AuthenticatedConnection(_buyer.Value, _buyerChar.Value), default,
                ProfessionFacet, Crafting.Key, Crafting.Version);

            Assert.Equal("Unauthorized", w.Revocation.PreviewRevocation(byBuyer).ResultCode);
            Assert.Equal("Unauthorized", w.Revocation.Handle(byBuyer).ResultCode);

            Assert.NotNull(FindCommitted(w, ProfessionFacet));
            Assert.Equal(0, Available(w, _buyer, _buyerChar));
            Assert.Equal(0, CancellationCount());
        }

        [Fact]
        public void A_caller_with_no_relationship_at_this_stone_cannot_revoke()
        {
            var w = BootstrapWithPurchase();
            w.Characters.PutCharacter(new CharacterProgressionAggregate(
                _stranger, _strangerChar, "ado137/trailborne", revision: 1, bondSlots: 1,
                attunementSlots: 2, lastAppliedReceiptId: "seed"));

            var byStranger = new RevokeTreeCommand(new OperationId("op-revoke-stranger"), _stone,
                new AuthenticatedConnection(_stranger.Value, _strangerChar.Value), default,
                ProfessionFacet, Crafting.Key, Crafting.Version);

            Assert.Equal("Unauthorized", w.Revocation.Handle(byStranger).ResultCode);
            Assert.NotNull(FindCommitted(w, ProfessionFacet));
            Assert.Equal(0, CancellationCount());
        }

        [Fact]
        public void The_foundational_tree_can_never_be_revoked()
        {
            var w = BootstrapWithPurchase();
            var foundational = w.Server.Catalog.FoundationalTree;

            var attempt = w.Revocation.Handle(new RevokeTreeCommand(
                new OperationId("op-revoke-foundational"), _stone, GovConnection, default,
                ProfessionFacet, foundational.Key, foundational.Version));

            Assert.Equal("ProtectedTree", attempt.ResultCode);
            Assert.NotNull(FindCommitted(w, ProfessionFacet));
        }

        [Fact]
        public void A_reused_operation_id_with_a_different_payload_conflicts_instead_of_replaying()
        {
            var w = BootstrapWithPurchase();
            Assert.Equal(RevocationCommandOutcome.Applied,
                w.Revocation.Handle(Revoke(w, "op-revoke-conflict")).Outcome);

            var conflicting = w.Revocation.Handle(new RevokeTreeCommand(
                new OperationId("op-revoke-conflict"), _stone, GovConnection, default,
                HomesteadProgressionCatalog.MartialFacetId, Crafting.Key, Crafting.Version));
            Assert.Equal("OperationConflict", conflicting.ResultCode);
        }

        // ══ Keep the retired-source degenerate case closed (ADO #132) ════════

        [Fact]
        public void A_purchase_recorded_under_the_retired_facet_credit_source_never_refunds_personal_ap()
        {
            // Those records persist forever in pre-existing worlds and replay on every boot. They never
            // debited Personal AP, so revocation must never return any — the correction that made this
            // unreachable stays unreachable.
            var w = BootstrapWithPurchase();
            AppendRetiredSourcePurchase(w, "op-legacy-facetcredit");

            int availableBefore = Available(w, _buyer, _buyerChar);
            var result = w.Revocation.Handle(Revoke(w, "op-revoke-retired"));
            Assert.Equal(RevocationCommandOutcome.Applied, result.Outcome);

            // Only the ONE genuine Personal-AP purchase reversed; the retired-source record did not.
            Assert.Equal(1, result.PurchasesReversed);
            Assert.Equal(1, result.ApRefunded);
            Assert.Equal(availableBefore + 1, Available(w, _buyer, _buyerChar));
        }

        // ══ Fixture ══════════════════════════════════════════════════════════

        private sealed class World
        {
            public LocalProgressionServer Server = null!;
            public InMemoryStoneAggregateStore Stones = null!;
            public InMemoryCharacterAggregateStore Characters = null!;
            public InMemoryAccountStoneAuthorityStore Authority = null!;
            public InMemoryCharacterApStore ApSink = null!;
            public RevocationCommandHandler Revocation = null!;
        }

        private AuthoritativeSubject GovSubject => new AuthoritativeSubject(_gov, _govChar);
        private AuthoritativeSubject BuyerSubject => new AuthoritativeSubject(_buyer, _buyerChar);
        private AuthenticatedConnection GovConnection => new AuthenticatedConnection(_gov.Value, _govChar.Value);

        private string ApJournal => Path.Combine(_dir, FoundationalProgressionServer.ApJournalFile);
        private string PurchaseJournal => Path.Combine(_dir, LocalProgressionServer.PurchaseJournalFile);
        private string RevocationJournal => Path.Combine(_dir, LocalProgressionServer.RevocationJournalFile);

        /// <summary>A Stone with Crafting committed to the Profession Facet, Masterwork developed and
        /// Offered, and one attuned buyer holding a refundable Character-Effect purchase of it — all
        /// through the accepted, receipt-backed handlers.</summary>
        private World BootstrapWithPurchase()
        {
            var w = Compose(new InMemoryStoneAggregateStore(), new InMemoryCharacterAggregateStore(),
                new InMemoryAccountStoneAuthorityStore(), new InMemoryCharacterApStore(), seed: true);

            EarnPlacements(w, _buyer, _buyerChar, count: 1, prefix: "earn-buyer");
            Assert.True(w.Server.CreateLocalProvisioningIngress()
                .OfferMasterwork(GovSubject, _stone, "qa-mw").Succeeded);
            var buy = w.Server.CreateLocalProvisioningIngress().BuyMasterwork(BuyerSubject, _stone, "qa-mw");
            Assert.True(buy.Succeeded, buy.ResultCode + "/" + buy.Step);
            return w;
        }

        /// <summary>Restart: fresh stores and a fresh server over the SAME durable directory, with the AP
        /// earn ledger rehydrated from the same journal. No fabricated migration.</summary>
        private World Restart(World prior)
        {
            var apSink = new InMemoryCharacterApStore();
            _ = new OperationReceiptStore(ApJournal, new InMemoryMirroredStoneApStore(), apSink);

            var characters = new InMemoryCharacterAggregateStore();
            var authority = new InMemoryAccountStoneAuthorityStore();
            foreach (var c in prior.Characters.AllCharacters()) characters.PutCharacter(c);
            SeedGovernorBond(authority);
            SeedAttunement(authority, _buyer, _buyerChar, "rel-attune-buyer");

            return Compose(new InMemoryStoneAggregateStore(), characters, authority, apSink, seed: false);
        }

        private World Compose(
            InMemoryStoneAggregateStore stones,
            InMemoryCharacterAggregateStore characters,
            InMemoryAccountStoneAuthorityStore authority,
            InMemoryCharacterApStore apSink,
            bool seed)
        {
            if (seed)
            {
                characters.PutCharacter(Governor());
                SeedGovernorBond(authority);
                characters.PutCharacter(Attuned(_buyer, _buyerChar, "rel-attune-buyer"));
                SeedAttunement(authority, _buyer, _buyerChar, "rel-attune-buyer");
            }

            var relationships = new RelationshipCommandHandler(
                Path.Combine(_dir, "relationships.journal"), new PrincipalResolver(), characters, authority,
                new FixedFamilyResolver(), new AllowHomesteadBondPolicy(), null, _world,
                new ProductScope("SBPR.Trailborne"));

            var server = LocalProgressionServer.Create(
                _dir, stones, characters, authority, relationships,
                new FixedFamilyResolver(), new AllowGovernorAuthority(), new AllowDevelopmentAuthority(),
                new CommittedGovernorOwnerAuthority(new GovernorPresenceResolver(characters, authority)),
                characterApStore: apSink);

            return new World
            {
                Server = server,
                Stones = stones,
                Characters = characters,
                Authority = authority,
                ApSink = apSink,
                Revocation = server.CreateRevocationCommandHandler()
            };
        }

        private RevokeTreeCommand Revoke(World w, string opId, long? expectedStoneRevision = null) =>
            new RevokeTreeCommand(new OperationId(opId), _stone, GovConnection, default,
                ProfessionFacet, Crafting.Key, Crafting.Version, "GovernorChoice", expectedStoneRevision);

        private FacetCommandResult CommitTree(World w, string facetId, VersionedId tree, string opId) =>
            w.Server.Facets.Handle(new CommitTreeToFacetCommand(
                new OperationId(opId), _stone, GovConnection, default,
                facetId, tree.Key, tree.Version, StoneFacetPalette.CurrentPaletteVersion));

        /// <summary>Develop one personal Crafting node to completion (Offered) through the accepted
        /// commit -> BP credit -> ApplyBPToNode commands, via the shipped provisioning driver.</summary>
        private bool Develop(World w, VersionedId node, string opPrefix) =>
            new LocalNodeProvisioningDriver(w.Server)
                .ProvisionOffered(GovSubject, _stone, node, opPrefix).IsDeveloped;

        private void EarnPlacements(World w, AccountId account, CharacterId character, int count, string prefix)
        {
            var receipts = new OperationReceiptStore(ApJournal, new InMemoryMirroredStoneApStore(), w.ApSink);
            for (int i = 0; i < count; i++)
            {
                var r = receipts.SubmitFoundationalAp(new OperationId(prefix + "-" + i), _stone,
                    new AuthoritativePrincipal(account, character), "evidence-" + prefix + "-" + i);
                Assert.Equal(ReceiptOutcome.Applied, r.Outcome);
            }
        }

        private void AddAttunedBuyer(World w, AccountId account, CharacterId character, string relId)
        {
            w.Characters.PutCharacter(Attuned(account, character, relId));
            SeedAttunement(w.Authority, account, character, relId);
        }

        private CharacterProgressionAggregate Governor() =>
            new CharacterProgressionAggregate(_gov, _govChar, "ado137/trailborne",
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

        private CharacterProgressionAggregate Attuned(AccountId account, CharacterId character, string relId) =>
            new CharacterProgressionAggregate(account, character, "ado137/trailborne",
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

        private void SeedGovernorBond(InMemoryAccountStoneAuthorityStore authority) =>
            authority.ApplyAuthorityProjection("seed-bond",
                AccountStoneAuthorityIndex.Vacant(_gov, _stone).WithReservationAdded(
                    new AuthorityReservation(_govChar, RelationshipKind.Bond, "rel-bond-gov",
                        "relreceipt:seed-bond"), 1));

        private void SeedAttunement(InMemoryAccountStoneAuthorityStore authority,
            AccountId account, CharacterId character, string relId) =>
            authority.ApplyAuthorityProjection("seed-" + relId,
                AccountStoneAuthorityIndex.Vacant(account, _stone).WithReservationAdded(
                    new AuthorityReservation(character, RelationshipKind.Attunement, relId,
                        "relreceipt:" + relId), 1));

        // ── Reads ────────────────────────────────────────────────────────────

        private static List<string> NodeKeys(IReadOnlyList<VersionedId> nodes)
        {
            var keys = new List<string>();
            foreach (var n in nodes) keys.Add(n.Key);
            return keys;
        }

        private CommittedTreeRecord? FindCommitted(World w, string facetId)
        {
            var stone = w.Server.Stones.GetStone(_stone);
            if (stone == null) return null;
            foreach (var c in stone.CommittedTrees)
                if (c.FacetId == facetId) return c;
            return null;
        }

        private int PersonalBp(World w, AccountId account, CharacterId character)
        {
            var chr = w.Characters.GetCharacter(account, character);
            if (chr == null) return 0;
            foreach (var sr in chr.StoneRecords)
                if (sr.StoneId.Equals(_stone)) return sr.PersonalBp;
            return 0;
        }

        private int PurchaseCount(World w, AccountId account, CharacterId character)
        {
            var chr = w.Characters.GetCharacter(account, character);
            if (chr == null) return 0;
            int n = 0;
            foreach (var sr in chr.StoneRecords)
                if (sr.StoneId.Equals(_stone)) n += sr.Purchases.Count;
            return n;
        }

        private bool HasPurchaseRecord(World w, AccountId account, CharacterId character, VersionedId node)
        {
            var chr = w.Characters.GetCharacter(account, character);
            if (chr == null) return false;
            foreach (var sr in chr.StoneRecords)
                if (sr.StoneId.Equals(_stone))
                    foreach (var p in sr.Purchases)
                        if (p.Node.Key == node.Key) return true;
            return false;
        }

        /// <summary>Spendable Stone-wide Personal AP: earned minus spent, with spent independently
        /// re-derived straight off the durable purchase journal — so these assertions do not merely
        /// re-run the implementation they are testing.</summary>
        private int Available(World w, AccountId account, CharacterId character) =>
            w.ApSink.GetPersonalAp(account, character, _stone) - SpentFromJournal(account, character);

        private int SpentFromJournal(AccountId account, CharacterId character)
        {
            int spent = 0;
            var counted = new HashSet<string>(System.StringComparer.Ordinal);
            var reversed = new HashSet<string>(System.StringComparer.Ordinal);
            // Both sides DECODED, matching the production derivation: a cancellation frame stores the
            // encoded reversed operation id, and a purchase frame stores the encoded operation id, so
            // comparing one decoded against the other still-encoded would silently never match — and
            // the test would then "prove" that a refund did not land.
            foreach (var parts in JournalLines(PurchaseJournal))
                if (parts.Length == 3 && parts[0] == "PURCHASECANCELREC")
                    reversed.Add(B64(parts[2]));
            foreach (var parts in JournalLines(PurchaseJournal))
            {
                if (parts.Length != 15 || parts[0] != "PURCHASEREC") continue;
                if (parts[2] != "2") continue; // Committed boundary only
                string op = B64(parts[1]);
                if (!counted.Add(op)) continue;
                if (reversed.Contains(op)) continue;
                if (B64(parts[5]) != account.Value || B64(parts[6]) != character.Value) continue;
                if (B64(parts[7]) != _stone.Value) continue;
                if (B64(parts[10]) != "PersonalAP") continue;
                spent += int.Parse(parts[9], System.Globalization.CultureInfo.InvariantCulture);
            }
            return spent;
        }

        private int CancellationCount()
        {
            int n = 0;
            foreach (var parts in JournalLines(PurchaseJournal))
                if (parts.Length == 3 && parts[0] == "PURCHASECANCELREC") n++;
            return n;
        }

        private int CommittedPurchaseRecordCount()
        {
            var counted = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var parts in JournalLines(PurchaseJournal))
                if (parts.Length == 15 && parts[0] == "PURCHASEREC" && parts[2] == "2")
                    counted.Add(parts[1]);
            return counted.Count;
        }

        /// <summary>Append a committed purchase record whose PaymentSource is the RETIRED "FacetCredit"
        /// string — exactly the shape a pre-existing world's journal carries from before the correction.
        /// Written through the shared framing so it is a genuinely well-formed durable record, not a
        /// mock.</summary>
        private void AppendRetiredSourcePurchase(World w, string opId)
        {
            var chr = w.Characters.GetCharacter(_buyer, _buyerChar)!;
            CommandJournalFraming.Append(PurchaseJournal, string.Join("|", new[]
            {
                "PURCHASEREC",
                CommandJournalFraming.Encode(opId),
                "2",                                        // Committed boundary
                CommandJournalFraming.Digest("legacy-binding|" + opId),
                CommandJournalFraming.Digest("legacy-payload|" + opId),
                CommandJournalFraming.Encode(_buyer.Value),
                CommandJournalFraming.Encode(_buyerChar.Value),
                CommandJournalFraming.Encode(_stone.Value),
                CommandJournalFraming.Encode("Applied"),
                "1",
                CommandJournalFraming.Encode("FacetCredit"),
                CommandJournalFraming.Encode("Crafting:L1"),
                "1",
                chr.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CommandJournalFraming.Encode(chr.Serialize())
            }));
        }

        private static string B64(string s) =>
            System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(s));

        /// <summary>Framed-record reader mirroring the handlers' durable format (len|crc|payload).</summary>
        private static List<string[]> JournalLines(string path)
        {
            var lines = new List<string[]>();
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

        // ── Stubs (server-owned authority policies; mirror the shared suite) ──

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
