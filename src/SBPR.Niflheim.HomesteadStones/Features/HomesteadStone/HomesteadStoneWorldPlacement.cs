using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Domain;
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
        private const int SeatAttempts = 8;
        private const float SeatKeepOut = 1.75f;
        private const float RecheckSeconds = 5f;
        private static readonly HashSet<string> EligibleHosts = new HashSet<string>(
            Enumerable.Range(1, 13).Select(index => "WoodHouse" + index)
                .Concat(new[] { "WoodFarm1", "WoodVillage1" }),
            StringComparer.Ordinal);
        private static readonly int CollisionMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
        private static ZoneSystem? scheduledFor;

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
                yield return new WaitForSeconds(RecheckSeconds);
            }
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

                var seats = HomesteadSeatGenerator.Generate(worldIdentity, SelectorVersion, candidate, SeatAttempts);
                var seat = HomesteadSeatGenerator.ChooseBest(seats, candidateSeat => EvaluateSeat(candidate, candidateSeat));
                if (!seat.HasSeat)
                {
                    Plugin.Log.LogWarning(
                        $"[Niflheim/HomesteadStones] Selected {candidate.Prefab} zone ({candidate.ZoneX},{candidate.ZoneZ}) " +
                        $"has no valid live seat after {seat.AttemptsEvaluated} attempts this realization.");
                    continue;
                }

                var position = new Vector3((float)seat.Seat.X, 0f, (float)seat.Seat.Z);
                if (!Heightmap.GetHeight(position, out var groundHeight)) continue;
                position.y = groundHeight;

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
                Plugin.Log.LogInfo(
                    $"[Niflheim/HomesteadStones] Placed {candidate.Prefab} zone ({candidate.ZoneX},{candidate.ZoneZ}) " +
                    $"attempt={seat.Seat.Attempt} seat=({position.x:0.00},{position.y:0.00},{position.z:0.00}).");
            }
        }

        private static SeatEvaluation EvaluateSeat(HomesteadCandidate host, SeatCandidate candidate)
        {
            var point = new Vector3((float)candidate.X, 0f, (float)candidate.Z);
            if (Heightmap.FindHeightmap(point) == null || !Heightmap.GetHeight(point, out var height) ||
                float.IsNaN(height) || float.IsInfinity(height)) return default;
            point.y = height;

            var hostCenter = new Vector2((float)host.X, (float)host.Z);
            var radialDistance = Vector2.Distance(new Vector2(point.x, point.z), hostCenter);
            var structural = Physics.OverlapSphere(
                    new Vector3((float)host.X, height + 2f, (float)host.Z),
                    (float)Math.Max(12.0, host.LocationRadius + 6.0),
                    CollisionMask,
                    QueryTriggerInteraction.Ignore)
                .Where(collider => IsHostStructure(host, collider))
                .Select(collider => collider.bounds)
                .ToArray();
            if (structural.Length == 0) return default;

            var hostBounds = structural[0];
            foreach (var bounds in structural.Skip(1)) hostBounds.Encapsulate(bounds);
            var closestX = Mathf.Clamp(point.x, hostBounds.min.x, hostBounds.max.x);
            var closestZ = Mathf.Clamp(point.z, hostBounds.min.z, hostBounds.max.z);
            var clearance = Vector2.Distance(new Vector2(point.x, point.z), new Vector2(closestX, closestZ));
            var footprintBlocked = Physics.OverlapCapsule(
                    point + Vector3.up * 0.25f,
                    point + Vector3.up * 2.25f,
                    0.9f,
                    CollisionMask,
                    QueryTriggerInteraction.Ignore)
                .Any(collider => IsHostStructure(host, collider));
            return new SeatEvaluation(
                !footprintBlocked && clearance >= SeatKeepOut,
                clearance,
                radialDistance,
                Math.Max(hostBounds.extents.x, hostBounds.extents.z));
        }

        private static bool IsHostStructure(HomesteadCandidate host, Collider collider)
        {
            if (collider == null || !collider.enabled || collider.isTrigger) return false;
            var piece = collider.GetComponentInParent<Piece>();
            if (piece == null) return false;
            var position = piece.transform.position;
            return HomesteadHostStructure.IsAttributed(
                piece.GetCreator(), position.x, position.z, host.X, host.Z, host.LocationRadius);
        }

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
