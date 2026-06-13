// ============================================================================
//  SBPR.CartographyPreview — §2E.2 headless preview harness plugin (issue 10)
// ----------------------------------------------------------------------------
//  A throwaway BepInEx plugin (NOT part of the shipped mod) that runs the SHIPPED
//  CartographyComposer against the REAL, live WorldGenerator inside the dedicated
//  server's Unity runtime, then writes preview PNGs to disk. Because it links the
//  shipped composer SOURCE (see the .csproj <Compile Include .../>), the preview is
//  byte-identical to what the in-game MapViewer renders — the verification leg that
//  was missing when §2E shipped blind (AT-RENDER-PREVIEW).
//
//  Route P2 (real WorldGenerator). P1 (port the worldgen math headless) was
//  empirically rejected: WorldGenerator.GetBiome/GetBiomeHeight bottom out in
//  DUtils.PerlinNoise -> UnityEngine.Mathf.PerlinNoise, a Unity native ECall that
//  throws "ECall methods must be packaged into a system module" under bare .NET.
//
//  Capture targets are read from the env var SBPR_PREVIEW_WINDOWS (semicolon-
//  separated "name,worldX,worldZ,radius") so the same plugin captures any seed/origin
//  without a rebuild; a sensible default set is used if unset.
// ============================================================================

using System;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Features.Cartography;

namespace SBPR.CartographyPreview
{
    [BepInPlugin(GUID, "SBPR Cartography Preview", "0.1.0")]
    public class PreviewPlugin : BaseUnityPlugin
    {
        public const string GUID = "net.danielgreen.sbpr.cartographypreview";

        internal static ManualLogSource? Log;
        private static bool _done;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("[SBPR/Preview] harness plugin loaded — waiting for WorldGenerator.Initialize.");
            new Harmony(GUID).PatchAll(typeof(WorldGenInitPatch));
        }

        /// <summary>
        /// Postfix on WorldGenerator.Initialize(World): worldgen is now live + deterministic
        /// from the server seed. Render the configured preview windows once.
        /// </summary>
        [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.Initialize))]
        private static class WorldGenInitPatch
        {
            private static void Postfix()
            {
                if (_done) return;
                _done = true;
                try { CaptureAll(); }
                catch (Exception e) { Log?.LogError("[SBPR/Preview] capture failed: " + e); }
            }
        }

        private static void CaptureAll()
        {
            var wg = WorldGenerator.instance;
            if (wg == null) { Log?.LogError("[SBPR/Preview] WorldGenerator.instance null after Initialize."); return; }

            string outDir = Environment.GetEnvironmentVariable("SBPR_PREVIEW_OUTDIR") ?? "/config/preview";
            Directory.CreateDirectory(outDir);

            const float pixelSize = 64f;   // vanilla Minimap.m_pixelSize
            const int textureSize = 256;   // vanilla Minimap.m_textureSize
            const int upscale = 12;        // on-disk preview scale (33*12 ≈ 396 px) — readable PNG

            var sampler = new HarnessSampler(wg);
            var palette = CartographyPalette.Vanilla; // headless: no Minimap.instance, use the locked literals

            foreach (var win in ParseWindows())
            {
                var spec = BoundedMapMath.ComputeWindow(win.x, win.z, win.radius, pixelSize, textureSize);
                Color32[] px = CartographyComposer.Compose(sampler, palette, spec, pixelSize, textureSize);
                string path = Path.Combine(outDir, $"preview_{win.name}.png");
                PngWriter.Write(path, px, spec.Size, spec.Size, upscale, flipY: true);
                Log?.LogInfo($"[SBPR/Preview] wrote {path}  (origin {win.x:F0},{win.z:F0} r={win.radius:F0} size={spec.Size})");
            }

            Log?.LogWarning("[SBPR/Preview] DONE — preview PNGs written. You can stop the server now.");
        }

        private readonly struct Win
        {
            public readonly string name; public readonly float x, z, radius;
            public Win(string n, float x, float z, float r) { name = n; this.x = x; this.z = z; radius = r; }
        }

        /// <summary>Parse SBPR_PREVIEW_WINDOWS ("name,x,z,radius;..."), or a default spanning
        /// spawn, a shoreline, and a biome-boundary so water+biome+relief all show.</summary>
        private static Win[] ParseWindows()
        {
            string? env = Environment.GetEnvironmentVariable("SBPR_PREVIEW_WINDOWS");
            if (!string.IsNullOrWhiteSpace(env))
            {
                var list = new System.Collections.Generic.List<Win>();
                foreach (var part in env!.Split(';'))
                {
                    var f = part.Split(',');
                    if (f.Length != 4) continue;
                    if (float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) &&
                        float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float r))
                        list.Add(new Win(f[0].Trim(), x, z, r));
                }
                if (list.Count > 0) return list.ToArray();
            }
            return new[]
            {
                new Win("spawn",     0f,    0f,    1000f),  // spawn meadows + nearby coast
                new Win("coast_e",   1500f, 0f,    1000f),  // eastward — likely a shoreline
                new Win("far_n",     0f,    4000f, 1000f),  // north — biome transition + relief
            };
        }
    }

    /// <summary>
    /// Harness <see cref="IBiomeSampler"/> over the live server WorldGenerator. Identical
    /// surface to the shipped <c>WorldGeneratorSampler</c>; kept separate only so the harness
    /// doesn't depend on the shipped sampler living in the same assembly.
    /// </summary>
    internal sealed class HarnessSampler : IBiomeSampler
    {
        private readonly WorldGenerator _wg;
        public HarnessSampler(WorldGenerator wg) { _wg = wg; }
        public Heightmap.Biome GetBiome(float wx, float wy) => _wg.GetBiome(wx, wy);
        public float GetBiomeHeight(Heightmap.Biome biome, float wx, float wy) => _wg.GetBiomeHeight(biome, wx, wy, out _);
    }
}
