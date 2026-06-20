// ============================================================================
//  DiscRingGeometry — xUnit structural tests (§2E.5.7, card t_12e15162).
// ----------------------------------------------------------------------------
//  Tests the SHIPPED pure geometry DiscRingGeometry (link-compiled from ../src,
//  not copied — see the .csproj). This is the durable CI fence for the minimap
//  DISC content-to-ring margin fix: it pins the STABLE radius RELATION that the
//  cartography mesh silhouette (LayoutMapRect → RectEdge/MeshRadius) and the
//  bronze bezel ring (EnsureBezelTexture → HoleRadius/RingOuterRadius) must
//  satisfy on BOTH surfaces, WITHOUT touching any volatile UnityEngine/render
//  internals (the same engine-free link-compile pattern as BoundedMapMath and
//  MapCaptionText).
//
//  THE BUG (§2E.5.7): LayoutMapRect used to size the rect by an INTEGER-FLOORED
//  upscale of the fog grid (edge = size · floor(TargetPx/size) ≤ TargetPx), while
//  the ring hole is sized off the RAW TargetPx. When TargetPx/size isn't integral
//  the floor drops the mesh radius BELOW the ring hole → a transparent annulus
//  opens (the disc has no backdrop, so the live game world showed through). The
//  fix sizes the rect to the full TargetPx square, so meshR = TargetPx/2 for EVERY
//  survey size on both surfaces.
//
//  Pins the §2E.5.7 acceptance behaviour that is unit-checkable headless:
//    • AT-DISC-RING-1 — meshR ≥ holeR at every survey size (content meets ring,
//      no transparent gap) — disc (200) AND modal (900).
//    • AT-DISC-RING-2 — meshR ≤ ringOuterR at every survey size (no #159/issue-6
//      edge-bleed past the outer ring) — disc (200) AND modal (900).
//  (AT-DISC-RING-3 zoom/feel and AT-DISC-RING-4 pin-tracking are guaranteed by
//   construction — uvRect is span-driven and every projection reads `edge` live
//   from the rect — and are Daniel's in-game eyeball; logs-green ≠ playable.)
//
//  Every expected value below is DERIVED from the DiscRingGeometry constants
//  (BezelInsetFrac 6/900, BezelRingFrac 10/900, BezelRingMinPx 4.5) and shown in
//  the comments — none are magic. The swept band (sizes 17..69, pixelSize ~30..130
//  m/px at radius 1000) mirrors scripts/disc-ring-geom-check.py.
// ============================================================================

using System;
using SBPR.Trailborne.Features.Cartography;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class DiscRingGeometryTests
    {
        // The two shipped TargetPx (MapViewer.cs): the corner minimap disc and the
        // full-screen modal. The fix must hold on BOTH (the shared MapSurface builder
        // touches the modal too — sizing the modal rect to 900 makes #204 size-robust).
        private const int DiscTargetPx = 200;
        private const int ModalTargetPx = 900;

        // Float comparison slack. The relations land with ≥1.3 px margin, so this only
        // guards against representation rounding, not against a real regression.
        private const float Eps = 1e-3f;

        // The swept survey-size band. survey.Size = 2·ceil(1000/pixelSize)+1, and the
        // table reads Minimap.m_pixelSize LIVE (not guaranteed 64), so size is NOT fixed.
        // 17..69 step 2 spans pixelSize ≈ 30..130 m/px — the plausible runtime band.
        public static TheoryData<int> SweptSizes()
        {
            var data = new TheoryData<int>();
            for (int size = 17; size <= 69; size += 2) data.Add(size);
            return data;
        }

        // ── Exact anchor values (derived by hand from the constants) ─────────────
        //  DISC 200:  holeR = 200·(0.5 − 6/900)            = 98.6667 px
        //             ringOuterR = holeR + max(200·10/900, 4.5)
        //                        = 98.6667 + max(2.222, 4.5) = 103.1667 px
        //             meshR = 200/2                         = 100.0 px
        //               → gap   = holeR − meshR = −1.333 px (content overdraws into ring) ✓
        //               → bleed = meshR − ringOuterR = −3.167 px (inside outer ring)      ✓
        //  MODAL 900: holeR = 900·(0.5 − 6/900)            = 444.0 px
        //             ringOuterR = 444 + max(900·10/900=10, 4.5) = 454.0 px
        //             meshR = 900/2                         = 450.0 px
        //               → gap   = −6.0 px ✓   bleed = −4.0 px ✓

        [Fact]
        public void Disc_anchor_radii_match_the_hand_derived_values()
        {
            Assert.Equal(98.6667f, DiscRingGeometry.HoleRadius(DiscTargetPx), 3);
            Assert.Equal(103.1667f, DiscRingGeometry.RingOuterRadius(DiscTargetPx), 3);
            Assert.Equal(100.0f, DiscRingGeometry.MeshRadius(DiscTargetPx, 33), 3);
        }

        [Fact]
        public void Modal_anchor_radii_match_the_hand_derived_values()
        {
            Assert.Equal(444.0f, DiscRingGeometry.HoleRadius(ModalTargetPx), 3);
            Assert.Equal(454.0f, DiscRingGeometry.RingOuterRadius(ModalTargetPx), 3);
            Assert.Equal(450.0f, DiscRingGeometry.MeshRadius(ModalTargetPx, 33), 3);
        }

        // ── AT-DISC-RING-1: content meets the ring (meshR ≥ holeR) at every size ──

        [Theory]
        [MemberData(nameof(SweptSizes))]
        public void Disc_meshRadius_meets_or_overdraws_the_ring_hole_at_every_size(int size)
        {
            float meshR = DiscRingGeometry.MeshRadius(DiscTargetPx, size);
            float holeR = DiscRingGeometry.HoleRadius(DiscTargetPx);
            Assert.True(meshR >= holeR - Eps,
                $"AT-DISC-RING-1 (disc {DiscTargetPx}): size {size} meshR={meshR} < holeR={holeR} — transparent annulus.");
        }

        [Theory]
        [MemberData(nameof(SweptSizes))]
        public void Modal_meshRadius_meets_or_overdraws_the_ring_hole_at_every_size(int size)
        {
            float meshR = DiscRingGeometry.MeshRadius(ModalTargetPx, size);
            float holeR = DiscRingGeometry.HoleRadius(ModalTargetPx);
            Assert.True(meshR >= holeR - Eps,
                $"AT-DISC-RING-1 (modal {ModalTargetPx}): size {size} meshR={meshR} < holeR={holeR} — #204 regressed.");
        }

        // ── AT-DISC-RING-2: no bleed past the outer ring (meshR ≤ ringOuterR) ─────

        [Theory]
        [MemberData(nameof(SweptSizes))]
        public void Disc_meshRadius_stays_inside_the_outer_ring_at_every_size(int size)
        {
            float meshR = DiscRingGeometry.MeshRadius(DiscTargetPx, size);
            float ringOuterR = DiscRingGeometry.RingOuterRadius(DiscTargetPx);
            Assert.True(meshR <= ringOuterR + Eps,
                $"AT-DISC-RING-2 (disc {DiscTargetPx}): size {size} meshR={meshR} > ringOuterR={ringOuterR} — #159 edge-bleed.");
        }

        [Theory]
        [MemberData(nameof(SweptSizes))]
        public void Modal_meshRadius_stays_inside_the_outer_ring_at_every_size(int size)
        {
            float meshR = DiscRingGeometry.MeshRadius(ModalTargetPx, size);
            float ringOuterR = DiscRingGeometry.RingOuterRadius(ModalTargetPx);
            Assert.True(meshR <= ringOuterR + Eps,
                $"AT-DISC-RING-2 (modal {ModalTargetPx}): size {size} meshR={meshR} > ringOuterR={ringOuterR} — #159 edge-bleed.");
        }

        // ── The fix is SIZE-INDEPENDENT: meshR is constant across the band ───────
        //  This is the property that closes the gap — the old integer floor made
        //  meshR vary with `size`; the fix pins it to TargetPx/2 regardless.

        [Fact]
        public void Disc_meshRadius_is_constant_across_the_whole_size_band()
        {
            float expected = DiscTargetPx / 2f;
            foreach (int size in new[] { 17, 33, 37, 53, 67, 69 })
                Assert.Equal(expected, DiscRingGeometry.MeshRadius(DiscTargetPx, size), 4);
        }

        // ── Regression DOCUMENTATION: the OLD integer-floored formula DID gap ─────
        //  Re-derive the pre-fix mesh radius locally (the shipped code no longer
        //  contains the floor) and prove it opened a > 4 px transparent annulus at
        //  some size — i.e. this guard would have caught the t_642687dd bug, and the
        //  new RectEdge formula removes it. NOT testing shipped code (the bug is
        //  gone); pinning WHY the fix is shaped the way it is.

        private static float OldFlooredMeshRadius(int targetPx, int size)
        {
            int upscale = Math.Max(1, targetPx / Math.Max(1, size)); // C# int division = FLOOR
            return (size * upscale) / 2f;
        }

        [Fact]
        public void Old_integer_floored_formula_opened_a_visible_disc_gap_that_the_fix_removes()
        {
            float holeR = DiscRingGeometry.HoleRadius(DiscTargetPx);

            // The pre-fix formula gapped at some swept size (e.g. size 67 → meshR 67 → gap +31.7 px).
            float worstOldGap = float.MinValue;
            for (int size = 17; size <= 69; size += 2)
                worstOldGap = Math.Max(worstOldGap, holeR - OldFlooredMeshRadius(DiscTargetPx, size));
            Assert.True(worstOldGap > 4f,
                $"expected the OLD floored formula to leave a > 4 px annulus somewhere; worst gap was {worstOldGap} px.");

            // The fix removes it everywhere (gap ≤ 0 at every size).
            float worstFixedGap = float.MinValue;
            for (int size = 17; size <= 69; size += 2)
                worstFixedGap = Math.Max(worstFixedGap, holeR - DiscRingGeometry.MeshRadius(DiscTargetPx, size));
            Assert.True(worstFixedGap <= Eps,
                $"the fix must close the annulus at every size; worst remaining gap was {worstFixedGap} px.");
        }
    }
}
