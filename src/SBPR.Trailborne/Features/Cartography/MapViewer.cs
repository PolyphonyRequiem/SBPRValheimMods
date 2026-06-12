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
        private const float PlayerMarkerPx  = 26f;

        // ── §2H free-rotate (held Local Map / FieldReadOnly only) ─────────────────────
        // The held map free-rotates with the player's heading (forward = up), is centred on
        // the PLAYER (not the bound Table), and pins a static square marker at dead centre
        // (§2H P2 "player-centred minimap"). The Surveyor's Table (TableEdit) view is
        // unaffected — it stays north-up + table-centred (§2H "Table view").
        //
        // 🔴 BUILD-CALIBRATED SIGN (§2H bullet 2). The exact rotation sense + whether
        // camera-yaw or body-yaw reads better cannot be confirmed on a headless build worker
        // (no GPU, no live camera) — the same discipline that locked m_pixelSize in the
        // UI-fork spike. Vanilla rotates its NORTH-UP marker by -cameraYaw
        // (UpdatePlayerMarker: m_smallMarker.rotation = Euler(0,0,-eulerAngles.y)); rotating
        // the MAP so forward = up is the opposite sense, so the first guess is +cameraYaw.
        // If the map turns the wrong way in-game, flip this one constant to -1f. This is the
        // single calibration knob Daniel's playtest tunes (Option A, 2026-06-11).
        private const float MapRotationSign = 1f;

        // ── §2E vanilla-cartography render constants ──────────────────────────────────
        // Shader uniform names on vanilla's map material (Minimap.Start :46916-46919,
        // CenterMap :47506-47514 — decomp-verified). We reuse a COPY of that material.
        private const string MainTexProp   = "_MainTex";   // RGB24 biome base color
        private const string FogTexProp    = "_FogTex";    // R8G8 explored(R)/shared(G) mask
        private const string ZoomProp      = "_zoom";
        private const string PixelSizeProp = "_pixelSize"; // SHADER uniform (= 200f/zoom), NOT m_pixelSize
        private const string MapCenterProp = "_mapCenter"; // world-space view centre
        private const string SharedFadeProp = "_SharedFade";

        // Palette — our own dark-Norse shroud (clean-room; no vanilla sprite copy).
        // CParchment/CShroud are the 2-color FALLBACK palette (graceful degradation, §2E);
        // CShroudA is the opaque shroud used to mask OVER the real vanilla cartography.
        private static readonly Color32 CParchment = new Color32(214, 198, 162, 255); // fallback: explored & in-disc
        private static readonly Color32 CShroud    = new Color32(14, 13, 11, 255);    // fallback: outside disc / unexplored
        private static readonly Color32 CShroudA   = new Color32(10, 9, 8, 255);       // mask shroud (opaque over cartography)
        private static readonly Color32 CClear     = new Color32(0, 0, 0, 0);          // mask lit cell (show cartography through)
        private static readonly Color   CBackdrop  = new Color(0f, 0f, 0f, 0.92f);

        // ── UI refs ───────────────────────────────────────────────────────────────────
        private GameObject? _root;          // the whole overlay (toggled active)
        private Canvas? _canvas;
        private RectTransform? _mapContainer; // §2H: rotates by heading in FieldReadOnly (identity in TableEdit)
        private RawImage? _mapImage;        // the CARTOGRAPHY layer (vanilla map material copy; 2-color fallback)
        private RawImage? _shroudImage;     // the disc+survey shroud mask layered OVER the cartography
        private RectTransform? _mapRect;    // the map square (pins/markers parent to this)
        private GameObject? _overlayLayer;  // pins + player marker + edge arrow live here
        private GameObject? _staticOverlay;  // §2H: never-rotated layer hosting the centred player marker
        private Texture2D? _tex;            // fallback 2-color fog texture
        private Texture2D? _shroudTex;      // the alpha shroud-mask texture (lit→clear, shroud→opaque)
        private Texture2D? _revealTex;      // 1×1 force-explored override for the copy's _FogTex
        private Material?  _mapMaterial;    // INSTANTIATED COPY of vanilla's map material (never vanilla's live one)
        private RawImage? _playerMarker;    // reused player dot/arrow
        private RawImage? _tableArrow;      // §2H field-mode: arrow toward the bound Table when player is off-disc
        private readonly List<GameObject> _pinObjects = new List<GameObject>();

        // ── Live request state ──────────────────────────────────────────────────────────
        private MapViewRequest _req;
        private bool _open;
        public bool IsOpen => _open;
        public MapViewerMode CurrentMode => _req.Mode;

        // ── IMapViewer ──────────────────────────────────────────────────────────────────

        public void Open(MapViewRequest request)
        {
            _req = request;
            EnsureCanvas();
            _root!.SetActive(true);
            _open = true;
            Render();
        }

        public void Refresh(MapViewRequest request)
        {
            if (!_open) return;
            _req = request;
            Render();
        }

        public void Close()
        {
            if (_root != null) _root.SetActive(false);
            _open = false;
        }

        // ── Render: §2E vanilla-cartography (material copy) + shroud mask + overlay ───────

        private void Render()
        {
            var survey = _req.Survey;
            if (survey == null || survey.Fog == null || survey.Size <= 0)
            {
                Plugin.Log.LogWarning("[Trailborne/Cartography] MapViewer.Render: empty/blank survey; nothing to draw.");
                return;
            }

            LayoutMapRect(survey.Size);

            // §2E LOCKED ROUTE: render the SAME cartography as the vanilla map by reusing a
            // COPY of vanilla's map material (biome/height/forest/water live in its shader +
            // four bound textures), framed to the bound origin's disc, with OUR fog window as
            // the shroud mask on top. Falls back to the legacy 2-color paint if the material
            // can't be obtained (e.g. Minimap not yet generated). PaintFog is retained as the
            // mandatory graceful-degradation path (§2E "Graceful degradation").
            bool carto = TryRenderVanillaCartography(survey);
            if (!carto)
                PaintFog(survey); // graceful degradation → the old explored/shroud two-color fill

            RebuildOverlay(survey);

            // §2H: in FieldReadOnly, centre the view on the player + rotate to heading. In
            // TableEdit this is a no-op (identity rotation, zero offset) → unchanged north-up
            // table-centred view. Applied on every Render AND every frame from Update() (so
            // rotation tracks heading at frame rate, not the 0.25 s survey refresh — §2H b6).
            ApplyFieldOrientation(survey);
        }

        /// <summary>
        /// §2E primary render. Instantiate a COPY of vanilla's map material
        /// (<c>Minimap.instance.m_mapImageLarge.material</c>), which carries the four bound
        /// cartography textures (_MainTex biome / _MaskTex forest+water / _HeightTex relief /
        /// _FogTex explored) + the compositing shader. Drive the copy's _mapCenter / _zoom /
        /// _pixelSize + the RawImage.uvRect to frame the bound origin's disc at our single
        /// fixed scale, then overlay OUR survey fog as the shroud mask. We never touch
        /// vanilla's live material or roots → nomap stays in force (AT-TABLEMAP-6).
        ///
        /// Returns false (→ caller falls back to PaintFog) if the vanilla material/textures
        /// aren't available yet. The exact uvRect↔_mapCenter/_pixelSize sampling is a GPU
        /// shader behavior that can't be confirmed from the C# decomp — the calibration
        /// constants below are decomp-derived (vanilla CenterMap :47506-47514 + WorldToMapPoint
        /// :47977) and MUST be verified/tuned in-client per §2E's mandatory micro-spike.
        /// </summary>
        private bool TryRenderVanillaCartography(SurveyData survey)
        {
            // Graceful degradation guards (§2E): no Minimap singleton, no large map image, or
            // generation hasn't run yet (no _MainTex) → bail to the 2-color fallback.
            var mm = Minimap.instance;
            if (mm == null || mm.m_mapImageLarge == null || mm.m_mapImageLarge.material == null)
                return false;

            float pixelSize  = survey.PixelSize > 0f ? survey.PixelSize : 64f;   // world m per source cell
            int   textureSize = survey.TextureSize > 0 ? survey.TextureSize : 256;
            Vector3 origin   = _req.BoundOrigin;

            // 1) Instantiate (once) a COPY of the vanilla map material. The copy inherits the
            //    four texture bindings by reference (Minimap.Start :46916-46919) + the shader.
            if (_mapMaterial == null)
            {
                try
                {
                    _mapMaterial = UnityEngine.Object.Instantiate(mm.m_mapImageLarge.material);
                    _mapMaterial.name = "SBPR_BoundedMapMaterial(Clone)";
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Trailborne/Cartography] MapViewer: could not copy vanilla map material ({e.Message}); falling back to 2-color paint.");
                    _mapMaterial = null;
                    return false;
                }
            }

            // The cartography main texture must be valid for the RawImage to draw; if the
            // vanilla map hasn't generated it yet, fall back rather than render blank (§2E).
            var mainTex = _mapMaterial.GetTexture(MainTexProp);
            if (mainTex == null)
                return false;

            // 2) Override the copy's _FogTex with a reveal-all (1×1 white R=G=1) texture so the
            //    cartography shows for the WHOLE framed window — vanilla's live _FogTex reflects
            //    the local player's real-time exploration, NOT our survey. OUR survey shroud is
            //    applied as a separate mask in step 4 (§2E bullet 3: shroud = our fog, not _FogTex).
            EnsureRevealTexture();
            if (_revealTex != null) _mapMaterial.SetTexture(FogTexProp, _revealTex);
            _mapMaterial.SetFloat(SharedFadeProp, 1f); // show shared/full cartography, no fade

            // 3) Frame EXACTLY the fog window at our single fixed scale. The window is the
            //    same WindowSpec the fog + pins were built from (BoundedMapMath): cells
            //    [cx-CellRadius .. cx+CellRadius], Size = 2*CellRadius+1. Framing the cartography
            //    to the identical window means cartography, shroud mask, and pin overlay all
            //    share ONE window definition → aligned by construction. The 1000 m disc sits
            //    inside this slightly-over-provisioned window (e.g. 33 cells × 64 m = 2112 m for
            //    a 2000 m disc), so a thin shroud ring frames the disc naturally (no boxy border).
            //
            //    uvRect is normalized over the full world texture. A source cell index c maps to
            //    uv = c / textureSize (vanilla WorldToMapPoint :47977: mx = x/pixelSize +
            //    textureSize/2 = cell index, then /textureSize). zoom = window uv span = Size/
            //    textureSize (vanilla CenterMap: uvRect.width = zoom; _pixelSize = 200f/zoom).
            int size = survey.Size;
            int cx = BoundedMapMath.WorldToCellX(survey.OriginX, pixelSize, textureSize);
            int cy = BoundedMapMath.WorldToCellY(survey.OriginZ, pixelSize, textureSize);
            float zoom = Mathf.Clamp((float)size / textureSize, 0.001f, 1f); // normalized uv span of the window
            // uv centre = the window's centre cell (+0.5 → cell centre) / textureSize.
            float mcx = (cx + 0.5f) / textureSize;
            float mcy = (cy + 0.5f) / textureSize;

            if (_mapImage != null)
            {
                _mapImage.material = _mapMaterial;
                _mapImage.texture  = mainTex;                       // valid main texture for the RawImage
                _mapImage.color    = Color.white;
                var uv = new Rect { width = zoom, height = zoom };
                uv.center = new Vector2(mcx, mcy);
                _mapImage.uvRect = uv;
            }

            // Drive the shader uniforms on OUR copy (vanilla CenterMap :47506-47514). _mapCenter
            // is the bound origin (world); _pixelSize = 200/zoom and _zoom = zoom mirror vanilla.
            _mapMaterial.SetFloat(ZoomProp, zoom);
            _mapMaterial.SetFloat(PixelSizeProp, 200f / Mathf.Max(zoom, 0.0001f));
            _mapMaterial.SetVector(MapCenterProp, new Vector4(origin.x, origin.z, 0f, 0f));

            // 4) Shroud mask = OUR survey fog (explored-AND-in-disc), opaque everywhere else.
            //    Layered as a second RawImage exactly over the cartography rect, so the disc is
            //    the natural edge (no boxy frame — AT-ISSUE1-BORDER) and beyond-radius reads as
            //    shroud (AT-TABLEMAP-2). Aligned to the same WindowSpec the fog was built from.
            PaintShroudMask(survey);

            return true;
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

        /// <summary>A 1×1 R=G=1 texture bound as _FogTex on the copy to force "fully explored"
        /// for the framed window (we shroud with our OWN fog mask instead — §2E bullet 3).</summary>
        private void EnsureRevealTexture()
        {
            if (_revealTex != null) return;
            _revealTex = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false)
            {
                name = "SBPR_RevealFog",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            // R = personal-explored, G = shared-explored (Minimap.Explore :1555-1566 writes R/G).
            _revealTex.SetPixels32(new[] { new Color32(255, 255, 0, 255) });
            _revealTex.Apply(updateMipmaps: false);
        }

        /// <summary>
        /// FALLBACK render (§2E graceful degradation). Paint the Size×Size bool fog window into
        /// our own RGBA32 two-color texture. Used only when the vanilla map material/textures
        /// are unavailable (Minimap not generated yet). Retained per §2E ("Keep PaintFog").
        /// </summary>
        private void PaintFog(SurveyData survey)
        {
            // Detach the cartography material so the fallback two-color texture is what shows.
            if (_mapImage != null) _mapImage.material = null;
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

        /// <summary>
        /// §2H: the anchored map-rect position of a world point WITHOUT the window-bounds
        /// rejection that <see cref="WorldToMapRect"/> applies. The projection is linear
        /// (cell deltas × on-screen cell size), so it extrapolates cleanly past the window
        /// edge — needed for player-centring when the held map's player has walked beyond the
        /// table-centred window (the table arrow then points back). Returns the same value as
        /// WorldToMapRect for in-window points.
        /// </summary>
        private Vector2 WorldToMapRectUnclamped(Vector3 world, SurveyData survey)
        {
            if (_mapRect == null) return Vector2.zero;
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
            return MapRectAnchor(wx, wy, size);
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

        private void UpdatePlayerMarker(SurveyData survey, Vector3 origin, float radius)
        {
            // §2H: the held Local Map (FieldReadOnly) is player-centred — the marker is a
            // STATIC square at dead centre on the never-rotated static overlay, and the world
            // rotates underneath it (Daniel-locked: no facing indicator). The Surveyor's Table
            // (TableEdit) keeps the original table-centred behaviour: the marker rides the
            // (un-rotated, un-offset) overlay at the player's projected offset from the table,
            // edge-clamped to the disc when the player is outside it.
            if (_req.Mode == MapViewerMode.FieldReadOnly)
            {
                UpdatePlayerMarkerFieldCentred();
                return;
            }
            UpdatePlayerMarkerTableCentred(survey, origin, radius);
        }

        /// <summary>
        /// §2H FieldReadOnly: player marker is a static featureless square at dead centre of
        /// the static (never-rotated) overlay. Forward = up is carried by the rotating world
        /// beneath it (AT-LMAP-ROT-2). No edge arrow here — the off-disc indicator points at
        /// the TABLE and rides the rotating overlay (UpdateTableArrow), per §2H bullet 5.
        /// </summary>
        private void UpdatePlayerMarkerFieldCentred()
        {
            EnsurePlayerMarker(staticLayer: true);
            if (_playerMarker == null) return;
            var player = Player.m_localPlayer;
            if (player == null) { _playerMarker.gameObject.SetActive(false); return; }

            _playerMarker.gameObject.SetActive(true);
            var rt = _playerMarker.rectTransform;
            rt.anchoredPosition = Vector2.zero;          // dead centre
            rt.localRotation = Quaternion.identity;       // static square, no facing (Daniel)
            rt.sizeDelta = new Vector2(PlayerMarkerPx, PlayerMarkerPx);
            _playerMarker.color = new Color(0.4f, 0.7f, 1f, 1f);
        }

        /// <summary>
        /// Original TableEdit player marker / edge arrow (unchanged behaviour). The marker
        /// shows at its real map position when inside the disc, or — when outside — as a
        /// direction arrow polar-clamped to the shroud radius (AT-MAP-EDGEARROW).
        /// </summary>
        private void UpdatePlayerMarkerTableCentred(SurveyData survey, Vector3 origin, float radius)
        {
            if (_overlayLayer == null || _mapRect == null) return;
            var player = Player.m_localPlayer;
            if (player == null) { if (_playerMarker != null) _playerMarker.gameObject.SetActive(false); return; }

            EnsurePlayerMarker(staticLayer: false);
            if (_playerMarker == null) return;
            _playerMarker.gameObject.SetActive(true);

            Vector3 ppos = player.transform.position;
            BoundedMapMath.EdgeClampToDisc(ppos.x, ppos.z, origin.x, origin.z, radius,
                out float cx, out float cz, out float angleDeg, out bool outside, out _);

            // Project the (clamped if outside) point onto the map rect.
            var projected = new Vector3(cx, 0f, cz);
            if (!WorldToMapRect(projected, survey, out Vector2 anchored))
            {
                // Off-grid even after clamp (shouldn't happen — clamp keeps it on the disc,
                // which the window fully contains): hide rather than draw garbage.
                _playerMarker.gameObject.SetActive(false);
                return;
            }

            var rt = _playerMarker.rectTransform;
            rt.anchoredPosition = anchored;
            rt.sizeDelta = new Vector2(PlayerMarkerPx, PlayerMarkerPx);

            if (outside)
            {
                // Edge arrow: point FROM the clamped disc point toward... the player is OUT,
                // so the arrow points outward along the bearing (toward the player's real
                // position) — i.e. "they're that way past the shroud". angleDeg is the bearing
                // origin→player (0°=+X/east, CCW). Map +Y = north(+Z), so screen rotation =
                // angleDeg measured from +X. Tint it distinctly so it reads as "off-map".
                rt.localRotation = Quaternion.Euler(0, 0, angleDeg);
                _playerMarker.color = new Color(1f, 0.5f, 0.2f, 1f); // off-disc arrow
            }
            else
            {
                rt.localRotation = Quaternion.identity;
                _playerMarker.color = new Color(0.4f, 0.7f, 1f, 1f); // in-disc dot
            }
        }

        /// <summary>
        /// §2H bullet 1+2: drive the FieldReadOnly view's player-centring offset + heading
        /// rotation each frame. The cartography/shroud/pins ride <see cref="_mapRect"/> inside
        /// <see cref="_mapContainer"/>; offsetting the map by -playerAnchor puts the PLAYER at
        /// the container origin (= screen centre = rotation pivot), and rotating the container
        /// by the camera yaw turns the world so forward = up. TableEdit resets both to identity
        /// (north-up, table-centred) so its view is byte-for-byte the pre-§2H behaviour.
        /// Per §2H bullet 6 this runs at frame rate (from Update), not the 0.25 s refresh.
        /// </summary>
        private void ApplyFieldOrientation(SurveyData survey)
        {
            if (_mapContainer == null || _mapRect == null) return;

            if (_req.Mode != MapViewerMode.FieldReadOnly)
            {
                // Table view: no rotation, no player-centring offset (unchanged).
                _mapContainer.localRotation = Quaternion.identity;
                _mapRect.anchoredPosition = Vector2.zero;
                return;
            }

            var player = Player.m_localPlayer;
            if (player == null) return;

            // 1) Player-centring offset: shift the map so the player's projected anchor lands
            //    at the container origin. The projection is unclamped (the player may be far
            //    from the table → beyond the window), so it extrapolates linearly.
            Vector3 ppos = player.transform.position;
            Vector2 playerAnchor = WorldToMapRectUnclamped(ppos, survey);
            _mapRect.anchoredPosition = -playerAnchor;

            // 2) Heading rotation. Camera yaw is the member vanilla's own marker reads
            //    (UpdatePlayerMarker: Utils.GetMainCamera().transform.rotation). If the camera
            //    isn't up yet, keep the last orientation rather than throw/blank (§2H "Graceful
            //    degradation"). The rotation SENSE is build-calibrated (MapRotationSign).
            var cam = Utils.GetMainCamera();
            if (cam == null) return;
            float camYaw = cam.transform.eulerAngles.y;
            float rotZ = MapRotationSign * camYaw;
            _mapContainer.localRotation = Quaternion.Euler(0f, 0f, rotZ);

            // 3) Pins ride the rotation for POSITION (they're under the container), but
            //    counter-rotate each icon so it stays screen-upright (§2H bullet 4 / AT-LMAP-ROT-3).
            CounterRotatePins(rotZ);

            // 4) Off-disc indicator: when the player is outside the 1000 m disc the TABLE is
            //    the off-screen target — show an arrow toward it at the view edge (§2H b5 /
            //    AT-LMAP-ROT-4). Lives under the rotating container, so its bearing composes
            //    with the container rotation automatically.
            UpdateTableArrow(survey, ppos);
        }

        /// <summary>§2H bullet 4: keep pin icons upright while the map rotates. Each pin rides
        /// the rotating container for POSITION; setting its own localRotation to the negative
        /// of the container's Z cancels the spin so the icon never goes upside-down. A no-op
        /// (identity) in TableEdit where rotZ is 0.</summary>
        private void CounterRotatePins(float containerZ)
        {
            var counter = Quaternion.Euler(0f, 0f, -containerZ);
            foreach (var go in _pinObjects)
                if (go != null) go.transform.localRotation = counter;
        }

        /// <summary>
        /// §2H bullet 5 (FieldReadOnly): when the player is outside the bound 1000 m disc, the
        /// player sits at screen-centre and the bound Table is off-view — show a direction
        /// arrow at the view edge pointing toward the Table. Built lazily under the rotating
        /// container so the bearing composes with the heading rotation (AT-LMAP-ROT-4). Hidden
        /// when the player is inside the disc.
        /// </summary>
        private void UpdateTableArrow(SurveyData survey, Vector3 playerPos)
        {
            if (_mapContainer == null || _mapRect == null) return;

            Vector3 origin = _req.BoundOrigin;
            float radius = _req.RadiusMeters > 0f ? _req.RadiusMeters : survey.RadiusMeters;
            float dx = playerPos.x - origin.x, dz = playerPos.z - origin.z;
            bool outside = (dx * dx + dz * dz) > radius * radius;

            if (!outside)
            {
                if (_tableArrow != null) _tableArrow.gameObject.SetActive(false);
                return;
            }

            EnsureTableArrow();
            if (_tableArrow == null) return;

            // Direction player→table in the UNROTATED map-pixel frame (the container rotation
            // is applied by the parent, so we work pre-rotation here).
            Vector2 playerAnchor = WorldToMapRectUnclamped(playerPos, survey);
            Vector2 tableAnchor  = WorldToMapRectUnclamped(origin, survey);
            Vector2 toTable = tableAnchor - playerAnchor;
            if (toTable.sqrMagnitude < 1e-4f) { _tableArrow.gameObject.SetActive(false); return; }
            Vector2 dir = toTable.normalized;

            // Clamp to a ring just inside the view square (player at centre).
            float edgeRadius = _mapRect.sizeDelta.x * 0.45f;
            var rt = _tableArrow.rectTransform;
            rt.anchoredPosition = dir * edgeRadius;
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg; // sprite points +X by default
            rt.localRotation = Quaternion.Euler(0f, 0f, ang);
            _tableArrow.gameObject.SetActive(true);
        }

        private void EnsureTableArrow()
        {
            if (_tableArrow != null || _mapContainer == null) return;
            var go = new GameObject("tableArrow");
            go.transform.SetParent(_mapContainer.transform, false);
            _tableArrow = go.AddComponent<RawImage>();
            _tableArrow.raycastTarget = false;
            _tableArrow.color = new Color(1f, 0.5f, 0.2f, 1f); // off-disc / "table that way"
            var rt = _tableArrow.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PlayerMarkerPx, PlayerMarkerPx);
        }

        private void EnsurePlayerMarker(bool staticLayer)
        {
            Transform? parent = staticLayer ? _staticOverlay?.transform : _overlayLayer?.transform;
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
            else if (_playerMarker.transform.parent != parent)
            {
                // Mode changed (field ↔ table): move the marker to the right layer. The static
                // layer never rotates (field centre marker); the overlay layer rides the map
                // (table offset marker).
                _playerMarker.transform.SetParent(parent, false);
                _playerMarker.rectTransform.anchoredPosition = Vector2.zero;
                _playerMarker.rectTransform.localRotation = Quaternion.identity;
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
            if (!_open) return;

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

            // §2H: drive the held-map free-rotate + player-centring at FRAME RATE (not the
            // 0.25 s survey Refresh — at 4 Hz rotation would visibly stutter, §2H bullet 6).
            // No-op in TableEdit (identity rotation, zero offset). Cheap: a camera-yaw read +
            // a transform set; the heavy fog/pin rebuild stays on Refresh.
            if (_req.Mode == MapViewerMode.FieldReadOnly)
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

            // §2H rotation container: a centred, zero-size pivot node. The cartography +
            // shroud + pins (the "world" layer) are its children, so rotating IT rotates the
            // whole map about screen-centre. In FieldReadOnly it spins with the player's
            // heading (forward = up); in TableEdit it stays identity (north-up). Because its
            // pivot is screen-centre and the field offset puts the PLAYER at screen-centre,
            // rotation is about the player (AT-LMAP-ROT-1/2).
            var containerGo = new GameObject("mapContainer");
            containerGo.transform.SetParent(_root.transform, false);
            _mapContainer = containerGo.AddComponent<RectTransform>();
            _mapContainer.anchorMin = _mapContainer.anchorMax = new Vector2(0.5f, 0.5f);
            _mapContainer.pivot = new Vector2(0.5f, 0.5f);
            _mapContainer.sizeDelta = Vector2.zero;

            // The cartography layer: a RawImage that shows a COPY of the vanilla map material
            // (§2E) — biome/height/forest/water — or the 2-color fallback texture. No boxy
            // frame behind it (AT-ISSUE1-BORDER): the bounded disc + shroud ring IS the edge,
            // matching the vanilla map's soft framing rather than a hard SBPR rectangle.
            // Parented to the rotation container; its anchoredPosition is the §2H player-
            // centring offset (0 in TableEdit → table-centred; -playerAnchor in FieldReadOnly
            // → player at centre).
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
            // (§2E bullet 3 — the one deliberate difference from vanilla).
            var shroudGo = new GameObject("shroudMask");
            shroudGo.transform.SetParent(mapGo.transform, false);
            _shroudImage = shroudGo.AddComponent<RawImage>();
            _shroudImage.raycastTarget = false; // never intercept the Table-mode pin clicks
            var shRt = _shroudImage.rectTransform;
            shRt.anchorMin = Vector2.zero; shRt.anchorMax = Vector2.one; // stretch to the cartography rect
            shRt.offsetMin = Vector2.zero; shRt.offsetMax = Vector2.zero;

            // Overlay layer (pins + the field-mode table arrow) sits on top of BOTH the
            // cartography and the shroud (added last → highest child). It rides the cartography
            // rigidly, so pins stay world-anchored under rotation + the player-centring offset
            // (AT-LMAP-ROT-3). Same rect + center as the map.
            _overlayLayer = new GameObject("overlay");
            _overlayLayer.transform.SetParent(mapGo.transform, false);
            var ovRt = _overlayLayer.AddComponent<RectTransform>();
            ovRt.anchorMin = ovRt.anchorMax = new Vector2(0.5f, 0.5f);
            ovRt.pivot = new Vector2(0.5f, 0.5f);
            ovRt.sizeDelta = Vector2.zero; // children anchor to center, position in px

            // §2H static overlay: a centred, zero-size, NEVER-rotated node that hosts the
            // player marker. In FieldReadOnly the player marker is a static square pinned at
            // dead centre while the world rotates underneath (Daniel-locked: no facing
            // indicator). In TableEdit the player marker is projected to its real offset from
            // the table here (the static layer == screen-centre == table-centre when the world
            // layer has zero offset + identity rotation, so TableEdit is unchanged).
            var staticGo = new GameObject("staticOverlay");
            staticGo.transform.SetParent(_root.transform, false);
            var stRt = staticGo.AddComponent<RectTransform>();
            stRt.anchorMin = stRt.anchorMax = new Vector2(0.5f, 0.5f);
            stRt.pivot = new Vector2(0.5f, 0.5f);
            stRt.sizeDelta = Vector2.zero;
            _staticOverlay = staticGo;

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
            if (_revealTex != null) UnityEngine.Object.Destroy(_revealTex);
            // Destroy OUR instantiated material copy (never vanilla's live material).
            if (_mapMaterial != null) UnityEngine.Object.Destroy(_mapMaterial);
            if (_instance == this) _instance = null;
        }
    }
}
