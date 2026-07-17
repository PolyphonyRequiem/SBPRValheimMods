using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    /// <summary>
    /// R6 (Blocker 2) — CI drift guard for the generated static geometry catalog. The catalog is checked in
    /// TWICE: once as the test fixture (tests/Fixtures) and once as the mod's embedded resource
    /// (src/.../Assets). They MUST be byte-identical — the fixture the tests validate is exactly the artifact
    /// production embeds and hash-pins at startup. If someone regenerates one and forgets the other, this
    /// fails in CI (which has no Valheim assets to re-extract). Regenerate with
    /// scripts/extract_homestead_geometry.py and copy the output to BOTH paths.
    /// </summary>
    public sealed class HomesteadCatalogDriftGuardTests
    {
        [Fact]
        public void The_test_fixture_and_the_embedded_mod_catalog_are_byte_identical()
        {
            // AppContext.BaseDirectory = tests/bin/<cfg>/net8.0; repo root is four levels up.
            var baseDir = new DirectoryInfo(System.AppContext.BaseDirectory);
            var repoRoot = baseDir.Parent!.Parent!.Parent!.Parent!.FullName;

            var fixture = Path.Combine(repoRoot, "tests", "Fixtures", "homestead-static-geometry.json");
            var embedded = Path.Combine(repoRoot, "src", "SBPR.Niflheim.HomesteadStones",
                "Assets", "homestead-static-geometry.json");

            Assert.True(File.Exists(fixture), $"missing fixture: {fixture}");
            Assert.True(File.Exists(embedded), $"missing embedded catalog: {embedded}");
            Assert.Equal(Sha256(fixture), Sha256(embedded));
        }

        private static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return System.BitConverter.ToString(sha.ComputeHash(stream));
        }
    }
}
