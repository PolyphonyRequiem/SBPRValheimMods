using SBPR.Niflheim.HomesteadStones.Application.Runtime;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T009R3 (Blocker 2) — the net48-ONLY read port that resolves an authenticated routed sender (a
    /// peer uid) into the server's own facts about that sender's CHARACTER, from ZNet's peer table and
    /// ZDOMan. It feeds the engine-free <see cref="AuthenticatedSenderBinder"/>.
    ///
    /// Why this replaces the T009R2 path: T009R2 derived the sender principal from
    /// <c>peer.m_characterID.UserID</c> — the character ZDOID's USER half, i.e. the platform/Steam id.
    /// But vanilla stamps a placed piece's creator with <c>Player.SetCreator(GetPlayerID())</c>, and
    /// <c>GetPlayerID()</c> returns the character ZDO's <c>ZDOVars.s_playerID</c> (a game-minted profile
    /// id), NOT the platform id. Those are different numbers on a real dedicated server, so the old
    /// comparison always mismatched. Here we resolve the sender's character ZDO (<c>peer.m_characterID</c>
    /// via <c>ZDOMan</c>) and read the SAME server-owned <c>s_playerID</c> the piece's creator was stamped
    /// from, plus the STABLE character ZDOID string as the acting character id (never <c>m_playerName</c>,
    /// which is mutable). Reconnect-stable: a new session gives the character a new ZDOID but the same
    /// <c>s_playerID</c>, so the creator principal is unchanged.
    ///
    /// References Valheim (ZNet, ZNetPeer, ZDOMan, ZDO, ZDOID, ZDOVars) → net48-only, not link-compiled.
    /// </summary>
    internal sealed class ZdoAuthenticatedSenderSource : IAuthenticatedSenderCharacterSource
    {
        internal static readonly ZdoAuthenticatedSenderSource Instance = new ZdoAuthenticatedSenderSource();

        public bool TryResolveSender(long senderPeerUid, out AuthenticatedSenderCharacter character)
        {
            character = AuthenticatedSenderCharacter.None;

            var znet = ZNet.instance;
            if (znet == null) return false;

            var peer = znet.GetPeer(senderPeerUid);
            if (peer == null) return false;

            ZDOID characterId = peer.m_characterID;
            if (characterId.IsNone()) return false;

            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return false;

            // The sender's CHARACTER ZDO in the server's own store. Its s_playerID is the exact value
            // vanilla stamped as the placed piece's s_creator (Player.GetPlayerID()).
            var characterZdo = zdoMan.GetZDO(characterId);
            if (characterZdo == null || !characterZdo.IsValid()) return false;

            long playerId = characterZdo.GetLong(ZDOVars.s_playerID, 0L);
            if (playerId == 0L) return false;   // unbindable — no minted profile id yet

            // Stable character id = the character ZDOID string (server-owned, durable across renames).
            character = new AuthenticatedSenderCharacter(playerId, characterId.ToString());
            return true;
        }
    }
}
