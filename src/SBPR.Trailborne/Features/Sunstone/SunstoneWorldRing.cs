// ============================================================================
//  Trailborne v3 (Swamp) — Sunstone Lens WORLD-SPACE eidetic head-halo
// ----------------------------------------------------------------------------
//  Design   : docs/design/sunstone-lens-trophy-ring.md (card t_68672b6b → t_d17d9b58, LOCKED)
//             §Q2/§1/§3/§4/§5 (the world-space reversal), §6 (Rune-Magic behavioral ref).
//  Mechanic : SunstoneLens.GatherHostiles + SunstoneProjection.Project (UNCHANGED — this
//             file only RENDERS the resulting ThreatBlips in world space).
//
//  The DIEGETIC render surface for the Sunstone Lens' standalone (no-minimap) detection:
//  a head-centric halo of billboarded creature TROPHIES floating in the 3D world around
//  the player at their REAL bearings. Supersedes the screen-space camera-relative radar.
//
//  The four LOCKED knobs (design §5, Daniel "just implement as directed"):
//    1. Occlusion → head-halo "Rune Magic dodge": trophies float in a tight halo around the
//       eye-point, rarely occluded; NO through-terrain material (honest depth).
//    2. Geometry → head-centric halo, variable radius AND scale ∝ distance:
//         u      = Clamp01(dist / DetectRadius)
//         radius = Lerp(HaloRadiusMin, HaloRadiusMax, u)   // near = inner (by your face), far = outer
//         scale  = Lerp(HaloScaleMax,  HaloScaleMin,  u)   // near = big, far → shrinks toward nothing
//       Placed on the REAL dir = blip.WorldPos - eye → camera-relative by construction (thesis
//       guard holds; NO SignedAngle, NO north frame injected).
//    3. Trophy-less → hybrid (remap table + generic fallback + startup dump) — all in
//       SunstoneProjection (the shared derivation); this surface just reads blip.Trophy ?? glyph.
//    4. Trophy render → FLAT billboarded sprites (the existing m_icons[0]) on a world-space quad,
//       NOT the 3D attach mesh. Reuses the exact sprite + star-pip + tint path.
//
//  Render substrate decision (engineer's call, design §1.2 "OR the equivalent world-space-canvas
//  Image; engineer's call"): each slot is a world-space uGUI Canvas (RenderMode.WorldSpace) +
//  Image, carrying the vanilla Billboard component (m_vertical=true) for camera-facing. This
//  maximizes reuse of the proven Image/sprite/preserveAspect/tint/star-pip path (the screen ring's
//  own code), gets the unlit UI shader automatically (legible in the dark Swamp; no Shader.Find
//  risk), and renders atlas sub-sprites + preserveAspect natively — vs hand-rolling a quad mesh +
//  UV/material that can't be GPU-verified headless.
//
//  Clean-side (ADR-0001): reads base-game Character/Billboard/GameCamera + the shared
//  SunstoneProjection only. Billboard is base-game (decomp Billboard:99987-100021, m_vertical=true,
//  LateUpdate LookAt(GetMainCamera())) — fair to read+adapt. ADR-0006: every slot is a
//  new GameObject() + AddComponent (Canvas/Image/Billboard), carrying NO ZNetView/Piece/networked
//  skeleton — purely cosmetic, client-local. Trophy/star sprites are READ as blueprint (reading an
//  asset is not cloning). NOT a clone-and-strip of a vanilla prefab.
//
//  #209 invariant: this class owns ONLY the visuals. The host MonoBehaviour
//  (SunstoneLensHudOverlay) stays active and keeps pumping the sweep; visibility toggles the
//  _worldContent CHILD root, NEVER the host. The world slots live in WORLD space (a root scene
//  GameObject), NOT under Hud.m_rootObject.
//
//  logs-green ≠ playable — Daniel verifies AT-EIDETIC-* in-game on a GPU client.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Sunstone
{
    /// <summary>
    /// The world-space billboarded trophy halo. Owned by <see cref="SunstoneLensHudOverlay"/> (the
    /// pump owner); driven each frame via <see cref="Render"/> when the standalone ring should show.
    /// Not a MonoBehaviour — the overlay's Update is the single pump (#209). The per-slot Billboard
    /// components ARE MonoBehaviours and self-drive their facing once their GameObject is active.
    /// </summary>
    public sealed class SunstoneWorldRing
    {
        // ── World-space halo tuning (single source of truth; Plugin binds ConfigEntry mirrors so
        //    Daniel converges feel on a joined client without a rebuild — the banner-windsock pattern).
        //    Defaults per design §4. Radii in world metres; scales in world units (trophy quad size). ──
        public const float DefaultHaloRadiusMin = 1.2f;   // inner radius — near enemies, close to your face
        public const float DefaultHaloRadiusMax = 3.0f;   // outer radius — far enemies push out
        public const float DefaultHaloScaleMax  = 0.6f;   // trophy world-size for the nearest enemy (big)
        public const float DefaultHaloScaleMin  = 0.12f;  // trophy world-size at the detection edge (shrinks)
        public const float DefaultHaloEyeOffsetY = 0f;    // lift the halo plane off the eye-point (clear the crosshair)

        // The world-space Canvas is authored at this reference pixel size, then scaled to world units
        // via localScale (worldSize / ReferencePx). 256 matches the procedural sprite resolution.
        private const float ReferencePx = 256f;
        private const float StarPipPx   = 44f;   // star-pip size in canvas pixels (proportional to the 256px trophy)
        private const float StarRowPad  = 12f;   // gap above the trophy for the star row (canvas pixels)

        // The world-space content root (a plain scene GameObject; NOT under Hud.m_rootObject). Toggled
        // for visibility; the host MonoBehaviour stays active so its Update keeps pumping (#209).
        private GameObject? _worldContent;
        private readonly List<Slot> _slots = new List<Slot>();
        private bool _disposed;

        /// <summary>One pooled world-space slot: a billboarded Canvas with a trophy Image + star pips.</summary>
        private sealed class Slot
        {
            public GameObject Go = null!;            // carries Canvas + Billboard; placed + scaled per frame
            public Canvas Canvas = null!;
            public RectTransform Rt = null!;
            public Image Trophy = null!;
            public RectTransform StarRow = null!;
            public readonly List<Image> Pips = new List<Image>();
            public Text? PipText;                    // Unicode-★ fallback when no vanilla star sprite resolved
        }

        /// <summary>True once the content root exists (built lazily on first <see cref="Render"/>).</summary>
        public bool Built => _worldContent != null;

        /// <summary>Idempotently build the world-content root. Safe to call repeatedly.</summary>
        private void EnsureBuilt()
        {
            if (_worldContent != null || _disposed) return;
            // A root scene object at the origin (identity/one) so each slot's world position == the
            // value we assign. Never reparented under the Hud canvas (that would apply screen-space
            // scale to world geometry). DontDestroyOnLoad is NOT used — it should die with the world;
            // the overlay's OnDestroy calls Dispose on logout/Hud teardown.
            _worldContent = new GameObject("SBPR_SunstoneWorldHalo");
            _worldContent.transform.position = Vector3.zero;
            _worldContent.transform.rotation = Quaternion.identity;
            _worldContent.transform.localScale = Vector3.one;
            _worldContent.SetActive(false);
        }

        /// <summary>
        /// Render the head-halo for this frame's blips. <paramref name="eye"/> is the player's
        /// eye-point (<c>Character.GetEyePoint()</c>); <paramref name="blips"/> is the shared
        /// projection (already aggro-tinted, trophy-resolved, star-counted). Pools + reuses slots;
        /// caps at <paramref name="maxIcons"/> showing the nearest N. Camera-relative by construction
        /// (each trophy sits on the real <c>blip.WorldPos - eye</c> bearing — thesis guard).
        /// </summary>
        public void Render(
            Vector3 eye,
            IReadOnlyList<ThreatBlip> blips,
            float detectRadius,
            float haloRadiusMin, float haloRadiusMax,
            float haloScaleMax,  float haloScaleMin,
            float eyeOffsetY,
            int maxIcons)
        {
            if (_disposed) return;
            EnsureBuilt();
            if (_worldContent == null) return;

            var cam = Utils.GetMainCamera();
            if (cam == null) { SetVisible(false); return; }

            SetVisible(true);

            Vector3 anchor = eye + Vector3.up * eyeOffsetY;
            float dr = Mathf.Max(1f, detectRadius);
            int cap = Mathf.Max(0, maxIcons);

            // Sort nearest-first so the cap keeps the most relevant threats (a horde shows the closest N).
            // The caller hands us the live blip list; copy indices we can sort without mutating it.
            _sortScratch.Clear();
            for (int i = 0; i < blips.Count; i++)
            {
                var b = blips[i];
                if (b.Character == null) continue;
                _sortScratch.Add(b);
            }
            _sortScratch.Sort((a, b) =>
            {
                float da = (a.WorldPos - anchor).sqrMagnitude;
                float db = (b.WorldPos - anchor).sqrMagnitude;
                return da.CompareTo(db);
            });

            int shown = 0;
            for (int i = 0; i < _sortScratch.Count && shown < cap; i++)
            {
                var blip = _sortScratch[i];

                Vector3 dir = blip.WorldPos - anchor;
                float dist = dir.magnitude;
                if (dist < 0.0001f) continue;                 // on top of the eye-point — skip (no stable bearing)
                Vector3 dirN = dir / dist;

                float u      = Mathf.Clamp01(dist / dr);       // 0 at the player, 1 at the detection edge
                float radius = Mathf.Lerp(haloRadiusMin, haloRadiusMax, u);
                float scale  = Mathf.Lerp(haloScaleMax,  haloScaleMin,  u);

                // Head-halo placement: along the REAL bearing, at the head-centric radius. The Billboard
                // component handles facing — no manual angle math, no SignedAngle, no north frame.
                Vector3 pos = anchor + dirN * radius;

                var slot = EnsureSlot(shown);
                slot.Go.transform.position = pos;
                slot.Go.transform.localScale = Vector3.one * (scale / ReferencePx);
                ApplySlot(slot, blip);
                slot.Go.SetActive(true);
                shown++;
            }

            // Park the unused tail.
            for (int i = shown; i < _slots.Count; i++)
                if (_slots[i].Go.activeSelf) _slots[i].Go.SetActive(false);
        }

        // Reused across frames to avoid per-frame allocation in the nearest-N sort.
        private readonly List<ThreatBlip> _sortScratch = new List<ThreatBlip>();

        /// <summary>Hide the whole halo (the content root toggles off; the host pump is untouched).</summary>
        public void Hide() => SetVisible(false);

        private void SetVisible(bool on)
        {
            if (_worldContent == null) return;
            if (_worldContent.activeSelf != on) _worldContent.SetActive(on);
        }

        /// <summary>Destroy the world-content root + pool. Called from the overlay's OnDestroy (logout / Hud teardown).</summary>
        public void Dispose()
        {
            _disposed = true;
            _slots.Clear();
            if (_worldContent != null)
            {
                Object.Destroy(_worldContent);
                _worldContent = null;
            }
        }

        // ───────────────────────────────────────────────
        // SLOT POOLING + RENDER
        // ───────────────────────────────────────────────

        private Slot EnsureSlot(int index)
        {
            while (_slots.Count <= index)
                _slots.Add(MakeSlot(_slots.Count));
            return _slots[index];
        }

        private Slot MakeSlot(int idx)
        {
            // The slot root: a world-space Canvas (its own RectTransform) + the vanilla Billboard so it
            // yaws to face the camera and stays upright (m_vertical=true). World size is driven by the
            // root's localScale (set per frame in Render); the Canvas is authored at ReferencePx.
            var go = new GameObject($"halo_slot_{idx}");
            go.transform.SetParent(_worldContent!.transform, worldPositionStays: false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            // No worldCamera/GraphicRaycaster: nothing here is interactive (every Image raycastTarget=false),
            // so the canvas renders via the scene cameras without an assigned event camera.

            var rt = go.GetComponent<RectTransform>();   // Canvas auto-adds a RectTransform
            rt.sizeDelta = new Vector2(ReferencePx, ReferencePx);
            rt.pivot = new Vector2(0.5f, 0.5f);

            // The vanilla camera-facing billboard (base-game, ADR-0001). m_vertical=true → yaws to the
            // camera, stays upright (the nameplate idiom). Reproduces no third-party code.
            var bb = go.AddComponent<Billboard>();
            bb.m_vertical = true;

            // Trophy image fills the canvas, centred.
            var trophyGo = new GameObject("trophy", typeof(RectTransform));
            trophyGo.transform.SetParent(rt, worldPositionStays: false);
            var trt = trophyGo.GetComponent<RectTransform>();
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = Vector2.zero;
            trt.sizeDelta = new Vector2(ReferencePx, ReferencePx);
            var trophy = trophyGo.AddComponent<Image>();
            trophy.raycastTarget = false;
            trophy.preserveAspect = true;

            // Star row above the trophy.
            var rowGo = new GameObject("stars", typeof(RectTransform));
            rowGo.transform.SetParent(rt, worldPositionStays: false);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin = rowRt.anchorMax = rowRt.pivot = new Vector2(0.5f, 0.5f);
            rowRt.anchoredPosition = new Vector2(0f, ReferencePx * 0.5f + StarRowPad);

            return new Slot { Go = go, Canvas = canvas, Rt = rt, Trophy = trophy, StarRow = rowRt };
        }

        private void ApplySlot(Slot slot, ThreatBlip blip)
        {
            // Trophy sprite (or the generic threat glyph for trophy-less hostiles) — from the shared
            // SunstoneProjection so the halo matches the disc + vanilla minimap exactly (the remap +
            // fallback live there, one copy). The aggro tint multiplies onto the sprite.
            slot.Trophy.sprite = blip.Trophy ?? SunstoneProjection.ThreatGlyph();
            slot.Trophy.color  = blip.Tint;

            RenderStars(slot, blip.Stars, blip.Tint);
        }

        private void RenderStars(Slot slot, int stars, Color tint)
        {
            var star = SunstoneProjection.StarSprite();

            if (star != null)
            {
                // Image pips using the real vanilla nameplate star art (design §1.5). The harvest +
                // 3★+ repeat policy live in SunstoneProjection; here we just lay out `stars` pips.
                if (slot.PipText != null) slot.PipText.gameObject.SetActive(false);

                float startX = -(stars - 1) * 0.5f * StarPipPx;
                for (int i = 0; i < stars; i++)
                {
                    Image img;
                    if (i < slot.Pips.Count) img = slot.Pips[i];
                    else
                    {
                        var pgo = new GameObject($"pip_{i}", typeof(RectTransform));
                        pgo.transform.SetParent(slot.StarRow, worldPositionStays: false);
                        var prt = pgo.GetComponent<RectTransform>();
                        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
                        prt.sizeDelta = new Vector2(StarPipPx, StarPipPx);
                        img = pgo.AddComponent<Image>();
                        img.raycastTarget = false;
                        img.preserveAspect = true;
                        slot.Pips.Add(img);
                    }
                    img.sprite = star;
                    img.color = tint;
                    img.rectTransform.anchoredPosition = new Vector2(startX + i * StarPipPx, 0f);
                    img.gameObject.SetActive(true);
                }
                for (int i = stars; i < slot.Pips.Count; i++)
                    slot.Pips[i].gameObject.SetActive(false);
            }
            else
            {
                // Fallback: a single Unicode ★ Text so the star count is never lost (degrade look, not info).
                for (int i = 0; i < slot.Pips.Count; i++) slot.Pips[i].gameObject.SetActive(false);
                if (slot.PipText == null && stars > 0)
                {
                    var font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                               ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                    var tgo = new GameObject("pip_text", typeof(RectTransform));
                    tgo.transform.SetParent(slot.StarRow, worldPositionStays: false);
                    var trt = tgo.GetComponent<RectTransform>();
                    trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
                    trt.sizeDelta = new Vector2(ReferencePx, StarPipPx);
                    slot.PipText = tgo.AddComponent<Text>();
                    slot.PipText.font = font;
                    slot.PipText.fontSize = 40;
                    slot.PipText.alignment = TextAnchor.MiddleCenter;
                    slot.PipText.horizontalOverflow = HorizontalWrapMode.Overflow;
                    slot.PipText.verticalOverflow = VerticalWrapMode.Overflow;
                    slot.PipText.raycastTarget = false;
                }
                if (slot.PipText != null)
                {
                    slot.PipText.gameObject.SetActive(stars > 0);
                    if (stars > 0)
                    {
                        slot.PipText.text = new string('\u2605', Mathf.Min(stars, 6));
                        slot.PipText.color = tint;
                    }
                }
            }
        }
    }
}
