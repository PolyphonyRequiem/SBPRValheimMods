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
    /// T027 remediation — the net48-ONLY isolated-QA seam that provisions full PERSONAL-NODE OWNERSHIP
    /// (developed + purchased) for the acting admin through the accepted, receipt-backed handlers, so a
    /// personal Permanent/Character Effect can actually be OWNED at runtime before a joined-client OWNER
    /// in-world proof. It is the exact sibling of <see cref="LocalProgressionProvisioningAdmin"/> (which
    /// develops a Stone-cultivated Local node) and <see cref="RelationshipProvisioningAdmin"/> (which
    /// establishes a relationship) — same transport-bound identity model, same admin gate, same server-owned
    /// config flag — but reaches a personal <c>NodePurchaseRecord</c> instead.
    ///
    /// Why this exists: the T027 Fletcher's Habit joined-client verdict (docs PR #393, QA card t_275c5173)
    /// found the required OWNER in-world proof STRUCTURALLY UNREACHABLE at reviewed head 9b48670 — no runtime
    /// seam (gameplay or QA) issues a <c>NodePurchaseRecord</c>, so no character could OWN Fletcher's Habit on
    /// a joined client. The only runtime caller of the provisioning ingress drove <c>DevelopLocalNode</c>,
    /// never <c>PurchaseNode</c>. This seam is the missing ingress: a real net48 caller reaching the accepted
    /// develop→purchase handlers with a server-owned identity, revisions, idempotency, and durable journals —
    /// never a provisional grant, never a direct purchase-state write. The same seam unblocks the sibling
    /// T026 Field Fletching I owner proof (personal purchase, same gap).
    ///
    /// Restriction (disabled by default; isolated-QA only):
    ///   * the server-owned BepInEx flag <c>Progression.EnableAdminPersonalNodeProvisioning</c> (default
    ///     FALSE) must be ON, AND
    ///   * the transport-authenticated sender must be a normalized server ADMIN.
    /// Neither is client-settable. Outside that gate the handler is never registered (flag off) or rejects
    /// (non-admin sender), so production behavior fails closed.
    ///
    /// Enable / invoke / disable / verify (isolated-QA operator steps):
    ///   1. ENABLE: set <c>[Progression] EnableAdminPersonalNodeProvisioning = true</c> in the server's
    ///      BepInEx config and restart the dedicated server. On boot the request handler registers per-peer.
    ///   2. INVOKE: join as a server ADMIN, stand inside the Homestead Stone Area, and run
    ///      <c>sbpr_purchase fletcher</c> (Fletcher's Habit) or <c>sbpr_purchase fieldfletch</c> (Field
    ///      Fletching I). The seam drives Bond→develop→release→Attune→purchase through accepted commands.
    ///   3. VERIFY: the server log prints "[personal-provisioning] outcome=Purchased ..."; the acting
    ///      character now holds the durable purchase record and OWNS the node (developed+purchased).
    ///   4. DISABLE: set the flag back to false and restart; the handler is no longer registered.
    ///
    /// References Valheim (ZNet, ZNetPeer, ZRpc, ZDOMan, ZDO, Terminal) → net48-only, not link-compiled.
    /// </summary>
    [HarmonyPatch]
    internal static class PersonalNodeProvisioningAdmin
    {
        // Direct per-peer ZRpc method name (hashed by ZRpc.Register — LOCK; a rename desyncs a session).
        internal const string RpcPurchase = "SBPR_Niflheim_ProvisionPersonalNode";

        // Client console command name.
        internal const string ConsoleCommand = "sbpr_purchase";

        // Payload discriminators: which authored personal Offered node the admin comes to own. Kept as a
        // small explicit enum so the client payload is only a bounded selector, never a node id it authors.
        private const int NodeFletchersHabit = 1;   // Archer / Fletcher's Habit (T027, Permanent Effect)
        private const int NodeFieldFletchingI = 2;  // Archer / Field Fletching I (T026, Character Effect)

        private static readonly VersionedId ArcherTree = HomesteadProgressionCatalog.ArcherTree;
        private static readonly VersionedId FletchersHabitNode = new VersionedId("FletchersHabit", 1);
        private static readonly VersionedId FieldFletchingINode = new VersionedId("FieldFletchingI", 1);

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
                peer.m_rpc.Register<int>(RpcPurchase, RPC_Purchase);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Personal-node provisioning handler registration failed (ignored): " + ex.Message);
            }
        }

        private static void RPC_Purchase(ZRpc rpc, int nodeSelector)
        {
            try
            {
                var localServer = LocalProgressionObserver.Server;
                var foundational = FoundationalPlacementObserver.Server;
                if (localServer == null || foundational == null) return;
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;
                if (EnableProvisioning == null || !EnableProvisioning.Value) return;   // re-checked at call time
                if (!TryResolveNode(nodeSelector, out var personalNode)) return;

                // Transport-authenticated peer (never the forgeable routed sender) — same as the sibling
                // Local-node / relationship seams.
                var peer = ZdoAuthenticatedSenderSource.PeerForRpc(znet, rpc);
                if (peer == null) return;

                // Admin gate with vanilla-normalized semantics.
                if (!SenderIsAdmin(znet, peer))
                {
                    Plugin.Log.LogWarning(
                        "[Niflheim/HomesteadStones] Rejected personal-node provisioning from non-admin sender.");
                    return;
                }

                // Server-derive the acting subject in the SAME bound-internal principal space the placement /
                // Local-node seams authorize under. An UNBOUND peer fails closed — we never fabricate or
                // provider-derive a gameplay principal.
                if (!ZdoAuthenticatedSenderSource.Instance.TryResolveFromPeer(peer, out var senderFacts))
                    return;
                string peerKey = ServerCreatorIdentity.CharacterSubject(senderFacts.PlayerId);
                if (string.IsNullOrEmpty(peerKey) ||
                    !foundational.BoundSessions.TryResolve(peerKey, out var boundPrincipal) ||
                    string.IsNullOrEmpty(boundPrincipal.Account.Value) ||
                    string.IsNullOrEmpty(boundPrincipal.Character.Value))
                {
                    Plugin.Log.LogWarning(
                        "[Niflheim/HomesteadStones] Personal-node provisioning: sender has no bound internal session (fail closed).");
                    return;
                }

                string account = boundPrincipal.Account.Value;
                string character = boundPrincipal.Character.Value;

                if (!TryResolveSenderStone(peer, foundational, out var stoneId))
                {
                    Plugin.Log.LogWarning(
                        "[Niflheim/HomesteadStones] Personal-node provisioning: sender not inside a Homestead Stone Area.");
                    return;
                }

                var subject = new AuthoritativeSubject(new AccountId(account), new CharacterId(character));

                // Deterministic op prefix binds the acting bound-internal subject, Stone, node, and world
                // scope so an exact re-run replays idempotently through the accepted handlers and any changed
                // binding produces a distinct prefix.
                string worldScope = SafeWorldScope(znet);
                string opPrefix = "qa-personal-" + ProvisioningOperationBinding.CorrelationTag(account, character)
                    + "-" + stoneId.Value + "-" + personalNode.Key;

                var ingress = localServer.CreateLocalProvisioningIngress();
                var result = ingress.ProvisionPersonalNodeOwnership(
                    subject, stoneId, ArcherTree, personalNode, opPrefix, worldScope);

                Plugin.Log.LogInfo("[Niflheim/HomesteadStones] " + result.ToOperatorLine()
                    + " subject=" + ProvisioningOperationBinding.CorrelationTag(account, character)
                    + " stone=" + stoneId.Value + " node=" + personalNode.Key);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Personal-node provisioning threw: " + ex);
            }
        }

        private static bool TryResolveNode(int selector, out VersionedId node)
        {
            switch (selector)
            {
                case NodeFletchersHabit: node = FletchersHabitNode; return true;
                case NodeFieldFletchingI: node = FieldFletchingINode; return true;
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
    /// T027 remediation — the CLIENT console command that invokes personal-node ownership provisioning. A
    /// joined admin runs <c>sbpr_purchase fletcher|fieldfletch</c>; it sends the node selector on the SERVER
    /// connection as a direct notice, landing on the server's transport-bound handler. Registered once when
    /// Terminal's command table initializes. Inert unless the server has the seam enabled and the caller is
    /// an admin.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class PersonalNodeProvisioningConsole
    {
        private static bool registered;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (registered) return;
            registered = true;
            try
            {
                _ = new Terminal.ConsoleCommand(PersonalNodeProvisioningAdmin.ConsoleCommand,
                    "SBPR: (isolated-QA, admin-only) come to OWN a personal node — usage: sbpr_purchase fletcher|fieldfletch",
                    args =>
                    {
                        // Selector 1 = Fletcher's Habit (default), 2 = Field Fletching I.
                        int selector = 1;
                        if (args.Length >= 2 && string.Equals(args[1], "fieldfletch", StringComparison.OrdinalIgnoreCase))
                            selector = 2;

                        var serverRpc = ZNet.instance != null ? ZNet.instance.GetServerRPC() : null;
                        if (serverRpc == null)
                        {
                            args.Context?.AddString("sbpr_purchase: not connected to a server.");
                            return;
                        }
                        serverRpc.Invoke(PersonalNodeProvisioningAdmin.RpcPurchase, selector);
                        args.Context?.AddString("sbpr_purchase: request sent (server admin + seam-enabled required to take effect).");
                    });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Personal-node provisioning console command registration failed (ignored): " + ex.Message);
            }
        }
    }
}
