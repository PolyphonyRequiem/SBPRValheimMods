using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;

namespace SBPR.Niflheim.HomesteadStones.Application.Accounts
{
    // IAP-009 Operator foundation — the live-server operator authority gate (engine-free CLEAN core).
    //
    // Every pilot account lifecycle operation (inspect, disable, delete, reset, retention) reuses the
    // EXISTING authenticated live-server Valheim admin gate; payload identity NEVER grants operator
    // authority (AIP-FR-019; contracts "Operator commands"). This gate makes that rule executable and
    // testable:
    //
    //  * The ONLY input that can authorize an operator command is a SERVER-OBSERVED authenticated admin
    //    host id (the same fact the shipped placement admin seam trusts, normalized by VanillaAdminIdentity
    //    against the server's own adminlist.txt). There is deliberately no constructor/parameter path that
    //    accepts a client-supplied "I am an admin" claim — a remote gameplay payload cannot manufacture
    //    admin authority, so there is NO second admin identity path exposed to gameplay payloads.
    //  * A non-admin authenticated peer is rejected (AT-AIP-NONADMIN-REJECT) with a stable, subject-free
    //    code and causes no mutation.
    //
    // net48 audit: System.Collections.Generic + the engine-free VanillaAdminIdentity. No UnityEngine /
    // Valheim / BepInEx, so it link-compiles into the net8 test project and ships under net48.

    /// <summary>A server-observed operator authorization fact: the authenticated admin-candidate host id
    /// read off the peer's own socket (NOT a payload claim), plus the server's platform tag. This is the
    /// only shape that can carry operator authority into the lifecycle service.</summary>
    public readonly struct ServerObservedAdminContext
    {
        public ServerObservedAdminContext(string authenticatedHostId, string serverPlatform)
        {
            AuthenticatedHostId = authenticatedHostId ?? string.Empty;
            ServerPlatform = string.IsNullOrEmpty(serverPlatform) ? VanillaAdminIdentity.DefaultPlatform : serverPlatform;
        }

        /// <summary>The authenticated socket host id of the operator, server-observed. Transient; never a
        /// client payload claim.</summary>
        public string AuthenticatedHostId { get; }

        /// <summary>The server's own platform tag (e.g. "Steam"), for vanilla-normalized admin matching.</summary>
        public string ServerPlatform { get; }

        public static ServerObservedAdminContext None => new ServerObservedAdminContext(string.Empty, VanillaAdminIdentity.DefaultPlatform);
    }

    /// <summary>The live-admin authority gate. Constructed over the server's own admin list snapshot
    /// (adminlist.txt), it authorizes an operator command ONLY when a server-observed authenticated host
    /// matches an admin entry under vanilla-normalized semantics. The admin list is server-owned; nothing
    /// here trusts a payload.</summary>
    public sealed class OperatorAdminGate
    {
        private readonly IReadOnlyCollection<string> _adminList;

        public OperatorAdminGate(IReadOnlyCollection<string> serverAdminList)
        {
            _adminList = serverAdminList ?? Array.Empty<string>();
        }

        /// <summary>True iff the server-observed authenticated host is a current server admin. An empty
        /// host, an empty admin list, or a non-admin peer all return false (fail closed).</summary>
        public bool IsAuthorizedOperator(ServerObservedAdminContext context) =>
            VanillaAdminIdentity.ListContainsId(_adminList, context.AuthenticatedHostId, context.ServerPlatform);

        /// <summary>Authorize or reject with a stable, subject-free reason. On rejection the caller must
        /// perform NO mutation (AT-AIP-NONADMIN-REJECT).</summary>
        public bool Authorize(ServerObservedAdminContext context, out string rejectionCode)
        {
            if (string.IsNullOrEmpty(context.AuthenticatedHostId))
            {
                rejectionCode = "UnauthenticatedPeer";
                return false;
            }
            if (!IsAuthorizedOperator(context))
            {
                rejectionCode = "NotAdmin";
                return false;
            }
            rejectionCode = string.Empty;
            return true;
        }
    }
}
