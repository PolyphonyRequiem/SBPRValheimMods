// Spec⇄code guard (CONTRIBUTING triangle): the wire schema's verb enum MUST equal the
// authoritative VerbCatalog. If someone adds a verb to the catalog without updating
// qa/contracts/request.schema.json (or vice-versa), this test fails — the same
// "code and spec change together" rule the repo enforces elsewhere.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class SchemaSyncTests
    {
        private static string FindRepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException($"Could not locate {relative} walking up from {AppContext.BaseDirectory}");
        }

        [Fact]
        public void RequestSchemaVerbEnum_EqualsCatalog()
        {
            string path = FindRepoFile("qa/contracts/request.schema.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var enumEl = doc.RootElement
                .GetProperty("$defs").GetProperty("verbName").GetProperty("enum");
            var schemaVerbs = new HashSet<string>(enumEl.EnumerateArray().Select(e => e.GetString()!), StringComparer.Ordinal);
            var catalogVerbs = new HashSet<string>(VerbCatalog.Names, StringComparer.Ordinal);
            Assert.True(schemaVerbs.SetEquals(catalogVerbs),
                $"request.schema.json verb enum drifted from VerbCatalog.\n" +
                $"  only in schema:  {string.Join(",", schemaVerbs.Except(catalogVerbs))}\n" +
                $"  only in catalog: {string.Join(",", catalogVerbs.Except(schemaVerbs))}");
        }

        [Fact]
        public void ReceiptSchemaReasonEnum_CoversAllRejectReasons()
        {
            string path = FindRepoFile("qa/contracts/receipt.schema.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var enumEl = doc.RootElement
                .GetProperty("properties").GetProperty("reason").GetProperty("enum");
            var schemaReasons = new HashSet<string>(enumEl.EnumerateArray().Select(e => e.GetString()!), StringComparer.Ordinal);
            var coreReasons = new HashSet<string>(Enum.GetNames(typeof(RejectReason)), StringComparer.Ordinal);
            Assert.True(schemaReasons.SetEquals(coreReasons),
                $"receipt.schema.json reason enum drifted from RejectReason.\n" +
                $"  only in schema: {string.Join(",", schemaReasons.Except(coreReasons))}\n" +
                $"  only in enum:   {string.Join(",", coreReasons.Except(schemaReasons))}");
        }
    }
}
