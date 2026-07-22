// Engine-bound game adapters (ADR-0009 §3, PR #408 VANILLA-BINDINGS.md §3.1/§3.2/§3.4) — M2R.
//
// These are the THIN Valheim/Unity implementations of the engine-free seam interfaces the
// control-plane core (Core.ControlPlane) declares. They contain the only lines in the M2R
// slice that touch the game, and each is a bounded READ or a main-thread scheduling hook —
// none mint/sign/grant product state (ADR-0009 §4 firewall). Built from PR #408's behavioral
// description (CLEAN side, from the spec, not from decompiled source).
//
// Reaching the game's own public API (ZNet.GetWorldUID/Name, ZNet.IsServer) is clean-room
// permitted: the wall is around OTHER mods, not the base game we mod (ADR-0001).
using System;
using System.Reflection;
using SBPR.QaHarness.T022.Core.ControlPlane;
using UnityEngine;

namespace SBPR.QaHarness.T022.Runtime
{
    /// <summary>
    /// Live world identity from ZNet public API (PR #408 §3.1). Null-guards ZNet.instance /
    /// ZNet.World so a pre-world-load read returns WorldLoaded=false rather than NREing.
    /// Observed-only — never mutates world state.
    /// </summary>
    internal sealed class ZNetWorldIdentitySource : IWorldIdentitySource
    {
        public bool WorldLoaded => ZNet.instance != null && ZNet.World != null;

        public long WorldUid => WorldLoaded ? ZNet.World.m_uid : 0L;

        public string? WorldName => WorldLoaded ? ZNet.World.m_name : null;

        public bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();
    }

    /// <summary>
    /// The helper's OWN main-thread scheduler (PR #408 §3.2) — a bounded action queue drained
    /// on the helper component's Update tick. It acquires NO game console/ScriptTools/ValBridge
    /// lock (ADR-0009 §5.2); it is a plain in-memory queue guarded only for the socket→main
    /// handoff. The clock is Unity's realtime-since-startup in ms.
    /// </summary>
    internal sealed class HelperMainThreadScheduler : IMainThreadScheduler
    {
        private readonly System.Collections.Generic.List<Action> _queue = new();

        public long NowUnixMs => (long)(Time.realtimeSinceStartupAsDouble * 1000.0);

        public void Post(Action action)
        {
            if (action == null) return;
            lock (_queue) { _queue.Add(action); }
        }

        /// <summary>Drain every queued action on the main thread (called from the component Update).</summary>
        public int Drain()
        {
            int n = 0;
            while (true)
            {
                Action next;
                lock (_queue)
                {
                    if (_queue.Count == 0) break;
                    next = _queue[0];
                    _queue.RemoveAt(0);
                }
                try { next(); } catch (Exception) { /* one bad continuation must not wedge the pump */ }
                n++;
            }
            return n;
        }
    }

    /// <summary>
    /// Reads the LIVE assembly_valheim module identity for the drift guard (PR #408 §1). MVID
    /// comes from the module; the version constants from the Version type's public fields.
    /// Pure reflection over the game we mod (clean-room permitted).
    /// </summary>
    internal static class GameAssemblyProbe
    {
        public static ObservedGameAssembly? Read()
        {
            try
            {
                Assembly asm = typeof(ZNet).Assembly;
                Guid mvid = asm.ManifestModule.ModuleVersionId;
                string version = ReadVersionString();
                uint net = ReadNetworkVersion();
                return new ObservedGameAssembly(mvid, version, net);
            }
            catch (Exception)
            {
                return null; // guard fails closed on a null observation
            }
        }

        private static string ReadVersionString()
        {
            try
            {
                // Version.GetVersionString() is the public vanilla accessor; fall back to the
                // CurrentVersion field if the accessor shape ever changes.
                var t = typeof(ZNet).Assembly.GetType("Version");
                if (t != null)
                {
                    var m = t.GetMethod("GetVersionString", BindingFlags.Public | BindingFlags.Static);
                    if (m != null)
                    {
                        var s = m.Invoke(null, null) as string;
                        if (!string.IsNullOrEmpty(s)) return s!;
                    }
                }
            }
            catch (Exception) { /* fall through */ }
            return string.Empty;
        }

        private static uint ReadNetworkVersion()
        {
            try
            {
                var t = typeof(ZNet).Assembly.GetType("Version");
                var f = t?.GetField("m_networkVersion", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (f != null)
                {
                    object? v = f.GetValue(null);
                    if (v is uint u) return u;
                    if (v is int i) return (uint)i;
                }
            }
            catch (Exception) { /* fall through */ }
            return 0u;
        }
    }
}
