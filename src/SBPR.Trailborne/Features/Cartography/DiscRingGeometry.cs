// ============================================================================
//  Trailborne v2 cartography — disc/ring radius geometry (the §2E.5.7 seam)
// ----------------------------------------------------------------------------
//  Pure, Unity-free radius arithmetic shared by the TWO writers that used to
//  size the cartography mesh and the bronze bezel ring INDEPENDENTLY and could
//  therefore drift apart:
//
//    • MapSurface.LayoutMapRect      — sizes the on-screen cartography rect, and
//      thus the CircularRawImage inscribed-disc mesh silhouette (meshR = edge/2).
//    • MapSurface.EnsureBezelTexture — sizes the ring hole (holeR) + outer edge
//      (ringOuterR) off the raw TargetPx.
//
//  THE BUG THIS FILE FENCES (§2E.5.7, card t_642687dd). LayoutMapRect used to
//  size the rect by an INTEGER-FLOORED upscale of the fog grid:
//      upscale = max(1, TargetPx / size)   // C# int division → FLOOR
//      edge    = size * upscale            // ≤ TargetPx, often strictly <
//  while the ring hole is sized off the RAW TargetPx. When TargetPx/size is not
//  integral, edge < TargetPx ⇒ meshR < holeR ⇒ a transparent annulus opens
//  between the cartography content and the ring. The corner minimap disc has no
//  backdrop, so that gap showed the live game world through it. At the 900 px
//  modal the 1-px floor slack is invisible; at the 200 px disc the same `size`
//  sheds 5–32 px of a ~99 px radius (only survey.Size=33 was lucky-clean).
//
//  THE FIX (LOCKED approach A — size-independent). Size the rect to the FULL
//  TargetPx square (RectEdge below), so meshR = TargetPx/2 for EVERY survey size
//  on BOTH surfaces — just OUTSIDE holeR (covered by the ring's inner edge, the
//  same intended content-under-ring overdraw §2E.5.6 already accepts) and inside
//  ringOuterR (no #159/issue-6 edge-bleed). The fog TEXTURE still upscales by the
//  integer factor (a separate Texture2D/SetPixels concern, untouched); only the
//  rect SILHOUETTE decouples from it. CircularRawImage samples uvRect per-vertex,
//  so the framed/zoomed cartography is identical regardless of rect px size —
//  the zoom/feel does not change (AT-DISC-RING-3).
//
//  WHY A SHARED HELPER. With both writers consuming THIS one source of truth, the
//  rect sizer and the bezel sizer can never drift again, and the radius relation
//  meshR ≥ holeR ∧ meshR ≤ ringOuterR becomes CI-gated by an engine-free xUnit
//  test (tests/DiscRingGeometryTests.cs) — AT-DISC-RING-1/2. Mirrors the
//  MapCaptionText / BoundedMapMath link-compile precedent: deliberately references
//  no UnityEngine / Valheim type so it compiles under net8.0 in the test project
//  AND under net48 in the mod. scripts/disc-ring-geom-check.py is the human-
//  readable derivation of the same relation; the xUnit test is the durable fence.
//
//  Clean-side (ADR-0001): all SBPR-authored cartography UI geometry; no vanilla
//  or third-party UI code involved.
// ============================================================================

using System;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// Single source of truth for the minimap/modal disc-vs-ring radius arithmetic
    /// (§2E.5.7). Both <c>MapSurface.LayoutMapRect</c> (rect/mesh) and
    /// <c>MapSurface.EnsureBezelTexture</c> (ring) consume these so the mesh
    /// silhouette and the bronze ring are sized from ONE formula set and cannot
    /// drift apart.
    /// </summary>
    public static class DiscRingGeometry
    {
        // ── #159 clip geometry as FRACTIONS of the target edge, NOT absolute px ──
        // 6/900 and 10/900 reproduce the modal's playtested look exactly; the disc
        // (TargetPx ~200) scales the inset/ring down with it instead of swallowing
        // a small disc (§4.2). Moved here from MapSurface so the bezel builder, the
        // rect sizer, and the headless guard all read the SAME constants.

        /// <summary>Bezel inner-hole inset as a fraction of TargetPx (§4.2).</summary>
        public const float BezelInsetFrac = 6f / 900f;

        /// <summary>Bronze ring width as a fraction of TargetPx (§4.2).</summary>
        public const float BezelRingFrac = 10f / 900f;

        /// <summary>
        /// Absolute minimum ring width (px). §2E.5: with the bezel transparent
        /// outside the ring, the ring is the ONLY disc edge — the pure 10/900
        /// fraction gives the 200 px disc a ~2.2 px thread that reads as weak.
        /// Floor it so the small disc keeps a legible bronze edge; the 900 px modal
        /// already exceeds the floor (its 10 px ring is byte-preserved).
        /// </summary>
        public const float BezelRingMinPx = 4.5f;

        /// <summary>
        /// §2E.5.7 approach A — the on-screen cartography rect EDGE (square side, px).
        /// Sized to the FULL <paramref name="targetPx"/>, decoupled from the integer
        /// fog-grid upscale that used to floor it below TargetPx. This is the ONE
        /// number <c>LayoutMapRect</c> writes into <c>_mapRect.sizeDelta</c>; every
        /// projection (pins, player marker, the <c>discR = edge*0.5</c> clip, the
        /// cursor inverse) reads <c>edge</c> live from the rect and pairs it with
        /// <c>DisplayedSpanMeters</c>, so resizing the rect rescales the whole set
        /// uniformly — pins/marker/cursor stay glued to terrain (AT-DISC-RING-4).
        /// Do NOT hard-code a second edge literal anywhere; this is the single source.
        /// </summary>
        public static float RectEdge(int targetPx) => targetPx;

        /// <summary>
        /// Content silhouette radius = inscribed circle of the rect = <c>RectEdge/2</c>.
        /// SIZE-INDEPENDENT by construction post-fix — that independence IS the fix
        /// (the integer floor on <paramref name="size"/> is what opened the gap). The
        /// <paramref name="size"/> parameter is retained so the swept-size guard reads
        /// naturally and documents that the result no longer depends on survey size.
        /// </summary>
        public static float MeshRadius(int targetPx, int size) => RectEdge(targetPx) * 0.5f;

        /// <summary>
        /// Bronze bezel ring inner-hole radius (px) — inside it is transparent so the
        /// circular cartography shows through. Sized off the raw <paramref name="targetPx"/>.
        /// </summary>
        public static float HoleRadius(int targetPx) => targetPx * (0.5f - BezelInsetFrac);

        /// <summary>
        /// Bronze bezel ring OUTER radius (px) — beyond it is transparent (the world
        /// shows through outside the disc). <c>HoleRadius + max(TargetPx·BezelRingFrac,
        /// BezelRingMinPx)</c>.
        /// </summary>
        public static float RingOuterRadius(int targetPx)
            => HoleRadius(targetPx) + Math.Max(targetPx * BezelRingFrac, BezelRingMinPx);
    }
}
