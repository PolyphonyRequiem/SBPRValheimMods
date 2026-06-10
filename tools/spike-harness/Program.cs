// ============================================================================
//  SPIKE harness (throwaway — card t_e8bbbe48). Branch-only; never merged to v1.
// ----------------------------------------------------------------------------
//  EXECUTES the same BoundedMapMath.cs the in-game viewer compiles, with hard
//  assertions, so the spike's risk-bearing arithmetic is proven by running — not
//  merely by compiling. Exit code 0 = all proofs pass; nonzero = a proof failed.
//
//  Proves, against vanilla ground truth (m_textureSize=256, m_pixelSize=64f):
//    P1  Over-provisioning fix: 1000 m window = 33x33 = 1089 cells (vs 65536) ~60x.
//    P2  World->cell math is byte-faithful to vanilla WorldToPixel + round-trips.
//    P3  Windowing copies the right source cells; disc-clip lights only explored
//        cells inside the 1000 m circle.
//    P4  Disc containment: the window fully contains the 1000 m disc (no in-disc
//        cell is clipped by the window edge) — every in-disc cell in the FULL grid
//        is accounted for by the windowed pass.
//    P5  Disc boundary: a cell center just inside 1000 m lights; just outside shrouds.
//    P6  Edge-clamp-to-disc: player outside -> clamped point lies exactly on the
//        1000 m circle with correct bearing; player inside -> no clamp.
//    P7  Origin at the world edge: off-array cells shroud cleanly, no crash / OOB.
// ============================================================================

using System;
using SBPR.Trailborne.Features.CartographySpike;

namespace SBPR.Trailborne.SpikeHarness
{
    internal static class Program
    {
        private const int   TextureSize = 256;   // vanilla Minimap.m_textureSize
        private const float PixelSize   = 64f;    // vanilla Minimap.m_pixelSize
        private const float Radius      = 1000f;  // v2 lock

        private static int _checks;
        private static int _fails;

        private static int Main()
        {
            Console.WriteLine("=== SBPR v2 bounded map-UI spike — math proof harness ===");
            Console.WriteLine($"world = {TextureSize}*{PixelSize} = {TextureSize * PixelSize} m | disc radius = {Radius} m\n");

            P1_OverProvisioning();
            P2_WorldToCellFidelity();
            P3_WindowingAndClip();
            P4_DiscContainment();
            P5_DiscBoundary();
            P6_EdgeClamp();
            P7_WorldEdgeOrigin();

            Console.WriteLine($"\n=== {_checks - _fails}/{_checks} checks passed ===");
            if (_fails == 0) Console.WriteLine("RESULT: ALL SPIKE MATH PROOFS PASS");
            else             Console.WriteLine($"RESULT: {_fails} PROOF(S) FAILED");
            return _fails == 0 ? 0 : 1;
        }

        // ── P1 ──────────────────────────────────────────────────────────────────────
        private static void P1_OverProvisioning()
        {
            Section("P1  Over-provisioning fix (the core ask)");
            var w = BoundedMapMath.ComputeWindow(0f, 0f, Radius, PixelSize, TextureSize);
            Check("cell radius = ceil(1000/64) = 16", w.CellRadius == 16, w.CellRadius);
            Check("window is 33x33", w.Size == 33, w.Size);
            Check("window cells = 1089", w.Cells == 1089, w.Cells);
            int full = TextureSize * TextureSize;
            double shrink = (double)full / w.Cells;
            Check("full world array = 65536 cells", full == 65536, full);
            Check("shrink factor > 55x (we keep ~1.7% of the array)", shrink > 55.0, $"{shrink:F1}x");
            Console.WriteLine($"     -> window {w.Size}x{w.Size}={w.Cells} vs full {full}  ({shrink:F1}x smaller)");
        }

        // ── P2 ──────────────────────────────────────────────────────────────────────
        private static void P2_WorldToCellFidelity()
        {
            Section("P2  World->cell faithful to vanilla WorldToPixel + round-trip");

            // Hand-computed against vanilla: px = round(x/64 + 128).
            Check("world(0,0)      -> cell(128,128)",
                BoundedMapMath.WorldToCellX(0f, PixelSize, TextureSize) == 128 &&
                BoundedMapMath.WorldToCellY(0f, PixelSize, TextureSize) == 128, "ok");
            Check("world(320,-640) -> cell(133,118)",
                BoundedMapMath.WorldToCellX(320f, PixelSize, TextureSize) == 133 &&
                BoundedMapMath.WorldToCellY(-640f, PixelSize, TextureSize) == 118, "ok");

            // Cell-center round-trip: world->cell->world-center->cell is stable.
            int n = 0;
            for (float x = -8000; x <= 8000; x += 64f)
            {
                int cx = BoundedMapMath.WorldToCellX(x, PixelSize, TextureSize);
                float back = BoundedMapMath.CellCenterWorldX(cx, PixelSize, TextureSize);
                int cx2 = BoundedMapMath.WorldToCellX(back, PixelSize, TextureSize);
                if (cx != cx2) { n++; }
            }
            Check("cell-center round-trip stable across the world span", n == 0, $"{n} mismatches");
        }

        // ── P3 ──────────────────────────────────────────────────────────────────────
        private static void P3_WindowingAndClip()
        {
            Section("P3  Windowing copies right cells; disc-clip lights only explored-in-disc");

            // Synthetic personal fog: mark a square of half-extent 600 m (explored) around
            // origin (0,0) — i.e. round(600/64)=9 cells each way => 19x19=361 cells, far
            // corner = sqrt(576^2+576^2)=815 m < 1000 so the WHOLE square is in-disc — PLUS a
            // far-away patch at (5000,5000) that must NOT leak into the window.
            var explored = new bool[TextureSize * TextureSize];
            MarkExploredSquare(explored, 0f, 0f, 600f);       // half-extent 600 m, inside the disc
            MarkExploredSquare(explored, 5000f, 5000f, 200f);  // far outside -> must be ignored

            var w = BoundedMapMath.ComputeWindow(0f, 0f, Radius, PixelSize, TextureSize);
            byte[] fog = BoundedMapMath.BuildWindowedFog(
                explored, TextureSize, w, 0f, 0f, Radius, PixelSize,
                out int exploredInDisc, out int discCells, out int copiedFromSource);

            Check("output buffer length = window cells", fog.Length == w.Cells, fog.Length);
            Check("some cells lit (explored square is inside the disc)", exploredInDisc > 0, exploredInDisc);
            Check("far patch did NOT leak: copiedFromSource == exploredInDisc",
                copiedFromSource == exploredInDisc, $"copied={copiedFromSource} lit={exploredInDisc}");
            // half-extent 600 m => round(600/64)=9 cells each way => 19x19 = 361 cells,
            // all in-disc (far corner 815 m < 1000). Exact, not a band.
            Check("lit-cell count = full 19x19 square (361), all in-disc",
                exploredInDisc == 361, exploredInDisc);
            Check("lit <= disc cells (clip is a subset of the disc)", exploredInDisc <= discCells, $"{exploredInDisc}<= {discCells}");
            Console.WriteLine($"     -> discCells={discCells} exploredInDisc={exploredInDisc} copiedFromSource={copiedFromSource}");
        }

        // ── P4 ──────────────────────────────────────────────────────────────────────
        private static void P4_DiscContainment()
        {
            Section("P4  Window fully contains the 1000 m disc (no in-disc cell clipped)");

            // Count in-disc cells over the FULL grid; compare to the windowed pass's discCells.
            const float ox = 0f, oz = 0f;
            int fullDisc = 0;
            float r2 = Radius * Radius;
            for (int y = 0; y < TextureSize; y++)
                for (int x = 0; x < TextureSize; x++)
                {
                    float cwx = BoundedMapMath.CellCenterWorldX(x, PixelSize, TextureSize);
                    float cwz = BoundedMapMath.CellCenterWorldZ(y, PixelSize, TextureSize);
                    float dx = cwx - ox, dz = cwz - oz;
                    if (dx * dx + dz * dz <= r2) fullDisc++;
                }

            var allExplored = new bool[TextureSize * TextureSize];
            for (int i = 0; i < allExplored.Length; i++) allExplored[i] = true;
            var w = BoundedMapMath.ComputeWindow(ox, oz, Radius, PixelSize, TextureSize);
            BoundedMapMath.BuildWindowedFog(allExplored, TextureSize, w, ox, oz, Radius, PixelSize,
                out int _, out int windowDisc, out int _);

            Check("full-grid disc cells == windowed disc cells (nothing clipped)",
                fullDisc == windowDisc, $"full={fullDisc} window={windowDisc}");
            // sanity: area of disc ~ pi*(1000/64)^2 ~= 767 cells
            Check("disc cell count near analytic pi*r^2 (~767)",
                Math.Abs(windowDisc - 767) <= 25, windowDisc);
            Console.WriteLine($"     -> disc cells (full grid) = {fullDisc}, (windowed) = {windowDisc}");
        }

        // ── P5 ──────────────────────────────────────────────────────────────────────
        private static void P5_DiscBoundary()
        {
            Section("P5  Disc boundary lights inside, shrouds outside");

            // Place the origin at a cell center so we can reason precisely. Origin (0,0)
            // is cell (128,128) center. Cell at +15 columns east = world x = 960 m
            // (inside 1000). Cell at +16 columns = 1024 m (outside 1000).
            var explored = new bool[TextureSize * TextureSize];
            for (int i = 0; i < explored.Length; i++) explored[i] = true; // everything explored

            var w = BoundedMapMath.ComputeWindow(0f, 0f, Radius, PixelSize, TextureSize);
            byte[] fog = BoundedMapMath.BuildWindowedFog(explored, TextureSize, w, 0f, 0f, Radius, PixelSize,
                out int _, out int _, out int _);

            // window-local index of the origin row (wy = CellRadius), columns at +15 / +16.
            int row = w.CellRadius;          // same row as origin
            int inCol  = w.CellRadius + 15;  // 960 m east -> inside
            int outCol = w.CellRadius + 16;  // 1024 m east -> outside
            byte litInside  = fog[row * w.Size + inCol];
            byte litOutside = fog[row * w.Size + outCol];
            Check("cell at 960 m (inside 1000) is LIT",   litInside == 1, litInside);
            Check("cell at 1024 m (outside 1000) is SHROUD", litOutside == 0, litOutside);
        }

        // ── P6 ──────────────────────────────────────────────────────────────────────
        private static void P6_EdgeClamp()
        {
            Section("P6  Edge indicator: polar clamp to the disc (NOT screen edge)");

            // Player due east, far outside.
            BoundedMapMath.EdgeClampToDisc(2000f, 0f, 0f, 0f, Radius,
                out float cx, out float cz, out float ang, out bool outside, out float dist);
            Check("outside flagged", outside, outside);
            Check("clamped point lies ON the 1000 m circle",
                Approx(MathF.Sqrt(cx * cx + cz * cz), Radius, 0.01f), $"r={MathF.Sqrt(cx*cx+cz*cz):F3}");
            Check("bearing due east = 0deg", Approx(ang, 0f, 0.01f), $"{ang:F2}");
            Check("distance reported = 2000", Approx(dist, 2000f, 0.01f), $"{dist:F1}");

            // Diagonal 3-4-5: player (3000,4000), clamp to (600,800).
            BoundedMapMath.EdgeClampToDisc(3000f, 4000f, 0f, 0f, Radius,
                out float dx2, out float dz2, out float ang2, out bool out2, out float _);
            Check("3-4-5 clamp -> (600,800)", Approx(dx2, 600f, 0.01f) && Approx(dz2, 800f, 0.01f),
                $"({dx2:F1},{dz2:F1})");
            Check("3-4-5 bearing ~ 53.13deg", Approx(ang2, 53.13f, 0.05f), $"{ang2:F2}");

            // Player INSIDE the disc -> no clamp.
            BoundedMapMath.EdgeClampToDisc(0f, 500f, 0f, 0f, Radius,
                out float ix, out float iz, out float _, out bool insideOut, out float idist);
            Check("inside the disc -> not flagged outside", !insideOut, insideOut);
            Check("inside -> point unchanged (no clamp)", Approx(ix, 0f, 0.001f) && Approx(iz, 500f, 0.001f),
                $"({ix:F1},{iz:F1})");
            Check("inside distance reported = 500", Approx(idist, 500f, 0.01f), $"{idist:F1}");

            // Boundary case: exactly on the circle is treated as inside (not clamped further).
            BoundedMapMath.EdgeClampToDisc(1000f, 0f, 0f, 0f, Radius,
                out float _, out float _, out float _, out bool boundOut, out float _);
            Check("exactly on the circle -> not flagged outside", !boundOut, boundOut);
        }

        // ── P7 ──────────────────────────────────────────────────────────────────────
        private static void P7_WorldEdgeOrigin()
        {
            Section("P7  Origin at the world edge: off-array cells shroud, no crash/OOB");

            // Origin near the +X world edge (x ~ 8100, world half-extent = 8192). Part of
            // the 1000 m window falls OFF the 256x256 array; those cells must shroud, and
            // BuildWindowedFog must not throw / index out of range.
            float ox = 8100f, oz = 0f;
            var explored = new bool[TextureSize * TextureSize];
            for (int i = 0; i < explored.Length; i++) explored[i] = true;

            var w = BoundedMapMath.ComputeWindow(ox, oz, Radius, PixelSize, TextureSize);
            bool threw = false;
            byte[] fog = Array.Empty<byte>();
            int discCells = 0, exploredInDisc = 0;
            try
            {
                fog = BoundedMapMath.BuildWindowedFog(explored, TextureSize, w, ox, oz, Radius, PixelSize,
                    out exploredInDisc, out discCells, out int _);
            }
            catch (Exception e) { threw = true; Console.WriteLine($"     EXCEPTION: {e.Message}"); }

            Check("no exception when window overruns the world array", !threw, threw ? "threw" : "clean");
            Check("output sized to window", fog.Length == w.Cells, fog.Length);
            // The east half of the disc is off-array (beyond x=8192), so lit < a centred disc.
            Check("some cells lit (west half on-array)", exploredInDisc > 0, exploredInDisc);
            Check("lit strictly less than a fully-on-array disc (~767)", exploredInDisc < 767, exploredInDisc);
            Console.WriteLine($"     -> edge origin discCells={discCells} exploredInDisc={exploredInDisc} (east half shrouded by world edge)");
        }

        // ── helpers ───────────────────────────────────────────────────────────────
        private static void MarkExploredSquare(bool[] explored, float cxWorld, float czWorld, float halfMeters)
        {
            int cx = BoundedMapMath.WorldToCellX(cxWorld, PixelSize, TextureSize);
            int cy = BoundedMapMath.WorldToCellY(czWorld, PixelSize, TextureSize);
            int rad = (int)Math.Round(halfMeters / PixelSize);
            for (int y = cy - rad; y <= cy + rad; y++)
                for (int x = cx - rad; x <= cx + rad; x++)
                    if (x >= 0 && y >= 0 && x < TextureSize && y < TextureSize)
                        explored[y * TextureSize + x] = true;
        }

        private static bool Approx(float a, float b, float eps) => Math.Abs(a - b) <= eps;

        private static void Section(string title)
        {
            Console.WriteLine($"\n-- {title}");
        }

        private static void Check(string label, bool ok, object detail)
        {
            _checks++;
            if (!ok) _fails++;
            Console.WriteLine($"   [{(ok ? "PASS" : "FAIL")}] {label}  ({detail})");
        }
    }
}
