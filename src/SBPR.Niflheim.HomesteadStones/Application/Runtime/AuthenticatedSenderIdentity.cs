using System;
using System.Globalization;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // T009R4 (Blockers 2 + 3) — the engine-free, TESTABLE transport-bound identity model.
    //
    // Two live blockers the T009R3 adversarial review found are reconciled here, in one place:
    //
    //   Blocker 2 (forgeable sender / wrong creator binding). Vanilla stamps a placed piece's creator
    //   with Player.SetCreator(GetPlayerID()); GetPlayerID() reads the placing character's
    //   ZDOVars.s_playerID — a game-minted, per-CHARACTER profile id. The correct authority split is:
    //     * ACCOUNT  = the authenticated platform/socket identity (Gate-A account subject). This is the
    //       account the exclusivity index / authority records are keyed under. It is NOT the piece
    //       creator and never the character.
    //     * CHARACTER = the server-owned s_playerID, rendered into a stable "player:<s_playerID>"
    //       subject. This is what a placed piece's ZDO s_creator is stamped from, so the ingress's
    //       creator==character binding compares two server-derived s_playerID values in ONE space.
    //   The T009R2/R3 code bound the ZDO creator to the ACCOUNT principal and used the character ZDOID
    //   string as the character id. Both are wrong: creator is a character fact, and the ZDOID is not
    //   the account.
    //
    //   Blocker 3 (reconnect stability). The live character ZDOID changes every session; using it as the
    //   durable character subject orphans a character's authority on reconnect/restart. s_playerID is
    //   durable across sessions, renames, and restarts, so "player:<s_playerID>" is the stable character
    //   subject that keeps relationships and receipts bound to the same character forever.
    //
    // The net48 layer (Features/Progression/ZdoAuthenticatedSenderSource.cs) supplies ONLY the two raw
    // server facts — the platform/socket account subject string and the character's s_playerID — read off
    // the server's own ZNetPeer + character ZDO. Everything derived from them lives here so every branch
    // (empty/zero, reconnect stability, account≠character) is unit-tested. No UnityEngine/Valheim here.
    public static class ServerCreatorIdentity
    {
        /// <summary>The shared, server-owned CHARACTER/creator principal space, keyed by the durable
        /// s_playerID. Both a placed ZDO's recorded s_creator and the authenticated sender's character
        /// s_playerID render through this, so the ingress's creator==character comparison is between two
        /// server-derived values in one space. A zero/absent player id renders empty (unbindable →
        /// CreatorMismatch upstream). Stable across reconnect: s_playerID is character-durable.</summary>
        public static string CharacterSubject(long playerId) =>
            playerId == 0L ? string.Empty : "player:" + playerId.ToString(CultureInfo.InvariantCulture);

        /// <summary>Back-compat alias. The placed ZDO's s_creator renders into the SAME space as the
        /// character subject (both are s_playerID), so creator binding is character binding.</summary>
        public static string CreatorPrincipal(long playerId) => CharacterSubject(playerId);
    }

    /// <summary>The server-owned facts about the authenticated sender, read off the server's own peer
    /// table + character ZDO — never a client payload. <see cref="PlayerId"/> is the character ZDO's
    /// ZDOVars.s_playerID (durable; the value vanilla stamps as a placed piece's s_creator).
    /// <see cref="PlatformSubject"/> is the authenticated platform/socket account subject (Gate-A). The
    /// live character ZDOID is deliberately NOT carried here: it is reconnect-unstable and must never be
    /// the durable character subject (Blocker 3).</summary>
    public readonly struct AuthenticatedSenderCharacter
    {
        public AuthenticatedSenderCharacter(long playerId, string platformSubject)
        {
            PlayerId = playerId;
            PlatformSubject = platformSubject ?? string.Empty;
        }

        /// <summary>The character ZDO's server-owned, durable s_playerID (== the placed piece's s_creator).</summary>
        public long PlayerId { get; }

        /// <summary>The authenticated platform/socket account subject (Gate-A account identity). Feeds the
        /// AccountId via the server-owned platform→account resolver; never the piece creator.</summary>
        public string PlatformSubject { get; }

        public static AuthenticatedSenderCharacter None => new AuthenticatedSenderCharacter(0L, string.Empty);
    }

    /// <summary>Server-owned read port resolving a transport-authenticated sender to the server's own
    /// facts about it. The net48 layer implements it over the ACTUAL transport seam (a direct per-peer
    /// ZRpc handler → ZNetPeer → character ZDO → s_playerID + socket host), NOT the forgeable routed
    /// sender id (Blocker 2); tests implement it in-memory. Nothing on it is client-authored.</summary>
    public interface IAuthenticatedSenderCharacterSource
    {
        /// <summary>Resolve one authenticated sender (an opaque transport handle) to its server-owned
        /// character facts. Returns false when the peer is unknown, has no bound character, or the
        /// character ZDO carries no s_playerID (unbindable → the caller rejects rather than crediting).</summary>
        bool TryResolveSender(long transportSenderHandle, out AuthenticatedSenderCharacter character);
    }

    /// <summary>The engine-free binder that turns the two server-owned facts into the (accountSubject,
    /// characterSubject) pair the runtime binds. The account is the platform subject; the character is the
    /// stable "player:<s_playerID>". The placed ZDO's creator is compared against the CHARACTER subject
    /// (both are s_playerID), never the account. Isolated here so every branch is unit-tested.</summary>
    public static class AuthenticatedSenderBinder
    {
        /// <summary>Bind a resolved sender to (accountSubject, characterSubject). Returns false when the
        /// character is unbindable — no s_playerID (0) or no platform account subject — so the caller
        /// rejects instead of comparing empty principals (which would spuriously "match" an empty ZDO
        /// creator). Reconnect-stable: s_playerID is character-durable, so a new session's different peer
        /// handle / character ZDOID still yields the same account + character subjects.</summary>
        public static bool TryBind(AuthenticatedSenderCharacter character, out string accountSubject, out string characterSubject)
        {
            accountSubject = character.PlatformSubject ?? string.Empty;
            characterSubject = ServerCreatorIdentity.CharacterSubject(character.PlayerId);
            return character.PlayerId != 0L
                   && !string.IsNullOrEmpty(accountSubject)
                   && !string.IsNullOrEmpty(characterSubject);
        }
    }
}
