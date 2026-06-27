// ============================================================================
//  TwistedPortalLabelScale — xUnit structural tests (FIX card t_f66a3e37, AT-LABEL-SCALE-MATH).
// ----------------------------------------------------------------------------
//  Tests the SHIPPED pure distance→world-scale math TwistedPortalLabelScale (link-compiled
//  from ../src, not copied — see the .csproj). This is the durable CI fence for FIX 3c: the
//  Route-B Twisted Portal labels must hold ~CONSTANT ON-SCREEN size across the overlay range
//  (clamped near/far) instead of shrinking with raw perspective. Same engine-free link-compile
//  pattern as SunstoneHaloGeometry / DiscRingGeometry / LensHandoffDecision — no UnityEngine /
//  Harmony internals touched.
//
//  WHY THIS MATTERS. Daniel's 2026-06-26 Niflheim playtest reported the labels shrank with
//  distance (sub-pixel at the overlay edge → unreadable). The architect (t_f739451f) locked
//  CONSTANT ON-SCREEN size: the world-scale multiplier rises in proportion to camera distance
//  (mul = camDist / refDist) so the on-screen (angular) size stays put, clamped to [minMul,
//  maxMul]. These tests pin the invariants so a future edit that re-introduces raw-perspective
//  shrink (a flat or shrinking multiplier) fails CI instead of shipping the regression again.
//
//  The named acceptance test from the card:
//    • AT-LABEL-SCALE-MATH — ScaleMul matches the policy in a link-compiled unit table:
//      result clamped to [minMul,maxMul]; never ≤ 0; monotonic-in-camDist within the band;
//      constant-angular (mul / camDist constant) inside the band; KneeFloor full ≤ knee and
//      == floor at the overlay radius. The SunstoneHaloGeometry precedent.
// ============================================================================

using SBPR.Trailborne.Features.Portals;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class TwistedPortalLabelScaleTests
    {
        // Representative live values (the shipped TwistedPortalLabelScale.Default* consts).
        private const float RefDist  = 24f;
        private const float MinMul   = 0.5f;
        private const float MaxMul   = 6f;
        private const float Radius   = 300f;   // overlay radius (KneeFloor edge)
        private const float Knee     = 10f;
        private const float Floor    = 0.6f;

        private static float Mul(LabelScaleMode mode, float camDist)
            => TwistedPortalLabelScale.ScaleMul(mode, camDist, RefDist, MinMul, MaxMul, Radius, Knee, Floor);

        // ════════════════════════════════════════════════════════════════════════════════════════
        //  ConstantOnScreen (default) — the headline: ~constant on-screen size, clamped near/far.
        // ════════════════════════════════════════════════════════════════════════════════════════

        // ── At refDist the multiplier is exactly 1 (the AUTHORED world size) ───────────────────────
        [Fact]
        public void Constant_at_refDist_multiplier_is_one()
        {
            float m = Mul(LabelScaleMode.ConstantOnScreen, RefDist);
            Assert.Equal(1f, m, 5);
        }

        // ── Inside the clamp band the multiplier rises ∝ camDist (constant ANGULAR size) ───────────
        //  The load-bearing invariant: mul / camDist == 1 / refDist for every in-band distance, i.e.
        //  the on-screen (pixel) size is the same near and far. A label that shrank with perspective
        //  would have a FALLING mul/camDist; this pins it constant.
        [Theory]
        [InlineData(13f)]   // band lower edge: minMul·refDist = 0.5·24 = 12, so ≥13 is strictly in-band
        [InlineData(18f)]
        [InlineData(24f)]
        [InlineData(60f)]
        [InlineData(120f)]  // band upper edge: maxMul·refDist = 6·24 = 144, so ≤120 is strictly in-band
        public void Constant_in_band_is_constant_angular(float camDist)
        {
            float m = Mul(LabelScaleMode.ConstantOnScreen, camDist);
            // mul / camDist must equal 1 / refDist everywhere inside the band.
            Assert.Equal(1f / RefDist, m / camDist, 5);
            // And it must be strictly inside the clamp band (not pinned at a clamp).
            Assert.True(m > MinMul && m < MaxMul, $"expected in-band at camDist={camDist}, got mul={m}");
        }

        // ── Outside the band the multiplier is FLAT at the clamp (no collapse / no balloon) ────────
        [Theory]
        [InlineData(0f)]     // camera on top of the portal → clamps to minMul
        [InlineData(6f)]
        [InlineData(11.9f)]  // just inside minMul·refDist=12 → still clamped
        public void Constant_near_clamps_to_minMul(float camDist)
        {
            Assert.Equal(MinMul, Mul(LabelScaleMode.ConstantOnScreen, camDist), 5);
        }

        [Theory]
        [InlineData(145f)]   // just past maxMul·refDist=144 → clamped
        [InlineData(300f)]   // overlay edge
        [InlineData(1000f)]  // defensively far
        public void Constant_far_clamps_to_maxMul(float camDist)
        {
            Assert.Equal(MaxMul, Mul(LabelScaleMode.ConstantOnScreen, camDist), 5);
        }

        // ── Monotonic non-decreasing in camDist (bigger world the farther away — never the reverse) ─
        [Fact]
        public void Constant_is_monotonic_non_decreasing_in_camDist()
        {
            float prev = float.MinValue;
            for (float d = 0f; d <= 400f; d += 2f)
            {
                float m = Mul(LabelScaleMode.ConstantOnScreen, d);
                Assert.True(m >= prev - 1e-6f, $"multiplier fell at camDist={d} (prev={prev}, m={m}) — must be non-decreasing");
                prev = m;
            }
        }

        // ── Always clamped to [minMul, maxMul] and strictly > 0 across the whole sweep ─────────────
        [Fact]
        public void Constant_result_is_always_clamped_and_positive()
        {
            for (float d = 0f; d <= 1000f; d += 5f)
            {
                float m = Mul(LabelScaleMode.ConstantOnScreen, d);
                Assert.InRange(m, MinMul, MaxMul);
                Assert.True(m > 0f, $"multiplier must be > 0 (was {m} at camDist={d})");
            }
        }

        // ── Lower refDist reads BIGGER on screen (the eyeball knob's documented direction) ─────────
        [Fact]
        public void Constant_lower_refDist_yields_a_bigger_multiplier_at_a_given_distance()
        {
            float near = TwistedPortalLabelScale.ScaleMul(LabelScaleMode.ConstantOnScreen, 30f, 12f, MinMul, MaxMul, Radius, Knee, Floor);
            float far  = TwistedPortalLabelScale.ScaleMul(LabelScaleMode.ConstantOnScreen, 30f, 48f, MinMul, MaxMul, Radius, Knee, Floor);
            Assert.True(near > far, "a smaller refDist must map a given distance to a larger on-screen size");
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        //  KneeFloor (selectable alt) — the Sunstone trophy-halo shape, high readable floor.
        // ════════════════════════════════════════════════════════════════════════════════════════

        // ── Full (1.0) at/under the knee ───────────────────────────────────────────────────────────
        [Theory]
        [InlineData(0f)]
        [InlineData(5f)]
        [InlineData(9.9f)]
        [InlineData(10f)]   // exactly at the knee → still full
        public void KneeFloor_within_the_knee_is_full_scale(float camDist)
        {
            Assert.Equal(1f, Mul(LabelScaleMode.KneeFloor, camDist), 5);
        }

        // ── Exactly the floor at the overlay radius ────────────────────────────────────────────────
        [Fact]
        public void KneeFloor_at_the_overlay_radius_is_the_floor()
        {
            Assert.Equal(Floor, Mul(LabelScaleMode.KneeFloor, Radius), 5);
        }

        // ── Linear between knee and radius ──────────────────────────────────────────────────────────
        [Fact]
        public void KneeFloor_is_linear_between_the_knee_and_the_radius()
        {
            // Midpoint of the 10→300 m span is 155 m → exactly halfway between full (1.0) and floor (0.6) → 0.8.
            float mid = Mul(LabelScaleMode.KneeFloor, (Knee + Radius) * 0.5f);
            Assert.Equal((1f + Floor) * 0.5f, mid, 5);
        }

        // ── Monotonic non-increasing with distance (far reads a little smaller — the depth cue) ─────
        [Fact]
        public void KneeFloor_is_monotonic_non_increasing_with_distance()
        {
            float prev = float.MaxValue;
            for (float d = 0f; d <= Radius; d += 2f)
            {
                float m = Mul(LabelScaleMode.KneeFloor, d);
                Assert.True(m <= prev + 1e-6f, $"multiplier rose at camDist={d} (prev={prev}, m={m}) — must be non-increasing");
                prev = m;
            }
        }

        // ── Beyond the radius clamps to the floor (never below) ─────────────────────────────────────
        [Theory]
        [InlineData(400f)]
        [InlineData(1000f)]
        public void KneeFloor_beyond_the_radius_clamps_to_the_floor(float camDist)
        {
            Assert.Equal(Floor, Mul(LabelScaleMode.KneeFloor, camDist), 5);
        }

        // ── The floor stays readable (high, vs Sunstone's 0.25) and the result is always > 0 ───────
        [Fact]
        public void KneeFloor_result_is_always_between_floor_and_one_and_positive()
        {
            for (float d = 0f; d <= 1000f; d += 5f)
            {
                float m = Mul(LabelScaleMode.KneeFloor, d);
                Assert.InRange(m, Floor, 1f);
                Assert.True(m > 0f, $"multiplier must be > 0 (was {m} at camDist={d})");
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        //  Defensive / degenerate inputs — a .cfg fat-finger can't produce a 0-size or NaN label.
        // ════════════════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void Constant_nonpositive_refDist_falls_back_to_default_no_divide_by_zero()
        {
            // refDist = 0 must NOT divide-by-zero; it falls back to DefaultRefDist and stays finite + in-band.
            float m = TwistedPortalLabelScale.ScaleMul(LabelScaleMode.ConstantOnScreen, 30f, 0f, MinMul, MaxMul, Radius, Knee, Floor);
            Assert.True(m > 0f && !float.IsNaN(m) && !float.IsInfinity(m));
            Assert.InRange(m, MinMul, MaxMul);
        }

        [Fact]
        public void Constant_swapped_clamp_band_is_normalised_not_inverted()
        {
            // minMul > maxMul (fat-fingered swap) must not invert the clamp; the result stays within the
            // normalised [lo, hi] band and positive.
            float m = TwistedPortalLabelScale.ScaleMul(LabelScaleMode.ConstantOnScreen, 1000f, RefDist, 6f, 0.5f, Radius, Knee, Floor);
            Assert.InRange(m, 0.5f, 6f);
            Assert.True(m > 0f);
        }

        [Fact]
        public void Constant_negative_camDist_is_floored_to_zero_then_clamped()
        {
            // A defensive negative distance clamps to minMul, never below, never NaN.
            float m = Mul(LabelScaleMode.ConstantOnScreen, -50f);
            Assert.Equal(MinMul, m, 5);
        }

        [Fact]
        public void KneeFloor_degenerate_radius_inside_the_knee_is_all_full_scale()
        {
            // overlayRadius ≤ knee must not divide by zero — everything sits inside the knee → full.
            float a = TwistedPortalLabelScale.ScaleMul(LabelScaleMode.KneeFloor, 5f, RefDist, MinMul, MaxMul, 8f, Knee, Floor);
            float b = TwistedPortalLabelScale.ScaleMul(LabelScaleMode.KneeFloor, 8f, RefDist, MinMul, MaxMul, 8f, Knee, Floor);
            Assert.Equal(1f, a, 5);
            Assert.Equal(1f, b, 5);
        }

        [Fact]
        public void KneeFloor_zero_floor_is_floored_above_zero_so_labels_never_vanish()
        {
            // A fat-fingered floor of 0 must be lifted to the hard MinPositiveMul, never 0 (invisible label).
            float m = TwistedPortalLabelScale.ScaleMul(LabelScaleMode.KneeFloor, Radius, RefDist, MinMul, MaxMul, Radius, Knee, 0f);
            Assert.True(m >= TwistedPortalLabelScale.MinPositiveMul - 1e-9f && m > 0f,
                $"floor=0 must be lifted to MinPositiveMul, got {m}");
        }

        // ── The default mode is ConstantOnScreen (the forward-compatible choice) ────────────────────
        [Fact]
        public void Default_mode_is_constant_on_screen()
        {
            Assert.Equal(LabelScaleMode.ConstantOnScreen, TwistedPortalLabelScale.DefaultMode);
        }

        // ── Default consts are sane (positive, ordered band, floor in (0,1]) ────────────────────────
        [Fact]
        public void Default_consts_are_internally_consistent()
        {
            Assert.True(TwistedPortalLabelScale.DefaultRefDist > 0f);
            Assert.True(TwistedPortalLabelScale.DefaultMinMul > 0f);
            Assert.True(TwistedPortalLabelScale.DefaultMaxMul > TwistedPortalLabelScale.DefaultMinMul);
            Assert.InRange(TwistedPortalLabelScale.DefaultFloor, 0f, 1f);
            Assert.True(TwistedPortalLabelScale.DefaultKnee >= 0f);
            Assert.True(TwistedPortalLabelScale.DefaultFloor > 0.25f,
                "the KneeFloor floor must be HIGHER than the Sunstone halo's 0.25 (readable far labels)");
        }
    }
}
