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

        // Root-cause regression (repair PR#424): a duplicate `connectionGeneration` key
        // shipped inside `properties` because JsonDocument (and RFC 8259) silently apply
        // last-wins, so no existing test could see the dead first definition — which also
        // dropped the `minimum: 0` constraint depending on parser order. Utf8JsonReader,
        // unlike JsonDocument, surfaces EVERY property token, so we can assert no object in
        // the schema declares the same member name twice. Engine-free and deterministic.
        [Fact]
        public void ReceiptSchema_HasNoDuplicateObjectKeys()
        {
            string path = FindRepoFile("qa/contracts/receipt.schema.json");
            byte[] bytes = File.ReadAllBytes(path);
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });

            // One name-set per open object on the stack.
            var stack = new Stack<HashSet<string>>();
            var duplicates = new List<string>();

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        stack.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.EndObject:
                        stack.Pop();
                        break;
                    case JsonTokenType.PropertyName:
                        string? name = reader.GetString();
                        if (name != null && stack.Count > 0 && !stack.Peek().Add(name))
                            duplicates.Add(name);
                        break;
                }
            }

            Assert.True(duplicates.Count == 0,
                $"receipt.schema.json declares duplicate object member(s): {string.Join(", ", duplicates.Distinct())}. " +
                "Duplicate JSON keys are undefined per RFC 8259 (last-wins in practice) and silently drop constraints.");
        }
    }
}
