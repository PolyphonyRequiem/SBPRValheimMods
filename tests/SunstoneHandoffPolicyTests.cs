// ============================================================================
//  SunstoneHandoffPolicy — xUnit decision-table tests (card t_54c989d3).
// ----------------------------------------------------------------------------
//  Tests the SHIPPED engine-free decision logic SunstoneHandoffPolicy (link-
//  compiled from ../src, not copied — see the .csproj), the §4 MinimapHandoffMode
//  load-bearer. This is the durable CI fence for the locked, Daniel-gated
//  AT-LENS-DISC-HANDOFF decision table: it pins WHERE the Sunstone Lens renders
//  threats (ring vs minimap) as a pure function of (mode, anyMinimapPresent),
//  WITHOUT touching any UnityEngine / Harmony / Minimap internals (the same
//  engine-free link-compile pattern as BoundedMapMath / DiscRingGeometry /
//  MapCaptionText).
//
//  WHY THIS MATTERS. The handoff routing is the single highest-risk decision in
//  the card (it gates the #209 ring-pump guard and the 3-surface handoff). The
//  decision table is small and total, so it is exhaustively unit-checkable here —
//  every (mode × present) cell is asserted against the locked design doc §4/§5,
//  so the table can never silently drift. (The Unity-side rendering — the actual
//  ring/disc/vanilla draw + the #209 pump-alive guarantee — is Daniel's in-game
//  eyeball; logs-green ≠ playable. These tests fence the DECISION, not the draw.)
//
//  THE LOCKED TABLE (design sunstone-lens-minimap-handoff.md §4, §5, §8):
//    mode=RingOnly      → ring ALWAYS shows threats; minimap NEVER hosts them.
//    mode=DiscWhenBound → present? ring HIDES threats, minimap hosts them.
//                         absent?  ring SHOWS threats (the no-minimap fallback).
//    mode=Both          → present? ring AND minimap both show threats.
//                         absent?  ring shows threats; minimap (none) hosts none.
// ============================================================================

using SBPR.Trailborne.Features.Sunstone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class SunstoneHandoffPolicyTests
    {
        // ── RingShowsThreats: the ring's visuals are on iff this is true (the #209-safe path hides
        //    _content's threat visuals when false; the pump stays alive regardless). ──

        [Theory]
        // RingOnly — the escape hatch: ring ALWAYS shows, minimap presence irrelevant.
        [InlineData(MinimapHandoffMode.RingOnly,      true,  true)]
        [InlineData(MinimapHandoffMode.RingOnly,      false, true)]
        // DiscWhenBound (DEFAULT) — ring shows ONLY when no minimap is present (fallback).
        [InlineData(MinimapHandoffMode.DiscWhenBound, true,  false)]
        [InlineData(MinimapHandoffMode.DiscWhenBound, false, true)]
        // Both — ring ALWAYS shows (it supplements the minimap, never replaced).
        [InlineData(MinimapHandoffMode.Both,          true,  true)]
        [InlineData(MinimapHandoffMode.Both,          false, true)]
        public void RingShowsThreats_matches_locked_table(MinimapHandoffMode mode, bool present, bool expected)
            => Assert.Equal(expected, SunstoneHandoffPolicy.RingShowsThreats(mode, present));

        // ── MinimapShowsThreats: a PRESENT minimap surface hosts threats iff this is true. Always
        //    false when no minimap is present (there is no surface to host them). ──

        [Theory]
        // RingOnly — the minimap NEVER hosts threats (they stay on the ring).
        [InlineData(MinimapHandoffMode.RingOnly,      true,  false)]
        [InlineData(MinimapHandoffMode.RingOnly,      false, false)]
        // DiscWhenBound — the minimap hosts threats whenever one is present.
        [InlineData(MinimapHandoffMode.DiscWhenBound, true,  true)]
        [InlineData(MinimapHandoffMode.DiscWhenBound, false, false)]
        // Both — the minimap hosts threats whenever one is present.
        [InlineData(MinimapHandoffMode.Both,          true,  true)]
        [InlineData(MinimapHandoffMode.Both,          false, false)]
        public void MinimapShowsThreats_matches_locked_table(MinimapHandoffMode mode, bool present, bool expected)
            => Assert.Equal(expected, SunstoneHandoffPolicy.MinimapShowsThreats(mode, present));

        // ── Cross-cutting invariants the table must satisfy (the design's prose, asserted) ──

        /// <summary>
        /// When NO minimap is present, a threat is ALWAYS visible SOMEWHERE in every mode — the ring is
        /// the universal fallback (design §1: "the ring is the no-minimap fallback only"). A mode that
        /// hid threats on every surface with no minimap up would be a detection blackout — forbidden.
        /// </summary>
        [Theory]
        [InlineData(MinimapHandoffMode.RingOnly)]
        [InlineData(MinimapHandoffMode.DiscWhenBound)]
        [InlineData(MinimapHandoffMode.Both)]
        public void No_minimap_present_always_shows_threats_on_the_ring(MinimapHandoffMode mode)
        {
            // With no minimap, the minimap can't host (no surface), so the ring MUST show — always.
            Assert.False(SunstoneHandoffPolicy.MinimapShowsThreats(mode, anyMinimapPresent: false));
            Assert.True(SunstoneHandoffPolicy.RingShowsThreats(mode, anyMinimapPresent: false));
        }

        /// <summary>
        /// DiscWhenBound (the Daniel-gated DEFAULT) is a true HANDOFF when a minimap is present: the ring
        /// hides its threats and the minimap takes them — they do not BOTH show (that's the Both mode).
        /// This is the locked §4 distinction between DiscWhenBound (replace) and Both (supplement).
        /// </summary>
        [Fact]
        public void DiscWhenBound_with_minimap_is_a_handoff_not_a_supplement()
        {
            Assert.False(SunstoneHandoffPolicy.RingShowsThreats(MinimapHandoffMode.DiscWhenBound, anyMinimapPresent: true));
            Assert.True(SunstoneHandoffPolicy.MinimapShowsThreats(MinimapHandoffMode.DiscWhenBound, anyMinimapPresent: true));
        }

        /// <summary>
        /// Both with a minimap present shows threats on BOTH surfaces (the supplement mode) — distinct
        /// from DiscWhenBound's exclusive handoff. The §4 escape hatches stay meaningfully different.
        /// </summary>
        [Fact]
        public void Both_with_minimap_shows_on_both_surfaces()
        {
            Assert.True(SunstoneHandoffPolicy.RingShowsThreats(MinimapHandoffMode.Both, anyMinimapPresent: true));
            Assert.True(SunstoneHandoffPolicy.MinimapShowsThreats(MinimapHandoffMode.Both, anyMinimapPresent: true));
        }

        /// <summary>
        /// RingOnly (the escape hatch) never lets the minimap host threats, even when one is present —
        /// the feature is effectively "ring as it always was." This is the §4 RingOnly contract.
        /// </summary>
        [Fact]
        public void RingOnly_never_hands_off_even_with_minimap()
        {
            Assert.True(SunstoneHandoffPolicy.RingShowsThreats(MinimapHandoffMode.RingOnly, anyMinimapPresent: true));
            Assert.False(SunstoneHandoffPolicy.MinimapShowsThreats(MinimapHandoffMode.RingOnly, anyMinimapPresent: true));
        }

        /// <summary>
        /// The Daniel-gated defaults are pinned here so a future enum reorder / default change is caught
        /// by CI, not discovered in-game: DiscWhenBound (handoff) + DotsAndTint (minimap blip style).
        /// </summary>
        [Fact]
        public void Gated_defaults_are_DiscWhenBound_and_DotsAndTint()
        {
            Assert.Equal(MinimapHandoffMode.DiscWhenBound, SunstoneHandoffPolicy.DefaultMode);
            Assert.Equal(BlipStyle.DotsAndTint, SunstoneHandoffPolicy.DefaultBlipStyle);
        }
    }
}
