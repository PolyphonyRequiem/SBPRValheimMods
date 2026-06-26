// ============================================================================
//  Trailborne v3 (Swamp) — Twisted Portal label DISTANCE-SCALE math (engine-free)
// ----------------------------------------------------------------------------
//  Card     : t_f66a3e37 (FIX 3c) — the through-terrain portal-name overlay labels
//             shrank with raw perspective (sub-pixel at the overlay edge → unreadable).
//  Impl spec: docs/v3/planning/twisted-portal-impl-spec.md §7.4.
//  Decision  (architect t_f739451f, Daniel-direction): labels hold ~CONSTANT
//             ON-SCREEN (angular) size across the overlay range, clamped near/far —
//             they must NOT shrink with raw perspective. Forward-compatible with the
//             look-to-aim travel model (t_3d908685) where distant labels must stay
//             aimable.
//
//  WHY THIS IS A SEPARATE ENGINE-FREE FILE. This is the SAME move as the Sunstone
//  trophy halo: the SCALE carries the distance behaviour, and that pure float curve
//  is exactly what the repo's test suite gates headlessly (the
//  SunstoneHaloGeometry.ScaleAt / DiscRingGeometry / LensHandoffDecision link-compile
//  precedent — tests/SBPR.Trailborne.Tests.csproj). Keeping it free of UnityEngine /
//  Valheim types lets tests/TwistedPortalLabelScaleTests.cs assert AT-LABEL-SCALE-MATH
//  under net8.0 — so a future edit that re-introduces raw-perspective shrink (the bug
//  being fixed) fails CI instead of shipping. TwistedPortalOverlay.DrawLabel reads the
//  live camera distance and only consumes the multiplier this file returns; the POLICY
//  lives here, the RENDER lives in engine code.
//
//  ON-SCREEN-SIZE MATH (why mul ∝ camDist is "constant angular"). A world object of
//  size S at camera distance d subtends an on-screen (angular) size ≈ S / d. If we
//  multiply S by mul = d / refDist, the on-screen size becomes
//      (S · d / refDist) / d = S / refDist  — INDEPENDENT of d.
//  So scaling the label's world size in proportion to the camera distance holds its
//  pixel size constant; refDist is the distance at which the multiplier is 1 (the
//  authored world size). The invariant the test pins is mul / camDist == 1 / refDist
//  inside the clamp band (constant-angular), flat (clamped) outside it.
//
//  Clean-side (ADR-0001): all SBPR-authored math; references no vanilla or third-party
//  type. Uses inline clamp/lerp so it stays UnityEngine-free (no Mathf / MathF) and
//  net48-safe — the engine-free link-compile rule SunstoneHaloGeometry follows.
// ============================================================================

namespace SBPR.Trailborne.Features.Portals
{
    /// <summary>
    /// How the floating label's world-scale responds to camera distance. Two modes ship behind a
    /// live config enum (reversibility is a hard requirement — never delete a mode):
    /// </summary>
    public enum LabelScaleMode
    {
        /// <summary>DEFAULT. Hold ~constant ON-SCREEN (angular) size: the world-scale multiplier rises
        /// in proportion to camera distance (mul = camDist / refDist), clamped to [minMul, maxMul] so a
        /// near label can't collapse into the portal mesh and a far one can't balloon into a giant
        /// billboard. Constant-angular inside the band, flat outside. The forward-compatible choice for
        /// the look-to-aim travel model (distant labels stay readable + aimable).</summary>
        ConstantOnScreen,

        /// <summary>Selectable alt — the Sunstone trophy-halo shape (<see cref="SunstoneShape"/>): full
        /// (1.0) at/under <c>knee</c> metres, <c>floor</c>× at <c>overlayRadius</c>, linear between
        /// (monotonically non-increasing). Keeps a faint depth cue (far reads a little smaller) while a
        /// HIGH readable floor (start 0.6, vs Sunstone's 0.25) stops the edge shrinking to nothing.</summary>
        KneeFloor,
    }

    /// <summary>
    /// Pure distance→world-scale-multiplier math for the on-step Twisted Portal labels (spec §7.4).
    /// Engine-free (no UnityEngine / Valheim refs) so tests/TwistedPortalLabelScaleTests.cs can gate
    /// AT-LABEL-SCALE-MATH headless in CI — the load-bearing invariant (labels do NOT shrink with raw
    /// perspective) cannot silently regress. The render loop (TwistedPortalOverlay.DrawLabel) reads the
    /// live camera distance and applies <see cref="ScaleMul"/> as a multiplier on the authored
    /// localScale; the POLICY lives here, the RENDER lives in engine code.
    /// </summary>
    public static class TwistedPortalLabelScale
    {
        // ── Defaults (single source of truth; Plugin binds live ConfigEntry mirrors so Daniel
        //    converges the feel on a joined client without a rebuild — the banner-windsock pattern).
        //    All flagged for the in-game eyeball pass (AT-LABEL-CONSTANT-SIZE). ──
        public const LabelScaleMode DefaultMode = LabelScaleMode.ConstantOnScreen;

        /// <summary>Camera distance (metres) at which the multiplier is 1.0 — the label renders at its
        /// authored world size. Closer → &lt;1 (smaller world, same pixels); farther → &gt;1 (bigger
        /// world to hold the pixel size) until <see cref="DefaultMaxMul"/>. Eyeball tunable.</summary>
        public const float DefaultRefDist = 24f;

        /// <summary>Lower clamp on the multiplier — a near label can't shrink below this fraction of its
        /// authored world size (stops it collapsing into the ~3 m portal ring). Eyeball tunable.</summary>
        public const float DefaultMinMul = 0.5f;

        /// <summary>Upper clamp on the multiplier — a far label can't grow beyond this multiple of its
        /// authored world size (stops it becoming a giant billboard at the overlay edge). Eyeball.</summary>
        public const float DefaultMaxMul = 6f;

        /// <summary>KneeFloor: distance (metres) at/under which a label is FULL (1.0) scale — the
        /// Sunstone 10 m knee. Eyeball tunable (KneeFloor mode only).</summary>
        public const float DefaultKnee = 10f;

        /// <summary>KneeFloor: multiplier at <c>overlayRadius</c> — the edge floor. HIGHER than
        /// Sunstone's 0.25 so far labels stay readable while keeping a faint depth cue (KneeFloor
        /// mode only). Eyeball tunable.</summary>
        public const float DefaultFloor = 0.6f;

        /// <summary>
        /// The world-scale MULTIPLIER for a label whose portal is <paramref name="camDist"/> metres from
        /// the camera. The render side multiplies the authored localScale by this.
        ///
        /// <para><b>ConstantOnScreen (default):</b> <c>clamp(camDist / refDist, minMul, maxMul)</c>. At
        /// <c>camDist == refDist</c> → 1 (authored size); inside the clamp band the on-screen (angular)
        /// size is constant (mul / camDist == 1 / refDist); outside it the multiplier is flat at the
        /// clamp. Ignores <paramref name="overlayRadius"/> / <paramref name="knee"/> /
        /// <paramref name="floor"/>.</para>
        ///
        /// <para><b>KneeFloor:</b> the Sunstone trophy-halo shape — full (1.0) for
        /// <paramref name="camDist"/> ≤ <paramref name="knee"/>, <paramref name="floor"/> at
        /// <paramref name="overlayRadius"/>, linear (monotonically non-increasing) between. Ignores
        /// <paramref name="refDist"/> / <paramref name="minMul"/> / <paramref name="maxMul"/>.</para>
        ///
        /// Defensive: a non-positive <paramref name="refDist"/> falls back to <see cref="DefaultRefDist"/>
        /// (no divide-by-zero); a degenerate <paramref name="overlayRadius"/> ≤ <paramref name="knee"/>
        /// makes the whole range full scale (guards the KneeFloor span div). The result is always &gt; 0.
        /// </summary>
        public static float ScaleMul(
            LabelScaleMode mode,
            float camDist,
            float refDist,
            float minMul,
            float maxMul,
            float overlayRadius,
            float knee,
            float floor)
        {
            if (camDist < 0f) camDist = 0f;

            if (mode == LabelScaleMode.KneeFloor)
                return KneeFloorMul(camDist, overlayRadius, knee, floor);

            return ConstantOnScreenMul(camDist, refDist, minMul, maxMul);
        }

        /// <summary>
        /// ConstantOnScreen: <c>clamp(camDist / refDist, minMul, maxMul)</c>. Inside the band the
        /// multiplier rises ∝ camDist so on-screen size is constant (mul / camDist == 1 / refDist);
        /// outside it flattens at minMul (near) / maxMul (far). A non-positive refDist falls back to
        /// the default so the division is always safe; a swapped [minMul &gt; maxMul] is normalised so
        /// the clamp can't invert.
        /// </summary>
        public static float ConstantOnScreenMul(float camDist, float refDist, float minMul, float maxMul)
        {
            if (refDist <= 0f) refDist = DefaultRefDist;
            // Normalise a fat-fingered swapped band so Clamp can't invert (lo must be ≤ hi).
            float lo = minMul <= maxMul ? minMul : maxMul;
            float hi = minMul <= maxMul ? maxMul : minMul;
            // Floor the band above zero so the multiplier is never ≤ 0 (a 0-size label is invisible).
            if (lo < MinPositiveMul) lo = MinPositiveMul;
            if (hi < lo) hi = lo;
            float raw = camDist / refDist;
            return Clamp(raw, lo, hi);
        }

        /// <summary>
        /// KneeFloor: the Sunstone trophy-halo curve shape (mirrors SunstoneHaloGeometry.ScaleAt). Full
        /// (1.0) for <paramref name="camDist"/> ≤ <paramref name="knee"/>, <paramref name="floor"/> at
        /// <paramref name="overlayRadius"/>, linear between; clamps to <paramref name="floor"/> beyond
        /// the radius. A degenerate <paramref name="overlayRadius"/> ≤ <paramref name="knee"/> makes the
        /// whole range full scale (the guard branch — no divide-by-zero). <paramref name="floor"/> is
        /// floored above zero so the result is never ≤ 0.
        /// </summary>
        public static float KneeFloorMul(float camDist, float overlayRadius, float knee, float floor)
        {
            if (floor < MinPositiveMul) floor = MinPositiveMul;
            if (floor > 1f) floor = 1f;
            if (knee < 0f) knee = 0f;

            float span = overlayRadius - knee;
            // span ≤ 0 → the whole overlay range sits inside the full-scale knee; everything is full.
            float t = span <= 0f ? 0f : Clamp01((camDist - knee) / span);
            float k = 1f - t;
            return Lerp(floor, 1f, k);   // k=1 (camDist≤knee) → 1.0 full; k=0 (camDist≥radius) → floor
        }

        /// <summary>Hard floor so a config fat-finger can never make a label 0-size (invisible).</summary>
        public const float MinPositiveMul = 0.01f;

        // Inline clamp/lerp keep this file free of UnityEngine.Mathf (the engine-free link-compile rule).
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
