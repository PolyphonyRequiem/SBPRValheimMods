// ============================================================================
//  Trailborne v2 cartography — Local Map binding controller (§2A.4)
// ----------------------------------------------------------------------------
//  Drives the Local Map's two binding rules (impl spec §2A.4 / requirements §2):
//    • DURABLE-WHILE-CARRIED: while ANY SBPR_LocalMap instance sits in the local
//      player's inventory (equipped or not), the bounded minimap binding is live;
//      it reverts to no-map the instant the last one leaves inventory
//      (dropped/traded/destroyed). (AT-MAP-DURABLE)
//    • FULL-SCREEN-REQUIRES-EQUIPPED: the full-screen bounded viewer opens only
//      while a Local Map is actively EQUIPPED (two hands). Merely carrying it keeps
//      the minimap bound but does not grant the full view. (AT-MAP-EQUIP)
//
//  WHY A POLLED CONTROLLER (not an inventory-changed hook): under v1's `nomap` world
//  setting, vanilla forces Minimap into MapMode.None every SetMapMode call (decomp
//  Minimap.SetMapMode :975 `if (Game.m_noMap) mode = None`), and both m_smallRoot /
//  m_largeRoot stay off — so we cannot ride vanilla's roots. The SBPR viewer is a
//  fully STANDALONE uGUI overlay (MapViewer.cs, productionized from the spike's own
//  Canvas). This controller is the client-only state machine that decides WHEN that
//  overlay's minimap-disc / full-view should be visible, then asks the viewer to
//  render the bound survey. It is attached to the live Minimap (client-only — the
//  dedicated server has no Minimap) and polls the local player each frame; the per-
//  frame cost is a couple of inventory probes, far cheaper than the vanilla fog redraw.
//
//  This card (M-A) ships the controller + state machine against the CartographyViewer
//  SEAM; the viewer RENDER is M-B (MapViewer.cs, same PR). Until a viewer is
//  registered, the controller still tracks state correctly and the seam degrades
//  gracefully (a one-line "viewer not installed" message), so M-A is verifiable on
//  its own (state transitions logged) without the render.
//
//  Clean-side (ADR-0001): reads base-game Player/Minimap/Inventory only.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cartography
{
    public class LocalMapController : MonoBehaviour
    {
        public const float SurveyRadiusMeters = 1000f; // shared lock with SurveyorTableTag

        // Re-poll cadence for inventory/equip state. Frame-cheap, but no need every frame —
        // a few times a second is responsive for a "did I just equip/drop the map" check.
        private const float PollSeconds = 0.25f;
        private float _nextPoll;

        // Last-seen state, so we only act (open/close/log) on a transition.
        private bool _hadMapCarried;
        private bool _hadMapEquipped;

        // Singleton-ish: the live controller, so the equip patch / table can query state.
        public static LocalMapController? Instance { get; private set; }

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            // Client + graphics only. On the dedicated server graphicsDeviceType is Null
            // and there is no local player, so this loop never runs there.
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;

            float now = Time.realtimeSinceStartup;
            if (now < _nextPoll) return;
            _nextPoll = now + PollSeconds;

            var player = Player.m_localPlayer;
            if (player == null)
            {
                // No player (menu / loading): tear down any live binding once.
                if (_hadMapCarried || _hadMapEquipped) ClearBinding("no local player");
                return;
            }

            var equippedMap = GetEquippedLocalMap(player);
            var carriedMap  = equippedMap ?? GetCarriedLocalMap(player);

            bool mapCarried  = carriedMap != null;
            bool mapEquipped = equippedMap != null;

            // ── Transition: carried-state changed (AT-MAP-DURABLE) ──
            if (mapCarried != _hadMapCarried)
            {
                if (mapCarried)
                    Plugin.Log.LogInfo("[Trailborne/Cartography] Local Map bound (in inventory) — minimap follows its 1000 m disc.");
                else
                    ClearBinding("map left inventory");
                _hadMapCarried = mapCarried;
            }

            // ── Transition: equipped-state changed (AT-MAP-EQUIP) ──
            if (mapEquipped != _hadMapEquipped)
            {
                if (mapEquipped) OpenFullView(equippedMap!);
                else CloseFullView("map unequipped");
                _hadMapEquipped = mapEquipped;
            }

            // Keep the viewer's bound survey fresh while equipped (the imprinted snapshot is
            // static, but the player marker / WorldPins reconcile move — the viewer redraws).
            if (mapEquipped && CartographyViewer.IsViewerOpen)
                RefreshOpenView(equippedMap!);
        }

        // ── Open / close the full-screen bounded viewer on the imprinted snapshot ─────

        private void OpenFullView(ItemDrop.ItemData map)
        {
            if (!LocalMap.IsImprinted(map))
            {
                // Blank map equipped: nothing to show. Tell the player why (plain English —
                // no custom $token exists; a custom one would leak as a literal).
                Player.m_localPlayer?.Message(
                    MessageHud.MessageType.Center, "This map is blank — imprint it at a Surveyor's Table");
                return;
            }

            var survey = LocalMap.ReadSurvey(map);
            if (survey == null)
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Map data unreadable");
                return;
            }

            LocalMap.TryGetBoundOrigin(map, out var origin);
            CartographyViewer.Open(new MapViewRequest
            {
                Survey       = survey,
                BoundOrigin  = origin,
                RadiusMeters = SurveyRadiusMeters,
                Mode         = MapViewerMode.FieldReadOnly, // field view is read-only (no pin edit)
                PinEditor    = null,                        // structurally cannot edit pins
            });
            Plugin.Log.LogInfo("[Trailborne/Cartography] Local Map equipped — opened bounded full view (field, read-only).");
        }

        private void RefreshOpenView(ItemDrop.ItemData map)
        {
            var survey = LocalMap.ReadSurvey(map);
            if (survey == null) return;
            LocalMap.TryGetBoundOrigin(map, out var origin);
            CartographyViewer.Refresh(new MapViewRequest
            {
                Survey       = survey,
                BoundOrigin  = origin,
                RadiusMeters = SurveyRadiusMeters,
                Mode         = MapViewerMode.FieldReadOnly,
                PinEditor    = null,
            });
        }

        private void CloseFullView(string why)
        {
            CartographyViewer.Close();
            Plugin.Log.LogInfo($"[Trailborne/Cartography] Local Map full view closed ({why}).");
        }

        private void ClearBinding(string why)
        {
            CloseFullView(why);
            _hadMapCarried = false;
            _hadMapEquipped = false;
        }

        // ── Inventory / equip probes ──────────────────────────────────────────────────

        /// <summary>The equipped Local Map (right hand), or null. Identifies by the prefab tag.</summary>
        private static ItemDrop.ItemData? GetEquippedLocalMap(Player player)
        {
            var inv = player.GetInventory();
            if (inv == null) return null;
            foreach (var it in inv.GetEquippedItems())
                if (IsLocalMap(it)) return it;
            return null;
        }

        /// <summary>Any carried Local Map instance (equipped or not), or null.</summary>
        private static ItemDrop.ItemData? GetCarriedLocalMap(Player player)
        {
            var inv = player.GetInventory();
            if (inv == null) return null;
            List<ItemDrop.ItemData> all = inv.GetAllItems();
            foreach (var it in all)
                if (IsLocalMap(it)) return it;
            return null;
        }

        private static bool IsLocalMap(ItemDrop.ItemData? item)
        {
            if (item?.m_dropPrefab == null) return false;
            return item.m_dropPrefab.GetComponent<LocalMapItemTag>() != null
                   || item.m_dropPrefab.name == LocalMap.LocalMapName;
        }
    }
}
