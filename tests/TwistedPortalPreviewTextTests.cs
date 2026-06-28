// ============================================================================
//  TwistedPortalPreviewText — xUnit structural tests (card t_d9ea1b2c / L3).
// ----------------------------------------------------------------------------
//  Tests the SHIPPED pure formatting logic TwistedPortalPreviewText (link-compiled
//  from ../src, not copied — see the .csproj). This is the durable CI fence for the
//  look-to-aim food-impact preview's TEXT contract (spec §5 / Beat 3): the belly-range
//  line, the three verdict branches (in range / need N berries / too far), and the
//  singular-vs-plural "berry"/"berries" wording — WITHOUT touching the world-space
//  render (Canvas / UI.Text / the highlight material), which is a GPU-client eyeball
//  (AT-FOOD-PREVIEW / AT-AIM-HIGHLIGHT) that cannot be asserted headless.
//
//  🔴 The wording is COLORBLIND-SAFE by design (Daniel is colourblind): reachability is
//  stated in WORDS, never by a red/green tint. These tests pin that the verdict text
//  carries the meaning, so a refactor can't silently regress to colour-only signalling.
//
//  The engine-free link-compile pattern shared with TwistedPortalOverlayModel /
//  AimPickMath / PortalEnergyMath: the asserted behaviour IS the shipped behaviour
//  (one copy, no fork), running under net8.0 in CI with no Valheim SDK fetched.
// ============================================================================

using SBPR.Trailborne.Features.Portals;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class TwistedPortalPreviewTextTests
    {
        // ── belly-covers branch: zero berries, "in range" ───────────────────────────────────────

        [Fact]
        public void BellyCovers_reads_in_range_with_no_berry_mention()
        {
            // belly range 250 m, jump 120 m → covered. Verdict is the plain "in range" (no berries).
            string s = TwistedPortalPreviewText.BuildFoodPreview(
                distanceMeters: 120f, bellyRangeMeters: 250f, bellyCovers: true,
                berriesNeeded: 0, berriesHeld: 0, reachable: true);
            Assert.Equal("belly 250m\nin range", s);
        }

        // ── shortfall + reachable branch: "need N berries (have M)" ──────────────────────────────

        [Fact]
        public void Shortfall_reachable_reads_need_berries_with_held_count()
        {
            // belly 80 m, jump 200 m → 120 m shortfall = 4 berries; player holds 7. Reachable.
            string s = TwistedPortalPreviewText.BuildFoodPreview(
                distanceMeters: 200f, bellyRangeMeters: 80f, bellyCovers: false,
                berriesNeeded: 4, berriesHeld: 7, reachable: true);
            Assert.Equal("belly 80m\nneed 4 berries (have 7)", s);
        }

        [Fact]
        public void Shortfall_reachable_one_berry_is_singular()
        {
            // The singular guard: "1 berry", never "1 berries".
            string s = TwistedPortalPreviewText.BuildFoodPreview(
                distanceMeters: 100f, bellyRangeMeters: 75f, bellyCovers: false,
                berriesNeeded: 1, berriesHeld: 3, reachable: true);
            Assert.Equal("belly 75m\nneed 1 berry (have 3)", s);
        }

        // ── shortfall + NOT reachable branch: "too far: N berries (have M)" ──────────────────────

        [Fact]
        public void Shortfall_unreachable_reads_too_far_with_the_gap()
        {
            // belly 50 m, jump 300 m → 250 m shortfall = 9 berries; player holds only 2. Too far.
            string s = TwistedPortalPreviewText.BuildFoodPreview(
                distanceMeters: 300f, bellyRangeMeters: 50f, bellyCovers: false,
                berriesNeeded: 9, berriesHeld: 2, reachable: false);
            Assert.Equal("belly 50m\ntoo far: 9 berries (have 2)", s);
        }

        [Fact]
        public void Unreachable_with_one_berry_short_is_singular()
        {
            string s = TwistedPortalPreviewText.BuildFoodPreview(
                distanceMeters: 90f, bellyRangeMeters: 60f, bellyCovers: false,
                berriesNeeded: 1, berriesHeld: 0, reachable: false);
            Assert.Equal("belly 60m\ntoo far: 1 berry (have 0)", s);
        }

        // ── the belly-range line reuses the shared invariant-culture distance formatter ──────────

        [Fact]
        public void Belly_range_uses_km_formatting_past_1km()
        {
            // belly 1440 m → "1.4km" (the overlay's FormatDistance, reused so the readout matches).
            string s = TwistedPortalPreviewText.BuildFoodPreview(
                distanceMeters: 100f, bellyRangeMeters: 1440f, bellyCovers: true,
                berriesNeeded: 0, berriesHeld: 0, reachable: true);
            Assert.Equal("belly 1.4km\nin range", s);
        }

        [Fact]
        public void Belly_range_of_zero_reads_0m_not_blank()
        {
            // An empty belly (no food) → "belly 0m" + a too-far verdict, never a blank first line.
            string s = TwistedPortalPreviewText.BuildFoodPreview(
                distanceMeters: 120f, bellyRangeMeters: 0f, bellyCovers: false,
                berriesNeeded: 4, berriesHeld: 0, reachable: false);
            Assert.Equal("belly 0m\ntoo far: 4 berries (have 0)", s);
        }

        // ── defensive: a negative held count never leaks a minus sign into the readout ───────────

        [Fact]
        public void Negative_held_count_clamps_to_zero()
        {
            string s = TwistedPortalPreviewText.BuildFoodPreview(
                distanceMeters: 200f, bellyRangeMeters: 80f, bellyCovers: false,
                berriesNeeded: 4, berriesHeld: -3, reachable: false);
            Assert.Equal("belly 80m\ntoo far: 4 berries (have 0)", s);
        }

        // ── the verdict is always WORDS (colourblind-safe): no branch is empty / colour-only ─────

        [Theory]
        [InlineData(true, 0, 0, true)]    // in range
        [InlineData(false, 3, 5, true)]   // need 3 berries
        [InlineData(false, 9, 1, false)]  // too far
        public void Every_branch_emits_a_nonblank_second_line(
            bool bellyCovers, int needed, int held, bool reachable)
        {
            string s = TwistedPortalPreviewText.BuildFoodPreview(
                distanceMeters: 150f, bellyRangeMeters: 90f, bellyCovers: bellyCovers,
                berriesNeeded: needed, berriesHeld: held, reachable: reachable);
            string[] lines = s.Split('\n');
            Assert.Equal(2, lines.Length);
            Assert.False(string.IsNullOrWhiteSpace(lines[1]));   // the verdict is never blank / colour-only
        }
    }
}
