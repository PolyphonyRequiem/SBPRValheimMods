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
        private static Sprite? _ringSprite;        // procedurally generated annulus

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
                RenderRing(player, chargePct);
            }
            else
            {
                // Ring suppressed (a minimap owns detection). Park the ring's visuals; the host + pump
                // stay alive (the load-bearing #209 line). _content hidden = no trophies/solar ring drawn.
                HideAllSlots();
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
                    + $"ringRadiusPx={(Plugin.LensRingRadiusPx?.Value ?? DefaultRingRadiusPx)} "
                    + $"(host activeInHierarchy={_root.gameObject.activeInHierarchy}, content active={_content.gameObject.activeSelf}).");
            }
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

            // Render from the projected blips (the SINGLE derivation — same tint/trophy/stars the disc +
            // vanilla minimap consume, AT-LENS-DISC-NODRIFT). Sort nearest-first so the maxIcons cap keeps
            // the most relevant threats (a horde shows the closest N).
            _blips.Sort((a, b) =>
            {
                float da = (a.WorldPos - origin).sqrMagnitude;
                float db = (b.WorldPos - origin).sqrMagnitude;
                return da.CompareTo(db);
            });

            int shown = 0;
            for (int i = 0; i < _blips.Count && shown < maxIcons; i++)
            {
                var blip = _blips[i];
                if (blip.Character == null || cam == null) continue;

                // Camera-relative bearing (THE thesis guard: never north-up). The exact angle the old
                // BearingGlyph computed — 0° = dead ahead, +90° = hard right.
                Vector3 to = blip.WorldPos - origin;
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
                        foreach (var blip in _blips)
                        {
                            if (blip.Character == null) continue;
                            float d = (blip.WorldPos - origin).sqrMagnitude;
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

            // Trophy sprite (or the generic threat glyph for trophy-less hostiles) — both come from the
            // shared SunstoneProjection so the ring matches the disc + vanilla minimap exactly.
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
            for (int i = 0; i < _blips.Count; i++)
            {
                var b = _blips[i];
                if (b.Character == null) continue;
                if ((b.WorldPos - origin).sqrMagnitude > r2) continue;
                into.Add(new DiscThreatMarker(b.WorldPos, b.Tint, trophy ? b.Trophy : null, b.Stars));
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
