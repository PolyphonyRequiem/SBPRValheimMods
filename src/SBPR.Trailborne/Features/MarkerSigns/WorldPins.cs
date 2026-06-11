using System;
using System.Collections.Generic;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.MarkerSigns
{
    /// <summary>
    /// The SBPR WorldPin substrate (design lock docs/design/marker-signs-worldpin.md §4,
    /// impl-spec docs/v2/planning/marker-signs-impl-spec.md §3). The vanilla minimap is
    /// only a RENDER TARGET; the source of truth is the world — the live marker-sign ZDOs.
    /// This engine PROJECTS pins from that truth and RECONCILES the projection by scanning
    /// the live ZDO set (derive-by-scan, §4.2), so it is stale-free BY CONSTRUCTION: a
    /// destroyed sign's ZDO is gone from the scan, so its pin is never re-projected, and
    /// because every projected pin is created with <c>save:false</c> nothing of ours is
    /// ever persisted client-side — no stale pin can survive a reconcile.
    ///
    /// This is the SHARED seam the v2 cartography viewer (Surveyor's Table / Local Map,
    /// cards t_38f9c77a / t_7b616020) is meant to consume — ONE pin model, do not fork
    /// (design §5). Those cards are not in flight yet, so the v2 disc-bound, server-
    /// authoritative-scan variant is a documented deferral (see §"Scope: v1 MVP" below);
    /// the public surface here (<see cref="Reconcile"/>, <see cref="ProjectPinnedNow"/>,
    /// <see cref="OnMarkerDestroyed"/>) is the contract the viewer will call.
    ///
    /// ── Scope: v1 MVP (what THIS build wires) ────────────────────────────────────────
    /// v1 nerfs the full M-key map (requirements.md §"Map nerf"); the only live render
    /// target is the player-centered MINIMAP CIRCLE. The minimap only ever shows nearby
    /// (= LOADED) zones, so a CLIENT-SIDE loaded-zone scan via
    /// <c>ZDOMan.GetAllZDOsWithPrefabIterative</c> covers everything the minimap can
    /// display, and AT-PIN-PERSIST / AT-PIN-DESTROY-DURABLE pass by construction (nothing
    /// of ours persists). The DEFERRED v2 piece — a 1000 m disc centered on a bound
    /// Surveyor's Table, fed by a SERVER-authoritative scan/RPC so it can show pins in
    /// zones the client hasn't loaded — is the cartography viewer's job (design §4.2/§4.3,
    /// impl-spec §3.2). It layers on top of this same engine via <see cref="Reconcile"/>'s
    /// <c>boundCenter</c>/<c>boundRadius</c> params; the routed RPC itself is out of scope
    /// here and is filed as the cartography cards' concern.
    ///
    /// CLEAN-SIDE (ADR-0001): every vanilla type touched here (Minimap, PinData, ZDOMan,
    /// ZDO, ZNet, Player) is base-game and fair to read/adapt. No third-party pin mod is
    /// referenced. Built entirely from the decomp viability findings re-verified 2026-06-10.
    /// </summary>
    public static class WorldPins
    {
        // The bound-disc radius for the v2 cartography Local-Map view (design §4.3, the
        // cartography 1000 m lock). In the v1 MVP minimap-circle path we do not impose a
        // disc bound (the minimap's own viewport is the bound), so callers pass
        // <see cref="NoBound"/> to disable disc-clipping.
        public const float CartographyDiscRadius = 1000f;
        public const float NoBound = -1f;

        // The client-local projection map: signZDOID → the PinData we projected for it.
        // TRANSIENT — rebuilt from the live ZDO set each reconcile; never persisted. This
        // is the diff target (which pins to add this pass, which to RemovePin). Keyed by
        // the sign's durable ZDOID (design §3 — ZDOID is IEquatable, stable across save).
        private static readonly Dictionary<ZDOID, Minimap.PinData> Projected =
            new Dictionary<ZDOID, Minimap.PinData>();

        // Scratch buffers reused across reconcile passes to avoid per-pass GC churn.
        private static readonly List<ZDO> ScanBuf = new List<ZDO>();
        private static readonly HashSet<ZDOID> LiveThisPass = new HashSet<ZDOID>();
        private static readonly List<ZDOID> ToRemove = new List<ZDOID>();

        // ─────────────────────────────────────────────────────────────────────────────
        // PUBLIC SURFACE — the contract the gesture (§4) and the future viewer consume.
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Full reconcile of the projected WorldPin set against the LIVE marker-sign ZDOs
        /// (derive-by-scan, design §4.2). Adds pins newly present-and-pinned, removes pins
        /// whose sign is no longer in the live set OR is no longer pinned. Idempotent and
        /// cheap to call on the map-open / periodic / login triggers (§4.4).
        ///
        /// <paramref name="boundCenter"/>/<paramref name="boundRadius"/> optionally clip to
        /// a disc (the v2 cartography 1000 m bound). Pass <see cref="NoBound"/> for the v1
        /// minimap-circle path (no disc clip). CLIENT-ONLY: no-ops without a Minimap
        /// instance (dedicated server has none), so it is safe to call unconditionally.
        /// </summary>
        public static void Reconcile(Vector3 boundCenter = default, float boundRadius = NoBound)
        {
            var map = Minimap.instance;
            if (map == null) return; // headless server / pre-Hud — nothing to render onto.

            // Rebuild the LIVE set from the world: scan each marker prefab's ZDOs and
            // collect the ones that are pinned (and inside the bound disc, if any).
            LiveThisPass.Clear();
            bool bounded = boundRadius > 0f;

            foreach (var m in MarkerSigns.MarkerTypes)
            {
                if (!ScanPrefab(m.PrefabName, out var zdos)) continue;
                foreach (var zdo in zdos)
                {
                    if (zdo == null) continue;
                    // Pinned is the only state that projects a pin (design §4.3).
                    if (!zdo.GetBool(MarkerSigns.ZdoPinned, false)) continue;

                    Vector3 pos = zdo.GetPosition();
                    if (bounded && Vector3.Distance(pos, boundCenter) > boundRadius) continue;

                    ZDOID id = zdo.m_uid;
                    LiveThisPass.Add(id);

                    // Already projected? Leave it (the m_icon override is STABLE per design
                    // §V1, so we need not re-skin). Position is fixed for a placed sign, so
                    // we don't chase moves. New? Project it.
                    if (!Projected.ContainsKey(id))
                    {
                        string markerType = zdo.GetString(MarkerSigns.ZdoMarkerType, "");
                        var def = ResolveType(markerType, m);
                        var pin = ProjectPin(map, pos, def);
                        if (pin != null) Projected[id] = pin;
                    }
                }
            }

            // Diff: drop any projected pin whose sign is no longer live-and-pinned. This is
            // where a destroyed / unpinned sign's pin disappears (AT-PIN-DESTROY-DURABLE):
            // its ZDO is gone from the scan, so it isn't in LiveThisPass, so it is removed.
            ToRemove.Clear();
            foreach (var kv in Projected)
                if (!LiveThisPass.Contains(kv.Key))
                    ToRemove.Add(kv.Key);

            foreach (var id in ToRemove)
            {
                if (Projected.TryGetValue(id, out var pin) && pin != null)
                    map.RemovePin(pin);
                Projected.Remove(id);
            }
        }

        /// <summary>
        /// Fast-path projection for a marker the local player JUST pinned via Shift+E
        /// (design §4.3). Projects the pin immediately so the player sees it without
        /// waiting for the next reconcile tick. Idempotent — a second call for an already-
        /// projected sign is a no-op. Returns true if a pin is now projected for this sign.
        /// CLIENT-ONLY (no-ops without a Minimap instance).
        /// </summary>
        public static bool ProjectPinnedNow(MarkerSignTag tag)
        {
            var map = Minimap.instance;
            if (map == null || tag == null) return false;

            ZDOID id = tag.GetZdoId();
            if (id.IsNone()) return false;
            if (Projected.ContainsKey(id)) return true; // already shown

            var def = MarkerSigns.ByKey(tag.MarkerType) ?? MarkerSigns.ByPrefab(tag.MarkerType);
            // The tag carries the live sprite the server can't load — prefer it.
            var pin = ProjectPin(map, tag.transform.position, def, tag.MarkerIcon);
            if (pin == null) return false;
            Projected[id] = pin;
            return true;
        }

        /// <summary>
        /// Fast-path UNPIN for a marker the local player JUST toggled off via Shift+E, or a
        /// sign that was just destroyed in a loaded zone (the <c>WearNTear.m_onDestroyed</c>
        /// fast path, design §4.3 / impl-spec §4.2 — AT-PIN-DESTROY-LOADED). Removes the
        /// projected pin promptly. The durable OFFLINE case needs no action here: the next
        /// <see cref="Reconcile"/> simply won't find the gone ZDO. CLIENT-ONLY.
        /// </summary>
        public static void RemoveProjected(ZDOID signZdoId)
        {
            var map = Minimap.instance;
            if (map == null) return;
            if (Projected.TryGetValue(signZdoId, out var pin))
            {
                if (pin != null) map.RemovePin(pin);
                Projected.Remove(signZdoId);
            }
        }

        /// <summary>
        /// The marker-sign destroy seam (impl-spec §4.2). Called by
        /// <see cref="MarkerSignTag"/>'s <c>WearNTear.m_onDestroyed</c> subscription on REAL
        /// destruction (decay/raid/demolish — never zone unload). Drops the projected pin
        /// promptly on any client that has the zone loaded. The durable offline backstop is
        /// the reconcile (§3.2). No-ops cleanly if the sign was never pinned/projected.
        /// </summary>
        public static void OnMarkerDestroyed(MarkerSignTag tag)
        {
            if (tag == null) return;
            ZDOID id = tag.GetZdoId();
            if (id.IsNone()) return;
            RemoveProjected(id);
        }

        /// <summary>
        /// Drop ALL projected pins (e.g. on logout / world-exit) without touching any ZDO.
        /// Purely clears the transient client-local render set so a fresh login rebuilds it
        /// from scratch via <see cref="Reconcile"/>. CLIENT-ONLY.
        /// </summary>
        public static void ClearAllProjected()
        {
            var map = Minimap.instance;
            if (map != null)
            {
                foreach (var kv in Projected)
                    if (kv.Value != null) map.RemovePin(kv.Value);
            }
            Projected.Clear();
        }

        /// <summary>
        /// Reset the transient projection map WITHOUT touching any Minimap (used when the
        /// Minimap itself is (re)created — world load / relogin / character switch). The old
        /// PinData refs belong to the destroyed Minimap's pin list; the new Minimap rebuilt
        /// its <c>m_pins</c> from the profile, which never contains our <c>save:false</c>
        /// pins. So we must drop our stale refs and let the next <see cref="Reconcile"/>
        /// rebuild the projection from scratch — otherwise a still-pinned sign would be
        /// skipped as "already projected" yet have no live pin (the AT-PIN-PERSIST trap on a
        /// fresh client join / server restart). Does NOT call RemovePin (the pins are
        /// already gone with the old Minimap).
        /// </summary>
        public static void ResetForNewMap()
        {
            Projected.Clear();
        }

        /// <summary>Count of pins currently projected (diagnostics / tests).</summary>
        public static int ProjectedCount => Projected.Count;

        /// <summary>
        /// Collect the LIVE pinned marker-signs inside a disc, projected as the cartography
        /// <see cref="Cartography.SurveyPin"/> shape, so the forked bounded viewer (card
        /// t_cb831069) renders THE SAME pin model the minimap circle uses — impl spec §2B:
        /// "consume the WorldPins seam from #100; do NOT fork a second pin model." This is
        /// the derive-by-scan source of truth (the live marker-sign ZDOs), disc-clipped to
        /// the bound 1000 m circle, so the Local-Map and Table viewer show the same world
        /// pins without persisting anything of their own.
        ///
        /// CLIENT-ONLY usefulness: the scan reads the RESIDENT ZDO set (loaded zones), the
        /// same coverage the minimap-circle MVP has. The server-authoritative offline-zone
        /// scan that would show pins in unloaded zones is the documented deferral (§"Scope"),
        /// and converges here — when it lands, this method's scan source upgrades and every
        /// caller (viewer + minimap) benefits with no fork. Returns an empty list if the
        /// ZDOMan isn't ready. Does NOT touch the projection map or the minimap.
        /// </summary>
        public static List<Cartography.SurveyPin> CollectInDiscPins(Vector3 boundCenter, float boundRadius)
        {
            var result = new List<Cartography.SurveyPin>();
            if (ZDOMan.instance == null) return result;
            bool bounded = boundRadius > 0f;

            foreach (var m in MarkerSigns.MarkerTypes)
            {
                if (!ScanPrefab(m.PrefabName, out var zdos)) continue;
                foreach (var zdo in zdos)
                {
                    if (zdo == null) continue;
                    if (!zdo.GetBool(MarkerSigns.ZdoPinned, false)) continue;

                    Vector3 pos = zdo.GetPosition();
                    if (bounded && Vector3.Distance(pos, boundCenter) > boundRadius) continue;

                    var def = ResolveType(zdo.GetString(MarkerSigns.ZdoMarkerType, ""), m);
                    int pinType = def != null ? (int)def.VanillaPinType : (int)Minimap.PinType.Icon0;
                    string label = def != null ? def.PinLabel : "Marker";
                    result.Add(new Cartography.SurveyPin(label, pinType, pos, isChecked: false, ownerId: 0L));
                }
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // INTERNALS
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Project ONE WorldPin onto the vanilla minimap: <c>AddPin(..., save:false, ...)</c>
        /// then override <c>PinData.m_icon</c> with the marker's custom sprite (design §V1 —
        /// the override is STABLE across UpdatePins rebuilds; no per-frame re-skin needed).
        /// <c>save:false</c> is MANDATORY — it keeps vanilla from ever persisting our pin to
        /// the player profile, which is what makes the persistence/durability ATs pass by
        /// construction. Returns the PinData, or null if it could not be created.
        /// </summary>
        private static Minimap.PinData? ProjectPin(
            Minimap map, Vector3 pos, MarkerSigns.MarkerType? def, Sprite? overrideIcon = null)
        {
            try
            {
                Minimap.PinType baseType = def != null ? def.VanillaPinType : Minimap.PinType.Icon0;
                string label = def != null ? def.PinLabel : "Marker";

                // save:false — never persisted; isChecked:false — a fresh field pin.
                var pin = map.AddPin(pos, baseType, label, save: false, isChecked: false);
                if (pin == null) return null;

                // Custom sprite override. Prefer an explicitly supplied sprite (the tag's
                // live MarkerIcon on the fast path); else load by the type's icon file.
                Sprite? sprite = overrideIcon;
                if (sprite == null && def != null)
                    sprite = Assets.LoadPngAsSprite(def.IconFile);
                if (sprite != null) pin.m_icon = sprite; // STABLE per design §V1

                return pin;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/WorldPins] ProjectPin failed at {pos}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Enumerate ALL ZDOs of one marker prefab from the resident ZDO set, driving the
        /// vanilla iterative scan to completion (it batches 400 sectors/call; we loop until
        /// it returns true, exactly as vanilla drives it at decomp :37512). Returns false if
        /// the ZDOMan isn't ready. The returned list is a SHARED scratch buffer — consume it
        /// before the next ScanPrefab call.
        /// </summary>
        private static bool ScanPrefab(string prefabName, out List<ZDO> zdos)
        {
            zdos = ScanBuf;
            ScanBuf.Clear();
            var zm = ZDOMan.instance;
            if (zm == null) return false;

            int index = 0;
            // Guard the loop so a pathological sector array can't hang the client.
            int safety = 0;
            while (!zm.GetAllZDOsWithPrefabIterative(prefabName, ScanBuf, ref index))
            {
                if (++safety > 100000)
                {
                    Plugin.Log.LogWarning(
                        $"[Trailborne/WorldPins] ScanPrefab('{prefabName}') hit the iteration safety cap; " +
                        "returning a partial set this pass.");
                    break;
                }
            }
            return true;
        }

        /// <summary>Resolve a marker-type definition from the ZDO's stored type string,
        /// falling back to the prefab the scan came from (the ZDO type should always be set
        /// at placement, but the prefab is an authoritative fallback).</summary>
        private static MarkerSigns.MarkerType? ResolveType(string storedType, MarkerSigns.MarkerType scanned)
        {
            if (!string.IsNullOrEmpty(storedType))
            {
                var byKey = MarkerSigns.ByKey(storedType);
                if (byKey != null) return byKey;
            }
            return scanned;
        }
    }
}
