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
        private const float VegetationClearRadius = 2.5f;
        // Current Homestead Stone hosts are the thirteen ordinary Meadows house locations only.
        // Farm/Village generators belong to the future village system and are intentionally excluded.
        private static readonly HashSet<string> EligibleHosts = new HashSet<string>(
            Enumerable.Range(1, 13).Select(index => "WoodHouse" + index),
            StringComparer.Ordinal);
        private static ZoneSystem? scheduledFor;

        /// <summary>R6 (Blocker 7) — set from <see cref="HomesteadRuntimeDriftCheck.Verify"/> at plugin Awake.
        /// When false, the placement loop refuses to run its create/reconcile work: a drifted game update
        /// disables realization loudly instead of seating Stones against renamed/removed engine callsites.</summary>
        internal static bool RealizationEnabled { get; set; }

        /// <summary>Durable per-world event provenance ledger. Rehydrated from the world ZDO on assignment so
        /// a fresh-world failure is a terminal fact across restarts (no phantom retries) — never session-only.</summary>
        private static HomesteadWorldLedger Ledger = new HomesteadWorldLedger();

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
            Plugin.Log.LogInfo(
                $"[Niflheim/HomesteadStones] Authored seat catalog ready: hosts={HomesteadAuthoredSeatCatalog.Count} " +
                $"version='{HomesteadAuthoredSeatCatalog.Version}' digest={HomesteadAuthoredSeatCatalog.ContentHash}.");
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
            // The reconciler compares full authored-table provenance. A Stone from an older placement
            // authority is reaped and its zone reopened for creation at Daniel's selected transform.
            var expectedByZone = BuildExpectedPlacements(selectedMetadata);
            // Reconcile before the event gate so unkeyed, unselected, mismatched, duplicate, and stale-
            // provenance Stones cannot suppress legitimate creation. Keep reconciliation INSIDE the same
            // tick-scoped durability boundary as creation: a transient ledger fault must abort this tick and
            // retry on the normal five-second loop, not kill the coroutine until process restart.
            try
            {
                var existing = ReconcileResidentStones(expectedByZone);
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
                if (existing.Contains(key)) continue;
                // Dedicated peer zones are Ghost-generated and never enter ZoneSystem.m_zones, so
                // IsZoneLoaded is permanently false here. The server-owned realization fact is the
                // selected location instance's persisted m_placed flag.
                if (!zoneSystem.m_locationInstances.TryGetValue(runtime.Zone, out var locationInstance) ||
                    !locationInstance.m_placed)
                    continue;

                // Same-version terminal failures remain terminal. Creation itself is governed by matching
                // Stone ZDO truth, not this advisory outcome ledger.
                if (Ledger.IsTerminal(candidate.ZoneX, candidate.ZoneZ, SelectorVersion, liveManifestGeneration: 0)) continue;

                HomesteadResolution resolution;
                Quaternion placementRotation;
                try
                {
                    resolution = ResolveSeat(candidate, worldIdentity, out placementRotation);
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
                var metadata = selectedMetadata[key];
                var provenance = HomesteadProvenanceCodec.FromRecord(metadata, record);

                // The registered template stays activeSelf=true under an inactive holder. Never mutate it:
                // ZNetScene reconstruction needs an active template so ZNetView.Awake consumes m_initZDO.
                ClearVegetationAround(position, zoneSystem);
                var instance = UnityEngine.Object.Instantiate(prefab, position, placementRotation);
                instance.name = HomesteadStoneRegistrar.PrefabName;
                instance.transform.SetParent(null, true);
                if (!StampIdentity(instance.GetComponent<ZNetView>(), provenance))
                {
                    if (ZNetScene.instance != null) ZNetScene.instance.Destroy(instance);
                    else UnityEngine.Object.Destroy(instance);
                    // R7 (Blocker 1) — a stamp failure records DURABLE failure provenance before cleanup, so the
                    // event is not silently lost and a persistently-failing stamp shows up as a terminal outcome
                    // rather than a phantom retry loop.
                    Ledger.Record(candidate.ZoneX, candidate.ZoneZ, HomesteadEventOutcome.Exception, SelectorVersion,
                        "identity/provenance stamp read-back verification failed");
                    PersistLedger();
                    Plugin.Log.LogError(
                        $"[Niflheim/HomesteadStones] Destroyed unstamped Stone at zone ({runtime.Zone.x},{runtime.Zone.y}); " +
                        "ZNetView/ZDO provenance stamping failed (durable failure recorded).");
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

        /// <summary>Resolve one Daniel-approved prefab-local transform against the authoritative LocationProxy
        /// position/rotation. No runtime collider catalog, Physics scoring, or generator manifest.</summary>
        private static HomesteadResolution ResolveSeat(
            HomesteadCandidate candidate,
            string worldIdentity,
            out Quaternion placementRotation)
        {
            placementRotation = Quaternion.identity;
            if (!HomesteadAuthoredSeatCatalog.TryGet(candidate.Prefab, out var seat))
                return HomesteadResolution.Fail(HomesteadResolutionStatus.GeometryUnavailable,
                    $"{candidate.Prefab} has no authored Homestead Stone transform.");

            if (!TryResolveHostFromZdo(candidate, out var hostOrigin, out var hostRotation))
                return HomesteadResolution.Fail(HomesteadResolutionStatus.CatalogUnavailable,
                    $"{candidate.Prefab} ({candidate.ZoneX},{candidate.ZoneZ}) authoritative location/proxy ZDO not resolvable yet (retryable).");

            seat.ToWorld(
                hostOrigin.x, hostOrigin.z, hostRotation.eulerAngles.y,
                out var seatX, out var seatZ, out var worldYawDegrees);
            placementRotation = Quaternion.Euler(0f, (float)worldYawDegrees, 0f);
            var radial = Math.Sqrt((seatX - hostOrigin.x) * (seatX - hostOrigin.x) +
                                   (seatZ - hostOrigin.z) * (seatZ - hostOrigin.z));
            var record = new ResolvedPlacementRecord(
                worldIdentity, SelectorVersion, candidate.Prefab, candidate.ZoneX, candidate.ZoneZ,
                seatX, seatZ, hostOrigin.y + seat.LocalY,
                radial, double.NaN,
                HomesteadSeatProvider.StaticGeometry, HomesteadAuthoredSeatCatalog.ContentHash, attempt: 0,
                providerVersion: HomesteadAuthoredSeatCatalog.Version, manifestGeneration: 0);
            return HomesteadResolution.Ok(record);
        }

        /// <summary>R6 (Blocker 1) — resolve a host's authoritative origin + realized yaw from the location/proxy
        /// ZDO, matched by the host prefab's <c>s_location</c> stable hash within the candidate's zone. Reads
        /// ZDO position + rotation only (no child-hierarchy discovery, no nearest-live-proxy guess). Returns
        /// false when no matching proxy ZDO exists yet — retryable, not terminal.</summary>
        private static bool TryResolveHostFromZdo(HomesteadCandidate candidate, out Vector3 hostOrigin, out Quaternion hostRotation)
        {
            hostOrigin = default;
            hostRotation = Quaternion.identity;
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
                hostRotation = zdo.GetRotation();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Reproduce the Location clear-area consequence for a Stone realized after vanilla vegetation
        /// placement. Only ZDOs whose prefab appears in ZoneSystem's vegetation table are eligible.
        /// </summary>
        private static void ClearVegetationAround(Vector3 position, ZoneSystem zoneSystem)
        {
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return;

            var radiusSq = VegetationClearRadius * VegetationClearRadius;
            var prefabNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var vegetation in zoneSystem.m_vegetation)
            {
                if (vegetation?.m_prefab == null || string.IsNullOrEmpty(vegetation.m_prefab.name)) continue;
                prefabNames.Add(vegetation.m_prefab.name);
            }

            var destroyed = 0;
            foreach (var prefabName in prefabNames)
            {
                var found = new List<ZDO>();
                var index = 0;
                while (!zdoMan.GetAllZDOsWithPrefabIterative(prefabName, found, ref index)) { }
                foreach (var zdo in found)
                {
                    if (zdo == null || !zdo.IsValid() || !zdo.IsOwner()) continue;
                    var candidate = zdo.GetPosition();
                    var dx = candidate.x - position.x;
                    var dz = candidate.z - position.z;
                    if ((dx * dx) + (dz * dz) > radiusSq) continue;
                    zdoMan.DestroyZDO(zdo);
                    destroyed++;
                }
            }

            if (destroyed > 0)
                Plugin.Log.LogInfo(
                    $"[Niflheim/HomesteadStones] Cleared {destroyed} vegetation ZDO(s) within " +
                    $"{VegetationClearRadius:0.0}m of authored seat ({position.x:0.00},{position.z:0.00}).");
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

        /// <summary>Build the expected provenance for each selected authored house transform.</summary>
        private static Dictionary<string, HomesteadExpectedPlacement> BuildExpectedPlacements(
            IReadOnlyDictionary<string, HomesteadAssignmentMetadata> selectedMetadata)
        {
            var expected = new Dictionary<string, HomesteadExpectedPlacement>(StringComparer.Ordinal);
            foreach (var pair in selectedMetadata)
            {
                var meta = pair.Value;
                if (!HomesteadAuthoredSeatCatalog.TryGet(meta.Prefab, out _)) continue;
                var provenance = new HomesteadStoneProvenance(
                    HomesteadProvenanceCodec.SchemaVersion, meta, HomesteadSeatProvider.StaticGeometry,
                    HomesteadAuthoredSeatCatalog.Version, HomesteadAuthoredSeatCatalog.ContentHash, 0);
                expected[pair.Key] = new HomesteadExpectedPlacement(provenance);
            }
            return expected;
        }

        /// <summary>R6 (Blocker 4) / R7 (Blocker 1) — production reconciliation using the full stable ZDOID
        /// (UserID, ID) and the FULL provenance read back through the shared codec. Enumerates every resident
        /// Stone ZDO into an engine-free <see cref="StoneReconcileFact"/> (full ZDOID + full provenance + keyed
        /// flag), runs the pure <see cref="StoneReconciler"/> policy against the expected placements, destroys
        /// every Stone the policy marks Destroy (unkeyed / unselected / mismatched / duplicate / stale-provenance),
        /// clears the ledger Created for any recovery zone so an upgrade re-creates, and returns the zone keys
        /// already satisfied by a kept Stone so the creation loop skips them. Runs BEFORE the event gate.</summary>
        private static HashSet<string> ReconcileResidentStones(
            IReadOnlyDictionary<string, HomesteadExpectedPlacement> expected)
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
                // Read the FULL provenance back through the SAME codec production stamped with, so the
                // reconciler compares provider/content/generation, not just basic assignment metadata.
                var provenance = HomesteadProvenanceCodec.Read(new ZdoProvenanceAccessor(zdo));
                var keyed = provenance.Assignment.ZoneX != int.MinValue && provenance.Assignment.ZoneZ != int.MinValue;
                // FULL stable identity: (UserID, ID) — never a truncated numeric. Two ZDOs can share ID
                // across different UserIDs; truncating would silently merge distinct Stones.
                var stable = new StableZdoId(zdo.m_uid.UserID, zdo.m_uid.ID);
                factByZdo[stable] = zdo;
                facts.Add(new StoneReconcileFact(stable, keyed, provenance));
            }

            var plan = StoneReconciler.Reconcile(facts, expected);
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
            // R7 (Blocker 1) — a Created ledger outcome is ADVISORY: when the reconciler reaped the only Stone
            // for a zone because its provenance was stale (a selector/provider/content/generation upgrade), the
            // Created entry must be cleared so recovery re-creates the Stone. Reality (the ZDO) is authoritative;
            // a sticky Created can never block an upgrade.
            var recovered = 0;
            foreach (var zoneKey in plan.RecoveryZoneKeys)
            {
                if (!TryParseZoneKey(zoneKey, out var zx, out var zz)) continue;
                Ledger.ClearForRecovery(zx, zz);
                recovered++;
            }
            if (recovered > 0)
            {
                PersistLedger();
                Plugin.Log.LogInfo(
                    $"[Niflheim/HomesteadStones] Cleared {recovered} stale Created ledger entry/entries for recovery (provenance upgrade).");
            }
            foreach (var key in plan.SatisfiedZoneKeys) satisfied.Add(key);
            if (reaped > 0)
                Plugin.Log.LogInfo(
                    $"[Niflheim/HomesteadStones] Reconciler kept {satisfied.Count} Stone(s), reaped {reaped}.");
            return satisfied;
        }

        /// <summary>R7 (Blocker 1) — persist the FULL provenance (assignment + provider/content/generation) onto
        /// the Stone ZDO via the shared engine-free codec, then read it back through the SAME codec and verify
        /// every field round-tripped. A partial/failed write fails verification and the caller reaps the Stone
        /// and records durable failure provenance.</summary>
        private static bool StampIdentity(ZNetView? networkView, HomesteadStoneProvenance provenance)
        {
            if (networkView == null) return false;
            var zdo = networkView.GetZDO();
            if (zdo == null) return false;
            if (!networkView.IsOwner()) networkView.ClaimOwnership();
            var accessor = new ZdoProvenanceAccessor(zdo);
            HomesteadProvenanceCodec.Stamp(accessor, provenance);
            return HomesteadProvenanceCodec.ReadBackMatches(accessor, provenance);
        }

        /// <summary>Adapts a live Valheim <see cref="ZDO"/> to the engine-free <see cref="IProvenanceWriter"/> /
        /// <see cref="IProvenanceReader"/> the codec uses, so production stamps/reads through the exact code the
        /// headless tests exercise against an in-memory store (Blocker 5). Owner-only writes via the ZNetView.</summary>
        private sealed class ZdoProvenanceAccessor : IProvenanceWriter, IProvenanceReader
        {
            private readonly ZDO zdo;
            internal ZdoProvenanceAccessor(ZDO zdo) => this.zdo = zdo;
            public void SetInt(string key, int value) => zdo.Set(key, value);
            public void SetLong(string key, long value) => zdo.Set(key, value);
            public void SetString(string key, string value) => zdo.Set(key, value);
            public int GetInt(string key, int missing) => zdo.GetInt(key, missing);
            public long GetLong(string key, long missing) => zdo.GetLong(key, missing);
            public string GetString(string key, string missing) => zdo.GetString(key, missing);
        }

        private static string ResolveWorldIdentity() =>
            ZNet.instance == null ? "unknown-world" : HomesteadWorldIdentity.FromUid(ZNet.instance.GetWorldUID());

        private static string Identity(HomesteadCandidate candidate) =>
            candidate.Prefab + ":" + candidate.ZoneX + ":" + candidate.ZoneZ;
        private static string ZoneKey(Vector2i zone) => zone.x + ":" + zone.y;

        /// <summary>Parse a "zx:zz" zone key (as produced by <see cref="StoneReconciler.ZoneKey(int,int)"/>) back
        /// into its integer coordinates for ledger recovery. Returns false on any malformed key.</summary>
        private static bool TryParseZoneKey(string zoneKey, out int zoneX, out int zoneZ)
        {
            zoneX = 0;
            zoneZ = 0;
            if (string.IsNullOrEmpty(zoneKey)) return false;
            var parts = zoneKey.Split(':');
            return parts.Length == 2 &&
                   int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out zoneX) &&
                   int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out zoneZ);
        }

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
