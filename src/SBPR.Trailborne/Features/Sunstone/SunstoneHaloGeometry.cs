// ============================================================================
//  Trailborne v3 (Swamp) — Sunstone Lens halo PLACEMENT geometry (engine-free)
// ----------------------------------------------------------------------------
//  Design : docs/design/sunstone-lens-trophy-ring.md §Q2 Knob #2 / §1.2 / §1.3
//  Card   : t_10bacccf (bug-fix — FIXED-distance ring + 10m scale knee). REVERSES
//           the t_d17d9b58 / PR #242 "variable radius AND scale" design that pushed
//           far enemies to the OUTER radius (away from your face) AND shrank them to
//           ~0.12 world-units — far + tiny = invisible (Daniel's reported symptom).
//
//  Daniel-LOCKED behaviour (he is the design authority on this report):
//    • PLACEMENT is at a FIXED distance. Every trophy renders equidistant from the
//      eye-point — a true fixed-radius ring (no outward push for far enemies). The
//      single HaloRadius is the only placement knob (AT-GATED; Daniel eyeballs the
//      metres on a GPU client, live-config).
//    • SCALE carries ALL the distance information, with a 10 m knee:
//        - enemy ≤ 10 m            → FULL scale   (the locked "1.0" → scaleNear)
//        - enemy at the 70 m edge  → 25% scale    (the locked "0.25" → 0.25·scaleNear)
//        - linear between, monotonically non-increasing with distance.
//      Implements Daniel's exact formula:
//        k     = 1 - Clamp01((dist - 10) / (detectRadius - 10))
//        scale = Lerp(scaleNear · 0.25, scaleNear, k)
//      The 10 m KNEE, the 0.25 FLOOR and the 1.0 CEILING are LOCKED (Daniel's
//      numbers). scaleNear (the absolute world-size that "1.0" maps to) is the only
//      AT-GATED eyeball tunable (config HaloScaleMax, kept). The old HaloScaleMin is
//      derived (0.25·scaleNear), no longer an independent knob.
//
//  WHY THIS IS A SEPARATE ENGINE-FREE FILE. The "fixed distance, scale-only range
//  cue" rule is the load-bearing invariant of this bug-fix, and it is exactly the
//  kind of pure float math the repo's test suite gates headlessly (the
//  DiscRingGeometry / BoundedMapMath / LensHandoffDecision link-compile precedent —
//  tests/SBPR.Trailborne.Tests.csproj). Keeping it free of UnityEngine / Valheim
//  types lets tests/SunstoneHaloGeometryTests.cs assert AT-HALO-FIXED-DIST +
//  AT-HALO-SCALE-KNEE under net8.0 — so a future edit that re-introduces a
//  distance-varying radius (the t_d17d9b58 regression) fails CI instead of shipping.
//  SunstoneWorldRing.Render reads the live eye-point + bearing and only consumes the
//  numbers this file returns; the POLICY lives here, the RENDER lives in engine code.
//
//  Clean-side (ADR-0001): all SBPR-authored math; references no vanilla or third-party
//  type. Uses System.Math-free inline clamp/lerp so it stays UnityEngine-free.
// ============================================================================

namespace SBPR.Trailborne.Features.Sunstone
{
    /// <summary>
    /// The resolved per-blip placement: how far from the eye-point the trophy sits (FIXED —
    /// independent of enemy range), and the world-scale of its quad (the 10 m knee curve).
    /// Returned by <see cref="SunstoneHaloGeometry.Resolve"/>; the render loop applies
    /// <see cref="Distance"/> along the real bearing and <see cref="Scale"/> to the quad.
    /// </summary>
    public readonly struct HaloPlacement
    {
        /// <summary>Metres from the eye-point along the bearing. ALWAYS == the configured HaloRadius
        /// (fixed-distance ring — the Daniel-LOCKED reversal of the variable-radius bug).</summary>
        public readonly float Distance;

        /// <summary>World-scale of the trophy quad: full (scaleNear) at ≤10 m, 0.25·scaleNear at the
        /// detection edge, linear between.</summary>
        public readonly float Scale;

        public HaloPlacement(float distance, float scale)
        {
            Distance = distance;
            Scale = scale;
        }
    }

    /// <summary>
    /// Pure placement/scale math for the Sunstone Lens world halo. Engine-free (no UnityEngine /
    /// Valheim refs) so tests/SunstoneHaloGeometryTests.cs can gate the fixed-distance + 10 m-knee
    /// contract headless in CI — the load-bearing invariant of bug-fix t_10bacccf cannot silently
    /// regress back to the variable-radius design that hid far enemies.
    /// </summary>
    public static class SunstoneHaloGeometry
    {
        /// <summary>Distance (metres) at/under which a trophy renders at FULL scale — the locked "1.0"
        /// knee (Daniel). Enemies closer than this are not drawn any bigger.</summary>
        public const float FullScaleKnee = 10f;

        /// <summary>Scale at the detection edge as a fraction of <c>scaleNear</c> — the locked "0.25"
        /// floor (Daniel). The edge trophy is a quarter-size, still readable, never shrunk to nothing.</summary>
        public const float EdgeScaleFactor = 0.25f;

        /// <summary>
        /// The 10 m-knee scale curve. <paramref name="dist"/> is the real eye→enemy distance,
        /// <paramref name="detectRadius"/> the detection range (e.g. 70 m), <paramref name="scaleNear"/>
        /// the full-size world-scale that "1.0" maps to (config HaloScaleMax). Returns:
        /// full (<paramref name="scaleNear"/>) for <paramref name="dist"/> ≤ <see cref="FullScaleKnee"/>,
        /// <see cref="EdgeScaleFactor"/>·<paramref name="scaleNear"/> at <paramref name="detectRadius"/>,
        /// linear between. Guards a degenerate <paramref name="detectRadius"/> ≤ knee (everything is
        /// within the knee → full scale) so a fat-fingered tiny DetectRadius can't divide by zero.
        /// </summary>
        public static float ScaleAt(float dist, float detectRadius, float scaleNear)
        {
            float span = detectRadius - FullScaleKnee;
            // span ≤ 0 → the whole detection range sits inside the full-scale knee; everything is full.
            float t = span <= 0f ? 0f : Clamp01((dist - FullScaleKnee) / span);
            float k = 1f - t;
            return Lerp(scaleNear * EdgeScaleFactor, scaleNear, k);
        }

        /// <summary>
        /// Resolve the full per-blip placement. <paramref name="haloRadius"/> is the FIXED ring distance
        /// (every trophy equidistant from the eye — no range-dependent push); the scale comes from the
        /// 10 m knee (<see cref="ScaleAt"/>). The render loop places the trophy at
        /// <c>eye + bearing · Distance</c> and scales its quad by <c>Scale</c>.
        /// </summary>
        public static HaloPlacement Resolve(float dist, float detectRadius, float haloRadius, float scaleNear)
            => new HaloPlacement(haloRadius, ScaleAt(dist, detectRadius, scaleNear));

        // Inline clamp/lerp keep this file free of UnityEngine.Mathf (the engine-free link-compile rule).
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
