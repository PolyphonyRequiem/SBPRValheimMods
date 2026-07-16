using SBPR.Niflheim.HomesteadStones.Application.Runtime;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T009R4 (Blocker 2) — the net48-ONLY read port that resolves a TRANSPORT-AUTHENTICATED sender —
    /// the actual <c>ZNetPeer</c> that delivered the packet on its own <c>ZRpc</c> — into the server's own
    /// facts about that sender's CHARACTER + ACCOUNT. It feeds the engine-free
    /// <see cref="AuthenticatedSenderBinder"/>.
    ///
    /// Why this replaces the routed-sender path (the T009R3 adversarial-review Blocker 2): vanilla
    /// <c>ZRoutedRpc.RoutedRPCData.m_senderPeerID</c> is serialized by the CLIENT and <c>RPC_RoutedRPC</c>
    /// never rewrites/validates it against the incoming <c>ZRpc</c>, so a routed handler's <c>long sender</c>
    /// is forgeable. High-value placement/provisioning authority must therefore be driven from a DIRECT
    /// per-peer <c>ZRpc</c> handler, where the server receives the real transport <c>ZRpc</c> and can find
    /// the exact <c>ZNetPeer</c> whose <c>m_rpc</c> delivered the packet (vanilla's own
    /// <c>ZNet.GetPeer(ZRpc)</c> seam). From that authenticated peer we read:
    ///   * ACCOUNT   = the authenticated socket host id (<c>m_socket.GetHostName()</c>), the platform/Gate-A
    ///     account subject (candidate A: platform id as account). Never a client claim.
    ///   * CHARACTER = the server-owned <c>ZDOVars.s_playerID</c> off the peer's character ZDO — the durable,
    ///     reconnect-stable id vanilla stamps as a placed piece's <c>s_creator</c>. Never the live character
    ///     ZDOID (which changes on reconnect — Blocker 3) and never the mutable player name.
    ///
    /// References Valheim (ZNet, ZNetPeer, ISocket, ZDOMan, ZDO, ZDOID, ZDOVars) → net48-only, not
    /// link-compiled into net8. The engine-free binder it feeds is fully unit-tested.
    /// </summary>
    internal sealed class ZdoAuthenticatedSenderSource
    {
        internal static readonly ZdoAuthenticatedSenderSource Instance = new ZdoAuthenticatedSenderSource();

        /// <summary>Resolve the TRUE authenticated peer that delivered a packet on <paramref name="rpc"/>.
        /// Vanilla's <c>ZNet.GetPeer(ZRpc)</c> is private; we reproduce it over the public
        /// <c>GetConnectedPeers()</c> table by matching <c>m_rpc</c> reference identity — the same
        /// transport-bound match, with no client-supplied id involved.</summary>
        internal static ZNetPeer? PeerForRpc(ZNet znet, ZRpc rpc)
        {
            if (znet == null || rpc == null) return null;
            foreach (var peer in znet.GetConnectedPeers())
            {
                if (peer != null && ReferenceEquals(peer.m_rpc, rpc)) return peer;
            }
            return null;
        }

        /// <summary>Read the server-owned character + account facts off a TRANSPORT-AUTHENTICATED peer.
        /// Returns false when the peer has no bound character, no resident character ZDO, no minted
        /// s_playerID, or no socket host id — any of which is unbindable (the caller then rejects rather
        /// than crediting). Nothing here is client-authored: the peer came from ZRpc reference identity,
        /// the s_playerID off the server's own ZDO, the account off the authenticated socket.</summary>
        internal bool TryResolveFromPeer(ZNetPeer? peer, out AuthenticatedSenderCharacter character)
        {
            character = AuthenticatedSenderCharacter.None;
            if (peer == null) return false;

            // ACCOUNT: the authenticated socket host id (platform/Gate-A account subject).
            var socket = peer.m_socket;
            string? host = socket != null ? socket.GetHostName() : null;
            if (string.IsNullOrEmpty(host)) return false;

            // CHARACTER: the peer's character ZDO in the server's own store → durable s_playerID.
            ZDOID characterId = peer.m_characterID;
            if (characterId.IsNone()) return false;

            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return false;
            var characterZdo = zdoMan.GetZDO(characterId);
            if (characterZdo == null || !characterZdo.IsValid()) return false;

            long playerId = characterZdo.GetLong(ZDOVars.s_playerID, 0L);
            if (playerId == 0L) return false;   // unbindable — no minted profile id yet

            character = new AuthenticatedSenderCharacter(playerId, host!);
            return true;
        }
    }
}
