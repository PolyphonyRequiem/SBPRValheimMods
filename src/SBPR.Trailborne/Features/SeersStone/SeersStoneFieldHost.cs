// ============================================================================
//  Trailborne v4 (Mountains) — Seer's Stone FIELD HOST (local-player wisp manager)
// ----------------------------------------------------------------------------
//  Design : docs/design/seers-stone.md §5/§6. One engine-side piece that needs the live
//           local Player:
//    • SeersStoneFieldHost — a MonoBehaviour on the local Player that owns the WispField
//      and ticks it each frame (worn-state → spawn/despawn wisps).
//
//  Pinning is NO LONGER an input patch. As of the 2026-06-27 re-lock the wisp itself is a
//  vanilla Hoverable+Interactable (WispBehaviour): walk up, press Use (E), the patch pins
//  and the wisp dims — driven by vanilla's own FindHoverObject/Interact pipeline. The old
//  Alt+E PinByLookInput Player.Update postfix is RETIRED (it pinned the ray's SOURCE object,
//  not the off-source wisp the player aimed at — the playtest defect).
//
//  Client-only (Player.m_localPlayer / GameCamera / Minimap exist only on a client).
//  Server-gated registration via Plugin (the LocalPlayerAttach patch) + the host attach hook.
//
//  Clean-side (ADR-0001): patches base-game Player.OnSpawned only (to attach the host);
//  the wisp interaction itself needs NO patch (vanilla drives it through the interface).
// ============================================================================

using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.SeersStone
{
    /// <summary>
    /// Local-player host for the personal wisp field. Attached to <c>Player.m_localPlayer</c> by the
    /// <see cref="LocalPlayerAttach"/> hook; owns one <see cref="WispField"/> and ticks it each frame
    /// with the current worn-state. Destroyed with the Player (logout/death) — its <c>OnDestroy</c>
    /// tears down every wisp (now parented to world root, so teardown is the explicit ClearAll).
    /// </summary>
    public sealed class SeersStoneFieldHost : MonoBehaviour
    {
        private WispField _field = null!;   // set in Awake (MonoBehaviour lifecycle; never used before)
        private Player _player = null!;

        private void Awake()
        {
            _player = GetComponent<Player>();
            _field = new WispField();
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
}
