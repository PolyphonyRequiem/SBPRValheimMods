using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Cooking;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Cooking
{
    /// <summary>
    /// T016 remediation (rebased onto the merged shared Local Effect runtime, PR #368) — the net48-ONLY
    /// playtest establishment seam that reaches a DEVELOPED Savor Local node so QA can prove the joined-client
    /// in-area 0.5 / exit 1.0 factor.
    ///
    /// It carries NO parallel provisional activation state (the earlier family-local SavorLocalContextIndex /
    /// SavorContextFactory are deleted). Establishment goes entirely through the reviewed shared substrate's
    /// <see cref="LocalNodeProvisioningDriver"/>: on <c>sbpr_savor on</c> the seam seeds a bare Stone-Level-2
    /// aggregate for the sender's Stone (if the live authoritative store has none yet) and then drives the
    /// ACCEPTED, receipt-backed commands (commit Cooking Tree → credit BP → develop Savor) so the node reaches
    /// Developed as real Stone-owned state. The ACTIVE/DORMANT status is still DERIVED per food tick by the
    /// authoritative <see cref="LocalActivationService"/> from current occupancy/governance/policy — there is
    /// no stored active flag and no second ledger.
    ///
    /// The acting Governor's Bond must already exist. QA establishes it first with the shipped
    /// <c>sbpr_provision bond</c> seam (RelationshipProvisioningAdmin); this seam only does the
    /// Facet→BP→development sequence a bonded Governor is authorized for, and any handler rejection surfaces in
    /// the log verbatim (a QA run that "provisions" has provably crossed the real gates).
    ///
    /// <c>sbpr_savor off</c> switches the Settlement Local policy (via the same accepted owner-only handler) to
    /// Attuned, so a non-related in-area occupant becomes policy-ineligible and the factor returns to 1.0 in
    /// place — a second exit proof alongside simply stepping outside the Stone Area.
    ///
    /// Restriction (disabled by default; playtest-only):
    ///   * the server-owned BepInEx flag <c>Cooking.EnableSavorPlaytestSeam</c> (default FALSE) must be ON, AND
    ///   * the transport-authenticated sender must be a normalized server ADMIN.
    /// Neither is client-settable. Outside the playtest path the handler is never registered (flag off) or
    /// rejects (non-admin sender).
    ///
    /// Enable / invoke / disable / verify (QA operator steps):
    ///   1. ENABLE: set <c>[Cooking] EnableSavorPlaytestSeam = true</c> (and <c>[Progression]
    ///      EnableAdminRelationshipProvisioning = true</c>) in the server's BepInEx config and restart.
    ///   2. BOND: join as a server ADMIN, stand inside a Homestead Stone Area, run <c>sbpr_provision bond</c>.
    ///   3. INVOKE: run <c>sbpr_savor on</c>. The seam develops the Savor node through accepted commands; your
    ///      active food timers now drain at ~half rate while you stand inside the Area.
    ///   4. VERIFY: watch a food status timer inside the Area (0.5) vs. stepping outside (1.0), and/or run
    ///      <c>sbpr_savor off</c> to switch the policy to Attuned (an unrelated occupant returns to 1.0).
    ///   5. DISABLE: set the flag back to false and restart; the handler is no longer registered.
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

        // Payload discriminator: 1 = develop Savor + Everyone policy, 0 = switch policy to Attuned.
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
                var foundational = Features.Progression.FoundationalPlacementObserver.Server;
                var local = Features.Progression.LocalProgressionObserver.Server;
                if (foundational == null || local == null) return;
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

                // Resolve the sender's BOUND INTERNAL principal (the same identity space the bond + placement
                // authorize under) and its current Stone Area — both server-owned, never a client claim.
                if (!Features.Progression.ZdoAuthenticatedSenderSource.Instance.TryResolveFromPeer(peer, out var senderFacts))
                    return;
                string peerKey = Application.Runtime.ServerCreatorIdentity.CharacterSubject(senderFacts.PlayerId);
                if (string.IsNullOrEmpty(peerKey) ||
                    !foundational.BoundSessions.TryResolve(peerKey, out var principal) ||
                    string.IsNullOrEmpty(principal.Account.Value) ||
                    string.IsNullOrEmpty(principal.Character.Value))
                {
                    Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Savor seam: sender has no bound internal session (fail closed).");
                    return;
                }

                if (!TryResolveSenderStone(peer, foundational, out var stoneId))
                {
                    Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Savor seam: sender not inside a Homestead Stone Area.");
                    return;
                }

                var subject = new AuthoritativeSubject(
                    new AccountId(principal.Account.Value), new CharacterId(principal.Character.Value));
                var driver = new LocalNodeProvisioningDriver(local);

                if (commandType == CmdOff)
                {
                    // In-place exit proof: switch the Settlement Local policy to Attuned via the accepted
                    // owner-only handler. An unrelated in-area occupant is no longer policy-eligible → factor 1.
                    string offOp = "savor-seam-off-" + stoneId.Value;
                    string rc = driver.SetPolicy(subject, stoneId, LocalBeneficiaryMode.Attuned, null, offOp);
                    Plugin.Log.LogInfo("[Niflheim/HomesteadStones] [savor-seam] policy→Attuned stone=" + stoneId.Value + " rc=" + rc);
                    return;
                }

                // Seed a bare Stone-Level-2 aggregate into the authoritative Local runtime store if none is
                // present yet, so the accepted commit/develop commands have a Stone to operate on. This is
                // NOT a fabricated development — the node still reaches Developed only through the driver's
                // accepted, receipt-backed commands below.
                EnsureBareStone(local.Stones, stoneId);

                // Develop the Savor node through accepted commands only, then set the Everyone policy so an
                // in-area occupant is eligible. Any rejection surfaces verbatim.
                var result = driver.Provision(subject, stoneId, CookingNodes.SavorTheHearth, "savor-seam-" + stoneId.Value);
                if (!result.IsDeveloped)
                {
                    Plugin.Log.LogWarning("[Niflheim/HomesteadStones] [savor-seam] provisioning did NOT develop Savor stone="
                        + stoneId.Value + " failedStep=" + result.FailedStep + " rc=" + result.ResultCode);
                    return;
                }
                string onOp = "savor-seam-on-" + stoneId.Value;
                string policyRc = driver.SetPolicy(subject, stoneId, LocalBeneficiaryMode.Everyone, null, onOp);
                Plugin.Log.LogInfo("[Niflheim/HomesteadStones] [savor-seam] Savor developed stone=" + stoneId.Value
                    + " steps=" + result.Steps + " policy=Everyone rc=" + policyRc);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Savor seam threw: " + ex);
            }
        }

        /// <summary>Seed a bare, undeveloped Stone-Level-2 Homestead aggregate for <paramref name="stoneId"/>
        /// into the authoritative store when absent, so the accepted commit/develop commands have a Stone to
        /// act on. No Tree committed, no node developed — those still cross the real handlers.</summary>
        private static void EnsureBareStone(IStoneAggregateStore stones, StoneId stoneId)
        {
            if (stones.GetStone(stoneId) != null) return;
            stones.PutStone(new StoneProgressionAggregate(
                stoneId, revision: 1, historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: new Domain.Snapshots.VersionedId("FoundationalTree", 1),
                foundationalCatalog: new Domain.Snapshots.VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: Domain.Content.HomesteadProgressionCatalog.CurrentContentRegistryVersion,
                createdProvenance: "savor-seam", updatedProvenance: "savor-seam",
                mirroredStoneAp: 0, lastAppliedReceiptId: "savor-seam"));
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
                return Application.Runtime.VanillaAdminIdentity.ListContainsId(
                    new List<string>(adminList), host!, Application.Runtime.VanillaAdminIdentity.DefaultPlatform);
            }
            catch { return false; }
        }

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
                    "SBPR: (playtest, admin-only) develop Savor the Hearth at your Stone / switch policy — usage: sbpr_savor on|off",
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
                        args.Context?.AddString("sbpr_savor: request sent (server admin + seam-enabled + prior sbpr_provision bond required to take effect).");
                    });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Savor console command registration failed (ignored): " + ex.Message);
            }
        }
    }
}
