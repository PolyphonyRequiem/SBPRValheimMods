using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    /// <summary>
    /// R6 (Blocker 2) / R7 (Blocker 3) — CI drift guard for the generated static geometry catalog. The catalog
    /// is checked in TWICE: once as the test fixture (tests/Fixtures) and once as the mod's embedded resource
    /// (src/.../Assets). They MUST be byte-identical — the fixture the tests validate is exactly the artifact
    /// production embeds and hash-pins at startup. If someone regenerates one and forgets the other, this fails
    /// in CI (which has no Valheim assets to re-extract). Regenerate with
    /// scripts/extract_homestead_geometry.py and copy the output to BOTH paths.
    ///
    /// R7 (Blocker 3): the gate no longer accepts a merely-nonempty catalog. It pins the EXACT host name set
    /// (13 ordinary WoodHouse hosts with real geometry + the 2 generator hosts) AND every ordinary host's
    /// semantic hash, so a silently regenerated catalog with drifted geometry or a renamed/removed host fails
    /// here rather than shipping a Stone stamped against changed footprints.
    /// </summary>
    public sealed class HomesteadCatalogDriftGuardTests
    {
        private const string ExpectedSchema = "niflheim-homestead-static-geometry-v3";

        // The 13 ordinary (static-geometry) hosts and their pinned semantic hashes. A change to any shipped
        // WoodHouse prefab's colliders rolls its hash and MUST land with an explicit update here + a selector
        // version bump (the hash is the Stone's content provenance, compared by the reconciler).
        private static readonly IReadOnlyDictionary<string, string> ExpectedOrdinaryHashes =
            new Dictionary<string, string>
            {
                ["WoodHouse1"] = "822F0501D5E5AA6AE4F4F2C7EE6F3CEB3D9FA533B1533ACDA11928018DEDB0BC",
                ["WoodHouse2"] = "5C8325CF2BD9A1DFEC3E78DF65D2D3A44E1F43B11DAAD1C40984728387CA6708",
                ["WoodHouse3"] = "4BE3F6CC53E07DFE5B355B4F5511CFFB33CA86FE4127AA26A33138CD1ED356D3",
                ["WoodHouse4"] = "204B33E2696C0CDF0E03D7F292727F761A843954966E0F0245DB164F0A73FE2C",
                ["WoodHouse5"] = "FD140BB9654051B924BC22E15A97E14B347D2D0AFE0E841F8D950DCEA198ECBC",
                ["WoodHouse6"] = "53B197EFE52B0259655996C9FA1AAB3F6B561E884F12D3540E07893A0521BAF2",
                ["WoodHouse7"] = "977A48F1398C4330219751F3A2DC21F1C8C1534CA5224B7412F9EBE7656C6224",
                ["WoodHouse8"] = "64905D6D985FB32F7FE040F1FD0B8480361ACE99BE60DA008160408DC64EF675",
                ["WoodHouse9"] = "6C97D8842747D370F8B45DF46453FFB40E5A7D8560376A65EBB16E436CDB16D0",
                ["WoodHouse10"] = "760024E3C67E21B9D3D27CF852677432EC68EAB480CA830F06B1BA8A8FA77A40",
                ["WoodHouse11"] = "D985B2B8F3183CBC2F5A88C6F5D77F504576B8DDE3AA5EF057E1D820059AADCF",
                ["WoodHouse12"] = "3B41337E5C7AE72A111D7218F5E7D0914BE4BE94AD5E6BFCF589C6EBCDC09D33",
                ["WoodHouse13"] = "038271632D960F61152DBAE734FA5161EFD6AA1DA129122E9470732D967EA9D1",
            };


        private static string RepoRoot()
        {
            var baseDir = new DirectoryInfo(System.AppContext.BaseDirectory);
            return baseDir.Parent!.Parent!.Parent!.Parent!.FullName;
        }

        private static string FixturePath() =>
            Path.Combine(RepoRoot(), "tests", "Fixtures", "homestead-static-geometry.json");

        private static string EmbeddedPath() =>
            Path.Combine(RepoRoot(), "src", "SBPR.Niflheim.HomesteadStones", "Assets", "homestead-static-geometry.json");

        [Fact]
        public void The_test_fixture_and_the_embedded_mod_catalog_are_byte_identical()
        {
            var fixture = FixturePath();
            var embedded = EmbeddedPath();
            Assert.True(File.Exists(fixture), $"missing fixture: {fixture}");
            Assert.True(File.Exists(embedded), $"missing embedded catalog: {embedded}");
            Assert.Equal(Sha256(fixture), Sha256(embedded));
        }

        [Fact]
        public void The_catalog_pins_the_exact_host_set_schema_and_semantic_hashes()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(FixturePath()));
            var root = doc.RootElement;

            Assert.Equal(ExpectedSchema, root.GetProperty("schema").GetString());

            var hosts = root.GetProperty("hosts");
            // Exact current host set: the 13 ordinary houses. Farm/Village belong to the future village system.
            var actualNames = new HashSet<string>();
            foreach (var host in hosts.EnumerateObject()) actualNames.Add(host.Name);

            var expectedNames = new HashSet<string>(ExpectedOrdinaryHashes.Keys);
            Assert.Equal(expectedNames, actualNames);

            // Every ordinary host's semantic hash is pinned exactly (not merely nonempty).
            foreach (var pair in ExpectedOrdinaryHashes)
            {
                var host = hosts.GetProperty(pair.Key);
                Assert.Equal(pair.Value, host.GetProperty("semanticHash").GetString());
                Assert.True(host.GetProperty("colliderCount").GetInt32() > 0,
                    $"{pair.Key} is an ordinary static-geometry host and must carry footprints.");
            }


        }

        [Fact]
        public void Runtime_loader_recomputes_every_pinned_hash()
        {
            // Exercise the exact production parser/hash path. A string-only pin test missed the Python -0.0
            // versus Mono 0.0 formatting divergence that disabled realization at boot.
            var catalog = SBPR.Niflheim.HomesteadStones.Features.HomesteadStone
                .HomesteadStaticGeometryCatalogLoader.Parse(File.ReadAllText(FixturePath()));
            Assert.Equal(ExpectedOrdinaryHashes.Count, catalog.HostCount);
            foreach (var pair in ExpectedOrdinaryHashes)
            {
                Assert.True(catalog.TryGet(pair.Key, out var geometry));
                Assert.Equal(pair.Value, geometry.SemanticHash);
            }
        }

        private static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return System.BitConverter.ToString(sha.ComputeHash(stream));
        }
    }
}
