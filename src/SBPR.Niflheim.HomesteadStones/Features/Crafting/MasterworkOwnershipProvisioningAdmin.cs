using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Features.Progression;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Crafting
{
    /// <summary>
    /// T022 remediation R4 — the net48-ONLY, isolated-QA seam that reaches an ACTIVE PURCHASED Masterwork
    /// personal Character Effect for a joined admin through the accepted, receipt-backed handlers, so the
    /// genuine dedicated-server four-AT Masterwork run is structurally reachable.
    ///
    /// WHY THIS EXISTS (the R4 gap): at PR #392 head the Masterwork issuance gate
    /// (<see cref="MasterworkIssuanceObserver"/> → WorkmanshipIssuanceProvider.IsMasterworkActive) requires a
    /// PERSONAL PURCHASE record for Masterwork@1 at the Stone plus an active relationship. But no runtime
    /// caller ever drove <see cref="LocalProvisioningIngress.PurchaseNode"/>, and the Local-node develop seam
    /// only develops Stone-cultivated Local nodes — so a joined principal could never acquire a Masterwork
    /// purchase record and IsMasterworkActive was always false. This seam is the missing ownership ingress:
    /// it drives <see cref="LocalProvisioningIngress.OwnMasterwork"/>, which (1) develops+offers Masterwork on
    /// the Stone via the SAME accepted commit→BP→ApplyBPToNode commands the Local path uses, then (2) purchases
    /// it via the accepted PurchaseCommandHandler. It carries NO parallel purchase/activation state, never a
    /// direct write; ACTIVE/DORMANT is still derived per craft by the DerivedActivationView (no second ledger).
    ///
    /// It never mints Attunement or Personal AP: the acting subject must already hold an active Bond (for the
    /// develop/offer) AND an active Attunement (for the purchase authority) established via the shipped
    /// <c>sbpr_provision bond</c> / <c>sbpr_provision attune</c> seam, and must already hold sufficient earned
    /// Personal AP (from real Foundational placement). An unauthorized / unattuned / unfunded subject is
    /// rejected verbatim by the real accepted gates (RelationshipRequired / InsufficientPersonalAP), so a QA
    /// run that "provisions" ownership has provably crossed the real authority, AP-debit, and idempotency gates.
    ///
    /// Restriction (disabled by default; isolated-QA only):
    ///   * the server-owned BepInEx flag <c>Crafting.EnableAdminMasterworkOwnershipProvisioning</c> (default
    ///     FALSE) must be ON, AND
    ///   * the transport-authenticated sender must be a normalized server ADMIN.
    /// Neither is client-settable. Outside that gate the handler is never registered (flag off) or rejects
    /// (non-admin sender), so production behavior fails closed. This is never a shipping gameplay command and
    /// never a gameplay shortcut — every gate the real purchase enforces still runs.
    ///
    /// Enable / invoke / disable / verify (isolated-QA operator steps):
    ///   1. ENABLE: set <c>[Crafting] EnableAdminMasterworkOwnershipProvisioning = true</c> (and
    ///      <c>[Progression] EnableAdminRelationshipProvisioning = true</c>) in the server's BepInEx config and
    ///      restart the dedicated server. On boot the request handler registers per-peer.
    ///   2. AUTHORIZE: join as a server ADMIN, stand inside the Homestead Stone Area, run
    ///      <c>sbpr_provision bond</c> then <c>sbpr_provision attune</c>, and ensure you have earned at least
    ///      the Masterwork purchase AP through real placement.
    ///   3. INVOKE: run <c>sbpr_master own</c>. The seam develops+offers Masterwork and purchases it through the
    ///      accepted handlers.
    ///   4. VERIFY: the server log prints "[masterwork-ownership] outcome=Purchased ..."; a subsequent eligible
    ///      craft inside the Area now issues a validated Workmanship Property (MasterworkIssuanceObserver).
    ///   5. DISABLE: set the flag back to false and restart; the handler is no longer registered.
    ///
    /// References Valheim (ZNet, ZNetPeer, ZRpc, ZDOMan, ZDO, Terminal) → net48-only, not link-compiled.
    /// </summary>
    [HarmonyPatch]
    internal static class MasterworkOwnershipProvisioningAdmin
    {
        // Direct per-peer ZRpc method name (hashed by ZRpc.Register — LOCK; a rename desyncs a mixed session).
        internal const string RpcOwn = "SBPR_Niflheim_ProvisionMasterworkOwnership";

        // Client console command name.
        internal const string ConsoleCommand = "sbpr_master";

        // Payload discriminator: 1 = offer (Governor develops+offers Masterwork), 2 = buy (attuned buyer
        // purchases the offered Masterwork). Kept separate because the accepted reservation model allows one
        // character only ONE active relationship per Stone: develop/offer needs a Bond, purchase needs an
        // Attunement, so the two halves are run by two subjects (the genuine two-client QA matrix).
        private const int CmdOffer = 1;
        private const int CmdBuy = 2;

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
                peer.m_rpc.Register<int>(RpcOwn, RPC_Own);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Masterwork ownership provisioning handler registration failed (ignored): " + ex.Message);
            }
        }

        private static void RPC_Own(ZRpc rpc, int commandType)
        {
            try
            {
                var localServer = LocalProgressionObserver.Server;
                var foundational = FoundationalPlacementObserver.Server;
                if (localServer == null || foundational == null) return;
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;
                if (EnableProvisioning == null || !EnableProvisioning.Value) return;   // re-checked at call time
                if (commandType != CmdOffer && commandType != CmdBuy) return;

                // Transport-authenticated peer (never the forgeable routed sender).
                var peer = ZdoAuthenticatedSenderSource.PeerForRpc(znet, rpc);
                if (peer == null) return;

                if (!SenderIsAdmin(znet, peer))
                {
                    Plugin.Log.LogWarning(
                        "[Niflheim/HomesteadStones] Rejected Masterwork ownership provisioning from non-admin sender.");
                    return;
                }

                // Server-derive the acting subject in the SAME bound-internal principal space the Bond/Attunement
                // were established under. An UNBOUND peer fails closed.
                if (!ZdoAuthenticatedSenderSource.Instance.TryResolveFromPeer(peer, out var senderFacts))
                    return;
                string peerKey = ServerCreatorIdentity.CharacterSubject(senderFacts.PlayerId);
                if (string.IsNullOrEmpty(peerKey) ||
                    !foundational.BoundSessions.TryResolve(peerKey, out var boundPrincipal) ||
                    string.IsNullOrEmpty(boundPrincipal.Account.Value) ||
                    string.IsNullOrEmpty(boundPrincipal.Character.Value))
                {
                    Plugin.Log.LogWarning(
                        "[Niflheim/HomesteadStones] Masterwork ownership provisioning: sender has no bound internal session (fail closed).");
                    return;
                }

                string account = boundPrincipal.Account.Value;
                string character = boundPrincipal.Character.Value;

                if (!TryResolveSenderStone(peer, foundational, out var stoneId))
                {
                    Plugin.Log.LogWarning(
                        "[Niflheim/HomesteadStones] Masterwork ownership provisioning: sender not inside a Homestead Stone Area.");
                    return;
                }

                var subject = new AuthoritativeSubject(new AccountId(account), new CharacterId(character));

                // Deterministic op prefix binds the acting bound-internal subject + Stone so an exact re-run
                // replays idempotently through the accepted handlers (single purchase, single AP debit).
                string opPrefix = "qa-master-" + ProvisioningOperationBinding.CorrelationTag(account, character)
                    + "-" + stoneId.Value;

                var ingress = localServer.CreateLocalProvisioningIngress();
                // Each half is authorized by the CALLER's own established relationship: offer needs the
                // caller's Bond, buy needs the caller's Attunement. The reservation model forbids one character
                // holding both at one Stone, so the two-client QA matrix runs offer as the Governor and buy as
                // the attuned buyer.
                LocalProvisioningResult result = commandType == CmdOffer
                    ? ingress.OfferMasterwork(subject, stoneId, opPrefix)
                    : ingress.BuyMasterwork(subject, stoneId, opPrefix);

                Plugin.Log.LogInfo("[Niflheim/HomesteadStones] [masterwork-ownership] "
                    + (commandType == CmdOffer ? "offer " : "buy ") + result.ToOperatorLine()
                    + " subject=" + ProvisioningOperationBinding.CorrelationTag(account, character)
                    + " stone=" + stoneId.Value);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Masterwork ownership provisioning threw: " + ex);
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
    /// T022 remediation R4 — the CLIENT console command that invokes Masterwork ownership provisioning. A
    /// joined admin runs <c>sbpr_master own</c>; it sends the discriminator on the SERVER connection as a
    /// direct notice, landing on the server's transport-bound handler. Registered once when Terminal's command
    /// table initializes. Inert unless the server has the seam enabled and the caller is an admin.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class MasterworkOwnershipProvisioningConsole
    {
        private static bool registered;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (registered) return;
            registered = true;
            try
            {
                _ = new Terminal.ConsoleCommand(MasterworkOwnershipProvisioningAdmin.ConsoleCommand,
                    "SBPR: (isolated-QA, admin-only) develop+offer or purchase Masterwork at your Stone — usage: sbpr_master offer|buy",
                    args =>
                    {
                        // 1 = offer (Governor develops+offers), 2 = buy (attuned buyer purchases). Default offer.
                        int cmd = 1;
                        if (args.Length >= 2 && string.Equals(args[1], "buy", StringComparison.OrdinalIgnoreCase))
                            cmd = 2;

                        var serverRpc = ZNet.instance != null ? ZNet.instance.GetServerRPC() : null;
                        if (serverRpc == null)
                        {
                            args.Context?.AddString("sbpr_master: not connected to a server.");
                            return;
                        }
                        serverRpc.Invoke(MasterworkOwnershipProvisioningAdmin.RpcOwn, cmd);
                        args.Context?.AddString("sbpr_master: request sent (server admin + seam-enabled + prior sbpr_provision bond [offer] / attune + earned AP [buy] required to take effect).");
                    });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Masterwork ownership console command registration failed (ignored): " + ex.Message);
            }
        }
    }
}
