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
        [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
        public static class CursorPumpPatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                // §2L.17 (card t_94cc9713): defer to the SINGLE debounced cursor authority
                // (ModalCursorDriver). Assert a free cursor in this LateUpdate phase while the authority
                // says free, but NEVER re-lock here. The old close-edge in this method was ONE OF THREE
                // competing re-lockers; on a 1-frame AnyOpen blip (MapSurface._root.activeSelf flickers
                // false during a uGUI rebuild) they each slammed lockState=Locked → SDL relative-mode
                // re-arm → cursor warps to centre, 2-3x/sec, even though lockState reads None ~96% of
                // frames. The lone re-lock authority is now ModalCursorDriver's DEBOUNCED close-edge.
                if (ModalCursorDriver.FreeThisFrame)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
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

        // §2L.12 (re-report ticket-cursor-captive-modals, card t_f7a5ad53): the REAL fix for the
        // cursor "capture"/snap-to-centre on every SBPR modal. Supersedes the per-frame
        // CursorPumpPatch approach as the load-bearing mechanism (the pump is kept only as a
        // belt-and-suspenders visible-cursor assert; this patch is what actually stops the snap).
        //
        // ⚠️ INCOMPLETE — read with §2L.14 below (card t_cad2c6f3). This narrative names Steam Input's
        // virtual gamepad as THE cause; that is only ONE of two cases. This patch is load-bearing ONLY
        // while vanilla's event-driven UpdateCursor actually fires (i.e. the input source is churning).
        // On a gamepad-ABSENT keyboard+mouse rig the source never flips, UpdateCursor never runs, and
        // this forced IsMouseActive is never read → cursor never freed. The source-independent
        // ModalCursorDriver in §2L.14 is what fixes Daniel's rig; this patch is KEPT for the
        // virtual-pad case. Treat the block below as the historical one-case account.
        //
        // ROOT CAUSE (decompiled, verified — assembly_valheim + assembly_utils + Unity.InputSystem):
        // Valheim 0.221.x routes UI pointers through the new Unity Input System, whose
        // InputSystemUIInputModule.ProcessPointer FORCES every mouse pointer event to screen-centre
        // and discards the real delta whenever Cursor.lockState == Locked (Unity.InputSystem
        // ~:47456) — that IS the snap. Nothing in the Input System WRITES lockState; the only
        // managed writers during play are Menu.UpdateCursor (assembly_valheim :45817) and
        // FejdStartup.UpdateCursor (:83091), BOTH computing
        //   Cursor.lockState = !ZInput.IsMouseActive() ? Locked : None
        // and both firing EVENT-DRIVEN on ZInput.OnInputLayoutChanged (an input-source switch),
        // never per-frame. So the cursor is re-Locked precisely when IsMouseActive() is false, i.e.
        // when the active input source is NOT KeyboardMouse (assembly_utils Internal_IsMouseActive
        // :10847 → m_inputSource == KeyboardMouse). On Daniel's KEYBOARD+MOUSE rig the culprit is
        // STEAM INPUT presenting a VIRTUAL gamepad: its drifting stick keeps firing OnActionPerformed
        // → flips m_inputSource to Gamepad → OnInputLayoutChanged → UpdateCursor recomputes Locked
        // every frame → ProcessPointer center-snaps. That is also why opening the inventory (a
        // mouse-driven action → m_mouseInputThisFrame suppresses the gamepad switch, OnInput :9590)
        // momentarily freed the cursor: the mouse action flipped the source back to KeyboardMouse.
        //
        // WHY THE PER-FRAME PUMP LOSES. CursorPumpPatch sets lockState=None in GameCamera.LateUpdate,
        // but the event-driven UpdateCursor sets it back to Locked in its own phase whenever the
        // virtual pad re-grabs the source — so the pump fights the engine and loses, constantly.
        //
        // THE FIX (work WITH the engine, don't race it). Postfix ZInput.IsMouseActive() → force true
        // while AnyOpen. Then vanilla's OWN UpdateCursor computes None+visible (no snap), and the very
        // drifting-pad churn that caused the bug now DRIVES the fix every frame. This mirrors the
        // existing TakeInputPatch idiom exactly (postfix a vanilla predicate → force a constant while
        // a modal is open). Blast radius is contained: IsMouseActive() has 9 readers; the only ones
        // that can fire while an SBPR modal owns the screen are the two UpdateCursor sites (exactly
        // what we want freed) — the gamepad item-drag gate (InventoryGui.UpdateItemDrag :41647) and
        // the large-map mouse raycast (Minimap.UpdateBiome :48730) are inert (Player/PlayerController
        // .TakeInput already forced false by AnyOpen; no vanilla inventory/large-map open over our
        // modal). Design intent confirmed by Daniel 2026-06-21: he uses KB+M, no gamepad-usability
        // requirement on the SBPR modals — the modals just need a free cursor regardless of the
        // source Steam Input presents. Server-safe: AnyOpen is always false on a dedicated server.
        [HarmonyPatch(typeof(ZInput), "IsMouseActive")]
        public static class MouseActiveForcePatch
        {
            [HarmonyPostfix]
            private static void Postfix(ref bool __result)
            {
                // While any SBPR modal is open, report the mouse as the active input source so
                // vanilla's event-driven UpdateCursor computes lockState=None (cursor free, no
                // Input-System center-snap) even when Steam Input's virtual pad owns the source.
                if (AnyOpen) __result = true;
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════
        // §2L.14 (ticket-cursor-lock-map-sign, card t_cad2c6f3): the SOURCE-INDEPENDENT cursor
        // assert + a diagnostic probe. THE THIRD report of this family — and the forensic reason
        // the §2L.12 fix above did NOT stick for Daniel.
        //
        // WHY §2L.12 MISSES DANIEL'S RIG (decompile-grounded, RequiemPrime logs 2026-06-22):
        // MouseActiveForcePatch makes vanilla's OWN UpdateCursor compute lockState=None — but both
        // UpdateCursor sites (Menu :45815, FejdStartup :83089) are EVENT-DRIVEN on
        // ZInput.OnInputLayoutChanged; they fire ONLY when the active input SOURCE flips. On a box
        // with Steam-Input's drifting VIRTUAL gamepad the source flips every frame → UpdateCursor
        // fires every frame → the fix rides that churn (works). On Daniel's KB+M box there is NO
        // gamepad at all (verified: no /dev/input/js*, no controller, Steam Input off) → the source
        // NEVER flips while the modal is open → UpdateCursor never runs → the forced IsMouseActive
        // is never read → the cursor is never recomputed to free. §2L.12 is parasitic on churn that
        // is ABSENT here; he is the exact case it does not cover.
        //
        // WHY THE OLD PUMP (CursorPumpPatch on GameCamera.LateUpdate) ALSO LOSES: Unity's
        // InputSystemUIInputModule.ProcessPointer center-snaps the pointer whenever it reads
        // lockState==Locked, and it runs in the UPDATE phase — BEFORE GameCamera.LateUpdate. So the
        // LateUpdate pump writes None one frame too late: the snap has already read Locked this frame.
        //
        // THE FIX (source-independent — does not depend on ANY input source or churn): a dedicated
        // MonoBehaviour that asserts lockState=None + visible=true in BOTH Update() AND LateUpdate()
        // every frame AnyOpen is true. The Update() assert runs early enough that ProcessPointer reads
        // None (no snap); the LateUpdate() assert re-affirms after vanilla's camera pass. This is the
        // belt the §2L.12 patch's "belt-and-suspenders pump" was supposed to be, moved to the phase
        // that actually beats the snap, and decoupled from the gamepad-churn assumption entirely.
        // Kept ALONGSIDE MouseActiveForcePatch (which still helps the virtual-pad case) — together they
        // cover both rigs. Server-safe: Hud.Awake only runs on a client (no Hud on the dedicated
        // server), and AnyOpen is always false there regardless.
        //
        // §2L.15 UPDATE: the §2L.14 diag build's first client test (RequiemPrime 2026-06-22) showed
        // this per-frame MonoBehaviour assert wins ~2/3 of frames but a re-locker still beats it ~1/3.
        // The decisive fix is MenuUpdateCursorForcePatch below (a direct postfix on the method that
        // writes lockState). This driver is KEPT as the belt-and-suspenders layer + the diag host.
        //
        // THE DIAGNOSTIC (gated on the SBPR_CursorDiag config flag, default ON in this build): every
        // ~30 frames while AnyOpen, log the INCOMING lockState, the raw IsMouseActive (NOTE: contaminated
        // by MouseActiveForcePatch — reads true while open), the UNCONTAMINATED gamepadActive source
        // signal, the count of Menu.UpdateCursor fires since the last line (high ⇒ event-driven re-lock
        // churn; zero ⇒ the Locked frames come from a native/non-managed seam), and each contributor's
        // open flag. This turns the next client repro into ground-truth instead of a guess.
        // ════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Bootstraps <see cref="ModalCursorDriver"/> onto the Hud the moment it awakes on a client.
        /// Hud.Awake is a clean every-client-start seam (never runs on the dedicated server → the
        /// whole driver is client-only, no server guard needed). Registered in Plugin.Awake; if you
        /// forget, PatchCheck ERRORs at boot (the t_564f695a lesson).
        /// </summary>
        [HarmonyPatch(typeof(Hud), "Awake")]
        public static class ModalCursorDriverBootstrapPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Hud __instance)
            {
                if (__instance == null) return;
                // One driver for the whole session. Attached to the Hud GameObject so it lives and
                // dies with the in-world HUD (gone on the main menu, back on world load).
                if (__instance.gameObject.GetComponent<ModalCursorDriver>() == null)
                    __instance.gameObject.AddComponent<ModalCursorDriver>();
            }
        }

        /// <summary>
        /// The source-independent per-frame cursor assert + the §2L.14 diagnostic probe. Asserts a
        /// free, visible cursor in BOTH Update (early enough to beat ProcessPointer's center-snap)
        /// and LateUpdate (re-affirm after the camera pass) every frame any SBPR modal is open, and
        /// restores the gameplay lock exactly once on the close edge. Pure client component.
        /// </summary>
        public sealed class ModalCursorDriver : MonoBehaviour
        {
            // ── §2L.17 SINGLE DEBOUNCED CURSOR AUTHORITY (card t_94cc9713) ──────────────────────
            // THE ROOT CAUSE the prior six builds missed (proven by Daniel's v0.2.35 live logs +
            // codebase grep): the cursor read lockState=None on ~96% of frames yet stayed captive,
            // ONLY on our custom UI, with gamepadActive=False and updateCursorFires=0. The one
            // anomalous log frame was `Locked visible=False` — a signature that NO vanilla writer
            // produces (Menu.UpdateCursor writes visible=true) and that ONLY our own close-edge wrote.
            // AnyOpen → CartographyViewer.IsViewerOpen → MapViewer.IsOpen → MapSurface.IsActive
            // (=> _root.activeSelf, MapSurface.cs:237) is a live hierarchy read that a uGUI layout
            // rebuild blips FALSE for a single frame. On that blip our close-edge slammed
            // lockState=Locked → Unity's Linux player re-armed SDL relative-mode → XWarpPointer warped
            // the cursor to screen-centre → next frame AnyOpen=true and we set None back. lockState=None
            // 96% of frames (the log), a centre-warp 2-3x/sec (Daniel's eyes). And there were THREE
            // asserters (this driver's Update + LateUpdate + EOF, plus CursorPumpPatch, plus
            // MenuUpdateCursorForcePatch) each with its OWN edge-detector — on Linux EVERY Locked↔None
            // toggle is an XWarpPointer, so "assert None harder" six times ADDED warpers. That is why
            // it got worse, not better, and why it's our-UI-only and needs no gamepad.
            //
            // THE FIX: ONE authority, DEBOUNCED. Free the cursor while open. On close, require AnyOpen
            // to stay false for CloseDebounceSeconds before re-locking ONCE — so a 1-frame activeSelf
            // blip can no longer re-lock/warp. CursorPumpPatch + MenuUpdateCursorForcePatch now DEFER to
            // FreeThisFrame instead of writing their own edges (no competing re-lockers). The diagnostic
            // now counts SUPPRESSED blips: a non-zero SuppressedBlips with a free, usable cursor in-game
            // is positive proof this was the mechanism.

            /// <summary>True while the debounced authority says the cursor should be free this frame.
            /// CursorPumpPatch / MenuUpdateCursorForcePatch read this instead of asserting their own
            /// edges, so there is exactly ONE re-lock decision in the system.</summary>
            internal static bool FreeThisFrame { get; private set; }

            /// <summary>How long AnyOpen must remain continuously false before we re-lock. One frame at
            /// 60fps ≈ 16ms; 120ms swallows multi-frame rebuild blips with margin while staying
            /// imperceptible on a real close.</summary>
            private const float CloseDebounceSeconds = 0.12f;

            /// <summary>Diagnostic: how many 1-frame "close" blips the debounce SWALLOWED this open
            /// session. Non-zero in-game with a working cursor = the §2L.17 diagnosis confirmed.</summary>
            internal static int SuppressedBlips;

            private bool _cursorFreed;
            private float _closingSince = -1f;
            private int _frame;
            private int _diagLinesEmitted;

            private void OnEnable()
            {
                FreeThisFrame = false;
                _cursorFreed = false;
                _closingSince = -1f;
            }

            private void Update()
            {
                bool open = AnyOpen;

                if (open)
                {
                    // Cancel any pending close — we're (still) open.
                    if (_closingSince >= 0f)
                    {
                        // We were within the debounce window and AnyOpen came back true → that was a
                        // transient blip, NOT a real close. Count it; this is the bug's fingerprint.
                        SuppressedBlips++;
                        _closingSince = -1f;
                    }

                    FreeThisFrame = true;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    _cursorFreed = true;

                    if ((Plugin.CursorDiag?.Value ?? false) && (_frame++ % 30 == 0) && _diagLinesEmitted < 60)
                    {
                        _diagLinesEmitted++;
                        Plugin.Log.LogInfo(
                            "[Trailborne/CursorDiag] §2L.17 AnyOpen=true incomingLockState=" + Cursor.lockState
                            + " incomingVisible=" + Cursor.visible
                            + " rawIsMouseActive=" + ZInput.IsMouseActive()
                            + " gamepadActive=" + ZInput.GamepadActive
                            + " updateCursorFires=" + MenuUpdateCursorForcePatch.FireCount
                            + " suppressedBlips=" + SuppressedBlips
                            + " | paint=" + SignPaintPanel.IsOpen
                            + " marker=" + MarkerSignPanel.IsOpen
                            + " viewer=" + SBPR.Trailborne.Features.Cartography.CartographyViewer.IsViewerOpen);
                        MenuUpdateCursorForcePatch.FireCount = 0;
                    }
                }
                else if (_cursorFreed)
                {
                    // AnyOpen is false — but DON'T re-lock yet. Start (or continue) the debounce timer.
                    // Only after AnyOpen has stayed false for CloseDebounceSeconds do we treat it as a
                    // real close and hand the cursor back exactly once. This is what makes a 1-frame
                    // activeSelf blip a no-op instead of a centre-warp.
                    if (_closingSince < 0f) _closingSince = Time.unscaledTime;

                    if (Time.unscaledTime - _closingSince >= CloseDebounceSeconds)
                    {
                        FreeThisFrame = false;
                        Cursor.lockState = CursorLockMode.Locked;
                        Cursor.visible = false;
                        _cursorFreed = false;
                        _closingSince = -1f;
                        _frame = 0;
                        _diagLinesEmitted = 0;

                        if (Plugin.CursorDiag?.Value ?? false)
                        {
                            Plugin.Log.LogInfo(
                                "[Trailborne/CursorDiag] §2L.17 real close — cursor relocked after debounce. "
                                + "suppressedBlips this session=" + SuppressedBlips);
                            SuppressedBlips = 0;
                        }
                    }
                    else
                    {
                        // Inside the debounce window: keep the cursor FREE (don't flicker it).
                        FreeThisFrame = true;
                        Cursor.lockState = CursorLockMode.None;
                        Cursor.visible = true;
                    }
                }
            }
        }

        // §2L.15 (ticket-cursor-lock-map-sign, card t_cad2c6f3 — FOLLOW-UP after the §2L.14 diag
        // build's first client test): the DIRECT, inlining-immune fix for the re-lock that §2L.12's
        // indirect IsMouseActive force could not stop.
        //
        // WHAT THE §2L.14 DIAG PROVED (RequiemPrime, 2026-06-22): with the per-frame ModalCursorDriver
        // running, the cursor reads None on ~2/3 of frames but STILL flips to Locked on ~1/3 — every
        // such Locked frame snaps the cursor to centre, so it still reads as "locked." A full scan of
        // BOTH game assemblies (assembly_valheim + assembly_utils/ZInput) confirms the ONLY managed
        // writers of lockState=Locked during play are Menu.UpdateCursor (:45815) and
        // FejdStartup.UpdateCursor (:83089, start-screen only). §2L.12 forces ZInput.IsMouseActive()
        // → true so UpdateCursor *should* compute None — yet Locked still gets through. The signature
        // of that is INLINING: IsMouseActive (a tiny assembly_utils method) is inlined into
        // UpdateCursor, so the Harmony detour on the standalone IsMouseActive never executes at that
        // call site (the exact class of failure that killed the old UpdateMouseCapture seam, §2L).
        //
        // THE FIX: postfix Menu.UpdateCursor itself and force lockState=None + visible=true when a
        // modal is open. This patches the METHOD THAT WRITES lockState, so it's immune to whether
        // IsMouseActive was inlined — whatever UpdateCursor computed, we overwrite it. UpdateCursor is
        // GUARANTEED non-inlined and patchable because vanilla references it as a delegate
        // (`ZInput.OnInputLayoutChanged += UpdateCursor`, :81810) — you cannot take a delegate to an
        // inlined body, so a real method body must exist. Server-safe: Menu only exists on a client,
        // and AnyOpen is always false on the dedicated server regardless.
        [HarmonyPatch(typeof(Menu), "UpdateCursor")]
        public static class MenuUpdateCursorForcePatch
        {
            // Diagnostic counter: how many times UpdateCursor fired since the last CursorDiag line.
            // A high count while a modal is open = vanilla's event-driven re-lock is the culprit
            // (something is flickering OnInputLayoutChanged). Zero = the Locked frames come from
            // elsewhere (native Input System) and need a different seam.
            internal static int FireCount;

            [HarmonyPostfix]
            private static void Postfix()
            {
                if (!AnyOpen) return;
                FireCount++;
                // §2L.17: defer to the single debounced authority. While the driver says free (which it
                // does for the entire open + the close-debounce window), overwrite whatever UpdateCursor
                // computed. We no longer assert our OWN edge — the driver owns the one re-lock decision,
                // so a 1-frame AnyOpen blip can't make THIS patch warp the cursor either.
                if (ModalCursorDriver.FreeThisFrame)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
        }

        // §2L.13 (card t_a1cf35b0, sibling of the cursor fix): the Inventory hotkey must NOT open the
        // inventory while an SBPR modal owns the screen. The toggle is read inside InventoryGui.Update
        // (assembly_valheim :41458 → Show(null)), NOT through Player.TakeInput, so the TakeInput block
        // above never gated it — Daniel could pop the inventory over the sign panel and the Local Map.
        // Mirror MenuOpenSuppressPatch: a skip-original prefix on InventoryGui.Show(Container, int)
        // gated on AnyOpen. While a modal is up, no legitimate container/station open can be triggered
        // (Player.TakeInput is forced false), so blocking Show here only suppresses the stray toggle.
        // Self-clearing (AnyOpen false the moment the modal closes → inventory opens normally again)
        // and server-safe (AnyOpen always false on a dedicated server).
        [HarmonyPatch(typeof(InventoryGui), "Show", new Type[] { typeof(Container), typeof(int) })]
        public static class InventoryOpenSuppressPatch
        {
            [HarmonyPrefix]
            private static bool Prefix()
            {
                // Return false → skip InventoryGui.Show (inventory does not open) while any SBPR
                // modal UI is up. Return true → normal vanilla behaviour.
                return !AnyOpen;
            }
        }
    }
}
