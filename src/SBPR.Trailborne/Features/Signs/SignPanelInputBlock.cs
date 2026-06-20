using System;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// The shared modal-UI session guard for every SBPR full-screen uGUI surface — both sign
    /// panels (Painted Sign, Marker Sign) AND the bounded map viewer (TableEdit + FieldReadOnly).
    /// (Type kept named <c>SignPanelInputBlock</c> to avoid churn; it has been the de-facto shared
    /// guard since the viewer was OR'd into <see cref="AnyOpen"/> — §2L.5.) While any of them is
    /// open, make the game behave like every vanilla full-screen GUI:
    ///   • block player CHARACTER input so movement / attack / build don't leak through
    ///     (postfix on <c>Player.TakeInput</c> → force false),
    ///   • freeze CAMERA mouse-look so the world doesn't spin while the player drags
    ///     across the surface (postfix on <c>PlayerController.TakeInput(bool)</c> → force
    ///     false while a modal is open), and
    ///   • free + show the mouse cursor so the player can click swatches / buttons / pins, and
    ///     restore the gameplay lock on close (per-frame pump on the LIVE
    ///     <c>GameCamera.LateUpdate</c> seam + one-shot restore — <see cref="CursorPumpPatch"/>, §2L).
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
    /// All seams are vanilla methods (verified against assembly_valheim.dll
    /// metadata — clean-room, no decompiled source; ADR-0001 base-game is fair game). Inert on
    /// the dedicated server: no surface ever opens there (no local Player), so <c>AnyOpen</c>
    /// stays false and every patch is pass-through.
    /// </summary>
    public static class SignPanelInputBlock
    {
        // True while EITHER SBPR sign panel is open — the Painted Sign paint/text panel OR
        // the Marker Sign reference panel (v2, card t_0c7b782d) — OR the forked bounded map
        // VIEWER (v2 cartography, card t_cb831069). All are full-screen uGUI surfaces that
        // must block character/camera input and free the cursor identically.
        //
        // §2I.2 (issue 6, Part A — LIVENESS INVARIANT, belt-and-suspenders): EVERY contributor
        // below MUST derive from its panel's LIVE GameObject state (`_root.activeSelf` / the
        // canvas), never a standalone bool that can outlive its surface. A side-bool that latches
        // `true` after the panel is gone keeps AnyOpen latched, which silently kills the §2G
        // E-to-open gate (CanOpenOnUse early-outs on AnyOpen) until something re-opens the stuck
        // surface — the exact "dead-E until you re-use the Table" defect. The three contributors
        // already satisfy this:
        //   • SignPaintPanel.IsOpen            → `_instance._root.activeSelf` (un-latchable)
        //   • MarkerSignPanel.IsOpen           → `_instance._root.activeSelf` (un-latchable)
        //   • CartographyViewer.IsViewerOpen   → MapViewer.IsOpen => `_root.activeSelf` (§2I.1 —
        //     converted from the old `_open` side-bool that COULD latch; that was the root cause).
        // If you add a fourth contributor, it MUST follow the same activeSelf/liveness discipline —
        // do NOT feed AnyOpen from a bool you flip by hand.
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

        // §2L (issue 7, card t_1f82da71 / parent t_f7a6db7a): free the cursor while ANY SBPR modal
        // UI is open, re-seated onto a LIVE every-frame seam, with a deterministic restore on close.
        //
        // WHY THE OLD SEAM IS DEAD. The previous MouseCapturePatch postfixed
        // GameCamera.UpdateMouseCapture. On Daniel's build that method is an EMPTY 1-byte `ret`
        // (assembly_valheim decomp :85469-85471 — `public void UpdateMouseCapture() { }`); vanilla
        // moved gameplay cursor management into the new Unity Input System and left this body empty.
        // A 1-byte method is a JIT inlining candidate, and a Harmony postfix on an inlined target
        // never fires at the call site — so the cursor-free half was a silent no-op while the
        // mouselook-freeze half (the live Player/PlayerController.TakeInput postfixes) kept working.
        // That is exactly Daniel's report: "stops mouselook = correct, cursor not free."
        //
        // THE LIVE SEAM. GameCamera.LateUpdate (decomp :85452) is a Unity lifecycle MESSAGE — never
        // inlined, called every frame by the engine, and is itself the live caller of the empty
        // UpdateMouseCapture (:85464). Postfixing it guarantees our write runs every frame. A
        // whole-assembly scan finds the only surviving managed `Cursor.lockState` writers are
        // Menu.UpdateCursor + FejdStartup.UpdateCursor (pause menu / start screen — neither runs
        // during gameplay), so once we set lockState=None nothing managed re-locks it mid-play and
        // the cursor stays free. (§2L.6 option 2; all seams base-game, ADR-0001 clean-side, verified
        // against assembly_valheim.dll metadata.) Kept as a [HarmonyPatch] rather than a standalone
        // MonoBehaviour pump specifically so Runtime/PatchCheck guarantees at boot that the cursor
        // seam is actually woven — a MonoBehaviour pump is invisible to that watchdog and a future
        // refactor forgetting to instantiate it would silently re-break the cursor (the t_564f695a
        // "ships dead" failure class).
        //
        // RESTORE ON CLOSE (§2L.4b). The old design relied on vanilla's UpdateMouseCapture to
        // re-lock the next frame; that re-lock is gone from managed code. So we own the close edge:
        // a single `_wasOpen` edge-detector restores lockState=Locked/visible=false exactly once on
        // the AnyOpen true→false transition, making AT-TABLE-RESTORE deterministic rather than luck.
        // The next frame vanilla/Input-System resumes ownership for normal play.
        //
        // Server-safe: GameCamera does not exist on the dedicated server, so LateUpdate never fires
        // there; AnyOpen is also always false (no local Player). Pure pass-through either way.
        private static bool _wasOpen;

        [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
        public static class CursorPumpPatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                bool open = AnyOpen;

                if (open)
                {
                    // Per-frame pump: re-assert a free, visible cursor every frame the modal is up.
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    _wasOpen = true;
                }
                else if (_wasOpen)
                {
                    // Close edge (true→false): hand the cursor back to gameplay exactly once.
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                    _wasOpen = false;
                }
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
