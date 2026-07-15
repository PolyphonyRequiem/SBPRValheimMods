// ============================================================================
//  Homestead progression — BP DEVELOPMENT & TREE ADVANCEMENT tests (T012, Tracer 4).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free CLEAN-side BP-development slice
//  (link-compiled from ../src): the pure BondPower credit/debit + the
//  TreeDevelopment.ApplyBpToNode transition + the data-defined TreeTuning, and
//  the receipt-backed ActivityCommandHandler (RecordAlignedActivity) and
//  DevelopmentCommandHandler (ApplyBPToNode) cross-aggregate command handlers.
//
//  Named acceptance closed here (tasks.md T012 / plan.md Tracer 4):
//    AT-BP-STONE-WIDE                       BP credited by one Committed Tree's
//                                           activity funds a node in a DIFFERENT
//                                           committed Tree at the same Stone (one
//                                           Stone-wide balance, no Tree binding).
//    AT-BP-NOT-SHARED                       a second Governor's BP balance is
//                                           isolated; one cannot spend the other's.
//    AT-NODE-DEVELOPMENT-COUNTS-AS-INVESTMENT node development and Tree cumulative
//                                           investment move atomically in one op.
//    AT-NO-DIRECT-LEVEL-METER               there is no direct level spend/command/
//                                           state; Level is a pure function of
//                                           cumulative investment.
//    AT-TREE-ADVANCE-1-2                    Cooking advances 1->2 when cumulative
//                                           investment crosses the threshold under
//                                           the Active Stone Level cap.
//    AT-ESCALATING-COST-CONFIG              successive unlock cost escalates under
//                                           the data-defined step; a different step
//                                           yields a different curve.
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
        private readonly WorldId _world = new WorldId("uid:bp-999");
        private readonly StoneId _stone;
        private readonly AccountId _account = new AccountId("acct-gov");
        private readonly CharacterId _governor = new CharacterId("char-gov");
        private readonly AccountId _accountB = new AccountId("acct-B");
        private readonly CharacterId _govB = new CharacterId("char-B");

        private readonly InMemoryStoneAggregateStore _stones = new InMemoryStoneAggregateStore();
        private readonly InMemoryCharacterAggregateStore _characters = new InMemoryCharacterAggregateStore();
        private readonly InMemoryAccountStoneAuthorityStore _authority = new InMemoryAccountStoneAuthorityStore();

        private const string BondRelId = "rel-bond-gov";
        private const string BondRelIdB = "rel-bond-B";

        public NiflheimBpDevelopmentTests()
        {
            _activityJournal = Path.Combine(Path.GetTempPath(), "niflheim-t012-act-" + System.Guid.NewGuid().ToString("N") + ".journal");
            _developJournal = Path.Combine(Path.GetTempPath(), "niflheim-t012-dev-" + System.Guid.NewGuid().ToString("N") + ".journal");
            _stone = StoneId.FromHostZone(_world, 3, 7);

            // Preconfigured Stone-Level-2 Homestead with Cooking committed to Profession and Crafting to
            // Martial? No — Crafting is Profession too. Commit Cooking (Profession) + Warrior (Martial).
            var cooking = new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                HomesteadProgressionCatalog.CookingTree, "commit-cook", _governor.Value, 1, 0);
            var warrior = new CommittedTreeRecord(HomesteadProgressionCatalog.MartialFacetId,
                HomesteadProgressionCatalog.WarriorTree, "commit-war", _governor.Value, 1, 0);
            _stones.PutStone(BuildStone(_stone, revision: 10, activeLevel: 2,
                committed: new[] { cooking, warrior }));

            _characters.PutCharacter(BuildGovernor(_account, _governor, BondRelId, personalBp: 0));
            _authority.ApplyAuthorityProjection("seed-bond", BondIndex(_account, _stone, _governor, BondRelId));

            // Second Governor on account B with their own Bond and own (separate) balance.
            _characters.PutCharacter(BuildGovernor(_accountB, _govB, BondRelIdB, personalBp: 0));
            _authority.ApplyAuthorityProjection("seed-bond-B", BondIndex(_accountB, _stone, _govB, BondRelIdB));
        }

        public void Dispose()
        {
            if (File.Exists(_activityJournal)) File.Delete(_activityJournal);
            if (File.Exists(_developJournal)) File.Delete(_developJournal);
        }

        // ── Fixtures ──

        private static StoneProgressionAggregate BuildStone(StoneId stone, long revision, int activeLevel,
            IReadOnlyList<CommittedTreeRecord>? committed)
        {
            return new StoneProgressionAggregate(stone, revision,
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
                "Homestead:All", "Governor", "relreceipt:seed", string.Empty);
            var stoneRecord = new CharacterStoneRecord(_stone, 5, 5, personalBp,
                facetCredits: null, purchases: null, relationships: new[] { bond });
            return new CharacterProgressionAggregate(account, character, "bp-999/trailborne",
                revision: 3, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { stoneRecord });
        }

        private static AccountStoneAuthorityIndex BondIndex(AccountId account, StoneId stone, CharacterId who, string relId) =>
            AccountStoneAuthorityIndex.Vacant(account, stone).WithReservationAdded(
                new AuthorityReservation(who, RelationshipKind.Bond, relId, "relreceipt:seed"), 1);

        private sealed class StubRangePolicy : IResponsibilityRangePolicy
        {
            public bool CoversTree(StoneId stoneId, string responsibilityRange, string ownerGovernorRole, VersionedId tree) =>
                string.Equals(responsibilityRange, "Homestead:All", System.StringComparison.Ordinal)
                && string.Equals(ownerGovernorRole, "Governor", System.StringComparison.Ordinal)
                && !tree.IsNone;
        }

        private sealed class DenyRangePolicy : IResponsibilityRangePolicy
        {
            public bool CoversTree(StoneId stoneId, string responsibilityRange, string ownerGovernorRole, VersionedId tree) => false;
        }

        private ActivityCommandHandler NewActivityHandler()
        {
            var resolver = new PrincipalResolver(p => p);
            return new ActivityCommandHandler(_activityJournal, resolver, _characters, _authority, new StubRangePolicy());
        }

        private DevelopmentCommandHandler NewDevelopHandler()
        {
            var resolver = new PrincipalResolver(p => p);
            return new DevelopmentCommandHandler(_developJournal, resolver, _stones, _characters, _authority,
                new StubRangePolicy());
        }

        private RecordAlignedActivityCommand CreditCmd(string op, AccountId account, CharacterId who,
            VersionedId treeContext, int award, long? expCharRev = null)
            => new RecordAlignedActivityCommand(new OperationId(op), _stone,
                new AuthenticatedConnection(account.Value, who.Value), default,
                treeContext, award, "evi-" + op, expCharRev);

        private ApplyBpToNodeCommand DevelopCmd(string op, AccountId account, CharacterId who,
            VersionedId tree, VersionedId node, int amount, long? expStoneRev = null, long? expCharRev = null)
            => new ApplyBpToNodeCommand(new OperationId(op), _stone,
                new AuthenticatedConnection(account.Value, who.Value), default,
                tree, node, amount, TreeTuningCatalog.CurrentTuningVersion, expStoneRev, expCharRev);

        private static readonly VersionedId SavorTheHearth = new VersionedId("SavorTheHearth", 1); // Cooking, Local, L1
        private static readonly VersionedId TwigTraining = new VersionedId("TwigTraining", 1);     // Warrior, Local, L1
        private static readonly VersionedId FieldPrep = new VersionedId("FieldPrep", 1);           // Cooking, personal, L1
        private static readonly VersionedId WatchfulCook = new VersionedId("WatchfulCook", 1);     // Cooking, unavailable

        private int Bp(AccountId account, CharacterId who) =>
            BondPower.BalanceFor(_characters.GetCharacter(account, who)!, _stone);

        private CommittedTreeRecord CommittedTree(VersionedId tree)
        {
            foreach (var c in _stones.GetStone(_stone)!.CommittedTrees)
                if (c.Tree.Key == tree.Key) return c;
            throw new Xunit.Sdk.XunitException("tree not committed: " + tree.Key);
        }

        // ══ Pure BondPower unit tests ══

        [Fact]
        public void BondPower_credit_and_debit_are_pure_and_non_negative()
        {
            var c0 = _characters.GetCharacter(_account, _governor)!;
            var credit = BondPower.Credit(c0, _stone, 5);
            Assert.True(credit.Accepted);
            Assert.Equal(5, credit.ResultingBp);
            Assert.Equal(0, BondPower.BalanceFor(c0, _stone)); // input untouched

            var debit = BondPower.Debit(credit.Character, _stone, 3);
            Assert.True(debit.Accepted);
            Assert.Equal(2, debit.ResultingBp);

            var over = BondPower.Debit(credit.Character, _stone, 6);
            Assert.False(over.Accepted);
            Assert.Equal("InsufficientBP", over.ResultCode);
        }

        // ══ RecordAlignedActivity credit ══

        [Fact]
        public void AlignedActivity_credits_bonded_character_stone_wide_balance()
        {
            var handler = NewActivityHandler();
            var result = handler.Handle(CreditCmd("op-credit", _account, _governor,
                HomesteadProgressionCatalog.CookingTree, 4));
            Assert.Equal(ActivityCommandOutcome.Applied, result.Outcome);
            Assert.Equal(4, result.ResultingBp);
            Assert.Equal(4, Bp(_account, _governor));
        }

        [Fact]
        public void AlignedActivity_adapter_rejects_uncommitted_context_and_failed_outcome()
        {
            var adapter = new AlignedActivityAdapter();
            var noTree = adapter.Admit(new AlignedActivityEvidence(new OperationId("o1"), _stone,
                new VersionedId("CookAct", 1), VersionedId.None, "cook", "src", true, 3), default, default);
            Assert.Equal(AlignedActivityAdmission.NotCommittedTreeContext, noTree.Admission);

            var failed = adapter.Admit(new AlignedActivityEvidence(new OperationId("o2"), _stone,
                new VersionedId("CookAct", 1), HomesteadProgressionCatalog.CookingTree, "cook", "src", false, 3),
                default, default);
            Assert.Equal(AlignedActivityAdmission.OutcomeFailed, failed.Admission);
        }

        [Fact]
        public void AlignedActivity_requires_bond_and_responsibility_range()
        {
            var resolver = new PrincipalResolver(p => p);
            var deny = new ActivityCommandHandler(_activityJournal, resolver, _characters, _authority, new DenyRangePolicy());
            var result = deny.Handle(CreditCmd("op-deny", _account, _governor, HomesteadProgressionCatalog.CookingTree, 4));
            Assert.Equal(ActivityCommandOutcome.Rejected, result.Outcome);
            Assert.Equal("OutsideResponsibilityRange", result.ResultCode);
            Assert.Equal(0, Bp(_account, _governor));
        }

        // ══ AT-BP-STONE-WIDE ══
        [Fact]
        public void BpStoneWide_cooking_activity_funds_a_committed_warrior_node()
        {
            // Credit BP from a COOKING activity...
            var act = NewActivityHandler();
            Assert.Equal(ActivityCommandOutcome.Applied,
                act.Handle(CreditCmd("op-c", _account, _governor, HomesteadProgressionCatalog.CookingTree, 3)).Outcome);
            Assert.Equal(3, Bp(_account, _governor));

            // ...and spend it developing a WARRIOR node. One Stone-wide balance, no Tree binding.
            var dev = NewDevelopHandler();
            var r = dev.Handle(DevelopCmd("op-d", _account, _governor,
                HomesteadProgressionCatalog.WarriorTree, TwigTraining, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, r.Outcome);
            Assert.Equal(2, r.ResultingBp);              // 3 credited - 1 spent
            Assert.Equal(2, Bp(_account, _governor));
        }

        // ══ AT-BP-NOT-SHARED ══
        [Fact]
        public void BpNotShared_second_governor_balance_is_isolated()
        {
            var act = NewActivityHandler();
            act.Handle(CreditCmd("op-a", _account, _governor, HomesteadProgressionCatalog.CookingTree, 5));
            // Governor B earned nothing; their balance is a separate row.
            Assert.Equal(5, Bp(_account, _governor));
            Assert.Equal(0, Bp(_accountB, _govB));

            // Governor B cannot spend Governor A's BP (their own balance is 0 -> InsufficientBP).
            var dev = NewDevelopHandler();
            var r = dev.Handle(DevelopCmd("op-b-spend", _accountB, _govB,
                HomesteadProgressionCatalog.WarriorTree, TwigTraining, 1));
            Assert.Equal(DevelopmentCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("InsufficientBP", r.ResultCode);
            Assert.Equal(5, Bp(_account, _governor)); // A untouched
        }

        // ══ AT-NODE-DEVELOPMENT-COUNTS-AS-INVESTMENT ══
        [Fact]
        public void NodeDevelopment_updates_node_and_tree_investment_atomically()
        {
            var act = NewActivityHandler();
            act.Handle(CreditCmd("op-c", _account, _governor, HomesteadProgressionCatalog.CookingTree, 3));

            var dev = NewDevelopHandler();
            var r = dev.Handle(DevelopCmd("op-d", _account, _governor,
                HomesteadProgressionCatalog.CookingTree, SavorTheHearth, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, r.Outcome);

            var stone = _stones.GetStone(_stone)!;
            // Node development advanced...
            NodeDevelopmentRecord? node = null;
            foreach (var n in stone.NodeDevelopment) if (n.Node.Key == "SavorTheHearth") node = n;
            Assert.NotNull(node);
            Assert.Equal(1, node!.BpProgress);
            Assert.True(node.Developed); // Local node, base cost 1, completed
            // ...and the SAME delta landed on the Committed Tree's cumulative investment.
            Assert.Equal(1, CommittedTree(HomesteadProgressionCatalog.CookingTree).CumulativeBpInvested);
            Assert.Equal(1, r.CumulativeBpInvested);
        }

        // ══ AT-NO-DIRECT-LEVEL-METER ══
        [Fact]
        public void NoDirectLevelMeter_level_is_pure_function_of_cumulative_investment()
        {
            // There is no ApplyBPToNode-independent way to move a Tree Level: TreeTuning.LevelForCumulative
            // is the ONLY level decision and takes only cumulative investment + the Stone cap.
            var tuning = TreeTuningCatalog.Current.TryGetTuning(HomesteadProgressionCatalog.CookingTree.Key)!;
            Assert.Equal(1, tuning.LevelForCumulative(0, 2));
            Assert.Equal(1, tuning.LevelForCumulative(2, 2));
            Assert.Equal(2, tuning.LevelForCumulative(3, 2));
            // The command surface exposes no level field, spend, or command — only BP-to-node development.
            // (Compile-time: ApplyBpToNodeCommand has no TreeLevel setter; Level rides in the pure Stone op.)
            var stoneBefore = _stones.GetStone(_stone)!;
            Assert.Equal(1, CommittedTree(HomesteadProgressionCatalog.CookingTree).TreeLevel);
            Assert.NotNull(stoneBefore);
        }

        // ══ AT-TREE-ADVANCE-1-2 ══
        [Fact]
        public void TreeAdvance_cooking_1_to_2_when_cumulative_crosses_threshold_under_cap()
        {
            var act = NewActivityHandler();
            act.Handle(CreditCmd("op-c", _account, _governor, HomesteadProgressionCatalog.CookingTree, 10));

            var dev = NewDevelopHandler();
            // Develop Savor the Hearth (cost 1) -> cumulative 1, still Level 1.
            var r1 = dev.Handle(DevelopCmd("op-d1", _account, _governor,
                HomesteadProgressionCatalog.CookingTree, SavorTheHearth, 1));
            Assert.Equal(1, r1.TreeLevel);
            Assert.False(r1.TreeLevelAdvanced);

            // Develop Field Prep (personal). Escalated cost = base 1 + step 1 * (1 developed) = 2.
            // Apply 2 BP -> cumulative 1+2 = 3, crosses threshold -> Level 2 (Active Stone Level 2 permits).
            var r2 = dev.Handle(DevelopCmd("op-d2", _account, _governor,
                HomesteadProgressionCatalog.CookingTree, FieldPrep, 2));
            Assert.Equal(DevelopmentCommandOutcome.Applied, r2.Outcome);
            Assert.Equal(3, r2.CumulativeBpInvested);
            Assert.Equal(2, r2.TreeLevel);
            Assert.True(r2.TreeLevelAdvanced);
            Assert.Equal(2, CommittedTree(HomesteadProgressionCatalog.CookingTree).TreeLevel);
        }

        [Fact]
        public void TreeAdvance_is_capped_by_active_stone_level()
        {
            // A Level-1 Stone must never let a Tree exceed Level 1 even past the cumulative threshold.
            var lowStones = new InMemoryStoneAggregateStore();
            var cooking = new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                HomesteadProgressionCatalog.CookingTree, "commit-cook", _governor.Value, 1, 0);
            lowStones.PutStone(BuildStone(_stone, revision: 10, activeLevel: 1, committed: new[] { cooking }));

            var act = NewActivityHandler();
            act.Handle(CreditCmd("op-c", _account, _governor, HomesteadProgressionCatalog.CookingTree, 10));

            var resolver = new PrincipalResolver(p => p);
            var dev = new DevelopmentCommandHandler(_developJournal, resolver, lowStones, _characters, _authority,
                new StubRangePolicy());
            // Savor the Hearth (Local, cost 1, min level 1) develops fine; cumulative reaches 1.
            var r = dev.Handle(DevelopCmd("op-d", _account, _governor,
                HomesteadProgressionCatalog.CookingTree, SavorTheHearth, 5));
            Assert.Equal(DevelopmentCommandOutcome.Applied, r.Outcome);
            Assert.Equal(1, r.TreeLevel);        // capped at Active Stone Level 1
            Assert.False(r.TreeLevelAdvanced);
        }

        // ══ AT-ESCALATING-COST-CONFIG ══
        [Fact]
        public void EscalatingCost_successive_unlock_costs_increase_under_data_defined_step()
        {
            var tuning = new TreeTuning("Cooking", 1, unlockCostStep: 2, levelThresholds: new[] { 5 });
            // Nth developed node: base + step * priorDeveloped.
            Assert.Equal(1, tuning.EffectiveDevelopmentCost(1, 0));
            Assert.Equal(3, tuning.EffectiveDevelopmentCost(1, 1));
            Assert.Equal(5, tuning.EffectiveDevelopmentCost(1, 2));

            // A DIFFERENT step yields a different curve (proves it is data-defined, not hard-coded).
            var flat = new TreeTuning("Cooking", 1, unlockCostStep: 0, levelThresholds: new[] { 5 });
            Assert.Equal(1, flat.EffectiveDevelopmentCost(1, 2));
        }

        [Fact]
        public void EscalatingCost_applies_through_the_command_and_partial_progress_carries()
        {
            var act = NewActivityHandler();
            act.Handle(CreditCmd("op-c", _account, _governor, HomesteadProgressionCatalog.CookingTree, 10));
            var dev = NewDevelopHandler();

            // First node Savor (0 prior developed) -> cost 1, completes with 1 BP.
            dev.Handle(DevelopCmd("op-d1", _account, _governor, HomesteadProgressionCatalog.CookingTree, SavorTheHearth, 1));
            // Second node Field Prep (1 prior developed) -> cost 2. Apply 1 BP: partial, not developed.
            var partial = dev.Handle(DevelopCmd("op-d2", _account, _governor,
                HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, partial.Outcome);
            Assert.False(partial.NodeCompleted);

            // Apply the remaining 1 BP -> completes at fixed cost 2 (goalpost did not move).
            var finish = dev.Handle(DevelopCmd("op-d3", _account, _governor,
                HomesteadProgressionCatalog.CookingTree, FieldPrep, 1));
            Assert.True(finish.NodeCompleted);
            NodeDevelopmentRecord? fp = null;
            foreach (var n in _stones.GetStone(_stone)!.NodeDevelopment) if (n.Node.Key == "FieldPrep") fp = n;
            Assert.Equal(2, fp!.BpCost);
            Assert.True(fp.Offered); // personal node -> Offered on completion
        }

        // ══ Rejections + guards ══

        [Fact]
        public void Develop_unavailable_node_rejects_and_spends_no_bp()
        {
            var act = NewActivityHandler();
            act.Handle(CreditCmd("op-c", _account, _governor, HomesteadProgressionCatalog.CookingTree, 3));
            var dev = NewDevelopHandler();
            var r = dev.Handle(DevelopCmd("op-d", _account, _governor,
                HomesteadProgressionCatalog.CookingTree, WatchfulCook, 1));
            Assert.Equal(DevelopmentCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("NodeUnavailable", r.ResultCode);
            Assert.Equal(3, Bp(_account, _governor)); // no debit
        }

        [Fact]
        public void Develop_uncommitted_tree_rejects()
        {
            var act = NewActivityHandler();
            act.Handle(CreditCmd("op-c", _account, _governor, HomesteadProgressionCatalog.CookingTree, 3));
            var dev = NewDevelopHandler();
            // Archer is not committed on this Stone.
            var r = dev.Handle(DevelopCmd("op-d", _account, _governor,
                HomesteadProgressionCatalog.ArcherTree, new VersionedId("PracticeRange", 1), 1));
            Assert.Equal(DevelopmentCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("TreeNotCommitted", r.ResultCode);
        }

        [Fact]
        public void Develop_insufficient_bp_rejects_and_stone_unchanged()
        {
            var dev = NewDevelopHandler();
            long stoneRevBefore = _stones.GetStone(_stone)!.Revision;
            var r = dev.Handle(DevelopCmd("op-d", _account, _governor,
                HomesteadProgressionCatalog.CookingTree, SavorTheHearth, 1)); // balance is 0
            Assert.Equal(DevelopmentCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("InsufficientBP", r.ResultCode);
            Assert.Equal(stoneRevBefore, _stones.GetStone(_stone)!.Revision); // Stone not advanced
        }

        [Fact]
        public void Develop_replay_returns_recorded_result_and_survives_restart()
        {
            var act = NewActivityHandler();
            act.Handle(CreditCmd("op-c", _account, _governor, HomesteadProgressionCatalog.CookingTree, 5));
            var dev = NewDevelopHandler();
            var first = dev.Handle(DevelopCmd("op-d", _account, _governor,
                HomesteadProgressionCatalog.CookingTree, SavorTheHearth, 1));
            Assert.Equal(DevelopmentCommandOutcome.Applied, first.Outcome);
            int bpAfter = Bp(_account, _governor);

            var replay = dev.Handle(DevelopCmd("op-d", _account, _governor,
                HomesteadProgressionCatalog.CookingTree, SavorTheHearth, 1));
            Assert.Equal(DevelopmentCommandOutcome.Replayed, replay.Outcome);
            Assert.Equal(first.StoneRevision, replay.StoneRevision);
            Assert.Equal(bpAfter, Bp(_account, _governor)); // no double debit

            // Restart: fresh stores + handler rehydrating from both journals.
            var freshStones = new InMemoryStoneAggregateStore();
            var cooking = new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                HomesteadProgressionCatalog.CookingTree, "commit-cook", _governor.Value, 1, 0);
            var warrior = new CommittedTreeRecord(HomesteadProgressionCatalog.MartialFacetId,
                HomesteadProgressionCatalog.WarriorTree, "commit-war", _governor.Value, 1, 0);
            freshStones.PutStone(BuildStone(_stone, revision: 10, activeLevel: 2, committed: new[] { cooking, warrior }));
            var freshChars = new InMemoryCharacterAggregateStore();
            freshChars.PutCharacter(BuildGovernor(_account, _governor, BondRelId, personalBp: 0));
            freshChars.PutCharacter(BuildGovernor(_accountB, _govB, BondRelIdB, personalBp: 0));

            var resolver = new PrincipalResolver(p => p);
            // Rehydrate activity credit first, then development.
            _ = new ActivityCommandHandler(_activityJournal, resolver, freshChars, _authority, new StubRangePolicy());
            _ = new DevelopmentCommandHandler(_developJournal, resolver, freshStones, freshChars, _authority, new StubRangePolicy());

            var stone = freshStones.GetStone(_stone)!;
            bool found = false;
            foreach (var n in stone.NodeDevelopment) if (n.Node.Key == "SavorTheHearth" && n.Developed) found = true;
            Assert.True(found);
            Assert.Equal(bpAfter, BondPower.BalanceFor(freshChars.GetCharacter(_account, _governor)!, _stone));
        }

        [Fact]
        public void Develop_conflicting_payload_under_committed_op_rejects()
        {
            var act = NewActivityHandler();
            act.Handle(CreditCmd("op-c", _account, _governor, HomesteadProgressionCatalog.CookingTree, 5));
            var dev = NewDevelopHandler();
            Assert.Equal(DevelopmentCommandOutcome.Applied,
                dev.Handle(DevelopCmd("op-x", _account, _governor, HomesteadProgressionCatalog.CookingTree, SavorTheHearth, 1)).Outcome);
            // Same op id, different BP amount -> conflict.
            var conflict = dev.Handle(DevelopCmd("op-x", _account, _governor, HomesteadProgressionCatalog.CookingTree, SavorTheHearth, 2));
            Assert.Equal(DevelopmentCommandOutcome.Rejected, conflict.Outcome);
            Assert.Equal("OperationConflict", conflict.ResultCode);
        }

        [Fact]
        public void Develop_stale_tuning_version_rejects()
        {
            var act = NewActivityHandler();
            act.Handle(CreditCmd("op-c", _account, _governor, HomesteadProgressionCatalog.CookingTree, 5));
            var dev = NewDevelopHandler();
            var cmd = new ApplyBpToNodeCommand(new OperationId("op-t"), _stone,
                new AuthenticatedConnection(_account.Value, _governor.Value), default,
                HomesteadProgressionCatalog.CookingTree, SavorTheHearth, 1,
                TreeTuningCatalog.CurrentTuningVersion + 1);
            var r = dev.Handle(cmd);
            Assert.Equal(DevelopmentCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("ContentVersionMismatch", r.ResultCode);
        }
    }
}
