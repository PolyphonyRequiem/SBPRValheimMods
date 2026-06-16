using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Spike
{
    /// <summary>
    /// ⚠️ THROWAWAY PHASE-0 SPIKE — NOT SHIPPABLE CODE (card t_5baa81c9). ⚠️
    ///
    /// Purpose: prove/disprove the "Stone of Drought" LOGICAL water-repel divot against the REAL
    /// vanilla water system, and let a human observe what the UNTOUCHED visual water does over a
    /// logically-carved bowl. Source of truth: docs/design/stone-of-drought-feasibility.md (#147).
    /// This file is deliberately self-contained under Spike/ and is wired into Plugin.Awake ONLY on
    /// the throwaway spike branch (spike/water-repel-drought-t_5baa81c9). Do NOT merge to v1.
    ///
    /// CLEAN-ROOM (ADR-0001): patches base-game <c>WaterVolume.GetWaterSurface</c> only. Reads no
    /// other mod's code; reproduces the divot from vanilla water internals (the doc walls off Rune
    /// Magic). All vanilla surfaces below were grounded against the decomp before writing:
    ///   • WaterVolume.GetWaterSurface(Vector3 point, float waveFactor=1f) — INSTANCE method,
    ///     decomp assembly_valheim.decompiled.cs:127768. Body returns
    ///     transform.position.y + wave + m_surfaceOffset. Postfix target (__instance + __result + point).
    ///   • Heightmap.GetHeight(Vector3 worldPos, out float height) — STATIC, decomp :109529.
    ///     Returns the TRUE WORLD seabed height (terrain heightmap only, no objects, no raycast).
    ///     ⚠️ NOTE the doc (#147) suggested WaterVolume.Depth() for the seabed clamp — that is WRONG:
    ///     Depth() (:127831) returns a NORMALIZED [0,1] depth (bilerp of m_normalizedDepth), not a
    ///     world height. Heightmap.GetHeight is the correct clamp source. (Correction carried into
    ///     the findings doc.)
    ///   • Player.m_localPlayer (:15449), Terminal.ConsoleCommand / ConsoleEventArgs (:35392) — for
    ///     the in-engine planting commands, mirroring the repo's BannerDiagCommand pattern.
    ///
    /// THE ONE THING NOTHING HEADLESS CAN ANSWER, and why this spike exists: lowering the LOGICAL
    /// surface (this postfix) does NOT move the rendered water mesh — that is a sibling GPU-driven
    /// MeshRenderer (vprefab: the vanilla "Water" prefab is a WaterVolume node + a SEPARATE
    /// WaterSurface node, 1025-vert subdivided grid). So a human must JOIN A CLIENT and look.
    /// </summary>
    [HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.GetWaterSurface))]
    internal static class WaterRepelSpike
    {
        // ── Live, console-tunable knobs (spike only — no config persistence) ────────────────
        // Defaults chosen to read clearly at a shoreline: a ~8 m bowl, a gentle cosine wall, the
        // seabed-floor clamp ON from the first commit (the doc's depth-limit, AC2). A human flips
        // these live with the `drought` console command to feel shallow-vs-deep and falloff shapes.

        /// <summary>Active test stones. Hardcoded-point spike: planted at the player's feet on demand.</summary>
        private static readonly List<Vector3> s_stones = new List<Vector3>();

        /// <summary>Radius of the divot's influence, metres. Beyond this, water is untouched.</summary>
        internal static float Radius = 8f;

        /// <summary>Maximum subtraction at the stone's centre, metres (the "well depth" cap — the doc's hard well cap, Q97).</summary>
        internal static float MaxWell = 6f;

        /// <summary>
        /// When true, never carve below the true seabed (Heightmap.GetHeight + Epsilon). This is the
        /// doc's "seabed-floor clamp" (AC2) and the natural depth-limit: shallow water → bowl reaches
        /// the seabed (stand on dry ground); deep water → same subtraction is clamped (only a surface
        /// dimple, can't dig a dry shaft to the ocean floor). ON by default, from commit 1.
        /// </summary>
        internal static bool SeabedClamp = true;

        /// <summary>Clearance above the seabed the clamp leaves, metres (so the floor reads as walkable, not z-fighting the ground).</summary>
        internal static float SeabedEpsilon = 0.15f;

        /// <summary>
        /// Falloff shape. true = SOFT (the doc's lean): bowl fades smoothly to nothing at the rim,
        /// no hard seam. false = HARD-ish cosine still, but full strength held longer then dropped.
        /// Both are radial cosine variants; this lets the observer compare the two the doc names.
        /// </summary>
        internal static bool SoftFalloff = true;

        /// <summary>Master switch so the observer can A/B "divot ON vs OFF" at the same spot without moving.</summary>
        internal static bool Enabled = true;

        // ── The hook ─────────────────────────────────────────────────────────────────────────
        // POSTFIX on the instance method. __result is vanilla's computed logical surface height;
        // we subtract a radial well for points near any active stone, then (optionally) clamp to the
        // seabed. Because EVERYTHING reads GetWaterSurface (swim depth, boat buoyancy, IsUnderWater),
        // lowering __result is the gameplay-true repel: the player should be able to stand on the
        // exposed seabed inside the bowl. That "should" is exactly what the human verifies (AC3-a).
        [HarmonyPostfix]
        private static void Postfix(WaterVolume __instance, Vector3 point, ref float __result)
        {
            try
            {
                if (!Enabled) return;
                int n = s_stones.Count;
                if (n == 0) return;

                // Find the strongest well at this point across all active stones (max subtraction).
                float well = 0f;
                for (int i = 0; i < n; i++)
                {
                    Vector3 s = s_stones[i];
                    float dx = point.x - s.x;
                    float dz = point.z - s.z;
                    float distXZ = Mathf.Sqrt(dx * dx + dz * dz);
                    if (distXZ >= Radius) continue;

                    // Normalised distance 0 (centre) → 1 (rim).
                    float t = distXZ / Radius;

                    // Radial cosine falloff. cos(0)=1 at centre → cos(pi/2)=0 at rim, squared/eased.
                    // SOFT: a single raised-cosine bell (smooth to zero slope at the rim — no seam).
                    // HARD-ish: hold near-full strength across the inner half, then cosine-drop — a
                    // more legible "this is the dry floor, here's the wall" read.
                    float shape;
                    if (SoftFalloff)
                    {
                        // 0.5*(1+cos(pi*t)) — classic Hann window: 1 at centre, 0 at rim, flat slope both ends.
                        shape = 0.5f * (1f + Mathf.Cos(Mathf.PI * t));
                    }
                    else
                    {
                        // Hold full strength to t=0.5, then raised-cosine drop over the outer half.
                        if (t <= 0.5f) shape = 1f;
                        else shape = 0.5f * (1f + Mathf.Cos(Mathf.PI * (t - 0.5f) / 0.5f));
                    }

                    float thisWell = MaxWell * shape;
                    if (thisWell > well) well = thisWell;
                }

                if (well <= 0f) return;

                float carved = __result - well;

                // Seabed-floor clamp (AC2 / the doc's depth limit). Never carve below true seabed+ε.
                // In SHALLOW water the seabed is just under the surface → carved hits the floor → you
                // stand on dry ground. In DEEP water the seabed is far below → the clamp never binds →
                // you get a surface dimple no deeper than MaxWell, can't drain the ocean.
                if (SeabedClamp)
                {
                    if (Heightmap.GetHeight(point, out float seabed))
                    {
                        float floor = seabed + SeabedEpsilon;
                        if (carved < floor) carved = floor;
                    }
                    // If no heightmap (point outside loaded terrain), fall through with the raw carve —
                    // can't clamp what we can't sample. Spike-acceptable; the observer plants on land-adjacent shallows.
                }

                // Never RAISE the surface — only ever a repel/divot, never a bulge.
                if (carved < __result) __result = carved;
            }
            catch (Exception e)
            {
                // Fail OPEN — a spike bug must never brick the water system for the observer.
                Plugin.Log.LogWarning($"[Trailborne/SPIKE drought] GetWaterSurface postfix error (failing open): {e.Message}");
            }
        }

        // ── Observer console API ──────────────────────────────────────────────────────────────
        // Mirrors the repo's BannerDiagCommand (Terminal.InitTerminal postfix). isCheat:false so it
        // works without devcommands; isSecret:true keeps it out of the player help list.
        internal static void PlantAtPlayer()
        {
            var p = Player.m_localPlayer;
            if (p == null) return;
            s_stones.Add(p.transform.position);
        }

        internal static void Clear() => s_stones.Clear();

        internal static int Count => s_stones.Count;
    }

    /// <summary>
    /// Registers the <c>drought</c> dev console command (spike only). Lets the human plant/clear a
    /// test stone at their feet and live-tune the divot while standing in the water looking at it —
    /// the only way to answer AC3 (stand-on-seabed? what does the untouched visual water do?
    /// shallow-vs-deep read?). Mirrors BannerDiagCommand's Terminal.InitTerminal postfix pattern.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class WaterRepelSpikeCommand
    {
        private static bool _registered;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_registered) return;
            _registered = true;

            new Terminal.ConsoleCommand(
                "drought",
                "[SBPR SPIKE] water-repel divot test. Subcommands: plant | clear | on | off | " +
                "clamp <0|1> | soft <0|1> | radius <m> | well <m> | eps <m> | status",
                delegate (Terminal.ConsoleEventArgs args)
                {
                    string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
                    switch (sub)
                    {
                        case "plant":
                            WaterRepelSpike.PlantAtPlayer();
                            args.Context.AddString($"[SPIKE] planted stone of drought #{WaterRepelSpike.Count} at your feet. Walk into the water and look.");
                            break;
                        case "clear":
                            WaterRepelSpike.Clear();
                            args.Context.AddString("[SPIKE] cleared all stones. Water returns to vanilla.");
                            break;
                        case "on":
                            WaterRepelSpike.Enabled = true;
                            args.Context.AddString("[SPIKE] divot ENABLED.");
                            break;
                        case "off":
                            WaterRepelSpike.Enabled = false;
                            args.Context.AddString("[SPIKE] divot DISABLED (stones kept; vanilla surface). Toggle for A/B.");
                            break;
                        case "clamp":
                            if (args.Length > 2) WaterRepelSpike.SeabedClamp = args[2] == "1" || args[2].ToLowerInvariant() == "true";
                            args.Context.AddString($"[SPIKE] seabed clamp = {WaterRepelSpike.SeabedClamp}");
                            break;
                        case "soft":
                            if (args.Length > 2) WaterRepelSpike.SoftFalloff = args[2] == "1" || args[2].ToLowerInvariant() == "true";
                            args.Context.AddString($"[SPIKE] soft falloff = {WaterRepelSpike.SoftFalloff} ({(WaterRepelSpike.SoftFalloff ? "Hann bell" : "hold-then-drop")})");
                            break;
                        case "radius":
                            if (args.Length > 2 && float.TryParse(args[2], out float r)) WaterRepelSpike.Radius = Mathf.Clamp(r, 1f, 64f);
                            args.Context.AddString($"[SPIKE] radius = {WaterRepelSpike.Radius} m");
                            break;
                        case "well":
                            if (args.Length > 2 && float.TryParse(args[2], out float w)) WaterRepelSpike.MaxWell = Mathf.Clamp(w, 0.5f, 50f);
                            args.Context.AddString($"[SPIKE] max well depth = {WaterRepelSpike.MaxWell} m");
                            break;
                        case "eps":
                            if (args.Length > 2 && float.TryParse(args[2], out float e)) WaterRepelSpike.SeabedEpsilon = Mathf.Clamp(e, 0f, 2f);
                            args.Context.AddString($"[SPIKE] seabed epsilon = {WaterRepelSpike.SeabedEpsilon} m");
                            break;
                        default:
                            args.Context.AddString(
                                $"[SPIKE] stones={WaterRepelSpike.Count} enabled={WaterRepelSpike.Enabled} " +
                                $"radius={WaterRepelSpike.Radius}m well={WaterRepelSpike.MaxWell}m " +
                                $"clamp={WaterRepelSpike.SeabedClamp} eps={WaterRepelSpike.SeabedEpsilon}m soft={WaterRepelSpike.SoftFalloff}\n" +
                                "  drought plant   — drop a stone at your feet\n" +
                                "  drought off/on  — A/B the divot in place\n" +
                                "  drought clamp 0 — disable seabed clamp (see it try to drain deep water)\n" +
                                "  drought soft 0  — switch to hold-then-drop falloff");
                            break;
                    }
                },
                isCheat: false, isNetwork: false, onlyServer: false, isSecret: true);

            Plugin.Log.LogInfo("[Trailborne/SPIKE] Registered `drought` dev console command (throwaway spike t_5baa81c9).");
        }
    }
}
