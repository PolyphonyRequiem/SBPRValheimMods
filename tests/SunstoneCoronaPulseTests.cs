// ============================================================================
//  SunstoneCoronaPulse — xUnit structural tests (card t_9d7c3dfe).
// ----------------------------------------------------------------------------
//  Tests the SHIPPED pure pulse-envelope math SunstoneCoronaPulse (link-compiled
//  from ../src, not copied — see the .csproj). This is the durable CI fence for the
//  Sunstone Lens corona's breathing ALPHA envelope, WITHOUT touching any volatile
//  UnityEngine/Harmony internals (the engine-free link-compile pattern shared with
//  SunstoneHaloGeometry / DiscRingGeometry / BoundedMapMath / LensHandoffDecision).
//
//  WHY THIS MATTERS. Daniel's /bug (card t_2d500d45) asked for a "3d slowly pulsing
//  'sun corona' disc," not a flat screen circle. The breath is the feature. The
//  ENVELOPE SHAPE — one shared Time.time phase, alpha clamped to [trough,peak],
//  periodic at Hz, a fat-fingered peak<trough reordered (no inverted/NaN pulse),
//  hz=0 degrading to a steady mid-glow — is the load-bearing invariant. These tests
//  pin it so a future edit that inverts the pulse, drops the clamp, or NaNs on a bad
//  .cfg fails CI instead of shipping.
//
//  The named acceptance test from the card:
//    • AT-CORONA-PULSE-MATH — alpha ∈ [trough,peak]; periodic at hz; the breath
//      midpoint + trough/peak anchor points land where the sinusoid says; peak<trough
//      reorders; hz=0 → steady mid-value.
//
//  Numbers are the architect-frozen directional defaults (hz=0.25, trough=0.10,
//  peak=0.28 around the 0.18 baseline). The tests assert the curve SHAPE + the locked
//  anchor points, not a magic absolute alpha — the defaults are live-config tunables.
// ============================================================================

using SBPR.Trailborne.Features.Sunstone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class SunstoneCoronaPulseTests
    {
        // The architect-frozen directional defaults (spec §0 / §4). hz=0.25 → one breath / 4 s,
        // so the sinusoid's quarter-period is exactly 1 s — handy, exact anchor points below.
        private const float Hz     = 0.25f;
        private const float Trough = 0.10f;
        private const float Peak   = 0.28f;
        private const float Mid    = (Trough + Peak) * 0.5f;   // 0.19 — the 0-phase start + every half-period

        // ── AT-CORONA-PULSE-MATH: the locked anchor points of one breath ─────────────────────────
        //  s(t) = 0.5*(1+sin(2π·hz·t)). At hz=0.25: t=0 → mid (rising), t=1 → peak, t=2 → mid,
        //  t=3 → trough, t=4 → mid (one full breath). Pins the phase convention + the depth.

        [Fact]
        public void PulseMath_zero_phase_starts_mid_breath()
        {
            // 0.5*(1+sin(0)) = 0.5 → exactly halfway up the trough→peak swing (rising).
            Assert.Equal(Mid, SunstoneCoronaPulse.AlphaAt(0.0, Hz, Trough, Peak), 5);
        }

        [Fact]
        public void PulseMath_quarter_period_is_the_peak()
        {
            // t = 1/(4·hz) = 1 s → sin(π/2) = 1 → full peak.
            Assert.Equal(Peak, SunstoneCoronaPulse.AlphaAt(1.0, Hz, Trough, Peak), 5);
        }

        [Fact]
        public void PulseMath_three_quarter_period_is_the_trough()
        {
            // t = 3/(4·hz) = 3 s → sin(3π/2) = -1 → full trough.
            Assert.Equal(Trough, SunstoneCoronaPulse.AlphaAt(3.0, Hz, Trough, Peak), 5);
        }

        [Fact]
        public void PulseMath_half_period_returns_to_mid()
        {
            // t = 1/(2·hz) = 2 s → sin(π) = 0 → mid (falling this time, but same value).
            Assert.Equal(Mid, SunstoneCoronaPulse.AlphaAt(2.0, Hz, Trough, Peak), 5);
        }

        [Fact]
        public void PulseMath_is_periodic_at_hz()
        {
            // AlphaAt(t) == AlphaAt(t + 1/hz) for an arbitrary off-anchor phase (period = 1/hz = 4 s).
            const double t = 0.7;
            Assert.Equal(
                SunstoneCoronaPulse.AlphaAt(t, Hz, Trough, Peak),
                SunstoneCoronaPulse.AlphaAt(t + 1.0 / Hz, Hz, Trough, Peak),
                5);
        }

        [Fact]
        public void PulseMath_alpha_is_always_within_trough_and_peak()
        {
            // Sweep 0..20 s in 0.1 s steps; the breath must never escape [trough, peak].
            for (int i = 0; i <= 200; i++)
            {
                double t = i * 0.1;
                float a = SunstoneCoronaPulse.AlphaAt(t, Hz, Trough, Peak);
                Assert.True(a >= Trough - 1e-6f, $"alpha {a} fell below trough at t={t}");
                Assert.True(a <= Peak + 1e-6f,   $"alpha {a} rose above peak at t={t}");
            }
        }

        // ── Defensive / degenerate cases (a fat-fingered .cfg must never invert or NaN) ──────────

        [Fact]
        public void PulseMath_peak_below_trough_is_reordered_not_inverted()
        {
            // A .cfg with peak < trough must degrade to the SAME swing as the ordered pair, never an
            // inverted pulse. AlphaAt(t, hz, peak, trough) == AlphaAt(t, hz, trough, peak) for all t.
            for (int i = 0; i <= 40; i++)
            {
                double t = i * 0.1;
                Assert.Equal(
                    SunstoneCoronaPulse.AlphaAt(t, Hz, Trough, Peak),
                    SunstoneCoronaPulse.AlphaAt(t, Hz, Peak, Trough),   // swapped args
                    6);
            }
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(5.0)]
        [InlineData(99.0)]
        public void PulseMath_zero_hz_holds_a_steady_mid_value(double time)
        {
            // hz = 0 → sin(0) = 0 at every t → a steady mid-glow (no breath), never a flicker or NaN.
            Assert.Equal(Mid, SunstoneCoronaPulse.AlphaAt(time, 0f, Trough, Peak), 5);
        }

        [Fact]
        public void PulseMath_out_of_range_trough_peak_are_clamped_to_unit()
        {
            // trough/peak are clamped to [0,1] before the swing, so a .cfg outside the range can't
            // push the alpha past a valid colour component. peak=2.0 → 1.0; trough=-0.5 → 0.0.
            Assert.Equal(1f, SunstoneCoronaPulse.AlphaAt(1.0, Hz, -0.5f, 2.0f), 5);   // quarter period → clamped peak
            Assert.Equal(0f, SunstoneCoronaPulse.AlphaAt(3.0, Hz, -0.5f, 2.0f), 5);   // three-quarter → clamped trough
        }

        [Fact]
        public void PulseMath_a_flat_envelope_breathes_to_a_constant()
        {
            // trough == peak → the swing collapses to a constant at that value (no division artefact).
            for (int i = 0; i <= 20; i++)
            {
                double t = i * 0.1;
                Assert.Equal(0.18f, SunstoneCoronaPulse.AlphaAt(t, Hz, 0.18f, 0.18f), 5);
            }
        }
    }
}
