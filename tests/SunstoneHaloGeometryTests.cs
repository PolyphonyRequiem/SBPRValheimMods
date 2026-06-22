// ============================================================================
//  SunstoneHaloGeometry — xUnit structural tests (bug-fix card t_10bacccf).
// ----------------------------------------------------------------------------
//  Tests the SHIPPED pure placement/scale math SunstoneHaloGeometry (link-compiled
//  from ../src, not copied — see the .csproj). This is the durable CI fence for the
//  Sunstone Lens halo's FIXED-distance + 10m-scale-knee geometry, WITHOUT touching
//  any volatile UnityEngine/Harmony internals (the engine-free link-compile pattern
//  shared with DiscRingGeometry / BoundedMapMath / LensHandoffDecision).
//
//  WHY THIS MATTERS. PR #242 (card t_d17d9b58) shipped a "variable radius AND scale
//  ∝ distance" halo: a far enemy was pushed to the OUTER radius (away from the eye)
//  AND shrunk toward ~0.12 world-units — far + tiny = invisible. Daniel reported the
//  symptom ("seemingly too far from the player to be clearly visible") and re-locked
//  the geometry: a TRUE fixed-distance ring where SCALE alone carries range, with a
//  10m knee (full ≤10m → 0.25× at the 50m edge). These tests pin both halves so a
//  future edit that re-introduces a distance-varying radius, or moves the 10m knee /
//  0.25 floor / 1.0 ceiling, fails CI instead of shipping the regression again.
//
//  The two named acceptance tests from the card:
//    • AT-HALO-FIXED-DIST  — every trophy renders at the SAME fixed distance from the
//      eye-point regardless of enemy range; only SCALE varies (no outward push).
//    • AT-HALO-SCALE-KNEE  — enemy ≤10m → full scale; enemy at 50m → 25% scale; linear
//      between; the edge trophy is still readable (a quarter-size, never ~nothing).
//
//  Numbers are Daniel-LOCKED (10m knee, 0.25 floor, 1.0 ceiling). scaleNear is the
//  AT-gated eyeball tunable; the tests use representative values and assert the curve
//  SHAPE + the locked anchor points, not a magic absolute world-size.
// ============================================================================

using SBPR.Trailborne.Features.Sunstone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class SunstoneHaloGeometryTests
    {
        // Representative live values: 50m detection range (the shipped DetectRadius default),
        // 0.6 full-scale world-size (the shipped HaloScaleMax default), 2.0m fixed ring radius.
        private const float DetectRadius = 50f;
        private const float ScaleNear    = 0.6f;
        private const float HaloRadius   = 2.0f;

        // ── AT-HALO-FIXED-DIST: placement distance is FIXED for every enemy, near or far ──────────
        //  This is the bug's core: the OLD code lerped radius 1.2→3.0 over 0..50m, pushing far
        //  enemies away from the face. The fix makes Distance == HaloRadius for ALL ranges.

        [Theory]
        [InlineData(0.5f)]
        [InlineData(5f)]
        [InlineData(10f)]
        [InlineData(25f)]
        [InlineData(49.9f)]
        [InlineData(50f)]
        [InlineData(75f)]   // beyond detection range (defensive) — still the fixed ring distance
        public void FixedDist_distance_is_always_the_halo_radius(float dist)
        {
            HaloPlacement p = SunstoneHaloGeometry.Resolve(dist, DetectRadius, HaloRadius, ScaleNear);
            Assert.Equal(HaloRadius, p.Distance, 5);
        }

        [Fact]
        public void FixedDist_a_near_and_a_far_enemy_sit_at_the_same_distance()
        {
            HaloPlacement near = SunstoneHaloGeometry.Resolve(3f,  DetectRadius, HaloRadius, ScaleNear);
            HaloPlacement far  = SunstoneHaloGeometry.Resolve(48f, DetectRadius, HaloRadius, ScaleNear);
            // The whole point of the reversal: no outward push. Same ring, different SIZE only.
            Assert.Equal(near.Distance, far.Distance, 5);
            Assert.True(near.Scale > far.Scale, "near enemy must render bigger than a far one (scale carries range)");
        }

        [Fact]
        public void FixedDist_changing_only_the_radius_moves_every_trophy_together()
        {
            HaloPlacement a = SunstoneHaloGeometry.Resolve(30f, DetectRadius, 2.0f, ScaleNear);
            HaloPlacement b = SunstoneHaloGeometry.Resolve(30f, DetectRadius, 3.5f, ScaleNear);
            Assert.Equal(2.0f, a.Distance, 5);
            Assert.Equal(3.5f, b.Distance, 5);
            // Scale is independent of the placement radius — only range + scaleNear drive it.
            Assert.Equal(a.Scale, b.Scale, 5);
        }

        // ── AT-HALO-SCALE-KNEE: full ≤10m, 0.25× at the 50m edge, linear between ──────────────────

        [Theory]
        [InlineData(0f)]
        [InlineData(2f)]
        [InlineData(9.9f)]
        [InlineData(10f)]   // exactly at the knee → still full
        public void ScaleKnee_within_ten_metres_is_full_scale(float dist)
        {
            float s = SunstoneHaloGeometry.ScaleAt(dist, DetectRadius, ScaleNear);
            Assert.Equal(ScaleNear, s, 5);
        }

        [Fact]
        public void ScaleKnee_at_the_detection_edge_is_a_quarter_scale()
        {
            float s = SunstoneHaloGeometry.ScaleAt(DetectRadius, DetectRadius, ScaleNear);
            Assert.Equal(ScaleNear * 0.25f, s, 5);   // the locked 0.25 floor — the "0.25"
        }

        [Fact]
        public void ScaleKnee_is_linear_between_the_knee_and_the_edge()
        {
            // Midpoint of the 10→50m span is 30m → exactly halfway between full (0.6) and 0.25× (0.15)
            // → 0.375. Pins the linearity Daniel specified.
            float midpoint = SunstoneHaloGeometry.ScaleAt(30f, DetectRadius, ScaleNear);
            float expected = (ScaleNear + ScaleNear * 0.25f) * 0.5f;   // (1.0 + 0.25)/2 × scaleNear = 0.625×
            Assert.Equal(expected, midpoint, 5);
        }

        [Fact]
        public void ScaleKnee_is_monotonically_non_increasing_with_distance()
        {
            // Sweep the full range; scale must never grow as the enemy gets farther (no popping bigger).
            float prev = float.MaxValue;
            for (float d = 0f; d <= DetectRadius; d += 1f)
            {
                float s = SunstoneHaloGeometry.ScaleAt(d, DetectRadius, ScaleNear);
                Assert.True(s <= prev + 1e-6f, $"scale rose at d={d} (prev={prev}, s={s}) — must be non-increasing");
                prev = s;
            }
        }

        [Fact]
        public void ScaleKnee_edge_trophy_is_still_readable_not_shrunk_to_nothing()
        {
            // The bug shrank the edge to ~0.12 world-units (HaloScaleMin); the fix floors it at 0.25×.
            // For the shipped 0.6 scaleNear that's 0.15 — but the INVARIANT is "a quarter of full,"
            // which is a meaningful, readable fraction, never an arbitrary near-zero.
            float edge = SunstoneHaloGeometry.ScaleAt(DetectRadius, DetectRadius, ScaleNear);
            Assert.True(edge >= ScaleNear * 0.25f - 1e-6f, "edge scale must not fall below the 0.25 floor");
            Assert.True(edge > 0f, "edge trophy must be visible");
        }

        // ── Edge / defensive cases ───────────────────────────────────────────────────────────────

        [Fact]
        public void ScaleKnee_beyond_the_detection_edge_clamps_to_the_floor()
        {
            // A defensively-passed dist > DetectRadius (a blip momentarily outside range) clamps to 0.25×,
            // never below — Clamp01 guards the curve so it can't invert past the edge.
            float s = SunstoneHaloGeometry.ScaleAt(75f, DetectRadius, ScaleNear);
            Assert.Equal(ScaleNear * 0.25f, s, 5);
        }

        [Fact]
        public void ScaleKnee_degenerate_detect_radius_inside_the_knee_is_all_full_scale()
        {
            // A fat-fingered tiny DetectRadius (≤ the 10m knee) must not divide by zero — everything
            // sits within the knee, so every trophy is full scale (the guard branch).
            float s = SunstoneHaloGeometry.ScaleAt(5f, 8f, ScaleNear);
            Assert.Equal(ScaleNear, s, 5);
            float sFar = SunstoneHaloGeometry.ScaleAt(8f, 8f, ScaleNear);
            Assert.Equal(ScaleNear, sFar, 5);
        }

        [Fact]
        public void ScaleKnee_scales_proportionally_with_scaleNear()
        {
            // scaleNear is the only eyeball tunable; doubling it doubles every point on the curve
            // (the knee/floor/ceiling are FRACTIONS, not absolutes).
            float a = SunstoneHaloGeometry.ScaleAt(30f, DetectRadius, 0.6f);
            float b = SunstoneHaloGeometry.ScaleAt(30f, DetectRadius, 1.2f);
            Assert.Equal(a * 2f, b, 5);
        }

        [Fact]
        public void Locked_constants_are_the_daniel_numbers()
        {
            // Guard the LOCKED knee + floor so a careless edit to the consts trips CI.
            Assert.Equal(10f,   SunstoneHaloGeometry.FullScaleKnee, 5);
            Assert.Equal(0.25f, SunstoneHaloGeometry.EdgeScaleFactor, 5);
        }
    }
}
