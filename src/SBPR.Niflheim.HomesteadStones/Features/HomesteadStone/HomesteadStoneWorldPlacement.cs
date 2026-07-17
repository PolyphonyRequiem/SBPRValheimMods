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
        private static readonly HashSet<string> EligibleHosts = new HashSet<string>(
            Enumerable.Range(1, 13).Select(index => "WoodHouse" + index)
                .Concat(new[] { "WoodFarm1", "WoodVillage1" }),
            StringComparer.Ordinal);
        private static ZoneSystem? scheduledFor;

        /// <summary>R6 (Blocker 7) — set from <see cref="HomesteadRuntimeDriftCheck.Verify"/> at plugin Awake.
        /// When false, the placement loop refuses to run its create/reconcile work: a drifted game update
        /// disables realization loudly instead of seating Stones against renamed/removed engine callsites.</summary>
        internal static bool RealizationEnabled { get; set; }

        /// <summary>Durable per-world event provenance ledger. Rehydrated from the world ZDO on assignment so
        /// a fresh-world failure is a terminal fact across restarts (no phantom retries) — never session-only.</summary>
        private static HomesteadWorldLedger Ledger = new HomesteadWorldLedger();

        /// <summary>Versioned generator-host seat manifest (Approach C). Empty until an operator scan supplies
        /// rows; generator hosts skip explicitly (ManifestRequired) until then. No runtime geometry guessing.</summary>
        private static HomesteadOperationalManifest Manifest = HomesteadOperationalManifest.Empty;

        /// <summary>R6 (Blocker 1) — the checked-in static geometry catalog, the production authority for
        /// ordinary-host footprints. Loaded + hash-pin-verified once at startup; a pin failure disables
        /// realization fail-closed rather than seating against drifted geometry.</summary>
        private static HomesteadStaticGeometryCatalog Catalog = HomesteadStaticGeometryCatalog.Empty;

        [HarmonyPatch(typeof(ZoneSystem), "Start")]
        [HarmonyPostfix]
        private static void OnZoneSystemStart(ZoneSystem __instance)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ReferenceEquals(scheduledFor, __instance)) return;
            if (!RealizationEnabled)
            {
                Plugin.Log.LogError(
                    "[Niflheim/HomesteadStones] Realization DISABLED (runtime drift check failed at load); "
                    + "placement loop will not run. Resolve the drift and restart.");
                return;
            }
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
            try
            {
                // R6 (Blocker 1) — load + hash-pin-verify the checked-in static catalog. A pin failure means
                // the shipped geometry drifted from the catalog; fail closed (no seating) rather than seat
                // against changed geometry.
                Catalog = HomesteadStaticGeometryCatalogLoader.Load();
                Plugin.Log.LogInfo(
                    $"[Niflheim/HomesteadStones] Static geometry catalog loaded: hosts={Catalog.HostCount} "
                    + $"digest={Catalog.CatalogDigest} schema='{Catalog.Schema}'.");
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError(
                    $"[Niflheim/HomesteadStones] Static catalog load/verify FAILED: {exception.Message}. "
                    + "Realization halted (catalog is the production authority; refusing to seat against drift).");
                yield break;
            }
            try
            {
                Ledger = HomesteadLedgerStore.Load(worldIdentity);
            }
            catch (HomesteadLedgerStore.LedgerIoException exception)
            {
                // Fail-closed: a present-but-unreadable ledger with no valid temp/backup must NOT be treated
                // as empty (that would phantom-retry every terminal zone). Disable realization for this world
                // until an operator resolves the corruption, rather than proceeding on a fabricated history.
                Plugin.Log.LogError(
                    $"[Niflheim/HomesteadStones] Ledger load fail-closed for world '{worldIdentity}': "
                    + $"{exception.Message}. Realization halted for this session.");
                yield break;
            }
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
                // R6 (Blocker 6) — reload the operational manifest each tick so a new generation supplied by
                // an operator is picked up WITHOUT a restart; generation is passed to the ledger gate so a
                // previously-ManifestRequired generator host becomes retryable when the generation advances.
                Manifest = HomesteadManifestStore.LoadOrReload(worldIdentity, SelectorVersion);
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
            // R6 (Blocker 4) — reconcile resident Stones with the FULL stable ZDOID (UserID, ID) via the
            // production StoneReconciler BEFORE the event gate, so unkeyed / unselected / mismatched /
            // duplicate Stones are reaped and stale zone entries can never suppress a legitimate creation.
            var existing = ReconcileResidentStones(selectedMetadata);
            try
            {
                PlaceSelected(prefab, worldIdentity, selected, byIdentity, existing, selectedMetadata, zoneSystem);
            }
            catch (HomesteadLedgerStore.LedgerIoException exception)
            {
                // Fail-closed: a ledger write that cannot be made durable aborts the rest of this tick rather
                // than continuing to place Stones whose provenance we cannot persist. The loop retries next
                // recheck; a persistently failing store keeps realization halted and loud, never silent.
                Plugin.Log.LogError(
                    $"[Niflheim/HomesteadStones] Ledger persist fail-closed this tick: {exception.Message}. "
                    + "Aborting remaining placements until the store recovers.");
            }
        }

        private static void PlaceSelected(
            GameObject prefab,
            string worldIdentity,
            IReadOnlyList<HomesteadCandidate> selected,
            IReadOnlyDictionary<string, RuntimeCandidate> byIdentity,
            HashSet<string> existing,
            IReadOnlyDictionary<string, HomesteadAssignmentMetadata> selectedMetadata,
            ZoneSystem zoneSystem)
        {
            foreach (var candidate in selected)
            {
                var runtime = byIdentity[Identity(candidate)];
                var key = ZoneKey(runtime.Zone);
                if (existing.Contains(key) || !zoneSystem.IsZoneLoaded(runtime.Zone)) continue;

                // Event gate (assumption audit): a same-version terminal outcome already recorded for this
                // host zone must NOT be re-attempted — this is what prevents counter-only phantom retries
                // after vanilla has set its generated flag. The live manifest generation is passed so a
                // ManifestRequired zone becomes retryable when a newer generation appears (R6 Blocker 6).
                if (Ledger.IsTerminal(candidate.ZoneX, candidate.ZoneZ, SelectorVersion, Manifest.Generation)) continue;

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
                    // ManifestRequired carries the generation it was decided under so a later generation can
                    // supersede it; all other failures carry generation 0 (version-scoped terminal).
                    var generation = resolution.Status == HomesteadResolutionStatus.ManifestRequired
                        ? Manifest.Generation : 0L;
                    Ledger.Record(candidate.ZoneX, candidate.ZoneZ, MapOutcome(resolution.Status), SelectorVersion, resolution.Detail, generation);
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
                // Generator hosts: OPERATIONAL-manifest-only (R6 Blocker 6). No matching row ⇒ ManifestRequired
                // (explicit, retryable skip). Never reads live geometry, never guesses a ring, never a
                // player-submittable row. The manifest document digest becomes the Stone's provenance stamp.
                return HomesteadPlacementResolver.ResolveGeneratorOperational(
                    worldIdentity, SelectorVersion, candidate, Manifest);
            }

            // R6 (Blocker 1) — ORDINARY host geometry comes from the CHECKED-IN static catalog keyed by exact
            // prefab, NOT a live LocationProxy child hierarchy. The host's realized transform/rotation comes
            // from the authoritative location/proxy ZDO (position + rotation + s_location hash), not a
            // nearest-live-proxy guess. A missing catalog entry or unresolvable host identity is RETRYABLE
            // (CatalogUnavailable), never a terminal GeometryUnavailable.
            if (!Catalog.TryGet(candidate.Prefab, out var geometry))
                return HomesteadResolution.Fail(HomesteadResolutionStatus.CatalogUnavailable,
                    $"{candidate.Prefab} ({candidate.ZoneX},{candidate.ZoneZ}) has no static catalog entry (retryable).");

            if (!TryResolveHostFromZdo(candidate, out var hostOrigin, out var yaw))
                return HomesteadResolution.Fail(HomesteadResolutionStatus.CatalogUnavailable,
                    $"{candidate.Prefab} ({candidate.ZoneX},{candidate.ZoneZ}) authoritative location/proxy ZDO not resolvable yet (retryable).");

            // Use the ZDO-authoritative host origin (not the coarse zone-instance XZ) so the seat and terrain
            // sample agree with the realized location pose.
            var authoritativeCandidate = new HomesteadCandidate(
                candidate.Prefab, candidate.ZoneX, candidate.ZoneZ, hostOrigin.x, hostOrigin.z, candidate.LocationRadius);
            return HomesteadPlacementResolver.ResolveOrdinary(
                worldIdentity, SelectorVersion, authoritativeCandidate, geometry, yaw, WorldGenHeight);
        }

        /// <summary>R6 (Blocker 1) — resolve a host's authoritative origin + realized yaw from the location/proxy
        /// ZDO, matched by the host prefab's <c>s_location</c> stable hash within the candidate's zone. Reads
        /// ZDO position + rotation only (no child-hierarchy discovery, no nearest-live-proxy guess). Returns
        /// false when no matching proxy ZDO exists yet — retryable, not terminal.</summary>
        private static bool TryResolveHostFromZdo(HomesteadCandidate candidate, out Vector3 hostOrigin, out double yawRadians)
        {
            hostOrigin = default;
            yawRadians = 0.0;
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return false;

            var wantHash = candidate.Prefab.GetStableHashCode();
            var zone = new Vector2i(candidate.ZoneX, candidate.ZoneZ);

            var found = new List<ZDO>();
            var index = 0;
            while (!zdoMan.GetAllZDOsWithPrefabIterative("LocationProxy", found, ref index)) { }
            foreach (var zdo in found)
            {
                if (zdo == null || !zdo.IsValid()) continue;
                if (zdo.GetInt(ZDOVars.s_location, 0) != wantHash) continue;
                // Bind to the candidate's zone so two same-prefab locations in adjacent zones never cross-match.
                if (ZoneSystem.GetZone(zdo.GetPosition()) != zone) continue;
                hostOrigin = zdo.GetPosition();
                yawRadians = zdo.GetRotation().eulerAngles.y * System.Math.PI / 180.0;
                return true;
            }
            return false;
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
            HomesteadResolutionStatus.CatalogUnavailable => HomesteadEventOutcome.CatalogUnavailable,
            _ => HomesteadEventOutcome.Exception,
        };

        private static void PersistLedger() => HomesteadLedgerStore.Save(Ledger);

        /// <summary>R6 (Blocker 4) — production reconciliation using the full stable ZDOID (UserID, ID).
        /// Enumerates every resident Stone ZDO into an engine-free <see cref="StoneReconcileFact"/> (carrying
        /// its full ZDOID + assignment metadata + keyed flag), runs the pure <see cref="StoneReconciler"/>
        /// policy against the selected assignments, then destroys every Stone the policy marks Destroy
        /// (unkeyed / unselected / mismatched / duplicate) and returns the zone keys already satisfied by a
        /// kept Stone so the creation loop skips them. Runs BEFORE the event gate.</summary>
        private static HashSet<string> ReconcileResidentStones(
            IReadOnlyDictionary<string, HomesteadAssignmentMetadata> selected)
        {
            var satisfied = new HashSet<string>(StringComparer.Ordinal);
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return satisfied;

            var found = new List<ZDO>();
            var index = 0;
            while (!zdoMan.GetAllZDOsWithPrefabIterative(HomesteadStoneRegistrar.PrefabName, found, ref index)) { }

            var factByZdo = new Dictionary<StableZdoId, ZDO>();
            var facts = new List<StoneReconcileFact>();
            foreach (var zdo in found)
            {
                if (zdo == null || !zdo.IsValid()) continue;
                var x = zdo.GetInt(HomesteadStoneData.LocationZoneXKey, int.MinValue);
                var z = zdo.GetInt(HomesteadStoneData.LocationZoneZKey, int.MinValue);
                var keyed = x != int.MinValue && z != int.MinValue;
                var metadata = new HomesteadAssignmentMetadata(
                    zdo.GetString(HomesteadStoneData.WorldIdentityKey, string.Empty),
                    zdo.GetString(HomesteadStoneData.SelectorVersionKey, string.Empty),
                    zdo.GetString(HomesteadStoneData.HostPrefabKey, string.Empty),
                    keyed ? x : 0,
                    keyed ? z : 0);
                // FULL stable identity: (UserID, ID) — never a truncated numeric. Two ZDOs can share ID
                // across different UserIDs; truncating would silently merge distinct Stones.
                var stable = new StableZdoId(zdo.m_uid.UserID, zdo.m_uid.ID);
                factByZdo[stable] = zdo;
                facts.Add(new StoneReconcileFact(stable, keyed, metadata));
            }

            var plan = StoneReconciler.Reconcile(facts, selected);
            var reaped = 0;
            foreach (var decision in plan.Decisions)
            {
                if (decision.Action != StoneReconcileAction.Destroy) continue;
                if (!factByZdo.TryGetValue(decision.ZdoId, out var zdo) || zdo == null || !zdo.IsValid()) continue;
                if (!zdo.IsOwner()) zdo.SetOwner(ZDOMan.GetSessionID());
                zdoMan.DestroyZDO(zdo);
                reaped++;
                Plugin.Log.LogWarning(
                    $"[Niflheim/HomesteadStones] Reaped resident Stone {decision.ZdoId} ({decision.Reason}).");
            }
            foreach (var key in plan.SatisfiedZoneKeys) satisfied.Add(key);
            if (reaped > 0)
                Plugin.Log.LogInfo(
                    $"[Niflheim/HomesteadStones] Reconciler kept {satisfied.Count} Stone(s), reaped {reaped}.");
            return satisfied;
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
