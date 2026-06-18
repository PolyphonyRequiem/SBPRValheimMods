// ─────────────────────────────────────────────────────────────────────────────
// MinimapMKeyOwnerPatch — SBPR owns the M (Map) input edge.
//
// Spec: docs/v2/planning/local-map-mkey-open-impl-spec.md §3 (card t_f9a04fda).
// Design: docs/design/map-provider-model.md §1 (🟢 DECIDED 2026-06-15) — "M is the
// single map key; SBPR owns it."
//
// Mechanism (LOCKED, spec §3): a Harmony PREFIX on Minimap.Update that, when our
// controller wants the M press, routes it to the controller and then CONSUMES the
// Map/JoyMap button edge via ZInput.ResetButtonStatus(...) — vanilla's own
// input-consume idiom (assembly_utils ButtonDef.ResetState) — so vanilla's own
// Minimap.Update body (which runs immediately after, same frame) reads a cleared
// edge and never toggles its Large map. No double-stack.
//
// Non-skip: the prefix is void → vanilla's Update STILL runs (pins, UpdateExplore /
// the Kit fog write, shared-map fade, death→None). We neutralize exactly ONE thing:
// the Map-button edge. This composes cleanly with the existing Minimap.Update
// POSTFIX in WorldPinReconcilePatches (different patch type, prefix vs postfix).
//
// Clean-side (ADR-0001): patches base-game Minimap only; reads vanilla ZInput. No
// other mod's code. Client+gfx only (early-out on a headless/server graphics device).
// ─────────────────────────────────────────────────────────────────────────────
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace SBPR.Trailborne.Features.Cartography
{
    [HarmonyPatch(typeof(Minimap), "Update")]
    public static class MinimapMKeyOwnerPatch
    {
        /// <summary>
        /// Runs BEFORE vanilla Minimap.Update each frame. If the M edge is down this
        /// frame and a SBPR controller wants to own it, route it to the controller,
        /// then CONSUME the edge so vanilla's own GetButtonDown("Map") (decomp
        /// :47085) sees nothing → no vanilla Large map opens.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix(Minimap __instance)
        {
            if (__instance == null) return;
            // Client + real graphics only. The dedicated server has no Minimap, but
            // guard defensively so a null/headless device can never reach ZInput.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;

            var ctrl = LocalMapController.Instance;
            if (ctrl == null) return;

            // Mirror vanilla's OWN suppression gate (:47074) + SBPR modal state so we
            // don't steal M while typing / in a menu / inventory. Without this, an "m"
            // typed into a name field or chat would trip the map.
            if (!ctrl.MKeyContextAllowsMapToggle()) return;

            // Vanilla's exact M predicate (decomp :47085): keyboard Map, or JoyMap
            // when not held with the look/alt modifiers.
            bool mEdge = ZInput.GetButtonDown("Map")
                         || (ZInput.GetButtonDown("JoyMap")
                             && (!ZInput.GetButton("JoyLTrigger") || !ZInput.GetButton("JoyLBumper"))
                             && !ZInput.GetButton("JoyAltKeys"));
            if (!mEdge) return;

            // SBPR owns M in ALL states (design §1). The controller decides what M
            // DOES (open / close / nothing) from provider + viewer state; we then
            // ALWAYS consume so vanilla never toggles its Large map on this press —
            // even in the "no bound map → M does nothing" row (design §1 row 1:
            // "SBPR fully suppresses vanilla's Large-map toggle … nomap-on AND -off").
            // So HandleMapKeyPressed() may be a no-op, but the consume below is not.
            ctrl.HandleMapKeyPressed();

            ZInput.ResetButtonStatus("Map");
            ZInput.ResetButtonStatus("JoyMap");
        }
    }
}
