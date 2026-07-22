// Server-role per-peer ZRpc bridge (ADR-0009 §2, §3.2, §5.1; PR #408 §3.4) — M2R.
//
// The dedicated server exposes NO host listener. Instead a Harmony POSTFIX on the private
// ZNet.OnNewConnection(ZNetPeer) registers exactly ONE fixed helper ZRpc verb on that peer's
// m_rpc after vanilla registration. When the runner (an authenticated GUI helper peer)
// invokes that verb, the handler resolves the ACTUAL delivering peer from the inbound ZRpc
// (ZNet.GetPeer(rpc)) — never a claimed identity — captures the connection generation, and
// delegates to the engine-free ServerRpcResponder for admission + admin recheck + a receipt,
// which it returns to the caller over the same peer rpc. In M2R the responder supports only
// status/ping/reject (no fixtures/actions), so this bridge performs ZERO game mutation.
//
// The hook is only installed once the arming gate has passed and the role is Server; the
// verb is namespaced "SBPRQA.Control" so it can never collide with a vanilla RPC name.
using System;
using BepInEx.Logging;
using HarmonyLib;
using SBPR.QaHarness.T022.Core.ControlPlane;

namespace SBPR.QaHarness.T022.Runtime
{
    /// <summary>Installs + services the fixed per-peer QA control RPC on the server role.</summary>
    internal static class QaServerRpcBridge
    {
        /// <summary>The single fixed, namespaced RPC verb name (never collides with a vanilla RPC).</summary>
        public const string RpcName = "SBPRQA.Control";

        /// <summary>The reply RPC the handler invokes back on the delivering peer with the receipt JSON.</summary>
        public const string RpcReplyName = "SBPRQA.ControlReply";

        // Set once at arm; read by the Harmony postfix. Null when disarmed => the postfix no-ops.
        private static ControlPlaneComponent? _component;
        private static ManualLogSource? _log;

        /// <summary>Arm the bridge: after this, new peer connections get the fixed QA verb registered.</summary>
        internal static void Arm(ControlPlaneComponent component, ManualLogSource log)
        {
            _component = component;
            _log = log;
        }

        /// <summary>Disarm: new connections no longer get the verb (existing ones are dropped on teardown).</summary>
        internal static void Disarm()
        {
            _component = null;
        }

        /// <summary>
        /// Harmony postfix target: ZNet.OnNewConnection(ZNetPeer). Registers the fixed QA control
        /// verb on the new peer's rpc, but ONLY when armed and running as the server role.
        /// </summary>
        internal static void OnNewConnectionPostfix(ZNetPeer peer)
        {
            var comp = _component;
            if (comp == null || !comp.IsArmed || comp.ServerResponder == null) return;
            if (peer == null || peer.m_rpc == null) return;
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            try
            {
                peer.m_rpc.Register<string>(RpcName, RpcControlHandler);
            }
            catch (Exception e)
            {
                _log?.LogWarning($"SBPRQA: failed to register control verb on peer: {e.GetType().Name}");
            }
        }

        // Inbound QA control call. `rpc` is the DELIVERING peer's rpc (authoritative identity).
        private static void RpcControlHandler(ZRpc rpc, string payload)
        {
            var comp = _component;
            var responder = comp?.ServerResponder;
            if (responder == null) return;
            try
            {
                // GetPeer(ZRpc) is private in ZNet — resolve the delivering peer reflectively
                // (base game, permitted). This binds the ACTUAL delivering peer, never a claim.
                ZNetPeer? peer = null;
                if (ZNet.instance != null)
                    peer = Traverse.Create(ZNet.instance).Method("GetPeer", new object[] { rpc }).GetValue<ZNetPeer>();
                string deliveringPeerId = peer != null ? peer.m_uid.ToString() : string.Empty;
                long generation = responder.Generation; // current bound generation
                long now = (long)(UnityEngine.Time.realtimeSinceStartupAsDouble * 1000.0);

                // The envelope carries its own claimed generation; the responder validates it
                // against the delivering peer. We pass the claimed generation via the payload's
                // admission path — the responder re-derives peer facts from the transport.
                ControlReceipt receipt = responder.Handle(deliveringPeerId, generation, payload, now);
                string json = EnvelopeCodec.EncodeReceipt(receipt);
                rpc.Invoke(RpcReplyName, json);
            }
            catch (Exception e)
            {
                _log?.LogWarning($"SBPRQA: control handler error: {e.GetType().Name}");
            }
        }

        /// <summary>
        /// Notify the responder a peer bound/unbound so its generation tracks the transport.
        /// Called from the connection/disconnection Harmony hooks.
        /// </summary>
        internal static void NotifyPeerConnected(ZNetPeer peer)
        {
            var responder = _component?.ServerResponder;
            if (responder == null || peer == null) return;
            responder.OnPeerConnected(peer.m_uid.ToString());
        }
    }

    /// <summary>Harmony patch declaration for the private ZNet.OnNewConnection(ZNetPeer) seam.</summary>
    [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
    internal static class ZNetOnNewConnectionPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(ZNetPeer peer)
        {
            QaServerRpcBridge.NotifyPeerConnected(peer);
            QaServerRpcBridge.OnNewConnectionPostfix(peer);
        }
    }
}
