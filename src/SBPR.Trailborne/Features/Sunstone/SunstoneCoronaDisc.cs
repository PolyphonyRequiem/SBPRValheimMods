// ============================================================================
//  Trailborne v3 (Swamp) — Sunstone Lens WORLD-SPACE pulsing sun-corona disc
// ----------------------------------------------------------------------------
//  Impl spec: docs/v3/planning/sunstone-lens-corona-impl-spec.md §2 (render arch),
//             §3 AT-CORONA-3D/ORIENT/PULSE/ART/SUBSTRATE/PUMP/CLEAN.
//  Card     : t_9d7c3dfe (graduates t_2d500d45 — Daniel /bug: "the ring itself is
//             just a screen space circle, not a 3d slowly pulsing 'sun corona' disc
//             like we discussed").
//  Pulse    : SunstoneCoronaPulse (engine-free, CI-gated — AT-CORONA-PULSE-MATH).
//
//  The DIEGETIC empty-state affordance for the Sunstone Lens: a glowing sun-corona disc
//  drawn in WORLD space, co-located with the trophy halo (SunstoneWorldRing), breathing
//  on a slow alpha pulse. REPLACES the flat screen-space annulus (SunstoneLensHudOverlay
//  ._emptyRing) the /bug report flagged. The corona is the world-space SUBSTRATE the
//  fixed-distance trophy halo orbits — "a sun on the floor with creature trophies
//  floating around it" — so the two read as ONE coherent world element.
//
//  SHARES SunstoneWorldRing's scene root (Knob #4 — replace, one lifecycle). This disc
//  parents UNDER the halo's SBPR_SunstoneWorldHalo root (via EnsureContentRoot), so a
//  single Hide()/Dispose() on the halo culls corona + trophies together (AT-CORONA-
//  SUBSTRATE). #209 invariant preserved: the host MonoBehaviour (the overlay) stays
//  active and keeps pumping; only this world-content CHILD toggles, NEVER the host.
//
//  Render substrate (mirrors SunstoneWorldRing's engineer's-call): a world-space uGUI
//  Canvas (RenderMode.WorldSpace) + Image carrying a procedural radial-glow sprite. The
//  unlit UI shader comes for free (legible in the dark Swamp; no Shader.Find risk), the
//  procedural sprite ships zero PNGs, and world size is driven by the disc root's
//  localScale — the exact proven path the trophy slots use, GPU-verifiable headless only
//  as "compiles," never as "renders" (the honesty rule below).
//
//  Two orientations (Knob #1, live-flippable, no rebuild):
//    • GroundPlane (default) — a flat XZ disc (Quaternion.Euler(90,0,0)) on the player's
//      feet (character-root + CoronaPlaneOffsetY). The "sun on the floor." No Billboard.
//    • CameraFacing — an upright disc carrying the vanilla Billboard (m_vertical=true,
//      the trophy-slot idiom) on the eye anchor. Yaws to face the camera, stays upright.
//
//  Clean-side (ADR-0001): reads base-game Character/Billboard/Player/Time only; the disc
//  + sprite are SBPR-authored. No vanilla UI cloned, no third-party mod code read. ADR-
//  0006: the disc is new GameObject() + AddComponent (Canvas/Image[/Billboard]), carrying
//  NO ZNetView/Piece/networked skeleton — purely cosmetic, client-local. The gold sprite
//  is generated procedurally (reading no asset). NOT a clone-and-strip of a vanilla prefab.
//
//  logs-green ≠ playable — this box renders nothing (Valheim shaders collapse headless).
//  AT-CORONA-3D/ORIENT/PULSE/ART/SUBSTRATE are Daniel's in-game confirms on a GPU client.
// ============================================================================

using UnityEngine;
using UnityEngine.UI;

namespace SBPR.Trailborne.Features.Sunstone
{
    /// <summary>
    /// The world-space pulsing sun-corona disc. Owned by <see cref="SunstoneLensHudOverlay"/> (the
    /// pump owner); driven each frame via <see cref="Render"/> while the lens is worn + charged.
    /// Not a MonoBehaviour — the overlay's Update is the single pump (#209). The optional per-disc
    /// Billboard (CameraFacing) IS a MonoBehaviour and self-drives its facing once its GameObject is
    /// active. Parents under <see cref="SunstoneWorldRing"/>'s shared scene root (one lifecycle).
    /// </summary>
    public sealed class SunstoneCoronaDisc
    {
        // ── Corona tuning defaults (single source of truth; Plugin binds ConfigEntry mirrors so Daniel
        //    converges the look on a joined client without a rebuild — the banner-windsock pattern, the
        //    SunstoneWorldRing.Default* precedent). DIRECTIONAL constants frozen from the architect's
        //    look-lock mock (spec §0/§4); Daniel's in-game eye on a GPU client is the final value. ──
        public const CoronaOrientation DefaultOrientation = CoronaOrientation.FeetGlow; // upright rising feet-glow (Daniel /bug 2026-06-24: flat disc clipped terrain + read flat)
        public const float DefaultPulseHz     = 0.25f;  // breaths/sec — one breath / 4 s (Knob #3 rate); 0 = steady glow
        public const float DefaultAlphaTrough = 0.10f;  // alpha at the breath trough (Knob #3 depth)
        public const float DefaultAlphaPeak   = 0.28f;  // alpha at the breath peak (around the 0.18 static baseline)
        public const float DefaultInnerFill   = 0.35f;  // [GroundPlane/CameraFacing radial sprite] 0 = thin hoop ↔ 1 = filled sun-disc (Knob #2)
        public const float DefaultThickness   = 0.45f;  // soft radiant-edge falloff width (radial rim AND the FeetGlow horizontal edge)
        public const float DefaultPlaneOffsetY = 0f;    // vertical lift off the anchor (ground/feet for GroundPlane+FeetGlow; eye for CameraFacing)
        // DefaultRadius mirrors the trophy HaloRadius so the corona's rim DEFAULT-tracks where the
        // trophies orbit (AT-CORONA-SUBSTRATE) — the disc IS the substrate. Independent live knob after.
        public const float DefaultRadius = SunstoneWorldRing.DefaultHaloRadius;

        // ── FeetGlow (upright rising vertical glow) tuning — the new default orientation. ──────────────
        // The vertical extent (height) the glow rises to off the feet, in world metres. The glow stands
        // UP this tall; alpha fades to 0 at the top (a soft dome) and at the ground (a soft meet → no
        // hard terrain-clip line). Directional; Daniel's eye on a GPU client is final (live-config).
        public const float DefaultHeight = 1.2f;
        // The height (world m) at which the rising glow reaches FULL width — Daniel's "full width around
        // .5m from the ground." fullWidthFrac = Clamp(Height bounded / FullWidthHeight) is derived in Render.
        public const float DefaultFullWidthHeight = 0.5f;
        // How NARROW the glow is at the ground contact, as a fraction of full width (a bright feet core
        // that blooms upward). 0.15 = a tight base flaring to full by FullWidthHeight.
        public const float DefaultBaseWidthFrac = 0.15f;

        // The corona is authored at this reference pixel size, then scaled to world units via the disc
        // root's localScale (worldDiameter / ReferencePx). 256 matches the procedural sprite resolution
        // and the trophy-slot ReferencePx — one convention across the shared root.
        private const float ReferencePx = 256f;

        // The shared world-content root the trophy halo owns (SBPR_SunstoneWorldHalo). The corona's own
        // objects parent under it so the halo's single Hide()/Dispose() culls the corona too. Held by
        // reference; re-resolved each Render in case the halo rebuilt its root (cheap, idempotent).
        private readonly SunstoneWorldRing _halo;

        private GameObject? _disc;      // the corona disc: Canvas + Image (+ optional Billboard)
        private RectTransform? _rt;
        private Image? _image;
        private Billboard? _billboard;  // present but disabled under GroundPlane; enabled under CameraFacing
        private bool _disposed;

        // Static cache of the procedural radial sprite (alpha-1 white; the gold tint + breathing alpha
        // are applied at draw via Image.color, so one texture serves every fill/thickness... EXCEPT the
        // fill/thickness shape the texture itself — so the cache is keyed on those two knobs below).
        private static Sprite? _coronaSprite;
        private static float _spriteInnerFill = -1f;
        private static float _spriteThickness = -1f;

        // Separate cache for the FeetGlow vertical-profile sprite (a DIFFERENT shape — the engine-free
        // SunstoneCoronaProfile rising glow, not the radial disc). Keyed on the three knobs that shape it.
        private static Sprite? _feetGlowSprite;
        private static float _feetFullWidthFrac = -1f;
        private static float _feetBaseWidthFrac = -1f;
        private static float _feetThickness = -1f;

        public SunstoneCoronaDisc(SunstoneWorldRing halo) { _halo = halo; }

        /// <summary>True once the disc object exists (built lazily on first <see cref="Render"/>).</summary>
        public bool Built => _disc != null;

        /// <summary>
        /// Render the corona for this frame. <paramref name="groundAnchor"/> is the player's feet /
        /// character-root world position (used for GroundPlane); <paramref name="eyeAnchor"/> is the
        /// eye-point (used for CameraFacing). The gold comes from <paramref name="gold"/> (the lens'
        /// CSolarRing RGB); the breathing alpha from <see cref="SunstoneCoronaPulse"/> on the shared
        /// <paramref name="time"/> phase (pass Time.time). Builds + re-anchors the disc; flipping the
        /// live <paramref name="orientation"/> re-orients next frame (no rebuild).
        /// </summary>
        public void Render(
            Vector3 groundAnchor,
            Vector3 eyeAnchor,
            CoronaOrientation orientation,
            float radius,
            float planeOffsetY,
            float innerFill,
            float thickness,
            float height,
            float fullWidthHeight,
            float baseWidthFrac,
            Color gold,
            double time,
            float hz,
            float trough,
            float peak)
        {
            if (_disposed) return;

            // Parent under the trophy halo's shared scene root (AT-CORONA-SUBSTRATE — one lifecycle).
            // Null only if the halo is disposed; then there's nothing to draw into.
            Transform? root = _halo.EnsureContentRoot();
            if (root == null) { SetActive(false); return; }

            EnsureBuilt(root);
            if (_disc == null || _rt == null || _image == null) return;

            // Keep the disc attached to the (possibly rebuilt) shared root.
            if (_disc.transform.parent != root)
                _disc.transform.SetParent(root, worldPositionStays: false);

            // ── Geometry: anchor + rotation + size per orientation (Knob #1). ───────────────────────
            if (orientation == CoronaOrientation.FeetGlow)
            {
                // DEFAULT. An upright, camera-facing VERTICAL glow standing UP out of the FEET. The
                // canvas pivot drops to bottom-centre (0.5, 0) so the transform origin sits at the
                // glow's GROUND-CONTACT point; the quad then rises upward from the feet anchor. The
                // vanilla Billboard yaws it to the camera and keeps it vertical (m_vertical=true), so
                // the base stays pinned at the feet while it always faces the player.
                _rt.pivot = new Vector2(0.5f, 0f);
                _disc.transform.position = groundAnchor + Vector3.up * planeOffsetY;
                if (_billboard != null && !_billboard.enabled) _billboard.enabled = true;

                // Non-uniform: WIDTH = 2·radius, HEIGHT = CoronaHeight. The canvas is authored square at
                // ReferencePx, so each axis maps reference px → its own world metres (the vertical profile
                // sprite is stretched into the W×H quad; v=0 = the soft ground meet, v=1 = the dome top).
                float wWidth  = Mathf.Max(0.01f, radius * 2f);
                float wHeight = Mathf.Max(0.01f, height);
                _disc.transform.localScale = new Vector3(wWidth / ReferencePx, wHeight / ReferencePx, 1f);

                // The vertical alpha profile (engine-free SunstoneCoronaProfile). fullWidthFrac = the
                // height (as a fraction of the quad) at which the glow blooms to full width — Daniel's
                // "full width around .5m" → fullWidthHeight / height. Shape-keyed; rebuilt only on change.
                float fullWidthFrac = wHeight <= 0.0001f ? 1f : fullWidthHeight / wHeight;
                _image.sprite = FeetGlowSprite(fullWidthFrac, baseWidthFrac, thickness);
            }
            else if (orientation == CoronaOrientation.GroundPlane)
            {
                // A flat XZ disc on the player's feet. Euler(90,0,0) lays the canvas (authored in its
                // local XY plane) flat into world XZ. No Billboard — it stays flat regardless of camera.
                // (SUPERSEDED as default — z-fights / hard-clips along the terrain line; kept selectable.)
                _rt.pivot = new Vector2(0.5f, 0.5f);
                _disc.transform.position = groundAnchor + Vector3.up * planeOffsetY;
                _disc.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                if (_billboard != null && _billboard.enabled) _billboard.enabled = false;

                float worldDiameter = Mathf.Max(0.01f, radius * 2f);
                _disc.transform.localScale = Vector3.one * (worldDiameter / ReferencePx);
                _image.sprite = CoronaSprite(innerFill, thickness);
            }
            else
            {
                // CameraFacing: an upright RADIAL disc on the eye anchor; the vanilla Billboard yaws it
                // to the camera and keeps it vertical (m_vertical=true — the trophy-slot idiom). Its
                // LateUpdate owns the rotation once enabled, so we only set position here.
                _rt.pivot = new Vector2(0.5f, 0.5f);
                _disc.transform.position = eyeAnchor + Vector3.up * planeOffsetY;
                if (_billboard != null && !_billboard.enabled) _billboard.enabled = true;

                float worldDiameter = Mathf.Max(0.01f, radius * 2f);
                _disc.transform.localScale = Vector3.one * (worldDiameter / ReferencePx);
                _image.sprite = CoronaSprite(innerFill, thickness);
            }

            // ── Pulse: the gold tint with the breathing alpha on the shared Time.time phase (Knob #3,
            //    engine-free SunstoneCoronaPulse — no drift, no jump on an orientation flip). The pulse
            //    is the OVERALL alpha multiplier; the per-pixel profile (radial or vertical) shapes WHERE
            //    the glow is, the pulse shapes HOW BRIGHT the whole thing breathes. ────────────────────
            float alpha = SunstoneCoronaPulse.AlphaAt(time, hz, trough, peak);
            _image.color = new Color(gold.r, gold.g, gold.b, alpha);

            SetActive(true);
        }

        /// <summary>Hide the corona (its disc child toggles off; the shared root + host pump untouched).</summary>
        public void Hide() => SetActive(false);

        /// <summary>
        /// Destroy the corona disc. Called from the overlay's OnDestroy (logout / Hud teardown). The
        /// shared scene root itself is the trophy halo's to dispose — we only drop our own child.
        /// </summary>
        public void Dispose()
        {
            _disposed = true;
            if (_disc != null)
            {
                Object.Destroy(_disc);
                _disc = null;
                _rt = null;
                _image = null;
                _billboard = null;
            }
        }

        private void SetActive(bool on)
        {
            if (_disc != null && _disc.activeSelf != on) _disc.SetActive(on);
        }

        // ───────────────────────────────────────────────
        // BUILD
        // ───────────────────────────────────────────────

        private void EnsureBuilt(Transform root)
        {
            if (_disc != null || _disposed) return;

            // The disc root: a world-space Canvas (its own RectTransform) + the corona Image. World size
            // is driven by localScale (set per frame in Render); the Canvas is authored at ReferencePx.
            var go = new GameObject("SBPR_SunstoneCorona");
            go.transform.SetParent(root, worldPositionStays: false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            // No worldCamera/GraphicRaycaster: nothing here is interactive (Image.raycastTarget=false),
            // so the canvas renders via the scene cameras without an assigned event camera (the trophy-
            // slot convention).

            var rt = go.GetComponent<RectTransform>();   // Canvas auto-adds a RectTransform
            rt.sizeDelta = new Vector2(ReferencePx, ReferencePx);
            rt.pivot = new Vector2(0.5f, 0.5f);

            // The vanilla camera-facing billboard (base-game, ADR-0001), present but DISABLED by default
            // (GroundPlane). Render() enables it under CameraFacing. m_vertical=true → yaws to the camera,
            // stays upright (the nameplate idiom the trophy slots use). Reproduces no third-party code.
            var bb = go.AddComponent<Billboard>();
            bb.m_vertical = true;
            bb.enabled = false;

            // The corona Image fills the canvas, centred. preserveAspect keeps the radial sprite circular.
            var imgGo = new GameObject("glow", typeof(RectTransform));
            imgGo.transform.SetParent(rt, worldPositionStays: false);
            var irt = imgGo.GetComponent<RectTransform>();
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
            irt.anchoredPosition = Vector2.zero;
            irt.sizeDelta = new Vector2(ReferencePx, ReferencePx);
            var img = imgGo.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;

            go.SetActive(false);

            _disc = go;
            _rt = rt;
            _image = img;
            _billboard = bb;
        }

        // ───────────────────────────────────────────────
        // PROCEDURAL RADIAL-GLOW SPRITE (Knob #2 — filled radial falloff, NOT the thin annulus)
        // ───────────────────────────────────────────────

        /// <summary>
        /// A procedurally-generated radial-glow sprite (no disk asset — the guarantee holds even if the
        /// modpack ships zero PNGs). White texture, tinted + alpha-pulsed at draw via Image.color.
        /// Shape is driven by <paramref name="innerFill"/> (0 = thin hoop ↔ 1 = filled sun-disc) and
        /// <paramref name="thickness"/> (the soft radiant-edge falloff width). Cached + rebuilt only
        /// when either knob changes (the live .cfg values rarely move).
        /// </summary>
        private static Sprite CoronaSprite(float innerFill, float thickness)
        {
            float fill = Clamp01(innerFill);
            float th   = Clamp01(thickness);
            if (_coronaSprite != null
                && Mathf.Approximately(_spriteInnerFill, fill)
                && Mathf.Approximately(_spriteThickness, th))
                return _coronaSprite;

            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];

            float cx = (size - 1) * 0.5f, cy = (size - 1) * 0.5f;
            float maxR = size * 0.5f - 1f;   // 1px guard so the rim feathers fully inside the texture

            // The radial alpha profile (AT-CORONA-ART). Endpoints:
            //   fill = 0 → opaque plateau collapses to nothing → only the rim band lights → a HOOP.
            //   fill = 1 → the plateau reaches the centre → a FILLED sun-disc.
            // The outer "radiant edge" always feathers from the plateau out to fully transparent at the
            // rim (the soft sun corona, never a hard cut). Computed per-pixel below.
            float rimStart = 1f - Mathf.Max(0.04f, th);            // outer soft edge begins here (0..1 of maxR)
            float coreEdge = (1f - fill) * rimStart;               // inner boundary of the opaque plateau
            float innerFeather = Mathf.Max(0.04f, rimStart * 0.18f); // soft inner edge width below the plateau

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / maxR;   // 0 at centre → 1 at maxR
                    float a;
                    if (r >= 1f)
                        a = 0f;
                    else if (r >= rimStart)
                        a = Clamp01((1f - r) / Mathf.Max(1e-4f, 1f - rimStart));   // radiant outer edge → 0 at rim
                    else if (r >= coreEdge)
                        a = 1f;                                                     // opaque plateau (the sun body)
                    else
                        a = Smooth01((r - (coreEdge - innerFeather)) / innerFeather); // soft inner edge below the core
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "SBPR_SunstoneCorona";
            _coronaSprite = sprite;
            _spriteInnerFill = fill;
            _spriteThickness = th;
            return sprite;
        }

        // ───────────────────────────────────────────────
        // PROCEDURAL FEET-GLOW SPRITE (the upright rising vertical glow — engine-free profile)
        // ───────────────────────────────────────────────

        /// <summary>
        /// A procedurally-generated VERTICAL-profile glow sprite for the FeetGlow orientation (no asset).
        /// The per-texel alpha is the engine-free <see cref="SunstoneCoronaProfile"/>: 0 at the ground
        /// (v=0, a soft meet → no hard terrain clip), a narrow bright core at the feet that blooms to full
        /// width by <paramref name="fullWidthFrac"/>, fading to a soft dome at the top (v=1) and to 0 at
        /// the horizontal rims. White texture; tinted gold + alpha-pulsed at draw via Image.color. Cached
        /// + rebuilt only when one of the three shape knobs changes (the live .cfg values rarely move).
        /// </summary>
        private static Sprite FeetGlowSprite(float fullWidthFrac, float baseWidthFrac, float thickness)
        {
            float fw = Clamp01(fullWidthFrac);
            float bw = Clamp01(baseWidthFrac);
            float th = Clamp01(thickness);
            if (_feetGlowSprite != null
                && Mathf.Approximately(_feetFullWidthFrac, fw)
                && Mathf.Approximately(_feetBaseWidthFrac, bw)
                && Mathf.Approximately(_feetThickness, th))
                return _feetGlowSprite;

            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];

            // Texel → normalized quad coords. u ∈ [0,1] across (x); v ∈ [0,1] up (y), where y=0 is the
            // BOTTOM row = the ground contact (the canvas pivot is bottom-centre at draw). The pure
            // SunstoneCoronaProfile owns the shape so it is CI-gated headless (AT-CORONA-FEET-PROFILE).
            float denom = size - 1;
            for (int y = 0; y < size; y++)
            {
                float v = y / denom;            // 0 at the ground row → 1 at the top row
                for (int x = 0; x < size; x++)
                {
                    float u = x / denom;        // 0 left rim → 1 right rim
                    float a = SunstoneCoronaProfile.AlphaAt(u, v, fw, bw, th);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "SBPR_SunstoneCoronaFeetGlow";
            _feetGlowSprite = sprite;
            _feetFullWidthFrac = fw;
            _feetBaseWidthFrac = bw;
            _feetThickness = th;
            return sprite;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        // Smoothstep (3t²-2t³) on a clamped [0,1] input — a soft edge with zero-slope endpoints, so the
        // inner falloff has no visible banding seam where it meets the opaque plateau.
        private static float Smooth01(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
