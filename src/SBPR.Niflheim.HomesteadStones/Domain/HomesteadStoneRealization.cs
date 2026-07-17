using System;

namespace SBPR.Niflheim.HomesteadStones.Domain
{
    // FIX R3 (event-time Homestead Stone realization) — engine-free lifecycle decisions.
    //
    // WHY THIS REPLACES HomesteadHeadlessSeat
    // R1/R2 reconstructed a host's footprint and leveled surface from generic persisted ZDO pivots on a
    // headless dedicated server. Fresh review (PR #323) rejected that as unsound: a ZDO transform pivot is
    // neither a collider bound nor a terrain sample, the creator==0-in-radius harvest also swept in the
    // LocationProxy / zone-control / vegetation, and the tests only exercised synthetic pre-labelled points.
    //
    // The correct seam is EVENT-TIME. Vanilla realizes a fresh zone via
    // `ZoneSystem.SpawnZone(Ghost|Full)` -> `PlaceLocations(zoneID, .., hmap, .., mode, spawnedObjects)`
    // (decompiled vanilla, base-game RE per ADR-0001). Inside `PlaceLocations` the host's own ZNetView
    // children are freshly `Instantiate`d at their final world positions with real colliders, the live
    // `Heightmap` is passed in, and the location's `TerrainModifier`/`TerrainComp` leveling has been applied.
    // Only AFTER `PlaceLocations` returns does `SpawnZone` destroy the ghost temp objects (Ghost mode). A
    // Harmony POSTFIX on `PlaceLocations` therefore observes the real geometry, on BOTH a listen server
    // (Full) and a headless dedicated server (Ghost), while it still exists — exactly the window R1/R2 tried
    // to reconstruct after the fact. ZDO persistence is retained by bracketing the additive Stone's creation
    // in `ZNetView.StartGhostInit()`/`FinishGhostInit()` in Ghost mode, mirroring how vanilla persists the
    // host structure ZDOs that survive the temp-object destruction.
    //
    // `PlaceLocations` also fires EXACTLY ONCE per zone for the lifetime of a world (vanilla guards the body
    // with `!value.m_placed`, and `SpawnZone` guards on `!IsZoneGenerated(zoneID)` and calls
    // `SetZoneGenerated` afterwards). So the fresh-creation event is one-shot: a restart re-loads the
    // persisted Stone ZDO instead and the postfix never re-fires for that zone. An already-generated host
    // that has NO Stone can therefore only be a pre-existing (pre-fix) world — a deferred migration case,
    // never a fresh-creation opportunity.
    //
    // This type holds those decisions as PURE logic so they are unit-tested under net8 and cannot silently
    // regress. Live geometry (collider bounds, Heightmap height) is supplied BY the engine-bound caller as
    // real numbers — this module never infers geometry from ZDO pivots.
    //
    // net48 audit: System only. Link-compiles into the net8 test project.

    /// <summary>What the event-time realizer should do for a zone whose vanilla host location was just
    /// placed. Stable ordinal — surfaced only as a diagnostic/branch selector, never persisted.</summary>
    public enum LocationRealizationAction
    {
        /// <summary>The just-placed host is not in the deterministic selected set: create nothing.</summary>
        NotSelected = 0,

        /// <summary>The host is selected and a Stone ZDO for its host zone already exists (restart / retry):
        /// reuse it, create nothing. Guarantees exactly one Stone per selected zone.</summary>
        ReuseExisting = 1,

        /// <summary>The host is selected, freshly placed, and has no resident Stone: evaluate the live seat
        /// and create exactly one additive Stone through ghost/full ZNetView init.</summary>
        CreateFresh = 2,
    }

    /// <summary>Pure event-time realization decision. The engine-bound `PlaceLocations` postfix supplies two
    /// server-owned booleans — is this just-placed host in the selected set, and does a Stone ZDO already
    /// exist for its host zone — and receives the branch to take. No geometry is inferred here.</summary>
    public static class HomesteadStoneLifecycle
    {
        public static LocationRealizationAction DecideEventTime(bool isSelectedHost, bool stoneAlreadyResident)
        {
            if (!isSelectedHost) return LocationRealizationAction.NotSelected;
            if (stoneAlreadyResident) return LocationRealizationAction.ReuseExisting;
            return LocationRealizationAction.CreateFresh;
        }
    }

    /// <summary>Live host-structure geometry captured AT EVENT TIME from the freshly spawned host colliders —
    /// a real world-space axis-aligned XZ bounding box plus the leveled ground height under a probe point.
    /// The engine-bound caller fills these from actual `Collider.bounds` and `Heightmap.GetHeight`, NOT from
    /// ZDO pivots. This struct is the seam that lets the seat scorer stay engine-free and unit-tested.</summary>
    public readonly struct LiveHostBounds
    {
        public LiveHostBounds(double minX, double minZ, double maxX, double maxZ, bool hasBounds)
        {
            MinX = minX;
            MinZ = minZ;
            MaxX = maxX;
            MaxZ = maxZ;
            HasBounds = hasBounds;
        }

        public double MinX { get; }
        public double MinZ { get; }
        public double MaxX { get; }
        public double MaxZ { get; }

        /// <summary>False when no host structural collider was captured this event (degenerate location):
        /// the caller must skip rather than seat against an empty footprint.</summary>
        public bool HasBounds { get; }

        /// <summary>Horizontal distance from a point to this AABB (0 inside), the clearance metric the live
        /// listen-server path already uses. Pure geometry over REAL collider bounds.</summary>
        public double HorizontalClearance(double x, double z)
        {
            var closestX = Clamp(x, MinX, MaxX);
            var closestZ = Clamp(z, MinZ, MaxZ);
            var dx = x - closestX;
            var dz = z - closestZ;
            return Math.Sqrt((dx * dx) + (dz * dz));
        }

        /// <summary>Largest half-extent of the AABB — the readable-yard-band reference the score uses.</summary>
        public double Extent => Math.Max((MaxX - MinX) * 0.5, (MaxZ - MinZ) * 0.5);

        private static double Clamp(double value, double min, double max) =>
            value < min ? min : (value > max ? max : value);
    }

    /// <summary>A deterministic seat reduced to the XZ the pure event-time scorer needs.</summary>
    public readonly struct EventSeat
    {
        public EventSeat(int attempt, double x, double z)
        {
            Attempt = attempt;
            X = x;
            Z = z;
        }

        public int Attempt { get; }
        public double X { get; }
        public double Z { get; }
    }

    /// <summary>A resolved event-time seat: the chosen attempt's XZ and the live-Heightmap ground Y under it.</summary>
    public readonly struct EventSeatResult
    {
        public EventSeatResult(bool hasSeat, int attempt, double x, double z, int attemptsEvaluated)
        {
            HasSeat = hasSeat;
            Attempt = attempt;
            X = x;
            Z = z;
            AttemptsEvaluated = attemptsEvaluated;
        }

        public bool HasSeat { get; }
        public int Attempt { get; }
        public double X { get; }
        public double Z { get; }
        public int AttemptsEvaluated { get; }
    }

    /// <summary>Pure, best-of-eight event-time seat scorer over REAL host-collider bounds. Identical scoring
    /// shape to the prior live <c>SeatEvaluation.Score</c>, but expressed engine-free so BOTH the listen
    /// server (Full) and the headless dedicated server (Ghost) share one unit-tested contract — each now runs
    /// inside the <c>PlaceLocations</c> event where the host colliders and Heightmap are live. The engine-
    /// bound caller resolves each seat's clearance from actual <see cref="LiveHostBounds"/> and supplies the
    /// live ground height; this scorer never touches ZDO pivots.</summary>
    public static class HomesteadEventSeatScorer
    {
        /// <summary>Minimum clearance (m) between the Stone capsule and the host structural AABB.</summary>
        public const double KeepOut = 1.75;

        /// <summary>Score every seat against the live host bounds and pick the best valid one (or none).
        /// A seat is rejected when its live horizontal clearance from the real collider AABB is below
        /// <see cref="KeepOut"/>.
        ///
        /// FIX R4 (#3): the yard/readability band is measured against the ACTUAL structural bounds
        /// (<see cref="LiveHostBounds.Extent"/> — the real freshly-spawned host collider AABB half-extent),
        /// NOT the vanilla <c>ZoneLocation.m_exteriorRadius</c> that R3 passed in. The location radius is a
        /// coarse authoring circle (often much larger than the built footprint) and made the scorer favour
        /// seats far outside the structure; the structural extent centres the readable yard band on the real
        /// building edge. The location radius is retained only as a hard host-attribution / seat-ring
        /// constraint upstream (collider attribution in the engine caller), never as the scoring reference.</summary>
        public static EventSeatResult ChooseBest(
            System.Collections.Generic.IReadOnlyList<EventSeat> seats,
            LiveHostBounds bounds,
            double hostCenterX,
            double hostCenterZ)
        {
            if (seats == null) throw new ArgumentNullException(nameof(seats));
            if (!bounds.HasBounds)
                return new EventSeatResult(false, -1, 0.0, 0.0, seats.Count);

            // Yard band is centred on the real structural edge: the half-extent of the live collider AABB.
            var structuralExtent = bounds.Extent;

            var found = false;
            var bestAttempt = -1;
            double bestX = 0.0, bestZ = 0.0;
            var bestScore = double.NegativeInfinity;

            foreach (var seat in seats)
            {
                var clearance = bounds.HorizontalClearance(seat.X, seat.Z);
                if (clearance < KeepOut) continue;
                var radial = Distance(seat.X, seat.Z, hostCenterX, hostCenterZ);
                var score = Score(clearance, radial, structuralExtent);
                if (double.IsNegativeInfinity(score)) continue;
                if (!found || score > bestScore || (score.Equals(bestScore) && seat.Attempt < bestAttempt))
                {
                    found = true;
                    bestAttempt = seat.Attempt;
                    bestX = seat.X;
                    bestZ = seat.Z;
                    bestScore = score;
                }
            }

            return new EventSeatResult(found, bestAttempt, bestX, bestZ, seats.Count);
        }

        private static double Score(double clearance, double radialDistance, double structuralExtent)
        {
            if (clearance < KeepOut) return double.NegativeInfinity;
            var yardBand = Math.Max(0.0, Math.Min(1.0, 1.0 - (Math.Abs(radialDistance - (structuralExtent + 2.5)) / 5.0)));
            return 100.0 + (clearance * 4.0) + (yardBand * 8.0) - (Math.Max(0.0, radialDistance - 12.0) * 2.0);
        }

        private static double Distance(double ax, double az, double bx, double bz)
        {
            var dx = ax - bx;
            var dz = az - bz;
            return Math.Sqrt((dx * dx) + (dz * dz));
        }
    }

    // ================================================================================================
    // FIX R4 (#1) — Terrain finalization decision (pure).
    // ================================================================================================

    /// <summary>What the event-time realizer must do about the live Heightmap(s) covering a freshly-placed
    /// host BEFORE it samples a ground height. Pure decision over vanilla-observable flags.</summary>
    public enum TerrainFinalizationAction
    {
        /// <summary>No covering Heightmap has a queued rebuild: terrain is already at its final leveled
        /// state, sample directly.</summary>
        AlreadyFinal = 0,

        /// <summary>At least one covering Heightmap has a queued (delayed) rebuild from the location's
        /// TerrainModifier leveling: force it to regenerate now, while the modifiers and temporary host
        /// geometry still exist, before sampling.</summary>
        ForceRegenerate = 1,
    }

    /// <summary>Pure decision for FIX R4 (#1). Vanilla's <c>TerrainModifier.Awake</c> pokes each covering
    /// Heightmap with <c>Poke(delayed:true)</c> (sets <c>m_doLateUpdate</c>); the actual <c>Regenerate()</c>
    /// only runs later in <c>Heightmap.CustomLateUpdate</c>. Our <c>PlaceLocations</c> postfix runs
    /// synchronously before <c>SpawnZone</c> resumes, so a covering Heightmap can still have a queued rebuild
    /// and its sampled Y would be pre-leveling. This decision tells the engine caller whether it must force
    /// the specific covering Heightmap(s) to their final queued state (narrowest safe op:
    /// <c>HaveQueuedRebuild()</c> then instance <c>Regenerate()</c>) before <c>Physics.SyncTransforms</c> and
    /// seat evaluation.</summary>
    public static class HomesteadTerrainFinalization
    {
        public static TerrainFinalizationAction Decide(bool anyCoveringHeightmapHasQueuedRebuild) =>
            anyCoveringHeightmapHasQueuedRebuild
                ? TerrainFinalizationAction.ForceRegenerate
                : TerrainFinalizationAction.AlreadyFinal;
    }

    // ================================================================================================
    // FIX R4 (#4) — Event-outcome provenance (pure).
    // ================================================================================================

    /// <summary>Provenance of a per-world/zone Homestead Stone realization outcome, tracked at the moment it
    /// happens (before vanilla marks the zone generated) so the periodic migration scan can distinguish a
    /// genuine pre-fix missing-Stone world from a fresh-event failure. Stable ordinal; diagnostic only.</summary>
    public enum StoneEventOutcome
    {
        /// <summary>No decision recorded for this zone yet this session.</summary>
        Unknown = 0,

        /// <summary>Fresh generation event fired and a Stone was created from live geometry.</summary>
        FreshCreated = 1,

        /// <summary>Fresh event fired, host was selected, but all eight deterministic seats were invalid
        /// against the live host bounds (or no ground height resolved). Terminal skip — NOT a migration.</summary>
        FreshInvalidSeats = 2,

        /// <summary>Fresh event fired but creation failed transiently (prefab missing, stamp failure, or an
        /// exception) while authoritative geometry was available. Bounded retry candidate — NOT a migration.</summary>
        FreshTransientFailure = 3,

        /// <summary>Zone was already generated on start and a matching persisted Stone exists: reuse.</summary>
        AlreadyGeneratedReuse = 4,

        /// <summary>Zone was already generated on start and no Stone exists, and no fresh event was ever
        /// observed this session: genuine pre-fix world requiring deferred migration.</summary>
        MigrationRequired = 5,
    }

    /// <summary>How the migration scan should classify a selected host that is missing its Stone, GIVEN the
    /// provenance recorded for its zone during this session. This is the FIX R4 (#4) fix: a fresh-event
    /// failure (invalid seats / transient) must NOT be relabelled as pre-fix migration just because vanilla
    /// has since marked the zone generated.</summary>
    public enum MigrationClassification
    {
        /// <summary>Nothing to report: not selected, Stone resident, or a fresh outcome already explains the
        /// state (created, invalid-seats terminal skip, or transient failure handled by retry).</summary>
        None = 0,

        /// <summary>Genuine pre-fix world: zone generated on start, no Stone, and no fresh event was observed
        /// this session. Emit the migration-required diagnostic (once).</summary>
        MigrationRequired = 1,

        /// <summary>Fresh event this session produced an honest 8-of-8 invalid-seat skip. Surface as a
        /// terminal fresh-skip warning, distinct from migration.</summary>
        FreshInvalidSeats = 2,

        /// <summary>Fresh event this session failed transiently (prefab/stamp/exception). Surface as a
        /// creation-failed diagnosis eligible for bounded retry, distinct from migration.</summary>
        FreshTransientFailure = 3,
    }

    /// <summary>Pure classifier for FIX R4 (#4). Given the server-owned facts about a selected host — whether
    /// its zone was already generated when the world started, whether a Stone is resident now, and the
    /// provenance recorded when its fresh event (if any) fired this session — it returns how the migration
    /// scan must classify it. Only a zone that was already generated on start, still has no Stone, AND saw no
    /// fresh event this session is a true migration.</summary>
    public static class HomesteadMigrationClassifier
    {
        public static MigrationClassification Classify(
            bool isSelectedHost, bool zoneGeneratedOnStart, bool stoneResident, StoneEventOutcome freshOutcome)
        {
            if (!isSelectedHost || stoneResident) return MigrationClassification.None;

            switch (freshOutcome)
            {
                case StoneEventOutcome.FreshCreated:
                    // A fresh Stone was created this session; if it is somehow not resident now that is a
                    // separate durability concern, never migration.
                    return MigrationClassification.None;
                case StoneEventOutcome.FreshInvalidSeats:
                    return MigrationClassification.FreshInvalidSeats;
                case StoneEventOutcome.FreshTransientFailure:
                    return MigrationClassification.FreshTransientFailure;
                default:
                    // No fresh event observed this session.
                    return zoneGeneratedOnStart
                        ? MigrationClassification.MigrationRequired
                        : MigrationClassification.None; // not generated + no event yet → event will handle it.
            }
        }
    }

    // ================================================================================================
    // FIX R4 (#2) — Metadata-aware selected-set ZDO reconciliation (pure).
    // ================================================================================================

    /// <summary>A minimal, engine-free projection of one resident Homestead Stone ZDO for reconciliation:
    /// its stable ZDO id (for deterministic duplicate tie-break) and the stamped identity metadata. The
    /// engine caller fills these from the real ZDO (<c>ZDOID</c> + the stamped keys), never from geometry.</summary>
    public readonly struct StoneZdoRecord
    {
        public StoneZdoRecord(long zdoId, string worldIdentity, string selectorVersion, string hostPrefab, int zoneX, int zoneZ)
        {
            ZdoId = zdoId;
            WorldIdentity = worldIdentity ?? string.Empty;
            SelectorVersion = selectorVersion ?? string.Empty;
            HostPrefab = hostPrefab ?? string.Empty;
            ZoneX = zoneX;
            ZoneZ = zoneZ;
        }

        /// <summary>Stable, deterministic ordering key for duplicate resolution (the vanilla ZDOID's uint id).</summary>
        public long ZdoId { get; }
        public string WorldIdentity { get; }
        public string SelectorVersion { get; }
        public string HostPrefab { get; }
        public int ZoneX { get; }
        public int ZoneZ { get; }
    }

    /// <summary>The expected identity of a currently-selected host zone, keyed by zone coord.</summary>
    public readonly struct SelectedStoneExpectation
    {
        public SelectedStoneExpectation(string worldIdentity, string selectorVersion, string hostPrefab, int zoneX, int zoneZ)
        {
            WorldIdentity = worldIdentity ?? string.Empty;
            SelectorVersion = selectorVersion ?? string.Empty;
            HostPrefab = hostPrefab ?? string.Empty;
            ZoneX = zoneX;
            ZoneZ = zoneZ;
        }

        public string WorldIdentity { get; }
        public string SelectorVersion { get; }
        public string HostPrefab { get; }
        public int ZoneX { get; }
        public int ZoneZ { get; }

        internal bool MetadataMatches(StoneZdoRecord record) =>
            string.Equals(WorldIdentity, record.WorldIdentity, StringComparison.Ordinal) &&
            string.Equals(SelectorVersion, record.SelectorVersion, StringComparison.Ordinal) &&
            string.Equals(HostPrefab, record.HostPrefab, StringComparison.Ordinal) &&
            ZoneX == record.ZoneX && ZoneZ == record.ZoneZ;
    }

    /// <summary>The disposition of a single resident Stone ZDO after reconciliation.</summary>
    public enum StoneReconcileDisposition
    {
        /// <summary>Matches the current selected set (world/selector/host prefab/zone) and is the kept
        /// canonical Stone for its zone: reuse.</summary>
        Reuse = 0,

        /// <summary>Absent from the current selected set, or its metadata mismatches the selected expectation
        /// for its zone: remove under the current pre-ratification disposable-world policy.</summary>
        Remove = 1,

        /// <summary>A duplicate matching ZDO for a zone whose canonical Stone is kept elsewhere: remove the
        /// extra deterministically.</summary>
        RemoveDuplicate = 2,
    }

    /// <summary>One reconciliation decision: the ZDO id and what to do with it.</summary>
    public readonly struct StoneReconcileDecision
    {
        public StoneReconcileDecision(long zdoId, int zoneX, int zoneZ, StoneReconcileDisposition disposition)
        {
            ZdoId = zdoId;
            ZoneX = zoneX;
            ZoneZ = zoneZ;
            Disposition = disposition;
        }

        public long ZdoId { get; }
        public int ZoneX { get; }
        public int ZoneZ { get; }
        public StoneReconcileDisposition Disposition { get; }
    }

    /// <summary>The full result of a reconciliation pass: per-ZDO decisions plus the set of zone coords that
    /// have a kept (reused) canonical Stone. A currently-selected zone NOT present in <see cref="KeptZoneKeys"/>
    /// has no valid resident Stone and must remain eligible for fresh event creation — a stale/removed Stone
    /// must never suppress fresh creation.</summary>
    public sealed class StoneReconcileResult
    {
        public StoneReconcileResult(
            System.Collections.Generic.IReadOnlyList<StoneReconcileDecision> decisions,
            System.Collections.Generic.IReadOnlyCollection<string> keptZoneKeys)
        {
            Decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
            KeptZoneKeys = keptZoneKeys ?? throw new ArgumentNullException(nameof(keptZoneKeys));
        }

        public System.Collections.Generic.IReadOnlyList<StoneReconcileDecision> Decisions { get; }

        /// <summary>Zone keys ("x:z") whose canonical Stone was kept (reused). A selected zone missing from
        /// this set is NOT suppressed: the fresh event may still create it.</summary>
        public System.Collections.Generic.IReadOnlyCollection<string> KeptZoneKeys { get; }
    }

    /// <summary>Pure metadata-aware reconciliation over ALL resident Stone ZDOs against the current selected
    /// set (FIX R4 (#2)). Restores the selected-set reconciliation R3 dropped:
    /// <list type="bullet">
    /// <item>matching world/selector/host prefab/zone AND in the selected set => reuse (one kept per zone);</item>
    /// <item>absent from the selected set OR metadata mismatch => remove (pre-ratification disposable policy);</item>
    /// <item>duplicate matching ZDOs for a zone => keep the lowest ZDO id deterministically, remove the extras;</item>
    /// <item>a removed/stale Stone never appears in <see cref="StoneReconcileResult.KeptZoneKeys"/>, so it
    /// cannot suppress fresh event creation for that zone.</item>
    /// </list>
    /// Engine-free: the caller supplies real ZDO ids + stamped metadata and executes the removals.</summary>
    public static class HomesteadStoneReconciler
    {
        public static StoneReconcileResult Reconcile(
            System.Collections.Generic.IReadOnlyList<StoneZdoRecord> residents,
            System.Collections.Generic.IReadOnlyDictionary<string, SelectedStoneExpectation> selectedByZoneKey)
        {
            if (residents == null) throw new ArgumentNullException(nameof(residents));
            if (selectedByZoneKey == null) throw new ArgumentNullException(nameof(selectedByZoneKey));

            var decisions = new System.Collections.Generic.List<StoneReconcileDecision>(residents.Count);
            var keptZoneKeys = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

            // Group candidate-matching residents per zone so duplicate resolution is deterministic.
            var matchingByZone = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<StoneZdoRecord>>(StringComparer.Ordinal);

            foreach (var record in residents)
            {
                var key = ZoneKey(record.ZoneX, record.ZoneZ);
                if (selectedByZoneKey.TryGetValue(key, out var expectation) && expectation.MetadataMatches(record))
                {
                    if (!matchingByZone.TryGetValue(key, out var list))
                    {
                        list = new System.Collections.Generic.List<StoneZdoRecord>();
                        matchingByZone[key] = list;
                    }
                    list.Add(record);
                }
                else
                {
                    // Not selected, or selected-zone metadata mismatch → remove.
                    decisions.Add(new StoneReconcileDecision(record.ZdoId, record.ZoneX, record.ZoneZ, StoneReconcileDisposition.Remove));
                }
            }

            foreach (var pair in matchingByZone)
            {
                var group = pair.Value;
                // Deterministic canonical selection: lowest ZDO id wins, tie-broken by list order (stable).
                group.Sort((a, b) => a.ZdoId.CompareTo(b.ZdoId));
                var canonical = group[0];
                decisions.Add(new StoneReconcileDecision(canonical.ZdoId, canonical.ZoneX, canonical.ZoneZ, StoneReconcileDisposition.Reuse));
                keptZoneKeys.Add(pair.Key);
                for (var i = 1; i < group.Count; i++)
                {
                    var extra = group[i];
                    decisions.Add(new StoneReconcileDecision(extra.ZdoId, extra.ZoneX, extra.ZoneZ, StoneReconcileDisposition.RemoveDuplicate));
                }
            }

            return new StoneReconcileResult(decisions, keptZoneKeys);
        }

        private static string ZoneKey(int x, int z) => x + ":" + z;
    }
}
