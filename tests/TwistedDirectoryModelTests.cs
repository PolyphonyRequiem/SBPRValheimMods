// ============================================================================
//  TwistedDirectoryModel — xUnit structural tests (card t_ccb454f8 / L2).
// ----------------------------------------------------------------------------
//  Tests the SHIPPED pure server-side slice policy + radius clamp behind the L2
//  server-authoritative candidate set (TwistedDirectoryModel, link-compiled from
//  ../src — not copied, see the .csproj). This is the durable CI fence for the
//  NON-NETWORK core of AT-PICK-LONGRANGE: the server filters its full Twisted-
//  Portal set to the within-range slice it ships to a stepping client (§2), and
//  a malformed/hostile request radius can never make the server serialize the
//  whole world (clamped into [Min, Max]).
//
//  The engine-free link-compile pattern shared with AimPickMath / PortalEnergyMath
//  / TwistedPortalOverlayModel: the asserted behaviour IS the shipped behaviour
//  (one copy, no fork), runs under net8.0 in CI with no Valheim SDK.
//
//  What these pin (the pure-logic half of AT-PICK-LONGRANGE / §2):
//    • the radius slice keeps portals within R and drops those beyond (PLANAR x/z);
//    • a far portal (>128 m, the client window the old walk couldn't see) is
//      INCLUDED when within the requested reach — the whole point of L2;
//    • the requested radius is clamped server-side: a huge ask gets the ceiling,
//      a zero/negative/NaN ask gets the floor (never an empty world-serialize);
//    • BuildSlice clears the output and returns the clamped radius applied.
// ============================================================================

using System.Collections.Generic;
using SBPR.Trailborne.Features.Portals;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class TwistedDirectoryModelTests
    {
        private static DirectoryRow RowAt(float x, float z, string rune = "", uint id = 1u)
            => new DirectoryRow(x, 0f, z, 0f, 0f, 0f, 1f, rune, 0L, id);

        // ── radius clamp band ───────────────────────────────────────────────────────────────────

        [Fact]
        public void ClampSliceRadius_passes_a_value_inside_the_band()
        {
            Assert.Equal(300f, TwistedDirectoryModel.ClampSliceRadius(300f));
        }

        [Fact]
        public void ClampSliceRadius_caps_a_huge_request_at_the_ceiling()
        {
            // A request to serialize the whole world is clamped DOWN, never honored.
            Assert.Equal(TwistedDirectoryModel.MaxSliceRadiusMeters,
                         TwistedDirectoryModel.ClampSliceRadius(1_000_000f));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-50f)]
        [InlineData(1f)]   // below the floor
        public void ClampSliceRadius_lifts_a_too_small_request_to_the_floor(float requested)
        {
            // A zero/negative/tiny radius must NOT silently produce an empty set (that looks exactly
            // like "no portals" — the §2 bug). It gets a usable floor instead.
            Assert.Equal(TwistedDirectoryModel.MinSliceRadiusMeters,
                         TwistedDirectoryModel.ClampSliceRadius(requested));
        }

        [Fact]
        public void ClampSliceRadius_routes_non_finite_to_the_floor()
        {
            Assert.Equal(TwistedDirectoryModel.MinSliceRadiusMeters,
                         TwistedDirectoryModel.ClampSliceRadius(float.NaN));
            Assert.Equal(TwistedDirectoryModel.MinSliceRadiusMeters,
                         TwistedDirectoryModel.ClampSliceRadius(float.PositiveInfinity));
        }

        // ── planar within-slice test ────────────────────────────────────────────────────────────

        [Fact]
        public void WithinSlice_uses_planar_distance_and_ignores_height()
        {
            // A portal 10 m away on the map but 500 m up a cliff is still "near" — the overlay/aim use
            // horizontal reach. (The row's y never enters WithinSlice; this asserts the x/z-only rule.)
            Assert.True(TwistedDirectoryModel.WithinSlice(10f, 0f, 0f, 0f, 50f));
            Assert.False(TwistedDirectoryModel.WithinSlice(60f, 0f, 0f, 0f, 50f));
        }

        [Fact]
        public void WithinSlice_boundary_is_inclusive()
        {
            // Exactly on the radius is inside (<=). 30-40-50 triangle: dist == 50 exactly.
            Assert.True(TwistedDirectoryModel.WithinSlice(30f, 40f, 0f, 0f, 50f));
        }

        // ── the core L2 behaviour: a far portal the old client-window walk couldn't see ───────────

        [Fact]
        public void BuildSlice_includes_a_portal_beyond_the_client_window()
        {
            // The exact AT-PICK-LONGRANGE shape: portal B is 250 m from the stepping player — WAY past
            // the ~64-128 m client ZDO window the L1 staging walk was limited to. The server holds it
            // (it holds the whole world) and the slice (reach 300 m) MUST include it. This is the test
            // that proves the directory reaches destinations the old walk silently dropped.
            var all = new List<DirectoryRow>
            {
                RowAt(0f, 0f, "Origin", id: 1u),     // the origin portal (player stands here)
                RowAt(250f, 0f, "FarHaven", id: 2u), // beyond one client's window — the long-range case
                RowAt(800f, 0f, "TooFar", id: 3u),   // outside the 300 m reach — correctly excluded
            };
            var into = new List<DirectoryRow>();
            float applied = TwistedDirectoryModel.BuildSlice(all, 0f, 0f, 300f, into);

            Assert.Equal(300f, applied);
            Assert.Equal(2, into.Count);
            Assert.Contains(into, r => r.Rune == "FarHaven");   // the >128 m destination is reachable
            Assert.DoesNotContain(into, r => r.Rune == "TooFar");
        }

        [Fact]
        public void BuildSlice_clears_the_output_first()
        {
            var all = new List<DirectoryRow> { RowAt(5f, 0f, "A", id: 9u) };
            var into = new List<DirectoryRow> { RowAt(999f, 999f, "STALE", id: 99u) };
            TwistedDirectoryModel.BuildSlice(all, 0f, 0f, 100f, into);
            Assert.Single(into);
            Assert.DoesNotContain(into, r => r.Rune == "STALE");
        }

        [Fact]
        public void BuildSlice_on_an_empty_world_returns_the_clamped_radius_and_no_rows()
        {
            var into = new List<DirectoryRow>();
            float applied = TwistedDirectoryModel.BuildSlice(new List<DirectoryRow>(), 100f, 100f, 0f, into);
            Assert.Empty(into);
            // requested 0 → clamped to the floor (not 0, not the whole world).
            Assert.Equal(TwistedDirectoryModel.MinSliceRadiusMeters, applied);
        }

        [Fact]
        public void BuildSlice_recenters_on_the_request_origin_not_the_world_origin()
        {
            // A player standing at (1000,1000) gets the slice around THEM, not around (0,0).
            var all = new List<DirectoryRow>
            {
                RowAt(1000f, 1000f, "AtPlayer", id: 1u), // 0 m from the request origin
                RowAt(0f, 0f, "AtWorldOrigin", id: 2u),  // ~1414 m from the player — out of a 300 m slice
            };
            var into = new List<DirectoryRow>();
            TwistedDirectoryModel.BuildSlice(all, 1000f, 1000f, 300f, into);
            Assert.Single(into);
            Assert.Equal("AtPlayer", into[0].Rune);
        }
    }
}
