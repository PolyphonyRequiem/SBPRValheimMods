// ============================================================================
//  Trailborne v3 (Swamp) — Sunstone Lens HUD overlay (the detection render surface)
// ----------------------------------------------------------------------------
//  Impl spec: docs/v3/planning/sunstone-lens-impl-spec.md §4-§5
//  Card     : t_2fd7bc7f
//
//  The render surface for the Sunstone Lens' monster detection. A client-only uGUI
//  overlay attached under Hud.instance.m_rootObject via a Hud.Awake postfix — the
//  SAME doctrine the Iron Compass uses (nomap.md §8: "pure HUD overlay, no game-state
//  patches"). This is deliberately NOT minimap pins: the SB server runs NoMap by
//  default (NoMapEnforcer sets GlobalKeys.NoMap), so a Minimap.PinType reveal would
//  have no surface. A HUD overlay works regardless of map state.
//
//  Behaviour (per tick, throttled to DetectInterval):
//    • Hidden unless the local player wears the Sunstone Lens (SunstoneLens.GetEquippedLens).
//    • If worn but charge < MinChargeToDetect → show a dim "depleted" state (no threats).
//    • If worn AND charged → sweep SunstoneLens.GatherHostiles and show:
//        - a threat COUNT badge,
//        - the distance + bearing to the NEAREST hostile (a small directional tick
//          derived from the main camera yaw vs the hostile bearing),
//        - the current charge % (mirrors the durability bar, at-a-glance).
//
//  Client-only by construction: Hud.Awake never fires on the dedicated server (no Hud),
//  and Character.GetAllCharacters / Player.m_localPlayer are client concerns. Everything
//  here is cosmetic — it reads game state, never writes it.
//
//  v0.1 is a FUNCTIONAL placeholder indicator (text + a simple arrow glyph), per the
//  icon/asset doctrine: the MECHANIC (who is revealed, when) is the acceptance target;
//  polished threat-overlay art is a v0.x follow-up. logs-green ≠ playable — Daniel
//  verifies AT-LENS-DETECT in-game.
//
//  Clean-side (ADR-0001): reads base-game Hud/Player/Character/GameCamera only; the uGUI
//  surface is our own (the MapViewer idiom this repo already ships). No vanilla UI cloned.
// ============================================================================

using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Sunstone
{
    /// <summary>
    /// The client-only HUD MonoBehaviour. Built once and attached under Hud.m_rootObject by
    /// <see cref="HudBootstrap"/>. Drives visibility + content from the equipped-lens state.
    /// </summary>
    public class SunstoneLensHudOverlay : MonoBehaviour
    {
        private static SunstoneLensHudOverlay? _instance;

        private RectTransform? _root;
        private Text? _title;     // "⚠ N nearby" threat count, or "Sunstone Lens — dim" when depleted
        private Text? _detail;    // "nearest 12m  ◄"   (distance + bearing tick) + charge %

        // Reused across sweeps to avoid per-frame allocations.
        private readonly List<Character> _hostiles = new List<Character>();

        private float _nextSweep;

        // Cream/parchment tones matching the rest of the SBPR uGUI (MapViewer pin labels).
        private static readonly Color CThreat   = new Color(0.95f, 0.45f, 0.35f, 0.98f); // alarm-warm
        private static readonly Color CClearTone = new Color(0.75f, 0.85f, 0.70f, 0.95f); // calm green
        private static readonly Color CDim      = new Color(0.70f, 0.66f, 0.55f, 0.75f); // depleted/dim

        /// <summary>Idempotently build the overlay under the given Hud root.</summary>
        public static void EnsureBuilt(GameObject hudRoot)
        {
            if (_instance != null && _instance._root != null) return;
            if (hudRoot == null) return;

            var host = new GameObject("SBPR_SunstoneLensHud");
            host.transform.SetParent(hudRoot.transform, worldPositionStays: false);
            _instance = host.AddComponent<SunstoneLensHudOverlay>();
            _instance.Build();
        }

        private void Build()
        {
            _root = gameObject.AddComponent<RectTransform>();
            // Bottom-center, sitting just above the hotbar / status area. Anchored so it
            // scales with the canvas like the rest of the HUD.
            _root.anchorMin = new Vector2(0.5f, 0f);
            _root.anchorMax = new Vector2(0.5f, 0f);
            _root.pivot     = new Vector2(0.5f, 0f);
            _root.anchoredPosition = new Vector2(0f, 170f);
            _root.sizeDelta = new Vector2(320f, 46f);

            var font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                       ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            _title  = MakeText("title",  font, 20, FontStyle.Bold,   new Vector2(0f, 12f));
            _detail = MakeText("detail", font, 15, FontStyle.Normal, new Vector2(0f, -10f));

            SetVisible(false);
        }

        private Text MakeText(string name, Font font, int size, FontStyle style, Vector2 anchored)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, worldPositionStays: false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchored;
            rt.sizeDelta = new Vector2(320f, 24f);

            var txt = go.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = size;
            txt.fontStyle = style;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.color = Color.white;

            // Dark outline so it reads over any background (same as MapViewer pin labels).
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            return txt;
        }

        private void SetVisible(bool on)
        {
            if (_root != null) _root.gameObject.SetActive(on);
        }

        private void Update()
        {
            // Guard everything: the HUD can outlive a world/player.
            var player = Player.m_localPlayer;
            if (player == null || _root == null)
            {
                SetVisible(false);
                return;
            }

            var lens = SunstoneLens.GetEquippedLens(player);
            if (lens == null)
            {
                SetVisible(false);   // not worn → overlay hidden entirely
                return;
            }

            SetVisible(true);

            float max = Mathf.Max(1f, lens.GetMaxDurability());
            float chargePct = Mathf.Clamp01(lens.m_durability / max) * 100f;

            // Depleted (inert) state — worn but not enough charge to detect (AC#5).
            if (lens.m_durability < SunstoneLens.MinChargeToDetect)
            {
                if (_title != null)
                {
                    _title.text = "Sunstone Lens — dim";
                    _title.color = CDim;
                }
                if (_detail != null)
                {
                    _detail.text = "no charge · recharge in the sun";
                    _detail.color = CDim;
                }
                return;
            }

            // Throttled detection sweep (cheap to keep the per-frame cost low).
            float interval = Plugin.LensDetectInterval?.Value ?? SunstoneLens.DefaultDetectInterval;
            if (Time.time >= _nextSweep)
            {
                _nextSweep = Time.time + Mathf.Max(0.1f, interval);
                float radius = Plugin.LensDetectRadius?.Value ?? SunstoneLens.DefaultDetectRadius;
                SunstoneLens.GatherHostiles(player, radius, _hostiles);
            }

            RenderThreats(player, chargePct);
        }

        private void RenderThreats(Player player, float chargePct)
        {
            int n = _hostiles.Count;

            if (n == 0)
            {
                if (_title != null)
                {
                    _title.text = "Sunstone Lens — clear";
                    _title.color = CClearTone;
                }
                if (_detail != null)
                {
                    _detail.text = $"nothing stirs · charge {chargePct:0}%";
                    _detail.color = CClearTone;
                }
                return;
            }

            // Find the nearest hostile for the distance + bearing readout.
            Vector3 origin = player.transform.position;
            Character? nearest = null;
            float nearestSqr = float.MaxValue;
            foreach (var c in _hostiles)
            {
                if (c == null) continue;
                float d = (c.transform.position - origin).sqrMagnitude;
                if (d < nearestSqr) { nearestSqr = d; nearest = c; }
            }

            if (_title != null)
            {
                _title.text = n == 1 ? "\u26A0 1 hostile near" : $"\u26A0 {n} hostiles near";
                _title.color = CThreat;
            }

            if (_detail != null)
            {
                float dist = nearest != null ? Mathf.Sqrt(nearestSqr) : 0f;
                string tick = nearest != null ? BearingGlyph(player, nearest) : "";
                _detail.text = $"nearest {dist:0}m {tick}  ·  charge {chargePct:0}%";
                _detail.color = CThreat;
            }
        }

        /// <summary>
        /// A coarse directional glyph (◄ ▲ ► ▼ and diagonals) pointing from the player's view
        /// heading toward <paramref name="target"/>. Bearing is relative to the main camera yaw
        /// so "▲" means "ahead of where you're looking". Camera-null safe (returns "").
        /// </summary>
        private static string BearingGlyph(Player player, Character target)
        {
            var cam = Utils.GetMainCamera();
            if (cam == null) return "";

            Vector3 to = target.transform.position - player.transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.001f) return "\u25CF"; // ● on top of you

            // Angle of the target relative to camera forward, in [-180, 180]; positive = right.
            Vector3 fwd = cam.transform.forward; fwd.y = 0f;
            float signed = Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);

            // 8-way compass tick.
            float a = signed;
            if (a >= -22.5f && a < 22.5f)   return "\u25B2"; // ▲ ahead
            if (a >= 22.5f && a < 67.5f)    return "\u25E5"; // ◥ ahead-right
            if (a >= 67.5f && a < 112.5f)   return "\u25B6"; // ▶ right
            if (a >= 112.5f && a < 157.5f)  return "\u25E2"; // ◢ behind-right
            if (a >= 157.5f || a < -157.5f) return "\u25BC"; // ▼ behind
            if (a >= -157.5f && a < -112.5f) return "\u25E3"; // ◣ behind-left
            if (a >= -112.5f && a < -67.5f) return "\u25C0"; // ◀ left
            return "\u25E4"; // ◤ ahead-left
        }

        /// <summary>
        /// Builds (idempotently) the HUD overlay once the Hud exists. Postfix on Hud.Awake —
        /// the exact pattern the Iron Compass design specs (nomap.md §8). Never fires on the
        /// dedicated server (no Hud). Server-safe + fail-quiet.
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
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning($"[Trailborne/Sunstone] Hud overlay bootstrap error (non-fatal): {e.Message}");
                }
            }
        }
    }
}
