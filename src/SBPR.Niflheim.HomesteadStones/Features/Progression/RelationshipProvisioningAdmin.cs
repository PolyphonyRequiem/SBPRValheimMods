using System;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T009R3 (Blocker 3) — the net48-ONLY admin/test provisioning seam. It is the smallest bounded,
    /// server-authoritative ingress that lets a real session ESTABLISH the Bond/Attunement that
    /// RecordFoundationalPlacement requires (without it, T009L cannot reach a single credited placement).
    ///
    /// It drives the engine-free <see cref="RelationshipProvisioningIngress"/> (which calls the SHIPPED
    /// <see cref="RelationshipCommandHandler"/>) with a SERVER-DERIVED principal:
    ///   * the subject account is the sender character's server-owned <c>s_playerID</c> (the same identity
    ///     space the placement runtime uses as the acting platform id), and the subject character is the
    ///     sender's STABLE character ZDOID — both read off the server's own character ZDO, never a payload;
    ///   * the target Stone is re-derived server-side from the sender character's world position via the
    ///     server-owned <see cref="FoundationalProgressionServer.StoneAreas"/> — the client cannot claim it.
    /// The command type (Attunement / Bond) is the ONLY payload byte; every credit-bearing fact is
    /// server-derived, and the shipped handler still enforces every invariant.
    ///
    /// Restriction (disabled by default; playtest-only):
    ///   * a server-owned BepInEx config flag <c>Progression.EnableAdminRelationshipProvisioning</c>
    ///     (default FALSE) must be ON, AND
    ///   * the routed sender must be a Valheim ADMIN (its peer host id is on the server admin list, the
    ///     same gate vanilla uses for <c>RPC_Save</c>).
    /// Neither the flag nor admin membership is client-settable. Outside the playtest path the handler is
    /// never registered (flag off) or rejects (non-admin sender), so this is not a shipping gameplay command.
    ///
    /// References Valheim (ZNet, ZRoutedRpc, ZDOMan, ZDO) → net48-only, not link-compiled into net8.
    /// </summary>
    [HarmonyPatch]
    internal static class RelationshipProvisioningAdmin
    {
        // Routed-RPC method name (hashed by ZRoutedRpc.Register — LOCK; a rename desyncs a mixed session).
        internal const string RpcProvision = "SBPR_Niflheim_ProvisionRelationship";

        // Payload discriminator: 2 = CreateAttunement, 1 = CreateBond (matches RelationshipCommandType).
        private const int CmdAttunement = (int)RelationshipCommandType.CreateAttunement;
        private const int CmdBond = (int)RelationshipCommandType.CreateBond;

        // Server-owned config gate (default OFF). Bound by Plugin.Awake.
        internal static ConfigEntry<bool>? EnableProvisioning;

        private static ZRoutedRpc? registeredRpc;

        /// <summary>Register the provisioning RPC on the server ONLY when the config flag is enabled. A
        /// disabled flag means the handler is never registered, so no provisioning is possible at all.</summary>
        internal static void Register()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) return;
            if (ReferenceEquals(registeredRpc, rpc)) return;

            bool isServer = ZNet.instance != null && ZNet.instance.IsServer();
            bool enabled = EnableProvisioning != null && EnableProvisioning.Value;
            if (isServer && enabled)
            {
                rpc.Register<int>(RpcProvision, RPC_Provision);
                Plugin.Log.LogInfo(
                    "[Niflheim/HomesteadStones] Admin relationship provisioning ENABLED (playtest seam) — handler registered.");
            }
            else if (isServer)
            {
                Plugin.Log.LogInfo(
                    "[Niflheim/HomesteadStones] Admin relationship provisioning disabled (config flag off) — handler NOT registered.");
            }
            registeredRpc = rpc;
        }

        private static void RPC_Provision(long sender, int commandType)
        {
            try
            {
                var server = FoundationalPlacementObserver.Server;
                if (server == null) return;
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;
                if (EnableProvisioning == null || !EnableProvisioning.Value) return;   // flag re-checked at call time

                if (commandType != CmdAttunement && commandType != CmdBond) return;

                var peer = znet.GetPeer(sender);
                if (peer == null) return;

                // Admin gate: the sender's peer host id must be on the server admin list (same gate vanilla
                // uses for RPC_Save). A non-admin sender is refused — provisioning is never client-open.
                if (!SenderIsAdmin(znet, peer))
                {
                    Plugin.Log.LogWarning(
                        "[Niflheim/HomesteadStones] Rejected relationship provisioning from non-admin sender " + sender + ".");
                    return;
                }

                // Server-derive the subject identity + target Stone entirely from server-owned facts.
                if (!ZdoAuthenticatedSenderSource.Instance.TryResolveSender(sender, out var senderCharacter))
                    return;
                if (!AuthenticatedSenderBinder.TryBind(senderCharacter, out string principal, out string characterId))
                    return;

                if (!TryResolveSenderStone(znet, server, peer, out var stoneId))
                {
                    Plugin.Log.LogWarning(
                        "[Niflheim/HomesteadStones] Relationship provisioning: sender not inside a Homestead Stone Area.");
                    return;
                }

                // Account principal = the SAME identity space the dedicated placement path binds (the
                // creator principal "player:<s_playerID>"), so a provisioned relationship authorizes the
                // subsequent placement credit for this player. Claim is empty; the shipped handler binds
                // from the connection alone.
                var subject = new AuthoritativeSubject(
                    new AccountId(principal),
                    new CharacterId(characterId));

                var ingress = server.CreateRelationshipProvisioningIngress();

                // Deterministic per-(subject, stone, command) operation id so a resend converges (Replayed)
                // rather than double-applying.
                string opId = "op-provision-" + ((int)commandType).ToString(CultureInfo.InvariantCulture)
                    + "-" + subject.Account.Value + "-" + stoneId.Value;
                string relId = "rel-provision-" + ((int)commandType).ToString(CultureInfo.InvariantCulture)
                    + "-" + subject.Character.Value;

                string worldScope = SafeWorldScope(znet);
                string requestedRange = commandType == CmdBond ? "Homestead:All" : string.Empty;

                var result = ingress.Provision(subject, stoneId, (RelationshipCommandType)commandType,
                    opId, relId, worldScope, requestedRange);

                Plugin.Log.LogInfo("[Niflheim/HomesteadStones] " + result.ToOperatorLine()
                    + " principal=" + principal + " stone=" + stoneId.Value);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Relationship provisioning threw: " + ex);
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
                return adminList != null && adminList.Contains(host);
            }
            catch { return false; }
        }

        /// <summary>Resolve the Stone whose Area the sender character currently occupies, from the server's
        /// own character ZDO position — never a client claim.</summary>
        private static bool TryResolveSenderStone(ZNet znet, FoundationalProgressionServer server, ZNetPeer peer, out Domain.Identity.StoneId stoneId)
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

        private static string SafeWorldScope(ZNet znet)
        {
            try { return (znet.GetWorldName() ?? "world") + "/" + znet.GetWorldUID().ToString(CultureInfo.InvariantCulture); }
            catch { return "world"; }
        }
    }

    /// <summary>
    /// T009R3 — registers the provisioning RPC once ZRoutedRpc is up (null at ZNet.Awake, rebuilt each
    /// session), same seam as the dedicated ingress bootstrap. Idempotent per ZRoutedRpc instance.
    /// </summary>
    [HarmonyPatch(typeof(Game), "Start")]
    internal static class RelationshipProvisioningAdminBootstrap
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            try { RelationshipProvisioningAdmin.Register(); }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Relationship provisioning RPC registration failed: " + ex);
            }
        }
    }
}
