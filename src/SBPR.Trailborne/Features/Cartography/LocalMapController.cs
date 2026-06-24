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
        private bool _hadMapEquipped;

        // §3 (map-provider-binding-impl-spec): the PROVIDER — the active provider Local Map
        // instance, or null. Identity is the ItemData INSTANCE reference (not prefab/display name —
        // every SBPR_LocalMap shares those). The instance is stable while the item lives in the
        // inventory, which is exactly the provider's lifetime. Client-only, non-replicated, zero
        // server cost. Its lifetime is the item's inventory residency and SURVIVES A RELOG: a fresh
        // controller is attached each session (Minimap.Start) with _provider null, then the one-shot
        // cold-start latch (below) re-derives it from carried imprinted maps so the disc returns
        // without re-equipping (AT-MAP-DURABLE — local-map-provider-persist-impl-spec §3). Survives
        // unequip while still carried.
        private ItemDrop.ItemData? _provider;

        // §3.1 (local-map-provider-persist-impl-spec): one-shot latch — have we run the cold-start
        // carry re-derivation yet this controller life? The controller is attached fresh on every
        // Minimap.Start (LocalMapBootstrapPatch), and Minimap.Start runs once per world-load / relog
        // / character-switch — so the controller's lifetime IS the session, and this resets to false
        // on every relog by construction (no explicit reset needed).
        private bool _coldStartResolved;

        // §4.3/§4.4: disc bind transition tracker, so we log bind/unbind once (not every poll) and
        // can tear down on the unbind edge. Mirrors the old _hadMapCarried transition style.
        private bool _minimapBound;

        // Singleton-ish: the live controller, so the equip patch / table can query state.
        public static LocalMapController? Instance { get; private set; }

        private void Awake() => Instance = this;
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Cached state from the throttled inventory poll (read every frame for the input
        // edge-check below, refreshed on the poll cadence).
        private ItemDrop.ItemData? _equippedMap;
        private bool _mapEquipped;

        private void Update()
        {
            // Client + graphics only. On the dedicated server graphicsDeviceType is Null
            // and there is no local player, so this loop never runs there.
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;

            var player = Player.m_localPlayer;
            if (player == null)
            {
                if (_minimapBound || _hadMapEquipped || _provider != null) ClearBinding("no local player");
                _equippedMap = null; _mapEquipped = false;
                return;
            }

            bool tableViewOwnsViewer = CartographyViewer.IsViewerOpen
                                       && CartographyViewer.CurrentMode == MapViewerMode.TableEdit;

            // ── OPEN/CLOSE on the M (Map) key — SBPR owns the M edge (design §1, 🟢 DECIDED
            //    2026-06-15; spec local-map-mkey-open-impl-spec.md, card t_f9a04fda).
            //
            //    The M press is NOT polled here: a Harmony PREFIX on Minimap.Update
            //    (MinimapMKeyOwnerPatch) catches the Map/JoyMap edge BEFORE vanilla's own
            //    body reads it, routes it into HandleMapKeyPressed() below, then CONSUMES the
            //    edge (ZInput.ResetButtonStatus) so vanilla never toggles its Large map on the
            //    same press (no double-stack in nomap=OFF). The prefix wins the ordering race
            //    deterministically — a MonoBehaviour.Update poll here could run after vanilla's
            //    read and lose. So the open/close gesture lives in HandleMapKeyPressed(), called
            //    by the prefix; this Update() no longer polls an open key.
            //
            //    Why M, not the Use key (E): the old §2G model opened on E to dodge vanilla's
            //    "Map" button. The design SUPERSEDED that — M is the single map key and SBPR
            //    owns it in every state. The E-to-open path is removed entirely; E is once again
            //    purely vanilla's interact key (door/chest/Table), with no map coupling.

            // ── THROTTLED: provider state machine (§3) + disc bind/unbind drive (§4.4, §5) ──
            float now = Time.realtimeSinceStartup;
            if (now < _nextPoll) return;
            _nextPoll = now + PollSeconds;

            // ── §3 (persist) COLD-START CARRY RE-DERIVATION — one-shot per session ───────────
            // local-map-provider-persist-impl-spec §3.2: the controller is attached fresh each
            // session with _provider == null, so a map carried-but-UNEQUIPPED across a relog would
            // otherwise show no disc until re-equipped (the bug — AT-PERSIST-CARRY). Run ONCE, before
            // the equip-edge machine, re-deriving the provider from load-restored inventory state.
            // The latch (not a "_provider == null" re-test) is what preserves §3.4: after it fires, an
            // in-session drop→re-pickup STILL stays unbound (AT-PERSIST-UNBIND-INTACT) — do NOT relax
            // this into "re-derive whenever _provider == null".
            if (!_coldStartResolved)
            {
                _coldStartResolved = true;            // one-shot regardless of outcome
                if (_provider == null)
                    _provider = ResolveColdStartProvider(player);   // may stay null (nothing carried)
            }

            var equippedMap = GetEquippedLocalMap(player);
            _equippedMap = equippedMap;
            bool mapEquipped = equippedMap != null;
            _mapEquipped = mapEquipped;

            // ── §3 PROVIDER STATE MACHINE ───────────────────────────────────────────────
            // §3.2 BIND: equipping a map (that isn't already the provider) makes it the provider —
            //   this IS the most-recent-equipped tie-break (equip A → A; later equip B → B). No
            //   separate equip hook: assigning on "equipped ≠ provider" is sufficient.
            if (mapEquipped && !ReferenceEquals(equippedMap, _provider))
            {
                _provider = equippedMap;
                Plugin.Log.LogInfo("[Trailborne/Cartography] Local Map provider set (equipped).");
            }
            // §3.3 HOLD / §3.4 UNBIND: with a provider but none equipped this poll, the provider
            //   persists ONLY while still carried. The single ContainsItem check covers all three
            //   unbind paths (drop / trade / death all remove the instance from the live inventory).
            else if (_provider != null)
            {
                var inv = player.GetInventory();
                if (inv == null || !inv.ContainsItem(_provider))
                {
                    _provider = null; // §3.4 unbind — disc torn down below on the bind-state transition
                    Plugin.Log.LogInfo("[Trailborne/Cartography] Local Map provider cleared (left inventory).");
                }
            }

            // ── LIVE FIELD-WRITE axis (live-update-cartography-impl-spec §2, card t_9c54d492) ──
            // Stamp the Kit-revealed ground into every carried imprinted in-region Local Map's stored
            // blob (the PLURAL write-set, keyed on HOLD + Kit — independent of the equip-keyed render
            // provider above). Placed AFTER the provider machine (§4 cold-start ordering) so a freshly
            // equipped map is already the provider when the write happens (readable logs: provider-set
            // then writes). LiveFieldWrite self-throttles to ~2 s and is the actual fix for issue 5
            // (carried maps never grew because nothing but Imprint wrote their blob). Client-only +
            // dirty-checked: standing still / no Kit / re-covering known ground ⇒ zero writes (§3).
            LiveFieldWrite.Tick(player);

            // ── §4.4 + §5 DISC DRIVE: render the disc iff (provider bound + imprinted + nomap ON). ──
            // §5: gate the RENDER (not the provider) on the live client nomap flag. In nomap-OFF the
            //   vanilla global minimap owns the corner — the local-map disc must stand down there
            //   (design §6). The provider machine above still runs in both modes (the full view +
            //   future dual-write key on it); only the minimap render is nomap-on-gated.
            bool shouldBindDisc = _provider != null
                                  && Game.m_noMap                       // §5 — nomap-ON only
                                  && LocalMap.IsImprinted(_provider!)   // §3.5 — blank provider shows no disc
                                  && LocalMap.ReadSurvey(_provider!) != null;

            if (shouldBindDisc)
                DriveMinimapDisc(_provider!);
            else
                UnbindMinimapDisc();

            // ── Transition: equipped-state changed (AT-MAP-EQUIP) ── close the FULL VIEW on unequip.
            // (The disc is independent — it stays bound to the still-carried provider underneath.)
            if (mapEquipped != _hadMapEquipped)
            {
                if (!mapEquipped && !tableViewOwnsViewer) CloseFullView("map unequipped");
                _hadMapEquipped = mapEquipped;
            }

            // Keep the full view's bound survey fresh while equipped + WE own the open viewer.
            if (mapEquipped && CartographyViewer.IsViewerOpen
                && CartographyViewer.CurrentMode == MapViewerMode.FieldReadOnly)
                RefreshOpenView(equippedMap!);
        }

        // ── §4.4 disc bind/unbind helpers (drive the carry minimap from provider state) ──

        /// <summary>
        /// §4.4: build the MapViewRequest for the provider's imprinted survey and (idempotently) show
        /// + refresh the carry minimap disc. Logs once on the bind transition (not every poll). The
        /// viewer renders it player-centred (R1), table-anchored shroud, no edge-arrow.
        /// </summary>
        private void DriveMinimapDisc(ItemDrop.ItemData provider)
        {
            var survey = LocalMap.ReadSurvey(provider);
            if (survey == null) { UnbindMinimapDisc(); return; } // defensive — gate already checked

            LocalMap.TryGetBoundOrigin(provider, out var origin);
            // §3.6 (disc-name-hint-impl-spec): the under-disc caption's NAME line. FORMATTED here
            // ("Local map for <Table>") — a deliberate split from the modal cartouche, which stays
            // BARE (§3.4). null when the provider has no imprinted name (pre-naming map) → the disc
            // caption renders the hint line only, never an empty "Local map for ". Carried via the
            // dedicated MapViewRequest.Caption (NOT Title — Title is force-cleared for the disc).
            string? caption = LocalMap.TryGetName(provider, out var capName)
                ? LocalMap.FormatDisplayName(capName)
                : null;
            CartographyViewer.BindMinimap(new MapViewRequest
            {
                Survey       = survey,
                BoundOrigin  = origin,
                RadiusMeters = SurveyRadiusMeters,
                Mode         = MapViewerMode.FieldReadOnly, // read-only; the viewer also forces this
                PinEditor    = null,
                Title        = string.Empty,                // a minimap shows no cartouche
                Caption      = caption,                     // §3.6 — name line UNDER the disc (disc-only)
                Minimap      = true,
            });

            if (!_minimapBound)
            {
                _minimapBound = true;
                Plugin.Log.LogInfo("[Trailborne/Cartography] Carry minimap disc bound (provider imprinted, nomap on).");
            }
        }

        /// <summary>§4.3: tear the disc down on the unbind transition (provider cleared / blank /
        /// nomap-off). Idempotent — only logs + calls UnbindMinimap on the bound→unbound edge.</summary>
        private void UnbindMinimapDisc()
        {
            if (!_minimapBound) return;
            _minimapBound = false;
            CartographyViewer.UnbindMinimap();
            Plugin.Log.LogInfo("[Trailborne/Cartography] Carry minimap disc unbound.");
        }

        /// <summary>
        /// SBPR owns the M edge (design §1). Called by MinimapMKeyOwnerPatch.Prefix when M is
        /// pressed in a map-toggle-allowed context (spec §4.2). Decides what M does from
        /// provider + viewer state:
        ///   • the Table owns the viewer (TableEdit) → M doesn't fight it.
        ///   • our field viewer already open → close it (toggle-shut, AT-MKEY-CLOSE).
        ///   • a local map is BOUND (provider != null, still carried, even if UNEQUIPPED) → open it.
        ///   • otherwise (no bound map) → nothing (design §1 row 1; the prefix still consumes M).
        /// </summary>
        public void HandleMapKeyPressed()
        {
            bool tableOwns = CartographyViewer.IsViewerOpen
                             && CartographyViewer.CurrentMode == MapViewerMode.TableEdit;
            if (tableOwns) return;   // the Table owns the viewer; M doesn't fight it

            bool ourFieldViewOpen = CartographyViewer.IsViewerOpen
                                    && CartographyViewer.CurrentMode == MapViewerMode.FieldReadOnly;
            if (ourFieldViewOpen) { CloseFullView("M key toggle"); return; }

            // OPEN: design §1 row 2 — opens the BOUND map, which survives unequip while still
            // carried. Prefer the live equipped instance; fall back to the provider (the
            // most-recently-equipped, still-carried instance the §3 state machine tracks).
            // This is the one behavioral widening vs the old E-gesture (which only fired while
            // equipped) and it is required by the design (AT-MKEY-OPEN tests both cases).
            var toOpen = _equippedMap ?? _provider;
            if (toOpen != null) OpenFullView(toOpen);
            // else: no bound map → M does nothing (row 1). Prefix already consumed the edge.
        }

        /// <summary>
        /// True when M should be claimed by SBPR this frame (spec §4.3). Mirrors vanilla's own
        /// Minimap.Update suppression gate (decomp :47074) PLUS SBPR modal state, so we don't
        /// steal M while typing or in a menu. NOTE: unlike the old Use-key discipline there is
        /// NO GetHoverObject() guard — M is not an interaction key, so hovering a door/chest/
        /// Table does NOT suppress M (that was the whole point of moving off Use:
        /// AT-MKEY-TABLE-COEXIST — E still surveys/imprints at a Table, M there opens the map,
        /// no shared key, no tension).
        /// </summary>
        public bool MKeyContextAllowsMapToggle()
        {
            // Vanilla's :47074 typing/menu guards — don't claim M while a text field, console,
            // inventory or pause menu is up (so a typed "m" still reaches them).
            if (Chat.instance != null && Chat.instance.HasFocus()) return false;
            if (Console.IsVisible() || TextInput.IsVisible() || InventoryGui.IsVisible()) return false;
            if (Menu.IsActive()) return false;

            // SBPR modal suppression — but NOT our own field viewer (M must be able to CLOSE it,
            // §4.2). A sign / paint panel up → M is text/other, don't claim it.
            if (SBPR.Trailborne.Features.Signs.SignPanelInputBlock.AnyOpen
                && !(CartographyViewer.IsViewerOpen
                     && CartographyViewer.CurrentMode == MapViewerMode.FieldReadOnly))
                return false;

            return true;
        }

        // ── §2G.2 open-hint RELOCATED (card t_26bba85b) ────────────────────────────────
        // The equipped HUD prompt ("[<$KEY_Map>] $piece_readmap") that used to float
        // bottom-centre here is GONE. Daniel's 2026-06-19 v0.2.27 playtest: the bottom hint
        // "doesn't really work for me" — it was spatially divorced from the disc it acts on and
        // only showed while equipped (narrower than when M actually opens the map, §2.4 of
        // local-map-disc-name-hint-impl-spec.md). The open-hint now lives as a two-line caption
        // (map NAME + the same rebind-correct [<$KEY_Map>] $piece_readmap hint) UNDER the carry
        // minimap disc, built on the disc MapSurface (ShowCaption) and driven from the provider
        // name (DriveMinimapDisc → MapViewRequest.Caption). The token stays vanilla $KEY_Map +
        // $piece_readmap (the 2026-06-05 sign-bug literal-leak lesson stands). See MapSurface.cs
        // (_discCaption) and MapViewer.Awake (_disc ShowCaption = true).

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
            LocalMap.TryGetName(map, out var mapName); // §2B.1: imprinted Table name → viewer title
            CartographyViewer.Open(new MapViewRequest
            {
                Survey       = survey,
                BoundOrigin  = origin,
                RadiusMeters = SurveyRadiusMeters,
                Mode         = MapViewerMode.FieldReadOnly, // field view is read-only (no pin edit)
                PinEditor    = null,                        // structurally cannot edit pins
                Title        = mapName,                     // §2B.1 — bound Table name as the on-screen title ("" → hidden)
            });
            Plugin.Log.LogInfo("[Trailborne/Cartography] Local Map equipped — opened bounded full view (field, read-only).");
        }

        private void RefreshOpenView(ItemDrop.ItemData map)
        {
            var survey = LocalMap.ReadSurvey(map);
            if (survey == null) return;
            LocalMap.TryGetBoundOrigin(map, out var origin);
            LocalMap.TryGetName(map, out var mapName); // §2B.1: keep the title stable across refreshes
            CartographyViewer.Refresh(new MapViewRequest
            {
                Survey       = survey,
                BoundOrigin  = origin,
                RadiusMeters = SurveyRadiusMeters,
                Mode         = MapViewerMode.FieldReadOnly,
                PinEditor    = null,
                Title        = mapName,
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
            UnbindMinimapDisc();   // §4.3 tear the disc down with the rest of the binding
            _provider = null;       // §3.4 the provider is gone (no player / world teardown)
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

        /// <summary>
        /// §3.2 (local-map-provider-persist-impl-spec): re-derive the most plausible provider from
        /// LOAD-RESTORED inventory state, ONCE at cold-start (driven by the _coldStartResolved latch).
        /// This is what lets a carried-but-unequipped imprinted map keep its disc across a relog
        /// (AT-PERSIST-CARRY), since a fresh controller starts with _provider == null each session.
        ///   (a) equipped local map, if any — belt-and-suspenders with vanilla's EquipInventoryItems
        ///       self-heal (§3.4): binds it turn 1 instead of waiting a poll for the re-equip.
        ///   (b) else the FIRST carried IMPRINTED local map in inventory-slot order — deterministic
        ///       (AT-PERSIST-MULTI); imprinted-filtered because a blank provider renders no disc, so
        ///       binding one would look like the bug isn't fixed (§3.2 note). Self-corrects to exact
        ///       most-recent on the next equip via the §3.2 edge bind.
        ///   (c) else null — nothing imprinted carried ⇒ no provider ⇒ no disc (correct).
        /// m_customData (hence IsImprinted/ReadSurvey) and m_dropPrefab (hence IsLocalMap) are both
        /// restored on load before this runs (spec §3.2). Clean-side: all reads are base-game.
        /// </summary>
        private static ItemDrop.ItemData? ResolveColdStartProvider(Player player)
        {
            // (a) equipped wins — matches the in-session §3.2 bind and the vanilla equip self-heal.
            var equipped = GetEquippedLocalMap(player);
            if (equipped != null) return equipped;

            // (b) first carried IMPRINTED local map, deterministic inventory-slot order.
            var inv = player.GetInventory();
            if (inv == null) return null;
            foreach (var it in inv.GetAllItems())
                if (IsLocalMap(it) && LocalMap.IsImprinted(it!)) return it;

            // (c) nothing to restore.
            return null;
        }

        // NOTE (§8 / local-map-provider-persist-impl-spec §3.2): GetCarriedLocalMap's old per-poll
        // "first carried map in inventory" selection was RETIRED in the provider-binding rework — it
        // is wrong for the live provider model, where carry-presence is tested via
        // Inventory.ContainsItem(_provider) (§3.4) and the provider is the most-recently-EQUIPPED
        // still-carried map (§3.3), not re-derived each poll. A SCOPED, IMPRINTED-FILTERED version of
        // that scan is resurrected ONLY inside ResolveColdStartProvider above, gated by the one-shot
        // _coldStartResolved latch so it runs exactly once per session — re-deriving the binding a
        // relog dropped WITHOUT reintroducing the per-poll re-bind that would violate §3.4.

        private static bool IsLocalMap(ItemDrop.ItemData? item)
        {
            if (item?.m_dropPrefab == null) return false;
            return item.m_dropPrefab.GetComponent<LocalMapItemTag>() != null
                   || item.m_dropPrefab.name == LocalMap.LocalMapName;
        }
    }
}
