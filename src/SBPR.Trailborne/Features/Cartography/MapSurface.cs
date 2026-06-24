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
        /// <summary>
        /// Multi-line caption STACK UNDER the disc (disc only — §3.1 of disc-name-hint-impl-spec +
        /// biome-indicator-impl-spec §3.1). Built on the non-rotating <c>_frame</c> below the bezel: the
        /// per-provider map NAME line (<see cref="MapViewRequest.Caption"/>), the current-biome NAME line,
        /// and a static localized <c>[&lt;$KEY_Map&gt;] $piece_readmap</c> hint line — one visual unit
        /// reading "this named local map, here, [M] to open." Off for the modal (its name lives in the
        /// top-centre Title cartouche; its biome in a separate fixed label under the title).
        /// </summary>
        public bool ShowCaption = false;
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

        // §3.4 disc-name-hint-impl-spec build-calibration knobs (Daniel's eyeball tunes — start here):
        //   • CaptionNameFontPx / CaptionHintFontPx: the two caption rows (name 18 / hint 16, §3.4).
        //   • CaptionGapPx: gap below the disc's nominal radius (TargetPx*0.5) to the caption TOP, so
        //     the caption clears the bezel ring (ringOuterR ≈ TargetPx*0.5 + ~3 px at 200 px) without
        //     colliding with the on-face chevron (AT-DISC-MARKER-1, which lives ON the disc face).
        private const float CaptionNameFontPx = 18f;
        private const float CaptionHintFontPx = 16f;
        private const float CaptionGapPx      = 10f;
        // biome-indicator-impl-spec §3.1/§4.1: the disc caption's MIDDLE biome line (name / biome /
        // hint). Sized to the hint row (16) so the NAME line stays the visual anchor; biome+hint read
        // as a tight lower pair under the name. Calibration knob — Daniel's eyeball tunes order/size.
        private const float CaptionBiomeFontPx = 16f;
        // biome-indicator-impl-spec §3.2/§4.3: the MODAL's fixed current-biome readout, a subtitle
        // UNDER the title cartouche. ~22 px reads as a subtitle, not a competing headline (title=34).
        private const float ModalBiomeFontPx = 22f;

        // §2H.1 b4 build-calibration knob: rotation sense. If the map turns the wrong way in-game,
        // flip to -1f. Single knob, shared by both surfaces; no north-up alternative (disorientation
        // is the intended design — Daniel).
        private const float MapRotationSign = 1f;

        // §4.2 / §2E.5.7: the #159 clip geometry (BezelInsetFrac, BezelRingFrac, BezelRingMinPx) and
        // the rect/mesh + ring radius arithmetic now live in the engine-free DiscRingGeometry helper —
        // the SINGLE source of truth consumed by LayoutMapRect (rect/mesh), EnsureBezelTexture (ring),
        // and the under-disc caption offset. Sharing one formula set is what guarantees the cartography
        // mesh silhouette and the bronze ring can never drift apart (the §2E.5.7 content-to-ring gap),
        // and makes the radius relation CI-gated by tests/DiscRingGeometryTests.cs.

        private static readonly Color32 CParchment = new Color32(214, 198, 162, 255);
        private static readonly Color32 CShroud    = new Color32(14, 13, 11, 255);
        private static readonly Color   CBackdrop  = new Color(0f, 0f, 0f, 0.92f);

        // ── Compass north ring palette (card t_fb53c9e4, M1; recolor t_540ace8c) ──
        // IronTint MULTIPLIES the bronze-baked bezel texture (which already encodes (0.62,0.55,0.42)), so
        // Color.white shows native bronze and IronTint shows iron. Iron in Valheim reads as a cool,
        // desaturated steel-grey — a touch blue-cool and LIGHTER than bronze so the ring reads "iron",
        // not "dark bronze". 🔴 This is the ONE genuinely visual constant in M1: Daniel EYEBALLS it
        // in-game against a real iron item and tunes (AT-COMPASS-BEZEL-GATED).
        //
        // 🔧 t_540ace8c — Daniel reported the equipped rim reads as a muddy dark brown-grey. Root cause:
        // the old (0.66,0.68,0.72) tint × the warm bronze base = (104,95,77) = #685F4D, dragged dark and
        // brown by the base. Retuned to a NEUTRAL medium grey: (0.677,0.764,1.0) × (0.62,0.55,0.42) =
        // (107,107,107) = #6B6B6B. NOTE the cap — the base's blue channel (0.42) limits a *neutral* grey
        // to RGB ≤ 107 on this tint-only path; a lighter neutral grey would require lifting the baked base
        // (MapSurface.cs:1423, shared with the unworn disc rim) or a separate worn-state bake. #6B6B6B is
        // the brightest neutral the clean single-constant edit can reach. Still AT-COMPASS-BEZEL-GATED:
        // Daniel eyeballs the textured product in-game and may push lighter (→ shared-base path).
        private static readonly Color CIronTint  = new Color(0.677f, 0.764f, 1.0f, 1f);
        // The N-glyph + ticks colour: a high-contrast off-white so it reads on the iron band (§3.4).
        private static readonly Color CNorthGlyph = new Color(0.92f, 0.93f, 0.96f, 1f);
        // The N-glyph font size as a fraction of the bezel hole radius, so the letter scales with the
        // surface (≈26 px on the 200 px disc, ≈120 px on the 900 px modal) — never a hard-coded px.
        private const float NorthGlyphFontFrac = 0.135f;
        private const float NorthTickFontFrac  = 0.105f;

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
        // ── Compass north ring (card t_fb53c9e4, M1) ──
        // The Iron Compass feature pushes its equip-state here each frame via SetCompassNorth (MapViewer
        // forwards it to both surfaces). When true: the bezel tints IRON (Path a — a per-frame _bezel.color
        // write, NEVER a _bezelTex mutation) and the N-glyph + ticks layer shows; when false the bezel
        // reverts to bronze (Color.white over the bronze-baked texture) and the N layer hides. North is a
        // property of the COMPASS, drawn on the surface only when worn (§5 invariant) — no persistence flag.
        private bool _compassWorn;
        // The N-glyph + ticks chevron-sibling. Parented to the ROTATING _mapContainer (:139, NOT the fixed
        // _frame the bezel rides) at a FIXED container-local point (0, +HoleRadius) = local-up: the
        // container's per-frame rotZ carries it to world-north's screen position for free (the player-marker
        // idiom, :1085-1098), and we counter-rotate the glyph itself by −rotZ for legibility. Built additively
        // (new GameObject + Image, ADR-0006); toggled by _compassWorn in ApplyFieldOrientation.
        private RectTransform? _northLayer;   // the orbiting container (N + ticks), child of _mapContainer
        private Text? _northGlyph;            // the "N" letter
        // §2H.2 (t_423f5bd7): true ⇔ the modal player marker is currently the OFF-disc edge-arrow,
        // which sets its own localRotation = angleDeg and must keep riding _mapContainer's +rotZ so
        // its composed on-screen bearing (rotZ + angleDeg) points outward (AT-MODAL-MARKER-3). For the
        // in-disc chevron on ANY rotating surface (disc OR modal/TableEdit) this stays false, so
        // ApplyFieldOrientation counter-rotates it to screen-up. Written in UpdatePlayerMarker, read
        // in ApplyFieldOrientation — both run each Render/TickRotation frame, set before read.
        private bool _markerOffDisc;
        private Text? _exitPrompt;
        private Text? _titleLabel;
        // biome-indicator-impl-spec §3.2/§4.3: the MODAL's fixed current-biome readout — a Text built
        // (modal-only, BuildPrompts) on the non-rotating _root, anchored UNDER the title cartouche.
        // Driven by UpdateBiomeLabel() from CurrentBiomeNameOrNull() (the ONE shared biome path, §3.4);
        // SetActive(false) when the biome is unresolved (None/unlocalized) so no empty bar / literal
        // shows. The DISC instance never builds this (ShowPrompts=false) — its biome rides _discCaption.
        private Text? _biomeLabel;
        // §3.1 disc-name-hint-impl-spec (+ biome-indicator-impl-spec §3.1): the under-disc caption STACK
        // (disc only, ShowCaption). _discCaption is a single multi-line Text on the non-rotating _frame,
        // THREE rows top→bottom: a per-provider NAME line (base fontSize 18) / the CURRENT-biome NAME line
        // (16, live from CurrentBiomeNameOrNull) / a STATIC localized hint line (<size=16> [<$KEY_Map>]
        // $piece_readmap). Name and biome are each conditionally omitted when absent; the hint always shows.
        // The hint is re-localized every Render (like UpdateExitPrompt/UpdateTitle) so a mid-session Map
        // rebind is reflected live (AT-REBIND-CORRECT); the biome line repaints on biome-border crossing.
        // _captionLastText skips the redundant Text.text set on the unchanged 0.25 s re-binds (layout cost).
        private Text? _discCaption;
        private string? _captionLastText;
        // Vanilla tokens only: $KEY_Map → the bound Map key (rebind-correct via ZInput), $piece_readmap
        // → "Read map". NO hardcoded "M", NO custom $piece_* literal (the 2026-06-05 sign-bug lesson).
        private const string CaptionHintRaw = "[<color=yellow><b>$KEY_Map</b></color>] $piece_readmap";
        private readonly List<GameObject> _pinObjects = new List<GameObject>();
        // Disc threat markers (Sunstone Lens → minimap handoff, card t_91e86951). Pulled from the
        // Cartography.ThreatMarkers registry each disc RebuildOverlay — the SAME inversion the survey
        // pins use (WorldPins.CollectInDiscPins). Disc-only; the modal is a nav surface, not a threat
        // radar, and the vanilla minimap (nomap-OFF) has its own overlay. Threat GameObjects are added
        // to _pinObjects so they counter-rotate upright + clear each rebuild for free (no new plumbing).
        private readonly List<DiscThreatMarker> _threatScratch = new List<DiscThreatMarker>();
        // Blip size + rim multiplier now live in the shared Cartography.MinimapThreatMetrics (card
        // t_bc017af4) so BOTH minimap surfaces (this disc + the vanilla corner overlay) read ONE symbol
        // and can't desync. Size resolves live via Plugin.ResolvedMinimapBlipPx (SunstoneLens/MinimapBlipPx
        // knob); the rim scale is the unchanged 0.6. ThreatRimInset (position, not size) stays local.
        private const float ThreatRimInset = 0.92f;  // seat a clamped rim blip at 92% of the visible disc radius

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

        /// <summary>
        /// Compass north ring (card t_fb53c9e4, M1). The Iron Compass feature pushes its equip-state here
        /// each frame (via <c>MapViewer.SetCompassNorth</c>). Stores the flag only; the actual iron/bronze
        /// bezel tint + N-glyph toggle apply per-frame in <see cref="ApplyFieldOrientation"/> (the same
        /// rotation path the player marker rides), so a hidden/inactive surface costs nothing. Idempotent.
        /// </summary>
        public void SetCompassNorth(bool compassWorn) => _compassWorn = compassWorn;

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
            UpdateBiomeLabel();
            UpdateCaption();
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
            // §2E.5 defect 2 (belt-and-suspenders): the cloned material inherits vanilla's live _SharedFade,
            // which the real Minimap drives from m_showSharedMapData (decomp :47106-47121). A bounded survey
            // view has no notion of another player's shared overlay, so pin it to 0 — the unexplored area is
            // our own reveal vs full shroud, never a faded shared blend. With the G=255 reveal above this is
            // defensive (those texels are no longer in the shared bucket), but it removes any dependency on
            // vanilla's live shared-data state bleeding the fade into our cloud.
            _shaderMat.SetFloat("_SharedFade", 0f);

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
        /// for the shader zoom AND every pin/marker projection (continuous, cell-snapped, and the cursor
        /// inverse), so they can never drift. When <c>ViewSpanMeters</c> &gt; 0 the surface locks that fixed
        /// span (the corner disc's tight nav window). When 0 (the modal) it frames the SURVEYED-DISC diameter
        /// (2 × effective radius — §2E.5.6), NOT the over-provisioned <c>Size × pixelSize</c> window: that
        /// 112 m surplus showed on screen as a ~22 px shroud annulus between the cartography edge and the
        /// bronze bezel ring (Daniel, v0.2.27-playtest). Both branches are clamped to the survey extent so
        /// neither can frame more than was surveyed.
        /// </summary>
        private float DisplayedSpanMeters(SurveyData survey, float pixelSize)
        {
            float fullSurveySpan = survey.Size * pixelSize;
            if (_cfg.ViewSpanMeters <= 0f)
            {
                // §2E.5.6 Knob 1 — frame the modal to the surveyed disc (2 × radius ≈ 2000 m at R=1000),
                // so the disc's edge meets the bezel ring instead of leaving a shroud band out to the
                // 2112 m over-provisioned window. Read the SAME effective radius RebuildOverlay uses
                // (see :RebuildOverlay) — do NOT hard-code 1000 — so a future radius change can't silently
                // re-open the gap. Clamp to the over-provisioned window defensively (2000 ≤ 2112 always
                // holds at R=1000, but the guard makes "never frame more than was surveyed" structural).
                float effectiveRadius = _req.RadiusMeters > 0f ? _req.RadiusMeters : survey.RadiusMeters;
                return Mathf.Min(2f * effectiveRadius, fullSurveySpan);
            }
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

            // §2E.5 unexplored = FULL SHROUD, not the "shared-by-others" look. Vanilla's _FogTex is
            // R8G8: R = m_explored (self), G = m_exploredOthers (shared via table/others). Reset fills
            // genuinely-unexplored as (255,255,255,255) — R=255 AND G=255 (Minimap.Reset, decomp :46976);
            // Explore zeroes R (:48043), ExploreOthers zeroes G (:48091). So R=255,G=0 is literally vanilla's
            // "someone shared this with you" code — which rendered the faded shared look Daniel rejected
            // (t_48c23824). We must mirror Reset on BOTH channels: G=255 too, so unexplored reads as
            // nobody-explored → solid fog-of-war cloud. cleared zeroes R (self-explored → revealed); G is
            // don't-care once R=0, kept 255 to stay strictly in the not-shared bucket.
            var fogged  = new Color32(255, 255, 255, 255);
            var cleared = new Color32(0, 255, 0, 255);
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

        /// <summary>
        /// Size the on-screen map square at a FIXED scale (no scroll-zoom).
        ///
        /// §2E.5.7 (card t_642687dd): the rect is sized to the FULL TargetPx square
        /// (<see cref="DiscRingGeometry.RectEdge"/>), NOT the old integer-floored
        /// <c>size·floor(TargetPx/size)</c>. That floor dropped the edge below
        /// TargetPx whenever <c>TargetPx/size</c> wasn't integral, so the mesh
        /// silhouette (meshR = edge/2) fell INSIDE the ring hole (sized off raw
        /// TargetPx in <see cref="EnsureBezelTexture"/>) and a transparent annulus
        /// opened — on the backdrop-less disc that gap showed the live game world.
        /// Sizing to TargetPx makes meshR = TargetPx/2 ≥ holeR for EVERY survey size
        /// on BOTH surfaces (and ≤ ringOuterR — no #159 bleed). The fog TEXTURE still
        /// upscales by the integer factor (a separate Texture2D concern, untouched);
        /// only the rect SILHOUETTE decouples. CircularRawImage samples uvRect
        /// per-vertex, so the framed/zoomed cartography is identical regardless of
        /// rect px — zoom/feel unchanged (AT-DISC-RING-3). <paramref name="size"/> is
        /// no longer read here; it stays as the documented render-time input.
        ///
        /// THE LANDMINE (#204 snapped-pin class): every projection reads
        /// <c>edge = _mapRect.sizeDelta.x</c> live and pairs it with
        /// DisplayedSpanMeters — WorldToSurfacePx, the snapped path, the
        /// <c>discR = edge*0.5</c> pin clip, and the TryRemovePinAtCursor inverse.
        /// Because they all read the rect, changing ONLY _mapRect.sizeDelta rescales
        /// the whole set uniformly and pins/marker/cursor stay glued (AT-DISC-RING-4).
        /// Do NOT introduce a second hard-coded edge literal — this is the one writer.
        /// </summary>
        private void LayoutMapRect(int size)
        {
            if (_mapRect == null) return;
            float edge = DiscRingGeometry.RectEdge(_cfg.TargetPx);
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
        /// CELL-SNAPPED world→anchored-px projection, used for PINS on the TABLE-centred modal so they
        /// annotate discrete fog cells exactly as the playtested view did (byte-faithful cell snap). The
        /// player-centred disc uses the continuous projection instead — cell-snapping there would make pins
        /// jitter a whole cell as the player walks. Returns false if the point falls outside the survey
        /// window grid.
        ///
        /// §2E.5.6 Knob 2 (THE LANDMINE): the snap (world→cell) is unchanged — banker's-rounded
        /// <c>WorldToCellX/Y</c>, preserving which discrete cell each pin annotates — but the cell is then
        /// re-projected through the SAME <see cref="DisplayedSpanMeters"/> framing <see cref="WorldToSurfacePx"/>
        /// uses, NOT the old hard-wired <c>edge / Size</c> (≡ the 2112 m over-provisioned grid). When Knob 1
        /// reframes the modal to 2 × radius, leaving this path on <c>edge / Size</c> would drift table pins
        /// outward up to ~+23.6 px at the disc edge (250 m→+5.9, 500 m→+11.8, 1000 m→+23.6) — pins floating
        /// off the terrain they annotate. Converting the snapped cell back to its world centre and reusing
        /// <see cref="WorldToSurfacePx"/> makes snapped pins, continuous pins, terrain, the player marker, and
        /// the <c>TryRemovePinAtCursor</c> inverse all frame at ONE span about ONE centre — they cannot desync.
        /// </summary>
        private bool WorldToSurfacePxSnapped(Vector3 world, SurveyData survey, out Vector2 anchored)
        {
            anchored = Vector2.zero;
            if (_mapRect == null) return false;
            float pixelSize = survey.PixelSize > 0f ? survey.PixelSize : 64f;
            int textureSize = survey.TextureSize > 0 ? survey.TextureSize : 256;
            int size = survey.Size;

            // Snap world → discrete fog cell exactly as today (byte-faithful: the pin still names the same
            // cell the playtested table view did). cx/cy are the survey-origin cell, retained ONLY for the
            // window-membership guard below — the projection no longer measures cell offsets off them.
            int cx = BoundedMapMath.WorldToCellX(survey.OriginX, pixelSize, textureSize);
            int cy = BoundedMapMath.WorldToCellY(survey.OriginZ, pixelSize, textureSize);
            int px = BoundedMapMath.WorldToCellX(world.x, pixelSize, textureSize);
            int py = BoundedMapMath.WorldToCellY(world.z, pixelSize, textureSize);

            // Same Size×Size window-membership guard as before: a point off the survey window never renders.
            int half = (size - 1) / 2;
            int wx = (px - cx) + half;
            int wy = (py - cy) + half;
            if (wx < 0 || wy < 0 || wx >= size || wy >= size) return false;

            // §2E.5.6 Knob 2: convert the snapped cell back to its world centre, then project it through the
            // SAME displayed-span/frame-centre as WorldToSurfacePx (the one source of truth). At the modal's
            // reframed 2000 m span this lands the snapped pin on the exact terrain cell — not the +Δpx the old
            // edge/Size = 2112 m grid produced.
            float snappedWorldX = BoundedMapMath.CellCenterWorldX(px, pixelSize, textureSize);
            float snappedWorldZ = BoundedMapMath.CellCenterWorldZ(py, pixelSize, textureSize);
            anchored = WorldToSurfacePx(new Vector3(snappedWorldX, 0f, snappedWorldZ), survey);
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

            // ── Sunstone threat layer (card t_91e86951): DISC ONLY. Pull live threat markers from the
            //    Cartography.ThreatMarkers registry — the same per-rebuild inversion the survey pins use
            //    above — and draw a tinted dot/icon per threat. Disc-only because the modal is a nav
            //    surface and the vanilla minimap (nomap-OFF) renders threats via its own overlay. The
            //    Lens provider returns an empty set unless the Lens is worn+charged AND the handoff mode
            //    feeds the disc, so this is inert with no Lens. Threat blips are added to _pinObjects so
            //    ApplyFieldOrientation counter-rotates them upright and ClearPinObjects clears them — no
            //    separate rotation/lifecycle plumbing (AT-LENS-DISC-CAMREL rides the same frame as pins).
            if (_cfg.PlayerCentred && ThreatMarkers.HasProviders)
            {
                try
                {
                    ThreatMarkers.Collect(FrameCenter(), radius, _threatScratch);
                    foreach (var t in _threatScratch)
                    {
                        if (!BoundedMapMath.InDisc(t.WorldPos.x, t.WorldPos.z, origin.x, origin.z, radius))
                            continue;
                        Vector2 anchored = WorldToSurfacePx(t.WorldPos, survey);
                        // Off-disc threats are NO LONGER dropped — clamp to the rim and draw smaller
                        // (card t_aab051ae item ④). With the 50m detection radius a hostile genuinely can
                        // sit outside the visible disc window, so the rim indicator is the "something's
                        // out there, that way" cue. On-disc threats draw full-size in place. The clamp is
                        // Cartography-owned geometry (BoundedMapMath) — no upward dep on Features/Sunstone.
                        bool offEdge = BoundedMapMath.ClampToRimPx(anchored.x, anchored.y, discR, ThreatRimInset,
                                                                  out float drawX, out float drawY);
                        SpawnThreatMarker(t, new Vector2(drawX, drawY), offEdge);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Trailborne/Cartography] MapSurface: threat-marker collect failed: {e.Message}");
                }
            }

            UpdatePlayerMarker(survey, origin, radius);
        }

        private void UpdatePlayerMarker(SurveyData survey, Vector3 origin, float radius)
        {
            if (_overlayLayer == null || _mapRect == null) return;
            var player = Player.m_localPlayer;
            if (player == null) { _markerOffDisc = false; if (_playerMarker != null) _playerMarker.gameObject.SetActive(false); return; }

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
                _markerOffDisc = false; // §2H.2: in-disc pivot chevron → counter-rotate to screen-up
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
                _markerOffDisc = true; // §2H.2: off-disc edge-arrow → keep its own angleDeg riding +rotZ (NOT counter-rotated)
                float inset = 0.94f;
                float ax = origin.x + (cx - origin.x) * inset;
                float az = origin.z + (cz - origin.z) * inset;
                rt.anchoredPosition = WorldToSurfacePx(new Vector3(ax, 0f, az), survey);
                rt.localRotation = Quaternion.Euler(0, 0, angleDeg);
                _playerMarker.color = new Color(1f, 0.5f, 0.2f, 1f);
            }
            else
            {
                _markerOffDisc = false; // §2H.2: in-disc chevron on the rotating modal/TableEdit → counter-rotate to screen-up
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

        // ── Sunstone threat marker (card t_91e86951; richened t_aab051ae) ─────────────────────────
        // Draw one threat blip on the disc: a tinted dot (the marker carries no Icon → BlipStyle.Dots)
        // or the supplied trophy icon, tinted by the aggro colour. Added to _pinObjects so it rides the
        // rotating container for POSITION and counter-rotates upright with the pins (CounterRotatePins),
        // and is cleared each rebuild by ClearPinObjects — the threat layer needs no separate plumbing.
        // The blip OWNS its Image.color (the aggro tint), which is why the disc honours the tint where
        // a vanilla AddPin would be clobbered white (design §5 / the WorldPins.ReapplyColors lesson).
        // offEdge: the threat was outside the visible disc and got clamped to the rim — draw it smaller
        // (a "that way" cue) and skip the star pips (no room at the bezel); on-disc threats draw full
        // size and carry a compact star row laid out locally (MountThreatStarPips) from the star sprite
        // the producer pushed through the marker — matches the vanilla minimap surface (card t_aab051ae).
        private void SpawnThreatMarker(DiscThreatMarker t, Vector2 anchored, bool offEdge)
        {
            if (_overlayLayer == null) return;
            var go = new GameObject("threat");
            go.transform.SetParent(_overlayLayer.transform, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            img.sprite = t.Icon != null ? t.Icon : ThreatDotSprite();
            img.color = t.Tint;   // OUR colour — survives (the disc overlay layer is never vanilla-stomped)

            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            float basePx = Plugin.ResolvedMinimapBlipPx;
            float px = offEdge ? basePx * MinimapThreatMetrics.RimScale : basePx;
            rt.sizeDelta = new Vector2(px, px);
            rt.anchoredPosition = anchored;
            _pinObjects.Add(go);

            if (!offEdge && t.Stars > 0)
                MountThreatStarPips(rt, t.Stars, px, t.Tint, t.StarSprite);
        }

        // Compact star-pip row above an on-disc threat blip (card t_aab051ae). Cartography-local so the
        // dependency arrow stays one-way: the producer (Sunstone) pushes the star SPRITE through the
        // DiscThreatMarker; we only lay out pips. Mirrors the vanilla-minimap overlay's row so a 2-star
        // hostile reads identically on both surfaces. Parented under the blip rt (rides its counter-rotation
        // + lifecycle). Falls back to a Unicode ★ Text when no sprite was supplied (never blank).
        // Pip size derives from the resolved blip px (card t_bc017af4) so pips scale WITH the live
        // blip-size knob and stay balanced at any magnitude — shared ratio with the vanilla-minimap
        // surface (Cartography.MinimapThreatMetrics.PipToBlipRatio), so the two surfaces can't desync.
        private static void MountThreatStarPips(RectTransform blipRt, int stars, float blipPx, Color tint, Sprite? starSprite)
        {
            if (blipRt == null || stars <= 0) return;
            stars = Mathf.Min(stars, 5);
            float pip = blipPx * MinimapThreatMetrics.PipToBlipRatio;

            var rowGo = new GameObject("stars", typeof(RectTransform));
            rowGo.transform.SetParent(blipRt, worldPositionStays: false);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin = rowRt.anchorMax = rowRt.pivot = new Vector2(0.5f, 0.5f);
            rowRt.anchoredPosition = new Vector2(0f, blipPx * 0.5f + pip * 0.6f);
            rowRt.sizeDelta = Vector2.zero;

            float startX = -(stars - 1) * 0.5f * pip;
            if (starSprite != null)
            {
                for (int i = 0; i < stars; i++)
                {
                    var pgo = new GameObject($"pip_{i}", typeof(RectTransform));
                    pgo.transform.SetParent(rowRt, worldPositionStays: false);
                    var prt = pgo.GetComponent<RectTransform>();
                    prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
                    prt.sizeDelta = new Vector2(pip, pip);
                    prt.anchoredPosition = new Vector2(startX + i * pip, 0f);
                    var pimg = pgo.AddComponent<Image>();
                    pimg.raycastTarget = false;
                    pimg.preserveAspect = true;
                    pimg.sprite = starSprite;
                    pimg.color = tint;
                }
            }
            else
            {
                var tgo = new GameObject("pip_text", typeof(RectTransform));
                tgo.transform.SetParent(rowRt, worldPositionStays: false);
                var trt = tgo.GetComponent<RectTransform>();
                trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
                trt.sizeDelta = new Vector2(40f, 10f);
                var txt = tgo.AddComponent<Text>();
                txt.font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                           ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                txt.fontSize = 9;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Overflow;
                txt.raycastTarget = false;
                txt.text = new string('\u2605', stars);
                txt.color = tint;
            }
        }

        // A code-generated near-white filled disc — the disc-side threat DOT. Cartography owns its own
        // copy (rather than reaching into Features/Sunstone) so the dependency arrow stays one-way:
        // Sunstone → Cartography.ThreatMarkers, never the reverse. White so the aggro tint reads via
        // Image.color; soft 1-px edge so it isn't aliased. No disk asset (guaranteed even with zero PNGs).
        private static Sprite? _threatDotSprite;
        private static Sprite ThreatDotSprite()
        {
            if (_threatDotSprite != null) return _threatDotSprite;
            const int sizePx = 64;
            var tex = new Texture2D(sizePx, sizePx, TextureFormat.RGBA32, false);
            var px = new Color[sizePx * sizePx];
            float cx = sizePx / 2f, cy = sizePx / 2f;
            float r = sizePx / 2f - 2f;
            for (int y = 0; y < sizePx; y++)
                for (int x = 0; x < sizePx; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * sizePx + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(r - d));
                }
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            _threatDotSprite = Sprite.Create(tex, new Rect(0, 0, sizePx, sizePx), new Vector2(0.5f, 0.5f), 100f);
            _threatDotSprite.name = "SBPR_DiscThreatDot";
            return _threatDotSprite;
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
            // screen-stable on EVERY rotating surface (§2H.2): the player-centred disc keeps "you"
            // pointing screen-up while the world spins, AND the table-centred modal / TableEdit in-disc
            // chevron now does the same (the pre-§2H.2 gate on _cfg.PlayerCentred left the modal chevron
            // riding +rotZ → pinned to map-north, the t_423f5bd7 bug). The ONLY marker we must NOT
            // counter-rotate is the off-disc edge-ARROW (_markerOffDisc), which sets its own
            // localRotation = angleDeg in UpdatePlayerMarker and must keep riding the container's +rotZ
            // so the composed angle (rotZ + angleDeg) points at the player's real bearing (AT-MODAL-MARKER-3).
            if (_playerMarker != null && !_markerOffDisc)
                _playerMarker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -rotZ);

            ApplyCompassNorth(rotZ);
        }

        /// <summary>
        /// Compass north ring (card t_fb53c9e4, M1) — the per-frame iron/bronze tint + the orbiting N.
        /// Driven off <c>_compassWorn</c> (pushed by the Iron Compass feature via SetCompassNorth), called
        /// from <see cref="ApplyFieldOrientation"/> so it shares the same <paramref name="rotZ"/> the map
        /// container and player marker ride.
        ///
        /// (a) BEZEL TINT — Path a (§3.4): a per-frame write to the RawImage <c>_bezel.color</c>. The bezel
        /// texture is a white-bronze band, so <c>Color.white</c> shows native bronze and <c>CIronTint</c>
        /// multiplies it to iron. We NEVER mutate the cached <c>_bezelTex</c> (that would corrupt the
        /// shared bronze constant). The bezel is a child of the NON-rotating <c>_frame</c>, so a recolor
        /// is rotation-invariant — correct, an iron ring reads identically at every heading.
        ///
        /// (b) THE N + TICKS — toggle the orbiting <c>_northLayer</c> on the same gate, then counter-rotate
        /// each child by <c>−rotZ</c> so the letters stay upright while the layer itself rides the
        /// container's <c>+rotZ</c> to world-north (the player-marker idiom). Face north → N at 12 o'clock;
        /// face east → N orbits to 9 o'clock; the letter never tilts.
        /// </summary>
        private void ApplyCompassNorth(float rotZ)
        {
            // (a) The iron/bronze bezel recolor — gated, reverts to bronze when the compass comes off.
            //     Write only when the bezel is the live ring (circular: disc + FieldReadOnly modal). On the
            //     square TableEdit view the bezel is deactivated (UpdateFrameForMode) — nothing to tint.
            bool bezelShown = _bezel != null && _bezel.gameObject.activeSelf;
            if (_bezel != null)
                _bezel.color = _compassWorn ? CIronTint : Color.white;

            // (b) The orbiting N + ticks. The N is the rim-of-the-iron-band marker, so it shows exactly
            //     where that band is a visible ring: gate it on (worn AND the bezel is shown). This couples
            //     the N to the iron ring as ONE element — both appear on the disc + the FieldReadOnly
            //     full-map modal (Daniel ④), and both stay off on the square TableEdit pin-editing view
            //     (where the bezel is hidden and a floating rim-N would have no ring to sit on). 🟡 The spec
            //     says "disc AND modal" without splitting FieldReadOnly vs TableEdit; tying the N to the
            //     bezel's own circular gate is the visually-coherent reading — flagged in the PR.
            if (_northLayer == null) return;
            bool showNorth = _compassWorn && bezelShown;
            if (_northLayer.gameObject.activeSelf != showNorth)
                _northLayer.gameObject.SetActive(showNorth);
            if (!showNorth) return;

            // The layer rides _mapContainer's +rotZ (it's a child) → carried to world-north for free.
            // Counter-rotate each glyph by −rotZ so "N" and the ticks read upright at every heading.
            var counter = Quaternion.Euler(0f, 0f, -rotZ);
            for (int i = 0; i < _northLayer.childCount; i++)
                _northLayer.GetChild(i).localRotation = counter;
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

        /// <summary>
        /// biome-indicator-impl-spec §3.4 — the ONE shared biome-name composition (AT-BIOME-SHARED).
        /// Returns the localized CURRENT-biome name (player position) or <c>null</c> when it can't be
        /// resolved. BOTH surfaces call this exact static method: the disc caption (<see cref="UpdateCaption"/>,
        /// §3.1) and the modal label (<see cref="UpdateBiomeLabel"/>, §3.2) — there is no divergent second
        /// biome path.
        ///
        /// Vanilla construction (decomp <c>Minimap.UpdateBiome</c>, base game — fair to read+adapt):
        /// <c>Localize("$biome_" + biome.ToString().ToLower())</c>. <c>Player.GetCurrentBiome()</c> is the
        /// proven call already used at <c>SunstoneLens.cs:351</c> — the cached single-biome value, safe to
        /// <c>.ToString()</c>. <c>static</c> because it reads only global state (<c>Player.m_localPlayer</c>,
        /// <c>Localization.instance</c>) — no instance fields, so the disc instance and the modal instance
        /// provably share one path.
        ///
        /// Two literal-leak guards (§3.5 — the 2026-06-05 sign-bug class, a <c>$token</c> reaching the player
        /// as raw text): (1) <c>Biome.None</c> → null (<c>None=0</c> has no <c>$biome_none</c> token); (2)
        /// unlocalized passthrough → null (<c>Localization.Localize</c> returns the input unchanged when a
        /// token is unknown, so an unmapped/future biome comes back as the literal <c>"$biome_xxx"</c> —
        /// <see cref="MapCaptionText.IsUnresolvedBiomeToken"/> catches it). Returns the human-readable name
        /// ("Meadows", "Black Forest", "Mistlands"), never the token. No <c>Minimap</c> access, no vanilla
        /// <c>m_biomeName*</c> mutation — we read player state and paint our own <c>Text</c> (nomap enforced).
        /// </summary>
        private static string? CurrentBiomeNameOrNull()
        {
            var player = Player.m_localPlayer;
            if (player == null) return null;

            Heightmap.Biome biome = player.GetCurrentBiome();
            if (biome == Heightmap.Biome.None) return null; // §3.5 — no $biome_none token exists

            var loc = Localization.instance;
            if (loc == null) return null;

            string text = loc.Localize("$biome_" + biome.ToString().ToLower());
            // §3.5 — unknown token comes back as the literal "$biome_xxx"; treat as null so it never leaks.
            return MapCaptionText.IsUnresolvedBiomeToken(text) ? null : text;
        }

        /// <summary>
        /// biome-indicator-impl-spec §3.2/§4.3 — the MODAL's fixed current-biome readout under the title.
        /// Sibling of <see cref="UpdateTitle"/>, called from <see cref="Render"/>. Sets the label text from
        /// <see cref="CurrentBiomeNameOrNull"/> (the shared §3.4 path) and hides the label when null so a
        /// pre-spawn / <c>Biome.None</c> frame shows nothing rather than an empty bar or a <c>$biome_*</c>
        /// literal (AT-BIOME-NONE-OMIT). DISC-safe: <c>_biomeLabel</c> is built only in the modal
        /// <c>ShowPrompts</c> path, so the null guard makes this a no-op on the disc even though
        /// <c>Render</c> is shared (the disc's biome rides <c>_discCaption</c> instead).
        /// </summary>
        private void UpdateBiomeLabel()
        {
            if (_biomeLabel == null) return;
            string? biome = CurrentBiomeNameOrNull();
            _biomeLabel.gameObject.SetActive(biome != null);
            _biomeLabel.text = biome ?? string.Empty;
        }

        /// <summary>
        /// §3.4/§3.6 disc-name-hint-impl-spec (+ biome-indicator-impl-spec §3.1/§4.2): refresh the under-disc
        /// caption — now a THREE-line stack, top→bottom: the per-provider map NAME line (<c>_req.Caption</c> =
        /// FormatDisplayName, set by DriveMinimapDisc), the CURRENT-biome NAME line (live from
        /// <see cref="CurrentBiomeNameOrNull"/>), and a STATIC localized <c>[&lt;$KEY_Map&gt;] $piece_readmap</c>
        /// hint line. Disc-only (no-op when the caption wasn't built, i.e. ShowCaption=false). The hint is
        /// re-localized every Render (like UpdateExitPrompt/UpdateTitle) so a mid-session Map rebind shows the
        /// new key live (AT-REBIND-CORRECT). The name and biome lines are each conditionally OMITTED when
        /// absent (<see cref="MapCaptionText.ComposeDiscCaption"/>) — a missing name never renders an empty
        /// "Local map for " tail (§3.4 AT-MAPNAME-BLANK); a missing/None biome omits the middle line entirely
        /// (AT-BIOME-NONE-OMIT), never a <c>$biome_none</c> literal. The _captionLastText guard skips the
        /// redundant Text.text write (+ layout rebuild) on unchanged 0.25 s re-binds; because the text now
        /// varies with the player's biome, a biome-border crossing changes <c>text</c> so the guard correctly
        /// repaints (one Text.text set per crossing — rare, desired). Per-row font sizes via rich-text
        /// &lt;size&gt; tags (§4.2: a single Text with \n keeps the rows glued).
        /// </summary>
        private void UpdateCaption()
        {
            if (_discCaption == null) return;
            var loc = Localization.instance;

            string hint = loc != null ? loc.Localize(CaptionHintRaw) : CaptionHintRaw;

            string? rawName = _req.Caption;
            string? nameLoc = string.IsNullOrEmpty(rawName)
                ? null
                : (loc != null ? loc.Localize(rawName) : rawName!);

            // The ONE shared biome path (§3.4) — same static helper the modal label uses (AT-BIOME-SHARED).
            string? biome = CurrentBiomeNameOrNull();

            // Pure assembly + literal-guard live in the engine-free MapCaptionText (link-compiled into the
            // headless test suite). name / biome / hint, each conditionally included; hint always present.
            string text = MapCaptionText.ComposeDiscCaption(
                nameLoc, biome, hint,
                (int)CaptionNameFontPx, (int)CaptionBiomeFontPx, (int)CaptionHintFontPx);

            if (text == _captionLastText) return; // skip redundant set + layout rebuild on unchanged re-binds
            _captionLastText = text;
            _discCaption.text = text;
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

            // §4.2 / §2E.5.7: thresholds scale with the target edge (fraction-of-radius), not absolute
            // px — and they come from the SHARED DiscRingGeometry helper so the ring hole and the
            // cartography mesh (LayoutMapRect → RectEdge) are sized from ONE formula set and can never
            // drift apart (the gap §2E.5.7 closed). holeR sits just INSIDE meshR (= TargetPx/2), so the
            // ring's inner edge covers the ~insetPx content-under-ring overdraw.
            float holeR      = DiscRingGeometry.HoleRadius(_cfg.TargetPx);
            float ringOuterR = DiscRingGeometry.RingOuterRadius(_cfg.TargetPx);
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

            // Compass north ring (card t_fb53c9e4, M1): the N-glyph + ticks chevron-sibling. Parented to
            // the ROTATING _mapContainer (NOT the fixed _frame the bezel rides) so it ORBITS to world-north
            // for free off the container's per-frame rotZ. Built here (hidden); toggled + counter-rotated
            // per-frame in ApplyFieldOrientation off _compassWorn.
            BuildNorthLayer();

            if (_cfg.ShowPrompts) BuildPrompts();
            if (_cfg.ShowCaption) BuildCaption();

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

            // biome-indicator-impl-spec §3.2/§4.3: the fixed current-biome readout, a SUBTITLE directly
            // under the title cartouche. Same (0.5,1) top-centre anchor/pivot as the title, one title-row
            // down (anchoredPosition ≈ (0,-84)); smaller than the title (ModalBiomeFontPx 22 vs 34) so it
            // reads as a subtitle, not a competing headline. Mirrors the disc's name-over-biome order →
            // one centred identity column on BOTH surfaces (AT-BIOME-SHARED is visual, not just code).
            // SetActive(false) initially; UpdateBiomeLabel() toggles it from CurrentBiomeNameOrNull().
            // -40 (title top) - 44 (≈ one title line) ≈ -84; a calibration knob (Daniel's eyeball tunes).
            var biomeGo = new GameObject("biomeLabel");
            biomeGo.transform.SetParent(_root.transform, false);
            _biomeLabel = biomeGo.AddComponent<Text>();
            _biomeLabel.font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                               ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _biomeLabel.fontSize = (int)ModalBiomeFontPx;
            _biomeLabel.alignment = TextAnchor.UpperCenter;
            _biomeLabel.color = new Color(1f, 0.95f, 0.8f, 0.95f); // title's warm tint (slightly softer)
            _biomeLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            _biomeLabel.verticalOverflow = VerticalWrapMode.Overflow;
            _biomeLabel.raycastTarget = false;
            var blRt = _biomeLabel.rectTransform;
            blRt.anchorMin = new Vector2(0.5f, 1f);
            blRt.anchorMax = new Vector2(0.5f, 1f);
            blRt.pivot = new Vector2(0.5f, 1f);
            blRt.anchoredPosition = new Vector2(0f, -84f);
            blRt.sizeDelta = new Vector2(1200f, 40f);
            var biomeOutline = biomeGo.AddComponent<Outline>();
            biomeOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            biomeOutline.effectDistance = new Vector2(1.5f, -1.5f);
            _biomeLabel.gameObject.SetActive(false);
        }

        /// <summary>
        /// §3.1/§3.4 disc-name-hint-impl-spec (+ biome-indicator-impl-spec §3.1): build the under-disc
        /// caption STACK (disc only — gated on <c>ShowCaption</c>). Text content (now up to three lines:
        /// name / biome / hint) is filled by <see cref="UpdateCaption"/> each Render; this just builds the
        /// element.
        ///
        /// PARENT = <c>_frame</c> (NOT <c>_mapContainer</c>). The spec §4.2 recommended <c>_root</c>
        /// "(NOT the rotating <c>_frame</c>)", but that premise is incorrect against the actual code:
        /// only <c>_mapContainer</c> rotates (ApplyFieldOrientation) — <c>_frame</c> is the NON-rotating
        /// corner-anchored host the fixed bezel itself rides ("child of the NON-rotating frame so it
        /// never spins", EnsureBuilt). Parenting the caption to <c>_frame</c> therefore (a) is screen-
        /// stable exactly like the bezel (AT-CAPTION-NO-ROTATE holds — verified non-rotating), and
        /// (b) derives the caption's position from the disc's OWN layout (frame-centre = disc-centre,
        /// one source of truth — the §3.1 route-A intent) instead of re-deriving the corner inset on
        /// <c>_root</c>. Same screen-stability guarantee the spec wanted; cleaner geometry. Flagged in
        /// the PR so the reviewer sees the deliberate spec-deviation + rationale.
        ///
        /// The caption TOP sits just below the visible disc bottom: <c>-(TargetPx*0.5 + BezelRingMinPx
        /// + CaptionGapPx)</c> — the disc radius plus the bronze ring's floor width plus the tunable
        /// gap, so it clears the bezel ring and never collides with the on-FACE chevron
        /// (AT-DISC-MARKER-1). UpperCenter + HorizontalWrapMode.Overflow → a long name extends
        /// symmetrically rather than clipping (§3.4). raycastTarget=false (passive HUD); an Outline
        /// keeps it legible over the live game world (the disc has no backdrop), mirroring the pin
        /// labels' idiom.
        /// </summary>
        private void BuildCaption()
        {
            if (_frame == null) return;

            var capGo = new GameObject("discCaption");
            capGo.transform.SetParent(_frame.transform, false);
            _discCaption = capGo.AddComponent<Text>();
            _discCaption.font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                                ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _discCaption.supportRichText = true;            // per-row <size> tags (name 18 / hint 16)
            _discCaption.fontSize = (int)CaptionNameFontPx; // base; <size> tags override per span
            _discCaption.alignment = TextAnchor.UpperCenter;
            _discCaption.color = new Color(1f, 0.95f, 0.8f, 0.97f);
            _discCaption.horizontalOverflow = HorizontalWrapMode.Overflow; // long names extend, don't clip (§3.4)
            _discCaption.verticalOverflow = VerticalWrapMode.Overflow;
            _discCaption.raycastTarget = false;

            var crt = _discCaption.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f); // disc centre (frame-local)
            crt.pivot = new Vector2(0.5f, 1f);                       // y = the caption's TOP edge
            // TOP just below the visible disc bottom: radius + ring floor + tunable gap. Derived from
            // the same constants the bezel uses, so it tracks the disc across the modal/disc TargetPx.
            float capTopY = -(_cfg.TargetPx * 0.5f + DiscRingGeometry.BezelRingMinPx + CaptionGapPx);
            crt.anchoredPosition = new Vector2(0f, capTopY);
            crt.sizeDelta = new Vector2(420f, CaptionNameFontPx + CaptionBiomeFontPx + CaptionHintFontPx + 16f);

            var outline = capGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
        }

        /// <summary>
        /// Compass north ring (card t_fb53c9e4, M1): build the N-glyph + cardinal ticks chevron-sibling.
        ///
        /// 🔴 PARENT = <c>_mapContainer</c> (the ROTATING interior), NOT the fixed <c>_frame</c> the bezel
        /// rides. This is the load-bearing parent split (§3.2): the iron BEZEL is a recolor that looks
        /// identical at every rotation, so it stays on the non-rotating <c>_frame</c>; the N glyph must
        /// ORBIT to follow world-north, so it rides <c>_mapContainer</c>'s per-frame <c>rotZ</c>. Same
        /// gate (<c>_compassWorn</c>), different parents. Pin N to <c>_frame</c> by mistake → a dead,
        /// non-orbiting glyph.
        ///
        /// Layout: <c>_northLayer</c> is a zero-size pivot coincident with <c>_mapContainer</c>'s centre.
        /// The N sits at container-local <c>(0, +HoleRadius)</c> = local-UP; the three ticks at the other
        /// cardinals (E/S/W) at the same radius. The container's <c>rotZ</c> sweeps the whole layer to the
        /// world bearing for free (zero yaw math); <see cref="ApplyFieldOrientation"/> counter-rotates each
        /// child by <c>−rotZ</c> so the letters stay upright (the player-marker idiom, :1098). The radius is
        /// bound SYMBOLICALLY to <c>DiscRingGeometry.HoleRadius(_cfg.TargetPx)</c> (the post-#213 single
        /// source of truth) — NEVER a literal — so it tracks the iron band on both the 200 px disc and the
        /// 900 px modal and follows any future margin tune. Built additively (new GameObject + Text,
        /// ADR-0006); starts hidden, toggled by the compass gate. raycastTarget=false (passive overlay).
        /// </summary>
        private void BuildNorthLayer()
        {
            if (_mapContainer == null) return;

            // The orbiting layer: a zero-size pivot at the container centre. Rides _mapContainer's rotation.
            var layerGo = new GameObject("compassNorth");
            layerGo.transform.SetParent(_mapContainer.transform, false);
            _northLayer = layerGo.AddComponent<RectTransform>();
            _northLayer.anchorMin = _northLayer.anchorMax = new Vector2(0.5f, 0.5f);
            _northLayer.pivot = new Vector2(0.5f, 0.5f);
            _northLayer.sizeDelta = Vector2.zero;

            // 🔴 Z-ORDER (card t_3f7f3a0f): lift the N + ticks IN FRONT of the iron bezel. The layer is parented
            // under _mapContainer (sibling 0 of _frame) while _bezel is sibling 1, so by uGUI's depth-first
            // sibling paint order the ENTIRE _mapContainer subtree — this layer included — draws BENEATH the
            // bezel and the iron band occludes the N. We can't fix it by reordering siblings (the map image
            // MUST stay below the bezel, which hard-alpha-clips the map edge at :1527) and we won't reparent
            // the N off _mapContainer (that's the orbit-for-free idiom documented above). Instead give
            // _northLayer its OWN nested Canvas with overrideSorting and a sortingOrder one above THIS surface's
            // canvas: it lifts only the N + ticks above the bezel while everything else stays put, and the
            // layer keeps riding _mapContainer's +rotZ (a Canvas component does not decouple transform
            // inheritance), so the orbit and the per-child −rotZ counter-rotation are completely unchanged.
            // 🔴 RELATIVE order (_cfg.SortingOrder + 1), NOT a hardcoded 5000: the disc's N (3000+1) must still
            // sort BELOW the modal surface (5000) when both exist, otherwise a global value would punch the
            // disc N through the modal. Precedent: the surface canvas sorts at _cfg.SortingOrder (:1465);
            // SignPaintPanel.cs:201 / MarkerSignPanel.cs:175 use sortingOrder to layer overlay UI. Every glyph
            // is raycastTarget=false (MakeNorthLabel), so this passive overlay needs no GraphicRaycaster.
            var northCanvas = layerGo.AddComponent<Canvas>();
            northCanvas.overrideSorting = true;
            northCanvas.sortingOrder = _cfg.SortingOrder + 1;

            // Radius = the bezel hole (on/just inside the iron band). Bound to the helper, never a literal.
            float r = DiscRingGeometry.HoleRadius(_cfg.TargetPx);
            float glyphPx = Mathf.Max(10f, r * NorthGlyphFontFrac);
            float tickPx  = Mathf.Max(8f,  r * NorthTickFontFrac);

            // N at local-UP (0, +r) — the world-north anchor. The big, high-contrast letter.
            _northGlyph = MakeNorthLabel("N", (int)glyphPx, new Vector2(0f, r));
            // Cardinal ticks at the other three rim positions so the ring reads as an oriented compass
            // (a thin "·" mark, dimmer than the N). They ride the same _northLayer + counter-rotate idiom.
            MakeNorthLabel("\u00b7", (int)tickPx, new Vector2(r, 0f));    // E
            MakeNorthLabel("\u00b7", (int)tickPx, new Vector2(0f, -r));   // S
            MakeNorthLabel("\u00b7", (int)tickPx, new Vector2(-r, 0f));   // W

            // Start hidden — the compass gate (ApplyFieldOrientation, off _compassWorn) shows it when worn.
            _northLayer.gameObject.SetActive(false);
        }

        /// <summary>
        /// Build one north-ring label (the N or a cardinal tick) as a child of <c>_northLayer</c> at a fixed
        /// container-local position. Mirrors the caption/pin-label text idiom (VanillaUISkin font + Outline
        /// for legibility over the live world; raycastTarget=false). The pivot is centred so the per-frame
        /// <c>−rotZ</c> counter-rotation in <see cref="ApplyFieldOrientation"/> spins it about its own centre.
        /// </summary>
        private Text MakeNorthLabel(string glyph, int fontPx, Vector2 localPos)
        {
            var go = new GameObject("north_" + glyph);
            go.transform.SetParent(_northLayer!.transform, false);

            var txt = go.AddComponent<Text>();
            txt.font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                       ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.fontSize = Mathf.Max(8, fontPx);
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.text = glyph;
            txt.color = CNorthGlyph;
            txt.raycastTarget = false;

            var rt = txt.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = localPos;
            rt.sizeDelta = new Vector2(fontPx * 2f + 8f, fontPx * 2f + 8f);

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            return txt;
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
