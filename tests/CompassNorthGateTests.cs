// ============================================================================
//  CompassNorthGate — xUnit structural tests (card t_fb53c9e4, M1).
// ----------------------------------------------------------------------------
//  Tests the SHIPPED pure decision logic CompassNorthGate (link-compiled from
//  ../src, not copied — see the .csproj). This is the durable CI fence for the
//  Iron Compass → map-surface north ring (AT-COMPASS-GATE): it pins the FULL
//  truth table that decides whether an SBPR map surface draws the compass north
//  ring and whether the HUD compass needle hides, WITHOUT touching any volatile
//  UnityEngine/Harmony internals (the engine-free link-compile pattern shared
//  with DiscRingGeometry / BoundedMapMath / LensHandoffDecision).
//
//  WHY THIS MATTERS. The CompassDiscMode policy is the load-bearing decision of
//  M1 (Daniel locked CompassDiscMode = DiscWhenBound — "the HUD needle goes away"
//  when a surface ring is up). Two failure classes this fence catches before they
//  ship:
//    • AT-COMPASS-DISC-PUMP (policy half) — a future edit that forgets to hide
//      the needle when a surface owns north under DiscWhenBound, or that hides it
//      when it shouldn't (e.g. with no compass worn, where the equip-gate already
//      governs it — force-hiding there is the #208/#209-adjacent regression).
//    • The §5 shared invariant — north appears on a surface IFF the compass is
//      worn: no compass → no ring at ANY mode, no surface → HUD needle at ANY mode.
//
//  Every assertion is derived from the impl-spec's §5 truth table; none are magic.
// ============================================================================

using SBPR.Trailborne.Features.Cartography;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class CompassNorthGateTests
    {
        // ── §5 invariant half 1: no compass worn → never a surface ring, never a force-hide, ANY mode.
        //    (When unworn the HUD needle is already hidden by the compass's own equip-gate, so the gate
        //    must NOT additionally force-hide — HideHudNeedle stays false. The surface stays north-blind.)

        [Theory]
        [InlineData(CompassDiscModeEnum.HudOnly, false)]
        [InlineData(CompassDiscModeEnum.HudOnly, true)]
        [InlineData(CompassDiscModeEnum.DiscWhenBound, false)]
        [InlineData(CompassDiscModeEnum.DiscWhenBound, true)]
        [InlineData(CompassDiscModeEnum.Both, false)]
        [InlineData(CompassDiscModeEnum.Both, true)]
        public void NoCompass_never_rings_never_force_hides(CompassDiscModeEnum mode, bool surfaceShowing)
        {
            var plan = CompassNorthGate.Resolve(surfaceShowing: surfaceShowing, compassWorn: false, mode: mode);
            Assert.False(plan.ShowSurfaceRing);   // surface stays north-blind without the compass
            Assert.False(plan.HideHudNeedle);     // the equip-gate already governs the needle; don't add a hide
        }

        // ── §5 invariant half 2: worn but NO surface to draw on → the HUD needle is the payoff, ANY mode.

        [Theory]
        [InlineData(CompassDiscModeEnum.HudOnly)]
        [InlineData(CompassDiscModeEnum.DiscWhenBound)]
        [InlineData(CompassDiscModeEnum.Both)]
        public void Worn_no_surface_keeps_the_hud_needle(CompassDiscModeEnum mode)
        {
            var plan = CompassNorthGate.Resolve(surfaceShowing: false, compassWorn: true, mode: mode);
            Assert.False(plan.ShowSurfaceRing);   // nothing to draw the ring on
            Assert.False(plan.HideHudNeedle);     // HUD needle is the north payoff
        }

        // ── HudOnly (escape hatch): worn + surface showing → ignore the surface, HUD needle always, ring never.

        [Fact]
        public void HudOnly_worn_surface_showing_keeps_needle_no_ring()
        {
            var plan = CompassNorthGate.Resolve(surfaceShowing: true, compassWorn: true, mode: CompassDiscModeEnum.HudOnly);
            Assert.False(plan.ShowSurfaceRing);   // escape hatch: never a surface ring
            Assert.False(plan.HideHudNeedle);     // HUD needle always under HudOnly
        }

        // ── DiscWhenBound (the gated DEFAULT): worn + surface showing → ring ON, needle HIDES (① "it goes away").

        [Fact]
        public void DiscWhenBound_worn_surface_showing_rings_and_hides_needle()
        {
            var plan = CompassNorthGate.Resolve(surfaceShowing: true, compassWorn: true, mode: CompassDiscModeEnum.DiscWhenBound);
            Assert.True(plan.ShowSurfaceRing);    // the iron bezel + N + ticks render on the surface
            Assert.True(plan.HideHudNeedle);      // ① the HUD needle "goes away"
        }

        [Fact]
        public void DiscWhenBound_worn_no_surface_keeps_needle_no_ring()
        {
            // No surface → DiscWhenBound falls back to the HUD needle (the surface clause is the only
            // thing that flips the handoff on).
            var plan = CompassNorthGate.Resolve(surfaceShowing: false, compassWorn: true, mode: CompassDiscModeEnum.DiscWhenBound);
            Assert.False(plan.ShowSurfaceRing);
            Assert.False(plan.HideHudNeedle);
        }

        // ── Both (supplement): worn + surface showing → ring ON, needle STAYS (both render at once).

        [Fact]
        public void Both_worn_surface_showing_rings_and_keeps_needle()
        {
            var plan = CompassNorthGate.Resolve(surfaceShowing: true, compassWorn: true, mode: CompassDiscModeEnum.Both);
            Assert.True(plan.ShowSurfaceRing);    // surface ring shows
            Assert.False(plan.HideHudNeedle);     // AND the HUD needle stays — both visible
        }

        // ── The ring only ever shows when worn AND a surface is showing AND the mode allows it. Sweep
        //    every (mode × worn × surfaceShowing) cell and assert ShowSurfaceRing matches the §5 table
        //    exactly (defends against a future edit drifting any single cell).

        [Theory]
        // worn=false rows → ring never (already covered above, re-asserted here in the full sweep for completeness)
        [InlineData(CompassDiscModeEnum.HudOnly, false, false, false)]
        [InlineData(CompassDiscModeEnum.HudOnly, false, true, false)]
        [InlineData(CompassDiscModeEnum.DiscWhenBound, false, false, false)]
        [InlineData(CompassDiscModeEnum.DiscWhenBound, false, true, false)]
        [InlineData(CompassDiscModeEnum.Both, false, false, false)]
        [InlineData(CompassDiscModeEnum.Both, false, true, false)]
        // worn=true, no surface → ring never (HUD needle fallback)
        [InlineData(CompassDiscModeEnum.HudOnly, true, false, false)]
        [InlineData(CompassDiscModeEnum.DiscWhenBound, true, false, false)]
        [InlineData(CompassDiscModeEnum.Both, true, false, false)]
        // worn=true, surface showing → ring iff the mode draws on the surface (HudOnly never; the others yes)
        [InlineData(CompassDiscModeEnum.HudOnly, true, true, false)]
        [InlineData(CompassDiscModeEnum.DiscWhenBound, true, true, true)]
        [InlineData(CompassDiscModeEnum.Both, true, true, true)]
        public void ShowSurfaceRing_matches_the_full_truth_table(
            CompassDiscModeEnum mode, bool worn, bool surfaceShowing, bool expectedRing)
        {
            var plan = CompassNorthGate.Resolve(surfaceShowing: surfaceShowing, compassWorn: worn, mode: mode);
            Assert.Equal(expectedRing, plan.ShowSurfaceRing);
        }

        // ── HideHudNeedle is true in EXACTLY one cell: DiscWhenBound + worn + surface showing.

        [Theory]
        [InlineData(CompassDiscModeEnum.HudOnly, false, false, false)]
        [InlineData(CompassDiscModeEnum.HudOnly, false, true, false)]
        [InlineData(CompassDiscModeEnum.HudOnly, true, false, false)]
        [InlineData(CompassDiscModeEnum.HudOnly, true, true, false)]
        [InlineData(CompassDiscModeEnum.DiscWhenBound, false, false, false)]
        [InlineData(CompassDiscModeEnum.DiscWhenBound, false, true, false)]
        [InlineData(CompassDiscModeEnum.DiscWhenBound, true, false, false)]
        [InlineData(CompassDiscModeEnum.DiscWhenBound, true, true, true)]   // ← the ONLY true cell
        [InlineData(CompassDiscModeEnum.Both, false, false, false)]
        [InlineData(CompassDiscModeEnum.Both, false, true, false)]
        [InlineData(CompassDiscModeEnum.Both, true, false, false)]
        [InlineData(CompassDiscModeEnum.Both, true, true, false)]
        public void HideHudNeedle_is_true_only_for_DiscWhenBound_worn_and_surface_showing(
            CompassDiscModeEnum mode, bool worn, bool surfaceShowing, bool expectedHide)
        {
            var plan = CompassNorthGate.Resolve(surfaceShowing: surfaceShowing, compassWorn: worn, mode: mode);
            Assert.Equal(expectedHide, plan.HideHudNeedle);
        }

        // ── The needle never hides while the surface ring is OFF (you can't "hand off" to a ring that
        //    isn't drawn). This is the AT-COMPASS-DISC-PUMP companion guard: a hidden needle ALWAYS
        //    implies a shown surface ring, so closing the surface restores the needle (no dead frame).

        [Theory]
        [InlineData(CompassDiscModeEnum.HudOnly)]
        [InlineData(CompassDiscModeEnum.DiscWhenBound)]
        [InlineData(CompassDiscModeEnum.Both)]
        public void Needle_never_hides_without_a_surface_ring(CompassDiscModeEnum mode)
        {
            foreach (var worn in new[] { false, true })
            foreach (var surface in new[] { false, true })
            {
                var plan = CompassNorthGate.Resolve(surfaceShowing: surface, compassWorn: worn, mode: mode);
                if (plan.HideHudNeedle)
                    Assert.True(plan.ShowSurfaceRing,
                        $"HideHudNeedle implies ShowSurfaceRing (mode={mode}, worn={worn}, surface={surface})");
            }
        }
    }
}
