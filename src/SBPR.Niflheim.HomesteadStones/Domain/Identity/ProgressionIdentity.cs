using System;

namespace SBPR.Niflheim.HomesteadStones.Domain.Identity
{
    // Authenticated principal + stable-identity value objects for the Homestead progression
    // proof (T002, Gate A). This is the CLEAN-side production home of the identity seam the
    // T001 spike proved (research.md Gate A: principal candidate A over E).
    //
    // net48 audit: only System.String / StringComparison.Ordinal are used here. No net5+ API,
    // no UnityEngine / Valheim / BepInEx reference, so this file link-compiles into the net8
    // test project exactly like Domain/HomesteadPlacement.cs while shipping under net48.

    /// <summary>Authenticated server/world identity. Prevents portable character state from
    /// authorizing a mutation against another world (data-model.md WorldId).</summary>
    public readonly struct WorldId : IEquatable<WorldId>
    {
        public WorldId(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
        public string Value { get; }
        public bool Equals(WorldId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is WorldId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    /// <summary>Stable Homestead Stone identity = WorldId + host zone (data-model.md StoneId).
    /// Never a ZDOID, network owner, or minted GUID.</summary>
    public readonly struct StoneId : IEquatable<StoneId>
    {
        public StoneId(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));

        public static StoneId FromHostZone(WorldId world, int zoneX, int zoneZ) =>
            new StoneId(world.Value + "|" + zoneX.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + "|" + zoneZ.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public string Value { get; }
        public bool Equals(StoneId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is StoneId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    /// <summary>Authenticated account subject. Authority/grouping/audit only — never gameplay
    /// progression ownership (data-model.md AccountId).</summary>
    public readonly struct AccountId : IEquatable<AccountId>
    {
        public AccountId(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
        public string Value { get; }
        public bool Equals(AccountId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is AccountId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    /// <summary>Server-bound character subject within an AccountId; owns gameplay progression.
    /// Never accepted from an unauthenticated payload (data-model.md CharacterId).</summary>
    public readonly struct CharacterId : IEquatable<CharacterId>
    {
        public CharacterId(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
        public string Value { get; }
        public bool Equals(CharacterId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is CharacterId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    /// <summary>Caller-generated unique operation id bound to principal, command, Stone, and
    /// payload digest. Same id + same request returns the recorded result (data-model.md).</summary>
    public readonly struct OperationId : IEquatable<OperationId>
    {
        public OperationId(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
        public string Value { get; }
        public bool Equals(OperationId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is OperationId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    /// <summary>Untrusted client payload identity claim — comes off the wire inside the command
    /// payload. Compared, never trusted (contracts.md common command envelope).</summary>
    public readonly struct ClaimedPrincipal
    {
        public ClaimedPrincipal(string? claimedAccountId, string? claimedCharacterId)
        {
            ClaimedAccountId = claimedAccountId;
            ClaimedCharacterId = claimedCharacterId;
        }

        public string? ClaimedAccountId { get; }
        public string? ClaimedCharacterId { get; }
    }

    /// <summary>Server-owned connection truth. The transport attributes the peer out-of-band
    /// (the ZRoutedRpc <c>sender</c> set from the authenticated socket, mirroring the in-tree
    /// TwistedPortalDirectory pattern); the payload can never set these.</summary>
    public readonly struct AuthenticatedConnection
    {
        public AuthenticatedConnection(string platformId, string actingCharacterId)
        {
            PlatformId = platformId;
            ActingCharacterId = actingCharacterId;
        }

        /// <summary>Stable platform id derived from the authenticated socket (candidate A).</summary>
        public string PlatformId { get; }

        /// <summary>Acting character observed at command time (peer character id).</summary>
        public string ActingCharacterId { get; }
    }

    /// <summary>The resolved, authoritative principal a mutation binds to.</summary>
    public readonly struct AuthoritativePrincipal
    {
        public AuthoritativePrincipal(AccountId account, CharacterId character, string platformId)
        {
            Account = account;
            Character = character;
            PlatformId = platformId;
        }

        public AccountId Account { get; }
        public CharacterId Character { get; }
        public string PlatformId { get; }
    }

    public enum PrincipalResolution
    {
        Bound,               // authenticated principal resolved; claim matched (or no claim)
        PrincipalMismatch,   // client payload claimed a different account/character
        UnauthenticatedPeer  // no server-attributed connection -> reject, never trust payload
    }

    /// <summary>
    /// Derives the authoritative principal from the authenticated connection and only
    /// <em>compares</em> the client claim. Payload identity can never become authority.
    /// Candidate E (server-owned platform-id -&gt; AccountId map, the R-003 exclusivity index)
    /// with candidate-A passthrough fallback.
    /// </summary>
    public sealed class PrincipalResolver
    {
        private readonly Func<string, string?>? _accountIdForPlatform;

        /// <param name="accountIdForPlatform">Server-owned platform-id -&gt; AccountId map
        /// (candidate E). Null, or a null return, falls back to candidate A (platform id as
        /// account).</param>
        public PrincipalResolver(Func<string, string?>? accountIdForPlatform)
        {
            _accountIdForPlatform = accountIdForPlatform;
        }

        public PrincipalResolution Resolve(
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            out AuthoritativePrincipal principal)
        {
            principal = default;

            // No server-attributed platform id -> not authenticated. Never synthesise identity
            // from the payload claim.
            if (string.IsNullOrEmpty(connection.PlatformId))
                return PrincipalResolution.UnauthenticatedPeer;

            string? mapped = _accountIdForPlatform != null ? _accountIdForPlatform(connection.PlatformId) : null;
            string accountId = string.IsNullOrEmpty(mapped) ? connection.PlatformId : mapped!;

            var resolved = new AuthoritativePrincipal(
                new AccountId(accountId),
                new CharacterId(connection.ActingCharacterId ?? string.Empty),
                connection.PlatformId);

            // The claim is compared, never trusted. A hostile client that fills the payload with
            // someone else's account/character is rejected here (contracts.md common envelope).
            if (!string.IsNullOrEmpty(claim.ClaimedAccountId) &&
                !string.Equals(claim.ClaimedAccountId, resolved.Account.Value, StringComparison.Ordinal))
                return PrincipalResolution.PrincipalMismatch;

            if (!string.IsNullOrEmpty(claim.ClaimedCharacterId) &&
                !string.Equals(claim.ClaimedCharacterId, resolved.Character.Value, StringComparison.Ordinal))
                return PrincipalResolution.PrincipalMismatch;

            principal = resolved;
            return PrincipalResolution.Bound;
        }
    }
}
