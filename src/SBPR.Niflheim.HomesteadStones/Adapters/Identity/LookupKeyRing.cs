using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Identity
{
    // IAP-003 Tracer 1 — the versioned, domain-separated HMAC boundary (engine-free CLEAN-side core).
    //
    // The pilot NEVER persists a raw provider/profile subject. Its only durable lookup key is a
    // full-length HMAC-SHA-256 over an unambiguous canonical encoding with explicit domain separation
    // (AIP-FR-004): credentials bind (credential-v1, provider namespace, issuer/backend, subject);
    // profiles bind (profile-v1, AccountId, s_playerID). The key ring holds one active key and at most
    // one previous key (data-model.md "Rotate/retire lookup key"), each ≥256 random bits, living outside
    // the account store. A missing required key version fails closed — it never falls back to a raw id
    // (AIP-FR-005, edge case "HMAC key missing").
    //
    // net48 audit: System.Security.Cryptography.HMACSHA256 + Encoding.UTF8 + BitConverter — all exist in
    // .NET Framework 4.8. No UnityEngine/Valheim/BepInEx.

    /// <summary>Explicit domain-separation tags. Distinct byte prefixes make a credential HMAC and a
    /// profile HMAC computed over structurally similar inputs unequal by construction, so a subject can
    /// never be replayed across the two lookup spaces (AIP-FR-004).</summary>
    public static class HmacDomain
    {
        public const string CredentialV1 = "credential-v1";
        public const string ProfileV1 = "profile-v1";
    }

    /// <summary>One HMAC key: a version identifier plus ≥256 random bits. The raw key bytes live only
    /// here (outside the account journal and its ordinary backups) and are never logged/exported. A key
    /// shorter than 256 bits is rejected at construction so a weak key can never be configured.</summary>
    public sealed class LookupHmacKey
    {
        /// <summary>Minimum key length in bits (AIP-FR-005). 256 bits == 32 bytes.</summary>
        public const int MinKeyBits = 256;

        private readonly byte[] _key;

        public LookupHmacKey(LookupKeyVersion version, byte[] keyBytes)
        {
            if (version.IsEmpty) throw new ArgumentException("Key version must be non-empty.", nameof(version));
            if (keyBytes == null) throw new ArgumentNullException(nameof(keyBytes));
            if (keyBytes.Length * 8 < MinKeyBits)
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture,
                        "Lookup HMAC key must be at least {0} bits; got {1}.", MinKeyBits, keyBytes.Length * 8),
                    nameof(keyBytes));
            Version = version;
            _key = (byte[])keyBytes.Clone();
        }

        public LookupKeyVersion Version { get; }

        public int KeyBits => _key.Length * 8;

        /// <summary>Mint a fresh key of exactly 256 random bits under the given version.</summary>
        public static LookupHmacKey Generate(LookupKeyVersion version)
        {
            byte[] bytes = new byte[MinKeyBits / 8];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return new LookupHmacKey(version, bytes);
        }

        /// <summary>Compute the full-length HMAC-SHA-256 over the canonical domain-separated message,
        /// rendered as lowercase hex. Full length — never truncated — so it is not brute-forceable back
        /// to the subject.</summary>
        internal string ComputeHex(byte[] canonicalMessage)
        {
            using (var hmac = new HMACSHA256(_key))
            {
                byte[] mac = hmac.ComputeHash(canonicalMessage);
                var sb = new StringBuilder(mac.Length * 2);
                foreach (byte b in mac) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }

    /// <summary>A computed lookup HMAC stamped with the key version that produced it. This — not the raw
    /// subject — is what the pilot persists and indexes.</summary>
    public readonly struct SubjectLookupHmac : IEquatable<SubjectLookupHmac>
    {
        public SubjectLookupHmac(string hex, LookupKeyVersion keyVersion)
        {
            Hex = hex ?? string.Empty;
            KeyVersion = keyVersion;
        }

        public string Hex { get; }
        public LookupKeyVersion KeyVersion { get; }
        public bool IsEmpty => string.IsNullOrEmpty(Hex);

        public bool Equals(SubjectLookupHmac other) =>
            string.Equals(Hex, other.Hex, StringComparison.Ordinal) && KeyVersion.Equals(other.KeyVersion);
        public override bool Equals(object? obj) => obj is SubjectLookupHmac other && Equals(other);
        public override int GetHashCode() =>
            (StringComparer.Ordinal.GetHashCode(Hex ?? string.Empty) * 397) ^ KeyVersion.GetHashCode();
        public override string ToString() => KeyVersion.Value + ":" + Hex;
    }

    /// <summary>Rejection when a required key version is not present in the ring — the pilot fails closed
    /// (AIP-FR-005; contracts `LookupKeyUnavailable`).</summary>
    public sealed class LookupKeyUnavailableException : Exception
    {
        public LookupKeyUnavailableException(string message) : base(message) { }
    }

    /// <summary>The active + optional-previous key ring. Computes credential/profile HMACs, resolves a
    /// message under active then previous key for lookup during rotation, and fails closed when a
    /// required version is missing. Canonical encoding is explicit, length-prefixed field framing so two
    /// distinct field tuples can never collide onto the same message bytes.</summary>
    public sealed class LookupKeyRing
    {
        private readonly LookupHmacKey _active;
        private readonly LookupHmacKey? _previous;

        public LookupKeyRing(LookupHmacKey active, LookupHmacKey? previous = null)
        {
            _active = active ?? throw new ArgumentNullException(nameof(active));
            if (previous != null && previous.Version.Equals(active.Version))
                throw new ArgumentException("Previous key version must differ from the active key version.", nameof(previous));
            _previous = previous;
        }

        public LookupKeyVersion ActiveVersion => _active.Version;
        public bool HasPrevious => _previous != null;
        public LookupKeyVersion PreviousVersion => _previous != null ? _previous.Version : default;

        /// <summary>True when the ring can serve the given version (active or configured previous).</summary>
        public bool Knows(LookupKeyVersion version) =>
            _active.Version.Equals(version) || (_previous != null && _previous.Version.Equals(version));

        // ---- Credential HMAC (credential-v1, providerNamespace, backendIssuer, subject) ----

        public SubjectLookupHmac CredentialHmacActive(string providerNamespace, string backendIssuer, string subject) =>
            CredentialHmac(_active, providerNamespace, backendIssuer, subject);

        /// <summary>Compute the credential HMAC under a specific known version; throws fail-closed if the
        /// version is unknown to the ring (AT-AIP-KEY-MISSING-FAIL-CLOSED).</summary>
        public SubjectLookupHmac CredentialHmacUnder(LookupKeyVersion version, string providerNamespace, string backendIssuer, string subject) =>
            CredentialHmac(KeyOrFailClosed(version), providerNamespace, backendIssuer, subject);

        private static SubjectLookupHmac CredentialHmac(LookupHmacKey key, string providerNamespace, string backendIssuer, string subject)
        {
            byte[] message = Canonical(new[] { HmacDomain.CredentialV1, providerNamespace, backendIssuer, subject });
            return new SubjectLookupHmac(key.ComputeHex(message), key.Version);
        }

        // ---- Profile HMAC (profile-v1, accountId, s_playerID) ----

        public SubjectLookupHmac ProfileHmacActive(string accountId, string playerId) =>
            ProfileHmac(_active, accountId, playerId);

        public SubjectLookupHmac ProfileHmacUnder(LookupKeyVersion version, string accountId, string playerId) =>
            ProfileHmac(KeyOrFailClosed(version), accountId, playerId);

        private static SubjectLookupHmac ProfileHmac(LookupHmacKey key, string accountId, string playerId)
        {
            byte[] message = Canonical(new[] { HmacDomain.ProfileV1, accountId, playerId });
            return new SubjectLookupHmac(key.ComputeHex(message), key.Version);
        }

        /// <summary>The active + previous versions a lookup should probe, active first. Used so a
        /// binding written under the previous key still resolves during rotation before lazy re-key.</summary>
        public IReadOnlyList<LookupKeyVersion> LookupVersions()
        {
            var versions = new List<LookupKeyVersion> { _active.Version };
            if (_previous != null) versions.Add(_previous.Version);
            return versions;
        }

        private LookupHmacKey KeyOrFailClosed(LookupKeyVersion version)
        {
            if (_active.Version.Equals(version)) return _active;
            if (_previous != null && _previous.Version.Equals(version)) return _previous;
            throw new LookupKeyUnavailableException(
                "Required lookup key version '" + version.Value + "' is not present in the ring; failing closed.");
        }

        /// <summary>Length-prefixed, domain-tagged canonical encoding. Each field is written as its
        /// UTF-8 byte length (4-byte little-endian) followed by its bytes, so no combination of field
        /// boundaries can be shifted to forge an equal message from different field values
        /// (AT-AIP-HMAC-CANONICAL).</summary>
        internal static byte[] Canonical(string[] fields)
        {
            using (var ms = new System.IO.MemoryStream())
            {
                // Frame with the field count first so a different arity cannot alias.
                WriteInt(ms, fields.Length);
                foreach (string f in fields)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(f ?? string.Empty);
                    WriteInt(ms, bytes.Length);
                    ms.Write(bytes, 0, bytes.Length);
                }
                return ms.ToArray();
            }
        }

        private static void WriteInt(System.IO.MemoryStream ms, int value)
        {
            byte[] b = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian) Array.Reverse(b);
            ms.Write(b, 0, b.Length);
        }
    }
}
