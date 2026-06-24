// ============================================================================
//  Trailborne v3 (Swamp) — Sunstone threat overlay on the VANILLA minimap (nomap-OFF)
// ----------------------------------------------------------------------------
//  Impl spec: docs/v3/planning/sunstone-minimap-handoff-impl-spec.md §5
//  Design   : docs/design/sunstone-lens-minimap-handoff.md §3.7/§6 (ACCEPTED, PR #214)
//  Card     : t_91e86951
//
//  Draws the Lens threat blips onto the VANILLA corner minimap in nomap-OFF worlds
//  (Daniel's universal "any minimap present → handoff" rule, 2026-06-20). The SBPR
//  carry-disc does NOT exist in nomap-OFF (LocalMapController gates the disc render
//  on Game.m_noMap), so this path targets the vanilla Minimap directly.
//
//  ── WHY A CUSTOM OVERLAY, NOT Minimap.AddPin (grounded OUT) ──────────────────────
//  Vanilla's UpdatePins() HARD-OVERWRITES pin.m_iconElement.color on EVERY refresh
//  (decomp Minimap.UpdatePins: `color2 = (ownerID != 0) ? grey : Color.white;
//  pin.m_iconElement.color = color2;`). So a vanilla pin CANNOT carry the per-aggro
//  dynamic tint Daniel locked in Knob 2 — it would be stomped white every frame.
//  (SBPR already feels this: WorldPins.ReapplyColors exists solely to re-stamp pin
//  colour after vanilla's stomp.) Daniel's steer 2026-06-20: "maybe you don't use
//  the actual pinning system?" — so this is a custom RectTransform layer that owns
//  its own Image.color and survives, exactly like the SBPR disc threat layer. One
//  renderer model (ThreatBlip), three hosts (ring / disc / vanilla minimap).
//
//  ── ORIENTATION: deliberately NORTH-UP, EXEMPT from the thesis (design §6) ───────
//  The vanilla corner map is north-up: the map texture is fixed and only the player
//  chevron (m_smallShipMarker) rotates (decomp UpdatePlayerMarker). SBPR does NOT
//  rotate the vanilla small map anywhere — there is no SBPR code that sets a
//  localRotation on m_smallRoot / m_pinRootSmall (the SBPR free-rotate behaviour is
//  exclusive to MapSurface, the nomap-ON carry-disc). So blips here sit at fixed
//  north-up map positions, exactly like vanilla pins. This is CORRECT per design §6:
//  in nomap-OFF the player already has free cardinal orientation from the vanilla
//  map, so AT-LENS-RING-CAMREL (re-scoped to NoMap-worlds-only) does NOT apply here.
//  Parenting under m_pinRootSmall makes the threat layer inherit whatever frame
//  vanilla uses for pins — north-up today, and any future change rides it for free.
//  DO NOT counter-rotate this layer (that is the disc's behaviour, wrong here).
//
//  Client-only by construction: Minimap.instance is null on a dedicated server.
//
//  Clean-side (ADR-0001): reads base-game Minimap public fields (m_pinRootSmall,
//  m_mapImageSmall, m_textureSize, m_pixelSize) and REPRODUCES vanilla's trivial
//  world→map arithmetic (fair to read+adapt). No vanilla UI prefab cloned, no
//  reflection into private members, no third-party mod code referenced.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SBPR.Trailborne.Features.Sunstone
{
    /// <summary>
    /// A custom transient threat overlay on the vanilla corner minimap (nomap-OFF path). Mounts a
    /// pooled set of <see cref="Image"/> blips under <c>Minimap.instance.m_pinRootSmall</c> and
    /// projects each <see cref="ThreatBlip"/> with vanilla's own world→small-map math. Owns its own
    /// <c>Image.color</c> so the aggro tint survives vanilla's per-refresh pin-colour stomp.
    /// </summary>
    internal sealed class SunstoneMinimapThreatLayer
    {
        private RectTransform? _layer;            // child of Minimap.instance.m_pinRootSmall
        private readonly List<Image> _pool = new List<Image>();
        // Blip size + rim multiplier now live in the shared Cartography.MinimapThreatMetrics (card
        // t_bc017af4) so BOTH minimap surfaces read ONE symbol and can't desync. The size is resolved
        // live via Plugin.ResolvedMinimapBlipPx (the SunstoneLens/MinimapBlipPx knob); the rim scale is
        // the unchanged 0.6 constant. RimInset (position, not size) stays local — out of that scope.
        private const float RimInset = 0.92f;     // seat a clamped rim blip at 92% of the visible radius

        /// <summary>
        /// (Re)mount the layer lazily under the vanilla small-map pin root. Returns false until the
        /// Minimap + m_pinRootSmall exist (so the caller can simply skip this tick). Re-parents if the
        /// Minimap was rebuilt (world load / relog) and our old layer went with it.
        /// </summary>
        private bool EnsureLayer()
        {
            var mm = Minimap.instance;
            if (mm == null || mm.m_pinRootSmall == null) return false;

            // Re-mount if our layer is gone (destroyed with a rebuilt Minimap) or was reparented.
            if (_layer == null || _layer.transform.parent != mm.m_pinRootSmall)
            {
                var go = new GameObject("SBPR_SunstoneMinimapThreats", typeof(RectTransform));
                go.transform.SetParent(mm.m_pinRootSmall, worldPositionStays: false);
                _layer = go.GetComponent<RectTransform>();
                _layer.anchorMin = _layer.anchorMax = _layer.pivot = new Vector2(0.5f, 0.5f);
                _layer.anchoredPosition = Vector2.zero;
                _layer.sizeDelta = Vector2.zero;   // a zero-size pivot; children are absolutely placed
                _pool.Clear();                     // old pool belonged to the old (destroyed) layer
            }
            return true;
        }

        /// <summary>
        /// Rebuild the blips from the latest swept set. Called from the overlay's Update when the
        /// active surface is the vanilla minimap (throttled to the detection cadence). Parks the unused
        /// pool tail rather than destroying it (no per-tick churn).
        /// </summary>
        internal void Render(IReadOnlyList<ThreatBlip> blips, BlipStyle style)
        {
            if (!EnsureLayer() || _layer == null) { Clear(); return; }
            var mm = Minimap.instance;
            if (mm == null) { Clear(); return; }

            // Visible-radius of the small map (for the off-edge rim clamp). The corner map's rect is
            // square; the inscribed circle radius is half the smaller extent.
            float visibleR = VisibleSmallMapRadiusPx(mm);

            Sprite dot = SunstoneProjection.DotSprite();
            int shown = 0;
            for (int i = 0; i < blips.Count; i++)
            {
                // Project to the small-map rect. Off-window points are NO LONGER dropped — they are
                // clamped to the rim and drawn smaller (card t_aab051ae item ④). TryVanillaSmallMapPos
                // returns the raw anchored pos (even out-of-window) plus whether it's inside the uv window.
                if (!TryVanillaSmallMapPos(mm, blips[i].WorldPos, out Vector2 anchored)) continue;

                bool offEdge = Cartography.BoundedMapMath.ClampToRimPx(anchored.x, anchored.y, visibleR, RimInset,
                                                                       out float drawX, out float drawY);
                Vector2 drawAt = new Vector2(drawX, drawY);

                var img = EnsurePoolImage(shown++);
                ClearStarPips(img);                 // pooled image reuse: drop last frame's star row first
                var rt = img.rectTransform;
                rt.anchoredPosition = drawAt;
                float basePx = Plugin.ResolvedMinimapBlipPx;
                float px = offEdge ? basePx * Cartography.MinimapThreatMetrics.RimScale : basePx;
                rt.sizeDelta = new Vector2(px, px);
                img.color = blips[i].Tint;          // OUR colour — no vanilla UpdatePins runs on our layer, so it survives
                img.sprite = style == BlipStyle.Trophy ? (blips[i].Trophy ?? dot) : dot;
                img.gameObject.SetActive(true);

                // Stars: only on in-window blips (a clamped rim indicator stays a clean small dot — no
                // room for pips at the bezel, and the rim blip is a "something's out there" cue, not a
                // full read). Mounted from the shared helper so the disc + vanilla map match exactly.
                if (!offEdge && blips[i].Stars > 0)
                    SunstoneProjection.MountStarPips(rt, blips[i].Stars, px, blips[i].Tint);
            }
            for (int i = shown; i < _pool.Count; i++)
            {
                ClearStarPips(_pool[i]);
                _pool[i].gameObject.SetActive(false);  // park tail
            }
        }

        /// <summary>Destroy any star-pip children mounted on a pooled blip last frame (so reuse doesn't
        /// accumulate rows). The pips are parented under a child named "stars" by MountStarPips.</summary>
        private static void ClearStarPips(Image img)
        {
            if (img == null) return;
            var existing = img.rectTransform.Find("stars");
            if (existing != null) Object.Destroy(existing.gameObject);
        }

        /// <summary>Inscribed-circle radius (px) of the vanilla small-map rect — the visible map window the
        /// rim clamp seats off-edge threats against.</summary>
        private static float VisibleSmallMapRadiusPx(Minimap mm)
        {
            var img = mm.m_mapImageSmall;
            if (img == null || img.rectTransform == null) return 0f;
            Rect rect = img.rectTransform.rect;
            return Mathf.Min(rect.width, rect.height) * 0.5f;
        }

        /// <summary>Hide every blip (called when the vanilla minimap is no longer the active surface).</summary>
        internal void Clear()
        {
            for (int i = 0; i < _pool.Count; i++)
                if (_pool[i] != null) _pool[i].gameObject.SetActive(false);
        }

        private Image EnsurePoolImage(int index)
        {
            while (_pool.Count <= index)
            {
                var go = new GameObject($"threat_{_pool.Count}", typeof(RectTransform));
                go.transform.SetParent(_layer, worldPositionStays: false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                img.preserveAspect = true;
                _pool.Add(img);
            }
            return _pool[index];
        }

        /// <summary>
        /// Project a world position to the vanilla SMALL minimap's local-GUI anchored position,
        /// reproducing vanilla's own math (it's private, but the arithmetic is trivial and fair to
        /// adapt per ADR-0001). The chain mirrors vanilla UpdatePins for the small map:
        ///   WorldToMapPoint:        mx = (p.x/m_pixelSize + textureSize/2) / textureSize ; my likewise on p.z
        ///   MapPointToLocalGuiPos:  ((m - uvMin)/uvW) * rectW  on each axis, about the rect centre
        /// so a blip lands on the exact terrain cell a vanilla pin would. Unlike vanilla pins (which clip
        /// off-window points away), we return the anchored pos EVEN when it falls outside the visible uv
        /// window — the caller's <see cref="Cartography.BoundedMapMath.ClampToRimPx"/> turns an off-window
        /// threat into a smaller rim indicator instead of dropping it (card t_aab051ae item ④). Returns false only
        /// when the map rect genuinely can't be read (no projection possible).
        /// </summary>
        private static bool TryVanillaSmallMapPos(Minimap mm, Vector3 world, out Vector2 anchored)
        {
            anchored = Vector2.zero;
            var img = mm.m_mapImageSmall;
            if (img == null || img.rectTransform == null) return false;

            float pixelSize = mm.m_pixelSize > 0f ? mm.m_pixelSize : 64f;
            int texSize = mm.m_textureSize > 0 ? mm.m_textureSize : 256;

            // WorldToMapPoint (vanilla): world → normalized [0,1] map texture coords (north-up, no rotation).
            float mx = (world.x / pixelSize + texSize / 2f) / texSize;
            float my = (world.z / pixelSize + texSize / 2f) / texSize;

            Rect uv = img.uvRect;
            // MapPointToLocalGuiPos (vanilla): normalize within uv, scale by the rect, centre at (0,0). We do
            // NOT early-out on out-of-uv here (vanilla's IsPointVisible cull) — the rim clamp wants the true
            // projected position so it can seat an off-window threat on the bezel.
            Rect rect = img.rectTransform.rect;
            float nx = (mx - uv.xMin) / uv.width;
            float ny = (my - uv.yMin) / uv.height;
            // Vanilla anchors pins under m_pinRootSmall, which is centred on the map; the GUI pos is
            // measured from the rect centre, so subtract half the rect extent.
            anchored = new Vector2(nx * rect.width - rect.width * 0.5f,
                                   ny * rect.height - rect.height * 0.5f);
            return true;
        }
    }
}
