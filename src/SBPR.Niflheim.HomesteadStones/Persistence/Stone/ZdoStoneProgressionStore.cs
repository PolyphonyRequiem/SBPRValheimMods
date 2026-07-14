using System;
using System.Collections.Generic;
using System.Globalization;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Features.HomesteadStone;

namespace SBPR.Niflheim.HomesteadStones.Persistence.Stone
{
    // net48-ONLY production ZDO sink for the Mirrored Stone AP projection (T002, Gate A).
    //
    // This file references UnityEngine/Valheim (ZDO, ZDOMan, ZNetView) and therefore does NOT
    // link-compile into the net8 test project — only the engine-free InMemoryMirroredStoneApStore
    // (MirroredStoneApStore.cs) is exercised by the contract/recovery tests. This shell writes the
    // receipt-derived Mirrored AP total onto the world-owned Stone ZDO as an OWNER-ONLY projection
    // (memory doctrine: owner-only ZDO writes via ZNetView/ClaimOwnership, never manual m_nview
    // pokes; never a second source of truth). It is accumulate-only — no debit path is exposed.
    //
    // ADR-0006 note: this reads/writes an existing world Stone ZDO enumerated by ZDOMan; it does not
    // clone or instantiate any prefab.

    public sealed class ZdoStoneProgressionStore : IMirroredStoneApStore
    {
        // Server-owned in-process cache so GetMirroredStoneAp is authoritative even before the ZDO is
        // resident in the local scene, and so replay stays idempotent per operation.
        private readonly InMemoryMirroredStoneApStore _cache = new InMemoryMirroredStoneApStore();

        // The durable journal is the single authority. This sink is a projection of it: at construction
        // (server boot) the OperationReceiptStore replays committed operations back through
        // ApplyMirroredApProjection, which warms _cache AND re-stamps the world Stone ZDO. That replay is
        // the warm-up path — this store therefore never reports a stale in-memory 0 while the durable
        // journal truth is non-zero, provided it is the SAME instance handed to the receipt store's
        // constructor (production wiring: construct this, pass it to `new OperationReceiptStore(...)`,
        // whose ctor rehydrates it before the first command). Do NOT read the ZDO as a second authority
        // to warm the cache — that would create a competing source of truth; the journal replay is the
        // one warm-up.

        public void ApplyMirroredApProjection(StoneId stoneId, string operationId, int mirroredApTotal)
        {
            _cache.ApplyMirroredApProjection(stoneId, operationId, mirroredApTotal);
            int total = _cache.GetMirroredStoneAp(stoneId);
            WriteZdoProjection(stoneId, total);
        }

        public int GetMirroredStoneAp(StoneId stoneId) => _cache.GetMirroredStoneAp(stoneId);

        public long GetStoneRevision(StoneId stoneId) => _cache.GetStoneRevision(stoneId);

        /// <summary>Write the current Mirrored AP total onto the world Stone ZDO owner-only. Best
        /// effort: if the ZDO is not resident (Stone unloaded) the authoritative cache still holds the
        /// value and a later reconciliation re-stamps it. Returns true when the ZDO was stamped.</summary>
        private static bool WriteZdoProjection(StoneId stoneId, int total)
        {
            if (!TryParseZone(stoneId, out int zoneX, out int zoneZ)) return false;
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return false;

            var found = new List<ZDO>();
            var index = 0;
            while (!zdoMan.GetAllZDOsWithPrefabIterative(HomesteadStoneRegistrar.PrefabName, found, ref index)) { }
            foreach (var zdo in found)
            {
                if (zdo == null) continue;
                if (zdo.GetInt(HomesteadStoneData.LocationZoneXKey, int.MinValue) != zoneX) continue;
                if (zdo.GetInt(HomesteadStoneData.LocationZoneZKey, int.MinValue) != zoneZ) continue;

                // Owner-only write: claim session ownership before mutating, per repo doctrine.
                if (!zdo.IsOwner()) zdo.SetOwner(ZDOMan.GetSessionID());
                zdo.Set(HomesteadStoneData.MirroredStoneApKey, total);
                return zdo.GetInt(HomesteadStoneData.MirroredStoneApKey, int.MinValue) == total;
            }
            return false;
        }

        /// <summary>Read the durable Mirrored AP projection back off the world Stone ZDO. Used by
        /// reconciliation/verification to confirm the ZDO matches the receipt-derived total.</summary>
        public static bool TryReadZdoProjection(StoneId stoneId, out int mirroredStoneAp)
        {
            mirroredStoneAp = 0;
            if (!TryParseZone(stoneId, out int zoneX, out int zoneZ)) return false;
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return false;

            var found = new List<ZDO>();
            var index = 0;
            while (!zdoMan.GetAllZDOsWithPrefabIterative(HomesteadStoneRegistrar.PrefabName, found, ref index)) { }
            foreach (var zdo in found)
            {
                if (zdo == null) continue;
                if (zdo.GetInt(HomesteadStoneData.LocationZoneXKey, int.MinValue) != zoneX) continue;
                if (zdo.GetInt(HomesteadStoneData.LocationZoneZKey, int.MinValue) != zoneZ) continue;
                mirroredStoneAp = zdo.GetInt(HomesteadStoneData.MirroredStoneApKey, 0);
                return true;
            }
            return false;
        }

        // StoneId.Value == "world|zoneX|zoneZ" (Domain.Identity.StoneId.FromHostZone).
        private static bool TryParseZone(StoneId stoneId, out int zoneX, out int zoneZ)
        {
            zoneX = 0; zoneZ = 0;
            var parts = (stoneId.Value ?? string.Empty).Split('|');
            if (parts.Length < 3) return false;
            return int.TryParse(parts[parts.Length - 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out zoneX)
                && int.TryParse(parts[parts.Length - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out zoneZ);
        }
    }
}
