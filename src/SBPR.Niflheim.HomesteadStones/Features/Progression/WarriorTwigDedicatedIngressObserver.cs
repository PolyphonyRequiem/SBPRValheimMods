using System;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T029 remediation — the net48-ONLY DEDICATED-server ingress observer for the Warrior T.W.I.G.
    /// Training gate. Direct analogue of <see cref="DedicatedPlacementIngressObserver"/>, but it GATES and
    /// UNDOES rather than credits.
    ///
    /// A joined dedicated-server client's T.W.I.G. build never runs Player.PlacePiece on the server (that
    /// is the listen-host <see cref="WarriorTwigPlacementObserver"/>); it replicates as a ZDO. Without this
    /// path a dedicated-client T.W.I.G. would stand completely ungated. The trust model matches the
    /// Foundational dedicated ingress exactly:
    ///   * the client sends a DIRECT per-peer notice carrying ONLY an opaque physical-instance pointer
    ///     (a ZDOID string) — never an eligibility or permission claim;
    ///   * the server authenticates the sender by the delivering ZRpc (never a forgeable routed id),
    ///     captures (transport identity + ZDOID) into the bounded <see cref="WarriorTwigPendingUndoQueue"/>,
    ///     and defers gating until the ZDO replicates;
    ///   * the pump re-derives every fact server-side (prefab, creator, position via the shared
    ///     <see cref="ZdoServerPlacedInstanceSource"/>; build Permission via vanilla PrivateArea.CheckAccess),
    ///     routes through the SAME gate, and DESTROYS the placed piece server-side on refusal (the removal
    ///     replicates to the client).
    ///
    /// References Valheim (ZNet, ZNetPeer, ZRpc, ZNetView, ZDO, ZDOID, ZDOMan, Player, Piece, PrivateArea,
    /// ZNetScene) → net48-only, not link-compiled into net8. The engine-free ingress + pending queue it
    /// drives are fully unit-tested. Clean-side (ADR-0001): base-game transport types only. ADR-0006: it
    /// destroys a live world instance on refusal; it never clones a prefab.
    /// </summary>
    [HarmonyPatch]
    internal static class WarriorTwigDedicatedIngressObserver
    {
        // Direct per-peer ZRpc method name (hashed by ZRpc.Register — LOCK; a rename desyncs a mixed session).
        internal const string RpcNotice = "SBPR_Niflheim_WarriorTwigPlacedNotice";

        // ── SERVER SIDE: register a DIRECT per-peer handler on every new connection ──────────────────

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        [HarmonyPostfix]
        private static void OnNewConnection(ZNetPeer peer)
        {
            try
            {
                if (peer == null || peer.m_rpc == null) return;
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                peer.m_rpc.Register<string>(RpcNotice, RPC_TwigPlacedNotice);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Warrior T.W.I.G. notice handler registration failed (ignored): " + ex.Message);
            }
        }

        /// <summary>SERVER notice handler. <paramref name="rpc"/> is the ACTUAL transport connection that
        /// delivered the packet — the server resolves the exact authenticated peer from it (never the
        /// payload). The transport-derived identity + ZDOID are captured into the pending queue; gating is
        /// deferred to the pump once the ZDO replicates.</summary>
        private static void RPC_TwigPlacedNotice(ZRpc rpc, string instanceKey)
        {
            try
            {
                var server = FoundationalPlacementObserver.Server;
                if (server == null) return;
                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;
                if (string.IsNullOrEmpty(instanceKey)) return;

                var peer = ZdoAuthenticatedSenderSource.PeerForRpc(znet, rpc);
                if (!ZdoAuthenticatedSenderSource.Instance.TryResolveFromPeer(peer, out var senderFacts))
                    return;

                // The peer key is the server-owned player:<s_playerID> character subject — the SAME key the
                // gate resolves the bound session and creator binding under.
                string peerKey = ServerCreatorIdentity.CharacterSubject(senderFacts.PlayerId);
                if (string.IsNullOrEmpty(peerKey)) return;

                var pending = server.WarriorTwigPending;
                if (pending == null) return;   // Local runtime not yet composed / armed.

                var result = pending.Enqueue(peerKey, instanceKey, DateTime.UtcNow.Ticks);
                if (result == WarriorTwigPendingUndoQueue.EnqueueResult.RejectedFull)
                    Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Warrior T.W.I.G. notice dropped: pending queue full (spam bound).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Warrior T.W.I.G. dedicated notice threw: " + ex);
            }
        }

        // ── SERVER SIDE: pump the pending queue on the ZDO replication cadence ────────────────────────

        [HarmonyPatch(typeof(ZDOMan), "Update")]
        [HarmonyPostfix]
        private static void OnZdoManUpdate()
        {
            try
            {
                var server = FoundationalPlacementObserver.Server;
                if (server == null) return;
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                var pending = server.WarriorTwigPending;
                if (pending == null || pending.Count == 0) return;

                var ingress = server.CreateWarriorTwigDedicatedIngress(ZdoServerPlacedInstanceSource.Instance);
                var resolved = pending.Pump(
                    DateTime.UtcNow.Ticks,
                    (peerKey, instanceKey) => ingress.Ingest(peerKey, instanceKey, BuildPermissionAt));

                foreach (var result in resolved)
                {
                    if (result.RequiresUndo)
                    {
                        UndoInstance(result.InstanceKey);
                        Plugin.Log.LogInfo("[Niflheim/HomesteadStones] " + result.ToOperatorLine() + " action=undone");
                    }
                    else
                    {
                        Plugin.Log.LogInfo("[Niflheim/HomesteadStones] " + result.ToOperatorLine() + " action=admitted");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Warrior T.W.I.G. pending pump threw: " + ex);
            }
        }

        /// <summary>Vanilla build-Permission (ward) check at a server-owned world position. The SAME gate
        /// vanilla uses for build access; never a client claim.</summary>
        private static bool BuildPermissionAt(double x, double z)
        {
            // Y is irrelevant to the ward radius test; use 0.
            return PrivateArea.CheckAccess(new Vector3((float)x, 0f, (float)z), 0f, flash: false);
        }

        /// <summary>Destroy the refused placed instance server-side (owner-claim then destroy). The removal
        /// replicates to the placing client. ADR-0006 compliant — removes a live world instance, no clone.</summary>
        private static void UndoInstance(string instanceKey)
        {
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return;
            if (!ZdoServerPlacedInstanceSource.TryParseInstanceKey(instanceKey, out var id) || id.IsNone()) return;

            var zns = ZNetScene.instance;
            if (zns == null) return;

            // Prefer destroying the live GameObject if it is instanced locally on the server.
            var go = zns.FindInstance(id);
            if (go != null)
            {
                var nview = go.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid())
                {
                    if (!nview.IsOwner()) nview.ClaimOwnership();
                    nview.Destroy();
                    return;
                }
                zns.Destroy(go);
                return;
            }

            // Not instanced locally — destroy the ZDO directly so the removal replicates.
            var zdo = zdoMan.GetZDO(id);
            if (zdo != null && zdo.IsValid())
            {
                if (!zdo.IsOwner()) zdo.SetOwner(ZDOMan.GetSessionID());
                zdoMan.DestroyZDO(zdo);
            }
        }

        // ── CLIENT SIDE: fire a notice for a locally successful T.W.I.G. placement ───────────────────

        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
        [HarmonyPostfix]
        private static void OnClientPlacePiece()
        {
            try
            {
                if (ZNet.instance == null || ZNet.instance.IsServer()) return;

                var piece = PlacedPieceCapture.PlacedPiece();
                if (piece == null) return;

                // Client-side filter: only notify for the exact T.W.I.G. prefab.
                string prefabName = piece.gameObject != null ? StripCloneSuffix(piece.gameObject.name) : string.Empty;
                if (!string.Equals(prefabName, WarriorPlacementConstants.TwigPrefabName, StringComparison.Ordinal))
                    return;

                var nview = piece.GetComponent<ZNetView>();
                var zdo = nview != null ? nview.GetZDO() : null;
                if (zdo == null || !zdo.IsValid()) return;

                string instanceKey = DedicatedPlacementIngressObserver.FormatInstanceKey(zdo.m_uid);

                var serverRpc = ZNet.instance.GetServerRPC();
                serverRpc?.Invoke(RpcNotice, instanceKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Warrior T.W.I.G. notice send failed (ignored): " + ex.Message);
            }
        }

        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }
    }

    /// <summary>The exact vanilla T.W.I.G. prefab name, mirrored from the engine-free
    /// <c>LocalPlacementProvider.TwigPrefabName</c> so the net48 client-side filter and the engine-free
    /// gate can never drift.</summary>
    internal static class WarriorPlacementConstants
    {
        internal const string TwigPrefabName = Adapters.Warrior.LocalPlacementProvider.TwigPrefabName;
    }
}
