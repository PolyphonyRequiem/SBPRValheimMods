// ============================================================================
//  Trailborne v2 cartography — CircularRawImage (the §2E.5 geometry clip)
// ----------------------------------------------------------------------------
//  A RawImage that tessellates its quad into a DISC (triangle fan) instead of a
//  rectangle, honouring uvRect so the framed texture still maps 1:1. Because the
//  four corners carry NO geometry, they emit no fragments — the corners are
//  transparent for free, regardless of the material bound (the cloned vanilla map
//  shader does NOT support a uGUI stencil/RectMask2D, so a mask-based clip would
//  be silently ignored; a geometry clip is material-agnostic).
//
//  This is the load-bearing fix for the §2E.5 "diamond / square-doesn't-fit-circle"
//  corner-gap defect (cards t_a39d3e5f disc + t_39324b99 modal): the §2H.1
//  inscribed-circle guarantee ("rotating the interior never uncovers a corner")
//  becomes TRUE BY CONSTRUCTION — a disc silhouette is rotation-invariant, so the
//  rotating cartography can never reveal a square corner. The bronze bezel ring is
//  drawn ON the disc edge by EnsureBezelTexture; outside the ring is transparent.
//
//  Clean-side (ADR-0001): this is our own uGUI mesh (the SignPaintPanel idiom).
//  Vanilla has no round map to copy — the disc is an SBPR design element (§2H.1),
//  so the clip is authored from scratch, not adapted from base-game UI.
//
//  NOTE (headless caveat): the GPU shader's *appearance* (the composited fog
//  cloud) cannot be judged on the CI box or this Intel-iGPU workstation — only on
//  a real GPU client (Daniel's RTX / Prime). What IS verifiable headlessly is the
//  geometry: vertex/triangle counts, the disc silhouette, and the uvRect mapping.
// ============================================================================

using UnityEngine;
using UnityEngine.UI;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// RawImage whose mesh is an inscribed disc (triangle fan) rather than a quad. The disc
    /// fills the rect's inscribed circle (radius = half the shorter edge); the rect corners
    /// emit no geometry and are therefore transparent. uvRect is honoured per-vertex so the
    /// framed/zoomed texture (vanilla map shader or the 2-colour fallback) maps exactly as it
    /// would on the quad — only the silhouette changes, not the sampling.
    /// </summary>
    public sealed class CircularRawImage : RawImage
    {
        // Fan resolution. 128 segments is sub-pixel-smooth at the 900 px modal and trivially
        // so at the ~200 px disc; the bronze bezel ring covers the faceting at the very edge.
        private const int Segments = 128;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect r = GetPixelAdjustedRect();
            float cx = r.x + r.width * 0.5f;
            float cy = r.y + r.height * 0.5f;
            float radX = r.width * 0.5f;
            float radY = r.height * 0.5f;

            Rect uv = uvRect;
            float uCx = uv.x + uv.width * 0.5f;
            float vCy = uv.y + uv.height * 0.5f;
            float uHalf = uv.width * 0.5f;
            float vHalf = uv.height * 0.5f;

            Color32 col = color;

            // Centre vertex (index 0) at the rect centre, sampling the uvRect centre.
            vh.AddVert(new Vector3(cx, cy, 0f), col, new Vector2(uCx, vCy));

            // Ring vertices 1..Segments+1 (the last duplicates the first to close the fan).
            for (int i = 0; i <= Segments; i++)
            {
                float a = (float)i / Segments * Mathf.PI * 2f;
                float dx = Mathf.Cos(a);
                float dy = Mathf.Sin(a);
                var pos = new Vector3(cx + dx * radX, cy + dy * radY, 0f);
                var texc = new Vector2(uCx + dx * uHalf, vCy + dy * vHalf);
                vh.AddVert(pos, col, texc);
            }

            for (int i = 1; i <= Segments; i++)
                vh.AddTriangle(0, i, i + 1);
        }
    }
}
