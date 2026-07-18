// ============================================================================
//  Homestead progression — fresh-process REHYDRATION tests (T002 Gate-A
//  remediation, card t_e1073dd8).
// ----------------------------------------------------------------------------
//  Pins the corrective contract that a brand-new OperationReceiptStore booted
//  over an existing durable journal (a fresh server process) rebuilds the Stone
//  and character projection balances AND their optimistic-concurrency revisions
//  from durable journal truth at CONSTRUCTION — before any new operation is
//  submitted. Without this, two separate processes each see revision 0 and can
//  both commit distinct operations against expected revision 0 (the verified
//  Gate-A defect), and the authoritative read state reports 0 AP while the
//  durable journal truth is non-zero.
//
//  Scope: exercises the SHIPPED, engine-free OperationReceiptStore + the
//  in-memory Stone/character sinks link-compiled from ../src, exactly like the
//  recovery/contract suites. "Fresh process" here means a fresh store + fresh
//  in-memory sinks over the SAME fsync'd journal path (the journal is the only
//  durable truth carried across the restart) — the same honest-scope convention
//  documented in NiflheimProgressionRecoveryTests.
// ============================================================================

using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimProgressionRehydrationTests : System.IDisposable
    {
        private readonly string _journalPath;
        private readonly StoneId _stone = StoneId.FromHostZone(new WorldId("uid:rehydrate"), 5, -7);
        private readonly AuthoritativePrincipal _owner =
            new AuthoritativePrincipal(new AccountId("acct-r"), new CharacterId("char-r"));

        public NiflheimProgressionRehydrationTests()
        {
            _journalPath = Path.Combine(Path.GetTempPath(),
                "niflheim-t002-rehydrate-" + System.Guid.NewGuid().ToString("N") + ".journal");
        }

        public void Dispose()
        {
            if (File.Exists(_journalPath)) File.Delete(_journalPath);
        }

        private OperationReceiptStore NewStore(out InMemoryMirroredStoneApStore stone, out InMemoryCharacterApStore character)
        {
            stone = new InMemoryMirroredStoneApStore();
            character = new InMemoryCharacterApStore();
            return new OperationReceiptStore(_journalPath, stone, character);
        }

        [Fact]
        public void FreshProcess_BootedBalancesAndRevisions_EqualDurableJournalTruth()
        {
            // First process: commit one operation, advancing every aggregate to 1.
            var store1 = NewStore(out _, out _);
            store1.SubmitFoundationalAp(new OperationId("op-boot"), _stone, _owner, "evi");

            // Second process: a fresh store over the same journal with EMPTY in-memory sinks. The
            // sinks must be rehydrated from durable journal truth at construction — before any submit.
            NewStore(out var stone2, out var character2);

            Assert.Equal(1, stone2.GetMirroredStoneAp(_stone));
            Assert.Equal(1, stone2.GetStoneRevision(_stone));
            Assert.Equal(1, character2.GetPersonalAp(_owner.Account, _owner.Character, _stone));
            Assert.Equal(1, character2.GetCumulativeAp(_owner.Account, _owner.Character, _stone));
            Assert.Equal(1, character2.GetCharacterRevision(_owner.Account, _owner.Character, _stone));
        }

        [Fact]
        public void FreshProcess_MultipleCommittedOps_BootedRevisionEqualsCommittedCount()
        {
            var store1 = NewStore(out _, out _);
            store1.SubmitFoundationalAp(new OperationId("op-a"), _stone, _owner, "evi-a");
            store1.SubmitFoundationalAp(new OperationId("op-b"), _stone, _owner, "evi-b");
            store1.SubmitFoundationalAp(new OperationId("op-c"), _stone, _owner, "evi-c");

            NewStore(out var stone2, out var character2);

            Assert.Equal(3, stone2.GetMirroredStoneAp(_stone));
            Assert.Equal(3, stone2.GetStoneRevision(_stone));
            Assert.Equal(3, character2.GetCharacterRevision(_owner.Account, _owner.Character, _stone));
        }

        [Fact]
        public void PostRestart_StaleExpectedStoneRevision_RejectsBeforeAnyDurableWrite()
        {
            // First process commits: Stone revision 0 -> 1 durable.
            var store1 = NewStore(out _, out _);
            store1.SubmitFoundationalAp(new OperationId("op-first"), _stone, _owner, "evi",
                expectedStoneRevision: 0, expectedCharacterRevision: 0);

            // Second process: fresh store over the same journal. A losing client that still expects
            // the pre-restart Stone revision 0 must be rejected by CAS BEFORE any journal write.
            var store2 = NewStore(out var stone2, out var character2);
            var stale = store2.SubmitFoundationalAp(new OperationId("op-stale"), _stone, _owner, "evi-stale",
                expectedStoneRevision: 0, expectedCharacterRevision: null);

            Assert.Equal(ReceiptOutcome.StaleStoneRevision, stale.Outcome);
            Assert.Equal(1, stale.StoneRevision); // reports current revision to refetch
            // Zero mutation: no durable record for the losing op, balances unchanged.
            Assert.DoesNotContain("op-stale", store2.DurableOperationIds());
            Assert.Equal(1, stone2.GetMirroredStoneAp(_stone));
            Assert.Equal(1, stone2.GetStoneRevision(_stone));
        }

        [Fact]
        public void PostRestart_StaleExpectedCharacterRevision_RejectsBeforeAnyDurableWrite()
        {
            var store1 = NewStore(out _, out _);
            store1.SubmitFoundationalAp(new OperationId("op-first"), _stone, _owner, "evi",
                expectedStoneRevision: 0, expectedCharacterRevision: 0);

            var store2 = NewStore(out _, out var character2);
            var stale = store2.SubmitFoundationalAp(new OperationId("op-stale-char"), _stone, _owner, "evi-stale",
                expectedStoneRevision: null, expectedCharacterRevision: 0);

            Assert.Equal(ReceiptOutcome.StaleCharacterRevision, stale.Outcome);
            Assert.Equal(1, stale.CharacterRevision);
            Assert.DoesNotContain("op-stale-char", store2.DurableOperationIds());
            Assert.Equal(1, character2.GetCharacterRevision(_owner.Account, _owner.Character, _stone));
        }

        [Fact]
        public void PostRestart_WellBehavedClientRefetchedRevision_CommitsAcrossRestart()
        {
            var store1 = NewStore(out _, out _);
            store1.SubmitFoundationalAp(new OperationId("op-first"), _stone, _owner, "evi",
                expectedStoneRevision: 0, expectedCharacterRevision: 0);

            // Fresh process. A well-behaved client refetched the booted revision 1 and commits against it.
            var store2 = NewStore(out var stone2, out var character2);
            var ok = store2.SubmitFoundationalAp(new OperationId("op-second"), _stone, _owner, "evi-2",
                expectedStoneRevision: 1, expectedCharacterRevision: 1);

            Assert.Equal(ReceiptOutcome.Applied, ok.Outcome);
            Assert.Equal(2, ok.StoneRevision);
            Assert.Equal(2, ok.CharacterRevision);
            Assert.Equal(2, stone2.GetMirroredStoneAp(_stone));
            Assert.Equal(2, character2.GetCumulativeAp(_owner.Account, _owner.Character, _stone));
        }

        [Fact]
        public void PostRestart_PartialNonTerminalOp_DoesNotCountTowardBootedRevision()
        {
            // A committed op (rev 1) plus a partial, NON-terminal op must boot to revision 1: the
            // quarantined partial is not a committed mutation and must not inflate the CAS token.
            var store1 = NewStore(out _, out _);
            store1.SubmitFoundationalAp(new OperationId("op-committed"), _stone, _owner, "evi");
            Assert.Throws<SimulatedDeath>(() =>
                store1.SubmitFoundationalAp(new OperationId("op-partial"), _stone, _owner, "evi-p",
                    crash: new CrashAfter(ReceiptBoundary.StoneApplied)));

            NewStore(out var stone2, out _);
            Assert.Equal(1, stone2.GetStoneRevision(_stone));
            Assert.Equal(1, stone2.GetMirroredStoneAp(_stone));
        }

        private sealed class SimulatedDeath : System.Exception { }

        private sealed class CrashAfter : ICrashInjector
        {
            private readonly ReceiptBoundary _target;
            public CrashAfter(ReceiptBoundary target) { _target = target; }
            public void AfterBoundary(ReceiptBoundary boundary)
            {
                if (boundary == _target) throw new SimulatedDeath();
            }
        }
    }
}
