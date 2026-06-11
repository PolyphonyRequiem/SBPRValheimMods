using System;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.MarkerSigns
{
    /// <summary>
    /// The WorldPin reconcile-TRIGGER driver (design §4.4, impl-spec §3.3). Drives
    /// <see cref="WorldPins.Reconcile"/> off the vanilla minimap lifecycle so the projected
    /// pin set stays consistent with the live marker-sign ZDOs:
    ///
    ///   • <b>Map open</b> — a <c>Minimap.SetMapMode</c> postfix reconciles whenever the map
    ///     transitions INTO a visible mode (Small or Large). This is the load-bearing
    ///     trigger (design §4.4): it is the moment a returning offline owner's stale pin
    ///     clears, because the scan no longer finds the destroyed sign's ZDO.
    ///   • <b>Light periodic tick</b> — a throttled <c>Minimap.Update</c> postfix reconciles
    ///     every <see cref="TickSeconds"/> while the map is live, so a Shift+E / destroy on
    ///     ANOTHER client is picked up without reopening the map.
    ///
    /// Both hooks are CLIENT-ONLY by construction: <c>Minimap.instance</c> is null on the
    /// dedicated server, so <see cref="WorldPins.Reconcile"/> early-outs there. The v1 MVP
    /// uses the unbounded (minimap-circle) reconcile — no disc clip. The v2 cartography
    /// viewer, when it lands, will additionally call <c>Reconcile(boundCenter, 1000f)</c>
    /// for its bound disc; that is the cartography cards' integration point, not wired here.
    ///
    /// CLEAN-SIDE (ADR-0001): patches the base-game <c>Minimap</c> only. Registered in
    /// Plugin.cs (the PatchCheck watchdog asserts it actually wove a method).
    /// </summary>
    [HarmonyPatch]
    public static class WorldPinReconcilePatches
    {
        // Periodic reconcile cadence while the map is live. A few seconds is responsive
        // enough to catch another client's Shift+E/destroy without scanning every frame
        // (the scan is O(loaded-sectors); cheap, but not free). Playtest-tunable (§4.4).
        private const float TickSeconds = 3f;

        private static float _nextTick;
        // Track the last mode we saw so the SetMapMode postfix can detect a transition
        // INTO a visible mode (vs a redundant set-to-same or a close).
        private static Minimap.MapMode _lastMode = Minimap.MapMode.None;

        // ── Map (re)created: drop the stale projection so it rebuilds from scratch. ──
        // Minimap.Awake assigns m_instance and rebuilds m_pins from the player profile
        // (which never holds our save:false pins). Our static Projected map still holds
        // PinData refs from the PREVIOUS Minimap — stale after a world load / relogin /
        // character switch. Clear them here so the first Reconcile re-projects every still-
        // pinned sign onto the NEW minimap (the AT-PIN-PERSIST guard: fresh client join /
        // server restart must not leave a still-pinned marker unrendered).
        [HarmonyPatch(typeof(Minimap), "Awake")]
        [HarmonyPostfix]
        public static void Awake_Postfix()
        {
            try
            {
                WorldPins.ResetForNewMap();
                _lastMode = Minimap.MapMode.None;
                _nextTick = 0f; // let the next Update tick reconcile promptly
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/WorldPins] Minimap.Awake reset suppressed: {e.Message}");
            }
        }

        // ── Map-open trigger: reconcile on entering a visible map mode. ──────────────
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
        [HarmonyPostfix]
        public static void SetMapMode_Postfix(Minimap.MapMode mode)
        {
            try
            {
                bool wasHidden = _lastMode == Minimap.MapMode.None;
                _lastMode = mode;
                // Reconcile when we OPEN/expand the map (None→Small/Large, or Small→Large).
                // The load-bearing case is opening from hidden; expanding to Large also
                // refreshes so a stale pin can't linger on the big map.
                if (mode != Minimap.MapMode.None && (wasHidden || mode == Minimap.MapMode.Large))
                {
                    WorldPins.Reconcile();
                    // Re-arm the periodic tick relative to this explicit reconcile.
                    _nextTick = Time.realtimeSinceStartup + TickSeconds;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/WorldPins] SetMapMode reconcile suppressed: {e.Message}");
            }
        }

        // ── Light periodic tick while the map is live. ───────────────────────────────
        [HarmonyPatch(typeof(Minimap), "Update")]
        [HarmonyPostfix]
        public static void Update_Postfix(Minimap __instance)
        {
            if (__instance == null) return;
            // Only tick while a map surface is actually showing — no point scanning while
            // the minimap is in None (e.g. a no-map context).
            if (__instance.m_mode == Minimap.MapMode.None) return;

            float now = Time.realtimeSinceStartup;
            if (now < _nextTick) return;
            _nextTick = now + TickSeconds;

            try
            {
                WorldPins.Reconcile();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/WorldPins] periodic reconcile suppressed: {e.Message}");
            }
        }
    }
}
