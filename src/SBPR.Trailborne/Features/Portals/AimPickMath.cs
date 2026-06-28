// ============================================================================
//  Trailborne v3 (Swamp) — Twisted Portal look-to-aim ANGULAR PICK (engine-free)
// ----------------------------------------------------------------------------
//  Card     : t_f4d0d5e1 (L1) — aim-pick destination + tap-E commit.
//  Impl spec: docs/v3/planning/twisted-portal-impl-spec.md §1 Q3 (Beat 2),
//             §4.4a (ResolveAimedDestination — the gaze pick).
//  Design   : Daniel 2026-06-27 — "aim the crosshair close to the target twisted
//             portal in the world ... press E to commit."
//
//  WHAT THIS IS. The pure selection rule behind look-to-aim travel: among the
//  candidate destination portals, pick the one whose WORLD-DIRECTION-FROM-THE-
//  PLAYER is closest in angle to the crosshair (camera forward), within an
//  aim-cone tolerance. This is the architect-locked **angular pick (option ii)**,
//  NOT a vanilla hover-raycast — you must be able to aim at a through-terrain
//  label behind a hill, where a line-of-sight collider raycast can't reach
//  (spec §4.4a "why angular, not raycast"; AT-AIM-THROUGHTERRAIN).
//
//  WHY THIS IS A SEPARATE ENGINE-FREE FILE. The selection math is a pure-logic
//  seam (angle comparison + cone gate) — exactly the shape the repo CI-gates
//  headless (the TwistedPortalOverlayModel / PortalEnergyMath / SunstoneHaloGeometry
//  link-compile precedent, tests/SBPR.Trailborne.Tests.csproj). Keeping it free of
//  UnityEngine / Valheim types lets tests/AimPickMathTests.cs assert the nearest-
//  to-crosshair pick, the cone gate, the normalization robustness, and the
//  stable tie-break under net8.0 in CI (AT-AIM-SELECT's pure-logic core). The
//  ENGINE side (reading GameCamera.forward, walking the candidate ZDO set) lives
//  in TwistedPortalCommitInput.cs; the POLICY lives here.
//
//  Clean-side (ADR-0001): all SBPR-authored logic; references no vanilla or
//  third-party type. Its own minimal Vec3 avoids a UnityEngine.Vector3 dependency
//  so the file link-compiles under net8.0 (the engine wrapper converts).
// ============================================================================

using System;
using System.Collections.Generic;

namespace SBPR.Trailborne.Features.Portals
{
    /// <summary>
    /// A minimal engine-free 3-vector so this selection math carries no UnityEngine
    /// dependency (the engine side converts a <c>UnityEngine.Vector3</c> to this).
    /// Used for both a candidate's world-direction-from-the-player and the camera forward.
    /// Need NOT be normalized on input — <see cref="AimPickMath"/> normalizes internally.
    /// </summary>
    public readonly struct AimVec
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public AimVec(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>Squared magnitude (cheap; used to reject near-zero direction vectors).</summary>
        public float SqrMagnitude => X * X + Y * Y + Z * Z;
    }

    /// <summary>
    /// Pure look-to-aim angular pick (spec §4.4a, Beat 2). Given a candidate set of world-
    /// directions-from-the-player and the camera forward, returns the index of the candidate whose
    /// direction is closest in angle to forward — provided it falls inside the aim cone. Engine-free
    /// so tests/AimPickMathTests.cs gates the contract headless in CI (AT-AIM-SELECT core).
    /// </summary>
    public static class AimPickMath
    {
        /// <summary>
        /// Default aim-cone half-angle, degrees (the live BepInEx knob's baseline, spec §4.4a). A
        /// candidate must lie within this many degrees of the crosshair to be selectable. Generous
        /// enough to feel forgiving when sweeping across a horizon of labels, tight enough that a
        /// portal well off to the side is not grabbed. Flagged for the in-game feel pass (AT-AIM-SELECT).
        /// </summary>
        public const float DefaultAimConeDegrees = 35f;

        /// <summary>A direction whose squared length is below this is treated as degenerate (the
        /// player standing essentially on top of a candidate, or a zeroed forward) and skipped.</summary>
        private const float MinSqrMagnitude = 1e-6f;

        /// <summary>
        /// Convert an aim-cone half-angle (degrees) to the cosine threshold the dot-product compares
        /// against. A candidate is in-cone when <c>dot(normForward, normDir) &gt;= cos(coneDegrees)</c>.
        /// Clamped to [0, 180] degrees: 0° ⇒ cos 1 (must be dead-on), 180° ⇒ cos −1 (everything in cone).
        /// </summary>
        public static float ConeCosFromDegrees(float coneDegrees)
        {
            if (coneDegrees < 0f) coneDegrees = 0f;
            if (coneDegrees > 180f) coneDegrees = 180f;
            return (float)Math.Cos(coneDegrees * (Math.PI / 180.0));
        }

        /// <summary>
        /// Pick the candidate whose direction is closest in angle to <paramref name="forward"/>,
        /// among those inside the aim cone (<paramref name="coneCos"/> = <see cref="ConeCosFromDegrees"/>).
        ///
        /// Returns the winning index into <paramref name="dirs"/>, or <c>-1</c> when nothing is in cone
        /// (no aimed destination this frame). <paramref name="bestDot"/> receives the cosine of the
        /// angle to the winner (in [−1, 1]; <c>-1f</c> sentinel when the result is −1) so the caller can
        /// surface a tightness/feel diagnostic without recomputing.
        ///
        /// Robust to unnormalized inputs (each direction + the forward are normalized internally).
        /// Degenerate directions (near-zero length — e.g. a candidate the player is standing on, or a
        /// zeroed forward) are skipped / fail closed. Stable: on an exact dot tie the lower index wins,
        /// so the selection does not flicker between two equidistant-angle candidates across frames.
        /// </summary>
        public static int PickByAim(IReadOnlyList<AimVec> dirs, AimVec forward, float coneCos, out float bestDot)
        {
            bestDot = -1f;
            if (dirs == null || dirs.Count == 0) return -1;

            // Normalize the forward once. A zeroed forward (no camera) ⇒ nothing selectable.
            float fSqr = forward.SqrMagnitude;
            if (fSqr < MinSqrMagnitude) return -1;
            float fInv = 1f / (float)Math.Sqrt(fSqr);
            float fx = forward.X * fInv;
            float fy = forward.Y * fInv;
            float fz = forward.Z * fInv;

            int best = -1;
            float bestCos = coneCos;   // start at the cone gate: only an in-cone candidate can beat it
            // Strictly-greater comparison below means the FIRST candidate to reach a given cos wins the
            // tie (lower index), and a candidate exactly on the cone edge (== coneCos) is admitted on the
            // first hit but not displaced by a later equal one — stable, flicker-free.
            bool any = false;

            for (int i = 0; i < dirs.Count; i++)
            {
                AimVec d = dirs[i];
                float dSqr = d.SqrMagnitude;
                if (dSqr < MinSqrMagnitude) continue;     // standing on it / degenerate — skip
                float dInv = 1f / (float)Math.Sqrt(dSqr);
                float dot = (d.X * dInv) * fx + (d.Y * dInv) * fy + (d.Z * dInv) * fz;

                if (!any)
                {
                    // First admissible candidate must clear the cone gate.
                    if (dot >= coneCos)
                    {
                        any = true;
                        best = i;
                        bestCos = dot;
                    }
                }
                else if (dot > bestCos)
                {
                    best = i;
                    bestCos = dot;
                }
            }

            if (best >= 0) bestDot = bestCos;
            return best;
        }
    }
}
