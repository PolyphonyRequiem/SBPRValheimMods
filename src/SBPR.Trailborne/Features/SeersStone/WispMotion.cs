// ============================================================================
//  Trailborne v4 (Mountains) — Seer's Stone WISP MOTION (engine-free helix math)
// ----------------------------------------------------------------------------
//  Design   : docs/design/seers-stone.md §wisp-motion — Daniel, 2026-06-25:
//    "centroid is the cylinder's AXIS; the wisp rides the WALL of that cylinder.
//     Slow circular orbit around the patch/location perimeter (radius = bounds +
//     a SLIGHT margin so it floats just OUTSIDE the foliage), while bobbing up and
//     down on a slow vertical sine. The two combined trace a helix on the cylinder
//     surface. It must extend SLIGHTLY beyond the area bounds so it's visible and
//     not hidden in geometry."  + the ground-aware-Y correction (Starbright, same
//     thread): terrain isn't flat, so the vertical sine rides ABOVE local ground at
//     the wisp's current orbit position, or it clips the uphill side on a slope.
//
//  WHY ENGINE-FREE. The orbit+bob is pure parametric geometry — given (centroid,
//  bounds-radius, time, tuning), where is the wisp this frame? Keeping it free of
//  UnityEngine lets tests/SeersStoneWispMotionTests.cs assert the helix invariants
//  headless in CI (radius = bounds+margin, the bob stays within amplitude, the orbit
//  closes over its period, phase offset spreads multiple wisps) — the exact
//  CompassNorthGate / DiscRingGeometry link-compile precedent. The MonoBehaviour
//  (WispBehaviour, engine-side) samples this each Update, adds the ground-height
//  read (Unity Physics / heightmap — the one thing this can't do headless), and
//  writes transform.position. POLICY/GEOMETRY here; STATE-READ + RENDER in the engine.
//
//  Clean-side (ADR-0001): SBPR-authored; references no vanilla or third-party type.
//  Uses a local Vec3 (not UnityEngine.Vector3) so it compiles under net8.0 in the
//  test project; the engine wrapper converts at the boundary.
// ============================================================================

using System;

namespace SBPR.Trailborne.Features.SeersStone
{
    /// <summary>Minimal engine-free 3-vector (the wrapper converts to/from UnityEngine.Vector3).</summary>
    public readonly struct Vec3
    {
        public readonly float X, Y, Z;
        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public float HorizontalDistanceTo(Vec3 o)
        {
            float dx = X - o.X, dz = Z - o.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// Tuning for one wisp's helix. Authored per-archetype (the per-kind knob Daniel wanted),
    /// defaults are sane Mountains-tier values. <see cref="BoundsRadius"/> comes from the live
    /// object (vegetation group radius or Location.m_exteriorRadius); everything else is config.
    /// </summary>
    public readonly struct WispMotionParams
    {
        /// <summary>The patch/location radius (the cylinder's radius BEFORE margin), metres.</summary>
        public readonly float BoundsRadius;
        /// <summary>Slight outward margin so the wisp floats OUTSIDE the foliage (Daniel: "slightly beyond").</summary>
        public readonly float Margin;
        /// <summary>Orbit period — seconds for one full loop around the perimeter (slow: Daniel "slow loops").</summary>
        public readonly float OrbitPeriod;
        /// <summary>Vertical sine amplitude, metres (how far up/down the bob travels from mid-height).</summary>
        public readonly float BobAmplitude;
        /// <summary>Vertical sine period, seconds (slow: Daniel "moves slowly ... sinusoidal").</summary>
        public readonly float BobPeriod;
        /// <summary>Mid-height of the bob above the orbit-point ground, metres (so min-bob still clears foliage).</summary>
        public readonly float BobMidHeight;
        /// <summary>Per-wisp phase offset (radians) so adjacent wisps don't orbit in lockstep.</summary>
        public readonly float PhaseOffset;

        public WispMotionParams(float boundsRadius, float margin, float orbitPeriod,
                                float bobAmplitude, float bobPeriod, float bobMidHeight, float phaseOffset)
        {
            BoundsRadius = boundsRadius;
            Margin = margin;
            OrbitPeriod = orbitPeriod;
            BobAmplitude = bobAmplitude;
            BobPeriod = bobPeriod;
            BobMidHeight = bobMidHeight;
            PhaseOffset = phaseOffset;
        }

        /// <summary>The cylinder wall radius: patch bounds + the visibility margin.</summary>
        public float OrbitRadius => BoundsRadius + Margin;

        /// <summary>Sane Mountains-tier defaults (orbit slow, bob gentle, wisp floats ~1.5 m up just outside bounds).</summary>
        public static WispMotionParams Default(float boundsRadius, float phaseOffset = 0f)
            => new WispMotionParams(
                boundsRadius: boundsRadius,
                margin:       0.75f,   // float just outside the foliage
                orbitPeriod:  12f,     // a slow ~12 s loop
                bobAmplitude: 0.5f,    // gentle ±0.5 m bob
                bobPeriod:    4f,      // slow vertical breathing
                bobMidHeight: 1.5f,    // mid-bob sits ~1.5 m above local ground
                phaseOffset:  phaseOffset);
    }

    /// <summary>
    /// Pure helix-on-a-cylinder solver for a single wisp. Engine-free so the geometry is CI-gated.
    /// The engine wrapper calls <see cref="HorizontalOffset"/> + <see cref="VerticalHeight"/> each
    /// frame, resolves ground height at the orbit point (the one engine-coupled step), and sets the
    /// world position = centroid.xz + horizontalOffset, groundY + verticalHeight.
    /// </summary>
    public static class WispMotion
    {
        private const float TwoPi = (float)(2.0 * Math.PI);

        /// <summary>
        /// The horizontal (XZ-plane) offset from the centroid AXIS to the wisp at time <paramref name="t"/>:
        /// a point on the circle of radius <see cref="WispMotionParams.OrbitRadius"/>, advancing one full
        /// turn per <see cref="WispMotionParams.OrbitPeriod"/> seconds, plus the per-wisp phase. Y is 0 here
        /// (height is the separate vertical-sine axis — together they trace the helix). This is the
        /// "rides the WALL of the cylinder" half of Daniel's spec.
        /// </summary>
        public static Vec3 HorizontalOffset(WispMotionParams p, float t)
        {
            float orbitOmega = p.OrbitPeriod > 0f ? TwoPi / p.OrbitPeriod : 0f;
            float angle = orbitOmega * t + p.PhaseOffset;
            float r = p.OrbitRadius;
            return new Vec3((float)Math.Cos(angle) * r, 0f, (float)Math.Sin(angle) * r);
        }

        /// <summary>
        /// The vertical height ABOVE LOCAL GROUND at time <paramref name="t"/>: a sine bob of amplitude
        /// <see cref="WispMotionParams.BobAmplitude"/> about <see cref="WispMotionParams.BobMidHeight"/>,
        /// one cycle per <see cref="WispMotionParams.BobPeriod"/> seconds. The wrapper adds this to the
        /// ground height SAMPLED AT THE ORBIT POINT (not the centroid) — that's the ground-aware-Y fix:
        /// on a slope the wisp tracks the terrain under its current orbit position so it never sinks into
        /// the uphill side. Always ≥ (mid − amplitude); keep BobMidHeight ≥ BobAmplitude so it never dips
        /// below ground from the sine alone.
        /// </summary>
        public static float VerticalHeight(WispMotionParams p, float t)
        {
            float bobOmega = p.BobPeriod > 0f ? TwoPi / p.BobPeriod : 0f;
            return p.BobMidHeight + p.BobAmplitude * (float)Math.Sin(bobOmega * t);
        }

        /// <summary>
        /// Convenience: the full local offset (horizontal + vertical) from a FLAT-ground centroid, for
        /// tests and for the flat-terrain fast path. On real terrain the wrapper must instead sample
        /// ground at centroid + HorizontalOffset and use VerticalHeight on top of THAT — see the method
        /// docs. Provided so the headless tests can assert the complete helix without a heightmap.
        /// </summary>
        public static Vec3 LocalOffsetFlat(WispMotionParams p, float t)
        {
            var h = HorizontalOffset(p, t);
            return new Vec3(h.X, VerticalHeight(p, t), h.Z);
        }
    }
}
