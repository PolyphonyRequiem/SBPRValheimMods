using System;

namespace SBPR.Niflheim.ProgressionSpike
{
    // Models the identity seam proven by the Gate-A prep matrix (candidate A -> E).
    //
    // The transport (ZRoutedRpc handler, server-side) attributes a peer to each request
    // out-of-band: the `sender` argument is set by the transport from the authenticated
    // socket, NOT from the RPC payload. The client CAN put whatever it likes in the
    // payload's claimed*Id fields; it CANNOT set the transport-attributed sender.
    //
    // This spike reproduces that trust boundary in-process so the property is executable
    // and testable without a live server: `AuthenticatedConnection` is the server-owned
    // truth; `ClaimedPrincipal` is untrusted client payload. The pipeline derives the
    // principal from the connection and only COMPARES the claim -> PrincipalMismatch on
    // drift. Payload can never become authority.

    // Untrusted: comes off the wire inside the command payload.
    public struct ClaimedPrincipal
    {
        public string ClaimedAccountId;
        public string ClaimedCharacterId;
    }

    // Trusted: server derives this from the authenticated socket (platform id, candidate A),
    // optionally indirected through a server-owned platform-id -> AccountId map (candidate E).
    public struct AuthenticatedConnection
    {
        // Stable platform id from m_socket.GetEndPointString() (server-derived, un-forgeable).
        public string PlatformId;
        // Acting character ZDOID observed at command time (peer.m_characterID).
        public string ActingCharacterId;
    }

    // The resolved, authoritative principal the pipeline binds a mutation to.
    public struct AuthoritativePrincipal
    {
        public string AccountId;       // server-issued (E) or platform-id passthrough (A)
        public string CharacterId;     // server-observed acting character
        public string PlatformId;      // audit anchor
    }

    public enum PrincipalResolution
    {
        Bound,                 // authenticated principal resolved; claim matched (or no claim)
        PrincipalMismatch,     // client payload claimed a different account/character
        UnauthenticatedPeer    // no server-attributed connection -> reject, never trust payload
    }

    public sealed class PrincipalResolver
    {
        // Candidate E: server-owned platform-id -> AccountId map. In production this is the
        // R-003 account exclusivity index; here it is an injected fake so the spike stays
        // engine-free. Absent a mapping we fall back to candidate A (platform id as account).
        private readonly Func<string, string> _accountIdForPlatform;

        public PrincipalResolver(Func<string, string> accountIdForPlatform)
        {
            _accountIdForPlatform = accountIdForPlatform;
        }

        public PrincipalResolution Resolve(
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            out AuthoritativePrincipal principal)
        {
            principal = default(AuthoritativePrincipal);

            // A peer with no server-attributed platform id is not authenticated. We reject.
            // We NEVER synthesise identity from the payload claim.
            if (string.IsNullOrEmpty(connection.PlatformId))
                return PrincipalResolution.UnauthenticatedPeer;

            string accountId = _accountIdForPlatform != null
                ? _accountIdForPlatform(connection.PlatformId)
                : connection.PlatformId;
            if (string.IsNullOrEmpty(accountId))
                accountId = connection.PlatformId; // candidate-A fallback

            var resolved = new AuthoritativePrincipal
            {
                AccountId = accountId,
                CharacterId = connection.ActingCharacterId,
                PlatformId = connection.PlatformId
            };

            // The claim is compared, never trusted. A hostile client that fills the payload
            // with someone else's account/character is rejected here (contracts.md:42-43).
            if (!string.IsNullOrEmpty(claim.ClaimedAccountId) &&
                !string.Equals(claim.ClaimedAccountId, resolved.AccountId, StringComparison.Ordinal))
                return PrincipalResolution.PrincipalMismatch;

            if (!string.IsNullOrEmpty(claim.ClaimedCharacterId) &&
                !string.Equals(claim.ClaimedCharacterId, resolved.CharacterId, StringComparison.Ordinal))
                return PrincipalResolution.PrincipalMismatch;

            principal = resolved;
            return PrincipalResolution.Bound;
        }
    }
}
