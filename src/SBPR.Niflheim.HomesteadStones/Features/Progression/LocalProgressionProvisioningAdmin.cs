using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T021 remediation 2 — the net48-ONLY isolated-QA seam that DEVELOPS a Stone-cultivated Local node
    /// (e.g. Refined Workshop) through the accepted, receipt-backed command handlers so the Local Effect
    /// can actually reach Active at runtime before a joined-client proof. It is the exact sibling of
    /// <see cref="RelationshipProvisioningAdmin"/> (which establishes the Governor Bond) — same transport-
    /// bound identity model, same admin gate, same server-owned config flag — but drives node development
    /// instead of relationship creation.
    ///
    /// The T021 joined-client rerun (PR #371 FAIL) proved the develop/purchase handlers had ZERO runtime
    /// callers, so a Local node was permanently Undeveloped and the +1 was inert end-to-end. This seam is
    /// the missing ingress: a real net48 caller reaching the accepted Facet-commit / node-development
    /// handlers with a server-owned identity, revisions, idempotency, and durable journals — never a
    /// provisional grant, never a direct node-state write.
    ///
    /// Restriction (disabled by default; isolated-QA only):
    ///   * the server-owned BepInEx flag <c>Progression.EnableAdminLocalNodeProvisioning</c> (default
    ///     FALSE) must be ON, AND
    ///   * the transport-authenticated sender must be a normalized server ADMIN.
    /// Neither is client-settable. Outside that gate the handler is never registered (flag off) or rejects
    /// (non-admin sender), so production behavior fails closed.
    ///
    /// Enable / invoke / disable / verify (isolated-QA operator steps):
    ///   1. ENABLE: set <c>[Progression] EnableAdminLocalNodeProvisioning = true</c> in the server's
    ///      BepInEx config and restart the dedicated server. On boot the request handler registers per-peer.
    ///   2. INVOKE: join as a server ADMIN, first establish the Governor Bond via
    ///      <c>sbpr_provision bond</c>, then stand inside the Homestead Stone Area and run
    ///      <c>sbpr_develop refined</c> (develops the Refined Workshop Local node).
    ///   3. VERIFY: the server log prints "[local-provisioning] outcome=Developed ..."; the durable
    ///      facet-commit + node-development journals now exist and the Refined Workshop Local Effect can
    ///      derive Active for an eligible occupant.
    ///   4. DISABLE: set the flag back to false and restart; the handler is no longer registered.
    ///
    /// References Valheim (ZNet, ZNetPeer, ZRpc, ZDOMan, ZDO, Terminal) → net48-only, not link-compiled.
    /// </summary>
    [HarmonyPatch]
    internal static class LocalProgressionProvisioningAdmin
    {
        // Direct per-peer ZRpc method name (hashed by ZRpc.Register — LOCK; a rename desyncs a session).
        internal const string RpcDevelop = "SBPR_Niflheim_ProvisionLocalNode";

        // Client console command name.
        internal const string ConsoleCommand = "sbpr_develop";

        // Payload discriminator: which authored Local node the admin develops. Kept as a small explicit
        // enum so the client payload is only a bounded selector, never a node id the client authors.
        private const int NodeRefinedWorkshop = 1;

        // The single Refined Workshop Local node identity (Crafting Tree, Level 1). Matches the catalog.
        private static readonly VersionedId RefinedWorkshopNode = new VersionedId("RefinedWorkshop", 1);

        // Server-owned config gate (default OFF). Bound by Plugin.Awake.
        internal static ConfigEntry<bool>? EnableProvisioning;

        // ── SERVER SIDE: register a DIRECT per-peer handler only when enabled ─────────────────────────

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        [HarmonyPostfix]
        private static void OnNewConnection(ZNetPeer peer)
        {
            try
            {
                if (peer == null || peer.m_rpc == null) return;
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                bool enabled = EnableProvisioning != null && EnableProvisioning.Value;
                if (!enabled) return;
                peer.m_rpc.Register<int>(RpcDevelop, RPC_Develop);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Local-node provisioning handler registration failed (ignored): " + ex.Message);
            }
        }

        private static void RPC_Develop(ZRpc rpc, int nodeSelector)
        {
            try
            {
                var localServer = LocalProgressionObserver.Server;
                var foundational = FoundationalPlacementObserver.Server;
                if (localServer == null || foundational == null) return;
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;
                if (EnableProvisioning == null || !EnableProvisioning.Value) return;   // re-checked at call time
                if (!TryResolveNode(nodeSelector, out var localNode)) return;

                // Transport-authenticated peer (never the forgeable routed sender) — same as the relationship
                // seam and the activation transport.
                var peer = ZdoAuthenticatedSenderSource.PeerForRpc(znet, rpc);
                if (peer == null) return;

                // Admin gate with vanilla-normalized semantics.
                if (!SenderIsAdmin(znet, peer))
                {
                    Plugin.Log.LogWarning(
                        "[Niflheim/HomesteadStones] Rejected local-node provisioning from non-admin sender.");
                    return;
                }

                // Server-derive the acting subject in the SAME bound-internal principal space the Governor
                // Bond was established under (RelationshipProvisioningAdmin). An UNBOUND peer fails closed —
                // we never fabricate or provider-derive a gameplay principal.
                if (!ZdoAuthenticatedSenderSource.Instance.TryResolveFromPeer(peer, out var senderFacts))
                    return;
                string peerKey = ServerCreatorIdentity.CharacterSubject(senderFacts.PlayerId);
                if (string.IsNullOrEmpty(peerKey) ||
                    !foundational.BoundSessions.TryResolve(peerKey, out var boundPrincipal) ||
                    string.IsNullOrEmpty(boundPrincipal.Account.Value) ||
                    string.IsNullOrEmpty(boundPrincipal.Character.Value))
                {
                    Plugin.Log.LogWarning(
                        "[Niflheim/HomesteadStones] Local-node provisioning: sender has no bound internal session (fail closed).");
                    return;
                }

                string account = boundPrincipal.Account.Value;
                string character = boundPrincipal.Character.Value;

                if (!TryResolveSenderStone(peer, foundational, out var stoneId))
                {
                    Plugin.Log.LogWarning(
                        "[Niflheim/HomesteadStones] Local-node provisioning: sender not inside a Homestead Stone Area.");
                    return;
                }

                var subject = new AuthoritativeSubject(new AccountId(account), new CharacterId(character));

                // Deterministic op prefix binds the acting bound-internal subject, Stone, node, and world
                // scope so an exact re-run replays idempotently through the accepted handlers and any changed
                // binding produces a distinct prefix.
                string worldScope = SafeWorldScope(znet);
                string opPrefix = "qa-local-" + ProvisioningOperationBinding.CorrelationTag(account, character)
                    + "-" + stoneId.Value + "-" + localNode.Key;

                var ingress = localServer.CreateLocalProvisioningIngress();
                var result = ingress.DevelopLocalNode(subject, stoneId, localNode, opPrefix);

                Plugin.Log.LogInfo("[Niflheim/HomesteadStones] " + result.ToOperatorLine()
                    + " subject=" + ProvisioningOperationBinding.CorrelationTag(account, character)
                    + " stone=" + stoneId.Value + " node=" + localNode.Key);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Local-node provisioning threw: " + ex);
            }
        }

        private static bool TryResolveNode(int selector, out VersionedId node)
        {
            switch (selector)
            {
                case NodeRefinedWorkshop: node = RefinedWorkshopNode; return true;
                default: node = VersionedId.None; return false;
            }
        }

        /// <summary>Admin gate: the sender's authenticated socket host id must match the server admin list
        /// under vanilla-normalized semantics, the same rule vanilla's RPC_Save uses.</summary>
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

        /// <summary>Resolve the Stone whose Area the sender character currently occupies, from the server's
        /// own character ZDO position — never a client claim.</summary>
        private static bool TryResolveSenderStone(ZNetPeer peer, Application.Runtime.FoundationalProgressionServer server, out StoneId stoneId)
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
    /// T021 remediation 2 — the CLIENT console command that invokes Local-node development. A joined admin
    /// runs <c>sbpr_develop refined</c>; it sends the node selector on the SERVER connection as a direct
    /// notice, landing on the server's transport-bound handler. Registered once when Terminal's command
    /// table initializes. Inert unless the server has the seam enabled and the caller is an admin.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class LocalProgressionProvisioningConsole
    {
        private static bool registered;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (registered) return;
            registered = true;
            try
            {
                _ = new Terminal.ConsoleCommand(LocalProgressionProvisioningAdmin.ConsoleCommand,
                    "SBPR: (isolated-QA, admin-only) develop a Homestead Local node — usage: sbpr_develop refined",
                    args =>
                    {
                        // Selector 1 = Refined Workshop (the only wired Local node so far).
                        int selector = 1;

                        var serverRpc = ZNet.instance != null ? ZNet.instance.GetServerRPC() : null;
                        if (serverRpc == null)
                        {
                            args.Context?.AddString("sbpr_develop: not connected to a server.");
                            return;
                        }
                        serverRpc.Invoke(LocalProgressionProvisioningAdmin.RpcDevelop, selector);
                        args.Context?.AddString("sbpr_develop: request sent (server admin + seam-enabled required to take effect).");
                    });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Local-node provisioning console command registration failed (ignored): " + ex.Message);
            }
        }
    }
}
