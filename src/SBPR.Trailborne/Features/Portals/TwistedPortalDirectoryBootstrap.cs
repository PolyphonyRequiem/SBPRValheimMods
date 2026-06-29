// ============================================================================
//  Trailborne v3 (Swamp) — Twisted Portal directory RPC BOOTSTRAP (L2)
// ----------------------------------------------------------------------------
//  Card     : t_ccb454f8 (L2). Registers the server-authoritative directory's
//             routed RPCs once per session, on BOTH sides.
//  Impl spec: docs/v3/planning/twisted-portal-impl-spec.md §2 (REQUIRED).
//
//  WHY Game.Start. The directory RPCs are GLOBAL routed RPCs (ZRoutedRpc.Register),
//  not per-ZNetView ones — so they must be registered once against the live
//  ZRoutedRpc.instance, not per-portal. Vanilla registers its global routed RPCs in
//  Game.Start (decomp :84118-84131: SleepStart, Ping/Pong, RPC_SetConnection,
//  RPC_DiscoverLocationResponse on all peers + RPC_DiscoverClosestLocation gated on
//  IsServer()). We mirror that exact hook + gate: by Game.Start, ZNet.Awake has already
//  constructed ZRoutedRpc (:66719) and the peer set is up, so registration is safe.
//
//  RUNS ON THE DEDICATED SERVER TOO (load-bearing). Unlike the Minimap/Hud bootstraps
//  (client-only — the server has no Minimap), Game.Start fires on the dedicated server
//  as well, which is exactly what L2 needs: the SERVER must register the REQUEST handler
//  so it can answer clients. TwistedPortalDirectory.Register() does the IsServer() gate
//  internally (request handler server-only, response handler everywhere).
//
//  IDEMPOTENT. Game.Start can fire again on a world reload (disconnect → rejoin /
//  singleplayer world switch). ZRoutedRpc is reconstructed each time ZNet.Awake runs,
//  so its m_functions map is fresh — meaning we MUST re-register each session, but must
//  NOT double-register within one ZRoutedRpc instance (Dictionary.Add throws on a dup
//  method-hash). We track the ZRoutedRpc instance we last registered against and only
//  re-register when it's a different (new) instance — a fresh session — never twice on
//  the same one.
//
//  Registered in Plugin.Awake (harmony.PatchAll) so PatchCheck stays green (an
//  attributed-but-unregistered patch ERRORs at boot — the meta-bug guard).
//
//  Clean-side (ADR-0001): postfixes base-game Game only; calls our own directory.
// ============================================================================

using HarmonyLib;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Portals
{
    [HarmonyPatch(typeof(Game), "Start")]
    public static class TwistedPortalDirectoryBootstrap
    {
        // The ZRoutedRpc instance we last registered our RPCs against. ZRoutedRpc is rebuilt every
        // session (ZNet.Awake → new ZRoutedRpc, decomp :66719), so a new instance == a new session
        // that needs (re-)registration; the same instance must never be registered twice (the
        // Dictionary.Add dup-hash throw). Reference compare, not a bool, so a reload re-registers.
        private static object? _registeredAgainst;

        [HarmonyPostfix]
        private static void Postfix()
        {
            // Server-gate parity with the rest of SBPR registration (belt-and-braces; the directory
            // is content the OnSBServer fan-out governs).
            if (!ServerContext.OnSBServer) return;

            var rpc = ZRoutedRpc.instance;
            if (rpc == null)
            {
                Plugin.Log.LogWarning(
                    "[Trailborne/TwistedPortal] Game.Start: ZRoutedRpc.instance is null — directory RPCs NOT registered " +
                    "this session (the look-to-aim picker falls back to the local-window candidate set).");
                return;
            }

            // Already registered against THIS ZRoutedRpc instance (Game.Start re-fired without a new
            // ZNet) → nothing to do; re-registering would throw on the duplicate method-hash add.
            if (ReferenceEquals(_registeredAgainst, rpc)) return;

            TwistedPortalDirectory.Register();
            _registeredAgainst = rpc;
        }
    }
}
