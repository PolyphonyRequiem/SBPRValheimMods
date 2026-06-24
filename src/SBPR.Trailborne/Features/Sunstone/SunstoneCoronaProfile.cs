// ============================================================================
//  Trailborne v3 (Swamp) — Sunstone Lens corona FEET-GLOW vertical profile (engine-free)
// ----------------------------------------------------------------------------
//  Impl spec: docs/v3/planning/sunstone-lens-corona-impl-spec.md §2.3 (the FeetGlow
//             render), §3 AT-CORONA-FEET-PROFILE (the CI-gated contract).
//  Card     : t_9d7c3dfe follow-up (Daniel /bug, Discord ticket-diegetic-halo-render
//             2026-06-24): the flat ground-plane corona disc "clips through the terrain
//             and seems perfectly flat." Wants "a more 'substantive' looking glow that
//             starts around the feet, appears at full width around .5m from ground, and
//             doesn't seem to 'hard clip' into the environment."
//
//  The per-pixel ALPHA PROFILE of the new FeetGlow corona — a camera-facing VERTICAL
//  billboard standing UP out of the player's feet (not a floor decal). The profile is a
//  pure function of normalized texel coordinates:
//      u ∈ [0,1] — horizontal across the quad (0 = left rim, 0.5 = centre column, 1 = right rim)
//      v ∈ [0,1] — vertical up the quad   (0 = GROUND contact at the feet, 1 = the dome top)
//  and three shape knobs (all live-config in Plugin.cs, defaulted in SunstoneCoronaDisc):
//      fullWidthFrac — the height (as a fraction of the quad) at which the glow reaches FULL
//                      width. = Clamp(CoronaFullWidthHeight / CoronaHeight). Daniel's "full
//                      width around .5m" with a ~1.2 m quad → ~0.42.
//      baseWidthFrac — how NARROW the glow is at the ground contact, as a fraction of full
//                      width (a narrow bright core concentrated at the feet).
//      thickness     — the soft radiant horizontal edge-feather width (reuses CoronaThickness).
//
//  The locked SHAPE (what makes it read "substantive, rising from the feet, no hard clip"):
//    • v = 0 (ground meet) → alpha 0. A SOFT bottom edge: the glow fades INTO the ground, so
//      where the billboard meets uneven terrain there is NO hard intersection line (the whole
//      point — a flat disc coplanar with terrain z-fights along a hard cut; a soft-bottomed
//      upright billboard never does).
//    • low v → a NARROW but bright core (the glow is concentrated at the feet, see HalfWidthAt).
//    • v rising to fullWidthFrac → the column BLOOMS to full width (HalfWidthAt grows base→full).
//    • v above fullWidthFrac → a soft DOME: width tapers back in and alpha fades to 0 at the top
//      (v→1) and at the horizontal rims (u→0,1). Soft on every edge, transparent at every border.
//
//  WHY THIS IS A SEPARATE ENGINE-FREE FILE. "Rises from the feet, full width by X, soft at the
//  ground and every rim" is the load-bearing invariant of this fix, and it is exactly the kind of
//  pure float math the repo gates headlessly — the SunstoneCoronaPulse / SunstoneHaloGeometry /
//  DiscRingGeometry link-compile precedent (tests/SBPR.Trailborne.Tests.csproj). Keeping it free
//  of UnityEngine / Valheim types lets tests/SunstoneCoronaProfileTests.cs assert AT-CORONA-FEET-
//  PROFILE under net8.0, so a future edit that re-introduces a hard bottom edge, stops the bloom,
//  or lets the glow reach the rims opaque fails CI instead of shipping wrong (this box renders
//  nothing — the math fence is the only headless guard the look has). SunstoneCoronaDisc bakes the
//  procedural texture by calling AlphaAt per-pixel; the POLICY lives here, the RENDER in engine code.
//
//  net48 caveat (the mod targets net48): System.Math only — there is NO MathF on net48. This file
//  uses only inline clamp/lerp/smoothstep (no transcendental calls), so it is allocation- and
//  MathF-free on both net48 (mod) and net8.0 (tests).
//
//  Clean-side (ADR-0001): all SBPR-authored math; references no vanilla or third-party type.
// ============================================================================

namespace SBPR.Trailborne.Features.Sunstone
{
    /// <summary>
    /// Pure alpha-profile math for the Sunstone Lens FeetGlow corona (the upright, camera-facing
    /// rising glow). Engine-free (no UnityEngine / Valheim refs) so tests/SunstoneCoronaProfileTests.cs
    /// can gate the "rises from the feet, blooms to full width by a set height, soft on every edge"
    /// contract headless in CI — the look cannot be GPU-verified on the build box, so this is its fence.
    /// </summary>
    public static class SunstoneCoronaProfile
    {
        // fullWidthFrac is clamped into this band so there is ALWAYS a base (a feet section that
        // blooms) and ALWAYS a dome (a tapering top) — a fat-fingered CoronaHeight ≤ FullWidthHeight
        // (frac → 1) or a tiny FullWidthHeight (frac → 0) can't collapse the shape.
        private const float MinFullWidthFrac = 0.05f;
        private const float MaxFullWidthFrac = 0.95f;

        // The dome never tapers to a literal point — it holds this much half-width at the very top so
        // the crown reads as a rounded dome, not a spike. (Alpha still fades to 0 at v=1 regardless.)
        private const float DomeTopHalfWidth = 0.10f;

        // Absolute half-width ceiling (< 0.5) so the bloom's widest point still leaves a transparent
        // horizontal margin — guarantees alpha == 0 at u=0 and u=1 (soft rims) even at full bloom.
        private const float MaxHalfWidth = 0.46f;

        /// <summary>
        /// The glow's HALF-WIDTH (fraction of the quad, 0..<see cref="MaxHalfWidth"/>) at vertical
        /// position <paramref name="v"/>. Grows from a narrow base (<paramref name="baseWidthFrac"/> of
        /// full) at the ground to full at <paramref name="fullWidthFrac"/>, then tapers back to a rounded
        /// dome at the top. Public so the bloom invariant (base strictly narrower than full) is pinned
        /// directly in CI, not inferred from a pixel sweep.
        /// </summary>
        public static float HalfWidthAt(float v, float fullWidthFrac, float baseWidthFrac)
        {
            float vv = Clamp01(v);
            float fw = Clamp(fullWidthFrac, MinFullWidthFrac, MaxFullWidthFrac);
            float baseHalf = MaxHalfWidth * Clamp01(baseWidthFrac);

            if (vv <= fw)
            {
                // Bloom: base half-width (at the feet) → full (MaxHalfWidth) at fullWidthFrac.
                float t = Smooth01(vv / fw);
                return Lerp(baseHalf, MaxHalfWidth, t);
            }

            // Dome: full → a rounded crown half-width as v climbs to the top.
            float td = Smooth01((vv - fw) / (1f - fw));
            return Lerp(MaxHalfWidth, DomeTopHalfWidth, td);
        }

        /// <summary>
        /// The vertical brightness envelope at <paramref name="v"/> (independent of u): 0 at the ground
        /// (soft meet), ramps to full across a short bottom feather (the bright feet core), holds through
        /// <paramref name="fullWidthFrac"/>, then fades to 0 at the dome top (v=1). Public for testing.
        /// </summary>
        public static float VerticalAlphaAt(float v, float fullWidthFrac)
        {
            float vv = Clamp01(v);
            float fw = Clamp(fullWidthFrac, MinFullWidthFrac, MaxFullWidthFrac);

            // Soft ground ramp: 0 at v=0 → 1 by a short feather, so the glow lifts off the ground with
            // no hard line. The feather is a fraction of fullWidthFrac (faster than the bloom) so the
            // feet core is already BRIGHT while it is still NARROW.
            float feather = fw * 0.5f;
            if (feather < 0.04f) feather = 0.04f;
            float rise = Smooth01(vv / feather);

            // Above fullWidthFrac, fade to 0 at the top (the dome). At/under fullWidthFrac, full.
            float fall = vv <= fw ? 1f : Smooth01((1f - vv) / (1f - fw));

            return rise * fall;
        }

        /// <summary>
        /// The per-texel corona alpha for the FeetGlow billboard. <paramref name="u"/>/<paramref name="v"/>
        /// are normalized quad coords (v=0 ground, v=1 top; u=0.5 centre column). Combines the vertical
        /// brightness envelope with a horizontal soft-edge falloff inside the per-height half-width.
        /// Returns [0,1]; the gold tint and the breathing alpha (SunstoneCoronaPulse) are applied on top
        /// at draw via Image.color.
        /// </summary>
        public static float AlphaAt(float u, float v, float fullWidthFrac, float baseWidthFrac, float thickness)
        {
            float vert = VerticalAlphaAt(v, fullWidthFrac);
            if (vert <= 0f) return 0f;

            float half = HalfWidthAt(v, fullWidthFrac, baseWidthFrac);
            float du = u - 0.5f;
            if (du < 0f) du = -du;            // |u - 0.5| → distance from the centre column
            if (du >= half) return 0f;        // outside the glow column → transparent (soft rims)

            // Horizontal soft edge: a solid centre, feathered to 0 at the half-width boundary. The
            // feather width is a fraction of the local half-width (CoronaThickness), min-clamped so a
            // very thin column still feathers rather than hard-cutting.
            float edge = half * Clamp01(thickness);
            if (edge < 0.02f) edge = 0.02f;
            float inner = half - edge;
            float horiz = du <= inner ? 1f : Smooth01((half - du) / edge);

            return vert * horiz;
        }

        // ── Inline clamp / lerp / smoothstep (no UnityEngine.Mathf, no MathF — the engine-free rule) ──
        private static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);
        private static float Clamp(float x, float lo, float hi) => x < lo ? lo : (x > hi ? hi : x);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        // Smoothstep (3t²-2t³) on a clamped [0,1] input — zero-slope endpoints, so the edges meet with
        // no visible banding seam where a feather joins the solid interior.
        private static float Smooth01(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
