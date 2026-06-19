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
        /// <summary>
        /// FIXED display span in metres — how much world the surface shows edge-to-edge. This is the
        /// single source of truth for both the shader framing (zoom) AND the pin/marker projection, so
        /// they cannot drift. NEITHER surface supports interactive zoom (Daniel: fixed-zoom by design);
        /// the two surfaces simply lock DIFFERENT fixed spans:
        ///   • 0  → "show the full local map" — derive the span from the survey window
        ///          (Size × pixelSize ≈ 2000 m for the 1000 m radius). The full M-view modal uses this.
        ///   • &gt;0 → a fixed tight span in metres — the corner minimap disc shows a small portion of the
        ///          surveyed area around the player; to see the whole local map you open the full map.
        /// The survey CAPTURE (1000 m, grid-anchored, §4.1 1:1) is unchanged either way — this knob only
        /// controls how far out the camera frames it, never what is surveyed.
        /// </summary>
        public float ViewSpanMeters = 0f;
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
        // §2E.5: with the bezel now TRANSPARENT outside the ring (defect 1), the ring is the ONLY disc
        // edge — the old opaque black backing used to imply it. The pure 10/900 fraction gives the 200 px
        // disc a ~2.2 px thread that reads as weak/absent (headless-verified). Floor the ring at a minimum
        // absolute width so the small disc keeps a legible bronze edge; the 900 px modal is unaffected
        // (its 10 px ring already exceeds the floor, so its playtested look is byte-preserved).
        private const float BezelRingMinPx = 4.5f;

        private static readonly Color32 CParchment = new Color32(214, 198, 162, 255);
        private static readonly Color32 CShroud    = new Color32(14, 13, 11, 255);
        private static readonly Color   CBackdrop  = new Color(0f, 0f, 0f, 0.92f);

        // ── Per-surface UI refs ──
        private readonly Transform _host;
        private readonly MapSurfaceConfig _cfg;

        private GameObject? _root;
        private Canvas? _canvas;
        private RectTransform? _frame;
        private RectTransform? _mapContainer;
        private RawImage? _mapImage;
        private RectTransform? _mapRect;
        private GameObject? _overlayLayer;
        private RawImage? _bezel;
        private Texture2D? _tex;
        private Texture2D? _revealTex;
        private Color32[]? _revealScratch;
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
            if (_revealTex != null) UnityEngine.Object.Destroy(_revealTex);
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
            // §2H.1 fixed-zoom: the displayed span is the SINGLE source of truth for framing. zoom is the
            // fraction of the full world texture the surface shows = displayedSpan / (textureSize*pixelSize).
            // For the modal (ViewSpanMeters=0) this reduces to size/textureSize (the full-survey view,
            // byte-unchanged). For the disc (ViewSpanMeters>0) it locks a tighter fixed span. NEITHER reads
            // an interactive zoom — both are fixed by design, just at different fixed spans.
            float displayedSpan = DisplayedSpanMeters(survey, pixelSize);
            float zoom = Mathf.Clamp(displayedSpan / (textureSize * pixelSize), 0.0001f, 1f);

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

            // §2E.5 defect 3: the uvRect window and the shader uniforms must be ONE framing, driven
            // in lockstep exactly as vanilla CenterMap does (decomp Minimap.cs:1014-1027). uvRect
            // (above) and these uniforms both derive from the SAME zoom + frameCenter — they cannot
            // drift. The fix vs the shipped bug: _mapCenter is the RAW WORLD vector (x, y, z) like
            // vanilla's SetVector("_mapCenter", centerPoint) (:1027). The shipped code passed
            // (x, z, 0, 0) — shoving Z into the Y slot and ZEROING the world-Z centre — so the shader
            // reconstructed every fragment's world position around z=0 while _MainTex framed correctly.
            // That single-axis disagreement is the "diamond / mostly-black strip": only the band where
            // the survey straddled z=0 lined up. Passing the real world centre re-agrees the two
            // transforms, so the genuinely-sampled biome/relief fills the whole square (out to its
            // corners), making the §2H.1 inscribed-circle guarantee true once the disc clip applies.
            _shaderMat.SetFloat("_zoom", zoom);
            _shaderMat.SetFloat("_pixelSize", 200f / Mathf.Max(zoom, 1e-4f));
            _shaderMat.SetVector("_mapCenter", new Vector4(frameCenter.x, frameCenter.y, frameCenter.z, 0f));

            // §2E.5 defect 2: reveal the bounded survey through vanilla's OWN _FogTex cloud instead of
            // laying an opaque flat shroud over the cartography. We override _FogTex on the clone with a
            // full-world reveal that un-fogs ONLY the surveyed disc — vanilla's map shader then composites
            // the real fog-of-war cloud everywhere else (AT-FOG-VANILLA), table-anchored for BOTH surfaces
            // (the reveal is in absolute world-texel space, so the player-centred disc shows the survey at
            // its true world position with no resample — R1 falls out for free).
            BindBoundedReveal(survey, textureSize);
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

        /// <summary>
        /// The world-space span (metres, edge-to-edge) the surface displays — the SINGLE source of truth
        /// for both the shader zoom and the pin/marker projection (so they can never drift). When
        /// <c>ViewSpanMeters</c> &gt; 0 the surface locks that fixed span (the corner disc's tight nav
        /// window); when 0 it shows the full survey window (<c>Size × pixelSize</c> — the full M-view). The
        /// fixed disc span is clamped to the survey extent so it can never frame more than was surveyed.
        /// </summary>
        private float DisplayedSpanMeters(SurveyData survey, float pixelSize)
        {
            float fullSurveySpan = survey.Size * pixelSize;
            if (_cfg.ViewSpanMeters <= 0f) return fullSurveySpan;
            return Mathf.Min(_cfg.ViewSpanMeters, fullSurveySpan);
        }

        // §§SURFACE_RENDER§§

        /// <summary>
        /// §2E.5 defect 2 — build (or refresh) a FULL-WORLD reveal mask and bind it as <c>_FogTex</c> on
        /// the cloned map material, so vanilla's own map shader renders the unexplored area as the real
        /// fog-of-war cloud (AT-FOG-VANILLA) and the surveyed disc as un-fogged cartography. This REPLACES
        /// the old opaque <c>_shroudImage</c> RGBA overlay that hid both the terrain and vanilla's cloud.
        ///
        /// Why full-world (textureSize²) and not a small windowed reveal: vanilla's cloned material samples
        /// <c>_FogTex</c> in FULL-texture UV space (the shader pairs it with <c>_MainTex</c>, also full-world),
        /// so a 256² reveal aligns 1:1 with the framed biome by construction — no windowed-registration spike
        /// needed (§2E.5.1's fallback, chosen deliberately as the low-risk route). We start fully fogged
        /// (R=255, vanilla's Reset convention, decomp Minimap.cs:494-498) and clear R=0 ONLY on cells that are
        /// lit in the table-anchored survey window (explored AND inside the 1000 m disc). The reveal is in
        /// ABSOLUTE world-cell space, so the disc and modal agree and the player-centred disc needs no
        /// resample — the survey shows at its true world position (R1 table-anchored shroud falls out for free).
        ///
        /// Clean-side (ADR-0001): R8G8 reveal format + the R=0 lit / R=255 fogged convention are read from the
        /// base-game decomp (m_fogTexture, :426-428; Explore sets pixel.r=0, :1561-1563), adapted onto our clone.
        /// </summary>
        private void BindBoundedReveal(SurveyData survey, int textureSize)
        {
            if (_shaderMat == null || survey.Fog == null || survey.Size <= 0) return;

            int tex = textureSize > 0 ? textureSize : 256;
            if (_revealTex == null || _revealTex.width != tex)
            {
                if (_revealTex != null) UnityEngine.Object.Destroy(_revealTex);
                _revealTex = new Texture2D(tex, tex, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "SBPR_BoundedReveal",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                _revealScratch = null; // force re-alloc at the new size
            }

            int count = tex * tex;
            if (_revealScratch == null || _revealScratch.Length != count)
                _revealScratch = new Color32[count];
            var px = _revealScratch;

            // Vanilla Reset fills the fog mask fully fogged (R=255). We mirror that, then un-fog the disc.
            var fogged  = new Color32(255, 0, 0, 255);
            var cleared = new Color32(0, 0, 0, 255);
            for (int i = 0; i < count; i++) px[i] = fogged;

            // Un-fog exactly the lit cells of the table-anchored survey window, mapped back to their
            // absolute source-cell position in the full 256² grid (the inverse of CaptureWindow).
            float pixelSize = survey.PixelSize > 0f ? survey.PixelSize : 64f;
            int size = survey.Size;
            int cx = BoundedMapMath.WorldToCellX(survey.OriginX, pixelSize, tex);
            int cy = BoundedMapMath.WorldToCellY(survey.OriginZ, pixelSize, tex);
            int half = (size - 1) / 2;
            var fog = survey.Fog;

            for (int wy = 0; wy < size; wy++)
            {
                int srcY = (wy - half) + cy;
                if (srcY < 0 || srcY >= tex) continue;
                int rowBase = srcY * tex;
                int fogRow = wy * size;
                for (int wx = 0; wx < size; wx++)
                {
                    int fi = fogRow + wx;
                    if (fi >= fog.Length || !fog[fi]) continue;
                    int srcX = (wx - half) + cx;
                    if (srcX < 0 || srcX >= tex) continue;
                    px[rowBase + srcX] = cleared;
                }
            }

            _revealTex.SetPixels32(px);
            _revealTex.Apply(updateMipmaps: false);
            _shaderMat.SetTexture("_FogTex", _revealTex);
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
            // Same displayed span the shader frames at (DisplayedSpanMeters) — so a pin/marker lands on the
            // exact terrain cell it annotates at the disc's tight zoom, not the full-survey span. If these
            // two ever diverged, pins would drift off their terrain as the zoom tightened.
            float span = DisplayedSpanMeters(survey, pixelSize);
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
            // §2E.5: the GPU-less fallback no longer has an opaque shroud overlay to disable (the reveal
            // is bound on the shader path only). Clear any stale _FogTex override so a later successful
            // shader render rebinds cleanly; the 2-colour fill below carries its own shroud colour.
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
                // A′: keep the chevron texture's own colours (white tint). Counter-rotation to screen-up
                // is applied in ApplyFieldOrientation so "up = your facing".
                _playerMarker.color = _playerMarker.texture != null ? Color.white : new Color(0.4f, 0.7f, 1f, 1f);
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
                // A′: in-disc marker keeps the chevron texture (white tint); the off-disc branch above
                // keeps its orange edge-arrow recolour as a distinct "you're off the map" indicator.
                _playerMarker.color = _playerMarker.texture != null ? Color.white : new Color(0.4f, 0.7f, 1f, 1f);
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
                var rt = _playerMarker.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(PlayerMarkerPx, PlayerMarkerPx);

                // §2H.1 A′ (card t_efe8b32b): the marker is the vanilla "you are here" glyph, not a bare
                // blue quad. Prefer vanilla's own player-marker art (blueprint read, ADR-0001/0006-clean —
                // no clone); if it can't be resolved, fall back to a procedurally-drawn chevron so the
                // marker is never invisible. ResolvePlayerMarkerArt logs which path won (so a GPU client's
                // log reveals whether vanilla art actually loaded — the fallback is not silent). On the
                // player-centred disc the glyph counter-rotates to stay screen-up = "you, forward = up"
                // (ApplyFieldOrientation), NOT a north indicator — the disc rotates to heading (AT-LMAP-TC-5).
                ResolvePlayerMarkerArt();
                if (_playerMarkerTexCache != null)
                {
                    // Tag the GO so the fallback-vs-vanilla state is inspectable in a UI dump.
                    go.name = _playerMarkerUsedFallback ? "playerMarker(chevronFallback)" : "playerMarker(vanilla)";
                    _playerMarker.texture = _playerMarkerTexCache;
                    // Honour the vanilla sprite's atlas sub-rect so we blit the arrow, not the whole UI atlas.
                    if (_playerMarkerSprite != null)
                    {
                        var t = _playerMarkerSprite.texture;
                        var r = _playerMarkerSprite.textureRect;
                        _playerMarker.uvRect = new Rect(r.x / t.width, r.y / t.height, r.width / t.width, r.height / t.height);
                    }
                    else
                    {
                        _playerMarker.uvRect = new Rect(0f, 0f, 1f, 1f);
                    }
                    _playerMarker.color = Color.white;
                }
                else
                {
                    _playerMarker.color = new Color(0.4f, 0.7f, 1f, 1f); // last-ditch (should never hit)
                }
            }
        }

        // §2H.1 A′: cache vanilla's player-marker art (read once). Static so both surfaces share it.
        // We capture the SPRITE (not just the texture) so the marker honours the arrow's atlas sub-rect
        // instead of blitting the whole UI atlas. _playerMarkerSprite==null AND _playerMarkerTexCache
        // being the chevron means the vanilla read fell through (logged loudly so Prime's log shows it).
        private static Sprite? _playerMarkerSprite;
        private static Texture? _playerMarkerTexCache;
        private static bool _playerMarkerTexResolved;
        private static bool _playerMarkerUsedFallback;

        /// <summary>
        /// Resolve the vanilla "you are here" player-marker art for our marker. Vanilla's marker is
        /// <c>Minimap.instance.m_smallMarker</c> / <c>m_largeMarker</c> — a RectTransform that carries (or
        /// whose child carries) a uGUI <c>Graphic</c> with the arrow sprite (decomp Minimap.cs:156/158,
        /// rotated by heading :1416). We READ that graphic's sprite/texture (blueprint read — ADR-0001/0006
        /// clean: reading a vanilla asset is not cloning). Checks BOTH the marker's own graphic and its
        /// children, and prefers a Sprite so the atlas sub-rect is honoured. If nothing resolves (null
        /// graphic, nomap-disabled tree, headless), we synthesize an upward chevron so the marker is never
        /// blank — and **log loudly** which path was taken, so "did vanilla art actually load?" is
        /// answerable from the BepInEx log on a real client (not silently masked by the fallback).
        /// </summary>
        private static void ResolvePlayerMarkerArt()
        {
            if (_playerMarkerTexResolved) return;
            _playerMarkerTexResolved = true;

            try
            {
                var mm = Minimap.instance;
                if (mm != null)
                {
                    // Try small marker, then large; for each, the transform's own graphic then children.
                    foreach (var src in new[] { mm.m_smallMarker, mm.m_largeMarker })
                    {
                        if (src == null) continue;
                        var graphics = src.GetComponentsInChildren<Graphic>(includeInactive: true);
                        foreach (var g in graphics)
                        {
                            if (g is Image img && img.sprite != null && img.sprite.texture != null)
                            {
                                _playerMarkerSprite = img.sprite;
                                _playerMarkerTexCache = img.sprite.texture;
                                Plugin.Log.LogInfo($"[Trailborne/Cartography] A′ player-marker: using VANILLA art " +
                                                   $"(sprite '{img.sprite.name}' on '{g.gameObject.name}').");
                                return;
                            }
                            if (g is RawImage raw && raw.texture != null)
                            {
                                _playerMarkerTexCache = raw.texture;
                                Plugin.Log.LogInfo($"[Trailborne/Cartography] A′ player-marker: using VANILLA art " +
                                                   $"(RawImage texture on '{g.gameObject.name}').");
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Cartography] A′ player-marker: vanilla art read threw ({e.Message}); using chevron fallback.");
            }

            _playerMarkerTexCache = BuildChevronTexture();
            _playerMarkerUsedFallback = true;
            Plugin.Log.LogWarning("[Trailborne/Cartography] A′ player-marker: vanilla art did NOT resolve " +
                                  "(null marker/graphic or headless) — using the SBPR chevron fallback. " +
                                  "If you see this on a GPU client, the vanilla-marker read needs revisiting.");
        }

        /// <summary>
        /// Procedural fallback marker: a filled upward chevron (▲-ish "you" glyph) on transparent. Drawn
        /// once. Up = +Y so that, screen-stable on the disc, it reads as "forward". Pure SBPR art (no
        /// vanilla dependency) — the never-blank guarantee for the A′ marker.
        /// </summary>
        private static Texture2D BuildChevronTexture()
        {
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, mipChain: false)
            {
                name = "SBPR_PlayerChevron",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[N * N];
            var clear = new Color32(0, 0, 0, 0);
            var fill  = new Color32(235, 240, 255, 255);
            var edge  = new Color32(20, 30, 50, 255);
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            // Up-arrowhead with a V-notch at the bottom (a "you are here, facing up" chevron). Geometry:
            //   • OUTER triangle: apex at top-centre (0.5,1), flaring to the bottom corners → point is UP.
            //   • INNER notch: a smaller up-pointing triangle carved out of the BOTTOM half, splitting the
            //     base into two wings/legs. solid = inside outer AND NOT inside the bottom notch.
            // row 0 = bottom (Unity texture convention), so fy increases upward and the apex sits at fy≈1.
            for (int y = 0; y < N; y++)
            {
                float fy = (float)y / (N - 1);                 // 0 bottom → 1 top
                float outerHalf = 0.42f * (1f - fy);           // 0 at the top apex → 0.42 at the base
                float notchHalf = fy < 0.5f ? 0.22f * (1f - 2f * fy) : 0f; // bottom V-notch (0 at mid → 0.22 at base)
                for (int x = 0; x < N; x++)
                {
                    float dx = Mathf.Abs((float)x / (N - 1) - 0.5f); // 0 centre → 0.5 edge
                    bool solid = dx <= outerHalf && dx >= notchHalf;
                    if (!solid) continue;
                    bool isEdge = dx > outerHalf - 0.045f || (notchHalf > 0f && dx < notchHalf + 0.045f) || fy > 0.95f;
                    px[y * N + x] = isEdge ? edge : fill;
                }
            }
            t.SetPixels32(px);
            t.Apply(updateMipmaps: false);
            return t;
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
            float span = DisplayedSpanMeters(survey, pixelSize); // MUST match the forward projection's span
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
        /// instead of a 900-px-tuned absolute inset swallowing a 200-px disc.
        ///
        /// §2E.5 defect 1 (transparent-outside): the alpha is now a BAND, not a contiguous opaque cover.
        ///   • inside holeR        → α=0 (transparent: the circular cartography shows through)
        ///   • holeR → ringOuterR  → opaque BRONZE ring (the disc edge)
        ///   • beyond ringOuterR    → α=0 (TRANSPARENT: the game world shows through outside the disc)
        /// The shipped bug lerped everything past holeR toward an opaque near-black `cornerShroud` (α=1),
        /// which on the no-backdrop HUD disc WAS the black square Daniel saw. The modal's "outside is dim"
        /// now comes solely from its separate ShowBackdrop layer, not from an opaque bezel — so the SAME
        /// transparent-outside bezel serves both surfaces (AT-DISC-CLIP == AT-MODAL-CLIP). The cartography
        /// itself is clipped to the circle by CircularRawImage (geometry fan), so corners emit no pixels
        /// regardless of the map shader; this bezel only draws the ring + guarantees outside-transparent.
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
            float ringPx     = Mathf.Max(_cfg.TargetPx * BezelRingFrac, BezelRingMinPx);
            float holeR      = discEdge - insetPx;
            float ringOuterR = holeR + ringPx;
            const float aa   = 0.9f;

            var ringColor = new Color(0.62f, 0.55f, 0.42f, 1f);

            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float dScreen = Mathf.Sqrt(dx * dx + dy * dy) * screenPerTex;

                    // Ring alpha = band: rises from 0→1 across the inner edge (holeR), falls 1→0 across
                    // the outer edge (ringOuterR). Inside the hole AND beyond the ring are both α=0.
                    float inner = Mathf.SmoothStep(0f, 1f, (dScreen - (holeR - aa)) / (2f * aa));
                    float outer = Mathf.SmoothStep(0f, 1f, (dScreen - (ringOuterR - aa)) / (2f * aa));
                    float ringAlpha = Mathf.Clamp01(inner - outer);
                    px[y * N + x] = (Color32)new Color(ringColor.r, ringColor.g, ringColor.b, ringAlpha);
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
            // §2E.5 defect 3: CircularRawImage tessellates the rect into an inscribed DISC (triangle fan)
            // honouring uvRect — the four corners carry no geometry, so they emit no fragments and the
            // disc silhouette is rotation-invariant (the §2H.1 inscribed-circle guarantee holds BY
            // CONSTRUCTION). This is material-agnostic: the cloned vanilla map shader does not support a
            // uGUI stencil/RectMask2D, so a mask-based clip would be silently ignored at disc scale.
            var mapGo = new GameObject("cartography");
            mapGo.transform.SetParent(_mapContainer.transform, false);
            _mapImage = mapGo.AddComponent<CircularRawImage>();
            _mapRect = _mapImage.rectTransform;
            _mapRect.anchorMin = _mapRect.anchorMax = new Vector2(0.5f, 0.5f);
            _mapRect.pivot = new Vector2(0.5f, 0.5f);
            _mapRect.sizeDelta = new Vector2(_cfg.TargetPx, _cfg.TargetPx);

            // §2E.5 defect 2: the opaque shroud-mask layer is GONE. Unexplored area is now vanilla's real
            // _FogTex cloud composited by the map shader (BindBoundedReveal), not an RGBA overlay.

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
