// ============================================================================
//  Seer's Stone — WISP MOTION tests (engine-free, net8.0/xUnit, CI-gated)
// ----------------------------------------------------------------------------
//  Gates the helix-on-a-cylinder geometry (Daniel 2026-06-25): the wisp rides the
//  WALL of a cylinder whose axis is the patch centroid — orbit radius = bounds +
//  margin, a slow circular loop, a slow vertical sine bob. If a future edit breaks
//  the radius (wisp orbits at the wrong distance / inside the foliage), flattens the
//  bob, or desyncs the orbit period, these fail.
// ============================================================================

using System;
using SBPR.Trailborne.Features.SeersStone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public class SeersStoneWispMotionTests
    {
        private static WispMotionParams P(float bounds = 2f, float margin = 0.75f, float orbit = 12f,
                                          float bobAmp = 0.5f, float bobPer = 4f, float bobMid = 1.5f, float phase = 0f)
            => new WispMotionParams(bounds, margin, orbit, bobAmp, bobPer, bobMid, phase);

        private static float Hyp(Vec3 v) => (float)Math.Sqrt(v.X * v.X + v.Z * v.Z);

        // ── Orbit radius = bounds + margin (the "floats just outside the foliage" rule) ──
        [Fact]
        public void Orbit_radius_is_bounds_plus_margin()
        {
            var p = P(bounds: 2f, margin: 0.75f);
            Assert.Equal(2.75f, p.OrbitRadius, 3);
            // The horizontal offset magnitude equals OrbitRadius at every time sample.
            for (float t = 0; t < 12f; t += 1.3f)
            {
                var h = WispMotion.HorizontalOffset(p, t);
                Assert.Equal(2.75f, Hyp(h), 3);
                Assert.Equal(0f, h.Y, 5); // horizontal axis carries no height
            }
        }

        // ── The orbit actually goes around (closes over its period) ──────────────────────
        [Fact]
        public void Orbit_returns_to_start_after_one_period()
        {
            var p = P(orbit: 12f);
            var a = WispMotion.HorizontalOffset(p, 0f);
            var b = WispMotion.HorizontalOffset(p, 12f); // one full period later
            Assert.Equal(a.X, b.X, 3);
            Assert.Equal(a.Z, b.Z, 3);
        }

        [Fact]
        public void Orbit_is_halfway_around_at_half_period()
        {
            var p = P(orbit: 12f, margin: 0f, bounds: 1f); // radius 1 for clean numbers
            var start = WispMotion.HorizontalOffset(p, 0f);
            var half  = WispMotion.HorizontalOffset(p, 6f);
            // Half a loop ⇒ diametrically opposite ⇒ position negated in XZ.
            Assert.Equal(-start.X, half.X, 3);
            Assert.Equal(-start.Z, half.Z, 3);
        }

        // ── Vertical sine bob stays within [mid-amp, mid+amp] ────────────────────────────
        [Fact]
        public void Bob_stays_within_amplitude_band()
        {
            var p = P(bobAmp: 0.5f, bobMid: 1.5f, bobPer: 4f);
            for (float t = 0; t < 20f; t += 0.25f)
            {
                float h = WispMotion.VerticalHeight(p, t);
                Assert.InRange(h, 1.5f - 0.5f - 1e-4f, 1.5f + 0.5f + 1e-4f);
            }
        }

        [Fact]
        public void Bob_midheight_at_t0_then_peaks_at_quarter_period()
        {
            var p = P(bobAmp: 0.5f, bobMid: 1.5f, bobPer: 4f);
            Assert.Equal(1.5f, WispMotion.VerticalHeight(p, 0f), 3);     // sin(0)=0 ⇒ mid
            Assert.Equal(2.0f, WispMotion.VerticalHeight(p, 1f), 3);     // quarter period ⇒ +amp peak
            Assert.Equal(1.0f, WispMotion.VerticalHeight(p, 3f), 3);     // three-quarter ⇒ -amp trough
        }

        // ── Default never dips below ground from the sine alone (mid ≥ amp) ──────────────
        [Fact]
        public void Default_bob_never_goes_below_ground()
        {
            var p = WispMotionParams.Default(boundsRadius: 2f);
            Assert.True(p.BobMidHeight >= p.BobAmplitude,
                "mid-height must be >= amplitude so the wisp never sinks below local ground from the sine");
            for (float t = 0; t < 30f; t += 0.3f)
                Assert.True(WispMotion.VerticalHeight(p, t) > 0f);
        }

        // ── Phase offset spreads adjacent wisps (anti-lockstep) ──────────────────────────
        [Fact]
        public void Phase_offset_separates_two_wisps()
        {
            var a = P(phase: 0f);
            var b = P(phase: (float)Math.PI); // half a turn out of phase
            var ha = WispMotion.HorizontalOffset(a, 0f);
            var hb = WispMotion.HorizontalOffset(b, 0f);
            // Opposite phase ⇒ opposite side of the orbit at the same instant.
            Assert.Equal(-ha.X, hb.X, 3);
            Assert.Equal(-ha.Z, hb.Z, 3);
        }

        // ── Degenerate params don't divide by zero ───────────────────────────────────────
        [Fact]
        public void Zero_periods_do_not_throw_or_nan()
        {
            var p = P(orbit: 0f, bobPer: 0f);
            var h = WispMotion.HorizontalOffset(p, 5f);
            float v = WispMotion.VerticalHeight(p, 5f);
            Assert.False(float.IsNaN(h.X) || float.IsNaN(h.Z) || float.IsNaN(v));
        }

        // ── LocalOffsetFlat composes both axes into the full helix point ─────────────────
        [Fact]
        public void LocalOffsetFlat_combines_horizontal_and_vertical()
        {
            var p = P(bounds: 2f, margin: 0.75f, bobMid: 1.5f);
            var o = WispMotion.LocalOffsetFlat(p, 0f);
            Assert.Equal(2.75f, Hyp(o), 3);   // horizontal radius preserved
            Assert.Equal(1.5f, o.Y, 3);       // mid-height at t0
        }
    }
}
