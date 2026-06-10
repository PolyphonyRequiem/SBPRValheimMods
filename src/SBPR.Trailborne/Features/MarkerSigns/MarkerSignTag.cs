using System;
using UnityEngine;

namespace SBPR.Trailborne.Features.MarkerSigns
{
    /// <summary>
    /// Per-instance MonoBehaviour tag on each placed Marker Sign. Carries the marker's
    /// ZDO-backed state and the destroy seam the WorldPin substrate consumes. Mirrors
    /// the owner-write ZDO pattern of <see cref="SBPR.Trailborne.Features.Signs.SignTag"/>.
    ///
    /// ZDO fields (save/wire contracts — locked in the first PR that registers them,
    /// never renamed; a rename orphans every placed instance, the same reason Pigments
    /// kept SBPR_Ink*):
    ///   • SBPR_MarkerType  (string) — the marker type key (poi/mining/shelter/portal).
    ///     Usually derivable from the prefab name, but stored so a server-side scan can
    ///     read the type straight off the ZDO without a prefab→type map.
    ///   • SBPR_Pinned      (bool)   — is this marker currently pinned on the placer's
    ///     map? Toggled by the Shift+E gesture (wired in the gesture milestone).
    ///   • SBPR_PinIconColor (string) — RESERVED, unused in the first cut (Q1 defers per-
    ///     pin color). Reserved now so the color fast-follow needs no ZDO migration.
    ///   • SBPR_PinTextColor (string) — RESERVED, unused in the first cut. Same.
    ///
    /// 🔴 Destroy hook (design §4.3 / impl-spec §4.2): we subscribe the marker's
    /// <c>WearNTear.m_onDestroyed</c> — a PUBLIC <c>Action</c> field fired owner-side
    /// inside <c>WearNTear.Destroy()</c> on REAL destruction only (decay / raid /
    /// demolish), NOT on zone unload. We deliberately do NOT use Unity's
    /// <c>OnDestroy</c>, which also fires on zone unload and would wrongly unpin a
    /// merely-unloaded sign — the single most important wiring detail in the feature.
    /// No Harmony patch is needed; it is a public subscribe seam.
    ///
    /// The owning component lifecycle (comfort/decay/visual) lives elsewhere; this tag
    /// is purely the marker's identity + pin-state + destroy-notification surface. All
    /// reads/writes guard on a live ZDO so the placement GHOST (no ZDO) is a no-op.
    /// </summary>
    public class MarkerSignTag : MonoBehaviour
    {
        private ZNetView? nview;
        private WearNTear? wnt;
        private bool destroyHookSubscribed;

        // Set by registration immediately after AddComponent (the prefab-template value),
        // and re-read from the ZDO on a placed instance in Awake. The ZDO is authoritative
        // for a placed sign; this template default seeds a freshly placed one.
        public string MarkerType = "";

        // The marker's icon sprite (the type-coded glyph). Set by registration so the
        // panel can show it for reference (AT-MARK-1) and the WorldPin projection can
        // override the pin's m_icon with it. Null on the headless server (no sprites).
        public Sprite? MarkerIcon;

        private void Awake()
        {
            nview = GetComponent<ZNetView>();
            wnt   = GetComponent<WearNTear>();

            // On a placed instance, the ZDO is the source of truth for the marker type;
            // fall back to the template default (set at registration) when the ZDO hasn't
            // been written yet (freshly placed this frame) or on the ghost (no ZDO).
            if (nview != null && nview.GetZDO() != null)
            {
                string stored = nview.GetZDO().GetString(MarkerSigns.ZdoMarkerType, "");
                if (!string.IsNullOrEmpty(stored))
                    MarkerType = stored;
                else if (!string.IsNullOrEmpty(MarkerType) && nview.IsOwner())
                    // First spawn of a freshly built marker: persist the type the player
                    // built so a later server-side scan can read it off the ZDO.
                    nview.GetZDO().Set(MarkerSigns.ZdoMarkerType, MarkerType);
            }

            SubscribeDestroyHook();
        }

        /// <summary>
        /// Subscribe the marker's <c>WearNTear.m_onDestroyed</c> so a REAL destruction
        /// (decay/raid/demolish — never a zone unload) drops the WorldPin promptly on any
        /// client with the zone loaded (the fast path, AT-PIN-DESTROY-LOADED). Idempotent.
        /// </summary>
        private void SubscribeDestroyHook()
        {
            if (destroyHookSubscribed || wnt == null) return;
            wnt.m_onDestroyed += OnSignDestroyed;
            destroyHookSubscribed = true;
        }

        private void OnDestroy()
        {
            // Unsubscribe to avoid a dangling delegate on a pooled/destroyed component.
            // NOTE: Unity OnDestroy is NOT our unpin signal (it fires on zone unload too);
            // it is used here ONLY to detach the WearNTear delegate. The real unpin is
            // driven by the WearNTear.m_onDestroyed callback below + the reconcile scan.
            if (destroyHookSubscribed && wnt != null)
            {
                wnt.m_onDestroyed -= OnSignDestroyed;
                destroyHookSubscribed = false;
            }
        }

        /// <summary>
        /// Fired owner-side on REAL destruction of the marker sign. Drops the projected
        /// WorldPin for this sign locally (the prompt online path). The durable offline
        /// case is handled by the derive-by-scan reconcile, which simply never re-projects
        /// a sign whose ZDO is gone.
        ///
        /// NOTE (milestone gating): the WorldPin projection/reconcile engine
        /// (WorldPins.cs) is not yet built — see the review-required block on this card.
        /// Until it lands this callback only records the destroy for that engine to
        /// consume; it intentionally does not touch the vanilla Minimap directly here, so
        /// there is no half-wired pin path. When WorldPins lands, this calls
        /// WorldPins.OnMarkerDestroyed(zdoid).
        /// </summary>
        private void OnSignDestroyed()
        {
            // The engine seam this will call once WorldPins.cs exists:
            //   WorldPins.OnMarkerDestroyed(this);
            // Kept as a no-op (not a silent one — logged at debug) until the engine lands,
            // so M1 ships a real, inert-by-design hook rather than dead or fake code.
            if (Plugin.Log != null)
                Plugin.Log.LogDebug(
                    $"[Trailborne/MarkerSigns] Marker sign destroyed (type='{MarkerType}'). " +
                    "WorldPin unpin will be handled by the reconcile engine once it lands.");
        }

        /// <summary>Current pinned state from the ZDO (false on the ghost / no ZDO).</summary>
        public bool ReadPinned()
        {
            if (nview == null || nview.GetZDO() == null) return false;
            return nview.GetZDO().GetBool(MarkerSigns.ZdoPinned, false);
        }

        /// <summary>
        /// Owner-write the pinned state. Returns false if the ZDO isn't ready (ghost /
        /// uninitialised) so the caller can avoid acting on a non-persisted toggle.
        /// Mirrors SignTag.WriteColors' owner-claim shape.
        /// </summary>
        public bool WritePinned(bool pinned)
        {
            if (nview == null || nview.GetZDO() == null) return false;
            if (!nview.IsOwner()) nview.ClaimOwnership();
            nview.GetZDO().Set(MarkerSigns.ZdoPinned, pinned);
            return true;
        }

        /// <summary>The marker sign's durable ZDOID — the WorldPin's durable key (design §3).
        /// Returns <c>ZDOID.None</c> on the ghost / no ZDO.</summary>
        public ZDOID GetZdoId()
        {
            if (nview == null || nview.GetZDO() == null) return ZDOID.None;
            return nview.GetZDO().m_uid;
        }
    }
}
