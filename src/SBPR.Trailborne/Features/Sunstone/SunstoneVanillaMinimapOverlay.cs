// ============================================================================
//  Trailborne v3 (Swamp) — Sunstone threats on the VANILLA minimap (nomap-OFF)
// ----------------------------------------------------------------------------
//  Design   : docs/design/sunstone-lens-minimap-handoff.md §3.7 + §6 (ACCEPTED)
//  Impl spec: docs/v3/planning/sunstone-minimap-handoff-impl-spec.md §4
//  Card     : t_54c989d3 (impl) ← t_3129842a (design)
//
//  Knob-3 (Daniel-gated): "ANY minimap present, for ANY reason, gets the handoff —
//  including the VANILLA minimap in nomap-OFF." The SBPR carry-disc does NOT exist
//  in nomap-OFF (LocalMapController gates the disc on Game.m_noMap), so the nomap-OFF
//  branch targets the vanilla Minimap directly.
//
//  WHY A CUSTOM OVERLAY, NOT AddPin (design §3.7, forced by the decomp). Vanilla's
//  UpdatePins() hard-overwrites every pin's m_iconElement.color to white/grey on
//  EVERY refresh (decomp Minimap.cs:47832-47836: `color2 = (ownerID!=0 ? grey :
//  Color.white); pin.m_iconElement.color = color2`). A vanilla pin therefore CANNOT
//  carry the per-aggro dynamic tint Daniel LOCKED in Knob 2 — it would be clobbered
//  to white every frame. So we draw our OWN RectTransform layer parented under the
//  vanilla small-map pin root and own its Image.color, projecting each ThreatBlip
//  through the SAME vanilla WorldToMapPoint / MapPointToLocalGuiPos math the pins use
//  (re-implemented here from the decomp — clean-side, ADR-0001).
//
//  🟢 THESIS RE-SCOPE (design §6, EXEMPT). The vanilla minimap is NORTH-UP — verified,
//  not assumed: vanilla WorldToMapPoint (decomp :47977) maps world→texture with ZERO
//  rotation (the map texture is fixed north-up; the player chevron m_smallMarker
//  rotates instead, :47897). In nomap-OFF the player ALREADY has a north-up vanilla
//  minimap and full cardinal orientation independent of the Sunstone, so detection
//  here leaks nothing the player didn't already have — the thesis defends the NoMap
//  worlds (ring + SBPR disc, both camera-relative), and nomap-OFF is not such a
//  world. This overlay is therefore deliberately north-up (it rides the vanilla
//  small-map frame), and AT-LENS-DISC-CAMREL does NOT apply to it.
//
//  ⚠️ TASK-NOTE CONTRADICTION FLAGGED FOR REVIEW (Starbright, 2026-06-20). The impl
//  card's item-5 note claims "SBPR's nomap-OFF minimap is SBPR-built free-rotate, NOT
//  vanilla north-up." That is NOT what ships: NO SBPR patch makes the vanilla small
//  minimap free-rotate (the v1 "minimap freely rotating" baseline from
//  PARKED-2026-06-03.md:20 was NEVER implemented — no patch touches m_smallRoot /
//  m_mapImageSmall rotation; cartography-v2.md:258 confirms the nerf was never
//  built). The LOCKED, Daniel-gated design doc §6 ("the vanilla minimap IS north-up
//  — verified") is authoritative and decomp-confirmed, so this overlay follows the
//  DESIGN DOC: north-up, EXEMPT from the camera-relative thesis guard. Surfaced in
//  the PR so a reviewer can confirm the design-doc reading wins over the card note.
//
//  #209 PUMP (AT-LENS-DISC-PUMP). This overlay is a passive SINK: it draws whatever
//  blips the live SunstoneLensHudOverlay.Update pump publishes. It never drives
//  detection; the ring overlay's Update must stay alive for it to receive blips. The
//  overlay's own Update only PROJECTS + DRAWS the already-collected blips.
//
//  Clean-side (ADR-0001): reads base-game Minimap fields (public m_mapImageSmall /
//  m_pinRootSmall / m_pinSizeSmall / m_textureSize / m_pixelSize) and reproduces its
//  world→gui projection from the decomp. The uGUI layer is our own (the MapSurface /
//  SignPaintPanel idiom). No vanilla UI prefab cloned; no third-party mod code.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SBPR.Trailborne.Features.Cartography;

namespace SBPR.Trailborne.Features.Sunstone
{
    /// <summary>
    /// Client-only overlay that draws Sunstone threat blips onto the VANILLA small minimap in
    /// nomap-OFF (design §3.7). A parallel RectTransform layer under <c>Minimap.m_pinRootSmall</c>,
    /// pooled like the ring's slots, projecting each <see cref="ThreatBlip"/> through vanilla's own
    /// world→minimap-pixel math so a blip lands on the exact terrain cell a vanilla pin would.
    /// Owns its own <c>Image.color</c> so the aggro tint survives (vanilla can't clobber a layer it
    /// doesn't know about). North-up by construction (it rides the vanilla north-up frame) — EXEMPT
    /// from the camera-relative thesis (design §6). Built + driven by <see cref="SunstoneLensHudOverlay"/>.
    /// </summary>
    public sealed class SunstoneVanillaMinimapOverlay
    {
        // Pooled blip slots (trophy/dot Image + star row), reused across frames — never per-frame churn.
        private sealed class Slot
        {
            public GameObject Go = null!;
            public RectTransform Rt = null!;
            public Image Icon = null!;
            public RectTransform StarRow = null!;
            public readonly List<Image> Pips = new List<Image>();
        }

        private readonly List<Slot> _slots = new List<Slot>();
        private RectTransform? _layer;        // our parallel layer under m_pinRootSmall
        private RectTransform? _layerParentSeen; // the pin-root we parented under (re-parent if the Minimap rebuilt)

        private const float BlipPx = 18f;     // dot/trophy size on the small minimap (vanilla pin is 32 px; threats read smaller)
        private const float PipPx  = 8f;

        /// <summary>
        /// True when the vanilla SMALL minimap is the active corner surface (nomap-OFF). This is the
        /// nomap-OFF half of the universal "is any minimap present?" predicate (design §1). Reads the
        /// public <c>Minimap.instance.m_mode</c>: <c>MapMode.Small</c> is the corner minimap; <c>None</c>
        /// is nomap-ON (no vanilla corner); <c>Large</c> is the full M-map open. We host threats on the
        /// SMALL corner surface only (the Large map is a deliberate, focused full-screen view).
        /// </summary>
        public static bool IsVanillaSmallMinimapShown()
        {
            var mm = Minimap.instance;
            return mm != null && mm.m_mode == Minimap.MapMode.Small;
        }

        /// <summary>
        /// Project + draw <paramref name="blips"/> onto the vanilla small minimap. Called from the ring
        /// overlay's Update only when the handoff routes threats to a present minimap AND the vanilla
        /// small map is the active surface. <paramref name="preferTrophy"/> mirrors the live BlipStyle.
        /// Hides all slots + returns early if the vanilla surface isn't available (so a mode flip to
        /// None/Large clears our layer cleanly).
        /// </summary>
        public void Render(IReadOnlyList<ThreatBlip> blips, bool preferTrophy)
        {
            var mm = Minimap.instance;
            if (mm == null || mm.m_mode != Minimap.MapMode.Small || mm.m_mapImageSmall == null || mm.m_pinRootSmall == null)
            {
                HideAll();
                return;
            }

            EnsureLayer(mm.m_pinRootSmall);
            if (_layer == null) { HideAll(); return; }

            var img = mm.m_mapImageSmall;
            Rect uvRect = img.uvRect;
            Rect rect = img.rectTransform.rect;
            int textureSize = mm.m_textureSize > 0 ? mm.m_textureSize : 256;
            float pixelSize = mm.m_pixelSize > 0f ? mm.m_pixelSize : 64f;

            int shown = 0;
            for (int i = 0; i < blips.Count; i++)
            {
                var b = blips[i];

                // Vanilla world→map-point (decomp Minimap.WorldToMapPoint :47977): normalized texture
                // coords, ZERO rotation (north-up). Then world→GUI (MapPointToLocalGuiPos :47938):
                // map-point → anchored px within the small-map rect, honouring the live uvRect window.
                WorldToMapPoint(b.WorldPos, textureSize, pixelSize, out float mx, out float my);
                // Off the visible small-map window → skip (mirror vanilla IsPointVisible :47957).
                if (mx <= uvRect.xMin || mx >= uvRect.xMax || my <= uvRect.yMin || my >= uvRect.yMax)
                    continue;
                Vector2 anchored = MapPointToLocalGuiPos(mx, my, uvRect, rect);

                var slot = EnsureSlot(shown);
                ApplySlot(slot, b, anchored, preferTrophy);
                slot.Go.SetActive(true);
                shown++;
            }

            for (int i = shown; i < _slots.Count; i++)
                _slots[i].Go.SetActive(false);
        }

        /// <summary>Hide every slot (handoff off, or the vanilla surface went away). Cheap; keeps the pool.</summary>
        public void HideAll()
        {
            for (int i = 0; i < _slots.Count; i++)
                _slots[i].Go.SetActive(false);
        }

        /// <summary>Tear the layer + pool down (e.g. when the Minimap is destroyed). Next Render rebuilds.</summary>
        public void Destroy()
        {
            if (_layer != null) Object.Destroy(_layer.gameObject);
            _layer = null;
            _layerParentSeen = null;
            _slots.Clear();
        }

        // ── Vanilla projection, re-implemented from the decomp (clean-side, ADR-0001) ──

        /// <summary>
        /// Vanilla <c>Minimap.WorldToMapPoint</c> (decomp :47977): world → normalized texture coords,
        /// ZERO rotation (the map is north-up; only the player chevron rotates). Reproduced from the
        /// base game we mod (fair to read + adapt).
        /// </summary>
        private static void WorldToMapPoint(Vector3 p, int textureSize, float pixelSize, out float mx, out float my)
        {
            int half = textureSize / 2;
            mx = p.x / pixelSize + half;
            my = p.z / pixelSize + half;
            mx /= textureSize;
            my /= textureSize;
        }

        /// <summary>
        /// Vanilla <c>Minimap.MapPointToLocalGuiPos</c> (decomp :47938): map-point → anchored px within
        /// the small-map rect, honouring the live uvRect zoom window.
        /// </summary>
        private static Vector2 MapPointToLocalGuiPos(float mx, float my, Rect uvRect, Rect transformRect)
        {
            Vector2 r = default;
            r.x = (mx - uvRect.xMin) / uvRect.width;
            r.y = (my - uvRect.yMin) / uvRect.height;
            r.x *= transformRect.width;
            r.y *= transformRect.height;
            return r;
        }

        // ── Layer + slot pooling ──

        private void EnsureLayer(RectTransform pinRoot)
        {
            // Re-parent if the Minimap (and thus its pin root) was rebuilt (world load / relogin), or
            // build the layer the first time. Centre-anchored to match the vanilla pin root's frame.
            if (_layer != null && _layerParentSeen == pinRoot) return;
            if (_layer != null) Object.Destroy(_layer.gameObject);

            var go = new GameObject("SBPR_SunstoneThreatLayer", typeof(RectTransform));
            go.transform.SetParent(pinRoot, worldPositionStays: false);
            _layer = go.GetComponent<RectTransform>();
            _layer.anchorMin = _layer.anchorMax = _layer.pivot = new Vector2(0.5f, 0.5f);
            _layer.anchoredPosition = Vector2.zero;
            _layer.sizeDelta = Vector2.zero; // children are absolutely placed about the pin-root centre
            _layerParentSeen = pinRoot;
            // Pool belongs to the old (destroyed) layer — drop it so EnsureSlot rebuilds under the new layer.
            _slots.Clear();
        }

        private Slot EnsureSlot(int index)
        {
            while (_slots.Count <= index)
                _slots.Add(MakeSlot(_slots.Count));
            return _slots[index];
        }

        private Slot MakeSlot(int idx)
        {
            var go = new GameObject($"threat_{idx}", typeof(RectTransform));
            go.transform.SetParent(_layer, worldPositionStays: false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

            var icon = go.AddComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;

            var rowGo = new GameObject("stars", typeof(RectTransform));
            rowGo.transform.SetParent(rt, worldPositionStays: false);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin = rowRt.anchorMax = rowRt.pivot = new Vector2(0.5f, 0.5f);

            return new Slot { Go = go, Rt = rt, Icon = icon, StarRow = rowRt };
        }

        private void ApplySlot(Slot slot, ThreatBlip blip, Vector2 anchored, bool preferTrophy)
        {
            slot.Rt.anchoredPosition = anchored;
            slot.Rt.sizeDelta = new Vector2(BlipPx, BlipPx);

            // DotsAndTint (default): a tinted dot. TrophyArt (Daniel's A/B): the trophy sprite (or the
            // generic glyph for trophy-less hostiles). Either way OUR Image.color carries the aggro tint
            // — vanilla never touches this layer, so the tint survives (the whole point of §3.7 option b).
            if (preferTrophy)
            {
                var sprite = blip.Trophy ?? SunstoneProjection.ThreatGlyph();
                slot.Icon.sprite = sprite;
            }
            else
            {
                slot.Icon.sprite = DotSprite();
            }
            slot.Icon.color = blip.Tint;

            RenderStars(slot, preferTrophy ? blip.Stars : 0, blip.Tint);
        }

        // Cached dot sprite built from the shared ThreatBlipArt texture (one allocation).
        private static Sprite? _dotSprite;
        private static Sprite DotSprite()
        {
            if (_dotSprite != null) return _dotSprite;
            var tex = ThreatBlipArt.DotTexture();
            _dotSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            _dotSprite.name = "SBPR_ThreatDotSprite";
            return _dotSprite;
        }

        private void RenderStars(Slot slot, int stars, Color tint)
        {
            // Only TrophyArt shows pips (a dot is already glanceable; pips on a dot would clutter the
            // small map). stars==0 hides the row.
            slot.StarRow.anchoredPosition = new Vector2(0f, BlipPx * 0.5f + 5f);
            var star = SunstoneProjection.StarSprite();

            int toShow = star != null ? stars : 0; // no vanilla star art yet → no pips on the minimap (dot stays clean)
            for (int i = 0; i < toShow; i++)
            {
                Image img;
                if (i < slot.Pips.Count) img = slot.Pips[i];
                else
                {
                    var pgo = new GameObject($"pip_{i}", typeof(RectTransform));
                    pgo.transform.SetParent(slot.StarRow, worldPositionStays: false);
                    var prt = pgo.GetComponent<RectTransform>();
                    prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
                    prt.sizeDelta = new Vector2(PipPx, PipPx);
                    img = pgo.AddComponent<Image>();
                    img.raycastTarget = false;
                    img.preserveAspect = true;
                    slot.Pips.Add(img);
                }
                img.sprite = star;
                img.color = tint;
                float startX = -(toShow - 1) * 0.5f * PipPx;
                img.rectTransform.anchoredPosition = new Vector2(startX + i * PipPx, 0f);
                img.gameObject.SetActive(true);
            }
            for (int i = toShow; i < slot.Pips.Count; i++)
                slot.Pips[i].gameObject.SetActive(false);
        }
    }
}
