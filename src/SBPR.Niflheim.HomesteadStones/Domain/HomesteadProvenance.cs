using System;
using System.Globalization;

namespace SBPR.Niflheim.HomesteadStones.Domain
{
    // ============================================================================
    // R7 (Blocker 1) — provider/content provenance is wired into ZDO truth.
    //
    // The R6 review rejected placement because "ZDO stamp/reconciliation compare
    // only basic assignment metadata" — a Stone's creation authority (which provider
    // produced it, from which content hash / manifest generation) was discarded, so
    // a selector/provider/content upgrade could not be detected and the event gate
    // relied on bare zone existence rather than fully-matching facts.
    //
    // This file is the ENGINE-FREE provenance codec + comparison. The net48
    // StampIdentity writes these fields onto the Stone ZDO and read-back verifies
    // them through the SAME codec; the reconciler reads them back into a
    // HomesteadStoneProvenance and compares the FULL fact against the expected
    // assignment. Because the codec operates over an abstract key-value surface
    // (IProvenanceWriter / IProvenanceReader), the exact production stamp + read is
    // exercised headless against an in-memory store (Blocker 5), not only mocked.
    // ============================================================================

    /// <summary>Abstract owner-only writer over a ZDO's typed vars. The net48 adapter implements this over the
    /// real ZDO; tests implement it over an in-memory dictionary so the SAME stamp code runs headless.</summary>
    internal interface IProvenanceWriter
    {
        void SetInt(string key, int value);
        void SetLong(string key, long value);
        void SetString(string key, string value);
    }

    /// <summary>Abstract reader over a ZDO's typed vars. Mirrors <see cref="IProvenanceWriter"/> so a stamp can
    /// be read back and verified through the same abstraction the reconciler reads through.</summary>
    internal interface IProvenanceReader
    {
        int GetInt(string key, int missing);
        long GetLong(string key, long missing);
        string GetString(string key, string missing);
    }

    /// <summary>The complete provenance fact persisted on a Stone ZDO: identity assignment PLUS provider/content
    /// provenance. Two provenances are equal only when EVERY field matches — the reconciler compares this whole
    /// fact, never a partial subset, so a provider/content/generation upgrade is detected as a mismatch.</summary>
    internal readonly struct HomesteadStoneProvenance : IEquatable<HomesteadStoneProvenance>
    {
        internal HomesteadStoneProvenance(
            int schemaVersion,
            HomesteadAssignmentMetadata assignment,
            HomesteadSeatProvider provider,
            string providerVersion,
            string contentHash,
            long manifestGeneration)
        {
            SchemaVersion = schemaVersion;
            Assignment = assignment;
            Provider = provider;
            ProviderVersion = providerVersion ?? string.Empty;
            ContentHash = contentHash ?? string.Empty;
            ManifestGeneration = manifestGeneration;
        }

        internal int SchemaVersion { get; }
        internal HomesteadAssignmentMetadata Assignment { get; }
        internal HomesteadSeatProvider Provider { get; }
        internal string ProviderVersion { get; }
        internal string ContentHash { get; }
        internal long ManifestGeneration { get; }

        public bool Equals(HomesteadStoneProvenance other) =>
            SchemaVersion == other.SchemaVersion &&
            Assignment.Equals(other.Assignment) &&
            Provider == other.Provider &&
            string.Equals(ProviderVersion, other.ProviderVersion, StringComparison.Ordinal) &&
            string.Equals(ContentHash, other.ContentHash, StringComparison.Ordinal) &&
            ManifestGeneration == other.ManifestGeneration;

        public override bool Equals(object? obj) => obj is HomesteadStoneProvenance other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = SchemaVersion;
                hash = (hash * 397) ^ Assignment.GetHashCode();
                hash = (hash * 397) ^ (int)Provider;
                hash = (hash * 397) ^ ProviderVersion.GetHashCode();
                hash = (hash * 397) ^ ContentHash.GetHashCode();
                return (hash * 397) ^ ManifestGeneration.GetHashCode();
            }
        }
    }

    /// <summary>Engine-free provenance stamp/read/compare. The production StampIdentity delegates to
    /// <see cref="Stamp"/> and <see cref="ReadBackMatches"/>; the reconciler delegates to <see cref="Read"/>.
    /// Field names are the durable <see cref="Features.HomesteadStone.HomesteadStoneData"/> keys.</summary>
    internal static class HomesteadProvenanceCodec
    {
        // R7 (Blocker 1) — the canonical provenance key names. HomesteadProvenanceCodec is the SINGLE
        // source of truth: the Features-layer HomesteadStoneData constants forward to these, and a guard
        // test (ProvenanceKeyContractTests) pins each literal to its ZdoKeyPrefix-derived value so the
        // engine-free codec and the net48 stamp/read cannot drift apart. The codec lives in Domain so it
        // compiles into the net8 test project without dragging the Features/ (UnityEngine) namespace.
        internal const string ProvenanceVersionKey = "niflheim.homestead.prov_version";
        internal const string LocationZoneXKey = "niflheim.homestead.location_zone_x";
        internal const string LocationZoneZKey = "niflheim.homestead.location_zone_z";
        internal const string WorldIdentityKey = "niflheim.homestead.world_identity";
        internal const string SelectorVersionKey = "niflheim.homestead.selector_version";
        internal const string HostPrefabKey = "niflheim.homestead.host_prefab";
        internal const string ProviderKindKey = "niflheim.homestead.provider_kind";
        internal const string ProviderVersionKey = "niflheim.homestead.provider_version";
        internal const string ContentHashKey = "niflheim.homestead.content_hash";
        internal const string ManifestGenerationKey = "niflheim.homestead.manifest_generation";

        internal const int SchemaVersion = 1;

        /// <summary>Build the full provenance fact for a resolved placement.</summary>
        internal static HomesteadStoneProvenance FromRecord(HomesteadAssignmentMetadata assignment, ResolvedPlacementRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            return new HomesteadStoneProvenance(
                SchemaVersion, assignment, record.Provider, record.ProviderVersion, record.ContentHash, record.ManifestGeneration);
        }

        /// <summary>Persist the full provenance onto a ZDO via the abstract writer. Writes EVERY field, including
        /// the schema version, so a read-back can verify completeness (a partial write fails verification).</summary>
        internal static void Stamp(IProvenanceWriter writer, HomesteadStoneProvenance provenance)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            writer.SetInt(ProvenanceVersionKey, provenance.SchemaVersion);
            writer.SetInt(LocationZoneXKey, provenance.Assignment.ZoneX);
            writer.SetInt(LocationZoneZKey, provenance.Assignment.ZoneZ);
            writer.SetString(WorldIdentityKey, provenance.Assignment.WorldIdentity);
            writer.SetString(SelectorVersionKey, provenance.Assignment.SelectorVersion);
            writer.SetString(HostPrefabKey, provenance.Assignment.Prefab);
            writer.SetString(ProviderKindKey, provenance.Provider.ToString());
            writer.SetString(ProviderVersionKey, provenance.ProviderVersion);
            writer.SetString(ContentHashKey, provenance.ContentHash);
            writer.SetLong(ManifestGenerationKey, provenance.ManifestGeneration);
        }

        /// <summary>Read the full provenance fact back from a ZDO via the abstract reader. Unkeyed/absent fields
        /// read as their sentinels so a partial or absent stamp yields a provenance that cannot match an
        /// expected one (schema version 0 / int.MinValue zones).</summary>
        internal static HomesteadStoneProvenance Read(IProvenanceReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var schema = reader.GetInt(ProvenanceVersionKey, 0);
            var zoneX = reader.GetInt(LocationZoneXKey, int.MinValue);
            var zoneZ = reader.GetInt(LocationZoneZKey, int.MinValue);
            var world = reader.GetString(WorldIdentityKey, string.Empty);
            var selector = reader.GetString(SelectorVersionKey, string.Empty);
            var prefab = reader.GetString(HostPrefabKey, string.Empty);
            var providerKind = ParseProvider(reader.GetString(ProviderKindKey, string.Empty));
            var providerVersion = reader.GetString(ProviderVersionKey, string.Empty);
            var contentHash = reader.GetString(ContentHashKey, string.Empty);
            var generation = reader.GetLong(ManifestGenerationKey, 0);
            var assignment = new HomesteadAssignmentMetadata(world, selector, prefab, zoneX, zoneZ);
            return new HomesteadStoneProvenance(schema, assignment, providerKind, providerVersion, contentHash, generation);
        }

        /// <summary>True when the value read back through <paramref name="reader"/> equals the provenance that
        /// was stamped — the production read-back verification. A missing/partial write fails this.</summary>
        internal static bool ReadBackMatches(IProvenanceReader reader, HomesteadStoneProvenance expected) =>
            Read(reader).Equals(expected);

        /// <summary>Whether a keyed provenance carries the current schema version and non-sentinel zone (i.e. it
        /// is a fully-stamped R7 Stone, not a legacy/partial one). A false result means the Stone predates the
        /// provenance stamp and must be reconciled/re-stamped rather than trusted.</summary>
        internal static bool IsFullyStamped(HomesteadStoneProvenance provenance) =>
            provenance.SchemaVersion == SchemaVersion &&
            provenance.Assignment.ZoneX != int.MinValue &&
            provenance.Assignment.ZoneZ != int.MinValue;

        private static HomesteadSeatProvider ParseProvider(string raw) =>
            Enum.TryParse<HomesteadSeatProvider>(raw, out var value) ? value : HomesteadSeatProvider.StaticGeometry;

        internal static string Describe(HomesteadStoneProvenance provenance) => string.Format(
            CultureInfo.InvariantCulture,
            "schema={0} world={1} selector={2} prefab={3} zone=({4},{5}) provider={6} providerVersion={7} content={8} gen={9}",
            provenance.SchemaVersion, provenance.Assignment.WorldIdentity, provenance.Assignment.SelectorVersion,
            provenance.Assignment.Prefab, provenance.Assignment.ZoneX, provenance.Assignment.ZoneZ,
            provenance.Provider, provenance.ProviderVersion, provenance.ContentHash, provenance.ManifestGeneration);
    }
}
