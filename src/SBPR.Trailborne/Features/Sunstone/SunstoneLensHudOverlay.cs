// ============================================================================
//  Trailborne v3 (Swamp) — Sunstone Lens HUD overlay (the trophy-ring detection surface)
// ----------------------------------------------------------------------------
//  Design   : docs/design/sunstone-lens-trophy-ring.md (card t_b8a19487, Daniel 2026-06-18/19)
//  Impl spec: docs/v3/planning/sunstone-lens-impl-spec.md §4-§5 (render SUPERSEDED by the ring)
//  Mechanic : SunstoneLens.GatherHostiles (unchanged — this file only RENDERS the result)
//
//  The render surface for the Sunstone Lens' monster detection: a screen-space,
//  camera-relative RING of creature TROPHIES around the player. Replaces the old
//  bottom-center text placeholder (which survives only as an optional debug readout).
//  This is the real design Daniel locked:
//    • Per detected hostile → its TROPHY sprite (read from the creature's CharacterDrop),
//      placed on a fixed-radius ring at the camera-relative BEARING to that enemy.
//    • Trophy SIZE ∝ proximity (near = big, far = small); the ring RADIUS is fixed.
//    • Star pips above the trophy for star-level enemies — using the REAL vanilla
//      nameplate star art harvested from EnemyHud (Daniel 2026-06-19).
//    • Aggro-state COLOUR tint (the "Rune of Awareness" element, Daniel 2026-06-19):
//      yellow = idle/alerted, orange = aggroed on another player, red = aggroed on YOU.
//      Reproduced from vanilla BaseAI primitives (IsAlerted / GetTargetCreature).
//    • Empty (worn+charged, nothing near) → a faint SOLAR RING outline (Daniel).
//    • Depleted (charge < MinChargeToDetect) or not worn → ring off.
//
//  Client-only by construction: Hud.Awake never fires on the dedicated server (no Hud),
//  and Character.GetAllCharacters / Player.m_localPlayer / EnemyHud.instance are client
//  concerns. Everything here is cosmetic — it reads game state, never writes it.
//
//  Clean-side (ADR-0001): reads base-game Hud/Player/Character/CharacterDrop/BaseAI/
//  EnemyHud/GameCamera only; the uGUI surface is our own (the MapViewer / Iron Compass
//  idiom this repo already ships). No vanilla UI cloned, no third-party mod code read —
//  the Rune-of-Awareness *behaviour* is reproduced from vanilla primitives only.
//  ADR-0006: the ring + trophy + star Images are built additively (new GameObject +
//  AddComponent); reusing the trophy/star SPRITES is reading an asset, not cloning.
//
//  logs-green ≠ playable — Daniel verifies AT-LENS-RING-* in-game.
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
        // ── Ring render tuning (single source of truth; Plugin binds ConfigEntry mirrors so Daniel
        //    converges feel on a joined client without a rebuild — the banner-windsock pattern). ──
        public const float DefaultRingRadiusPx     = 180f;  // fixed screen-space ring radius
        public const float DefaultRingCenterOffsetY = 0f;   // nudge ring centre up/down from screen centre
        public const float DefaultRingIconMinPx    = 28f;   // trophy size at the edge of detection range
        public const float DefaultRingIconMaxPx    = 64f;   // trophy size right on top of the player
        public const int   DefaultRingMaxIcons     = 12;    // cap so a horde doesn't spawn 80 images
        public const bool  DefaultShowEmptyRing    = true;  // faint solar ring when worn+charged-but-clear (Daniel)
        public const bool  DefaultShowDepletedHint = false; // ring fully off when depleted (Daniel)
        public const bool  DefaultDebugTextReadout = false; // legacy text line, debug aid only

        public const bool  DefaultDebugMount = true;  // diagnostic cut (t_d5949685): emit mount/wear LogInfo. Bake to false once the ring is confirmed rendering in-game.

        // ── §4 / §5 handoff knobs (card t_54c989d3; design sunstone-lens-minimap-handoff.md §4/§5).
        //    Daniel-gated defaults FORWARD to the engine-free SunstoneHandoffPolicy (the single source
        //    of truth, CI-assertable headless). Live Config.Bind in Plugin.Awake binds off these consts
        //    so Daniel flips them on a joined client without a rebuild (banner-windsock pattern). ──
        public const MinimapHandoffMode DefaultMinimapHandoffMode = SunstoneHandoffPolicy.DefaultMode;
        public const BlipStyle          DefaultBlipStyle          = SunstoneHandoffPolicy.DefaultBlipStyle;

        private static SunstoneLensHudOverlay? _instance;

        private RectTransform? _root;       // the host — ALWAYS ACTIVE (carries this MonoBehaviour's Update pump)
        private RectTransform? _content;    // the visibility child — toggled by SetVisible (NEVER the host; see SetVisible)
        private Image? _emptyRing;          // the faint solar ring outline (empty / substrate)
        private Text?  _debugText;          // optional legacy "⚠ N · nearest Xm · charge Y%" readout
        private bool   _loggedFirstShow;    // diagnostic: log once on the first visible frame

        // Diagnostic-logging gate (t_d5949685). Reads the live Plugin config when present (so Daniel can
        // flip it in a joined session), else the Default* const — the no-Plugin-context fallback idiom.
        // internal so the sibling HudBootstrap (mount log) can share the one gate.
        internal static bool DebugMount => Plugin.LensRingDebugMount?.Value ?? DefaultDebugMount;

        // Pooled per-hostile ring slots (reused across sweeps — never create/destroy per frame).
        private readonly List<Slot> _slots = new List<Slot>();

        // Reused across sweeps to avoid per-frame allocations.
        private readonly List<Character> _hostiles = new List<Character>();
        private float _nextSweep;

        // ── Faint solar ring tint (the empty-state affordance; ring-only, NOT lifted). ──
        // Faint solar ring — the sunstone's stored daylight glowing faintly.
        private static readonly Color CSolarRing = new Color(0.98f, 0.78f, 0.36f, 0.18f);

        // ── Static caches (ring-only; the per-hostile visual derivation — trophy/star/glyph/tint —
        //    is LIFTED to SunstoneProjection so the ring, the SBPR disc, and the vanilla overlay
        //    share ONE mapping (AT-LENS-DISC-NODRIFT). This file keeps only the solar-ring sprite. ──
        private static Sprite? _ringSprite;        // procedurally generated annulus (empty-state outline)

        // The carry-disc + vanilla-minimap overlay (the handoff sink for nomap-OFF). Built lazily;
        // driven from THIS Update pump so the detection feed is single-sourced (#209 / AT-LENS-DISC-PUMP).
        private readonly SunstoneVanillaMinimapOverlay _vanillaOverlay = new SunstoneVanillaMinimapOverlay();
        private static bool _providerRegistered;

        /// <summary>
        /// The live hostile list the detection sweep last published (AT-LENS-DISC-NODRIFT / -PUMP). The
        /// SBPR disc's IThreatMarkerProvider (SunstoneProjection.CollectThreatBlips) reads this so every
        /// surface is fed from the SAME pump — null until the first sweep runs (the provider then falls
        /// back to a direct gather for that one frame). Static so the provider reaches it without a
        /// back-reference into the overlay instance.
        /// </summary>
        internal static List<Character>? LiveHostilesOrNull { get; private set; }

        /// <summary>A single pooled ring slot: a trophy Image plus a row of star pips.</summary>
        private sealed class Slot
        {
            public GameObject Go = null!;
            public RectTransform Rt = null!;
            public Image Trophy = null!;
            public RectTransform StarRow = null!;
            public readonly List<Image> Pips = new List<Image>();
            public Text? PipText;   // Unicode-★ fallback when no vanilla star sprite resolved
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

            // §3.6: register the threat-marker provider so the SBPR carry-disc (MapSurface.RebuildOverlay)
            // pulls our live blips each rebuild. Coupling arrow points Sunstone → Cartography (we register
            // INTO the registry; Cartography never references Sunstone). Idempotent across Hud rebuilds.
            if (!_providerRegistered)
            {
                Cartography.ThreatMarkerRegistry.Register(new SunstoneThreatProvider());
                _providerRegistered = true;
                Plugin.Log.LogInfo("[Trailborne/Sunstone] Threat-marker provider registered (SBPR disc consumes the Sunstone feed).");
            }
        }

        private void Build()
        {
            _root = gameObject.AddComponent<RectTransform>();
            // Centre on screen (camera-relative radar). Anchored centre so it scales with the canvas.
            _root.anchorMin = new Vector2(0.5f, 0.5f);
            _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot     = new Vector2(0.5f, 0.5f);
            float offY = Plugin.LensRingCenterOffsetY?.Value ?? DefaultRingCenterOffsetY;
            _root.anchoredPosition = new Vector2(0f, offY);
            _root.sizeDelta = new Vector2(2f, 2f); // children are absolutely placed; root is a pivot

            // ── Visibility container (the t_d5949685 fix): the host GameObject (_root) carries THIS
            //    MonoBehaviour, so it MUST stay active or Unity stops calling Update() — and Update()
            //    is the only thing that ever un-hides the overlay. So visibility toggles a CHILD
            //    (_content), never the host. _content is a centred pivot coincident with _root, so the
            //    absolute anchoredPositions of every child (ring/slots/text) are unchanged. (Before this
            //    fix, SetVisible(false) at the end of Build() deactivated the host, freezing the Update
            //    pump dead → the overlay rendered NOTHING forever, worn/charged or not — the exact
            //    self-deactivating-host bug the Iron Compass had, PR #208.)
            var contentGo = new GameObject("content", typeof(RectTransform));
            contentGo.transform.SetParent(_root, worldPositionStays: false);
            _content = contentGo.GetComponent<RectTransform>();
            _content.anchorMin = _content.anchorMax = _content.pivot = new Vector2(0.5f, 0.5f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = new Vector2(2f, 2f);

            float radius = Plugin.LensRingRadiusPx?.Value ?? DefaultRingRadiusPx;

            // The faint solar ring outline (empty state + substrate). Centre-anchored, sized to 2R.
            var ringGo = new GameObject("solar_ring", typeof(RectTransform));
            ringGo.transform.SetParent(_content, worldPositionStays: false);
            var ringRt = ringGo.GetComponent<RectTransform>();
            ringRt.anchorMin = ringRt.anchorMax = ringRt.pivot = new Vector2(0.5f, 0.5f);
            ringRt.anchoredPosition = Vector2.zero;
            ringRt.sizeDelta = new Vector2(radius * 2f, radius * 2f);
            _emptyRing = ringGo.AddComponent<Image>();
            _emptyRing.sprite = RingSprite();
            _emptyRing.color = CSolarRing;
            _emptyRing.raycastTarget = false;
            _emptyRing.gameObject.SetActive(false);

            // Optional legacy debug text, just below the ring.
            var font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                       ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _debugText = MakeText("debug", font, 14, FontStyle.Normal, new Vector2(0f, -(radius + 22f)));
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
                ClearDetectionSurfaces();   // no detection → disc/vanilla overlay must not show stale threats
                return;
            }

            var lens = SunstoneLens.GetEquippedLens(player);
            if (lens == null)
            {
                SetVisible(false);   // not worn → overlay hidden entirely
                ClearDetectionSurfaces();
                return;
            }

            // Depleted (inert) state — worn but not enough charge to detect (AC#5). Ring OFF
            // (Daniel: depleted hint default off). The durability bar already signals "dim".
            if (lens.m_durability < SunstoneLens.MinChargeToDetect)
            {
                ClearDetectionSurfaces();   // inert → no blips on ANY surface (the disc/vanilla overlay too)
                bool showDepleted = Plugin.LensRingShowDepletedHint?.Value ?? DefaultShowDepletedHint;
                if (!showDepleted)
                {
                    SetVisible(false);
                    return;
                }
                // Optional faint hint: show ONLY the solar ring, dimmer, no trophies.
                SetVisible(true);
                HideAllSlots();
                if (_emptyRing != null)
                {
                    _emptyRing.gameObject.SetActive(true);
                    _emptyRing.color = new Color(CSolarRing.r, CSolarRing.g, CSolarRing.b, CSolarRing.a * 0.5f);
                }
                if (_debugText != null) _debugText.gameObject.SetActive(false);
                return;
            }

            SetVisible(true);

            // Diagnostic (t_d5949685): on the FIRST visible frame, log the resolved placement so a fresh
            // client LogOutput.log can split a mount/pump failure (this line never appears) from an
            // on-screen-but-empty ring (line appears with a sane anchoredPosition/size). Re-armed on hide.
            if (DebugMount && !_loggedFirstShow && _root != null && _content != null)
            {
                _loggedFirstShow = true;
                Plugin.Log.LogInfo($"[Trailborne/Sunstone] LensHud first show: root.anchoredPosition={_root.anchoredPosition} "
                    + $"ringRadiusPx={(Plugin.LensRingRadiusPx?.Value ?? DefaultRingRadiusPx)} "
                    + $"(host activeInHierarchy={_root.gameObject.activeInHierarchy}, content active={_content.gameObject.activeSelf}).");
            }

            float max = Mathf.Max(1f, lens.GetMaxDurability());
            float chargePct = Mathf.Clamp01(lens.m_durability / max) * 100f;

            // Throttled detection sweep (cheap to keep the per-frame cost low). The result is PUBLISHED
            // to LiveHostilesOrNull so the SBPR disc + vanilla-minimap overlay feed from the SAME pump
            // (single-sourced detection — AT-LENS-DISC-NODRIFT / -PUMP). This sweep runs regardless of
            // which surface draws, so hiding the ring's visuals (DiscWhenBound) never starves the
            // minimap surfaces (the #209 guard: the pump is THIS Update, which stays alive because the
            // host GameObject is never deactivated — only _content is, via SetVisible).
            float interval = Plugin.LensDetectInterval?.Value ?? SunstoneLens.DefaultDetectInterval;
            if (Time.time >= _nextSweep)
            {
                _nextSweep = Time.time + Mathf.Max(0.1f, interval);
                float radius = Plugin.LensDetectRadius?.Value ?? SunstoneLens.DefaultDetectRadius;
                SunstoneLens.GatherHostiles(player, radius, _hostiles);
                LiveHostilesOrNull = _hostiles; // publish for the disc/vanilla-overlay consumers
            }

            // ── §4 HANDOFF ROUTING (the load-bearer; AT-LENS-DISC-HANDOFF). Decide WHERE threats draw
            //    from (mode × any-minimap-present), then: keep the ring's _content visible iff the ring
            //    shows threats, and feed the active minimap surface iff a minimap hosts them. The ring's
            //    detection pump (this method) is ALWAYS alive — we only toggle the ring's VISUALS. ──
            var mode = Plugin.LensMinimapHandoffMode?.Value ?? DefaultMinimapHandoffMode;
            bool preferTrophy = (Plugin.LensBlipStyle?.Value ?? DefaultBlipStyle) == BlipStyle.TrophyArt;
            ThreatMarkerRegistry.PreferTrophyArt = preferTrophy; // the SBPR disc reads this each rebuild

            bool anyMinimap = IsAnyMinimapPresent();
            bool ringShowsThreats = SunstoneHandoffPolicy.RingShowsThreats(mode, anyMinimap);
            bool minimapShowsThreats = SunstoneHandoffPolicy.MinimapShowsThreats(mode, anyMinimap);

            // The vanilla-minimap overlay (nomap-OFF). The SBPR disc (nomap-ON) is fed via the registry
            // provider that MapSurface.RebuildOverlay already pulls — no per-frame call needed here.
            if (minimapShowsThreats && SunstoneVanillaMinimapOverlay.IsVanillaSmallMinimapShown())
                _vanillaOverlay.Render(BuildBlips(player), preferTrophy);
            else
                _vanillaOverlay.HideAll();

            // The ring: visuals on only when the ring hosts threats; pump unaffected (the empty solar
            // ring still shows when ringShowsThreats — it's the lens-live affordance, not a threat).
            if (ringShowsThreats)
            {
                RenderRing(player, chargePct);
            }
            else
            {
                // Ring handed off to a minimap: hide the ring's threat visuals (slots + solar ring +
                // debug text) WITHOUT deactivating the host (the #209 fix path). _content stays visible
                // so the host's Update keeps pumping; we just park the ring's own children.
                HideAllSlots();
                if (_emptyRing != null) _emptyRing.gameObject.SetActive(false);
                if (_debugText != null) _debugText.gameObject.SetActive(false);
            }
        }

        // Reused blip buffer for the vanilla-minimap overlay feed (avoid per-frame allocation).
        private readonly List<ThreatBlip> _blipBuf = new List<ThreatBlip>();

        /// <summary>
        /// Project the live published hostiles into shared <see cref="ThreatBlip"/>s for the vanilla
        /// overlay. Uses the SAME SunstoneProjection every surface consumes (AT-LENS-DISC-NODRIFT).
        /// </summary>
        private List<ThreatBlip> BuildBlips(Player player)
        {
            _blipBuf.Clear();
            var hostiles = LiveHostilesOrNull ?? _hostiles;
            foreach (var c in hostiles)
            {
                if (c == null || c.IsDead()) continue;
                _blipBuf.Add(SunstoneProjection.Project(c, player));
            }
            return _blipBuf;
        }

        /// <summary>
        /// The universal "is ANY minimap present?" predicate (design §1, Daniel-gated universal rule).
        /// True when EITHER the SBPR carry-disc is bound (nomap-ON: <c>CartographyViewer.IsMinimapBound</c>)
        /// OR the vanilla small minimap is showing (nomap-OFF). The two are mutually exclusive by vanilla
        /// construction (SetMapMode forces None under Game.m_noMap), so this is a clean OR.
        /// </summary>
        private static bool IsAnyMinimapPresent()
            => CartographyViewer.IsMinimapBound || SunstoneVanillaMinimapOverlay.IsVanillaSmallMinimapShown();

        /// <summary>
        /// Detection is inactive this frame (no player, lens not worn, or inert) — clear the published
        /// hostile feed (so the SBPR disc's provider returns no blips) and hide the vanilla-minimap
        /// overlay. WITHOUT this, a lens that goes inert would leave stale blips on the disc / vanilla
        /// minimap until the next active sweep. Empties the shared <see cref="_hostiles"/> buffer so the
        /// provider's fallback gather also sees nothing, then republishes the (now empty) list.
        /// </summary>
        private void ClearDetectionSurfaces()
        {
            _hostiles.Clear();
            LiveHostilesOrNull = _hostiles;
            _vanillaOverlay.HideAll();
        }

        private void RenderRing(Player player, float chargePct)
        {
            float ringR    = Plugin.LensRingRadiusPx?.Value   ?? DefaultRingRadiusPx;
            float detectR  = Plugin.LensDetectRadius?.Value    ?? SunstoneLens.DefaultDetectRadius;
            float minPx    = Plugin.LensRingIconMinPx?.Value   ?? DefaultRingIconMinPx;
            float maxPx    = Plugin.LensRingIconMaxPx?.Value   ?? DefaultRingIconMaxPx;
            int   maxIcons = Plugin.LensRingMaxIcons?.Value     ?? DefaultRingMaxIcons;
            bool  showEmpty = Plugin.LensRingShowEmpty?.Value   ?? DefaultShowEmptyRing;

            var cam = Utils.GetMainCamera();
            Vector3 origin = player.transform.position;

            // Build the to-render list: nearest-first, capped at maxIcons (a horde shows the closest N).
            // Reuse _hostiles; sort by distance so the cap keeps the most relevant threats.
            _hostiles.Sort((a, b) =>
            {
                if (a == null) return 1;
                if (b == null) return -1;
                float da = (a.transform.position - origin).sqrMagnitude;
                float db = (b.transform.position - origin).sqrMagnitude;
                return da.CompareTo(db);
            });

            int shown = 0;
            for (int i = 0; i < _hostiles.Count && shown < maxIcons; i++)
            {
                var c = _hostiles[i];
                if (c == null || cam == null) continue;

                // Camera-relative bearing (THE thesis guard: never north-up). The exact angle the old
                // BearingGlyph computed — 0° = dead ahead, +90° = hard right.
                Vector3 to = c.transform.position - origin;
                to.y = 0f;
                Vector3 fwd = cam.transform.forward; fwd.y = 0f;
                if (to.sqrMagnitude < 0.0001f || fwd.sqrMagnitude < 0.0001f) continue;
                float signed = Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);
                float rad = signed * Mathf.Deg2Rad;
                // 0° at the TOP of the ring (straight ahead), +90° to the right.
                Vector2 pos = new Vector2(Mathf.Sin(rad) * ringR, Mathf.Cos(rad) * ringR);

                // Size ∝ proximity (fixed ring radius; only icon size encodes distance).
                float dist = to.magnitude;
                float t = 1f - Mathf.Clamp01(dist / Mathf.Max(1f, detectR));
                float scale = Mathf.Lerp(minPx, maxPx, t);

                // §2 ZERO-DRIFT: derive tint/trophy/pips via the SHARED SunstoneProjection — the SAME
                // producer the SBPR disc + vanilla overlay consume (AT-LENS-DISC-NODRIFT). The ring used
                // to derive these inline (AggroTint/ResolveTrophySprite/GetLevel); routing through the
                // projection is what guarantees a future aggro-colour tweak changes every surface together.
                ThreatBlip blip = SunstoneProjection.Project(c, player);

                var slot = EnsureSlot(shown);
                ApplySlot(slot, blip, pos, scale);
                slot.Go.SetActive(true);
                shown++;
            }

            // Park the unused tail.
            for (int i = shown; i < _slots.Count; i++)
                _slots[i].Go.SetActive(false);

            // Faint solar ring: shown whenever worn+charged (substrate + empty-state affordance).
            if (_emptyRing != null)
            {
                _emptyRing.gameObject.SetActive(showEmpty);
                _emptyRing.color = CSolarRing;
            }

            // Optional legacy debug readout.
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
                        foreach (var c in _hostiles)
                        {
                            if (c == null) continue;
                            float d = (c.transform.position - origin).sqrMagnitude;
                            if (d < nearest) nearest = d;
                        }
                        _debugText.text = $"⚠ {shown} hostile{(shown == 1 ? "" : "s")} · nearest {Mathf.Sqrt(nearest):0}m · charge {chargePct:0}%";
                    }
                }
            }
        }

        private void HideAllSlots()
        {
            for (int i = 0; i < _slots.Count; i++)
                _slots[i].Go.SetActive(false);
        }

        // ───────────────────────────────────────────────
        // SLOT POOLING + RENDER
        // ───────────────────────────────────────────────

        private Slot EnsureSlot(int index)
        {
            while (_slots.Count <= index)
                _slots.Add(MakeSlot(_slots.Count));
            return _slots[index];
        }

        private Slot MakeSlot(int idx)
        {
            var go = new GameObject($"slot_{idx}", typeof(RectTransform));
            go.transform.SetParent(_content, worldPositionStays: false);  // under _content (visibility child), not the host
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var trophy = go.AddComponent<Image>();
            trophy.raycastTarget = false;
            trophy.preserveAspect = true;

            // Star row above the trophy.
            var rowGo = new GameObject("stars", typeof(RectTransform));
            rowGo.transform.SetParent(rt, worldPositionStays: false);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin = rowRt.anchorMax = rowRt.pivot = new Vector2(0.5f, 0.5f);

            return new Slot { Go = go, Rt = rt, Trophy = trophy, StarRow = rowRt };
        }

        private void ApplySlot(Slot slot, ThreatBlip blip, Vector2 pos, float scale)
        {
            slot.Rt.anchoredPosition = pos;
            slot.Rt.sizeDelta = new Vector2(scale, scale);

            // Trophy sprite (or the generic threat glyph for trophy-less hostiles) — from the SHARED
            // projection (AT-LENS-DISC-NODRIFT). The ring ALWAYS draws trophy art (its icons are full-
            // size, unlike the disc-tiny minimap blips); the aggro tint multiplies onto it.
            slot.Trophy.sprite = blip.Trophy ?? SunstoneProjection.ThreatGlyph();
            slot.Trophy.color = blip.Tint;   // aggro-state tint multiplies onto the trophy

            RenderStars(slot, blip.Stars, scale, blip.Tint);
        }

        private void RenderStars(Slot slot, int stars, float trophyScale, Color tint)
        {
            // Position the star row just above the trophy.
            slot.StarRow.anchoredPosition = new Vector2(0f, trophyScale * 0.5f + 8f);

            var star = SunstoneProjection.StarSprite();
            const float pip = 12f;

            if (star != null)
            {
                // Image pips using the real vanilla nameplate star art.
                if (slot.PipText != null) slot.PipText.gameObject.SetActive(false);

                float startX = -(stars - 1) * 0.5f * pip;
                for (int i = 0; i < stars; i++)
                {
                    Image img;
                    if (i < slot.Pips.Count) img = slot.Pips[i];
                    else
                    {
                        var pgo = new GameObject($"pip_{i}", typeof(RectTransform));
                        pgo.transform.SetParent(slot.StarRow, worldPositionStays: false);
                        var prt = pgo.GetComponent<RectTransform>();
                        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
                        prt.sizeDelta = new Vector2(pip, pip);
                        img = pgo.AddComponent<Image>();
                        img.raycastTarget = false;
                        img.preserveAspect = true;
                        slot.Pips.Add(img);
                    }
                    img.sprite = star;
                    img.color = tint;
                    img.rectTransform.anchoredPosition = new Vector2(startX + i * pip, 0f);
                    img.gameObject.SetActive(true);
                }
                for (int i = stars; i < slot.Pips.Count; i++)
                    slot.Pips[i].gameObject.SetActive(false);
            }
            else
            {
                // Fallback: a single Unicode ★ Text so the star count is never lost.
                for (int i = 0; i < slot.Pips.Count; i++) slot.Pips[i].gameObject.SetActive(false);
                if (slot.PipText == null && stars > 0)
                {
                    var font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                               ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                    var tgo = new GameObject("pip_text", typeof(RectTransform));
                    tgo.transform.SetParent(slot.StarRow, worldPositionStays: false);
                    var trt = tgo.GetComponent<RectTransform>();
                    trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
                    trt.sizeDelta = new Vector2(80f, 16f);
                    slot.PipText = tgo.AddComponent<Text>();
                    slot.PipText.font = font;
                    slot.PipText.fontSize = 13;
                    slot.PipText.alignment = TextAnchor.MiddleCenter;
                    slot.PipText.horizontalOverflow = HorizontalWrapMode.Overflow;
                    slot.PipText.verticalOverflow = VerticalWrapMode.Overflow;
                    slot.PipText.raycastTarget = false;
                }
                if (slot.PipText != null)
                {
                    slot.PipText.gameObject.SetActive(stars > 0);
                    if (stars > 0)
                    {
                        slot.PipText.text = new string('\u2605', Mathf.Min(stars, 6));
                        slot.PipText.color = tint;
                    }
                }
            }
        }

        // ───────────────────────────────────────────────
        // RING-ONLY ART (the per-hostile visual derivation — trophy/star/glyph/tint — was LIFTED to
        // SunstoneProjection so all three surfaces share one mapping. Only the solar-ring annulus,
        // which is unique to the camera-relative ring, stays here.)
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
