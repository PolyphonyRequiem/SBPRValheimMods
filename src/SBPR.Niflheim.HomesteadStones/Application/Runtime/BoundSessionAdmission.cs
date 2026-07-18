using System;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // IAP-007W — the engine-free coupler that WIRES the account+character admission lifecycle
    // (Tracer 1/2: PilotCharacterAdmissionService) to the live gameplay-principal index
    // (BoundSessionPrincipalIndex) the placement observer/ingress resolves against.
    //
    // The gap this closes (PR #338 pre-merge): admission mints and one-session-admits an internal
    // (AccountId, CharacterId, SessionId), and the gameplay hot path already RESOLVES a peer's bound
    // internal principal from BoundSessionPrincipalIndex — but nothing ever PUBLISHED into that index,
    // so on a real server the observer/ingress always failed closed (credited nothing). This type is the
    // one seam that publishes on successful session ACTIVATION and removes on session CLOSE/disconnect.
    //
    // Trust model (task requirement): the peer key is a SERVER-OBSERVED fact — the durable s_playerID the
    // net48 layer reads off the authenticated peer's own character ZDO — NOT a payload identity. The
    // published principal is the BOUND INTERNAL (AccountId, CharacterId) admission minted, never a
    // provider/platform subject.
    //
    // Fail-closed ordering (task requirement): the index is published ONLY after
    // PilotCharacterAdmissionService.ActivateSession returns None (the lease promoted to Active under the
    // exact account/session/transport). A rejected activation publishes NOTHING, so the gameplay path
    // stays failed-closed for an un-activated peer. One-session and stale-disconnect semantics are
    // preserved by delegating to the admission lease (CloseSession) and the session-qualified
    // BoundSessionPrincipalIndex.TryUnbind (a stale close for a superseded session is a no-op).
    //
    // net48 audit: System.* + the engine-free admission/identity value objects only. No
    // UnityEngine/Valheim/BepInEx — link-compiles under net8 and ships under net48.
    public sealed class BoundSessionAdmission
    {
        private readonly PilotCharacterAdmissionService _admission;
        private readonly BoundSessionPrincipalIndex _boundSessions;

        public BoundSessionAdmission(PilotCharacterAdmissionService admission, BoundSessionPrincipalIndex boundSessions)
        {
            _admission = admission ?? throw new ArgumentNullException(nameof(admission));
            _boundSessions = boundSessions ?? throw new ArgumentNullException(nameof(boundSessions));
        }

        /// <summary>Activate a resolved character session and, ONLY on success, publish the bound internal
        /// principal into the live index under the server-owned <paramref name="peerKey"/> (the durable
        /// s_playerID). Fail-closed: a rejected activation (lease mismatch, character-not-owned, account not
        /// admissible) publishes nothing and returns the rejection code, so the gameplay path credits
        /// nothing for an un-activated peer. An empty peer key is refused (unbindable), because the observer
        /// keys the index by that same s_playerID — a session with no server-owned peer key could never be
        /// resolved and must not silently "succeed".</summary>
        public CharacterRejectionCode ActivateAndBind(
            string peerKey, PilotAccountId accountId, SessionId sessionId, long transportHandle, PilotCharacterId characterId)
        {
            if (string.IsNullOrEmpty(peerKey))
                return CharacterRejectionCode.ProfileSubjectInvalid;

            var code = _admission.ActivateSession(accountId, sessionId, transportHandle, characterId);
            if (code != CharacterRejectionCode.None)
                return code;   // fail closed: no bind published for a rejected activation

            var principal = new PilotSessionPrincipal(
                new AccountId(accountId.Value),
                new CharacterId(characterId.Value),
                sessionId.Value);
            _boundSessions.Bind(peerKey, principal);
            return CharacterRejectionCode.None;
        }

        /// <summary>Close a peer's session on disconnect: release the admission lease (only a lease whose
        /// account/session/transport all match is removed — a stale disconnect cannot close a newer
        /// admission) AND remove the live bound principal for the same session under the peer key. The
        /// index removal is session-qualified, so a late disconnect for a superseded session whose id no
        /// longer matches the live bind is a no-op and never tears down a newer reconnect. Returns true iff
        /// this call removed the live bound principal.</summary>
        public bool CloseAndUnbind(
            string peerKey, PilotAccountId accountId, SessionId sessionId, long transportHandle)
        {
            // Release the ephemeral admission lease (matching account/session/transport only).
            _admission.CloseSession(accountId, sessionId, transportHandle);
            // Remove the live bound principal only if THIS session still occupies the peer key.
            return _boundSessions.TryUnbind(peerKey, sessionId.Value);
        }
    }
}
