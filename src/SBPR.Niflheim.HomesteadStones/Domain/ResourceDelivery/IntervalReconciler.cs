using System;
using System.Collections.Generic;

namespace SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery
{
    // RD-T002 (Gate A) — the pure reconcile-before-mutation interval integrator (spec RD-007 /
    // data-model §Offline reconciliation / contracts §ReconcileStoneContribution). This is the second
    // load-bearing Gate-A mechanism: exact offline interval reconciliation that is TERMINALLY IDENTICAL
    // whether the elapsed span is processed as one offline jump or as arbitrary smaller online
    // partitions.
    //
    // DESIGN
    //   Contribution accrues at a piecewise-constant rate. The caller resolves the rate schedule by
    //   splitting [lastCursor, receivedServerTime) at every participation/maturity/lifecycle boundary
    //   (a segment carries the whole exact contribution units-per-second in force over that span). The
    //   reconciler integrates that schedule into the Resource Delivery meter:
    //     * accumulate exact whole units (no floating point — data-model modeling rule 6);
    //     * complete/deposit as many delivery cycles as the threshold and CAPACITY permit;
    //     * carry threshold excess forward as residual progress (spec RD-007 "retain threshold excess");
    //     * once capacity blocks a completed bundle, enter PendingCapacity, freeze at that effective
    //       time, and DISCARD later Resource Delivery time (spec RD-007 "discard only Resource Delivery
    //       time elapsed after PendingCapacity begins").
    //
    // PARTITION EQUIVALENCE (the AT-RD-007 property)
    //   Reconcile over [a,c) from state S equals Reconcile over [a,b) from S, then [b,c) from the
    //   resulting state, for any split point b. Guaranteed because (1) integration is additive over the
    //   cursor, (2) residual progress is carried in the returned state, and (3) once PendingCapacity is
    //   latched no later time is banked. Same-time ties are broken by durable receipt order upstream;
    //   this integrator treats a zero-length segment as contributing zero, so re-partitioning at an
    //   exact instant is a no-op.
    //
    // net48 audit: System.Collections.Generic only. Engine-free; link-compiles into net8 tests.

    /// <summary>One piecewise-constant contribution segment: the exact whole units-per-second in force
    /// over <c>[StartSeconds, EndSeconds)</c>. The caller produces these by splitting the elapsed span
    /// at every boundary; an empty/zero-rate span (e.g. a 0× participation lapse) is a legal segment
    /// that simply banks nothing.</summary>
    public readonly struct ContributionSegment
    {
        public ContributionSegment(long startSeconds, long endSeconds, long unitsPerSecond)
        {
            StartSeconds = startSeconds;
            EndSeconds = endSeconds;
            UnitsPerSecond = unitsPerSecond < 0 ? 0 : unitsPerSecond; // clock anomaly / paused = zero
        }

        public long StartSeconds { get; }
        public long EndSeconds { get; }
        public long UnitsPerSecond { get; }

        public long DurationSeconds => EndSeconds > StartSeconds ? EndSeconds - StartSeconds : 0;
        public long Units => DurationSeconds * UnitsPerSecond;
    }

    /// <summary>Status of the Resource Delivery meter after reconciliation (data-model Aggregate 3
    /// §Resource Delivery meter Status).</summary>
    public enum DeliveryMeterStatus
    {
        Accruing = 0,
        PendingCapacity = 1,
        Dormant = 2
    }

    /// <summary>The immutable meter state carried across reconciliations. <see cref="ResidualUnits"/> is
    /// the in-cycle progress toward the next completion; <see cref="CursorSeconds"/> is the last
    /// reconciled server time. When <see cref="Status"/> is PendingCapacity the meter is frozen at
    /// <see cref="CursorSeconds"/> and banks no later time until the pending bundle deposits.</summary>
    public readonly struct DeliveryMeterState
    {
        public DeliveryMeterState(long cursorSeconds, long residualUnits, long completedCycles,
            DeliveryMeterStatus status)
        {
            CursorSeconds = cursorSeconds;
            ResidualUnits = residualUnits < 0 ? 0 : residualUnits;
            CompletedCycles = completedCycles;
            Status = status;
        }

        public long CursorSeconds { get; }
        public long ResidualUnits { get; }

        /// <summary>Total delivery cycles completed/deposited over this meter's lifetime. Monotonic.</summary>
        public long CompletedCycles { get; }

        public DeliveryMeterStatus Status { get; }

        public static DeliveryMeterState Start(long cursorSeconds) =>
            new DeliveryMeterState(cursorSeconds, 0, 0, DeliveryMeterStatus.Accruing);
    }

    /// <summary>The exact contribution-integration + delivery-cycle reconciler (spec RD-007). Pure and
    /// deterministic: same inputs → same terminal state, and any interval partition converges.</summary>
    public static class IntervalReconciler
    {
        /// <summary>Reconcile the meter forward across <paramref name="segments"/> (which must tile
        /// <c>[state.CursorSeconds, receivedServerTimeSeconds)</c> in ascending, non-overlapping order).
        /// Completes as many cycles as <paramref name="thresholdUnits"/> and capacity permit, carries
        /// residual progress, and latches PendingCapacity when a completed bundle cannot deposit.</summary>
        /// <param name="state">Prior meter state (cursor + residual + status).</param>
        /// <param name="segments">Ascending, contiguous rate segments covering the elapsed span.</param>
        /// <param name="receivedServerTimeSeconds">The server time this reconciliation advances to.</param>
        /// <param name="thresholdUnits">Exact whole units per delivery cycle (Humble = 24 baseline
        /// contribution-hours; here expressed in the caller's chosen unit).</param>
        /// <param name="capacityCyclesAvailable">How many completed bundles can still DEPOSIT before the
        /// Stockpile is full. <c>long.MaxValue</c> = unbounded. When a cycle completes with zero capacity
        /// remaining, the meter latches PendingCapacity and banks no further time.</param>
        /// <param name="hasNonEmptyBundle">Whether any active node contributes a non-empty bundle. When
        /// false the meter is Dormant: it still integrates residual progress but completes no cycle
        /// (data-model Aggregate 3: "complete no empty cycle").</param>
        public static DeliveryMeterState Reconcile(
            DeliveryMeterState state,
            IReadOnlyList<ContributionSegment> segments,
            long receivedServerTimeSeconds,
            long thresholdUnits,
            long capacityCyclesAvailable,
            bool hasNonEmptyBundle = true)
        {
            if (thresholdUnits <= 0) throw new ArgumentOutOfRangeException(nameof(thresholdUnits));

            // A meter already frozen on capacity banks no later time until its pending bundle deposits
            // (handled by a separate deposit/withdrawal path). The cursor STAYS at the latch time — later
            // wall time is discarded, not banked — so a partitioned replay stays terminally identical.
            if (state.Status == DeliveryMeterStatus.PendingCapacity)
                return state;

            long residual = state.ResidualUnits;
            long completed = state.CompletedCycles;
            long capacity = capacityCyclesAvailable;
            long cursor = state.CursorSeconds;

            if (segments != null)
            {
                foreach (var seg in segments)
                {
                    // Ignore any portion at or before the cursor (idempotent re-partition) and clamp to
                    // the received server time.
                    long start = seg.StartSeconds < cursor ? cursor : seg.StartSeconds;
                    long end = seg.EndSeconds > receivedServerTimeSeconds ? receivedServerTimeSeconds : seg.EndSeconds;
                    if (end <= start) { if (seg.EndSeconds > cursor) cursor = Math.Min(seg.EndSeconds, receivedServerTimeSeconds); continue; }

                    long rate = seg.UnitsPerSecond;
                    long residualAtSegStart = residual;
                    long available = residual + (end - start) * rate;

                    if (!hasNonEmptyBundle)
                    {
                        // Dormant: accrue residual but complete no cycle.
                        residual = available;
                        cursor = end;
                        continue;
                    }

                    long cyclesDrainedThisSeg = 0;
                    while (available >= thresholdUnits)
                    {
                        if (capacity <= 0)
                        {
                            // Completed bundle cannot deposit -> PendingCapacity, latched at the EXACT
                            // crossing time of this completion. Later time is discarded (not banked): the
                            // meter freezes carrying exactly one pending bundle worth of residual.
                            long unitsNeeded = (cyclesDrainedThisSeg + 1) * thresholdUnits - residualAtSegStart;
                            long crossTime = rate > 0 ? start + CeilDiv(unitsNeeded, rate) : end;
                            if (crossTime > end) crossTime = end;
                            return new DeliveryMeterState(crossTime, thresholdUnits, completed,
                                DeliveryMeterStatus.PendingCapacity);
                        }
                        available -= thresholdUnits;
                        completed += 1;
                        capacity -= 1;
                        cyclesDrainedThisSeg += 1;
                    }

                    residual = available;
                    cursor = end;
                }
            }

            long finalCursor = receivedServerTimeSeconds > cursor ? receivedServerTimeSeconds : cursor;
            var status = hasNonEmptyBundle ? DeliveryMeterStatus.Accruing : DeliveryMeterStatus.Dormant;
            return new DeliveryMeterState(finalCursor, residual, completed, status);
        }

        private static long CeilDiv(long a, long b) => (a + b - 1) / b;
    }
}
