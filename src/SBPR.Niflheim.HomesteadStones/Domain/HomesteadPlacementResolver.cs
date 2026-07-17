using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SBPR.Niflheim.HomesteadStones.Domain
{
    // ============================================================================
    // R5 — engine-free seat evaluation + resolution.
    //
    // Given a host candidate (prefab, zone, world XZ, yaw) and its static geometry,
    // this evaluates the 8 deterministic candidate seats analytically against the
    // host's static collider footprints — NO Physics, NO Heightmap. Terrain Y is
    // supplied by the caller via a pure height function (WorldGenerator.GetHeight on
    // the live server, or the offline port in tests), valid because every seat is
    // clamped to <= the flatten level radius (6.0 m) where the ground is leveled to
    // host-origin Y.
    //
    // Generator hosts (WoodFarm1 / WoodVillage1) never reach the geometry path: they
    // resolve ONLY through a versioned manifest, and fail explicitly (ManifestRequired)
    // when no matching row exists.
    // ============================================================================

    /// <summary>Pure world-generation height at a world XZ. On the live dedicated server this wraps
    /// <c>WorldGenerator.instance.GetHeight</c> (pure procedural noise — headless-safe, no Heightmap
    /// GameObject); in tests it wraps the offline WorldZones.WorldGen port. Returns false if the height
    /// is unavailable/non-finite so the caller can skip honestly rather than seat on garbage.</summary>
    internal delegate bool WorldHeightFunction(double worldX, double worldZ, out double height);

    /// <summary>Analytic seat evaluator over host-local static collider footprints. Everything is a pure
    /// function of the footprints + host pose; no engine calls. Rotation is applied to the seat, mapped
    /// into host-local space, so the footprints stay rotation-free.</summary>
    internal static class HomesteadStaticSeatEvaluator
    {
        /// <summary>Seats beyond this radius from the host origin are NOT guaranteed flat at host-origin Y
        /// (they fall in the smoothed annulus, 6.04–9.0 m), so R5 clamps/rejects them (SPIKE 2 INV-1).</summary>
        internal const double LevelRadius = 6.0;

        /// <summary>Minimum clear distance from any host structure for a valid seat (production SeatKeepOut).
        ///
        /// R6 (Blocker 2): with the CORRECTED extraction math (full transform matrices + mesh bounds), the
        /// densest house (WoodHouse2) has a true maximum clearance of ~1.355 m within the 6 m level radius,
        /// and WoodHouse1 ~1.765 m. The R5 keepout of 1.75 m was only satisfiable because the old extractor
        /// UNDERSTATED footprints (dropped rotation/scale/mesh). Honest geometry requires a keepout that all
        /// 13 hosts can satisfy: 1.25 m still clears the 0.5 m Stone disc with 0.75 m of margin beyond its
        /// edge — ample for a marker players walk up to — while keeping every house resolvable. The
        /// All_thirteen_ordinary_houses_resolve test pins this invariant against future geometry drift.</summary>
        internal const double SeatKeepOut = 1.25;

        /// <summary>Homestead Stone footprint radius; the seat disc must clear at least this much.</summary>
        internal const double StoneRadius = 0.5;

        /// <summary>Evaluate one candidate seat (world XZ) against the host's static footprints. Returns a
        /// <see cref="SeatEvaluation"/> whose Score ranks valid seats; invalid seats score -inf.</summary>
        internal static SeatEvaluation Evaluate(
            HomesteadCandidate host,
            HomesteadHostGeometry geometry,
            double hostYawRadians,
            SeatCandidate seat)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            var radial = Distance(seat.X, seat.Z, host.X, host.Z);
            if (radial > LevelRadius) return default;              // outside guaranteed-flat annulus
            if (geometry.Footprints.Count == 0) return default;    // no structure to hug → no seat

            // Map the world seat into HOST-LOCAL space: translate to host origin, then rotate by -yaw so
            // the axis-aligned local footprints can be tested directly (footprints carry no rotation).
            var dx = seat.X - host.X;
            var dz = seat.Z - host.Z;
            var cos = Math.Cos(-hostYawRadians);
            var sin = Math.Sin(-hostYawRadians);
            var localX = (dx * cos) - (dz * sin);
            var localZ = (dx * sin) + (dz * cos);

            // Clearance = shortest distance from the local seat to any footprint box edge; 0 if inside a box.
            var clearance = double.PositiveInfinity;
            var hostExtent = 0.0;
            foreach (var box in geometry.Footprints)
            {
                hostExtent = Math.Max(hostExtent, Math.Max(Math.Abs(box.LocalX) + box.HalfX, Math.Abs(box.LocalZ) + box.HalfZ));
                var ddx = Math.Max(0.0, Math.Abs(localX - box.LocalX) - box.HalfX);
                var ddz = Math.Max(0.0, Math.Abs(localZ - box.LocalZ) - box.HalfZ);
                var d = Math.Sqrt((ddx * ddx) + (ddz * ddz));
                if (d < clearance) clearance = d;
            }
            if (double.IsInfinity(clearance)) return default;

            var valid = clearance >= SeatKeepOut;
            return new SeatEvaluation(valid, clearance, radial, hostExtent);
        }

        private static double Distance(double ax, double az, double bx, double bz)
        {
            var dx = ax - bx;
            var dz = az - bz;
            return Math.Sqrt((dx * dx) + (dz * dz));
        }

        /// <summary>Deterministically enumerate candidate seats over a fixed polar lattice inside the level
        /// radius. Unlike a blind random ring (which misses the sparse clear discs a cluttered house leaves
        /// near its edge), this covers the reachable annulus at a fixed resolution, so the best-scoring clear
        /// seat is found reproducibly. Pure function of (host, ring/step counts) — no engine, no RNG.</summary>
        internal static IReadOnlyList<SeatCandidate> LatticeSeats(HomesteadCandidate host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var seats = new List<SeatCandidate>();
            var attempt = 0;
            // Rings from just inside the keep-out floor out to the level radius, at 0.25 m radial resolution
            // (matches the SPIKE-2 prototype occupancy grid cell). Angular resolution scales with radius to
            // keep a roughly constant ~0.25 m arc spacing, so sparse clear discs near the cluttered edge of a
            // large house are not skipped between samples. Pure function — no engine, no RNG.
            const double ArcSpacing = 0.25;
            for (var r = StoneRadius; r <= LevelRadius + 1e-9; r += 0.25)
            {
                var steps = Math.Max(24, (int)Math.Ceiling((2.0 * Math.PI * r) / ArcSpacing));
                for (var a = 0; a < steps; a++)
                {
                    var angle = a * (2.0 * Math.PI / steps);
                    seats.Add(new SeatCandidate(
                        attempt++,
                        host.X + (Math.Cos(angle) * r),
                        host.Z + (Math.Sin(angle) * r)));
                }
            }
            return seats;
        }
    }

    /// <summary>The engine-free resolver that turns a selected host candidate into a
    /// <see cref="ResolvedPlacementRecord"/> or an explicit non-success status. This is the single seam the
    /// net48 adapter calls per selected, loaded host; every branch is unit-tested headless.</summary>
    internal static class HomesteadPlacementResolver
    {
        /// <summary>Resolve an ORDINARY host from its static geometry. Terrain Y comes from <paramref name="height"/>
        /// at the chosen seat XZ (guaranteed == host-origin Y because the seat is clamped to the level radius).</summary>
        internal static HomesteadResolution ResolveOrdinary(
            string worldIdentity,
            string selectorVersion,
            HomesteadCandidate candidate,
            HomesteadHostGeometry geometry,
            double hostYawRadians,
            WorldHeightFunction height)
        {
            if (worldIdentity == null) throw new ArgumentNullException(nameof(worldIdentity));
            if (selectorVersion == null) throw new ArgumentNullException(nameof(selectorVersion));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (height == null) throw new ArgumentNullException(nameof(height));

            if (geometry.Footprints.Count == 0)
                return HomesteadResolution.Fail(HomesteadResolutionStatus.GeometryUnavailable,
                    $"{candidate.Prefab} ({candidate.ZoneX},{candidate.ZoneZ}) exposed zero static colliders.");

            var seats = HomesteadStaticSeatEvaluator.LatticeSeats(candidate);
            var choice = HomesteadSeatGenerator.ChooseBest(
                seats, seat => HomesteadStaticSeatEvaluator.Evaluate(candidate, geometry, hostYawRadians, seat));
            if (!choice.HasSeat)
                return HomesteadResolution.Fail(HomesteadResolutionStatus.NoValidSeat,
                    $"{candidate.Prefab} ({candidate.ZoneX},{candidate.ZoneZ}) had no valid static-geometry seat " +
                    $"within {HomesteadStaticSeatEvaluator.LevelRadius:0.0}m after {choice.AttemptsEvaluated} attempts.");

            var seatCandidate = choice.Seat;
            var evaluation = HomesteadStaticSeatEvaluator.Evaluate(candidate, geometry, hostYawRadians, seatCandidate);
            // Terrain Y is the flattened HOST-ORIGIN height, NOT the seat XZ height. SPIKE 2 proved the
            // location's `flatten` TerrainModifier levels the ground to host-origin Y within the level
            // radius (<=6.0 m); every seat is clamped inside that radius (INV-1), so the seat sits on that
            // same flattened plane. Sampling WorldGenerator.GetHeight at the seat XZ instead would read the
            // pre-flatten procedural noise under the seat and float/sink the Stone off the leveled pad.
            if (!height(candidate.X, candidate.Z, out var seatY) ||
                double.IsNaN(seatY) || double.IsInfinity(seatY))
                return HomesteadResolution.Fail(HomesteadResolutionStatus.NoValidSeat,
                    $"{candidate.Prefab} ({candidate.ZoneX},{candidate.ZoneZ}) world-gen height unavailable at host origin.");

            var record = new ResolvedPlacementRecord(
                worldIdentity, selectorVersion, candidate.Prefab, candidate.ZoneX, candidate.ZoneZ,
                seatCandidate.X, seatCandidate.Z, seatY,
                evaluation.RadialDistance, evaluation.Clearance,
                HomesteadSeatProvider.StaticGeometry, geometry.SemanticHash, seatCandidate.Attempt);
            return HomesteadResolution.Ok(record);
        }

        /// <summary>R6 (Blocker 6) — resolve a GENERATOR host from the validated OPERATIONAL manifest. Returns
        /// a record whose <see cref="ResolvedPlacementRecord.ContentHash"/> is the manifest DOCUMENT DIGEST
        /// (provenance stamped onto the Stone ZDO) and whose <paramref name="generation"/> the caller records
        /// against the ledger so ManifestRequired stays retryable when a newer generation appears. No matching
        /// row ⇒ ManifestRequired; never a runtime geometry guess, never a player-submittable row.</summary>
        internal static HomesteadResolution ResolveGeneratorOperational(
            string worldIdentity,
            string selectorVersion,
            HomesteadCandidate candidate,
            HomesteadOperationalManifest manifest)
        {
            if (worldIdentity == null) throw new ArgumentNullException(nameof(worldIdentity));
            if (selectorVersion == null) throw new ArgumentNullException(nameof(selectorVersion));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            if (!manifest.TryGet(candidate.Prefab, candidate.ZoneX, candidate.ZoneZ, out var row))
                return HomesteadResolution.Fail(HomesteadResolutionStatus.ManifestRequired,
                    $"{candidate.Prefab} ({candidate.ZoneX},{candidate.ZoneZ}) has no operational manifest row " +
                    $"for world='{worldIdentity}' selector='{selectorVersion}' generation={manifest.Generation}.");

            var radial = Math.Sqrt(((row.SeatX - candidate.X) * (row.SeatX - candidate.X)) +
                                   ((row.SeatZ - candidate.Z) * (row.SeatZ - candidate.Z)));
            var record = new ResolvedPlacementRecord(
                worldIdentity, selectorVersion, candidate.Prefab, candidate.ZoneX, candidate.ZoneZ,
                row.SeatX, row.SeatZ, row.SeatY, radial, double.NaN,
                HomesteadSeatProvider.Manifest, manifest.DocumentDigest, -1);
            return HomesteadResolution.Ok(record);
        }
    }
}
