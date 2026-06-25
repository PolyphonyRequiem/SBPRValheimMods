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
//  WHAT THIS DRAWS (Model A — INFORMATIONAL, card scope). When the local player
//  stands on / near a Twisted Portal, a floating world-space label appears over
//  every nearby Twisted Portal showing its RUNE NAME (+ optional distance),
//  rendered THROUGH TERRAIN (a ZTest-Always material on the label so it's legible
//  behind hills — the design's "visible through terrain" reading; the
//  BugattiBoys/PortalIndicator behaviour reproduced from uGUI primitives only).
//  It is a READ-OUT, NOT a destination picker — travel is the server-side
//  rune-name match the core card (C1) owns. (Model B's selectable directory is
//  explicitly OUT of v3.0 scope.)
//
//  🔴 MULTIPLAYER (spec §2 / §7.2): the label set is populated from the Twisted
//  Portal ZDOs THIS CLIENT HOLDS (ZDOMan.GetAllZDOsWithPrefabIterative filtered to
//  our hash). On a dedicated server a client holds only ~64–128 m of ZDOs, NOT a
//  guaranteed 300 m — so the overlay is BEST-EFFORT CLIENT COSMETIC: it shows the
//  portals the client currently has, which may be fewer than the true 300 m set on
//  a far-flung world. This is an ACCEPTED v3.0 limitation (AT-OVERLAY), documented,
//  not a bug — travel itself (C1) resolves server-side and is correct.
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

        private static TwistedPortalOverlay? _instance;

        // The world-space label field — lives in its OWN scene root (NOT under Hud.m_rootObject),
        // so screen-space scale never touches the world labels. The host (this) carries the pump.
        private readonly LabelField _field = new LabelField();

        private float _nextRefresh;
        private bool _loggedFirstPopulate;

        // Reused across refreshes to avoid per-frame allocation.
        private readonly List<ZDO> _zdoScratch = new List<ZDO>();
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
            public PortalRow(Vector3 pos, string rune, bool hasRune) { Pos = pos; Rune = rune; HasRune = hasRune; }
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
                return;
            }

            if (Time.time < _nextRefresh) return;
            _nextRefresh = Time.time + Mathf.Max(0.1f,
                Plugin.TwistedOverlayRefreshInterval?.Value ?? DefaultRefreshInterval);

            float proximity = Plugin.TwistedOverlayProximityRange?.Value ?? TwistedPortalOverlayModel.DefaultProximityRange;
            float radius    = Plugin.TwistedOverlayRadius?.Value         ?? TwistedPortalOverlayModel.DefaultOverlayRadius;
            int   maxLabels = Plugin.TwistedOverlayMaxLabels?.Value      ?? TwistedPortalOverlayModel.DefaultMaxLabels;
            bool  showUnnamed   = Plugin.TwistedOverlayShowUnnamed?.Value   ?? TwistedPortalOverlayModel.DefaultShowUnnamed;
            bool  showDistance  = Plugin.TwistedOverlayShowDistance?.Value  ?? TwistedPortalOverlayModel.DefaultShowDistance;
            bool  throughTerrain = Plugin.TwistedOverlayThroughTerrain?.Value ?? TwistedPortalOverlayModel.DefaultThroughTerrain;
            float labelScale  = Plugin.TwistedOverlayLabelScale?.Value   ?? TwistedPortalOverlayModel.DefaultLabelScale;
            float labelHeight = Plugin.TwistedOverlayLabelHeight?.Value  ?? TwistedPortalOverlayModel.DefaultLabelHeight;

            // ── Gather every Twisted Portal ZDO this peer holds (the §2 client window) and turn it
            //    into rows of (position, rune, hasRune, distance). ──
            Vector3 here = player.transform.position;
            BuildRows(here);

            // ── Proximity TRIGGER (spec §7.3): the overlay shows only while the player is within
            //    ~proximity m of SOME Twisted Portal (you're standing on / near one). Off-portal it
            //    hides entirely. Reuse the row set we already built (nearest distance). ──
            float nearestSqr = float.MaxValue;
            for (int i = 0; i < _rows.Count; i++)
            {
                float d = (_rows[i].Pos - here).sqrMagnitude;
                if (d < nearestSqr) nearestSqr = d;
            }
            bool nearAPortal = _rows.Count > 0 && nearestSqr <= proximity * proximity;
            if (!nearAPortal)
            {
                _field.Hide();
                return;
            }

            // ── Pure selection: nearest-N within radius, unnamed skipped unless asked (the model). ──
            _candidates.Clear();
            for (int i = 0; i < _rows.Count; i++)
            {
                float dist = Vector3.Distance(_rows[i].Pos, here);
                _candidates.Add(new OverlayCandidate(dist, _rows[i].HasRune));
            }
            TwistedPortalOverlayModel.SelectNearest(_candidates, radius, maxLabels, showUnnamed, _selected);

            // ── Hand the chosen rows to the world-space field as (worldPos, labelText, hasRune). ──
            _field.BeginFrame(throughTerrain, labelScale);
            int drawn = 0;
            for (int s = 0; s < _selected.Count; s++)
            {
                int idx = _selected[s];
                PortalRow row = _rows[idx];
                float dist = _candidates[idx].Distance;
                string text = TwistedPortalOverlayModel.BuildLabel(row.Rune, row.HasRune, dist, showDistance);
                Vector3 labelPos = row.Pos + Vector3.up * labelHeight;
                _field.DrawLabel(drawn, labelPos, text, row.HasRune);
                drawn++;
            }
            _field.EndFrame(drawn);

            LogFirstPopulate(drawn);
        }

        /// <summary>
        /// Walk the Twisted-Portal ZDO set THIS PEER HOLDS and fill <see cref="_rows"/> with each
        /// one's (position, censored rune, hasRune). Uses the same paged
        /// <c>GetAllZDOsWithPrefabIterative</c> drain idiom the core's destination walk uses
        /// (SBPR_TwistedPortal.ResolveDestination) — drain it fully (returns false until exhausted).
        /// 🔴 On a dedicated server this is only the ~64–128 m client window (§2) — best-effort.
        /// </summary>
        private void BuildRows(Vector3 here)
        {
            _rows.Clear();

            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return;

            _zdoScratch.Clear();
            int index = 0;
            while (!zdoMan.GetAllZDOsWithPrefabIterative(TwistedPortal.PortalPieceName, _zdoScratch, ref index)) { }

            foreach (var z in _zdoScratch)
            {
                if (z == null) continue;
                string raw = z.GetString(SBPR_TwistedPortal.ZdoRuneName, string.Empty);
                // Censor on read (the core's ReadRuneName precedent) so a label can never show
                // un-filtered UGC even if a legacy ZDO stored raw bytes.
                string rune = string.IsNullOrEmpty(raw)
                    ? string.Empty
                    : CensorShittyWords.FilterUGC(raw, UGCType.Text, 0L);
                bool hasRune = !string.IsNullOrEmpty(rune);
                _rows.Add(new PortalRow(z.GetPosition(), rune, hasRune));
            }
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
            private const float ReferencePx = 256f;
            private const float CanvasWidthPx  = 512f;
            private const float CanvasHeightPx = 256f;
            private const int   FontPx = 40;

            // Named portals read warm/white; unnamed read a dim grey (informational, can't pair).
            private static readonly Color NamedColor   = new Color(0.96f, 0.92f, 0.78f, 1f);
            private static readonly Color UnnamedColor = new Color(0.62f, 0.66f, 0.62f, 0.85f);

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
            /// record this frame's through-terrain + scale choices.</summary>
            public void BeginFrame(bool throughTerrain, float labelScale)
            {
                if (_disposed) return;
                EnsureBuilt();
                if (_root == null) return;
                if (Utils.GetMainCamera() == null) { SetVisible(false); return; }

                _throughTerrain = throughTerrain;
                _frameScale = Mathf.Max(0.01f, labelScale);
                SetVisible(true);
            }

            private float _frameScale = 1f;

            /// <summary>Place + fill the <paramref name="index"/>-th pooled label at
            /// <paramref name="worldPos"/> with <paramref name="text"/>. Named vs unnamed tints the
            /// text; the slot is scaled to <c>labelScale</c> world-metres.</summary>
            public void DrawLabel(int index, Vector3 worldPos, string text, bool hasRune)
            {
                if (_disposed || _root == null) return;

                Slot slot = EnsureSlot(index);
                slot.Go.transform.position = worldPos;
                slot.Go.transform.localScale = Vector3.one * (_frameScale / ReferencePx);

                slot.Label.text = text;
                slot.Label.color = hasRune ? NamedColor : UnnamedColor;
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
                var outline = labelGo.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.92f);
                outline.effectDistance = new Vector2(2f, -2f);

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
