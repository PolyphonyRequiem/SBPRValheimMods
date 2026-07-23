using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;

namespace SBPR.Niflheim.HomesteadStones.Application.Accounts
{
    // IAP-015 — the bounded wire contract for the live operator command surface (engine-free CLEAN core).
    //
    // The client sends a single delimited string over the direct per-peer ZRpc handler; the server parses
    // it here with hard bounds (length, arg count, arg width) so a malformed/oversized frame fails closed
    // BEFORE any service is touched. The payload carries ONLY: a wire version, a correlation id, an
    // operation id (for idempotent replay), a verb token, and bounded opaque selector args. It carries NO
    // authority — the delivering peer's server-observed admin context is the only authority (see
    // LiveOperatorCommandRouter). Response is a parallel delimited string carrying only opaque ids, coarse
    // statuses, result/receipt codes, and safe counts.
    //
    // Wire grammar (pipe-delimited, key=value pairs after the header):
    //   REQ:  v1|<correlationId>|<operationId>|<verb>|<arg0>|<arg1>...
    //   RESP: v1|<correlationId>|<verb>|<ok|reject>|<resultCode>|<k=v>|<k=v>...
    // The delimiter '|' and '=' are forbidden inside tokens (rejected on parse), so the framing is
    // unambiguous without a JSON dependency.

    /// <summary>The bounded set of verbs the live client surface exposes. Whole-fixture reset, arbitrary
    /// scoped reset, quarantine, raw-subject lookup, and journal editing are deliberately ABSENT.</summary>
    public enum OperatorVerb
    {
        Unknown = 0,
        OpenPilot,
        Inspect,
        Export,
        Disable,
        Delete,
        Purge,
        RetentionPurge,
        ClosePilot,
    }

    /// <summary>A parsed, bounded operator request. Never carries authority; the delivering peer's
    /// server-observed admin context is the only authority.</summary>
    public sealed class OperatorWireRequest
    {
        private readonly List<string> _args;

        private OperatorWireRequest(string correlationId, string operationId, string verbToken,
            OperatorVerb verb, List<string> args)
        {
            CorrelationId = correlationId;
            OperationId = operationId;
            VerbToken = verbToken;
            Verb = verb;
            _args = args;
        }

        public string CorrelationId { get; }
        public string OperationId { get; }
        public string VerbToken { get; }
        public OperatorVerb Verb { get; }
        public int ArgCount => _args.Count;
        public string Arg(int i) => i >= 0 && i < _args.Count ? _args[i] : string.Empty;

        /// <summary>The first arg parsed as an internal AccountId selector. Only accepts the opaque
        /// <c>acct-...</c> shape — a raw provider subject or free-form string is refused, so the live
        /// surface cannot be driven by a provider subject (task point 4).</summary>
        public bool TryGetAccountId(out PilotAccountId accountId)
        {
            accountId = default;
            if (_args.Count < 1) return false;
            string a = _args[0];
            if (string.IsNullOrEmpty(a) || !a.StartsWith("acct-", StringComparison.Ordinal)) return false;
            accountId = new PilotAccountId(a);
            return true;
        }

        /// <summary>Parse + bound a wire request. Returns false (fail closed) for a null/oversized/malformed
        /// frame; <paramref name="correlationId"/> is recovered when the header is intact so a rejection can
        /// still be correlated, else "unknown".</summary>
        public static bool TryParse(string? wire, out OperatorWireRequest request, out string correlationId)
        {
            request = null!;
            correlationId = "unknown";
            if (string.IsNullOrEmpty(wire)) return false;
            if (wire!.Length > LiveOperatorCommandRouter.MaxWireLength) return false;

            var parts = wire.Split('|');
            // v | corr | op | verb  (4 header fields) + up to MaxArgs args.
            if (parts.Length < 4) return false;
            if (parts.Length > 4 + LiveOperatorCommandRouter.MaxArgs) return false;

            if (!string.Equals(parts[0], LiveOperatorCommandRouter.WireVersion, StringComparison.Ordinal)) return false;

            string corr = parts[1];
            string opId = parts[2];
            string verbToken = parts[3];

            if (!IsBoundedToken(corr) || !IsBoundedToken(opId) || !IsBoundedToken(verbToken)) return false;
            correlationId = corr;

            var args = new List<string>();
            for (int i = 4; i < parts.Length; i++)
            {
                if (!IsBoundedToken(parts[i])) return false;
                args.Add(parts[i]);
            }

            var verb = ParseVerb(verbToken);
            request = new OperatorWireRequest(corr, opId, verbToken, verb, args);
            return true;
        }

        private static OperatorVerb ParseVerb(string token)
        {
            switch (token)
            {
                case "open-pilot": return OperatorVerb.OpenPilot;
                case "inspect": return OperatorVerb.Inspect;
                case "export": return OperatorVerb.Export;
                case "disable": return OperatorVerb.Disable;
                case "delete": return OperatorVerb.Delete;
                case "purge": return OperatorVerb.Purge;
                case "retention-purge": return OperatorVerb.RetentionPurge;
                case "close-pilot": return OperatorVerb.ClosePilot;
                default: return OperatorVerb.Unknown;
            }
        }

        /// <summary>A token is bounded: non-null, within MaxArgLength, and free of the framing delimiters
        /// and control characters (so the grammar stays unambiguous and no injection can smuggle a field).</summary>
        internal static bool IsBoundedToken(string s)
        {
            if (s == null) return false;
            if (s.Length > LiveOperatorCommandRouter.MaxArgLength) return false;
            foreach (char c in s)
            {
                if (c == '|' || c == '=' || c == '\n' || c == '\r' || c == '\0') return false;
                if (char.IsControl(c)) return false;
            }
            return true;
        }
    }

    /// <summary>A bounded, subject-free operator response. Every value is an opaque id, coarse status,
    /// result code, receipt/correlation id, or a safe count — never a raw subject/HMAC/token/path.</summary>
    public sealed class OperatorWireResponse
    {
        private readonly List<KeyValuePair<string, string>> _fields = new List<KeyValuePair<string, string>>();

        private OperatorWireResponse(string correlationId, string verbToken, bool accepted, string resultCode)
        {
            CorrelationId = correlationId ?? string.Empty;
            VerbToken = verbToken ?? string.Empty;
            Accepted = accepted;
            ResultCode = resultCode ?? string.Empty;
        }

        public string CorrelationId { get; }
        public string VerbToken { get; }
        public bool Accepted { get; }
        public string ResultCode { get; }
        public IReadOnlyList<KeyValuePair<string, string>> Fields => _fields;

        public static OperatorWireResponse Ok(string correlationId, string verbToken, string resultCode) =>
            new OperatorWireResponse(correlationId, verbToken, true, resultCode);

        public static OperatorWireResponse Reject(string correlationId, string verbToken, string resultCode) =>
            new OperatorWireResponse(correlationId, verbToken, false, resultCode);

        /// <summary>Add a bounded, subject-free field. Values that would break the wire grammar or exceed
        /// the bound are sanitized to a stable placeholder rather than leaked or truncated mid-token.</summary>
        public void Add(string key, string value)
        {
            _fields.Add(new KeyValuePair<string, string>(key, Sanitize(value)));
        }

        public string? Get(string key)
        {
            foreach (var f in _fields)
                if (string.Equals(f.Key, key, StringComparison.Ordinal)) return f.Value;
            return null;
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (!OperatorWireRequest.IsBoundedToken(value)) return "?";
            return value;
        }

        /// <summary>Serialize to the bounded wire response string.</summary>
        public string ToWire()
        {
            var sb = new StringBuilder();
            sb.Append(LiveOperatorCommandRouter.WireVersion).Append('|')
              .Append(CorrelationId).Append('|')
              .Append(VerbToken).Append('|')
              .Append(Accepted ? "ok" : "reject").Append('|')
              .Append(ResultCode);
            foreach (var f in _fields)
                sb.Append('|').Append(f.Key).Append('=').Append(f.Value);
            return sb.ToString();
        }
    }
}
