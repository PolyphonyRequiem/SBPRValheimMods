using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain;

namespace SBPR.Niflheim.HomesteadStones.Features.HomesteadStone
{
    /// <summary>
    /// net48 loader for the checked-in <see cref="HomesteadStaticGeometryCatalog"/> (R6 Blocker 1). The
    /// catalog JSON (the same schema the offline extractor emits and the tests validate) ships as an EMBEDDED
    /// resource in the mod assembly, so production reads authored geometry from a pinned artifact — never
    /// from a live LocationProxy child hierarchy. The semantic-hash pin is enforced at load: a drifted
    /// catalog throws and realization stays disabled rather than seating against changed geometry.
    ///
    /// net48's BCL has no System.Text.Json, and a BepInEx plugin should not drag a JSON NuGet chain, so this
    /// uses a tiny engine-free scanner scoped to the fixed catalog schema (objects, arrays, strings, numbers).
    /// It parses the EXACT bytes the tests validate, so the pinned artifact is identical across both.
    /// </summary>
    internal static class HomesteadStaticGeometryCatalogLoader
    {
        internal const string EmbeddedResourceName =
            "SBPR.Niflheim.HomesteadStones.Assets.homestead-static-geometry.json";

        private static HomesteadStaticGeometryCatalog? cached;

        /// <summary>Load (once) and verify the embedded catalog. Throws on a hash-pin mismatch or a missing
        /// resource so the caller can disable realization fail-closed.</summary>
        internal static HomesteadStaticGeometryCatalog Load()
        {
            if (cached != null) return cached;
            var assembly = typeof(HomesteadStaticGeometryCatalogLoader).Assembly;
            using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
                ?? throw new FileNotFoundException(
                    $"Embedded static geometry catalog '{EmbeddedResourceName}' not found in {assembly.GetName().Name}.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var json = reader.ReadToEnd();
            cached = Parse(json);
            return cached;
        }

        /// <summary>Engine-free parse of the catalog schema into hash-verified host rows. Exposed for tests so
        /// the SAME parser validates the fixture that production ships.</summary>
        internal static HomesteadStaticGeometryCatalog Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var value = MiniJson.Parse(json);
            if (value is not Dictionary<string, object?> root)
                throw new InvalidOperationException("Catalog root is not a JSON object.");

            var schema = root.TryGetValue("schema", out var s) && s is string str ? str : string.Empty;
            var rows = new List<HomesteadCatalogHostRow>();
            if (!root.TryGetValue("hosts", out var hostsObj) || hostsObj is not Dictionary<string, object?> hosts)
                throw new InvalidOperationException("Catalog has no 'hosts' object.");

            foreach (var kv in hosts)
            {
                if (kv.Value is not Dictionary<string, object?> host) continue;
                if (!host.TryGetValue("colliders", out var collidersObj) || collidersObj is not List<object?> colliders) continue;
                var footprints = new List<StaticColliderFootprint>();
                foreach (var c in colliders)
                {
                    if (c is not Dictionary<string, object?> box) continue;
                    footprints.Add(new StaticColliderFootprint(
                        Num(box, "cx"), Num(box, "cz"), Num(box, "halfX"), Num(box, "halfZ")));
                }
                if (footprints.Count == 0) continue;   // generator host — manifest-only, not a catalog host
                var stored = host.TryGetValue("semanticHash", out var h) && h is string hs ? hs : string.Empty;
                rows.Add(new HomesteadCatalogHostRow(kv.Key, footprints, stored));
            }
            return HomesteadStaticGeometryCatalog.FromHostRows(schema, rows);
        }

        private static double Num(Dictionary<string, object?> obj, string key) =>
            obj.TryGetValue(key, out var v) && v is double d ? d : 0.0;

        /// <summary>Minimal, engine-free JSON reader. Supports objects, arrays, strings (with the standard
        /// escapes), numbers (as double), true/false/null. Sufficient for the machine-emitted catalog schema;
        /// not a general-purpose parser.</summary>
        private static class MiniJson
        {
            internal static object? Parse(string text)
            {
                var index = 0;
                var value = ParseValue(text, ref index);
                SkipWhitespace(text, ref index);
                if (index != text.Length)
                    throw new InvalidOperationException($"Trailing content in JSON at {index}.");
                return value;
            }

            private static object? ParseValue(string t, ref int i)
            {
                SkipWhitespace(t, ref i);
                if (i >= t.Length) throw new InvalidOperationException("Unexpected end of JSON.");
                var c = t[i];
                switch (c)
                {
                    case '{': return ParseObject(t, ref i);
                    case '[': return ParseArray(t, ref i);
                    case '"': return ParseString(t, ref i);
                    case 't': Expect(t, ref i, "true"); return true;
                    case 'f': Expect(t, ref i, "false"); return false;
                    case 'n': Expect(t, ref i, "null"); return null;
                    default: return ParseNumber(t, ref i);
                }
            }

            private static Dictionary<string, object?> ParseObject(string t, ref int i)
            {
                var obj = new Dictionary<string, object?>(StringComparer.Ordinal);
                i++;   // consume '{'
                SkipWhitespace(t, ref i);
                if (i < t.Length && t[i] == '}') { i++; return obj; }
                while (true)
                {
                    SkipWhitespace(t, ref i);
                    var key = ParseString(t, ref i);
                    SkipWhitespace(t, ref i);
                    if (i >= t.Length || t[i] != ':') throw new InvalidOperationException($"Expected ':' at {i}.");
                    i++;
                    obj[key] = ParseValue(t, ref i);
                    SkipWhitespace(t, ref i);
                    if (i >= t.Length) throw new InvalidOperationException("Unterminated object.");
                    if (t[i] == ',') { i++; continue; }
                    if (t[i] == '}') { i++; break; }
                    throw new InvalidOperationException($"Expected ',' or '}}' at {i}.");
                }
                return obj;
            }

            private static List<object?> ParseArray(string t, ref int i)
            {
                var list = new List<object?>();
                i++;   // consume '['
                SkipWhitespace(t, ref i);
                if (i < t.Length && t[i] == ']') { i++; return list; }
                while (true)
                {
                    list.Add(ParseValue(t, ref i));
                    SkipWhitespace(t, ref i);
                    if (i >= t.Length) throw new InvalidOperationException("Unterminated array.");
                    if (t[i] == ',') { i++; continue; }
                    if (t[i] == ']') { i++; break; }
                    throw new InvalidOperationException($"Expected ',' or ']' at {i}.");
                }
                return list;
            }

            private static string ParseString(string t, ref int i)
            {
                if (t[i] != '"') throw new InvalidOperationException($"Expected string at {i}.");
                i++;
                var sb = new StringBuilder();
                while (i < t.Length)
                {
                    var c = t[i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (i >= t.Length) break;
                        var e = t[i++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                var hex = t.Substring(i, 4);
                                sb.Append((char)ushort.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                                i += 4;
                                break;
                            default: throw new InvalidOperationException($"Bad escape '\\{e}' at {i}.");
                        }
                        continue;
                    }
                    sb.Append(c);
                }
                throw new InvalidOperationException("Unterminated string.");
            }

            private static double ParseNumber(string t, ref int i)
            {
                var start = i;
                while (i < t.Length && (char.IsDigit(t[i]) || t[i] == '-' || t[i] == '+' || t[i] == '.' || t[i] == 'e' || t[i] == 'E'))
                    i++;
                var token = t.Substring(start, i - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    throw new InvalidOperationException($"Bad number '{token}' at {start}.");
                return value;
            }

            private static void Expect(string t, ref int i, string literal)
            {
                if (i + literal.Length > t.Length || t.Substring(i, literal.Length) != literal)
                    throw new InvalidOperationException($"Expected '{literal}' at {i}.");
                i += literal.Length;
            }

            private static void SkipWhitespace(string t, ref int i)
            {
                while (i < t.Length && (t[i] == ' ' || t[i] == '\t' || t[i] == '\n' || t[i] == '\r')) i++;
            }
        }
    }
}
