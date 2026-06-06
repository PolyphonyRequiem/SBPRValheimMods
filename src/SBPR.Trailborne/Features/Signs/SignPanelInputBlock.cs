using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// While the Painted Sign panel is open, make it usable the same way every vanilla
    /// full-screen GUI does:
    ///   • block player character input so movement / attack / build don't leak through
    ///     the panel (postfix on <c>Player.TakeInput</c> → force false),
    ///   • release the mouse cursor so the player can click swatches/buttons (postfix on
    ///     <c>GameCamera.UpdateMouseCapture</c> → if the panel is open, unlock + show the
    ///     cursor, overriding the camera's per-frame re-capture), and
    ///   • freeze the camera rotation so the world doesn't spin while the player drags
    ///     across the panel (prefix on <c>GameCamera.UpdateCamera</c> → skip the body
    ///     entirely while the panel is open). Vanilla full-screen UIs (InventoryGui,
    ///     Menu, Minimap, etc.) already gate camera rotation behind a big boolean in
    ///     UpdateCamera; we just add our panel to the same suppression set the surgical
    ///     way (early-return) so behaviour matches vanilla muscle memory exactly.
    ///
    /// All three seams are vanilla public methods (verified against assembly_valheim.dll
    /// metadata — clean-room, no decompiled source). Inert on the dedicated server:
    /// the panel never opens there (no local Player), so <c>SignPaintPanel.IsOpen</c>
    /// stays false and every patch is pass-through.
    /// </summary>
    public static class SignPanelInputBlock
    {
        [HarmonyPatch(typeof(Player), "TakeInput")]
        public static class TakeInputPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ref bool __result)
            {
                if (SignPaintPanel.IsOpen) __result = false;
            }
        }

        [HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
        public static class MouseCapturePatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (!SignPaintPanel.IsOpen) return;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
        public static class UpdateCameraPatch
        {
            // Daniel 2026-06-05: "the camera doesn't lock when the UI appears." Vanilla
            // already suppresses camera rotation behind InventoryGui.IsVisible() etc.;
            // we mirror that pattern with a prefix that early-returns while OUR panel is
            // open, so the camera doesn't drift on the player's mouse drag across the
            // paint UI. False = skip the original UpdateCamera body for this frame.
            [HarmonyPrefix]
            private static bool Prefix()
            {
                return !SignPaintPanel.IsOpen; // true = run vanilla, false = skip
            }
        }
    }
}
