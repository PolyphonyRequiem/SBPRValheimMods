using System;
using System.Security.Cryptography;

namespace SBPR.Niflheim.HomesteadStones.Domain.Accounts
{
    // IAP-003 Tracer 1 — first-bind account and credential foundation (engine-free CLEAN-side core).
    //
    // Opaque, server-minted identity value objects for the pilot account layer (data-model.md
    // "Identity vocabulary"). These are DISTINCT from the legacy Homestead Domain/Identity/AccountId:
    // that legacy type is the pre-supersession "AccountId == authenticated provider subject" shape;
    // these Pilot* types are the server-minted, provider-independent authority this task introduces.
    // Kept in a separate namespace + under distinct names so both can link-compile into the net8 test
    // project without a symbol collision while the supersession is documented (spec §Proposed
    // supersession boundary; A8).
    //
    // net48 audit: only System.* (String, Security.Cryptography.RandomNumberGenerator, Globalization).
    // No UnityEngine / Valheim / BepInEx, so this ships under net48 AND link-compiles under net8.

    /// <summary>Opaque, CSPRNG-minted server account identifier with ≥128 bits of entropy. Account
    /// authority/grouping/audit only; never provider- or profile-derived (data-model.md `AccountId`,
    /// AIP-FR-003).</summary>
    public readonly struct PilotAccountId : IEquatable<PilotAccountId>
    {
        public PilotAccountId(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
        public string Value { get; }
        public bool Equals(PilotAccountId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is PilotAccountId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public bool IsEmpty => string.IsNullOrEmpty(Value);
    }

    /// <summary>Opaque server identifier for one durable provider-credential binding lifecycle
    /// (data-model.md `CredentialBindingId`).</summary>
    public readonly struct CredentialBindingId : IEquatable<CredentialBindingId>
    {
        public CredentialBindingId(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
        public string Value { get; }
        public bool Equals(CredentialBindingId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is CredentialBindingId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public bool IsEmpty => string.IsNullOrEmpty(Value);
    }

    /// <summary>Stable operator-safe selector for one pre-account enrollment record (data-model.md
    /// `AllowlistEntryId`).</summary>
    public readonly struct AllowlistEntryId : IEquatable<AllowlistEntryId>
    {
        public AllowlistEntryId(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
        public string Value { get; }
        public bool Equals(AllowlistEntryId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is AllowlistEntryId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public bool IsEmpty => string.IsNullOrEmpty(Value);
    }

    /// <summary>Server-issued durable audit/replay identity for one accepted account mutation
    /// (data-model.md `AccountReceiptId`).</summary>
    public readonly struct AccountReceiptId : IEquatable<AccountReceiptId>
    {
        public AccountReceiptId(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
        public string Value { get; }
        public bool Equals(AccountReceiptId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is AccountReceiptId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public bool IsEmpty => string.IsNullOrEmpty(Value);
    }

    /// <summary>Opaque, CSPRNG-minted server character identifier with ≥128 bits of entropy. Owns
    /// gameplay progression within one account; never `s_playerID`, character ZDOID, or display-name
    /// derived (data-model.md `CharacterId`, AIP-FR-003/AIP-FR-010). Introduced by IAP-005 Tracer 2.</summary>
    public readonly struct PilotCharacterId : IEquatable<PilotCharacterId>
    {
        public PilotCharacterId(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
        public string Value { get; }
        public bool Equals(PilotCharacterId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is PilotCharacterId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public bool IsEmpty => string.IsNullOrEmpty(Value);
    }

    /// <summary>Random in-memory process/session identifier for one active connection. NOT durable
    /// identity: it exists only in the ephemeral admission index and is cleared on restart
    /// (data-model.md `SessionId`). Introduced by IAP-005 Tracer 2.</summary>
    public readonly struct SessionId : IEquatable<SessionId>
    {
        public SessionId(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
        public string Value { get; }
        public bool Equals(SessionId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is SessionId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public bool IsEmpty => string.IsNullOrEmpty(Value);
    }

    /// <summary>Configured HMAC-key identifier permitting bounded active/previous-key resolution and
    /// lazy re-key (data-model.md `LookupKeyVersion`).</summary>
    public readonly struct LookupKeyVersion : IEquatable<LookupKeyVersion>
    {
        public LookupKeyVersion(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
        public string Value { get; }
        public bool Equals(LookupKeyVersion other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is LookupKeyVersion other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public bool IsEmpty => string.IsNullOrEmpty(Value);
    }

    /// <summary>Mints opaque, cryptographically-random server identifiers with ≥128 bits of entropy
    /// (AIP-FR-003). Every id is 128 bits of CSPRNG output rendered as 32 lowercase hex characters,
    /// prefixed with a short type tag so operator logs can tell an account id from a binding id without
    /// the tag ever being provider-derived. The randomness — never a provider/profile subject — is the
    /// identity, so a minted id reveals nothing about the credential that owns it.</summary>
    public static class OpaqueIdMint
    {
        /// <summary>Bits of entropy in every minted id. The spec floor is 128; we mint exactly 128 so
        /// the entropy proof (AT-AIP-INTERNAL-ID-ENTROPY) has a stable, auditable width.</summary>
        public const int EntropyBits = 128;

        private const int EntropyBytes = EntropyBits / 8; // 16 bytes → 128 bits

        public static PilotAccountId NewAccountId() => new PilotAccountId("acct-" + RandomHex());
        public static CredentialBindingId NewCredentialBindingId() => new CredentialBindingId("cred-" + RandomHex());
        public static AllowlistEntryId NewAllowlistEntryId() => new AllowlistEntryId("allow-" + RandomHex());
        public static AccountReceiptId NewReceiptId() => new AccountReceiptId("rcpt-" + RandomHex());
        public static PilotCharacterId NewCharacterId() => new PilotCharacterId("char-" + RandomHex());
        public static SessionId NewSessionId() => new SessionId("sess-" + RandomHex());

        /// <summary>The raw 128-bit random core (no type tag), lowercase hex. Exposed so entropy tests
        /// can assert width/uniqueness on the identity core rather than the human tag.</summary>
        public static string RandomHex()
        {
            byte[] bytes = new byte[EntropyBytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return ToHex(bytes);
        }

        private static string ToHex(byte[] bytes)
        {
            const string hex = "0123456789abcdef";
            char[] chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = hex[bytes[i] >> 4];
                chars[i * 2 + 1] = hex[bytes[i] & 0xF];
            }
            return new string(chars);
        }
    }
}
