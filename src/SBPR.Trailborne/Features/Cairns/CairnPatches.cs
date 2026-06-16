using System;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cairns
{
    /// <summary>
    /// Harmony patches for cairn behavior:
    ///   • WearNTear.Damage prefix → swallow COMBAT damage on cairns (player/monster
    ///     hits go through Damage(HitData); time decay does NOT, so this is immunity to
    ///     griefing only — it never touches decay).
    ///   • WearNTear.Awake postfix → backfill missed TIME decay when a chunk loads after
    ///     being out-of-zone, using the shared SBPR_LastWearTick in-game-day clock and
    ///     the shared Cairns.DecayHpPerDay rate (so it agrees with the resident ticker in
    ///     CairnTag). Keeps a 5% floor as reload safety — only the resident ticker is
    ///     allowed to drive a cairn all the way to collapse.
    ///   • SE_Rested.CalculateComfortLevel postfix → max-clamp cairn comfort
    ///     floor into the result.
    /// </summary>
    [HarmonyPatch]
    public static class CairnPatches
    {
        // ── Damage immunity ─────────────────────────────────────────────
        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
        [HarmonyPrefix]
        public static bool WearNTear_Damage_Prefix(WearNTear __instance)
        {
            if (__instance == null) return true;
            // GetComponentInParent so Harmony works regardless of which child collider was hit.
            var tag = __instance.GetComponent<CairnTag>();
            if (tag == null) tag = __instance.GetComponentInParent<CairnTag>();
            if (tag != null)
            {
                // Swallow COMBAT damage entirely (this method is the player/monster hit
                // path — Damage(HitData) → RPC_Damage → ApplyDamage(totalDamage, hit)).
                // TIME decay does NOT come through here: the resident ticker and backfill
                // call ApplyDamage(float) directly, which never invokes Damage(HitData).
                // So immunity-to-griefing and decay are cleanly separate — leave this as-is.
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
            var tag = __instance.GetComponent<CairnTag>();
            if (tag == null) return;
            var nview = __instance.GetComponent<ZNetView>();
            if (nview == null || nview.GetZDO() == null) return;
            if (!nview.IsOwner()) return; // owner-only writes ZDO

            // Shared in-game-day clock (EnvMan day length, NOT /86400) — the same value
            // the resident ticker reads, so resident + out-of-zone decay never double-count.
            float nowDay = Cairns.CurrentWearDay();
            if (nowDay < 0f) return; // world clock not up yet

            float lastWearDay = nview.GetZDO().GetFloat(Cairns.ZdoLastWearTick, -1f);
            if (lastWearDay < 0f)
            {
                // First load — seed and bail.
                nview.GetZDO().Set(Cairns.ZdoLastWearTick, nowDay);
                return;
            }

            float deltaDays = nowDay - lastWearDay;
            if (deltaDays <= 0f)
            {
                nview.GetZDO().Set(Cairns.ZdoLastWearTick, nowDay);
                return;
            }

            // Shared time-decay rate (Cairns.DecayHpPerDay, default 10 HP/in-game-day),
            // same source the resident ticker uses. KEEP the 5% floor: this backfill is
            // reload safety for time the cairn was unloaded, and must NOT collapse a cairn
            // sight-unseen on chunk load — only the resident ticker (which the player is
            // watching) is allowed to drive HP to 0 and trigger the vanilla destroy path.
            float decayHp = Cairns.DecayHpPerDay * deltaDays;
            float curHp = nview.GetZDO().GetFloat(ZDOVars.s_health, __instance.m_health);
            float newHp = Mathf.Max(__instance.m_health * 0.05f, curHp - decayHp); // floor — don't kill from backfill
            if (newHp < curHp)
            {
                // Route through vanilla ApplyDamage(float): it sets s_health AND fires the
                // REGISTERED RPC_HealthChanged, refreshing the cached health% that the
                // ember/downgrade/hover read. (The old code fired "WNTHealthChanged" — a
                // string vanilla never registers — so the cache stayed stale until reload.)
                // newHp is floored ≥5% > 0, so ApplyDamage subtracts without hitting Destroy.
                float applied = curHp - newHp;
                try
                {
                    __instance.ApplyDamage(applied);
                    Plugin.Log.LogInfo(
                        $"[Trailborne/M2] Cairn backfill: missed {deltaDays:F2}d → decayed {applied:F1} HP ({curHp:F0} → {newHp:F0}).");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Trailborne/M2] Backfill ApplyDamage threw: {e.Message}");
                }
            }
            nview.GetZDO().Set(Cairns.ZdoLastWearTick, nowDay);
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
                int bonus = Cairns.GetCairnComfortBonus(position);
                if (bonus > __result) __result = bonus;

                // Feed the open-air Rested patch's throttled cairn-in-range cache from this 2 s path
                // (vanilla calls CalculateComfortLevel every 2 s via Player.UpdateBaseValue for the
                // local player), so the ~50 Hz UpdateEnvStatusEffects postfix adds ZERO new physics.
                CairnComfortStash.Stash(bonus > 0);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/M2] Comfort patch suppressed exception: {e.Message}");
            }
        }
    }
}
