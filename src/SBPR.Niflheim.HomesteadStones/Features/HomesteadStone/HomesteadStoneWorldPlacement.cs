using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Domain;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Features.Progression;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.HomesteadStone
{
    /// <summary>
    /// Server-authoritative runtime adapter. It computes one stable global assignment only after
    /// Location generation completes, then periodically realizes selected Stones whose zones are
    /// currently loaded. Unloaded selections remain deferred and are revisited as the active area moves.
    /// </summary>
    [HarmonyPatch]
    internal static class HomesteadStoneWorldPlacement
    {
        private const string SelectorVersion = "niflheim-homestead-playtest-v1";
        private const float MinimumDistance = 128f;
        private const double Density = 0.40;
        private const float RecheckSeconds = 5f;
        /// <summary>Max XZ distance a LocationProxy may sit from the assignment XZ to be treated as this
        /// candidate's realized host root (locations are placed within a 64 m zone; a small radius is ample).</summary>
        private const float LiveHostMatchRadius = 24f;
        private static readonly HashSet<string> EligibleHosts = new HashSet<string>(
            Enumerable.Range(1, 13).Select(index => "WoodHouse" + index)
                .Concat(new[] { "WoodFarm1", "WoodVillage1" }),
            StringComparer.Ordinal);
        private static ZoneSystem? scheduledFor;

        /// <summary>Durable per-world event provenance ledger. Rehydrated from the world ZDO on assignment so
        /// a fresh-world failure is a terminal fact across restarts (no phantom retries) — never session-only.</summary>
        private static HomesteadWorldLedger Ledger = new HomesteadWorldLedger();

        /// <summary>Versioned generator-host seat manifest (Approach C). Empty until an operator scan supplies
        /// rows; generator hosts skip explicitly (ManifestRequired) until then. No runtime geometry guessing.</summary>
        private static HomesteadGeneratorManifest Manifest = HomesteadGeneratorManifest.Empty;

        [HarmonyPatch(typeof(ZoneSystem), "Start")]
        [HarmonyPostfix]
        private static void OnZoneSystemStart(ZoneSystem __instance)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ReferenceEquals(scheduledFor, __instance)) return;
            scheduledFor = __instance;
            __instance.StartCoroutine(PlacementLoop(__instance));
        }

        [HarmonyPatch(typeof(ZoneSystem), "OnDestroy")]
        [HarmonyPostfix]
        private static void OnZoneSystemDestroyed(ZoneSystem __instance)
        {
            if (ReferenceEquals(scheduledFor, __instance)) scheduledFor = null;
        }

        private static System.Collections.IEnumerator PlacementLoop(ZoneSystem zoneSystem)
        {
            while (!zoneSystem.LocationsGenerated)
                yield return new WaitForSeconds(1f);

            var worldIdentity = ResolveWorldIdentity();
            Ledger = HomesteadLedgerStore.Load(worldIdentity);
            var instances = BuildCandidates(zoneSystem);
            var selection = HomesteadSelector.Select(
                instances.Select(candidate => candidate.Domain).ToList(),
                new HomesteadSelectionConfig(worldIdentity, SelectorVersion, MinimumDistance, Density));
            var byIdentity = instances.ToDictionary(candidate => candidate.Identity, candidate => candidate);

            foreach (var warning in selection.Warnings)
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Selector target warning: " + warning);
            Plugin.Log.LogInfo(
                $"[Niflheim/HomesteadStones] Assignment ready world='{worldIdentity}' candidates={instances.Count} " +
                $"selected={selection.Selected.Count} minimumDistance={MinimumDistance:0}m density={Density:P0} selector={SelectorVersion}.");

            while (ReferenceEquals(ZoneSystem.instance, zoneSystem))
            {
                PlaceLoaded(zoneSystem, worldIdentity, selection.Selected, byIdentity);
                ReconcileStoneAreas(worldIdentity);
                yield return new WaitForSeconds(RecheckSeconds);
            }
        }

        /// <summary>T009R4 (Blocker 1) — server-authoritatively (re)register the Homestead Stone Areas from
        /// the REAL resident/persisted Stone ZDOs into the live runtime's membership. Without this the
        /// membership stays empty and every placement resolves OutsideStoneArea, so nothing can ever be
        /// credited. Each resident Stone ZDO carries the host zone (its stable StoneId inputs) and a world
        /// position (the Area center); the engine-free <see cref="StoneAreaRegistrar"/> reconciles the
        /// membership to exactly the current resident set (add / move / remove). Idempotent per tick.</summary>
        private static void ReconcileStoneAreas(string worldIdentity)
        {
            var server = FoundationalPlacementObserver.Server;
            if (server == null) return;   // not the composed authoritative server
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return;

            var world = new WorldId(worldIdentity);
            var facts = new List<StoneAreaRegistrar.StoneAreaFact>();

            var found = new List<ZDO>();
            var index = 0;
            while (!zdoMan.GetAllZDOsWithPrefabIterative(HomesteadStoneRegistrar.PrefabName, found, ref index)) { }
            foreach (var zdo in found)
            {
                if (zdo == null || !zdo.IsValid()) continue;
                int zoneX = zdo.GetInt(HomesteadStoneData.LocationZoneXKey, int.MinValue);
                int zoneZ = zdo.GetInt(HomesteadStoneData.LocationZoneZKey, int.MinValue);
                if (zoneX == int.MinValue || zoneZ == int.MinValue) continue;   // unkeyed → no Area

                // Only bind Areas for Stones belonging to THIS world identity (server-owned fact).
                string zdoWorld = zdo.GetString(HomesteadStoneData.WorldIdentityKey, string.Empty);
                if (!string.IsNullOrEmpty(zdoWorld) &&
                    !string.Equals(zdoWorld, worldIdentity, StringComparison.Ordinal)) continue;

                var stoneId = StoneId.FromHostZone(world, zoneX, zoneZ);
                Vector3 pos = zdo.GetPosition();
                facts.Add(new StoneAreaRegistrar.StoneAreaFact(
                    stoneId, pos.x, pos.z, Domain.StoneProgression.StoneAreaMembership.DefaultAreaRadius));
            }

            var result = StoneAreaRegistrar.Reconcile(server.StoneAreas, facts);
            if (result.Registered > 0 || result.Updated > 0 || result.Unregistered > 0)
                Plugin.Log.LogInfo("[Niflheim/HomesteadStones] " + result.ToString());
        }

        private static List<RuntimeCandidate> BuildCandidates(ZoneSystem zoneSystem) =>
            zoneSystem.m_locationInstances
                .Where(pair => pair.Value.m_location != null && EligibleHosts.Contains(pair.Value.m_location.m_prefabName))
                .Select(pair => new RuntimeCandidate(
                    pair.Key,
                    new HomesteadCandidate(
                        pair.Value.m_location.m_prefabName,
                        pair.Key.x,
                        pair.Key.y,
                        pair.Value.m_position.x,
                        pair.Value.m_position.z,
                        Math.Max(2f, pair.Value.m_location.m_exteriorRadius))))
                .ToList();

        private static void PlaceLoaded(
            ZoneSystem zoneSystem,
            string worldIdentity,
            IReadOnlyList<HomesteadCandidate> selected,
            IReadOnlyDictionary<string, RuntimeCandidate> byIdentity)
        {
            var prefab = ZNetScene.instance?.GetPrefab(HomesteadStoneRegistrar.PrefabName);
            if (prefab == null) return;

            var selectedMetadata = selected.ToDictionary(
                candidate => ZoneKey(new Vector2i(candidate.ZoneX, candidate.ZoneZ)),
                candidate => new HomesteadAssignmentMetadata(
                    worldIdentity, SelectorVersion, candidate.Prefab, candidate.ZoneX, candidate.ZoneZ),
                StringComparer.Ordinal);
            var existing = ReconcileExisting(selectedMetadata);
            foreach (var candidate in selected)
            {
                var runtime = byIdentity[Identity(candidate)];
                var key = ZoneKey(runtime.Zone);
                if (existing.Contains(key) || !zoneSystem.IsZoneLoaded(runtime.Zone)) continue;

                // Event gate (assumption audit): a same-version terminal outcome already recorded for this
                // host zone must NOT be re-attempted — this is what prevents counter-only phantom retries
                // after vanilla has set its generated flag.
                if (Ledger.IsTerminal(candidate.ZoneX, candidate.ZoneZ, SelectorVersion)) continue;

                HomesteadResolution resolution;
                try
                {
                    resolution = ResolveSeat(candidate, worldIdentity);
                }
                catch (Exception exception)
                {
                    // Every event outcome including exceptions is captured (durable, terminal until a version
                    // change) — never swallowed into a silent retry.
                    Ledger.Record(candidate.ZoneX, candidate.ZoneZ, HomesteadEventOutcome.Exception, SelectorVersion, exception.Message);
                    PersistLedger();
                    Plugin.Log.LogError(
                        $"[Niflheim/HomesteadStones] Exception resolving {candidate.Prefab} zone " +
                        $"({candidate.ZoneX},{candidate.ZoneZ}): {exception}");
                    continue;
                }

                if (!resolution.IsResolved)
                {
                    Ledger.Record(candidate.ZoneX, candidate.ZoneZ, MapOutcome(resolution.Status), SelectorVersion, resolution.Detail);
                    PersistLedger();
                    Plugin.Log.LogWarning(
                        $"[Niflheim/HomesteadStones] {candidate.Prefab} zone ({candidate.ZoneX},{candidate.ZoneZ}) " +
                        $"unresolved: {resolution.Status} — {resolution.Detail}");
                    continue;
                }

                var record = resolution.Record!;
                var position = new Vector3((float)record.SeatX, (float)record.SeatY, (float)record.SeatZ);

                // The registered template stays activeSelf=true under an inactive holder. Never mutate it:
                // ZNetScene reconstruction needs an active template so ZNetView.Awake consumes m_initZDO.
                var instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
                instance.name = HomesteadStoneRegistrar.PrefabName;
                instance.transform.SetParent(null, true);
                var metadata = selectedMetadata[key];
                if (!StampIdentity(instance.GetComponent<ZNetView>(), metadata))
                {
                    if (ZNetScene.instance != null) ZNetScene.instance.Destroy(instance);
                    else UnityEngine.Object.Destroy(instance);
                    Plugin.Log.LogError(
                        $"[Niflheim/HomesteadStones] Destroyed unkeyed Stone at zone ({runtime.Zone.x},{runtime.Zone.y}); " +
                        "ZNetView/ZDO identity stamping failed.");
                    continue;
                }

                existing.Add(key);
                Ledger.Record(candidate.ZoneX, candidate.ZoneZ, HomesteadEventOutcome.Created, SelectorVersion,
                    record.Provider.ToString());
                PersistLedger();
                Plugin.Log.LogInfo(
                    $"[Niflheim/HomesteadStones] Placed {candidate.Prefab} zone ({candidate.ZoneX},{candidate.ZoneZ}) " +
                    $"provider={record.Provider} radial={record.RadialFromHost:0.00}m " +
                    $"seat=({position.x:0.00},{position.y:0.00},{position.z:0.00}).");
            }
        }

        /// <summary>Resolve a seat for one selected host, engine-free. Ordinary hosts read the LIVE host
        /// root's AUTHORED static colliders (no Physics scene) + its realized rotation, then score seats
        /// analytically with terrain Y from <see cref="WorldGenerator"/> pure noise. Generator hosts route
        /// exclusively through the versioned manifest. NO Physics.*, NO live Heightmap here.</summary>
        private static HomesteadResolution ResolveSeat(HomesteadCandidate candidate, string worldIdentity)
        {
            if (HomesteadHostClassifier.IsGenerator(candidate.Prefab))
            {
                // Generator hosts: manifest-only. Content hash of the live host (empty geometry ⇒ empty hash),
                // matched against the versioned manifest row. No matching row ⇒ ManifestRequired (explicit skip).
                var genGeometry = ReadLiveHostGeometry(candidate);
                var contentHash = genGeometry?.SemanticHash ?? HomesteadGeometryHash.Compute(System.Array.Empty<StaticColliderFootprint>());
                return HomesteadPlacementResolver.ResolveGenerator(
                    worldIdentity, SelectorVersion, candidate, contentHash, Manifest);
            }

            var geometry = ReadLiveHostGeometry(candidate);
            if (geometry == null)
                return HomesteadResolution.Fail(HomesteadResolutionStatus.GeometryUnavailable,
                    $"{candidate.Prefab} ({candidate.ZoneX},{candidate.ZoneZ}) live host root not found in loaded zone.");

            var yaw = ReadLiveHostYaw(candidate);
            return HomesteadPlacementResolver.ResolveOrdinary(
                worldIdentity, SelectorVersion, candidate, geometry, yaw, WorldGenHeight);
        }

        /// <summary>Locate the live host root GameObject in the loaded zone and read its authored static
        /// collider footprints (SPIKE-2 Approach A). Returns null if the host instance is not present.</summary>
        private static HomesteadHostGeometry? ReadLiveHostGeometry(HomesteadCandidate candidate)
        {
            var host = FindLiveHostRoot(candidate);
            if (host == null) return null;
            return HomesteadHostGeometryProvider.FromHostRoot(candidate.Prefab, host, host.transform.rotation);
        }

        private static double ReadLiveHostYaw(HomesteadCandidate candidate)
        {
            var host = FindLiveHostRoot(candidate);
            // The footprints are de-rotated into the host-local frame by the provider, so the resolver must
            // re-apply the SAME realized yaw. Reading it from the live root keeps geometry + seat consistent.
            return host == null ? 0.0 : host.transform.rotation.eulerAngles.y * System.Math.PI / 180.0;
        }

        /// <summary>Find the realized host root instance for a candidate by matching its LocationProxy/root
        /// near the location XZ within the loaded zone. Reads live transform + authored components only.</summary>
        private static GameObject? FindLiveHostRoot(HomesteadCandidate candidate)
        {
            var znetScene = ZNetScene.instance;
            if (znetScene == null) return null;
            var center = new Vector3((float)candidate.X, 0f, (float)candidate.Z);
            LocationProxy? best = null;
            var bestDistanceSq = float.MaxValue;
            foreach (var proxy in UnityEngine.Object.FindObjectsByType<LocationProxy>(FindObjectsSortMode.None))
            {
                if (proxy == null) continue;
                var position = proxy.transform.position;
                var dx = position.x - center.x;
                var dz = position.z - center.z;
                var distanceSq = (dx * dx) + (dz * dz);
                if (distanceSq < bestDistanceSq && distanceSq <= LiveHostMatchRadius * LiveHostMatchRadius)
                {
                    bestDistanceSq = distanceSq;
                    best = proxy;
                }
            }
            return best == null ? null : best.gameObject;
        }

        /// <summary>Pure world-generation height (WorldGenerator noise — headless-safe, no Heightmap GameObject).
        /// Adapts the engine <see cref="WorldGenerator"/> to the engine-free <see cref="WorldHeightFunction"/>.</summary>
        private static bool WorldGenHeight(double worldX, double worldZ, out double height)
        {
            height = 0.0;
            var generator = WorldGenerator.instance;
            if (generator == null) return false;
            var value = generator.GetHeight((float)worldX, (float)worldZ);
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            height = value;
            return true;
        }

        private static HomesteadEventOutcome MapOutcome(HomesteadResolutionStatus status) => status switch
        {
            HomesteadResolutionStatus.NoValidSeat => HomesteadEventOutcome.NoValidSeat,
            HomesteadResolutionStatus.ManifestRequired => HomesteadEventOutcome.ManifestRequired,
            HomesteadResolutionStatus.GeometryUnavailable => HomesteadEventOutcome.GeometryUnavailable,
            _ => HomesteadEventOutcome.Exception,
        };

        private static void PersistLedger() => HomesteadLedgerStore.Save(Ledger);

        private static HashSet<string> ReconcileExisting(
            IReadOnlyDictionary<string, HomesteadAssignmentMetadata> selected)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return result;
            var found = new List<ZDO>();
            var index = 0;
            while (!zdoMan.GetAllZDOsWithPrefabIterative(HomesteadStoneRegistrar.PrefabName, found, ref index)) { }
            foreach (var zdo in found)
            {
                var x = zdo.GetInt(HomesteadStoneData.LocationZoneXKey, int.MinValue);
                var z = zdo.GetInt(HomesteadStoneData.LocationZoneZKey, int.MinValue);
                var key = ZoneKey(new Vector2i(x, z));
                var actual = new HomesteadAssignmentMetadata(
                    zdo.GetString(HomesteadStoneData.WorldIdentityKey, string.Empty),
                    zdo.GetString(HomesteadStoneData.SelectorVersionKey, string.Empty),
                    zdo.GetString(HomesteadStoneData.HostPrefabKey, string.Empty),
                    x,
                    z);
                if (x != int.MinValue && z != int.MinValue &&
                    selected.TryGetValue(key, out var expected) && expected.Matches(actual))
                {
                    result.Add(key);
                    continue;
                }

                // This build is explicitly pre-ratification: selector/config changes may reroll the
                // disposable playtest world, so stale assignments must not accumulate as a union.
                if (!zdo.IsOwner()) zdo.SetOwner(ZDOMan.GetSessionID());
                zdoMan.DestroyZDO(zdo);
                Plugin.Log.LogWarning($"[Niflheim/HomesteadStones] Removed stale assignment ZDO at ({x},{z}).");
            }
            return result;
        }

        private static bool StampIdentity(ZNetView? networkView, HomesteadAssignmentMetadata metadata)
        {
            if (networkView == null) return false;
            var zdo = networkView.GetZDO();
            if (zdo == null) return false;
            if (!networkView.IsOwner()) networkView.ClaimOwnership();
            zdo.Set(HomesteadStoneData.LocationZoneXKey, metadata.ZoneX);
            zdo.Set(HomesteadStoneData.LocationZoneZKey, metadata.ZoneZ);
            zdo.Set(HomesteadStoneData.WorldIdentityKey, metadata.WorldIdentity);
            zdo.Set(HomesteadStoneData.SelectorVersionKey, metadata.SelectorVersion);
            zdo.Set(HomesteadStoneData.HostPrefabKey, metadata.Prefab);
            return zdo.GetInt(HomesteadStoneData.LocationZoneXKey, int.MinValue) == metadata.ZoneX &&
                   zdo.GetInt(HomesteadStoneData.LocationZoneZKey, int.MinValue) == metadata.ZoneZ &&
                   zdo.GetString(HomesteadStoneData.WorldIdentityKey, string.Empty) == metadata.WorldIdentity &&
                   zdo.GetString(HomesteadStoneData.SelectorVersionKey, string.Empty) == metadata.SelectorVersion &&
                   zdo.GetString(HomesteadStoneData.HostPrefabKey, string.Empty) == metadata.Prefab;
        }

        private static string ResolveWorldIdentity() =>
            ZNet.instance == null ? "unknown-world" : HomesteadWorldIdentity.FromUid(ZNet.instance.GetWorldUID());

        private static string Identity(HomesteadCandidate candidate) =>
            candidate.Prefab + ":" + candidate.ZoneX + ":" + candidate.ZoneZ;
        private static string ZoneKey(Vector2i zone) => zone.x + ":" + zone.y;

        private sealed class RuntimeCandidate
        {
            internal RuntimeCandidate(Vector2i zone, HomesteadCandidate domain)
            {
                Zone = zone;
                Domain = domain;
            }
            internal Vector2i Zone { get; }
            internal HomesteadCandidate Domain { get; }
            internal string Identity => HomesteadStoneWorldPlacement.Identity(Domain);
        }
    }
}
