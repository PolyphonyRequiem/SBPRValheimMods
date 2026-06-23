// ============================================================================
//  LensHandoffDecision — xUnit structural tests (card t_91e86951).
// ----------------------------------------------------------------------------
//  Tests the SHIPPED pure decision logic LensHandoffDecision (link-compiled from
//  ../src, not copied — see the .csproj). This is the durable CI fence for the
//  Sunstone Lens → minimap handoff: it pins the FULL truth table that decides
//  which surface draws Lens threats and whether the ring's content is visible,
//  WITHOUT touching any volatile UnityEngine/Harmony internals (the engine-free
//  link-compile pattern shared with DiscRingGeometry / BoundedMapMath).
//
//  WHY THIS MATTERS. The surface cascade + MinimapHandoffMode interaction is the
//  load-bearing policy of the whole card (Daniel gated MinimapHandoffMode =
//  DiscWhenBound — the ring is the no-minimap fallback only). Two failure classes
//  this fence catches before they ship:
//    • AT-LENS-DISC-NODRIFT (policy half) — a future edit that makes the ring
//      inert when no minimap is present (regressing the #209-adjacent "renders
//      nothing" class), or that forgets to feed the minimap when it should.
//    • The universal "any minimap present → handoff" rule — that BOTH the SBPR
//      disc (nomap-ON) and the vanilla minimap (nomap-OFF) trigger the handoff,
//      and the ring only wins when NO minimap exists at all.
//
//  Every assertion is derived from the design doc's §4 truth table; none are magic.
// ============================================================================

using SBPR.Trailborne.Features.Sunstone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class LensHandoffDecisionTests
    {
        // ── ResolveSurface: the world-state cascade (design §1 table) ────────────────────────────
        // SBPR disc bound wins; else vanilla minimap showing; else the ring (no minimap anywhere).

        [Fact]
        public void ResolveSurface_no_minimap_present_is_the_ring_fallback()
            => Assert.Equal(LensSurface.Ring, LensHandoffDecision.ResolveSurface(sbprDiscBound: false, vanillaMinimapShowing: false));

        [Fact]
        public void ResolveSurface_sbpr_disc_bound_is_the_disc()
            => Assert.Equal(LensSurface.SbprDisc, LensHandoffDecision.ResolveSurface(sbprDiscBound: true, vanillaMinimapShowing: false));

        [Fact]
        public void ResolveSurface_vanilla_minimap_showing_is_the_vanilla_surface()
            => Assert.Equal(LensSurface.VanillaMinimap, LensHandoffDecision.ResolveSurface(sbprDiscBound: false, vanillaMinimapShowing: true));

        [Fact]
        public void ResolveSurface_sbpr_disc_wins_the_impossible_tie()
            // The two are mutually exclusive by vanilla construction (SetMapMode forces None under
            // Game.m_noMap), but if both ever read true the richer SBPR disc must win — never split.
            => Assert.Equal(LensSurface.SbprDisc, LensHandoffDecision.ResolveSurface(sbprDiscBound: true, vanillaMinimapShowing: true));

        // ── Resolve: the ring is ALWAYS the fallback when no minimap is present, every mode ───────

        [Theory]
        [InlineData(MinimapHandoffMode.RingOnly)]
        [InlineData(MinimapHandoffMode.DiscWhenBound)]
        [InlineData(MinimapHandoffMode.Both)]
        public void Ring_surface_always_shows_ring_and_never_feeds_minimap(MinimapHandoffMode mode)
        {
            var plan = LensHandoffDecision.Resolve(LensSurface.Ring, mode);
            Assert.True(plan.RingContentVisible);
            Assert.False(plan.FeedMinimap);
        }

        // ── Resolve: DiscWhenBound (the gated DEFAULT) — ring hides, the minimap is fed ───────────

        [Theory]
        [InlineData(LensSurface.SbprDisc)]
        [InlineData(LensSurface.VanillaMinimap)]
        public void DiscWhenBound_hands_off_to_either_minimap_and_hides_the_ring(LensSurface surface)
        {
            var plan = LensHandoffDecision.Resolve(surface, MinimapHandoffMode.DiscWhenBound);
            Assert.False(plan.RingContentVisible);   // ring hides (the handoff)
            Assert.True(plan.FeedMinimap);           // the minimap surface shows threats
            Assert.Equal(surface, plan.MinimapTarget);
        }

        // ── Resolve: RingOnly (escape hatch) — ring stays, the minimap is NOT fed ─────────────────

        [Theory]
        [InlineData(LensSurface.SbprDisc)]
        [InlineData(LensSurface.VanillaMinimap)]
        public void RingOnly_keeps_the_ring_and_suppresses_the_minimap(LensSurface surface)
        {
            var plan = LensHandoffDecision.Resolve(surface, MinimapHandoffMode.RingOnly);
            Assert.True(plan.RingContentVisible);    // ring always renders
            Assert.False(plan.FeedMinimap);          // minimap suppressed
        }

        // ── Resolve: Both (supplement) — ring stays AND the minimap is fed ────────────────────────

        [Theory]
        [InlineData(LensSurface.SbprDisc)]
        [InlineData(LensSurface.VanillaMinimap)]
        public void Both_shows_the_ring_and_feeds_the_minimap(LensSurface surface)
        {
            var plan = LensHandoffDecision.Resolve(surface, MinimapHandoffMode.Both);
            Assert.True(plan.RingContentVisible);    // ring stays
            Assert.True(plan.FeedMinimap);           // AND the minimap shows threats
            Assert.Equal(surface, plan.MinimapTarget);
        }

        // ── The universal rule: the vanilla minimap (nomap-OFF) gets the handoff EXACTLY like the
        //    SBPR disc — Daniel's "any minimap present, for any reason" override. The two surfaces
        //    must produce IDENTICAL ring/feed plans under each mode (only the target differs). ─────

        [Theory]
        [InlineData(MinimapHandoffMode.RingOnly)]
        [InlineData(MinimapHandoffMode.DiscWhenBound)]
        [InlineData(MinimapHandoffMode.Both)]
        public void Vanilla_minimap_and_sbpr_disc_get_the_same_handoff_treatment(MinimapHandoffMode mode)
        {
            var disc = LensHandoffDecision.Resolve(LensSurface.SbprDisc, mode);
            var vanilla = LensHandoffDecision.Resolve(LensSurface.VanillaMinimap, mode);
            Assert.Equal(disc.RingContentVisible, vanilla.RingContentVisible);
            Assert.Equal(disc.FeedMinimap, vanilla.FeedMinimap);
        }

        // ── End-to-end: the one-shot convenience overload composes the cascade + the mode ─────────

        [Fact]
        public void EndToEnd_default_mode_nomap_off_renders_on_the_vanilla_minimap()
        {
            // nomap-OFF: vanilla corner map showing, no SBPR disc, default DiscWhenBound.
            var plan = LensHandoffDecision.Resolve(sbprDiscBound: false, vanillaMinimapShowing: true,
                                                   mode: MinimapHandoffMode.DiscWhenBound);
            Assert.False(plan.RingContentVisible);                    // ring hidden (NOT "ring stays")
            Assert.True(plan.FeedMinimap);
            Assert.Equal(LensSurface.VanillaMinimap, plan.MinimapTarget);
        }

        [Fact]
        public void EndToEnd_default_mode_no_minimap_keeps_the_ring()
        {
            var plan = LensHandoffDecision.Resolve(sbprDiscBound: false, vanillaMinimapShowing: false,
                                                   mode: MinimapHandoffMode.DiscWhenBound);
            Assert.True(plan.RingContentVisible);
            Assert.False(plan.FeedMinimap);
        }

        [Fact]
        public void EndToEnd_default_mode_nomap_on_bound_disc_renders_on_the_disc()
        {
            var plan = LensHandoffDecision.Resolve(sbprDiscBound: true, vanillaMinimapShowing: false,
                                                   mode: MinimapHandoffMode.DiscWhenBound);
            Assert.False(plan.RingContentVisible);
            Assert.True(plan.FeedMinimap);
            Assert.Equal(LensSurface.SbprDisc, plan.MinimapTarget);
        }

        // ── Corona persistence (card t_7416e5b9): the "lens is live" cue is DECOUPLED from the threat
        //    trophies. The corona shows whenever the ring's content shows, OR — when a minimap owns the
        //    threat feed and the ring is hidden — whenever the persist knob is ON (default). This is the
        //    re-homed survivor of the flat-ring pulsing-aura idea (t_acaa0190): the lens reads "live"
        //    regardless of which surface draws threats, instead of going dark on the minimap path. ──────

        [Fact]
        public void Corona_shows_on_the_no_minimap_ring_fallback_every_mode_and_either_persist_setting()
        {
            // The ring fallback always carries the corona (same RenderWorldHalo path) — persistence knob irrelevant.
            foreach (var mode in new[] { MinimapHandoffMode.RingOnly, MinimapHandoffMode.DiscWhenBound, MinimapHandoffMode.Both })
            foreach (var persist in new[] { true, false })
                Assert.True(LensHandoffDecision.Resolve(LensSurface.Ring, mode, persist).CoronaContentVisible);
        }

        [Theory]
        [InlineData(LensSurface.SbprDisc)]
        [InlineData(LensSurface.VanillaMinimap)]
        public void Corona_persists_on_the_minimap_handoff_when_the_knob_is_on(LensSurface surface)
        {
            // DiscWhenBound hides the ring's trophies, but the corona stays lit (knob ON = default).
            var plan = LensHandoffDecision.Resolve(surface, MinimapHandoffMode.DiscWhenBound, coronaPersistsOnMinimap: true);
            Assert.False(plan.RingContentVisible);     // threats handed to the minimap
            Assert.True(plan.CoronaContentVisible);    // but the lens-live cue persists
        }

        [Theory]
        [InlineData(LensSurface.SbprDisc)]
        [InlineData(LensSurface.VanillaMinimap)]
        public void Corona_goes_dark_on_the_minimap_handoff_when_the_knob_is_off(LensSurface surface)
        {
            // Knob OFF restores the pre-card behaviour: corona dark whenever a minimap owns detection.
            var plan = LensHandoffDecision.Resolve(surface, MinimapHandoffMode.DiscWhenBound, coronaPersistsOnMinimap: false);
            Assert.False(plan.RingContentVisible);
            Assert.False(plan.CoronaContentVisible);   // dark with the trophies
        }

        [Theory]
        [InlineData(MinimapHandoffMode.RingOnly)]
        [InlineData(MinimapHandoffMode.Both)]
        public void Corona_shows_whenever_the_ring_content_shows_regardless_of_persist_knob(MinimapHandoffMode mode)
        {
            // RingOnly + Both both keep the ring visible on a minimap surface; the corona rides it whether
            // or not the persist knob is on (corona = ringContentVisible || persist).
            foreach (var surface in new[] { LensSurface.SbprDisc, LensSurface.VanillaMinimap })
            foreach (var persist in new[] { true, false })
            {
                var plan = LensHandoffDecision.Resolve(surface, mode, persist);
                Assert.True(plan.RingContentVisible);
                Assert.True(plan.CoronaContentVisible);
            }
        }

        [Fact]
        public void Corona_default_overload_persists_on_the_minimap_handoff()
        {
            // The convenience overload defaults coronaPersistsOnMinimap to true (the shipped default).
            var plan = LensHandoffDecision.Resolve(sbprDiscBound: true, vanillaMinimapShowing: false,
                                                   mode: MinimapHandoffMode.DiscWhenBound);
            Assert.False(plan.RingContentVisible);
            Assert.True(plan.CoronaContentVisible);
        }
    }
}
