// ============================================================================
//  T021 remediation 2 (t_79588427) — Local-node development / personal-node
//  purchase INGRESS runtime-caller tests.
// ----------------------------------------------------------------------------
//  The T021 joined-client rerun (PR #371 FAIL) proved the accepted progression
//  command handlers wired into LocalProgressionServer + LocalNodeProvisioningDriver
//  + PurchaseCommandHandler had ZERO runtime callers, so a Stone-cultivated Local
//  node (Refined Workshop) could never reach Developed at runtime and its Local
//  Effect could never derive Active. These tests exercise the SHIPPED, engine-free
//  LocalProvisioningIngress (the runtime caller the net48 admin/isolated-QA seam
//  drives) end-to-end through the SAME shared runtime a live server composes:
//
//    * Refined Workshop develops to Active through accepted commands only — the
//      positive effective-Level-3 precondition the FAIL found unreachable.
//    * The ingress seeds only the bare pre-progression Stone envelope when absent,
//      never overwriting an existing/rehydrated Stone (restart rehydrates the
//      developed node from the durable journals, not the seam).
//    * Re-running the same provisioning replays idempotently (no double develop).
//    * Hostile/unauthorized/stale/replay-conflict and the personal-node purchase
//      authority gate all reject WITHOUT mutation — the real handlers fail closed.
//    * No provisional activation, no direct node-state write: activation is a pure
//      derivation off the developed Stone (AT-NO-ACTIVE-LEDGER preserved).
// ============================================================================

using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
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
    public sealed class NiflheimLocalProvisioningIngressTests : System.IDisposable
    {
        private readonly string _dir;
        private readonly WorldId _world = new WorldId("uid:t021r2-ingress");
        private readonly StoneId _stone;

        private readonly AccountId _gov = new AccountId("acct-gov");
        private readonly CharacterId _govChar = new CharacterId("char-gov");
        private readonly AccountId _hostile = new AccountId("acct-hostile");
        private readonly CharacterId _hostileChar = new CharacterId("char-hostile");

        // Refined Workshop — Crafting Tree, Stone-cultivated Local node, Level 1.
        private static readonly VersionedId RefinedWorkshop = new VersionedId("RefinedWorkshop", 1);
        private static readonly VersionedId Crafting = HomesteadProgressionCatalog.CraftingTree;
        // Masterwork — Crafting Tree, personal Offered node (the purchase path).
        private static readonly VersionedId Masterwork = new VersionedId("Masterwork", 1);

        public NiflheimLocalProvisioningIngressTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "niflheim-t021r2-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _stone = StoneId.FromHostZone(_world, 7, 5);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        }

        // ── Composition helpers ──────────────────────────────────────────────

        private LocalProgressionServer NewServer(
            InMemoryStoneAggregateStore stones,
            InMemoryCharacterAggregateStore characters,
            InMemoryAccountStoneAuthorityStore authority)
        {
            var relationships = new RelationshipCommandHandler(
                Path.Combine(_dir, "relationships.journal"), new PrincipalResolver(), characters, authority,
                new FixedFamilyResolver(), new AllowHomesteadBondPolicy(), AlwaysAtStoneProximity.Instance, null, _world,
                new ProductScope("SBPR.Trailborne"));

            return LocalProgressionServer.Create(
                _dir, stones, characters, authority, relationships,
                new FixedFamilyResolver(), new AllowGovernorAuthority(), new AllowDevelopmentAuthority(),
                new CommittedGovernorOwnerAuthority(new GovernorPresenceResolver(characters, authority)));
        }

        private CharacterProgressionAggregate Governor() =>
            new CharacterProgressionAggregate(_gov, _govChar, "t021r2/trailborne",
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

        private AccountStoneAuthorityIndex BondIndex() =>
            AccountStoneAuthorityIndex.Vacant(_gov, _stone).WithReservationAdded(
                new AuthorityReservation(_govChar, RelationshipKind.Bond, "rel-bond-gov",
                    "relreceipt:seed-bond"), 1);

        // A bonded Governor + EMPTY Stone store: the ingress must SEED the bare Stone and reach Developed
        // purely through accepted commands — no Stone aggregate pre-exists (the live-server condition).
        private (LocalProgressionServer server, InMemoryStoneAggregateStore stones) Bootstrapped()
        {
            var stones = new InMemoryStoneAggregateStore();
            var characters = new InMemoryCharacterAggregateStore();
            var authority = new InMemoryAccountStoneAuthorityStore();
            characters.PutCharacter(Governor());
            authority.ApplyAuthorityProjection("seed-bond", BondIndex());
            var server = NewServer(stones, characters, authority);
            return (server, stones);
        }

        private OccupantPresence Presence(bool inside, bool gov) =>
            new OccupantPresence(_gov, _govChar, true, true, inside, gov);

        private static bool NodeDeveloped(StoneProgressionAggregate stone, VersionedId node)
        {
            foreach (var d in stone.NodeDevelopment)
                if (d.Node.Key == node.Key && d.Developed) return true;
            return false;
        }

        // ── The positive path: Refined Workshop develops to Active via the ingress ──

        [Fact]
        public void Ingress_develops_refined_workshop_from_empty_store_via_accepted_commands()
        {
            var (server, stones) = Bootstrapped();
            Assert.Null(stones.GetStone(_stone)); // no Stone exists before the ingress runs.

            var ingress = server.CreateLocalProvisioningIngress();
            var result = ingress.DevelopLocalNode(
                new AuthoritativeSubject(_gov, _govChar), _stone, RefinedWorkshop, "qa-refined");

            Assert.True(result.Succeeded, result.ResultCode + "/" + result.Step);
            Assert.Equal("Developed", result.Kind);

            // Stone-owned developed state + committed Crafting Tree — both via the real handlers.
            var stone = stones.GetStone(_stone)!;
            Assert.True(NodeDeveloped(stone, RefinedWorkshop));
            bool committed = false;
            foreach (var c in stone.CommittedTrees) if (c.Tree.Key == Crafting.Key) committed = true;
            Assert.True(committed);
        }

        [Fact]
        public void Developed_refined_workshop_derives_active_for_eligible_occupant()
        {
            var (server, _) = Bootstrapped();
            server.CreateLocalProvisioningIngress()
                .DevelopLocalNode(new AuthoritativeSubject(_gov, _govChar), _stone, RefinedWorkshop, "qa-refined");

            // The positive effective-Level-3 precondition: the Local Effect is Active for an eligible
            // occupant inside the Area with an authorized Governor present.
            var snap = server.Activation.Fetch(_stone, Presence(inside: true, gov: true));
            Assert.True(snap.AuthorityPresent);
            Assert.True(snap.IsActive(RefinedWorkshop));

            // Governance/occupancy dormancy re-derives the effect away with zero writes.
            Assert.False(server.Activation.Fetch(_stone, Presence(inside: false, gov: true)).IsActive(RefinedWorkshop));
            Assert.False(server.Activation.Fetch(_stone, Presence(inside: true, gov: false)).IsActive(RefinedWorkshop));
        }

        [Fact]
        public void Ingress_is_idempotent_on_replay()
        {
            var (server, stones) = Bootstrapped();
            var ingress = server.CreateLocalProvisioningIngress();
            var first = ingress.DevelopLocalNode(new AuthoritativeSubject(_gov, _govChar), _stone, RefinedWorkshop, "qa-refined");
            Assert.True(first.Succeeded);
            long rev = stones.GetStone(_stone)!.Revision;

            // Re-run the SAME provisioning: every accepted command replays, no double development.
            var again = ingress.DevelopLocalNode(new AuthoritativeSubject(_gov, _govChar), _stone, RefinedWorkshop, "qa-refined");
            Assert.True(again.Succeeded);
            Assert.Equal(rev, stones.GetStone(_stone)!.Revision);
        }

        [Fact]
        public void Seed_never_overwrites_an_existing_stone()
        {
            var (server, stones) = Bootstrapped();
            server.CreateLocalProvisioningIngress()
                .DevelopLocalNode(new AuthoritativeSubject(_gov, _govChar), _stone, RefinedWorkshop, "qa-refined");
            long developedRev = stones.GetStone(_stone)!.Revision;
            Assert.True(NodeDeveloped(stones.GetStone(_stone)!, RefinedWorkshop));

            // A second ingress over the SAME (now developed) store must NOT re-seed a bare envelope that
            // would wipe the developed node — the developed state is preserved.
            var ingress2 = server.CreateLocalProvisioningIngress();
            ingress2.DevelopLocalNode(new AuthoritativeSubject(_gov, _govChar), _stone, RefinedWorkshop, "qa-refined");
            Assert.True(NodeDeveloped(stones.GetStone(_stone)!, RefinedWorkshop));
            Assert.Equal(developedRev, stones.GetStone(_stone)!.Revision);
        }

        [Fact]
        public void Restart_rehydrates_developed_node_from_durable_journals_not_the_seed()
        {
            var (server1, _) = Bootstrapped();
            server1.CreateLocalProvisioningIngress()
                .DevelopLocalNode(new AuthoritativeSubject(_gov, _govChar), _stone, RefinedWorkshop, "qa-refined");
            Assert.True(server1.Activation.Fetch(_stone, Presence(true, true)).IsActive(RefinedWorkshop));

            // Full restart: fresh stores + fresh handlers over the SAME durable directory. The durable
            // Facet/Development journals rehydrate the developed node — no ingress runs on boot.
            var stones2 = new InMemoryStoneAggregateStore();
            var characters2 = new InMemoryCharacterAggregateStore();
            var authority2 = new InMemoryAccountStoneAuthorityStore();
            characters2.PutCharacter(Governor());
            authority2.ApplyAuthorityProjection("seed-bond", BondIndex());
            var server2 = NewServer(stones2, characters2, authority2);

            Assert.True(NodeDeveloped(stones2.GetStone(_stone)!, RefinedWorkshop));
            Assert.True(server2.Activation.Fetch(_stone, Presence(true, true)).IsActive(RefinedWorkshop));
        }

        // ── Fail-closed: hostile / unauthorized / bad input reject with no mutation ──

        [Fact]
        public void Hostile_subject_without_bond_cannot_develop()
        {
            var (server, stones) = Bootstrapped();
            var ingress = server.CreateLocalProvisioningIngress();
            var attempt = ingress.DevelopLocalNode(
                new AuthoritativeSubject(_hostile, _hostileChar), _stone, RefinedWorkshop, "qa-hostile");

            Assert.False(attempt.Succeeded);
            // The bare Stone may be seeded, but NO node is developed — the accepted commands reject.
            var stone = stones.GetStone(_stone);
            if (stone != null)
                Assert.False(NodeDeveloped(stone, RefinedWorkshop));
        }

        [Fact]
        public void Unauthenticated_subject_rejects_before_any_command()
        {
            var (server, stones) = Bootstrapped();
            var ingress = server.CreateLocalProvisioningIngress();
            var attempt = ingress.DevelopLocalNode(
                new AuthoritativeSubject(new AccountId(""), new CharacterId("")), _stone, RefinedWorkshop, "qa-x");
            Assert.False(attempt.Succeeded);
            Assert.Equal("Unauthenticated", attempt.ResultCode);
            Assert.Null(stones.GetStone(_stone)); // no seed, no mutation.
        }

        [Fact]
        public void Non_local_node_rejects_without_mutation()
        {
            var (server, stones) = Bootstrapped();
            var ingress = server.CreateLocalProvisioningIngress();
            // Masterwork is a personal Offered node, not a Stone-cultivated Local node — the driver rejects.
            var attempt = ingress.DevelopLocalNode(
                new AuthoritativeSubject(_gov, _govChar), _stone, Masterwork, "qa-master");
            Assert.False(attempt.Succeeded);
            Assert.Equal("NotALocalNode", attempt.ResultCode);
        }

        // ── Purchase path: authority gate is a real reachable caller ─────────

        [Fact]
        public void Purchase_without_attunement_rejects_relationship_required()
        {
            var (server, _) = Bootstrapped();
            // Develop first so the Stone exists; the Governor holds a Bond, NOT an Attunement.
            server.CreateLocalProvisioningIngress()
                .DevelopLocalNode(new AuthoritativeSubject(_gov, _govChar), _stone, RefinedWorkshop, "qa-refined");

            var ingress = server.CreateLocalProvisioningIngress();
            var attempt = ingress.PurchaseNode(
                new AuthoritativeSubject(_gov, _govChar), _stone, Crafting, Masterwork,
                VersionedId.None, PurchasePaymentSource.PersonalAp, "qa-purchase-1");

            // Bond alone is not purchase authority (spec US3) — the accepted handler rejects with no mutation.
            Assert.False(attempt.Succeeded);
            Assert.Equal("RelationshipRequired", attempt.ResultCode);
        }

        [Fact]
        public void Purchase_unauthenticated_rejects_before_handler()
        {
            var (server, _) = Bootstrapped();
            var ingress = server.CreateLocalProvisioningIngress();
            var attempt = ingress.PurchaseNode(
                new AuthoritativeSubject(new AccountId(""), new CharacterId("")), _stone, Crafting, Masterwork,
                VersionedId.None, PurchasePaymentSource.PersonalAp, "qa-x");
            Assert.False(attempt.Succeeded);
            Assert.Equal("Unauthenticated", attempt.ResultCode);
        }

        // ── T022 remediation R4: Masterwork OWNERSHIP provisioning — offer + buy reach ACTIVE purchased ──

        private readonly AccountId _buyer = new AccountId("acct-buyer");
        private readonly CharacterId _buyerChar = new CharacterId("char-buyer");

        // An ATTUNED buyer character holding Personal AP (earned via real Foundational placement in-world;
        // seeded here as an already-earned balance) and an active Attunement relationship record at the Stone.
        private CharacterProgressionAggregate AttunedBuyer(int personalAp) =>
            new CharacterProgressionAggregate(_buyer, _buyerChar, "t022r4/trailborne",
                revision: 2, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[]
                {
                    new CharacterStoneRecord(_stone, personalAp, personalAp, 0, purchases: null,
                        relationships: new[]
                        {
                            new RelationshipRecord("rel-attune-buyer", RelationshipKind.Attunement,
                                RelationshipStatus.Active, "Homestead:All", string.Empty,
                                "relreceipt:seed-attune", string.Empty)
                        })
                });

        // Add the buyer's active Attunement reservation to an existing authority index for the Stone.
        private void SeedBuyerAttunement(InMemoryAccountStoneAuthorityStore authority)
        {
            var idx = AccountStoneAuthorityIndex.Vacant(_buyer, _stone).WithReservationAdded(
                new AuthorityReservation(_buyerChar, RelationshipKind.Attunement, "rel-attune-buyer",
                    "relreceipt:seed-attune"), 1);
            authority.ApplyAuthorityProjection("seed-attune", idx);
        }

        // Full two-subject bootstrap: a bonded Governor (offer authority) + an attuned funded buyer (purchase
        // authority), over shared in-memory stores. Returns the composed server + the shared stores so a test
        // can read the post-purchase aggregates through the SAME production gate the issuance observer uses.
        private (LocalProgressionServer server, InMemoryStoneAggregateStore stones,
                 InMemoryCharacterAggregateStore characters, InMemoryAccountStoneAuthorityStore authority)
            OwnershipBootstrap(int buyerAp)
        {
            var stones = new InMemoryStoneAggregateStore();
            var characters = new InMemoryCharacterAggregateStore();
            var authority = new InMemoryAccountStoneAuthorityStore();
            characters.PutCharacter(Governor());
            authority.ApplyAuthorityProjection("seed-bond", BondIndex());
            characters.PutCharacter(AttunedBuyer(buyerAp));
            SeedBuyerAttunement(authority);
            var server = NewServer(stones, characters, authority);
            return (server, stones, characters, authority);
        }

        // The exact production Masterwork activation gate (WorkmanshipIssuanceProvider.IsMasterworkActive):
        // a purchase record for Masterwork@1 at the Stone AND an active relationship. Read from the shared
        // stores after provisioning, proving the derived state, not a fabricated flag.
        private bool MasterworkActiveFor(LocalProgressionServer server, AccountId account, CharacterId character)
        {
            var stone = server.Stones.GetStone(_stone);
            var chr = server.Characters.GetCharacter(account, character);
            var auth = server.Authority.GetAuthority(account, _stone);
            if (stone == null || chr == null || auth == null) return false;
            return new SBPR.Niflheim.HomesteadStones.Adapters.Crafting.WorkmanshipIssuanceProvider(
                new HomesteadProgressionCatalog()).IsMasterworkActive(stone, chr, auth);
        }

        [Fact]
        public void Ownership_offer_then_buy_reaches_active_purchased_masterwork_via_accepted_handlers()
        {
            var (server, stones, _, _) = OwnershipBootstrap(buyerAp: 1);

            // Governor develops+offers Masterwork through the accepted commands.
            var offer = server.CreateLocalProvisioningIngress()
                .OfferMasterwork(new AuthoritativeSubject(_gov, _govChar), _stone, "qa-mw");
            Assert.True(offer.Succeeded, offer.ResultCode + "/" + offer.Step);
            // Masterwork is now Offered on the Stone (developed personal node).
            bool offered = false;
            foreach (var d in stones.GetStone(_stone)!.NodeDevelopment)
                if (d.Node.Key == Masterwork.Key && d.Offered) offered = true;
            Assert.True(offered);

            // Attuned buyer purchases Masterwork through the accepted PurchaseCommandHandler.
            var buy = server.CreateLocalProvisioningIngress()
                .BuyMasterwork(new AuthoritativeSubject(_buyer, _buyerChar), _stone, "qa-mw");
            Assert.True(buy.Succeeded, buy.ResultCode + "/" + buy.Step);
            Assert.Equal("Purchased", buy.Kind);

            // The exact production gate now derives Masterwork ACTIVE for the buyer — the previously
            // structurally-unreachable state.
            Assert.True(MasterworkActiveFor(server, _buyer, _buyerChar));
        }

        [Fact]
        public void Ownership_buy_before_offer_rejects_node_not_offered_no_purchase()
        {
            var (server, _, characters, _) = OwnershipBootstrap(buyerAp: 1);

            // Establish a Stone context (Governor develops the Local Refined Workshop) WITHOUT offering
            // Masterwork, so the Stone exists but the personal node is not Offered. Purchase must reject
            // verbatim with NodeNotOffered and no mutation — not StoneNotFound.
            server.CreateLocalProvisioningIngress()
                .DevelopLocalNode(new AuthoritativeSubject(_gov, _govChar), _stone, RefinedWorkshop, "qa-refined");

            var buy = server.CreateLocalProvisioningIngress()
                .BuyMasterwork(new AuthoritativeSubject(_buyer, _buyerChar), _stone, "qa-mw");
            Assert.False(buy.Succeeded);
            Assert.Equal("NodeNotOffered", buy.ResultCode);
            Assert.False(MasterworkActiveFor(server, _buyer, _buyerChar));
        }

        [Fact]
        public void Ownership_buy_by_unattuned_subject_rejects_relationship_required()
        {
            var (server, _, _, _) = OwnershipBootstrap(buyerAp: 1);
            server.CreateLocalProvisioningIngress()
                .OfferMasterwork(new AuthoritativeSubject(_gov, _govChar), _stone, "qa-mw");

            // The Governor holds a Bond, NOT an Attunement — Bond is not purchase authority (spec US3).
            var buy = server.CreateLocalProvisioningIngress()
                .BuyMasterwork(new AuthoritativeSubject(_gov, _govChar), _stone, "qa-mw");
            Assert.False(buy.Succeeded);
            Assert.Equal("RelationshipRequired", buy.ResultCode);
        }

        [Fact]
        public void Ownership_buy_by_unfunded_buyer_rejects_insufficient_personal_ap()
        {
            var (server, _, _, _) = OwnershipBootstrap(buyerAp: 0); // attuned but no earned AP.
            server.CreateLocalProvisioningIngress()
                .OfferMasterwork(new AuthoritativeSubject(_gov, _govChar), _stone, "qa-mw");

            var buy = server.CreateLocalProvisioningIngress()
                .BuyMasterwork(new AuthoritativeSubject(_buyer, _buyerChar), _stone, "qa-mw");
            Assert.False(buy.Succeeded);
            Assert.Equal("InsufficientPersonalAP", buy.ResultCode);
            Assert.False(MasterworkActiveFor(server, _buyer, _buyerChar));
        }

        [Fact]
        public void Ownership_offer_of_wrong_ownership_local_node_rejects_not_an_offered_node()
        {
            var (server, _, _, _) = OwnershipBootstrap(buyerAp: 1);
            // ProvisionOffered refuses a Stone-cultivated Local node (Refined Workshop) — the ownership guard.
            var attempt = server.CreateLocalProvisioningIngress().DevelopLocalNode(
                new AuthoritativeSubject(_gov, _govChar), _stone, RefinedWorkshop, "qa-refined"); // sanity: local OK
            Assert.True(attempt.Succeeded);

            var offered = new LocalNodeProvisioningDriver(server)
                .ProvisionOffered(new AuthoritativeSubject(_gov, _govChar), _stone, RefinedWorkshop, "qa-wrong");
            Assert.False(offered.IsDeveloped);
            Assert.Equal("NotAnOfferedNode", offered.ResultCode);
        }

        [Fact]
        public void Ownership_buy_is_idempotent_on_replay_single_purchase_and_debit()
        {
            var (server, _, characters, _) = OwnershipBootstrap(buyerAp: 1);
            server.CreateLocalProvisioningIngress()
                .OfferMasterwork(new AuthoritativeSubject(_gov, _govChar), _stone, "qa-mw");

            var first = server.CreateLocalProvisioningIngress()
                .BuyMasterwork(new AuthoritativeSubject(_buyer, _buyerChar), _stone, "qa-mw");
            Assert.True(first.Succeeded);
            Assert.Equal("Purchased", first.Kind);

            // Exact replay: the accepted PurchaseCommandHandler returns the recorded terminal result, one
            // purchase record, one AP debit — never a second purchase / double debit.
            var again = server.CreateLocalProvisioningIngress()
                .BuyMasterwork(new AuthoritativeSubject(_buyer, _buyerChar), _stone, "qa-mw");
            Assert.True(again.Succeeded);
            Assert.Equal("Replayed", again.Kind);

            int masterworkPurchases = 0;
            var chr = characters.GetCharacter(_buyer, _buyerChar)!;
            foreach (var sr in chr.StoneRecords)
                if (sr.StoneId.Equals(_stone))
                    foreach (var p in sr.Purchases)
                        if (p.Node.Key == Masterwork.Key) masterworkPurchases++;
            Assert.Equal(1, masterworkPurchases);
            Assert.True(MasterworkActiveFor(server, _buyer, _buyerChar));
        }

        [Fact]
        public void Ownership_own_composite_two_subjects_reaches_active_purchased()
        {
            var (server, _, _, _) = OwnershipBootstrap(buyerAp: 1);
            var result = server.CreateLocalProvisioningIngress().OwnMasterwork(
                new AuthoritativeSubject(_gov, _govChar), new AuthoritativeSubject(_buyer, _buyerChar),
                _stone, "qa-mw");
            Assert.True(result.Succeeded, result.ResultCode + "/" + result.Step);
            Assert.Equal("Purchased", result.Kind);
            Assert.True(MasterworkActiveFor(server, _buyer, _buyerChar));
        }

        // ── T027 remediation: full personal-node OWNERSHIP through accepted handlers ─────────
        //
        // The missing runtime seam the T027 Fletcher's Habit joined-client verdict found: no code path
        // could make a character OWN (developed + purchased) a personal Offered node on a joined client,
        // so the OWNER in-world proof was structurally unreachable. ProvisionPersonalNodeOwnership is that
        // ingress — it drives Bond→develop→release→Attune→purchase entirely through the accepted handlers.

        // Archer / Fletcher's Habit — the T027 personal Permanent-Effect node the seam must reach OWNED.
        private static readonly VersionedId Archer = HomesteadProgressionCatalog.ArcherTree;
        private static readonly VersionedId FletchersHabit = new VersionedId("FletchersHabit", 1);
        private static readonly VersionedId FieldFletchingI = new VersionedId("FieldFletchingI", 1);

        // A fresh admin subject that pre-holds NO relationship: the seam must establish everything itself.
        private readonly AccountId _owner = new AccountId("acct-owner");
        private readonly CharacterId _ownerChar = new CharacterId("char-owner");

        private bool OwnsFletchers(LocalProgressionServer server)
        {
            var stone = ((InMemoryStoneAggregateStore)server.Stones).GetStone(_stone);
            var character = server.Characters.GetCharacter(_owner, _ownerChar);
            if (stone == null || character == null) return false;
            var authority = server.Authority.GetAuthority(_owner, _stone);
            return new SBPR.Niflheim.HomesteadStones.Adapters.Archer.ProjectileRecoveryProvider(server.Catalog)
                .OwnsFletchersHabit(stone, character, authority);
        }

        [Fact]
        public void Ownership_ingress_makes_fresh_subject_OWN_fletchers_habit_via_accepted_commands()
        {
            var (server, _) = Bootstrapped();
            var ingress = server.CreateLocalProvisioningIngress();

            var result = ingress.ProvisionPersonalNodeOwnership(
                new AuthoritativeSubject(_owner, _ownerChar), _stone, Archer, FletchersHabit,
                "qa-fletcher", "t021r2/trailborne");

            // The terminal accepted-handler outcome is a real purchase (or idempotent replay), not a stub.
            Assert.True(result.Succeeded, result.ResultCode + "/" + result.Step);
            Assert.Contains(result.Kind, new[] { "Purchased", "Replayed" });

            // The durable ownership truth the recovery provider reads is now true: developed + purchased.
            Assert.True(OwnsFletchers(server));
        }

        [Fact]
        public void Ownership_persists_after_relationship_release_permanent_effect()
        {
            // The seam releases the develop-Bond before attuning; ownership is a Permanent-Effect truth that
            // persists regardless of the currently-active relationship. After the whole flow the caller owns
            // the node even though the seam's final Attunement is what remains active.
            var (server, _) = Bootstrapped();
            var ok = server.CreateLocalProvisioningIngress().ProvisionPersonalNodeOwnership(
                new AuthoritativeSubject(_owner, _ownerChar), _stone, Archer, FletchersHabit,
                "qa-fletcher", "t021r2/trailborne");
            Assert.True(ok.Succeeded, ok.ResultCode + "/" + ok.Step);
            Assert.True(OwnsFletchers(server));
        }

        [Fact]
        public void Ownership_ingress_is_idempotent_on_replay()
        {
            var (server, _) = Bootstrapped();
            var ingress = server.CreateLocalProvisioningIngress();
            var first = ingress.ProvisionPersonalNodeOwnership(
                new AuthoritativeSubject(_owner, _ownerChar), _stone, Archer, FletchersHabit,
                "qa-fletcher", "t021r2/trailborne");
            Assert.True(first.Succeeded, first.ResultCode + "/" + first.Step);

            int purchasesAfterFirst = PurchaseCount(server, FletchersHabit);

            // Re-run the SAME provisioning: every accepted command replays, exactly one purchase record.
            var again = ingress.ProvisionPersonalNodeOwnership(
                new AuthoritativeSubject(_owner, _ownerChar), _stone, Archer, FletchersHabit,
                "qa-fletcher", "t021r2/trailborne");
            Assert.True(again.Succeeded, again.ResultCode + "/" + again.Step);
            Assert.Equal(purchasesAfterFirst, PurchaseCount(server, FletchersHabit));
            Assert.Equal(1, purchasesAfterFirst);
        }

        [Fact]
        public void Ownership_ingress_also_reaches_field_fletching_i_the_t026_sibling()
        {
            var (server, _) = Bootstrapped();
            var result = server.CreateLocalProvisioningIngress().ProvisionPersonalNodeOwnership(
                new AuthoritativeSubject(_owner, _ownerChar), _stone, Archer, FieldFletchingI,
                "qa-fieldfletch", "t021r2/trailborne");
            Assert.True(result.Succeeded, result.ResultCode + "/" + result.Step);
            Assert.Equal(1, PurchaseCount(server, FieldFletchingI));
        }

        [Fact]
        public void Ownership_ingress_rejects_a_non_personal_node_without_mutation()
        {
            var (server, stones) = Bootstrapped();
            // RefinedWorkshop is a Stone-cultivated Local node, never a personal Offered purchase.
            var attempt = server.CreateLocalProvisioningIngress().ProvisionPersonalNodeOwnership(
                new AuthoritativeSubject(_owner, _ownerChar), _stone, Crafting, RefinedWorkshop,
                "qa-bad", "t021r2/trailborne");
            Assert.False(attempt.Succeeded);
            Assert.Equal("NotAPersonalNode", attempt.ResultCode);
            // No purchase and no develop of the Local node crept in.
            Assert.Equal(0, PurchaseCount(server, RefinedWorkshop));
        }

        [Fact]
        public void Ownership_ingress_rejects_unauthenticated_before_any_command()
        {
            var (server, stones) = Bootstrapped();
            var attempt = server.CreateLocalProvisioningIngress().ProvisionPersonalNodeOwnership(
                new AuthoritativeSubject(new AccountId(""), new CharacterId("")), _stone, Archer, FletchersHabit,
                "qa-x", "t021r2/trailborne");
            Assert.False(attempt.Succeeded);
            Assert.Equal("Unauthenticated", attempt.ResultCode);
            Assert.Null(server.Characters.GetCharacter(new AccountId(""), new CharacterId("")));
        }

        private int PurchaseCount(LocalProgressionServer server, VersionedId node)
        {
            var c = server.Characters.GetCharacter(_owner, _ownerChar);
            if (c == null) return 0;
            int n = 0;
            foreach (var sr in c.StoneRecords)
                if (sr.StoneId.Equals(_stone))
                    foreach (var p in sr.Purchases)
                        if (p.Node.Key == node.Key) n++;
            return n;
        }

        // ── Stubs (server-owned authority policies; mirror the shared-suite fixtures) ──

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
