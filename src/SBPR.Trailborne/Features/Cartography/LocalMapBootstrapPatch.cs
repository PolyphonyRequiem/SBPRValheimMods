// ============================================================================
//  Trailborne v2 cartography — Local Map controller bootstrap
// ----------------------------------------------------------------------------
//  Attaches the client-only LocalMapController (the carry/equip state machine that
//  drives the bounded viewer) onto the live Minimap once it has started — the same
//  bootstrap idiom the spike used (a registered Harmony postfix on Minimap.Start, so
//  the PatchCheck watchdog stays green; an attributed-but-unregistered class ERRORs
//  at boot).
//
//  Client-only by construction: Minimap.Start only runs where a Minimap exists (a
//  graphics client); the dedicated server never instantiates one, so this postfix
//  never fires server-side and the controller adds zero server cost.
//
//  Clean-side (ADR-0001): postfixes the base-game Minimap only.
// ============================================================================

using HarmonyLib;

namespace SBPR.Trailborne.Features.Cartography
{
    [HarmonyPatch(typeof(Minimap), "Start")]
    public static class LocalMapBootstrapPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Minimap __instance)
        {
            if (__instance == null) return;
            if (__instance.GetComponent<LocalMapController>() == null)
            {
                __instance.gameObject.AddComponent<LocalMapController>();
                Plugin.Log.LogInfo(
                    "[Trailborne/Cartography] LocalMapController attached to Minimap (Local Map carry/equip binding live).");
            }
        }
    }
}
