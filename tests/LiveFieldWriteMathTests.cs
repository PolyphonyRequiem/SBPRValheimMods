// ============================================================================
//  LiveFieldWriteMath — xUnit structural tests for the live-update WRITE axis
//  (card t_9c54d492, live-update-cartography-impl-spec.md §2/§3/§5).
// ----------------------------------------------------------------------------
//  Tests the PURE, engine-free pieces the live field-write axis is built on,
//  link-compiled from the SHIPPED BoundedMapMath (../src, not copied — see the
//  .csproj). These three helpers are exactly the parts the card asks to "test
//  headless": the in-region write-set pre-filter (§2.1 step 3), the grid-cell
//  bound-map match that keys both the merge guard and the Table ingest (§5), and
//  the flip-detecting OR that is the dirty-check / perf gate (§2.3).
//
//  Because SurveyData.MergeFrom(out changed) delegates its fog loop to
//  BoundedMapMath.OrMergeFog, the shipped dirty-check IS the behaviour asserted
//  here — there is no second copy to drift.
//
//  Vanilla constants (Minimap.m_textureSize=256, m_pixelSize=64f) per the
//  BoundedMapMath header, confirmed against assembly_valheim.
// ============================================================================

using SBPR.Trailborne.Features.Cartography;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class LiveFieldWriteMathTests
    {
        private const float PixelSize = 64f;
        private const int TextureSize = 256;
        private const float R = 1000f; // the locked survey radius (LocalMapController.SurveyRadiusMeters)

        // ── In-region write-set pre-filter (§2.1 step 3 / AT-LIVE-OUTREGION) ─────

        [Fact]
        public void InRegion_player_at_origin_is_in_region()
        {
            // Standing on the bound origin is trivially inside the 1000 m disc.
            Assert.True(BoundedMapMath.InRegionForLiveWrite(0f, 0f, 0f, 0f, R, PixelSize));
        }

        [Fact]
        public void InRegion_player_just_inside_radius_is_in_region()
        {
            // 999 m east of a map bound at origin → inside the 1000 m disc → written.
            Assert.True(BoundedMapMath.InRegionForLiveWrite(999f, 0f, 0f, 0f, R, PixelSize));
        }

        [Fact]
        public void InRegion_player_far_outside_radius_is_excluded()
        {
            // 2000 m away from the map's region → NOT written (AT-LIVE-OUTREGION): the player
            // left the disc, the pre-filter skips the array build for that map.
            Assert.False(BoundedMapMath.InRegionForLiveWrite(2000f, 0f, 0f, 0f, R, PixelSize));
        }

        [Fact]
        public void InRegion_has_one_cell_rim_margin_to_avoid_edge_flicker()
        {
            // The filter is widened by exactly one pixelSize (64 m) so a player walking the rim
            // doesn't flicker in/out of the write-set. At R + 0.5·pixelSize (1032 m) the raw disc
            // (1000 m) would EXCLUDE, but the margined filter still INCLUDES.
            float justPastRaw = R + PixelSize * 0.5f; // 1032 m
            Assert.False(BoundedMapMath.InDisc(justPastRaw, 0f, 0f, 0f, R));                       // raw disc: out
            Assert.True(BoundedMapMath.InRegionForLiveWrite(justPastRaw, 0f, 0f, 0f, R, PixelSize)); // margined: in

            // ...but the margin is bounded — well past R + pixelSize is still excluded.
            Assert.False(BoundedMapMath.InRegionForLiveWrite(R + PixelSize + 50f, 0f, 0f, 0f, R, PixelSize));
        }

        // ── Grid-cell bound-map match (§5 — ingest key + merge guard) ────────────

        [Fact]
        public void SameOriginCell_identical_origins_match()
        {
            Assert.True(BoundedMapMath.SameOriginCell(0f, 0f, 0f, 0f, PixelSize, TextureSize));
        }

        [Fact]
        public void SameOriginCell_sub_cell_drift_still_matches_rebuild_readoption()
        {
            // A Table rebuilt a few metres off the original lands in the SAME 64 m fog cell →
            // re-adopts its bound maps (AT-INGEST-REBUILD). 0 and 30 both map to cell 128
            // (30/64 + 128 = 128.47 → 128), so the bound map matches the rebuilt Table.
            Assert.True(BoundedMapMath.SameOriginCell(0f, 0f, 30f, 30f, PixelSize, TextureSize));
        }

        [Fact]
        public void SameOriginCell_different_cell_does_not_match_accepted_orphan()
        {
            // A Table rebuilt a FULL cell away (64 m) lands in a different cell (129) → does NOT
            // re-adopt the map bound to the original cell (128). Accepted orphan (design §4.0a).
            Assert.False(BoundedMapMath.SameOriginCell(0f, 0f, 64f, 0f, PixelSize, TextureSize));
        }

        // ── Flip-detecting OR dirty-check (§2.3 — the perf gate) ─────────────────

        [Fact]
        public void OrMergeFog_new_ground_lights_cells_and_reports_changed()
        {
            var dst = new bool[] { false, false, false, false };
            var src = new bool[] { false, true, false, true };

            BoundedMapMath.OrMergeFog(dst, src, out bool changed);

            Assert.True(changed);                                  // two cells flipped false→true
            Assert.Equal(new[] { false, true, false, true }, dst); // dst now lit where src was
        }

        [Fact]
        public void OrMergeFog_recovering_known_ground_reports_unchanged()
        {
            // The steady state: src is a subset of what dst already has lit → nothing flips →
            // changed == false → the caller SKIPS the reserialize+rewrite (zero-write steady state).
            var dst = new bool[] { true, true, false, true };
            var src = new bool[] { true, false, false, true };

            BoundedMapMath.OrMergeFog(dst, src, out bool changed);

            Assert.False(changed);
            Assert.Equal(new[] { true, true, false, true }, dst); // dst is unchanged
        }

        [Fact]
        public void OrMergeFog_never_clears_an_already_lit_cell()
        {
            // OR semantics: a cell lit in dst but dark in src STAYS lit (fog never regresses).
            var dst = new bool[] { true, false };
            var src = new bool[] { false, false };

            BoundedMapMath.OrMergeFog(dst, src, out bool changed);

            Assert.False(changed);
            Assert.Equal(new[] { true, false }, dst);
        }

        [Fact]
        public void OrMergeFog_length_mismatch_does_not_throw()
        {
            // Defensive: merges over min(dst,src). (Same-cell windows are always equal length, but
            // a guard means a future grid change can't crash the hot path.)
            var dst = new bool[] { false, false };
            var src = new bool[] { true, true, true };

            BoundedMapMath.OrMergeFog(dst, src, out bool changed);

            Assert.True(changed);
            Assert.Equal(new[] { true, true }, dst);
        }
    }
}
