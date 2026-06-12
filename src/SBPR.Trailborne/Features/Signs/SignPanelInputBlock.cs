using System;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// While the Painted Sign panel is open, make it behave like every vanilla
    /// full-screen GUI:
    ///   • block player CHARACTER input so movement / attack / build don't leak through
    ///     the panel (postfix on <c>Player.TakeInput</c> → force false),
    ///   • freeze CAMERA mouse-look so the world doesn't spin while the player drags
    ///     across the panel (postfix on <c>PlayerController.TakeInput(bool)</c> → force
    ///     false while the panel is open), and
    ///   • release the mouse cursor so the player can click swatches/buttons (postfix on
    ///     <c>GameCamera.UpdateMouseCapture</c> → unlock + show the cursor each frame).
    ///
    /// Why <c>PlayerController.TakeInput</c> is the right camera seam (Issue 6 fix):
    /// vanilla's <c>PlayerController.LateUpdate</c> zeroes mouse-look with
    /// <c>if (!TakeInput(look:true) || InInventoryEtc()) m_character.SetMouseLook(Vector2.zero)</c>,
    /// and <c>PlayerController.TakeInput</c> ALREADY returns false whenever
    /// <c>TextInput.IsVisible()</c> — i.e. whenever the VANILLA sign text dialog is up.
    /// Our panel REPLACES that dialog (SignInteractPatch suppresses it), so that built-in
    /// suppression never triggers and the camera kept rotating. Forcing the same
    /// <c>TakeInput</c> false while our panel is open restores the exact vanilla gate our
    /// replacement bypassed — no nuking of <c>GameCamera.UpdateCamera</c> (the earlier
    /// approach, which also targeted the wrong method: <c>UpdateCamera</c> only gates
    /// camera ZOOM, not look — and was never even registered).
    ///
    /// All three seams are vanilla methods (verified against assembly_valheim.dll
    /// metadata — clean-room, no decompiled source). Inert on the dedicated server: the
    /// panel never opens there (no local Player), so <c>SignPaintPanel.IsOpen</c> stays
    /// false and every patch is pass-through.
    /// </summary>
    public static class SignPanelInputBlock
    {
        // True while EITHER SBPR sign panel is open — the Painted Sign paint/text panel OR
        // the Marker Sign reference panel (v2, card t_0c7b782d) — OR the forked bounded map
        // VIEWER (v2 cartography, card t_cb831069). All are full-screen uGUI surfaces that
        // must block character/camera input and free the cursor identically.
        internal static bool AnyOpen =>
            SignPaintPanel.IsOpen
            || MarkerSignPanel.IsOpen
            || SBPR.Trailborne.Features.Cartography.CartographyViewer.IsViewerOpen;

        [HarmonyPatch(typeof(Player), "TakeInput")]
        public static class TakeInputPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ref bool __result)
            {
                if (AnyOpen) __result = false;
            }
        }

        // Freeze camera mouse-look by routing through vanilla's own suppression gate.
        // PlayerController.TakeInput(bool look = false) is a single private method (the
        // bool is a default param, not an overload), but we supply the explicit
        // parameter Type[] anyway per the overload-disambiguation discipline so a future
        // vanilla overload can't silently re-bind us to the wrong target.
        [HarmonyPatch(typeof(PlayerController), "TakeInput", new Type[] { typeof(bool) })]
        public static class PlayerControllerTakeInputPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ref bool __result)
            {
                if (AnyOpen) __result = false;
            }
        }

        [HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
        public static class MouseCapturePatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (!AnyOpen) return;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        /// <summary>
        /// §2F (issue 7): suppress vanilla's pause-menu open while ANY SBPR modal UI is up, so
        /// the Escape that closes our viewer / sign panel does NOT also pop the game menu the
        /// same frame.
        ///
        /// Seam = <c>Menu.Show()</c> — a single parameterless public instance method (decomp
        /// <c>Menu.cs:45762</c>) whose ONLY internal caller is the Escape/JoyMenu gate in
        /// <c>Menu.Update</c>. A skip-original prefix (gated on <see cref="AnyOpen"/>) cleanly
        /// stops the menu from opening WITHOUT consuming the keystroke globally or touching any
        /// other input path. Chosen over hooking <c>Minimap.IsOpen()</c> (which feeds ~10
        /// vanilla gates — build/craft/interact/camera — and would have a wide blast radius);
        /// <c>Menu.Show</c> has exactly one caller and one effect.
        ///
        /// Self-clearing (AT-VIEWEXIT-3): the gate keys on <see cref="AnyOpen"/>, false the
        /// moment the viewer/panel closes. The very Escape that closes the viewer is swallowed
        /// for the menu THAT frame; the next Escape (AnyOpen now false → prefix passes through)
        /// opens the menu normally. Escape is never permanently eaten.
        ///
        /// Because <see cref="AnyOpen"/> already covers all three SBPR surfaces (both sign
        /// panels + the map viewer), this one prefix fixes the identical Escape→menu leak on
        /// MarkerSignPanel / SignPaintPanel in the same stroke (AT-VIEWEXIT-5). Server-safe:
        /// AnyOpen is always false on a dedicated server (no local Player), so it's pure
        /// pass-through there.
        /// </summary>
        [HarmonyPatch(typeof(Menu), "Show", new Type[0])]
        public static class MenuOpenSuppressPatch
        {
            [HarmonyPrefix]
            private static bool Prefix()
            {
                // Return false → skip Menu.Show's original body (the menu does not open) while
                // any SBPR modal UI is up. Return true → normal vanilla behaviour.
                return !AnyOpen;
            }
        }
    }
}
