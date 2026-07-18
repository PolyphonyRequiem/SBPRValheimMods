using System;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Domain;
using SBPR.Niflheim.HomesteadStones.Features.HomesteadStone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    /// <summary>
    /// R6 (Blocker 1/2) — the static geometry catalog is the production authority. These tests use the SAME
    /// engine-free parser production ships (<see cref="HomesteadStaticGeometryCatalogLoader.Parse"/>) against
    /// the checked-in fixture, so what is validated here is byte-for-byte what production embeds and loads.
    /// </summary>
    public sealed class HomesteadStaticGeometryCatalogTests
    {
        private static string FixtureJson()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "homestead-static-geometry.json");
            return File.ReadAllText(path);
        }

        [Fact]
        public void Catalog_loads_all_thirteen_ordinary_hosts_and_pins_their_hashes()
        {
            var catalog = HomesteadStaticGeometryCatalogLoader.Parse(FixtureJson());
            Assert.Equal(13, catalog.HostCount);   // 13 ordinary houses; generators carry no catalog geometry
            for (var i = 1; i <= 13; i++)
            {
                var prefab = "WoodHouse" + i;
                Assert.True(catalog.TryGet(prefab, out var geometry), $"{prefab} must be in the catalog.");
                Assert.True(geometry.ColliderCount > 0);
                // The geometry's semantic hash is the canonical recompute — the load verified stored == recompute.
                Assert.Equal(geometry.SemanticHash, HomesteadGeometryHash.Compute(geometry.Footprints));
            }
        }

        [Fact]
        public void Catalog_digest_is_stable_and_nonempty()
        {
            var a = HomesteadStaticGeometryCatalogLoader.Parse(FixtureJson());
            var b = HomesteadStaticGeometryCatalogLoader.Parse(FixtureJson());
            Assert.False(string.IsNullOrEmpty(a.CatalogDigest));
            Assert.Equal(a.CatalogDigest, b.CatalogDigest);   // deterministic across loads
        }

        [Fact]
        public void Catalog_generators_are_absent_because_they_carry_no_static_geometry()
        {
            var catalog = HomesteadStaticGeometryCatalogLoader.Parse(FixtureJson());
            Assert.False(catalog.TryGet("WoodFarm1", out _));
            Assert.False(catalog.TryGet("WoodVillage1", out _));
        }

        [Fact]
        public void A_drifted_stored_hash_fails_the_pin_at_load()
        {
            // Hand-built one-host document whose stored hash does NOT match its footprints → must throw.
            const string json = @"{
              ""schema"": ""test"",
              ""hosts"": {
                ""WoodHouse1"": {
                  ""semanticHash"": ""DEADBEEF"",
                  ""colliders"": [ { ""cx"": 0.0, ""cz"": 0.0, ""halfX"": 1.0, ""halfZ"": 1.0 } ]
                }
              }
            }";
            var ex = Assert.Throws<InvalidOperationException>(() => HomesteadStaticGeometryCatalogLoader.Parse(json));
            Assert.Contains("pin FAILED", ex.Message);
        }

        [Fact]
        public void A_correct_stored_hash_passes_the_pin_at_load()
        {
            var footprints = new[] { new StaticColliderFootprint(0.0, 0.0, 1.0, 1.0) };
            var hash = HomesteadGeometryHash.Compute(footprints);
            var json = @"{ ""schema"": ""test"", ""hosts"": { ""WoodHouse1"": { ""semanticHash"": """ + hash + @""",
              ""colliders"": [ { ""cx"": 0.0, ""cz"": 0.0, ""halfX"": 1.0, ""halfZ"": 1.0 } ] } } }";
            var catalog = HomesteadStaticGeometryCatalogLoader.Parse(json);
            Assert.Equal(1, catalog.HostCount);
            Assert.True(catalog.TryGet("WoodHouse1", out var geometry));
            Assert.Equal(hash, geometry.SemanticHash);
        }
    }
}
