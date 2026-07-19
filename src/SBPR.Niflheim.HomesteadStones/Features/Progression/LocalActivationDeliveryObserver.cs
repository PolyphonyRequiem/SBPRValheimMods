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
                // stoneId, x, z — the client asks about one Stone at its server-observed position.
                peer.m_rpc.Register<string, float, float>(RpcRequest, RPC_ActivationRequest);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Activation request handler registration failed (ignored): " + ex.Message);
            }
        }

        /// <summary>SERVER handler: derive the requesting occupant's read model from authoritative state +
        /// server-observed presence and reply with the serialized snapshot. Fail closed on any missing
        /// input. Identity is the transport-authenticated peer, never the payload.</summary>
        private static void RPC_ActivationRequest(ZRpc rpc, string stoneIdValue, float x, float z)
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

                // Server-observed presence facts: Area occupancy from the server's StoneAreaMembership,
                // owner from the server-owned owner registry, relationship/governor from committed authority
                // state. These are cross-account SERVER truth, never client claims.
                bool inside = foundational.StoneAreas.IsInside(stoneId, x, z);
                bool isOwner = LocalProgressionObserver.OwnerByStone.TryGetValue(stoneId.Value, out var ownerAcct)
                    && string.Equals(ownerAcct, account, StringComparison.Ordinal);
                bool hasRelationship = runtime.Authority.GetAuthority(occupant, stoneId).HasActive(ch);
                bool governorPresent = LocalProgressionObserver.OwnerByStone.ContainsKey(stoneId.Value);

                var presence = new OccupantPresence(occupant, ch, isOwner, hasRelationship, inside, governorPresent);
                var delivery = runtime.Activation.Publish(stoneId, presence, "request");
                rpc.Invoke(RpcSnapshot, delivery.Snapshot.Serialize());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Activation request threw: " + ex);
            }
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
        /// server, supplying the local player's position so the server can confirm Area occupancy. Bounded:
        /// carries only the Stone id + position, never any activation authority.</summary>
        internal static void RequestSnapshot(Domain.Identity.StoneId stoneId, Vector3 position)
        {
            try
            {
                var znet = ZNet.instance;
                if (znet == null || znet.IsServer()) return; // the host reads the server runtime directly
                var serverRpc = znet.GetServerRPC();
                serverRpc?.Invoke(RpcRequest, stoneId.Value, position.x, position.z);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Activation request send failed (ignored): " + ex.Message);
            }
        }
    }
}
