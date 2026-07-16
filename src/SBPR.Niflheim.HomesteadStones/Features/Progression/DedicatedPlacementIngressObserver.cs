using System;
using System.Globalization;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T009R4 — the DEDICATED-server ingress seam, rebuilt to be TRANSPORT-BOUND and race-safe.
    ///
    /// What T009R2/R3 got wrong (adversarial review Blockers 2 + 5):
    ///   * Blocker 2 (forgeable sender). The old handler was a ZRoutedRpc routed method whose
    ///     <c>long sender</c> is the CLIENT-serialized <c>RoutedRPCData.m_senderPeerID</c>; vanilla's
    ///     <c>RPC_RoutedRPC</c> never validates it against the delivering <c>ZRpc</c>, so a hostile client
    ///     could forge another peer's id and redirect credit. High-value authority must come from the
    ///     ACTUAL transport. This is now a DIRECT per-peer <c>ZRpc</c> handler registered on every peer's
    ///     own rpc, so the server receives the real <c>ZRpc</c> and resolves the exact authenticated
    ///     <c>ZNetPeer</c> that delivered the packet (vanilla's own <c>ZNet.GetPeer(ZRpc)</c> seam,
    ///     reproduced over the public peer table). Identity is derived from THAT peer — never the payload.
    ///   * Blocker 5 (replication race). The placed piece's ZDO transmits to the server LATER, on the
    ///     ZDOMan.Update cadence, so an inline ingest usually failed NoSuchInstance permanently. The
    ///     handler now captures the transport-authenticated identity + physical ZDOID into the server's
    ///     bounded <see cref="PendingRevalidationQueue"/>; a pump on the ZDOMan.Update tick retries the
    ///     shared revalidation only until the authoritative ZDO appears or a short deadline expires.
    ///
    /// The notice payload still carries ONLY an opaque physical-instance pointer (a ZDOID string). Account,
    /// character, Stone, position, prefab, creator, and permissions are all re-derived server-side. The
    /// send side is a direct invoke on the server peer's rpc, so it lands on this exact handler.
    ///
    /// References Valheim (ZNet, ZNetPeer, ZRpc, ZNetView, ZDO, ZDOID, ZDOMan, Player, Piece) → net48-only,
    /// not link-compiled into net8. The engine-free ingress + pending queue it drives are fully unit-tested.
    /// Clean-side (ADR-0001): base-game transport types only.
    /// </summary>
    [HarmonyPatch]
    internal static class DedicatedPlacementIngressObserver
    {
        // Direct per-peer ZRpc method name (hashed by ZRpc.Register — LOCK; a rename desyncs a mixed session).
        internal const string RpcNotice = "SBPR_Niflheim_FoundationalPlacedNotice";

        // Client-side spam guard only (the SERVER re-resolves identity authoritatively).
        private static readonly FoundationalPrefabMap PrefabMap = FoundationalPrefabMap.CurrentBuild;

        // ── SERVER SIDE: register a DIRECT per-peer handler on every new connection ──────────────────

        /// <summary>Register the transport-bound notice handler on each peer's OWN rpc as it connects. The
        /// handler receives the real delivering <c>ZRpc</c>, so the sender is authenticated by transport,
        /// not by a forgeable payload id. Server-only: a pure client never registers the receive handler.</summary>
        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        [HarmonyPostfix]
        private static void OnNewConnection(ZNetPeer peer)
        {
            try
            {
                if (peer == null || peer.m_rpc == null) return;
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                peer.m_rpc.Register<string>(RpcNotice, RPC_PlacedNotice);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Placement notice handler registration failed (ignored): " + ex.Message);
            }
        }

        /// <summary>The SERVER notice handler. <paramref name="rpc"/> is the ACTUAL transport connection
        /// that delivered the packet — the server resolves the exact authenticated peer from it (never the
        /// payload). <paramref name="instanceKey"/> is the opaque candidate pointer. The transport-derived
        /// identity + ZDOID are captured into the pending-revalidation queue; the credit-bearing ingest is
        /// deferred to the pump once the ZDO replicates. Nothing is credited inline here.</summary>
        private static void RPC_PlacedNotice(ZRpc rpc, string instanceKey)
        {
            try
            {
                var server = FoundationalPlacementObserver.Server;
                if (server == null) return;
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;
                if (string.IsNullOrEmpty(instanceKey)) return;

                // Blocker 2: the AUTHENTICATED peer is the one whose m_rpc == the delivering rpc — the
                // vanilla transport-bound match, with no client-supplied id involved.
                var peer = ZdoAuthenticatedSenderSource.PeerForRpc(znet, rpc);
                if (!ZdoAuthenticatedSenderSource.Instance.TryResolveFromPeer(peer, out var senderFacts))
                    return;
                if (!AuthenticatedSenderBinder.TryBind(senderFacts, out string account, out string character))
                    return;

                // Blocker 5: defer — the ZDO may not have replicated yet. Capture identity now, ingest later.
                var result = server.PendingPlacements.Enqueue(account, character, instanceKey, DateTime.UtcNow.Ticks);
                if (result == PendingRevalidationQueue.EnqueueResult.RejectedFull)
                    Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Placement notice dropped: pending queue full (spam bound).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Dedicated placement notice threw: " + ex);
            }
        }

        // ── SERVER SIDE: pump the pending queue on the ZDO replication cadence ────────────────────────

        /// <summary>Drive one pending-revalidation pump per ZDOMan.Update tick. Each pending entry re-runs
        /// the shared <see cref="DedicatedPlacementIngress"/> (which independently re-derives every
        /// credit-bearing fact from the server's own ZDO store) ONLY until the ZDO resolves or the deadline
        /// expires. Terminal outcomes are logged; timeouts write no credit.</summary>
        [HarmonyPatch(typeof(ZDOMan), "Update")]
        [HarmonyPostfix]
        private static void OnZdoManUpdate()
        {
            try
            {
                var server = FoundationalPlacementObserver.Server;
                if (server == null) return;
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (server.PendingPlacements.Count == 0) return;

                var ingress = server.CreateDedicatedIngress(ZdoServerPlacedInstanceSource.Instance);
                var resolved = server.PendingPlacements.Pump(
                    DateTime.UtcNow.Ticks,
                    (account, character, key) => ingress.Ingest(account, character, key));

                foreach (var outcome in resolved)
                    Plugin.Log.LogInfo("[Niflheim/HomesteadStones] " + outcome.ToOperatorLine());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Pending placement pump threw: " + ex);
            }
        }

        // ── CLIENT SIDE: fire a notice for a locally successful placement ────────────────────────────

        /// <summary>Postfix on the placing client's own <c>Player.PlacePiece</c>. On a NON-server peer
        /// (a joined dedicated-server client) whose placement succeeded, send a direct notice on the SERVER
        /// peer's rpc pointing at the placed piece's ZDOID so the server can revalidate + credit. On the
        /// authoritative host this no-ops — the listen-host <see cref="FoundationalPlacementObserver"/>
        /// already credits directly.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
        [HarmonyPostfix]
        private static void OnClientPlacePiece()
        {
            try
            {
                if (ZNet.instance == null || ZNet.instance.IsServer()) return;

                var piece = PlacedPieceCapture.PlacedPiece();
                if (piece == null) return;

                // Client-side spam guard: only notify for pieces that map to a Foundational stable id.
                string prefabName = piece.gameObject != null ? StripCloneSuffix(piece.gameObject.name) : string.Empty;
                if (string.IsNullOrEmpty(PrefabMap.ResolveStablePieceId(prefabName))) return;

                var nview = piece.GetComponent<ZNetView>();
                var zdo = nview != null ? nview.GetZDO() : null;
                if (zdo == null || !zdo.IsValid()) return;   // no durable instance yet → nothing to point at

                string instanceKey = FormatInstanceKey(zdo.m_uid);

                // Direct per-peer invoke on the SERVER connection: this lands on the server's transport-bound
                // handler above (NOT a routed RPC), so the server authenticates us by the delivering ZRpc.
                var serverRpc = ZNet.instance.GetServerRPC();
                serverRpc?.Invoke(RpcNotice, instanceKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Dedicated placement notice send failed (ignored): " + ex.Message);
            }
        }

        internal static string FormatInstanceKey(ZDOID id) =>
            id.UserID.ToString(CultureInfo.InvariantCulture) + ":" + id.ID.ToString(CultureInfo.InvariantCulture);

        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }
    }
}
