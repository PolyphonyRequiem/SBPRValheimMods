using System;
using System.Collections.Generic;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Domain;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    // FIX R2 regression suite for the SERVER-AUTHORITATIVE headless seat resolver (review blocker 1).
    //
    // The R1 dedicated fallback picked seats[0] blindly and used base WorldGenerator.GetHeight, which could
    // persist a Stone inside compact host geometry or off the location's leveled surface. These tests pin the
    // pure resolver that replaces it: it evaluates ALL eight deterministic seats against harvested host-
    // structure ZDO facts, enforces a footprint keep-out + clearance, validates the final Y from persisted
    // leveled-surface evidence, defers when no evidence exists yet, and skips honestly when all eight fail.
    public sealed class HomesteadHeadlessSeatResolverTests
    {
        private static readonly HeadlessSeatModel Model = new HeadlessSeatModel(keepOut: 1.75, surfaceSampleRadius: 6.0);

        private static IReadOnlyList<SeatFact> Seats(params (double x, double z)[] xz) =>
            xz.Select((p, i) => new SeatFact(i, p.x, p.z)).ToList();

        [Fact]
        public void Defers_when_no_structure_evidence_is_persisted_yet()
        {
            // Host location placed but its ghost-spawned structure ZDOs have not flushed: we must DEFER, never
            // guess a seat or a surface.
            var seats = Seats((5.0, 0.0), (0.0, 5.0), (-5.0, 0.0));
            var resolution = HomesteadHeadlessSeatResolver.Resolve(
                seats, Array.Empty<HostStructureFact>(), hostCenterX: 0.0, hostCenterZ: 0.0, hostRadius: 4.0, Model);

            Assert.False(resolution.HasSeat);
            Assert.Equal(HeadlessSeatOutcome.NoStructureEvidence, resolution.Outcome);
            Assert.Equal(3, resolution.AttemptsEvaluated);
        }

        [Fact]
        public void Evaluates_all_eight_seats_and_reports_eight_attempts()
        {
            // Provide 8 seats where none is valid (all inside the footprint) and confirm all 8 were considered
            // (no first-seat short-circuit) — the honest 8-of-8 skip.
            var structure = new List<HostStructureFact> { new HostStructureFact(0.0, 0.0, 30.0) };
            var seats = Enumerable.Range(0, 8)
                .Select(i => new SeatFact(i, Math.Cos(i) * 0.5, Math.Sin(i) * 0.5)) // all within 0.5 m of host point
                .ToList();

            var resolution = HomesteadHeadlessSeatResolver.Resolve(
                seats, structure, hostCenterX: 0.0, hostCenterZ: 0.0, hostRadius: 4.0, Model);

            Assert.False(resolution.HasSeat);
            Assert.Equal(HeadlessSeatOutcome.AllSeatsRejected, resolution.Outcome);
            Assert.Equal(8, resolution.AttemptsEvaluated);
        }

        [Fact]
        public void Rejects_seats_inside_the_footprint_keep_out_of_a_compact_host()
        {
            // Compact host: a dense cluster of structure points near the center. Only a seat well clear of all
            // of them (and with nearby surface evidence) may be chosen.
            var structure = new List<HostStructureFact>
            {
                new HostStructureFact(0.0, 0.0, 30.0),
                new HostStructureFact(1.0, 0.0, 30.0),
                new HostStructureFact(0.0, 1.0, 30.0),
                new HostStructureFact(-1.0, 0.0, 30.0),
                new HostStructureFact(0.0, -1.0, 30.0),
                new HostStructureFact(4.5, 0.0, 31.0), // an outer wall point giving surface evidence to the clear seat
            };
            // Seat 0 sits 1 m from a structure point (inside 1.75 m keep-out) → rejected.
            // Seat 1 sits at (5,0): 0.5 m from the outer wall point (inside keep-out) → rejected.
            // Seat 2 sits at (3,0): nearest structure point is the outer wall at 1.5 m (inside keep-out) → rejected.
            // Seat 3 sits at (6.2,0): 1.7 m from outer wall — still inside 1.75 → rejected.
            // Seat 4 sits at (6.3,0): 1.8 m from outer wall (clear) AND within 6 m surface radius → valid.
            var seats = Seats((0.5, 0.0), (5.0, 0.0), (3.0, 0.0), (6.2, 0.0), (6.3, 0.0));

            var resolution = HomesteadHeadlessSeatResolver.Resolve(
                seats, structure, hostCenterX: 0.0, hostCenterZ: 0.0, hostRadius: 4.0, Model);

            Assert.True(resolution.HasSeat);
            Assert.Equal(4, resolution.Seat.Attempt);
            Assert.Equal(6.3, resolution.Seat.X, 3);
        }

        [Fact]
        public void Rejects_a_clear_seat_that_has_no_leveled_surface_evidence_within_range()
        {
            // A seat can be clear of the footprint yet sit beyond any structure point — headlessly we cannot
            // validate its final surface, so it must be rejected (never forced onto base world height).
            var structure = new List<HostStructureFact> { new HostStructureFact(0.0, 0.0, 30.0) };
            // Only seat: 20 m away — clear of the 1.75 m keep-out but far outside the 6 m surface radius.
            var seats = Seats((20.0, 0.0));

            var resolution = HomesteadHeadlessSeatResolver.Resolve(
                seats, structure, hostCenterX: 0.0, hostCenterZ: 0.0, hostRadius: 4.0, Model);

            Assert.False(resolution.HasSeat);
            Assert.Equal(HeadlessSeatOutcome.AllSeatsRejected, resolution.Outcome);
        }

        [Fact]
        public void Resolves_final_y_from_the_lowest_nearby_leveled_surface_point()
        {
            // The chosen seat's Y comes from the lowest attributed structure base within the sample radius
            // (foundations sit on the leveled surface; min-Y biases to ground and never floats the Stone).
            var structure = new List<HostStructureFact>
            {
                new HostStructureFact(0.0, 0.0, 32.0),   // center foundation, leveled surface at 32
                new HostStructureFact(3.0, 0.0, 34.0),   // a wall higher up
                new HostStructureFact(5.0, 0.0, 31.5),   // near the seat, lower base
            };
            // Seat clear of footprint (>=1.75 m from all) with surface evidence within 6 m.
            var seats = Seats((5.0, 3.0));

            var resolution = HomesteadHeadlessSeatResolver.Resolve(
                seats, structure, hostCenterX: 0.0, hostCenterZ: 0.0, hostRadius: 4.0, Model);

            Assert.True(resolution.HasSeat);
            // Lowest structure Y within 6 m of (5,3): all three points are within range; min is 31.5.
            Assert.Equal(31.5, resolution.Seat.Y, 3);
        }

        [Fact]
        public void Chooses_the_best_valid_seat_by_clearance_and_yard_band_not_the_first()
        {
            // Two valid seats; the resolver must score all attempts and pick the higher-scoring one, proving
            // it is best-of-eight and not first-valid.
            var structure = new List<HostStructureFact>
            {
                new HostStructureFact(0.0, 0.0, 30.0),
                new HostStructureFact(6.5, 0.0, 30.0),
            };
            // Seat 0 at (2.9,0): 2.9 m clearance from center point, but only ~3.6 m from the (6.5,0) point.
            // Actually nearest for seat0 is center at 2.9 m. Seat 1 at (6.5,3): nearest is (6.5,0) at 3.0 m.
            // Seat 1 has more clearance (3.0 > 2.9) and sits in a readable yard band → should win.
            var seats = Seats((2.9, 0.0), (6.5, 3.0));

            var resolution = HomesteadHeadlessSeatResolver.Resolve(
                seats, structure, hostCenterX: 0.0, hostCenterZ: 0.0, hostRadius: 4.0, Model);

            Assert.True(resolution.HasSeat);
            Assert.Equal(1, resolution.Seat.Attempt);
        }

        [Fact]
        public void Model_rejects_a_surface_radius_smaller_than_the_keep_out()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HeadlessSeatModel(keepOut: 2.0, surfaceSampleRadius: 1.0));
        }
    }
}
