using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SBPR.Niflheim.HomesteadStones.Domain
{
    // ============================================================================
    // R6 (Blocker 1) — the STATIC GEOMETRY CATALOG is the production authority.
    //
    // R5 discovered live LocationProxy child hierarchies at runtime to read a host's
    // colliders. On a headless dedicated server those child GameObjects may not be
    // instantiated, and "nearest live proxy" is a guess. R6 replaces that entirely:
    // the production ordinary-host footprint comes from a CHECKED-IN, offline-generated
    // catalog keyed by exact prefab + semantic content hash. The runtime only supplies
    // the host's realized transform/rotation (from authoritative location/proxy ZDO
    // data) and the terrain height; the geometry itself is authored data, not a live read.
    //
    // The catalog is engine-free: it parses the same JSON fixture the tests validate and
    // the offline extractor emits. Production loads that fixture as an embedded resource
    // and VERIFIES each host's stored semantic hash equals the recomputed hash at startup
    // (a pin). A mismatch means the shipped AssetBundle or extractor drifted and the
    // catalog must be regenerated + the selector version rolled — it never silently
    // reseats against changed geometry.
    //
    // Missing identity / missing catalog entry is RETRYABLE (CatalogUnavailable), never a
    // terminal GeometryUnavailable: a host whose catalog row is temporarily unavailable
    // must be re-attempted, not permanently abandoned.
    // ============================================================================

    /// <summary>Parsed, hash-pinned static geometry catalog for the ordinary Homestead hosts. Engine-free:
    /// the same JSON the offline extractor emits and the tests validate. Production loads it from an embedded
    /// resource; tests load it from the checked-in fixture. Either way the semantic-hash pin is enforced.</summary>
    internal sealed class HomesteadStaticGeometryCatalog
    {
        private readonly Dictionary<string, HomesteadHostGeometry> byPrefab;

        private HomesteadStaticGeometryCatalog(Dictionary<string, HomesteadHostGeometry> hosts, string schema, string catalogDigest)
        {
            byPrefab = hosts;
            Schema = schema;
            CatalogDigest = catalogDigest;
        }

        internal string Schema { get; }

        /// <summary>Order-independent digest of every host's (prefab, semanticHash) pair. Production verifies
        /// this at startup and can stamp it so a reviewer can prove which catalog a build shipped.</summary>
        internal string CatalogDigest { get; }

        internal int HostCount => byPrefab.Count;
        internal IReadOnlyCollection<string> Prefabs => byPrefab.Keys.ToList();

        /// <summary>Look up a host's authored static footprints by exact prefab name. Returns false when the
        /// host is absent from the catalog (the caller treats this as retryable CatalogUnavailable, not a
        /// terminal geometry fault).</summary>
        internal bool TryGet(string prefab, out HomesteadHostGeometry geometry) =>
            byPrefab.TryGetValue(prefab, out geometry!);

        /// <summary>Build a catalog from already-parsed host rows, enforcing the semantic-hash pin: the stored
        /// hash for each host MUST equal the hash recomputed from its footprints via the ONE canonical
        /// <see cref="HomesteadGeometryHash"/> schema. Any mismatch throws — a drifted catalog must not load.</summary>
        internal static HomesteadStaticGeometryCatalog FromHostRows(
            string schema, IEnumerable<HomesteadCatalogHostRow> hostRows)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            if (hostRows == null) throw new ArgumentNullException(nameof(hostRows));

            var hosts = new Dictionary<string, HomesteadHostGeometry>(StringComparer.Ordinal);
            foreach (var row in hostRows)
            {
                var recomputed = HomesteadGeometryHash.Compute(row.Footprints);
                if (!string.Equals(recomputed, row.StoredSemanticHash, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Catalog hash pin FAILED for '{row.Prefab}': stored={row.StoredSemanticHash} recomputed={recomputed}. " +
                        "The shipped geometry drifted from the catalog; regenerate the catalog and roll the selector version.");
                // The geometry's own SemanticHash is the canonical recomputed value (== stored, just verified).
                hosts[row.Prefab] = new HomesteadHostGeometry(row.Prefab, row.Footprints, recomputed);
            }

            var digest = StableHash.Hex(hosts
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + pair.Value.SemanticHash)
                .ToArray());
            return new HomesteadStaticGeometryCatalog(hosts, schema, digest);
        }

        internal static HomesteadStaticGeometryCatalog Empty { get; } =
            new HomesteadStaticGeometryCatalog(
                new Dictionary<string, HomesteadHostGeometry>(StringComparer.Ordinal), string.Empty, string.Empty);
    }

    /// <summary>One raw catalog host row as parsed from the JSON fixture: prefab, its footprints, and the
    /// STORED semantic hash the extractor wrote. The catalog verifies stored == recomputed at load.</summary>
    internal sealed class HomesteadCatalogHostRow
    {
        internal HomesteadCatalogHostRow(string prefab, IReadOnlyList<StaticColliderFootprint> footprints, string storedSemanticHash)
        {
            Prefab = prefab ?? throw new ArgumentNullException(nameof(prefab));
            Footprints = footprints ?? throw new ArgumentNullException(nameof(footprints));
            StoredSemanticHash = storedSemanticHash ?? throw new ArgumentNullException(nameof(storedSemanticHash));
        }

        internal string Prefab { get; }
        internal IReadOnlyList<StaticColliderFootprint> Footprints { get; }
        internal string StoredSemanticHash { get; }
    }
}
