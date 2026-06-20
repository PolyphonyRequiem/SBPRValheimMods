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

        // ── Aggro-state tint (the Rune-of-Awareness colour code, §1.8). ──
        private static readonly Color CYellow = new Color(0.95f, 0.82f, 0.29f, 1f);  // idle / alerted, no target
        private static readonly Color COrange = new Color(0.95f, 0.55f, 0.16f, 1f);  // aggroed on ANOTHER player
        private static readonly Color CRed    = new Color(0.90f, 0.25f, 0.17f, 1f);  // aggroed on YOU
        // Faint solar ring — the sunstone's stored daylight glowing faintly.
        private static readonly Color CSolarRing = new Color(0.98f, 0.78f, 0.36f, 0.18f);

        // ── Static caches (resolved once, reused). ──
        private static readonly Dictionary<string, Sprite?> _trophyCache = new Dictionary<string, Sprite?>();
        private static Sprite? _starSprite;        // harvested from vanilla EnemyHud nameplate
        private static bool    _starHarvestDone;   // stop retrying once resolved (or proven absent)
        private static Sprite? _ringSprite;        // procedurally generated annulus
        private static Sprite? _threatGlyph;       // generic glyph for trophy-less hostiles
        private static bool    _threatGlyphDone;   // resolved once (PNG, else procedural)
        // Loaded by BARE filename: pack-modpack.sh flattens assets/icons/items/*.png into the plugin
        // folder root, and Assets.LoadPngAsSprite resolves Path.Combine(PluginFolder, filename).
        private const  string  ThreatGlyphIcon = "threat_fallback_v0.1.png";

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
                return;
            }

            var lens = SunstoneLens.GetEquippedLens(player);
            if (lens == null)
            {
                SetVisible(false);   // not worn → overlay hidden entirely
                return;
            }

            // Depleted (inert) state — worn but not enough charge to detect (AC#5). Ring OFF
            // (Daniel: depleted hint default off). The durability bar already signals "dim".
            if (lens.m_durability < SunstoneLens.MinChargeToDetect)
            {
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

            // Throttled detection sweep (cheap to keep the per-frame cost low).
            float interval = Plugin.LensDetectInterval?.Value ?? SunstoneLens.DefaultDetectInterval;
            if (Time.time >= _nextSweep)
            {
                _nextSweep = Time.time + Mathf.Max(0.1f, interval);
                float radius = Plugin.LensDetectRadius?.Value ?? SunstoneLens.DefaultDetectRadius;
                SunstoneLens.GatherHostiles(player, radius, _hostiles);
            }

            RenderRing(player, chargePct);
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

                Color tint = AggroTint(c, player);
                int stars = Mathf.Max(0, c.GetLevel() - 1);

                var slot = EnsureSlot(shown);
                ApplySlot(slot, c, pos, scale, tint, stars);
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

        private void ApplySlot(Slot slot, Character c, Vector2 pos, float scale, Color tint, int stars)
        {
            slot.Rt.anchoredPosition = pos;
            slot.Rt.sizeDelta = new Vector2(scale, scale);

            // Trophy sprite (or the generic threat glyph for trophy-less hostiles).
            var sprite = ResolveTrophySprite(c);
            slot.Trophy.sprite = sprite ?? ThreatGlyph();
            slot.Trophy.color = tint;   // aggro-state tint multiplies onto the trophy

            RenderStars(slot, stars, scale, tint);
        }

        private void RenderStars(Slot slot, int stars, float trophyScale, Color tint)
        {
            // Position the star row just above the trophy.
            slot.StarRow.anchoredPosition = new Vector2(0f, trophyScale * 0.5f + 8f);

            var star = StarSprite();
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
        // VANILLA-ASSET RESOLUTION (read-only — reproduced from base-game primitives)
        // ───────────────────────────────────────────────

        /// <summary>
        /// The trophy sprite for a creature: its <c>CharacterDrop</c> drop whose item is
        /// <c>ItemType.Trophy</c> (enum 13, decomp :57641), taking that item's <c>m_icons[0]</c>.
        /// Resolved once per creature prefab and cached (a null result is cached too, so a
        /// trophy-less hostile isn't re-scanned). This is the same (creature → CharacterDrop →
        /// Trophy) identity surface vanilla's own <c>Player.AddTrophy</c> uses, read the other way.
        /// </summary>
        private static Sprite? ResolveTrophySprite(Character c)
        {
            string key = StripCloneSuffix(c.name);
            if (_trophyCache.TryGetValue(key, out var cached)) return cached;

            Sprite? sprite = null;
            try
            {
                var cd = c.GetComponent<CharacterDrop>();
                if (cd != null && cd.m_drops != null)
                {
                    foreach (var d in cd.m_drops)
                    {
                        if (d == null || d.m_prefab == null) continue;
                        var id = d.m_prefab.GetComponent<ItemDrop>();
                        var shared = id != null ? id.m_itemData?.m_shared : null;
                        if (shared == null) continue;
                        if (shared.m_itemType != ItemDrop.ItemData.ItemType.Trophy) continue;
                        if (shared.m_icons != null && shared.m_icons.Length > 0 && shared.m_icons[0] != null)
                        {
                            sprite = shared.m_icons[0];
                            break;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Sunstone] trophy resolve failed for {key}: {e.Message}");
            }

            _trophyCache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// Harvest the real vanilla nameplate STAR sprite from <c>EnemyHud.m_baseHud</c>'s
        /// <c>level_2</c>/<c>level_3</c> children (the 1★/2★ decorations, decomp :38487-38488).
        /// Returns null until <c>EnemyHud.instance</c> + its base-hud template exist (then caches).
        /// Daniel 2026-06-19: "use the Valheim stars used to decorate the monster nameplates."
        /// </summary>
        private static Sprite? StarSprite()
        {
            if (_starHarvestDone) return _starSprite;
            try
            {
                var eh = EnemyHud.instance;
                var baseHud = eh != null ? eh.m_baseHud : null;
                if (baseHud == null) return null;   // not ready yet — retry next render

                _starSprite = FindStarSprite(baseHud.transform, "level_2")
                              ?? FindStarSprite(baseHud.transform, "level_3");
                _starHarvestDone = true;            // template exists: accept whatever we found (may be null)
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Sunstone] star-sprite harvest failed: {e.Message}");
                _starHarvestDone = true;
            }
            return _starSprite;
        }

        private static Sprite? FindStarSprite(Transform root, string childName)
        {
            var t = root.Find(childName);
            if (t == null) return null;
            var img = t.GetComponent<Image>();
            if (img != null && img.sprite != null) return img.sprite;
            img = t.GetComponentInChildren<Image>(includeInactive: true);
            return img != null ? img.sprite : null;
        }

        /// <summary>
        /// Aggro-state tint (the Rune-of-Awareness colour code, §1.8) — reproduced from vanilla
        /// BaseAI: <c>IsAlerted()</c> (:5450) + <c>GetTargetCreature()</c> (:5564), the same surface
        /// vanilla's own EnemyHud reads (:38538). red = targeting the local player, orange = targeting
        /// another player, yellow = idle/alerted-without-a-player-target. Fails safe to yellow.
        /// </summary>
        private static Color AggroTint(Character c, Player localPlayer)
        {
            try
            {
                var ai = c.GetBaseAI();
                if (ai == null || !ai.IsAlerted()) return CYellow;
                var target = ai.GetTargetCreature();
                if (target == null) return CYellow;
                if (target == (Character)localPlayer) return CRed;
                if (target.IsPlayer()) return COrange;
                return CYellow;
            }
            catch
            {
                return CYellow;
            }
        }

        /// <summary>
        /// The generic threat glyph for trophy-less hostiles (summoned minions, some boss adds).
        /// Loads the shipped PNG (assets/icons/ui/threat_fallback_v0.1.png — near-white so the aggro
        /// tint reads); if the PNG didn't ship, generates a procedural danger-triangle so a
        /// trophy-less hostile is NEVER invisible (degrades to "still shown," never a crash). The
        /// fallback slot is a defined position on the ring, not a skip.
        /// </summary>
        private static Sprite ThreatGlyph()
        {
            if (_threatGlyphDone && _threatGlyph != null) return _threatGlyph;
            if (!_threatGlyphDone)
            {
                _threatGlyph = Assets.LoadPngAsSprite(ThreatGlyphIcon);
                _threatGlyphDone = true;
                if (_threatGlyph == null)
                    Plugin.Log.LogWarning(
                        $"[Trailborne/Sunstone] threat-fallback glyph '{ThreatGlyphIcon}' did not load; "
                        + "using procedural danger-triangle (trophy-less hostiles still render).");
            }
            return _threatGlyph ?? ProceduralThreatGlyph();
        }

        private static Sprite? _proceduralThreat;
        /// <summary>A code-generated near-white danger triangle (no disk dependency).</summary>
        private static Sprite ProceduralThreatGlyph()
        {
            if (_proceduralThreat != null) return _proceduralThreat;
            const int s = 128;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var clear = new Color(1f, 1f, 1f, 0f);
            var px = new Color[s * s];
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            var ink = new Color(0.92f, 0.92f, 0.92f, 1f);
            // Triangle vertices (apex top, base bottom), drawn as a thick outline.
            var apex = new Vector2(s / 2f, s * 0.12f);
            var bl   = new Vector2(s * 0.14f, s * 0.84f);
            var br   = new Vector2(s * 0.86f, s * 0.84f);
            DrawThickLine(px, s, apex, bl, ink, 7f);
            DrawThickLine(px, s, bl, br, ink, 7f);
            DrawThickLine(px, s, br, apex, ink, 7f);
            // Exclamation bar + dot.
            for (int y = (int)(s * 0.34f); y < (int)(s * 0.62f); y++)
                for (int x = (int)(s / 2f - 4); x <= (int)(s / 2f + 4); x++)
                    if (x >= 0 && x < s && y >= 0 && y < s) px[y * s + x] = ink;
            FillDisc(px, s, new Vector2(s / 2f, s * 0.70f), 5.5f, ink);

            tex.SetPixels(px);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            _proceduralThreat = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            _proceduralThreat.name = "SBPR_ThreatGlyphProc";
            return _proceduralThreat;
        }

        private static void DrawThickLine(Color[] px, int s, Vector2 a, Vector2 b, Color c, float w)
        {
            float len = Vector2.Distance(a, b);
            int steps = Mathf.CeilToInt(len);
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, steps == 0 ? 0f : (float)i / steps);
                FillDisc(px, s, p, w * 0.5f, c);
            }
        }

        private static void FillDisc(Color[] px, int s, Vector2 c, float r, Color col)
        {
            int x0 = Mathf.Max(0, (int)(c.x - r)), x1 = Mathf.Min(s - 1, (int)(c.x + r));
            int y0 = Mathf.Max(0, (int)(c.y - r)), y1 = Mathf.Min(s - 1, (int)(c.y + r));
            float r2 = r * r;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - c.x, dy = y - c.y;
                    if (dx * dx + dy * dy <= r2) px[y * s + x] = col;
                }
        }

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

        // Mirror of vanilla ItemDrop.GetPrefabName clone-suffix strip (decomp :58940): cut at the
        // first '(' or ' ' so "Draugr(Clone)" matches the cache key "Draugr".
        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int i = name.IndexOfAny(new[] { '(', ' ' });
            return i >= 0 ? name.Substring(0, i) : name;
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
