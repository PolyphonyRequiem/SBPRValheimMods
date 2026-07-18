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
                ["WoodHouse1"] = "ABCDC6D6A4D3B5ECC0FFE226489BB967CCC837029CFB41B7219B705C7E849DA3",
                ["WoodHouse2"] = "5C8325CF2BD9A1DFEC3E78DF65D2D3A44E1F43B11DAAD1C40984728387CA6708",
                ["WoodHouse3"] = "4BE3F6CC53E07DFE5B355B4F5511CFFB33CA86FE4127AA26A33138CD1ED356D3",
                ["WoodHouse4"] = "741767C20C84495746751047BCA5E0B62EADA16702AC89C2C85EC833722612AF",
                ["WoodHouse5"] = "AE39400D4320381D737F24766B60ED04E40606266846AC252A0B1650A251977D",
                ["WoodHouse6"] = "7618274A42E9A8E6B7B0D1C4F1373881823DD9A78774FD660FE619260A033E69",
                ["WoodHouse7"] = "977A48F1398C4330219751F3A2DC21F1C8C1534CA5224B7412F9EBE7656C6224",
                ["WoodHouse8"] = "64905D6D985FB32F7FE040F1FD0B8480361ACE99BE60DA008160408DC64EF675",
                ["WoodHouse9"] = "6C97D8842747D370F8B45DF46453FFB40E5A7D8560376A65EBB16E436CDB16D0",
                ["WoodHouse10"] = "760024E3C67E21B9D3D27CF852677432EC68EAB480CA830F06B1BA8A8FA77A40",
                ["WoodHouse11"] = "D985B2B8F3183CBC2F5A88C6F5D77F504576B8DDE3AA5EF057E1D820059AADCF",
                ["WoodHouse12"] = "3B41337E5C7AE72A111D7218F5E7D0914BE4BE94AD5E6BFCF589C6EBCDC09D33",
                ["WoodHouse13"] = "038271632D960F61152DBAE734FA5161EFD6AA1DA129122E9470732D967EA9D1",
            };

        // The 2 generator hosts route through the operational manifest, not static geometry, so they carry the
        // canonical empty-footprint hash (SHA-256 of the empty string). Pinned so a generator host never
        // silently acquires static geometry.
        private const string EmptyFootprintHash =
            "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";
        private static readonly string[] ExpectedGeneratorHosts = { "WoodFarm1", "WoodVillage1" };

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
            // Exact host set: the 13 ordinary hosts + the 2 generator hosts, nothing more, nothing missing.
            var actualNames = new HashSet<string>();
            foreach (var host in hosts.EnumerateObject()) actualNames.Add(host.Name);

            var expectedNames = new HashSet<string>(ExpectedOrdinaryHashes.Keys);
            foreach (var g in ExpectedGeneratorHosts) expectedNames.Add(g);
            Assert.Equal(expectedNames, actualNames);

            // Every ordinary host's semantic hash is pinned exactly (not merely nonempty).
            foreach (var pair in ExpectedOrdinaryHashes)
            {
                var host = hosts.GetProperty(pair.Key);
                Assert.Equal(pair.Value, host.GetProperty("semanticHash").GetString());
                Assert.True(host.GetProperty("colliderCount").GetInt32() > 0,
                    $"{pair.Key} is an ordinary static-geometry host and must carry footprints.");
            }

            // Generator hosts carry the canonical empty-footprint hash and zero colliders.
            foreach (var generator in ExpectedGeneratorHosts)
            {
                var host = hosts.GetProperty(generator);
                Assert.Equal(EmptyFootprintHash, host.GetProperty("semanticHash").GetString());
                Assert.Equal(0, host.GetProperty("colliderCount").GetInt32());
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
