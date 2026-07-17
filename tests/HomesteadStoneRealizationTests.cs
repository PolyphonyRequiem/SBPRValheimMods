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

        // NOTE: the R3 HomesteadStoneLifecycle.DecideExistingWorld / ExistingWorldAction pair is REMOVED in
        // R4 and fully superseded by the provenance-aware HomesteadMigrationClassifier (see the FIX R4 (#4)
        // section below), which distinguishes a genuine pre-fix migration from a fresh-event failure.

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
                Seats((5, 0), (0, 5)), bounds, hostCenterX: 0.0, hostCenterZ: 0.0);
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
                seats, bounds, hostCenterX: 0.0, hostCenterZ: 0.0);
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
                Seats((3.5, 0), (4.0, 0), (5.0, 0)), bounds, hostCenterX: 0.0, hostCenterZ: 0.0);
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
                Seats((5.0, 0.0), (8.0, 0.0)), bounds, hostCenterX: 0.0, hostCenterZ: 0.0);
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
                Seats((6.0, 0.0), (-6.0, 0.0)), bounds, hostCenterX: 0.0, hostCenterZ: 0.0);
            Assert.True(result.HasSeat);
            Assert.Equal(0, result.Attempt);
        }

        // ---- FIX R4 (#3): structural-extent scoring, NOT location radius --------------------------

        [Fact]
        public void Scorer_scores_against_structural_extent_choosing_differently_than_location_radius_would()
        {
            // Discriminating case the review demanded: the choice must flip between scoring by the ACTUAL
            // structural extent vs the coarse location radius. We decouple clearance from radial distance with
            // a rectangular host AABB (long in X, thin in Z), then give two seats with IDENTICAL clearance but
            // different radial distance — so the only thing that can break the tie is the yard band, which is
            // centred on (reference + 2.5).
            //
            //   Host AABB: x∈[-10,10], z∈[-1,1]  →  structural Extent = 10 (the real building half-length).
            //   Seat A = (0, 3):  clearance = 3 - 1  = 2,  radial = 3.
            //   Seat B = (12, 0): clearance = 12 - 10 = 2, radial = 12.
            //
            // Under the REAL structural extent (10) the yard band peaks at radial 12.5, so Seat B (radial 12)
            // scores higher and is chosen. Under the OLD location-radius reference (a small ~2 m radius) the
            // band would peak at 4.5 and Seat A (radial 3) would win. The scorer uses the structural extent,
            // so it MUST choose Seat B.
            var bounds = new LiveHostBounds(minX: -10, minZ: -1, maxX: 10, maxZ: 1, hasBounds: true);
            Assert.Equal(10.0, bounds.Extent, 6);

            var result = HomesteadEventSeatScorer.ChooseBest(
                Seats((0.0, 3.0), (12.0, 0.0)), bounds, hostCenterX: 0.0, hostCenterZ: 0.0);

            Assert.True(result.HasSeat);
            Assert.Equal(1, result.Attempt); // Seat B — chosen by REAL structural extent, not a small location radius.
        }

        // ---- FIX R4 (#1): terrain finalization decision/order ------------------------------------

        [Fact]
        public void Terrain_finalization_forces_regenerate_when_a_covering_heightmap_has_a_queued_rebuild()
        {
            // Vanilla TerrainModifier.Awake queued a DELAYED Poke; the covering Heightmap still carries an
            // un-applied leveling. The postfix runs before CustomLateUpdate, so it MUST force the regenerate
            // now rather than sample pre-leveling terrain.
            Assert.Equal(
                TerrainFinalizationAction.ForceRegenerate,
                HomesteadTerrainFinalization.Decide(anyCoveringHeightmapHasQueuedRebuild: true));
        }

        [Fact]
        public void Terrain_finalization_samples_directly_when_no_covering_heightmap_has_a_queued_rebuild()
        {
            // No queued rebuild → terrain is already at its final leveled state; do not force a needless
            // regenerate, sample directly.
            Assert.Equal(
                TerrainFinalizationAction.AlreadyFinal,
                HomesteadTerrainFinalization.Decide(anyCoveringHeightmapHasQueuedRebuild: false));
        }

        // ---- FIX R4 (#2): metadata-aware selected-set reconciliation ------------------------------

        private static SelectedStoneExpectation Expect(string prefab, int x, int z) =>
            new SelectedStoneExpectation("world-1", "sel-v1", prefab, x, z);

        private static StoneZdoRecord Record(long id, string world, string sel, string prefab, int x, int z) =>
            new StoneZdoRecord(id, world, sel, prefab, x, z);

        private static IReadOnlyDictionary<string, SelectedStoneExpectation> Selected(
            params (string prefab, int x, int z)[] items)
        {
            var d = new Dictionary<string, SelectedStoneExpectation>(StringComparer.Ordinal);
            foreach (var it in items) d[it.x + ":" + it.z] = Expect(it.prefab, it.x, it.z);
            return d;
        }

        [Fact]
        public void Reconcile_reuses_a_matching_selected_stone()
        {
            var result = HomesteadStoneReconciler.Reconcile(
                new List<StoneZdoRecord> { Record(1, "world-1", "sel-v1", "WoodHouse1", 3, 4) },
                Selected(("WoodHouse1", 3, 4)));

            var d = Assert.Single(result.Decisions);
            Assert.Equal(StoneReconcileDisposition.Reuse, d.Disposition);
            Assert.Contains("3:4", result.KeptZoneKeys);
        }

        [Fact]
        public void Reconcile_removes_an_unselected_stone()
        {
            // A Stone whose zone is not in the current selected set (selector reroll) must be removed so
            // rerolls cannot accumulate a union of Stones.
            var result = HomesteadStoneReconciler.Reconcile(
                new List<StoneZdoRecord> { Record(1, "world-1", "sel-v1", "WoodHouse1", 9, 9) },
                Selected(("WoodHouse1", 3, 4)));

            var d = Assert.Single(result.Decisions);
            Assert.Equal(StoneReconcileDisposition.Remove, d.Disposition);
            Assert.Empty(result.KeptZoneKeys);
        }

        [Fact]
        public void Reconcile_removes_a_metadata_mismatched_stone_on_a_selected_zone()
        {
            // Selected zone (3,4), but the resident Stone carries a stale host prefab / selector: metadata
            // mismatch → remove, and the zone is NOT kept (so fresh creation is not suppressed).
            var result = HomesteadStoneReconciler.Reconcile(
                new List<StoneZdoRecord> { Record(1, "world-1", "OLD-selector", "WoodHouse1", 3, 4) },
                Selected(("WoodHouse1", 3, 4)));

            var d = Assert.Single(result.Decisions);
            Assert.Equal(StoneReconcileDisposition.Remove, d.Disposition);
            Assert.DoesNotContain("3:4", result.KeptZoneKeys);
        }

        [Fact]
        public void Reconcile_keeps_the_lowest_id_duplicate_and_removes_the_extras_deterministically()
        {
            // Three matching ZDOs for the same selected zone → keep the lowest id (5), remove 7 and 9 as
            // duplicates, deterministically.
            var result = HomesteadStoneReconciler.Reconcile(
                new List<StoneZdoRecord>
                {
                    Record(9, "world-1", "sel-v1", "WoodHouse1", 3, 4),
                    Record(5, "world-1", "sel-v1", "WoodHouse1", 3, 4),
                    Record(7, "world-1", "sel-v1", "WoodHouse1", 3, 4),
                },
                Selected(("WoodHouse1", 3, 4)));

            var reuse = Assert.Single(result.Decisions, x => x.Disposition == StoneReconcileDisposition.Reuse);
            Assert.Equal(5, reuse.ZdoId);
            var dupes = result.Decisions.Where(x => x.Disposition == StoneReconcileDisposition.RemoveDuplicate)
                .Select(x => x.ZdoId).OrderBy(x => x).ToList();
            Assert.Equal(new List<long> { 7, 9 }, dupes);
            Assert.Contains("3:4", result.KeptZoneKeys);
        }

        [Fact]
        public void Reconcile_stale_stone_on_a_selected_zone_does_not_suppress_fresh_creation()
        {
            // The load-bearing invariant: a removed stale Stone must leave the selected zone NOT kept, so the
            // event seam is free to create a fresh Stone for it.
            var selectedZoneKey = "3:4";
            var result = HomesteadStoneReconciler.Reconcile(
                new List<StoneZdoRecord> { Record(1, "world-1", "OLD-selector", "WoodHouse1", 3, 4) },
                Selected(("WoodHouse1", 3, 4)));

            Assert.DoesNotContain(selectedZoneKey, result.KeptZoneKeys); // zone remains eligible for fresh creation
        }

        // ---- FIX R4 (#4): provenance-aware migration classification ------------------------------

        [Fact]
        public void Migration_true_prefix_world_generated_at_start_missing_stone_no_event()
        {
            // Zone was already generated when the world started, still has no Stone, and no fresh event fired
            // this session → genuine pre-fix migration.
            Assert.Equal(
                MigrationClassification.MigrationRequired,
                HomesteadMigrationClassifier.Classify(
                    isSelectedHost: true, zoneGeneratedOnStart: true, stoneResident: false,
                    freshOutcome: StoneEventOutcome.Unknown));
        }

        [Fact]
        public void Migration_fresh_invalid_seats_is_not_labelled_migration()
        {
            // A fresh event this session honestly rejected all eight seats. Even though vanilla has since
            // marked the zone generated, this MUST NOT be relabelled as migration.
            Assert.Equal(
                MigrationClassification.FreshInvalidSeats,
                HomesteadMigrationClassifier.Classify(
                    isSelectedHost: true, zoneGeneratedOnStart: true, stoneResident: false,
                    freshOutcome: StoneEventOutcome.FreshInvalidSeats));
        }

        [Fact]
        public void Migration_fresh_transient_failure_is_not_labelled_migration()
        {
            Assert.Equal(
                MigrationClassification.FreshTransientFailure,
                HomesteadMigrationClassifier.Classify(
                    isSelectedHost: true, zoneGeneratedOnStart: true, stoneResident: false,
                    freshOutcome: StoneEventOutcome.FreshTransientFailure));
        }

        [Fact]
        public void Migration_fresh_created_needs_no_diagnostic()
        {
            Assert.Equal(
                MigrationClassification.None,
                HomesteadMigrationClassifier.Classify(
                    isSelectedHost: true, zoneGeneratedOnStart: false, stoneResident: false,
                    freshOutcome: StoneEventOutcome.FreshCreated));
        }

        [Fact]
        public void Migration_resident_or_unselected_needs_nothing()
        {
            Assert.Equal(
                MigrationClassification.None,
                HomesteadMigrationClassifier.Classify(
                    isSelectedHost: true, zoneGeneratedOnStart: true, stoneResident: true,
                    freshOutcome: StoneEventOutcome.Unknown));
            Assert.Equal(
                MigrationClassification.None,
                HomesteadMigrationClassifier.Classify(
                    isSelectedHost: false, zoneGeneratedOnStart: true, stoneResident: false,
                    freshOutcome: StoneEventOutcome.Unknown));
        }

        [Fact]
        public void Migration_not_generated_at_start_and_no_event_yet_is_not_a_migration()
        {
            // Fresh world: the zone was not generated at start and its one-shot event has not fired yet. The
            // event will create the Stone; this is NOT a migration.
            Assert.Equal(
                MigrationClassification.None,
                HomesteadMigrationClassifier.Classify(
                    isSelectedHost: true, zoneGeneratedOnStart: false, stoneResident: false,
                    freshOutcome: StoneEventOutcome.Unknown));
        }
    }
}
