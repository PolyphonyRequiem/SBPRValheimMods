using System;
using System.Collections.Generic;
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
    /// T009R4 (Blockers 2 + 4) — the net48-ONLY admin provisioning seam, rebuilt TRANSPORT-BOUND and
    /// correctly admin-gated. It is the smallest bounded, server-authoritative invocation that lets a real
    /// session ESTABLISH the Bond/Attunement RecordFoundationalPlacement requires, so T009L can reach a
    /// credited placement.
    ///
    /// What T009R3 got wrong (adversarial review):
    ///   * Blocker 2 — it was a ZRoutedRpc routed handler keyed on the forgeable client-serialized
    ///     <c>long sender</c>. High-value provisioning authority must never ride the routed sender. This is
    ///     now a DIRECT per-peer <c>ZRpc</c> handler: the server receives the real delivering <c>ZRpc</c>
    ///     and resolves the exact authenticated <c>ZNetPeer</c>. Account, character, and target Stone are
    ///     all derived from THAT peer + server-owned ZDO state; the client payload is only the command
    ///     discriminator (Attunement / Bond).
    ///   * Blocker 4 — the admin gate used raw <c>GetAdminList().Contains(host)</c>, which does not match
    ///     vanilla admin semantics. It now uses <see cref="VanillaAdminIdentity.ListContainsId"/>, a
    ///     clean-room reproduction of <c>ZNet.ListContainsId</c> (platform-qualified OR bare user id on the
    ///     server's platform) — the same normalization vanilla's RPC_Save gate uses.
    ///
    /// Restriction (disabled by default; playtest-only):
    ///   * the server-owned BepInEx flag <c>Progression.EnableAdminRelationshipProvisioning</c> (default
    ///     FALSE) must be ON, AND
    ///   * the transport-authenticated sender must be a normalized server ADMIN.
    /// Neither is client-settable. Outside the playtest path the handler is never registered (flag off) or
    /// rejects (non-admin sender).
    ///
    /// Enable / invoke / disable / verify (T009L operator steps):
    ///   1. ENABLE: set <c>[Progression] EnableAdminRelationshipProvisioning = true</c> in the server's
    ///      BepInEx config (BepInEx/config/net.danielgreen.sbpr.niflheim.homesteadstones.cfg) and restart
    ///      the dedicated server. On boot the log prints "Admin relationship provisioning ENABLED".
    ///   2. INVOKE: join as a server ADMIN (your platform id is on adminlist.txt), stand inside a Homestead
    ///      Stone Area, and run the client console command <c>sbpr_provision attune</c> (or
    ///      <c>sbpr_provision bond</c>). The command sends a direct notice on the server connection.
    ///   3. VERIFY: the server log prints "[relationship-provisioning] outcome=Applied ..."; a subsequent
    ///      eligible placement inside that Stone Area now credits Foundational AP.
    ///   4. DISABLE: set the flag back to false and restart; the handler is no longer registered.
    ///
    /// References Valheim (ZNet, ZNetPeer, ZRpc, ZDOMan, ZDO, Terminal) → net48-only, not link-compiled.
    /// </summary>
    [HarmonyPatch]
    internal static class RelationshipProvisioningAdmin
    {
        // Direct per-peer ZRpc method name (hashed by ZRpc.Register — LOCK; a rename desyncs a mixed session).
        internal const string RpcProvision = "SBPR_Niflheim_ProvisionRelationship";

        // Client console command name.
        internal const string ConsoleCommand = "sbpr_provision";

        // Payload discriminator: 2 = CreateAttunement, 1 = CreateBond (matches RelationshipCommandType).
        private const int CmdAttunement = (int)RelationshipCommandType.CreateAttunement;
        private const int CmdBond = (int)RelationshipCommandType.CreateBond;

        // Server-owned config gate (default OFF). Bound by Plugin.Awake.
        internal static ConfigEntry<bool>? EnableProvisioning;

        // ── SERVER SIDE: register a DIRECT per-peer handler only when enabled ─────────────────────────

        /// <summary>Register the transport-bound provisioning handler on each peer's OWN rpc as it
        /// connects — but ONLY on the server AND only when the config flag is enabled. A disabled flag means
        /// the handler is never registered, so provisioning is impossible.</summary>
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
                peer.m_rpc.Register<int>(RpcProvision, RPC_Provision);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Provisioning handler registration failed (ignored): " + ex.Message);
            }
        }

        private static void RPC_Provision(ZRpc rpc, int commandType)
        {
            try
            {
                var server = FoundationalPlacementObserver.Server;
                if (server == null) return;
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;
                if (EnableProvisioning == null || !EnableProvisioning.Value) return;   // re-checked at call time
                if (commandType != CmdAttunement && commandType != CmdBond) return;

                // Blocker 2: the AUTHENTICATED peer is the one whose m_rpc delivered this packet.
                var peer = ZdoAuthenticatedSenderSource.PeerForRpc(znet, rpc);
                if (peer == null) return;

                // Blocker 4: admin gate with vanilla-normalized semantics (NOT raw Contains).
                if (!SenderIsAdmin(znet, peer))
                {
                    Plugin.Log.LogWarning(
                        "[Niflheim/HomesteadStones] Rejected relationship provisioning from non-admin sender.");
                    return;
                }

                // Server-derive the subject identity ENTIRELY from server-owned state, and — critically —
                // in the SAME principal space placement authorizes under (T009L2 Blocker 1). Placement
                // (FoundationalPlacementObserver / DedicatedPlacementIngress) keys the bound-session index
                // by the durable player:<s_playerID> character subject and credits under the BOUND INTERNAL
                // (AccountId, CharacterId) admission published there. Provisioning MUST resolve the identical
                // bound internal principal, or the Attunement it creates lives under a different identity than
                // the placement that needs it (the live T009L2 FAIL: relationship under provider subject,
                // placement under bound internal → RelationshipRequired, zero AP).
                //
                // We read ONLY the peer's server-owned durable s_playerID off its character ZDO to form the
                // peer key; the raw provider/socket account subject is never carried into the gameplay
                // relationship, receipt, journal, or log. An UNBOUND peer (no admitted, activated internal
                // session) FAILS CLOSED — we never fabricate or provider-derive a gameplay principal.
                if (!ZdoAuthenticatedSenderSource.Instance.TryResolveFromPeer(peer, out var senderFacts))
                    return;
                string peerKey = ServerCreatorIdentity.CharacterSubject(senderFacts.PlayerId);
                if (string.IsNullOrEmpty(peerKey) ||
                    !server.BoundSessions.TryResolve(peerKey, out var boundPrincipal) ||
                    string.IsNullOrEmpty(boundPrincipal.Account.Value) ||
                    string.IsNullOrEmpty(boundPrincipal.Character.Value))
                {
                    Plugin.Log.LogWarning(
                        "[Niflheim/HomesteadStones] Relationship provisioning: sender has no bound internal session (fail closed).");
                    return;
                }

                string account = boundPrincipal.Account.Value;
                string character = boundPrincipal.Character.Value;

                if (!TryResolveSenderStone(peer, server, out var stoneId))
                {
                    Plugin.Log.LogWarning(
                        "[Niflheim/HomesteadStones] Relationship provisioning: sender not inside a Homestead Stone Area.");
                    return;
                }

                var subject = new AuthoritativeSubject(new AccountId(account), new CharacterId(character));
                var ingress = server.CreateRelationshipProvisioningIngress();

                // Blocker 3: bind the operation id + relationship id to ALL material fields (bound internal
                // account, bound internal character, Stone, command, range) so an exact retry replays and a
                // changed binding conflicts.
                string worldScope = SafeWorldScope(znet);
                string requestedRange = commandType == CmdBond ? "Homestead:All" : string.Empty;
                string opId = ProvisioningOperationBinding.OperationId(
                    account, character, stoneId, (RelationshipCommandType)commandType, requestedRange, worldScope);
                string relId = ProvisioningOperationBinding.RelationshipId(character, (RelationshipCommandType)commandType);

                var result = ingress.Provision(subject, stoneId, (RelationshipCommandType)commandType,
                    opId, relId, worldScope, requestedRange);

                // PII-free operator log: the outcome line + a pseudonymous correlation tag derived from the
                // bound internal character (never the raw provider/socket account or the s_playerID). The
                // bound internal AccountId/CharacterId are already server-minted opaque ids, but we still emit
                // only a short digest so the operator log carries a stable correlation handle without echoing
                // any subject that could enter an export.
                Plugin.Log.LogInfo("[Niflheim/HomesteadStones] " + result.ToOperatorLine()
                    + " subject=" + ProvisioningOperationBinding.CorrelationTag(account, character)
                    + " stone=" + stoneId.Value);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Relationship provisioning threw: " + ex);
            }
        }

        /// <summary>Admin gate: the sender's authenticated socket host id must match the server admin list
        /// under vanilla-normalized semantics (<see cref="VanillaAdminIdentity.ListContainsId"/>), the same
        /// rule vanilla's RPC_Save uses. A non-admin sender is refused.</summary>
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
        private static bool TryResolveSenderStone(ZNetPeer peer, FoundationalProgressionServer server, out Domain.Identity.StoneId stoneId)
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
    /// T009R4 (Blocker 4) — the CLIENT console command that invokes provisioning. A joined admin runs
    /// <c>sbpr_provision attune|bond</c>; it sends the command discriminator on the SERVER connection as a
    /// direct notice, landing on the server's transport-bound handler. Registered once when Terminal's
    /// command table initializes. The command is inert unless the server has the seam enabled and the
    /// caller is an admin — the client cannot self-authorize.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class RelationshipProvisioningConsole
    {
        private static bool registered;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (registered) return;
            registered = true;
            try
            {
                _ = new Terminal.ConsoleCommand(RelationshipProvisioningAdmin.ConsoleCommand,
                    "SBPR: (playtest, admin-only) provision a Homestead relationship — usage: sbpr_provision attune|bond",
                    args =>
                    {
                        int cmd = (int)RelationshipCommandType.CreateAttunement;
                        if (args.Length >= 2 && string.Equals(args[1], "bond", StringComparison.OrdinalIgnoreCase))
                            cmd = (int)RelationshipCommandType.CreateBond;

                        var serverRpc = ZNet.instance != null ? ZNet.instance.GetServerRPC() : null;
                        if (serverRpc == null)
                        {
                            args.Context?.AddString("sbpr_provision: not connected to a server.");
                            return;
                        }
                        serverRpc.Invoke(RelationshipProvisioningAdmin.RpcProvision, cmd);
                        args.Context?.AddString("sbpr_provision: request sent (server admin + seam-enabled required to take effect).");
                    });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Provisioning console command registration failed (ignored): " + ex.Message);
            }
        }
    }
}
