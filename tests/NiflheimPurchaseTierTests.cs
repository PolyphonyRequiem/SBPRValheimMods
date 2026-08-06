// ============================================================================
//  Homestead progression — PURCHASE / TIER ACCESS tests (T013, US3).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free CLEAN-side purchase slice
//  (link-compiled from ../src): the pure NodePurchases.PurchaseNode transition
//  (one debit + one purchase record + exact Offered-Set provenance), the pure
//  NodePurchases.DeriveSameTreeTierAccess derivation (never stored as XP), and
//  the receipt-backed PurchaseCommandHandler (PurchaseNode) over its engine-free
//  projection sink. Nodes are driven to Offered through the real T012
//  DevelopmentCommandHandler so the pipeline is exercised end to end.
//
//  Named acceptance closed here (tasks.md T013 / plan.md US3):
//    AT-LOCAL-NOT-OFFERED         Local (and unavailable) nodes never enter the
//                                 Offered Set and reject purchase.
//    AT-PERSONAL-BECOMES-OFFERED  a completed Stone-developed personal node
//                                 becomes Offered and is purchasable by an
//                                 eligible actively Attuned character.
//    AT-PURCHASE-IDEMPOTENT       one debit + one purchase commit atomically;
//                                 replay is idempotent; conflicting reuse rejects.
//    AT-TIER-SAME-TREE            same-Tree Attunement Tier Access is derived from
//                                 prior same-Tree Offered purchases + Tree/Stone
//                                 caps; Swift Preparation's prior-Offered set is
//                                 enforced; sibling/Local/unavailable are inert.
//  Plus required hostile/edge coverage: Bond-alone rejection, stale revision,
//  hostile identity, unoffered/wrong-Tree/unavailable, insufficient balance,
//  stale Offered-Set expectation, and restart/recovery.
// ============================================================================

using System.Collections.Generic;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Activities;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimPurchaseTierTests : System.IDisposable
    {
        private readonly string _activityJournal;
        private readonly string _developJournal;
        private readonly string _purchaseJournal;
        private readonly WorldId _world = new WorldId("uid:pur-913");
        private readonly StoneId _stone;

        // A bonded Governor (develops nodes to Offered) and an attuned purchaser.
        private readonly AccountId _account = new AccountId("acct-gov");
        private readonly CharacterId _governor = new CharacterId("char-gov");
        private readonly AccountId _accountAtt = new AccountId("acct-att");
        private readonly CharacterId _attuned = new CharacterId("char-att");
        // A bonded-only actor (Bond is NOT purchase authority).
        private readonly AccountId _accountBondOnly = new AccountId("acct-bond-only");
        private readonly CharacterId _bondOnly = new CharacterId("char-bond-only");

        private readonly InMemoryStoneAggregateStore _stones = new InMemoryStoneAggregateStore();
        private readonly InMemoryCharacterAggregateStore _characters = new InMemoryCharacterAggregateStore();
        private readonly InMemoryAccountStoneAuthorityStore _authority = new InMemoryAccountStoneAuthorityStore();

        private const string BondRelId = "rel-bond-gov";
        private const string AttRelId = "rel-att";
        private const string BondOnlyRelId = "rel-bond-only";

        private ActivityCommandHandler _activity;
        private DevelopmentCommandHandler _develop;
        private PurchaseCommandHandler _purchase;

        private static readonly VersionedId Cooking = HomesteadProgressionCatalog.CookingTree;
        private static readonly VersionedId Warrior = HomesteadProgressionCatalog.WarriorTree;
        private static readonly VersionedId Savor = new VersionedId("SavorTheHearth", 1);   // Local
        private static readonly VersionedId FieldPrep = new VersionedId("FieldPrep", 1);    // personal L1
        private static readonly VersionedId IronStomach = new VersionedId("IronStomach", 1);// personal L1
        private static readonly VersionedId SwiftPrep = new VersionedId("SwiftPreparation", 1); // personal L2
        private static readonly VersionedId WatchfulCook = new VersionedId("WatchfulCook", 1);   // unavailable L2
        private static readonly VersionedId ReadyHands = new VersionedId("ReadyHands", 1);  // Warrior personal L1

        public NiflheimPurchaseTierTests()
        {
            _activityJournal = TempJournal("activity");
            _developJournal = TempJournal("develop");
            _purchaseJournal = TempJournal("purchase");
            _stone = StoneId.FromHostZone(_world, 5, 7);

            _stones.PutStone(BuildStone(revision: 10, activeLevel: 2, committed: new[]
            {
                new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                    Cooking, "seed-commit-cook", _governor.Value, 1, 0),
                new CommittedTreeRecord(HomesteadProgressionCatalog.MartialFacetId,
                    Warrior, "seed-commit-war", _governor.Value, 1, 0),
            }));

            _characters.PutCharacter(BuildGovernor(_account, _governor, BondRelId, personalBp: 20));
            _authority.ApplyAuthorityProjection("seed-bond", BondIndex(_account, _governor, BondRelId));

            _characters.PutCharacter(BuildAttuned(_accountAtt, _attuned, personalAp: 10));
            _authority.ApplyAuthorityProjection("seed-att", AttIndex(_accountAtt, _attuned));

            _characters.PutCharacter(BuildGovernor(_accountBondOnly, _bondOnly, BondOnlyRelId, personalBp: 0, personalAp: 10));
            _authority.ApplyAuthorityProjection("seed-bond-only", BondIndex(_accountBondOnly, _bondOnly, BondOnlyRelId));

            _activity = NewActivityHandler();
            _develop = NewDevelopHandler();
            _purchase = NewPurchaseHandler();
        }

        public void Dispose()
        {
            foreach (var p in new[] { _activityJournal, _developJournal, _purchaseJournal })
                if (File.Exists(p)) File.Delete(p);
        }

        private static string TempJournal(string tag) => Path.Combine(Path.GetTempPath(),
            "niflheim-t013-" + tag + "-" + System.Guid.NewGuid().ToString("N") + ".journal");

        private ActivityCommandHandler NewActivityHandler() =>
            new ActivityCommandHandler(_activityJournal, new PrincipalResolver(), _stones, _characters,
                _authority, new StubDevelopmentAuthority());

        private DevelopmentCommandHandler NewDevelopHandler() =>
            new DevelopmentCommandHandler(_developJournal, new PrincipalResolver(), _stones, _characters,
                _authority, new StubDevelopmentAuthority(), new HomesteadProgressionCatalog(), null);

        private PurchaseCommandHandler NewPurchaseHandler() =>
            new PurchaseCommandHandler(_purchaseJournal, new PrincipalResolver(), _stones, _characters,
                _authority, new HomesteadProgressionCatalog());

        // ── Fixtures ──

        private StoneProgressionAggregate BuildStone(long revision, int activeLevel,
            IReadOnlyList<CommittedTreeRecord>? committed)
        {
            return new StoneProgressionAggregate(_stone, revision,
                historicalStoneLevel: 2, activeStoneLevel: activeLevel,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "created", updatedProvenance: "seed",
                mirroredStoneAp: 0, lastAppliedReceiptId: "r-seed",
                committedTrees: committed, nodeDevelopment: null);
        }

        private CharacterProgressionAggregate BuildGovernor(AccountId account, CharacterId character,
            string bondRelId, int personalBp, int personalAp = 0)
        {
            var bond = new RelationshipRecord(bondRelId, RelationshipKind.Bond, RelationshipStatus.Active,
                "Homestead:All", "Governor", "relreceipt:seed-bond", string.Empty);
            var stoneRecord = new CharacterStoneRecord(_stone, personalAp, 0, personalBp,
                purchases: null, relationships: new[] { bond });
            return new CharacterProgressionAggregate(account, character, "pur-913/trailborne",
                revision: 3, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { stoneRecord });
        }

        private CharacterProgressionAggregate BuildAttuned(AccountId account, CharacterId character, int personalAp)
        {
            var att = new RelationshipRecord(AttRelId, RelationshipKind.Attunement, RelationshipStatus.Active,
                string.Empty, string.Empty, "relreceipt:seed-att", string.Empty);
            var stoneRecord = new CharacterStoneRecord(_stone, personalAp, personalAp, 0,
                purchases: null, relationships: new[] { att });
            return new CharacterProgressionAggregate(account, character, "pur-913/trailborne",
                revision: 1, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { stoneRecord });
        }

        private AccountStoneAuthorityIndex BondIndex(AccountId account, CharacterId who, string relId) =>
            AccountStoneAuthorityIndex.Vacant(account, _stone).WithReservationAdded(
                new AuthorityReservation(who, RelationshipKind.Bond, relId, "relreceipt:seed-bond"), 1);

        private AccountStoneAuthorityIndex AttIndex(AccountId account, CharacterId who) =>
            AccountStoneAuthorityIndex.Vacant(account, _stone).WithReservationAdded(
                new AuthorityReservation(who, RelationshipKind.Attunement, AttRelId, "relreceipt:seed-att"), 1);

        // ── Command builders ──

        private void CreditBp(int amount, string op)
        {
            var adapter = new AlignedActivityAdapter();
            var evidence = new AlignedActivityEvidence(new OperationId(op), _stone,
                "activity.cook", 1, "CookedMeal", Cooking, amount, serverAttributed: true);
            var admission = adapter.Admit(evidence,
                new AuthenticatedConnection(_account.Value, _governor.Value), default);
            Assert.True(admission.IsAdmitted);
            var r = _activity.Handle(admission.Command);
            Assert.Equal(ActivityCommandOutcome.Applied, r.Outcome);
        }

        private DevelopmentCommandResult DevelopToComplete(string op, VersionedId tree, VersionedId node)
        {
            // The successive-unlock cost curve escalates per already-developed node in the Tree, so a
            // node may need several BP deltas. Apply 1 BP per op until the node completes.
            DevelopmentCommandResult last = default;
            for (int i = 0; i < 16; i++)
            {
                var cmd = new ApplyBPToNodeCommand(new OperationId(op + "-" + i), _stone,
                    new AuthenticatedConnection(_account.Value, _governor.Value), default,
                    tree.Key, tree.Version, node.Key, node.Version, 1);
                last = _develop.Handle(cmd);
                Assert.Equal(DevelopmentCommandOutcome.Applied, last.Outcome);
                if (last.NodeCompleted) break;
            }
            return last;
        }

        private PurchaseNodeCommand Purchase(string op, AccountId account, CharacterId who,
            VersionedId tree, VersionedId node,
            PurchasePaymentSource pay = PurchasePaymentSource.PersonalAp,
            string? expectedSetKey = null, int expectedSetVersion = 0,
            long? expStone = null, long? expChar = null)
            => new PurchaseNodeCommand(new OperationId(op), _stone,
                new AuthenticatedConnection(account.Value, who.Value), default,
                tree.Key, tree.Version, node.Key, node.Version,
                expectedSetKey ?? string.Empty, expectedSetVersion, pay, expStone, expChar);

        private void OfferPersonalCookingL1()
        {
            CreditBp(10, "op-credit-offer");
            Assert.True(DevelopToComplete("op-dev-fieldprep", Cooking, FieldPrep).NodeOffered);
            Assert.True(DevelopToComplete("op-dev-ironstomach", Cooking, IronStomach).NodeOffered);
        }

        private int ApOf(AccountId account, CharacterId who)
        {
            var c = _characters.GetCharacter(account, who)!;
            foreach (var sr in c.StoneRecords)
                if (sr.StoneId.Equals(_stone)) return sr.PersonalAp;
            return 0;
        }

        private int PurchaseCountOf(AccountId account, CharacterId who, VersionedId node)
        {
            var c = _characters.GetCharacter(account, who)!;
            int n = 0;
            foreach (var sr in c.StoneRecords)
                if (sr.StoneId.Equals(_stone))
                    foreach (var p in sr.Purchases)
                        if (p.Node.Key == node.Key) n++;
            return n;
        }

        // ── AT-LOCAL-NOT-OFFERED ──
        [Fact]
        public void Local_node_is_never_offered_and_rejects_purchase()
        {
            CreditBp(5, "op-credit");
            var d = DevelopToComplete("op-savor", Cooking, Savor);
            Assert.True(d.NodeCompleted);
            Assert.False(d.NodeOffered); // Local completes but is never Offered

            var r = _purchase.Handle(Purchase("op-buy-local", _accountAtt, _attuned, Cooking, Savor));
            Assert.Equal(PurchaseCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("NodeNotOffered", r.ResultCode);
        }

        [Fact]
        public void Unavailable_node_rejects_purchase()
        {
            var r = _purchase.Handle(Purchase("op-buy-unavail", _accountAtt, _attuned, Cooking, WatchfulCook));
            Assert.Equal(PurchaseCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("NodeNotOffered", r.ResultCode);
        }

        [Fact]
        public void Authored_personal_node_not_yet_developed_rejects_purchase()
        {
            // FieldPrep is a personal Offered-capable node but has not been developed -> not Offered yet.
            var r = _purchase.Handle(Purchase("op-buy-early", _accountAtt, _attuned, Cooking, FieldPrep));
            Assert.Equal(PurchaseCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("NodeNotOffered", r.ResultCode);
        }

        // ── AT-PERSONAL-BECOMES-OFFERED ──
        [Fact]
        public void Completed_personal_node_becomes_offered_and_attuned_can_purchase()
        {
            OfferPersonalCookingL1();
            int apBefore = ApOf(_accountAtt, _attuned);

            var r = _purchase.Handle(Purchase("op-buy-fieldprep", _accountAtt, _attuned, Cooking, FieldPrep));
            Assert.Equal(PurchaseCommandOutcome.Applied, r.Outcome);
            Assert.Equal(1, r.ApDebited);
            Assert.Equal("PersonalAP", r.PaymentSource);
            Assert.Equal(apBefore - 1, ApOf(_accountAtt, _attuned));
            Assert.Equal(1, PurchaseCountOf(_accountAtt, _attuned, FieldPrep));
        }

        // ── Bond alone is NOT purchase authority ──
        [Fact]
        public void Bonded_but_unattuned_actor_cannot_purchase()
        {
            OfferPersonalCookingL1();
            var r = _purchase.Handle(Purchase("op-buy-bond", _accountBondOnly, _bondOnly, Cooking, FieldPrep));
            Assert.Equal(PurchaseCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("RelationshipRequired", r.ResultCode);
        }

        // ── AT-PURCHASE-IDEMPOTENT ──
        [Fact]
        public void Purchase_replay_is_idempotent_single_debit()
        {
            OfferPersonalCookingL1();
            int apBefore = ApOf(_accountAtt, _attuned);

            var first = _purchase.Handle(Purchase("op-idem", _accountAtt, _attuned, Cooking, FieldPrep));
            Assert.Equal(PurchaseCommandOutcome.Applied, first.Outcome);

            var replay = _purchase.Handle(Purchase("op-idem", _accountAtt, _attuned, Cooking, FieldPrep));
            Assert.Equal(PurchaseCommandOutcome.Replayed, replay.Outcome);

            // Exactly one debit and one purchase record despite the replay.
            Assert.Equal(apBefore - 1, ApOf(_accountAtt, _attuned));
            Assert.Equal(1, PurchaseCountOf(_accountAtt, _attuned, FieldPrep));
        }

        [Fact]
        public void Conflicting_reuse_of_operation_id_rejects()
        {
            OfferPersonalCookingL1();
            var first = _purchase.Handle(Purchase("op-conflict", _accountAtt, _attuned, Cooking, FieldPrep));
            Assert.Equal(PurchaseCommandOutcome.Applied, first.Outcome);

            // Same op id, DIFFERENT node binding -> OperationConflict, zero mutation.
            var conflict = _purchase.Handle(Purchase("op-conflict", _accountAtt, _attuned, Cooking, IronStomach));
            Assert.Equal(PurchaseCommandOutcome.Rejected, conflict.Outcome);
            Assert.Equal("OperationConflict", conflict.ResultCode);
            Assert.Equal(0, PurchaseCountOf(_accountAtt, _attuned, IronStomach));
        }

        [Fact]
        public void Second_distinct_purchase_of_same_node_rejects_already_acquired()
        {
            OfferPersonalCookingL1();
            Assert.Equal(PurchaseCommandOutcome.Applied,
                _purchase.Handle(Purchase("op-buy-1", _accountAtt, _attuned, Cooking, FieldPrep)).Outcome);

            var again = _purchase.Handle(Purchase("op-buy-2", _accountAtt, _attuned, Cooking, FieldPrep));
            Assert.Equal(PurchaseCommandOutcome.Rejected, again.Outcome);
            Assert.Equal("AlreadyAcquired", again.ResultCode);
        }

        // ── AT-TIER-SAME-TREE ──
        [Fact]
        public void Tier_access_2_requires_full_prior_offered_set_same_tree()
        {
            var catalog = new HomesteadProgressionCatalog();

            // Baseline: attuned purchaser with no purchases -> Tier 1.
            var att0 = _characters.GetCharacter(_accountAtt, _attuned)!;
            Assert.Equal(1, NodePurchases.DeriveSameTreeTierAccess(att0, _stones.GetStone(_stone)!, catalog, Cooking));

            OfferPersonalCookingL1();

            // Only ONE prior L1 node acquired -> still Tier 1 (prior set incomplete).
            Assert.Equal(PurchaseCommandOutcome.Applied,
                _purchase.Handle(Purchase("op-fp", _accountAtt, _attuned, Cooking, FieldPrep)).Outcome);
            var att1 = _characters.GetCharacter(_accountAtt, _attuned)!;
            Assert.Equal(1, NodePurchases.DeriveSameTreeTierAccess(att1, _stones.GetStone(_stone)!, catalog, Cooking));

            // BOTH L1 personal Offered nodes acquired -> Tier 2 (caps already at Level 2 after offering).
            Assert.Equal(PurchaseCommandOutcome.Applied,
                _purchase.Handle(Purchase("op-is", _accountAtt, _attuned, Cooking, IronStomach)).Outcome);
            var att2 = _characters.GetCharacter(_accountAtt, _attuned)!;
            Assert.Equal(2, NodePurchases.DeriveSameTreeTierAccess(att2, _stones.GetStone(_stone)!, catalog, Cooking));
        }

        [Fact]
        public void Swift_preparation_requires_prior_offered_set()
        {
            OfferPersonalCookingL1();

            // Offering the L1 personal set invested cumulative 3 BP (Field Prep 1 + Iron Stomach 2),
            // crossing the configured 1->2 threshold, so Cooking Tree Level is already 2. Develop Swift
            // Preparation (a Level-2 node) to Offered.
            Assert.True(DevelopToComplete("op-dev-swift", Cooking, SwiftPrep).NodeOffered);

            // Attuned purchaser holds NEITHER prior node -> Swift rejects PriorOfferedSetIncomplete.
            var early = _purchase.Handle(Purchase("op-swift-early", _accountAtt, _attuned, Cooking, SwiftPrep));
            Assert.Equal(PurchaseCommandOutcome.Rejected, early.Outcome);
            Assert.Equal("PriorOfferedSetIncomplete", early.ResultCode);

            // Acquire both L1 prior nodes, then Swift is purchasable.
            Assert.Equal(PurchaseCommandOutcome.Applied,
                _purchase.Handle(Purchase("op-fp", _accountAtt, _attuned, Cooking, FieldPrep)).Outcome);
            Assert.Equal(PurchaseCommandOutcome.Applied,
                _purchase.Handle(Purchase("op-is", _accountAtt, _attuned, Cooking, IronStomach)).Outcome);
            var swift = _purchase.Handle(Purchase("op-swift", _accountAtt, _attuned, Cooking, SwiftPrep));
            Assert.Equal(PurchaseCommandOutcome.Applied, swift.Outcome);
        }

        [Fact]
        public void Sibling_tree_and_local_nodes_neither_grant_nor_block_cooking_access()
        {
            var stone = _stones.GetStone(_stone)!;
            var catalog = new HomesteadProgressionCatalog();

            // Offer + purchase a WARRIOR personal node and complete a Cooking LOCAL node.
            OfferPersonalCookingL1();
            Assert.True(DevelopToComplete("op-dev-readyhands", Warrior, ReadyHands).NodeOffered);
            Assert.True(DevelopToComplete("op-dev-savor", Cooking, Savor).NodeCompleted);

            // Purchase the Warrior node (sibling Tree).
            Assert.Equal(PurchaseCommandOutcome.Applied,
                _purchase.Handle(Purchase("op-rh", _accountAtt, _attuned, Warrior, ReadyHands)).Outcome);

            // Cooking Tier Access is unaffected by the sibling purchase or the Local node: still Tier 1
            // (no Cooking prior-Offered set acquired yet).
            var att = _characters.GetCharacter(_accountAtt, _attuned)!;
            Assert.Equal(1, NodePurchases.DeriveSameTreeTierAccess(att, stone, catalog, Cooking));
        }

        // ── Hostile identity / stale / balance ──
        [Fact]
        public void Hostile_identity_claim_rejects()
        {
            OfferPersonalCookingL1();
            var cmd = new PurchaseNodeCommand(new OperationId("op-hostile"), _stone,
                new AuthenticatedConnection(_accountAtt.Value, _attuned.Value),
                new ClaimedPrincipal(_account.Value, _governor.Value),
                Cooking.Key, Cooking.Version, FieldPrep.Key, FieldPrep.Version,
                string.Empty, 0, PurchasePaymentSource.PersonalAp);
            var r = _purchase.Handle(cmd);
            Assert.Equal(PurchaseCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("PrincipalMismatch", r.ResultCode);
        }

        [Fact]
        public void Stale_character_revision_rejects_with_zero_mutation()
        {
            OfferPersonalCookingL1();
            int apBefore = ApOf(_accountAtt, _attuned);
            var r = _purchase.Handle(Purchase("op-stale", _accountAtt, _attuned, Cooking, FieldPrep,
                expChar: 999));
            Assert.Equal(PurchaseCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("StaleCharacterRevision", r.ResultCode);
            Assert.Equal(apBefore, ApOf(_accountAtt, _attuned));
        }

        [Fact]
        public void Stale_offered_set_expectation_rejects()
        {
            OfferPersonalCookingL1();
            var r = _purchase.Handle(Purchase("op-stale-set", _accountAtt, _attuned, Cooking, FieldPrep,
                expectedSetKey: "Cooking:L9", expectedSetVersion: 1));
            Assert.Equal(PurchaseCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("ContentVersionMismatch", r.ResultCode);
        }

        [Fact]
        public void Matching_offered_set_expectation_accepts()
        {
            OfferPersonalCookingL1();
            var expected = NodePurchases.OfferedSetIdFor(Cooking, 1, 1);
            var r = _purchase.Handle(Purchase("op-good-set", _accountAtt, _attuned, Cooking, FieldPrep,
                expectedSetKey: expected.Key, expectedSetVersion: expected.Version));
            Assert.Equal(PurchaseCommandOutcome.Applied, r.Outcome);
        }

        [Fact]
        public void Insufficient_personal_ap_rejects()
        {
            OfferPersonalCookingL1();
            // Drain the attuned purchaser's AP by buying both nodes (start with 10, plenty), then try a
            // node when broke. Simpler: build a poor attuned actor.
            var poorAcct = new AccountId("acct-poor");
            var poorChar = new CharacterId("char-poor");
            _characters.PutCharacter(BuildAttunedPoor(poorAcct, poorChar));
            _authority.ApplyAuthorityProjection("seed-poor",
                AccountStoneAuthorityIndex.Vacant(poorAcct, _stone).WithReservationAdded(
                    new AuthorityReservation(poorChar, RelationshipKind.Attunement, "rel-att-poor", "relreceipt:poor"), 1));

            var r = _purchase.Handle(Purchase("op-poor", poorAcct, poorChar, Cooking, FieldPrep));
            Assert.Equal(PurchaseCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("InsufficientPersonalAP", r.ResultCode);
        }

        [Fact]
        public void Wrong_tree_binding_rejects()
        {
            OfferPersonalCookingL1();
            // FieldPrep belongs to Cooking; claim it under Warrior -> TreeMismatch.
            var r = _purchase.Handle(Purchase("op-wrongtree", _accountAtt, _attuned, Warrior, FieldPrep));
            Assert.Equal(PurchaseCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("TreeMismatch", r.ResultCode);
        }

        [Fact]
        public void Retired_facet_credit_payment_source_is_rejected_and_never_funds_a_purchase()
        {
            // ADO #106/#132: Facet Credit does not exist. A revocation refund returns ordinary Stone-wide
            // Personal AP, so there is no Facet-locked balance to pay from. This REPLACES the former
            // Facet_credit_payment_debits_matching_facet_credit, which asserted the withdrawn rule.
            OfferPersonalCookingL1();
            var r = _purchase.Handle(Purchase("op-retired-source", _accountAtt, _attuned, Cooking, FieldPrep,
                pay: PurchasePaymentSource.FacetCredit));

            Assert.Equal(PurchaseCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("PaymentSourceRetired", r.ResultCode);

            // Zero mutation: no purchase record, and the buyer's Personal AP is untouched.
            var after = _characters.GetCharacter(_accountAtt, _attuned)!;
            foreach (var s in after.StoneRecords)
                if (s.StoneId.Equals(_stone))
                {
                    Assert.Empty(s.Purchases);
                    Assert.Equal(10, s.PersonalAp);
                }
        }

        // The cancellation-entry refund (deterministic replay + idempotent cancellation) is asserted in
        // NiflheimPersonalApAuthorityTests, against the REAL derived earned−spent balance path. This
        // fixture uses the legacy pure-domain seam (no ICharacterApStore), where the derivation does not
        // run, so an assertion here would be vacuous.

        // ── Restart / recovery ──
        [Fact]
        public void Restart_preserves_purchase_and_replay_is_pure()
        {
            OfferPersonalCookingL1();
            Assert.Equal(PurchaseCommandOutcome.Applied,
                _purchase.Handle(Purchase("op-restart", _accountAtt, _attuned, Cooking, FieldPrep)).Outcome);

            // Fresh stores seeded with the ORIGINAL pre-purchase attuned state; fresh handler rehydrates.
            var freshChars = new InMemoryCharacterAggregateStore();
            freshChars.PutCharacter(BuildAttuned(_accountAtt, _attuned, personalAp: 10));

            var rehydrated = new PurchaseCommandHandler(_purchaseJournal, new PrincipalResolver(),
                _stones, freshChars, _authority, new HomesteadProgressionCatalog());

            // Purchase present after boot (projection rebuilt from journal truth).
            var c = freshChars.GetCharacter(_accountAtt, _attuned)!;
            int count = 0, ap = 0;
            foreach (var sr in c.StoneRecords)
                if (sr.StoneId.Equals(_stone))
                {
                    ap = sr.PersonalAp;
                    foreach (var p in sr.Purchases) if (p.Node.Key == FieldPrep.Key) count++;
                }
            Assert.Equal(1, count);
            Assert.Equal(9, ap);

            var replay = rehydrated.Handle(Purchase("op-restart", _accountAtt, _attuned, Cooking, FieldPrep));
            Assert.Equal(PurchaseCommandOutcome.Replayed, replay.Outcome);
        }

        // ── AT-JOURNAL-DELIMITER-SAFE (ADO #127) ──
        // A StoneId is "world|zoneX|zoneZ" by construction, so a caller-composed operation id
        // legitimately embeds '|'. Written raw into the pipe-delimited frame it explodes the field
        // count and the strict parser rejects EVERY record — total, silent purchase loss on restart.
        [Fact]
        public void Purchase_with_pipes_in_operation_id_survives_restart_from_journal()
        {
            const string PipedOp = "savor-seam-on-uid:-898655635|3|2";
            OfferPersonalCookingL1();
            Assert.Equal(PurchaseCommandOutcome.Applied,
                _purchase.Handle(Purchase(PipedOp, _accountAtt, _attuned, Cooking, FieldPrep)).Outcome);

            var freshChars = new InMemoryCharacterAggregateStore();
            freshChars.PutCharacter(BuildAttuned(_accountAtt, _attuned, personalAp: 10));

            var rehydrated = new PurchaseCommandHandler(_purchaseJournal, new PrincipalResolver(),
                _stones, freshChars, _authority, new HomesteadProgressionCatalog());

            var c = freshChars.GetCharacter(_accountAtt, _attuned)!;
            int count = 0, ap = 0;
            foreach (var sr in c.StoneRecords)
                if (sr.StoneId.Equals(_stone))
                {
                    ap = sr.PersonalAp;
                    foreach (var p in sr.Purchases) if (p.Node.Key == FieldPrep.Key) count++;
                }
            Assert.Equal(1, count);
            Assert.Equal(9, ap);

            var replay = rehydrated.Handle(Purchase(PipedOp, _accountAtt, _attuned, Cooking, FieldPrep));
            Assert.Equal(PurchaseCommandOutcome.Replayed, replay.Outcome);
        }

        private CharacterProgressionAggregate BuildAttunedPoor(AccountId account, CharacterId character)
        {
            var att = new RelationshipRecord("rel-att-poor", RelationshipKind.Attunement,
                RelationshipStatus.Active, string.Empty, string.Empty, "relreceipt:poor", string.Empty);
            var stoneRecord = new CharacterStoneRecord(_stone, 0, 0, 0,
                purchases: null, relationships: new[] { att });
            return new CharacterProgressionAggregate(account, character, "pur-913/trailborne",
                revision: 1, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { stoneRecord });
        }

        // ── Stubs ──

        private sealed class StubDevelopmentAuthority : IGovernorDevelopmentAuthority
        {
            public bool CanDevelop(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                VersionedId tree)
            {
                return string.Equals(responsibilityRange, "Homestead:All", System.StringComparison.Ordinal)
                    && string.Equals(ownerGovernorRole, "Governor", System.StringComparison.Ordinal)
                    && !tree.IsNone;
            }
        }
    }
}
