// ============================================================================
//  Trailborne v3 (Swamp) — Sunstone Lens HUD overlay (the detection pump + empty-state surface)
// ----------------------------------------------------------------------------
//  Design   : docs/design/sunstone-lens-trophy-ring.md (card t_68672b6b → t_d17d9b58, world-space)
//  Impl spec: docs/v3/planning/sunstone-lens-impl-spec.md §4-§5 (render → the world halo)
//  Mechanic : SunstoneLens.GatherHostiles (unchanged — this file PUMPS the sweep + drives surfaces)
//
//  This MonoBehaviour is the client-only detection PUMP + lifecycle owner. Mounted once under
//  Hud.m_rootObject by HudBootstrap; its Update() runs the throttled hostile sweep, derives the
//  shared ThreatBlips (SunstoneProjection), resolves the minimap-handoff plan, and drives whichever
//  surface owns detection this tick. The standalone (no-minimap) render surface is the WORLD-SPACE
//  eidetic trophy halo (SunstoneWorldRing) — a head-centric halo of billboarded creature trophies
//  floating in the 3D world at their real bearings (variable radius+scale ∝ distance, vanilla star
//  pips, aggro tint). The earlier screen-space camera-relative radar is SUPERSEDED (card t_68672b6b).
//
//  EMPTY-STATE AFFORDANCE → WORLD-SPACE PULSING SUN-CORONA DISC (card t_9d7c3dfe, Daniel /bug
//  t_2d500d45: "the ring itself is just a screen space circle, not a 3d slowly pulsing 'sun corona'
//  disc like we discussed"). The old flat SCREEN-space solar ring (the `_emptyRing` Image + its
//  `RingSprite()` annulus + `SolarRingRadiusPx`) is REMOVED; the empty-state cue is now a glowing
//  sun-corona disc drawn in WORLD space (SunstoneCoronaDisc), co-located with the trophy halo in the
//  SAME scene root (SBPR_SunstoneWorldHalo) and breathing on a slow alpha pulse (engine-free,
//  CI-gated SunstoneCoronaPulse). The corona is the world-space SUBSTRATE the fixed-distance trophy
//  halo orbits — a sun on the floor with creature trophies floating around it. One Hide()/Dispose()
//  lifecycle for both (the corona shares the halo's root). `CSolarRing` (the gold) is KEPT.
//
//  What this file still draws directly (screen-space, under Hud.m_rootObject):
//    • the optional legacy debug text readout (Sunstone.DebugTextReadout, default off).
//  The threat TROPHIES and the empty-state sun-corona are BOTH delegated to world space
//  (SunstoneWorldRing + SunstoneCoronaDisc, their own shared scene root).
//
//  🔴 #209 invariant (t_d5949685 / PR #208): SetVisible toggles a _content CHILD, NEVER the host
//  GameObject — the host carries THIS Update pump, and the minimap surfaces' detection feed depends
//  on it staying alive. The world halo + corona's objects live in WORLD space (NOT under
//  Hud.m_rootObject); only the visuals move, the pump stays.
//
//  Client-only by construction: Hud.Awake never fires on the dedicated server (no Hud), and
//  Character.GetAllCharacters / Player.m_localPlayer / EnemyHud.instance are client concerns.
//  Everything here is cosmetic — it reads game state, never writes it.
//
//  Clean-side (ADR-0001): reads base-game Hud/Player/Character/CharacterDrop/BaseAI/EnemyHud/
//  GameCamera/Billboard only; the uGUI surface is our own. No vanilla UI cloned, no third-party mod
//  code read — the Rune-of-Awareness behaviour is reproduced from vanilla primitives only. ADR-0006:
//  every world slot + the sun-corona disc are built additively (new GameObject + AddComponent);
//  reusing the trophy/star SPRITES is reading an asset, not cloning; the corona sprite is procedural.
//
//  logs-green ≠ playable — Daniel verifies AT-EIDETIC-* in-game on a GPU client.
// ============================================================================

using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using SBPR.Trailborne.Runtime;
using SBPR.Trailborne.Features.Cartography;

namespace SBPR.Trailborne.Features.Sunstone
{
    /// <summary>
    /// The client-only HUD MonoBehaviour. Built once and attached under Hud.m_rootObject by
    /// <see cref="HudBootstrap"/>. Drives visibility + the trophy ring from the equipped-lens state.
    /// </summary>
    public class SunstoneLensHudOverlay : MonoBehaviour
    {
        // ── Empty-state + readout tuning (single source of truth; Plugin binds ConfigEntry mirrors so
        //    Daniel converges feel on a joined client without a rebuild — the banner-windsock pattern).
        //    The screen-space ring-geometry knobs (RingRadiusPx/CenterOffsetY/IconMin/Max) were REMOVED
        //    with the screen radar; the faint flat solar RING is now REMOVED too (card t_9d7c3dfe) — the
        //    empty-state affordance graduated to the WORLD-SPACE pulsing sun-corona disc (the corona's
        //    own Corona* knobs live on SunstoneCoronaDisc). What remains here: the trophy-cap + the
        //    empty/depleted/debug toggles. ──
        public const int   DefaultRingMaxIcons     = 12;    // cap so a horde doesn't spawn 80 slots (carried)
        public const bool  DefaultShowEmptyRing    = true;  // repurposed (card t_9d7c3dfe): master on/off for the sun-corona when worn+charged-but-clear (Daniel)
        public const bool  DefaultShowDepletedHint = false; // halo + corona fully off when depleted (Daniel)
        public const bool  DefaultDebugTextReadout = false; // legacy text line, debug aid only

        public const bool  DefaultDebugMount = true;  // diagnostic cut (t_d5949685): emit mount/wear LogInfo. Bake to false once the halo is confirmed rendering in-game.

        private static SunstoneLensHudOverlay? _instance;

        private RectTransform? _root;       // the host — ALWAYS ACTIVE (carries this MonoBehaviour's Update pump)
        private RectTransform? _content;    // the visibility child — toggled by SetVisible (NEVER the host; see SetVisible)
        private Text?  _debugText;          // optional legacy "⚠ N · nearest Xm · charge Y%" readout
        private bool   _loggedFirstShow;    // diagnostic: log once on the first visible frame

        // ── The WORLD-SPACE eidetic trophy halo (card t_d17d9b58). The standalone (no-minimap) render
        //    surface: a head-centric halo of billboarded creature trophies floating in the 3D world at
        //    their real bearings. Owned here (this MonoBehaviour is the single Update pump, #209), but
        //    its slot objects live in WORLD space (its own scene root), NOT under Hud.m_rootObject. ──
        private readonly SunstoneWorldRing _worldRing = new SunstoneWorldRing();

        // ── The WORLD-SPACE pulsing sun-corona disc (card t_9d7c3dfe — the empty-state affordance,
        //    graduated from the removed flat screen-space ring). Shares the trophy halo's scene root
        //    (one Hide()/Dispose() lifecycle); breathes on the engine-free CI-gated SunstoneCoronaPulse.
        //    The substrate the fixed-distance trophy halo orbits — "a sun on the floor." ──
        private readonly SunstoneCoronaDisc _corona;

        // Diagnostic-logging gate (t_d5949685). Reads the live Plugin config when present (so Daniel can
        // flip it in a joined session), else the Default* const — the no-Plugin-context fallback idiom.
        // internal so the sibling HudBootstrap (mount log) can share the one gate.
        internal static bool DebugMount => Plugin.LensRingDebugMount?.Value ?? DefaultDebugMount;

        // Reused across sweeps to avoid per-frame allocations.
        private readonly List<Character> _hostiles = new List<Character>();
        private float _nextSweep;

        // ── Sunstone Lens → minimap handoff (card t_91e86951). ──
        // The per-sweep render-ready blips, derived ONCE via SunstoneProjection and consumed by all
        // three surfaces (ring / SBPR disc / vanilla minimap) — zero-drift (AT-LENS-DISC-NODRIFT).
        private readonly List<ThreatBlip> _blips = new List<ThreatBlip>();
        // The Cartography seam adapter: hands the SBPR carry-disc the latest blips on its rebuild pull.
        // Registered once (first Update) into Cartography.ThreatMarkers; holds _blips by reference.
        private SunstoneThreatProvider? _discProvider;
        // The nomap-OFF custom overlay on the vanilla corner minimap (own Image.color → tint survives).
        private readonly SunstoneMinimapThreatLayer _vanillaLayer = new SunstoneMinimapThreatLayer();
        // Per-tick handoff state (set in Update, read by the disc provider's pull + the vanilla render).
        private bool _feedDiscNow;             // true ⇔ the SBPR disc should receive _blips this rebuild
        private BlipStyle _blipStyleNow = BlipStyle.Dots;  // dots vs trophy on the minimap surfaces

        // ── Aggro-state tint (the Rune-of-Awareness colour code, §1.8). Single source of truth now
        //    lives in SunstoneProjection; the sun-corona uses its own faint solar colour below. ──
        // Faint solar gold — the sunstone's stored daylight glowing faintly. KEPT (card t_9d7c3dfe):
        // it's the corona's gold tint now (the per-frame breathing alpha comes from SunstoneCoronaPulse,
        // so only the RGB here is load-bearing; the literal 0.18 alpha is the static baseline the pulse
        // envelope breathes around, 0.10↔0.28).
        private static readonly Color CSolarRing = new Color(0.98f, 0.78f, 0.36f, 0.18f);

        /// <summary>Wire the sun-corona to the trophy halo so it shares the halo's scene root + lifecycle.</summary>
        public SunstoneLensHudOverlay()
        {
            _corona = new SunstoneCoronaDisc(_worldRing);
        }

        /// <summary>Idempotently build the overlay under the given Hud root.</summary>
        public static void EnsureBuilt(GameObject hudRoot)
        {
            if (_instance != null && _instance._root != null) return;
            if (hudRoot == null) return;

            var host = new GameObject("SBPR_SunstoneLensHud");
            host.transform.SetParent(hudRoot.transform, worldPositionStays: false);
            _instance = host.AddComponent<SunstoneLensHudOverlay>();
            _instance.Build();
        }

        private void Build()
        {
            _root = gameObject.AddComponent<RectTransform>();
            // Centre on screen — the host carries only the optional debug text now. (The threat trophies
            // AND the empty-state sun-corona both live in WORLD space — SunstoneWorldRing + the corona's
            // shared scene root — NOT on this canvas, card t_9d7c3dfe.)
            _root.anchorMin = new Vector2(0.5f, 0.5f);
            _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot     = new Vector2(0.5f, 0.5f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(2f, 2f); // children are absolutely placed; root is a pivot

            // ── Visibility container (the t_d5949685 fix): the host GameObject (_root) carries THIS
            //    MonoBehaviour, so it MUST stay active or Unity stops calling Update() — and Update()
            //    is the only thing that ever un-hides the overlay. So visibility toggles a CHILD
            //    (_content), never the host. _content is a centred pivot coincident with _root, so the
            //    absolute anchoredPositions of every child (debug text) are unchanged. (Before this
            //    fix, SetVisible(false) at the end of Build() deactivated the host, freezing the Update
            //    pump dead → the overlay rendered NOTHING forever, worn/charged or not — the exact
            //    self-deactivating-host bug the Iron Compass had, PR #208.)
            var contentGo = new GameObject("content", typeof(RectTransform));
            contentGo.transform.SetParent(_root, worldPositionStays: false);
            _content = contentGo.GetComponent<RectTransform>();
            _content.anchorMin = _content.anchorMax = _content.pivot = new Vector2(0.5f, 0.5f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = new Vector2(2f, 2f);

            // The empty-state affordance (the faint "lens is live" glow) is NO LONGER a screen-space ring
            // built here (card t_9d7c3dfe) — it's the world-space pulsing sun-corona disc, built lazily by
            // SunstoneCoronaDisc into the trophy halo's shared scene root on its first Render(). Nothing
            // to construct on this canvas for it.

            // Optional legacy debug text, centred below the screen pivot.
            var font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                       ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _debugText = MakeText("debug", font, 14, FontStyle.Normal, new Vector2(0f, -162f));
            _debugText.gameObject.SetActive(false);

            SetVisible(false);
        }

        private Text MakeText(string name, Font font, int size, FontStyle style, Vector2 anchored)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_content, worldPositionStays: false);  // under _content (visibility child), not the host

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchored;
            rt.sizeDelta = new Vector2(360f, 24f);

            var txt = go.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = size;
            txt.fontStyle = style;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.color = Color.white;
            txt.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            return txt;
        }

        private void SetVisible(bool on)
        {
            // The world halo + sun-corona must never linger when the overlay is being hidden (unequip /
            // depleted / no-player / minimap-owns-detection): hide them here so every inert path drops
            // them immediately (AT-EIDETIC-5 / AT-CORONA-GATED). When turning visible the caller decides
            // whether to actually draw the halo + corona (RenderWorldHalo) vs only the empty/depleted
            // affordance — we don't show them here, we only guarantee the OFF case. Safe before either is
            // built (no-op). Both share one scene root, so this is one coherent hide.
            if (!on) { _worldRing.Hide(); _corona.Hide(); }

            // Toggle the CONTENT child, NEVER the host (_root): the host carries this MonoBehaviour's
            // Update pump, so deactivating it would freeze the overlay un-recoverably (t_d5949685 — the
            // same self-deactivating-host bug the Iron Compass had, PR #208). The host stays active for
            // the lifetime of the HUD; visibility lives entirely on _content.
            if (_content == null || _content.gameObject.activeSelf == on) return;
            _content.gameObject.SetActive(on);
            if (!on) _loggedFirstShow = false;   // re-arm the first-show diagnostic for the next show
            // Diagnostic (t_d5949685): a transition LINE here PROVES the Update pump is alive (the bug
            // was that it was frozen dead). PRESENT in a client LogOutput.log = pump pumping; ABSENT
            // across a wear/unwear = pump frozen (the old self-deactivating-host failure).
            if (DebugMount)
                Plugin.Log.LogInfo($"[Trailborne/Sunstone] LensHud: content → {(on ? "VISIBLE" : "hidden")} "
                    + $"(host activeInHierarchy={_root?.gameObject.activeInHierarchy}).");
        }

        private void Update()
        {
            // Guard everything: the HUD can outlive a world/player.
            var player = Player.m_localPlayer;
            if (player == null || _root == null)
            {
                SetVisible(false);
                StandDownMinimaps();
                return;
            }

            var lens = SunstoneLens.GetEquippedLens(player);
            if (lens == null)
            {
                SetVisible(false);   // not worn → overlay hidden entirely
                StandDownMinimaps();  // and clear any threats left on a minimap surface
                return;
            }

            // Depleted (inert) state — worn but not enough charge to detect (AC#5). Corona OFF
            // (Daniel: depleted hint default off). The durability bar already signals "dim".
            if (lens.m_durability < SunstoneLens.MinChargeToDetect)
            {
                StandDownMinimaps();  // depleted = inert detection → no threats on any minimap surface
                bool showDepleted = Plugin.LensRingShowDepletedHint?.Value ?? DefaultShowDepletedHint;
                if (!showDepleted)
                {
                    SetVisible(false);
                    return;
                }
                // Optional faint hint: show ONLY the sun-corona, DIMMER + STEADY (no pulse), no trophies.
                // The world halo's slot trophies are parked but its shared scene root stays active so the
                // corona (which lives under it) can render (spec §2.6 — the depleted parity for the old
                // half-alpha flat ring). #209: this toggles world-content children, never the host pump.
                SetVisible(true);
                _worldRing.ShowRootWithoutTrophies();
                RenderCorona(player, depletedDim: true);
                if (_debugText != null) _debugText.gameObject.SetActive(false);
                return;
            }

            // ── Sunstone Lens → minimap handoff (card t_91e86951). The lens is ACTIVE here (worn +
            //    charged). Resolve which surface owns detection this tick and how the gated
            //    MinimapHandoffMode splits the ring vs the minimap, then render accordingly. ──
            EnsureDiscProviderRegistered();

            var mode = Plugin.LensMinimapHandoffMode?.Value ?? MinimapHandoffMode.DiscWhenBound;
            _blipStyleNow = Plugin.LensMinimapBlipStyle?.Value ?? BlipStyle.Dots;

            // The two live world facts the pure decision consumes. SBPR disc bound = nomap-ON + a local
            // map bound + imprinted (CartographyViewer.IsMinimapBound). Vanilla corner map showing =
            // nomap-OFF (Minimap forces MapMode.None under Game.m_noMap, so Small ⇔ nomap-OFF).
            bool sbprDiscBound = CartographyViewer.IsMinimapBound;
            var mm = Minimap.instance;
            bool vanillaMinimapShowing = mm != null && mm.m_mode == Minimap.MapMode.Small;

            LensRenderPlan plan = LensHandoffDecision.Resolve(sbprDiscBound, vanillaMinimapShowing, mode);

            // ── Throttled detection sweep (the expensive Character scan stays at the configured cadence). ──
            float interval = Plugin.LensDetectInterval?.Value ?? SunstoneLens.DefaultDetectInterval;
            if (Time.time >= _nextSweep)
            {
                _nextSweep = Time.time + Mathf.Max(0.1f, interval);
                float radius = Plugin.LensDetectRadius?.Value ?? SunstoneLens.DefaultDetectRadius;
                SunstoneLens.GatherHostiles(player, radius, _hostiles);
            }

            // Derive render-ready blips EVERY frame from the (throttled) hostile set — the cheap part
            // (tint/trophy-cache/level), so the ring keeps its per-frame aggro-tint freshness AND the
            // minimap surfaces read the SAME single derivation (AT-LENS-DISC-NODRIFT). One source.
            SunstoneProjection.Project(_hostiles, player, _blips);

            // ── Ring content (#209 invariant — AT-LENS-DISC-PUMP). "Ring hides" = SetVisible(false) on
            //    the _content CHILD only; the host (_root) carrying THIS Update pump stays active, so the
            //    sweep + projection above keep running and the minimap surfaces keep getting fed even
            //    while the ring is hidden. NEVER deactivate the host here. ──
            SetVisible(true);            // host stays active; _content toggled to plan below
            LogFirstShowDiagnostic();

            float max = Mathf.Max(1f, lens.GetMaxDurability());
            float chargePct = Mathf.Clamp01(lens.m_durability / max) * 100f;

            if (plan.RingContentVisible)
            {
                _content?.gameObject.SetActive(true);
                RenderWorldHalo(player, chargePct);
            }
            else
            {
                // Halo suppressed (a minimap owns detection). Park the world halo + the screen content;
                // the host + pump stay alive (the load-bearing #209 line). _content hidden = no solar
                // ring/text; the world halo's own root is hidden too = no trophies in the world.
                _worldRing.Hide();
                _content?.gameObject.SetActive(false);
            }

            // ── Feed the active minimap surface (if the mode hands off). The SBPR disc pulls _blips on
            //    its own rebuild via the registered provider (gated by _feedDiscNow); the vanilla minimap
            //    overlay is driven directly here. Exactly one is ever live (mutually exclusive surfaces). ──
            bool feedDisc = plan.FeedMinimap && plan.MinimapTarget == LensSurface.SbprDisc;
            bool feedVanilla = plan.FeedMinimap && plan.MinimapTarget == LensSurface.VanillaMinimap;
            _feedDiscNow = feedDisc;
            if (feedVanilla) _vanillaLayer.Render(_blips, _blipStyleNow);
            else _vanillaLayer.Clear();
        }

        /// <summary>
        /// Stand every minimap surface down (called from the inert early-returns — not worn / depleted).
        /// Clears the disc feed flag + the swept blips (so the disc provider hands out nothing on its next
        /// rebuild) and clears the vanilla overlay. The ring's own visibility is handled by SetVisible at
        /// each call site; this only governs the two MINIMAP surfaces so an unequipped/depleted lens can't
        /// leave stale threats on a minimap.
        /// </summary>
        private void StandDownMinimaps()
        {
            _feedDiscNow = false;
            _blips.Clear();
            _vanillaLayer.Clear();
        }

        /// <summary>Register the SBPR-disc threat provider exactly once (idempotent via the registry).</summary>
        private void EnsureDiscProviderRegistered()
        {
            if (_discProvider != null) return;
            _discProvider = new SunstoneThreatProvider(this);
            ThreatMarkers.Register(_discProvider);
        }

        // Diagnostic (t_d5949685): on the FIRST visible frame, log the resolved placement so a fresh
        // client LogOutput.log can split a mount/pump failure (this line never appears) from an
        // on-screen-but-empty ring (line appears with a sane anchoredPosition/size). Re-armed on hide.
        private void LogFirstShowDiagnostic()
        {
            if (DebugMount && !_loggedFirstShow && _root != null && _content != null)
            {
                _loggedFirstShow = true;
                Plugin.Log.LogInfo($"[Trailborne/Sunstone] LensHud first show: root.anchoredPosition={_root.anchoredPosition} "
                    + $"worldHaloBuilt={_worldRing.Built} "
                    + $"(host activeInHierarchy={_root.gameObject.activeInHierarchy}, content active={_content.gameObject.activeSelf}).");
            }
        }

        /// <summary>
        /// Render the standalone (no-minimap) detection surface: the WORLD-SPACE eidetic trophy halo
        /// (delegated to <see cref="SunstoneWorldRing"/>) plus the world-space pulsing sun-corona disc
        /// (<see cref="SunstoneCoronaDisc"/>, the empty-state substrate) + the optional debug readout.
        /// Consumes the shared <see cref="_blips"/> derivation (same tint/trophy/stars the disc + vanilla
        /// minimap read — AT-EIDETIC-MINIMAP-UNAFFECTED holds: one source).
        /// </summary>
        private void RenderWorldHalo(Player player, float chargePct)
        {
            float detectR   = Plugin.LensDetectRadius?.Value     ?? SunstoneLens.DefaultDetectRadius;
            int   maxIcons  = Plugin.LensRingMaxIcons?.Value      ?? DefaultRingMaxIcons;
            float radius    = Plugin.LensHaloRadius?.Value        ?? SunstoneWorldRing.DefaultHaloRadius;
            float scaleMax  = Plugin.LensHaloScaleMax?.Value      ?? SunstoneWorldRing.DefaultHaloScaleMax;
            float eyeOffset = Plugin.LensHaloEyeOffsetY?.Value    ?? SunstoneWorldRing.DefaultHaloEyeOffsetY;

            // The halo anchor is the player's EYE-POINT (Character.GetEyePoint(), public :8655) — the
            // head-centric halo (Knob #1/#2). Recompute every frame; the per-slot Billboard handles facing.
            Vector3 eye = player.GetEyePoint();

            // Draw the world halo from the shared projection. Camera-relative by construction (each
            // trophy sits on the REAL blip.WorldPos - eye bearing — the thesis guard, no SignedAngle,
            // no north frame). FIXED-distance ring (radius) + scale-only range cue (10m knee off
            // scaleMax) per bug-fix t_10bacccf. The world ring pools + caps + sorts nearest-N internally.
            _worldRing.Render(eye, _blips, detectR, radius, scaleMax, eyeOffset, maxIcons);

            int shown = Mathf.Min(maxIcons, CountBlips());

            // The world-space pulsing sun-corona disc (the empty-state affordance, card t_9d7c3dfe):
            // shown whenever worn+charged (gated by the repurposed ShowEmptyRing), breathing on the slow
            // pulse. It's the substrate the trophy halo orbits — drawn into the SAME shared scene root.
            RenderCorona(player, depletedDim: false);

            // Optional legacy debug readout (default off).
            bool debug = Plugin.LensRingDebugText?.Value ?? DefaultDebugTextReadout;
            if (_debugText != null)
            {
                _debugText.gameObject.SetActive(debug);
                if (debug)
                {
                    if (shown == 0)
                        _debugText.text = $"Sunstone Lens — clear · charge {chargePct:0}%";
                    else
                    {
                        float nearest = float.MaxValue;
                        foreach (var blip in _blips)
                        {
                            if (blip.Character == null) continue;
                            float d = (blip.WorldPos - eye).sqrMagnitude;
                            if (d < nearest) nearest = d;
                        }
                        _debugText.text = $"⚠ {shown} hostile{(shown == 1 ? "" : "s")} · nearest {Mathf.Sqrt(nearest):0}m · charge {chargePct:0}%";
                    }
                }
            }
        }

        /// <summary>
        /// Drive the world-space sun-corona disc (card t_9d7c3dfe) from the live Corona* config (the
        /// banner-windsock pattern — every knob live-tunable on a joined client, no rebuild). Gated by
        /// the repurposed <c>ShowEmptyRing</c> master toggle: OFF → the corona hides. The orientation
        /// picks the anchor — GroundPlane on the player's feet (character-root), CameraFacing on the
        /// eye-point. <paramref name="depletedDim"/> renders the corona dimmer + STEADY (no pulse) for
        /// the optional depleted hint (parity with the old half-alpha flat ring), else it breathes on
        /// the engine-free <see cref="SunstoneCoronaPulse"/>.
        /// </summary>
        private void RenderCorona(Player player, bool depletedDim)
        {
            bool showCorona = Plugin.LensRingShowEmpty?.Value ?? DefaultShowEmptyRing;
            if (!showCorona) { _corona.Hide(); return; }

            var orientation = Plugin.LensCoronaOrientation?.Value ?? SunstoneCoronaDisc.DefaultOrientation;
            float radius    = Plugin.LensCoronaRadius?.Value      ?? SunstoneCoronaDisc.DefaultRadius;
            float offsetY   = Plugin.LensCoronaPlaneOffsetY?.Value ?? SunstoneCoronaDisc.DefaultPlaneOffsetY;
            float innerFill = Plugin.LensCoronaInnerFill?.Value    ?? SunstoneCoronaDisc.DefaultInnerFill;
            float thickness = Plugin.LensCoronaThickness?.Value    ?? SunstoneCoronaDisc.DefaultThickness;
            float hz        = Plugin.LensCoronaPulseHz?.Value       ?? SunstoneCoronaDisc.DefaultPulseHz;
            float trough    = Plugin.LensCoronaAlphaTrough?.Value   ?? SunstoneCoronaDisc.DefaultAlphaTrough;
            float peak      = Plugin.LensCoronaAlphaPeak?.Value     ?? SunstoneCoronaDisc.DefaultAlphaPeak;

            // GroundPlane anchors on the player's feet (the character-root transform position ≈ ground);
            // CameraFacing anchors on the eye-point. Both passed so the disc picks per its orientation.
            Vector3 groundAnchor = player.transform.position;
            Vector3 eyeAnchor    = player.GetEyePoint();

            if (depletedDim)
            {
                // Depleted hint: a dim, STEADY glow (no breath) — pass hz=0 so SunstoneCoronaPulse holds
                // the mid-value, and halve the envelope so it reads fainter than the live corona (parity
                // with the old CSolarRing.a * 0.5f flat-ring hint).
                _corona.Render(groundAnchor, eyeAnchor, orientation, radius, offsetY, innerFill, thickness,
                    CSolarRing, time: 0.0, hz: 0f, trough: trough * 0.5f, peak: peak * 0.5f);
                return;
            }

            // Live corona: breathe on the shared Time.time phase (one phase → no drift across an
            // orientation flip or vs the trophy tint — AT-CORONA-PULSE).
            _corona.Render(groundAnchor, eyeAnchor, orientation, radius, offsetY, innerFill, thickness,
                CSolarRing, Time.time, hz, trough, peak);
        }

        /// <summary>Count of non-null blips this tick (for the debug readout's "N hostiles").</summary>
        private int CountBlips()
        {
            int n = 0;
            for (int i = 0; i < _blips.Count; i++)
                if (_blips[i].Character != null) n++;
            return n;
        }

        /// <summary>Tear the world halo + sun-corona down with the overlay (logout / Hud teardown). #209:
        /// only the visuals are destroyed; the host pump's own lifecycle is Unity's to manage. The corona
        /// shares the halo's scene root, so disposing the halo culls the corona's parent too — but the
        /// corona disposes its own child explicitly so a stale Image can't linger.</summary>
        private void OnDestroy()
        {
            _corona.Dispose();
            _worldRing.Dispose();
        }

        // ───────────────────────────────────────────────
        // SBPR-DISC THREAT PROVIDER (the Cartography seam adapter — card t_91e86951)
        // ───────────────────────────────────────────────

        /// <summary>
        /// Append this overlay's latest swept blips as <see cref="DiscThreatMarker"/>s, the Cartography
        /// seam's pull contract. Called by <see cref="SunstoneThreatProvider"/> from inside the disc's
        /// <c>MapSurface.RebuildOverlay</c>. Returns nothing unless THIS tick the handoff plan feeds the
        /// SBPR disc (<see cref="_feedDiscNow"/>) — so a hidden disc / ring-only mode / depleted lens
        /// shows no threats. Disc-clip by <paramref name="radius"/> is the producer's courtesy; the disc
        /// also clips to its visible circle. Under <see cref="BlipStyle.Dots"/> the marker carries a null
        /// Icon (the disc draws a tinted dot); under Trophy it carries the trophy sprite.
        /// </summary>
        internal void CollectDiscThreats(Vector3 origin, float radius, List<DiscThreatMarker> into)
        {
            if (!_feedDiscNow) return;
            float r2 = radius > 0f ? radius * radius : float.MaxValue;
            bool trophy = _blipStyleNow == BlipStyle.Trophy;
            Sprite? starSprite = SunstoneProjection.StarSprite();   // pushed into the marker so the disc draws pips without an upward dep
            for (int i = 0; i < _blips.Count; i++)
            {
                var b = _blips[i];
                if (b.Character == null) continue;
                if ((b.WorldPos - origin).sqrMagnitude > r2) continue;
                into.Add(new DiscThreatMarker(b.WorldPos, b.Tint, trophy ? b.Trophy : null, b.Stars, starSprite));
            }
        }

        /// <summary>
        /// The <see cref="IThreatMarkerProvider"/> adapter Sunstone registers into the Cartography
        /// <c>ThreatMarkers</c> registry. Holds the owning overlay and forwards the pull — keeping the
        /// dependency arrow Sunstone → Cartography (Cartography never references the Lens, only the seam).
        /// </summary>
        private sealed class SunstoneThreatProvider : IThreatMarkerProvider
        {
            private readonly SunstoneLensHudOverlay _owner;
            public SunstoneThreatProvider(SunstoneLensHudOverlay owner) { _owner = owner; }
            public void CollectThreatBlips(Vector3 origin, float radius, List<DiscThreatMarker> into)
                => _owner.CollectDiscThreats(origin, radius, into);
        }

        /// <summary>
        /// Builds (idempotently) the HUD overlay once the Hud exists. Postfix on Hud.Awake —
        /// the exact pattern the Iron Compass uses (nomap.md §8). Never fires on the dedicated
        /// server (no Hud). Server-safe + fail-quiet.
        /// </summary>
        [HarmonyPatch(typeof(Hud), "Awake")]
        public static class HudBootstrap
        {
            [HarmonyPostfix]
            public static void Postfix(Hud __instance)
            {
                try
                {
                    if (__instance == null || __instance.m_rootObject == null) return;
                    EnsureBuilt(__instance.m_rootObject);
                    // Diagnostic (t_d5949685): the success path used to be SILENT, so a client log could
                    // neither confirm nor deny the overlay mount. Log it (DebugMount-gated) — this line
                    // PRESENT in LogOutput.log proves the postfix ran and the overlay mounted under
                    // m_rootObject; ABSENT means the postfix never fired / m_rootObject was null.
                    if (DebugMount)
                        Plugin.Log.LogInfo("[Trailborne/Sunstone] LensHud mounted under Hud.m_rootObject "
                            + $"(m_rootObject non-null={__instance.m_rootObject != null}).");
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning($"[Trailborne/Sunstone] Hud overlay bootstrap error (non-fatal): {e.Message}");
                }
            }
        }
    }
}
