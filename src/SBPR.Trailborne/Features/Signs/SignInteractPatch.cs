using System;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Harmony prefix on <c>Sign.Interact</c>: when the player interacts with one of
    /// OUR placed signs (carries a <see cref="SignTag"/>), open the custom combined
    /// Paint+Text panel (§A2.6) INSTEAD of the vanilla single-line text dialog, and
    /// skip the vanilla body. Signs without a SignTag fall through to vanilla unchanged.
    ///
    /// This is the player ENTRYPOINT for the Painted Sign feature (re-lock 2026-06-05):
    /// it replaces both the vanilla text dialog AND the retired apply-ink-item gesture.
    ///
    /// Behaviour notes:
    ///   • Only the primary interact (not hold) opens the panel; a held interact falls
    ///     through to vanilla so we don't fight the long-press affordance.
    ///   • Client-only effect: the panel early-returns without a local Player, so on the
    ///     dedicated server this prefix simply suppresses the vanilla dialog for our
    ///     piece (there is no server-side sign dialog anyway).
    ///   • The deferred Shift+E map-pin gesture is NOT wired in v0.1.0 (tracked
    ///     follow-up); this patch intentionally does not add it.
    /// </summary>
    [HarmonyPatch(typeof(Sign), nameof(Sign.Interact))]
    public static class SignInteractPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Sign __instance, Humanoid character, bool hold, bool alt, ref bool __result)
        {
            if (hold) return true; // let vanilla handle held-interact
            var tag = __instance.GetComponent<SignTag>();
            if (tag == null) return true; // not ours — vanilla behavior

            try
            {
                SignPaintPanel.Open(__instance);
                __result = true;  // interaction handled
                return false;     // skip the vanilla text dialog
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Trailborne/M1] Failed to open sign panel: {e}");
                return true; // fall back to vanilla on unexpected error
            }
        }
    }
}
