// ADR-0009 M2 — single-slot dispatcher + bounded queue tests.
// Covers AT-QA-BUSY-TIMEOUT-CANCEL (core FSM half): one primitive in flight,
// BUSY / QueueFull shedding, TIMEOUT frees the slot, CANCELLED, idempotent re-offer,
// FIFO promotion with a fresh deadline, and cancel-in-queue.
using SBPR.QaHarness.T022.Core.ControlPlane;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class ControlDispatcherTests
    {
        private const long T = 10_000; // arbitrary "now"
        private const long Timeout = 5_000;

        // ── Single slot: first goes in flight, second is BUSY (depth 0) ──────
        [Fact]
        public void FirstOffer_InFlight_SecondBusy()
        {
            var d = new ControlDispatcher(maxQueueDepth: 0);
            Assert.Equal(SlotState.InFlight, d.Offer("a", T, Timeout).State);
            Assert.Equal("a", d.InFlightId);
            var second = d.Offer("b", T, Timeout);
            Assert.Equal(ControlPlaneReason.Busy, second.Reason);
        }

        // ── Bounded queue: depth 1 accepts one waiter, sheds the next ────────
        [Fact]
        public void BoundedQueue_AcceptsThenQueueFull()
        {
            var d = new ControlDispatcher(maxQueueDepth: 1);
            Assert.Equal(SlotState.InFlight, d.Offer("a", T, Timeout).State);
            Assert.Equal(SlotState.Queued, d.Offer("b", T, Timeout).State);
            Assert.Equal(1, d.QueuedCount);
            Assert.Equal(ControlPlaneReason.QueueFull, d.Offer("c", T, Timeout).Reason);
        }

        // ── Idempotent re-offer of a live id returns its state, no 2nd slot ──
        [Fact]
        public void ReOfferLiveId_Idempotent()
        {
            var d = new ControlDispatcher(maxQueueDepth: 1);
            d.Offer("a", T, Timeout);
            var again = d.Offer("a", T, Timeout);
            Assert.True(again.Accepted);
            Assert.Equal(SlotState.InFlight, again.State);
            Assert.Equal("a", d.InFlightId);
            Assert.Equal(0, d.QueuedCount); // no second slot created
        }

        // ── TIMEOUT frees the slot on Poll past deadline ─────────────────────
        [Fact]
        public void Poll_PastDeadline_TimesOutAndFrees()
        {
            var d = new ControlDispatcher();
            d.Offer("a", T, Timeout);
            Assert.NotNull(d.Poll(T + Timeout - 1, Timeout)); // still in flight before deadline
            var after = d.Poll(T + Timeout, Timeout);         // at deadline: expired
            Assert.Null(after);
            Assert.True(d.Idle);
            Assert.Equal(SlotState.TimedOut, d.Status("a")!.State);
        }

        // ── Timeout promotes the FIFO head with a FRESH deadline ─────────────
        [Fact]
        public void Timeout_PromotesQueueHead_FreshDeadline()
        {
            var d = new ControlDispatcher(maxQueueDepth: 2);
            d.Offer("a", T, Timeout);
            d.Offer("b", T, Timeout);
            var s = d.Poll(T + Timeout, Timeout);      // a times out, b promoted
            Assert.Equal("b", s!.RequestId);
            Assert.Equal(SlotState.InFlight, s.State);
            // b's deadline is fresh (now+timeout), so it survives a poll at the old deadline.
            Assert.NotNull(d.Poll(T + Timeout, Timeout));
            Assert.Equal("b", d.InFlightId);
        }

        // ── Complete frees the slot and promotes the next ────────────────────
        [Fact]
        public void Complete_FreesAndPromotes()
        {
            var d = new ControlDispatcher(maxQueueDepth: 1);
            d.Offer("a", T, Timeout);
            d.Offer("b", T, Timeout);
            Assert.Equal(ControlPlaneReason.None, d.Complete("a", T + 100, Timeout));
            Assert.Equal("b", d.InFlightId);
            Assert.Equal(SlotState.Completed, d.Status("a")!.State);
        }

        [Fact]
        public void Complete_WrongId_Unknown()
        {
            var d = new ControlDispatcher();
            d.Offer("a", T, Timeout);
            Assert.Equal(ControlPlaneReason.UnknownRequest, d.Complete("zzz", T, Timeout));
        }

        // ── Cancel in flight frees the slot ──────────────────────────────────
        [Fact]
        public void CancelInFlight_FreesSlot()
        {
            var d = new ControlDispatcher(maxQueueDepth: 1);
            d.Offer("a", T, Timeout);
            d.Offer("b", T, Timeout);
            Assert.Equal(ControlPlaneReason.None, d.Cancel("a", T + 50, Timeout));
            Assert.Equal(SlotState.Cancelled, d.Status("a")!.State);
            Assert.Equal("b", d.InFlightId); // b promoted
        }

        // ── Cancel a queued entry: skipped at promotion, never runs ──────────
        [Fact]
        public void CancelQueued_SkippedAtPromotion()
        {
            var d = new ControlDispatcher(maxQueueDepth: 2);
            d.Offer("a", T, Timeout);
            d.Offer("b", T, Timeout);
            d.Offer("c", T, Timeout);
            Assert.Equal(ControlPlaneReason.None, d.Cancel("b", T, Timeout)); // cancel the queued middle
            d.Complete("a", T + 10, Timeout);                                 // a done -> promote
            Assert.Equal("c", d.InFlightId);                                  // b skipped
            Assert.Equal(SlotState.Cancelled, d.Status("b")!.State);
        }

        [Fact]
        public void Cancel_UnknownId_Unknown()
            => Assert.Equal(ControlPlaneReason.UnknownRequest, new ControlDispatcher().Cancel("nope", T, Timeout));

        // ── Terminal id cannot be re-offered ─────────────────────────────────
        [Fact]
        public void ReOfferTerminalId_Rejected()
        {
            var d = new ControlDispatcher();
            d.Offer("a", T, Timeout);
            d.Complete("a", T + 1, Timeout);
            Assert.Equal(ControlPlaneReason.UnknownRequest, d.Offer("a", T + 2, Timeout).Reason);
        }

        [Fact]
        public void EmptyIdOrBadTimeout_Rejected()
        {
            var d = new ControlDispatcher();
            Assert.False(d.Offer("", T, Timeout).Accepted);
            Assert.False(d.Offer("x", T, 0).Accepted);
        }
    }
}
