using System;

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
        internal const string LocationZoneXKey = ZdoKeyPrefix + "location_zone_x";
        internal const string LocationZoneZKey = ZdoKeyPrefix + "location_zone_z";

        internal const string WorldIdentityKey = ZdoKeyPrefix + "world_identity";
        internal const string SelectorVersionKey = ZdoKeyPrefix + "selector_version";
        internal const string HostPrefabKey = ZdoKeyPrefix + "host_prefab";

        /// <summary>Future Niflheim account/entity owner identity; not blindly Valheim PlayerID.</summary>
        internal const string ResourceOwnerKey = ZdoKeyPrefix + "resource_owner";

        /// <summary>Claim / lastActive timestamp — reserved for later policy, not consumed by this thin integration.</summary>
        internal const string ClaimedAtKey = ZdoKeyPrefix + "claimed_at";

        /// <summary>worldgen-vs-player origin tag for later lifecycle policy.</summary>
        internal const string OriginTagKey = ZdoKeyPrefix + "origin_tag";

        /// <summary>Reserved WorldZones RegionKey metadata. It does not gate/refuse MVP assignment.</summary>
        internal const string RegionKeyKey = ZdoKeyPrefix + "region_key";
    }
}
