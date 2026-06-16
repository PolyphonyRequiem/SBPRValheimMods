// ============================================================================
//  Trailborne v2 cartography — the viewer COORDINATOR (two MapSurface instances)
// ----------------------------------------------------------------------------
//  Post map-provider-binding-impl-spec §4: MapViewer is now a thin coordinator. The
//  §2H.1 render machinery lives in MapSurface (one factored layer-tree builder); this
//  class owns TWO instances of it and routes the CartographyViewer seam's two channels:
//    • the MODAL full view  (table-centred, backdrop + prompts, Esc/click input) —
//      Open/Refresh/Close, opened by the Surveyor's Table (TableEdit) or the equipped
//      Local Map (FieldReadOnly). UNCHANGED behaviour (the playtested §2H.1 view).
//    • the carry-state MINIMAP DISC (player-centred per R1, corner-anchored, passive) —
//      BindMinimap/UnbindMinimap/IsMinimapBound, driven by LocalMapController from the
//      §3 provider state machine, gated on Game.m_noMap (§5).
//  ONE viewer object, two surfaces, ONE shared renderer — never a parallel hierarchy.
//
//  Clean-side (ADR-0001): the surfaces are our own uGUI; no vanilla UI prefab cloned.
// ============================================================================

using UnityEngine;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// Client-only singleton MonoBehaviour implementing <see cref="IMapViewer"/>. Holds the modal
    /// full-view surface and the carry-disc surface, and ticks both each frame (rotation always; the
    /// modal also handles its own Esc/click input). Lives on its own host (DontDestroyOnLoad).
    /// </summary>
    public class MapViewer : MonoBehaviour, IMapViewer
    {
        private static MapViewer? _instance;
        public static MapViewer? Instance => _instance;

        // §4.2 sizing knobs. The modal keeps the playtested 900 px; the disc is the small corner
        // surface (~200 px) at HUD-tier sortingOrder so the full view cleanly covers it when opened.
        private const int ModalTargetPx = 900;
        private const int ModalSortOrder = 5000;
        private const int DiscTargetPx = 200;
        private const int DiscSortOrder = 3000;
        private const float DiscCornerMarginPx = 24f;

        private MapSurface? _modal;   // the full-screen table-centred view (Open/Refresh/Close)
        private MapSurface? _disc;    // the player-centred carry minimap (BindMinimap/UnbindMinimap)

        /// <summary>Idempotently create + register the viewer (called from the bootstrap).</summary>
        public static void EnsureRegistered()
        {
            if (_instance != null) return;
            var host = new GameObject("SBPR_MapViewerHost");
            UnityEngine.Object.DontDestroyOnLoad(host);
            _instance = host.AddComponent<MapViewer>();
            CartographyViewer.Register(_instance);
        }

        private void Awake()
        {
            _modal = new MapSurface(transform, new MapSurfaceConfig
            {
                TargetPx = ModalTargetPx,
                SortingOrder = ModalSortOrder,
                ShowBackdrop = true,
                ShowPrompts = true,
                HandleInput = true,
                PlayerCentred = false,
                ScreenAnchor = new Vector2(0.5f, 0.5f),
            });
            _disc = new MapSurface(transform, new MapSurfaceConfig
            {
                TargetPx = DiscTargetPx,
                SortingOrder = DiscSortOrder,
                ShowBackdrop = false,
                ShowPrompts = false,
                HandleInput = false,
                PlayerCentred = true,                       // R1: player-centred camera
                ScreenAnchor = new Vector2(1f, 1f),          // top-right (vanilla minimap's home)
                CornerMarginPx = DiscCornerMarginPx,
            });
        }

        // ── IMapViewer: modal full-view channel (unchanged behaviour) ───────────────────

        public void Open(MapViewRequest request) => _modal?.Show(request);
        public void Refresh(MapViewRequest request) => _modal?.Refresh(request);
        public void Close() => _modal?.Hide();
        public bool IsOpen => _modal != null && _modal.IsActive;
        public MapViewerMode CurrentMode => _modal != null ? _modal.Mode : MapViewerMode.FieldReadOnly;

        // ── IMapViewer: §4.1 minimap-disc channel ───────────────────────────────────────

        public void BindMinimap(MapViewRequest request)
        {
            // Force the disc's request to carry the minimap flag + a read-only shape regardless of
            // what the caller set, so a disc can never accidentally run modal/TableEdit logic.
            request.Minimap = true;
            request.Mode = MapViewerMode.FieldReadOnly;
            request.PinEditor = null;
            request.Title = string.Empty; // a minimap shows no cartouche
            _disc?.Show(request);
        }

        public void UnbindMinimap() => _disc?.Hide();
        public bool IsMinimapBound => _disc != null && _disc.IsActive;

        // ── Per-frame tick: rotation for both surfaces; modal also runs its own input ────

        private void Update()
        {
            // Modal Esc/click input first (returns true if Esc closed it this frame).
            if (_modal != null && _modal.IsActive)
            {
                bool closed = _modal.TickInput();
                if (!closed) _modal.TickRotation();
            }

            // The disc is passive (no input) but rotates to heading at frame rate, like the modal.
            if (_disc != null && _disc.IsActive)
                _disc.TickRotation();
        }

        private void OnDestroy()
        {
            _modal?.Destroy();
            _disc?.Destroy();
            if (_instance == this) _instance = null;
        }
    }
}
