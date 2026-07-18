using System;
using System.Globalization;
using System.IO;
using BepInEx;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T009 — engine-bound composition + startup rehydration for the live Foundational AP runtime. On
    /// the authoritative server (and only there) this builds the durable FoundationalProgressionServer
    /// under a stable, world-scoped server-owned path and arms the placement observer. On a client the
    /// patch no-ops (the observer stays disarmed).
    ///
    /// net48-only (references ZNet/UnityEngine + the ZDO-backed Stone AP sink), so it is not
    /// link-compiled into the net8 test suite; the engine-free FoundationalProgressionServer.Create it
    /// calls IS unit-tested. Rehydration is inherited from Create (the two durable journals replay onto
    /// their projections at construction), so a server restart resumes exactly the persisted state.
    /// </summary>
    [HarmonyPatch]
    internal static class FoundationalRuntimeBootstrap
    {
        private static ZNet? composedFor;

        [HarmonyPatch(typeof(ZNet), "Awake")]
        [HarmonyPostfix]
        private static void OnZNetAwake(ZNet __instance)
        {
            try
            {
                if (__instance == null || !__instance.IsServer()) return;
                if (ReferenceEquals(composedFor, __instance) && FoundationalPlacementObserver.Server != null) return;

                string durableDir = ResolveDurableDirectory(__instance);
                var server = FoundationalProgressionServer.Create(
                    durableDir,
                    familyResolver: ServerHomesteadFamilyResolver.Instance,
                    bondAuthority: ServerHomesteadBondPolicy.Instance,
                    stoneApStore: new ZdoStoneProgressionStore());

                FoundationalPlacementObserver.Server = server;
                composedFor = __instance;
                Plugin.Log.LogInfo(
                    "[Niflheim/HomesteadStones] Foundational live runtime composed (server-authoritative). " +
                    $"durable='{durableDir}' observed={server.Runtime.Log.TotalObserved} " +
                    $"rehydratedReceipts={server.Receipts.DurableOperationIds().Count}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Foundational runtime composition failed: " + ex);
            }
        }

        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        [HarmonyPostfix]
        private static void OnZNetDestroyed(ZNet __instance)
        {
            if (ReferenceEquals(composedFor, __instance))
            {
                composedFor = null;
                FoundationalPlacementObserver.Server = null;
            }
        }

        /// <summary>Stable, world-scoped, server-owned durable directory for the progression journals.
        /// Lives under the BepInEx config root (a writable, server-owned location) keyed by the world's
        /// name + UID so two worlds never share journals and the same world always resolves the same
        /// path across restarts.</summary>
        private static string ResolveDurableDirectory(ZNet znet)
        {
            string worldName = SanitizeSegment(SafeWorldName(znet));
            string uid = znet.GetWorldUID().ToString(CultureInfo.InvariantCulture);
            return Path.Combine(Paths.ConfigPath, "sbpr-niflheim-homestead", worldName + "-" + uid);
        }

        private static string SafeWorldName(ZNet znet)
        {
            try { return znet.GetWorldName() ?? "world"; }
            catch { return "world"; }
        }

        private static string SanitizeSegment(string s)
        {
            if (string.IsNullOrEmpty(s)) return "world";
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(Path.GetInvalidFileNameChars(), chars[i]) >= 0) chars[i] = '_';
            return new string(chars);
        }
    }

    /// <summary>Server-owned per-Stone family classification for the live runtime. Provisional proof
    /// policy: the shipped Homestead Stones are the Settlement/Homestead family. A production build
    /// sources this from the Stone aggregate; kept as a small seam so the engine-free relationship
    /// handler stays pure.</summary>
    internal sealed class ServerHomesteadFamilyResolver : IStoneFamilyResolver
    {
        internal static readonly ServerHomesteadFamilyResolver Instance = new ServerHomesteadFamilyResolver();

        public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
        {
            family = "Settlement";
            variant = "Homestead";
            return true;
        }
    }

    /// <summary>Server-owned Bond authority policy for the live runtime. Provisional proof policy:
    /// authorizes the authored "Homestead:All" Governor range. Never client-authored.</summary>
    internal sealed class ServerHomesteadBondPolicy : IBondAuthorityPolicy
    {
        internal static readonly ServerHomesteadBondPolicy Instance = new ServerHomesteadBondPolicy();

        public bool TryAuthorizeBond(StoneId stoneId, string requestedResponsibilityRange,
            out string grantedRange, out string grantedRole)
        {
            grantedRange = requestedResponsibilityRange ?? string.Empty;
            grantedRole = "Governor";
            return string.Equals(requestedResponsibilityRange, "Homestead:All", StringComparison.Ordinal);
        }
    }
}
