// Execution-time admin/owner authority recheck against the live server (ADR-0009 §5.1;
// PR #408 §3.4) — M2R. Server fixture/control verbs re-check admin authority at the MOMENT
// of execution, not just at arm. In M2R the only executable verbs are status/ping, so this
// recheck gates even those: a delivering peer must currently be a recognized admin/owner on
// this server. Reads the game's own admin list (ZNet.instance admin check) — a bounded READ,
// no mutation, clean-room permitted (base game).
using System;
using HarmonyLib;
using SBPR.QaHarness.T022.Core.ControlPlane;

namespace SBPR.QaHarness.T022.Runtime
{
    /// <summary>Live admin/owner recheck backed by the server's admin list.</summary>
    internal sealed class ZNetServerAuthorityRecheck : IServerAuthorityRecheck
    {
        public bool IsAuthorized(string deliveringPeerId)
        {
            if (string.IsNullOrEmpty(deliveringPeerId)) return false;
            try
            {
                ZNet znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return false;
                // The delivering peer id is the peer's m_uid (as string). Resolve the peer and
                // check it holds admin authority. ZNet.ListContainsId / m_adminList is the vanilla
                // admin surface; we consult it via the public admin check where available.
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

        // Consult the server's admin list for this peer's social id / host. Vanilla exposes the
        // admin list via ZNet.m_adminList (SyncedList) checked in RPC_ServerHandshake; we read it
        // through the public m_adminList.Contains on the peer's host string when reachable, else
        // fail closed. Kept defensive: any shape drift returns false (no authority granted).
        private static bool IsPeerAdmin(ZNet znet, ZNetPeer peer)
        {
            try
            {
                string? host = peer.m_socket != null ? peer.m_socket.GetHostName() : null;
                if (string.IsNullOrEmpty(host)) return false;
                // m_adminList is a private SyncedList; reach it reflectively (base game, permitted).
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
