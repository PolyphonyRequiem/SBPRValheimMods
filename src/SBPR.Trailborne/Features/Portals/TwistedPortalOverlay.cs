// ============================================================================
//  Trailborne v3 (Swamp) — Twisted Portal on-step proximity OVERLAY (render)
// ----------------------------------------------------------------------------
//  Card     : t_e732bd8b (C3) — the through-terrain portal-name overlay.
//  Impl spec: docs/v3/planning/twisted-portal-impl-spec.md §7 (the overlay).
//  Design   : docs/design/nomap.md §7 ("on-step shows visible portal names,
//             visible through terrain").
//  Model    : TwistedPortalOverlayModel (engine-free; the nearest-N / radius /
//             unnamed-skip / distance-format policy — CI-gated in
//             tests/TwistedPortalOverlayModelTests.cs).
//
//  WHAT THIS DRAWS (LOOK-TO-AIM — the INTERACTIVE selection surface, L3 t_d9ea1b2c).
//  When the local player stands on / near a Twisted Portal, a floating world-space
//  label appears over every nearby Twisted Portal showing its RUNE NAME (+ optional
//  distance), rendered THROUGH TERRAIN (a ZTest-Always material on the label so it's
//  legible behind hills — the design's "visible through terrain" reading; the
//  BugattiBoys/PortalIndicator behaviour reproduced from uGUI primitives only).
//
//  🟥 LOOK-TO-AIM (spec §7, superseded 2026-06-27): the labels are also the AIM
//  TARGETS. The destination the crosshair is pointing at (the angular pick L1's
//  TwistedPortalCommitInput publishes) gets a SELECTED-HIGHLIGHT — a brighter
//  (luminance, NOT hue — Daniel is colourblind) tint + a size bump — and the read-only
//  FOOD-IMPACT PREVIEW (TwistedPortalEnergy.PreviewJump → TwistedPortalPreviewText)
//  renders under that one label: belly range vs the jump distance, and the berries the
//  shortfall would need ("the impact to food"). The preview is NON-MUTATING (PreviewJump
//  spends nothing); the debit happens only on tap-E commit (L1). The overlay is no
//  longer informational — it IS the picker surface; travel is the aim + tap-E the
//  commit input owns.
//
//  🔴 MULTIPLAYER (spec §2 / §7.2): the label set is populated from the SHARED
//  TwistedPortalCandidates.Gather seam (the SAME source the commit input aims at, so the
//  highlighted label IS the portal you travel to). On a dedicated server that staging set
//  is only the ~64–128 m client window today; L2 (t_ccb454f8) swaps Gather's body for the
//  server-authoritative RPC set so the picker reaches long-range destinations — this
//  overlay inherits that automatically (it reads the seam, not a private walk).
//
//  ARCHITECTURE (the SunstoneLensHudOverlay / SunstoneWorldRing precedent, #209):
//    • The PUMP is a MonoBehaviour mounted ONCE under Hud.m_rootObject by a
//      Hud.Awake postfix (HudBootstrap). It stays ACTIVE for the HUD's lifetime so
//      Unity keeps calling Update(); visibility toggles the WORLD-SPACE LABEL FIELD
//      (a separate scene root), NEVER the host (the self-deactivating-host bug the
//      Iron Compass + Sunstone Lens both hit, PR #208 / t_d5949685).
//    • The LABELS live in WORLD space (their own root GameObject, NOT under the Hud
//      canvas — a screen-space canvas would apply screen scale to world geometry).
//      Each is a world-space uGUI Canvas + UI.Text + the vanilla Billboard
//      (m_vertical=true) so it faces the camera and stays upright.
//
//  Client-only by construction: Hud.Awake never fires on the dedicated server
//  (no Hud), and Player.m_localPlayer / Utils.GetMainCamera() are client concerns.
//  Everything here is cosmetic — it READS portal ZDOs, never writes them.
//
//  Clean-side (ADR-0001): reads base-game Hud / Player / ZDOMan / ZDO / Billboard /
//  GameCamera + our own SBPR_TwistedPortal ZDO slot only; the uGUI label surface is
//  ours, built additively (ADR-0006: new GameObject + AddComponent, no ZNetView /
//  Piece / Instantiate). The through-terrain label idiom referenced from
//  BugattiBoys/PortalIndicator is reproduced from Unity uGUI primitives only — zero
//  third-party gameplay code read or copied (AT-VANILLA-ONLY).
//
//  logs-green ≠ playable — "does it read right through terrain" is Daniel's in-game
//  eyeball on a GPU client (AT-OVERLAY). The headless evidence is: the model tests,
//  a clean 0/0 build, and the mount/populate diagnostics this file logs.
// ============================================================================

using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Portals
{
    /// <summary>
    /// The client-only on-step overlay PUMP. Built once under Hud.m_rootObject by
    /// <see cref="HudBootstrap"/>; its <see cref="Update"/> runs the throttled proximity check +
    /// ZDO walk and drives the world-space <see cref="LabelField"/>. The host stays active for the
    /// HUD's lifetime (#209); only the label field's visibility toggles.
    /// </summary>
    public class TwistedPortalOverlay : MonoBehaviour
    {
        // ── Render cadence (engine-side; the model owns the pure selection policy). The proximity
        //    check + ZDO walk are O(held-portals), cheap, so a ~0.5 s refresh costs nothing on the
        //    client and is invisible to the eye (the Sunstone Lens sweep cadence). ──
        public const float DefaultRefreshInterval = 0.5f;

        // The aimed-label highlight grows the selected label by this factor on top of its distance-
        // compensated scale (the colourblind-safe size cue — Daniel is colourblind, so the highlight
        // can NEVER lean on hue alone; a visible size bump + the food-preview block that appears on
        // exactly one label carry the "this is selected" signal independent of colour). Live-tunable
        // via Plugin.TwistedOverlayHighlightScaleBump; eyeball-converged like the other label dials.
        public const float DefaultHighlightScaleBump = 1.25f;

        private static TwistedPortalOverlay? _instance;

        // The world-space label field — lives in its OWN scene root (NOT under Hud.m_rootObject),
        // so screen-space scale never touches the world labels. The host (this) carries the pump.
        private readonly LabelField _field = new LabelField();

        private float _nextRefresh;
        private bool _loggedFirstPopulate;

        // Cached across frames: the throttled ZDO-walk + selection result is computed on the refresh
        // cadence, but the DRAW + aimed-highlight runs EVERY FRAME against this cache so the highlight
        // tracks the crosshair smoothly (AT-AIM-HIGHLIGHT) without re-walking ZDOs each frame.
        private bool _nearAPortal;

        // ── Food-impact preview recompute cache (L3, Beat 3). The per-frame work is just the highlight
        //    transform/colour (zero alloc); the PreviewJump belly+inventory read is bounded: it
        //    recomputes IMMEDIATELY when the aim moves to a new label (so a sweep shows the right cost at
        //    once) and at most every PreviewRefreshInterval while the aim stays on ONE label (the
        //    belly/berry numbers change slowly). ──
        private const float PreviewRefreshInterval = 0.25f;
        private ZDOID _previewAimedId;
        private bool _hasPreview;
        private string _previewText = string.Empty;
        private float _nextPreviewRefresh;

        // Reused across refreshes to avoid per-frame allocation.
        private readonly List<ZDO> _zdoScratch = new List<ZDO>();
        private readonly List<TwistedDestination> _candScratch = new List<TwistedDestination>();
        private readonly List<PortalRow> _rows = new List<PortalRow>();
        private readonly List<OverlayCandidate> _candidates = new List<OverlayCandidate>();
        private readonly List<int> _selected = new List<int>();

        /// <summary>One nearby portal's render facts (parallel to the engine-free
        /// <see cref="OverlayCandidate"/> the model selects over).</summary>
        private readonly struct PortalRow
        {
            public readonly Vector3 Pos;
            public readonly string Rune;
            public readonly bool HasRune;
            // The portal's ZDO id — the stable identity used to match the aimed destination (published
            // by TwistedPortalCommitInput) to its drawn label, so the highlight + food preview land on
            // the SAME portal the aim-pick selected and tap-E will travel to (no drift, L3).
            public readonly ZDOID Id;
            public PortalRow(Vector3 pos, string rune, bool hasRune, ZDOID id)
            { Pos = pos; Rune = rune; HasRune = hasRune; Id = id; }
        }

        // Diagnostic-logging gate (the Iron Compass / Sunstone self-deactivating-host pump lesson).
        // Reads the live Plugin config when present, else the const — the no-Plugin-context fallback.
        internal static bool DebugMount => Plugin.TwistedOverlayDebugMount?.Value ?? true;

        /// <summary>Idempotently build + mount the overlay pump under the given Hud root.</summary>
        public static void EnsureBuilt(GameObject hudRoot)
        {
            if (_instance != null) return;
            if (hudRoot == null) return;
            // Idempotent against a Hud re-Awake (scene reload): never double-mount.
            if (hudRoot.transform.Find("SBPR_TwistedPortalOverlay") != null) return;

            var host = new GameObject("SBPR_TwistedPortalOverlay");
            host.transform.SetParent(hudRoot.transform, worldPositionStays: false);
            _instance = host.AddComponent<TwistedPortalOverlay>();
        }

        private void Update()
        {
            // Guard everything: the HUD can outlive a world / player.
            var player = Player.m_localPlayer;
            if (player == null)
            {
                _field.Hide();
                _nearAPortal = false;
                ClearPreview();
                return;
            }

            // ── THROTTLED (refresh cadence): re-walk the held Twisted-Portal ZDOs + re-run the
            //    nearest-N selection. Portals are static, so the expensive walk doesn't need per-frame
            //    work — but the DRAW + aimed-highlight below DOES, so it runs every frame against the
            //    cached selection (AT-AIM-HIGHLIGHT: the highlight tracks the crosshair as you sweep). ──
            if (Time.time >= _nextRefresh)
            {
                _nextRefresh = Time.time + Mathf.Max(0.1f,
                    Plugin.TwistedOverlayRefreshInterval?.Value ?? DefaultRefreshInterval);
                RefreshSelection(player);
            }

            // Off-portal (Beat 1 inactive) → hide the field + drop any stale preview, and stop.
            if (!_nearAPortal)
            {
                _field.Hide();
                ClearPreview();
                return;
            }

            // ── EVERY FRAME: draw the cached selection with the aimed-label highlight + food-impact
            //    preview, so both track the crosshair smoothly between ZDO refreshes. ──
            DrawSelection(player);
        }

        /// <summary>
        /// THROTTLED pass: gather the held Twisted-Portal ZDOs, compute the proximity trigger
        /// (Beat 1), and run the engine-free nearest-N selection into <see cref="_selected"/>. Caches
        /// <see cref="_nearAPortal"/> + the parallel <see cref="_rows"/> / <see cref="_candidates"/> /
        /// <see cref="_selected"/> lists for the per-frame <see cref="DrawSelection"/> to consume.
        /// </summary>
        private void RefreshSelection(Player player)
        {
            float proximity = Plugin.TwistedOverlayProximityRange?.Value ?? TwistedPortalOverlayModel.DefaultProximityRange;
            float radius    = Plugin.TwistedOverlayRadius?.Value         ?? TwistedPortalOverlayModel.DefaultOverlayRadius;
            int   maxLabels = Plugin.TwistedOverlayMaxLabels?.Value      ?? TwistedPortalOverlayModel.DefaultMaxLabels;
            bool  showUnnamed = Plugin.TwistedOverlayShowUnnamed?.Value  ?? TwistedPortalOverlayModel.DefaultShowUnnamed;

            // ── Gather every Twisted Portal ZDO this peer holds (the §2 client window — or, once L2
            //    lands, the server-authoritative set, since BuildRows reads the SHARED candidate seam)
            //    and turn it into rows of (position, rune, hasRune, id). ──
            Vector3 here = player.transform.position;
            BuildRows(here);

            // ── Proximity TRIGGER (spec §7.3 / Beat 1): the overlay shows only while the player is
            //    within ~proximity m of SOME Twisted Portal (you're standing on / near one). ──
            float nearestSqr = float.MaxValue;
            for (int i = 0; i < _rows.Count; i++)
            {
                float d = (_rows[i].Pos - here).sqrMagnitude;
                if (d < nearestSqr) nearestSqr = d;
            }
            _nearAPortal = _rows.Count > 0 && nearestSqr <= proximity * proximity;
            if (!_nearAPortal) return;

            // ── Pure selection: nearest-N within radius, unnamed skipped unless asked (the model). ──
            _candidates.Clear();
            for (int i = 0; i < _rows.Count; i++)
            {
                float dist = Vector3.Distance(_rows[i].Pos, here);
                _candidates.Add(new OverlayCandidate(dist, _rows[i].HasRune));
            }
            TwistedPortalOverlayModel.SelectNearest(_candidates, radius, maxLabels, showUnnamed, _selected);
        }

        /// <summary>
        /// PER-FRAME pass: draw the cached selection, highlighting the AIMED destination's label and
        /// rendering the read-only food-impact preview on it (L3, Beat 3). Reads the aim state L1
        /// publishes (<see cref="TwistedPortalCommitInput.OnPortal"/> / <see cref="TwistedPortalCommitInput.HasAim"/>
        /// / <see cref="TwistedPortalCommitInput.AimedDestination"/>) and matches it to a drawn label by
        /// ZDO id, so the highlight + preview land on exactly the portal tap-E will travel to (no drift).
        /// Render config is read here (not on the throttle) so live-config edits apply immediately
        /// (the banner-windsock pattern).
        /// </summary>
        private void DrawSelection(Player player)
        {
            bool  showDistance   = Plugin.TwistedOverlayShowDistance?.Value  ?? TwistedPortalOverlayModel.DefaultShowDistance;
            bool  throughTerrain = Plugin.TwistedOverlayThroughTerrain?.Value ?? TwistedPortalOverlayModel.DefaultThroughTerrain;
            float labelScale  = Plugin.TwistedOverlayLabelScale?.Value   ?? TwistedPortalOverlayModel.DefaultLabelScale;
            float labelHeight = Plugin.TwistedOverlayLabelHeight?.Value  ?? TwistedPortalOverlayModel.DefaultLabelHeight;
            float radius      = Plugin.TwistedOverlayRadius?.Value       ?? TwistedPortalOverlayModel.DefaultOverlayRadius;

            // ── FIX 3c distance-compensated scale policy (live config; banner-windsock eyeball) ──
            LabelScaleMode scaleMode = Plugin.TwistedOverlayLabelScaleMode?.Value ?? TwistedPortalLabelScale.DefaultMode;
            float scaleRefDist = Plugin.TwistedOverlayLabelScaleRefDist?.Value ?? TwistedPortalLabelScale.DefaultRefDist;
            float scaleMinMul  = Plugin.TwistedOverlayLabelScaleMinMul?.Value  ?? TwistedPortalLabelScale.DefaultMinMul;
            float scaleMaxMul  = Plugin.TwistedOverlayLabelScaleMaxMul?.Value  ?? TwistedPortalLabelScale.DefaultMaxMul;
            float scaleKnee    = Plugin.TwistedOverlayLabelScaleKnee?.Value    ?? TwistedPortalLabelScale.DefaultKnee;
            float scaleFloor   = Plugin.TwistedOverlayLabelScaleFloor?.Value   ?? TwistedPortalLabelScale.DefaultFloor;

            // ── L3 look-to-aim render knobs (live config). HighlightAimed gates the whole interactive
            //    surface (off → the overlay falls back to the informational render); ShowFoodPreview
            //    gates the Beat-3 readout independently. ──
            bool  highlightAimed = Plugin.TwistedOverlayHighlightAimed?.Value  ?? true;
            bool  showFoodPreview = Plugin.TwistedOverlayShowFoodPreview?.Value ?? true;
            float highlightBump  = Plugin.TwistedOverlayHighlightScaleBump?.Value ?? DefaultHighlightScaleBump;

            // ── Read the published aim state (L1's commit input). The aimed destination's ZDO id is
            //    what a drawn label is matched against to highlight it + carry the food preview. ──
            bool aimActive = highlightAimed
                             && TwistedPortalCommitInput.OnPortal
                             && TwistedPortalCommitInput.HasAim;
            ZDOID aimedId = aimActive ? TwistedPortalCommitInput.AimedDestination.Id : default;

            // ── Food-impact preview (bounded recompute; spec §5 / Beat 3). Read-only PreviewJump on
            //    the aimed destination — recomputed immediately when the aim moves to a new label and
            //    at most every PreviewRefreshInterval while it stays on one. ──
            UpdateFoodPreview(player, aimActive, showFoodPreview);

            _field.BeginFrame(throughTerrain, labelScale,
                scaleMode, scaleRefDist, scaleMinMul, scaleMaxMul, radius, scaleKnee, scaleFloor);
            int drawn = 0;
            for (int s = 0; s < _selected.Count; s++)
            {
                int idx = _selected[s];
                PortalRow row = _rows[idx];
                float dist = _candidates[idx].Distance;
                string text = TwistedPortalOverlayModel.BuildLabel(row.Rune, row.HasRune, dist, showDistance);

                // The aimed label: highlight it (brighter colour + a size bump — the colourblind-safe
                // cue) and append the food-impact preview block under the rune (Beat 3). Match by stable
                // ZDO id so the highlight is provably the portal the aim-pick chose + tap-E will travel to.
                bool isAimed = aimActive && row.Id != default && row.Id == aimedId;
                if (isAimed && showFoodPreview && _hasPreview)
                    text = text + "\n" + _previewText;

                Vector3 labelPos = row.Pos + Vector3.up * labelHeight;
                _field.DrawLabel(drawn, labelPos, text, row.HasRune, isAimed, isAimed ? highlightBump : 1f);
                drawn++;
            }
            _field.EndFrame(drawn);

            LogFirstPopulate(drawn);
        }

        /// <summary>
        /// Recompute the read-only food-impact preview for the aimed destination (spec §5 / Beat 3),
        /// bounded so the per-frame draw stays cheap: recomputes IMMEDIATELY when the aim moves to a
        /// new label (so a crosshair sweep shows the right cost at once) and at most every
        /// <see cref="PreviewRefreshInterval"/> while the aim holds on ONE label (belly/berry numbers
        /// drift slowly). NON-MUTATING — <see cref="TwistedPortalEnergy.PreviewJump"/> spends nothing.
        /// </summary>
        private void UpdateFoodPreview(Player player, bool aimActive, bool showFoodPreview)
        {
            if (!aimActive || !showFoodPreview)
            {
                ClearPreview();
                return;
            }

            TwistedDestination aimed = TwistedPortalCommitInput.AimedDestination;
            ZDOID aimedId = aimed.Id;

            bool aimChanged = !_hasPreview || aimedId != _previewAimedId;
            if (!aimChanged && Time.time < _nextPreviewRefresh) return;

            _nextPreviewRefresh = Time.time + PreviewRefreshInterval;
            _previewAimedId = aimedId;

            // The jump distance is PLAYER → destination — the SAME Vector3.Distance the commit path
            // debits (SBPR_TwistedPortal.CommitTravel: player.transform.position → selected.Position),
            // so the preview equals what committing will charge (AT-FOOD-PREVIEW: commit matches preview).
            float distance = Vector3.Distance(player.transform.position, aimed.Position);
            TwistedPortalEnergy.JumpPreview p = TwistedPortalEnergy.PreviewJump(player, distance);
            _previewText = TwistedPortalPreviewText.BuildFoodPreview(
                p.DistanceMeters, p.BellyRangeMeters, p.BellyCovers, p.BerriesNeeded, p.BerriesHeld, p.Reachable);
            _hasPreview = true;
        }

        /// <summary>Drop any cached food preview (off-portal, no aim, or preview disabled).</summary>
        private void ClearPreview()
        {
            _hasPreview = false;
            _previewText = string.Empty;
            _previewAimedId = default;
        }

        /// <summary>
        /// Walk the Twisted-Portal candidate set (the SHARED <see cref="TwistedPortalCandidates.Gather"/>
        /// source, also used by the commit input — so the label you aim at IS the portal you travel to,
        /// no drift between the two surfaces) and fill <see cref="_rows"/> with each one's (position,
        /// censored rune, hasRune). 🔴 On a dedicated server this is only the ~64–128 m client window
        /// (§2) — the L1 STAGING set L2 (t_ccb454f8) swaps for the server-authoritative RPC set.
        /// </summary>
        private void BuildRows(Vector3 here)
        {
            _rows.Clear();

            TwistedPortalCandidates.Gather(here, _zdoScratch, _candScratch);
            foreach (var c in _candScratch)
                _rows.Add(new PortalRow(c.Position, c.Rune, c.HasRune, c.Id));
        }

        // Diagnostic: on the FIRST populated frame, log how many labels were drawn so a client
        // LogOutput.log can split "pump never ran / no portals held" (line absent) from "labels
        // drawn but invisible" (line present with a count). Re-armed when the field hides.
        private void LogFirstPopulate(int drawn)
        {
            if (drawn == 0) { _loggedFirstPopulate = false; return; }
            if (_loggedFirstPopulate || !DebugMount) return;
            _loggedFirstPopulate = true;
            Plugin.Log.LogInfo(
                $"[Trailborne/TwistedPortal] Overlay populated: {drawn} nearby Twisted Portal label(s) drawn "
                + "(best-effort from the ZDOs this client holds — on a dedicated server this is the ~64–128 m "
                + "window, §2, NOT a guaranteed 300 m; AT-OVERLAY).");
        }

        private void OnDestroy()
        {
            _field.Dispose();
            if (_instance == this) _instance = null;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        //  THE WORLD-SPACE LABEL FIELD — pooled billboarded through-terrain rune labels.
        //  Owned by the pump above; its slot objects live in WORLD space (their own scene root,
        //  NEVER under Hud.m_rootObject — #209). Not a MonoBehaviour: the pump's Update is the single
        //  driver; the per-slot Billboard components ARE MonoBehaviours and self-face the camera.
        // ════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>The pooled world-space label field. <see cref="BeginFrame"/> →
        /// N×<see cref="DrawLabel"/> → <see cref="EndFrame"/> each refresh; <see cref="Hide"/> when
        /// off-portal; <see cref="Dispose"/> on Hud teardown.</summary>
        private sealed class LabelField
        {
            // The world-space Canvas is authored at this reference pixel size, then scaled to world
            // units via localScale (worldSize / ReferencePx). Generous so two short lines never clip.
            //
            // ── DE-FUZZ via SUPERSAMPLE (FIX 3b, t_f66a3e37). ────────────────────────────────────
            // A world-space uGUI label minified to ~1 world-metre rasterises too few texels/metre →
            // blur (smeared further by the Outline). The fix is to author the glyph atlas + canvas at
            // N× the pixel density AND minify by the SAME N× (localScale = labelScale / ReferencePx,
            // ReferencePx ∝ N) so the on-screen WORLD size is UNCHANGED but the raster is supersampled
            // (crisp). The single Supersample factor below ties FontPx / ReferencePx / canvas-px
            // together so they can NEVER drift apart: world footprint = CanvasWidthPx × (labelScale /
            // ReferencePx) = 512·N·labelScale / (256·N) — the N cancels, so world size is invariant in
            // N BY CONSTRUCTION (not just by comment). Converge N by eye on a GPU client (banner-
            // windsock); it's baked into slot construction, so a change is a rebuild, not live-config.
            private const int   Supersample    = 4;          // glyph-atlas density multiplier (eyeball-converged)
            private const float ReferencePx    = 256f * Supersample;   // 1024
            private const float CanvasWidthPx  = 512f * Supersample;   // 2048
            private const float CanvasHeightPx = 256f * Supersample;   // 1024
            private const int   FontPx = 40 * Supersample;             // 160

            // Named portals read warm/white; unnamed read a dim grey (informational, can't pair).
            private static readonly Color NamedColor   = new Color(0.96f, 0.92f, 0.78f, 1f);
            private static readonly Color UnnamedColor = new Color(0.62f, 0.66f, 0.62f, 0.85f);

            // The AIMED-label highlight colour (L3). 🔴 COLOURBLIND-SAFE: Daniel is colourblind, so the
            // highlight must be distinguishable by LUMINANCE, not hue — this is a near-white at full
            // alpha (brighter than BOTH NamedColor and UnnamedColor), so the selected label reads as
            // "lit up" regardless of hue perception. It is the SECONDARY cue; the PRIMARY cues are the
            // size bump (DrawLabel scaleBump) + the food-preview block that appears on only this label.
            private static readonly Color AimedColor   = new Color(1f, 0.98f, 0.86f, 1f);

            // unity_GUIZTestMode is the per-material ZTest the UI/Default shader honours
            // (`ZTest [unity_GUIZTestMode]`). Setting it to Always (CompareFunction.Always = 8) makes
            // the label render THROUGH terrain — the card's headline "visible through terrain" trick.
            private const string ZTestProp = "unity_GUIZTestMode";

            private GameObject? _root;          // world-space content root (toggled for visibility)
            private readonly List<Slot> _slots = new List<Slot>();
            private Material? _ztestMaterial;   // shared ZTest-Always clone of the default UI material
            private bool _throughTerrain = true;
            private bool _disposed;

            private sealed class Slot
            {
                public GameObject Go = null!;   // carries Canvas + Billboard; placed + scaled per frame
                public RectTransform Rt = null!;
                public Text Label = null!;
                public bool ZTestApplied;       // whether the through-terrain material is on this slot's Text
            }

            /// <summary>Idempotently build the world-content root (a plain scene object at the origin
            /// so each slot's world position == the value we assign). Never under the Hud canvas.</summary>
            private void EnsureBuilt()
            {
                if (_root != null || _disposed) return;
                _root = new GameObject("SBPR_TwistedPortalOverlayRoot");
                _root.transform.position = Vector3.zero;
                _root.transform.rotation = Quaternion.identity;
                _root.transform.localScale = Vector3.one;
                _root.SetActive(false);
            }

            /// <summary>Open a refresh: ensure the root exists + visible (when a camera exists),
            /// record this frame's through-terrain + scale choices + the camera position (for the
            /// per-label distance-compensated scale, FIX 3c).</summary>
            public void BeginFrame(
                bool throughTerrain,
                float labelScale,
                LabelScaleMode scaleMode,
                float refDist,
                float minMul,
                float maxMul,
                float overlayRadius,
                float knee,
                float floor)
            {
                if (_disposed) return;
                EnsureBuilt();
                if (_root == null) return;
                var cam = Utils.GetMainCamera();
                if (cam == null) { SetVisible(false); return; }

                _throughTerrain = throughTerrain;
                _frameScale = Mathf.Max(0.01f, labelScale);
                // FIX 3c: capture the camera position + the live scale policy so each DrawLabel can
                // distance-compensate (hold ~constant on-screen size). Scale (our localScale) and facing
                // (the Billboard rotation) are independent transform channels — no conflict with m_invert.
                _frameCameraPos = cam.transform.position;
                _frameScaleMode = scaleMode;
                _frameRefDist = refDist;
                _frameMinMul = minMul;
                _frameMaxMul = maxMul;
                _frameOverlayRadius = overlayRadius;
                _frameKnee = knee;
                _frameFloor = floor;
                SetVisible(true);
            }

            private float _frameScale = 1f;
            // FIX 3c per-frame scale state (set in BeginFrame, read in DrawLabel).
            private Vector3 _frameCameraPos = Vector3.zero;
            private LabelScaleMode _frameScaleMode = TwistedPortalLabelScale.DefaultMode;
            private float _frameRefDist = TwistedPortalLabelScale.DefaultRefDist;
            private float _frameMinMul = TwistedPortalLabelScale.DefaultMinMul;
            private float _frameMaxMul = TwistedPortalLabelScale.DefaultMaxMul;
            private float _frameOverlayRadius = TwistedPortalOverlayModel.DefaultOverlayRadius;
            private float _frameKnee = TwistedPortalLabelScale.DefaultKnee;
            private float _frameFloor = TwistedPortalLabelScale.DefaultFloor;

            /// <summary>Place + fill the <paramref name="index"/>-th pooled label at
            /// <paramref name="worldPos"/> with <paramref name="text"/>. Named vs unnamed tints the
            /// text; the slot is scaled to <c>labelScale</c> world-metres. When <paramref name="aimed"/>
            /// is set (the look-to-aim selected label, L3) the label is tinted the brighter
            /// <see cref="AimedColor"/> (a LUMINANCE cue, colourblind-safe) and grown by
            /// <paramref name="scaleBump"/> on top of its distance-compensated scale — the size bump +
            /// the food-preview block (folded into <paramref name="text"/> by the caller) are the
            /// primary hue-independent "this is selected" signal (AT-AIM-HIGHLIGHT).</summary>
            public void DrawLabel(int index, Vector3 worldPos, string text, bool hasRune,
                bool aimed = false, float scaleBump = 1f)
            {
                if (_disposed || _root == null) return;

                Slot slot = EnsureSlot(index);
                slot.Go.transform.position = worldPos;
                // FIX 3c: distance-compensate the world-scale so the label holds ~constant ON-SCREEN
                // size across the overlay range (clamped near/far) instead of shrinking with raw
                // perspective. The multiplier is the engine-free, CI-gated TwistedPortalLabelScale curve
                // (AT-LABEL-SCALE-MATH) — the same SCALE-carries-range move as the Sunstone trophy halo.
                // L3: the aimed label multiplies in an extra scaleBump so the selected destination reads
                // visibly larger (the colourblind-safe size cue), independent of the highlight tint.
                float camDist = Vector3.Distance(_frameCameraPos, worldPos);
                float mul = TwistedPortalLabelScale.ScaleMul(
                    _frameScaleMode, camDist, _frameRefDist, _frameMinMul, _frameMaxMul,
                    _frameOverlayRadius, _frameKnee, _frameFloor);
                float bump = scaleBump > 0f ? scaleBump : 1f;
                slot.Go.transform.localScale = Vector3.one * (_frameScale / ReferencePx) * mul * bump;

                slot.Label.text = text;
                slot.Label.color = aimed ? AimedColor : (hasRune ? NamedColor : UnnamedColor);
                ApplyMaterial(slot);
                slot.Go.SetActive(true);
            }

            /// <summary>Close the refresh: park every pooled slot beyond <paramref name="drawn"/>.</summary>
            public void EndFrame(int drawn)
            {
                if (_disposed) return;
                for (int i = drawn; i < _slots.Count; i++)
                    if (_slots[i].Go.activeSelf) _slots[i].Go.SetActive(false);
            }

            public void Hide() => SetVisible(false);

            private void SetVisible(bool on)
            {
                if (_root == null) return;
                if (_root.activeSelf != on) _root.SetActive(on);
            }

            public void Dispose()
            {
                _disposed = true;
                _slots.Clear();
                if (_root != null) { Object.Destroy(_root); _root = null; }
                if (_ztestMaterial != null) { Object.Destroy(_ztestMaterial); _ztestMaterial = null; }
            }

            private Slot EnsureSlot(int index)
            {
                while (_slots.Count <= index) _slots.Add(MakeSlot(_slots.Count));
                return _slots[index];
            }

            private Slot MakeSlot(int idx)
            {
                // The slot root: a world-space Canvas + the vanilla Billboard so it yaws to face the
                // camera and stays upright (m_vertical=true — the nameplate idiom). World size is
                // driven by the root's localScale (set per frame); the Canvas is authored at ReferencePx.
                var go = new GameObject($"twisted_label_{idx}");
                go.transform.SetParent(_root!.transform, worldPositionStays: false);

                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                // No worldCamera / GraphicRaycaster: nothing here is interactive (raycastTarget=false),
                // so the canvas renders via the scene cameras without an assigned event camera.

                var rt = go.GetComponent<RectTransform>();   // Canvas auto-adds a RectTransform
                rt.sizeDelta = new Vector2(CanvasWidthPx, CanvasHeightPx);
                rt.pivot = new Vector2(0.5f, 0.5f);

                var bb = go.AddComponent<Billboard>();        // base-game camera-facing (ADR-0001)
                bb.m_vertical = true;
                // UN-MIRROR (FIX 3a, t_f66a3e37). Vanilla Billboard.LateUpdate
                // (assembly_valheim.decompiled.cs:100000-100020) does transform.LookAt(camera), which
                // points the transform's +Z TOWARD the camera. A uGUI Canvas renders its content on +Z,
                // so a viewer reading +Z-at-them sees the canvas from BEHIND → text reads back-to-front
                // (mirrored). m_invert (:99991, default false — never set, so every label mirrored)
                // reflects the look-target to BEHIND the label (vector = pos - (camPos - pos), :100006-
                // 100008), so LookAt ends with +Z pointing AWAY from the camera → glyphs read forward.
                // The reflect runs BEFORE the m_vertical step (:100010-100013), so upright-yaw is
                // preserved; ZTest-Always (the through-terrain trick) is on the material, independent of
                // facing — unaffected. This is the exact vanilla knob; no hand-rolled 180° rotation.
                bb.m_invert = true;

                // The label Text fills the canvas, centred, two-line capable (overflow both axes).
                var labelGo = new GameObject("text", typeof(RectTransform));
                labelGo.transform.SetParent(rt, worldPositionStays: false);
                var lrt = labelGo.GetComponent<RectTransform>();
                lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.anchoredPosition = Vector2.zero;
                lrt.sizeDelta = new Vector2(CanvasWidthPx, CanvasHeightPx);

                var font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                           ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                var txt = labelGo.AddComponent<Text>();
                txt.font = font;
                txt.fontSize = FontPx;
                txt.fontStyle = FontStyle.Bold;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Overflow;
                txt.color = NamedColor;
                txt.raycastTarget = false;

                // Black outline for legibility against the bright sky / dark swamp (the Sunstone debug
                // text precedent). The outline adds verts to the SAME CanvasRenderer/material, so it is
                // also through-terrain when the ZTest material is applied — no separate plumbing.
                // FIX 3b: effectDistance scales with Supersample so the VISUAL outline thickness is
                // unchanged at the higher atlas density (2px × N = 8px at N=4). Kept (not dropped) — the
                // dark-swamp / bright-sky contrast need is real (the Sunstone debug-text precedent).
                var outline = labelGo.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.92f);
                outline.effectDistance = new Vector2(2f * Supersample, -2f * Supersample);

                return new Slot { Go = go, Rt = rt, Label = txt, ZTestApplied = false };
            }

            /// <summary>Apply (or remove) the through-terrain ZTest-Always material on this slot's
            /// Text to match the live <see cref="_throughTerrain"/> choice (Daniel A/B's it in-game —
            /// the banner-windsock live-config pattern). Builds the shared material lazily, NON-
            /// destructively (a CLONE of the default UI material — never mutate the shared default,
            /// the painted-sign sharedMaterial lesson).</summary>
            private void ApplyMaterial(Slot slot)
            {
                if (_throughTerrain)
                {
                    EnsureZTestMaterial(slot);
                    if (_ztestMaterial != null && !slot.ZTestApplied)
                    {
                        slot.Label.material = _ztestMaterial;
                        slot.ZTestApplied = true;
                    }
                }
                else if (slot.ZTestApplied)
                {
                    // Back to honest depth: clear our override so the Text uses the default UI material.
                    slot.Label.material = null;
                    slot.ZTestApplied = false;
                }
            }

            /// <summary>Build the shared ZTest-Always material once, cloned from a live Text's default
            /// UI material (no Shader.Find risk — the SunstoneWorldRing reasoning). The UI/Default
            /// shader reads <c>unity_GUIZTestMode</c> for its ZTest, so setting it to Always renders
            /// the label through terrain. The font atlas texture is fed to the CanvasRenderer
            /// separately (Text.mainTexture), so a material with no font texture is correct here.</summary>
            private void EnsureZTestMaterial(Slot slot)
            {
                if (_ztestMaterial != null) return;
                var baseMat = slot.Label.material;   // default UI canvas material for an active Graphic
                if (baseMat == null) return;
                _ztestMaterial = new Material(baseMat) { name = "SBPR_TwistedLabel_ZTestAlways" };
                _ztestMaterial.SetInt(ZTestProp, (int)UnityEngine.Rendering.CompareFunction.Always);
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        //  BOOTSTRAP — mount the overlay under Hud.m_rootObject (the Sunstone Lens / Iron Compass
        //  Hud.Awake-postfix doctrine). Never fires on the dedicated server (no Hud). Fail-quiet.
        // ════════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds (idempotently) the overlay pump once the Hud exists. Postfix on Hud.Awake — the
        /// exact pattern the Sunstone Lens HUD + Iron Compass use (spec §7.1 Route reasoning). MUST
        /// be registered in Plugin.Awake via harmony.PatchAll or PatchCheck ERRORs at boot (the
        /// t_564f695a unregistered-patch lesson). Server-safe + fail-quiet.
        /// </summary>
        [HarmonyPatch(typeof(Hud), "Awake")]
        public static class HudBootstrap
        {
            [HarmonyPostfix]
            public static void Postfix(Hud __instance)
            {
                try
                {
                    if (__instance == null || __instance.m_rootObject == null) return;
                    EnsureBuilt(__instance.m_rootObject);
                    if (DebugMount)
                        Plugin.Log.LogInfo("[Trailborne/TwistedPortal] Overlay pump mounted under Hud.m_rootObject "
                            + $"(m_rootObject non-null={__instance.m_rootObject != null}).");
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning($"[Trailborne/TwistedPortal] Overlay bootstrap error (non-fatal): {e.Message}");
                }
            }
        }
    }
}
