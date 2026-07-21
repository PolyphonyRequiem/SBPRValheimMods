using System;
using System.Collections.Generic;

namespace SBPR.Niflheim.HomesteadStones.Domain.Accounts
{
    // T022 privacy fix — operator-contact acceptance policy (engine-free CLEAN-side core).
    //
    // The privacy disclosure presented before a real QA subject is provisioned MUST name a
    // routable operator contact channel (AIP-FR-002/025). A non-routable placeholder such as
    // `pilot-ops@example.invalid` is NOT a complete disclosure and must never reach the subject
    // prompt/write. This policy is the single, deterministic decision the host defers to before it
    // builds the disclosure: it accepts an explicit operator-supplied / t009l-configured contact and
    // fails closed on an absent, malformed, `.invalid`, or otherwise documented-placeholder value.
    //
    // The contact is DISCLOSURE METADATA, not a secret — it is printed in the notice. This policy
    // therefore only decides routability/placeholder-ness; it never handles secrets.
    //
    // net48 audit: System.* only. No UnityEngine / Valheim / BepInEx.

    /// <summary>The subject-free result of validating an operator contact channel. When
    /// <see cref="IsAcceptable"/> is false, <see cref="RejectionCode"/> is a stable, printable code the
    /// host surfaces verbatim (never the raw contact interpolated into a secret path).</summary>
    public readonly struct OperatorContactValidation
    {
        private OperatorContactValidation(bool acceptable, string rejectionCode, string normalized)
        {
            IsAcceptable = acceptable;
            RejectionCode = rejectionCode ?? string.Empty;
            NormalizedContact = normalized ?? string.Empty;
        }

        public bool IsAcceptable { get; }

        /// <summary>Stable rejection code (empty when acceptable): one of
        /// <c>OperatorContactAbsent</c>, <c>OperatorContactMalformed</c>,
        /// <c>OperatorContactNonRoutablePlaceholder</c>.</summary>
        public string RejectionCode { get; }

        /// <summary>The trimmed contact value to present in the disclosure (empty when rejected).</summary>
        public string NormalizedContact { get; }

        internal static OperatorContactValidation Accept(string normalized) =>
            new OperatorContactValidation(true, string.Empty, normalized);
        internal static OperatorContactValidation Reject(string code) =>
            new OperatorContactValidation(false, code, string.Empty);
    }

    /// <summary>Decides whether an operator-supplied contact channel is a routable disclosure contact.
    /// Deterministic and engine-free so the host and its tests share one decision.</summary>
    public static class OperatorContactPolicy
    {
        public const string CodeAbsent = "OperatorContactAbsent";
        public const string CodeMalformed = "OperatorContactMalformed";
        public const string CodePlaceholder = "OperatorContactNonRoutablePlaceholder";

        // RFC 2606 / 6761 reserved, non-routable TLDs/domains, plus documented placeholder domains.
        // A contact whose host is or ends with any of these is never a real operator contact.
        private static readonly string[] ReservedHostSuffixes =
        {
            ".invalid", ".example", ".test", ".localhost",
            "example.com", "example.org", "example.net",
        };

        // Documented placeholder tokens that operators paste when they have not set a real value.
        // Compared case-insensitively against the whole trimmed value and the email local-part/host.
        private static readonly string[] PlaceholderTokens =
        {
            "changeme", "change-me", "todo", "tbd", "none", "n/a", "na",
            "placeholder", "example", "your-contact-here", "unset", "null",
        };

        /// <summary>Validate an operator contact channel. Accepts a routable email (<c>local@host</c>)
        /// or an <c>http(s)</c>/<c>mailto:</c> URL; fails closed on absent, malformed, reserved
        /// (<c>.invalid</c> etc.), or documented-placeholder values.</summary>
        public static OperatorContactValidation Validate(string? contact)
        {
            if (string.IsNullOrWhiteSpace(contact))
                return OperatorContactValidation.Reject(CodeAbsent);

            string value = contact!.Trim();

            // No internal whitespace / control characters in a single contact channel.
            foreach (char c in value)
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                    return OperatorContactValidation.Reject(CodeMalformed);

            if (MatchesPlaceholderToken(value))
                return OperatorContactValidation.Reject(CodePlaceholder);

            if (!TryExtractHost(value, out string host))
                return OperatorContactValidation.Reject(CodeMalformed);

            host = host.ToLowerInvariant();
            if (host.Length == 0)
                return OperatorContactValidation.Reject(CodeMalformed);

            // A routable host must be a dotted name (has a TLD) and not a bare/loopback host.
            if (host == "localhost" || host.IndexOf('.') < 0)
                return OperatorContactValidation.Reject(CodePlaceholder);

            foreach (var suffix in ReservedHostSuffixes)
            {
                if (host == suffix.TrimStart('.'))
                    return OperatorContactValidation.Reject(CodePlaceholder);
                if (host.EndsWith(suffix, StringComparison.Ordinal))
                    return OperatorContactValidation.Reject(CodePlaceholder);
            }

            if (MatchesPlaceholderToken(host))
                return OperatorContactValidation.Reject(CodePlaceholder);

            return OperatorContactValidation.Accept(value);
        }

        private static bool MatchesPlaceholderToken(string candidate)
        {
            string lower = candidate.ToLowerInvariant();
            foreach (var token in PlaceholderTokens)
                if (string.Equals(lower, token, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>Extract the host of an email / http(s) / mailto contact. Returns false when the
        /// value is not a recognizable routable channel shape.</summary>
        private static bool TryExtractHost(string value, out string host)
        {
            host = string.Empty;

            if (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("mailto:".Length);

            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)) return false;
                if (string.IsNullOrEmpty(uri.Host)) return false;
                host = uri.Host;
                return true;
            }

            // Email shape: exactly one '@', a non-empty local part, and a non-empty host.
            int at = value.IndexOf('@');
            if (at <= 0) return false;
            if (value.IndexOf('@', at + 1) >= 0) return false;   // more than one '@'
            string local = value.Substring(0, at);
            string domain = value.Substring(at + 1);
            if (local.Length == 0 || domain.Length == 0) return false;
            if (domain.StartsWith(".", StringComparison.Ordinal) ||
                domain.EndsWith(".", StringComparison.Ordinal)) return false;
            host = domain;
            return true;
        }
    }
}
