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
    /// FIX R3 — Server-authoritative, EVENT-TIME Homestead Stone realization.
    ///
    /// R1/R2 tried to realize Stones from a periodic loop that, on a headless dedicated server, reconstructed
    /// host footprint and leveled surface from generic persisted ZDO pivots. Fresh review (PR #323) rejected
    /// that as unsound: a ZDO pivot is neither a collider bound nor a terrain sample, and the creator==0
    /// harvest also swept in the LocationProxy / zone-control / vegetation.
    ///
    /// This build instead grounds creation in the vanilla fresh-zone realization event. Vanilla
    /// <c>ZoneSystem.SpawnZone(Ghost|Full)</c> calls <c>PlaceLocations(zoneID, .., hmap, .., mode, spawned)</c>,
    /// which freshly instantiates the host's own ZNetView children at their final world positions with real
    /// colliders, over a live <c>Heightmap</c>, after the location's terrain leveling — and only AFTER
    /// <c>PlaceLocations</c> returns does <c>SpawnZone</c> destroy the ghost temp objects (Ghost mode). A
    /// Harmony POSTFIX on <c>PlaceLocations</c> therefore sees the real geometry, on BOTH a listen server
    /// (Full) and a headless dedicated server (Ghost), while it still exists. We evaluate the full best-of-
    /// eight seat contract against those live host-collider bounds + live Heightmap, then instantiate the
    /// additive Stone through correct ghost/full ZNetView init so its persistent ZDO survives the temp-object
    /// destruction (verified against decompiled <c>ZNetView.Awake</c>/<c>OnDestroy</c>, base-game RE per
    /// ADR-0001: ghost-init creates a persistent ZDO in ZDOMan and returns before AddInstance; OnDestroy never
    /// destroys the ZDO).
    ///
    /// <c>PlaceLocations</c> fires exactly once per zone per world (vanilla guards on <c>!m_placed</c> and
    /// <c>!IsZoneGenerated</c>), so fresh creation is a one-shot event; a restart re-loads the persisted Stone
    /// ZDO and the postfix never re-fires. A selected host whose zone is ALREADY generated but has no Stone can
    /// only be a pre-fix world — the periodic reconcile emits one migration-required diagnostic and never
    /// guesses geometry.
    /// </summary>
    [HarmonyPatch]
    internal static class HomesteadStoneWorldPlacement
    {
        private const string SelectorVersion = "niflheim-homestead-playtest-v1";
        private const float MinimumDistance = 128f;
        private const double Density = 0.40;
        private const int SeatAttempts = 8;
        private const float RecheckSeconds = 5f;
        private static readonly HashSet<string> EligibleHosts = new HashSet<string>(
            Enumerable.Range(1, 13).Select(index => "WoodHouse" + index)
                .Concat(new[] { "WoodFarm1", "WoodVillage1" }),
            StringComparer.Ordinal);
        private static readonly int CollisionMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
        private static ZoneSystem? scheduledFor;

        // Per-world state — RECREATED on every ZoneSystem.Start and cleared on destroy so no assignment,
        // migration-warned set, or prefab-missing latch survives a world reload.
        private static readonly object StateGate = new object();
        private static string worldIdentity = "unknown-world";
        private static bool selectionReady;
        private static Dictionary<string, HomesteadAssignmentMetadata> selectedByZoneKey =
            new Dictionary<string, HomesteadAssignmentMetadata>(StringComparer.Ordinal);
        private static Dictionary<string, RuntimeCandidate> selectedRuntimeByZoneKey =
            new Dictionary<string, RuntimeCandidate>(StringComparer.Ordinal);
        private static readonly HashSet<string> migrationWarned = new HashSet<string>(StringComparer.Ordinal);
        private static bool prefabMissingLogged;

        // FIX R4 (#4) — per-zone fresh-event provenance for this session, so the migration scan can tell a
        // genuine pre-fix missing-Stone world from a fresh-event failure. Reset every ZoneSystem.Start.
        private static Dictionary<string, StoneEventOutcome> eventOutcomeByZoneKey =
            new Dictionary<string, StoneEventOutcome>(StringComparer.Ordinal);
        // Zones that were ALREADY generated at the moment selection became ready this session (i.e. before any
        // fresh PlaceLocations event this session could have fired for them). A selected zone in this set with
        // no Stone and no fresh event is a pre-fix migration.
        private static HashSet<string> zonesGeneratedAtStart = new HashSet<string>(StringComparer.Ordinal);
        // FIX R4 (#4) — per-zone bounded retry budget for transient fresh-creation failures.
        private static Dictionary<string, int> transientRetryByZoneKey =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private const int MaxTransientRetries = 3;
        private static readonly HashSet<string> freshSkipWarned = new HashSet<string>(StringComparer.Ordinal);

        [HarmonyPatch(typeof(ZoneSystem), "Start")]
        [HarmonyPostfix]
        private static void OnZoneSystemStart(ZoneSystem __instance)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ReferenceEquals(scheduledFor, __instance)) return;
            scheduledFor = __instance;
            lock (StateGate)
            {
                selectionReady = false;
                worldIdentity = "unknown-world";
                selectedByZoneKey = new Dictionary<string, HomesteadAssignmentMetadata>(StringComparer.Ordinal);
                selectedRuntimeByZoneKey = new Dictionary<string, RuntimeCandidate>(StringComparer.Ordinal);
                migrationWarned.Clear();
                prefabMissingLogged = false;
                eventOutcomeByZoneKey = new Dictionary<string, StoneEventOutcome>(StringComparer.Ordinal);
                zonesGeneratedAtStart = new HashSet<string>(StringComparer.Ordinal);
                transientRetryByZoneKey = new Dictionary<string, int>(StringComparer.Ordinal);
                freshSkipWarned.Clear();
            }
            __instance.StartCoroutine(ReconcileLoop(__instance));
        }

        [HarmonyPatch(typeof(ZoneSystem), "OnDestroy")]
        [HarmonyPostfix]
        private static void OnZoneSystemDestroyed(ZoneSystem __instance)
        {
            if (!ReferenceEquals(scheduledFor, __instance)) return;
            scheduledFor = null;
            lock (StateGate)
            {
                selectionReady = false;
                selectedByZoneKey = new Dictionary<string, HomesteadAssignmentMetadata>(StringComparer.Ordinal);
                selectedRuntimeByZoneKey = new Dictionary<string, RuntimeCandidate>(StringComparer.Ordinal);
                migrationWarned.Clear();
                prefabMissingLogged = false;
                eventOutcomeByZoneKey = new Dictionary<string, StoneEventOutcome>(StringComparer.Ordinal);
                zonesGeneratedAtStart = new HashSet<string>(StringComparer.Ordinal);
                transientRetryByZoneKey = new Dictionary<string, int>(StringComparer.Ordinal);
                freshSkipWarned.Clear();
            }
        }

        /// <summary>Event-time creation seam. Runs after vanilla has freshly placed the location's structure
        /// (live colliders + live Heightmap + terrain leveling exist) and BEFORE ghost temp objects are
        /// destroyed. Realizes exactly one additive Stone for a freshly-placed selected host that has none.</summary>
        [HarmonyPatch(typeof(ZoneSystem), "PlaceLocations")]
        [HarmonyPostfix]
        private static void OnPlaceLocations(ZoneSystem __instance, Vector2i zoneID, Heightmap hmap, ZoneSystem.SpawnMode mode)
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (mode != ZoneSystem.SpawnMode.Ghost && mode != ZoneSystem.SpawnMode.Full) return;
                if (!EnsureSelection(__instance)) return;

                if (!__instance.m_locationInstances.TryGetValue(zoneID, out var instance) ||
                    instance.m_location == null || !instance.m_placed) return;
                if (!EligibleHosts.Contains(instance.m_location.m_prefabName)) return;

                var key = ZoneKey(zoneID);
                RuntimeCandidate runtime;
                HomesteadAssignmentMetadata metadata;
                lock (StateGate)
                {
                    if (!selectedRuntimeByZoneKey.TryGetValue(key, out runtime!)) return; // not a selected host
                    metadata = selectedByZoneKey[key];
                }

                var stoneAlreadyResident = HasResidentStone(zoneID);
                var action = HomesteadStoneLifecycle.DecideEventTime(isSelectedHost: true, stoneAlreadyResident);
                if (action != LocationRealizationAction.CreateFresh)
                {
                    // ReuseExisting (restart/retry) or NotSelected: never create a duplicate.
                    if (action == LocationRealizationAction.ReuseExisting)
                        RecordOutcome(key, StoneEventOutcome.AlreadyGeneratedReuse);
                    return;
                }

                var prefab = ZNetScene.instance?.GetPrefab(HomesteadStoneRegistrar.PrefabName);
                if (prefab == null)
                {
                    if (!prefabMissingLogged)
                    {
                        prefabMissingLogged = true;
                        Plugin.Log.LogError(
                            $"[Niflheim/HomesteadStones] Homestead Stone prefab '{HomesteadStoneRegistrar.PrefabName}' " +
                            "is not registered in ZNetScene; no Stone can realize. Check prefab registration/bundle load.");
                    }
                    // FIX R4 (#4): prefab-missing is a TRANSIENT fresh-event failure (creation-failed), not a
                    // pre-fix migration. Record provenance so the migration scan never relabels it.
                    RecordOutcome(key, StoneEventOutcome.FreshTransientFailure);
                    return;
                }
                prefabMissingLogged = false;

                if (!TryResolveEventSeat(runtime.Domain, hmap, out var position, out var attempt))
                {
                    // FIX R4 (#4): honest all-eight rejection is a TERMINAL fresh skip, distinct from both a
                    // transient failure and a pre-fix migration.
                    RecordOutcome(key, StoneEventOutcome.FreshInvalidSeats);
                    Plugin.Log.LogWarning(
                        $"[Niflheim/HomesteadStones] Selected host {runtime.Domain.Prefab} zone ({runtime.Domain.ZoneX}," +
                        $"{runtime.Domain.ZoneZ}) placed but all {SeatAttempts} deterministic seats were rejected against " +
                        "live host bounds (footprint overlap / insufficient clearance) or the live Heightmap could not " +
                        "resolve a ground height. No Stone created this event (terminal fresh skip, not migration).");
                    return;
                }

                if (!CreateStone(prefab, position, metadata, mode))
                {
                    // FIX R4 (#4): a stamp/instantiate failure with live geometry available is a TRANSIENT
                    // fresh failure eligible for bounded retry, never a migration.
                    RecordOutcome(key, StoneEventOutcome.FreshTransientFailure);
                    return;
                }

                RecordOutcome(key, StoneEventOutcome.FreshCreated);
                Plugin.Log.LogInfo(
                    $"[Niflheim/HomesteadStones] Realized {runtime.Domain.Prefab} zone ({runtime.Domain.ZoneX}," +
                    $"{runtime.Domain.ZoneZ}) at event time seat#{attempt}=({position.x:0.00},{position.y:0.00}," +
                    $"{position.z:0.00}) mode={mode}.");
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError($"[Niflheim/HomesteadStones] Event-time realization failed for zone {zoneID}: {exception}");
            }
        }

        /// <summary>Periodic reconcile: (re)register Stone Areas from resident Stone ZDOs, and emit exactly one
        /// migration-required diagnostic per selected host whose zone is ALREADY generated but has no Stone
        /// (pre-fix world). It never creates a Stone — creation is exclusively the one-shot event seam.</summary>
        private static System.Collections.IEnumerator ReconcileLoop(ZoneSystem zoneSystem)
        {
            while (!zoneSystem.LocationsGenerated)
                yield return new WaitForSeconds(1f);

            EnsureSelection(zoneSystem);

            while (ReferenceEquals(ZoneSystem.instance, zoneSystem))
            {
                ReconcileStoneAreas();
                DiagnoseExistingWorldMigration(zoneSystem);
                yield return new WaitForSeconds(RecheckSeconds);
            }
        }

        /// <summary>Compute the deterministic global selection once locations are generated. Idempotent.</summary>
        private static bool EnsureSelection(ZoneSystem zoneSystem)
        {
            lock (StateGate)
            {
                if (selectionReady) return true;
            }
            if (!zoneSystem.LocationsGenerated) return false;

            var identity = ResolveWorldIdentity();
            var instances = BuildCandidates(zoneSystem);
            var selection = HomesteadSelector.Select(
                instances.Select(candidate => candidate.Domain).ToList(),
                new HomesteadSelectionConfig(identity, SelectorVersion, MinimumDistance, Density));
            var byIdentity = instances.ToDictionary(candidate => candidate.Identity, candidate => candidate);

            var zoneMetadata = new Dictionary<string, HomesteadAssignmentMetadata>(StringComparer.Ordinal);
            var zoneRuntime = new Dictionary<string, RuntimeCandidate>(StringComparer.Ordinal);
            foreach (var candidate in selection.Selected)
            {
                var key = ZoneKey(new Vector2i(candidate.ZoneX, candidate.ZoneZ));
                zoneMetadata[key] = new HomesteadAssignmentMetadata(
                    identity, SelectorVersion, candidate.Prefab, candidate.ZoneX, candidate.ZoneZ);
                zoneRuntime[key] = byIdentity[Identity(candidate)];
            }

            lock (StateGate)
            {
                if (selectionReady) return true; // another caller won the race
                worldIdentity = identity;
                selectedByZoneKey = zoneMetadata;
                selectedRuntimeByZoneKey = zoneRuntime;
                selectionReady = true;
            }

            // FIX R4 (#4): snapshot which selected zones were ALREADY generated at the moment selection became
            // ready this session. On a fresh world the peer zones have not been ghost-spawned yet (false); on a
            // pre-fix world the zone is already generated (true). A selected zone that was generated-at-start,
            // still has no Stone, and sees no fresh event this session is the only genuine migration case.
            foreach (var candidate in selection.Selected)
            {
                var zone = new Vector2i(candidate.ZoneX, candidate.ZoneZ);
                if (IsZoneGenerated(zoneSystem, zone))
                {
                    lock (StateGate) { zonesGeneratedAtStart.Add(ZoneKey(zone)); }
                }
            }

            foreach (var warning in selection.Warnings)
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Selector target warning: " + warning);
            Plugin.Log.LogInfo(
                $"[Niflheim/HomesteadStones] Assignment ready world='{identity}' candidates={instances.Count} " +
                $"selected={selection.Selected.Count} minimumDistance={MinimumDistance:0}m density={Density:P0} selector={SelectorVersion}.");
            return true;
        }

        /// <summary>T009R4 + FIX R4 (#2) — reconcile ALL resident Homestead Stone ZDOs against the current
        /// selected set (metadata-aware), remove stale/unselected/duplicate Stones under the pre-ratification
        /// disposable-world policy, then (re)register the Stone Areas from the REAL kept resident Stone ZDOs.
        /// A removed/stale Stone leaves no kept entry for its zone, so it can never suppress fresh creation.</summary>
        private static void ReconcileStoneAreas()
        {
            var server = FoundationalPlacementObserver.Server;
            if (server == null) return;
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return;

            string identity;
            Dictionary<string, HomesteadAssignmentMetadata> selectedSnapshot;
            lock (StateGate)
            {
                identity = worldIdentity;
                selectedSnapshot = new Dictionary<string, HomesteadAssignmentMetadata>(selectedByZoneKey, StringComparer.Ordinal);
            }

            var found = new List<ZDO>();
            var index = 0;
            while (!zdoMan.GetAllZDOsWithPrefabIterative(HomesteadStoneRegistrar.PrefabName, found, ref index)) { }

            // Project resident ZDOs into pure records keyed by stable ZDOID for deterministic dedup.
            var records = new List<StoneZdoRecord>(found.Count);
            var zdoById = new Dictionary<long, ZDO>();
            foreach (var zdo in found)
            {
                if (zdo == null || !zdo.IsValid()) continue;
                int zoneX = zdo.GetInt(HomesteadStoneData.LocationZoneXKey, int.MinValue);
                int zoneZ = zdo.GetInt(HomesteadStoneData.LocationZoneZKey, int.MinValue);
                if (zoneX == int.MinValue || zoneZ == int.MinValue) continue;
                long id = zdo.m_uid.ID;
                records.Add(new StoneZdoRecord(
                    id,
                    zdo.GetString(HomesteadStoneData.WorldIdentityKey, string.Empty),
                    zdo.GetString(HomesteadStoneData.SelectorVersionKey, string.Empty),
                    zdo.GetString(HomesteadStoneData.HostPrefabKey, string.Empty),
                    zoneX, zoneZ));
                zdoById[id] = zdo;
            }

            // Build the expected selected identities keyed by zone.
            var expectations = new Dictionary<string, SelectedStoneExpectation>(StringComparer.Ordinal);
            foreach (var pair in selectedSnapshot)
            {
                var m = pair.Value;
                expectations[pair.Key] = new SelectedStoneExpectation(
                    m.WorldIdentity, m.SelectorVersion, m.Prefab, m.ZoneX, m.ZoneZ);
            }

            var reconcile = HomesteadStoneReconciler.Reconcile(records, expectations);

            var world = new WorldId(identity);
            var facts = new List<StoneAreaRegistrar.StoneAreaFact>();
            var removed = 0;
            foreach (var decision in reconcile.Decisions)
            {
                if (!zdoById.TryGetValue(decision.ZdoId, out var zdo)) continue;
                if (decision.Disposition == StoneReconcileDisposition.Reuse)
                {
                    var stoneId = StoneId.FromHostZone(world, decision.ZoneX, decision.ZoneZ);
                    Vector3 pos = zdo.GetPosition();
                    facts.Add(new StoneAreaRegistrar.StoneAreaFact(
                        stoneId, pos.x, pos.z, Domain.StoneProgression.StoneAreaMembership.DefaultAreaRadius));
                }
                else
                {
                    // Remove or RemoveDuplicate: owner-authoritatively destroy the stale/unselected/duplicate ZDO.
                    if (!zdo.IsOwner()) zdo.SetOwner(ZDOMan.GetSessionID());
                    zdoMan.DestroyZDO(zdo);
                    removed++;
                    Plugin.Log.LogInfo(
                        $"[Niflheim/HomesteadStones] reconcile removed {decision.Disposition} Stone ZDO id={decision.ZdoId} " +
                        $"zone ({decision.ZoneX},{decision.ZoneZ}) — not in current selected set / metadata mismatch / duplicate.");
                }
            }

            var result = StoneAreaRegistrar.Reconcile(server.StoneAreas, facts);
            if (result.Registered > 0 || result.Updated > 0 || result.Unregistered > 0 || removed > 0)
                Plugin.Log.LogInfo($"[Niflheim/HomesteadStones] {result} stonesRemoved={removed}");
        }

        /// <summary>FIX R4 (#4) — classify each selected host missing its Stone using PROVENANCE recorded this
        /// session, so a fresh-event failure is never mislabelled as a pre-fix migration. Only a zone that was
        /// already generated at session start, still has no Stone, and saw NO fresh event this session is a
        /// true migration. Fresh invalid-seat skips and transient failures get their own distinct diagnostics.</summary>
        private static void DiagnoseExistingWorldMigration(ZoneSystem zoneSystem)
        {
            List<KeyValuePair<string, RuntimeCandidate>> selectedSnapshot;
            HashSet<string> generatedAtStart;
            Dictionary<string, StoneEventOutcome> outcomes;
            lock (StateGate)
            {
                if (!selectionReady) return;
                selectedSnapshot = selectedRuntimeByZoneKey.ToList();
                generatedAtStart = new HashSet<string>(zonesGeneratedAtStart, StringComparer.Ordinal);
                outcomes = new Dictionary<string, StoneEventOutcome>(eventOutcomeByZoneKey, StringComparer.Ordinal);
            }

            foreach (var pair in selectedSnapshot)
            {
                var key = pair.Key;
                var runtime = pair.Value;
                bool resident = HasResidentStone(runtime.Zone);
                bool generated = generatedAtStart.Contains(key);
                var outcome = outcomes.TryGetValue(key, out var recorded) ? recorded : StoneEventOutcome.Unknown;

                var classification = HomesteadMigrationClassifier.Classify(
                    isSelectedHost: true, zoneGeneratedOnStart: generated, stoneResident: resident, freshOutcome: outcome);

                switch (classification)
                {
                    case MigrationClassification.MigrationRequired:
                        if (!migrationWarned.Add(key)) break;
                        Plugin.Log.LogWarning(
                            $"[Niflheim/HomesteadStones] migration-required: selected host {runtime.Domain.Prefab} zone " +
                            $"({runtime.Domain.ZoneX},{runtime.Domain.ZoneZ}) was generated by a pre-fix world and has no " +
                            "resident Homestead Stone. Its one-shot fresh-generation placement event already fired before " +
                            "this fix and its live geometry is gone; this build does NOT reconstruct geometry from persisted " +
                            "ZDOs. Regenerate a disposable fresh world (Astley seed reproduces the layout) to realize this " +
                            "Stone. A future migration/provider seam is preserved for pre-release.");
                        break;
                    case MigrationClassification.FreshInvalidSeats:
                        if (!freshSkipWarned.Add(key)) break;
                        Plugin.Log.LogWarning(
                            $"[Niflheim/HomesteadStones] fresh-skip (not migration): selected host {runtime.Domain.Prefab} " +
                            $"zone ({runtime.Domain.ZoneX},{runtime.Domain.ZoneZ}) had a live fresh-generation event this " +
                            "session but all eight deterministic seats were invalid against live host bounds. Terminal skip; " +
                            "no retry, and NOT a pre-fix migration.");
                        break;
                    case MigrationClassification.FreshTransientFailure:
                        HandleTransientRetry(key, runtime);
                        break;
                    case MigrationClassification.None:
                    default:
                        break;
                }
            }
        }

        /// <summary>FIX R4 (#4) — bounded retry policy for a TRANSIENT fresh-creation failure. If authoritative
        /// geometry is still available (the zone's live host colliders + Heightmap still exist because the zone
        /// is NOT yet marked generated), re-attempt creation up to <see cref="MaxTransientRetries"/> times.
        /// Once the zone is generated (geometry gone) the failure becomes terminal and is reported as
        /// creation-failed, still distinct from a pre-fix migration.</summary>
        private static void HandleTransientRetry(string key, RuntimeCandidate runtime)
        {
            var zoneSystem = ZoneSystem.instance;
            if (zoneSystem == null) return;

            bool geometryGone = IsZoneGenerated(zoneSystem, runtime.Zone);
            int attempts;
            lock (StateGate)
            {
                transientRetryByZoneKey.TryGetValue(key, out attempts);
            }

            if (geometryGone || attempts >= MaxTransientRetries)
            {
                if (freshSkipWarned.Add(key))
                    Plugin.Log.LogWarning(
                        $"[Niflheim/HomesteadStones] creation-failed (not migration): selected host {runtime.Domain.Prefab} " +
                        $"zone ({runtime.Domain.ZoneX},{runtime.Domain.ZoneZ}) had a transient fresh-creation failure and " +
                        (geometryGone
                            ? "the authoritative live geometry is no longer available (zone generated); "
                            : $"exhausted the bounded retry budget ({MaxTransientRetries}); ") +
                        "no Stone this session. This is a fresh creation failure, NOT a pre-fix migration.");
                return;
            }

            // Geometry still live: bump the counter. The one-shot PlaceLocations event will not re-fire, so a
            // true re-attempt is only possible if the zone gets re-realized; recording the attempt keeps the
            // diagnosis honest and bounded without forcing geometry.
            lock (StateGate)
            {
                transientRetryByZoneKey[key] = attempts + 1;
            }
        }

        /// <summary>Record the fresh-event provenance outcome for a zone key (thread-safe).</summary>
        private static void RecordOutcome(string key, StoneEventOutcome outcome)
        {
            lock (StateGate) { eventOutcomeByZoneKey[key] = outcome; }
        }

        /// <summary>Vanilla <c>ZoneSystem.IsZoneGenerated(Vector2i)</c> is private; call it via Traverse.</summary>
        private static bool IsZoneGenerated(ZoneSystem zoneSystem, Vector2i zone) =>
            Traverse.Create(zoneSystem)
                .Method("IsZoneGenerated", new[] { typeof(Vector2i) }, new object[] { zone })
                .GetValue<bool>();

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

        /// <summary>Resolve the best-of-eight seat against LIVE host geometry at event time. Host structural
        /// bounds come from real freshly-spawned host colliders (not ZDO pivots); the final Y comes from the
        /// live Heightmap. Returns false on an honest 8-of-8 rejection or an unresolved ground height.</summary>
        private static bool TryResolveEventSeat(
            HomesteadCandidate candidate, Heightmap hmap, out Vector3 position, out int attempt)
        {
            position = default;
            attempt = -1;

            // FIX R4 (#1): finalize the live terrain BEFORE sampling. Vanilla's TerrainModifier.Awake pokes
            // each covering Heightmap with a DELAYED rebuild (m_doLateUpdate) that only regenerates later in
            // Heightmap.CustomLateUpdate. This postfix runs synchronously before SpawnZone resumes, so a
            // covering Heightmap can still carry a queued pre-leveling surface. We force the SPECIFIC covering
            // Heightmap(s) to their final queued state now — while the location modifiers and temporary host
            // geometry still exist — using the narrowest safe vanilla ops (instance HaveQueuedRebuild() then
            // Regenerate()), NOT the global Heightmap.ForceGenerateAll().
            FinalizeCoveringTerrain(candidate);

            // Flush the just-instantiated host transforms + regenerated terrain colliders into the physics
            // engine so the overlap query and height sample see the final leveled state without a FixedUpdate.
            Physics.SyncTransforms();

            var bounds = CaptureLiveHostBounds(candidate);
            if (!bounds.HasBounds) return false;

            var seats = HomesteadSeatGenerator.Generate(worldIdentity, SelectorVersion, candidate, SeatAttempts);
            var eventSeats = new List<EventSeat>(seats.Count);
            foreach (var s in seats) eventSeats.Add(new EventSeat(s.Attempt, s.X, s.Z));

            // FIX R4 (#3): score against the ACTUAL structural bounds (LiveHostBounds.Extent), not the coarse
            // location radius. The location radius survives only as the hard host-attribution / seat-ring
            // constraint applied in CaptureLiveHostBounds / IsHostStructure above.
            var chosen = HomesteadEventSeatScorer.ChooseBest(
                eventSeats, bounds, candidate.X, candidate.Z);
            if (!chosen.HasSeat) return false;

            var probe = new Vector3((float)chosen.X, 0f, (float)chosen.Z);
            var height = ResolveGroundHeight(hmap, probe);
            if (!height.HasValue) return false;

            position = new Vector3((float)chosen.X, height.Value, (float)chosen.Z);
            attempt = chosen.Attempt;
            return true;
        }

        /// <summary>FIX R4 (#1) — force the covering Heightmap(s) over the host footprint to their final
        /// queued leveled state before sampling. Finds only the Heightmap(s) that cover the host point +
        /// footprint radius (narrowest scope), and for each with a queued rebuild calls the instance
        /// Regenerate() to drain the delayed Poke deterministically now, rather than waiting for a
        /// CustomLateUpdate that may not have run yet inside this synchronous PlaceLocations postfix.</summary>
        private static void FinalizeCoveringTerrain(HomesteadCandidate host)
        {
            var probeCenter = new Vector3((float)host.X, 0f, (float)host.Z);
            var radius = (float)Math.Max(12.0, host.LocationRadius + 6.0);

            // Decide (pure) whether any covering Heightmap has a queued rebuild.
            var covering = new List<Heightmap>();
            Heightmap.FindHeightmap(probeCenter, radius, covering);
            var anyQueued = false;
            foreach (var heightmap in covering)
            {
                if (heightmap != null && heightmap.HaveQueuedRebuild()) { anyQueued = true; break; }
            }

            var action = HomesteadTerrainFinalization.Decide(anyQueued);
            if (action != TerrainFinalizationAction.ForceRegenerate) return;

            foreach (var heightmap in covering)
            {
                if (heightmap != null && heightmap.HaveQueuedRebuild())
                    heightmap.Regenerate();
            }
        }

        /// <summary>Build the host structural AABB from the freshly-spawned host colliders present at event
        /// time. Physics.SyncTransforms flushes the just-instantiated transforms into the physics engine so
        /// the overlap query sees them without waiting for a FixedUpdate. Only enabled, non-trigger colliders
        /// on host-attributed Pieces (creator==0, inside the location radius) contribute — the same live
        /// attribution the prior listen-server path used.</summary>
        private static LiveHostBounds CaptureLiveHostBounds(HomesteadCandidate host)
        {
            Physics.SyncTransforms();
            var probeCenter = new Vector3((float)host.X, 0f, (float)host.Z);
            if (Heightmap.GetHeight(probeCenter, out var centerHeight) && !float.IsNaN(centerHeight))
                probeCenter.y = centerHeight;

            var colliders = Physics.OverlapSphere(
                probeCenter,
                (float)Math.Max(12.0, host.LocationRadius + 6.0),
                CollisionMask,
                QueryTriggerInteraction.Ignore);

            var have = false;
            double minX = 0, minZ = 0, maxX = 0, maxZ = 0;
            foreach (var collider in colliders)
            {
                if (!IsHostStructure(host, collider)) continue;
                var b = collider.bounds;
                if (!have)
                {
                    have = true;
                    minX = b.min.x; minZ = b.min.z; maxX = b.max.x; maxZ = b.max.z;
                }
                else
                {
                    if (b.min.x < minX) minX = b.min.x;
                    if (b.min.z < minZ) minZ = b.min.z;
                    if (b.max.x > maxX) maxX = b.max.x;
                    if (b.max.z > maxZ) maxZ = b.max.z;
                }
            }
            return new LiveHostBounds(minX, minZ, maxX, maxZ, have);
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

        /// <summary>Live ground height under the seat: the public static Heightmap resolver over whatever
        /// Heightmap covers the point (the same call vanilla's own GetHeight uses). The zone hmap is present
        /// as evidence the event is live, but its per-instance sampler is not public API.</summary>
        private static float? ResolveGroundHeight(Heightmap hmap, Vector3 point)
        {
            if (Heightmap.GetHeight(point, out var height) && !float.IsNaN(height) && !float.IsInfinity(height))
                return height;
            return null;
        }

        /// <summary>Instantiate the additive Stone. In Ghost mode we bracket the instantiate in
        /// StartGhostInit/FinishGhostInit so the ZNetView creates a PERSISTENT ZDO in ZDOMan and returns
        /// before AddInstance (verified against decompiled ZNetView.Awake); the temp GameObject is then
        /// destroyed but its ZDO persists and is saved with the world, exactly like vanilla's ghost-spawned
        /// location structures. In Full mode the Stone stays a live scene instance. Identity is stamped
        /// atomically on the created ZDO; a stamping failure destroys the instance and creates nothing.</summary>
        private static bool CreateStone(GameObject prefab, Vector3 position, HomesteadAssignmentMetadata metadata, ZoneSystem.SpawnMode mode)
        {
            var ghost = mode == ZoneSystem.SpawnMode.Ghost;
            if (ghost) ZNetView.StartGhostInit();
            GameObject instance;
            try
            {
                instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
                instance.name = HomesteadStoneRegistrar.PrefabName;
                instance.transform.SetParent(null, true);

                if (!StampIdentity(instance.GetComponent<ZNetView>(), metadata))
                {
                    DestroyInstance(instance);
                    Plugin.Log.LogError(
                        $"[Niflheim/HomesteadStones] Destroyed unkeyed Stone at zone ({metadata.ZoneX},{metadata.ZoneZ}); " +
                        "ZNetView/ZDO identity stamping failed.");
                    return false;
                }
            }
            finally
            {
                if (ghost) ZNetView.FinishGhostInit();
            }

            if (ghost)
            {
                // The persistent ZDO now lives in ZDOMan; the temp GameObject is not needed on the server and
                // is destroyed like vanilla's ghost temp objects. The ZDO survives (OnDestroy never destroys it).
                UnityEngine.Object.Destroy(instance);
            }
            return true;
        }

        private static void DestroyInstance(GameObject instance)
        {
            if (ZNetScene.instance != null) ZNetScene.instance.Destroy(instance);
            else UnityEngine.Object.Destroy(instance);
        }

        /// <summary>True when a resident Stone ZDO already exists for the given host zone coord (any world
        /// identity match / unset). Guarantees idempotence and restart-reuse: the event seam creates only when
        /// this is false.</summary>
        private static bool HasResidentStone(Vector2i zone)
        {
            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return false;
            var found = new List<ZDO>();
            var index = 0;
            while (!zdoMan.GetAllZDOsWithPrefabIterative(HomesteadStoneRegistrar.PrefabName, found, ref index)) { }
            foreach (var zdo in found)
            {
                if (zdo == null || !zdo.IsValid()) continue;
                var x = zdo.GetInt(HomesteadStoneData.LocationZoneXKey, int.MinValue);
                var z = zdo.GetInt(HomesteadStoneData.LocationZoneZKey, int.MinValue);
                if (x == zone.x && z == zone.y) return true;
            }
            return false;
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
