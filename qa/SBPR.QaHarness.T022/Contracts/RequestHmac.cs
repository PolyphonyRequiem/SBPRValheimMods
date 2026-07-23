// Engine-free HMAC-SHA256 request signing/verification (ADR-0009 §3.2, §5.1).
// Uses System.Security.Cryptography only — no game/BepInEx dependency. The canonical
// signed string is a fixed, order-stable concatenation of the envelope's authenticated
// fields so the runner and helper compute the same MAC without a JSON serializer.
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SBPR.QaHarness.T022.Core
{
    /// <summary>Deterministic HMAC-SHA256 over an envelope's authenticated fields.</summary>
    public static class RequestHmac
    {
        /// <summary>
        /// Build the canonical signing string. Fields are joined with '\n' in a FIXED
        /// order; every authenticated field — including the connection generation — is
        /// included so tampering any of them (generation included) invalidates the MAC.
        /// Deliberately not JSON — a stable manual layout avoids serializer ambiguity.
        /// </summary>
        public static string CanonicalString(
            string nonce, long seq, long expiryUnixMs, string role,
            long worldUid, string verb, string requestId, long connectionGeneration)
        {
            var sb = new StringBuilder();
            sb.Append(nonce ?? string.Empty).Append('\n');
            sb.Append(seq.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(expiryUnixMs.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(role ?? string.Empty).Append('\n');
            sb.Append(worldUid.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(verb ?? string.Empty).Append('\n');
            sb.Append(requestId ?? string.Empty).Append('\n');
            sb.Append(connectionGeneration.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Compute the lowercase hex HMAC-SHA256 of <paramref name="canonical"/> under <paramref name="secret"/>.</summary>
        public static string Compute(string secret, string canonical)
        {
            using var mac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? string.Empty));
            byte[] hash = mac.ComputeHash(Encoding.UTF8.GetBytes(canonical ?? string.Empty));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>
        /// Constant-time comparison of two hex MACs. Fail-closed on null/length mismatch.
        /// </summary>
        public static bool Verify(string expected, string actual)
        {
            if (expected == null || actual == null) return false;
            byte[] a = Encoding.ASCII.GetBytes(expected);
            byte[] b = Encoding.ASCII.GetBytes(actual);
            if (a.Length != b.Length) return false;
            // Manual constant-time compare — CryptographicOperations.FixedTimeEquals
            // is not available on net48, so we accumulate differences without early exit.
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
