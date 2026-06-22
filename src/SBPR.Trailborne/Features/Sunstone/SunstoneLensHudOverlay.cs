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
//  What this file still draws directly (screen-space, under Hud.m_rootObject):
//    • the faint SOLAR RING empty-state affordance (design §1.6 — "either surface is acceptable"
//      for the empty cue; kept screen-space as the lower-risk choice),
//    • the optional legacy debug text readout (Sunstone.DebugTextReadout, default off).
//  The threat TROPHIES are delegated to SunstoneWorldRing (world space, its own scene root).
//
//  🔴 #209 invariant (t_d5949685 / PR #208): SetVisible toggles a _content CHILD, NEVER the host
//  GameObject — the host carries THIS Update pump, and the minimap surfaces' detection feed depends
//  on it staying alive. The world halo's slot objects live in WORLD space (NOT under Hud.m_rootObject);
//  only the visuals move, the pump stays.
//
//  Client-only by construction: Hud.Awake never fires on the dedicated server (no Hud), and
//  Character.GetAllCharacters / Player.m_localPlayer / EnemyHud.instance are client concerns.
//  Everything here is cosmetic — it reads game state, never writes it.
//
//  Clean-side (ADR-0001): reads base-game Hud/Player/Character/CharacterDrop/BaseAI/EnemyHud/
//  GameCamera/Billboard only; the uGUI surface is our own. No vanilla UI cloned, no third-party mod
//  code read — the Rune-of-Awareness behaviour is reproduced from vanilla primitives only. ADR-0006:
//  every world slot + the solar ring are built additively (new GameObject + AddComponent); reusing
//  the trophy/star SPRITES is reading an asset, not cloning.
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
        // ── Empty-state solar-ring + readout tuning (single source of truth; Plugin binds ConfigEntry
        //    mirrors so Daniel converges feel on a joined client without a rebuild — the banner-windsock
        //    pattern). The screen-space ring-geometry knobs (RingRadiusPx/CenterOffsetY/IconMin/Max) are
        //    REMOVED with the screen radar — the trophy halo is now world-space (SunstoneWorldRing, the
        //    Halo* knobs). The faint solar ring stays a screen-space empty-state affordance (design §1.6
        //    engineer's escape hatch: "an empty-state cue, not a threat marker, so either surface is
        //    acceptable") at a fixed size — no live knob, it's a minor affordance. ──
        public const int   DefaultRingMaxIcons     = 12;    // cap so a horde doesn't spawn 80 slots (carried)
        public const bool  DefaultShowEmptyRing    = true;  // faint solar ring when worn+charged-but-clear (Daniel)
        public const bool  DefaultShowDepletedHint = false; // halo + ring fully off when depleted (Daniel)
        public const bool  DefaultDebugTextReadout = false; // legacy text line, debug aid only

        public const bool  DefaultDebugMount = true;  // diagnostic cut (t_d5949685): emit mount/wear LogInfo. Bake to false once the halo is confirmed rendering in-game.

        // Fixed screen-space radius (px) of the faint solar empty-state ring (replaces the removed
        // live RingRadiusPx — the ring is an ambient "lens is live" cue, not a tunable threat surface).
        private const float SolarRingRadiusPx = 140f;

        private static SunstoneLensHudOverlay? _instance;

        private RectTransform? _root;       // the host — ALWAYS ACTIVE (carries this MonoBehaviour's Update pump)
        private RectTransform? _content;    // the visibility child — toggled by SetVisible (NEVER the host; see SetVisible)
        private Image? _emptyRing;          // the faint solar ring outline (empty / substrate) — SCREEN-space affordance
        private Text?  _debugText;          // optional legacy "⚠ N · nearest Xm · charge Y%" readout
        private bool   _loggedFirstShow;    // diagnostic: log once on the first visible frame

        // ── The WORLD-SPACE eidetic trophy halo (card t_d17d9b58). The standalone (no-minimap) render
        //    surface: a head-centric halo of billboarded creature trophies floating in the 3D world at
        //    their real bearings. Owned here (this MonoBehaviour is the single Update pump, #209), but
        //    its slot objects live in WORLD space (its own scene root), NOT under Hud.m_rootObject. ──
        private readonly SunstoneWorldRing _worldRing = new SunstoneWorldRing();

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
        //    lives in SunstoneProjection; the empty-ring uses its own faint solar colour below. ──
        // Faint solar ring — the sunstone's stored daylight glowing faintly.
        private static readonly Color CSolarRing = new Color(0.98f, 0.78f, 0.36f, 0.18f);

        // ── Static caches (resolved once, reused). ──
        private static Sprite? _ringSprite;        // procedurally generated annulus (the faint solar ring)

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
            // Centre on screen — the host carries the faint solar empty-state ring + optional debug text.
            // (The threat trophies now live in WORLD space, in SunstoneWorldRing, NOT on this canvas.)
            _root.anchorMin = new Vector2(0.5f, 0.5f);
            _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot     = new Vector2(0.5f, 0.5f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(2f, 2f); // children are absolutely placed; root is a pivot

            // ── Visibility container (the t_d5949685 fix): the host GameObject (_root) carries THIS
            //    MonoBehaviour, so it MUST stay active or Unity stops calling Update() — and Update()
            //    is the only thing that ever un-hides the overlay. So visibility toggles a CHILD
            //    (_content), never the host. _content is a centred pivot coincident with _root, so the
            //    absolute anchoredPositions of every child (solar ring/text) are unchanged. (Before this
            //    fix, SetVisible(false) at the end of Build() deactivated the host, freezing the Update
            //    pump dead → the overlay rendered NOTHING forever, worn/charged or not — the exact
            //    self-deactivating-host bug the Iron Compass had, PR #208.)
            var contentGo = new GameObject("content", typeof(RectTransform));
            contentGo.transform.SetParent(_root, worldPositionStays: false);
            _content = contentGo.GetComponent<RectTransform>();
            _content.anchorMin = _content.anchorMax = _content.pivot = new Vector2(0.5f, 0.5f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = new Vector2(2f, 2f);

            // The faint solar ring outline (empty state + substrate) — a SCREEN-space affordance at a
            // fixed radius (design §1.6: the empty-state cue may stay screen-space — "either surface is
            // acceptable"). The world halo draws the actual threats; this is just the "lens is live" glow.
            var ringGo = new GameObject("solar_ring", typeof(RectTransform));
            ringGo.transform.SetParent(_content, worldPositionStays: false);
            var ringRt = ringGo.GetComponent<RectTransform>();
            ringRt.anchorMin = ringRt.anchorMax = ringRt.pivot = new Vector2(0.5f, 0.5f);
            ringRt.anchoredPosition = Vector2.zero;
            ringRt.sizeDelta = new Vector2(SolarRingRadiusPx * 2f, SolarRingRadiusPx * 2f);
            _emptyRing = ringGo.AddComponent<Image>();
            _emptyRing.sprite = RingSprite();
            _emptyRing.color = CSolarRing;
            _emptyRing.raycastTarget = false;
            _emptyRing.gameObject.SetActive(false);

            // Optional legacy debug text, just below the ring.
            var font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                       ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _debugText = MakeText("debug", font, 14, FontStyle.Normal, new Vector2(0f, -(SolarRingRadiusPx + 22f)));
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
            // The world halo must never linger when the overlay is being hidden (unequip / depleted /
            // no-player / minimap-owns-detection): hide it here so every inert path drops it immediately
            // (AT-EIDETIC-5 "unequip → halo gone immediately"). When turning visible the caller decides
            // whether to actually draw the halo (RenderWorldHalo) vs only the empty/depleted affordance —
            // we don't show it here, we only guarantee the OFF case. Safe before the ring is built (no-op).
            if (!on) _worldRing.Hide();

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

            // Depleted (inert) state — worn but not enough charge to detect (AC#5). Ring OFF
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
                // Optional faint hint: show ONLY the solar ring, dimmer, no trophies. The world halo is off.
                SetVisible(true);
                _worldRing.Hide();
                if (_emptyRing != null)
                {
                    _emptyRing.gameObject.SetActive(true);
                    _emptyRing.color = new Color(CSolarRing.r, CSolarRing.g, CSolarRing.b, CSolarRing.a * 0.5f);
                }
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
        /// (delegated to <see cref="SunstoneWorldRing"/>) plus the screen-space faint solar ring + the
        /// optional debug readout. Consumes the shared <see cref="_blips"/> derivation (same tint/trophy/
        /// stars the disc + vanilla minimap read — AT-EIDETIC-MINIMAP-UNAFFECTED holds: one source).
        /// </summary>
        private void RenderWorldHalo(Player player, float chargePct)
        {
            float detectR   = Plugin.LensDetectRadius?.Value     ?? SunstoneLens.DefaultDetectRadius;
            int   maxIcons  = Plugin.LensRingMaxIcons?.Value      ?? DefaultRingMaxIcons;
            bool  showEmpty = Plugin.LensRingShowEmpty?.Value     ?? DefaultShowEmptyRing;
            float radMin    = Plugin.LensHaloRadiusMin?.Value     ?? SunstoneWorldRing.DefaultHaloRadiusMin;
            float radMax    = Plugin.LensHaloRadiusMax?.Value     ?? SunstoneWorldRing.DefaultHaloRadiusMax;
            float scaleMax  = Plugin.LensHaloScaleMax?.Value      ?? SunstoneWorldRing.DefaultHaloScaleMax;
            float scaleMin  = Plugin.LensHaloScaleMin?.Value      ?? SunstoneWorldRing.DefaultHaloScaleMin;
            float eyeOffset = Plugin.LensHaloEyeOffsetY?.Value    ?? SunstoneWorldRing.DefaultHaloEyeOffsetY;

            // The halo anchor is the player's EYE-POINT (Character.GetEyePoint(), public :8655) — the
            // head-centric halo (Knob #1/#2). Recompute every frame; the per-slot Billboard handles facing.
            Vector3 eye = player.GetEyePoint();

            // Draw the world halo from the shared projection. Camera-relative by construction (each
            // trophy sits on the REAL blip.WorldPos - eye bearing — the thesis guard, no SignedAngle,
            // no north frame). The world ring pools + caps + sorts nearest-N internally.
            _worldRing.Render(eye, _blips, detectR, radMin, radMax, scaleMax, scaleMin, eyeOffset, maxIcons);

            int shown = Mathf.Min(maxIcons, CountBlips());

            // Faint solar ring (screen-space empty-state affordance): shown whenever worn+charged.
            if (_emptyRing != null)
            {
                _emptyRing.gameObject.SetActive(showEmpty);
                _emptyRing.color = CSolarRing;
            }

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

        /// <summary>Count of non-null blips this tick (for the debug readout's "N hostiles").</summary>
        private int CountBlips()
        {
            int n = 0;
            for (int i = 0; i < _blips.Count; i++)
                if (_blips[i].Character != null) n++;
            return n;
        }

        /// <summary>Tear the world halo down with the overlay (logout / Hud teardown). #209: only the
        /// visuals are destroyed; the host pump's own lifecycle is Unity's to manage.</summary>
        private void OnDestroy()
        {
            _worldRing.Dispose();
        }

        // ───────────────────────────────────────────────
        // SOLAR-RING SPRITE (the only procedural asset still owned here; trophy/star/glyph/tint
        // derivation moved to SunstoneProjection so all three surfaces share one copy — card t_91e86951)
        // ───────────────────────────────────────────────

        /// <summary>
        /// A procedurally-generated thin annulus sprite for the faint solar ring (no disk asset —
        /// the guarantee holds even if the modpack ships zero PNGs). White ring, tinted by Image.color.
        /// </summary>
        private static Sprite RingSprite()
        {
            if (_ringSprite != null) return _ringSprite;
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            var clear = new Color(1f, 1f, 1f, 0f);
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            float cx = size / 2f, cy = size / 2f;
            float outer = size / 2f - 2f;
            float thickness = 3f;
            float inner = outer - thickness;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d <= outer && d >= inner)
                    {
                        // 1px anti-alias at both edges so the ring isn't aliased.
                        float aOuter = Mathf.Clamp01(outer - d);
                        float aInner = Mathf.Clamp01(d - inner);
                        float a = Mathf.Min(1f, Mathf.Min(aOuter + 0.0f, aInner + 0.0f) + 0.4f);
                        px[y * size + x] = new Color(1f, 1f, 1f, a);
                    }
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            _ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _ringSprite.name = "SBPR_SolarRing";
            return _ringSprite;
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
