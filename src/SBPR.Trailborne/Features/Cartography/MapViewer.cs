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

        // Palette — our own dark-Norse parchment/shroud (clean-room; no vanilla sprite copy).
        private static readonly Color32 CParchment = new Color32(214, 198, 162, 255); // explored & in-disc
        private static readonly Color32 CShroud    = new Color32(14, 13, 11, 255);    // outside disc / unexplored
        private static readonly Color   CBackdrop  = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Color   CFrame     = new Color(0.45f, 0.36f, 0.22f, 1f);

        // ── UI refs ───────────────────────────────────────────────────────────────────
        private GameObject? _root;          // the whole overlay (toggled active)
        private Canvas? _canvas;
        private RawImage? _mapImage;        // the painted fog texture
        private RectTransform? _mapRect;    // the map square (pins/markers parent to this)
        private GameObject? _overlayLayer;  // pins + player marker + edge arrow live here
        private Texture2D? _tex;
        private RawImage? _playerMarker;    // reused player dot/arrow
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

        // ── Render: paint fog texture + overlay pins/markers ─────────────────────────────

        private void Render()
        {
            var survey = _req.Survey;
            if (survey == null || survey.Fog == null || survey.Size <= 0)
            {
                Plugin.Log.LogWarning("[Trailborne/Cartography] MapViewer.Render: empty/blank survey; nothing to draw.");
                return;
            }

            PaintFog(survey);
            LayoutMapRect(survey.Size);
            RebuildOverlay(survey);
        }

        /// <summary>Paint the Size×Size bool fog window into our own RGBA32 texture.</summary>
        private void PaintFog(SurveyData survey)
        {
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

            if (_mapImage != null) _mapImage.texture = _tex;
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

            float edge = _mapRect.sizeDelta.x;
            float cell = edge / size;
            // Center of the cell, relative to the rect's centered pivot.
            anchored = new Vector2((wx + 0.5f) * cell - edge / 2f,
                                   (wy + 0.5f) * cell - edge / 2f);
            return true;
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

        // ── Player marker / edge arrow (full polish in M-C) ──────────────────────────────

        private void UpdatePlayerMarker(SurveyData survey, Vector3 origin, float radius)
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

        private void EnsurePlayerMarker()
        {
            if (_playerMarker != null || _overlayLayer == null) return;
            var go = new GameObject("playerMarker");
            go.transform.SetParent(_overlayLayer.transform, false);
            _playerMarker = go.AddComponent<RawImage>();
            _playerMarker.color = new Color(0.4f, 0.7f, 1f, 1f);
            var rt = _playerMarker.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PlayerMarkerPx, PlayerMarkerPx);
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

            // ESC closes the full-screen view (the fork owns its own close path — it does NOT
            // rely on vanilla Minimap's M/ESC handling, which is dead under nomap). For the
            // field Local-Map view the controller will also close it on unequip; this is the
            // explicit player dismiss. We do NOT toggle vanilla map mode.
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

            // A simple carved frame behind the map (our own flat color; visual polish later).
            var frame = new GameObject("frame");
            frame.transform.SetParent(_root.transform, false);
            var frameImg = frame.AddComponent<Image>();
            frameImg.color = CFrame;
            var frameRt = frame.GetComponent<RectTransform>();
            frameRt.anchorMin = frameRt.anchorMax = new Vector2(0.5f, 0.5f);
            frameRt.pivot = new Vector2(0.5f, 0.5f);
            frameRt.sizeDelta = new Vector2(MaxFullViewPx + 24, MaxFullViewPx + 24);

            // The painted fog map.
            var mapGo = new GameObject("boundedFog");
            mapGo.transform.SetParent(_root.transform, false);
            _mapImage = mapGo.AddComponent<RawImage>();
            _mapRect = _mapImage.rectTransform;
            _mapRect.anchorMin = _mapRect.anchorMax = new Vector2(0.5f, 0.5f);
            _mapRect.pivot = new Vector2(0.5f, 0.5f);
            _mapRect.sizeDelta = new Vector2(MaxFullViewPx, MaxFullViewPx);

            // Overlay layer (pins + markers) sits on top of the fog, same rect + center.
            _overlayLayer = new GameObject("overlay");
            _overlayLayer.transform.SetParent(mapGo.transform, false);
            var ovRt = _overlayLayer.AddComponent<RectTransform>();
            ovRt.anchorMin = ovRt.anchorMax = new Vector2(0.5f, 0.5f);
            ovRt.pivot = new Vector2(0.5f, 0.5f);
            ovRt.sizeDelta = Vector2.zero; // children anchor to center, position in px

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
            if (_instance == this) _instance = null;
        }
    }
}
