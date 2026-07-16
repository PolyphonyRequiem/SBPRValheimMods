using System;
using System.Collections.Generic;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // T009R4 (Blocker 4) — the engine-free, TESTABLE admin-identity normalization adapter.
    //
    // The blocker: the T009R3 admin gate used raw `GetAdminList().Contains(host)`. Vanilla does NOT match
    // admin identities that way. `ZNet.ListContainsId(SyncedList, idString)` (used by RPC_Save, the same
    // gate we mirror) normalizes the id: it parses the candidate as a PlatformUserID and, when it is on
    // the server's own platform, matches EITHER the full "<platform>:<user>" form OR the bare "<user>"
    // form against the list. A raw Contains(host) misses admins listed in the alternate form, so a real
    // admin can be silently refused (or, worse, a naive equality check elsewhere could be spoofable).
    //
    // This pure adapter reproduces that normalization behaviorally (clean-room: it is a DESCRIPTION of the
    // vanilla rule, re-derived from the base game, not a copy of engine code). The net48 admin seam calls
    // Normalize on the authenticated peer's socket host id and each admin-list entry, then compares
    // normalized forms — so the shipped gate matches vanilla admin semantics exactly. Nothing here is
    // client-authored: both inputs are server-owned (the authenticated socket host and the server's
    // adminlist.txt).
    //
    // net48 audit: System.String only. No net5+ surface, no UnityEngine/Valheim, so it link-compiles into
    // the net8 test project and every branch (platform-qualified, bare, cross-platform) is unit-tested.
    public static class VanillaAdminIdentity
    {
        /// <summary>The server's own platform tag. On a dedicated Steam server this is "Steam"; the seam
        /// passes the live platform so tests can pin cross-platform behavior. Case-sensitive ordinal, as
        /// vanilla platform tags are fixed literals.</summary>
        public const string DefaultPlatform = "Steam";

        /// <summary>Does <paramref name="candidateId"/> (an authenticated socket host id) match any entry
        /// in <paramref name="adminList"/> under vanilla-normalized semantics for <paramref name="serverPlatform"/>?
        /// A candidate on the server's own platform matches EITHER its full "<platform>:<user>" form OR its
        /// bare "<user>" form; a candidate on a different platform matches only its full form. Mirrors
        /// ZNet.ListContainsId. Empty candidate never matches.</summary>
        public static bool ListContainsId(IReadOnlyCollection<string> adminList, string candidateId, string serverPlatform)
        {
            if (adminList == null || adminList.Count == 0) return false;
            if (string.IsNullOrEmpty(candidateId)) return false;
            serverPlatform = string.IsNullOrEmpty(serverPlatform) ? DefaultPlatform : serverPlatform;

            SplitPlatformUser(candidateId, serverPlatform, out string platform, out string user, out string fullForm);

            bool onServerPlatform = string.Equals(platform, serverPlatform, StringComparison.Ordinal);

            foreach (var entry in adminList)
            {
                if (string.IsNullOrEmpty(entry)) continue;
                if (string.Equals(entry, fullForm, StringComparison.Ordinal)) return true;
                if (onServerPlatform && !string.IsNullOrEmpty(user) &&
                    string.Equals(entry, user, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>Parse a candidate id into (platform, user, fullForm) mirroring PlatformUserID.TryParse
        /// then the (m_steamPlatform, idString) fallback: an id containing a ':' is "<platform>:<user>";
        /// otherwise it is a bare user id on the SERVER's platform. fullForm is always "<platform>:<user>".</summary>
        private static void SplitPlatformUser(string candidateId, string serverPlatform,
            out string platform, out string user, out string fullForm)
        {
            int sep = candidateId.IndexOf(':');
            if (sep > 0 && sep < candidateId.Length - 1)
            {
                platform = candidateId.Substring(0, sep);
                user = candidateId.Substring(sep + 1);
            }
            else
            {
                // No platform qualifier → the candidate is a bare user id on the server's own platform.
                platform = serverPlatform;
                user = candidateId;
            }
            fullForm = platform + ":" + user;
        }
    }
}
