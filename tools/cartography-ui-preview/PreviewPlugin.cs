// ============================================================================
//  SBPR.CartographyUiPreview — THROWAWAY GPU verification harness (NOT shipped)
// ----------------------------------------------------------------------------
//  Renders the real MapSurface (the §2E.5 render-correctness fix + A′ marker) on
//  a GPU client and dumps PNGs, so the GPU-only acceptance tests can be judged
//  WITHOUT a human navigating in-world:
//    AT-DISC-CLIP   — transparent outside the disc (no black square)
//    AT-FOG-VANILLA — unexplored = vanilla's real fog-of-war cloud (shader), not flat fill
//    AT-DISC-FILL   — continuous disc, no diamond / corner ocean bleed
//    AT-DISC-MARKER-1 — the player marker is the vanilla "you are here" glyph (logged)
//
//  WHY in-world (not at boot): MapSurface.TryRenderVanillaShader clones
//  Minimap.instance.m_mapImageLarge.material, whose _MainTex/_FogTex are only
//  populated after Minimap.Start runs inside a loaded world (decomp Minimap.cs
//  :464/:658 require ZNet.World). At the main menu there is no Minimap, so a boot
//  render would fall through to the 2-colour PaintFog fallback and prove nothing
//  about the cloud. So we hook Minimap.Start (postfix) — it fires exactly when a
//  world has loaded and the map material is live — then build + capture there.
//
//  Capture trigger: set SBPR_UIPREVIEW_OUT=/abs/dir. The harness waits N frames
//  after Minimap.Start for the GPU to composite, renders the disc + modal at a
//  synthetic survey, ReadPixels → EncodeToPNG, writes disc.png + modal.png, logs
//  the marker path (vanilla vs chevron), and quits the game so the run is
//  unattended end-to-end.
// ============================================================================

using System;
using System.Collections;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Features.Cartography;

namespace SBPR.CartographyUiPreview
{
    [BepInPlugin("net.danielgreen.sbpr.cartographyuipreview", "SBPR Cartography UI Preview", "0.1.0")]
    public sealed class PreviewPlugin : BaseUnityPlugin
    {
        internal static string? OutDir;
        internal static ManualLogSourceShim Log = null!;
        private static bool _fired;

        private void Awake()
        {
            Log = new ManualLogSourceShim(Logger);
            OutDir = Environment.GetEnvironmentVariable("SBPR_UIPREVIEW_OUT");
            if (string.IsNullOrEmpty(OutDir))
            {
                Logger.LogWarning("[UIPreview] SBPR_UIPREVIEW_OUT not set — harness idle (no capture).");
                return;
            }
            Directory.CreateDirectory(OutDir);
            new Harmony("net.danielgreen.sbpr.cartographyuipreview").PatchAll(typeof(MinimapStartPatch));
            Logger.LogInfo($"[UIPreview] armed — will capture to {OutDir} when a world's Minimap starts.");
        }

        [HarmonyPatch(typeof(Minimap), "Start")]
        private static class MinimapStartPatch
        {
            private static void Postfix(Minimap __instance)
            {
                if (_fired || string.IsNullOrEmpty(OutDir)) return;
                _fired = true;
                __instance.StartCoroutine(CaptureRoutine());
            }
        }

        // Build a synthetic survey: a partial-disc reveal (so we see BOTH lit cartography AND the
        // unexplored cloud in one frame) centred on the player (or origin), at the canonical 1000 m
        // bound, native pixelSize=64 / textureSize=256. This exercises BindBoundedReveal exactly as a
        // real Surveyor's Table would, without needing the player to have actually explored anything.
        private static SurveyData BuildSyntheticSurvey()
        {
            const float pixelSize = 64f;
            const int textureSize = 256;
            const float radius = 1000f;

            Vector3 c = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;

            int cr = Mathf.CeilToInt(radius / pixelSize);     // 16
            int size = 2 * cr + 1;                              // 33
            int half = (size - 1) / 2;
            var fog = new bool[size * size];
            float r2 = radius * radius;

            for (int wy = 0; wy < size; wy++)
            {
                for (int wx = 0; wx < size; wx++)
                {
                    float offX = (wx - half) * pixelSize;
                    float offZ = (wy - half) * pixelSize;
                    bool inDisc = (offX * offX + offZ * offZ) <= r2;
                    // Reveal ~60% of the disc (the lower-left lobe) so the rest renders as the vanilla
                    // cloud — gives the eye an explored/unexplored boundary to judge AT-FOG-VANILLA.
                    bool revealed = inDisc && (offX < 250f || offZ < 250f);
                    fog[wy * size + wx] = revealed;
                }
            }

            return new SurveyData
            {
                OriginX = c.x,
                OriginZ = c.z,
                PixelSize = pixelSize,
                TextureSize = textureSize,
                RadiusMeters = radius,
                Size = size,
                Fog = fog,
            };
        }

        private static IEnumerator CaptureRoutine()
        {
            // Minimap.Start fires while the world is still "Connecting" — the map material's
            // _MainTex/_FogTex aren't populated yet and Player.m_localPlayer is null. Wait for the
            // player to actually spawn AND a few seconds for the map textures to upload, so we capture
            // the REAL cartography (AT-FOG-VANILLA) and the marker resolves against a live player.
            float waited = 0f;
            while (Player.m_localPlayer == null && waited < 120f)
            {
                yield return new WaitForSeconds(0.5f);
                waited += 0.5f;
            }
            Log.Info($"[UIPreview] player ready after {waited:F1}s (null={Player.m_localPlayer == null}); settling map textures…");
            // Extra settle so the fog/biome textures finish uploading after spawn.
            yield return new WaitForSeconds(4f);

            Log.Info("[UIPreview] building MapSurfaces…");
            var survey = BuildSyntheticSurvey();
            Vector3 origin = new Vector3(survey.OriginX, 0f, survey.OriginZ);

            var host = new GameObject("SBPR_UIPreviewHost");
            UnityEngine.Object.DontDestroyOnLoad(host);

            // DISC (player-centred, TargetPx=200, FIXED 400m tight span) and MODAL (table-centred,
            // TargetPx=900, full-survey span) — the two real configs from MapViewer, so we capture exactly
            // what ships. Disc anchored centre-screen + bigger ONLY for a legible capture (in-game it's a
            // 200px top-right corner element); the ViewSpanMeters=400 zoom is the SHIPPED value.
            var discCfg = new MapSurfaceConfig
            {
                TargetPx = 600, SortingOrder = 3000, ShowBackdrop = false, ShowPrompts = false,
                HandleInput = false, PlayerCentred = true,
                ScreenAnchor = new Vector2(0.5f, 0.5f), CornerMarginPx = 16f,
                ViewSpanMeters = 400f,   // SHIPPED disc zoom — the tight nav window
            };
            var modalCfg = new MapSurfaceConfig
            {
                TargetPx = 900, SortingOrder = 5000, ShowBackdrop = true, ShowPrompts = false,
                HandleInput = false, PlayerCentred = false,
                ScreenAnchor = new Vector2(0.5f, 0.5f),
            };

            var req = new MapViewRequest
            {
                Survey = survey, BoundOrigin = origin, RadiusMeters = survey.RadiusMeters,
                Mode = MapViewerMode.FieldReadOnly, Title = "UIPreview",
            };

            // --- DISC first (no backdrop, so its transparent-outside is visible over the game world) ---
            var disc = new MapSurface(host.transform, discCfg);
            disc.Show(req);
            for (int i = 0; i < 5; i++) { disc.TickRotation(); yield return new WaitForEndOfFrame(); }
            yield return CaptureScreen(Path.Combine(OutDir!, "disc.png"));
            disc.Hide();

            // --- MODAL (full-screen, dim backdrop) ---
            var modal = new MapSurface(host.transform, modalCfg);
            modal.Show(req);
            for (int i = 0; i < 5; i++) { modal.TickRotation(); yield return new WaitForEndOfFrame(); }
            yield return CaptureScreen(Path.Combine(OutDir!, "modal.png"));
            modal.Hide();

            Log.Info("[UIPreview] capture complete; writing DONE sentinel and quitting.");
            try { File.WriteAllText(Path.Combine(OutDir!, "DONE"), DateTime.UtcNow.ToString("o")); } catch { }
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        private static IEnumerator CaptureScreen(string path)
        {
            yield return new WaitForEndOfFrame(); // ReadPixels must run after the frame is drawn
            int w = Screen.width, h = Screen.height;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            try
            {
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Log.Info($"[UIPreview] wrote {path} ({w}x{h}).");
            }
            catch (Exception e) { Log.Warn($"[UIPreview] PNG write failed: {e.Message}"); }
            UnityEngine.Object.Destroy(tex);
        }
    }

    // Tiny shim so the plugin's static helpers can log without threading the BepInEx logger everywhere.
    internal sealed class ManualLogSourceShim
    {
        private readonly BepInEx.Logging.ManualLogSource _l;
        public ManualLogSourceShim(BepInEx.Logging.ManualLogSource l) { _l = l; }
        public void Info(string m) => _l.LogInfo(m);
        public void Warn(string m) => _l.LogWarning(m);
    }
}
