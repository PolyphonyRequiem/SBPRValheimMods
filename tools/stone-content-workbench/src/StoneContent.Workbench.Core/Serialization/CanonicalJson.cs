using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using StoneContent.Workbench.Core.Model;

namespace StoneContent.Workbench.Core.Serialization
{
    // Deterministic canonical JSON codec for the Stone content asset. Two hard guarantees:
    //   * LOAD is strict — unknown properties are refused (no silent drop), missing authored fields
    //     are refused (no silent default), and enum-like strings are preserved verbatim (the semantic
    //     validator owns enum-value checking with a stable diagnostic code, not the parser).
    //   * SERIALIZE is canonical — fixed property order, fixed 2-space indent, LF newlines, a trailing
    //     newline, and stable array order (authored order preserved). Serializing the same document
    //     twice is byte-identical, and the checked-in asset round-trips byte-for-byte.
    //
    // No I/O: callers pass JSON text in and get text/records out. The CLI/web adapters own files.
    public static class CanonicalJson
    {
        public sealed class JsonLoadException : Exception
        {
            public JsonLoadException(string message) : base(message) { }
        }

        // ── Load ────────────────────────────────────────────────────────────────────────────────
        public static StoneContentDocument Load(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Disallow,
                    AllowTrailingCommas = false,
                });
            }
            catch (JsonException ex)
            {
                throw new JsonLoadException("Asset is not well-formed JSON: " + ex.Message);
            }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new JsonLoadException("Root must be a JSON object.");

                // $schema is an allowed, ignored authoring convenience (not part of the model).
                var rootAllowed = new HashSet<string>(StringComparer.Ordinal)
                {
                    "$schema", "formatVersion", "assetId", "family", "variant",
                    "versions", "foundational", "facets", "trees", "nodes",
                };
                RejectUnknown(root, rootAllowed, "");

                return new StoneContentDocument(
                    FormatVersion: GetInt(root, "formatVersion", ""),
                    AssetId: GetString(root, "assetId", ""),
                    Family: GetString(root, "family", ""),
                    Variant: GetString(root, "variant", ""),
                    Versions: LoadVersions(GetObject(root, "versions", "")),
                    Foundational: LoadFoundational(GetObject(root, "foundational", "")),
                    Facets: LoadArray(root, "facets", LoadFacet),
                    Trees: LoadArray(root, "trees", LoadTree),
                    Nodes: LoadArray(root, "nodes", LoadNode));
            }
        }

        private static VersionPins LoadVersions(JsonElement e)
        {
            RejectUnknown(e, new HashSet<string>(StringComparer.Ordinal)
                { "contentRegistry", "foundationalCatalog", "facetPalette", "treeTuning" }, "versions");
            return new VersionPins(
                GetInt(e, "contentRegistry", "versions"),
                GetInt(e, "foundationalCatalog", "versions"),
                GetInt(e, "facetPalette", "versions"),
                GetInt(e, "treeTuning", "versions"));
        }

        private static FoundationalSection LoadFoundational(JsonElement e)
        {
            RejectUnknown(e, new HashSet<string>(StringComparer.Ordinal) { "tree", "catalog" }, "foundational");
            var tree = GetObject(e, "tree", "foundational");
            RejectUnknown(tree, new HashSet<string>(StringComparer.Ordinal) { "id", "version" }, "foundational.tree");
            var cat = GetObject(e, "catalog", "foundational");
            RejectUnknown(cat, new HashSet<string>(StringComparer.Ordinal)
                { "id", "version", "versionTag", "members", "exclusions" }, "foundational.catalog");
            return new FoundationalSection(
                new VersionedRef(GetString(tree, "id", "foundational.tree"), GetInt(tree, "version", "foundational.tree")),
                new FoundationalCatalogDef(
                    GetString(cat, "id", "foundational.catalog"),
                    GetInt(cat, "version", "foundational.catalog"),
                    GetString(cat, "versionTag", "foundational.catalog"),
                    GetStringArray(cat, "members", "foundational.catalog"),
                    GetStringArray(cat, "exclusions", "foundational.catalog")));
        }

        private static FacetDef LoadFacet(JsonElement e, string path)
        {
            RejectUnknown(e, new HashSet<string>(StringComparer.Ordinal) { "id", "category", "candidateTreeIds" }, path);
            return new FacetDef(
                GetString(e, "id", path),
                GetString(e, "category", path),
                GetStringArray(e, "candidateTreeIds", path));
        }

        private static TreeDef LoadTree(JsonElement e, string path)
        {
            RejectUnknown(e, new HashSet<string>(StringComparer.Ordinal) { "id", "version", "category", "tuning" }, path);
            var tuning = GetObject(e, "tuning", path);
            RejectUnknown(tuning, new HashSet<string>(StringComparer.Ordinal)
                { "initialLevel", "unlockCostStep", "cumulativeBpThresholds" }, path + ".tuning");
            return new TreeDef(
                GetString(e, "id", path),
                GetInt(e, "version", path),
                GetString(e, "category", path),
                new TreeTuningDef(
                    GetInt(tuning, "initialLevel", path + ".tuning"),
                    GetInt(tuning, "unlockCostStep", path + ".tuning"),
                    GetIntArray(tuning, "cumulativeBpThresholds", path + ".tuning")));
        }

        private static NodeDef LoadNode(JsonElement e, string path)
        {
            RejectUnknown(e, new HashSet<string>(StringComparer.Ordinal)
            {
                "id", "version", "treeId", "treeLevel", "displayLabel", "outcomeType",
                "ownership", "firstBuildStatus", "pricing", "requirements",
            }, path);
            var pricing = GetObject(e, "pricing", path);
            RejectUnknown(pricing, new HashSet<string>(StringComparer.Ordinal) { "developmentBp", "purchaseAp" }, path + ".pricing");
            var req = GetObject(e, "requirements", path);
            RejectUnknown(req, new HashSet<string>(StringComparer.Ordinal)
            {
                "requiresCommittedTree", "requiresCurrentContentVersion", "minActiveStoneLevel",
                "minTreeLevel", "requiresActiveAttunement", "requiresOfferedStatus",
                "requiresDevelopmentAuthority", "requiresResponsibilityRange", "priorOfferedNodeIds",
            }, path + ".requirements");
            return new NodeDef(
                GetString(e, "id", path),
                GetInt(e, "version", path),
                GetString(e, "treeId", path),
                GetInt(e, "treeLevel", path),
                GetString(e, "displayLabel", path),
                GetString(e, "outcomeType", path),
                GetString(e, "ownership", path),
                GetString(e, "firstBuildStatus", path),
                new NodePricingDef(GetNullableInt(pricing, "developmentBp", path + ".pricing"),
                                   GetNullableInt(pricing, "purchaseAp", path + ".pricing")),
                new NodeRequirementsDef(
                    GetBool(req, "requiresCommittedTree", path + ".requirements"),
                    GetBool(req, "requiresCurrentContentVersion", path + ".requirements"),
                    GetInt(req, "minActiveStoneLevel", path + ".requirements"),
                    GetInt(req, "minTreeLevel", path + ".requirements"),
                    GetBool(req, "requiresActiveAttunement", path + ".requirements"),
                    GetBool(req, "requiresOfferedStatus", path + ".requirements"),
                    GetBool(req, "requiresDevelopmentAuthority", path + ".requirements"),
                    GetBool(req, "requiresResponsibilityRange", path + ".requirements"),
                    GetStringArray(req, "priorOfferedNodeIds", path + ".requirements")));
        }

        // ── strict readers ─────────────────────────────────────────────────────────────────────
        private static void RejectUnknown(JsonElement obj, HashSet<string> allowed, string path)
        {
            foreach (var p in obj.EnumerateObject())
                if (!allowed.Contains(p.Name))
                    throw new JsonLoadException($"Unknown property '{JoinPath(path, p.Name)}' is not allowed.");
        }

        private static JsonElement Require(JsonElement obj, string name, string path)
        {
            if (!obj.TryGetProperty(name, out var v))
                throw new JsonLoadException($"Missing required property '{JoinPath(path, name)}'.");
            return v;
        }

        private static string GetString(JsonElement obj, string name, string path)
        {
            var v = Require(obj, name, path);
            if (v.ValueKind != JsonValueKind.String)
                throw new JsonLoadException($"Property '{JoinPath(path, name)}' must be a string.");
            return v.GetString()!;
        }

        private static int GetInt(JsonElement obj, string name, string path)
        {
            var v = Require(obj, name, path);
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var i))
                throw new JsonLoadException($"Property '{JoinPath(path, name)}' must be an integer.");
            return i;
        }

        private static int? GetNullableInt(JsonElement obj, string name, string path)
        {
            var v = Require(obj, name, path);
            if (v.ValueKind == JsonValueKind.Null) return null;
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var i))
                throw new JsonLoadException($"Property '{JoinPath(path, name)}' must be an integer or null.");
            return i;
        }

        private static bool GetBool(JsonElement obj, string name, string path)
        {
            var v = Require(obj, name, path);
            if (v.ValueKind != JsonValueKind.True && v.ValueKind != JsonValueKind.False)
                throw new JsonLoadException($"Property '{JoinPath(path, name)}' must be a boolean.");
            return v.GetBoolean();
        }

        private static JsonElement GetObject(JsonElement obj, string name, string path)
        {
            var v = Require(obj, name, path);
            if (v.ValueKind != JsonValueKind.Object)
                throw new JsonLoadException($"Property '{JoinPath(path, name)}' must be an object.");
            return v;
        }

        private static IReadOnlyList<string> GetStringArray(JsonElement obj, string name, string path)
        {
            var v = Require(obj, name, path);
            if (v.ValueKind != JsonValueKind.Array)
                throw new JsonLoadException($"Property '{JoinPath(path, name)}' must be an array.");
            var list = new List<string>();
            int i = 0;
            foreach (var item in v.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    throw new JsonLoadException($"Property '{JoinPath(path, name)}[{i}]' must be a string.");
                list.Add(item.GetString()!);
                i++;
            }
            return list;
        }

        private static IReadOnlyList<int> GetIntArray(JsonElement obj, string name, string path)
        {
            var v = Require(obj, name, path);
            if (v.ValueKind != JsonValueKind.Array)
                throw new JsonLoadException($"Property '{JoinPath(path, name)}' must be an array.");
            var list = new List<int>();
            int i = 0;
            foreach (var item in v.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var n))
                    throw new JsonLoadException($"Property '{JoinPath(path, name)}[{i}]' must be an integer.");
                list.Add(n);
                i++;
            }
            return list;
        }

        private static IReadOnlyList<T> LoadArray<T>(JsonElement obj, string name, Func<JsonElement, string, T> loadItem)
        {
            var v = Require(obj, name, "");
            if (v.ValueKind != JsonValueKind.Array)
                throw new JsonLoadException($"Property '{name}' must be an array.");
            var list = new List<T>();
            int i = 0;
            foreach (var item in v.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    throw new JsonLoadException($"Property '{name}[{i}]' must be an object.");
                list.Add(loadItem(item, $"{name}[{i}]"));
                i++;
            }
            return list;
        }

        private static string JoinPath(string path, string name) =>
            string.IsNullOrEmpty(path) ? name : path + "." + name;

        // ── Serialize (canonical) ────────────────────────────────────────────────────────────────
        // Emits a fixed property order, 2-space indent, LF newlines, and a single trailing newline.
        // The $schema hint is emitted first so the checked-in asset round-trips byte-for-byte.
        public static string Serialize(StoneContentDocument d, string? schemaRef = "./homestead-stone.content.schema.json")
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            var w = new CanonicalWriter();
            w.OpenObject();
            if (schemaRef != null) w.Prop("$schema", schemaRef);
            w.Prop("formatVersion", d.FormatVersion);
            w.Prop("assetId", d.AssetId);
            w.Prop("family", d.Family);
            w.Prop("variant", d.Variant);

            w.PropObject("versions");
            w.Prop("contentRegistry", d.Versions.ContentRegistry);
            w.Prop("foundationalCatalog", d.Versions.FoundationalCatalog);
            w.Prop("facetPalette", d.Versions.FacetPalette);
            w.Prop("treeTuning", d.Versions.TreeTuning);
            w.CloseObject();

            w.PropObject("foundational");
            w.PropObject("tree");
            w.Prop("id", d.Foundational.Tree.Id);
            w.Prop("version", d.Foundational.Tree.Version);
            w.CloseObject();
            w.PropObject("catalog");
            w.Prop("id", d.Foundational.Catalog.Id);
            w.Prop("version", d.Foundational.Catalog.Version);
            w.Prop("versionTag", d.Foundational.Catalog.VersionTag);
            w.PropStringArray("members", d.Foundational.Catalog.Members);
            w.PropStringArray("exclusions", d.Foundational.Catalog.Exclusions);
            w.CloseObject();
            w.CloseObject();

            w.PropArray("facets");
            foreach (var f in d.Facets)
            {
                w.OpenArrayObject();
                w.Prop("id", f.Id);
                w.Prop("category", f.Category);
                w.PropStringArray("candidateTreeIds", f.CandidateTreeIds);
                w.CloseObject();
            }
            w.CloseArray();

            w.PropArray("trees");
            foreach (var t in d.Trees)
            {
                w.OpenArrayObject();
                w.Prop("id", t.Id);
                w.Prop("version", t.Version);
                w.Prop("category", t.Category);
                w.PropObject("tuning");
                w.Prop("initialLevel", t.Tuning.InitialLevel);
                w.Prop("unlockCostStep", t.Tuning.UnlockCostStep);
                w.PropIntArray("cumulativeBpThresholds", t.Tuning.CumulativeBpThresholds);
                w.CloseObject();
                w.CloseObject();
            }
            w.CloseArray();

            w.PropArray("nodes");
            foreach (var n in d.Nodes)
            {
                w.OpenArrayObject();
                w.Prop("id", n.Id);
                w.Prop("version", n.Version);
                w.Prop("treeId", n.TreeId);
                w.Prop("treeLevel", n.TreeLevel);
                w.Prop("displayLabel", n.DisplayLabel);
                w.Prop("outcomeType", n.OutcomeType);
                w.Prop("ownership", n.Ownership);
                w.Prop("firstBuildStatus", n.FirstBuildStatus);
                w.PropObject("pricing");
                w.PropNullableInt("developmentBp", n.Pricing.DevelopmentBp);
                w.PropNullableInt("purchaseAp", n.Pricing.PurchaseAp);
                w.CloseObject();
                w.PropObject("requirements");
                w.Prop("requiresCommittedTree", n.Requirements.RequiresCommittedTree);
                w.Prop("requiresCurrentContentVersion", n.Requirements.RequiresCurrentContentVersion);
                w.Prop("minActiveStoneLevel", n.Requirements.MinActiveStoneLevel);
                w.Prop("minTreeLevel", n.Requirements.MinTreeLevel);
                w.Prop("requiresActiveAttunement", n.Requirements.RequiresActiveAttunement);
                w.Prop("requiresOfferedStatus", n.Requirements.RequiresOfferedStatus);
                w.Prop("requiresDevelopmentAuthority", n.Requirements.RequiresDevelopmentAuthority);
                w.Prop("requiresResponsibilityRange", n.Requirements.RequiresResponsibilityRange);
                w.PropStringArray("priorOfferedNodeIds", n.Requirements.PriorOfferedNodeIds);
                w.CloseObject();
                w.CloseObject();
            }
            w.CloseArray();

            w.CloseObject();
            return w.Build();
        }

        // A tiny deterministic JSON writer: 2-space indent, LF, trailing newline. Hand-rolled so
        // formatting is fixed by contract, not by a serializer's changeable defaults.
        private sealed class CanonicalWriter
        {
            private readonly StringBuilder _sb = new StringBuilder();
            private int _indent;
            // Stack of "does the current container already have a member?" to place commas.
            private readonly List<bool> _hasMember = new List<bool> { false };

            private void Line(string text)
            {
                _sb.Append('\n');
                _sb.Append(new string(' ', _indent * 2));
                _sb.Append(text);
            }

            private void CommaIfNeeded()
            {
                if (_hasMember[_hasMember.Count - 1]) _sb.Append(',');
                _hasMember[_hasMember.Count - 1] = true;
            }

            private void Push() => _hasMember.Add(false);
            private void Pop() => _hasMember.RemoveAt(_hasMember.Count - 1);

            public void OpenObject()
            {
                _sb.Append('{');
                _indent++;
                Push();
            }

            public void CloseObject()
            {
                _indent--;
                Pop();
                Line("}");
            }

            public void PropObject(string name)
            {
                CommaIfNeeded();
                Line(Quote(name) + ": {");
                _indent++;
                Push();
            }

            public void PropArray(string name)
            {
                CommaIfNeeded();
                Line(Quote(name) + ": [");
                _indent++;
                Push();
            }

            public void CloseArray()
            {
                _indent--;
                Pop();
                Line("]");
            }

            public void OpenArrayObject()
            {
                CommaIfNeeded();
                Line("{");
                _indent++;
                Push();
            }

            public void Prop(string name, string value)
            {
                CommaIfNeeded();
                Line(Quote(name) + ": " + Quote(value));
            }

            public void Prop(string name, int value)
            {
                CommaIfNeeded();
                Line(Quote(name) + ": " + value.ToString(CultureInfo.InvariantCulture));
            }

            public void Prop(string name, bool value)
            {
                CommaIfNeeded();
                Line(Quote(name) + ": " + (value ? "true" : "false"));
            }

            public void PropNullableInt(string name, int? value)
            {
                CommaIfNeeded();
                Line(Quote(name) + ": " + (value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "null"));
            }

            public void PropStringArray(string name, IReadOnlyList<string> values)
            {
                CommaIfNeeded();
                if (values.Count == 0)
                {
                    Line(Quote(name) + ": []");
                    return;
                }
                Line(Quote(name) + ": [");
                _indent++;
                for (int i = 0; i < values.Count; i++)
                    Line(Quote(values[i]) + (i < values.Count - 1 ? "," : ""));
                _indent--;
                Line("]");
            }

            public void PropIntArray(string name, IReadOnlyList<int> values)
            {
                CommaIfNeeded();
                if (values.Count == 0)
                {
                    Line(Quote(name) + ": []");
                    return;
                }
                Line(Quote(name) + ": [");
                _indent++;
                for (int i = 0; i < values.Count; i++)
                    Line(values[i].ToString(CultureInfo.InvariantCulture) + (i < values.Count - 1 ? "," : ""));
                _indent--;
                Line("]");
            }

            public string Build()
            {
                // The opening brace was written without a leading newline; strip the leading '\n'
                // that Line() would have added to the first member is not present because OpenObject
                // appends '{' directly. Ensure a single trailing newline.
                return _sb.ToString() + "\n";
            }

            private static string Quote(string s)
            {
                var b = new StringBuilder(s.Length + 2);
                b.Append('"');
                foreach (var c in s)
                {
                    switch (c)
                    {
                        case '"': b.Append("\\\""); break;
                        case '\\': b.Append("\\\\"); break;
                        case '\b': b.Append("\\b"); break;
                        case '\f': b.Append("\\f"); break;
                        case '\n': b.Append("\\n"); break;
                        case '\r': b.Append("\\r"); break;
                        case '\t': b.Append("\\t"); break;
                        default:
                            if (c < 0x20)
                                b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                            else
                                b.Append(c);
                            break;
                    }
                }
                b.Append('"');
                return b.ToString();
            }
        }
    }
}
