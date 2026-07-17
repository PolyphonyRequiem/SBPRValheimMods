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
        private const int SeatAttempts = 8;
        private const float SeatKeepOut = 1.75f;
        private const float RecheckSeconds = 5f;
        // A selected zone whose vanilla host location is placed but which stays Stone-less past this many
        // seconds is the actionable "resident selected zone remains Stone-less" signal. It is deliberately
        // a few realization passes long so a normally-realizing zone never trips it.
        private const double StonelessWarnSeconds = 30.0;
        private static readonly HashSet<string> EligibleHosts = new HashSet<string>(
            Enumerable.Range(1, 13).Select(index => "WoodHouse" + index)
                .Concat(new[] { "WoodFarm1", "WoodVillage1" }),
            StringComparer.Ordinal);
        private static readonly int CollisionMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
        private static ZoneSystem? scheduledFor;

        // Bounded diagnostic state (per composed server run). The reporter emits one summary line only when
        // a realization pass changes shape; the watch fires one warning per stone-less selected zone episode;
        // prefabAvailabilityLogged ensures the prefab-availability line is emitted at most once per state.
        private static readonly RealizationPassReporter PassReporter = new RealizationPassReporter();
        private static readonly StonelessWatch Stoneless = new StonelessWatch(StonelessWarnSeconds);
        private static bool prefabMissingLogged;

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
                var elapsed = Time.realtimeSinceStartup;
                PlaceLoaded(zoneSystem, worldIdentity, selection.Selected, byIdentity, elapsed);
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
            IReadOnlyDictionary<string, RuntimeCandidate> byIdentity,
            float elapsedSeconds)
        {
            var prefab = ZNetScene.instance?.GetPrefab(HomesteadStoneRegistrar.PrefabName);
            if (prefab == null)
            {
                // Bounded, actionable: the prefab is not registered, so NOTHING can ever realize. Log once
                // per missing-state (not per tick) so the operator sees the real blocker instead of silence.
                if (!prefabMissingLogged)
                {
                    prefabMissingLogged = true;
                    Plugin.Log.LogError(
                        $"[Niflheim/HomesteadStones] Homestead Stone prefab '{HomesteadStoneRegistrar.PrefabName}' " +
                        "is not registered in ZNetScene; no Stone can realize. Check prefab registration/bundle load.");
                }
                return;
            }
            prefabMissingLogged = false;

            var selectedMetadata = selected.ToDictionary(
                candidate => ZoneKey(new Vector2i(candidate.ZoneX, candidate.ZoneZ)),
                candidate => new HomesteadAssignmentMetadata(
                    worldIdentity, SelectorVersion, candidate.Prefab, candidate.ZoneX, candidate.ZoneZ),
                StringComparer.Ordinal);
            var existing = ReconcileExisting(selectedMetadata);

            var pass = new RealizationPass();
            var stonelessZoneKeys = new List<string>();

            foreach (var candidate in selected)
            {
                var runtime = byIdentity[Identity(candidate)];
                var key = ZoneKey(runtime.Zone);

                // Data-only realization gate. The prior build gated on zoneSystem.IsZoneLoaded(zone), which
                // is permanently false on a dedicated server for any peer-realized zone (vanilla only adds
                // zones to m_zones around ZNet.GetReferencePosition(), the far-away server sentinel). The
                // server-owned truth that a zone's world/terrain exists is that its vanilla location instance
                // is PLACED (set in ghost OR full spawn) — exactly the signal that produced the resident
                // structure ZDOs. We trigger on that, so realization no longer depends on scene realization
                // around a non-existent local player.
                bool alreadyResident = existing.Contains(key);
                bool hostPlaced = IsHostLocationPlaced(zoneSystem, runtime.Zone);
                var gate = HomesteadRealizationGateEvaluator.Evaluate(alreadyResident, hostPlaced);
                pass.Observe(gate);
                if (gate != RealizationGate.Eligible) continue;

                // Zone is placed and Stone-less: it is a realization candidate this pass. Track it for the
                // stone-less watch; it is removed from the watch automatically once it realizes.
                stonelessZoneKeys.Add(key);

                if (!TryResolveSeat(zoneSystem, worldIdentity, candidate, out var position))
                {
                    pass.SeatSkipped();
                    continue;
                }

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
                stonelessZoneKeys.Remove(key);
                pass.Realized();
                Plugin.Log.LogInfo(
                    $"[Niflheim/HomesteadStones] Placed {candidate.Prefab} zone ({candidate.ZoneX},{candidate.ZoneZ}) " +
                    $"seat=({position.x:0.00},{position.y:0.00},{position.z:0.00}).");
            }

            // Bounded diagnostics: one summary line only when the pass shape changed (never per-tick spam).
            var summary = PassReporter.Consider(pass);
            if (summary != null) Plugin.Log.LogInfo("[Niflheim/HomesteadStones] " + summary);

            // Actionable stone-less warning: a selected zone whose host location is placed but which has gone
            // Stone-less past the bounded interval. Fired at most once per zone per stone-less episode.
            foreach (var stonelessKey in Stoneless.Advance(elapsedSeconds, stonelessZoneKeys))
                Plugin.Log.LogWarning(
                    $"[Niflheim/HomesteadStones] Selected zone {stonelessKey} host location is placed but has had " +
                    $"no realized Homestead Stone for over {StonelessWarnSeconds:0}s; every seat attempt is failing. " +
                    "Investigate seat/terrain resolution for this zone.");
        }

        /// <summary>Server-owned truth that a selected zone's world exists: its vanilla location instance is
        /// PLACED. This is set by ZoneSystem in BOTH ghost and full spawn, so it is true on a dedicated
        /// server for peer-realized zones — unlike IsZoneLoaded, which only tracks the (non-existent) local
        /// player's active area. When the location instance is absent (world not generated to that zone yet)
        /// it reads as not-placed and the candidate is deferred.</summary>
        private static bool IsHostLocationPlaced(ZoneSystem zoneSystem, Vector2i zone) =>
            zoneSystem.m_locationInstances.TryGetValue(zone, out var instance) && instance.m_placed;

        /// <summary>Resolve a persistent seat for an eligible candidate. When the candidate's zone is
        /// scene-instantiated on THIS peer (listen server / singleplayer host), the live collider-aware seat
        /// evaluation runs exactly as before. On a headless dedicated server the peer zone is never scene-
        /// instantiated (ZNetScene realizes objects only around ZNet.GetReferencePosition()), so live
        /// Heightmap/colliders are absent; we fall back to the deterministic best-of-8 seat with a data-only
        /// height from WorldGenerator, which is the same terrain source vanilla used to place the host.</summary>
        private static bool TryResolveSeat(
            ZoneSystem zoneSystem, string worldIdentity, HomesteadCandidate candidate, out Vector3 position)
        {
            position = default;
            var seats = HomesteadSeatGenerator.Generate(worldIdentity, SelectorVersion, candidate, SeatAttempts);

            if (zoneSystem.IsZoneLoaded(new Vector2i(candidate.ZoneX, candidate.ZoneZ)))
            {
                // Scene-instantiated locally: use the full collider-aware evaluation + live Heightmap.
                var seat = HomesteadSeatGenerator.ChooseBest(seats, candidateSeat => EvaluateSeat(candidate, candidateSeat));
                if (!seat.HasSeat) return false;
                var p = new Vector3((float)seat.Seat.X, 0f, (float)seat.Seat.Z);
                if (!Heightmap.GetHeight(p, out var groundHeight)) return false;
                p.y = groundHeight;
                position = p;
                return true;
            }

            // Headless dedicated server: no live scene around the peer zone. Pick the first deterministic
            // seat and resolve Y from world-generation data (the authoritative terrain source that exists
            // without scene realization). This is idempotent and stable across restarts by construction.
            var world = WorldGenerator.instance;
            if (world == null) return false;
            var chosen = seats[0];
            var height = world.GetHeight((float)chosen.X, (float)chosen.Z);
            if (float.IsNaN(height) || float.IsInfinity(height)) return false;
            position = new Vector3((float)chosen.X, height, (float)chosen.Z);
            return true;
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
