// ============================================================================
//  SPIKE (throwaway — card t_e8bbbe48). NOT production code. Branch-only.
// ----------------------------------------------------------------------------
//  AT-SPIKE-RENDER proof object: a client-only hotkey viewer that
//    1. reflects the LIVE personal auto-map fog (Minimap.m_explored, private bool[])
//       + reads the public m_pixelSize / m_textureSize off the running game,
//    2. windows it to a 1000 m disc via BoundedMapMath (the over-provisioning fix),
//    3. paints OUR OWN Texture2D from the windowed array (NOT vanilla's 4-texture
//       shader composite), and
//    4. shows it full-screen on a custom uGUI Canvas + RawImage at a FIXED zoom,
//       everything outside the disc shrouded.
//
//  This file's VALUE in a headless spike: it compiles 0/0 against the REAL
//  assembly_valheim.dll + UnityEngine.UI.dll, which proves every API the fork
//  depends on (private-field reflection, Texture2D.SetPixels32, RawImage on a
//  Canvas, point filtering for fixed zoom) actually exists and is reachable —
//  i.e. the render path is real, not imagined. Pixel output itself can only be
//  eyeballed on a joined client (Daniel), per the repo's "logs-green != playable"
//  rule; the findings doc states exactly what was proven by execution vs compile.
//
//  Hotkeys (client only, needs a local player + a real graphics device):
//    F9  — toggle the bounded viewer. First press binds the origin to the
//          player's CURRENT position (the "test origin"); shroud everything > 1000 m.
//    F10 — while open, re-bind origin to current player pos & rebuild (walk-test
//          the windowing as you move).
// ============================================================================

using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace SBPR.Trailborne.Features.CartographySpike
{
    public class SpikeMapViewer : MonoBehaviour
    {
        // Spike constants — the v2 lock: 1000 m radius, fixed zoom.
        private const float RadiusMeters = 1000f;
        private const int   FixedTexUpscale = 12;   // each window cell → 12 screen px (fixed "zoom")

        // Reflected once, lazily, from the live Minimap.
        private static FieldInfo? _fiExplored;

        private GameObject? _canvasRoot;
        private RawImage?   _rawImage;
        private Texture2D?  _tex;
        private bool _open;
        private Vector3 _origin;

        private void Update()
        {
            // Client-only & graphics-only. On the dedicated server graphicsDeviceType is
            // Null and there is no local player, so this never fires there.
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            if (Player.m_localPlayer == null || Minimap.instance == null) return;

            if (Input.GetKeyDown(KeyCode.F9))
            {
                if (_open) Close();
                else Open(Player.m_localPlayer.transform.position);
            }
            else if (_open && Input.GetKeyDown(KeyCode.F10))
            {
                Rebuild(Player.m_localPlayer.transform.position);
            }
        }

        private void Open(Vector3 origin)
        {
            EnsureCanvas();
            Rebuild(origin);
            _canvasRoot!.SetActive(true);
            _open = true;
        }

        private void Close()
        {
            if (_canvasRoot != null) _canvasRoot.SetActive(false);
            _open = false;
        }

        private void Rebuild(Vector3 origin)
        {
            _origin = origin;
            Minimap mm = Minimap.instance;

            // Public on the live game (verified :46692/:46694) — read directly, no hardcoding.
            int   textureSize = mm.m_textureSize;
            float pixelSize   = mm.m_pixelSize;

            bool[]? explored = ReadExplored(mm);
            if (explored == null)
            {
                Plugin.Log.LogError("[Spike] Could not reflect Minimap.m_explored — render aborted.");
                return;
            }

            var w = BoundedMapMath.ComputeWindow(origin.x, origin.z, RadiusMeters, pixelSize, textureSize);
            byte[] fog = BoundedMapMath.BuildWindowedFog(
                explored, textureSize, w, origin.x, origin.z, RadiusMeters, pixelSize,
                out int exploredInDisc, out int discCells, out int copiedFromSource);

            PaintTexture(w.Size, fog);

            Plugin.Log.LogInfo(
                $"[Spike] Bounded viewer rebuilt @origin=({origin.x:F0},{origin.z:F0}) | " +
                $"pixelSize={pixelSize} textureSize={textureSize} | " +
                $"window={w.Size}x{w.Size}={w.Cells} cells (vs full {textureSize * textureSize}) | " +
                $"discCells={discCells} exploredInDisc={exploredInDisc} copiedFromSource={copiedFromSource} | " +
                $"over-provisioning shrink = {(double)(textureSize * textureSize) / w.Cells:F1}x");
        }

        // ── Reflection: the personal fog array is private bool[] ─────────────────────
        private static bool[]? ReadExplored(Minimap mm)
        {
            if (_fiExplored == null)
            {
                _fiExplored = typeof(Minimap).GetField(
                    "m_explored", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            return _fiExplored?.GetValue(mm) as bool[];
        }

        // ── Our own texture, painted from the windowed array (no vanilla shader) ─────
        private void PaintTexture(int size, byte[] fog)
        {
            if (_tex == null || _tex.width != size)
            {
                if (_tex != null) UnityEngine.Object.Destroy(_tex);
                _tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "_Spike bounded fog",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Point,   // crisp cells at fixed zoom
                };
            }

            var parchment = new Color32(210, 196, 160, 255); // explored & in-disc
            var shroud    = new Color32(14, 13, 11, 255);     // outside disc / unexplored
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++)
                px[i] = fog[i] == 1 ? parchment : shroud;

            _tex.SetPixels32(px);
            _tex.Apply(updateMipmaps: false);

            if (_rawImage != null)
            {
                _rawImage.texture = _tex;
                var rt = _rawImage.rectTransform;
                rt.sizeDelta = new Vector2(size * FixedTexUpscale, size * FixedTexUpscale);
            }
        }

        // ── Custom full-screen Canvas + centered RawImage (same uGUI idiom as the
        //    shipped SignPaintPanel — proven render surface in this repo). ────────────
        private void EnsureCanvas()
        {
            if (_canvasRoot != null) return;

            _canvasRoot = new GameObject("SBPR_SpikeMapCanvas");
            UnityEngine.Object.DontDestroyOnLoad(_canvasRoot);

            var canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000; // above the HUD
            _canvasRoot.AddComponent<CanvasScaler>();
            _canvasRoot.AddComponent<GraphicRaycaster>();

            // Dim full-screen backdrop so the bounded disc reads as "the whole view".
            var bg = new GameObject("backdrop");
            bg.transform.SetParent(_canvasRoot.transform, false);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.85f);
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;

            var imgGo = new GameObject("boundedFog");
            imgGo.transform.SetParent(_canvasRoot.transform, false);
            _rawImage = imgGo.AddComponent<RawImage>();
            var rt = _rawImage.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);

            _canvasRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            if (_canvasRoot != null) UnityEngine.Object.Destroy(_canvasRoot);
        }
    }
}
