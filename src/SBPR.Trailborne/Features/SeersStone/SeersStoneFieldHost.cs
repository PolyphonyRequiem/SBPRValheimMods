// ============================================================================
//  Trailborne v4 (Mountains) — Seer's Stone FIELD HOST + Alt+E pin-by-look
// ----------------------------------------------------------------------------
//  Design : docs/design/seers-stone.md. Two engine-side pieces that need the live
//           local Player:
//    1. SeersStoneFieldHost — a MonoBehaviour on the local Player that owns the
//       WispField and ticks it each frame (worn-state → spawn/despawn wisps).
//    2. The Alt+E pin-by-look INPUT: a Harmony postfix on Player.Update that, when
//       the stone is worn and Alt+E is pressed, raycasts the camera forward, and if
//       it hits a wisp's source object, asks SeersStonePinDecision and places the pin.
//
//  Both are client-only (Player.m_localPlayer / GameCamera / Minimap exist only on a
//  client). Server-gated registration via Plugin (the patch) + the host attach hook.
//
//  Clean-side (ADR-0001): patches base-game Player only; reads Pickable/Location/
//  Minimap via public API. The pin DECISION is the engine-free SeersStonePinDecision.
// ============================================================================

using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.SeersStone
{
    /// <summary>
    /// Local-player host for the personal wisp field. Attached to <c>Player.m_localPlayer</c> by the
    /// <see cref="LocalPlayerAttach"/> hook; owns one <see cref="WispField"/> and ticks it each frame
    /// with the current worn-state. Destroyed with the Player (logout/death) — which tears down all
    /// wisps (they're children of this host's transform).
    /// </summary>
    public sealed class SeersStoneFieldHost : MonoBehaviour
    {
        private WispField _field = null!;   // set in Awake (MonoBehaviour lifecycle; never used before)
        private Player _player = null!;

        private void Awake()
        {
            _player = GetComponent<Player>();
            _field = new WispField(transform);
        }

        private void OnDestroy()
        {
            _field?.ClearAll();
        }

        private void Update()
        {
            if (_player == null || _player != Player.m_localPlayer) return;
            // Server-side / nomap-irrelevant guards are unnecessary here: wisps are a pure local
            // visual; the only gate is whether THIS player wears the stone right now.
            bool worn = SeersStone.IsWearing(_player);
            _field.Tick(_player.transform.position, worn);
        }

        /// <summary>Live wisp count (for the render harness + diagnostics).</summary>
        public int ActiveWispCount => _field?.ActiveCount ?? 0;

        /// <summary>Enumerate the active wisps (pin-by-look raycast resolves a hit to one of these).</summary>
        internal WispField Field => _field;
    }

    /// <summary>
    /// Attaches a <see cref="SeersStoneFieldHost"/> to the local player when it spawns. Postfix on
    /// Player.OnSpawned (the standard "local player is now in-world" hook). Idempotent — never adds a
    /// second host.
    /// </summary>
    [HarmonyPatch(typeof(Player), "OnSpawned")]
    public static class LocalPlayerAttach
    {
        [HarmonyPostfix]
        public static void Postfix(Player __instance)
        {
            try
            {
                if (__instance == null || __instance != Player.m_localPlayer) return;
                if (__instance.GetComponent<SeersStoneFieldHost>() == null)
                    __instance.gameObject.AddComponent<SeersStoneFieldHost>();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/SeersStone] Field host attach failed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Pin-by-look: a Player.Update postfix that, while the stone is worn, watches for Alt+E and on
    /// press raycasts the camera forward. If it hits a Pickable or Location, asks
    /// <see cref="SeersStonePinDecision"/> and (on ShouldPin) calls Minimap.AddPin (Pickable) or
    /// DiscoverLocation (Location). Failures are swallowed — a pin bug must never break Player.Update.
    /// </summary>
    [HarmonyPatch(typeof(Player), "Update")]
    public static class PinByLookInput
    {
        // The camera-forward raycast mask: solid pieces + items + terrain-adjacent so we hit the
        // berry bush / ore / location collider the wisp sits on. Resolved once.
        private static int _rayMask = -1;
        private const float RayDistance = 50f;

        [HarmonyPostfix]
        public static void Postfix(Player __instance)
        {
            try
            {
                if (__instance == null || __instance != Player.m_localPlayer) return;
                if (Minimap.instance == null || GameCamera.instance == null) return;

                // Gate on the stone being worn — no stone, no pin-by-look.
                if (!SeersStone.IsWearing(__instance)) return;

                // Alt+E (Daniel's chosen gesture): E with LeftAlt held, edge-triggered.
                if (!(Input.GetKeyDown(KeyCode.E) && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))))
                    return;

                // Don't fire while a text field / menu is capturing input.
                if (Console.IsVisible() || (Chat.instance != null && Chat.instance.HasFocus())) return;

                TryPinUnderCrosshair();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/SeersStone] Pin-by-look error (ignored): {e.Message}");
            }
        }

        private static void TryPinUnderCrosshair()
        {
            if (_rayMask < 0)
                _rayMask = LayerMask.GetMask("piece", "piece_nonsolid", "item", "Default", "Default_small", "static_solid", "character_noenv");

            // GameCamera.instance.m_camera is private; the camera's own transform (public) carries the
            // same position + forward we need for the crosshair ray (the IronCompass idiom). Use it
            // directly — no reflection needed for a transform.
            var camT = GameCamera.instance.transform;
            if (camT == null) return;
            var origin = camT.position;
            var dir = camT.forward;

            if (!Physics.Raycast(origin, dir, out var hit, RayDistance, _rayMask, QueryTriggerInteraction.Collide))
                return;

            // Resolve the hit to a Pickable or a Location.
            var pickable = hit.collider.GetComponentInParent<Pickable>();
            var location = pickable == null ? hit.collider.GetComponentInParent<Location>() : null;
            if (pickable == null && location == null) return;

            string prefab, friendly;
            WispHitKind kind;
            Vector3 pos;
            if (pickable != null)
            {
                prefab = StripClone(pickable.gameObject.name);
                friendly = SafeHoverName(pickable, prefab);
                kind = WispHitKind.Pickable;
                pos = pickable.transform.position;
            }
            else
            {
                prefab = StripClone(location!.gameObject.name);
                friendly = prefab;
                kind = WispHitKind.Location;
                pos = location.transform.position;
            }

            // Decide via the engine-free core (eligibility re-check, no-count label, merge dedup).
            var existing = SnapshotPins();
            var plan = SeersStonePinDecision.Decide(
                prefab, friendly, kind, new Vec3(pos.x, pos.y, pos.z),
                GetEligibility(), existing);

            if (!plan.ShouldPin)
            {
                // Quiet on merge/ineligible; a soft log helps debugging without spamming.
                return;
            }

            PlacePin(plan, pos);
        }

        private static void PlacePin(PinPlan plan, Vector3 pos)
        {
            if (plan.Kind == WispHitKind.Location)
            {
                // The vanilla "shown on map" path for a location-grade POI.
                Minimap.instance.DiscoverLocation(pos, Minimap.PinType.Icon3, plan.Label, showMap: false);
            }
            else
            {
                // Abundance pin: one pin for the patch, NO count, private by default (save=true so it
                // persists; isChecked=false). The wisp aggregate IS the patch, so one AddPin marks it.
                Minimap.instance.AddPin(pos, Minimap.PinType.Icon3, plan.Label, save: true, isChecked: false);
            }
            Plugin.Log.LogInfo($"[Trailborne/SeersStone] Pinned '{plan.Label}' ({plan.Kind}) at {pos}.");
        }

        // Minimap.m_pins is private — read it via reflection, the established SBPR idiom
        // (SurveyorTableTag.ReadPins). Cached FieldInfo.
        private static System.Reflection.FieldInfo? _fiPins;

        // Snapshot existing pins as the engine-free ExistingPin list for the merge check.
        private static IReadOnlyList<ExistingPin> SnapshotPins()
        {
            var list = new List<ExistingPin>();
            try
            {
                if (_fiPins == null)
                    _fiPins = typeof(Minimap).GetField("m_pins",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var pins = _fiPins?.GetValue(Minimap.instance) as List<Minimap.PinData>;
                if (pins == null) return list;
                foreach (var pin in pins)
                {
                    if (pin == null || pin.m_name == null) continue;
                    list.Add(new ExistingPin(pin.m_name, new Vec3(pin.m_pos.x, pin.m_pos.y, pin.m_pos.z)));
                }
            }
            catch { /* never let a snapshot error block a pin */ }
            return list;
        }

        // The loaded whitelist as the eligibility object (M1). SeersStoneWhitelist holds the singleton;
        // expose its eligibility for the decision core.
        private static SeersStoneEligibility GetEligibility() => SeersStoneWhitelist.Eligibility;

        private static string SafeHoverName(Pickable p, string fallback)
        {
            try
            {
                var n = p.GetHoverName();
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch { }
            return fallback;
        }

        private static string StripClone(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int i = name.IndexOfAny(new[] { '(', ' ' });
            return i >= 0 ? name.Substring(0, i) : name;
        }
    }
}
