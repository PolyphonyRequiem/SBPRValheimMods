using System;
using System.Reflection;
using SBPR.Niflheim.HomesteadStones.Domain;
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
            ok &= AssertMethod(typeof(Heightmap), "GetHeight", new[] { typeof(Vector3), typeof(float).MakeByRefType() });
            ok &= AssertProperty(typeof(WorldGenerator), "instance", isStatic: true);
            ok &= AssertProperty(typeof(ZoneSystem), "LocationsGenerated", isStatic: false);
            ok &= AssertField(typeof(ZoneSystem), "m_locationInstances");

            // R6 (Blocker 1) — the authoritative host-transform read + resident-Stone enumeration seams. The
            // production path resolves host origin/rotation from the LocationProxy ZDO (s_location hash +
            // GetPosition/GetRotation) and enumerates Stones/proxies via ZDOMan, so those exact members must
            // exist. LocationProxy remains a required type (its ZDO is the identity source), but we no longer
            // depend on discovering its live child hierarchy.
            ok &= AssertType(typeof(LocationProxy));
            ok &= AssertMethod(typeof(ZoneSystem), "GetZone", new[] { typeof(Vector3) });
            ok &= AssertMethod(typeof(ZDO), "GetRotation", Type.EmptyTypes);
            ok &= AssertMethod(typeof(ZDO), "GetPosition", Type.EmptyTypes);
            ok &= AssertField(typeof(ZDOVars), "s_location");
            ok &= AssertMethod(typeof(ZDOMan), "GetAllZDOsWithPrefabIterative",
                new[] { typeof(string), typeof(System.Collections.Generic.List<ZDO>), typeof(int).MakeByRefType() });

            // The runtime placement authority is the 13-row authored transform table. The collider catalog is
            // build-time validation evidence only and must never be able to disable gameplay at boot.
            ok &= AssertAuthoredSeatPins();

            Plugin.Log.LogInfo(
                "[Niflheim/HomesteadStones] Runtime drift check: " + (ok ? "all required targets/callsites present." : "FAILED — see errors above; realization disabled until resolved."));
            return ok;
        }

        private static bool AssertAuthoredSeatPins()
        {
            if (HomesteadAuthoredSeatCatalog.Count != 13)
            {
                Plugin.Log.LogError(
                    $"[Niflheim/HomesteadStones] Drift: authored seat catalog has {HomesteadAuthoredSeatCatalog.Count} hosts; expected 13.");
                return false;
            }
            Plugin.Log.LogInfo(
                $"[Niflheim/HomesteadStones] Authored seat pin OK: hosts=13 version='{HomesteadAuthoredSeatCatalog.Version}' " +
                $"digest={HomesteadAuthoredSeatCatalog.ContentHash}.");
            return true;
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
