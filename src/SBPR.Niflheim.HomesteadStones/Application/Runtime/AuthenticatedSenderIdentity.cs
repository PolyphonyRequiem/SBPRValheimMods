using System;
using System.Globalization;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // T009R3 (Blocker 2) — the engine-free, TESTABLE creator-identity adapter.
    //
    // Why this exists (the T009R2 integration-review defect): vanilla stamps a placed piece's creator
    // with Player.SetCreator(GetPlayerID()), and GetPlayerID() reads the placing character's
    // ZDOVars.s_playerID — a per-character profile id minted by the game, NOT the platform/Steam id.
    // The T009R2 dedicated ingress observer derived the sender principal from peer.m_characterID.UserID
    // (the character ZDOID's USER half — the platform/Steam id), then compared it to the ZDO's recorded
    // s_creator. On a real dedicated server those two are DIFFERENT numbers, so every legitimate
    // placement failed CreatorMismatch and earned zero receipts — the exact live blocker T009L hit.
    //
    // The correct reconciliation is server-owned on BOTH sides:
    //   * the placed piece's ZDO records s_creator = the placing character's s_playerID (a long);
    //   * to bind the authenticated sender, the server resolves that sender's CHARACTER ZDO (from the
    //     peer's m_characterID, server-owned) and reads the SAME server-owned s_playerID off it.
    // Both values are then rendered into ONE principal space here, so the ingress's creator==sender
    // check compares two server-derived s_playerID values that genuinely match for the real creator.
    //
    // This type is pure (no UnityEngine/Valheim), so the whole conversion — including the empty/zero and
    // reconnect (character ZDOID changes, s_playerID stable) cases — is unit-tested. The net48 layer
    // (Features/Progression/ZdoAuthenticatedSenderSource.cs) only supplies the two raw server facts.
    public static class ServerCreatorIdentity
    {
        /// <summary>The shared, server-owned creator/actor principal space. Both the placed ZDO's
        /// recorded s_creator and the authenticated sender's character s_playerID render through this,
        /// so the ingress's creator==sender comparison is between two server-derived values in one space.
        /// A zero/absent player id renders as empty (an unbindable identity → CreatorMismatch upstream).</summary>
        public static string CreatorPrincipal(long playerId) =>
            playerId == 0L ? string.Empty : "player:" + playerId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The two server-owned facts about the authenticated sender's character, read off the
    /// server's own character ZDO — never from a client payload. <see cref="PlayerId"/> is the character
    /// ZDO's ZDOVars.s_playerID (the same value vanilla stamps as a placed piece's s_creator);
    /// <see cref="CharacterZdoId"/> is the STABLE character-ZDOID string used as the acting character id
    /// (never the mutable player display name).</summary>
    public readonly struct AuthenticatedSenderCharacter
    {
        public AuthenticatedSenderCharacter(long playerId, string characterZdoId)
        {
            PlayerId = playerId;
            CharacterZdoId = characterZdoId ?? string.Empty;
        }

        /// <summary>The character ZDO's server-owned s_playerID (== the placed piece's recorded creator).</summary>
        public long PlayerId { get; }

        /// <summary>The STABLE character ZDOID string. Used as the acting character id; never a display name.</summary>
        public string CharacterZdoId { get; }

        public static AuthenticatedSenderCharacter None => new AuthenticatedSenderCharacter(0L, string.Empty);
    }

    /// <summary>Server-owned read port that resolves an authenticated routed sender (a peer uid) to the
    /// server's own facts about that sender's character. The net48 layer implements it over ZNet's peer
    /// table + ZDOMan (peer.m_characterID → character ZDO → s_playerID); tests implement it in-memory.
    /// Nothing on it is client-authored.</summary>
    public interface IAuthenticatedSenderCharacterSource
    {
        /// <summary>Resolve one authenticated sender (routed-RPC peer uid) to its server-owned character
        /// facts. Returns false when the peer is unknown, has no bound character, or the character ZDO
        /// carries no s_playerID (an unbindable sender → the ingress rejects rather than crediting).</summary>
        bool TryResolveSender(long senderPeerUid, out AuthenticatedSenderCharacter character);
    }

    /// <summary>The engine-free binder that turns the two server-owned character facts into the shared
    /// principal + stable character id the dedicated ingress compares against the placed ZDO's creator.
    /// Isolated here (not inside the net48 observer) so every branch is unit-tested.</summary>
    public static class AuthenticatedSenderBinder
    {
        /// <summary>Bind a resolved sender character to (creatorPrincipal, characterId). Returns false
        /// when the character is unbindable — no s_playerID (0) or no stable character ZDOID — so the
        /// caller rejects instead of comparing an empty principal (which would spuriously "match" an
        /// empty ZDO creator). Reconnect-stable: the s_playerID is character-durable, so a new session's
        /// different peer uid / character ZDOID still yields the same creator principal.</summary>
        public static bool TryBind(AuthenticatedSenderCharacter character, out string creatorPrincipal, out string characterId)
        {
            creatorPrincipal = ServerCreatorIdentity.CreatorPrincipal(character.PlayerId);
            characterId = character.CharacterZdoId ?? string.Empty;
            return character.PlayerId != 0L
                   && !string.IsNullOrEmpty(creatorPrincipal)
                   && !string.IsNullOrEmpty(characterId);
        }
    }
}
