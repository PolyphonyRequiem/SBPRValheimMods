using System;
using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Features.MarkerSigns;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Harmony prefix on <c>Sign.Interact</c>. Routes interaction for OUR placed signs and
    /// skips the vanilla body; vanilla signs (no SBPR tag) fall through unchanged.
    ///
    /// Two SBPR sign families are handled:
    ///   • <b>Painted Sign</b> (carries a <see cref="SignTag"/>): primary E opens the custom
    ///     combined Paint+Text panel (§A2.6). A held interact falls through to vanilla.
    ///   • <b>Marker Sign</b> (carries a <see cref="MarkerSignTag"/>, v2 Marker Signs feature,
    ///     card t_0c7b782d):
    ///       – primary E (alt==false): open the dedicated <see cref="MarkerSignPanel"/> (icon +
    ///         name + pin state + pin/unpin button — NOT the pigment SignPaintPanel, which a
    ///         marker has no colors for and which hard-requires a SignTag).
    ///       – <b>Shift+E (alt==true): toggle the marker's SBPR_Pinned ZDO bool and
    ///         project/remove its WorldPin</b> — the deferred map-pin gesture, now wired
    ///         (design §6 / impl-spec §4.1). A plain Painted Sign has no pin gesture, so on a
    ///         SignTag-only sign Shift+E falls through to vanilla.
    ///
    /// Behaviour notes:
    ///   • A held interact always falls through to vanilla (don't fight the long-press).
    ///   • Client-only effect: the panel early-returns without a local Player, and the pin
    ///     projection no-ops without a Minimap instance, so on the dedicated server this
    ///     prefix simply suppresses the vanilla dialog for our pieces.
    ///   • Grounding: the Shift key arrives as <c>alt==true</c> via
    ///     <c>ZInput.GetButton("AltPlace") → Player.Interact(go, hold, alt) →
    ///     componentInParent.Interact(this, hold, alt)</c> (decomp :16115/:19270/:19280).
    ///     No new input plumbing.
    /// </summary>
    [HarmonyPatch(typeof(Sign), nameof(Sign.Interact))]
    public static class SignInteractPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Sign __instance, Humanoid character, bool hold, bool alt, ref bool __result)
        {
            if (hold) return true; // let vanilla handle held-interact

            // ── Marker Sign branch (v2): carries a MarkerSignTag. ──
            var marker = __instance.GetComponent<MarkerSignTag>();
            if (marker != null)
            {
                if (alt)
                {
                    // Shift+E → toggle pinned + project/remove the WorldPin (the fast path).
                    try
                    {
                        bool nowPinned = !marker.ReadPinned();
                        if (!marker.WritePinned(nowPinned))
                        {
                            // ZDO not ready (ghost / not yet networked) — let vanilla have it.
                            return true;
                        }

                        if (nowPinned) WorldPins.ProjectPinnedNow(marker);
                        else           WorldPins.RemoveProjected(marker.GetZdoId());

                        __result = true; // interaction handled
                        return false;    // consume — no vanilla dialog
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError($"[Trailborne/MarkerSigns] Shift+E pin toggle failed: {e}");
                        return true; // fall back to vanilla on unexpected error
                    }
                }

                // Primary E on a marker → the marker reference panel (icon + name + pin
                // state + pin/unpin button, §1.4 / AT-MARK-1). This is a DEDICATED panel,
                // NOT SignPaintPanel — a marker has no pigment colors (Q1 defers color) and
                // SignPaintPanel hard-requires a SignTag, so routing a marker there would
                // silently no-op.
                try
                {
                    MarkerSignPanel.Open(__instance);
                    __result = true;
                    return false;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"[Trailborne/MarkerSigns] Failed to open marker sign panel: {e}");
                    return true;
                }
            }

            // ── Painted Sign branch (v1): carries a SignTag. ──
            var tag = __instance.GetComponent<SignTag>();
            if (tag == null) return true; // not ours — vanilla behavior

            // A plain Painted Sign has no pin gesture; Shift+E falls through to vanilla.
            if (alt) return true;

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
