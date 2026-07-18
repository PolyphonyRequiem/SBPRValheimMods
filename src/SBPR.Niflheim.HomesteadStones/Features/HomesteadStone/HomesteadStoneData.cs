using System;
using SBPR.Niflheim.HomesteadStones.Domain;

namespace SBPR.Niflheim.HomesteadStones.Features.HomesteadStone
{
    /// <summary>
    /// The stable Homestead Stone data-key contract selected for the current playtest build.
    /// D3 is the host Valheim Location's zone coordinate `(zoneX,zoneZ)`. Downstream slices
    /// must not substitute ZDOID/network owner, WorldZones transient identity, or a minted GUID.
    /// Ownership remains a future Niflheim account/entity identity; this integration slice
    /// only reserves the key and does not freeze claim/migration policy.
    /// </summary>
    internal static class HomesteadStoneData
    {
        /// <summary>The ZDO var key namespace for homestead-stone fields. Reserved now.</summary>
        internal const string ZdoKeyPrefix = "niflheim.homestead.";

        /// <summary>
        /// Stable D3 identity for a location-seated Stone: the host Location's Valheim zone
        /// coordinate `(zoneX,zoneZ)`. Store the two coordinates explicitly; do not derive
        /// identity from ZDOID, network ownership, world position, or a minted GUID.
        /// </summary>
        // R7 (Blocker 1) — the provenance key names + schema version are OWNED by the engine-free
        // HomesteadProvenanceCodec (Domain), which is the single source of truth the stamp, read-back
        // verification, reconciler, and headless tests all share. These members forward to that authority
        // so there is exactly ONE definition of each key literal (no drift between the codec and this
        // Features-layer contract). A guard test (ProvenanceKeyContractTests) asserts the codec's key
        // literals equal the ZdoKeyPrefix-derived names below, catching any accidental divergence.
        internal const string LocationZoneXKey = HomesteadProvenanceCodec.LocationZoneXKey;
        internal const string LocationZoneZKey = HomesteadProvenanceCodec.LocationZoneZKey;

        internal const string WorldIdentityKey = HomesteadProvenanceCodec.WorldIdentityKey;
        internal const string SelectorVersionKey = HomesteadProvenanceCodec.SelectorVersionKey;
        internal const string HostPrefabKey = HomesteadProvenanceCodec.HostPrefabKey;

        /// <summary>R7 (Blocker 1) — provider/content provenance persisted into ZDO truth. These are the
        /// versioned keys the stamp writes, read-back verifies, and the reconciler compares, so a Stone's
        /// creation authority (which provider produced it, from which content/manifest generation) is a
        /// durable fact — not something re-guessed from bare zone existence. A selector/provider/content
        /// upgrade is detected by a mismatch on these keys and reaps the stale Stone.</summary>
        internal const string ProvenanceVersionKey = HomesteadProvenanceCodec.ProvenanceVersionKey;
        internal const string ProviderKindKey = HomesteadProvenanceCodec.ProviderKindKey;
        internal const string ProviderVersionKey = HomesteadProvenanceCodec.ProviderVersionKey;
        internal const string ContentHashKey = HomesteadProvenanceCodec.ContentHashKey;
        internal const string ManifestGenerationKey = HomesteadProvenanceCodec.ManifestGenerationKey;

        /// <summary>The stamp schema version this build writes. Bumping it invalidates older stamps on read-back
        /// comparison so a provenance-schema change forces a re-stamp rather than a silent partial match.</summary>
        internal const int ProvenanceSchemaVersion = HomesteadProvenanceCodec.SchemaVersion;

        /// <summary>Future Niflheim account/entity owner identity; not blindly Valheim PlayerID.</summary>
        internal const string ResourceOwnerKey = ZdoKeyPrefix + "resource_owner";

        /// <summary>Claim / lastActive timestamp — reserved for later policy, not consumed by this thin integration.</summary>
        internal const string ClaimedAtKey = ZdoKeyPrefix + "claimed_at";

        /// <summary>worldgen-vs-player origin tag for later lifecycle policy.</summary>
        internal const string OriginTagKey = ZdoKeyPrefix + "origin_tag";

        /// <summary>Reserved WorldZones RegionKey metadata. It does not gate/refuse MVP assignment.</summary>
        internal const string RegionKeyKey = ZdoKeyPrefix + "region_key";

        /// <summary>
        /// Mirrored Stone AP durable projection (T002, Gate A). This ZDO int is a receipt-derived
        /// PROJECTION of the durable operation-receipt journal — not a second source of truth. It
        /// equals the sum of accepted mirrored deltas after receipt reconciliation and is written
        /// owner-only; it is never debited in this proof (data-model.md StoneProgression invariants).
        /// </summary>
        internal const string MirroredStoneApKey = ZdoKeyPrefix + "mirrored_stone_ap";
    }
}
