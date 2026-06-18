// ============================================================================
//  Trailborne v3 (Swamp) — Iron Compass HUD overlay (the camera-yaw needle dial)
// ----------------------------------------------------------------------------
//  Impl spec: docs/v3/planning/iron-compass-impl-spec.md §4
//  Card     : t_ee61472f
//
//  The render surface for the Iron Compass. A client-only uGUI overlay attached
//  under Hud.instance.m_rootObject via a Hud.Awake postfix (CompassHudBootstrapPatch).
//  This is deliberately a SEPARATE HUD overlay, NEVER a north arrow on the local
//  map: v1/v2 ship the map with no north indicator by design (cartography §2H.1),
//  and the compass is the earned tool that grants cardinal orientation without
//  reversing that locked difficulty. A HUD overlay also works under the SB server's
//  default NoMap (NoMapEnforcer sets GlobalKeys.NoMap), where minimap pins have no
//  surface (AT-COMPASS-NOMAP-SAFE).
//
//  Behaviour (per Update, only while worn):
//    • Hidden unless the local player WEARS the Iron Compass in the Trinket slot
//      (Inventory.GetEquippedItems() + Trinket-slot filter + prefab-name match —
//      the CartographersKit.IsWearingKit / SunstoneLens.GetEquippedLens precedent;
//      NOT a HaveItem carry-gate, the spec §2/§4.3 correction).
//    • The NEEDLE rotates to true world north relative to the camera yaw, lerping
//      toward the target so it lags slightly (Q3 — Config.Bind-tunable).
//    • The DIAL FACE tilts as you look up/down (pitch → ~45° UI tilt, Q4-tunable),
//      a subtle 3D-instrument feel (AT-COMPASS-TILT).
//
//  Q2: this is a 2D UGUI element (the native tool for a screen overlay), NOT a
//  procedural mesh. v0.1 renders the dial + needle from procedural UGUI primitives
//  (translucent disc + N/E/S/W labels + a red-tipped needle bar) so it is legible
//  with ZERO art dependency — "you can tell it's a compass," the locked art bar.
//  Polished authored dial/needle sprites are a v0.x follow-up: the dial + needle
//  carry Image components, so a real sprite drops straight in (same deferral the
//  Sunstone overlay's polished threat-art uses). The held-trinket WORLD mesh is
//  deferred to v0.2+ (requirements.md:696) — orthogonal to this 2D overlay.
//
//  Q4: the anchor is a Config.Bind ENUM (CompassAnchor), default TopCenter, which
//  is NoMap-safe (independent of any minimap). The enum is scaffolded from day one
//  to extend to the carry-state Local Map disc (BelowMapDisc / OnMapDiscOverlay,
//  t_7dd54899) and the future Eye-of-Odin global minimap — only TopCenter is wired
//  in v1; the other cases resolve to TopCenter with a one-time log until their dock
//  targets exist. The abstraction does not exclude them (forward-ref, Daniel).
//
//  Client-only by construction: Hud.Awake never fires on the dedicated server (no
//  Hud), and GameCamera.instance / Player.m_localPlayer are client concerns.
//  Everything here is cosmetic — it reads game state, never writes it. No game-state
//  patch, no map mutation, no ZDO. It cannot desync or corrupt a save.
//
//  Clean-side (ADR-0001): reads base-game Hud/Player/Inventory/GameCamera only; the
//  uGUI surface is our own (the MapViewer / SunstoneLensHudOverlay idiom this repo
//  already ships). Decomp lines cited (GameCamera :85308/:85422, Trinket = 24
//  :57652) are base-game, fair game to read and adapt. No vanilla UI cloned.
//
//  logs-green ≠ playable — needle direction, lag feel, tilt, and anchor placement
//  are GPU-client checks Daniel verifies in-game (the named AT-COMPASS-*).
// ============================================================================

using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Exploration
{
    /// <summary>
    /// Where the compass overlay anchors on the HUD. A Config.Bind enum so the placement
    /// can be tuned in one joined session (Q4) and — crucially — so the anchor abstraction
    /// is extensible from day one to dock targets that do not exist yet.
    ///
    /// v1 wires <see cref="TopCenter"/> only (always works, independent of any minimap —
    /// NoMap-safe). The remaining cases are scaffolded so a later card can dock the compass
    /// to the carry-state Local Map disc (t_7dd54899) or the future Eye-of-Odin global
    /// minimap WITHOUT changing this enum — until those targets exist, they resolve to
    /// TopCenter with a one-time log. (Daniel forward-ref, 2026-06-17.)
    /// </summary>
    public enum CompassAnchor
    {
        /// <summary>v1 default — a fixed screen position at the top-center, NoMap-safe.</summary>
        TopCenter = 0,

        /// <summary>NOT WIRED v1 — dock just below the carry-state Local Map disc (t_7dd54899).</summary>
        BelowMapDisc = 1,

        /// <summary>NOT WIRED v1 — overlay onto the carry-state Local Map disc (t_7dd54899).</summary>
        OnMapDiscOverlay = 2,

        /// <summary>NOT WIRED v1 — forward-ref: dock to the Eye-of-Odin global minimap when it lands.</summary>
        EyeOfOdinMinimap = 3,
    }

    /// <summary>
    /// The client-only HUD MonoBehaviour. Built once and attached under Hud.m_rootObject by
    /// <see cref="CompassHudBootstrapPatch"/>. Drives visibility from the equipped-compass
    /// state, the needle rotation from the camera yaw (with lag), and the dial-face tilt from
    /// the camera pitch.
    /// </summary>
    public class SBPR_CompassHud : MonoBehaviour
    {
        // ── Defaults (single source of truth; the Config binds in Plugin mirror these so a
        //    no-Plugin unit context still resolves a sane value, the SunstoneLens pattern). ──
        public const float DefaultNeedleLag = 8f;    // Mathf.LerpAngle(cur, target, dt * rate) — higher = snappier, lower = laggier
        public const float DefaultMaxTilt   = 45f;   // max dial-face tilt (deg) at full look-up/down (design §8 "~45°")
        public const float DefaultSize      = 140f;  // dial footprint, px square
        public const float DefaultOffsetX   = 0f;    // nudge from the anchor, px (+right)
        public const float DefaultOffsetY   = -94f;  // nudge from the anchor, px (TopCenter: −down from the top edge)

        private RectTransform? _root;      // the anchored host (visibility + placement)
        private RectTransform? _tiltRect;  // the pitch-tilt container (holds dial + needle)
        private RectTransform? _needleRect; // the rotating needle

        private float _needleAngle;        // current (lagged) needle Z angle, degrees
        private bool  _needleSeeded;       // snap to target on first show (no spin-up from 0)

        private CompassAnchor _appliedAnchor = (CompassAnchor)(-1); // force first ApplyAnchor
        private bool _unwiredAnchorWarned;

        // Cream/parchment + alarm tones matching the rest of the SBPR uGUI (MapViewer / Sunstone).
        private static readonly Color CDiscBackdrop = new Color(0.10f, 0.11f, 0.09f, 0.55f); // translucent dark dial
        private static readonly Color CLabel        = new Color(0.92f, 0.89f, 0.78f, 0.95f); // parchment letters
        private static readonly Color CNeedleNorth  = new Color(0.90f, 0.32f, 0.26f, 0.98f); // red north tip
        private static readonly Color CNeedleSouth  = new Color(0.78f, 0.80f, 0.82f, 0.95f); // pale south tail

        /// <summary>Build (idempotently) the dial + needle children under this host.</summary>
        public void Build()
        {
            _root = gameObject.GetComponent<RectTransform>();
            if (_root == null) _root = gameObject.AddComponent<RectTransform>();

            // Placement is (re)applied every Update from config, so just seed a size here.
            ApplyAnchor(CompassAnchor.TopCenter, DefaultSize, DefaultOffsetX, DefaultOffsetY);

            // ── Tilt container: fills the root; pitch rotates THIS so the whole dial face
            //    tilts away as you look up/down (AT-COMPASS-TILT "dial face"), while the needle
            //    rotates within it for heading. Keeps tilt (pitch) and heading (yaw) independent.
            var tiltGo = new GameObject("tilt", typeof(RectTransform));
            tiltGo.transform.SetParent(_root, worldPositionStays: false);
            _tiltRect = tiltGo.GetComponent<RectTransform>();
            _tiltRect.anchorMin = Vector2.zero;
            _tiltRect.anchorMax = Vector2.one;
            _tiltRect.offsetMin = Vector2.zero;
            _tiltRect.offsetMax = Vector2.zero;

            // ── Dial backdrop: a translucent disc (built-in round Knob sprite if available, else
            //    a solid quad). The fixed lettered ring — does NOT rotate (spec §4.2). v0.1
            //    placeholder; a real authored dial sprite drops into this Image later.
            var disc = MakeImage("dial", _tiltRect, CDiscBackdrop);
            StretchFull(disc.rectTransform);
            disc.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd"); // round, optional
            disc.type = Image.Type.Simple;
            disc.preserveAspect = true;

            // ── Fixed cardinal labels N/E/S/W around the dial (do not rotate).
            var font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                       ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            MakeLabel("N", font, _tiltRect, new Vector2(0.5f, 0.93f));
            MakeLabel("E", font, _tiltRect, new Vector2(0.93f, 0.5f));
            MakeLabel("S", font, _tiltRect, new Vector2(0.5f, 0.07f));
            MakeLabel("W", font, _tiltRect, new Vector2(0.07f, 0.5f));

            // ── The needle: a centered RectTransform we rotate to heading. Two stacked quads —
            //    a red north tip + a pale south tail — so the pointing direction is unambiguous.
            //    Rendered LAST (last sibling) so it draws on top of the dial.
            var needleGo = new GameObject("needle", typeof(RectTransform));
            needleGo.transform.SetParent(_tiltRect, worldPositionStays: false);
            _needleRect = needleGo.GetComponent<RectTransform>();
            _needleRect.anchorMin = new Vector2(0.5f, 0.5f);
            _needleRect.anchorMax = new Vector2(0.5f, 0.5f);
            _needleRect.pivot     = new Vector2(0.5f, 0.5f);
            _needleRect.anchoredPosition = Vector2.zero;
            _needleRect.sizeDelta = new Vector2(DefaultSize * 0.14f, DefaultSize * 0.78f); // thin, tall bar

            var north = MakeImage("north", _needleRect, CNeedleNorth);
            north.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            north.rectTransform.anchorMax = new Vector2(1f, 1f);
            north.rectTransform.offsetMin = Vector2.zero;
            north.rectTransform.offsetMax = Vector2.zero;

            var south = MakeImage("south", _needleRect, CNeedleSouth);
            south.rectTransform.anchorMin = new Vector2(0f, 0f);
            south.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            south.rectTransform.offsetMin = Vector2.zero;
            south.rectTransform.offsetMax = Vector2.zero;

            SetVisible(false);
        }

        // ── Anchor resolution (Q4) ────────────────────────────────────────────────────────
        // Only TopCenter is wired in v1. The dock-to-disc / Eye-of-Odin cases resolve to
        // TopCenter (with a one-time log) until their targets exist — the abstraction stays
        // extensible without pretending to dock to something that isn't built yet.
        private CompassAnchor ResolveAnchor(CompassAnchor requested)
        {
            if (requested == CompassAnchor.TopCenter) return CompassAnchor.TopCenter;

            if (!_unwiredAnchorWarned)
            {
                _unwiredAnchorWarned = true;
                Plugin.Log.LogInfo(
                    $"[Trailborne/Exploration] CompassAnchor.{requested} is not wired yet (its dock target — "
                    + "the carry-state Local Map disc / Eye-of-Odin minimap — does not exist in v1). "
                    + "Falling back to TopCenter. The enum case is reserved for when that target lands.");
            }
            return CompassAnchor.TopCenter;
        }

        private void ApplyAnchor(CompassAnchor anchor, float size, float offsetX, float offsetY)
        {
            if (_root == null) return;
            switch (ResolveAnchor(anchor))
            {
                // v1: a fixed screen position at the top-center, independent of any minimap.
                case CompassAnchor.TopCenter:
                default:
                    _root.anchorMin = new Vector2(0.5f, 1f);
                    _root.anchorMax = new Vector2(0.5f, 1f);
                    _root.pivot     = new Vector2(0.5f, 1f);
                    break;
            }
            _root.sizeDelta = new Vector2(size, size);
            _root.anchoredPosition = new Vector2(offsetX, offsetY);
        }

        private void Update()
        {
            var player = Player.m_localPlayer;
            if (player == null || _root == null)
            {
                SetVisible(false);
                return;
            }

            bool wearing = IsWearingCompass(player);
            if (_root.gameObject.activeSelf != wearing) SetVisible(wearing);
            if (!wearing) { _needleSeeded = false; return; }

            // Re-apply placement from live config so Daniel can tune anchor/size/offset in one
            // joined session (Q4) without a rebuild — cheap (a few RectTransform field writes).
            var anchor  = Plugin.CompassAnchor?.Value   ?? CompassAnchor.TopCenter;
            float size  = Plugin.CompassSize?.Value     ?? DefaultSize;
            float offX  = Plugin.CompassOffsetX?.Value   ?? DefaultOffsetX;
            float offY  = Plugin.CompassOffsetY?.Value   ?? DefaultOffsetY;
            if (anchor != _appliedAnchor || _root.sizeDelta.x != size
                || _root.anchoredPosition.x != offX || _root.anchoredPosition.y != offY)
            {
                ApplyAnchor(anchor, size, offX, offY);
                _appliedAnchor = anchor;
            }

            var cam = GameCamera.instance;   // decomp :85422 — null until the camera spawns
            if (cam == null || _needleRect == null || _tiltRect == null) return;

            Vector3 euler = cam.transform.eulerAngles;   // .y = yaw (heading), .x = pitch (look up/down)

            // ── Yaw → needle rotation, with the "slight lag" (Q3). The needle points to WORLD
            //    NORTH relative to where the camera faces: turning right (yaw ↑) rotates the world
            //    left under you, so the needle rotates by −yaw. LerpAngle handles the 0/360 wrap so
            //    a full spin sweeps the needle the short way (no long-way-around jump, AT-COMPASS-HEADING).
            //    NOTE (honesty): which screen direction is "north" is a GPU-client check — if the
            //    needle reads mirrored/offset in-game it's a one-line sign/offset tune (AT-COMPASS-HEADING).
            float targetZ = -euler.y;
            if (!_needleSeeded) { _needleAngle = targetZ; _needleSeeded = true; } // snap on first show
            float lagRate = Plugin.CompassNeedleLag?.Value ?? DefaultNeedleLag;
            _needleAngle = Mathf.LerpAngle(_needleAngle, targetZ, Time.deltaTime * Mathf.Max(0.01f, lagRate));
            _needleRect.localRotation = Quaternion.Euler(0f, 0f, _needleAngle);

            // ── Pitch → dial-face tilt (AT-COMPASS-TILT "dial face tilts"). Tilt the whole dial
            //    container (not just the needle) so the face foreshortens like a real instrument.
            float maxTilt = Plugin.CompassMaxTilt?.Value ?? DefaultMaxTilt;
            _tiltRect.localRotation = Quaternion.Euler(MapPitchToTilt(euler.x, maxTilt), 0f, 0f);
        }

        /// <summary>
        /// Map the camera pitch (eulerAngles.x, which wraps 0..360) to a clamped UI tilt. Looking
        /// up reads as ~350° not −10°, so unwrap to signed −90..90 first, then scale to ±maxTilt.
        /// </summary>
        private static float MapPitchToTilt(float rawPitchEuler, float maxTilt)
        {
            float signed = rawPitchEuler > 180f ? rawPitchEuler - 360f : rawPitchEuler;
            signed = Mathf.Clamp(signed, -90f, 90f);
            return signed * (maxTilt / 90f);
        }

        // ── Equip-gate (spec §4.3): worn in the Trinket slot, NOT merely carried. Reads the
        //    PUBLIC Inventory.GetEquippedItems() (decomp :57192), filters on the Trinket slot,
        //    and matches m_dropPrefab.name (clone-suffix-stripped) against IronCompass.CompassName.
        //    The CartographersKit.IsWearingKit / SunstoneLens.GetEquippedLens precedent. Gating on
        //    the SLOT (not just the name) means a future Trinket can't accidentally satisfy it.
        private static bool IsWearingCompass(Player player)
        {
            if (player == null) return false;
            var inv = player.GetInventory();
            if (inv == null) return false;

            foreach (var item in inv.GetEquippedItems())
            {
                if (item == null || item.m_shared == null) continue;
                if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Trinket) continue;
                var drop = item.m_dropPrefab;
                if (drop == null) continue;
                if (StripCloneSuffix(drop.name) == IronCompass.CompassName)
                    return true;
            }
            return false;
        }

        // Mirror of vanilla ItemDrop.GetPrefabName clone-suffix strip (decomp :58940): cut at the
        // first '(' or ' ' so "SBPR_IronCompass(Clone)" matches CompassName.
        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int i = name.IndexOfAny(new[] { '(', ' ' });
            return i >= 0 ? name.Substring(0, i) : name;
        }

        private void SetVisible(bool on)
        {
            if (_root != null && _root.gameObject.activeSelf != on)
                _root.gameObject.SetActive(on);
        }

        // ── UGUI construction helpers (additive — the MapViewer / SunstoneLensHudOverlay idiom) ──

        private static Image MakeImage(string name, RectTransform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false; // a pure HUD readout — never intercept clicks
            return img;
        }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void MakeLabel(string text, Font font, RectTransform parent, Vector2 anchor)
        {
            var go = new GameObject("lbl_" + text, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(24f, 24f);

            var txt = go.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = 18;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.color = CLabel;
            txt.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
        }
    }

    /// <summary>
    /// The ONE Harmony patch in the feature: a postfix on Hud.Awake that mounts the
    /// <see cref="SBPR_CompassHud"/> overlay under Hud.m_rootObject. Parenting under m_rootObject
    /// means the overlay inherits the HUD's Canvas + GuiScaler (the same parent the vanilla
    /// health/food/stamina RectTransforms live under, decomp :38989+), so it scales and shows/hides
    /// with the rest of the HUD automatically — when m_rootObject slides to its not-visible position
    /// to hide the HUD, the compass goes with it (AT-COMPASS-HUD-HIDE, free correct behaviour).
    ///
    /// MUST be registered in Plugin.Awake via harmony.PatchAll(typeof(CompassHudBootstrapPatch)) or
    /// PatchCheck ERRORs at boot (the unregistered-patch lesson, t_564f695a). This is the single
    /// reason the Iron Compass is NOT patch-free. Never fires on the dedicated server (no Hud).
    /// Idempotent + server-safe + fail-quiet.
    /// </summary>
    [HarmonyPatch(typeof(Hud), "Awake")]
    public static class CompassHudBootstrapPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Hud __instance)
        {
            try
            {
                if (!ServerContext.OnSBServer) return;               // SBPR server-gating doctrine
                if (__instance == null || __instance.m_rootObject == null) return;
                // Idempotent: never double-mount (Hud can re-Awake on scene reload).
                if (__instance.m_rootObject.transform.Find("SBPR_CompassHud") != null) return;

                var host = new GameObject("SBPR_CompassHud", typeof(RectTransform));
                host.transform.SetParent(__instance.m_rootObject.transform, worldPositionStays: false);
                host.AddComponent<SBPR_CompassHud>().Build();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Exploration] Compass HUD bootstrap error (non-fatal): {e.Message}");
            }
        }
    }
}
