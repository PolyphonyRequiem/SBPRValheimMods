using SBPR.Niflheim.HomesteadStones.Adapters.Identity;

namespace SBPR.Niflheim.HomesteadStones.Features.PilotIdentity
{
    /// <summary>
    /// IAP-001 Gate 0 — the net48-ONLY transport adapter that reads the ONE server-observed authenticated
    /// Steam transport fact off a TRANSPORT-AUTHENTICATED <c>ZNetPeer</c> and hands it to the engine-free
    /// <see cref="PilotProviderGate"/>. It is the production edge the Gate-0 acceptance tests prove through
    /// the engine-free gate — this file itself references Valheim (ZNet/ZNetPeer/ISocket), so it is net48-
    /// only and NOT link-compiled into the net8 test project, exactly like
    /// <see cref="Features.Progression.ZdoAuthenticatedSenderSource"/>.
    ///
    /// WHY THIS SEAM (not the routed sender): PR #317 established that the routed
    /// <c>ZRoutedRpc.RoutedRPCData.m_senderPeerID</c> is CLIENT-serialized and never re-validated, so it
    /// is forgeable. The pilot transport principal must therefore be read from a DIRECT per-peer
    /// <c>ZRpc</c> handler, where the server finds the exact <c>ZNetPeer</c> whose <c>m_rpc</c> delivered
    /// the packet (reproduced over the public <c>GetConnectedPeers()</c> table by <c>m_rpc</c> reference
    /// identity — the same match <see cref="Features.Progression.ZdoAuthenticatedSenderSource.PeerForRpc"/>
    /// uses). From that authenticated peer we read ONLY the Steam socket host id
    /// (<c>m_socket.GetHostName()</c>) — the platform/Gate-A subject. Never a client claim, never the
    /// mutable player name, never the reconnect-unstable character ZDOID.
    ///
    /// PROVIDER: Steamworks is the one named pilot backend (see <see cref="PilotProviderGate"/> docs). The
    /// dedicated server this composes against authenticates Steam sockets; a non-Steam host id is rejected
    /// by the gate as ProviderUnsupported. This adapter does NOT create an account, compute an HMAC, or
    /// log the raw subject — Gate 0 is proof-only.
    ///
    /// SUBJECT STABILITY: the host id read here is durable across reconnect and server restart (it is the
    /// authenticated Steam identity, not the per-session peer handle or character ZDOID), so two sessions
    /// resolve to the same canonical subject — the property AT-AIP-PROVIDER-RECONNECT asserts through the
    /// engine-free gate.
    /// </summary>
    internal sealed class ZdoPilotProviderSubjectSource
    {
        private readonly PilotProviderGate _gate;

        internal ZdoPilotProviderSubjectSource(PilotProviderGate gate)
        {
            _gate = gate;
        }

        /// <summary>Resolve the TRUE authenticated peer that delivered a packet on <paramref name="rpc"/>,
        /// reproducing the private <c>ZNet.GetPeer(ZRpc)</c> over the public connected-peers table by
        /// <c>m_rpc</c> reference identity. No client-supplied id is involved.</summary>
        internal static ZNetPeer? PeerForRpc(ZNet znet, ZRpc rpc)
        {
            if (znet == null || rpc == null) return null;
            foreach (var peer in znet.GetConnectedPeers())
            {
                if (peer != null && ReferenceEquals(peer.m_rpc, rpc)) return peer;
            }
            return null;
        }

        /// <summary>Read the ONE server-observed authenticated Steam transport fact off a transport-
        /// authenticated peer: the socket host id. Returns <see cref="ServerObservedTransportSubject.None"/>
        /// when the peer/socket is absent (unauthenticated → the gate rejects). Nothing here is client-
        /// authored: the peer came from ZRpc reference identity, the host off the authenticated socket.</summary>
        internal ServerObservedTransportSubject ObserveTransportSubject(ZNetPeer? peer)
        {
            if (peer == null) return ServerObservedTransportSubject.None;

            var socket = peer.m_socket;
            string? host = socket != null ? socket.GetHostName() : null;
            if (string.IsNullOrEmpty(host)) return ServerObservedTransportSubject.None;

            // The opaque per-peer transport handle (uid) — used only so reconnect stability is asserted
            // against the durable host id, never as the identity itself.
            long transportHandle = peer.m_uid;
            return new ServerObservedTransportSubject(host!, transportHandle);
        }

        /// <summary>The full Gate-0 edge: observe the authenticated peer that delivered <paramref name="rpc"/>
        /// and resolve it through the engine-free gate. Returns the transient verified principal or a stable
        /// rejection. The raw subject is never logged or serialized by this method.</summary>
        internal PilotProviderRejection TryResolve(ZNet znet, ZRpc rpc, out VerifiedProviderPrincipal principal)
        {
            var peer = PeerForRpc(znet, rpc);
            var observed = ObserveTransportSubject(peer);
            return _gate.TryResolve(observed, out principal);
        }
    }
}
