using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// While the Painted Sign panel is open, make it usable the same way every vanilla
    /// full-screen GUI does:
    ///   • block player character input so movement / attack / build don't leak through
    ///     the panel (postfix on <c>Player.TakeInput</c> → force false), and
    ///   • release the mouse cursor so the player can click swatches/buttons (postfix on
    ///     <c>GameCamera.UpdateMouseCapture</c> → if the panel is open, unlock + show the
    ///     cursor, overriding the camera's per-frame re-capture).
    ///
    /// Both seams are vanilla public methods (verified against assembly_valheim.dll
    /// metadata — clean-room, no decompiled source). Inert on the dedicated server:
    /// the panel never opens there (no local Player), so <c>SignPaintPanel.IsOpen</c>
    /// stays false and both postfixes are pass-through.
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
    }
}
