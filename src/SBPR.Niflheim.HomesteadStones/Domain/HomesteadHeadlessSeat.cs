using System;
using System.Collections.Generic;

namespace SBPR.Niflheim.HomesteadStones.Domain
{
    // FIX R2 (headless seating safety contract) — engine-free, server-authoritative seat resolution for a
    // dedicated server, where the peer zone is NEVER scene-instantiated (no live Heightmap, no vanilla Piece
    // colliders around it). The prior R1 fallback picked seats[0] blindly with base WorldGenerator.GetHeight;
    // review rejected that as silently weakening §5's contract (all 8 attempts, host footprint/clearance, a
    // valid final surface, honest 8-of-8 skip).
    //
    // The authoritative headless seam is the location's OWN persisted structure ZDOs. On a dedicated server a
    // joined peer's location zone is realized via CreateGhostZones -> SpawnZone(SpawnMode.Ghost), which spawns
    // and persists the host's structure ZDOs (decompiled vanilla ZoneSystem.SpawnLocation, base-game RE per
    // ADR-0001). Those ZDOs carry the REAL world positions the host was built at, including the Y that the
    // location's own TerrainModifier/TerrainComp leveling produced — exactly the "final terrain surface
    // including location/terrain modifications" the review demanded, and the same set the live collider path
    // attributes as host structure. Harvesting them (creator == 0, inside the location radius) gives a
    // data-only equivalent of the live footprint/clearance/surface evaluation, with no scene realization.
    //
    // Contract preserved headlessly:
    //   * ALL eight deterministic seats are evaluated (never first-seat).
    //   * A conservative structural clearance model rejects seats whose keep-out radius overlaps any attributed
    //     host structure point (footprint proxy) or that fall short of the required clearance.
    //   * The final surface Y is resolved from nearby attributed structure Y (leveled-surface evidence), not
    //     base world height. A seat with no local surface evidence is invalid, not forced.
    //   * If the host is placed but NO attributed structure evidence exists yet (ghost spawn not persisted),
    //     creation is DEFERRED (revisited next pass) rather than guessed.
    //   * If evidence exists but all eight seats fail, the zone is SKIPPED with an honest reason.
    //
    // net48 audit: System + collections only. Link-compiles into the net8 test project.

    /// <summary>A single attributed host-structure fact harvested from a persisted structure ZDO: its world
    /// XZ and the Y the host was actually built/leveled at. Y is authoritative leveled-surface evidence.</summary>
    public readonly struct HostStructureFact
    {
        public HostStructureFact(double x, double z, double y)
        {
            X = x;
            Z = z;
            Y = y;
        }

        public double X { get; }
        public double Z { get; }
        public double Y { get; }
    }

    /// <summary>Why a headless seat resolution did not produce a seat. Stable ordinal — surfaced only as a
    /// diagnostic reason, never persisted as domain state.</summary>
    public enum HeadlessSeatOutcome
    {
        /// <summary>A valid seat was chosen from server-authoritative structure evidence.</summary>
        Resolved = 0,

        /// <summary>The host location is placed but no attributed structure ZDOs are persisted near it yet
        /// (ghost spawn not yet flushed). Creation is DEFERRED and revisited, not forced.</summary>
        NoStructureEvidence = 1,

        /// <summary>Structure evidence exists, but every one of the eight deterministic seats was rejected —
        /// footprint overlap, insufficient clearance, or no local surface evidence to validate the final Y.
        /// The zone is skipped with an honest 8-of-8 result rather than forcing a dubious seat.</summary>
        AllSeatsRejected = 2,
    }

    /// <summary>Conservative, data-only seat resolution parameters. Distances are metres.</summary>
    public sealed class HeadlessSeatModel
    {
        public HeadlessSeatModel(double keepOut, double surfaceSampleRadius)
        {
            if (keepOut <= 0.0) throw new ArgumentOutOfRangeException(nameof(keepOut));
            if (surfaceSampleRadius < keepOut)
                throw new ArgumentOutOfRangeException(nameof(surfaceSampleRadius),
                    "Surface sample radius must be at least the keep-out radius so a footprint-clear seat can still anchor to nearby structure surface evidence.");
            KeepOut = keepOut;
            SurfaceSampleRadius = surfaceSampleRadius;
        }

        /// <summary>Minimum horizontal distance from a seat to the nearest attributed host structure point.
        /// Doubles as the footprint keep-out (a seat closer than this is treated as inside the footprint).</summary>
        public double KeepOut { get; }

        /// <summary>Radius within which nearby attributed structure Y is treated as valid leveled-surface
        /// evidence for a seat. A seat with no structure point inside this radius cannot have its final
        /// surface validated headlessly and is rejected (never forced onto base world height).</summary>
        public double SurfaceSampleRadius { get; }
    }

    /// <summary>A resolved headless seat: horizontal position from the deterministic generator, and a final Y
    /// validated from persisted host-structure leveled-surface evidence.</summary>
    public readonly struct HeadlessSeat
    {
        public HeadlessSeat(int attempt, double x, double z, double y)
        {
            Attempt = attempt;
            X = x;
            Z = z;
            Y = y;
        }

        public int Attempt { get; }
        public double X { get; }
        public double Z { get; }
        public double Y { get; }
    }

    /// <summary>Outcome of resolving a headless seat over all eight deterministic attempts.</summary>
    public readonly struct HeadlessSeatResolution
    {
        public HeadlessSeatResolution(HeadlessSeatOutcome outcome, HeadlessSeat seat, int attemptsEvaluated)
        {
            Outcome = outcome;
            Seat = seat;
            AttemptsEvaluated = attemptsEvaluated;
        }

        public HeadlessSeatOutcome Outcome { get; }
        public HeadlessSeat Seat { get; }
        public int AttemptsEvaluated { get; }
        public bool HasSeat => Outcome == HeadlessSeatOutcome.Resolved;
    }

    /// <summary>Pure, server-authoritative seat resolver. Evaluates ALL eight deterministic seats against
    /// harvested host-structure facts and either returns the best valid seat or an honest skip/defer reason.</summary>
    public static class HomesteadHeadlessSeatResolver
    {
        public static HeadlessSeatResolution Resolve(
            IReadOnlyList<SeatFact> seats,
            IReadOnlyList<HostStructureFact> hostStructure,
            double hostCenterX,
            double hostCenterZ,
            double hostRadius,
            HeadlessSeatModel model)
        {
            if (seats == null) throw new ArgumentNullException(nameof(seats));
            if (hostStructure == null) throw new ArgumentNullException(nameof(hostStructure));
            if (model == null) throw new ArgumentNullException(nameof(model));

            // No persisted structure evidence yet: the host location is placed but its ghost-spawned structure
            // ZDOs have not flushed. We CANNOT know the footprint or the leveled surface, so we defer rather
            // than guess (§5a / review blocker 1).
            if (hostStructure.Count == 0)
                return new HeadlessSeatResolution(HeadlessSeatOutcome.NoStructureEvidence, default, seats.Count);

            var found = false;
            var best = default(HeadlessSeat);
            var bestScore = double.NegativeInfinity;

            // Evaluate every attempt — never short-circuit to the first seat.
            foreach (var seat in seats)
            {
                if (!TryEvaluate(seat, hostStructure, hostCenterX, hostCenterZ, hostRadius, model,
                        out var surfaceY, out var clearance, out var radialDistance))
                    continue;

                var score = Score(clearance, radialDistance, hostRadius);
                if (double.IsNegativeInfinity(score)) continue;
                if (!found || score > bestScore || (score.Equals(bestScore) && seat.Attempt < best.Attempt))
                {
                    found = true;
                    best = new HeadlessSeat(seat.Attempt, seat.X, seat.Z, surfaceY);
                    bestScore = score;
                }
            }

            return found
                ? new HeadlessSeatResolution(HeadlessSeatOutcome.Resolved, best, seats.Count)
                : new HeadlessSeatResolution(HeadlessSeatOutcome.AllSeatsRejected, default, seats.Count);
        }

        private static bool TryEvaluate(
            SeatFact seat,
            IReadOnlyList<HostStructureFact> hostStructure,
            double hostCenterX,
            double hostCenterZ,
            double hostRadius,
            HeadlessSeatModel model,
            out double surfaceY,
            out double clearance,
            out double radialDistance)
        {
            surfaceY = 0.0;
            clearance = double.PositiveInfinity;
            radialDistance = Distance(seat.X, seat.Z, hostCenterX, hostCenterZ);

            var haveSurface = false;
            var surfaceMinY = double.PositiveInfinity;
            var nearestDistance = double.PositiveInfinity;

            foreach (var fact in hostStructure)
            {
                var distance = Distance(seat.X, seat.Z, fact.X, fact.Z);
                if (distance < nearestDistance) nearestDistance = distance;
                if (distance <= model.SurfaceSampleRadius)
                {
                    haveSurface = true;
                    // Conservative ground: the lowest attributed structure base within the sample radius. Host
                    // foundations sit on the leveled surface; walls/roofs are above it, so min-Y biases toward
                    // ground and never floats the Stone above terrain.
                    if (fact.Y < surfaceMinY) surfaceMinY = fact.Y;
                }
            }

            clearance = nearestDistance;

            // Footprint proxy: a seat inside the keep-out radius of ANY attributed host structure point is
            // treated as overlapping the host footprint and rejected.
            if (nearestDistance < model.KeepOut) return false;

            // No leveled-surface evidence within range: we cannot validate the final Y headlessly, so this
            // seat is invalid (we never fall back to base world height for it).
            if (!haveSurface) return false;

            surfaceY = surfaceMinY;
            return true;
        }

        /// <summary>Mirror of the live <c>SeatEvaluation.Score</c> shape: reward clearance and a readable yard
        /// band around the host radius, penalize seats that drift far out. Negative infinity = invalid.</summary>
        private static double Score(double clearance, double radialDistance, double hostRadius)
        {
            if (clearance < 1.75) return double.NegativeInfinity;
            var yardBand = Math.Max(0.0, Math.Min(1.0, 1.0 - (Math.Abs(radialDistance - (hostRadius + 2.5)) / 5.0)));
            return 100.0 + (clearance * 4.0) + (yardBand * 8.0) - (Math.Max(0.0, radialDistance - 12.0) * 2.0);
        }

        private static double Distance(double ax, double az, double bx, double bz)
        {
            var dx = ax - bx;
            var dz = az - bz;
            return Math.Sqrt((dx * dx) + (dz * dz));
        }
    }

    /// <summary>A deterministic seat reduced to the XZ facts the pure headless resolver needs (decoupled from
    /// the engine <c>SeatCandidate</c> so tests build seats directly).</summary>
    public readonly struct SeatFact
    {
        public SeatFact(int attempt, double x, double z)
        {
            Attempt = attempt;
            X = x;
            Z = z;
        }

        public int Attempt { get; }
        public double X { get; }
        public double Z { get; }
    }
}
