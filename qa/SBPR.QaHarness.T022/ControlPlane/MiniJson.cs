// Minimal, strict, dependency-free JSON reader/writer for control envelopes (ADR-0009
// §3.2) — M2R runtime wiring.
//
// The SDK-shielded net48 helper assembly references System.* only (no System.Text.Json,
// no Newtonsoft), and mirroring RequestHmac/LoopbackFrameParser's discipline we do NOT
// pull a serializer: a control frame payload is a small, flat JSON object with string /
// number / bool fields plus one nested flat "args" object of primitive values. This is a
// deliberately tiny, bounded, allocation-frugal reader that parses exactly that shape and
// nothing more — it rejects anything it does not understand (nesting beyond one args
// level, arrays, exponents in an unexpected place, trailing garbage) fail-closed rather
// than silently coercing. Never throws on malformed input: TryParse returns false.
//
// Engine-free (System.* only), so it link-compiles into the net8 xUnit suite and the
// net48 helper from one source, and carries no game/BepInEx dependency.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SBPR.QaHarness.T022.Core.ControlPlane
{
    /// <summary>A parsed JSON scalar: string, long, double, bool, or null. Bounded value union.</summary>
    public sealed class JsonScalar
    {
        public enum Kind { Null = 0, String = 1, Long = 2, Double = 3, Bool = 4 }

        public Kind ScalarKind { get; }
        public string? Str { get; }
        public long Int64 { get; }
        public double Dbl { get; }
        public bool Boolean { get; }

        private JsonScalar(Kind kind, string? s, long i, double d, bool b)
        {
            ScalarKind = kind; Str = s; Int64 = i; Dbl = d; Boolean = b;
        }

        public static readonly JsonScalar NullValue = new(Kind.Null, null, 0, 0, false);
        public static JsonScalar Of(string s) => new(Kind.String, s, 0, 0, false);
        public static JsonScalar Of(long i) => new(Kind.Long, null, i, 0, false);
        public static JsonScalar Of(double d) => new(Kind.Double, null, 0, d, false);
        public static JsonScalar Of(bool b) => new(Kind.Bool, null, 0, 0, b);

        /// <summary>The value boxed for RequestEnvelope.Args (string / long / double / bool / null).</summary>
        public object? AsArgValue() => ScalarKind switch
        {
            Kind.String => Str,
            Kind.Long => Int64,
            Kind.Double => Dbl,
            Kind.Bool => Boolean,
            _ => null,
        };
    }

    /// <summary>
    /// A strict reader for the bounded control-envelope JSON shape: one flat top-level object
    /// whose values are scalars or a single nested flat object named by the caller (args).
    /// </summary>
    public sealed class MiniJsonObject
    {
        private readonly Dictionary<string, JsonScalar> _scalars;
        private readonly Dictionary<string, MiniJsonObject> _objects;

        internal MiniJsonObject(
            Dictionary<string, JsonScalar> scalars,
            Dictionary<string, MiniJsonObject> objects)
        {
            _scalars = scalars;
            _objects = objects;
        }

        public bool TryGetString(string key, out string value)
        {
            value = string.Empty;
            if (_scalars.TryGetValue(key, out var s) && s.ScalarKind == JsonScalar.Kind.String)
            {
                value = s.Str ?? string.Empty;
                return true;
            }
            return false;
        }

        public bool TryGetLong(string key, out long value)
        {
            value = 0;
            if (_scalars.TryGetValue(key, out var s) && s.ScalarKind == JsonScalar.Kind.Long)
            {
                value = s.Int64;
                return true;
            }
            return false;
        }

        public bool TryGetObject(string key, out MiniJsonObject value)
        {
            if (_objects.TryGetValue(key, out var o))
            {
                value = o;
                return true;
            }
            value = Empty;
            return false;
        }

        /// <summary>Flat scalar entries (used to build RequestEnvelope.Args). Nested objects are not exposed here.</summary>
        public IReadOnlyDictionary<string, JsonScalar> Scalars => _scalars;

        public static readonly MiniJsonObject Empty =
            new(new Dictionary<string, JsonScalar>(StringComparer.Ordinal),
                new Dictionary<string, MiniJsonObject>(StringComparer.Ordinal));
    }

    /// <summary>Strict, non-throwing JSON parser for the bounded control-envelope shape.</summary>
    public static class MiniJson
    {
        /// <summary>Hard cap on total input length — a control envelope is small; anything larger is refused.</summary>
        public const int MaxInputChars = 64 * 1024;

        /// <summary>
        /// Parse a top-level JSON object. Values may be scalars (string/number/bool/null) or a
        /// single level of nested flat object. Returns false on any malformed input, deeper
        /// nesting, arrays, or trailing content. Never throws.
        /// </summary>
        public static bool TryParse(string? text, out MiniJsonObject obj)
        {
            obj = MiniJsonObject.Empty;
            if (text == null || text.Length == 0 || text.Length > MaxInputChars) return false;
            var p = new Parser(text);
            try
            {
                p.SkipWs();
                if (!p.TryReadObject(depth: 0, out var parsed)) return false;
                p.SkipWs();
                if (!p.AtEnd) return false; // no trailing garbage
                obj = parsed;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private sealed class Parser
        {
            private const int MaxDepth = 1; // top-level object may contain at most one nested object level
            private readonly string _s;
            private int _i;

            public Parser(string s) { _s = s; _i = 0; }

            public bool AtEnd => _i >= _s.Length;

            public void SkipWs()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') _i++;
                    else break;
                }
            }

            public bool TryReadObject(int depth, out MiniJsonObject obj)
            {
                obj = MiniJsonObject.Empty;
                if (depth > MaxDepth) return false;
                if (AtEnd || _s[_i] != '{') return false;
                _i++; // consume '{'
                var scalars = new Dictionary<string, JsonScalar>(StringComparer.Ordinal);
                var objects = new Dictionary<string, MiniJsonObject>(StringComparer.Ordinal);
                SkipWs();
                if (!AtEnd && _s[_i] == '}') { _i++; obj = new MiniJsonObject(scalars, objects); return true; }
                while (true)
                {
                    SkipWs();
                    if (!TryReadString(out var key)) return false;
                    SkipWs();
                    if (AtEnd || _s[_i] != ':') return false;
                    _i++;
                    SkipWs();
                    if (AtEnd) return false;
                    char c = _s[_i];
                    if (c == '{')
                    {
                        if (!TryReadObject(depth + 1, out var nested)) return false;
                        if (scalars.ContainsKey(key) || objects.ContainsKey(key)) return false;
                        objects.Add(key, nested);
                    }
                    else
                    {
                        if (!TryReadScalar(out var scalar)) return false;
                        if (objects.ContainsKey(key) || scalars.ContainsKey(key)) return false;
                        scalars.Add(key, scalar);
                    }
                    SkipWs();
                    if (AtEnd) return false;
                    char sep = _s[_i];
                    if (sep == ',') { _i++; continue; }
                    if (sep == '}') { _i++; break; }
                    return false;
                }
                obj = new MiniJsonObject(scalars, objects);
                return true;
            }

            private bool TryReadScalar(out JsonScalar scalar)
            {
                scalar = JsonScalar.NullValue;
                if (AtEnd) return false;
                char c = _s[_i];
                if (c == '"')
                {
                    if (!TryReadString(out var s)) return false;
                    scalar = JsonScalar.Of(s);
                    return true;
                }
                if (c == 't' || c == 'f')
                {
                    if (Match("true")) { scalar = JsonScalar.Of(true); return true; }
                    if (Match("false")) { scalar = JsonScalar.Of(false); return true; }
                    return false;
                }
                if (c == 'n')
                {
                    if (Match("null")) { scalar = JsonScalar.NullValue; return true; }
                    return false;
                }
                return TryReadNumber(out scalar);
            }

            private bool Match(string literal)
            {
                if (_i + literal.Length > _s.Length) return false;
                for (int k = 0; k < literal.Length; k++)
                    if (_s[_i + k] != literal[k]) return false;
                _i += literal.Length;
                return true;
            }

            private bool TryReadNumber(out JsonScalar scalar)
            {
                scalar = JsonScalar.NullValue;
                int start = _i;
                bool isDouble = false;
                if (!AtEnd && _s[_i] == '-') _i++;
                int digits = 0;
                while (!AtEnd && _s[_i] >= '0' && _s[_i] <= '9') { _i++; digits++; }
                if (digits == 0) return false;
                if (!AtEnd && _s[_i] == '.')
                {
                    isDouble = true; _i++;
                    int frac = 0;
                    while (!AtEnd && _s[_i] >= '0' && _s[_i] <= '9') { _i++; frac++; }
                    if (frac == 0) return false;
                }
                if (!AtEnd && (_s[_i] == 'e' || _s[_i] == 'E'))
                {
                    isDouble = true; _i++;
                    if (!AtEnd && (_s[_i] == '+' || _s[_i] == '-')) _i++;
                    int exp = 0;
                    while (!AtEnd && _s[_i] >= '0' && _s[_i] <= '9') { _i++; exp++; }
                    if (exp == 0) return false;
                }
                string token = _s.Substring(start, _i - start);
                if (!isDouble && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                {
                    scalar = JsonScalar.Of(l);
                    return true;
                }
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    if (double.IsNaN(d) || double.IsInfinity(d)) return false;
                    scalar = JsonScalar.Of(d);
                    return true;
                }
                return false;
            }

            private bool TryReadString(out string value)
            {
                value = string.Empty;
                if (AtEnd || _s[_i] != '"') return false;
                _i++;
                var sb = new StringBuilder();
                while (!AtEnd)
                {
                    char c = _s[_i++];
                    if (c == '"') { value = sb.ToString(); return true; }
                    if (c == '\\')
                    {
                        if (AtEnd) return false;
                        char e = _s[_i++];
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
                                if (_i + 4 > _s.Length) return false;
                                if (!ushort.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber,
                                        CultureInfo.InvariantCulture, out var code)) return false;
                                sb.Append((char)code);
                                _i += 4;
                                break;
                            default: return false;
                        }
                    }
                    else if (c < 0x20)
                    {
                        return false; // raw control char in a string is malformed
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                return false; // unterminated string
            }
        }

        /// <summary>Escape a string for embedding in the receipt JSON writer below.</summary>
        public static string EscapeString(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s!.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
