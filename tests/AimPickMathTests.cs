// ============================================================================
//  AimPickMath — xUnit structural tests (card t_f4d0d5e1 / L1).
// ----------------------------------------------------------------------------
//  Tests the SHIPPED pure look-to-aim angular pick AimPickMath (link-compiled
//  from ../src, not copied — see the .csproj). This is the durable CI fence for
//  the NON-VISUAL core of AT-AIM-SELECT / AT-AIM-THROUGHTERRAIN: given a set of
//  candidate world-directions and the crosshair (camera forward), the candidate
//  closest in ANGLE to the crosshair (within the aim cone) is the selected
//  destination — WITHOUT any line-of-sight collider (so a portal behind a hill
//  is selectable, which is the whole point of the angular pick vs a raycast).
//
//  The engine-free link-compile pattern shared with TwistedPortalOverlayModel /
//  PortalEnergyMath / SunstoneHaloGeometry: the asserted behaviour IS the shipped
//  behaviour (one copy, no fork), runs under net8.0 in CI with no Valheim SDK.
//
//  What these pin (the pure-logic half of AT-AIM-SELECT):
//    • the nearest-angle-to-crosshair candidate wins (not nearest-distance);
//    • the aim cone gates selection (nothing in cone ⇒ -1, no destination);
//    • unnormalized inputs are handled (internal normalization);
//    • a degenerate (near-zero) direction or forward fails closed / is skipped;
//    • exact-angle ties are broken by lower index (flicker-free across frames);
//    • the through-terrain property: distance is irrelevant — only ANGLE selects.
// ============================================================================

using System.Collections.Generic;
using SBPR.Trailborne.Features.Portals;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class AimPickMathTests
    {
        // A wide-open cone so cone-gating doesn't interfere with pure "closest angle" assertions.
        private static readonly float WideCone = AimPickMath.ConeCosFromDegrees(170f);

        // ── closest-ANGLE-to-crosshair wins (the core selection rule) ───────────────────────────

        [Fact]
        public void Picks_candidate_closest_in_angle_to_forward()
        {
            // forward points +Z. Candidate 1 is dead-on +Z; candidate 0 is off to +X.
            var dirs = new List<AimVec>
            {
                new AimVec(1f, 0f, 0f),   // 90° off
                new AimVec(0f, 0f, 1f),   // 0° off — the winner
                new AimVec(0.3f, 0f, 1f), // slightly off +Z
            };
            int pick = AimPickMath.PickByAim(dirs, new AimVec(0f, 0f, 1f), WideCone, out float dot);
            Assert.Equal(1, pick);
            Assert.True(dot > 0.999f);
        }

        [Fact]
        public void Distance_is_irrelevant_only_angle_selects_through_terrain()
        {
            // AT-AIM-THROUGHTERRAIN core: a FAR portal dead-on the crosshair beats a NEAR portal off
            // to the side. The math takes DIRECTIONS (already distance-stripped by the engine wrapper),
            // so there is no collider / line-of-sight notion here at all — a portal behind a hill is
            // exactly as selectable as one in the open, as long as its direction lines up.
            var dirs = new List<AimVec>
            {
                new AimVec(1f, 0f, 0.05f),  // near & beside (engine would pass a short vector; we use direction)
                new AimVec(0f, 0f, 1f),     // far & dead-on
            };
            int pick = AimPickMath.PickByAim(dirs, new AimVec(0f, 0f, 1f), WideCone, out _);
            Assert.Equal(1, pick);
        }

        [Fact]
        public void Sweeping_forward_moves_the_selection()
        {
            var dirs = new List<AimVec>
            {
                new AimVec(-1f, 0f, 1f),  // up-left
                new AimVec(1f, 0f, 1f),   // up-right
            };
            // Aim left → candidate 0; aim right → candidate 1.
            Assert.Equal(0, AimPickMath.PickByAim(dirs, new AimVec(-1f, 0f, 1f), WideCone, out _));
            Assert.Equal(1, AimPickMath.PickByAim(dirs, new AimVec(1f, 0f, 1f), WideCone, out _));
        }

        // ── the aim cone gates selection ─────────────────────────────────────────────────────────

        [Fact]
        public void Nothing_in_cone_returns_minus_one()
        {
            // forward +Z, the only candidate is 90° off to +X, cone is a tight 35°.
            var dirs = new List<AimVec> { new AimVec(1f, 0f, 0f) };
            float cone = AimPickMath.ConeCosFromDegrees(35f);
            int pick = AimPickMath.PickByAim(dirs, new AimVec(0f, 0f, 1f), cone, out float dot);
            Assert.Equal(-1, pick);
            Assert.Equal(-1f, dot);
        }

        [Fact]
        public void Candidate_just_inside_cone_is_selected_just_outside_is_not()
        {
            float cone = AimPickMath.ConeCosFromDegrees(30f);
            // 20° off +Z (in a 30° cone) vs 40° off (outside).
            var inside = new List<AimVec> { Dir2D(20f) };
            var outside = new List<AimVec> { Dir2D(40f) };
            Assert.Equal(0, AimPickMath.PickByAim(inside, new AimVec(0f, 0f, 1f), cone, out _));
            Assert.Equal(-1, AimPickMath.PickByAim(outside, new AimVec(0f, 0f, 1f), cone, out _));
        }

        [Fact]
        public void Cone_cos_clamps_degenerate_angles()
        {
            // 0° cone ⇒ cos 1 (must be dead-on); 180° ⇒ cos -1 (everything in cone).
            Assert.True(AimPickMath.ConeCosFromDegrees(0f) > 0.999f);
            Assert.True(AimPickMath.ConeCosFromDegrees(180f) < -0.999f);
            // Out-of-range inputs clamp, never NaN.
            Assert.True(AimPickMath.ConeCosFromDegrees(-10f) > 0.999f);
            Assert.True(AimPickMath.ConeCosFromDegrees(999f) < -0.999f);
        }

        // ── robustness: unnormalized inputs, degenerate vectors, ties, empties ──────────────────

        [Fact]
        public void Handles_unnormalized_inputs()
        {
            // Same directions as the dead-on test but scaled by wildly different magnitudes.
            var dirs = new List<AimVec>
            {
                new AimVec(50f, 0f, 0f),    // long, 90° off
                new AimVec(0f, 0f, 0.001f), // tiny, dead-on
            };
            int pick = AimPickMath.PickByAim(dirs, new AimVec(0f, 0f, 9999f), WideCone, out _);
            Assert.Equal(1, pick);
        }

        [Fact]
        public void Degenerate_direction_is_skipped()
        {
            // Candidate 0 is a near-zero vector (player standing on it) → skipped; candidate 1 wins.
            var dirs = new List<AimVec>
            {
                new AimVec(0f, 0f, 0f),
                new AimVec(0f, 0f, 1f),
            };
            Assert.Equal(1, AimPickMath.PickByAim(dirs, new AimVec(0f, 0f, 1f), WideCone, out _));
        }

        [Fact]
        public void Zero_forward_returns_minus_one()
        {
            var dirs = new List<AimVec> { new AimVec(0f, 0f, 1f) };
            Assert.Equal(-1, AimPickMath.PickByAim(dirs, new AimVec(0f, 0f, 0f), WideCone, out _));
        }

        [Fact]
        public void Empty_or_null_candidate_set_returns_minus_one()
        {
            Assert.Equal(-1, AimPickMath.PickByAim(new List<AimVec>(), new AimVec(0f, 0f, 1f), WideCone, out _));
            Assert.Equal(-1, AimPickMath.PickByAim(null!, new AimVec(0f, 0f, 1f), WideCone, out _));
        }

        [Fact]
        public void Exact_angle_tie_breaks_to_lower_index()
        {
            // Two candidates symmetric about +Z (same angle) → the lower index wins, deterministically,
            // so the live selection does not flicker between two equidistant-angle labels each frame.
            var dirs = new List<AimVec>
            {
                new AimVec(0.5f, 0f, 1f),
                new AimVec(-0.5f, 0f, 1f),
            };
            Assert.Equal(0, AimPickMath.PickByAim(dirs, new AimVec(0f, 0f, 1f), WideCone, out _));
        }

        // ── helper: a unit direction `deg` degrees off +Z in the X–Z plane ──────────────────────
        private static AimVec Dir2D(float deg)
        {
            double r = deg * (System.Math.PI / 180.0);
            return new AimVec((float)System.Math.Sin(r), 0f, (float)System.Math.Cos(r));
        }
    }
}
