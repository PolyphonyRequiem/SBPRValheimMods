// ============================================================================
//  QA-M3 fixture ledger core (canonical, t_4db82cc0) — engine-free.
//  Adopted from the reviewed parallel prebuild (t_b5413567); namespace re-homed
//  into SBPR.QaHarness.T022.Core.Fixtures. Consumed under net48 by the helper and
//  link-compiled by the net8 tests-core suite. Still System.* only.
// ----------------------------------------------------------------------------
//  OwnedResourceLedger — the crash-safe record of exactly which world objects this
//  fixture helper CREATED and therefore may destroy. It is the load-bearing safety
//  invariant of the whole prebuild:
//
//    AT-QA-CLEANUP-NO-LEAK   : every object the helper created is destroyed on cleanup.
//    unrelated-object-safety : an object the helper did NOT create has no ledger entry
//                              and is therefore structurally un-deletable by cleanup.
//
//  State machine per owned resource:
//    Planned  -> the plan expanded this id, nothing created yet
//    Created  -> IFixtureWorld.Create succeeded; handle recorded (owned, cleanable)
//    Failed   -> Create failed (partial failure); NOT owned, nothing to clean
//    Removed  -> Destroy succeeded; no longer owned
//
//  ENSURE is idempotent: re-ensuring a plan already fully Created is a no-op that
//  creates nothing new. Because OwnedResourceIds are a deterministic function of the
//  plan, a crash mid-ensure recovers by reloading the snapshot and re-driving ensure —
//  already-Created ids are skipped, only the missing tail is created.
//
//  CRASH RECOVERY: the ledger persists via a pure snapshot codec (SnapshotCodec).
//  Recovery = Load(previous snapshot) then Reconcile against IFixtureWorld.Exists —
//  a Created entry whose world object vanished (crash between create and snapshot,
//  or manual world edit) is downgraded so cleanup/ensure treat it correctly.
//
//  Engine-free: System.* only. No product identity/AP/ownership/signature/verdict.
// ============================================================================

using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    public enum OwnedResourceState
    {
        Planned = 0,
        Created = 1,
        Failed = 2,
        Removed = 3
    }

    /// <summary>One tracked owned resource: its deterministic id, current state, and (when Created)
    /// the world handle used to destroy it. Immutable value; transitions produce new entries.</summary>
    public readonly struct OwnedResourceEntry
    {
        public OwnedResourceEntry(OwnedResourceId id, ResourceCategory category, string logicalId,
            double radiusMeters, OwnedResourceState state, string handle)
        {
            Id = id;
            Category = category;
            LogicalId = logicalId ?? string.Empty;
            RadiusMeters = radiusMeters;
            State = state;
            Handle = handle ?? string.Empty;
        }

        public OwnedResourceId Id { get; }
        public ResourceCategory Category { get; }
        public string LogicalId { get; }
        public double RadiusMeters { get; }
        public OwnedResourceState State { get; }
        public string Handle { get; }

        public OwnedResourceEntry With(OwnedResourceState state, string handle) =>
            new OwnedResourceEntry(Id, Category, LogicalId, RadiusMeters, state, handle);
    }

    /// <summary>Aggregate outcome of an ensure() call.</summary>
    public sealed class EnsureResult
    {
        public EnsureResult(int created, int alreadyPresent, int failed, IReadOnlyList<OwnedResourceId> failedIds)
        {
            Created = created;
            AlreadyPresent = alreadyPresent;
            Failed = failed;
            FailedIds = failedIds;
        }

        /// <summary>Objects created by THIS call.</summary>
        public int Created { get; }

        /// <summary>Objects that were already Created before this call (idempotent skip).</summary>
        public int AlreadyPresent { get; }

        /// <summary>Objects whose world Create failed during this call (partial failure).</summary>
        public int Failed { get; }

        public IReadOnlyList<OwnedResourceId> FailedIds { get; }

        /// <summary>True iff every planned resource is now Created (no failures outstanding).</summary>
        public bool FullySatisfied => Failed == 0;
    }

    /// <summary>Aggregate outcome of a cleanup() call.</summary>
    public sealed class CleanupResult
    {
        public CleanupResult(int removed, int alreadyGone, int retryable, IReadOnlyList<OwnedResourceId> retryableIds)
        {
            Removed = removed;
            AlreadyGone = alreadyGone;
            Retryable = retryable;
            RetryableIds = retryableIds;
        }

        public int Removed { get; }
        public int AlreadyGone { get; }

        /// <summary>Objects whose Destroy returned a transient failure — cleanup can be retried.</summary>
        public int Retryable { get; }

        public IReadOnlyList<OwnedResourceId> RetryableIds { get; }

        /// <summary>True iff no owned object remains to clean.</summary>
        public bool FullyCleaned => Retryable == 0;
    }

    /// <summary>
    /// The crash-safe owned-resource ledger. Drives an IFixtureWorld to create/destroy fixture
    /// scaffolding and tracks ownership so cleanup never leaks and never deletes an unrelated object.
    /// </summary>
    public sealed class OwnedResourceLedger
    {
        // Keyed by OwnedResourceId.Canonical so the map is stable across snapshot round-trips.
        private readonly Dictionary<string, OwnedResourceEntry> _entries;
        private readonly string _fixtureId;

        private OwnedResourceLedger(string fixtureId, Dictionary<string, OwnedResourceEntry> entries)
        {
            _fixtureId = fixtureId;
            _entries = entries;
        }

        public string FixtureId => _fixtureId;

        /// <summary>Start a fresh ledger for a validated plan. Every planned resource begins Planned.</summary>
        public static OwnedResourceLedger ForPlan(ValidatedFixturePlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var map = new Dictionary<string, OwnedResourceEntry>(StringComparer.Ordinal);
            foreach (var r in plan.Resources)
            {
                // A validated plan never contains duplicate ids, but guard defensively.
                map[r.Id.Canonical] = new OwnedResourceEntry(r.Id, r.Category, r.LogicalId,
                    r.RadiusMeters, OwnedResourceState.Planned, string.Empty);
            }
            return new OwnedResourceLedger(plan.FixtureId, map);
        }

        public IReadOnlyCollection<OwnedResourceEntry> Entries
        {
            get
            {
                var list = new List<OwnedResourceEntry>(_entries.Values);
                list.Sort((a, b) => string.CompareOrdinal(a.Id.Canonical, b.Id.Canonical));
                return list;
            }
        }

        public int CountInState(OwnedResourceState state)
        {
            int n = 0;
            foreach (var e in _entries.Values) if (e.State == state) n++;
            return n;
        }

        // ---- ENSURE: idempotently create every Planned/Failed resource, skip Created ----

        /// <summary>Drive the world to create every not-yet-Created resource, durably stamping each
        /// with an exact QA ownership marker (world uid + run nonce + fixture id + owned id) as part
        /// of creation. Idempotent: an entry already Created is skipped (counted AlreadyPresent). A
        /// Failed entry from a prior partial run is retried. Never creates the artifact under test —
        /// only allowlisted scaffolding.</summary>
        public EnsureResult Ensure(IFixtureWorld world, FixtureRunContext runContext)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            int created = 0, already = 0, failed = 0;
            var failedIds = new List<OwnedResourceId>();

            foreach (var key in OrderedKeys())
            {
                var e = _entries[key];
                if (e.State == OwnedResourceState.Created)
                {
                    already++;
                    continue;
                }
                if (e.State == OwnedResourceState.Removed)
                {
                    // A removed resource is not re-created by ensure; ensure only satisfies the plan
                    // forward, and Removed is a terminal post-cleanup state.
                    continue;
                }

                var marker = FixtureOwnershipMarker.For(runContext, _fixtureId, e.Id);
                var op = world.Create(e.Id, e.Category, e.LogicalId, e.RadiusMeters, marker);
                if (op.Ok)
                {
                    _entries[key] = e.With(OwnedResourceState.Created, op.Handle);
                    created++;
                }
                else
                {
                    _entries[key] = e.With(OwnedResourceState.Failed, string.Empty);
                    failed++;
                    failedIds.Add(e.Id);
                }
            }

            return new EnsureResult(created, already, failed, failedIds);
        }

        // ---- CLEANUP: destroy ONLY owned (Created) resources; deterministic reverse order ----

        /// <summary>Compute the deterministic cleanup plan: the owned (Created) ids in reverse creation
        /// order. Pure — no world access — so it can be inspected/asserted before executing.</summary>
        public IReadOnlyList<OwnedResourceId> CleanupPlan()
        {
            var keys = OrderedKeys();
            var plan = new List<OwnedResourceId>();
            for (int i = keys.Count - 1; i >= 0; i--)
            {
                var e = _entries[keys[i]];
                if (e.State == OwnedResourceState.Created) plan.Add(e.Id);
            }
            return plan;
        }

        /// <summary>Destroy every owned (Created) resource. Idempotent and retryable: an object the
        /// world reports already gone is counted AlreadyGone and marked Removed; a transient Destroy
        /// failure leaves the entry Created so a later cleanup retries it. NEVER touches an entry the
        /// ledger does not own — unrelated world objects have no entry and are unreachable here.</summary>
        public CleanupResult Cleanup(IFixtureWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            int removed = 0, alreadyGone = 0, retryable = 0;
            var retryableIds = new List<OwnedResourceId>();

            foreach (var id in CleanupPlan())
            {
                var key = id.Canonical;
                var e = _entries[key];

                if (!world.Exists(e.Handle))
                {
                    _entries[key] = e.With(OwnedResourceState.Removed, string.Empty);
                    alreadyGone++;
                    continue;
                }

                var op = world.Destroy(e.Id, e.Handle);
                if (op.Ok)
                {
                    _entries[key] = e.With(OwnedResourceState.Removed, string.Empty);
                    removed++;
                }
                else
                {
                    retryable++;
                    retryableIds.Add(e.Id);
                }
            }

            return new CleanupResult(removed, alreadyGone, retryable, retryableIds);
        }

        // ---- CRASH RECOVERY: reload a snapshot, then reconcile beliefs against world truth ----

        /// <summary>Reconcile the ledger's beliefs against the world after a crash/reload. A Created
        /// entry whose world object no longer Exists is downgraded to Planned (so ensure re-creates it)
        /// — this closes the "crashed after create, before the object durably landed" gap. Returns the
        /// number of entries downgraded.</summary>
        public int ReconcileWithWorld(IFixtureWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            int downgraded = 0;
            foreach (var key in OrderedKeys())
            {
                var e = _entries[key];
                if (e.State == OwnedResourceState.Created && !world.Exists(e.Handle))
                {
                    _entries[key] = e.With(OwnedResourceState.Planned, string.Empty);
                    downgraded++;
                }
            }
            return downgraded;
        }

        // ---- DURABLE MARKER RECOVERY: adopt this run's exact survivors after a crash ----

        /// <summary>
        /// Bounded, fail-closed recovery from the durable ON-OBJECT ownership markers. This closes the
        /// "crashed after spawn, before the snapshot was written" gap the snapshot file alone cannot:
        /// a fresh/corrupt ledger has empty handles and cannot rediscover a survivor, but the survivor
        /// carries its own exact marker, so we scope discovery to EXACTLY (this world uid, this run
        /// nonce, this fixture id) and adopt each matching object into the Created state under the
        /// owned id its marker names.
        ///
        /// FAIL-CLOSED: the whole recovery refuses (no adoption, no world side effect, ledger left
        /// untouched) if ANY candidate is malformed, two candidates claim the same owned id, a
        /// candidate carries a foreign world/run, or a candidate for THIS world/run/fixture names an
        /// owned id the current plan does not expect. Unmarked / unrelated objects are never returned
        /// by discovery, so they are structurally un-adoptable and preserved. Same-run markers whose
        /// entry is already Created are treated as idempotent (handle refreshed, not double-created).
        /// Returns the number of entries adopted (0 when there is nothing to recover).
        /// </summary>
        public FixtureRecoveryStatus RecoverFromMarkers(IFixtureWorld world, FixtureRunContext runContext, out int adopted)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            adopted = 0;

            var candidates = world.DiscoverMarked();
            if (candidates == null || candidates.Count == 0)
                return FixtureRecoveryStatus.Ok;

            // First pass: decode + scope + validate EVERY candidate before mutating anything, so a
            // violation aborts the entire recovery closed (no partial adoption).
            var claims = new Dictionary<string, string>(StringComparer.Ordinal); // resourceCanonical -> handle
            foreach (var cand in candidates)
            {
                if (!FixtureOwnershipMarker.TryDecode(cand.MarkerPayload, out var marker))
                    return FixtureRecoveryStatus.MalformedMarker;

                // Foreign world/run: not ours — but this is a HARD refuse, because a disposable QA
                // world/run should never see another run's marker; if it does, ownership is unsafe.
                if (marker.WorldUid != runContext.WorldUid)
                    return FixtureRecoveryStatus.ForeignWorld;
                if (!string.Equals(marker.RunNonce, runContext.RunNonce, StringComparison.Ordinal))
                    return FixtureRecoveryStatus.ForeignRun;

                // Different fixture id under the SAME run belongs to a sibling fixture's ledger — not
                // this ledger's concern; skip it (it will be adopted by its own fixture's recovery).
                if (!string.Equals(marker.FixtureId, _fixtureId, StringComparison.Ordinal))
                    continue;

                // A marker for THIS world/run/fixture MUST name an owned id the current plan expects.
                if (!_entries.ContainsKey(marker.ResourceCanonical))
                    return FixtureRecoveryStatus.UnexpectedResource;

                if (claims.ContainsKey(marker.ResourceCanonical))
                    return FixtureRecoveryStatus.DuplicateMarker;

                if (string.IsNullOrEmpty(cand.SpawnedInstanceId))
                    return FixtureRecoveryStatus.MalformedMarker;

                claims[marker.ResourceCanonical] = cand.SpawnedInstanceId;
            }

            // Second pass: every claim validated — adopt into Created (idempotent handle refresh).
            foreach (var kv in claims)
            {
                var e = _entries[kv.Key];
                if (e.State != OwnedResourceState.Created || !string.Equals(e.Handle, kv.Value, StringComparison.Ordinal))
                    adopted++;
                _entries[kv.Key] = e.With(OwnedResourceState.Created, kv.Value);
            }

            return FixtureRecoveryStatus.Ok;
        }

        // ---- Snapshot (pure durable form for crash recovery) ----

        public LedgerSnapshot ToSnapshot()
        {
            var rows = new List<OwnedResourceEntry>();
            foreach (var key in OrderedKeys()) rows.Add(_entries[key]);
            return new LedgerSnapshot(_fixtureId, rows);
        }

        public static OwnedResourceLedger FromSnapshot(LedgerSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var map = new Dictionary<string, OwnedResourceEntry>(StringComparer.Ordinal);
            foreach (var e in snapshot.Entries) map[e.Id.Canonical] = e;
            return new OwnedResourceLedger(snapshot.FixtureId, map);
        }

        private List<string> OrderedKeys()
        {
            var keys = new List<string>(_entries.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }
    }
}
