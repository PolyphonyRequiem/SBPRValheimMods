using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SBPR.Niflheim.HomesteadStones.Domain
{
    // ============================================================================
    // R6 (Blocker 6) — operational manifest provider for GENERATOR hosts.
    //
    // The 10/114 generator hosts (WoodFarm1 / WoodVillage1) have no offline-reproducible
    // static geometry, so their seats come from a TRUSTED, operator-supplied manifest —
    // never a runtime geometry guess and never a row an ordinary player can submit.
    //
    // This file is the engine-free parser/validator/document. The net48 adapter
    // (HomesteadManifestStore) loads the raw text from a configured file and hands it here.
    //
    // A manifest carries a monotonically increasing GENERATION. `ManifestRequired` is
    // recorded against the generation that was current when it was decided; when a NEW
    // generation with a valid matching row appears, the resolver is allowed to retry
    // (the ledger's ManifestRequired terminal is generation-scoped, not permanent).
    //
    // Validation rejects malformed / non-finite / out-of-bounds / stale rows and requires:
    //   * exact world UID + selector version (whole-document scope keys)
    //   * provider version + document content digest (provenance stamped onto the Stone ZDO)
    //   * per-row host prefab + zone + finite seat coordinates within the zone/host bounds
    //   * a complete selected-set: every row's (prefab,zone) is unique
    // ============================================================================

    /// <summary>Why a candidate manifest row or document was rejected — surfaced to operators verbatim.</summary>
    internal enum HomesteadManifestRejection
    {
        None,
        MalformedRow,
        NonFiniteCoordinate,
        OutOfZoneBounds,
        OutOfHostBounds,
        DuplicateRow,
        WorldMismatch,
        SelectorMismatch,
        MissingProvenance,
        StaleGeneration,
    }

    /// <summary>One validated manifest row, ready to resolve a generator host seat. Carries the document's
    /// provider version + generation + digest so the resolver can stamp provenance onto the Stone ZDO and
    /// enforce it on reuse/reconciliation.</summary>
    internal sealed class HomesteadOperationalManifestRow
    {
        internal HomesteadOperationalManifestRow(
            string hostPrefab, int zoneX, int zoneZ,
            double seatX, double seatZ, double seatY,
            string contentHash)
        {
            HostPrefab = hostPrefab ?? throw new ArgumentNullException(nameof(hostPrefab));
            ContentHash = contentHash ?? throw new ArgumentNullException(nameof(contentHash));
            ZoneX = zoneX;
            ZoneZ = zoneZ;
            SeatX = seatX;
            SeatZ = seatZ;
            SeatY = seatY;
        }

        internal string HostPrefab { get; }
        internal int ZoneX { get; }
        internal int ZoneZ { get; }
        internal double SeatX { get; }
        internal double SeatZ { get; }
        internal double SeatY { get; }
        internal string ContentHash { get; }

        internal string Key => Compose(HostPrefab, ZoneX, ZoneZ);

        internal static string Compose(string hostPrefab, int zoneX, int zoneZ) =>
            string.Join("|", hostPrefab,
                zoneX.ToString(CultureInfo.InvariantCulture),
                zoneZ.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>A validated operational manifest: whole-document scope keys + provenance + the accepted rows,
    /// plus any rejected raw rows with reasons for operator visibility. Absence of a matching row is a
    /// first-class outcome (ManifestRequired), never a guess.</summary>
    internal sealed class HomesteadOperationalManifest
    {
        internal const long ZoneHalfExtentMeters = 32;   // Valheim zone is 64 m; half-extent = 32 m
        internal const double MaxHostSeatRadius = 96.0;  // generous generator-host bound (village exterior radius)

        private readonly Dictionary<string, HomesteadOperationalManifestRow> rows;

        private HomesteadOperationalManifest(
            string worldIdentity, string selectorVersion, string providerVersion,
            long generation, string documentDigest,
            IReadOnlyList<HomesteadOperationalManifestRow> acceptedRows,
            IReadOnlyList<(string raw, HomesteadManifestRejection reason)> rejectedRows)
        {
            WorldIdentity = worldIdentity;
            SelectorVersion = selectorVersion;
            ProviderVersion = providerVersion;
            Generation = generation;
            DocumentDigest = documentDigest;
            rows = acceptedRows.ToDictionary(r => r.Key, r => r, StringComparer.Ordinal);
            RejectedRows = rejectedRows;
        }

        internal string WorldIdentity { get; }
        internal string SelectorVersion { get; }
        internal string ProviderVersion { get; }

        /// <summary>Monotonic generation. A higher generation than the one a ManifestRequired outcome was
        /// recorded against permits a retry.</summary>
        internal long Generation { get; }

        /// <summary>Content digest of the whole accepted document — stamped onto each Stone ZDO for provenance
        /// and enforced on reuse. Distinct from a per-row content hash.</summary>
        internal string DocumentDigest { get; }

        internal int Count => rows.Count;
        internal IReadOnlyList<(string raw, HomesteadManifestRejection reason)> RejectedRows { get; }

        internal static HomesteadOperationalManifest Empty { get; } =
            new HomesteadOperationalManifest(
                string.Empty, string.Empty, string.Empty, 0, string.Empty,
                Array.Empty<HomesteadOperationalManifestRow>(),
                Array.Empty<(string, HomesteadManifestRejection)>());

        internal bool IsEmpty => rows.Count == 0 && Generation == 0;

        internal bool TryGet(string hostPrefab, int zoneX, int zoneZ, out HomesteadOperationalManifestRow row) =>
            rows.TryGetValue(HomesteadOperationalManifestRow.Compose(hostPrefab, zoneX, zoneZ), out row!);

        /// <summary>
        /// Parse + validate a manifest document. Format is a small line-based text (comment lines start with
        /// '#', blank lines ignored). Header lines:
        ///   version=1
        ///   world=&lt;worldIdentity&gt;
        ///   selector=&lt;selectorVersion&gt;
        ///   provider=&lt;providerVersion&gt;
        ///   generation=&lt;long&gt;
        /// Row lines (tab-separated):
        ///   row\t&lt;hostPrefab&gt;\t&lt;zoneX&gt;\t&lt;zoneZ&gt;\t&lt;seatX&gt;\t&lt;seatZ&gt;\t&lt;seatY&gt;\t&lt;contentHash&gt;
        /// A row is rejected (not fatal) for malformed fields, non-finite/out-of-bounds coordinates, or a
        /// duplicate (prefab,zone). Missing/mismatched header scope keys or missing provenance make the WHOLE
        /// document empty (returns Empty-with-generation-0), because a document we cannot trust must not
        /// supply any seat.
        /// </summary>
        internal static HomesteadOperationalManifest Parse(
            string? text, string expectedWorldIdentity, string expectedSelectorVersion)
        {
            if (string.IsNullOrWhiteSpace(text)) return Empty;
            if (expectedWorldIdentity == null) throw new ArgumentNullException(nameof(expectedWorldIdentity));
            if (expectedSelectorVersion == null) throw new ArgumentNullException(nameof(expectedSelectorVersion));

            string? world = null, selector = null, provider = null;
            long generation = 0;
            var headerGenerationSeen = false;
            var accepted = new List<HomesteadOperationalManifestRow>();
            var rejected = new List<(string, HomesteadManifestRejection)>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var rawLine in text!.Replace("\r\n", "\n").Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                if (TryHeader(line, "world=", out var w)) { world = w; continue; }
                if (TryHeader(line, "selector=", out var s)) { selector = s; continue; }
                if (TryHeader(line, "provider=", out var p)) { provider = p; continue; }
                if (TryHeader(line, "generation=", out var g))
                {
                    if (long.TryParse(g, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
                    {
                        generation = parsed;
                        headerGenerationSeen = true;
                    }
                    continue;
                }
                if (TryHeader(line, "version=", out _)) continue;

                if (line.StartsWith("row\t", StringComparison.Ordinal) || line.StartsWith("row ", StringComparison.Ordinal))
                {
                    var parts = line.Split('\t');
                    if (parts.Length != 8) { rejected.Add((line, HomesteadManifestRejection.MalformedRow)); continue; }
                    var prefab = parts[1].Trim();
                    if (prefab.Length == 0) { rejected.Add((line, HomesteadManifestRejection.MalformedRow)); continue; }
                    if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var zx) ||
                        !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var zz))
                    { rejected.Add((line, HomesteadManifestRejection.MalformedRow)); continue; }
                    if (!TryFinite(parts[4], out var sx) || !TryFinite(parts[5], out var sz) || !TryFinite(parts[6], out var sy))
                    { rejected.Add((line, HomesteadManifestRejection.NonFiniteCoordinate)); continue; }
                    var contentHash = parts[7].Trim();
                    if (contentHash.Length == 0) { rejected.Add((line, HomesteadManifestRejection.MissingProvenance)); continue; }

                    // Zone-bound + host-bound checks: seat XZ must sit inside the host zone's 64 m cell and
                    // within a generous host radius of the zone center, so a typo/garbage coordinate cannot
                    // plant a Stone across the map.
                    var zoneCenterX = (double)zx * (ZoneHalfExtentMeters * 2);
                    var zoneCenterZ = (double)zz * (ZoneHalfExtentMeters * 2);
                    if (Math.Abs(sx - zoneCenterX) > ZoneHalfExtentMeters ||
                        Math.Abs(sz - zoneCenterZ) > ZoneHalfExtentMeters)
                    { rejected.Add((line, HomesteadManifestRejection.OutOfZoneBounds)); continue; }
                    var radial = Math.Sqrt(((sx - zoneCenterX) * (sx - zoneCenterX)) + ((sz - zoneCenterZ) * (sz - zoneCenterZ)));
                    if (radial > MaxHostSeatRadius)
                    { rejected.Add((line, HomesteadManifestRejection.OutOfHostBounds)); continue; }

                    var key = HomesteadOperationalManifestRow.Compose(prefab, zx, zz);
                    if (!seenKeys.Add(key)) { rejected.Add((line, HomesteadManifestRejection.DuplicateRow)); continue; }
                    accepted.Add(new HomesteadOperationalManifestRow(prefab, zx, zz, sx, sz, sy, contentHash));
                    continue;
                }

                rejected.Add((line, HomesteadManifestRejection.MalformedRow));
            }

            // Whole-document trust gates: scope keys must match and provenance must be present. A document we
            // cannot trust supplies NO seats (Empty), so a mismatched/forged manifest cannot seat a Stone.
            if (world == null || !string.Equals(world, expectedWorldIdentity, StringComparison.Ordinal)) return Empty;
            if (selector == null || !string.Equals(selector, expectedSelectorVersion, StringComparison.Ordinal)) return Empty;
            if (string.IsNullOrEmpty(provider) || !headerGenerationSeen || generation <= 0) return Empty;

            var digest = StableHash.Hex(
                world, selector, provider!, generation.ToString(CultureInfo.InvariantCulture),
                string.Join(";", accepted
                    .OrderBy(r => r.Key, StringComparer.Ordinal)
                    .Select(r => string.Format(CultureInfo.InvariantCulture,
                        "{0}:{1:0.0000}:{2:0.0000}:{3:0.0000}:{4}", r.Key, r.SeatX, r.SeatZ, r.SeatY, r.ContentHash))));

            return new HomesteadOperationalManifest(
                world, selector, provider!, generation, digest, accepted, rejected);
        }

        private static bool TryHeader(string line, string prefix, out string value)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                value = line.Substring(prefix.Length).Trim();
                return true;
            }
            value = string.Empty;
            return false;
        }

        private static bool TryFinite(string token, out double value)
        {
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                !double.IsNaN(value) && !double.IsInfinity(value))
                return true;
            value = 0.0;
            return false;
        }
    }
}
