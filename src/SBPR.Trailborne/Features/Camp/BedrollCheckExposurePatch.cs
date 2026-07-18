using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Camp
{
    /// <summary>
    /// Prefab-gated relax of the vanilla <see cref="Bed"/> exposure gate for the SBPR
    /// bedroll ONLY (spec §2.2). Drops the <c>cover ≥ 0.8</c> clause while KEEPING the
    /// <c>underRoof</c> requirement (Q6 — no open-sky sleep), so the tent canopy
    /// (underRoof=true, cover≈0.47) becomes a legal sleep spot for the bedroll but for
    /// nothing else.
    ///
    /// 🔴 REGRESSION GUARD (AT-BEDROLL-VANILLA): the patch is gated on
    /// <see cref="BedrollTag"/> presence. On every vanilla bed (and every other SBPR
    /// piece) the prefix returns true (run original), so a real bed under a 0.47-cover
    /// lean-to STILL refuses ($msg_bedtooexposed). An ungated relax would be a global
    /// balance change — non-negotiably wrong. See the smoke test in the review notes.
    ///
    /// ── ARCHITECTURE NOTE (verified against decomp, surfaced for review) ──
    /// Under the shipped design, the bedroll's E-press is owned by
    /// <see cref="BedrollTag"/> (an Interactable that reimplements the full sleep-gate
    /// chain and drives AttachStart WITHOUT setting spawn — the only way to honor
    /// Daniel's "no spawn overwrite" lock, since vanilla Bed.Interact's sleep branch
    /// :99643 is unreachable without first claiming the bed as spawn :99613/:99651).
    /// BedrollTag applies the SAME relaxed exposure rule inline. Consequently
    /// <c>Bed.CheckExposure</c> is not on the live sleep path for our prefab today, so
    /// this patch is BELT-AND-BRACES: it guarantees the relaxed rule holds for our
    /// prefab through ANY code path that calls Bed.CheckExposure (a future refactor
    /// routing sleep through Bed.Interact, another mod invoking it, etc.). It is
    /// prefab-gated and cannot regress vanilla beds. Kept per spec §2.2 and to keep the
    /// exposure rule single-sourced-in-intent even if the interaction owner changes.
    ///
    /// Patch shape: Prefix on the private <c>Bed.CheckExposure(Player)</c>. When our tag
    /// is present, do the cover test ourselves with the relaxed rule and SKIP the
    /// original (set __result, return false). Otherwise return true → run vanilla
    /// unchanged. Registered via harmony.PatchAll(typeof(BedrollCheckExposurePatch)) in
    /// Plugin.Awake — PatchCheck ERRORs at boot if this weaves nothing.
    /// </summary>
    [HarmonyPatch(typeof(Bed), "CheckExposure")]
    public static class BedrollCheckExposurePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Bed __instance, Player human, ref bool __result)
        {
            // Not our bedroll → run vanilla CheckExposure unchanged (regression guard).
            if (__instance == null || __instance.GetComponent<BedrollTag>() == null)
                return true;

            // Our bedroll: relaxed rule. underRoof still required; 0.8 cover dropped.
            Cover.GetCoverForPoint(__instance.GetSpawnPoint(), out float coverPercentage, out bool underRoof);
            if (!underRoof)
            {
                if (human != null) human.Message(MessageHud.MessageType.Center, "$msg_bedneedroof");
                __result = false;
                return false;
            }
            _ = coverPercentage;   // intentionally NOT gated on < 0.8 — the relax.
            __result = true;
            return false;
        }
    }
}
