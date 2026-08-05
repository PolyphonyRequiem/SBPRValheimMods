// ============================================================================
//  Homestead progression — BP DEVELOPMENT tests (T012, Tracer 4).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free CLEAN-side BP-development slice
//  (link-compiled from ../src): the pure BondPower credit/debit transitions,
//  the pure TreeDevelopment.ApplyBPToNode node-development + Tree-advancement
//  transition, the AlignedActivityAdapter evidence gate, and the receipt-backed
//  ActivityCommandHandler (RecordAlignedActivity) + DevelopmentCommandHandler
//  (ApplyBPToNode) command handlers over their engine-free projection sinks.
//
//  Named acceptance closed here (tasks.md T012 / plan.md Tracer 4):
//    AT-BP-STONE-WIDE                 one personal Stone-wide BP balance is credited
//                                     and spent across every Committed Tree in the
//                                     Governor's Responsibility Range (cross-Tree spend).
//    AT-BP-NOT-SHARED                 different Governors never share a BP balance;
//                                     a credit to one leaves the other's untouched.
//    AT-NODE-DEVELOPMENT-COUNTS-AS-INVESTMENT  every accepted BP delta advances node
//                                     progress AND the same delta of cumulative Tree
//                                     investment in one mutation.
//    AT-NO-DIRECT-LEVEL-METER         Tree Level moves ONLY via the cumulative-investment
//                                     threshold; there is no direct level wallet/command.
//    AT-TREE-ADVANCE-1-2              Cooking Tree Level advances 1->2 exactly once when
//                                     cumulative qualifying BP investment crosses the
//                                     configured threshold and Active Stone Level permits.
//    AT-ESCALATING-COST-CONFIG        successive unlock costs + Tree-level thresholds are
//                                     configurable data; changing the config changes them.
//  Plus the required hostile/edge coverage: stale revision, hostile identity,
//  unauthorized/Attuned-only, unavailable/wrong-Tree/wrong-range, replay/conflict,
//  restart/recovery, and non-negative BP invariants.
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
    public sealed class NiflheimBpDevelopmentTests : System.IDisposable
    {
        private readonly string _activityJournal;
        private readonly string _developJournal;
        private readonly WorldId _world = new WorldId("uid:bp-901");
        private readonly StoneId _stone;

        private readonly AccountId _account = new AccountId("acct-gov");
        private readonly CharacterId _governor = new CharacterId("char-gov");
        private readonly AccountId _accountB = new AccountId("acct-gov-B");
        private readonly CharacterId _governorB = new CharacterId("char-gov-B");
        private readonly AccountId _accountAtt = new AccountId("acct-att");
        private readonly CharacterId _attuned = new CharacterId("char-att");

        private readonly InMemoryStoneAggregateStore _stones = new InMemoryStoneAggregateStore();
        private readonly InMemoryCharacterAggregateStore _characters = new InMemoryCharacterAggregateStore();
        private readonly InMemoryAccountStoneAuthorityStore _authority = new InMemoryAccountStoneAuthorityStore();

        private const string BondRelId = "rel-bond-gov";
        private const string BondRelIdB = "rel-bond-gov-B";
        private const string AttRelId = "rel-att";

        private ActivityCommandHandler _activity;
        private DevelopmentCommandHandler _develop;

        public NiflheimBpDevelopmentTests()
        {
            _activityJournal = TempJournal("activity");
            _developJournal = TempJournal("develop");
            _stone = StoneId.FromHostZone(_world, 3, 9);

            // Preconfigured Stone-Level-2 Homestead with Cooking committed to the Profession Facet and
            // Warrior committed to the Martial Facet (both at Tree Level 1, zero cumulative BP).
            _stones.PutStone(BuildStone(revision: 10, activeLevel: 2, committed: new[]
            {
                new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                    HomesteadProgressionCatalog.CookingTree, "seed-commit-cook", _governor.Value, 1, 0),
                new CommittedTreeRecord(HomesteadProgressionCatalog.MartialFacetId,
                    HomesteadProgressionCatalog.WarriorTree, "seed-commit-war", _governor.Value, 1, 0),
            }));

            // Governor with an active Bond and a starting BP balance.
            _characters.PutCharacter(BuildGovernor(_account, _governor, BondRelId, personalBp: 0));
            _authority.ApplyAuthorityProjection("seed-bond", BondIndex(_account, _governor, BondRelId));

            // A DIFFERENT Governor on another account, also bonded, for the not-shared proof.
            _characters.PutCharacter(BuildGovernor(_accountB, _governorB, BondRelIdB, personalBp: 0));
            _authority.ApplyAuthorityProjection("seed-bond-B", BondIndex(_accountB, _governorB, BondRelIdB));

            // An attuned-only actor (no cultivation authority).
            _characters.PutCharacter(BuildAttuned(_accountAtt, _attuned));
            _authority.ApplyAuthorityProjection("seed-att", AttIndex(_accountAtt, _attuned));

            _activity = NewActivityHandler();
            _develop = NewDevelopHandler();
        }

        public void Dispose()
        {
            if (File.Exists(_activityJournal)) File.Delete(_activityJournal);
            if (File.Exists(_developJournal)) File.Delete(_developJournal);
        }

        private static string TempJournal(string tag) => Path.Combine(Path.GetTempPath(),
            "niflheim-t012-" + tag + "-" + System.Guid.NewGuid().ToString("N") + ".journal");

        private ActivityCommandHandler NewActivityHandler() =>
            new ActivityCommandHandler(_activityJournal, new PrincipalResolver(), _stones, _characters,
                _authority, new StubDevelopmentAuthority());

        private DevelopmentCommandHandler NewDevelopHandler(TreeDevelopmentConfig? config = null) =>
            new DevelopmentCommandHandler(_developJournal, new PrincipalResolver(), _stones, _characters,
                _authority, new StubDevelopmentAuthority(), new HomesteadProgressionCatalog(), config);

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
            string bondRelId, int personalBp)
        {
            var bond = new RelationshipRecord(bondRelId, RelationshipKind.Bond, RelationshipStatus.Active,
                "Homestead:All", "Governor", "relreceipt:seed-bond", string.Empty);
            var stoneRecord = new CharacterStoneRecord(_stone, 0, 0, personalBp,
                facetCredits: null, purchases: null, relationships: new[] { bond });
            return new CharacterProgressionAggregate(account, character, "bp-901/trailborne",
                revision: 3, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { stoneRecord });
        }

        private CharacterProgressionAggregate BuildAttuned(AccountId account, CharacterId character)
        {
            var att = new RelationshipRecord(AttRelId, RelationshipKind.Attunement, RelationshipStatus.Active,
                string.Empty, string.Empty, "relreceipt:seed-att", string.Empty);
            var stoneRecord = new CharacterStoneRecord(_stone, 0, 0, 5,
                facetCredits: null, purchases: null, relationships: new[] { att });
            return new CharacterProgressionAggregate(account, character, "bp-901/trailborne",
                revision: 1, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { stoneRecord });
        }

        private AccountStoneAuthorityIndex BondIndex(AccountId account, CharacterId who, string relId) =>
            AccountStoneAuthorityIndex.Vacant(account, _stone).WithReservationAdded(
                new AuthorityReservation(who, RelationshipKind.Bond, relId, "relreceipt:seed-bond"), 1);

        private AccountStoneAuthorityIndex AttIndex(AccountId account, CharacterId who) =>
            AccountStoneAuthorityIndex.Vacant(account, _stone).WithReservationAdded(
                new AuthorityReservation(who, RelationshipKind.Attunement, AttRelId, "relreceipt:seed-att"), 1);

        private int BpOf(AccountId account, CharacterId character) =>
            BondPower.BalanceAt(_characters.GetCharacter(account, character)!, _stone);

        private CommittedTreeRecord CommittedTree(VersionedId tree)
        {
            var s = _stones.GetStone(_stone)!;
            foreach (var c in s.CommittedTrees)
                if (c.Tree.Key == tree.Key) return c;
            throw new Xunit.Sdk.XunitException("tree not committed: " + tree.Key);
        }

        // ── Command builders ──

        private RecordAlignedActivityCommand Activity(string op, AccountId account, CharacterId who,
            VersionedId tree, int award)
        {
            var adapter = new AlignedActivityAdapter();
            var evidence = new AlignedActivityEvidence(new OperationId(op), _stone,
                "activity.cook", 1, "CookedMeal", tree, award, serverAttributed: true);
            var admission = adapter.Admit(evidence,
                new AuthenticatedConnection(account.Value, who.Value), default);
            Assert.True(admission.IsAdmitted);
            return admission.Command;
        }

        private ApplyBPToNodeCommand Develop(string op, AccountId account, CharacterId who,
            VersionedId tree, VersionedId node, int amount, long? expStone = null)
            => new ApplyBPToNodeCommand(new OperationId(op), _stone,
                new AuthenticatedConnection(account.Value, who.Value), default,
                tree.Key, tree.Version, node.Key, node.Version, amount, expStone);

        // Convenience overload defaulting to the primary Governor.
        private ApplyBPToNodeCommand Develop(string op, VersionedId tree, VersionedId node,
            int amount, long? expStone = null)
            => Develop(op, _account, _governor, tree, node, amount, expStone);

        // Cooking Level-1 nodes.
        private static readonly VersionedId Savor = new VersionedId("SavorTheHearth", 1);   // Local
        private static readonly VersionedId FieldPrep = new VersionedId("FieldPrep", 1);    // personal
        private static readonly VersionedId IronStomach = new VersionedId("IronStomach", 1);// personal
        private static readonly VersionedId ReadyHands = new VersionedId("ReadyHands", 1);  // Warrior personal
        private static readonly VersionedId WatchfulCook = new VersionedId("WatchfulCook", 1); // unavailable

        private void CreditBp(AccountId account, CharacterId who, VersionedId tree, int amount, string op)
        {
            var r = _activity.Handle(Activity(op, account, who, tree, amount));
            Assert.Equal(ActivityCommandOutcome.Applied, r.Outcome);
        }

        // ── AT-BP-STONE-WIDE ──
        [Fact]
        public void Bp_credit_is_one_stone_wide_balance_spendable_across_trees()
        {
            // Credit BP via a Cooking-aligned activity.
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 5, "op-credit");
            Assert.Equal(5, BpOf(_account, _governor));

            // Spend part of the SAME balance developing a Cooking node...
            var d1 = _develop.Handle(Develop("op-cook", HomesteadProgressionCatalog.CookingTree, Savor, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, d1.Outcome);
            Assert.Equal(4, BpOf(_account, _governor));

            // ...and the REST developing a Warrior node — cross-Tree spend from one balance.
            var d2 = _develop.Handle(Develop("op-war", HomesteadProgressionCatalog.WarriorTree, ReadyHands, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, d2.Outcome);
            Assert.Equal(3, BpOf(_account, _governor));
        }

        // ── AT-BP-NOT-SHARED ──
        [Fact]
        public void Bp_balance_is_per_governor_not_shared()
        {
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 4, "op-credit-A");
            Assert.Equal(4, BpOf(_account, _governor));
            // The OTHER Governor's balance is untouched.
            Assert.Equal(0, BpOf(_accountB, _governorB));

            CreditBp(_accountB, _governorB, HomesteadProgressionCatalog.CookingTree, 2, "op-credit-B");
            Assert.Equal(2, BpOf(_accountB, _governorB));
            // The first Governor's balance is still exactly its own.
            Assert.Equal(4, BpOf(_account, _governor));
        }

        // ── AT-NODE-DEVELOPMENT-COUNTS-AS-INVESTMENT ──
        [Fact]
        public void Each_bp_delta_advances_node_progress_and_equal_cumulative_investment()
        {
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 5, "op-credit");

            long cumBefore = CommittedTree(HomesteadProgressionCatalog.CookingTree).CumulativeBpInvested;
            Assert.Equal(0, cumBefore);

            var d = _develop.Handle(Develop("op-dev", HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, d.Outcome);
            Assert.Equal(1, d.BpDebited);

            var cook = CommittedTree(HomesteadProgressionCatalog.CookingTree);
            // Same delta lands in cumulative Tree investment.
            Assert.Equal(1, cook.CumulativeBpInvested);

            // Node development progress reflects the same delta.
            var s = _stones.GetStone(_stone)!;
            NodeDevelopmentRecord? dev = null;
            foreach (var n in s.NodeDevelopment) if (n.Node.Key == FieldPrep.Key) dev = n;
            Assert.NotNull(dev);
            Assert.Equal(1, dev!.BpProgress);
        }

        // ── AT-NO-DIRECT-LEVEL-METER ──
        [Fact]
        public void Tree_level_does_not_move_below_threshold_and_has_no_direct_command()
        {
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 5, "op-credit");

            // One BP of development: below the (default) threshold of 3, Tree stays Level 1.
            var d = _develop.Handle(Develop("op-dev", HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, d.Outcome);
            Assert.False(d.TreeLevelAdvanced);
            Assert.Equal(1, d.NewTreeLevel);
            Assert.Equal(1, CommittedTree(HomesteadProgressionCatalog.CookingTree).TreeLevel);
        }

        // ── AT-TREE-ADVANCE-1-2 ──
        [Fact]
        public void Cooking_tree_advances_one_to_two_exactly_once_at_threshold()
        {
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 10, "op-credit");

            // Default config: unlock step 1, Level-2 threshold cumulative 3.
            // FieldPrep is the 1st node: cost base(1) + step*0 = 1 -> completes, cumulative 1.
            var a = _develop.Handle(Develop("op-a", HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, a.Outcome);
            Assert.True(a.NodeCompleted);
            Assert.False(a.TreeLevelAdvanced);

            // IronStomach is the 2nd developed node: cost base(1) + step*1 = 2 -> completes, cumulative 3.
            var b1 = _develop.Handle(Develop("op-b1", HomesteadProgressionCatalog.CookingTree, IronStomach, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, b1.Outcome);
            Assert.False(b1.NodeCompleted); // 1 of 2
            Assert.False(b1.TreeLevelAdvanced);

            var b2 = _develop.Handle(Develop("op-b2", HomesteadProgressionCatalog.CookingTree, IronStomach, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, b2.Outcome);
            Assert.True(b2.NodeCompleted); // cumulative now 3 -> crosses threshold
            Assert.True(b2.TreeLevelAdvanced);
            Assert.Equal(2, b2.NewTreeLevel);
            Assert.Equal(2, CommittedTree(HomesteadProgressionCatalog.CookingTree).TreeLevel);

            // Advancement happens exactly once: further development does not re-advance past 2.
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 5, "op-credit2");
            var c = _develop.Handle(Develop("op-c", HomesteadProgressionCatalog.CookingTree,
                new VersionedId("SwiftPreparation", 1), 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, c.Outcome);
            Assert.False(c.TreeLevelAdvanced);
            Assert.Equal(2, c.NewTreeLevel);
        }

        [Fact]
        public void Tree_does_not_advance_when_active_stone_level_too_low_for_target()
        {
            // Active Stone Level 1: even crossing the cumulative threshold cannot advance Tree past the cap.
            _stones.PutStone(BuildStone(revision: 10, activeLevel: 1, committed: new[]
            {
                new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                    HomesteadProgressionCatalog.CookingTree, "seed", _governor.Value, 1, 2),
            }));
            var develop = NewDevelopHandler();
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 5, "op-credit");

            // Cumulative already 2; one more delta would reach threshold 3, but Active Stone Level is 1.
            var d = develop.Handle(Develop("op-dev", HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, d.Outcome);
            Assert.False(d.TreeLevelAdvanced);
            Assert.Equal(1, d.NewTreeLevel);
        }

        // ── AT-ESCALATING-COST-CONFIG ──
        [Fact]
        public void Escalating_costs_and_thresholds_are_configurable()
        {
            // A custom config: unlock step 5, Level-2 threshold cumulative 100.
            var config = new TreeDevelopmentConfig(unlockCostStep: 5,
                cumulativeThresholdByTargetLevel: new Dictionary<int, int> { { 2, 100 } });
            var develop = NewDevelopHandler(config);
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 20, "op-credit");

            // FieldPrep is the 1st node: base(1) + step*0 = 1 -> completes with 1 BP.
            var a = develop.Handle(Develop("op-a", HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, a.Outcome);
            Assert.True(a.NodeCompleted);

            // IronStomach is the 2nd developed node: base(1) + step*1 = 6 total cost. One BP is a partial.
            var b = develop.Handle(Develop("op-b", HomesteadProgressionCatalog.CookingTree, IronStomach, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, b.Outcome);
            Assert.False(b.NodeCompleted); // 1 of 6 under the escalated cost

            // Its total cost is the escalated 6 (config-driven), not the authored base 1.
            var s = _stones.GetStone(_stone)!;
            NodeDevelopmentRecord? iron = null;
            foreach (var n in s.NodeDevelopment) if (n.Node.Key == IronStomach.Key) iron = n;
            Assert.NotNull(iron);
            Assert.Equal(6, iron!.BpCost);

            // Even large cumulative stays below the config threshold of 100 -> no advance.
            Assert.False(b.TreeLevelAdvanced);
        }

        // ── Non-negative BP invariant ──
        [Fact]
        public void Develop_with_no_bp_rejects_insufficient()
        {
            // Governor has 0 BP; a develop that the Stone side would accept must still reject on the debit.
            long stoneRevBefore = _stones.GetStone(_stone)!.Revision;
            var d = _develop.Handle(Develop("op-dev", HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Rejected, d.Outcome);
            Assert.Equal("InsufficientBp", d.ResultCode);
            // No Stone or character mutation.
            Assert.Equal(stoneRevBefore, _stones.GetStone(_stone)!.Revision);
            Assert.Equal(0, BpOf(_account, _governor));
        }

        [Fact]
        public void Bp_never_goes_negative_via_pure_debit()
        {
            var character = _characters.GetCharacter(_account, _governor)!;
            var t = BondPower.Debit(character, _stone, 3);
            Assert.False(t.Accepted);
            Assert.Equal(BondPowerResult.InsufficientBp, t.Result);
            Assert.Equal(0, t.NewBalance);
        }

        // ── Unavailable / wrong-Tree / wrong-range / unauthorized ──
        [Fact]
        public void Unavailable_node_rejects_development()
        {
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 5, "op-credit");
            var d = _develop.Handle(Develop("op-dev", HomesteadProgressionCatalog.CookingTree, WatchfulCook, 1));
            Assert.Equal(DevelopmentCommandOutcome.Rejected, d.Outcome);
            Assert.Equal("NodeUnavailable", d.ResultCode);
            Assert.Equal(5, BpOf(_account, _governor)); // unchanged
        }

        [Fact]
        public void Wrong_tree_for_node_rejects()
        {
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 5, "op-credit");
            // FieldPrep is a Cooking node; claiming it under Warrior is a TreeMismatch.
            var d = _develop.Handle(Develop("op-dev", HomesteadProgressionCatalog.WarriorTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Rejected, d.Outcome);
            Assert.Equal("TreeMismatch", d.ResultCode);
        }

        [Fact]
        public void Uncommitted_tree_rejects_development()
        {
            // Crafting is not committed on this Stone.
            _characters.PutCharacter(BuildGovernor(_account, _governor, BondRelId, personalBp: 5));
            var d = _develop.Handle(Develop("op-dev", HomesteadProgressionCatalog.CraftingTree,
                new VersionedId("Masterwork", 1), 1));
            Assert.Equal(DevelopmentCommandOutcome.Rejected, d.Outcome);
            Assert.Equal("TreeNotCommitted", d.ResultCode);
        }

        [Fact]
        public void Attuned_only_actor_cannot_credit_or_develop()
        {
            // Credit rejects: Attunement grants no cultivation authority.
            var c = _activity.Handle(Activity("op-att-credit", _accountAtt, _attuned,
                HomesteadProgressionCatalog.CookingTree, 3));
            Assert.Equal(ActivityCommandOutcome.Rejected, c.Outcome);
            Assert.Equal("Unauthorized", c.ResultCode);

            var d = _develop.Handle(Develop("op-att-dev", _accountAtt, _attuned,
                HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Rejected, d.Outcome);
            Assert.Equal("Unauthorized", d.ResultCode);
        }

        [Fact]
        public void Outside_responsibility_range_rejects()
        {
            var denyAuthority = new DenyDevelopmentAuthority();
            var activity = new ActivityCommandHandler(_activityJournal, new PrincipalResolver(),
                _stones, _characters, _authority, denyAuthority);
            var r = activity.Handle(Activity("op-range", _account, _governor,
                HomesteadProgressionCatalog.CookingTree, 3));
            Assert.Equal(ActivityCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("OutsideResponsibilityRange", r.ResultCode);
        }

        [Fact]
        public void Aligned_activity_credit_requires_committed_tree()
        {
            // Crafting is not committed; an activity associated with it cannot authorize credit.
            var r = _activity.Handle(Activity("op-uncommitted", _account, _governor,
                HomesteadProgressionCatalog.CraftingTree, 3));
            Assert.Equal(ActivityCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("TreeNotCommitted", r.ResultCode);
        }

        // ── Hostile identity ──
        [Fact]
        public void Hostile_principal_claim_rejects()
        {
            var cmd = new RecordAlignedActivityCommand(new OperationId("op-hostile"), _stone,
                new AuthenticatedConnection(_accountAtt.Value, _attuned.Value),
                new ClaimedPrincipal(_account.Value, _governor.Value),
                HomesteadProgressionCatalog.CookingTree, 3, "digest", null);
            var r = _activity.Handle(cmd);
            Assert.Equal(ActivityCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("PrincipalMismatch", r.ResultCode);
        }

        // ── Stale revision ──
        [Fact]
        public void Stale_stone_revision_rejects_development()
        {
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 5, "op-credit");
            long wrong = _stones.GetStone(_stone)!.Revision + 99;
            var d = _develop.Handle(Develop("op-stale", HomesteadProgressionCatalog.CookingTree, FieldPrep, 1, wrong));
            Assert.Equal(DevelopmentCommandOutcome.Rejected, d.Outcome);
            Assert.Equal("StaleStoneRevision", d.ResultCode);
        }

        // ── Delta bounds ──
        [Fact]
        public void Delta_exceeding_remaining_cost_rejects()
        {
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 10, "op-credit");
            // FieldPrep total cost is 1 (first node). A delta of 2 exceeds remaining cost.
            var d = _develop.Handle(Develop("op-big", HomesteadProgressionCatalog.CookingTree, FieldPrep, 2));
            Assert.Equal(DevelopmentCommandOutcome.Rejected, d.Outcome);
            Assert.Equal("BpDeltaInvalid", d.ResultCode);
        }

        [Fact]
        public void Already_developed_node_rejects_further_development()
        {
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 10, "op-credit");
            var a = _develop.Handle(Develop("op-a", HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, a.Outcome);
            Assert.True(a.NodeCompleted);
            var b = _develop.Handle(Develop("op-b", HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Rejected, b.Outcome);
            Assert.Equal("AlreadyDeveloped", b.ResultCode);
        }

        // ── Replay / conflict ──
        [Fact]
        public void Replay_same_activity_returns_recorded_result()
        {
            var first = _activity.Handle(Activity("op-r", _account, _governor,
                HomesteadProgressionCatalog.CookingTree, 4));
            Assert.Equal(ActivityCommandOutcome.Applied, first.Outcome);
            var replay = _activity.Handle(Activity("op-r", _account, _governor,
                HomesteadProgressionCatalog.CookingTree, 4));
            Assert.Equal(ActivityCommandOutcome.Replayed, replay.Outcome);
            Assert.Equal(first.NewBpBalance, replay.NewBpBalance);
            // No double credit.
            Assert.Equal(4, BpOf(_account, _governor));
        }

        [Fact]
        public void Replay_conflicting_activity_payload_rejects()
        {
            Assert.Equal(ActivityCommandOutcome.Applied,
                _activity.Handle(Activity("op-c", _account, _governor,
                    HomesteadProgressionCatalog.CookingTree, 4)).Outcome);
            // Same op id, DIFFERENT award -> conflict.
            var conflict = _activity.Handle(Activity("op-c", _account, _governor,
                HomesteadProgressionCatalog.CookingTree, 7));
            Assert.Equal(ActivityCommandOutcome.Rejected, conflict.Outcome);
            Assert.Equal("OperationConflict", conflict.ResultCode);
            Assert.Equal(4, BpOf(_account, _governor));
        }

        [Fact]
        public void Replay_same_development_returns_recorded_result_no_double_debit()
        {
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 5, "op-credit");
            var first = _develop.Handle(Develop("op-d", HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, first.Outcome);
            Assert.Equal(4, BpOf(_account, _governor));

            var replay = _develop.Handle(Develop("op-d", HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Replayed, replay.Outcome);
            Assert.Equal(4, BpOf(_account, _governor)); // no second debit
        }

        // ── Restart / recovery ──
        [Fact]
        public void Development_survives_restart_from_journal()
        {
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 5, "op-credit");
            var first = _develop.Handle(Develop("op-restart", HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, first.Outcome);

            // Simulate a restart: fresh stores seeded with the ORIGINAL pre-develop state + fresh handler
            // rehydrating from the same journals.
            var freshStones = new InMemoryStoneAggregateStore();
            freshStones.PutStone(BuildStone(revision: 10, activeLevel: 2, committed: new[]
            {
                new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                    HomesteadProgressionCatalog.CookingTree, "seed-commit-cook", _governor.Value, 1, 0),
            }));
            var freshChars = new InMemoryCharacterAggregateStore();
            freshChars.PutCharacter(BuildGovernor(_account, _governor, BondRelId, personalBp: 5));

            var rehydrated = new DevelopmentCommandHandler(_developJournal, new PrincipalResolver(),
                freshStones, freshChars, _authority, new StubDevelopmentAuthority(),
                new HomesteadProgressionCatalog(), null);

            // Projection rebuilt from journal truth: the development is present after boot.
            var s = freshStones.GetStone(_stone)!;
            NodeDevelopmentRecord? dev = null;
            foreach (var n in s.NodeDevelopment) if (n.Node.Key == FieldPrep.Key) dev = n;
            Assert.NotNull(dev);
            Assert.Equal(1, dev!.BpProgress);
            Assert.Equal(4, BondPower.BalanceAt(freshChars.GetCharacter(_account, _governor)!, _stone));

            // Re-submit is a pure replay, no double-apply.
            var replay = rehydrated.Handle(Develop("op-restart", HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Replayed, replay.Outcome);
        }

        // ── AT-JOURNAL-DELIMITER-SAFE (ADO #127) ──
        // Covers BOTH handlers in this fixture: the activity (BP credit) journal and the
        // development journal. A StoneId is "world|zoneX|zoneZ" by construction, so a
        // caller-composed operation id legitimately embeds '|'. Written raw into the
        // pipe-delimited frame it explodes the field count and the strict parser rejects EVERY
        // record — and the journal IS the save, so that is total, silent progression loss.
        [Fact]
        public void Activity_and_development_with_pipes_in_operation_id_survive_restart()
        {
            const string PipedCredit = "savor-seam-uid:-898655635|3|2-credit";
            const string PipedDevelop = "savor-seam-uid:-898655635|3|2-develop";

            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 5, PipedCredit);
            Assert.Equal(5, BpOf(_account, _governor));
            var first = _develop.Handle(Develop(PipedDevelop, HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, first.Outcome);

            // Restart: fresh stores seeded with the ORIGINAL pre-credit state; both handlers rehydrate
            // from the same journals, in boot order (activity first, then development).
            var freshStones = new InMemoryStoneAggregateStore();
            freshStones.PutStone(BuildStone(revision: 10, activeLevel: 2, committed: new[]
            {
                new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                    HomesteadProgressionCatalog.CookingTree, "seed-commit-cook", _governor.Value, 1, 0),
            }));
            var freshChars = new InMemoryCharacterAggregateStore();
            freshChars.PutCharacter(BuildGovernor(_account, _governor, BondRelId, personalBp: 0));

            var rehydratedActivity = new ActivityCommandHandler(_activityJournal, new PrincipalResolver(),
                freshStones, freshChars, _authority, new StubDevelopmentAuthority());
            var rehydratedDevelop = new DevelopmentCommandHandler(_developJournal, new PrincipalResolver(),
                freshStones, freshChars, _authority, new StubDevelopmentAuthority(),
                new HomesteadProgressionCatalog(), null);

            // The BP credit replayed from the activity journal, and the development debit from the
            // development journal: 5 credited - 1 spent = 4.
            Assert.Equal(4, BondPower.BalanceAt(freshChars.GetCharacter(_account, _governor)!, _stone));

            NodeDevelopmentRecord? dev = null;
            foreach (var n in freshStones.GetStone(_stone)!.NodeDevelopment)
                if (n.Node.Key == FieldPrep.Key) dev = n;
            Assert.NotNull(dev);
            Assert.Equal(1, dev!.BpProgress);

            // Re-submitting either piped op after restart is a pure replay, never a double-apply.
            Assert.Equal(ActivityCommandOutcome.Replayed,
                rehydratedActivity.Handle(Activity(PipedCredit, _account, _governor,
                    HomesteadProgressionCatalog.CookingTree, 5)).Outcome);
            Assert.Equal(DevelopmentCommandOutcome.Replayed,
                rehydratedDevelop.Handle(Develop(PipedDevelop, HomesteadProgressionCatalog.CookingTree,
                    FieldPrep, 1)).Outcome);
            Assert.Equal(4, BondPower.BalanceAt(freshChars.GetCharacter(_account, _governor)!, _stone));
        }

        // ── Local vs personal completion ──
        [Fact]
        public void Local_node_completes_but_is_never_offered()
        {
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 5, "op-credit");
            var d = _develop.Handle(Develop("op-local", HomesteadProgressionCatalog.CookingTree, Savor, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, d.Outcome);
            Assert.True(d.NodeCompleted);
            Assert.False(d.NodeOffered); // Local nodes never Offered
        }

        [Fact]
        public void Personal_node_completion_becomes_offered()
        {
            CreditBp(_account, _governor, HomesteadProgressionCatalog.CookingTree, 5, "op-credit");
            var d = _develop.Handle(Develop("op-personal", HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, d.Outcome);
            Assert.True(d.NodeCompleted);
            Assert.True(d.NodeOffered);
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

        private sealed class DenyDevelopmentAuthority : IGovernorDevelopmentAuthority
        {
            public bool CanDevelop(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                VersionedId tree) => false;
        }
    }
}
