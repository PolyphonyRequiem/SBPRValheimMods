// ============================================================================
//  SPIKE (throwaway — card t_e8bbbe48). NOT production code. Branch-only.
// ----------------------------------------------------------------------------
//  Bootstraps the SpikeMapViewer onto the live Minimap once it has started.
//  Registered via harmony.PatchAll in Plugin.Awake so the PatchCheck watchdog
//  (Runtime/PatchCheck.cs) stays green — an attributed-but-unregistered class
//  would ERROR at boot.
//
//  Client-only by construction: Minimap.Start only runs where a Minimap exists
//  (a graphics client); the dedicated server never instantiates one, so this
//  postfix never fires server-side and adds zero server cost.
// ============================================================================

using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.CartographySpike
{
    [HarmonyPatch(typeof(Minimap), "Start")]
    public static class SpikeBootstrapPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Minimap __instance)
        {
            if (__instance.GetComponent<SpikeMapViewer>() == null)
            {
                __instance.gameObject.AddComponent<SpikeMapViewer>();
                Plugin.Log.LogInfo(
                    "[Spike] SpikeMapViewer attached to Minimap — press F9 in-game to toggle the bounded 1000 m viewer.");
            }
        }
    }
}
