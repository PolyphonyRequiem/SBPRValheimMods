// ============================================================================
//  Trailborne v2 cartography — the viewer seam (the §4 shared-viewer boundary)
// ----------------------------------------------------------------------------
//  The forked map viewer itself (the uGUI RawImage/Canvas render of a windowed
//  SurveyData at fixed zoom, the edge-clamp arrow, etc.) is the HIGHEST-RISK piece
//  of the v2 tier and is a SEPARATE impl card: t_7b616020 (engineer-ui), gated on
//  the UI-fork spike (t_e8bbbe48). It is explicitly NOT built here.
//
//  This file is the SEAM between the two cards (impl spec §4: "the viewer is shared
//  by the Local Map (read-only field mode) and the Surveyor's Table (pin-removal
//  Table mode). Build it once with a mode flag."). The Surveyor's Table (this card)
//  owns the survey DATA, its ZDO persistence, and the pin-removal BACKEND; it calls
//  CartographyViewer.Open(...) when used. The viewer card registers a real
//  IMapViewer here; until it does, Open(...) degrades gracefully (no crash, a clear
//  player message) so the Table's own acceptance tests (SHARED/PERSIST/WARD/PLACE)
//  do not depend on the viewer existing.
//
//  Clean-side (ADR-0001): no UI is built here; this is a delegation boundary only.
// ============================================================================

using UnityEngine;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>Which behavior the forked viewer runs in (impl spec §1.4 / §2B / D4).</summary>
    public enum MapViewerMode
    {
        /// <summary>Field Local-Map view: read-only. No pin editing.</summary>
        FieldReadOnly,
        /// <summary>Surveyor's Table view: shared data, pin REMOVAL enabled.</summary>
        TableEdit,
    }

    /// <summary>
    /// The pin-removal backend contract the viewer calls back into when running in
    /// <see cref="MapViewerMode.TableEdit"/>. The Surveyor's Table implements this
    /// (<see cref="SurveyorTableTag"/>): a viewer click → <see cref="RemovePinNear"/>
    /// removes from the Table's SHARED record and persists it owner-authoritatively.
    /// The field Local-Map view is read-only and is handed a null editor, so it
    /// structurally cannot remove pins (AT-TABLE-PINEDIT). This keeps the "what gets
    /// edited + how it persists" on the data-owning card and the "click handling +
    /// render" on the viewer card.
    /// </summary>
    public interface ICartographyPinEditor
    {
        /// <summary>
        /// Remove the shared pin closest to <paramref name="worldPos"/> within
        /// <paramref name="radius"/> metres and persist the change. Returns true if one
        /// was removed. No-op + false if the caller lacks ward access (the implementer
        /// re-checks PrivateArea.CheckAccess — never trust the UI to have gated).
        /// </summary>
        bool RemovePinNear(Vector3 worldPos, float radius);

        /// <summary>
        /// Re-read the editor's CURRENT shared survey (post-edit) so the viewer can re-render
        /// against live Table data after a removal — the snapshot the viewer was opened with
        /// is stale once a pin is removed. Returns null if the survey can't be read.
        /// </summary>
        SurveyData? ReadCurrentSurvey();
    }

    /// <summary>
    /// What the viewer is asked to display: a (snapshot of the) windowed survey, the
    /// bound origin + radius it is clipped to, the mode, and (TableEdit only) the pin
    /// editor backend. The viewer card consumes this; this card produces it.
    /// </summary>
    public struct MapViewRequest
    {
        public SurveyData Survey;            // the windowed fog + pins to render
        public Vector3 BoundOrigin;          // disc centre (Table position)
        public float RadiusMeters;           // hard shroud radius (1000 m)
        public MapViewerMode Mode;
        public ICartographyPinEditor? PinEditor;  // non-null only in TableEdit mode
    }

    /// <summary>
    /// The viewer implementation the viewer impl card registers. The seam stays narrow:
    /// Open (show + render), Refresh (re-render the live request while open — player
    /// marker / pins move), Close (hide), and IsOpen (state probe the controller polls).
    /// The viewer owns all uGUI/render state behind these.
    /// </summary>
    public interface IMapViewer
    {
        void Open(MapViewRequest request);
        void Refresh(MapViewRequest request);
        void Close();
        bool IsOpen { get; }

        /// <summary>The mode the viewer is currently showing (meaningful only while open).
        /// Lets a caller avoid clobbering a TableEdit session with a field refresh.</summary>
        MapViewerMode CurrentMode { get; }
    }

    /// <summary>
    /// Static registration point + safe dispatch for the forked viewer. The viewer
    /// card calls <see cref="Register"/> in its own bootstrap; the Surveyor's Table
    /// calls <see cref="Open"/>. Decoupled so the two cards build and ship independently
    /// (this foundation card first, per the build-order note), and so a missing viewer
    /// is a graceful "not available yet" message, never a NullReferenceException.
    /// </summary>
    public static class CartographyViewer
    {
        private static IMapViewer? _impl;

        /// <summary>True once the viewer card has registered a real implementation.</summary>
        public static bool IsAvailable => _impl != null;

        /// <summary>
        /// Register the forked-viewer implementation (called by the viewer card's
        /// bootstrap). Last registration wins; logged so double-registration is visible.
        /// </summary>
        public static void Register(IMapViewer impl)
        {
            if (_impl != null)
                Plugin.Log.LogWarning(
                    "[Trailborne/Cartography] CartographyViewer.Register: replacing an already-registered " +
                    "viewer implementation. (Expected once, from the viewer impl card.)");
            _impl = impl;
            Plugin.Log.LogInfo("[Trailborne/Cartography] Forked map viewer registered.");
        }

        /// <summary>
        /// Open the viewer for the given request, or degrade gracefully if no viewer is
        /// registered yet (this foundation card ships before the viewer card). Returns
        /// true if a viewer actually opened. The DATA-side work (contribute/merge + ZDO
        /// persistence) has already happened on the Table before this is called, so a
        /// missing viewer does not lose any survey progress — only the visual is deferred.
        /// </summary>
        public static bool Open(MapViewRequest request)
        {
            if (_impl == null)
            {
                // Foundation-card state: survey recorded + persisted, viewer not yet built.
                // Tell the player plainly instead of silently doing nothing. Plain English
                // (no custom $piece_* token exists — it would leak as a literal).
                Player.m_localPlayer?.Message(
                    MessageHud.MessageType.Center,
                    "Survey recorded (map viewer not installed yet)");
                Plugin.Log.LogInfo(
                    "[Trailborne/Cartography] Table used: survey contributed + persisted, but the forked " +
                    "viewer is not registered yet (impl card t_7b616020). Skipping render (no data lost).");
                return false;
            }

            try
            {
                _impl.Open(request);
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[Trailborne/Cartography] Viewer.Open threw: {e}");
                return false;
            }
        }

        /// <summary>True if a viewer is registered AND currently showing.</summary>
        public static bool IsViewerOpen => _impl != null && _impl.IsOpen;

        /// <summary>The mode the open viewer is in, or FieldReadOnly if none (safe default).</summary>
        public static MapViewerMode CurrentMode => _impl != null ? _impl.CurrentMode : MapViewerMode.FieldReadOnly;

        /// <summary>
        /// Re-render the currently-open viewer against an updated request (the imprinted
        /// snapshot is static, but the player marker + reconciled WorldPins move). No-op if
        /// no viewer is registered or it isn't open. Never throws out (logged + swallowed).
        /// </summary>
        public static void Refresh(MapViewRequest request)
        {
            if (_impl == null || !_impl.IsOpen) return;
            try { _impl.Refresh(request); }
            catch (System.Exception e) { Plugin.Log.LogError($"[Trailborne/Cartography] Viewer.Refresh threw: {e}"); }
        }

        /// <summary>
        /// Hide the viewer if one is registered + open. Safe to call unconditionally (the
        /// controller calls it on every map-unequip / map-left-inventory transition).
        /// </summary>
        public static void Close()
        {
            if (_impl == null) return;
            try { _impl.Close(); }
            catch (System.Exception e) { Plugin.Log.LogError($"[Trailborne/Cartography] Viewer.Close threw: {e}"); }
        }
    }
}
