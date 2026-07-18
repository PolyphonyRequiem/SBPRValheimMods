// ============================================================================
//  RD-T006 (Gate B) — Server inventory transaction spike tests.
// ----------------------------------------------------------------------------
//  Exercises StockTransactionHarness (link-compiled from ../src). Gate B proves
//  the load-bearing player-inventory <-> Stone-Stock transaction seam that
//  donations (Tracer 3) and withdrawals (Tracer 5) later depend on, WITHOUT
//  implementing any Resource Delivery gameplay. Plan Gate B exit:
//
//    "The same operation converges to ONE transfer or NO transfer with no
//     duplicated or lost player or Stone items."
//
//  Coverage:
//    * server resolves the EXACT authored donation vector (no trusted client qty);
//    * full debit/credit FIT: insufficient source items, over-capacity deposit,
//      and a player inventory that cannot accept the whole withdrawn vector all
//      reject with NO mutation;
//    * STALE revisions on either ledger reject pre-write;
//    * REPLAY: same op id + binding returns the one recorded result; a conflicting
//      binding under the same op id is OperationConflict;
//    * DISCONNECT / PROCESS DEATH: a crash at every debit/credit/commit boundary
//      converges to exactly one transfer or none, with both ledgers consistent.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class ResourceDeliveryGateBTests : IDisposable
    {
        private readonly string _journal;

        public ResourceDeliveryGateBTests()
        {
            _journal = Path.Combine(Path.GetTempPath(), "rd-stock-" + Guid.NewGuid().ToString("N") + ".jrnl");
        }

        public void Dispose()
        {
            if (File.Exists(_journal)) File.Delete(_journal);
        }

        // ── Fixture ──────────────────────────────────────────────────────────
        // Level-2 Humble authored options (contracts §SelectDonationMenu Level-2 pool).
        private static ItemVector V(params (string item, long qty)[] pairs)
        {
            var d = new Dictionary<string, long>();
            foreach (var p in pairs) d[p.item] = p.qty;
            return new ItemVector(d);
        }

        private static Dictionary<string, ItemVector> AuthoredOptions() => new Dictionary<string, ItemVector>
        {
            { "opt-20wood", V(("Wood", 20)) },
            { "opt-20stone", V(("Stone", 20)) },
            { "opt-10wood10stone", V(("Wood", 10), ("Stone", 10)) },
        };

        private StockTransactionHarness Harness(
            Dictionary<string, long>? inventory = null,
            Dictionary<string, long>? stock = null,
            CapacityPolicy? stockPolicy = null) =>
            new StockTransactionHarness(
                _journal,
                stockPolicy ?? CapacityPolicy.Level2Stock,
                CapacityPolicy.PlayerCarry,
                inventory ?? new Dictionary<string, long> { { "Wood", 50 }, { "Stone", 50 } },
                stock ?? new Dictionary<string, long>(),
                AuthoredOptions());

        /// <summary>A crash injector that throws right after the chosen durable boundary.</summary>
        private sealed class CrashAfter : IStockCrashInjector
        {
            private readonly StockTransferBoundary _at;
            public CrashAfter(StockTransferBoundary at) => _at = at;
            public void AfterBoundary(StockTransferBoundary boundary)
            {
                if (boundary == _at) throw new InvalidOperationException("injected process death after " + boundary);
            }
        }

        // ── Authored-vector resolution ─────────────────────────────────────────

        [Fact]
        public void Donation_ServerResolvesExactAuthoredVector_ClientSuppliesNoQuantity()
        {
            var h = Harness();
            var r = h.SubmitDonation("op-1", "opt-20wood", 0, 0);
            Assert.Equal(StockTransferOutcome.Applied, r.Outcome);
            Assert.Equal(30, h.CurrentInventory()["Wood"]); // 50 − 20
            Assert.Equal(20, h.CurrentStock()["Wood"]);     // 0 + 20 (the authored amount, not a client claim)
            Assert.Equal(50, h.CurrentInventory()["Stone"]); // untouched
        }

        [Fact]
        public void Donation_UnknownOption_Rejected_NoMutation()
        {
            var h = Harness();
            var r = h.SubmitDonation("op-1", "opt-nonexistent", 0, 0);
            Assert.Equal(StockTransferOutcome.OptionNotAccepted, r.Outcome);
            Assert.Equal("DonationOptionNotAccepted", r.ResultCode);
            Assert.Equal(50, h.CurrentInventory()["Wood"]);
            Assert.Equal(0, h.StockRevision());
        }

        // ── Debit/credit fit ───────────────────────────────────────────────────

        [Fact]
        public void Donation_InsufficientPlayerItems_Rejected_NoMutation()
        {
            var h = Harness(inventory: new Dictionary<string, long> { { "Wood", 5 } });
            var r = h.SubmitDonation("op-1", "opt-20wood", 0, 0);
            Assert.Equal(StockTransferOutcome.SourceItemsMissing, r.Outcome);
            Assert.Equal("DonationItemsMissing", r.ResultCode);
            Assert.Equal(5, h.CurrentInventory()["Wood"]);      // unchanged
            Assert.False(h.CurrentStock().ContainsKey("Wood")); // nothing deposited
        }

        [Fact]
        public void Donation_ExceedsStockCapacity_Rejected_NoMutation()
        {
            // Stock already holds 495 Wood; per-item cap is 500, so +20 (→515) cannot fit.
            var h = Harness(
                inventory: new Dictionary<string, long> { { "Wood", 50 } },
                stock: new Dictionary<string, long> { { "Wood", 495 } });
            var r = h.SubmitDonation("op-1", "opt-20wood", 0, 0);
            Assert.Equal(StockTransferOutcome.DestinationCannotFit, r.Outcome);
            Assert.Equal("StoneStockCapacityExceeded", r.ResultCode);
            Assert.Equal(50, h.CurrentInventory()["Wood"]);  // debit rolled back (never happened)
            Assert.Equal(495, h.CurrentStock()["Wood"]);     // deposit never happened
        }

        [Fact]
        public void Withdrawal_MovesStockToPlayer_Once()
        {
            var h = Harness(
                inventory: new Dictionary<string, long> { { "Wood", 10 } },
                stock: new Dictionary<string, long> { { "Wood", 40 }, { "Stone", 40 } });
            var r = h.WithdrawStock("op-1", V(("Wood", 15), ("Stone", 5)), 0, 0);
            Assert.Equal(StockTransferOutcome.Applied, r.Outcome);
            Assert.Equal(25, h.CurrentInventory()["Wood"]); // 10 + 15
            Assert.Equal(5, h.CurrentInventory()["Stone"]);
            Assert.Equal(25, h.CurrentStock()["Wood"]);     // 40 − 15
            Assert.Equal(35, h.CurrentStock()["Stone"]);    // 40 − 5
        }

        [Fact]
        public void Withdrawal_StockLacksFullVector_Rejected_NoMutation()
        {
            var h = Harness(
                inventory: new Dictionary<string, long> { { "Wood", 10 } },
                stock: new Dictionary<string, long> { { "Wood", 5 } });
            var r = h.WithdrawStock("op-1", V(("Wood", 15)), 0, 0);
            Assert.Equal(StockTransferOutcome.SourceItemsMissing, r.Outcome);
            Assert.Equal("StockQuantityUnavailable", r.ResultCode);
            Assert.Equal(10, h.CurrentInventory()["Wood"]);
            Assert.Equal(5, h.CurrentStock()["Wood"]);
        }

        [Fact]
        public void Withdrawal_PlayerInventoryCannotFit_Rejected_NoMutation()
        {
            // A tight player policy: max 100 units. Player holds 95, withdrawing 15 (→110) cannot fit.
            var tightPlayer = new CapacityPolicy(64, 100, 100);
            var h = new StockTransactionHarness(_journal, CapacityPolicy.Level2Stock, tightPlayer,
                new Dictionary<string, long> { { "Wood", 95 } },
                new Dictionary<string, long> { { "Wood", 40 } },
                AuthoredOptions());
            var r = h.WithdrawStock("op-1", V(("Wood", 15)), 0, 0);
            Assert.Equal(StockTransferOutcome.DestinationCannotFit, r.Outcome);
            Assert.Equal("PlayerInventoryCannotFit", r.ResultCode);
            Assert.Equal(95, h.CurrentInventory()["Wood"]);
            Assert.Equal(40, h.CurrentStock()["Wood"]);
        }

        // ── Stale revisions ────────────────────────────────────────────────────

        [Fact]
        public void Donation_StaleInventoryRevision_Rejected_NoMutation()
        {
            var h = Harness();
            h.SubmitDonation("op-1", "opt-20wood", 0, 0); // advances both revisions to 1
            // A second op still expecting revision 0 is stale.
            var r = h.SubmitDonation("op-2", "opt-20stone", 0, 1);
            Assert.Equal(StockTransferOutcome.StaleInventoryRevision, r.Outcome);
            Assert.Equal(1, r.InventoryRevision); // tells the caller the current revision
            Assert.Equal(50, h.CurrentInventory()["Stone"]); // second op mutated nothing
        }

        [Fact]
        public void Donation_StaleStockRevision_Rejected_NoMutation()
        {
            var h = Harness();
            h.SubmitDonation("op-1", "opt-20wood", 0, 0);
            var r = h.SubmitDonation("op-2", "opt-20stone", 1, 0); // inv ok, stock stale
            Assert.Equal(StockTransferOutcome.StaleStockRevision, r.Outcome);
            Assert.Equal(1, r.StockRevision);
        }

        [Fact]
        public void SecondOp_WithCurrentRevisions_Applies()
        {
            var h = Harness();
            h.SubmitDonation("op-1", "opt-20wood", 0, 0);
            var r = h.SubmitDonation("op-2", "opt-20stone", 1, 1);
            Assert.Equal(StockTransferOutcome.Applied, r.Outcome);
            Assert.Equal(20, h.CurrentStock()["Stone"]);
            Assert.Equal(20, h.CurrentStock()["Wood"]);
        }

        // ── Replay / conflict ──────────────────────────────────────────────────

        [Fact]
        public void Replay_SameOpAndBinding_ReturnsRecordedResult_NoDoubleTransfer()
        {
            var h = Harness();
            var first = h.SubmitDonation("op-1", "opt-20wood", 0, 0);
            Assert.Equal(StockTransferOutcome.Applied, first.Outcome);
            // Same op id + same binding, even with a now-stale expected revision, replays the winner.
            var replay = h.SubmitDonation("op-1", "opt-20wood", 0, 0);
            Assert.Equal(StockTransferOutcome.Replayed, replay.Outcome);
            Assert.Equal(first.ReceiptId, replay.ReceiptId);
            Assert.Equal(30, h.CurrentInventory()["Wood"]); // transferred exactly once
            Assert.Equal(20, h.CurrentStock()["Wood"]);
        }

        [Fact]
        public void Replay_AcrossRestart_ReturnsRecordedResult()
        {
            Harness().SubmitDonation("op-1", "opt-20wood", 0, 0);
            // "Restart": a fresh harness over the same journal and same opening balances.
            var replay = Harness().SubmitDonation("op-1", "opt-20wood", 0, 0);
            Assert.Equal(StockTransferOutcome.Replayed, replay.Outcome);
            Assert.Equal(30, Harness().CurrentInventory()["Wood"]);
            Assert.Equal(20, Harness().CurrentStock()["Wood"]);
        }

        [Fact]
        public void ConflictingBinding_UnderSameOpId_Rejected()
        {
            var h = Harness();
            h.SubmitDonation("op-1", "opt-20wood", 0, 0);
            // Same op id, different authored option (different binding) → conflict, no second transfer.
            var conflict = h.SubmitDonation("op-1", "opt-20stone", 0, 0);
            Assert.Equal(StockTransferOutcome.OperationConflict, conflict.Outcome);
            Assert.False(h.CurrentStock().ContainsKey("Stone"));
        }

        // ── Process death / disconnect: converge to one transfer or none ────────

        [Theory]
        [InlineData(StockTransferBoundary.IntentJournaled)]
        [InlineData(StockTransferBoundary.SourceDebited)]
        [InlineData(StockTransferBoundary.DestinationCredited)]
        public void CrashBeforeCommit_LeavesNoTransfer_ResumeCompletesExactlyOnce(StockTransferBoundary crashAt)
        {
            // First attempt crashes after `crashAt` (before the terminal record).
            Assert.Throws<InvalidOperationException>(() =>
                Harness().SubmitDonation("op-1", "opt-20wood", 0, 0, new CrashAfter(crashAt)));

            // After the crash, BEFORE resume: no terminal record → NO transfer is observable.
            var afterCrash = Harness();
            Assert.Equal(50, afterCrash.CurrentInventory()["Wood"]);
            Assert.False(afterCrash.CurrentStock().ContainsKey("Wood"));
            Assert.Equal(0, afterCrash.InventoryRevision());

            // Resume on a fresh process: the same op drives forward from its partial state and commits.
            var resumed = Harness().SubmitDonation("op-1", "opt-20wood", 0, 0);
            Assert.Equal(StockTransferOutcome.Applied, resumed.Outcome);

            var final = Harness();
            Assert.Equal(30, final.CurrentInventory()["Wood"]); // exactly one debit
            Assert.Equal(20, final.CurrentStock()["Wood"]);     // exactly one credit
            Assert.Equal(1, final.StockRevision());             // exactly one committed mutation
        }

        [Fact]
        public void CrashAfterCommit_RecoversTransfer_ReplayReturnsWinner()
        {
            // Crash injected AFTER the terminal record: the transfer is durable and recoverable.
            Assert.Throws<InvalidOperationException>(() =>
                Harness().SubmitDonation("op-1", "opt-20wood", 0, 0, new CrashAfter(StockTransferBoundary.Committed)));

            var recovered = Harness();
            Assert.Equal(30, recovered.CurrentInventory()["Wood"]); // transfer survived
            Assert.Equal(20, recovered.CurrentStock()["Wood"]);
            Assert.Equal(1, recovered.StockRevision());

            // A same-op retry after recovery replays the winner rather than transferring again.
            var replay = Harness().SubmitDonation("op-1", "opt-20wood", 0, 0);
            Assert.Equal(StockTransferOutcome.Replayed, replay.Outcome);
            Assert.Equal(30, Harness().CurrentInventory()["Wood"]);
        }

        [Fact]
        public void Withdrawal_CrashBeforeCommit_LeavesNoTransfer_ResumeCompletesOnce()
        {
            Dictionary<string, long> Inv() => new Dictionary<string, long> { { "Wood", 10 } };
            Dictionary<string, long> Stk() => new Dictionary<string, long> { { "Wood", 40 } };
            StockTransactionHarness H() => new StockTransactionHarness(_journal, CapacityPolicy.Level2Stock,
                CapacityPolicy.PlayerCarry, Inv(), Stk(), AuthoredOptions());

            Assert.Throws<InvalidOperationException>(() =>
                H().WithdrawStock("op-1", V(("Wood", 15)), 0, 0, new CrashAfter(StockTransferBoundary.SourceDebited)));

            var afterCrash = H();
            Assert.Equal(10, afterCrash.CurrentInventory()["Wood"]); // no credit yet
            Assert.Equal(40, afterCrash.CurrentStock()["Wood"]);     // no debit observable (no terminal)

            var resumed = H().WithdrawStock("op-1", V(("Wood", 15)), 0, 0);
            Assert.Equal(StockTransferOutcome.Applied, resumed.Outcome);

            var final = H();
            Assert.Equal(25, final.CurrentInventory()["Wood"]);
            Assert.Equal(25, final.CurrentStock()["Wood"]);
            Assert.Equal(1, final.StockRevision());
        }

        // ── Round-trip conservation: nothing is created or destroyed ────────────

        [Fact]
        public void DonateThenWithdraw_ConservesTotalUnits()
        {
            var h = Harness(
                inventory: new Dictionary<string, long> { { "Wood", 50 } },
                stock: new Dictionary<string, long>());
            long TotalWood() => h.CurrentInventory().GetValueOrDefault("Wood") + h.CurrentStock().GetValueOrDefault("Wood");
            Assert.Equal(50, TotalWood());

            h.SubmitDonation("op-1", "opt-20wood", 0, 0);
            Assert.Equal(50, TotalWood()); // conserved: 30 in inventory + 20 in Stock

            h.WithdrawStock("op-2", V(("Wood", 20)), 1, 1);
            Assert.Equal(50, TotalWood()); // conserved: back to 50 inventory + 0 Stock
            Assert.Equal(50, h.CurrentInventory()["Wood"]);
            Assert.False(h.CurrentStock().ContainsKey("Wood"));
        }

        // ── Partial-resume interleaving: an intervening commit serializes competing transfers ───
        //
        // REGRESSION (RD Gate B partial-resume interleaving overdraw). Operation A journals intent
        // against opening revision 0 and CRASHES before committing. Operation B then commits against
        // that same opening revision 0, consuming source items and advancing the revision. When A
        // RESUMES it must revalidate against CURRENT state bound to its intent-time revision — an
        // intervening commit makes A stale, so A rejects with NO mutation. Before the fix, A skipped
        // validation, re-applied its stale journaled debit, and projected negative (overdrawn) items.

        [Fact]
        public void PartialResume_AfterInterveningCommit_RejectsStale_NoOverdraw()
        {
            // Player holds exactly 20 Wood: enough for ONE 20-wood donation, not two.
            Dictionary<string, long> Inv() => new Dictionary<string, long> { { "Wood", 20 } };
            StockTransactionHarness H() => new StockTransactionHarness(_journal, CapacityPolicy.Level2Stock,
                CapacityPolicy.PlayerCarry, Inv(), new Dictionary<string, long>(), AuthoredOptions());

            // Operation A journals intent against revision 0, then crashes before committing.
            Assert.Throws<InvalidOperationException>(() =>
                H().SubmitDonation("op-A", "opt-20wood", 0, 0, new CrashAfter(StockTransferBoundary.IntentJournaled)));

            // No terminal record yet → A is invisible; both ledgers still at opening.
            Assert.Equal(20, H().CurrentInventory()["Wood"]);
            Assert.Equal(0, H().StockRevision());

            // Operation B commits against the SAME opening revision 0, consuming all 20 Wood.
            var b = H().SubmitDonation("op-B", "opt-20wood", 0, 0);
            Assert.Equal(StockTransferOutcome.Applied, b.Outcome);
            Assert.Equal(0, H().CurrentInventory().GetValueOrDefault("Wood")); // player drained
            Assert.Equal(20, H().CurrentStock()["Wood"]);
            Assert.Equal(1, H().StockRevision());

            // A RESUMES. It was version-bound to revision 0; B advanced to 1 → A is stale and rejects.
            var resumeA = H().SubmitDonation("op-A", "opt-20wood", 0, 0);
            Assert.Equal(StockTransferOutcome.StaleInventoryRevision, resumeA.Outcome);
            Assert.Equal(1, resumeA.InventoryRevision); // caller told the current revision to refetch

            // No overdraw: exactly one transfer survived, no negative projection.
            var final = H();
            Assert.Equal(0, final.CurrentInventory().GetValueOrDefault("Wood"));
            Assert.Equal(20, final.CurrentStock()["Wood"]);
            Assert.Equal(1, final.StockRevision());
            Assert.All(final.CurrentInventory().Values, v => Assert.True(v >= 0));
            Assert.All(final.CurrentStock().Values, v => Assert.True(v >= 0));
        }

        [Fact]
        public void PartialResume_Withdrawal_AfterInterveningCommit_RejectsStale_NoOverdraw()
        {
            // Stock holds exactly 20 Wood: enough for ONE 20-wood withdrawal, not two.
            Dictionary<string, long> Stk() => new Dictionary<string, long> { { "Wood", 20 } };
            StockTransactionHarness H() => new StockTransactionHarness(_journal, CapacityPolicy.Level2Stock,
                CapacityPolicy.PlayerCarry, new Dictionary<string, long>(), Stk(), AuthoredOptions());

            // Withdrawal A journals intent against revision 0, then crashes before committing.
            Assert.Throws<InvalidOperationException>(() =>
                H().WithdrawStock("op-A", V(("Wood", 20)), 0, 0, new CrashAfter(StockTransferBoundary.IntentJournaled)));

            Assert.Equal(20, H().CurrentStock()["Wood"]); // no terminal → nothing moved
            Assert.Equal(0, H().StockRevision());

            // Withdrawal B commits against the same opening revision 0, draining the Stock.
            var b = H().WithdrawStock("op-B", V(("Wood", 20)), 0, 0);
            Assert.Equal(StockTransferOutcome.Applied, b.Outcome);
            Assert.Equal(0, H().CurrentStock().GetValueOrDefault("Wood"));
            Assert.Equal(20, H().CurrentInventory()["Wood"]);

            // A resumes: bound to revision 0, now stale → rejects, no negative Stock. Both revisions
            // advance together, and validation checks inventory first, so the reported outcome is
            // StaleInventoryRevision (cosmetic ordering — the point is the stale rejection with no write).
            var resumeA = H().WithdrawStock("op-A", V(("Wood", 20)), 0, 0);
            Assert.Equal(StockTransferOutcome.StaleInventoryRevision, resumeA.Outcome);
            Assert.Equal(1, resumeA.InventoryRevision);

            var final = H();
            Assert.Equal(0, final.CurrentStock().GetValueOrDefault("Wood"));
            Assert.Equal(20, final.CurrentInventory()["Wood"]);
            Assert.Equal(1, final.StockRevision());
            Assert.All(final.CurrentStock().Values, v => Assert.True(v >= 0));
        }

        [Fact]
        public void PartialResume_AfterInterveningCommit_DrainsSource_RejectsSourceMissing()
        {
            // Distinct framing from the same-item case: A donates Wood (crashes at intent), B donates
            // the same Wood against opening revision 0 (commits, rev→1), draining the source. A naive
            // resume that trusts its stale journaled intent would re-debit already-gone Wood and
            // overdraw. The version binding catches it first: A is bound to rev 0 and is now stale.
            Dictionary<string, long> Inv() => new Dictionary<string, long> { { "Wood", 20 }, { "Stone", 20 } };
            StockTransactionHarness H() => new StockTransactionHarness(_journal, CapacityPolicy.Level2Stock,
                CapacityPolicy.PlayerCarry, Inv(), new Dictionary<string, long>(), AuthoredOptions());

            Assert.Throws<InvalidOperationException>(() =>
                H().SubmitDonation("op-A", "opt-20wood", 0, 0, new CrashAfter(StockTransferBoundary.IntentJournaled)));

            // B donates the Wood A intended, against opening revision 0 → advances to rev 1.
            var b = H().SubmitDonation("op-B", "opt-20wood", 0, 0);
            Assert.Equal(StockTransferOutcome.Applied, b.Outcome);
            Assert.Equal(0, H().CurrentInventory().GetValueOrDefault("Wood"));

            // A resumes bound to rev 0 → stale reject; source Wood is gone but we reject BEFORE touching it.
            var resumeA = H().SubmitDonation("op-A", "opt-20wood", 0, 0);
            Assert.Equal(StockTransferOutcome.StaleInventoryRevision, resumeA.Outcome);

            var final = H();
            Assert.Equal(0, final.CurrentInventory().GetValueOrDefault("Wood"));
            Assert.Equal(20, final.CurrentStock()["Wood"]);
            Assert.All(final.CurrentInventory().Values, v => Assert.True(v >= 0));
        }

        [Fact]
        public void PartialResume_NoIntervening_ResumeStillCommitsExactlyOnce()
        {
            // Idempotency preserved: with NO intervening commit, a resumed partial's bound revision
            // still matches, so it drives forward and commits exactly one transfer (the happy resume).
            Dictionary<string, long> Inv() => new Dictionary<string, long> { { "Wood", 50 } };
            StockTransactionHarness H() => new StockTransactionHarness(_journal, CapacityPolicy.Level2Stock,
                CapacityPolicy.PlayerCarry, Inv(), new Dictionary<string, long>(), AuthoredOptions());

            Assert.Throws<InvalidOperationException>(() =>
                H().SubmitDonation("op-A", "opt-20wood", 0, 0, new CrashAfter(StockTransferBoundary.SourceDebited)));

            var resume = H().SubmitDonation("op-A", "opt-20wood", 0, 0);
            Assert.Equal(StockTransferOutcome.Applied, resume.Outcome);
            Assert.Equal(30, H().CurrentInventory()["Wood"]);
            Assert.Equal(20, H().CurrentStock()["Wood"]);
            Assert.Equal(1, H().StockRevision());
        }

        [Fact]
        public void PartialResume_TerminalReplay_StillIdempotent_AcrossInterleave()
        {
            // Once A has a terminal record, replay must ALWAYS win regardless of later commits — the
            // committed transfer is durable and version binding never re-gates a terminal replay.
            Dictionary<string, long> Inv() => new Dictionary<string, long> { { "Wood", 50 }, { "Stone", 50 } };
            StockTransactionHarness H() => new StockTransactionHarness(_journal, CapacityPolicy.Level2Stock,
                CapacityPolicy.PlayerCarry, Inv(), new Dictionary<string, long>(), AuthoredOptions());

            var a = H().SubmitDonation("op-A", "opt-20wood", 0, 0); // commits, rev→1
            Assert.Equal(StockTransferOutcome.Applied, a.Outcome);
            var b = H().SubmitDonation("op-B", "opt-20stone", 1, 1); // intervening commit, rev→2
            Assert.Equal(StockTransferOutcome.Applied, b.Outcome);

            // A replays with its original (now-stale) expected revisions → Replayed, not stale.
            var replayA = H().SubmitDonation("op-A", "opt-20wood", 0, 0);
            Assert.Equal(StockTransferOutcome.Replayed, replayA.Outcome);
            Assert.Equal(a.ReceiptId, replayA.ReceiptId);
            Assert.Equal(30, H().CurrentInventory()["Wood"]); // still exactly one A-transfer
            Assert.Equal(2, H().StockRevision());
        }

        [Fact]
        public void PartialResume_CapacityRevalidated_AfterInterveningDeposit()
        {
            // A donation A journals intent while Stock has room, crashes; B deposits enough that A's
            // vector would now overflow Stock capacity. A's resume rejects stale (rev advanced) — the
            // deposit that ate the capacity is exactly the commit that advances the revision, so the
            // optimistic-revision gate and the capacity gate are consistent (no over-capacity write).
            var tightStock = new CapacityPolicy(16, 30, 500); // total cap 30 units
            StockTransactionHarness H() => new StockTransactionHarness(_journal, tightStock,
                CapacityPolicy.PlayerCarry,
                new Dictionary<string, long> { { "Wood", 50 }, { "Stone", 50 } },
                new Dictionary<string, long>(), AuthoredOptions());

            Assert.Throws<InvalidOperationException>(() =>
                H().SubmitDonation("op-A", "opt-20wood", 0, 0, new CrashAfter(StockTransferBoundary.IntentJournaled)));

            // B deposits 20 Stone (Stock now 20/30) against rev 0 → rev 1.
            var b = H().SubmitDonation("op-B", "opt-20stone", 0, 0);
            Assert.Equal(StockTransferOutcome.Applied, b.Outcome);

            // A resumes: bound to rev 0, now stale. Rejects before any write — Stock never exceeds cap.
            var resumeA = H().SubmitDonation("op-A", "opt-20wood", 0, 0);
            Assert.Equal(StockTransferOutcome.StaleInventoryRevision, resumeA.Outcome);
            var final = H();
            Assert.False(final.CurrentStock().ContainsKey("Wood"));
            Assert.Equal(20, final.CurrentStock()["Stone"]);
            long total = 0; foreach (var v in final.CurrentStock().Values) total += v;
            Assert.True(total <= 30); // capacity never violated
        }
    }
}
