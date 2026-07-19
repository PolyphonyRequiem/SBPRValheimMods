using System;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T016 shared runtime substrate — the BOUNDED server→client Local Effect delivery TRANSPORT
    /// (contracts.md §"Notification contract"). This is the missing channel the T021 investigation
    /// identified: the crafting/cooking/placement gates run CLIENT-side, but every activation fact is
    /// SERVER-owned, and there was no bridge. This transport is that bridge — and NOTHING more:
    ///
    ///   * CLIENT → SERVER: a bounded per-peer REQUEST carrying ONLY the Stone id the client is asking
    ///     about and the client's server-observed position (so the server can confirm Area occupancy). No
    ///     activation, revision, or effect state is ever authored by the client.
    ///   * SERVER → CLIENT: the server derives the per-occupant <see cref="LocalActivationSnapshot"/> from
    ///     the authoritative <see cref="LocalProgressionObserver.Server"/> + server-observed presence facts
    ///     (owner/relationship/governor/Area) and replies with the serialized snapshot. The client applies
    ///     it into its <see cref="LocalProgressionObserver.ClientCache"/>, which drops stale/reordered
    ///     replies by sequence.
    ///
    /// The server resolves the requesting peer's BOUND INTERNAL principal from the delivering ZRpc (the
    /// same transport-authenticated match the placement ingress uses), never the payload. Fail closed: an
    /// unbound peer, an unknown Stone, or a missing runtime yields a Denied snapshot (empty, all-inactive).
    ///
    /// References Valheim (ZNet, ZNetPeer, ZRpc, Player) → net48-only, NOT link-compiled into net8. The
    /// engine-free service/cache/snapshot it drives are fully unit-tested. Clean-side (ADR-0001): base-game
    /// transport types only; no other mod code.
    /// </summary>
    [HarmonyPatch]
    internal static class LocalActivationDeliveryObserver
    {
        // Direct per-peer ZRpc method names (hashed by ZRpc.Register — LOCK; a rename desyncs a mixed session).
        internal const string RpcRequest = "SBPR_Niflheim_LocalActivationRequest";
        internal const string RpcSnapshot = "SBPR_Niflheim_LocalActivationSnapshot";

        // ── SERVER SIDE: register the request handler on every new connection ────────────────────────

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        [HarmonyPostfix]
        private static void OnNewConnection(ZNetPeer peer)
        {
            try
            {
                if (peer == null || peer.m_rpc == null) return;
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                // The client asks about ONE Stone. It carries NO position: the server resolves the
                // requesting peer's current position from its own character ZDO (payload-authoritative
                // occupancy was the PR #368 review Blocker 2 — a client could forge x/z to claim it stood
                // inside any Area). See RPC_ActivationRequest.
                peer.m_rpc.Register<string>(RpcRequest, RPC_ActivationRequest);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Activation request handler registration failed (ignored): " + ex.Message);
            }
        }

        /// <summary>SERVER handler: derive the requesting occupant's read model from authoritative state +
        /// server-observed presence and reply with the serialized snapshot. Fail closed on any missing
        /// input. Identity is the transport-authenticated peer, never the payload; the occupant's position
        /// (for Area occupancy) is resolved server-side from the peer's character ZDO, never client x/z.</summary>
        private static void RPC_ActivationRequest(ZRpc rpc, string stoneIdValue)
        {
            try
            {
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;
                var runtime = LocalProgressionObserver.Server;
                var foundational = FoundationalPlacementObserver.Server;
                if (runtime == null || foundational == null) return;
                if (string.IsNullOrEmpty(stoneIdValue)) return;

                var stoneId = new Domain.Identity.StoneId(stoneIdValue);

                // Resolve the transport-authenticated peer's BOUND INTERNAL principal (never the payload).
                var peer = ZdoAuthenticatedSenderSource.PeerForRpc(znet, rpc);
                string account = string.Empty, character = string.Empty;
                if (!ZdoAuthenticatedSenderSource.Instance.TryResolveFromPeer(peer, out var senderFacts)
                    || !AuthenticatedSenderBinder.TryBind(senderFacts, out account, out character))
                {
                    // Unbound peer — reply fail-closed so the client invalidates any held effect.
                    ReplyDenied(rpc, stoneId, string.Empty);
                    return;
                }

                var occupant = new Domain.Identity.AccountId(account);
                var ch = new Domain.Identity.CharacterId(character);

                // Server-owned occupancy: resolve the peer's CURRENT position from its own character ZDO and
                // confirm it is inside THIS Stone's Area. The client never supplies a position, so a forged
                // coordinate cannot claim occupancy (PR #368 review Blocker 2). Fail closed when the peer's
                // ZDO/position authority is unavailable.
                if (!TryResolvePeerPosition(peer, out float px, out float pz))
                {
                    ReplyDenied(rpc, stoneId, account);
                    return;
                }
                bool inside = foundational.StoneAreas.IsInside(stoneId, px, pz);

                // Cross-account governance facts (owner + Stone-wide authorized-Governor presence) and the
                // occupant's own relationship activity are DERIVED from committed state — never a dead flag
                // and never a client claim (PR #368 review Blocker 1). ComposePresence owns owner/governor
                // derivation; the relationship-active fact is the occupant's own committed reservation.
                bool hasRelationship = runtime.Authority.GetAuthority(occupant, stoneId).HasActive(ch);
                var presence = runtime.ComposePresence(stoneId, occupant, ch, hasRelationship, inside);

                var delivery = runtime.Activation.Publish(stoneId, presence, "request");
                rpc.Invoke(RpcSnapshot, delivery.Snapshot.Serialize());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Activation request threw: " + ex);
            }
        }

        /// <summary>Resolve the transport-authenticated peer's CURRENT world position from its server-owned
        /// character ZDO (never a client claim), mirroring RelationshipProvisioningAdmin.TryResolveSenderStone.
        /// Fail closed (returns false) when ZDO/position authority is unavailable.</summary>
        private static bool TryResolvePeerPosition(ZNetPeer? peer, out float x, out float z)
        {
            x = 0f; z = 0f;
            if (peer == null) return false;
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return false;
            if (peer.m_characterID.IsNone()) return false;
            var zdo = zdoMan.GetZDO(peer.m_characterID);
            if (zdo == null || !zdo.IsValid()) return false;
            Vector3 pos = zdo.GetPosition();
            x = pos.x; z = pos.z;
            return true;
        }

        private static void ReplyDenied(ZRpc rpc, Domain.Identity.StoneId stoneId, string account)
        {
            try
            {
                var snap = LocalActivationSnapshot.Denied(stoneId, new Domain.Identity.AccountId(account ?? string.Empty), 0);
                rpc.Invoke(RpcSnapshot, snap.Serialize());
            }
            catch { /* best-effort fail-closed reply */ }
        }

        // ── CLIENT SIDE: register the snapshot receive handler on the server connection ──────────────

        /// <summary>On a non-server peer, register the snapshot receive handler on the server connection so
        /// pushed/replied snapshots land in the client cache. The listen-host acts as its own client via the
        /// same holder, so no registration is needed there (it reads the server runtime directly).</summary>
        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        [HarmonyPostfix]
        private static void OnNewConnection_Client(ZNetPeer peer)
        {
            try
            {
                if (peer == null || peer.m_rpc == null) return;
                if (ZNet.instance == null || ZNet.instance.IsServer()) return;
                peer.m_rpc.Register<string>(RpcSnapshot, RPC_ActivationSnapshot);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Activation snapshot handler registration failed (ignored): " + ex.Message);
            }
        }

        /// <summary>CLIENT handler: apply a delivered snapshot into the bounded cache (which drops
        /// stale/reordered ones by sequence). The client never authors activation — it only records what the
        /// server derived.</summary>
        private static void RPC_ActivationSnapshot(ZRpc rpc, string serialized)
        {
            try
            {
                if (string.IsNullOrEmpty(serialized)) return;
                var snapshot = LocalActivationSnapshot.Deserialize(serialized);
                LocalProgressionObserver.ClientCache.Apply(snapshot);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Activation snapshot apply failed (ignored): " + ex.Message);
            }
        }

        /// <summary>CLIENT helper: request the current Local activation read model for a Stone from the
        /// server. Bounded: carries ONLY the Stone id — never any position or activation authority. The
        /// server resolves the requesting peer's occupancy server-side from its own character ZDO, so a
        /// client cannot forge where it stands (PR #368 review Blocker 2).</summary>
        internal static void RequestSnapshot(Domain.Identity.StoneId stoneId)
        {
            try
            {
                var znet = ZNet.instance;
                if (znet == null || znet.IsServer()) return; // the host reads the server runtime directly
                var serverRpc = znet.GetServerRPC();
                serverRpc?.Invoke(RpcRequest, stoneId.Value);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Activation request send failed (ignored): " + ex.Message);
            }
        }
    }
}
