using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Features.HomesteadStone;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T021 Refined Workshop — CLIENT-side resident-Stone lookup. The persistent Homestead Stone prefab is
    /// replicated to every joined client (its ZDO carries the stable host-zone key), so a client can read
    /// the set of nearby Stone centers straight from its OWN replicated ZDO view to decide WHICH Stone Area
    /// it currently stands in. That decision is only a client convenience — it selects which Stone to ask
    /// the server about and which cache row to read; the server independently reconfirms occupancy from the
    /// requesting peer's own character ZDO before stamping any activation snapshot (PR #368 review
    /// Blocker 2). Nothing here is an authority.
    ///
    /// References Valheim (ZDOMan, ZDO) → net48-only, not link-compiled into net8.
    /// </summary>
    internal static class HomesteadStoneClientIndex
    {
        internal readonly struct ResidentStone
        {
            public ResidentStone(int zoneX, int zoneZ, float x, float z)
            {
                ZoneX = zoneX; ZoneZ = zoneZ; X = x; Z = z;
            }
            public int ZoneX { get; }
            public int ZoneZ { get; }
            public float X { get; }
            public float Z { get; }
        }

        /// <summary>The currently-replicated Homestead Stone instances known to this client, each with its
        /// stable host-zone key and world position. Reads the client's own ZDO view (no server call). Returns
        /// an empty list when the ZDO system is unavailable or no Stone has replicated yet.</summary>
        internal static List<ResidentStone> ResidentStones()
        {
            var result = new List<ResidentStone>();
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return result;

            var found = new List<ZDO>();
            int index = 0;
            while (!zdoMan.GetAllZDOsWithPrefabIterative(HomesteadStoneRegistrar.PrefabName, found, ref index)) { }
            foreach (var zdo in found)
            {
                if (zdo == null || !zdo.IsValid()) continue;
                int zoneX = zdo.GetInt(HomesteadStoneData.LocationZoneXKey, int.MinValue);
                int zoneZ = zdo.GetInt(HomesteadStoneData.LocationZoneZKey, int.MinValue);
                if (zoneX == int.MinValue || zoneZ == int.MinValue) continue; // unkeyed → skip.
                Vector3 pos = zdo.GetPosition();
                result.Add(new ResidentStone(zoneX, zoneZ, pos.x, pos.z));
            }
            return result;
        }
    }
}
