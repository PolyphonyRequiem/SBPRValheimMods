// ============================================================================
//  Trailborne v2 cartography — windowed-fog cell math (the §2C / §4 shared seam)
// ----------------------------------------------------------------------------
//  Pure, Unity-free geometry for the bounded 1000 m survey. No UnityEngine types,
//  so the arithmetic is trivially testable and reusable by every consumer of the
//  windowed-fog format: the Surveyor's Table (this card), the Local Map snapshot
//  (t_7b616020), and the forked viewer (t_7b616020). One format, defined once
//  (impl spec §4).
//
//  Productionized from the GO-WITH-CAVEATS spike (card t_e8bbbe48,
//  docs/v2/investigations/2026-06-10-bounded-map-ui-fork-spike.md): the spike's
//  BoundedMapMath ran 31/31 executed assertions (over-provisioning fix, vanilla
//  WorldToPixel fidelity, disc clip, world-edge safety, edge-clamp-to-disc). This
//  is the same arithmetic, de-spiked (the throwaway banner + harness are gone).
//
//  Clean-side (ADR-0001): every vanilla constant/formula cited here is the base
//  game (assembly_valheim), which we may read and adapt. Confirmed against the
//  Bog Witch / Ashlands decomp:
//    Minimap.m_textureSize = 256   (:46692, public)
//    Minimap.m_pixelSize   = 64f   (:46694, public)  -> 256*64 = 16384 m world
//    WorldToPixel(p):  px = RoundToInt(p.x/m_pixelSize + m_textureSize/2)  (:47998)
//                      py = RoundToInt(p.z/m_pixelSize + m_textureSize/2)
//    m_explored = new bool[m_textureSize*m_textureSize]                    (:46910)
//    index = py*m_textureSize + px         (Explore(int,int), :48038)
//  RoundToInt == (int)Math.Round(x, ToEven) (banker's rounding) — matched here so
//  our window aligns cell-for-cell with vanilla's fog array.
// ============================================================================

using System;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// Unity-free geometry for the bounded survey disc. All distances in metres,
    /// world coords are the game's (X = east, Z = north). The window is a small
    /// Size×Size sub-rectangle of the full 256² fog grid, centred on a bound origin
    /// and clipped to a radius disc — the over-provisioning fix (≈1089 cells vs
    /// 65536 for R=1000/pixelSize=64, a ~60× shrink at native resolution, no resample).
    /// </summary>
    public static class BoundedMapMath
    {
        /// <summary>The Size×Size window of source cells copied out of the full m_explored array.</summary>
        public struct WindowSpec
        {
            public int CenterX, CenterY;          // source cell of the bound origin
            public int CellRadius;                // ceil(radius / pixelSize)
            public int Size;                      // 2*CellRadius + 1 (window is Size x Size)
            public int OriginCellX, OriginCellY;  // top-left source cell = Center - CellRadius
            public int Cells => Size * Size;
        }

        // ── World <-> cell, faithful to vanilla WorldToPixel / Explore index ────────

        public static int WorldToCellX(float worldX, float pixelSize, int textureSize)
            => (int)Math.Round(worldX / pixelSize + textureSize / 2, MidpointRounding.ToEven);

        public static int WorldToCellY(float worldZ, float pixelSize, int textureSize)
            => (int)Math.Round(worldZ / pixelSize + textureSize / 2, MidpointRounding.ToEven);

        /// <summary>World-space centre (X) of source cell column cellX (inverse of WorldToCellX).</summary>
        public static float CellCenterWorldX(int cellX, float pixelSize, int textureSize)
            => (cellX - textureSize / 2) * pixelSize;

        /// <summary>World-space centre (Z) of source cell row cellY.</summary>
        public static float CellCenterWorldZ(int cellY, float pixelSize, int textureSize)
            => (cellY - textureSize / 2) * pixelSize;

        // ── Windowing (the over-provisioning fix, impl spec §2C) ────────────────────

        /// <summary>
        /// Compute the Size×Size window of cells centred on the bound origin that fully
        /// contains the <paramref name="radiusMeters"/> disc, at the native pixelSize
        /// grid (no resample). For R=1000, pixelSize=64: CellRadius=16, Size=33 → 1089
        /// cells vs the full 65536. That ≈60× shrink is the locked over-provisioning fix.
        /// </summary>
        public static WindowSpec ComputeWindow(float originX, float originZ, float radiusMeters,
                                               float pixelSize, int textureSize)
        {
            int cr = (int)Math.Ceiling(radiusMeters / pixelSize);
            int cx = WorldToCellX(originX, pixelSize, textureSize);
            int cy = WorldToCellY(originZ, pixelSize, textureSize);
            return new WindowSpec
            {
                CenterX = cx,
                CenterY = cy,
                CellRadius = cr,
                Size = 2 * cr + 1,
                OriginCellX = cx - cr,
                OriginCellY = cy - cr,
            };
        }

        /// <summary>
        /// Window the live full-world <paramref name="explored"/> array (length
        /// textureSize²) into a Size×Size bool window, clipped to the radius disc:
        ///   true  = explored AND inside the disc (render lit)
        ///   false = shroud (outside disc, OR unexplored, OR off the world array edge)
        /// Row-major, window index = wy*Size + wx. The disc clip is done in WORLD space
        /// (cell centres) so the boundary is a true radius circle regardless of where the
        /// origin sits inside its cell. Out-params expose the evidence (lit-in-disc count,
        /// total in-disc cells) callers/logs assert on.
        /// </summary>
        public static bool[] BuildWindowedFog(bool[] explored, int textureSize, WindowSpec w,
                                              float originX, float originZ, float radiusMeters,
                                              float pixelSize,
                                              out int exploredInDisc, out int discCells)
        {
            var outBuf = new bool[w.Size * w.Size];
            float r2 = radiusMeters * radiusMeters;
            exploredInDisc = 0;
            discCells = 0;

            for (int wy = 0; wy < w.Size; wy++)
            {
                for (int wx = 0; wx < w.Size; wx++)
                {
                    int srcX = w.OriginCellX + wx;
                    int srcY = w.OriginCellY + wy;
                    int oi = wy * w.Size + wx;

                    // Off the world array (near the map edge) → permanent shroud.
                    if (srcX < 0 || srcY < 0 || srcX >= textureSize || srcY >= textureSize)
                    {
                        outBuf[oi] = false;
                        continue;
                    }

                    float cwx = CellCenterWorldX(srcX, pixelSize, textureSize);
                    float cwz = CellCenterWorldZ(srcY, pixelSize, textureSize);
                    float dx = cwx - originX, dz = cwz - originZ;
                    bool inDisc = (dx * dx + dz * dz) <= r2;
                    if (inDisc) discCells++;

                    bool lit = inDisc && explored[srcY * textureSize + srcX];
                    outBuf[oi] = lit;
                    if (lit) exploredInDisc++;
                }
            }
            return outBuf;
        }

        /// <summary>
        /// True if a world point is inside the radius disc around the bound origin. Used
        /// to clip which pins are stored / removed (pins only live inside the disc).
        /// </summary>
        public static bool InDisc(float px, float pz, float originX, float originZ, float radiusMeters)
        {
            float dx = px - originX, dz = pz - originZ;
            return (dx * dx + dz * dz) <= radiusMeters * radiusMeters;
        }

        // ── Edge indicator: polar clamp to the disc (NOT Hud.ClampToScreenEdge) ──────

        /// <summary>
        /// Project the (possibly off-disc) player position onto the radius circle around
        /// the bound origin, giving the clamped world point + the bearing the arrow points.
        /// This is the map-space clamp the requirements call for; vanilla's
        /// Hud.ClampToScreenEdge (:34731) clamps to Screen.width/height and is the WRONG
        /// precedent. <paramref name="angleDeg"/> is the bearing FROM origin TO player,
        /// 0° = +X (east), CCW positive, matching Atan2(dz, dx). Lives here (the shared
        /// seam) so the viewer card (t_7b616020) consumes the same proven math.
        /// </summary>
        public static void EdgeClampToDisc(float px, float pz, float ox, float oz, float radius,
                                           out float clampedX, out float clampedZ,
                                           out float angleDeg, out bool isOutside, out float distance)
        {
            float dx = px - ox, dz = pz - oz;
            distance = (float)Math.Sqrt(dx * dx + dz * dz);
            angleDeg = (float)(Math.Atan2(dz, dx) * 180.0 / Math.PI);

            if (distance <= radius || distance <= 1e-6f)
            {
                isOutside = distance > radius;
                clampedX = px;
                clampedZ = pz;
                if (distance <= 1e-6f) { clampedX = ox + radius; clampedZ = oz; angleDeg = 0f; }
                return;
            }

            isOutside = true;
            float ux = dx / distance, uz = dz / distance;
            clampedX = ox + ux * radius;
            clampedZ = oz + uz * radius;
        }
    }
}
