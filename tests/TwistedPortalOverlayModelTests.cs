// ============================================================================
//  TwistedPortalOverlayModel — xUnit structural tests (card t_e732bd8b / C3).
// ----------------------------------------------------------------------------
//  Tests the SHIPPED pure selection/format logic TwistedPortalOverlayModel
//  (link-compiled from ../src, not copied — see the .csproj). This is the durable
//  CI fence for the on-step Twisted Portal overlay's NON-VISUAL contract: the
//  nearest-N cap, the radius filter, the unnamed-skip policy, and the distance
//  text formatting — WITHOUT touching the world-space render (Canvas / UI.Text /
//  the through-terrain ZTest-Always material), which is a GPU-client eyeball
//  (AT-OVERLAY) that cannot be asserted headless.
//
//  The engine-free link-compile pattern shared with SunstoneHaloGeometry /
//  DiscRingGeometry / LensHandoffDecision: the asserted behaviour IS the shipped
//  behaviour (one copy, no fork), and it runs under net8.0 in CI with no Valheim
//  SDK fetched.
//
//  What these pin (the model half of AT-OVERLAY):
//    • nearest-first ordering + the maxLabels cap keeps the CLOSEST N (horde guard);
//    • the overlay-radius filter excludes portals beyond reach;
//    • unnamed portals are skipped unless includeUnnamed (informational, can't pair);
//    • the distance text reads "142m" / "1.4km" invariant-culture, never "1,4km";
//    • the label text is rune-on-top, range-below, with the unnamed placeholder.
// ============================================================================

using System.Collections.Generic;
using SBPR.Trailborne.Features.Portals;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class TwistedPortalOverlayModelTests
    {
        private static OverlayCandidate Named(float d)   => new OverlayCandidate(d, hasRune: true);
        private static OverlayCandidate Unnamed(float d) => new OverlayCandidate(d, hasRune: false);

        // ── nearest-N + the cap (AT-OVERLAY: a horde of portals shows the closest N) ─────────────

        [Fact]
        public void Select_orders_nearest_first()
        {
            var cands = new List<OverlayCandidate> { Named(120f), Named(10f), Named(85f) };
            var into = new List<int>();
            TwistedPortalOverlayModel.SelectNearest(cands, 300f, 12, true, into);
            // indices ordered by distance: 1 (10m), 2 (85m), 0 (120m)
            Assert.Equal(new[] { 1, 2, 0 }, into.ToArray());
        }

        [Fact]
        public void Select_caps_at_max_labels_keeping_the_closest()
        {
            var cands = new List<OverlayCandidate>
            {
                Named(50f), Named(5f), Named(200f), Named(25f), Named(150f),
            };
            var into = new List<int>();
            TwistedPortalOverlayModel.SelectNearest(cands, 300f, 2, true, into);
            // keep the two closest: 5m (idx 1) then 25m (idx 3)
            Assert.Equal(new[] { 1, 3 }, into.ToArray());
        }

        [Fact]
        public void Select_zero_or_negative_cap_yields_nothing()
        {
            var cands = new List<OverlayCandidate> { Named(10f), Named(20f) };
            var into = new List<int>();
            TwistedPortalOverlayModel.SelectNearest(cands, 300f, 0, true, into);
            Assert.Empty(into);
            TwistedPortalOverlayModel.SelectNearest(cands, 300f, -3, true, into);
            Assert.Empty(into);
        }

        // ── the overlay-radius filter (spec §7.2: within OverlayRadius) ──────────────────────────

        [Fact]
        public void Select_excludes_portals_beyond_the_radius()
        {
            var cands = new List<OverlayCandidate> { Named(290f), Named(310f), Named(301f) };
            var into = new List<int>();
            TwistedPortalOverlayModel.SelectNearest(cands, 300f, 12, true, into);
            Assert.Equal(new[] { 0 }, into.ToArray());   // only the 290m one is within 300m
        }

        [Fact]
        public void Select_includes_a_portal_exactly_on_the_radius()
        {
            var cands = new List<OverlayCandidate> { Named(300f) };
            var into = new List<int>();
            TwistedPortalOverlayModel.SelectNearest(cands, 300f, 12, true, into);
            Assert.Single(into);   // boundary is inclusive (<= radius)
        }

        // ── the unnamed-skip policy (Model A: unnamed portals can't pair; informational only) ────

        [Fact]
        public void Select_skips_unnamed_when_not_requested()
        {
            var cands = new List<OverlayCandidate> { Unnamed(10f), Named(20f), Unnamed(5f) };
            var into = new List<int>();
            TwistedPortalOverlayModel.SelectNearest(cands, 300f, 12, includeUnnamed: false, into);
            Assert.Equal(new[] { 1 }, into.ToArray());   // only the named portal at 20m
        }

        [Fact]
        public void Select_includes_unnamed_when_requested()
        {
            var cands = new List<OverlayCandidate> { Unnamed(10f), Named(20f), Unnamed(5f) };
            var into = new List<int>();
            TwistedPortalOverlayModel.SelectNearest(cands, 300f, 12, includeUnnamed: true, into);
            // nearest-first across both kinds: 5m (idx 2), 10m (idx 0), 20m (idx 1)
            Assert.Equal(new[] { 2, 0, 1 }, into.ToArray());
        }

        [Fact]
        public void Select_clears_the_output_list_first()
        {
            var into = new List<int> { 99, 98 };   // stale content from a prior refresh
            TwistedPortalOverlayModel.SelectNearest(new List<OverlayCandidate>(), 300f, 12, true, into);
            Assert.Empty(into);
        }

        [Fact]
        public void Select_null_or_empty_input_is_safe()
        {
            var into = new List<int>();
            TwistedPortalOverlayModel.SelectNearest(null!, 300f, 12, true, into);
            Assert.Empty(into);
            TwistedPortalOverlayModel.SelectNearest(new List<OverlayCandidate>(), 300f, 12, true, into);
            Assert.Empty(into);
        }

        [Fact]
        public void Select_tie_on_distance_is_stable_on_input_order()
        {
            var cands = new List<OverlayCandidate> { Named(50f), Named(50f), Named(50f) };
            var into = new List<int>();
            TwistedPortalOverlayModel.SelectNearest(cands, 300f, 12, true, into);
            Assert.Equal(new[] { 0, 1, 2 }, into.ToArray());   // equidistant → original order, no flicker
        }

        // ── distance formatting (invariant-culture: "142m" / "1.4km", never "1,4km") ─────────────

        [Theory]
        [InlineData(0f, "0m")]
        [InlineData(3.2f, "3m")]
        [InlineData(141.6f, "142m")]    // rounds to nearest metre
        [InlineData(999f, "999m")]
        [InlineData(1000f, "1.0km")]
        [InlineData(1440f, "1.4km")]    // one-decimal km
        [InlineData(2950f, "3.0km")]    // 2.95 rounds half-away-from-zero → 3.0
        public void FormatDistance_reads_human(float meters, string expected)
        {
            Assert.Equal(expected, TwistedPortalOverlayModel.FormatDistance(meters));
        }

        [Fact]
        public void FormatDistance_clamps_negative_to_zero()
        {
            Assert.Equal("0m", TwistedPortalOverlayModel.FormatDistance(-50f));
        }

        // ── label text (rune-on-top, range-below; unnamed placeholder) ───────────────────────────

        [Fact]
        public void BuildLabel_named_with_distance_is_two_lines()
        {
            string s = TwistedPortalOverlayModel.BuildLabel("Eld余", true, 142f, showDistance: true);
            Assert.Equal("Eld余\n142m", s);
        }

        [Fact]
        public void BuildLabel_named_without_distance_is_just_the_rune()
        {
            string s = TwistedPortalOverlayModel.BuildLabel("Mistmark", true, 142f, showDistance: false);
            Assert.Equal("Mistmark", s);
        }

        [Fact]
        public void BuildLabel_unnamed_uses_the_placeholder()
        {
            string s = TwistedPortalOverlayModel.BuildLabel("", false, 80f, showDistance: true);
            Assert.Equal(TwistedPortalOverlayModel.UnnamedPlaceholder + "\n80m", s);
        }

        [Fact]
        public void BuildLabel_blank_rune_but_hasRune_falls_back_to_placeholder()
        {
            // Defensive: hasRune true but the string is empty/whitespace-from-censor → placeholder,
            // never a blank floating label.
            string s = TwistedPortalOverlayModel.BuildLabel("", true, 10f, showDistance: false);
            Assert.Equal(TwistedPortalOverlayModel.UnnamedPlaceholder, s);
        }

        [Fact]
        public void Locked_defaults_are_the_spec_numbers()
        {
            // Guard the spec §7 anchors so a careless edit trips CI: 300 m reach, ~3 m proximity.
            Assert.Equal(300f, TwistedPortalOverlayModel.DefaultOverlayRadius, 5);
            Assert.Equal(3f, TwistedPortalOverlayModel.DefaultProximityRange, 5);
        }
    }
}
