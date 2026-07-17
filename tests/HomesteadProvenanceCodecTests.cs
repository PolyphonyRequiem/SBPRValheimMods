using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    /// <summary>
    /// R7 (Blocker 1 / Blocker 5) — exercises the PRODUCTION provenance stamp/read/compare codec against an
    /// in-memory typed-var store that stands in for a ZDO. This is the exact code the net48 StampIdentity and
    /// the reconciler delegate to (via IProvenanceWriter / IProvenanceReader), so the round-trip, partial-write
    /// detection, and full-fact comparison are verified headless — not only through mocked reconciler inputs.
    ///
    /// It also pins the codec key literals to the ZdoKeyPrefix contract so the engine-free codec (the single
    /// source of truth) and the net48 HomesteadStoneData constants that forward to it can never drift apart.
    /// </summary>
    public sealed class HomesteadProvenanceCodecTests
    {
        private const string Prefix = "niflheim.homestead.";

        /// <summary>In-memory typed-var store standing in for a ZDO. Mirrors the ZdoProvenanceAccessor seam:
        /// SetX writes, GetX reads back with a caller-supplied sentinel for absent keys.</summary>
        private sealed class InMemoryZdo : IProvenanceWriter, IProvenanceReader
        {
            private readonly Dictionary<string, int> ints = new Dictionary<string, int>();
            private readonly Dictionary<string, long> longs = new Dictionary<string, long>();
            private readonly Dictionary<string, string> strings = new Dictionary<string, string>();

            public void SetInt(string key, int value) => ints[key] = value;
            public void SetLong(string key, long value) => longs[key] = value;
            public void SetString(string key, string value) => strings[key] = value;
            public int GetInt(string key, int missing) => ints.TryGetValue(key, out var v) ? v : missing;
            public long GetLong(string key, long missing) => longs.TryGetValue(key, out var v) ? v : missing;
            public string GetString(string key, string missing) => strings.TryGetValue(key, out var v) ? v : missing;

            internal void Remove(string key) { ints.Remove(key); longs.Remove(key); strings.Remove(key); }
        }

        private static HomesteadStoneProvenance StaticProvenance() =>
            new HomesteadStoneProvenance(
                HomesteadProvenanceCodec.SchemaVersion,
                new HomesteadAssignmentMetadata("uid:-898655635", "niflheim-homestead-playtest-v1", "WoodHouse5", -25, -30),
                HomesteadSeatProvider.StaticGeometry, "catalog-digest-A", "geo-hash-A", 0);

        private static HomesteadStoneProvenance ManifestProvenance() =>
            new HomesteadStoneProvenance(
                HomesteadProvenanceCodec.SchemaVersion,
                new HomesteadAssignmentMetadata("uid:-898655635", "niflheim-homestead-playtest-v1", "WoodVillage1", 4, 4),
                HomesteadSeatProvider.Manifest, "op-v1", "doc-digest", 5);

        [Fact]
        public void Stamp_then_read_round_trips_every_field()
        {
            var zdo = new InMemoryZdo();
            var provenance = StaticProvenance();

            HomesteadProvenanceCodec.Stamp(zdo, provenance);
            var read = HomesteadProvenanceCodec.Read(zdo);

            Assert.Equal(provenance, read);
            Assert.True(HomesteadProvenanceCodec.ReadBackMatches(zdo, provenance));
            Assert.True(HomesteadProvenanceCodec.IsFullyStamped(read));
        }

        [Fact]
        public void Round_trips_a_manifest_provenance_including_generation()
        {
            var zdo = new InMemoryZdo();
            var provenance = ManifestProvenance();

            HomesteadProvenanceCodec.Stamp(zdo, provenance);

            Assert.True(HomesteadProvenanceCodec.ReadBackMatches(zdo, provenance));
            Assert.Equal(HomesteadSeatProvider.Manifest, HomesteadProvenanceCodec.Read(zdo).Provider);
            Assert.Equal(5, HomesteadProvenanceCodec.Read(zdo).ManifestGeneration);
        }

        [Fact]
        public void A_partial_stamp_missing_the_content_hash_fails_read_back_verification()
        {
            // Simulate a torn/partial write: everything stamped, then the content-hash key drops out. The
            // read-back must NOT match the intended provenance, so production reaps the Stone.
            var zdo = new InMemoryZdo();
            var provenance = StaticProvenance();
            HomesteadProvenanceCodec.Stamp(zdo, provenance);
            zdo.Remove(HomesteadProvenanceCodec.ContentHashKey);

            Assert.False(HomesteadProvenanceCodec.ReadBackMatches(zdo, provenance));
        }

        [Fact]
        public void A_missing_manifest_generation_fails_read_back_for_a_manifest_stone()
        {
            var zdo = new InMemoryZdo();
            var provenance = ManifestProvenance();
            HomesteadProvenanceCodec.Stamp(zdo, provenance);
            zdo.Remove(HomesteadProvenanceCodec.ManifestGenerationKey);

            // The generation reads back as its sentinel (0) which differs from the stamped 5 => no match.
            Assert.False(HomesteadProvenanceCodec.ReadBackMatches(zdo, provenance));
        }

        [Fact]
        public void An_empty_zdo_reads_as_an_unstamped_provenance()
        {
            var read = HomesteadProvenanceCodec.Read(new InMemoryZdo());

            Assert.False(HomesteadProvenanceCodec.IsFullyStamped(read));
            Assert.Equal(0, read.SchemaVersion);
            Assert.Equal(int.MinValue, read.Assignment.ZoneX);
        }

        [Fact]
        public void A_provenance_stamped_under_an_older_schema_is_not_fully_stamped()
        {
            var zdo = new InMemoryZdo();
            var provenance = StaticProvenance();
            HomesteadProvenanceCodec.Stamp(zdo, provenance);
            // Force a legacy schema version onto the stamp.
            zdo.SetInt(HomesteadProvenanceCodec.ProvenanceVersionKey, HomesteadProvenanceCodec.SchemaVersion - 1);

            Assert.False(HomesteadProvenanceCodec.IsFullyStamped(HomesteadProvenanceCodec.Read(zdo)));
            Assert.False(HomesteadProvenanceCodec.ReadBackMatches(zdo, provenance));
        }

        [Fact]
        public void Two_provenances_differing_only_in_provider_version_do_not_match()
        {
            var zdo = new InMemoryZdo();
            HomesteadProvenanceCodec.Stamp(zdo, StaticProvenance());

            var upgraded = new HomesteadStoneProvenance(
                HomesteadProvenanceCodec.SchemaVersion,
                new HomesteadAssignmentMetadata("uid:-898655635", "niflheim-homestead-playtest-v1", "WoodHouse5", -25, -30),
                HomesteadSeatProvider.StaticGeometry, "catalog-digest-B", "geo-hash-A", 0);

            Assert.False(HomesteadProvenanceCodec.ReadBackMatches(zdo, upgraded));
        }

        [Fact]
        public void Codec_key_literals_match_the_zdo_key_prefix_contract()
        {
            // The codec is the single source of truth; HomesteadStoneData forwards to these. Pin each literal
            // so a rename on either side is caught before it silently orphans every already-stamped Stone.
            Assert.Equal(Prefix + "prov_version", HomesteadProvenanceCodec.ProvenanceVersionKey);
            Assert.Equal(Prefix + "location_zone_x", HomesteadProvenanceCodec.LocationZoneXKey);
            Assert.Equal(Prefix + "location_zone_z", HomesteadProvenanceCodec.LocationZoneZKey);
            Assert.Equal(Prefix + "world_identity", HomesteadProvenanceCodec.WorldIdentityKey);
            Assert.Equal(Prefix + "selector_version", HomesteadProvenanceCodec.SelectorVersionKey);
            Assert.Equal(Prefix + "host_prefab", HomesteadProvenanceCodec.HostPrefabKey);
            Assert.Equal(Prefix + "provider_kind", HomesteadProvenanceCodec.ProviderKindKey);
            Assert.Equal(Prefix + "provider_version", HomesteadProvenanceCodec.ProviderVersionKey);
            Assert.Equal(Prefix + "content_hash", HomesteadProvenanceCodec.ContentHashKey);
            Assert.Equal(Prefix + "manifest_generation", HomesteadProvenanceCodec.ManifestGenerationKey);
        }
    }
}
