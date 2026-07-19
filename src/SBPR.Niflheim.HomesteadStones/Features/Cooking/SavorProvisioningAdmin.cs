using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Cooking
{
    /// <summary>
    /// T016 remediation — the net48-ONLY playtest establishment seam for an ACTIVE Savor Local context.
    ///
    /// Why it exists: the live server composes only the Foundational AP slice, not the full Stone-progression
    /// command runtime (Facet commit / BP development / policy), so nothing in a live session yet DEVELOPS a
    /// Savor Local node. Without an established active context the food-timer observer correctly derives
    /// factor 1.0 forever — which is precisely why the joined-client proof FAILed. Rather than redesign the
    /// whole progression runtime for one Cooking node (out of this remediation's scope), this bounded seam
    /// establishes the developed-Savor Stone context at the sender's current Stone Area, exactly mirroring
    /// the T009R3/R4 <c>RelationshipProvisioningAdmin</c> pattern (config-flag + admin-gated, transport-bound,
    /// server-derived Stone identity, never client-authored). The established context is the SAME
    /// developed-Savor shape the T014/T016 tests derive, so the live in-world factor matches the unit proof.
    ///
    /// Contract preserved: the seam establishes only the DEVELOPED Stone context + governance fact; the
    /// ACTIVE/DORMANT status is still DERIVED per food tick from the current occupancy/policy (never a stored
    /// active flag). Clearing the context (or stepping outside the Area) flips the factor to 1 on the next
    /// tick with zero writes — no second active-effects ledger.
    ///
    /// Restriction (disabled by default; playtest-only):
    ///   * the server-owned BepInEx flag <c>Cooking.EnableSavorPlaytestSeam</c> (default FALSE) must be ON, AND
    ///   * the transport-authenticated sender must be a normalized server ADMIN.
    /// Neither is client-settable. Outside the playtest path the handler is never registered (flag off) or
    /// rejects (non-admin sender).
    ///
    /// Enable / invoke / disable / verify (QA operator steps):
    ///   1. ENABLE: set <c>[Cooking] EnableSavorPlaytestSeam = true</c> in the server's BepInEx config and
    ///      restart. On boot the log prints "Savor playtest seam ENABLED".
    ///   2. INVOKE: join as a server ADMIN, stand inside a Homestead Stone Area, and run the client console
    ///      command <c>sbpr_savor on</c>. The server establishes an active Savor context at that Stone; your
    ///      active food timers now drain at ~half rate. Run <c>sbpr_savor off</c> to clear it.
    ///   3. VERIFY: watch a food status timer inside the Area (0.5) vs. stepping outside / after `off` (1.0).
    ///   4. DISABLE: set the flag back to false and restart; the handler is no longer registered.
    ///
    /// References Valheim (ZNet, ZNetPeer, ZRpc, ZDOMan, ZDO, Terminal) → net48-only, not link-compiled.
    /// </summary>
    [HarmonyPatch]
    internal static class SavorProvisioningAdmin
    {
        // Direct per-peer ZRpc method name (hashed by ZRpc.Register — LOCK; a rename desyncs a mixed session).
        internal const string RpcSavor = "SBPR_Niflheim_ProvisionSavor";

        // Client console command name.
        internal const string ConsoleCommand = "sbpr_savor";

        // Payload discriminator: 1 = establish active context, 0 = clear it.
        private const int CmdOn = 1;
        private const int CmdOff = 0;

        // Server-owned config gate (default OFF). Bound by Plugin.Awake.
        internal static ConfigEntry<bool>? EnableSeam;

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        [HarmonyPostfix]
        private static void OnNewConnection(ZNetPeer peer)
        {
            try
            {
                if (peer == null || peer.m_rpc == null) return;
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                bool enabled = EnableSeam != null && EnableSeam.Value;
                if (!enabled) return;
                peer.m_rpc.Register<int>(RpcSavor, RPC_Savor);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Savor seam handler registration failed (ignored): " + ex.Message);
            }
        }

        private static void RPC_Savor(ZRpc rpc, int commandType)
        {
            try
            {
                var server = Features.Progression.FoundationalPlacementObserver.Server;
                if (server == null) return;
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;
                if (EnableSeam == null || !EnableSeam.Value) return;
                if (commandType != CmdOn && commandType != CmdOff) return;

                var peer = Features.Progression.ZdoAuthenticatedSenderSource.PeerForRpc(znet, rpc);
                if (peer == null) return;

                if (!SenderIsAdmin(znet, peer))
                {
                    Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Rejected Savor seam request from non-admin sender.");
                    return;
                }

                if (!TryResolveSenderStone(peer, server, out var stoneId))
                {
                    Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Savor seam: sender not inside a Homestead Stone Area.");
                    return;
                }

                if (commandType == CmdOff)
                {
                    server.SavorContexts.Clear(stoneId);
                    Plugin.Log.LogInfo("[Niflheim/HomesteadStones] [savor-seam] cleared active Savor context stone=" + stoneId.Value);
                    return;
                }

                // Establish the developed-Savor context at this Stone (default Everyone policy, authorized
                // Governor present) — the exact shape the T014/T016 derivation makes active for an in-area
                // occupant. The ACTIVE status is still derived per tick from current occupancy/policy.
                var stone = SavorContextFactory.DevelopedSavorStone(stoneId, SettlementLocalPolicy.Default);
                server.SavorContexts.Set(stoneId, new SavorLocalContext(stone, authorizedGovernorPresent: true));
                Plugin.Log.LogInfo("[Niflheim/HomesteadStones] [savor-seam] established active Savor context stone=" + stoneId.Value);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Savor seam threw: " + ex);
            }
        }

        private static bool SenderIsAdmin(ZNet znet, ZNetPeer peer)
        {
            try
            {
                var socket = peer.m_socket;
                string? host = socket != null ? socket.GetHostName() : null;
                if (string.IsNullOrEmpty(host)) return false;
                var adminList = znet.GetAdminList();
                if (adminList == null) return false;
                return VanillaAdminIdentity.ListContainsId(new List<string>(adminList), host!, VanillaAdminIdentity.DefaultPlatform);
            }
            catch { return false; }
        }

        private static bool TryResolveSenderStone(ZNetPeer peer, FoundationalProgressionServer server, out StoneId stoneId)
        {
            stoneId = default;
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return false;
            if (peer.m_characterID.IsNone()) return false;
            var zdo = zdoMan.GetZDO(peer.m_characterID);
            if (zdo == null || !zdo.IsValid()) return false;
            Vector3 pos = zdo.GetPosition();
            return server.StoneAreas.TryResolve(pos.x, pos.z, out stoneId);
        }
    }

    /// <summary>
    /// T016 remediation — the CLIENT console command that invokes the Savor playtest seam. A joined admin
    /// runs <c>sbpr_savor on|off</c>; it sends the discriminator on the SERVER connection as a direct notice,
    /// landing on the server's transport-bound handler. Registered once when Terminal's command table
    /// initializes. Inert unless the server has the seam enabled and the caller is an admin.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class SavorProvisioningConsole
    {
        private static bool registered;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (registered) return;
            registered = true;
            try
            {
                _ = new Terminal.ConsoleCommand(SavorProvisioningAdmin.ConsoleCommand,
                    "SBPR: (playtest, admin-only) establish/clear an active Savor the Hearth context at your Stone — usage: sbpr_savor on|off",
                    args =>
                    {
                        int cmd = 1;
                        if (args.Length >= 2 && string.Equals(args[1], "off", StringComparison.OrdinalIgnoreCase))
                            cmd = 0;

                        var serverRpc = ZNet.instance != null ? ZNet.instance.GetServerRPC() : null;
                        if (serverRpc == null)
                        {
                            args.Context?.AddString("sbpr_savor: not connected to a server.");
                            return;
                        }
                        serverRpc.Invoke(SavorProvisioningAdmin.RpcSavor, cmd);
                        args.Context?.AddString("sbpr_savor: request sent (server admin + seam-enabled required to take effect).");
                    });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Savor console command registration failed (ignored): " + ex.Message);
            }
        }
    }
}
