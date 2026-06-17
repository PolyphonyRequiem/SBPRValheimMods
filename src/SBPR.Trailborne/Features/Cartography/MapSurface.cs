// ============================================================================
//  Trailborne v2 cartography — ONE rendering surface (the shared §2H.1 layer tree)
// ----------------------------------------------------------------------------
//  Factored out of MapViewer (map-provider-binding-impl-spec §4.2): the §2H.1
//  circular rotate-to-heading viewer — its Canvas/frame/bezel/cartography/shroud/
//  overlay layer tree plus the shader-or-fallback render — built ONCE here and
//  instanced at two sizes:
//    • the full-screen MODAL full view (table-centred, edge-arrow, backdrop +
//      esc/title prompts), and
//    • the carry-state MINIMAP DISC (player-centred per Daniel's R1: marker fixed
//      dead-centre, the survey scrolls under the player; the SHROUD stays table-
//      anchored to the imprinted 1000 m bound so it creeps in from the bounded
//      side and goes all-shroud past the disc; NO edge-arrow — a player-centred
//      camera can't fall off the window so §2H.1 mechanic 2 does not carry over).
//
//  ONE layer-tree builder, two MapSurfaceConfig instances — never a parallel
//  hierarchy (the design's hard rule: a second circular renderer would re-ship the
//  issue-6 edge-bleed at disc size). The #159 hard alpha-clip bezel is built PER
//  surface and parameterized by FRACTION-OF-TARGET (not absolute px), so the disc's
//  inset/ring scale down with it instead of swallowing a 200 px disc (§4.2 caveat —
//  the highest-risk viewer detail).
//
//  Clean-side (ADR-0001): vanilla Minimap material / camera yaw read+adapted; the
//  uGUI surface is our own (the SignPaintPanel idiom). No vanilla UI prefab cloned.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SBPR.Trailborne.Features.Cartography
{
    using MarkerSignsType = SBPR.Trailborne.Features.MarkerSigns.MarkerSigns;
    using WorldPins = SBPR.Trailborne.Features.MarkerSigns.WorldPins;

    /// <summary>
    /// Per-surface configuration. The two instances differ only in these knobs; all the
    /// render machinery below is shared. Modal = the full-screen table-centred view; the
    /// disc = the corner, player-centred carry minimap (R1).
    /// </summary>
    public sealed class MapSurfaceConfig
    {
        /// <summary>On-screen square edge (px) the surface targets. 900 (modal) or ~200 (disc).</summary>
        public int TargetPx = 900;
        /// <summary>Canvas sortingOrder. Modal = 5000 (over the disc); disc = 3000 (HUD-tier).</summary>
        public int SortingOrder = 5000;
        /// <summary>Dim full-screen backdrop behind the disc (modal only). Off for the HUD disc.</summary>
        public bool ShowBackdrop = true;
        /// <summary>Bottom-centre "[Esc] Close map" + top-centre title cartouche (modal only).</summary>
        public bool ShowPrompts = true;
        /// <summary>Handle Esc-close + TableEdit pin-click in Tick (modal only). The disc is passive.</summary>
        public bool HandleInput = true;
        /// <summary>
        /// R1 centring. false = TABLE-centred (modal: window static on BoundOrigin, player marker
        /// orbits, edge-arrow off-disc — the playtested §2H.1 behaviour, byte-unchanged). true =
        /// PLAYER-centred (disc: window scrolls under a dead-centre marker, shroud stays anchored to
        /// the table's 1000 m bound, NO edge-arrow — Daniel's R1 resolution).
        /// </summary>
        public bool PlayerCentred = false;
        /// <summary>Screen anchor (0..1) for the frame. (0.5,0.5)=centre (modal); (1,1)=top-right (disc).</summary>
        public Vector2 ScreenAnchor = new Vector2(0.5f, 0.5f);
        /// <summary>Margin (px) from the anchored screen edge to the disc centre (disc only).</summary>
        public float CornerMarginPx = 16f;
    }

    /// <summary>
    /// One circular rotate-to-heading map surface. Owns its own Canvas + layer tree + textures.
    /// MapViewer owns two of these (modal + disc) and routes the seam's channels to them.
    /// </summary>
    public sealed class MapSurface
    {
        // ── Sizing / palette (shared; the disc scales the bezel by fraction — see EnsureBezelTexture) ──
        private const float PinIconPx      = 22f;
        private const float PinLabelFontPx = 14f;
        private const float PlayerMarkerPx = 26f;

        // §2H.1 b4 build-calibration knob: rotation sense. If the map turns the wrong way in-game,
        // flip to -1f. Single knob, shared by both surfaces; no north-up alternative (disorientation
        // is the intended design — Daniel).
        private const float MapRotationSign = 1f;

        // §4.2: the #159 clip geometry as FRACTIONS of the target edge, NOT absolute px, so the disc
        // inset/ring scale with it (6/900 and 10/900 reproduce the modal's playtested look exactly).
        private const float BezelInsetFrac = 6f / 900f;
        private const float BezelRingFrac  = 10f / 900f;

        private static readonly Color32 CParchment = new Color32(214, 198, 162, 255);
        private static readonly Color32 CShroud    = new Color32(14, 13, 11, 255);
        private static readonly Color32 CShroudA   = new Color32(10, 9, 8, 255);
        private static readonly Color32 CClear     = new Color32(0, 0, 0, 0);
        private static readonly Color   CBackdrop  = new Color(0f, 0f, 0f, 0.92f);

        // ── Per-surface UI refs ──
        private readonly Transform _host;
        private readonly MapSurfaceConfig _cfg;

        private GameObject? _root;
        private Canvas? _canvas;
        private RectTransform? _frame;
        private RectTransform? _mapContainer;
        private RawImage? _mapImage;
        private RawImage? _shroudImage;
        private RectTransform? _mapRect;
        private GameObject? _overlayLayer;
        private RawImage? _bezel;
        private Texture2D? _tex;
        private Texture2D? _shroudTex;
        private Material?  _shaderMat;
        private Texture2D? _bezelTex;
        private RawImage? _playerMarker;
        private Text? _exitPrompt;
        private Text? _titleLabel;
        private readonly List<GameObject> _pinObjects = new List<GameObject>();

        private MapViewRequest _req;

        public MapSurface(Transform host, MapSurfaceConfig cfg)
        {
            _host = host;
            _cfg = cfg;
        }

        // ── Public surface API (MapViewer drives these) ──

        /// <summary>Open-state is derived from the live root (un-latchable — §2I.1), never a side bool.</summary>
        public bool IsActive => _root != null && _root.activeSelf;
        public MapViewerMode Mode => _req.Mode;

        public void Show(MapViewRequest request)
        {
            _req = request;
            EnsureBuilt();
            _root!.SetActive(true);
            Render();
        }

        public void Refresh(MapViewRequest request)
        {
            if (!IsActive) return;
            _req = request;
            Render();
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        public void Destroy()
        {
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            if (_shroudTex != null) UnityEngine.Object.Destroy(_shroudTex);
            if (_shaderMat != null) UnityEngine.Object.Destroy(_shaderMat);
            if (_bezelTex != null) UnityEngine.Object.Destroy(_bezelTex);
        }

        // ── Render: §2E.3 shader cartography + shroud mask + overlay (shared by both surfaces) ──

        private void Render()
        {
            var survey = _req.Survey;
            if (survey == null || survey.Fog == null || survey.Size <= 0)
            {
                Plugin.Log.LogWarning("[Trailborne/Cartography] MapSurface.Render: empty/blank survey; nothing to draw.");
                return;
            }

            LayoutMapRect(survey.Size);

            if (!TryRenderVanillaShader(survey))
                PaintFog(survey); // never-blank guard → the explored/shroud two-color fill

            RebuildOverlay(survey);
            UpdateExitPrompt();
            UpdateTitle();
            UpdateFrameForMode();
            ApplyFieldOrientation(survey);
        }

        /// <summary>
        /// §2E.3 SHADER render — the parchment look (a COPY of vanilla's styled large-map material).
        /// The framing centre is the bound TABLE origin for a table-centred surface, or the PLAYER
        /// position for a player-centred one (R1): centring the shader on the player is what makes the
        /// cartography scroll under a dead-centre marker. The hard 1000 m shroud (PaintShroudMask) stays
        /// keyed to the table-anchored bound either way, so the revealed area never follows the player.
        /// </summary>
        private bool TryRenderVanillaShader(SurveyData survey)
        {
            var mm = Minimap.instance;
            if (mm == null || mm.m_mapImageLarge == null || mm.m_mapImageLarge.material == null)
                return false;

            Material liveMat = mm.m_mapImageLarge.material;
            Texture? mainTex = liveMat.GetTexture("_MainTex");
            if (mainTex == null) return false;

            if (_shaderMat == null)
            {
                try
                {
                    _shaderMat = UnityEngine.Object.Instantiate(liveMat);
                    _shaderMat.name = "SBPR_BoundedMapMaterial(Clone)";
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Trailborne/Cartography] MapSurface: could not copy vanilla map material ({e.Message}); falling back to PaintFog.");
                    _shaderMat = null;
                    return false;
                }
            }

            float pixelSize   = survey.PixelSize > 0f ? survey.PixelSize : 64f;
            int   textureSize = survey.TextureSize > 0 ? survey.TextureSize : 256;
            int   size        = survey.Size;

            // §4.2 / R1: the framing centre. Table-centred → BoundOrigin (the §2H.1 lock, unchanged).
            // Player-centred → the live player position, so the parchment scrolls under the marker.
            Vector3 frameCenter = FrameCenter();

            float mx = frameCenter.x / pixelSize + textureSize / 2f;
            float my = frameCenter.z / pixelSize + textureSize / 2f;
            float uvCx = mx / textureSize;
            float uvCy = my / textureSize;
            float zoom = Mathf.Clamp((float)size / textureSize, 0.0001f, 1f);

            if (_mapImage != null)
            {
                _mapImage.material = _shaderMat;
                _mapImage.texture  = mainTex;
                _mapImage.color    = Color.white;
                var uv = _mapImage.uvRect;
                uv.width  = zoom;
                uv.height = zoom;
                uv.center = new Vector2(uvCx, uvCy);
                _mapImage.uvRect = uv;
            }

            _shaderMat.SetFloat("_zoom", zoom);
            _shaderMat.SetFloat("_pixelSize", 200f / Mathf.Max(zoom, 1e-4f));
            _shaderMat.SetVector("_mapCenter", new Vector4(frameCenter.x, frameCenter.z, 0f, 0f));

            PaintShroudMask(survey);
            return true;
        }

        /// <summary>
        /// §4.2 / R1: the world point the cartography window is centred on. Table-centred surfaces
        /// frame the bound origin (§2H.1 — the window is static on the table). Player-centred surfaces
        /// frame the live player position; if the player isn't up yet, fall back to the bound origin so
        /// the disc still renders (graceful — never blank).
        /// </summary>
        private Vector3 FrameCenter()
        {
            if (_cfg.PlayerCentred)
            {
                var p = Player.m_localPlayer;
                if (p != null) return p.transform.position;
            }
            return _req.BoundOrigin;
        }

        // §§SURFACE_RENDER§§

        /// <summary>
        /// True if the world point (wx,wz) is LIT in the table-anchored survey — i.e. explored AND
        /// inside the imprinted 1000 m bound. Maps world → survey source cell → survey window cell →
        /// fog index. Outside the surveyed window → not lit (shroud). This is the table-anchored test
        /// the player-centred disc samples per screen texel (R1: the shroud never follows the player).
        /// </summary>
        private static bool SampleLitAt(SurveyData survey, float wx, float wz)
        {
            float pixelSize  = survey.PixelSize > 0f ? survey.PixelSize : 64f;
            int textureSize  = survey.TextureSize > 0 ? survey.TextureSize : 256;
            int size         = survey.Size;
            if (survey.Fog == null || size <= 0) return false;

            int srcX = BoundedMapMath.WorldToCellX(wx, pixelSize, textureSize);
            int srcY = BoundedMapMath.WorldToCellY(wz, pixelSize, textureSize);
            int cx   = BoundedMapMath.WorldToCellX(survey.OriginX, pixelSize, textureSize);
            int cy   = BoundedMapMath.WorldToCellY(survey.OriginZ, pixelSize, textureSize);
            int half = (size - 1) / 2;
            int swx = (srcX - cx) + half;
            int swy = (srcY - cy) + half;
            if (swx < 0 || swy < 0 || swx >= size || swy >= size) return false;
            int idx = swy * size + swx;
            return idx >= 0 && idx < survey.Fog.Length && survey.Fog[idx];
        }

        // §4.2/R1: the player-centred disc resamples its shroud mask at this fixed resolution so the
        // shroud-creep edge stays smooth (sub-cell) as the player walks. The table-centred modal uses
        // the survey's own Size×Size window (a direct 1:1 copy — its playtested path, unchanged).
        private const int DiscShroudTexN = 128;

        /// <summary>
        /// Build the shroud-mask texture (lit → transparent so cartography shows through; else → opaque
        /// shroud). TABLE-centred surfaces copy the survey fog 1:1 (the §2E playtested path). PLAYER-
        /// centred surfaces RESAMPLE: each texel maps to a world point offset from the player, tested
        /// against the table-anchored survey (SampleLitAt) — so as the player walks toward the survey
        /// edge the shroud creeps in from that side, and past the 1000 m bound it goes all shroud (R1).
        /// </summary>
        private void PaintShroudMask(SurveyData survey)
        {
            if (_cfg.PlayerCentred) PaintShroudMaskPlayerCentred(survey);
            else                    PaintShroudMaskTableCentred(survey);
        }

        private void PaintShroudMaskTableCentred(SurveyData survey)
        {
            int size = survey.Size;
            if (_shroudTex == null || _shroudTex.width != size)
            {
                if (_shroudTex != null) UnityEngine.Object.Destroy(_shroudTex);
                _shroudTex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "SBPR_ShroudMask",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }

            var px = new Color32[size * size];
            var fog = survey.Fog;
            int n = Mathf.Min(px.Length, fog.Length);
            for (int i = 0; i < n; i++) px[i] = fog[i] ? CClear : CShroudA;
            for (int i = n; i < px.Length; i++) px[i] = CShroudA;

            _shroudTex.SetPixels32(px);
            _shroudTex.Apply(updateMipmaps: false);
            ApplyShroudTex();
        }

        private void PaintShroudMaskPlayerCentred(SurveyData survey)
        {
            int N = DiscShroudTexN;
            if (_shroudTex == null || _shroudTex.width != N)
            {
                if (_shroudTex != null) UnityEngine.Object.Destroy(_shroudTex);
                _shroudTex = new Texture2D(N, N, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "SBPR_ShroudMaskDisc",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }

            float pixelSize = survey.PixelSize > 0f ? survey.PixelSize : 64f;
            int   size      = survey.Size;
            float spanMeters = size * pixelSize;       // world width the cartography rect covers
            Vector3 center  = FrameCenter();           // the player (R1)

            var px = new Color32[N * N];
            for (int my = 0; my < N; my++)
            {
                // texel-centre v in [0,1] → world Z offset from the player (north = +Z = up, bottom-up rows)
                float v = (my + 0.5f) / N;
                float wz = center.z + (v - 0.5f) * spanMeters;
                for (int mx = 0; mx < N; mx++)
                {
                    float u = (mx + 0.5f) / N;
                    float wx = center.x + (u - 0.5f) * spanMeters;
                    px[my * N + mx] = SampleLitAt(survey, wx, wz) ? CClear : CShroudA;
                }
            }
            _shroudTex.SetPixels32(px);
            _shroudTex.Apply(updateMipmaps: false);
            ApplyShroudTex();
        }

        private void ApplyShroudTex()
        {
            if (_shroudImage != null)
            {
                _shroudImage.texture = _shroudTex;
                _shroudImage.color = Color.white;
                _shroudImage.gameObject.SetActive(true);
            }
        }

        /// <summary>Size the on-screen map square at a FIXED scale (no scroll-zoom).</summary>
        private void LayoutMapRect(int size)
        {
            if (_mapRect == null) return;
            int upscale = Mathf.Max(1, _cfg.TargetPx / Mathf.Max(1, size));
            float edge = size * upscale;
            _mapRect.sizeDelta = new Vector2(edge, edge);
        }

        /// <summary>
        /// CONTINUOUS world→anchored-px projection about the surface's frame centre. Algebraically
        /// equal to the old WorldToMapRectContinuous when the centre is the survey origin (table-
        /// centred modal), and re-centres on the player for the disc (R1). north=+Z = +y (up).
        /// </summary>
        private Vector2 WorldToSurfacePx(Vector3 world, SurveyData survey)
        {
            if (_mapRect == null) return Vector2.zero;
            float pixelSize = survey.PixelSize > 0f ? survey.PixelSize : 64f;
            float span = survey.Size * pixelSize;
            float edge = _mapRect.sizeDelta.x;
            Vector3 c = FrameCenter();
            float ax = (world.x - c.x) / span * edge;
            float az = (world.z - c.z) / span * edge;
            return new Vector2(ax, az);
        }

        /// <summary>
        /// CELL-SNAPPED world→anchored-px projection (the old WorldToMapRect/MapRectAnchor path),
        /// used for PINS on the TABLE-centred modal so they annotate discrete fog cells exactly as the
        /// playtested view did (byte-faithful). The player-centred disc uses the continuous projection
        /// instead — cell-snapping there would make pins jitter a whole cell as the player walks.
        /// Returns false if the point falls outside the survey window grid.
        /// </summary>
        private bool WorldToSurfacePxSnapped(Vector3 world, SurveyData survey, out Vector2 anchored)
        {
            anchored = Vector2.zero;
            if (_mapRect == null) return false;
            float pixelSize = survey.PixelSize > 0f ? survey.PixelSize : 64f;
            int textureSize = survey.TextureSize > 0 ? survey.TextureSize : 256;
            int size = survey.Size;

            int cx = BoundedMapMath.WorldToCellX(survey.OriginX, pixelSize, textureSize);
            int cy = BoundedMapMath.WorldToCellY(survey.OriginZ, pixelSize, textureSize);
            int px = BoundedMapMath.WorldToCellX(world.x, pixelSize, textureSize);
            int py = BoundedMapMath.WorldToCellY(world.z, pixelSize, textureSize);

            int half = (size - 1) / 2;
            float wx = (px - cx) + half;
            float wy = (py - cy) + half;
            if (wx < 0 || wy < 0 || wx >= size || wy >= size) return false;

            float edge = _mapRect.sizeDelta.x;
            float cell = edge / size;
            anchored = new Vector2((wx + 0.5f) * cell - edge / 2f,
                                   (wy + 0.5f) * cell - edge / 2f);
            return true;
        }

        /// <summary>
        /// FALLBACK render (never-blank guard) — paint the Size×Size fog window into a 2-colour
        /// texture. Used only when the vanilla styled material is unavailable. Player-centred surfaces
        /// would need a resample here too, but this path is the GPU-less guard (no shader = no GPU
        /// client = the disc isn't a use case), so the table-window copy is acceptable as a last resort.
        /// </summary>
        private void PaintFog(SurveyData survey)
        {
            if (_shroudImage != null) _shroudImage.gameObject.SetActive(false);

            int size = survey.Size;
            if (_tex == null || _tex.width != size)
            {
                if (_tex != null) UnityEngine.Object.Destroy(_tex);
                _tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "SBPR_BoundedFog",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Point,
                };
            }

            var px = new Color32[size * size];
            var fog = survey.Fog;
            int n = Mathf.Min(px.Length, fog.Length);
            for (int i = 0; i < n; i++) px[i] = fog[i] ? CParchment : CShroud;

            _tex.SetPixels32(px);
            _tex.Apply(updateMipmaps: false);

            if (_mapImage != null)
            {
                _mapImage.material = null;
                _mapImage.texture = _tex;
                _mapImage.uvRect = new Rect(0f, 0f, 1f, 1f);
                _mapImage.color = Color.white;
            }
        }

        // §§SURFACE_OVERLAY§§

        /// <summary>
        /// Rebuild the pin + player-marker overlay. Pins render inside the table-anchored 1000 m bound
        /// (AT-MAP-BOUND) and, on the player-centred disc, additionally only within the visible disc
        /// radius (so a far pin doesn't float onto the bezel). Player marker: table-centred surfaces use
        /// the §2H.1 in-disc-dot / off-disc-edge-arrow; player-centred disc pins the marker dead-centre
        /// with NO edge-arrow (R1 — a centred camera can't fall off the window).
        /// </summary>
        private void RebuildOverlay(SurveyData survey)
        {
            ClearPinObjects();
            if (_overlayLayer == null || _mapRect == null) return;

            Vector3 origin = _req.BoundOrigin;
            float radius = _req.RadiusMeters > 0f ? _req.RadiusMeters : survey.RadiusMeters;

            var rendered = new List<SurveyPin>();
            if (survey.Pins != null) rendered.AddRange(survey.Pins);
            try
            {
                var live = WorldPins.CollectInDiscPins(origin, radius);
                foreach (var lp in live) AddIfNew(rendered, lp);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Cartography] MapSurface: live WorldPins scan failed: {e.Message}");
            }

            float edge = _mapRect.sizeDelta.x;
            float discR = edge * 0.5f; // visible disc radius (inscribed circle of the square rect)

            foreach (var pin in rendered)
            {
                if (!BoundedMapMath.InDisc(pin.Pos.x, pin.Pos.z, origin.x, origin.z, radius))
                    continue; // beyond the table-anchored 1000 m bound never renders (AT-MAP-BOUND)

                Vector2 anchored;
                if (_cfg.PlayerCentred)
                {
                    // Disc: continuous player-relative projection. Clip to the visible circle so a pin
                    // near the bound edge but outside the small disc window doesn't draw over the bezel.
                    anchored = WorldToSurfacePx(pin.Pos, survey);
                    if (anchored.sqrMagnitude > discR * discR) continue;
                }
                else
                {
                    // Modal: cell-snapped projection (byte-faithful to the playtested table-centred view).
                    if (!WorldToSurfacePxSnapped(pin.Pos, survey, out anchored)) continue;
                }

                SpawnPinMarker(pin, anchored);
            }

            UpdatePlayerMarker(survey, origin, radius);
        }

        private void UpdatePlayerMarker(SurveyData survey, Vector3 origin, float radius)
        {
            if (_overlayLayer == null || _mapRect == null) return;
            var player = Player.m_localPlayer;
            if (player == null) { if (_playerMarker != null) _playerMarker.gameObject.SetActive(false); return; }

            EnsurePlayerMarker();
            if (_playerMarker == null) return;
            _playerMarker.gameObject.SetActive(true);
            var rt = _playerMarker.rectTransform;
            rt.sizeDelta = new Vector2(PlayerMarkerPx, PlayerMarkerPx);

            // ── R1 player-centred disc: the marker is the PIVOT — always dead-centre, always the
            //    in-disc dot, NO edge-arrow (a centred camera can't fall off the window; Daniel
            //    explicitly removed §2H.1 mechanic 2 for the disc). ──
            if (_cfg.PlayerCentred)
            {
                rt.anchoredPosition = Vector2.zero;
                rt.localRotation = Quaternion.identity;
                _playerMarker.color = new Color(0.4f, 0.7f, 1f, 1f);
                return;
            }

            // ── Table-centred modal: the §2H.1 in-disc dot / off-disc outward edge arrow (unchanged). ──
            Vector3 ppos = player.transform.position;
            BoundedMapMath.EdgeClampToDisc(ppos.x, ppos.z, origin.x, origin.z, radius,
                out float cx, out float cz, out float angleDeg, out bool outside, out _);

            if (outside)
            {
                float inset = 0.94f;
                float ax = origin.x + (cx - origin.x) * inset;
                float az = origin.z + (cz - origin.z) * inset;
                rt.anchoredPosition = WorldToSurfacePx(new Vector3(ax, 0f, az), survey);
                rt.localRotation = Quaternion.Euler(0, 0, angleDeg);
                _playerMarker.color = new Color(1f, 0.5f, 0.2f, 1f);
            }
            else
            {
                rt.anchoredPosition = WorldToSurfacePx(ppos, survey);
                rt.localRotation = Quaternion.identity;
                _playerMarker.color = new Color(0.4f, 0.7f, 1f, 1f);
            }
        }

        // §§SURFACE_PINS§§

        private void SpawnPinMarker(SurveyPin pin, Vector2 anchored)
        {
            if (_overlayLayer == null) return;
            var go = new GameObject("pin");
            go.transform.SetParent(_overlayLayer.transform, false);
            var img = go.AddComponent<RawImage>();

            Sprite? sprite = ResolvePinSprite(pin);
            if (sprite != null) img.texture = sprite.texture;
            else img.color = PinTint(pin);

            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PinIconPx, PinIconPx);
            rt.anchoredPosition = anchored;
            _pinObjects.Add(go);

            // §2K pin label — only on surfaces that show prompts (the modal). The tiny disc stays
            // glanceable (no label clutter at minimap scale).
            if (_cfg.ShowPrompts && !string.IsNullOrWhiteSpace(pin.Name))
            {
                var labelGo = new GameObject("pinLabel");
                labelGo.transform.SetParent(go.transform, false);
                var txt = labelGo.AddComponent<Text>();
                txt.font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                           ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                txt.fontSize = (int)PinLabelFontPx;
                txt.alignment = TextAnchor.UpperCenter;
                txt.color = new Color(1f, 0.95f, 0.8f, 0.97f);
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Overflow;
                txt.raycastTarget = false;
                txt.text = pin.Name;
                var lrt = txt.rectTransform;
                lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
                lrt.pivot = new Vector2(0.5f, 1f);
                lrt.anchoredPosition = new Vector2(0f, -(PinIconPx * 0.5f + 2f));
                lrt.sizeDelta = new Vector2(160f, 20f);
                var outline = labelGo.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }
        }

        private static Sprite? ResolvePinSprite(SurveyPin pin)
        {
            try
            {
                foreach (var mk in MarkerSignsType.MarkerTypes)
                    if ((int)mk.VanillaPinType == pin.Type && !string.IsNullOrEmpty(mk.IconFile))
                        return Runtime.Assets.LoadPngAsSprite(mk.IconFile);
            }
            catch { /* fall through to dot */ }
            return null;
        }

        private static Color PinTint(SurveyPin pin)
            => pin.Checked ? new Color(0.5f, 0.8f, 0.5f, 1f) : new Color(0.95f, 0.85f, 0.4f, 1f);

        private void EnsurePlayerMarker()
        {
            Transform? parent = _overlayLayer?.transform;
            if (parent == null) return;
            if (_playerMarker == null)
            {
                var go = new GameObject("playerMarker");
                go.transform.SetParent(parent, false);
                _playerMarker = go.AddComponent<RawImage>();
                _playerMarker.raycastTarget = false;
                _playerMarker.color = new Color(0.4f, 0.7f, 1f, 1f);
                var rt = _playerMarker.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(PlayerMarkerPx, PlayerMarkerPx);
            }
        }

        private void ClearPinObjects()
        {
            foreach (var go in _pinObjects)
                if (go != null) UnityEngine.Object.Destroy(go);
            _pinObjects.Clear();
        }

        private static void AddIfNew(List<SurveyPin> list, SurveyPin pin)
        {
            const float prox2 = 1.0f * 1.0f;
            foreach (var e in list)
            {
                if (e.Type != pin.Type) continue;
                float dx = e.Pos.x - pin.Pos.x, dz = e.Pos.z - pin.Pos.z;
                if (dx * dx + dz * dz > prox2) continue;
                if (!string.Equals(e.Name ?? "", pin.Name ?? "", StringComparison.Ordinal)) continue;
                return;
            }
            list.Add(pin);
        }

        // §§SURFACE_ROTATE§§

        /// <summary>
        /// §2H.1 b5: keep pin icons upright while the interior rotates. Each pin rides the rotating
        /// container for POSITION; setting its own localRotation to the negative of the container's Z
        /// cancels the spin so the icon (and its child label) never goes upside-down.
        /// </summary>
        private void CounterRotatePins(float containerZ)
        {
            var counter = Quaternion.Euler(0f, 0f, -containerZ);
            foreach (var go in _pinObjects)
                if (go != null) go.transform.localRotation = counter;
        }

        /// <summary>
        /// Drive the rotating INTERIOR to heading. The rect itself stays centred (the player-centring
        /// for the disc is done in the SHADER reframe + shroud resample + pin projection, NOT by
        /// shifting this rect — so the small bezel always clips a centred disc cleanly). Both surfaces
        /// rotate about screen-centre: table-centred → the table is the pivot (marker orbits);
        /// player-centred → the player is the pivot (marker stays dead-centre, world spins under it).
        /// Runs every frame from MapViewer.Tick so rotation tracks heading at frame rate.
        /// </summary>
        public void ApplyFieldOrientation(SurveyData survey)
        {
            if (_mapContainer == null || _mapRect == null) return;
            _mapRect.anchoredPosition = Vector2.zero;

            var cam = Utils.GetMainCamera();
            if (cam == null) return;
            float camYaw = cam.transform.eulerAngles.y;
            float rotZ = MapRotationSign * camYaw;
            _mapContainer.localRotation = Quaternion.Euler(0f, 0f, rotZ);
            CounterRotatePins(rotZ);

            // The player marker rides the rotating overlay too. Counter-rotate it so its icon stays
            // screen-stable. On the player-centred disc this keeps "you" pointing screen-up while the
            // world spins under you; on the table-centred modal the in-disc dot stays upright (the
            // off-disc edge ARROW sets its own localRotation in UpdatePlayerMarker and is refreshed
            // each Render, so this counter-rotation of a per-frame-rebuilt marker is harmless there).
            if (_cfg.PlayerCentred && _playerMarker != null)
                _playerMarker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -rotZ);
        }

        /// <summary>Re-apply rotation every frame on the live survey (cheap: a yaw read + a transform set).</summary>
        public void TickRotation()
        {
            if (!IsActive) return;
            var survey = _req.Survey;
            if (survey != null && survey.Fog != null && survey.Size > 0)
                ApplyFieldOrientation(survey);
        }

        /// <summary>
        /// §2H.1: the circular bezel is shown for the held-map FieldReadOnly modal AND for the disc
        /// (both are circular). Only the TableEdit modal hides it (it keeps the fuller square extent
        /// for pin editing). The disc is structurally always circular, so its bezel is always on.
        /// </summary>
        private void UpdateFrameForMode()
        {
            if (_bezel == null) return;
            bool circular = _cfg.PlayerCentred || _req.Mode == MapViewerMode.FieldReadOnly;
            _bezel.gameObject.SetActive(circular);
        }

        private void UpdateExitPrompt()
        {
            if (_exitPrompt == null) return;
            string raw = _req.Mode == MapViewerMode.TableEdit
                ? "[<color=yellow><b>Esc</b></color>] Close map     [<color=yellow><b>Left-click</b></color>] Remove pin"
                : "[<color=yellow><b>Esc</b></color>] Close map";
            _exitPrompt.text = Localization.instance != null ? Localization.instance.Localize(raw) : raw;
        }

        private void UpdateTitle()
        {
            if (_titleLabel == null) return;
            string title = _req.Title ?? string.Empty;
            bool show = !string.IsNullOrEmpty(title);
            _titleLabel.gameObject.SetActive(show);
            if (!show) return;
            _titleLabel.text = Localization.instance != null ? Localization.instance.Localize(title) : title;
        }

        // ── Input: only the modal (HandleInput) processes Esc-close + TableEdit pin-click ──

        /// <summary>Modal input tick (Esc close, TableEdit pin removal). The disc is passive (no input).
        /// Returns true if Esc closed the surface this frame (so MapViewer can stop further work).</summary>
        public bool TickInput()
        {
            if (!_cfg.HandleInput || !IsActive) return false;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Hide();
                return true;
            }

            if (_req.Mode == MapViewerMode.TableEdit && _req.PinEditor != null && Input.GetMouseButtonDown(0))
                TryRemovePinAtCursor();

            return false;
        }

        // §§SURFACE_INPUT§§

        /// <summary>
        /// Map the cursor back to a world position (inverse of WorldToSurfacePx) and ask the pin editor
        /// to remove the nearest shared pin. Owner-authoritative persistence + ward re-check happen in
        /// the editor. Only ever runs on the table-centred TableEdit modal, so FrameCenter == origin.
        /// </summary>
        private void TryRemovePinAtCursor()
        {
            var survey = _req.Survey;
            if (survey == null || _mapRect == null || _req.PinEditor == null) return;

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _mapRect, Input.mousePosition, _canvas?.worldCamera, out local))
                return;

            float edge = _mapRect.sizeDelta.x;
            if (Mathf.Abs(local.x) > edge / 2f || Mathf.Abs(local.y) > edge / 2f)
                return;

            // Inverse of WorldToSurfacePx about the frame centre: px → world offset → world point.
            float pixelSize = survey.PixelSize > 0f ? survey.PixelSize : 64f;
            float span = survey.Size * pixelSize;
            Vector3 c = FrameCenter();
            float worldX = c.x + (local.x / edge) * span;
            float worldZ = c.z + (local.y / edge) * span;
            var worldPos = new Vector3(worldX, 0f, worldZ);

            float tol = pixelSize * 2f;
            if (_req.PinEditor.RemovePinNear(worldPos, tol))
            {
                Plugin.Log.LogInfo($"[Trailborne/Cartography] Table-view removed a pin near ({worldX:F0},{worldZ:F0}).");
                var fresh = _req.PinEditor.ReadCurrentSurvey();
                if (fresh != null) _req.Survey = fresh;
                Render();
            }
        }

        // ── §2H.1 b3 / §4.2: the FIXED circular bezel (#159 hard alpha clip), fraction-parameterized ──

        /// <summary>
        /// Build (once) the hard circular alpha-clip bezel for THIS surface. The #159 inset + ring band
        /// are expressed as FRACTIONS of the target edge (§4.2), so the disc scales them down with it
        /// instead of a 900-px-tuned absolute inset swallowing a 200-px disc. Transparent inside the
        /// visible disc (cartography shows through), a bronze ring at the edge, opaque shroud beyond —
        /// ring + shroud one contiguous opaque cover that clips everything past the radius.
        /// </summary>
        private Texture2D EnsureBezelTexture(float bezelEdgeScreenPx)
        {
            if (_bezelTex != null) return _bezelTex;

            const int N = 1024;
            _bezelTex = new Texture2D(N, N, TextureFormat.RGBA32, mipChain: false)
            {
                name = "SBPR_Bezel",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float half = N / 2f;
            float screenPerTex = bezelEdgeScreenPx / N;

            // §4.2: thresholds scale with the target edge (fraction-of-radius), not absolute px.
            float discEdge   = _cfg.TargetPx * 0.5f;
            float insetPx    = _cfg.TargetPx * BezelInsetFrac;
            float ringPx     = _cfg.TargetPx * BezelRingFrac;
            float holeR      = discEdge - insetPx;
            float ringOuterR = holeR + ringPx;
            const float aa   = 0.9f;

            var ringColor    = new Color(0.62f, 0.55f, 0.42f, 1f);
            var cornerShroud = new Color(0.04f, 0.035f, 0.03f, 1f);

            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float dScreen = Mathf.Sqrt(dx * dx + dy * dy) * screenPerTex;
                    float coverage = Mathf.SmoothStep(0f, 1f, (dScreen - (holeR - aa)) / (2f * aa));
                    float ringMix = Mathf.SmoothStep(0f, 1f, (dScreen - (ringOuterR - aa)) / (2f * aa));
                    Color rgb = Color.Lerp(ringColor, cornerShroud, ringMix);
                    px[y * N + x] = (Color32)new Color(rgb.r, rgb.g, rgb.b, coverage);
                }
            }
            _bezelTex.SetPixels32(px);
            _bezelTex.Apply(updateMipmaps: false);
            return _bezelTex;
        }

        // §§SURFACE_BUILD§§

        /// <summary>
        /// Build the §2H.1 layer tree ONCE for this surface. SHARED by the modal + the disc — the only
        /// differences are config-driven: TargetPx (900 vs ~200), sortingOrder, screen anchor +
        /// corner offset, and whether the backdrop / prompts exist. The hierarchy (frame → rotating
        /// container → cartography → shroud → overlay, plus the fixed bezel sized ×√2) is identical.
        /// </summary>
        private void EnsureBuilt()
        {
            if (_root != null) return;
            EnsureEventSystem();

            _root = new GameObject(_cfg.PlayerCentred ? "SBPR_MapDiscRoot" : "SBPR_MapViewerRoot");
            _root.transform.SetParent(_host, false);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = _cfg.SortingOrder;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            _root.AddComponent<GraphicRaycaster>();

            // Dim full-screen backdrop (modal only) so the bounded disc reads as "the whole view".
            if (_cfg.ShowBackdrop)
            {
                var bg = new GameObject("backdrop");
                bg.transform.SetParent(_root.transform, false);
                var bgImg = bg.AddComponent<Image>();
                bgImg.color = CBackdrop;
                var bgRt = bg.GetComponent<RectTransform>();
                bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
                bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
            }

            // The FIXED frame (never rotates). Centred (modal) or corner-anchored (disc). Its sizeDelta
            // is the target square; the rotating interior + fixed bezel + prompts hang off it.
            float bezelEdge = _cfg.TargetPx * 1.41421356f; // √2 → covers the rotated square's corners
            var frameGo = new GameObject("frame");
            frameGo.transform.SetParent(_root.transform, false);
            _frame = frameGo.AddComponent<RectTransform>();
            _frame.anchorMin = _frame.anchorMax = _cfg.ScreenAnchor;
            _frame.pivot = new Vector2(0.5f, 0.5f);
            _frame.sizeDelta = new Vector2(_cfg.TargetPx, _cfg.TargetPx);
            _frame.anchoredPosition = ComputeFrameAnchoredPos(bezelEdge);

            // The ROTATING interior (zero-size pivot under the frame). Children rotate about screen-centre.
            var containerGo = new GameObject("mapContainer");
            containerGo.transform.SetParent(_frame.transform, false);
            _mapContainer = containerGo.AddComponent<RectTransform>();
            _mapContainer.anchorMin = _mapContainer.anchorMax = new Vector2(0.5f, 0.5f);
            _mapContainer.pivot = new Vector2(0.5f, 0.5f);
            _mapContainer.sizeDelta = Vector2.zero;

            // Cartography layer (parchment shader / 2-colour fallback), parented to the rotating container.
            var mapGo = new GameObject("cartography");
            mapGo.transform.SetParent(_mapContainer.transform, false);
            _mapImage = mapGo.AddComponent<RawImage>();
            _mapRect = _mapImage.rectTransform;
            _mapRect.anchorMin = _mapRect.anchorMax = new Vector2(0.5f, 0.5f);
            _mapRect.pivot = new Vector2(0.5f, 0.5f);
            _mapRect.sizeDelta = new Vector2(_cfg.TargetPx, _cfg.TargetPx);

            // Shroud mask (our fog as an alpha mask) stretched over the cartography.
            var shroudGo = new GameObject("shroudMask");
            shroudGo.transform.SetParent(mapGo.transform, false);
            _shroudImage = shroudGo.AddComponent<RawImage>();
            _shroudImage.raycastTarget = false;
            var shRt = _shroudImage.rectTransform;
            shRt.anchorMin = Vector2.zero; shRt.anchorMax = Vector2.one;
            shRt.offsetMin = Vector2.zero; shRt.offsetMax = Vector2.zero;

            // Overlay layer (pins + player marker) — added last → highest child.
            _overlayLayer = new GameObject("overlay");
            _overlayLayer.transform.SetParent(mapGo.transform, false);
            var ovRt = _overlayLayer.AddComponent<RectTransform>();
            ovRt.anchorMin = ovRt.anchorMax = new Vector2(0.5f, 0.5f);
            ovRt.pivot = new Vector2(0.5f, 0.5f);
            ovRt.sizeDelta = Vector2.zero;

            // Fixed bezel (#159 hard alpha clip), child of the NON-rotating frame so it never spins,
            // sized ×√2 to cover the rotated square's corners.
            var bezelGo = new GameObject("bezel");
            bezelGo.transform.SetParent(_frame.transform, false);
            _bezel = bezelGo.AddComponent<RawImage>();
            _bezel.raycastTarget = false;
            _bezel.texture = EnsureBezelTexture(bezelEdge);
            _bezel.color = Color.white;
            var bzRt = _bezel.rectTransform;
            bzRt.anchorMin = bzRt.anchorMax = new Vector2(0.5f, 0.5f);
            bzRt.pivot = new Vector2(0.5f, 0.5f);
            bzRt.sizeDelta = new Vector2(bezelEdge, bezelEdge);

            if (_cfg.ShowPrompts) BuildPrompts();

            _root.SetActive(false);
        }

        /// <summary>
        /// Frame anchored position. Centred surfaces sit at origin. Corner-anchored (disc) surfaces are
        /// pulled in from the anchored screen corner by CornerMargin + half the √2 bezel, so the whole
        /// circular bezel (the widest visible element) clears the screen edge.
        /// </summary>
        private Vector2 ComputeFrameAnchoredPos(float bezelEdge)
        {
            if (_cfg.ScreenAnchor == new Vector2(0.5f, 0.5f)) return Vector2.zero;
            float inset = _cfg.CornerMarginPx + bezelEdge * 0.5f;
            float sx = _cfg.ScreenAnchor.x >= 1f ? -inset : (_cfg.ScreenAnchor.x <= 0f ? inset : 0f);
            float sy = _cfg.ScreenAnchor.y >= 1f ? -inset : (_cfg.ScreenAnchor.y <= 0f ? inset : 0f);
            return new Vector2(sx, sy);
        }

        // §§SURFACE_PROMPTS§§

        /// <summary>Build the §2F exit prompt (bottom-centre) + §2B.1 title cartouche (top-centre). Modal
        /// only — the disc has neither (it's a passive glanceable HUD element).</summary>
        private void BuildPrompts()
        {
            if (_root == null) return;

            var promptGo = new GameObject("exitPrompt");
            promptGo.transform.SetParent(_root.transform, false);
            _exitPrompt = promptGo.AddComponent<Text>();
            _exitPrompt.font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                               ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _exitPrompt.fontSize = 26;
            _exitPrompt.alignment = TextAnchor.LowerCenter;
            _exitPrompt.color = new Color(1f, 0.95f, 0.8f, 0.95f);
            _exitPrompt.horizontalOverflow = HorizontalWrapMode.Overflow;
            _exitPrompt.verticalOverflow = VerticalWrapMode.Overflow;
            _exitPrompt.raycastTarget = false;
            var prRt = _exitPrompt.rectTransform;
            prRt.anchorMin = new Vector2(0.5f, 0f);
            prRt.anchorMax = new Vector2(0.5f, 0f);
            prRt.pivot = new Vector2(0.5f, 0f);
            prRt.anchoredPosition = new Vector2(0f, 40f);
            prRt.sizeDelta = new Vector2(1200f, 48f);

            var titleGo = new GameObject("titleLabel");
            titleGo.transform.SetParent(_root.transform, false);
            _titleLabel = titleGo.AddComponent<Text>();
            _titleLabel.font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                               ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _titleLabel.fontSize = 34;
            _titleLabel.fontStyle = FontStyle.Bold;
            _titleLabel.alignment = TextAnchor.UpperCenter;
            _titleLabel.color = new Color(1f, 0.95f, 0.8f, 0.97f);
            _titleLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            _titleLabel.verticalOverflow = VerticalWrapMode.Overflow;
            _titleLabel.raycastTarget = false;
            var tlRt = _titleLabel.rectTransform;
            tlRt.anchorMin = new Vector2(0.5f, 1f);
            tlRt.anchorMax = new Vector2(0.5f, 1f);
            tlRt.pivot = new Vector2(0.5f, 1f);
            tlRt.anchoredPosition = new Vector2(0f, -40f);
            tlRt.sizeDelta = new Vector2(1200f, 52f);
            _titleLabel.gameObject.SetActive(false);
        }

        private static void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) return;
            if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var es = new GameObject("SBPR_MapViewerEventSystem");
            UnityEngine.Object.DontDestroyOnLoad(es);
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
    }
}
