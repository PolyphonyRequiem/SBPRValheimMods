// ============================================================================
//  SunstoneCoronaProfile — xUnit structural tests (FeetGlow corona follow-up).
// ----------------------------------------------------------------------------
//  Tests the SHIPPED pure vertical-profile math SunstoneCoronaProfile (link-compiled
//  from ../src, not copied — see the .csproj). This is the durable CI fence for the
//  Sunstone Lens FeetGlow corona's SHAPE, WITHOUT touching any volatile UnityEngine/
//  Harmony internals (the engine-free link-compile pattern shared with SunstoneCorona-
//  Pulse / SunstoneHaloGeometry / DiscRingGeometry / BoundedMapMath).
//
//  WHY THIS MATTERS. Daniel's /bug (Discord ticket-diegetic-halo-render, 2026-06-24)
//  asked for the flat ground-plane corona — which "clips through the terrain and seems
//  perfectly flat" — to become "a more 'substantive' looking glow that starts around the
//  feet, appears at full width around .5m from ground, and doesn't seem to 'hard clip'
//  into the environment." The vertical alpha PROFILE is the feature. Its locked SHAPE —
//  zero alpha at the ground (a soft meet → no hard terrain-clip line); zero alpha at both
//  horizontal rims (soft sides); a narrow base that BLOOMS to full width by a set height;
//  alpha fading to zero at the dome top — is the load-bearing invariant. This box renders
//  nothing (Valheim shaders collapse headless), so this math fence is the ONLY headless
//  guard the look has. These tests pin it so a future edit that re-introduces a hard
//  bottom edge, stops the bloom, or lets the glow reach the rims opaque fails CI.
//
//  The named acceptance test from the card:
//    • AT-CORONA-FEET-PROFILE — alpha==0 at v=0 (soft ground); alpha==0 at u∈{0,1} (soft
//      sides); the centre column reaches ~peak by v=fullWidthFrac; the available width at
//      the base is strictly LESS than at fullWidthFrac (the bloom); alpha → 0 as v→1 (dome).
//
//  Numbers below are the architect-frozen directional defaults (Height=1.2, FullWidth-
//  Height=0.5 → fullWidthFrac≈0.42, BaseWidthFrac=0.15, Thickness=0.45). The tests assert
//  the curve SHAPE + the locked structural relations, not magic absolute alphas — every
//  number is a live-config tunable Daniel converges in-game.
// ============================================================================

using SBPR.Trailborne.Features.Sunstone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class SunstoneCoronaProfileTests
    {
        // Architect-frozen directional defaults. fullWidthFrac = FullWidthHeight / Height = 0.5 / 1.2.
        private const float FullWidthFrac = 0.5f / 1.2f;   // ≈ 0.4167
        private const float BaseWidthFrac = 0.15f;
        private const float Thickness     = 0.45f;

        private const float Center = 0.5f;   // the u of the centre column

        // ── AT-CORONA-FEET-PROFILE: the soft ground meet (no hard terrain-clip line) ──────────────

        [Fact]
        public void Profile_alpha_is_zero_at_the_ground_contact()
        {
            // v = 0 (the bottom row = ground contact) must be fully transparent across the WHOLE width,
            // so the upright billboard fades INTO the ground with no hard intersection line.
            for (int i = 0; i <= 20; i++)
            {
                float u = i / 20f;
                float a = SunstoneCoronaProfile.AlphaAt(u, 0f, FullWidthFrac, BaseWidthFrac, Thickness);
                Assert.True(a <= 1e-6f, $"alpha {a} was not ~0 at the ground (u={u}, v=0)");
            }
        }

        [Fact]
        public void Profile_alpha_lifts_off_just_above_the_ground()
        {
            // Just above the ground the centre column must already be lighting up (a bright feet core),
            // i.e. the soft ground ramp is SHORT — it's zero AT the ground but not still-zero above it.
            float a = SunstoneCoronaProfile.AlphaAt(Center, 0.12f, FullWidthFrac, BaseWidthFrac, Thickness);
            Assert.True(a > 0.2f, $"centre column still dark just above the ground (alpha {a})");
        }

        // ── AT-CORONA-FEET-PROFILE: soft horizontal rims (transparent at both sides) ──────────────

        [Theory]
        [InlineData(0.20f)]
        [InlineData(0.4167f)]
        [InlineData(0.70f)]
        public void Profile_alpha_is_zero_at_both_horizontal_rims(float v)
        {
            // u = 0 and u = 1 (the extreme left/right of the quad) must be transparent at every height,
            // so the glow has soft sides and never paints an opaque rectangular edge.
            Assert.Equal(0f, SunstoneCoronaProfile.AlphaAt(0f, v, FullWidthFrac, BaseWidthFrac, Thickness), 5);
            Assert.Equal(0f, SunstoneCoronaProfile.AlphaAt(1f, v, FullWidthFrac, BaseWidthFrac, Thickness), 5);
        }

        // ── AT-CORONA-FEET-PROFILE: the BLOOM (narrow base → full width by fullWidthFrac) ─────────

        [Fact]
        public void Profile_blooms_base_is_strictly_narrower_than_full_width()
        {
            // The half-width low on the column (the feet) must be strictly LESS than at fullWidthFrac —
            // this is the "rises narrow, blooms to full width by ~0.5m" shape, not a constant column.
            float baseHalf = SunstoneCoronaProfile.HalfWidthAt(0.05f, FullWidthFrac, BaseWidthFrac);
            float fullHalf = SunstoneCoronaProfile.HalfWidthAt(FullWidthFrac, FullWidthFrac, BaseWidthFrac);
            Assert.True(baseHalf < fullHalf,
                $"base half-width {baseHalf} was not narrower than full {fullHalf} (no bloom)");
        }

        [Fact]
        public void Profile_half_width_grows_monotonically_up_the_bloom()
        {
            // Across the bloom region (v: 0 → fullWidthFrac) the half-width must be non-decreasing — a
            // smooth flare, never narrowing on the way up to full.
            float prev = -1f;
            for (int i = 0; i <= 20; i++)
            {
                float v = FullWidthFrac * (i / 20f);
                float half = SunstoneCoronaProfile.HalfWidthAt(v, FullWidthFrac, BaseWidthFrac);
                Assert.True(half >= prev - 1e-6f, $"half-width shrank inside the bloom at v={v}");
                prev = half;
            }
        }

        [Fact]
        public void Profile_center_column_reaches_full_brightness_by_full_width_height()
        {
            // The vertical brightness envelope on the centre column must be at/near full (1.0) by the
            // time the glow reaches full width — the body of the glow is lit, not just the feet.
            float a = SunstoneCoronaProfile.VerticalAlphaAt(FullWidthFrac, FullWidthFrac);
            Assert.True(a >= 0.999f, $"centre brightness {a} was not full by fullWidthFrac");
        }

        // ── AT-CORONA-FEET-PROFILE: the soft DOME top (fades to nothing at v=1) ───────────────────

        [Fact]
        public void Profile_alpha_fades_to_zero_at_the_dome_top()
        {
            // v = 1 (the very top) must be transparent on the centre column — the glow domes over and
            // fades out, it does not paint a hard flat cap.
            float a = SunstoneCoronaProfile.AlphaAt(Center, 1f, FullWidthFrac, BaseWidthFrac, Thickness);
            Assert.Equal(0f, a, 5);
        }

        [Fact]
        public void Profile_center_brightness_decreases_through_the_dome()
        {
            // Above fullWidthFrac the centre-column brightness must be monotonically NON-increasing down
            // to 0 at the top (the dome fade), never brightening again.
            float prev = 2f;
            for (int i = 0; i <= 20; i++)
            {
                float v = FullWidthFrac + (1f - FullWidthFrac) * (i / 20f);
                float a = SunstoneCoronaProfile.VerticalAlphaAt(v, FullWidthFrac);
                Assert.True(a <= prev + 1e-6f, $"dome brightness rose at v={v} (a={a}, prev={prev})");
                prev = a;
            }
        }

        // ── Range + defensive guards (a fat-fingered .cfg must never escape [0,1] or NaN) ─────────

        [Fact]
        public void Profile_alpha_is_always_within_unit_range()
        {
            // Sweep the whole quad: alpha must never leave [0,1] for any (u,v) — it's a colour component.
            for (int yi = 0; yi <= 40; yi++)
            {
                float v = yi / 40f;
                for (int xi = 0; xi <= 40; xi++)
                {
                    float u = xi / 40f;
                    float a = SunstoneCoronaProfile.AlphaAt(u, v, FullWidthFrac, BaseWidthFrac, Thickness);
                    Assert.True(a >= 0f && a <= 1f, $"alpha {a} escaped [0,1] at (u={u}, v={v})");
                }
            }
        }

        [Theory]
        [InlineData(-0.5f)]   // fullWidthFrac below range → clamped, still a valid shape
        [InlineData(0f)]
        [InlineData(1.0f)]
        [InlineData(2.0f)]    // above range → clamped
        public void Profile_degenerate_full_width_frac_stays_in_range_and_keeps_a_soft_ground(float fwf)
        {
            // Even a fat-fingered CoronaHeight/FullWidthHeight (frac → 0 or → 1) must still give a soft
            // ground (alpha 0 at v=0) and stay in [0,1) — the band-clamp in the profile guarantees a base
            // and a dome always exist.
            Assert.Equal(0f, SunstoneCoronaProfile.AlphaAt(Center, 0f, fwf, BaseWidthFrac, Thickness), 5);
            for (int i = 0; i <= 20; i++)
            {
                float v = i / 20f;
                float a = SunstoneCoronaProfile.AlphaAt(Center, v, fwf, BaseWidthFrac, Thickness);
                Assert.True(a >= 0f && a <= 1f, $"alpha {a} escaped [0,1] at v={v}, fwf={fwf}");
            }
        }

        [Fact]
        public void Profile_zero_base_width_still_has_a_soft_lit_core_above_the_feet()
        {
            // BaseWidthFrac = 0 (a near-point base) must not produce a fully-dark column — by fullWidth
            // the centre is still lit (the bloom opens it up regardless of how narrow the base is).
            float a = SunstoneCoronaProfile.AlphaAt(Center, FullWidthFrac, FullWidthFrac, 0f, Thickness);
            Assert.True(a > 0.5f, $"centre column dark at full-width with zero base width (alpha {a})");
        }
    }
}
