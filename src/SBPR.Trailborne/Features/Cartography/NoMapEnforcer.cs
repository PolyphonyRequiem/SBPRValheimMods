// ============================================================================
//  Trailborne v2 cartography — NoMap enforcement (the tier's enforced precondition)
// ----------------------------------------------------------------------------
//  Impl spec §3.5 (cartography-impl-spec.md), requirements §3.5, card t_2f9fc470
//  (parent spec card t_8c9abf6f / PR #115).
//
//  The whole cartography tier — "no global map → earn bounded local maps" — was
//  built assuming Game.m_noMap is on, but NOTHING enforced it. On a fresh/local
//  world the GlobalKeys.NoMap key is NOT set until a host runs `nomap` by hand,
//  so the forked Local Map viewer competed with a free full-world map. This patch
//  makes the mod OWN its own premise: it sets GlobalKeys.NoMap SERVER-SIDE by
//  default, built as a LIFTABLE gate so a future Mistlands advancement can lift it.
//
//  ── The mechanism (decomp-verified, assembly_valheim.decompiled.cs) ───────────
//  Game.UpdateNoMap (:85133) sets m_noMap = ZoneSystem.GetGlobalKey(NoMap) || the
//  per-player client pref, then forces Minimap.SetMapMode(None) when true. So
//  setting the key server-side forces the global map UI off for everyone.
//
//   ⭐1 SetGlobalKey is a ROUTED RPC (:98480 → InvokeRoutedRPC), not an inline
//       write — the server handler RPC_SetGlobalKey (:98539) does GlobalKeyAdd +
//       SendGlobalKeys(Everybody), and is idempotent (its own !Contains guard).
//       So the call must run SERVER-SIDE (handler is registered server-only,
//       ZoneSystem.Start :96434) and must be idempotent on every world-load.
//   ⭐2 The RPC handler is registered ONLY under ZNet.IsServer() — enforcement is
//       inherently a server action (exactly like the `nomap` console command,
//       :37350, sets it under its own IsServer gate).
//   ⭐3 NoMap PERSISTS automatically: enum idx 26 < NonServerOption (32) → it rides
//       m_startingGlobalKeys → the world .fwl meta (World.SaveWorldMetaData
//       :95780). Vanilla restores it on the next boot with NO action from us. We
//       enforce an INVARIANT, we don't persist STATE → AT-NOMAP-3 holds by
//       construction.
//   ⭐4 A freshly-joined client inherits it with ZERO client-side mod action:
//       ZoneSystem.OnNewPeer (:96593) → SendGlobalKeys(peer) → the client's
//       RPC_GlobalKeys (:96462) rebuilds + runs UpdateNoMap → SetMapMode(None).
//       So this feature is PURELY server-side → AT-NOMAP-2 holds by construction.
//   ⭐6 The WorldSetup WIPE hazard (the hook-timing trap): ZNet.LoadWorld ends by
//       calling WorldSetup() → ZoneSystem.SetStartingGlobalKeys() (:98441), which
//       CLEARS all 32 world-modifier keys and re-adds only the persisted set. So
//       enforcement MUST run AFTER that rebuild — a LoadWorld POSTFIX is. Do NOT
//       hook ZoneSystem.Start (runs before the world DB / starting-keys load and
//       races the wipe). We postfix the SAME proven seam as LegacyTerrainOpZdoCleanup.
//
//  ── Clean-room (ADR-0001) ─────────────────────────────────────────────────────
//  Setting a vanilla GlobalKeys member via ZoneSystem's own public API is
//  base-game — reading/adapting the game we mod is allowed; no third-party mod code.
//
//  Server-only by construction (LoadWorld only runs server-side, :66811-66823) AND
//  explicitly gated on ServerContext.OnSBServer — the same belt-and-braces as every
//  other SBPR registration. Registered in Plugin.cs; PatchCheck asserts it wove.
//  logs-green ≠ playable — Daniel verifies AT-NOMAP-* in-game (M opens nothing).
// ============================================================================

using HarmonyLib;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// Server-side enforcement of <c>GlobalKeys.NoMap</c> at world load — the
    /// cartography tier's enforced precondition. Postfix on <c>ZNet.LoadWorld</c>
    /// (the same proven server-only, once-per-boot, post-<c>WorldSetup</c> seam
    /// <see cref="SBPR.Trailborne.Features.Trailblazing.LegacyTerrainOpZdoCleanup"/>
    /// uses). On load, if the liftable gate (<see cref="ShouldEnforceNoMap"/>) says
    /// to enforce and the key isn't already present, set it — idempotent, fail-loud,
    /// non-fatal. See file header + impl-spec §3.5 for the decomp-verified rationale.
    /// </summary>
    [HarmonyPatch(typeof(ZNet), "LoadWorld")]
    public static class NoMapEnforcer
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]   // after vanilla body + WorldSetup (⭐6); order-independent vs the legacy-ZDO sweep
        public static void Postfix()
        {
            try
            {
                // Server gate (belt-and-braces; LoadWorld is server-only anyway, ⭐2).
                if (!ServerContext.OnSBServer) return;

                var zs = ZoneSystem.instance;
                if (zs == null)
                {
                    // Defensive: LoadWorld implies ZoneSystem exists, but never assume.
                    Plugin.Log.LogWarning(
                        "[Trailborne/NoMap] LoadWorld postfix: ZoneSystem.instance is null; " +
                        "NoMap enforcement skipped this boot. If a global map appears, this is why.");
                    return;
                }

                // The LIFTABLE gate (§3.5.2). Folds in the SBPR_EnforceNoMap escape hatch
                // (§3.5.3) AND the future Mistlands lift. The boot-log fires EITHER WAY
                // (AT-NOMAP-BOOTLOG): the lesson of this bug is that a silent, unenforced
                // premise shipped false for the whole tier — never silent again.
                if (!ShouldEnforceNoMap(zs))
                {
                    if (!Plugin.EnforceNoMap.Value)
                        Plugin.Log.LogWarning(
                            "[Trailborne/NoMap] SBPR_EnforceNoMap=false — the mod is DELIBERATELY NOT holding " +
                            "GlobalKeys.NoMap. The vanilla global map may be available (debug / non-cartography " +
                            "server). This is a config choice, not a bug.");
                    else
                        Plugin.Log.LogInfo(
                            "[Trailborne/NoMap] Enforcement lifted (ShouldEnforceNoMap=false) — not re-asserting " +
                            "NoMap this boot (a future Mistlands advancement owns the lift).");
                    return;
                }

                // Idempotent (⭐1): the GetGlobalKey check + the server handler's own
                // !Contains guard mean a repeat boot, a second LoadWorld, or a hand-set
                // `nomap` all collapse to a no-op with no re-broadcast.
                if (zs.GetGlobalKey(GlobalKeys.NoMap))
                {
                    Plugin.Log.LogInfo(
                        "[Trailborne/NoMap] GlobalKeys.NoMap already set; mod holds the global-map disable " +
                        "(the cartography tier is the only map).");
                    return;
                }

                // Routed RPC → server handler → GlobalKeyAdd + SendGlobalKeys(Everybody)
                // (⭐1/⭐4). Persists via m_startingGlobalKeys → world .fwl (⭐3).
                zs.SetGlobalKey(GlobalKeys.NoMap);
                Plugin.Log.LogWarning(
                    "[Trailborne/NoMap] Global map DISABLED by default (set GlobalKeys.NoMap server-side). " +
                    "Liftable at the Mistlands tier advancement (future). The cartography tier is now the only " +
                    "map. (card t_2f9fc470)");
            }
            catch (System.Exception e)
            {
                // Fail LOUD but NON-FATAL (the honesty rule): a thrown enforcer must NEVER
                // take down world load. Worst case the global map is available and this
                // ERROR line says exactly why.
                Plugin.Log.LogError($"[Trailborne/NoMap] enforce-on-load threw (non-fatal): {e}");
            }
        }

        /// <summary>
        /// The LIFTABLE gate (§3.5.2). Returns <c>true</c> while the global map should
        /// stay disabled, <c>false</c> once it should be re-granted. TODAY it checks
        /// only the <c>SBPR_EnforceNoMap</c> escape hatch and otherwise always enforces
        /// — this card ships the SEAM, not the Mistlands trigger. The future Mistlands
        /// card flips this to read its own persisted advancement flag and stops the
        /// per-boot re-assert.
        /// </summary>
        /// <remarks>
        /// ⭐5 (spec §3.5.0): the latch must be a real vanilla <c>GlobalKeys</c> enum
        /// member or NoMap's own presence/absence — a custom-named global key resolves
        /// to <c>NonServerOption</c> and neither persists nor is enum-queryable, so it
        /// is the WRONG durable latch. Do NOT mint one here. The <paramref name="zs"/>
        /// parameter is the seam the future trigger reads (e.g. a persisted progression
        /// key); it is intentionally unused today.
        /// </remarks>
        internal static bool ShouldEnforceNoMap(ZoneSystem zs)
        {
            // Escape hatch first (§3.5.3): a server operator / debug session can opt out
            // without a recompile. Default ON (enforced) per Daniel's directive.
            if (!Plugin.EnforceNoMap.Value) return false;

            // FUTURE (Mistlands tier card): return false once the world has advanced past
            // the point the global map is re-granted, reading a SERVER-SIDE, PERSISTED
            // signal off `zs` (a real vanilla GlobalKeys member, or a small SBPR world-data
            // flag the Mistlands card owns) — e.g.:
            //     if (zs.GetGlobalKey(<the persisted Mistlands-reached key>)) return false;
            // The lift itself is then a single server-side RemoveGlobalKey(GlobalKeys.NoMap)
            // at the advancement, and this guard stops re-asserting NoMap on the next boot.
            //
            // TODAY: always enforce — the safe default (the map stays disabled until the
            // future card lands). This card only ships the seam + the always-enforce default
            // so lifting later is a one-method flip, not a code rip-out.
            return true;
        }
    }
}
