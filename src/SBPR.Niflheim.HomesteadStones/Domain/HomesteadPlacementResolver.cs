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

        /// <summary>Minimum clear distance from any host structure for a valid seat (production SeatKeepOut).</summary>
        internal const double SeatKeepOut = 1.75;

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

    /// <summary>One versioned manifest row for a generator host: the operator/client-observed seat for a
    /// specific world UID + selector version + host zone + host content hash. A row is only usable when ALL
    /// four keys match the live candidate (INV-5 version-drift guard) — otherwise it is treated as absent.</summary>
    internal sealed class HomesteadManifestRow
    {
        internal HomesteadManifestRow(
            string worldIdentity, string selectorVersion, string hostPrefab, int zoneX, int zoneZ,
            double seatX, double seatZ, double seatY, string contentHash)
        {
            WorldIdentity = worldIdentity ?? throw new ArgumentNullException(nameof(worldIdentity));
            SelectorVersion = selectorVersion ?? throw new ArgumentNullException(nameof(selectorVersion));
            HostPrefab = hostPrefab ?? throw new ArgumentNullException(nameof(hostPrefab));
            ContentHash = contentHash ?? throw new ArgumentNullException(nameof(contentHash));
            ZoneX = zoneX;
            ZoneZ = zoneZ;
            SeatX = seatX;
            SeatZ = seatZ;
            SeatY = seatY;
        }

        internal string WorldIdentity { get; }
        internal string SelectorVersion { get; }
        internal string HostPrefab { get; }
        internal int ZoneX { get; }
        internal int ZoneZ { get; }
        internal double SeatX { get; }
        internal double SeatZ { get; }
        internal double SeatY { get; }
        internal string ContentHash { get; }

        internal string Key => Compose(WorldIdentity, SelectorVersion, HostPrefab, ZoneX, ZoneZ);

        internal static string Compose(string worldIdentity, string selectorVersion, string hostPrefab, int zoneX, int zoneZ) =>
            string.Join("|", worldIdentity, selectorVersion, hostPrefab,
                zoneX.ToString(CultureInfo.InvariantCulture), zoneZ.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>A read-only, deterministic manifest for generator-host seats. Absence of a matching row is a
    /// first-class outcome (ManifestRequired), never a guess. Duplicate keys are rejected at construction so
    /// the manifest cannot silently disagree with itself.</summary>
    internal sealed class HomesteadGeneratorManifest
    {
        private readonly Dictionary<string, HomesteadManifestRow> rows;

        internal HomesteadGeneratorManifest(IEnumerable<HomesteadManifestRow> manifestRows)
        {
            if (manifestRows == null) throw new ArgumentNullException(nameof(manifestRows));
            rows = new Dictionary<string, HomesteadManifestRow>(StringComparer.Ordinal);
            foreach (var row in manifestRows)
            {
                if (rows.ContainsKey(row.Key))
                    throw new ArgumentException("Duplicate manifest key: " + row.Key, nameof(manifestRows));
                rows[row.Key] = row;
            }
        }

        internal static HomesteadGeneratorManifest Empty { get; } =
            new HomesteadGeneratorManifest(Array.Empty<HomesteadManifestRow>());

        internal int Count => rows.Count;

        /// <summary>Look up a manifest row that matches ALL of world + selector + host + zone. The caller must
        /// still verify the content hash against the live host (INV-5) before trusting the seat.</summary>
        internal bool TryGet(string worldIdentity, string selectorVersion, string hostPrefab, int zoneX, int zoneZ, out HomesteadManifestRow row) =>
            rows.TryGetValue(HomesteadManifestRow.Compose(worldIdentity, selectorVersion, hostPrefab, zoneX, zoneZ), out row!);
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
            if (!height(seatCandidate.X, seatCandidate.Z, out var seatY) ||
                double.IsNaN(seatY) || double.IsInfinity(seatY))
                return HomesteadResolution.Fail(HomesteadResolutionStatus.NoValidSeat,
                    $"{candidate.Prefab} ({candidate.ZoneX},{candidate.ZoneZ}) world-gen height unavailable at chosen seat.");

            var record = new ResolvedPlacementRecord(
                worldIdentity, selectorVersion, candidate.Prefab, candidate.ZoneX, candidate.ZoneZ,
                seatCandidate.X, seatCandidate.Z, seatY,
                evaluation.RadialDistance, evaluation.Clearance,
                HomesteadSeatProvider.StaticGeometry, geometry.SemanticHash, seatCandidate.Attempt);
            return HomesteadResolution.Ok(record);
        }

        /// <summary>Resolve a GENERATOR host from the versioned manifest ONLY. No matching+content-valid row
        /// ⇒ ManifestRequired (explicit skip). Never replays the DungeonGenerator, never guesses a ring here.</summary>
        internal static HomesteadResolution ResolveGenerator(
            string worldIdentity,
            string selectorVersion,
            HomesteadCandidate candidate,
            string liveContentHash,
            HomesteadGeneratorManifest manifest)
        {
            if (worldIdentity == null) throw new ArgumentNullException(nameof(worldIdentity));
            if (selectorVersion == null) throw new ArgumentNullException(nameof(selectorVersion));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (liveContentHash == null) throw new ArgumentNullException(nameof(liveContentHash));
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));

            if (!manifest.TryGet(worldIdentity, selectorVersion, candidate.Prefab, candidate.ZoneX, candidate.ZoneZ, out var row))
                return HomesteadResolution.Fail(HomesteadResolutionStatus.ManifestRequired,
                    $"{candidate.Prefab} ({candidate.ZoneX},{candidate.ZoneZ}) has no manifest row for world='{worldIdentity}' selector='{selectorVersion}'.");

            if (!string.Equals(row.ContentHash, liveContentHash, StringComparison.Ordinal))
                return HomesteadResolution.Fail(HomesteadResolutionStatus.ManifestRequired,
                    $"{candidate.Prefab} ({candidate.ZoneX},{candidate.ZoneZ}) manifest content hash mismatch (drift); row invalidated.");

            var radial = Math.Sqrt(((row.SeatX - candidate.X) * (row.SeatX - candidate.X)) +
                                   ((row.SeatZ - candidate.Z) * (row.SeatZ - candidate.Z)));
            var record = new ResolvedPlacementRecord(
                worldIdentity, selectorVersion, candidate.Prefab, candidate.ZoneX, candidate.ZoneZ,
                row.SeatX, row.SeatZ, row.SeatY, radial, double.NaN,
                HomesteadSeatProvider.Manifest, row.ContentHash, -1);
            return HomesteadResolution.Ok(record);
        }
    }
}
