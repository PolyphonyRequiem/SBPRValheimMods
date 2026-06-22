// ============================================================================
//  BoundedMapMath — xUnit structural tests (CLEANUP 3/3, card t_4364809c).
// ----------------------------------------------------------------------------
//  Tests the SHIPPED pure geometry BoundedMapMath (link-compiled from ../src,
//  not copied — see the .csproj). This is exactly the STRUCTURAL, POWERFUL,
//  LOW-VOLATILITY testing the card asks for: it pins the STABLE CONTRACT of the
//  windowed-fog format (vanilla WorldToPixel fidelity, the over-provisioning
//  window, disc clipping, world-edge safety, polar edge-clamp) — the math three
//  cartography consumers depend on — WITHOUT touching any volatile UI/render
//  internals.
//
//  Provenance: BoundedMapMath was productionized from the GO-WITH-CAVEATS spike
//  (card t_e8bbbe48), which ran 31/31 executed assertions over these same
//  categories. The spike harness was throwaway; this suite is the durable,
//  CI-gating re-statement of those invariants against the shipped source.
//
//  Every expected value below is DERIVED from the vanilla constants the file
//  header documents (Minimap.m_textureSize=256, m_pixelSize=64f, banker's-
//  rounding WorldToPixel) or computed by hand in the comments — none are magic.
// ============================================================================

using System;
using SBPR.Trailborne.Features.Cartography;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class BoundedMapMathTests
    {
        // Vanilla Minimap constants (file header, confirmed against assembly_valheim).
        private const float PixelSize = 64f;
        private const int TextureSize = 256;
        private const float Tol = 1e-4f;

        // ── World ⇄ cell fidelity to vanilla WorldToPixel (banker's rounding) ────

        [Fact]
        public void WorldToCell_origin_maps_to_texture_center()
        {
            // px = Round(0/64 + 256/2) = Round(128) = 128.
            Assert.Equal(128, BoundedMapMath.WorldToCellX(0f, PixelSize, TextureSize));
            Assert.Equal(128, BoundedMapMath.WorldToCellY(0f, PixelSize, TextureSize));
        }

        [Fact]
        public void WorldToCell_one_pixel_east_is_one_cell_right()
        {
            // px = Round(64/64 + 128) = Round(129) = 129.
            Assert.Equal(129, BoundedMapMath.WorldToCellX(64f, PixelSize, TextureSize));
        }

        [Theory]
        // Midpoints resolve via banker's rounding (MidpointRounding.ToEven), exactly
        // as Mathf.RoundToInt does, so our window aligns cell-for-cell with vanilla's
        // m_explored array. 32/64=0.5 → 128.5 → 128 (even); 96/64=1.5 → 129.5 → 130 (even).
        [InlineData(32f, 128)]
        [InlineData(96f, 130)]
        [InlineData(-32f, 128)] // -0.5 → 127.5 → 128 (even)
        public void WorldToCell_uses_bankers_rounding_at_midpoints(float worldX, int expectedCell)
        {
            Assert.Equal(expectedCell, BoundedMapMath.WorldToCellX(worldX, PixelSize, TextureSize));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(128)]
        [InlineData(255)]
        public void CellCenter_then_WorldToCell_roundtrips_to_the_same_cell(int cell)
        {
            // CellCenterWorldX is the documented inverse of WorldToCellX at cell centres.
            float wx = BoundedMapMath.CellCenterWorldX(cell, PixelSize, TextureSize);
            Assert.Equal(cell, BoundedMapMath.WorldToCellX(wx, PixelSize, TextureSize));
        }

        // ── The over-provisioning window (impl-spec §2C; the locked ~60× shrink) ─

        [Fact]
        public void ComputeWindow_R1000_is_the_locked_33x33_1089_cell_window()
        {
            // cr = ceil(1000/64) = ceil(15.625) = 16; Size = 33; Cells = 1089
            // vs the full 256² = 65536 (≈60× shrink). This is the locked fix.
            var w = BoundedMapMath.ComputeWindow(0f, 0f, 1000f, PixelSize, TextureSize);
            Assert.Equal(16, w.CellRadius);
            Assert.Equal(33, w.Size);
            Assert.Equal(1089, w.Cells);
            Assert.Equal(128, w.CenterX);
            Assert.Equal(128, w.CenterY);
            Assert.Equal(112, w.OriginCellX); // 128 - 16
            Assert.Equal(112, w.OriginCellY);
        }

        // ── Windowed fog: disc clip, fog-independence, lit ⊆ in-disc ─────────────
        //
        // Controlled tractable grid: textureSize=8, pixelSize=1, origin (0,0),
        // radius 1.5 (r²=2.25). Cell (sx,sy) centre is (sx-4, sy-4); in-disc iff
        // (sx-4)² + (sy-4)² ≤ 2.25. Hand-enumerated, the in-disc set is the 3×3
        // offset block {-1,0,1}² around centre cell 4 → 9 cells. (Offset ±2 gives 4 > 2.25.)

        private const int SmallTex = 8;
        private const float SmallPix = 1f;
        private const float SmallR = 1.5f;
        private const int InteriorDiscCells = 9;

        [Fact]
        public void BuildWindowedFog_disc_geometry_is_independent_of_fog_content()
        {
            var w = BoundedMapMath.ComputeWindow(0f, 0f, SmallR, SmallPix, SmallTex);

            var allLit = new bool[SmallTex * SmallTex];
            Array.Fill(allLit, true);
            BoundedMapMath.BuildWindowedFog(allLit, SmallTex, w, 0f, 0f, SmallR, SmallPix,
                out int litWhenAllExplored, out int discCellsAllLit);

            var noneLit = new bool[SmallTex * SmallTex]; // all false
            BoundedMapMath.BuildWindowedFog(noneLit, SmallTex, w, 0f, 0f, SmallR, SmallPix,
                out int litWhenNoneExplored, out int discCellsNoneLit);

            // discCells is pure geometry — same regardless of what's explored.
            Assert.Equal(InteriorDiscCells, discCellsAllLit);
            Assert.Equal(InteriorDiscCells, discCellsNoneLit);
            // All-explored ⇒ every in-disc cell lit; none-explored ⇒ zero lit.
            Assert.Equal(InteriorDiscCells, litWhenAllExplored);
            Assert.Equal(0, litWhenNoneExplored);
        }

        [Fact]
        public void BuildWindowedFog_never_lights_a_cell_outside_the_disc()
        {
            var w = BoundedMapMath.ComputeWindow(0f, 0f, SmallR, SmallPix, SmallTex);
            var allLit = new bool[SmallTex * SmallTex];
            Array.Fill(allLit, true);

            bool[] fog = BoundedMapMath.BuildWindowedFog(allLit, SmallTex, w, 0f, 0f, SmallR, SmallPix,
                out int exploredInDisc, out _);

            // Window buffer is exactly Size×Size...
            Assert.Equal(w.Size * w.Size, fog.Length);
            // ...and with everything explored, the count of lit window cells equals the
            // in-disc count — i.e. NO out-of-disc cell is ever lit (lit ⊆ in-disc).
            int litCount = 0;
            foreach (bool b in fog) if (b) litCount++;
            Assert.Equal(exploredInDisc, litCount);
            Assert.Equal(InteriorDiscCells, litCount);
        }

        // ── World-edge safety: a window straddling the array edge must not throw and
        //    must shroud the off-array cells (the spike's "world-edge safety" category). ──

        [Fact]
        public void BuildWindowedFog_clips_safely_at_the_world_edge()
        {
            // Origin AT the corner cell (0,0) → world (-4,-4). The radius-1.5 disc and
            // its window straddle the negative array edge. On-array in-disc cells are
            // just (0,0),(0,1),(1,0),(1,1) = 4 (sx²+sy² ≤ 2.25 with sx,sy ≥ 0), so the
            // edge clipping must reduce discCells below the interior 9 — and never throw.
            float cornerWorld = BoundedMapMath.CellCenterWorldX(0, SmallPix, SmallTex); // -4
            var w = BoundedMapMath.ComputeWindow(cornerWorld, cornerWorld, SmallR, SmallPix, SmallTex);

            var allLit = new bool[SmallTex * SmallTex];
            Array.Fill(allLit, true);

            bool[] fog = BoundedMapMath.BuildWindowedFog(
                allLit, SmallTex, w, cornerWorld, cornerWorld, SmallR, SmallPix,
                out int exploredInDisc, out int discCells);

            Assert.Equal(w.Size * w.Size, fog.Length);
            Assert.Equal(4, discCells);                 // hand-enumerated on-array in-disc count
            Assert.True(discCells < InteriorDiscCells);  // edge clipping really removed cells
            Assert.Equal(discCells, exploredInDisc);     // all-explored ⇒ on-array in-disc all lit
        }

        // ── InDisc boundary semantics (≤ radius is inside, used for pin clipping) ─

        [Fact]
        public void InDisc_is_inclusive_at_the_boundary()
        {
            // (3,4) is exactly distance 5 from the origin — a 3-4-5 triangle.
            Assert.True(BoundedMapMath.InDisc(3f, 4f, 0f, 0f, 5f));   // on the circle ⇒ inside
            Assert.True(BoundedMapMath.InDisc(0f, 0f, 0f, 0f, 5f));   // centre ⇒ inside
            Assert.False(BoundedMapMath.InDisc(3f, 4.01f, 0f, 0f, 5f)); // just beyond ⇒ outside
        }

        // ── EdgeClampToDisc: polar clamp onto the radius circle (NOT screen-edge) ─

        [Fact]
        public void EdgeClamp_leaves_an_interior_point_untouched()
        {
            BoundedMapMath.EdgeClampToDisc(1f, 0f, 0f, 0f, 10f,
                out float cx, out float cz, out float _, out bool isOutside, out float dist);
            Assert.False(isOutside);
            Assert.Equal(1f, cx, 4);
            Assert.Equal(0f, cz, 4);
            Assert.Equal(1f, dist, 4);
        }

        [Fact]
        public void EdgeClamp_projects_an_exterior_point_onto_the_circle_east()
        {
            // Player due east, far outside: clamps to (radius, 0), bearing 0° = +X.
            BoundedMapMath.EdgeClampToDisc(100f, 0f, 0f, 0f, 10f,
                out float cx, out float cz, out float angle, out bool isOutside, out float dist);
            Assert.True(isOutside);
            Assert.Equal(10f, cx, 4);
            Assert.Equal(0f, cz, 4);
            Assert.Equal(0f, angle, 3);
            Assert.Equal(100f, dist, 4);
            // The clamped point lies exactly on the radius circle.
            Assert.Equal(10f, (float)Math.Sqrt(cx * cx + cz * cz), 3);
        }

        [Fact]
        public void EdgeClamp_bearing_is_atan2_north_is_90_degrees()
        {
            BoundedMapMath.EdgeClampToDisc(0f, 100f, 0f, 0f, 10f,
                out float cx, out float cz, out float angle, out _, out _);
            Assert.Equal(0f, cx, 3);
            Assert.Equal(10f, cz, 4);
            Assert.Equal(90f, angle, 3); // atan2(dz,dx), +Z (north) = 90°
        }

        [Fact]
        public void EdgeClamp_degenerate_origin_point_is_defined_not_NaN()
        {
            // Player exactly on the bound origin: the 1e-6 guard picks a stable
            // (ox+radius, oz) clamp with bearing 0 instead of dividing by zero.
            BoundedMapMath.EdgeClampToDisc(5f, 5f, 5f, 5f, 10f,
                out float cx, out float cz, out float angle, out bool isOutside, out float dist);
            Assert.False(isOutside);
            Assert.Equal(15f, cx, 4); // ox + radius
            Assert.Equal(5f, cz, 4);
            Assert.Equal(0f, angle, 4);
            Assert.Equal(0f, dist, 4);
            Assert.False(float.IsNaN(angle));
        }

        // ── ClampToRimPx: UI-space rim clamp for off-edge threat blips (t_aab051ae) ─

        [Fact]
        public void ClampToRimPx_interior_point_draws_in_place_not_offedge()
        {
            // A point well inside the visible radius is returned unchanged, offEdge=false.
            bool off = BoundedMapMath.ClampToRimPx(10f, 0f, 100f, 0.92f, out float cx, out float cy);
            Assert.False(off);
            Assert.Equal(10f, cx, 4);
            Assert.Equal(0f, cy, 4);
        }

        [Fact]
        public void ClampToRimPx_point_on_circle_is_not_offedge()
        {
            // mag == radius is treated as on-map (<=), so a blip exactly at the edge stays full-size.
            bool off = BoundedMapMath.ClampToRimPx(100f, 0f, 100f, 0.92f, out _, out _);
            Assert.False(off);
        }

        [Fact]
        public void ClampToRimPx_exterior_point_clamps_to_rim_inset_and_reports_offedge()
        {
            // Due-east at 500px, visible radius 100, inset 0.92 → clamps to (92, 0), offEdge=true.
            bool off = BoundedMapMath.ClampToRimPx(500f, 0f, 100f, 0.92f, out float cx, out float cy);
            Assert.True(off);
            Assert.Equal(92f, cx, 3);
            Assert.Equal(0f, cy, 3);
            // The clamped point sits on the inset circle (preserves the bearing).
            Assert.Equal(92f, (float)Math.Sqrt(cx * cx + cy * cy), 3);
        }

        [Fact]
        public void ClampToRimPx_preserves_bearing_diagonal()
        {
            // A 3-4-5 direction scaled out (300,400)=dist 500, radius 100, inset 0.9 → magnitude 90,
            // same unit direction (0.6, 0.8) → (54, 72).
            bool off = BoundedMapMath.ClampToRimPx(300f, 400f, 100f, 0.9f, out float cx, out float cy);
            Assert.True(off);
            Assert.Equal(54f, cx, 3);
            Assert.Equal(72f, cy, 3);
            Assert.Equal(90f, (float)Math.Sqrt(cx * cx + cy * cy), 3);
        }

        [Fact]
        public void ClampToRimPx_degenerate_center_point_is_defined_not_NaN()
        {
            // A blip exactly at the centre (mag 0) can't be projected onto a rim direction; the 1e-4
            // guard returns it in place, offEdge=false, no divide-by-zero.
            bool off = BoundedMapMath.ClampToRimPx(0f, 0f, 100f, 0.92f, out float cx, out float cy);
            Assert.False(off);
            Assert.Equal(0f, cx, 4);
            Assert.Equal(0f, cy, 4);
            Assert.False(float.IsNaN(cx));
            Assert.False(float.IsNaN(cy));
        }
    }
}
