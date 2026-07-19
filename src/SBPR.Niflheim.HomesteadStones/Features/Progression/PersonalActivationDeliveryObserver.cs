using System;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T026 remediation — the BOUNDED server→client PERSONAL Character-Effect delivery TRANSPORT. This is
    /// the channel the T026 review (PR #373) found missing: Field Fletching I is a personal Character Effect
    /// whose active/dormant status is server-owned (purchase record + active relationship), but the sibling
    /// Local delivery transport carries Stone-owned LOCAL snapshots only, so a pure joined client had no
    /// personal read model and always failed closed. This transport is that bridge — and NOTHING more:
    ///
    ///   * CLIENT → SERVER: a bounded per-peer REQUEST carrying ONLY the Stone id the client is asking
    ///     about. No activation, purchase, relationship, or effect state is ever authored by the client.
    ///   * SERVER → CLIENT: the server resolves the requesting peer's BOUND INTERNAL principal from the
    ///     delivering ZRpc (the same transport-authenticated match the placement ingress / Local delivery
    ///     use — never the payload), derives the per-(occupant, character) <see cref="PersonalActivationSnapshot"/>
    ///     from the authoritative <see cref="LocalProgressionObserver.Server"/> stores (Stone + character +
    ///     authority, via the shipped DerivedActivationView), and replies with the serialized snapshot. The
    ///     client applies it into its <see cref="LocalProgressionObserver.PersonalClientCache"/>, which drops
    ///     stale/reordered replies by sequence.
    ///
    /// Unlike the Local channel, a personal Character Effect is NOT area/policy/governor gated, so this
    /// transport needs NO server-side position resolution — active == (purchase AND active relationship),
    /// per character. Fail closed: an unbound peer, an unknown Stone, or a missing runtime yields a Denied
    /// snapshot (empty, all-inactive) so the client invalidates any previously delivered effect.
    ///
    /// References Valheim (ZNet, ZNetPeer, ZRpc) → net48-only, NOT link-compiled into net8. The engine-free
    /// service/cache/snapshot it drives are fully unit-tested. Clean-side (ADR-0001): base-game transport
    /// types only; no other mod code.
    /// </summary>
    [HarmonyPatch]
    internal static class PersonalActivationDeliveryObserver
    {
        // Direct per-peer ZRpc method names (hashed by ZRpc.Register — LOCK; a rename desyncs a mixed session).
        internal const string RpcRequest = "SBPR_Niflheim_PersonalActivationRequest";
        internal const string RpcSnapshot = "SBPR_Niflheim_PersonalActivationSnapshot";

        // ── SERVER SIDE: register the request handler on every new connection ────────────────────────

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        [HarmonyPostfix]
        private static void OnNewConnection(ZNetPeer peer)
        {
            try
            {
                if (peer == null || peer.m_rpc == null) return;
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                peer.m_rpc.Register<string>(RpcRequest, RPC_PersonalActivationRequest);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Personal activation request handler registration failed (ignored): " + ex.Message);
            }
        }

        /// <summary>SERVER handler: derive the requesting caller's personal read model from authoritative
        /// state and reply with the serialized snapshot. Fail closed on any missing input. Identity is the
        /// transport-authenticated peer's BOUND INTERNAL principal, never the payload; the personal effect is
        /// not area-gated, so no position is resolved.</summary>
        private static void RPC_PersonalActivationRequest(ZRpc rpc, string stoneIdValue)
        {
            try
            {
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;
                var runtime = LocalProgressionObserver.Server;
                if (runtime == null) return;
                if (string.IsNullOrEmpty(stoneIdValue)) return;

                var stoneId = new Domain.Identity.StoneId(stoneIdValue);

                // Resolve the transport-authenticated peer's BOUND INTERNAL principal (never the payload).
                var peer = ZdoAuthenticatedSenderSource.PeerForRpc(znet, rpc);
                string account = string.Empty, character = string.Empty;
                if (!ZdoAuthenticatedSenderSource.Instance.TryResolveFromPeer(peer, out var senderFacts)
                    || !AuthenticatedSenderBinder.TryBind(senderFacts, out account, out character))
                {
                    // Unbound peer — reply fail-closed so the client invalidates any held effect.
                    ReplyDenied(rpc, stoneId, string.Empty, string.Empty);
                    return;
                }

                var occupant = new Domain.Identity.AccountId(account);
                var ch = new Domain.Identity.CharacterId(character);

                // Publish (re-derive + bump the delivery sequence) and reply with the snapshot. Fetch would
                // also work for a pure read, but Publish keeps the monotonic sequence advancing so a client
                // that later receives a notification converges — the snapshot is a fresh derivation either
                // way and never a second ledger.
                var delivery = runtime.PersonalActivation.Publish(stoneId, occupant, ch, "request");
                rpc.Invoke(RpcSnapshot, delivery.Snapshot.Serialize());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Personal activation request threw: " + ex);
            }
        }

        private static void ReplyDenied(ZRpc rpc, Domain.Identity.StoneId stoneId, string account, string character)
        {
            try
            {
                var snap = PersonalActivationSnapshot.Denied(
                    stoneId, new Domain.Identity.AccountId(account ?? string.Empty),
                    new Domain.Identity.CharacterId(character ?? string.Empty), 0);
                rpc.Invoke(RpcSnapshot, snap.Serialize());
            }
            catch { /* best-effort fail-closed reply */ }
        }

        // ── CLIENT SIDE: register the snapshot receive handler on the server connection ──────────────

        /// <summary>On a non-server peer, register the snapshot receive handler on the server connection so
        /// pushed/replied snapshots land in the client cache. The listen-host reads the server runtime
        /// directly, so no registration is needed there.</summary>
        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        [HarmonyPostfix]
        private static void OnNewConnection_Client(ZNetPeer peer)
        {
            try
            {
                if (peer == null || peer.m_rpc == null) return;
                if (ZNet.instance == null || ZNet.instance.IsServer()) return;
                peer.m_rpc.Register<string>(RpcSnapshot, RPC_PersonalActivationSnapshot);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Personal activation snapshot handler registration failed (ignored): " + ex.Message);
            }
        }

        /// <summary>CLIENT handler: apply a delivered snapshot into the bounded cache (which drops
        /// stale/reordered ones by sequence). The client never authors activation — it only records what the
        /// server derived.</summary>
        private static void RPC_PersonalActivationSnapshot(ZRpc rpc, string serialized)
        {
            try
            {
                if (string.IsNullOrEmpty(serialized)) return;
                var snapshot = PersonalActivationSnapshot.Deserialize(serialized);
                LocalProgressionObserver.PersonalClientCache.Apply(snapshot);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Personal activation snapshot apply failed (ignored): " + ex.Message);
            }
        }

        /// <summary>CLIENT helper: request the current personal Character-Effect read model for a Stone from
        /// the server. Bounded: carries ONLY the Stone id — never any activation authority. The server
        /// resolves the requesting peer's bound principal server-side, so a client cannot forge whose effect
        /// it asks for.</summary>
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
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Personal activation request send failed (ignored): " + ex.Message);
            }
        }
    }
}
