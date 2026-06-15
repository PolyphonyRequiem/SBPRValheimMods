// ============================================================================
//  Trailborne v2 cartography — the forked bounded map VIEWER (§2B, the high-risk piece)
// ----------------------------------------------------------------------------
//  Productionizes the GO-WITH-CAVEATS spike (card t_e8bbbe48): a FORK of the vanilla
//  map UI that renders OUR windowed fog array, NOT vanilla's 256² world texture and
//  NOT vanilla's 4-texture shader composite. It is a fully STANDALONE uGUI overlay
//  (own Canvas / RawImage), which is MANDATORY under v1's `nomap` world setting —
//  vanilla forces Minimap into MapMode.None and keeps m_smallRoot / m_largeRoot off
//  (decomp Minimap.SetMapMode :975 `if (Game.m_noMap) mode = None`), so we cannot ride
//  vanilla's roots. The fork owns its own open/close path.
//
//  Registered behind the CartographyViewer seam (IMapViewer), so it is shared by:
//    • the Local Map (FieldReadOnly mode — no pin editing), opened by LocalMapController
//      on equip; and
//    • the Surveyor's Table (TableEdit mode — pin REMOVAL enabled via ICartographyPinEditor),
//      opened by SurveyorTableTag.Interact.
//  ONE viewer, a mode flag — never two forks (impl spec §4).
//
//  Render path (all proven feasible by the spike, compiled 0/0 against the real DLLs):
//    1. SurveyData carries the Size×Size bool fog window + bound origin + radius + pins.
//    2. Paint our own Texture2D (RGBA32, Point filter for crisp cells at fixed zoom) —
//       parchment = explored-and-in-disc, shroud = everything else.
//    3. Show it on a centered RawImage at a FIXED upscale (no scroll-zoom — AT-MAP-FIXEDZOOM).
//    4. Overlay pins (inside the disc only — AT-MAP-BOUND), the player marker, and —
//       when the player is outside the disc — a direction arrow polar-clamped to the
//       1000 m shroud radius (BoundedMapMath.EdgeClampToDisc — AT-MAP-EDGEARROW; the
//       edge-arrow render + Table-mode pin-click are finished in M-C).
//
//  Clean-side (ADR-0001): vanilla Minimap/PinData read+adapted; the uGUI surface is our
//  own (the SignPaintPanel idiom this repo already ships). No vanilla UI prefab cloned.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SBPR.Trailborne.Features.Cartography
{
    // Alias the MarkerSigns TYPE: the bare name `MarkerSigns` binds to the sibling
    // NAMESPACE from here, so alias it to the static class for the MarkerTypes lookup
    // (the shared #100 WorldPin marker model — impl spec §2B: consume the seam, don't fork).
    using MarkerSignsType = SBPR.Trailborne.Features.MarkerSigns.MarkerSigns;
    using WorldPins = SBPR.Trailborne.Features.MarkerSigns.WorldPins;
    /// <summary>
    /// The forked bounded viewer. A client-only singleton MonoBehaviour that implements
    /// <see cref="IMapViewer"/> and registers itself with <see cref="CartographyViewer"/>.
    /// Lives on its own host GameObject (DontDestroyOnLoad), like SignPaintPanel.
    /// </summary>
    public class MapViewer : MonoBehaviour, IMapViewer
    {
        // ── Singleton + registration ──────────────────────────────────────────────────
        private static MapViewer? _instance;
        public static MapViewer? Instance => _instance;

        /// <summary>Idempotently create + register the viewer (called from the bootstrap).</summary>
        public static void EnsureRegistered()
        {
            if (_instance != null) return;
            var host = new GameObject("SBPR_MapViewerHost");
            UnityEngine.Object.DontDestroyOnLoad(host);
            _instance = host.AddComponent<MapViewer>();
            CartographyViewer.Register(_instance);
        }

        // ── Fixed-zoom render constants ───────────────────────────────────────────────
        // Each window cell → a fixed number of screen px. The full view fills most of the
        // screen height; we size the RawImage by (Size * upscale) and clamp to the screen
        // so a small window (33×33) reads big and a (future) larger window still fits.
        private const int   MaxFullViewPx   = 900;  // target on-screen square edge (full view)
        private const float PinIconPx       = 22f;  // pin marker size on screen
        private const float PinLabelFontPx  = 14f;   // §2K: pin label text size (annotation scale)
        private const float PlayerMarkerPx  = 26f;

        // ── §2H.1 orientation model — fixed-window, TABLE-centred, circular, rotate-to-heading ──
        // The held Local Map (FieldReadOnly) is a FIXED-WINDOW, TABLE-centred, CIRCULAR,
        // rotate-to-heading minimap (Daniel-locked, card t_05e702ee, 2026-06-12):
        //   • TABLE-centred, no pan: the 1000 m window is static on the bound Table's survey
        //     origin and does NOT slide to follow the player (resolves #4).
        //   • Player marker at its TRUE table-relative position; HIDDEN + an outward edge arrow
        //     shown when the player leaves the disc (resolves #3).
        //   • CIRCULAR with a FIXED bezel/frame that never rotates — only the interior content
        //     (cartography + shroud + pins + marker) rotates, clipped to a fixed disc (#9 + #2).
        //   • Rotates to heading about the TABLE pivot. A player away from the table sees their
        //     marker ORBIT screen centre — intended (the table is the pivot, not the player).
        //   • NO north reference of any kind (Daniel: disorientation is the design; the future
        //     swamp Iron Compass is the earned orientation tool). No north-up mode/toggle.
        //
        // The Surveyor's Table (TableEdit) view ALSO rotates-to-heading (issue #1, Daniel-locked
        // 2026-06-12, starbright-engineering comment): a north-locked table view was a free North
        // reference, which defeats the no-North pillar. Both views rotate; neither has a North
        // indicator. The table view stays table-centred + square (fuller extent for pin-editing
        // visibility); only its rotation changes vs. the pre-§2H.1 north-up lock.
        //
        // 🔴 BUILD-CALIBRATED SIGN (§2H.1 b4). The rotation sense + camera-vs-body-yaw source
        // can't be confirmed on a headless build worker (no GPU, no live camera). Vanilla rotates
        // its NORTH-UP marker by -cameraYaw; rotating the MAP so forward = up is the opposite
        // sense, so the first guess is +cameraYaw. If the map turns the wrong way in-game, flip
        // this one constant to -1f. It is the single calibration knob; there is NO other flag and
        // NO north-up alternative to expose (Daniel reversed that — disorientation is intended).
        private const float MapRotationSign = 1f;

        // §2H.1 b3 (issue 6 — edge-bleed fix): the held map's visible disc is the inscribed
        // circle of the square cartography texture, which is invariant under rotation about centre
        // — so rotating never uncovers an empty corner (the four corner triangles are shroud-
        // opaque). The fixed bezel is drawn OVER the interior and its OPAQUE cover (bronze ring +
        // shroud) is the hard circular clip: everything beyond the disc radius is occluded. The
        // visible disc is inset this many SCREEN px INSIDE the inscribed circle (which met the
        // square with ZERO margin) so the square's four straight tangents (12/6/9/3 o'clock) always
        // sit under the opaque cover, never on the sub-pixel seam that leaked the parchment slivers.
        private const float BezelDiscInsetPx = 6f;

        // Width (screen px) of the bronze ring band drawn at the visible disc edge. The opaque
        // shroud fills everything beyond it, so ring + shroud form ONE contiguous opaque cover
        // (no thin isolated band that bilinear upscaling can thin into a seam — the other half of
        // the issue-6 fix). Roughly matches the prior ~9 px frame so the look is unchanged.
        private const float BezelRingWidthPx = 10f;

        // ── §2E.3 LOCKED RENDER: vanilla styled material is THE render (issue 10) ─────
        // The cartography is the REAL vanilla parchment look — a COPY of vanilla's styled
        // large-map material (Minimap.m_mapImageLarge.material: paper texture + cloud/haze +
        // fog feathering live in that GPU display shader, NOT in the four data textures it
        // composites), framed to our bound 1000 m disc. Confirmed good on Daniel's GPU client
        // (v0.2.23-playtest, 2026-06-12) with NO toggle. The earlier selectable SHADER-vs-CPU
        // render mode and its CPU compositor (CartographyComposer, §2E.1) were REMOVED: the CPU
        // path existed to insure against "this client can't drive the vanilla map shader," but a
        // client that can't drive the vanilla map shader can't see the vanilla map either — it
        // insured a non-scenario. PaintFog remains the only fallback (the never-blank guard for
        // the pre-join / Minimap-not-generated window — that is NOT "CPU mode").

        // Palette — our own dark-Norse shroud (clean-room; no vanilla sprite copy).
        // CParchment/CShroud are the 2-color PaintFog FALLBACK palette (never-blank guard, §2E);
        // CShroudA is the opaque shroud used to mask OVER the real vanilla cartography.
        private static readonly Color32 CParchment = new Color32(214, 198, 162, 255); // fallback: explored & in-disc
        private static readonly Color32 CShroud    = new Color32(14, 13, 11, 255);    // fallback: outside disc / unexplored
        private static readonly Color32 CShroudA   = new Color32(10, 9, 8, 255);       // mask shroud (opaque over cartography)
        private static readonly Color32 CClear     = new Color32(0, 0, 0, 0);          // mask lit cell (show cartography through)
        private static readonly Color   CBackdrop  = new Color(0f, 0f, 0f, 0.92f);

        // ── UI refs ───────────────────────────────────────────────────────────────────
        private GameObject? _root;          // the whole overlay (toggled active)
        private Canvas? _canvas;
        private RectTransform? _frame;      // §2H.1: FIXED, screen-aligned circular clip+bezel (NEVER rotates)
        private RectTransform? _mapContainer; // §2H.1: the rotating INTERIOR (rotates to heading in both modes)
        private RawImage? _mapImage;        // the CARTOGRAPHY layer (CPU composite; 2-color fallback)
        private RawImage? _shroudImage;     // the disc+survey shroud mask layered OVER the cartography
        private RectTransform? _mapRect;    // the map square (pins/markers parent to this)
        private GameObject? _overlayLayer;  // pins + player marker + edge arrow live here
        private RawImage? _bezel;           // §2H.1: fixed circular frame ring drawn over the disc edge
        private Texture2D? _tex;            // fallback 2-color fog texture
        private Texture2D? _shroudTex;      // the alpha shroud-mask texture (lit→clear, shroud→opaque)
        private Material?  _shaderMat;      // §2E.3: instantiated COPY of vanilla's styled map material (THE render)
        private Texture2D? _bezelTex;       // §2H.1: the circular bezel ring texture (built once)
        private RawImage? _playerMarker;    // reused player dot/arrow
        private Text? _exitPrompt;          // §2F: bottom-center "[Esc] Close map" (+ TableEdit pin hint)
        private Text? _titleLabel;          // §2B.1: top-center Table-name cartouche (issue 10)
        private readonly List<GameObject> _pinObjects = new List<GameObject>();

        // ── Live request state ──────────────────────────────────────────────────────────
        private MapViewRequest _req;

        // §2I.1/§2I.2 (issue 6, Part A): open-state is derived from the CANVAS, never a side
        // bool. The two sign panels (SignPaintPanel.IsOpen, MarkerSignPanel.IsOpen) already key
        // on `_root.activeSelf` and so cannot desync; the viewer's old standalone `_open` bool
        // COULD latch true while nothing was on screen (a Close() that was skipped, an exception
        // out of Render() after `_open=true`, a scene/route change), which kept
        // CartographyViewer.IsViewerOpen → SignPanelInputBlock.AnyOpen latched and silently
        // killed E-to-open until the Table was re-used. Deriving IsOpen from the live root makes
        // the gate un-latchable: if the overlay isn't active, every consumer reads "closed".
        public bool IsOpen => _root != null && _root.activeSelf;
        public MapViewerMode CurrentMode => _req.Mode;

        // ── IMapViewer ──────────────────────────────────────────────────────────────────

        public void Open(MapViewRequest request)
        {
            _req = request;
            EnsureCanvas();
            _root!.SetActive(true);   // §2I.1: this IS the open-state now — IsOpen reads _root.activeSelf
            Render();
        }

        public void Refresh(MapViewRequest request)
        {
            if (!IsOpen) return;
            _req = request;
            Render();
        }

        public void Close()
        {
            if (_root != null) _root.SetActive(false);   // §2I.1: deactivating the root IS the close
        }

        // ── Render: §2E.1 CPU-composite cartography + shroud mask + overlay ───────────────

        private void Render()
        {
            var survey = _req.Survey;
            if (survey == null || survey.Fog == null || survey.Size <= 0)
            {
                Plugin.Log.LogWarning("[Trailborne/Cartography] MapViewer.Render: empty/blank survey; nothing to draw.");
                return;
            }

            LayoutMapRect(survey.Size);

            // §2E.3 LOCKED ROUTE (Daniel, v0.2.23-playtest 2026-06-12): the cartography is the
            // REAL vanilla styled map material (the parchment look), framed to the bound origin's
            // 1000 m disc, with OUR fog window as the shroud mask on top. There is NO render-mode
            // toggle: the selectable SHADER-vs-CPU pick and the CPU compositor were removed once
            // the shader render was confirmed good on a real GPU client (a client that can't drive
            // the vanilla map shader can't see the vanilla map either, so the CPU fallback insured
            // a non-scenario). PaintFog is the ONLY fallback — the never-blank guard for the
            // pre-join / Minimap-not-generated window (WorldGenerator/Minimap absent), NOT a
            // second render mode.
            if (!TryRenderVanillaShader(survey))
                PaintFog(survey); // never-blank guard → the explored/shroud two-color fill

            RebuildOverlay(survey);

            // §2F.3: mode-aware exit prompt text. FieldReadOnly shows just the close hint;
            // TableEdit also surfaces the left-click pin-removal affordance.
            UpdateExitPrompt();

            // §2B.1: top-center Table-name cartouche (issue 10). Hidden when the request
            // carries no Title (unnamed Table / pre-1.6 imprinted map).
            UpdateTitle();

            // §2H.1: the held map (FieldReadOnly) is CIRCULAR with the fixed bezel; the table
            // view (TableEdit) keeps its fuller SQUARE extent for pin-editing visibility, so the
            // bezel/corner-mask is hidden there. Toggle per Render by mode.
            UpdateFrameForMode();

            // §2H.1: in both modes, rotate the interior to heading (table-centred, no pan). In
            // TableEdit this also rotates now (issue #1). Applied on every Render AND every frame
            // from Update() (so rotation tracks heading at frame rate, not the 0.25 s refresh).
            ApplyFieldOrientation(survey);
        }

        /// <summary>
        /// §2E.3 SHADER render — the parchment look. Reuse a COPY of vanilla's STYLED large-map
        /// material (<c>Minimap.instance.m_mapImageLarge.material</c>), which carries the four
        /// bound cartography textures (<c>_MainTex</c> biome / <c>_MaskTex</c> forest / <c>_HeightTex</c>
        /// relief / <c>_FogTex</c> fog) AND the display shader that composites the paper texture,
        /// cloud/haze, and fog feathering — i.e. the actual vanilla map LOOK. Drive the copy's
        /// <c>_mapCenter</c>/<c>_zoom</c>/<c>_pixelSize</c> + the RawImage <c>uvRect</c> in LOCKSTEP
        /// (vanilla <c>CenterMap</c>, Minimap.cs:1004-1034) to frame the bound origin's 1000&#160;m
        /// disc at our fixed scale. OUR survey fog is then overlaid as the hard 1000&#160;m shroud
        /// mask on top (vanilla's native <c>_FogTex</c> haze stays inside the disc — Daniel's
        /// "keep vanilla haze, hard cutoff past 1000&#160;m" lean; tunable later).
        ///
        /// <para>We instantiate a COPY and never mutate vanilla's live material or its roots, so
        /// nomap stays in force (AT-RENDER-NOMAP-INTACT). Returns <c>false</c> → caller falls back
        /// to <see cref="PaintFog"/> (the never-blank guard) when the styled material/textures
        /// aren't generated: that happens on a headless / GPU-less client (vanilla gates the bake on
        /// <c>graphicsDeviceType != Null</c>, Minimap.cs:552) or pre-join — but on a real GPU client
        /// (the only kind that can see the vanilla map at all) the styled material IS present, which
        /// is why this is the unconditional render and the CPU composite was removed (§2E.3). The
        /// blank-render bug the ORIGINAL attempt (§2E, v0.2.22) hit was a uvRect↔uniform
        /// double-transform; this copies CenterMap's lockstep exactly to avoid it.</para>
        /// </summary>
        private bool TryRenderVanillaShader(SurveyData survey)
        {
            var mm = Minimap.instance;
            if (mm == null || mm.m_mapImageLarge == null || mm.m_mapImageLarge.material == null)
                return false;

            Material liveMat = mm.m_mapImageLarge.material;

            // The styled material only carries a generated _MainTex on a GPU client that has run
            // GenerateWorldMap (Minimap.cs:552 gates it on graphicsDeviceType != Null). No main
            // texture → nothing to draw → fall back to PaintFog cleanly (the never-blank guard).
            Texture? mainTex = liveMat.GetTexture("_MainTex");
            if (mainTex == null)
                return false;

            // Instantiate ONE persistent COPY of the styled material (inherits the 4 texture
            // bindings + the shader by reference). Never touch vanilla's live material/roots.
            if (_shaderMat == null)
            {
                try
                {
                    _shaderMat = UnityEngine.Object.Instantiate(liveMat);
                    _shaderMat.name = "SBPR_BoundedMapMaterial(Clone)";
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Trailborne/Cartography] MapViewer: could not copy vanilla map material ({e.Message}); falling back to PaintFog.");
                    _shaderMat = null;
                    return false;
                }
            }

            float pixelSize   = survey.PixelSize > 0f ? survey.PixelSize : 64f;   // world m per source cell
            int   textureSize = survey.TextureSize > 0 ? survey.TextureSize : 256;
            int   size        = survey.Size;
            Vector3 origin    = _req.BoundOrigin;

            // Framing — vanilla CenterMap lockstep (Minimap.cs:1004-1034). WorldToMapPoint
            // (Minimap.cs:1496-1501): mx = p.x/pixelSize + textureSize/2 (texture-pixel units),
            // then /textureSize → 0..1 uv. The window's uv span (zoom) = our cell count /
            // textureSize. uvRect.center, _mapCenter, _pixelSize, and _zoom MUST all be set
            // consistently or the shader samples the wrong region → blank (the §2E bug).
            float mx = origin.x / pixelSize + textureSize / 2f;
            float my = origin.z / pixelSize + textureSize / 2f;
            float uvCx = mx / textureSize;
            float uvCy = my / textureSize;
            float zoom = Mathf.Clamp((float)size / textureSize, 0.0001f, 1f); // uv span of our window

            if (_mapImage != null)
            {
                _mapImage.material = _shaderMat;
                _mapImage.texture  = mainTex;           // a valid main texture so the RawImage draws
                _mapImage.color    = Color.white;
                var uv = _mapImage.uvRect;
                uv.width  = zoom;
                uv.height = zoom;
                uv.center = new Vector2(uvCx, uvCy);
                _mapImage.uvRect = uv;
            }

            // Drive the shader uniforms on OUR copy in lockstep with the uvRect above
            // (vanilla CenterMap :1024-1027). _pixelSize is the SHADER uniform (200/zoom), NOT the
            // world pixelSize; _mapCenter is the bound origin in world space.
            _shaderMat.SetFloat("_zoom", zoom);
            _shaderMat.SetFloat("_pixelSize", 200f / Mathf.Max(zoom, 1e-4f));
            _shaderMat.SetVector("_mapCenter", new Vector4(origin.x, origin.z, 0f, 0f));

            // OUR hard 1000 m shroud mask on top (same window as the fog/pins). Vanilla's native
            // _FogTex haze stays live inside the disc (left as-is in the copied material).
            PaintShroudMask(survey);

            return true;
        }

        /// <summary>
        /// §2H.1 b3 (issue 6 — edge-bleed fix): build (once) the FIXED circular bezel — a HARD
        /// circular alpha cover drawn OVER the rotating interior. It is fully transparent inside
        /// the visible disc (cartography shows through), a bronze ring band at the disc edge, and
        /// OPAQUE shroud-tone everywhere beyond — so ring + shroud are ONE contiguous opaque cover
        /// that clips everything past the disc radius. Because the map interior renders with the
        /// vanilla map SHADER (no stencil pass), a uGUI <c>Mask</c> can't clip it; this alpha cover
        /// is the shader-agnostic clip that always works.
        ///
        /// <para>Two changes vs. the leaking version: (1) the transparent hole is inset
        /// <see cref="BezelDiscInsetPx"/> screen px INSIDE the square cartography's inscribed circle
        /// (which previously met the square with ZERO margin), so the square's four straight
        /// tangents (12/6/9/3 o'clock) always sit under opaque cover; (2) the disc edge is built
        /// with ANALYTIC anti-aliasing at high resolution (not a low-res Bilinear step), so the
        /// alpha reaches fully opaque well inside the square edge — no sub-pixel/bilinear seam for
        /// parchment to bleed through. The bezel RawImage is sized ×√2 of the map square so the
        /// opaque corners always cover the rotating interior's square corners (the #2 + #9 fix is
        /// preserved).</para>
        /// </summary>
        /// <param name="bezelEdgeScreenPx">On-screen edge length of the bezel RawImage (px), used
        /// to map texture px ↔ screen px so the inset/ring widths are exact screen distances.</param>
        private Texture2D EnsureBezelTexture(float bezelEdgeScreenPx)
        {
            if (_bezelTex != null) return _bezelTex;

            // High enough that the on-screen mapping is ~1:1 (no upscale smear at the disc edge).
            const int N = 1024;
            _bezelTex = new Texture2D(N, N, TextureFormat.RGBA32, mipChain: false)
            {
                name = "SBPR_Bezel",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            float half = N / 2f;
            float screenPerTex = bezelEdgeScreenPx / N; // screen px represented by one texture px

            // Thresholds in SCREEN px from centre. The square cartography's inscribed circle (= its
            // straight-edge tangents) is at MaxFullViewPx/2; the visible transparent disc stops a
            // few px short of it so those straight edges are always covered.
            float discEdge   = MaxFullViewPx * 0.5f;                 // inscribed circle / square edge
            float holeR      = discEdge - BezelDiscInsetPx;          // visible transparent disc radius
            float ringOuterR = holeR + BezelRingWidthPx;             // bronze ring outer edge; beyond = shroud
            const float aa   = 0.9f;                                 // analytic edge feather (screen px)

            var ringColor    = new Color(0.62f, 0.55f, 0.42f, 1f);   // muted bronze frame
            var cornerShroud = new Color(0.04f, 0.035f, 0.03f, 1f);  // opaque shroud-tone cover

            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float dScreen = Mathf.Sqrt(dx * dx + dy * dy) * screenPerTex;

                    // Alpha: 0 inside the hole → 1 outside, feathered over ±aa at the disc edge.
                    // Reaches full opacity by holeR+aa, far inside the square edge at discEdge.
                    float coverage = Mathf.SmoothStep(0f, 1f, (dScreen - (holeR - aa)) / (2f * aa));
                    // RGB: bronze in the ring band, fading to shroud past the ring outer edge. Both
                    // are opaque, so this transition is purely cosmetic (never a bleed path).
                    float ringMix = Mathf.SmoothStep(0f, 1f, (dScreen - (ringOuterR - aa)) / (2f * aa));
                    Color rgb = Color.Lerp(ringColor, cornerShroud, ringMix);

                    px[y * N + x] = (Color32)new Color(rgb.r, rgb.g, rgb.b, coverage);
                }
            }
            _bezelTex.SetPixels32(px);
            _bezelTex.Apply(updateMipmaps: false);
            return _bezelTex;
        }

        /// <summary>
        /// Build the shroud-mask texture from the survey fog window: lit (explored-and-in-disc)
        /// → transparent (cartography shows through); everything else → opaque shroud. The mask
        /// RawImage sits over the cartography at the same rect, so the bounded disc is realized
        /// purely by the mask (the one deliberate difference from vanilla — §2E bullet 3).
        /// Same bottom-up row convention as PaintFog so it aligns with the cartography window.
        /// </summary>
        private void PaintShroudMask(SurveyData survey)
        {
            int size = survey.Size;
            if (_shroudTex == null || _shroudTex.width != size)
            {
                if (_shroudTex != null) UnityEngine.Object.Destroy(_shroudTex);
                _shroudTex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "SBPR_ShroudMask",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear, // soft shroud edge (not crisp cells — this is the mask)
                };
            }

            var px = new Color32[size * size];
            var fog = survey.Fog;
            int n = Mathf.Min(px.Length, fog.Length);
            for (int i = 0; i < n; i++)
                px[i] = fog[i] ? CClear : CShroudA; // lit → see cartography; else → opaque shroud
            for (int i = n; i < px.Length; i++)
                px[i] = CShroudA;

            _shroudTex.SetPixels32(px);
            _shroudTex.Apply(updateMipmaps: false);

            if (_shroudImage != null)
            {
                _shroudImage.texture = _shroudTex;
                _shroudImage.color = Color.white;
                _shroudImage.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// FALLBACK render (§2E.3 never-blank guard). Paint the Size×Size bool fog window into
        /// our own RGBA32 two-color texture. Used ONLY when the vanilla styled map material/textures
        /// are unavailable (Minimap not generated yet / pre-join / GPU-less) — i.e. when
        /// <see cref="TryRenderVanillaShader"/> returns false. This is NOT a selectable render mode
        /// (the §2E.1 CPU composite was removed in §2E.3); it is the last-resort guard so the disc
        /// is never blank.
        /// </summary>
        private void PaintFog(SurveyData survey)
        {
            // Detach the cartography composite so the fallback two-color texture is what shows.
            if (_shroudImage != null) _shroudImage.gameObject.SetActive(false);

            int size = survey.Size;
            if (_tex == null || _tex.width != size)
            {
                if (_tex != null) UnityEngine.Object.Destroy(_tex);
                _tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "SBPR_BoundedFog",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Point, // crisp cells at fixed zoom (AT-MAP-FIXEDZOOM)
                };
            }

            var px = new Color32[size * size];
            var fog = survey.Fog;
            // Texture rows are bottom-up; our fog window row wy=0 is the southmost cell
            // (OriginCellY, increasing north), which matches texture v=0 at the bottom —
            // so a direct copy keeps north = up on screen.
            int n = Mathf.Min(px.Length, fog.Length);
            for (int i = 0; i < n; i++)
                px[i] = fog[i] ? CParchment : CShroud;

            _tex.SetPixels32(px);
            _tex.Apply(updateMipmaps: false);

            if (_mapImage != null)
            {
                // §2E.3: detach the vanilla styled material so the 2-color fallback texture isn't
                // drawn THROUGH the parchment shader. Previously the (now-removed) CPU composite leg
                // always cleared this between a shader failure and PaintFog; with the shader leg now
                // the only cartography render, PaintFog must clear it itself or a shader→fallback
                // transition would render blank.
                _mapImage.material = null;
                _mapImage.texture = _tex;
                _mapImage.uvRect = new Rect(0f, 0f, 1f, 1f); // reset any cartography windowing
                _mapImage.color = Color.white;
            }
        }

        /// <summary>Size the on-screen map square at a FIXED scale (no scroll-zoom).</summary>
        private void LayoutMapRect(int size)
        {
            if (_mapRect == null) return;
            // Fixed upscale: fill up to MaxFullViewPx, integer-floored so cells stay crisp.
            int upscale = Mathf.Max(1, MaxFullViewPx / Mathf.Max(1, size));
            float edge = size * upscale;
            _mapRect.sizeDelta = new Vector2(edge, edge);
        }

        /// <summary>
        /// Rebuild the pin + player-marker overlay. Pins render ONLY inside the disc
        /// (AT-MAP-BOUND); the player marker shows at its real map position when inside the
        /// disc, or — when outside — as a direction arrow polar-clamped to the shroud radius
        /// (AT-MAP-EDGEARROW). World→map-rect projection uses the same windowed cell math the
        /// fog texture was built from, so pins land exactly on the fog they annotate.
        /// </summary>
        private void RebuildOverlay(SurveyData survey)
        {
            ClearPinObjects();
            if (_overlayLayer == null || _mapRect == null) return;

            Vector3 origin = _req.BoundOrigin;
            float radius = _req.RadiusMeters > 0f ? _req.RadiusMeters : survey.RadiusMeters;
            int size = survey.Size;

            // ── Pins: union the imprinted SNAPSHOT pins with the LIVE WorldPins (§2B —
            //    consume the #100 seam, render ONE shared model, do not fork). The snapshot
            //    carries what was surveyed at imprint; the live scan adds marker-signs pinned
            //    since (and is the Table-view's live source). De-dup by (type, ~1 m, name).
            var rendered = new List<SurveyPin>();
            if (survey.Pins != null) rendered.AddRange(survey.Pins);
            try
            {
                var live = WorldPins.CollectInDiscPins(origin, radius);
                foreach (var lp in live) AddIfNew(rendered, lp);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Cartography] MapViewer: live WorldPins scan failed: {e.Message}");
            }

            foreach (var pin in rendered)
            {
                if (!BoundedMapMath.InDisc(pin.Pos.x, pin.Pos.z, origin.x, origin.z, radius))
                    continue; // beyond 1000 m never renders (AT-MAP-BOUND)
                if (!WorldToMapRect(pin.Pos, survey, out Vector2 anchored))
                    continue;
                SpawnPinMarker(pin, anchored);
            }

            // ── Player marker / edge arrow ──
            UpdatePlayerMarker(survey, origin, radius);
        }

        /// <summary>
        /// Project a world point to the map RawImage's local anchored position (pixels,
        /// centered pivot). Returns false if the point is outside the window grid. Mirrors
        /// the fog windowing: cell = WorldToCell(world), then offset from window center cell,
        /// scaled by the on-screen cell size. North = up (matches the bottom-up texture copy).
        /// </summary>
        private bool WorldToMapRect(Vector3 world, SurveyData survey, out Vector2 anchored)
        {
            anchored = Vector2.zero;
            if (_mapRect == null) return false;

            float pixelSize = survey.PixelSize > 0f ? survey.PixelSize : 64f;
            int textureSize = survey.TextureSize > 0 ? survey.TextureSize : 256;
            int size = survey.Size;

            // Window center cell = the bound origin's cell (BoundedMapMath, vanilla-faithful).
            int cx = BoundedMapMath.WorldToCellX(survey.OriginX, pixelSize, textureSize);
            int cy = BoundedMapMath.WorldToCellY(survey.OriginZ, pixelSize, textureSize);
            int px = BoundedMapMath.WorldToCellX(world.x, pixelSize, textureSize);
            int py = BoundedMapMath.WorldToCellY(world.z, pixelSize, textureSize);

            // Window-cell coords (wx,wy), 0..size-1, center cell at (size-1)/2.
            int half = (size - 1) / 2;
            float wx = (px - cx) + half;
            float wy = (py - cy) + half;
            if (wx < 0 || wy < 0 || wx >= size || wy >= size) return false;

            anchored = MapRectAnchor(wx, wy, size);
            return true;
        }

        /// <summary>Shared core: window-cell (wx,wy) → anchored px relative to the rect's
        /// centered pivot. North = up (matches the bottom-up texture copy).</summary>
        private Vector2 MapRectAnchor(float wx, float wy, int size)
        {
            float edge = _mapRect!.sizeDelta.x;
            float cell = edge / size;
            return new Vector2((wx + 0.5f) * cell - edge / 2f,
                               (wy + 0.5f) * cell - edge / 2f);
        }

        /// <summary>
        /// §2H.1: CONTINUOUS (sub-cell) projection of a world point to anchored map-rect px,
        /// WITHOUT the window-bounds rejection that <see cref="WorldToMapRect"/> applies and
        /// WITHOUT per-cell integer rounding — so the in-disc player marker travels SMOOTHLY as
        /// the player walks, instead of jumping a whole cell (64 m → ~27 px) at a time. The
        /// fractional cell index mirrors vanilla WorldToPixel's pre-round value
        /// (world/pixelSize + textureSize/2). Pins keep the cell-snapped
        /// <see cref="WorldToMapRect"/> projection (they annotate discrete fog cells); only the
        /// in-disc player marker uses this.
        /// </summary>
        private Vector2 WorldToMapRectContinuous(Vector3 world, SurveyData survey)
        {
            if (_mapRect == null) return Vector2.zero;
            float pixelSize = survey.PixelSize > 0f ? survey.PixelSize : 64f;
            int textureSize = survey.TextureSize > 0 ? survey.TextureSize : 256;
            int size = survey.Size;

            // Fractional cell index (no Math.Round — the smooth analogue of WorldToCellX/Y).
            float fcx = survey.OriginX / pixelSize + textureSize / 2f;
            float fcy = survey.OriginZ / pixelSize + textureSize / 2f;
            float fpx = world.x / pixelSize + textureSize / 2f;
            float fpy = world.z / pixelSize + textureSize / 2f;

            float half = (size - 1) / 2f;
            float wx = (fpx - fcx) + half;
            float wy = (fpy - fcy) + half;
            return MapRectAnchor(wx, wy, size);
        }

        // ── Overlay element construction ─────────────────────────────────────────────────

        private void SpawnPinMarker(SurveyPin pin, Vector2 anchored)
        {
            if (_overlayLayer == null) return;
            var go = new GameObject("pin");
            go.transform.SetParent(_overlayLayer.transform, false);
            var img = go.AddComponent<RawImage>();

            // Prefer the marker type's sprite (shared WorldPins model) so Local-Map and
            // Table views read identically; fall back to a colored dot if none.
            Sprite? sprite = ResolvePinSprite(pin);
            if (sprite != null) img.texture = sprite.texture;
            else img.color = PinTint(pin);

            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PinIconPx, PinIconPx);
            rt.anchoredPosition = anchored;
            _pinObjects.Add(go);

            // §2K (issue #11): render the pin's resolved label as a Text CHILD of the pin go.
            // Because §2H.1's CounterRotatePins rotates the whole pin GameObject upright, a child
            // label inherits the upright-cancel, position, and ClearPinObjects lifecycle for
            // free — no new rotation code, CounterRotatePins is untouched. ResolveLabel already
            // fell back to the type label upstream, so a non-blank Name renders that; a genuinely
            // empty Name renders no label (no empty box). raycastTarget off so it never eats the
            // TableEdit pin-removal click.
            if (!string.IsNullOrWhiteSpace(pin.Name))
            {
                var labelGo = new GameObject("pinLabel");
                labelGo.transform.SetParent(go.transform, false);   // child of the pin → rides + counter-rotates with it
                var txt = labelGo.AddComponent<Text>();
                txt.font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                           ?? Resources.GetBuiltinResource<Font>("Arial.ttf");   // SAME font as §2B.1 title / §2F exit prompt
                txt.fontSize = (int)PinLabelFontPx;                  // annotation scale, below the 26/34 prompt/title
                txt.alignment = TextAnchor.UpperCenter;              // sits centred BELOW the icon
                txt.color = new Color(1f, 0.95f, 0.8f, 0.97f);       // parchment-cream, matches title/exit prompt
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Overflow;
                txt.raycastTarget = false;                           // never eat the TableEdit left-click-remove ray
                txt.text = pin.Name;
                var lrt = txt.rectTransform;
                lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
                lrt.pivot = new Vector2(0.5f, 1f);                   // top-centre pivot → grows downward
                lrt.anchoredPosition = new Vector2(0f, -(PinIconPx * 0.5f + 2f)); // just under the icon
                lrt.sizeDelta = new Vector2(160f, 20f);
                // Legibility over the §2E.1 composite (Outline precedent: MarkerSignPanel).
                var outline = labelGo.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }
        }

        /// <summary>
        /// Resolve a pin's sprite from the SHARED WorldPins marker model (impl spec §2B —
        /// consume #100's seam, do NOT fork a second pin model). Matches the stored vanilla
        /// PinType to a marker-type definition's icon; null if no custom icon (→ colored dot).
        /// </summary>
        private static Sprite? ResolvePinSprite(SurveyPin pin)
        {
            try
            {
                foreach (var mk in MarkerSignsType.MarkerTypes)
                {
                    if ((int)mk.VanillaPinType == pin.Type && !string.IsNullOrEmpty(mk.IconFile))
                        return Runtime.Assets.LoadPngAsSprite(mk.IconFile);
                }
            }
            catch { /* fall through to dot */ }
            return null;
        }

        private static Color PinTint(SurveyPin pin)
            => pin.Checked ? new Color(0.5f, 0.8f, 0.5f, 1f) : new Color(0.95f, 0.85f, 0.4f, 1f);

        // ── Player marker / edge arrow ───────────────────────────────────────────────────

        /// <summary>
        /// §2H.1: the player marker rides the rotating interior at its TRUE table-relative
        /// position (M1). Inside the disc it is the in-disc dot; OUTSIDE the disc the dot is
        /// HIDDEN and a single edge arrow, clamped to the disc edge, points OUTWARD toward the
        /// player's real bearing (resolves #3 — the marker never renders beyond the disc). This
        /// is one behaviour for BOTH modes now: the held map and the table view are both
        /// table-centred, so the marker math is identical (the table view simply doesn't rotate
        /// the interior when the player stands at the table ≈ centre — handled in
        /// ApplyFieldOrientation, not here).
        /// </summary>
        private void UpdatePlayerMarker(SurveyData survey, Vector3 origin, float radius)
        {
            UpdatePlayerMarkerTableCentred(survey, origin, radius);
        }

        /// <summary>
        /// Table-centred player marker / edge arrow (§2H.1, resolves #3). The marker sits at the
        /// player's real offset from the bound table and travels as the player walks. Inside the
        /// 1000 m disc it is the in-disc dot; outside, the in-disc dot is HIDDEN and the marker
        /// becomes an outward edge arrow polar-clamped to the disc radius (AT-LMAP-TC-2 /
        /// AT-MAP-EDGEARROW). It lives on the rotating overlay, so its position rides the heading
        /// rotation; its icon is counter-rotated upright by CounterRotatePins.
        /// </summary>
        private void UpdatePlayerMarkerTableCentred(SurveyData survey, Vector3 origin, float radius)
        {
            if (_overlayLayer == null || _mapRect == null) return;
            var player = Player.m_localPlayer;
            if (player == null) { if (_playerMarker != null) _playerMarker.gameObject.SetActive(false); return; }

            EnsurePlayerMarker();
            if (_playerMarker == null) return;
            _playerMarker.gameObject.SetActive(true);

            Vector3 ppos = player.transform.position;
            BoundedMapMath.EdgeClampToDisc(ppos.x, ppos.z, origin.x, origin.z, radius,
                out float cx, out float cz, out float angleDeg, out bool outside, out _);

            var rt = _playerMarker.rectTransform;
            rt.sizeDelta = new Vector2(PlayerMarkerPx, PlayerMarkerPx);

            if (outside)
            {
                // OUTSIDE the disc: hide the in-disc dot semantics and show the edge arrow just
                // INSIDE the disc edge, pointing OUTWARD along the bearing toward the player's
                // real position ("they're that way, past the shroud"). We pull the arrow a few %
                // inside the disc radius so it sits in the bezel's transparent interior, not under
                // the fixed ring band. The clamped point sits on the disc, which the window fully
                // contains, so the projection always succeeds.
                float inset = 0.94f; // fraction of the disc radius → just inside the bezel ring
                float ax = origin.x + (cx - origin.x) * inset;
                float az = origin.z + (cz - origin.z) * inset;
                var clamped = new Vector3(ax, 0f, az);
                if (!WorldToMapRect(clamped, survey, out Vector2 edgeAnchored))
                {
                    _playerMarker.gameObject.SetActive(false);
                    return;
                }
                rt.anchoredPosition = edgeAnchored;
                rt.localRotation = Quaternion.Euler(0, 0, angleDeg);
                _playerMarker.color = new Color(1f, 0.5f, 0.2f, 1f); // off-disc arrow
            }
            else
            {
                // INSIDE: the in-disc dot at the player's true projected position. Use the
                // CONTINUOUS (sub-cell) projection so the marker travels smoothly as the player
                // walks (M1), rather than jumping a whole 64 m cell at a time.
                rt.anchoredPosition = WorldToMapRectContinuous(ppos, survey);
                rt.localRotation = Quaternion.identity;
                _playerMarker.color = new Color(0.4f, 0.7f, 1f, 1f); // in-disc dot
            }
        }

        /// <summary>
        /// §2H.1: show the fixed circular bezel/corner-mask ONLY in FieldReadOnly (the held map
        /// is a circle). In TableEdit the bezel is hidden so the Surveyor's Table keeps its fuller
        /// SQUARE extent for pin-editing visibility (a circular clip would hide edge pins you are
        /// trying to manage). Both modes still rotate-to-heading; only the framing differs.
        /// </summary>
        private void UpdateFrameForMode()
        {
            if (_bezel == null) return;
            bool circular = _req.Mode == MapViewerMode.FieldReadOnly;
            _bezel.gameObject.SetActive(circular);
        }

        /// <summary>
        /// §2H.1 b1/b3/b4: drive the rotating INTERIOR each frame. The interior (cartography +
        /// shroud + pins + marker, all under <see cref="_mapContainer"/>) is TABLE-centred (no
        /// player-pan) and rotates to heading about the table = screen centre; the fixed circular
        /// frame/bezel (<see cref="_frame"/>) NEVER rotates. BOTH modes rotate-to-heading now
        /// (issue #1 fold-in): a north-locked table view was a free North reference and is gone.
        /// Per §2H.1 this runs at frame rate (from Update), not the 0.25 s refresh.
        /// </summary>
        private void ApplyFieldOrientation(SurveyData survey)
        {
            if (_mapContainer == null || _mapRect == null) return;

            // TABLE-centred in both modes: the window is static on the bound origin — NO
            // player-centring pan (resolves #4). _mapRect stays at zero offset; the bound
            // table sits at screen centre by construction (§2B/§2E put BoundOrigin at centre).
            _mapRect.anchoredPosition = Vector2.zero;

            // Heading rotation. Camera yaw is the member vanilla's own marker reads
            // (Utils.GetMainCamera().transform.rotation). If the camera isn't up yet, keep the
            // last orientation rather than throw/blank (graceful degradation). The rotation
            // SENSE is the single build-calibration knob (MapRotationSign). Both FieldReadOnly
            // and TableEdit rotate (issue #1 — no north-up lock anywhere).
            var cam = Utils.GetMainCamera();
            if (cam == null) return;
            float camYaw = cam.transform.eulerAngles.y;
            float rotZ = MapRotationSign * camYaw;
            _mapContainer.localRotation = Quaternion.Euler(0f, 0f, rotZ);

            // Pins (and the player marker) ride the rotation for POSITION; counter-rotate each
            // pin icon so it stays screen-upright (§2H.1 b5 / AT-LMAP-TC-3). The pin LABELS
            // (§2K) are children of the pin GameObjects, so they inherit this counter-rotation.
            CounterRotatePins(rotZ);
        }

        /// <summary>§2H.1 b5: keep pin icons upright while the interior rotates. Each pin rides
        /// the rotating container for POSITION; setting its own localRotation to the negative of
        /// the container's Z cancels the spin so the icon (and its child §2K label) never goes
        /// upside-down. A no-op (identity) when the interior isn't rotated (player standing at
        /// the table in TableEdit ≈ 0 yaw is still a real rotation; this always tracks it).</summary>
        private void CounterRotatePins(float containerZ)
        {
            var counter = Quaternion.Euler(0f, 0f, -containerZ);
            foreach (var go in _pinObjects)
                if (go != null) go.transform.localRotation = counter;
        }

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

        /// <summary>De-dup a pin into a list by (type, ~1 m proximity, name) — same rule as
        /// SurveyData.AddOrUpdatePin, so the snapshot+live union never double-draws a pin.</summary>
        private static void AddIfNew(List<SurveyPin> list, SurveyPin pin)
        {
            const float prox2 = 1.0f * 1.0f;
            foreach (var e in list)
            {
                if (e.Type != pin.Type) continue;
                float dx = e.Pos.x - pin.Pos.x, dz = e.Pos.z - pin.Pos.z;
                if (dx * dx + dz * dz > prox2) continue;
                if (!string.Equals(e.Name ?? "", pin.Name ?? "", StringComparison.Ordinal)) continue;
                return; // already present
            }
            list.Add(pin);
        }

        // ── Input: Escape closes; Table-mode click removes the nearest pin ───────────────

        private void Update()
        {
            if (!IsOpen) return;

            // ESC closes the full-screen view (the fork owns its own close path). The viewer
            // is a standalone uGUI overlay — it never rode vanilla's Minimap M/ESC handling
            // (the OPEN trigger is the Use key while equipped, §2G; the Table opens it via
            // Interact). For the field Local-Map view the controller also closes it on unequip;
            // this is the explicit player dismiss. We do NOT toggle vanilla map mode.
            //
            // NOTE (§2F): this Close() is the half that WORKS. The same Escape also reaches
            // vanilla's Menu.Show gate and opens the pause menu — that leak is suppressed by
            // the Menu.Show prefix inside SignPanelInputBlock (gated on AnyOpen), NOT here.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }

            // TableEdit mode: a left-click on a pin removes it via the pin-removal backend
            // (AT-TABLE-PINEDIT). FieldReadOnly mode never wires this (PinEditor is null), so
            // the field view structurally cannot remove pins.
            if (_req.Mode == MapViewerMode.TableEdit && _req.PinEditor != null && Input.GetMouseButtonDown(0))
                TryRemovePinAtCursor();

            // §2H.1: drive the rotate-to-heading at FRAME RATE (not the 0.25 s survey Refresh —
            // at 4 Hz rotation would visibly stutter). BOTH modes rotate now (issue #1 fold-in):
            // the table view no longer north-locks. Cheap: a camera-yaw read + a transform set;
            // the heavy fog/pin rebuild stays on Refresh.
            {
                var survey = _req.Survey;
                if (survey != null && survey.Fog != null && survey.Size > 0)
                    ApplyFieldOrientation(survey);
            }
        }

        /// <summary>
        /// Map the cursor (screen px) back to a world position via the inverse of
        /// WorldToMapRect, then ask the pin editor to remove the nearest shared pin within a
        /// tolerance. Owner-authoritative persistence + ward re-check happen in the editor
        /// (SurveyorTableTag.RemovePinNear) — the viewer never trusts itself to have gated.
        /// </summary>
        private void TryRemovePinAtCursor()
        {
            var survey = _req.Survey;
            if (survey == null || _mapRect == null || _req.PinEditor == null) return;

            // Cursor → map-rect local point.
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _mapRect, Input.mousePosition, _canvas?.worldCamera, out local))
                return;

            float edge = _mapRect.sizeDelta.x;
            if (Mathf.Abs(local.x) > edge / 2f || Mathf.Abs(local.y) > edge / 2f)
                return; // click outside the map square

            // Inverse of WorldToMapRect: local px → window cell → world.
            float pixelSize = survey.PixelSize > 0f ? survey.PixelSize : 64f;
            int textureSize = survey.TextureSize > 0 ? survey.TextureSize : 256;
            int size = survey.Size;
            float cell = edge / size;

            float wx = (local.x + edge / 2f) / cell - 0.5f;
            float wy = (local.y + edge / 2f) / cell - 0.5f;
            int half = (size - 1) / 2;

            int cx = BoundedMapMath.WorldToCellX(survey.OriginX, pixelSize, textureSize);
            int cy = BoundedMapMath.WorldToCellY(survey.OriginZ, pixelSize, textureSize);
            int px = Mathf.RoundToInt(wx - half) + cx;
            int py = Mathf.RoundToInt(wy - half) + cy;

            float worldX = BoundedMapMath.CellCenterWorldX(px, pixelSize, textureSize);
            float worldZ = BoundedMapMath.CellCenterWorldZ(py, pixelSize, textureSize);
            var worldPos = new Vector3(worldX, 0f, worldZ);

            // Removal tolerance ≈ a couple of cells in world metres (forgiving click target).
            float tol = pixelSize * 2f;
            if (_req.PinEditor.RemovePinNear(worldPos, tol))
            {
                Plugin.Log.LogInfo($"[Trailborne/Cartography] Table-view removed a pin near ({worldX:F0},{worldZ:F0}).");
                // Re-read the now-edited shared survey so the render drops the removed pin
                // (the snapshot we were opened with is stale after the removal).
                var fresh = _req.PinEditor.ReadCurrentSurvey();
                if (fresh != null) _req.Survey = fresh;
                Render();
            }
        }

        // ── Canvas / overlay construction (SignPaintPanel idiom) ─────────────────────────

        /// <summary>
        /// §2F.3: set the exit-prompt text for the current mode. FieldReadOnly shows only the
        /// close hint; TableEdit also surfaces the left-click pin-removal affordance (that mode
        /// is the only one where MapViewer wires the pin-removal click — MapViewer.Update). The
        /// "[Esc]" key is a hardcoded literal (NOT a $KEY_ token — Escape is never a rebindable
        /// ZInput button); the "[Left-click]" verb is likewise a plain literal (UI left-click
        /// isn't a Trailborne-rebound action). Localized so the trailing word tokens (if any)
        /// resolve; the bracketed keys stay literal.
        /// </summary>
        private void UpdateExitPrompt()
        {
            if (_exitPrompt == null) return;
            string raw = _req.Mode == MapViewerMode.TableEdit
                ? "[<color=yellow><b>Esc</b></color>] Close map     [<color=yellow><b>Left-click</b></color>] Remove pin"
                : "[<color=yellow><b>Esc</b></color>] Close map";
            _exitPrompt.text = Localization.instance != null ? Localization.instance.Localize(raw) : raw;
        }

        /// <summary>
        /// §2B.1 (issue 10): show the bound Table's name as a top-centre cartouche while the
        /// view is open. The title comes from _req.Title — TableEdit threads the live Table
        /// name (SurveyorTableTag.GetTableName), FieldReadOnly the imprinted map's name
        /// (LocalMap.TryGetName). Mode-agnostic: one code path for both. An empty/null Title
        /// hides the label entirely (an unnamed Table's view, or a pre-1.6 imprinted map —
        /// AT-TABLENAME-7 no-orphan). Localized so any tokens resolve; a plain place name
        /// passes through unchanged.
        /// </summary>
        private void UpdateTitle()
        {
            if (_titleLabel == null) return;
            string title = _req.Title ?? string.Empty;
            bool show = !string.IsNullOrEmpty(title);
            _titleLabel.gameObject.SetActive(show);
            if (!show) return;
            _titleLabel.text = Localization.instance != null ? Localization.instance.Localize(title) : title;
        }

        private void EnsureCanvas()
        {
            if (_root != null) return;
            EnsureEventSystem();

            _root = new GameObject("SBPR_MapViewerRoot");
            _root.transform.SetParent(transform, false);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5000; // above the HUD, same as SignPaintPanel
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            _root.AddComponent<GraphicRaycaster>();

            // Dim full-screen backdrop so the bounded disc reads as "the whole view".
            var bg = new GameObject("backdrop");
            bg.transform.SetParent(_root.transform, false);
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = CBackdrop;
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;

            // §2H.1 b3: FIXED circular frame (the "box" Daniel wants to stop spinning). A
            // centred, square node sized to the on-screen map that NEVER rotates. It hosts the
            // rotating interior + the fixed bezel ring + the fixed title/exit-prompt. The disc
            // is realized by the shroud-opaque corners + the bezel ring on top, not a literal
            // shader clip (the inscribed-circle geometry guarantees rotating the interior never
            // uncovers an empty corner — §2H.1 b3).
            var frameGo = new GameObject("frame");
            frameGo.transform.SetParent(_root.transform, false);
            _frame = frameGo.AddComponent<RectTransform>();
            _frame.anchorMin = _frame.anchorMax = new Vector2(0.5f, 0.5f);
            _frame.pivot = new Vector2(0.5f, 0.5f);
            _frame.sizeDelta = new Vector2(MaxFullViewPx, MaxFullViewPx);

            // §2H.1 b3: the ROTATING INTERIOR. A centred, zero-size pivot node under the fixed
            // frame. The cartography + shroud + pins + player marker are its children, so
            // rotating IT rotates the whole interior about screen-centre = the TABLE pivot.
            // BOTH modes rotate to heading (issue #1). The frame around it does NOT rotate.
            var containerGo = new GameObject("mapContainer");
            containerGo.transform.SetParent(_frame.transform, false);
            _mapContainer = containerGo.AddComponent<RectTransform>();
            _mapContainer.anchorMin = _mapContainer.anchorMax = new Vector2(0.5f, 0.5f);
            _mapContainer.pivot = new Vector2(0.5f, 0.5f);
            _mapContainer.sizeDelta = Vector2.zero;

            // The cartography layer: a RawImage that shows the §2E.1 CPU-composited cartography
            // texture (biome/water/relief) — or the 2-color fallback texture. No boxy frame
            // behind it (AT-ISSUE1-BORDER): the bounded disc + shroud ring IS the edge. Parented
            // to the rotating container; TABLE-centred (zero offset — §2H.1 removes the pan).
            var mapGo = new GameObject("cartography");
            mapGo.transform.SetParent(_mapContainer.transform, false);
            _mapImage = mapGo.AddComponent<RawImage>();
            _mapRect = _mapImage.rectTransform;
            _mapRect.anchorMin = _mapRect.anchorMax = new Vector2(0.5f, 0.5f);
            _mapRect.pivot = new Vector2(0.5f, 0.5f);
            _mapRect.sizeDelta = new Vector2(MaxFullViewPx, MaxFullViewPx);

            // The shroud mask: a RawImage stretched over the cartography (child → renders above
            // it). Its texture is OUR survey fog as an alpha mask (lit→transparent, else→opaque
            // shroud), so the 1000 m disc + unexplored cells read as shroud OVER the real map
            // (§2E.1 — the one deliberate difference from vanilla). The shroud-opaque corners
            // are what make the rotating square read as a disc (no empty corners).
            var shroudGo = new GameObject("shroudMask");
            shroudGo.transform.SetParent(mapGo.transform, false);
            _shroudImage = shroudGo.AddComponent<RawImage>();
            _shroudImage.raycastTarget = false; // never intercept the Table-mode pin clicks
            var shRt = _shroudImage.rectTransform;
            shRt.anchorMin = Vector2.zero; shRt.anchorMax = Vector2.one; // stretch to the cartography rect
            shRt.offsetMin = Vector2.zero; shRt.offsetMax = Vector2.zero;

            // Overlay layer (pins + player marker) sits on top of BOTH the cartography and the
            // shroud (added last → highest child). It rides the cartography rigidly, so pins +
            // marker stay world-anchored under rotation (AT-LMAP-TC-3). Same rect + center.
            _overlayLayer = new GameObject("overlay");
            _overlayLayer.transform.SetParent(mapGo.transform, false);
            var ovRt = _overlayLayer.AddComponent<RectTransform>();
            ovRt.anchorMin = ovRt.anchorMax = new Vector2(0.5f, 0.5f);
            ovRt.pivot = new Vector2(0.5f, 0.5f);
            ovRt.sizeDelta = Vector2.zero; // children anchor to center, position in px

            // §2H.1 b3 (issue 6 edge-bleed fix): the FIXED circular bezel — a HARD circular alpha
            // cover drawn OVER the rotating interior but as a child of the non-rotating frame (so it
            // never spins — the #2 fix). It is sized LARGER than the map square — to MaxFullViewPx*√2
            // — so that when the interior rotates, the square's corners (which extend to half-edge*√2
            // from centre) are fully covered by the bezel's opaque shroud and never read as a spinning
            // diamond over the backdrop. The visible disc is the inscribed circle of the square map,
            // inset a few px so the square's straight tangents stay under the opaque cover (no
            // parchment bleed). EnsureBezelTexture takes the on-screen bezel edge so its inset/ring
            // widths are exact SCREEN px. raycastTarget off so it never eats the TableEdit pin click.
            float bezelEdge = MaxFullViewPx * 1.41421356f; // √2 → covers the rotated square's corners
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

            // §2F.3 exit prompt: a bottom-centre instructional label parented to _root (so it
            // toggles with the overlay). Literal "[Esc]" — NOT a $KEY_ token: Escape is a
            // hardcoded KeyCode.Escape in vanilla, never a rebindable ZInput button, so a
            // $KEY_ token would leak as an unresolved literal (the 2026-06-05 sign-bug lesson).
            // Mode-aware text is set in Render() ([Left-click] Remove pin in TableEdit only).
            // Wears the shared VanillaUISkin.Font, degrading to Arial like the sign panels.
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
            prRt.anchoredPosition = new Vector2(0f, 40f); // bottom-centre
            prRt.sizeDelta = new Vector2(1200f, 48f);

            // §2B.1 title cartouche (issue 10): a TOP-centre label parented to _root (toggles
            // with the overlay). HARD PLACEMENT CONTRACT with the §2F exit prompt: title =
            // TOP-centre, exit prompt = BOTTOM-centre — they never collide. Wears the same
            // shared VanillaUISkin.Font + parchment-cream tint as the exit prompt so it reads
            // as a map cartouche. Set per-Render from _req.Title (hidden when empty). Slightly
            // larger + bold so it reads as a heading above the bounded map square.
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
            tlRt.anchoredPosition = new Vector2(0f, -40f); // top-centre
            tlRt.sizeDelta = new Vector2(1200f, 52f);
            _titleLabel.gameObject.SetActive(false); // shown only when _req.Title is non-empty

            _root.SetActive(false);
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("SBPR_MapViewerEventSystem");
            UnityEngine.Object.DontDestroyOnLoad(es);
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        private void OnDestroy()
        {
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            if (_shroudTex != null) UnityEngine.Object.Destroy(_shroudTex);
            if (_shaderMat != null) UnityEngine.Object.Destroy(_shaderMat);
            if (_bezelTex != null) UnityEngine.Object.Destroy(_bezelTex);
            if (_instance == this) _instance = null;
        }
    }
}
