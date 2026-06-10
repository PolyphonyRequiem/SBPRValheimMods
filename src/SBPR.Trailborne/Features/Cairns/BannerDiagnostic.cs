using System.Text;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cairns
{
    /// <summary>
    /// TEMPORARY Step-1 diagnostic (card t_7de074f3 — cairn-banner ATTEMPT #6). Proves the
    /// Cloth failure mode BEFORE any fix is written, because Daniel's in-game observation
    /// ("a one-end-anchored cloth with gravity ON cannot stand upright — it MUST hang down,
    /// yet both the real-Cloth and the shader-only banners stand up and look identical") makes
    /// it airtight that five prior attempts tuned a solver that was never integrating. We refuse
    /// to write a sixth blind fix; this component makes the runtime state observable instead.
    ///
    /// Attached to the banner GameObject right after its renderer + (Option A) Cloth are built;
    /// logs a greppable <c>[BannerDiag]</c> report from Start through +4 s, then self-disables.
    /// Every number below is something STATIC ANALYSIS CANNOT PRODUCE — which is exactly why the
    /// prior attempts (all static reasoning about pins/stiffness/dims) kept missing the cause:
    ///
    ///   1. SCALE CHAIN — walks banner → … → root and flags any NON-UNIFORM lossyScale.
    ///      UnityEngine.Cloth silently refuses to simulate under skewed world scale; this is the
    ///      card's PRIME SUSPECT and Cloth's #1 real-world failure mode. The constituent
    ///      transforms are all localScale=1 by static reading, so a non-uniform lossyScale can
    ///      only be injected at runtime (placement/registration) — only this probe can see it.
    ///   2. CLOTH STATE — enabled, particle/coefficient/mesh-vertex counts, pinned-vs-free
    ///      split, useGravity, stiffness, current externalAcceleration.
    ///   3. ORIENTATION (Daniel's actual complaint, yaw-immune) — world-Y of the pinned mount
    ///      vs the free tail tip: does the rest pose HANG DOWN (tail below mount) or STAND UP
    ///      (tail above mount)? The default align mode is a pure yaw about world-up, which leaves
    ///      world-Y untouched, so this is robust to the wind driver's rotation. It runs for the
    ///      cloth-LESS Option B too — a static mesh that "stands up" proves the cause is
    ///      geometry/transform (SHARED by both options), not the solver.
    ///   4. SOLVER LIVENESS — the card's exact probe: sample the tail particle's LOCAL y (and the
    ///      max per-particle local displacement) once a second. Transform rotation does NOT write
    ///      back into Cloth.vertices (they are local), so a tail-y that never changes ⇒ solver
    ///      INERT; a value that falls then settles ⇒ solver RUNNING. Each sample also logs the
    ///      transform euler so the reader can confirm whether the driver's rotation was active.
    ///   5. VERDICT — layered lines naming the suspect, derived from 1–4 (scale, orientation,
    ///      liveness reported as distinct findings rather than one early-return guess).
    ///
    /// Pure client art: only ever attached on a client (the banner is never built on a headless
    /// server — BuildBanner bails on IsHeadless before this point), touches no ZDO / network /
    /// gameplay, removes itself after 4 s. DELETE this file + its wiring once the attempt-#6
    /// rebuild lands and is verified.
    /// </summary>
    public sealed class BannerDiagnostic : MonoBehaviour
    {
        // Set by CairnTag right after AddComponent, while the GameObject is still being built.
        public string Label = "?";          // "<color>/A-cloth" or "<color>/B-shader"
        public Mesh? BakedMesh;             // the per-instance baked banner mesh (rest geometry)
        public Cloth? Cloth;                // null for Option B (shader-wave: a static mesh, no solver)

        private Vector3[]? _initialClothLocal;   // particle positions at first post-activation frame (≈rest)
        private int _tailIdx = -1;               // particle with the lowest rest-pose local-Y (the tail tip)
        private bool _anyNonUniformScale;
        private float _peakLocalMove;            // largest cumulative per-particle local displacement seen
        private float _tailWorldYFirst = float.NaN;  // tail world-Y at the first sample (gravity-settle ref)
        private Vector3 _eulerFirst;             // transform euler at first sample (to see if the driver rotated)
        private bool _eulerChanged;
        private int _sample;
        private const int MaxSamples = 4;

        private void Start()
        {
            try { ReportStatic(); }
            catch (System.Exception e) { Plugin.Log.LogError($"[BannerDiag] {Label} static probe threw: {e}"); }

            // Baseline particle snapshot + tail-tip index (lowest rest-pose local-Y). This is the
            // FIRST post-activation frame, not a guaranteed pre-simulation rest pose — but the
            // inert-vs-running gap (a tail-y that never moves vs one that falls and settles over
            // 4 s) dwarfs a single step, so this baseline is decisive.
            var cloth = Cloth;
            if (cloth != null)
            {
                try
                {
                    _initialClothLocal = cloth.vertices;   // Cloth.vertices returns a fresh local-space copy
                    float minY = float.PositiveInfinity;
                    for (int i = 0; i < _initialClothLocal.Length; i++)
                        if (_initialClothLocal[i].y < minY) { minY = _initialClothLocal[i].y; _tailIdx = i; }
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"[BannerDiag] {Label} initial cloth snapshot threw: {e}");
                }
            }

            InvokeRepeating(nameof(Sample), 1f, 1f);
        }

        private void ReportStatic()
        {
            // ── 1. SCALE CHAIN (prime suspect) ────────────────────────────────────────
            var sb = new StringBuilder();
            Transform? t = transform;
            int depth = 0;
            while (t != null && depth < 12)
            {
                Vector3 ls = t.lossyScale;
                bool u = IsUniform(ls);
                if (!u) _anyNonUniformScale = true;
                sb.Append("    [").Append(depth).Append("] '").Append(t.name).Append("' localScale=")
                  .Append(Fmt(t.localScale)).Append(" lossyScale=").Append(Fmt(ls))
                  .Append(u ? "  uniform" : "  NON-UNIFORM <<<").Append('\n');
                t = t.parent;
                depth++;
            }
            Plugin.Log.LogWarning(
                $"[BannerDiag] {Label} SCALE CHAIN ({(_anyNonUniformScale ? "FAIL — non-uniform scale present (Cloth won't simulate)" : "PASS — all uniform")}):\n{sb}");

            // ── 2a. Rest geometry (does the mesh sit below its pivot?) ─────────────────
            var mesh = BakedMesh;
            if (mesh != null)
            {
                Bounds b = mesh.bounds;   // local-space rest bounds
                bool below = b.max.y <= 0.05f && b.min.y < -0.05f;
                Plugin.Log.LogWarning(
                    $"[BannerDiag] {Label} MESH '{mesh.name}' localBounds center={Fmt(b.center)} size={Fmt(b.size)} " +
                    $"y∈[{b.min.y:0.000},{b.max.y:0.000}] verts={mesh.vertexCount} → " +
                    $"{(below ? "geometry BELOW pivot (rest pose SHOULD hang down)" : "geometry NOT cleanly below pivot (pivot/seating may be wrong)")}");
            }

            // ── 2b. Cloth configuration ───────────────────────────────────────────────
            var c = Cloth;
            if (c != null)
            {
                var coeffs = c.coefficients;
                int pinned = 0, free = 0;
                if (coeffs != null)
                    foreach (var cc in coeffs) { if (cc.maxDistance <= 1e-4f) pinned++; else free++; }
                int particles = -1;
                try { particles = c.vertices.Length; } catch { /* report -1 */ }
                Plugin.Log.LogWarning(
                    $"[BannerDiag] {Label} CLOTH enabled={c.enabled} particles={particles} " +
                    $"coeffs={(coeffs != null ? coeffs.Length : -1)} meshVerts={(mesh != null ? mesh.vertexCount : -1)} " +
                    $"pinned={pinned} free={free} useGravity={c.useGravity} " +
                    $"stretch={c.stretchingStiffness:0.00} bend={c.bendingStiffness:0.00} damping={c.damping:0.00} " +
                    $"extAccel={Fmt(c.externalAcceleration)}");
            }
            else
            {
                Plugin.Log.LogWarning(
                    $"[BannerDiag] {Label} CLOTH none — Option B is a STATIC mesh + wind-shader material, no solver. " +
                    "If THIS stands up, it proves the 'stands up' is geometry/transform, NOT the Cloth solver.");
            }
        }

        private void Sample()
        {
            _sample++;
            try
            {
                Vector3 wpos = transform.position;
                Vector3 euler = transform.rotation.eulerAngles;
                if (_sample == 1) _eulerFirst = euler;
                else if ((euler - _eulerFirst).sqrMagnitude > 1f) _eulerChanged = true;

                float maxLocalMove = 0f;
                float tailLocalY = float.NaN, mountWorldY = float.NaN, tailWorldY = float.NaN;

                var cloth = Cloth;
                var mesh = BakedMesh;
                Matrix4x4 l2w = transform.localToWorldMatrix;

                if (cloth != null && _initialClothLocal != null)
                {
                    Vector3[] cur = cloth.vertices;
                    if (cur.Length == _initialClothLocal.Length)
                    {
                        var coeffs = cloth.coefficients;
                        float mountYSum = 0f; int mountN = 0;
                        for (int i = 0; i < cur.Length; i++)
                        {
                            float d = (cur[i] - _initialClothLocal[i]).magnitude;   // LOCAL displacement (rotation-immune)
                            if (d > maxLocalMove) maxLocalMove = d;
                            if (coeffs != null && i < coeffs.Length && coeffs[i].maxDistance <= 1e-4f)
                            { mountYSum += l2w.MultiplyPoint3x4(cur[i]).y; mountN++; }   // pinned mount world-Y
                        }
                        if (mountN > 0) mountWorldY = mountYSum / mountN;
                        if (_tailIdx >= 0 && _tailIdx < cur.Length)
                        {
                            tailLocalY = cur[_tailIdx].y;                               // the card's exact probe
                            tailWorldY = l2w.MultiplyPoint3x4(cur[_tailIdx]).y;        // yaw-immune orientation
                        }
                        if (maxLocalMove > _peakLocalMove) _peakLocalMove = maxLocalMove;
                    }
                    else
                    {
                        Plugin.Log.LogWarning(
                            $"[BannerDiag] {Label} t+{_sample}s cloth particle count changed " +
                            $"({_initialClothLocal.Length}→{cur.Length}); liveness probe skipped this sample.");
                    }
                }
                else if (cloth == null && mesh != null)
                {
                    // Option B: a static mesh under the banner transform. World-Y of the mesh top
                    // (pivot/mount) vs bottom (tail) — no solver, pure geometry under the transform.
                    Bounds b = mesh.bounds;
                    mountWorldY = l2w.MultiplyPoint3x4(new Vector3(b.center.x, b.max.y, b.center.z)).y;
                    tailWorldY = l2w.MultiplyPoint3x4(new Vector3(b.center.x, b.min.y, b.center.z)).y;
                    tailLocalY = b.min.y;
                }

                if (_sample == 1 && !float.IsNaN(tailWorldY)) _tailWorldYFirst = tailWorldY;

                string hang = (float.IsNaN(mountWorldY) || float.IsNaN(tailWorldY)) ? "n/a"
                    : (tailWorldY < mountWorldY - 0.02f ? "HANGS-DOWN"
                       : (tailWorldY > mountWorldY + 0.02f ? "STANDS-UP <<<" : "~flat/horizontal"));

                Plugin.Log.LogWarning(
                    $"[BannerDiag] {Label} t+{_sample}s pos={Fmt(wpos)} euler={Fmt(euler)} " +
                    $"tailLocalY={tailLocalY:0.0000} maxLocalMove={maxLocalMove:0.0000} " +
                    $"mountWorldY={mountWorldY:0.000} tailWorldY={tailWorldY:0.000} → {hang}");

                if (_sample >= MaxSamples)
                {
                    CancelInvoke(nameof(Sample));
                    Verdict(hang);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[BannerDiag] {Label} sample {_sample} threw: {e}");
                CancelInvoke(nameof(Sample));
            }
        }

        // Layered verdict: report scale, orientation, and solver-liveness as DISTINCT findings.
        // Daniel's complaint is "stands up", so orientation leads; liveness explains why A≈B.
        private void Verdict(string hang)
        {
            var sb = new StringBuilder();
            var cloth = Cloth;

            // (a) Prime suspect — orthogonal, report whenever present.
            if (_anyNonUniformScale)
                sb.Append("SCALE=FAIL (PRIME SUSPECT: non-uniform parent lossyScale — UnityEngine.Cloth refuses to "
                    + "simulate under skewed world scale; fix by re-parenting/neutralizing so the banner lossyScale is uniform). ");
            else
                sb.Append("SCALE=uniform (prime suspect ruled out). ");

            // (b) Orientation — Daniel's actual visual complaint, yaw-immune.
            if (hang.StartsWith("STANDS-UP"))
                sb.Append("ORIENTATION=STANDS-UP — Daniel's bug REPRODUCED: the tail settles ABOVE the mount in world-Y. "
                    + "This is geometry/transform (baked mesh not below pivot, or a flipped/rotated parent), a cause SHARED by "
                    + "Option A and Option B. The Step-2 rebuild's clean planar grid + explicit edge-pin fixes this by construction. ");
            else if (hang.StartsWith("HANGS-DOWN"))
                sb.Append("ORIENTATION=HANGS-DOWN — the tail rests BELOW the mount (gravity-correct seating) in THIS build/config. ");
            else
                sb.Append($"ORIENTATION={hang}. ");

            // (c) Solver liveness — only meaningful for Option A (Option B has no solver by design).
            if (cloth == null)
            {
                sb.Append("SOLVER=n/a (Option B is a static mesh, no Cloth — if it STANDS-UP that alone proves the cause is NOT the solver). ");
            }
            else if (_initialClothLocal == null)
            {
                sb.Append("SOLVER=unknown (could not snapshot cloth particles — see earlier error). ");
            }
            else if (_peakLocalMove < 0.01f)
            {
                sb.Append($"SOLVER=INERT — cloth particles moved <1cm over {MaxSamples}s with gravity "
                    + $"{(cloth.useGravity ? "ON" : "OFF")} (transform rotation does not alter local Cloth.vertices, so this is "
                    + "rotation-immune). The Cloth is enabled yet NOT stepping (the card's suspect #2) — exactly why expensive "
                    + "Option A looked identical to free Option B. Fix belongs in how the Cloth/SMR is constructed, not pins/stiffness/wind. ");
            }
            else
            {
                sb.Append($"SOLVER=RUNNING — cloth particles moved (peak {_peakLocalMove:0.000}m local"
                    + $"{(_eulerChanged ? "; note the wind driver also rotated the transform during the window" : "")}). The solver IS integrating. ");
            }

            Plugin.Log.LogWarning($"[BannerDiag] {Label} ===== VERDICT: {sb}");
            enabled = false;   // one-shot; stop ticking
        }

        private static bool IsUniform(Vector3 s, float tol = 1e-3f) =>
            Mathf.Abs(s.x - s.y) <= tol && Mathf.Abs(s.y - s.z) <= tol && Mathf.Abs(s.x - s.z) <= tol;

        private static string Fmt(Vector3 v) => $"({v.x:0.0000},{v.y:0.0000},{v.z:0.0000})";
    }
}
