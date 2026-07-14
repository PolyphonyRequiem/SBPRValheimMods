// ============================================================================
//  Homestead progression — Foundational AP RECOVERY tests (T002, Gate A).
// ----------------------------------------------------------------------------
//  Proves the durable-journal recovery contract on the SHIPPED, engine-free
//  OperationReceiptStore + ReceiptRecovery (link-compiled from ../src).
//
//  Named acceptance covered here:
//    AT-P0-REPLAY   retry / reconnect returns the recorded result with no
//                   duplicate state, including a SIMULATED restart after every
//                   one of the four durable boundaries.
//    (recovery report: RECOVERABLE vs QUARANTINE vs CLEAN classification.)
//
//  HONEST SCOPE — what "restart" means here vs. what it does NOT:
//  A crash injector THROWS in-process after the Nth durable boundary, then a
//  brand-new store is constructed over the SAME fsync'd journal path. Because the
//  journal is the only durable truth, that fresh store is behaviourally a
//  restarted process for the purpose of journal replay: re-submitting the same
//  operationId must converge to one terminal result. This is NOT a real OS
//  process kill — the exception unwinds a live process and file handles close
//  normally. Real child-PROCESS death at every durable write was already proven
//  by the T001 Gate-A spike (AT-P0-CRASH-EACH-WRITE, accepted); a real
//  child-process in-world reproduction is scoped to T003, not claimed by this
//  suite.
// ============================================================================

using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Recovery;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimProgressionRecoveryTests : System.IDisposable
    {
        private readonly string _journalPath;
        private readonly StoneId _stone = StoneId.FromHostZone(new WorldId("uid:9"), 3, 3);
        private readonly AuthoritativePrincipal _owner =
            new AuthoritativePrincipal(new AccountId("acct-1"), new CharacterId("char-1"), "plat-1");

        public NiflheimProgressionRecoveryTests()
        {
            _journalPath = Path.Combine(Path.GetTempPath(), "niflheim-t002-recovery-" + System.Guid.NewGuid().ToString("N") + ".journal");
        }

        public void Dispose()
        {
            if (File.Exists(_journalPath)) File.Delete(_journalPath);
        }

        // Throws after boundary == TargetBoundary to SIMULATE a mid-operation crash (in-process,
        // not a real OS kill — see the honest-scope note in the file header). Recovery is proven by
        // replaying the fsync'd journal in a fresh store.
        private sealed class CrashAfter : ICrashInjector
        {
            private readonly ReceiptBoundary _target;
            public CrashAfter(ReceiptBoundary target) { _target = target; }
            public void AfterBoundary(ReceiptBoundary boundary)
            {
                if (boundary == _target) throw new SimulatedProcessDeath();
            }
        }

        private sealed class SimulatedProcessDeath : System.Exception { }

        private OperationReceiptStore NewStore(out InMemoryMirroredStoneApStore stone, out InMemoryCharacterApStore character)
        {
            // Fresh in-memory aggregates + a fresh store over the SAME on-disk journal == a restarted
            // process for the purpose of journal replay (the journal is the only durable truth carried
            // across the simulated restart). Not a real OS process boundary — that is T001/T003 scope.
            stone = new InMemoryMirroredStoneApStore();
            character = new InMemoryCharacterApStore();
            return new OperationReceiptStore(_journalPath, stone, character);
        }

        [Theory]
        [InlineData(ReceiptBoundary.IntentJournaled)]
        [InlineData(ReceiptBoundary.StoneApplied)]
        [InlineData(ReceiptBoundary.CharacterApplied)]
        [InlineData(ReceiptBoundary.Committed)]
        public void AtP0Replay_SimulatedCrashAfterEveryBoundary_RecoversExactlyOneResult(ReceiptBoundary crashAt)
        {
            const string opId = "op-crash";

            // First "process": submit and simulate a crash right after boundary crashAt.
            var store1 = NewStore(out _, out _);
            Assert.Throws<SimulatedProcessDeath>(() =>
                store1.SubmitFoundationalAp(new OperationId(opId), _stone, _owner, "evi", new CrashAfter(crashAt)));

            // Second "process": fresh store over the same journal, re-submit the same operationId.
            var store2 = NewStore(out var stone2, out var character2);
            var result = store2.SubmitFoundationalAp(new OperationId(opId), _stone, _owner, "evi");

            Assert.Equal(1, result.PersonalAp);
            Assert.Equal(1, result.CumulativeAp);
            Assert.Equal(1, result.MirroredStoneAp);
            Assert.Equal(1, stone2.GetMirroredStoneAp(_stone));
            Assert.Equal(1, character2.GetPersonalAp(_owner.Account, _owner.Character, _stone));
            Assert.Equal(1, character2.GetCumulativeAp(_owner.Account, _owner.Character, _stone));

            // Third submit is a pure replay: still exactly one, identical receipt.
            var store3 = NewStore(out _, out _);
            var replay = store3.SubmitFoundationalAp(new OperationId(opId), _stone, _owner, "evi");
            Assert.Equal(ReceiptOutcome.Replayed, replay.Outcome);
            Assert.Equal(result.ReceiptId, replay.ReceiptId);
            Assert.Equal(1, replay.MirroredStoneAp);
        }

        [Fact]
        public void Recovery_TerminalOperation_ReportsRecoverableWithOneBalance()
        {
            var store = NewStore(out _, out _);
            store.SubmitFoundationalAp(new OperationId("op-ok"), _stone, _owner, "evi");

            var recovery = new ReceiptRecovery(store);
            var state = recovery.Inspect("op-ok");
            Assert.Equal(RecoveryStatus.Recoverable, state.Status);
            Assert.Equal(1, state.PersonalAp);
            Assert.Equal(1, state.CumulativeAp);
            Assert.Equal(1, state.MirroredStoneAp);
            Assert.Contains("RECOVERABLE", recovery.BuildReport("op-ok"));
        }

        [Fact]
        public void Recovery_PartialNonTerminalState_ReportsQuarantineNotGuessed()
        {
            // Die before the terminal record is durable -> partial state.
            var store1 = NewStore(out _, out _);
            Assert.Throws<SimulatedProcessDeath>(() =>
                store1.SubmitFoundationalAp(new OperationId("op-partial"), _stone, _owner, "evi",
                    new CrashAfter(ReceiptBoundary.StoneApplied)));

            var recovery = new ReceiptRecovery(NewStore(out _, out _));
            var state = recovery.Inspect("op-partial");
            Assert.Equal(RecoveryStatus.Quarantine, state.Status);
            var report = recovery.BuildReport("op-partial");
            Assert.Contains("QUARANTINE", report);
            Assert.Contains("not auto-guessed", report);
        }

        [Fact]
        public void Recovery_UnknownOperation_ReportsClean()
        {
            var recovery = new ReceiptRecovery(NewStore(out _, out _));
            Assert.Equal(RecoveryStatus.Clean, recovery.Inspect("never-began").Status);
        }

        [Fact]
        public void Recovery_TornTailFromPartialWrite_IsTruncatedNotAccepted()
        {
            var store = NewStore(out _, out _);
            store.SubmitFoundationalAp(new OperationId("op-clean"), _stone, _owner, "evi");

            // Append raw garbage bytes simulating a half-written frame from process death.
            using (var fs = new FileStream(_journalPath, FileMode.Append, FileAccess.Write))
            {
                var junk = new byte[] { 0x7F, 0x11, 0x22 }; // shorter than an 8-byte header
                fs.Write(junk, 0, junk.Length);
                fs.Flush();
            }

            var reopened = NewStore(out _, out _);
            var durable = reopened.ReadDurable(out long tornTail);
            Assert.True(tornTail > 0);
            // The one good operation still recovers cleanly; the torn tail is ignored, not "repaired".
            var recovery = new ReceiptRecovery(reopened);
            Assert.Equal(RecoveryStatus.Recoverable, recovery.Inspect("op-clean").Status);
            Assert.Equal(1, recovery.Inspect("op-clean").MirroredStoneAp);
        }
    }
}
