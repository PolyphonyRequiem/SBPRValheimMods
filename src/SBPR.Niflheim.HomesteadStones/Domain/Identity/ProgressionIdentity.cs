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

    /// <summary>Server-owned connection truth. IAP-007 Tracer 3: the transport attributes the peer
    /// out-of-band from the BOUND INTERNAL SESSION established at admission (Tracer 1/2) — the
    /// server-minted <see cref="AccountId"/> and <see cref="ActingCharacterId"/> (internal
    /// <c>CharacterId</c>). The payload can never set these, and no provider/profile subject
    /// (<c>PlatformId</c>, raw <c>s_playerID</c>) appears here any more (AIP-FR-014/015).</summary>
    public readonly struct AuthenticatedConnection
    {
        public AuthenticatedConnection(string accountId, string actingCharacterId)
        {
            AccountId = accountId;
            ActingCharacterId = actingCharacterId;
        }

        /// <summary>Internal, server-minted account id from the bound session (never a provider subject).</summary>
        public string AccountId { get; }

        /// <summary>Internal, server-minted acting character id from the bound session
        /// (never a raw <c>s_playerID</c> / character ZDOID).</summary>
        public string ActingCharacterId { get; }
    }

    /// <summary>The resolved, authoritative principal a mutation binds to. IAP-007 Tracer 3: it
    /// carries ONLY the internal account/character; the raw provider <c>PlatformId</c> was removed
    /// from every gameplay binding/receipt/log (AIP-FR-015).</summary>
    public readonly struct AuthoritativePrincipal
    {
        public AuthoritativePrincipal(AccountId account, CharacterId character)
        {
            Account = account;
            Character = character;
        }

        public AccountId Account { get; }
        public CharacterId Character { get; }
    }

    /// <summary>The internal gameplay session principal handed to gameplay commands and world
    /// adapters after admission (contracts.md §"Gameplay principal contract",
    /// <c>PilotSessionPrincipal</c>). The provider subject, <c>ProviderKey</c>, raw
    /// <c>s_playerID</c>, and profile HMAC are all absent — it is purely the bound internal
    /// identity plus the ephemeral session id.</summary>
    public readonly struct PilotSessionPrincipal
    {
        public PilotSessionPrincipal(AccountId account, CharacterId character, string sessionId)
        {
            Account = account;
            Character = character;
            SessionId = sessionId ?? string.Empty;
        }

        public AccountId Account { get; }
        public CharacterId Character { get; }

        /// <summary>Ephemeral process-local session id (not durable identity).</summary>
        public string SessionId { get; }

        /// <summary>The authoritative binding a gameplay mutation commits under.</summary>
        public AuthoritativePrincipal ToPrincipal() => new AuthoritativePrincipal(Account, Character);
    }

    public enum PrincipalResolution
    {
        Bound,               // authenticated principal resolved; claim matched (or no claim)
        PrincipalMismatch,   // client payload claimed a different account/character
        UnauthenticatedPeer  // no server-attributed connection -> reject, never trust payload
    }

    /// <summary>
    /// IAP-007 Tracer 3: binds the authoritative gameplay principal straight from the BOUND INTERNAL
    /// SESSION carried on the authenticated connection and only <em>compares</em> the client claim.
    /// Payload identity can never become authority.
    ///
    /// The provider-shaped resolver is gone: there is no platform-id -&gt; AccountId lookup function,
    /// no candidate-A passthrough fallback, and NO provider lookup or network call on this path
    /// (AIP-FR-014/018, AT-AIP-NO-PROVIDER-HOTPATH). Admission (Tracer 1/2) already minted and bound
    /// the internal <c>AccountId</c>/<c>CharacterId</c>; this resolver only reads them off the
    /// connection and validates the claim.
    /// </summary>
    public sealed class PrincipalResolver
    {
        public PrincipalResolver() { }

        public PrincipalResolution Resolve(
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            out AuthoritativePrincipal principal)
        {
            principal = default;

            // No bound internal account id -> no admitted session. Never synthesise identity from the
            // payload claim, and never fall back to a raw provider subject (there is none here).
            if (string.IsNullOrEmpty(connection.AccountId))
                return PrincipalResolution.UnauthenticatedPeer;

            var resolved = new AuthoritativePrincipal(
                new AccountId(connection.AccountId),
                new CharacterId(connection.ActingCharacterId ?? string.Empty));

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
