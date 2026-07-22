// ============================================================================
//  QA-M4 bounded/redacted receipts + verdict firewall (ADR-0009 §6) — M4.
// ----------------------------------------------------------------------------
//  RedactedReceipt — the DESCRIPTIVE primitive-fact receipt the helper emits per
//  request. It NEVER contains a product PASS/FAIL: Outcome is a mechanical enum
//  with no PASS/FAIL member (only the external runner composes a verdict, §6).
//  Observed carries raw facts (quality, tooltip text, present KEY names) but
//  NEVER a raw m_customData VALUE (those are represented only by a bounded,
//  non-reversible digest so a signature value can't leak into evidence — threat
//  T4/T5). A byte budget caps the serialized size so a hostile/oversized
//  observation can't blow the receipt channel.
//
//  Mirrors qa/prebuild-m4/contracts.py RedactedReceipt + evidence.py
//  {extract_observed_facts, redact_receipt, assert_no_product_verdict}
//  (reviewed t_d5a29850 prebuild). Engine-free: System.* only.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SBPR.QaHarness.T022.Core.Evidence
{
    /// <summary>Mechanical primitive-receipt outcome. Deliberately NO PASS/FAIL member (ADR-0009 §6).</summary>
    public enum ReceiptOutcome
    {
        Ok = 0,
        Rejected,
        Busy,
        Timeout,
        Cancelled,
    }

    /// <summary>
    /// A primitive-fact receipt (ADR-0009 §6). Immutable value object. <see cref="Observed"/> is an
    /// ordinal string->object map of raw facts; it MUST NOT carry a verdict-shaped key (asserted by
    /// <see cref="ReceiptFirewall.AssertNoProductVerdict"/>) and MUST NOT carry a raw custom-data value map.
    /// </summary>
    public sealed class RedactedReceipt
    {
        /// <summary>Observed-map keys that would smuggle a product verdict — forbidden (case-insensitive).</summary>
        public static readonly IReadOnlyList<string> ForbiddenObservedKeys =
            new[] { "pass", "fail", "verdict", "at_result", "accepted" };

        public string RequestId { get; }
        public string Verb { get; }
        public string Role { get; }
        public long WorldUid { get; }
        public string Nonce { get; }
        public long Seq { get; }
        public long ConnectionGeneration { get; }
        public long TsUnixMs { get; }
        public ReceiptOutcome Outcome { get; }

        /// <summary>Descriptive facts only (never a verdict; never a raw secret value).</summary>
        public IReadOnlyDictionary<string, object?> Observed { get; }

        /// <summary>The evidence reason when <see cref="Outcome"/> is Rejected; <see cref="EvidenceReason.None"/> otherwise.</summary>
        public EvidenceReason RejectReason { get; }

        public RedactedReceipt(
            string requestId, string verb, string role, long worldUid, string nonce, long seq,
            long connectionGeneration, long tsUnixMs, ReceiptOutcome outcome,
            IReadOnlyDictionary<string, object?>? observed = null,
            EvidenceReason rejectReason = EvidenceReason.None)
        {
            if (string.IsNullOrEmpty(requestId)) throw new ArgumentException("requestId must be non-empty.", nameof(requestId));
            if (string.IsNullOrEmpty(verb)) throw new ArgumentException("verb must be non-empty.", nameof(verb));
            RequestId = requestId;
            Verb = verb;
            Role = role ?? string.Empty;
            WorldUid = worldUid;
            Nonce = nonce ?? string.Empty;
            Seq = seq;
            ConnectionGeneration = connectionGeneration;
            TsUnixMs = tsUnixMs;
            Outcome = outcome;
            Observed = observed ?? new Dictionary<string, object?>(StringComparer.Ordinal);
            RejectReason = rejectReason;
        }

        /// <summary>Return a copy with a different observed map (used by the redactor).</summary>
        public RedactedReceipt WithObserved(IReadOnlyDictionary<string, object?> observed)
            => new RedactedReceipt(RequestId, Verb, Role, WorldUid, Nonce, Seq, ConnectionGeneration,
                TsUnixMs, Outcome, observed, RejectReason);
    }

    /// <summary>Raised when helper-side output would smuggle a product verdict into a receipt (ADR-0009 §6).</summary>
    public sealed class HelperVerdictException : Exception
    {
        public HelperVerdictException(string message) : base(message) { }
    }

    /// <summary>
    /// The receipt firewall + observed-fact construction + bounded redaction (ADR-0009 §6).
    /// Pure functions; no world access.
    /// </summary>
    public static class ReceiptFirewall
    {
        /// <summary>
        /// Build the observed payload from raw item state. RAW FACTS ONLY (threat T4): prefab,
        /// quality, sorted present KEY names, and — if provided — the verbatim tooltip string. It
        /// NEVER emits a raw custom-data VALUE; each value is represented by a bounded digest so a
        /// signature value cannot leak into evidence (threat T5).
        /// </summary>
        public static IReadOnlyDictionary<string, object?> ExtractObservedFacts(
            string prefab, int quality, IReadOnlyDictionary<string, string>? customData, string? tooltipText = null)
        {
            var facts = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["prefab"] = prefab,
                ["quality"] = quality,
            };
            var keyNames = new List<string>();
            var digests = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            if (customData != null)
            {
                foreach (var kv in customData)
                {
                    keyNames.Add(kv.Key);
                    digests[kv.Key] = BoundedDigest(kv.Value);
                }
            }
            keyNames.Sort(StringComparer.Ordinal);
            facts["custom_key_names"] = keyNames.ToArray();
            facts["custom_value_digests"] = digests;
            if (tooltipText != null) facts["tooltip_text"] = tooltipText;
            return facts;
        }

        /// <summary>
        /// A short, non-reversible-enough descriptor: length + first/last char. Deliberately NOT the
        /// raw value and NOT a full cryptographic hash (which could itself be a copyable signature
        /// token). Just enough to prove presence/absence/change of a value without surfacing it.
        /// </summary>
        public static string BoundedDigest(string? value, int cap = 12)
        {
            string v = value ?? string.Empty;
            int n = v.Length;
            string head = n > 0 ? v.Substring(0, 1) : string.Empty;
            string tail = n > 0 ? v.Substring(n - 1, 1) : string.Empty;
            string label = "len=" + n.ToString(CultureInfo.InvariantCulture) + ";edge='" + head + "'..'" + tail + "'";
            int max = cap * 4;
            return label.Length <= max ? label : label.Substring(0, max);
        }

        /// <summary>
        /// Raise <see cref="HelperVerdictException"/> if a receipt smuggles a product verdict (ADR-0009 §6).
        /// Guards two ways: outcome is a mechanical enum (no PASS/FAIL member exists), and observed
        /// carries none of the forbidden verdict-shaped keys (case-insensitive).
        /// </summary>
        public static void AssertNoProductVerdict(RedactedReceipt receipt)
        {
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));
            var lowered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in receipt.Observed.Keys)
            {
                lowered.Add(k.ToLowerInvariant());
            }
            var hits = RedactedReceipt.ForbiddenObservedKeys.Where(f => lowered.Contains(f)).ToArray();
            if (hits.Length > 0)
                throw new HelperVerdictException(
                    "receipt observed carries product-verdict key(s): " + string.Join(",", hits));
        }

        /// <summary>
        /// Enforce the receipt firewall before emission: assert no verdict, strip any raw custom-data
        /// value map that leaked in, and enforce a serialized byte budget by collapsing an oversized
        /// tooltip to a length marker (a hostile giant tooltip can't blow the channel). Returns a
        /// firewalled copy.
        /// </summary>
        public static RedactedReceipt Redact(RedactedReceipt receipt, int byteBudget = 4096)
        {
            AssertNoProductVerdict(receipt);
            var observed = new Dictionary<string, object?>(receipt.Observed, StringComparer.Ordinal);
            // Never allow a raw values map to ride along.
            observed.Remove("custom_values");
            observed.Remove("custom_data");

            if (ApproximateSize(observed) > byteBudget && observed.TryGetValue("tooltip_text", out var tt))
            {
                string s = tt?.ToString() ?? string.Empty;
                observed["tooltip_text"] = "<redacted:len=" + s.Length.ToString(CultureInfo.InvariantCulture) + ">";
            }
            return receipt.WithObserved(observed);
        }

        // A cheap, engine-free serialized-size estimate (no JSON dependency): sum key + rendered-value
        // lengths. Deterministic and monotone in payload growth — enough to bound the receipt.
        private static int ApproximateSize(IReadOnlyDictionary<string, object?> observed)
        {
            int total = 0;
            foreach (var kv in observed)
            {
                total += kv.Key.Length + Render(kv.Value).Length + 4;
            }
            return total;
        }

        private static string Render(object? value)
        {
            switch (value)
            {
                case null: return "null";
                case string s: return s;
                case IEnumerable<string> arr: return string.Join(",", arr);
                case System.Collections.IDictionary d:
                {
                    var sb = new StringBuilder();
                    foreach (System.Collections.DictionaryEntry e in d)
                        sb.Append(e.Key).Append('=').Append(e.Value).Append(';');
                    return sb.ToString();
                }
                default: return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }
    }
}
