using System;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne
{
    /// <summary>
    /// Harmony patches for cairn behavior:
    ///   • WearNTear.Damage prefix → swallow damage on cairns (combat-immune,
    ///     only natural UpdateWear decay ticks affect HP).
    ///   • WearNTear.Awake postfix → backfill missed wear ticks when a chunk
    ///     loads after being out-of-zone, using ZDO SBPR_LastWearTick.
    ///   • SE_Rested.CalculateComfortLevel postfix → max-clamp cairn comfort
    ///     floor into the result.
    /// </summary>
    [HarmonyPatch]
    public static class TrailborneCairnPatches
    {
        // ── Damage immunity ─────────────────────────────────────────────
        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
        [HarmonyPrefix]
        public static bool WearNTear_Damage_Prefix(WearNTear __instance)
        {
            if (__instance == null) return true;
            // GetComponentInParent so Harmony works regardless of which child collider was hit.
            var tag = __instance.GetComponent<TrailborneCairnTag>();
            if (tag == null) tag = __instance.GetComponentInParent<TrailborneCairnTag>();
            if (tag != null)
            {
                // Swallow the damage entirely. Cairns only decay via UpdateWear weather paths.
                return false;
            }
            return true;
        }

        // ── Out-of-zone decay backfill ──────────────────────────────────
        [HarmonyPatch(typeof(WearNTear), "Awake")]
        [HarmonyPostfix]
        public static void WearNTear_Awake_Postfix(WearNTear __instance)
        {
            if (__instance == null) return;
            var tag = __instance.GetComponent<TrailborneCairnTag>();
            if (tag == null) return;
            var nview = __instance.GetComponent<ZNetView>();
            if (nview == null || nview.GetZDO() == null) return;
            if (!nview.IsOwner()) return; // owner-only writes ZDO

            double nowDay = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() / 86400.0 : 0.0;
            // GetFloat works for backfill — Valheim's in-game-day is small enough to fit comfortably.
            float lastWearDay = nview.GetZDO().GetFloat(TrailborneM2.ZdoLastWearTick, -1f);
            if (lastWearDay < 0f)
            {
                // First load — seed and bail.
                nview.GetZDO().Set(TrailborneM2.ZdoLastWearTick, (float)nowDay);
                return;
            }

            float deltaDays = (float)nowDay - lastWearDay;
            if (deltaDays <= 0f)
            {
                nview.GetZDO().Set(TrailborneM2.ZdoLastWearTick, (float)nowDay);
                return;
            }

            // Vanilla c_RainDamage is 5 HP per c_RainDamageTime (60s) clamped to
            // c_RainDamageMax (0.5 of max). Use a conservative day-rate proxy:
            // ~10 HP/day weather decay when missed. Tuning lives in v0.2.0.
            const float decayHpPerDay = 10f;
            float decayHp = decayHpPerDay * deltaDays;
            float curHp = nview.GetZDO().GetFloat(ZDOVars.s_health, __instance.m_health);
            float newHp = Mathf.Max(__instance.m_health * 0.05f, curHp - decayHp); // don't kill from backfill
            if (newHp < curHp)
            {
                nview.GetZDO().Set(ZDOVars.s_health, newHp);
                nview.InvokeRPC(ZNetView.Everybody, "WNTHealthChanged", newHp);
                TrailbornePlugin.Log.LogInfo(
                    $"[Trailborne/M2] Cairn backfill: missed {deltaDays:F2}d → decayed {decayHp:F1} HP ({curHp:F0} → {newHp:F0}).");
            }
            nview.GetZDO().Set(TrailborneM2.ZdoLastWearTick, (float)nowDay);
        }

        // ── Comfort floor injection ─────────────────────────────────────
        // Patches SE_Rested.CalculateComfortLevel(bool inShelter, Vector3 position)
        // to clamp the result UP to the highest cairn comfort floor in range.
        // Doesn't touch the vanilla ComfortGroup table — cairns live OUTSIDE it
        // intentionally so they stack on top instead of dedup-replacing furniture.
        [HarmonyPatch(typeof(SE_Rested), nameof(SE_Rested.CalculateComfortLevel), new Type[] { typeof(bool), typeof(Vector3) })]
        [HarmonyPostfix]
        public static void SE_Rested_CalculateComfortLevel_Postfix(bool inShelter, Vector3 position, ref int __result)
        {
            try
            {
                int bonus = TrailborneM2.GetCairnComfortBonus(position);
                if (bonus > __result) __result = bonus;
            }
            catch (Exception e)
            {
                TrailbornePlugin.Log.LogWarning($"[Trailborne/M2] Comfort patch suppressed exception: {e.Message}");
            }
        }
    }
}
