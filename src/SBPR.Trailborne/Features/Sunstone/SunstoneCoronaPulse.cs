// ============================================================================
//  Trailborne v3 (Swamp) — Sunstone Lens corona PULSE envelope (engine-free)
// ----------------------------------------------------------------------------
//  Impl spec: docs/v3/planning/sunstone-lens-corona-impl-spec.md §2.2 (the body),
//             §3 AT-CORONA-PULSE-MATH (the CI-gated contract).
//  Card     : t_9d7c3dfe (graduates t_2d500d45 — Daniel /bug: "the ring itself is
//             just a screen space circle, not a 3d slowly pulsing 'sun corona' disc").
//
//  The slow alpha "breath" of the world-space sun-corona disc. The corona's PER-FRAME
//  alpha is computed here from one shared Time.time phase so every surface that could
//  show the corona breathes in lockstep by construction (no per-object accumulator →
//  no drift, no jump when the orientation enum is flipped mid-breath — AT-CORONA-PULSE).
//
//  WHY THIS IS A SEPARATE ENGINE-FREE FILE. The breathing ENVELOPE SHAPE (one shared
//  phase, alpha clamped to [trough,peak], periodic at Hz, peak<trough reordered, hz=0 →
//  steady mid) is the load-bearing invariant of this card, and it is exactly the kind of
//  pure float math the repo's test suite gates headlessly — the SunstoneHaloGeometry /
//  DiscRingGeometry / LensHandoffDecision link-compile precedent (tests/
//  SBPR.Trailborne.Tests.csproj). Keeping it free of UnityEngine / Valheim types lets
//  tests/SunstoneCoronaPulseTests.cs assert AT-CORONA-PULSE-MATH under net8.0, so a
//  future edit that inverts the pulse, drops the clamp, or NaNs on a fat-fingered .cfg
//  fails CI instead of shipping. SunstoneCoronaDisc.Render reads the live Time.time and
//  only consumes the number this file returns; the POLICY lives here, the RENDER lives
//  in engine code (the SunstoneHaloGeometry split, mirrored exactly).
//
//  net48 caveat (the mod targets net48): System.Math only — there is NO MathF on net48,
//  so the sinusoid uses System.Math.Sin/PI and casts to float at the boundary.
//
//  Clean-side (ADR-0001): all SBPR-authored math; references no vanilla or third-party
//  type. (The CoronaOrientation live-config enum lives here too so the Config bind in
//  Plugin.cs and any engine-free test share one definition — the LensHandoffDecision
//  MinimapHandoffMode/BlipStyle precedent for feature-policy enums.)
// ============================================================================

namespace SBPR.Trailborne.Features.Sunstone
{
    /// <summary>
    /// Orientation of the world-space corona disc (Knob #1, design-defaulted to
    /// <see cref="GroundPlane"/>, live-flippable via Config — the banner-windsock pattern).
    /// Defined in this engine-free file (not the engine-side render) so the Plugin bind and a
    /// headless test can share one definition — the LensHandoffDecision enum precedent.
    /// </summary>
    public enum CoronaOrientation
    {
        /// <summary>DEFAULT (architect). A flat horizontal disc in the XZ plane on the player —
        /// the "sun on the floor" the fixed-distance trophy halo orbits. No Billboard; stays flat
        /// regardless of camera.</summary>
        GroundPlane,

        /// <summary>An upright, camera-facing disc on the eye anchor — the trophy-slot Billboard
        /// idiom (m_vertical=true). Yaws to face the camera, stays upright.</summary>
        CameraFacing,
    }

    /// <summary>
    /// Pure pulse-envelope math for the Sunstone Lens corona. Engine-free (no UnityEngine /
    /// Valheim refs) so tests/SunstoneCoronaPulseTests.cs can gate the breathing envelope headless —
    /// the locked SHAPE (one shared phase, clamped to [trough,peak], periodic at Hz) can't drift.
    /// net48: System.Math only (no MathF).
    /// </summary>
    public static class SunstoneCoronaPulse
    {
        /// <summary>
        /// The breathing alpha at wall-clock <paramref name="time"/> seconds (pass Time.time — one
        /// shared phase so every consumer breathes in lockstep, no drift). <paramref name="hz"/> =
        /// breaths/sec (Knob #3 rate); the alpha swings between <paramref name="trough"/> and
        /// <paramref name="peak"/> (Knob #3 depth) on a sinusoid. trough/peak are clamped + ordered
        /// defensively so a fat-fingered .cfg (peak &lt; trough) degrades to a steady glow, never an
        /// inverted or NaN pulse. <paramref name="hz"/> = 0 holds a steady mid-value (no breath).
        /// </summary>
        public static float AlphaAt(double time, float hz, float trough, float peak)
        {
            float lo = Clamp01(trough);
            float hi = Clamp01(peak);
            if (hi < lo) { float t = lo; lo = hi; hi = t; }   // order defensively
            // s ∈ [0,1]; 0.5*(1+sin) so a 0-phase start sits mid-breath rising.
            double s = 0.5 * (1.0 + System.Math.Sin(time * (2.0 * System.Math.PI * hz)));
            return (float)(lo + (hi - lo) * s);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
