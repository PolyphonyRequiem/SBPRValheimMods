using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Domain
{
    // ============================================================================
    // R5 — engine-free Homestead seat authority from STATIC host geometry.
    //
    // SPIKE 1 (t_24a5d20d) proved a headless dedicated server has NO live host
    // colliders and NO built Heightmap in its physics scene, so any Physics.Overlap /
    // Heightmap.GetHeight seat scorer is doomed (allColliders48m=0, groundHit=False)
    // regardless of where the realization barrier is scoped.
    //
    // SPIKE 2 (t_fd1f1698) proved the seat can instead be derived WITHOUT a physics
    // scene, split by host class:
    //   * ORDINARY WoodHouse1..13 (104/114): read the host prefab's authored static
    //     Collider footprints directly (they ship in the AssetBundle; the headless
    //     server just never instantiates them into a physics scene). Transform by the
    //     location instance pose/rotation, evaluate all 8 candidate seats analytically.
    //   * GENERATOR WoodFarm1 / WoodVillage1 (10/114): 0 static colliders + a
    //     terrain-gated CampRadial DungeonGenerator layout that is NOT reproducible
    //     offline from seed alone. These route EXCLUSIVELY through a versioned manifest.
    //
    // Terrain Y comes from the world-gen height function at the seat XZ, valid ONLY
    // inside the location's `flatten` TerrainModifier level radius (6.04 m), where the
    // ground is leveled flat to host-origin Y. Every candidate seat ring is therefore
    // clamped to <= 6.0 m.
    //
    // This file is engine-free (System + value objects only): NO UnityEngine, NO
    // Physics, NO Heightmap, NO Valheim. It link-compiles into the net8 test project
    // exactly like HomesteadPlacement.cs, so every seam below is unit-tested headless.
    // The net48 Unity adapter's ONLY job is to read live Collider components + call the
    // world-gen height function, then hand the engine-free record to persistence.
    // ============================================================================

    /// <summary>The two host classes SPIKE 2 split the eligible set into.</summary>
    internal enum HomesteadHostClass
    {
        /// <summary>WoodHouse1..13 — full static collider hierarchy present in the prefab.</summary>
        Ordinary,

        /// <summary>WoodFarm1 / WoodVillage1 — DungeonGenerator (CampRadial) hosts with zero
        /// static colliders and a terrain-gated runtime layout; manifest-only.</summary>
        Generator,
    }

    /// <summary>How a resolved seat was authored — the provenance that survives into the ZDO
    /// and the durable ledger, so a reviewer can tell a computed seat from a manifest one.</summary>
    internal enum HomesteadSeatProvider
    {
        /// <summary>Ordinary host: seat computed from static collider footprints (SPIKE 2 Approach A).</summary>
        StaticGeometry,

        /// <summary>Generator host: seat taken verbatim from a versioned manifest row (Approach C).</summary>
        Manifest,
    }

    /// <summary>Classifies an eligible host prefab into its SPIKE-2 host class. The eligible set
    /// itself is owned by the caller; this only says which resolution path a host must take.</summary>
    internal static class HomesteadHostClassifier
    {
        private static readonly HashSet<string> GeneratorHosts =
            new HashSet<string>(new[] { "WoodFarm1", "WoodVillage1" }, StringComparer.Ordinal);

        internal static HomesteadHostClass Classify(string prefab)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            return GeneratorHosts.Contains(prefab) ? HomesteadHostClass.Generator : HomesteadHostClass.Ordinary;
        }

        internal static bool IsGenerator(string prefab) => Classify(prefab) == HomesteadHostClass.Generator;
    }

    /// <summary>One host-local axis-aligned XZ collider footprint: center + half-extents, expressed
    /// relative to the host origin with NO host rotation applied (per-node local rotation is folded
    /// into a conservative AABB by the extractor, matching probe_house_slots_v2 / SPIKE 2).</summary>
    internal readonly struct StaticColliderFootprint : IEquatable<StaticColliderFootprint>
    {
        internal StaticColliderFootprint(double localX, double localZ, double halfX, double halfZ)
        {
            if (halfX < 0.0) throw new ArgumentOutOfRangeException(nameof(halfX));
            if (halfZ < 0.0) throw new ArgumentOutOfRangeException(nameof(halfZ));
            LocalX = localX;
            LocalZ = localZ;
            HalfX = halfX;
            HalfZ = halfZ;
        }

        /// <summary>Host-local X of the box center (host origin = 0).</summary>
        internal double LocalX { get; }

        /// <summary>Host-local Z of the box center (host origin = 0).</summary>
        internal double LocalZ { get; }

        internal double HalfX { get; }
        internal double HalfZ { get; }

        public bool Equals(StaticColliderFootprint other) =>
            LocalX.Equals(other.LocalX) && LocalZ.Equals(other.LocalZ) &&
            HalfX.Equals(other.HalfX) && HalfZ.Equals(other.HalfZ);

        public override bool Equals(object? obj) => obj is StaticColliderFootprint other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = LocalX.GetHashCode();
                hash = (hash * 397) ^ LocalZ.GetHashCode();
                hash = (hash * 397) ^ HalfX.GetHashCode();
                return (hash * 397) ^ HalfZ.GetHashCode();
            }
        }
    }

    /// <summary>The static footprint of one ordinary host prefab: the load-bearing collider set plus a
    /// stable semantic hash that pins the geometry against silent AssetBundle drift. Rotation is applied
    /// analytically at resolve time; the footprints themselves are host-local and rotation-free.</summary>
    internal sealed class HomesteadHostGeometry
    {
        internal HomesteadHostGeometry(string prefab, IReadOnlyList<StaticColliderFootprint> footprints, string semanticHash)
        {
            Prefab = prefab ?? throw new ArgumentNullException(nameof(prefab));
            Footprints = footprints ?? throw new ArgumentNullException(nameof(footprints));
            SemanticHash = semanticHash ?? throw new ArgumentNullException(nameof(semanticHash));
        }

        internal string Prefab { get; }
        internal IReadOnlyList<StaticColliderFootprint> Footprints { get; }

        /// <summary>Order-independent SHA-256 of the footprint set (see HomesteadGeometryHash). A mismatch
        /// against the pinned fixture means the shipped prefab changed and the selector version must roll.</summary>
        internal string SemanticHash { get; }

        internal int ColliderCount => Footprints.Count;
    }

    /// <summary>Stable, order-independent hash of a static footprint set. Matches the offline extractor
    /// (extract_homestead_geometry.py) so the shipped fixture and the runtime read agree byte-for-byte.</summary>
    internal static class HomesteadGeometryHash
    {
        internal static string Compute(IEnumerable<StaticColliderFootprint> footprints)
        {
            if (footprints == null) throw new ArgumentNullException(nameof(footprints));
            var rows = footprints
                .Select(f => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:0.0000}|{1:0.0000}|{2:0.0000}|{3:0.0000}",
                    f.LocalX, f.LocalZ, f.HalfX, f.HalfZ))
                .OrderBy(row => row, StringComparer.Ordinal);
            return StableHash.Hex(string.Join("\n", rows));
        }
    }

    /// <summary>The versioned, engine-free output of a successful seat resolution — the single record both
    /// the static-geometry and manifest providers normalize to, and the single shape persistence consumes.
    /// Carries complete world/selector/host/zone/provenance metadata so nothing downstream re-guesses.</summary>
    internal sealed class ResolvedPlacementRecord : IEquatable<ResolvedPlacementRecord>
    {
        internal ResolvedPlacementRecord(
            string worldIdentity,
            string selectorVersion,
            string hostPrefab,
            int zoneX,
            int zoneZ,
            double seatX,
            double seatZ,
            double seatY,
            double radialFromHost,
            double clearance,
            HomesteadSeatProvider provider,
            string contentHash,
            int attempt)
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
            RadialFromHost = radialFromHost;
            Clearance = clearance;
            Provider = provider;
            Attempt = attempt;
        }

        internal string WorldIdentity { get; }
        internal string SelectorVersion { get; }
        internal string HostPrefab { get; }
        internal int ZoneX { get; }
        internal int ZoneZ { get; }
        internal double SeatX { get; }
        internal double SeatZ { get; }
        internal double SeatY { get; }
        internal double RadialFromHost { get; }
        internal double Clearance { get; }
        internal HomesteadSeatProvider Provider { get; }

        /// <summary>Provider content hash — the host geometry semantic hash for StaticGeometry, or the
        /// manifest row content hash for Manifest. Version-drift guard (INV-5): a mismatch invalidates reuse.</summary>
        internal string ContentHash { get; }
        internal int Attempt { get; }

        public bool Equals(ResolvedPlacementRecord? other) =>
            other != null &&
            WorldIdentity == other.WorldIdentity && SelectorVersion == other.SelectorVersion &&
            HostPrefab == other.HostPrefab && ZoneX == other.ZoneX && ZoneZ == other.ZoneZ &&
            SeatX.Equals(other.SeatX) && SeatZ.Equals(other.SeatZ) && SeatY.Equals(other.SeatY) &&
            RadialFromHost.Equals(other.RadialFromHost) && Clearance.Equals(other.Clearance) &&
            Provider == other.Provider && ContentHash == other.ContentHash && Attempt == other.Attempt;

        public override bool Equals(object? obj) => Equals(obj as ResolvedPlacementRecord);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = WorldIdentity.GetHashCode();
                hash = (hash * 397) ^ HostPrefab.GetHashCode();
                hash = (hash * 397) ^ ZoneX;
                hash = (hash * 397) ^ ZoneZ;
                hash = (hash * 397) ^ SeatX.GetHashCode();
                hash = (hash * 397) ^ SeatZ.GetHashCode();
                hash = (hash * 397) ^ (int)Provider;
                return hash;
            }
        }
    }

    /// <summary>Why a resolution produced no persistable record. Every non-success outcome is explicit and
    /// captured (acceptance: exception provenance, no phantom retries, honest terminal markers).</summary>
    internal enum HomesteadResolutionStatus
    {
        Resolved,

        /// <summary>Ordinary host but no candidate seat passed validity/clearance within the level radius.</summary>
        NoValidSeat,

        /// <summary>Generator host with no matching manifest row: skip explicitly, never guess.</summary>
        ManifestRequired,

        /// <summary>Ordinary host whose live geometry read returned zero footprints (should not happen on a
        /// well-formed prefab; treated as a terminal, non-retryable data fault, not a transient miss).</summary>
        GeometryUnavailable,
    }

    /// <summary>The engine-free result of one resolution attempt: either a record or an explicit status.</summary>
    internal readonly struct HomesteadResolution
    {
        private HomesteadResolution(HomesteadResolutionStatus status, ResolvedPlacementRecord? record, string detail)
        {
            Status = status;
            Record = record;
            Detail = detail ?? string.Empty;
        }

        internal HomesteadResolutionStatus Status { get; }
        internal ResolvedPlacementRecord? Record { get; }
        internal string Detail { get; }
        internal bool IsResolved => Status == HomesteadResolutionStatus.Resolved && Record != null;

        internal static HomesteadResolution Ok(ResolvedPlacementRecord record) =>
            new HomesteadResolution(HomesteadResolutionStatus.Resolved, record, string.Empty);

        internal static HomesteadResolution Fail(HomesteadResolutionStatus status, string detail) =>
            new HomesteadResolution(status, null, detail);
    }
}
