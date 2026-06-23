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
    ///   • free the mouse cursor by MASQUERADING AS A VANILLA GUI (§2L.18) — see below.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────────────────
    /// §2L.18 CURSOR APPROACH — masquerade, do NOT write Cursor.lockState (card t_94cc9713,
    /// Daniel's call 2026-06-23). The prior seven builds (§2L.7-R … §2L.17) all WROTE
    /// Cursor.lockState directly and all failed for Daniel's Linux rig: his live v0.2.35 logs
    /// proved lockState=None on ~96% of frames with the cursor STILL captive and ONLY on our
    /// custom UI — i.e. the capture is happening BELOW managed Cursor.lockState (SDL relative-mouse
    /// on the native Linux player), where no C# lockState write can reach it. The §2L.17 debounce
    /// build measured suppressedBlips=0, definitively killing the "we re-lock ourselves" theory too.
    ///
    /// So stop fighting lockState. Instead reproduce what a VANILLA GUI-open does — the path that
    /// empirically frees the cursor on Daniel's box (opening the inventory frees it) and that
    /// mature mods (Jotunn's GUIManager.BlockInput) use — by telling the game a GUI is open and
    /// letting vanilla's OWN systems release the cursor. Two BASE-GAME levers (ADR-0001: vanilla is
    /// fair game; this reproduces Jotunn's *behaviour*, no mod code copied):
    ///   1. <c>GameCamera.m_mouseCapture = false</c> + <c>GameCamera.UpdateMouseCapture()</c> on the
    ///      open edge, restored to true on close — vanilla's own mouse-capture flag/refresh.
    ///   2. <c>TextInput.IsVisible()</c> postfix → true while <see cref="AnyOpen"/>, so every vanilla
    ///      gate that frees the cursor / suppresses world input for an open text dialog fires for
    ///      our modal exactly as it does for a real one.
    /// No Cursor.lockState writes anywhere in this file — vanilla owns the cursor; we only signal
    /// "a GUI is open." A SBPR_CursorDiag-gated probe logs what vanilla computes so the in-game
    /// test is ground-truth, not a guess.
    /// ─────────────────────────────────────────────────────────────────────────────────────────
    ///
    /// Why <c>PlayerController.TakeInput</c> is the right camera seam (Issue 6 fix):
    /// vanilla's <c>PlayerController.LateUpdate</c> zeroes mouse-look with
    /// <c>if (!TakeInput(look:true) || InInventoryEtc()) m_character.SetMouseLook(Vector2.zero)</c>,
    /// and <c>PlayerController.TakeInput</c> ALREADY returns false whenever
    /// <c>TextInput.IsVisible()</c> — i.e. whenever the VANILLA sign text dialog is up. Our panel
    /// REPLACES that dialog, so that built-in suppression never triggered and the camera kept
    /// rotating; forcing the same <c>TakeInput</c> false restores the exact vanilla gate our
    /// replacement bypassed. (With §2L.18's TextInput masquerade ALSO active, that vanilla gate now
    /// fires on its own too — belt and suspenders.)
    ///
    /// All seams are vanilla methods (clean-room, ADR-0001 base-game is fair game). Inert on the
    /// dedicated server: no surface ever opens there (no local Player), so <c>AnyOpen</c> stays
    /// false and every patch is pass-through.
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
        [HarmonyPatch(typeof(PlayerController), "TakeInput", new System.Type[] { typeof(bool) })]
        public static class PlayerControllerTakeInputPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ref bool __result)
            {
                if (AnyOpen) __result = false;
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════
        // §2L.18 — THE MASQUERADE CURSOR DRIVER (card t_94cc9713). Edge-driven input block hosted
        // on the LIVE GameCamera.LateUpdate seam (a Unity lifecycle message — never inlined, fires
        // every frame on the client, never on the dedicated server). On the AnyOpen false→true edge
        // we ENABLE the vanilla input block (mirrors Jotunn.GUIManager.EnableInputBlock); on the
        // true→false edge we RESET it. NO Cursor.lockState writes — vanilla's own GUI-open cursor
        // path does the freeing once we've signalled a GUI is open (lever 2, the TextInput
        // masquerade below, is what fires those vanilla gates).
        //
        // Kept as a [HarmonyPatch] (not a free MonoBehaviour) so Runtime/PatchCheck guarantees at
        // boot that the seam actually wove — a MonoBehaviour pump is invisible to that watchdog and
        // a refactor forgetting to instantiate it would silently re-break the cursor (t_564f695a).
        // ════════════════════════════════════════════════════════════════════════════════════
        private static bool _blocked;
        private static int _diagFrame;
        private static int _diagLines;

        [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
        public static class CursorPumpPatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                bool open = AnyOpen;

                if (open && !_blocked)
                {
                    EnableInputBlock();
                    _blocked = true;
                    _diagFrame = 0;
                    _diagLines = 0;
                }
                else if (!open && _blocked)
                {
                    ResetInputBlock();
                    _blocked = false;
                }

                // DIAGNOSTIC (SBPR_CursorDiag, default ON): log what VANILLA computes for the cursor
                // while our masquerade is active. We do NOT write lockState — so lockState/visible
                // here are vanilla's own decision given "a GUI is open." Cursor free + usable in-game
                // with lockState=None ⇒ masquerade works. Still captive ⇒ capture is below managed
                // lockState (SDL) and no managed path can fix it (definitive negative result).
                if (open && (Plugin.CursorDiag?.Value ?? false) && (_diagFrame++ % 30 == 0) && _diagLines < 60)
                {
                    _diagLines++;
                    Plugin.Log.LogInfo(
                        "[Trailborne/CursorDiag] §2L.18 masquerade AnyOpen=true vanillaLockState=" + Cursor.lockState
                        + " vanillaVisible=" + Cursor.visible
                        + " textInputMasqueraded=" + AnyOpen
                        + " mouseCaptureCleared=" + _blocked
                        + " rawIsMouseActive=" + ZInput.IsMouseActive()
                        + " gamepadActive=" + ZInput.GamepadActive
                        + " | paint=" + SignPaintPanel.IsOpen
                        + " marker=" + MarkerSignPanel.IsOpen
                        + " viewer=" + SBPR.Trailborne.Features.Cartography.CartographyViewer.IsViewerOpen);
                }
            }
        }

        // Mirror Jotunn.GUIManager.EnableInputBlock: clear vanilla's mouse-capture flag and refresh
        // it, exactly as opening a vanilla GUI does. m_mouseCapture is private + we don't publicize
        // assembly_valheim, so reach it through HarmonyLib Traverse. UpdateMouseCapture() is public.
        private static void EnableInputBlock()
        {
            var gc = GameCamera.instance;
            if (gc == null) return;
            Traverse.Create(gc).Field("m_mouseCapture").SetValue(false);
            gc.UpdateMouseCapture();
        }

        // Mirror Jotunn.GUIManager.ResetInputBlock: restore vanilla's mouse-capture on close.
        private static void ResetInputBlock()
        {
            var gc = GameCamera.instance;
            if (gc == null) return;
            Traverse.Create(gc).Field("m_mouseCapture").SetValue(true);
            gc.UpdateMouseCapture();
        }

        // §2L.18 lever 2 — the TextInput masquerade. Postfix TextInput.IsVisible() → true while a
        // SBPR modal is open. Every vanilla gate that frees the cursor / suppresses world input for
        // an open text dialog reads this predicate, so our modal gets the identical treatment a real
        // vanilla text dialog gets — the same path that frees the cursor when Daniel opens a vanilla
        // GUI. NRE-safe: a full decomp scan confirms no caller dereferences TextInput.instance after
        // checking IsVisible() (IsVisible itself null-guards m_instance). Server-safe: AnyOpen false
        // on a dedicated server → pass-through.
        [HarmonyPatch(typeof(TextInput), "IsVisible")]
        public static class TextInputMasqueradePatch
        {
            [HarmonyPostfix]
            private static void Postfix(ref bool __result)
            {
                if (AnyOpen) __result = true;
            }
        }

        /// <summary>
        /// §2F (issue 7): suppress vanilla's pause-menu open while ANY SBPR modal UI is up, so
        /// the Escape that closes our viewer / sign panel does NOT also pop the game menu the
        /// same frame. Seam = <c>Menu.Show()</c> (single parameterless caller in Menu.Update).
        /// Self-clearing (keys on <see cref="AnyOpen"/>) and server-safe.
        /// </summary>
        [HarmonyPatch(typeof(Menu), "Show", new System.Type[0])]
        public static class MenuOpenSuppressPatch
        {
            [HarmonyPrefix]
            private static bool Prefix()
            {
                return !AnyOpen;
            }
        }

        // §2L.13 (card t_a1cf35b0, sibling of the cursor fix): the Inventory hotkey must NOT open the
        // inventory while an SBPR modal owns the screen. The toggle is read inside InventoryGui.Update
        // (→ Show(null)), NOT through Player.TakeInput, so the TakeInput block above never gated it —
        // Daniel could pop the inventory over the sign panel and the Local Map. A skip-original prefix
        // on InventoryGui.Show(Container, int) gated on AnyOpen. While a modal is up, no legitimate
        // container/station open can be triggered (Player.TakeInput is forced false), so blocking Show
        // here only suppresses the stray toggle. Self-clearing + server-safe.
        [HarmonyPatch(typeof(InventoryGui), "Show", new System.Type[] { typeof(Container), typeof(int) })]
        public static class InventoryOpenSuppressPatch
        {
            [HarmonyPrefix]
            private static bool Prefix()
            {
                return !AnyOpen;
            }
        }
    }
}
