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
using UnityEngine.UI;

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
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_promptRoot != null) UnityEngine.Object.Destroy(_promptRoot);
        }

        // Cached state from the throttled inventory poll (read every frame for the input
        // edge-check below, refreshed on the poll cadence).
        private ItemDrop.ItemData? _equippedMap;
        private bool _mapEquipped;

        // §2G.2 equipped HUD prompt ("[E] Open map"). A client-only screen-overlay canvas,
        // built lazily, toggled with the equipped+not-open state. Bottom-centre, consistent
        // with the viewer's own "[Esc] Close map" exit prompt (§2F).
        private GameObject? _promptRoot;
        private Text? _promptText;
        private bool _promptShown;

        private void Update()
        {
            // Client + graphics only. On the dedicated server graphicsDeviceType is Null
            // and there is no local player, so this loop never runs there.
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;

            var player = Player.m_localPlayer;
            if (player == null)
            {
                if (_hadMapCarried || _hadMapEquipped) ClearBinding("no local player");
                _equippedMap = null; _mapEquipped = false;
                return;
            }

            bool tableViewOwnsViewer = CartographyViewer.IsViewerOpen
                                       && CartographyViewer.CurrentMode == MapViewerMode.TableEdit;

            // ── EVERY-FRAME: the open/dismiss edge (a one-frame GetButtonDown would be missed
            //    by the throttled poll). Equipping BINDS the map; it does NOT auto-pop the
            //    overlay — the player ACTIVATES the full view with the USE key (E) while
            //    equipped (§2G, issue-7 correction).
            //
            //    🔴 §2G LOCKED OPEN INPUT — the Use key, NOT the "Map" button. The old design
            //    repurposed vanilla's "Map" button assuming it was dead under nomap; that
            //    premise was INVERTED (decomp: the "Map" button is dead only when Game.m_noMap
            //    is true, and in that world there's no minimap either — but Daniel's playtest
            //    runs nomap=OFF, where M opens vanilla's full map, so binding our viewer to
            //    "Map" stacked both surfaces). The Use key never drives vanilla's Minimap
            //    toggle, so our open is collision-free by construction (AT-LMAP-OPEN-2/3).
            //
            //    Use-key discipline (§2G): only open on an OTHERWISE-IDLE Use press —
            //      • GetHoverObject() == null  → standing at a door/chest/Table, Use interacts
            //        with THAT (the Table's survey+imprint wins — AT-LMAP-TABLE-COEXIST), not
            //        the map.
            //      • !AnyModalOpen             → a text field / inventory / a sign panel
            //        swallow the press instead of tripping a map-open.
            //    The CLOSE (toggle-shut) path is NOT gated by those — our own open viewer
            //    must always be dismissible with Use even though it satisfies the modal check.
            if (_mapEquipped && _equippedMap != null && !tableViewOwnsViewer
                && (ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse")))
            {
                bool ourFieldViewOpen = CartographyViewer.IsViewerOpen
                                        && CartographyViewer.CurrentMode == MapViewerMode.FieldReadOnly;
                if (ourFieldViewOpen)
                    CloseFullView("use key toggle");          // always allow close
                else if (CanOpenOnUse(player))                // open only on an idle press
                    OpenFullView(_equippedMap);
            }

            // ── THROTTLED: the heavier inventory/equip state scan + transitions ──
            float now = Time.realtimeSinceStartup;
            if (now < _nextPoll) return;
            _nextPoll = now + PollSeconds;

            var equippedMap = GetEquippedLocalMap(player);
            var carriedMap  = equippedMap ?? GetCarriedLocalMap(player);
            _equippedMap = equippedMap;

            bool mapCarried  = carriedMap != null;
            bool mapEquipped = equippedMap != null;
            _mapEquipped = mapEquipped;

            // ── Transition: carried-state changed (AT-MAP-DURABLE) ──
            if (mapCarried != _hadMapCarried)
            {
                if (mapCarried)
                    Plugin.Log.LogInfo("[Trailborne/Cartography] Local Map bound (in inventory) — minimap follows its 1000 m disc.");
                else if (!tableViewOwnsViewer)
                    ClearBinding("map left inventory");
                _hadMapCarried = mapCarried;
            }

            // ── Transition: equipped-state changed (AT-MAP-EQUIP) ── close on unequip.
            if (mapEquipped != _hadMapEquipped)
            {
                if (!mapEquipped && !tableViewOwnsViewer) CloseFullView("map unequipped");
                _hadMapEquipped = mapEquipped;
            }

            // Keep the viewer's bound survey fresh while equipped (the imprinted snapshot is
            // static, but the player marker / WorldPins reconcile move — the viewer redraws).
            // Only when WE own the open viewer (field mode), never over a Table session.
            if (mapEquipped && CartographyViewer.IsViewerOpen
                && CartographyViewer.CurrentMode == MapViewerMode.FieldReadOnly)
                RefreshOpenView(equippedMap!);

            // §2G.2: the equipped HUD prompt ("[E] Open map") so a held map never reads as an
            // inert hoe. Shown only while a map is EQUIPPED and the field viewer is NOT open
            // (the open-prompt and the viewer's own "[Esc] Close map" exit prompt are mutually
            // exclusive). Refreshed on the poll cadence — cheap, no need every frame.
            bool fieldViewOpen = CartographyViewer.IsViewerOpen
                                 && CartographyViewer.CurrentMode == MapViewerMode.FieldReadOnly;
            UpdateEquippedPrompt(mapEquipped && !fieldViewOpen);
        }

        /// <summary>
        /// §2G use-key discipline: only open the map on an OTHERWISE-IDLE Use press.
        /// Returns false (suppress the open) when the player is hovering an interactable
        /// (Use should interact with THAT — door/chest/Table — AT-LMAP-TABLE-COEXIST) or when
        /// any modal UI / text input is up (a sign panel, the inventory, a text field, or our
        /// own viewer/panels swallow the press). Mirrors vanilla's own Use gating
        /// (Player.Update → Interact(m_hovering) at decomp :16116).
        /// </summary>
        private static bool CanOpenOnUse(Player player)
        {
            // Hovering a world object → that interaction wins (the Table's survey+imprint, a
            // door, a chest). GetHoverObject() is the public accessor (decomp Player :14699).
            if (player.GetHoverObject() != null) return false;

            // Modal suppression: vanilla text input / inventory, plus our own SBPR modal UIs
            // (sign panels + the map viewer) via the shared SignPanelInputBlock.AnyOpen.
            if (TextInput.IsVisible() || InventoryGui.IsVisible()) return false;
            if (SBPR.Trailborne.Features.Signs.SignPanelInputBlock.AnyOpen) return false;

            return true;
        }

        // ── §2G.2 equipped HUD prompt ("[<$KEY_Use>] $piece_readmap") ───────────────────

        /// <summary>
        /// Show/hide the equipped-map open prompt. Built lazily on first show. The key token is
        /// the VANILLA <c>$KEY_Use</c> bound-key token + the vanilla <c>$piece_readmap</c>
        /// string (decomp MapTable :114046 uses exactly "$KEY_Use $piece_readmap"), localized
        /// through <c>Localization.instance.Localize</c> so the token resolves to the player's
        /// real key (e.g. "E") and stays rebind-correct — the proven CairnInteractable /
        /// SurveyorTableTag idiom. A CUSTOM $piece_* token would leak as a literal (the
        /// 2026-06-05 sign-bug lesson), so we reuse vanilla tokens only.
        /// </summary>
        private void UpdateEquippedPrompt(bool show)
        {
            if (show == _promptShown && _promptRoot != null) return;
            _promptShown = show;

            if (!show)
            {
                if (_promptRoot != null) _promptRoot.SetActive(false);
                return;
            }

            EnsurePromptCanvas();
            if (_promptRoot != null) _promptRoot.SetActive(true);
        }

        private void EnsurePromptCanvas()
        {
            if (_promptRoot != null) return;

            _promptRoot = new GameObject("SBPR_LocalMapPrompt");
            UnityEngine.Object.DontDestroyOnLoad(_promptRoot);
            var canvas = _promptRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4000; // above the HUD, below the open viewer (5000)
            var scaler = _promptRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var txtGo = new GameObject("promptText");
            txtGo.transform.SetParent(_promptRoot.transform, false);
            _promptText = txtGo.AddComponent<Text>();
            _promptText.font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                               ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _promptText.fontSize = 26;
            _promptText.alignment = TextAnchor.LowerCenter;
            _promptText.color = new Color(1f, 0.95f, 0.8f, 0.95f);
            _promptText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _promptText.verticalOverflow = VerticalWrapMode.Overflow;
            _promptText.raycastTarget = false;
            // Vanilla $KEY_Use → the player's bound Use key; $piece_readmap → "Read map".
            string raw = "[<color=yellow><b>$KEY_Use</b></color>] $piece_readmap";
            _promptText.text = Localization.instance != null ? Localization.instance.Localize(raw) : raw;

            var rt = _promptText.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 90f); // bottom-centre, clear of the hotbar
            rt.sizeDelta = new Vector2(900f, 48f);
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
