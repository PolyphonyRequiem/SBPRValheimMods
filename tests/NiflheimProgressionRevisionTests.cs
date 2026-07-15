// ============================================================================
//  Homestead progression — REVISIONED-COMMAND / optimistic-concurrency tests
//  (T002, Gate A).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free CLEAN-side slice (link-compiled from
//  ../src). Pins the accepted contract that every Foundational-placement mutation
//  carries expected Stone/character revisions (contracts.md common command
//  envelope lines 16, 34-38) and that a losing concurrent client is rejected by
//  compare-and-set BEFORE any durable write, with zero mutation.
//
//  Named acceptance touched here:
//    AT-P0-AP-ATOMIC   an applied operation reports the committed Stone and
//                      character revisions it produced.
//    Contract-test minimum item 3: "two-client race on the same expected
//                      revision" — the second loses with StaleStoneRevision /
//                      StaleCharacterRevision and mutates nothing.
//
//  Scope note: this is single-process compare-and-set over the server-owned
//  aggregates; a true two-CLIENT in-world revision race under real process death
//  is T003's Gate-A verification, not claimed here.
// ============================================================================

using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Activities;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimProgressionRevisionTests : System.IDisposable
    {
        private readonly string _journalPath;
        private readonly WorldId _world = new WorldId("uid:555");
        private readonly StoneId _stone;
        private readonly AccountId _ownerAccount = new AccountId("plat-owner");
        private readonly CharacterId _ownerCharacter = new CharacterId("char-owner");

        private readonly OperationReceiptStore _receipts;
        private readonly InMemoryMirroredStoneApStore _stoneStore;
        private readonly InMemoryCharacterApStore _characterStore;
        private readonly ProgressionCommandPipeline _pipeline;

        public NiflheimProgressionRevisionTests()
        {
            _journalPath = Path.Combine(Path.GetTempPath(), "niflheim-t002-revision-" + System.Guid.NewGuid().ToString("N") + ".journal");
            _stone = StoneId.FromHostZone(_world, 8, 8);
            _stoneStore = new InMemoryMirroredStoneApStore();
            _characterStore = new InMemoryCharacterApStore();
            _receipts = new OperationReceiptStore(_journalPath, _stoneStore, _characterStore);

            var resolver = new PrincipalResolver(platform => platform);
            var authorizer = new PreconfiguredTestAuthorizer().Allow(_ownerAccount, _stone);
            _pipeline = new ProgressionCommandPipeline(resolver, _receipts, authorizer);
        }

        public void Dispose()
        {
            if (File.Exists(_journalPath)) File.Delete(_journalPath);
        }

        private AuthenticatedConnection OwnerConnection() => new AuthenticatedConnection("plat-owner", "char-owner");

        private FoundationalPlacementCommand Command(
            string operationId, string evidence = "foundation_wood_floor",
            long? expectedStoneRevision = null, long? expectedCharacterRevision = null)
        {
            var adapter = new FoundationalPlacementAdapter();
            var facts = new FoundationalPlacementEvidence(
                new OperationId(operationId), _stone,
                stablePieceId: evidence, pieceInstanceProvenance: "prov-" + evidence,
                insideStoneArea: true, placementSucceeded: true, foundationalCatalogVersion: "v1");
            var admission = adapter.Admit(facts, OwnerConnection(), new ClaimedPrincipal("plat-owner", "char-owner"),
                expectedStoneRevision, expectedCharacterRevision);
            Assert.True(admission.IsAdmitted);
            return admission.Command;
        }

        [Fact]
        public void AppliedOperation_ReportsCommittedStoneAndCharacterRevisions()
        {
            // Caller observed the empty Stone/character (revision 0) and expects to commit against it.
            var result = _pipeline.Handle(Command("op-rev-1", expectedStoneRevision: 0, expectedCharacterRevision: 0));

            Assert.Equal(CommandOutcome.Applied, result.Outcome);
            Assert.Equal(1, result.StoneRevision);
            Assert.Equal(1, result.CharacterRevision);
            Assert.Equal(1, _stoneStore.GetStoneRevision(_stone));
            Assert.Equal(1, _characterStore.GetCharacterRevision(_ownerAccount, _ownerCharacter, _stone));
        }

        [Fact]
        public void TwoClientRace_OnSameExpectedStoneRevision_LoserRejectsStaleWithoutMutation()
        {
            // Both clients observed revision 0. First commits and advances the Stone revision to 1.
            var first = _pipeline.Handle(Command("op-winner", "foundation_wood_wall", expectedStoneRevision: 0, expectedCharacterRevision: 0));
            Assert.Equal(CommandOutcome.Applied, first.Outcome);
            Assert.Equal(1, first.StoneRevision);

            // Second client is a DIFFERENT operation still expecting revision 0 -> it lost the race.
            var loser = _pipeline.Handle(Command("op-loser", "foundation_wood_pole", expectedStoneRevision: 0, expectedCharacterRevision: 0));

            Assert.Equal(CommandOutcome.Rejected, loser.Outcome);
            Assert.Equal("StaleStoneRevision", loser.ResultCode);
            // The rejection reports the current revision the caller must refetch.
            Assert.Equal(1, loser.StoneRevision);

            // Zero mutation from the losing command: balances/revisions reflect only the winner, and
            // no durable journal record was written for the loser.
            Assert.Equal(1, _stoneStore.GetMirroredStoneAp(_stone));
            Assert.Equal(1, _stoneStore.GetStoneRevision(_stone));
            Assert.DoesNotContain("op-loser", _receipts.DurableOperationIds());
        }

        [Fact]
        public void StaleCharacterRevision_RejectsWithoutMutation()
        {
            var first = _pipeline.Handle(Command("op-c1", "foundation_wood_wall", expectedStoneRevision: 0, expectedCharacterRevision: 0));
            Assert.Equal(CommandOutcome.Applied, first.Outcome);
            Assert.Equal(1, first.CharacterRevision);

            // Stale character revision (still expects 0) while the Stone revision is not asserted.
            var loser = _pipeline.Handle(Command("op-c2", "foundation_wood_pole", expectedStoneRevision: null, expectedCharacterRevision: 0));

            Assert.Equal(CommandOutcome.Rejected, loser.Outcome);
            Assert.Equal("StaleCharacterRevision", loser.ResultCode);
            Assert.Equal(1, loser.CharacterRevision);
            Assert.Equal(1, _characterStore.GetPersonalAp(_ownerAccount, _ownerCharacter, _stone));
            Assert.DoesNotContain("op-c2", _receipts.DurableOperationIds());
        }

        [Fact]
        public void FreshExpectedRevision_AdvancesAndPermitsNextCommit()
        {
            _pipeline.Handle(Command("op-a", "foundation_wood_beam", expectedStoneRevision: 0, expectedCharacterRevision: 0));
            // A well-behaved second client refetched revision 1 and commits against it.
            var second = _pipeline.Handle(Command("op-b", "foundation_wood_roof", expectedStoneRevision: 1, expectedCharacterRevision: 1));

            Assert.Equal(CommandOutcome.Applied, second.Outcome);
            Assert.Equal(2, second.StoneRevision);
            Assert.Equal(2, second.CharacterRevision);
            Assert.Equal(2, _stoneStore.GetMirroredStoneAp(_stone));
        }

        [Fact]
        public void NullExpectedRevisions_CompareAny_StillCommits()
        {
            // Backwards-compatible compare-any path: no expectation supplied still commits (the
            // transport supplies the expectation for real client commands).
            var result = _pipeline.Handle(Command("op-any"));
            Assert.Equal(CommandOutcome.Applied, result.Outcome);
            Assert.Equal(1, result.StoneRevision);
        }

        [Fact]
        public void Replay_WithNowStaleExpectedRevision_StillReturnsRecordedResult_NotStale()
        {
            // The original committed at revision 0->1. A retry/reconnect of the SAME operation still
            // carrying expectedStoneRevision:0 must return the recorded result (Replayed), because the
            // op already committed exactly once — it is not a fresh losing race.
            var first = _pipeline.Handle(Command("op-replay", "foundation_wood_floor", expectedStoneRevision: 0, expectedCharacterRevision: 0));
            var replay = _pipeline.Handle(Command("op-replay", "foundation_wood_floor", expectedStoneRevision: 0, expectedCharacterRevision: 0));

            Assert.Equal(CommandOutcome.Applied, first.Outcome);
            Assert.Equal(CommandOutcome.Replayed, replay.Outcome);
            Assert.Equal(first.ReceiptId, replay.ReceiptId);
            Assert.Equal(1, _stoneStore.GetMirroredStoneAp(_stone));
        }
    }
}
