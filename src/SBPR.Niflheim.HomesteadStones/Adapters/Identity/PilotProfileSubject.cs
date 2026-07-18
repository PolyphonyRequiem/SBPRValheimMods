using System;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Identity
{
    // IAP-005 Tracer 2 — the transient verified profile subject (engine-free CLEAN-side core).
    //
    // Valheim's profile picker remains the pilot selector (spec closed-pilot decision #4, AIP-FR-009):
    // the SERVER observes the authenticated peer's character ZDO s_playerID. That value is a transient
    // profile/creator fact — never the durable domain CharacterId (AIP-FR-010). This struct carries it
    // only long enough to compute an account-scoped ProfileSubjectHmac and to verify creator evidence;
    // it is memory-only and never serialized/logged, exactly like VerifiedProviderPrincipal.
    //
    // The client may CHOOSE a local profile, but it can never supply the admitted account, character,
    // or creator binding: this type is minted only from a server-observed peer fact (contracts.md
    // "Profile adapter port"; the client payload cannot manufacture it).
    //
    // net48 audit: only System.* here. No UnityEngine/Valheim/BepInEx — link-compiles under net8 and
    // ships under net48 exactly like the Gate-0/Tracer-1 identity slices.

    /// <summary>One server-observed authenticated profile fact: the nonzero <c>s_playerID</c> read off
    /// the authenticated peer's server-owned character ZDO. Transient; the raw value never becomes
    /// durable identity and is discarded once resolved to an internal CharacterId (data-model.md
    /// "VerifiedProfileSubject", contracts.md "Profile adapter port").</summary>
    public readonly struct VerifiedProfileSubject
    {
        public VerifiedProfileSubject(long playerId, long transportHandle)
        {
            PlayerId = playerId;
            TransportHandle = transportHandle;
        }

        /// <summary>The server-observed <c>s_playerID</c>. A zero/negative value is NOT a real profile
        /// fact (Valheim uses 0 before a profile is chosen) and does not resolve.</summary>
        public long PlayerId { get; }

        /// <summary>The opaque per-peer transport handle the profile fact was observed on. Ties the
        /// profile fact to the same authenticated peer the provider principal was resolved from, so a
        /// stale/foreign handle cannot smuggle another peer's profile.</summary>
        public long TransportHandle { get; }

        /// <summary>True only when the server observed a real (nonzero, non-negative) profile subject.
        /// A zero/negative <c>s_playerID</c> rejects as <c>ProfileSubjectInvalid</c> before any mint.</summary>
        public bool IsResolved => PlayerId > 0L;

        /// <summary>The canonical string form of the profile subject fed into the account-scoped
        /// profile HMAC. Culture-invariant so the same numeric id hashes identically on every host.</summary>
        public string CanonicalPlayerId => PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static VerifiedProfileSubject None => new VerifiedProfileSubject(0L, 0L);
    }
}
