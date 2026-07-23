// Engine-bound REAL execution-time authority source (ADR-0009 §5.1, PR #408 §3.1/§3.4) — M3R.
//
// ZNetServerAuthoritySource is the thin Valheim implementation of the engine-free
// IServerAuthoritySource that FixtureAuthority.Recheck consults at the MOMENT a fixture
// lifecycle op runs. It answers three bounded READ questions against the live server, none
// of which mutate anything:
//   • IsServer      — ZNet.instance.IsServer()  (fixtures are a Server-role-only op)
//   • WorldLoaded   — ZNet.World != null        (spawns NRE before world load)
//   • IsAdmin(peer) — the delivering peer currently holds admin/owner authority
//
// Admin resolution mirrors the M2R ZNetServerAuthorityRecheck exactly (the same vanilla
// admin surface, ZNet.m_adminList checked against the peer's host string), so the fixture
// gate and the M2R control gate agree on "who is admin right now". Any shape drift returns
// false (fail-closed — no authority granted). Reading the base game's admin list is
// clean-room permitted (ADR-0001); no other-mod source is used.
using System;
using HarmonyLib;
using SBPR.QaHarness.T022.Core.ControlPlane;
using SBPR.QaHarness.T022.Core.Fixtures;

namespace SBPR.QaHarness.T022.Runtime
{
    /// <summary>Live execution-time authority facts for the fixture recheck (observed-only, fail-closed).</summary>
    internal sealed class ZNetServerAuthoritySource : IServerAuthoritySource
    {
        /// <summary>True iff this process is the authoritative server right now.</summary>
        public bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        /// <summary>True iff a world is loaded (fixtures NRE before world load).</summary>
        public bool WorldLoaded => ZNet.instance != null && ZNet.World != null;

        /// <summary>
        /// True iff the delivering peer currently holds admin/owner authority. Re-read at execution
        /// time (never cached from arm). Resolves the peer by its m_uid, checks the server's admin
        /// list against the peer's host string. Fail-closed on any drift/error.
        /// </summary>
        public bool IsAdmin(string deliveringPeerId)
        {
            if (string.IsNullOrEmpty(deliveringPeerId)) return false;
            try
            {
                ZNet znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return false;
                if (!long.TryParse(deliveringPeerId, out long uid)) return false;
                ZNetPeer peer = znet.GetPeer(uid);
                if (peer == null || !peer.IsReady()) return false;
                return IsPeerAdmin(znet, peer);
            }
            catch (Exception)
            {
                return false; // fail closed
            }
        }

        // Consult the server's admin list for this peer's host string (same surface as the M2R
        // control-plane admin recheck). m_adminList is a private SyncedList; reach it reflectively
        // (base game, permitted). Any shape drift returns false (no authority granted).
        private static bool IsPeerAdmin(ZNet znet, ZNetPeer peer)
        {
            try
            {
                string? host = peer.m_socket != null ? peer.m_socket.GetHostName() : null;
                if (string.IsNullOrEmpty(host)) return false;
                var adminList = Traverse.Create(znet).Field("m_adminList").GetValue();
                if (adminList == null) return false;
                var contains = Traverse.Create(adminList).Method("Contains", new object[] { host! });
                if (!contains.MethodExists()) return false;
                object result = contains.GetValue(host!);
                return result is bool b && b;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
