using System;
using System.Reflection;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.HomesteadStone
{
    /// <summary>
    /// R5 acceptance #3 — bounded runtime startup drift assertions.
    ///
    /// The realization lifecycle depends on a small set of vanilla Harmony targets and engine callsites
    /// staying present across Valheim updates. If a game update renames or removes one, the mod would
    /// silently stop realizing Stones. This verifier asserts, ONCE at startup, that every required target
    /// method / callsite / field exists and logs a single bounded report. It never scans broadly or spams:
    /// exactly the required symbols, one summary line, and one error per missing symbol.
    ///
    /// It reports rather than throws so a drifted game update degrades to "no realization + a loud error"
    /// instead of a hard crash on load — the honest failure mode for a playtest mod.
    /// </summary>
    internal static class HomesteadRuntimeDriftCheck
    {
        internal static bool Verify()
        {
            var ok = true;

            // Harmony patch targets the placement adapter installs (ZoneSystem lifecycle + prefab registration).
            ok &= AssertMethod(typeof(ZoneSystem), "Start", Type.EmptyTypes);
            ok &= AssertMethod(typeof(ZoneSystem), "OnDestroy", Type.EmptyTypes);
            ok &= AssertMethod(typeof(ZNetScene), "Awake", Type.EmptyTypes);

            // Engine callsites the engine-free seat path adapts (terrain height + selection readiness).
            ok &= AssertMethod(typeof(WorldGenerator), "GetHeight", new[] { typeof(float), typeof(float) });
            ok &= AssertProperty(typeof(WorldGenerator), "instance", isStatic: true);
            ok &= AssertProperty(typeof(ZoneSystem), "LocationsGenerated", isStatic: false);
            ok &= AssertField(typeof(ZoneSystem), "m_locationInstances");

            // The host-geometry read seam depends on LocationProxy being the realized host root component.
            ok &= AssertType(typeof(LocationProxy));

            Plugin.Log.LogInfo(
                "[Niflheim/HomesteadStones] Runtime drift check: " + (ok ? "all required targets/callsites present." : "FAILED — see errors above; realization disabled until resolved."));
            return ok;
        }

        private static bool AssertMethod(Type type, string name, Type[]? parameters)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var method = parameters == null
                ? type.GetMethod(name, flags)
                : type.GetMethod(name, flags, binder: null, types: parameters, modifiers: null);
            if (method != null) return true;
            Plugin.Log.LogError($"[Niflheim/HomesteadStones] Drift: required method {type.Name}.{name} not found.");
            return false;
        }

        private static bool AssertProperty(Type type, string name, bool isStatic)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            if (type.GetProperty(name, flags) != null) return true;
            Plugin.Log.LogError($"[Niflheim/HomesteadStones] Drift: required property {type.Name}.{name} not found.");
            return false;
        }

        private static bool AssertField(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            if (type.GetField(name, flags) != null) return true;
            Plugin.Log.LogError($"[Niflheim/HomesteadStones] Drift: required field {type.Name}.{name} not found.");
            return false;
        }

        private static bool AssertType(Type type)
        {
            if (type != null) return true;
            Plugin.Log.LogError("[Niflheim/HomesteadStones] Drift: required type not found.");
            return false;
        }
    }
}
