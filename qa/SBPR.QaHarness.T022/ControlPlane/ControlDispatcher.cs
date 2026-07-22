// Single-slot dispatcher core + bounded request queue (ADR-0009 §3.2, §5.2) — M2.
//
// "One primitive in flight per process." Each helper owns its own main-thread queue
// with explicit poll / cancel / deadline; a second concurrent request either queues
// (up to a bounded depth) or is shed BUSY/QueueFull; an over-deadline request returns
// TIMEOUT and frees the slot; cancel returns CANCELLED. No loops, no sleeps, no
// monolithic commands — this is what structurally avoids the ValBridge/ScriptTools
// deadlock class (§5.2).
//
// This is the ENGINE-FREE CORE of that dispatcher: the state machine only. It does not
// spawn threads, own a MonoBehaviour.Update pump, or execute any verb — the live pump
// (which calls Poll each frame with the game clock and hands the accepted verb to an
// executor) is a later, separately-reviewed slice. Time is injected (nowUnixMs) so the
// FSM is fully deterministic and unit-testable without a clock. Not thread-safe by
// design: the live pump is single-threaded on the helper's own component.
using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core.ControlPlane
{
    /// <summary>Lifecycle state of a single tracked request in the dispatcher.</summary>
    public enum SlotState
    {
        /// <summary>Accepted and waiting behind the in-flight slot in the bounded queue.</summary>
        Queued = 1,
        /// <summary>Currently occupying the single execution slot.</summary>
        InFlight = 2,
        /// <summary>Finished successfully (the executor reported completion).</summary>
        Completed = 3,
        /// <summary>Exceeded its deadline; slot was freed.</summary>
        TimedOut = 4,
        /// <summary>Cancelled by an explicit cancel before completion.</summary>
        Cancelled = 5,
    }

    /// <summary>Immutable snapshot of a tracked request's status, returned by Poll (no execution side effect).</summary>
    public sealed class SlotStatus
    {
        public string RequestId { get; }
        public SlotState State { get; }
        /// <summary>Terminal states (Completed/TimedOut/Cancelled) are final; a repeat poll returns the same snapshot.</summary>
        public bool IsTerminal => State == SlotState.Completed || State == SlotState.TimedOut || State == SlotState.Cancelled;

        public SlotStatus(string requestId, SlotState state)
        {
            RequestId = requestId;
            State = state;
        }
    }

    /// <summary>The outcome of offering a request to the dispatcher.</summary>
    public sealed class OfferResult
    {
        public ControlPlaneReason Reason { get; }
        public bool Accepted => Reason == ControlPlaneReason.None;
        /// <summary>The state the request landed in when accepted (InFlight if the slot was free, else Queued).</summary>
        public SlotState State { get; }

        private OfferResult(ControlPlaneReason reason, SlotState state)
        {
            Reason = reason;
            State = state;
        }

        public static OfferResult Reject(ControlPlaneReason reason) => new(reason, default);
        public static OfferResult Accept(SlotState state) => new(ControlPlaneReason.None, state);
    }

    /// <summary>
    /// Single-slot, deadline-bounded, cancellable dispatcher with a bounded FIFO backlog.
    /// Deterministic: every time-dependent transition takes an explicit nowUnixMs. The
    /// caller drives it: Offer(request) to admit, Poll(now) each tick to expire deadlines
    /// and promote the queue, Complete(id) when the executor finishes, Cancel(id) to abort.
    /// </summary>
    public sealed class ControlDispatcher
    {
        private sealed class Entry
        {
            public string RequestId = string.Empty;
            public long DeadlineUnixMs;
            public SlotState State;
        }

        private readonly int _maxQueueDepth;
        private readonly List<Entry> _backlog = new();
        private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
        private Entry? _inFlight;

        /// <param name="maxQueueDepth">
        /// How many requests may wait BEHIND the in-flight slot. 0 = strict single-slot
        /// (a second concurrent request is shed BUSY). &gt;0 = a bounded FIFO backlog; the
        /// (depth+1)th concurrent request is shed QueueFull.
        /// </param>
        public ControlDispatcher(int maxQueueDepth = 0)
        {
            if (maxQueueDepth < 0) throw new ArgumentOutOfRangeException(nameof(maxQueueDepth));
            _maxQueueDepth = maxQueueDepth;
        }

        /// <summary>The requestId currently occupying the slot, or null when idle.</summary>
        public string? InFlightId => _inFlight?.RequestId;

        /// <summary>Number of requests waiting behind the slot.</summary>
        public int QueuedCount => _backlog.Count;

        /// <summary>True when nothing is executing.</summary>
        public bool Idle => _inFlight == null;

        /// <summary>
        /// Offer a request for execution. A duplicate live requestId is idempotent (returns
        /// its current non-terminal state, never a second slot). If the slot is free the
        /// request goes InFlight with a deadline of now+timeout; otherwise it queues, or is
        /// shed BUSY (depth 0) / QueueFull (backlog full).
        /// </summary>
        public OfferResult Offer(string? requestId, long nowUnixMs, long timeoutMs)
        {
            if (string.IsNullOrEmpty(requestId)) return OfferResult.Reject(ControlPlaneReason.MalformedFrame);
            if (timeoutMs <= 0) return OfferResult.Reject(ControlPlaneReason.MalformedFrame);

            // Idempotency: a live (queued/in-flight) duplicate returns its current state.
            if (_byId.TryGetValue(requestId!, out var existing))
            {
                if (existing.State == SlotState.Queued || existing.State == SlotState.InFlight)
                    return OfferResult.Accept(existing.State);
                // Terminal: the id is spent. Refuse re-use rather than silently re-running.
                return OfferResult.Reject(ControlPlaneReason.UnknownRequest);
            }

            var entry = new Entry { RequestId = requestId!, DeadlineUnixMs = nowUnixMs + timeoutMs };

            if (_inFlight == null)
            {
                entry.State = SlotState.InFlight;
                _inFlight = entry;
                _byId[entry.RequestId] = entry;
                return OfferResult.Accept(SlotState.InFlight);
            }

            // Slot busy — queue if there's room, else shed.
            if (_maxQueueDepth == 0) return OfferResult.Reject(ControlPlaneReason.Busy);
            if (_backlog.Count >= _maxQueueDepth) return OfferResult.Reject(ControlPlaneReason.QueueFull);

            entry.State = SlotState.Queued;
            _backlog.Add(entry);
            _byId[entry.RequestId] = entry;
            return OfferResult.Accept(SlotState.Queued);
        }

        /// <summary>
        /// Advance the clock. Expires the in-flight request if its deadline passed (freeing
        /// the slot and promoting the head of the backlog with a FRESH deadline of
        /// now+timeout), and returns the current in-flight status. Call every tick.
        /// </summary>
        public SlotStatus? Poll(long nowUnixMs, long promotedTimeoutMs)
        {
            if (_inFlight != null && nowUnixMs >= _inFlight.DeadlineUnixMs)
            {
                _inFlight.State = SlotState.TimedOut;
                _inFlight = null;
                PromoteQueueHead(nowUnixMs, promotedTimeoutMs);
            }
            return _inFlight == null ? null : new SlotStatus(_inFlight.RequestId, _inFlight.State);
        }

        /// <summary>Look up any tracked request's status (queued/in-flight/terminal), or null if unknown.</summary>
        public SlotStatus? Status(string? requestId)
        {
            if (requestId != null && _byId.TryGetValue(requestId, out var e))
                return new SlotStatus(e.RequestId, e.State);
            return null;
        }

        /// <summary>
        /// The executor reports the in-flight primitive finished. Marks it Completed, frees
        /// the slot, and promotes the backlog head. Returns UnknownRequest if the id is not
        /// the current in-flight request.
        /// </summary>
        public ControlPlaneReason Complete(string? requestId, long nowUnixMs, long promotedTimeoutMs)
        {
            if (_inFlight == null || requestId == null || !string.Equals(requestId, _inFlight.RequestId, StringComparison.Ordinal))
                return ControlPlaneReason.UnknownRequest;
            _inFlight.State = SlotState.Completed;
            _inFlight = null;
            PromoteQueueHead(nowUnixMs, promotedTimeoutMs);
            return ControlPlaneReason.None;
        }

        /// <summary>
        /// Cancel a tracked request. If it is in flight the slot is freed and the backlog
        /// head promoted; if it is merely queued it is marked Cancelled and skipped when it
        /// would reach the head. Returns UnknownRequest for an unknown/terminal id.
        /// </summary>
        public ControlPlaneReason Cancel(string? requestId, long nowUnixMs, long promotedTimeoutMs)
        {
            if (requestId == null || !_byId.TryGetValue(requestId, out var e))
                return ControlPlaneReason.UnknownRequest;
            if (e.State == SlotState.InFlight)
            {
                e.State = SlotState.Cancelled;
                _inFlight = null;
                PromoteQueueHead(nowUnixMs, promotedTimeoutMs);
                return ControlPlaneReason.None;
            }
            if (e.State == SlotState.Queued)
            {
                // Lazily removed from the FIFO at promotion time; mark terminal now.
                e.State = SlotState.Cancelled;
                return ControlPlaneReason.None;
            }
            return ControlPlaneReason.UnknownRequest; // already terminal
        }

        // Promote the next non-cancelled backlog entry into the free slot with a fresh
        // deadline. Cancelled queued entries are discarded here (lazy removal).
        private void PromoteQueueHead(long nowUnixMs, long promotedTimeoutMs)
        {
            while (_backlog.Count > 0)
            {
                var head = _backlog[0];
                _backlog.RemoveAt(0);
                if (head.State != SlotState.Queued) continue; // skip cancelled-in-queue
                head.State = SlotState.InFlight;
                head.DeadlineUnixMs = nowUnixMs + (promotedTimeoutMs > 0 ? promotedTimeoutMs : 1);
                _inFlight = head;
                return;
            }
        }
    }
}
