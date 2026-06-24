// ============================================================================
//  MinimapThreatMetrics — xUnit structural tests (card t_bc017af4).
// ----------------------------------------------------------------------------
//  Pins the SHARED Sunstone minimap threat-blip render ratios that BOTH minimap
//  surfaces read (the SBPR carry-disc in MapSurface + the vanilla corner overlay
//  in SunstoneMinimapThreatLayer). The class is engine-free, so it link-compiles
//  here (../src, not copied — see the .csproj) and these asserts gate CI.
//
//  WHY THIS MATTERS. Before this card each surface carried its OWN copy of the size
//  consts (BlipPx/ThreatBlipPx = 14f, the 0.6 rim multiplier, the 7f pip size), so a
//  change to one could silently desync the other. Daniel asked for the blips ~75%
//  larger (2026-06-24); the fix single-homes the ratios in MinimapThreatMetrics and
//  has both surfaces resolve the live SunstoneLens/MinimapBlipPx knob. These tests
//  pin the historical relationships so a future edit that breaks scale (the +75%
//  default), pip balance (pip = blip×0.5), or the rim multiplier fails CI instead of
//  shipping a regression. They assert the CONSTANTS only — full on-screen visual
//  parity is Daniel's joined-client eyeball (AT-BLIP-PARITY, honest scope).
// ============================================================================

using SBPR.Trailborne.Features.Cartography;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class MinimapThreatMetricsTests
    {
        // AT-BLIP-SIZE: the new default is exactly 75% larger than the historical 14px blip.
        [Fact]
        public void BlipPx_is_75pct_larger_than_the_historical_14()
            => Assert.Equal(14f * 1.75f, MinimapThreatMetrics.DefaultBlipPx, 3);

        // AT-BLIP-PIP-BALANCE: the pip ratio reproduces the historical 7/14 so pips stay balanced
        // at any knob magnitude (pip = blipPx × this ratio on BOTH surfaces).
        [Fact]
        public void Pip_ratio_reproduces_the_historical_7_over_14()
            => Assert.Equal(7f / 14f, MinimapThreatMetrics.PipToBlipRatio, 3);

        // AT-BLIP-RIM: the off-edge rim multiplier is unchanged (value 0.6, just single-homed now).
        [Fact]
        public void Rim_scale_is_unchanged_at_0_6()
            => Assert.Equal(0.6f, MinimapThreatMetrics.RimScale, 3);
    }
}
