using System;
using System.Collections.Generic;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Domain;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    // FIX R3 regression suite for the EVENT-TIME Homestead Stone realization lifecycle.
    //
    // The fresh review (PR #323) rejected the R1/R2 approach of reconstructing host footprint and leveled
    // surface from generic persisted ZDO pivots. R3 moves creation into the vanilla fresh-zone realization
    // event (ZoneSystem.PlaceLocations postfix), where the host's own colliders and the live Heightmap
    // exist. These tests pin the engine-free decisions and seat scorer that drive that seam:
    //   * DecideEventTime: selected+fresh -> CreateFresh; selected+resident -> ReuseExisting (no duplicate);
    //     unselected -> NotSelected (create nothing).
    //   * DecideExistingWorld: an already-generated selected host with no Stone -> MigrationRequired (never
    //     force a seat); a not-yet-generated one -> None (the one-shot event will create it).
    //   * HomesteadEventSeatScorer: best-of-eight over REAL collider AABB clearance (not ZDO pivots), honest
    //     8-of-8 rejection, and no seat when no host bounds were captured.
    public sealed class HomesteadStoneRealizationTests
    {
        // ---- Lifecycle branch decisions -------------------------------------------------------------

        [Fact]
        public void EventTime_creates_exactly_one_stone_for_a_selected_fresh_host()
        {
            Assert.Equal(
                LocationRealizationAction.CreateFresh,
                HomesteadStoneLifecycle.DecideEventTime(isSelectedHost: true, stoneAlreadyResident: false));
        }

        [Fact]
        public void EventTime_reuses_and_never_duplicates_when_a_stone_is_already_resident()
        {
            // Restart / retry: the host's PlaceLocations event will not normally re-fire, but if any path
            // reaches the decision with a resident Stone, it must reuse — never create a second one.
            Assert.Equal(
                LocationRealizationAction.ReuseExisting,
                HomesteadStoneLifecycle.DecideEventTime(isSelectedHost: true, stoneAlreadyResident: true));
        }

        [Fact]
        public void EventTime_creates_nothing_for_an_unselected_host()
        {
            Assert.Equal(
                LocationRealizationAction.NotSelected,
                HomesteadStoneLifecycle.DecideEventTime(isSelectedHost: false, stoneAlreadyResident: false));
            Assert.Equal(
                LocationRealizationAction.NotSelected,
                HomesteadStoneLifecycle.DecideEventTime(isSelectedHost: false, stoneAlreadyResident: true));
        }

        [Fact]
        public void ExistingWorld_selected_generated_host_without_stone_requires_migration_not_placement()
        {
            // A selected host whose zone is ALREADY generated (its one-shot placement event already fired and
            // destroyed the live geometry) but has no Stone can only be a pre-fix world: report migration,
            // never guess geometry or force a seat.
            Assert.Equal(
                ExistingWorldAction.MigrationRequired,
                HomesteadStoneLifecycle.DecideExistingWorld(
                    isSelectedHost: true, zoneAlreadyGenerated: true, stoneAlreadyResident: false));
        }

        [Fact]
        public void ExistingWorld_not_yet_generated_zone_is_not_a_migration()
        {
            // Not generated yet → the one-shot event will create it. Not a migration case.
            Assert.Equal(
                ExistingWorldAction.None,
                HomesteadStoneLifecycle.DecideExistingWorld(
                    isSelectedHost: true, zoneAlreadyGenerated: false, stoneAlreadyResident: false));
        }

        [Fact]
        public void ExistingWorld_resident_or_unselected_needs_nothing()
        {
            Assert.Equal(
                ExistingWorldAction.None,
                HomesteadStoneLifecycle.DecideExistingWorld(
                    isSelectedHost: true, zoneAlreadyGenerated: true, stoneAlreadyResident: true));
            Assert.Equal(
                ExistingWorldAction.None,
                HomesteadStoneLifecycle.DecideExistingWorld(
                    isSelectedHost: false, zoneAlreadyGenerated: true, stoneAlreadyResident: false));
        }

        // ---- Live host bounds geometry --------------------------------------------------------------

        [Fact]
        public void LiveHostBounds_horizontal_clearance_is_zero_inside_and_positive_outside()
        {
            // A 4x4 m AABB centred at origin. These are REAL collider-derived numbers supplied by the engine
            // caller, not inferred from ZDO pivots.
            var bounds = new LiveHostBounds(minX: -2, minZ: -2, maxX: 2, maxZ: 2, hasBounds: true);
            Assert.Equal(0.0, bounds.HorizontalClearance(0, 0), 6);      // inside
            Assert.Equal(0.0, bounds.HorizontalClearance(2, 0), 6);      // on the edge
            Assert.Equal(3.0, bounds.HorizontalClearance(5, 0), 6);      // 3 m clear of the +X face
            Assert.Equal(2.0, bounds.Extent, 6);                          // half-extent
        }

        // ---- Best-of-eight event-time seat scorer ---------------------------------------------------

        private static IReadOnlyList<EventSeat> Seats(params (double x, double z)[] xz) =>
            xz.Select((p, i) => new EventSeat(i, p.x, p.z)).ToList();

        [Fact]
        public void Scorer_returns_no_seat_when_no_host_bounds_were_captured()
        {
            // Degenerate location: no host structural collider captured this event → skip, do not seat.
            var bounds = new LiveHostBounds(0, 0, 0, 0, hasBounds: false);
            var result = HomesteadEventSeatScorer.ChooseBest(
                Seats((5, 0), (0, 5)), bounds, hostRadius: 4.0, hostCenterX: 0.0, hostCenterZ: 0.0);
            Assert.False(result.HasSeat);
        }

        [Fact]
        public void Scorer_rejects_all_eight_seats_inside_the_footprint_and_reports_eight_attempts()
        {
            // Compact host AABB; all eight seats sit inside/too close to the keep-out → honest 8-of-8 skip.
            var bounds = new LiveHostBounds(minX: -3, minZ: -3, maxX: 3, maxZ: 3, hasBounds: true);
            var seats = Enumerable.Range(0, 8)
                .Select(i => new EventSeat(i, Math.Cos(i) * 0.5, Math.Sin(i) * 0.5)) // all within the AABB
                .ToList();
            var result = HomesteadEventSeatScorer.ChooseBest(
                seats, bounds, hostRadius: 4.0, hostCenterX: 0.0, hostCenterZ: 0.0);
            Assert.False(result.HasSeat);
            Assert.Equal(8, result.AttemptsEvaluated);
        }

        [Fact]
        public void Scorer_rejects_seats_inside_the_keep_out_and_accepts_a_clear_one()
        {
            var bounds = new LiveHostBounds(minX: -3, minZ: -3, maxX: 3, maxZ: 3, hasBounds: true);
            // Seat 0 at (3.5,0): 0.5 m clearance (< 1.75 keep-out) → rejected.
            // Seat 1 at (4.0,0): 1.0 m clearance (< 1.75) → rejected.
            // Seat 2 at (5.0,0): 2.0 m clearance (>= 1.75) → valid.
            var result = HomesteadEventSeatScorer.ChooseBest(
                Seats((3.5, 0), (4.0, 0), (5.0, 0)), bounds, hostRadius: 3.0, hostCenterX: 0.0, hostCenterZ: 0.0);
            Assert.True(result.HasSeat);
            Assert.Equal(2, result.Attempt);
        }

        [Fact]
        public void Scorer_chooses_the_best_valid_seat_by_score_not_the_first()
        {
            var bounds = new LiveHostBounds(minX: -3, minZ: -3, maxX: 3, maxZ: 3, hasBounds: true);
            // Seat 0 at (5,0): 2.0 m clearance. Seat 1 at (8,0): 5.0 m clearance and still within a readable
            // yard band → higher score. The scorer must evaluate both and pick the better one.
            var result = HomesteadEventSeatScorer.ChooseBest(
                Seats((5.0, 0.0), (8.0, 0.0)), bounds, hostRadius: 3.0, hostCenterX: 0.0, hostCenterZ: 0.0);
            Assert.True(result.HasSeat);
            Assert.Equal(1, result.Attempt);
        }

        [Fact]
        public void Scorer_is_deterministic_and_ties_break_on_lowest_attempt()
        {
            // Two symmetric seats with identical clearance and radial distance → identical score; the tie
            // must break to the lowest attempt index for stable, restart-identical placement.
            var bounds = new LiveHostBounds(minX: -2, minZ: -2, maxX: 2, maxZ: 2, hasBounds: true);
            var result = HomesteadEventSeatScorer.ChooseBest(
                Seats((6.0, 0.0), (-6.0, 0.0)), bounds, hostRadius: 2.0, hostCenterX: 0.0, hostCenterZ: 0.0);
            Assert.True(result.HasSeat);
            Assert.Equal(0, result.Attempt);
        }
    }
}
