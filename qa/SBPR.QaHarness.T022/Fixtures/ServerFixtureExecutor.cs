// ============================================================================
//  QA-M3R real fixture adapter (t_1572d041) — engine-free executor policy.
// ----------------------------------------------------------------------------
//  ServerFixtureExecutor — the engine-free orchestration that a server fixture
//  verb runs through, composing the pieces M3 shipped into one gated, crash-safe
//  lifecycle. It is the brain the thin engine-bound seam/authority adapters plug
//  into; it references only interfaces (IServerAuthoritySource, IFixtureWorld,
//  DeliveringPeerState) + the durable LedgerSnapshotStore, never Unity/Valheim.
//
//  Per-request lifecycle (fixture create):
//    1. MAP    the admitted verb+args -> a validated, vanilla-only, bounded plan
//              (FixtureRequestMapper). A product id / non-fixture verb fails here.
//    2. RECOVER load any prior crash snapshot for this fixture id and RECONCILE it
//              against world truth (a Created entry whose object vanished is
//              downgraded so ensure re-creates it, never double-creates).
//    3. GATE   re-check authority AT EXECUTION (server role + world loaded +
//              delivering-peer bound on current generation + admin re-read) via
//              the existing FixtureProvisioner. A refused gate performs NO world
//              side effect and writes NO snapshot.
//    4. ENSURE idempotently create the plan's owned resources through the seam.
//    5. PERSIST atomically snapshot the ledger AFTER the world op, so a crash
//              between create and snapshot is recovered by step 2's reconcile.
//
//  Cleanup mirrors it: recover+reconcile, gate, cleanup (destroy ONLY owned ids),
//  persist; when fully cleaned the durable snapshot is deleted so the world save
//  carries no harness ledger (ADR-0009 §5.4 no-leak).
//
//  OWNED-ONLY guarantee: every id the executor can ever destroy came from a plan
//  it mapped and a Create it recorded; an unrelated world object has no ledger
//  entry and is structurally unreachable by cleanup, across restarts.
//
//  Engine-free: System.* only. No product state, craft/upgrade/transfer/tamper,
//  or verdict — this stands up ordinary vanilla scaffolding and tears it down.
// ============================================================================

using System;
using System.Collections.Generic;
using SBPR.QaHarness.T022.Core.ControlPlane;

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    /// <summary>The typed outcome class of a fixture execution attempt.</summary>
    public enum FixtureExecStatus
    {
        /// <summary>The world op ran; see the counts on the result.</summary>
        Executed = 0,

        /// <summary>The verb+args did not map to a vanilla fixture plan (non-fixture verb / product id / bounds).</summary>
        MapRejected = 1,

        /// <summary>The execution-time authority recheck refused; NO world side effect, NO snapshot write.</summary>
        AuthorityRejected = 2,

        /// <summary>A durable snapshot could not be persisted after a world op (fail-closed I/O surface).</summary>
        PersistFailed = 3,

        /// <summary>Bounded durable-marker recovery found a corrupt/ambiguous/foreign world state and refused
        /// (fail-closed): NO world side effect, NO snapshot write. The disposable world must be inspected.</summary>
        RecoveryRefused = 4,

        /// <summary>Cleanup removed every owned object but the durable snapshot could not be deleted, so a
        /// leftover ledger persists. Observable + retryable rather than a silently-swallowed leak.</summary>
        SnapshotDeleteFailed = 5,
    }

    /// <summary>The descriptive result of one fixture execution (primitive facts only — never a verdict).</summary>
    public sealed class FixtureExecResult
    {
        public FixtureExecStatus Status { get; }

        /// <summary>The map rejection reason when <see cref="Status"/> is MapRejected.</summary>
        public FixtureMapReason MapReason { get; }

        /// <summary>The authority reason when <see cref="Status"/> is AuthorityRejected.</summary>
        public FixtureAuthorityReason AuthorityReason { get; }

        /// <summary>Objects created/cleaned by this call (0 when not Executed).</summary>
        public int Created { get; }
        public int AlreadyPresent { get; }
        public int Failed { get; }
        public int Removed { get; }
        public int AlreadyGone { get; }
        public int Retryable { get; }

        /// <summary>Number of ledger entries reconciled (downgraded) against world truth on recovery.</summary>
        public int Reconciled { get; }

        public string Detail { get; }

        private FixtureExecResult(FixtureExecStatus status, FixtureMapReason mapReason,
            FixtureAuthorityReason authorityReason, int created, int alreadyPresent, int failed,
            int removed, int alreadyGone, int retryable, int reconciled, string detail)
        {
            Status = status;
            MapReason = mapReason;
            AuthorityReason = authorityReason;
            Created = created;
            AlreadyPresent = alreadyPresent;
            Failed = failed;
            Removed = removed;
            AlreadyGone = alreadyGone;
            Retryable = retryable;
            Reconciled = reconciled;
            Detail = detail ?? string.Empty;
        }

        internal static FixtureExecResult MapRejected(FixtureMapReason reason, string detail) =>
            new(FixtureExecStatus.MapRejected, reason, FixtureAuthorityReason.None, 0, 0, 0, 0, 0, 0, 0, detail);

        internal static FixtureExecResult AuthorityRejected(FixtureAuthorityReason reason) =>
            new(FixtureExecStatus.AuthorityRejected, FixtureMapReason.None, reason, 0, 0, 0, 0, 0, 0, 0, string.Empty);

        internal static FixtureExecResult PersistFailed(string detail, int reconciled) =>
            new(FixtureExecStatus.PersistFailed, FixtureMapReason.None, FixtureAuthorityReason.None,
                0, 0, 0, 0, 0, 0, reconciled, detail);

        internal static FixtureExecResult RecoveryRefused(string detail) =>
            new(FixtureExecStatus.RecoveryRefused, FixtureMapReason.None, FixtureAuthorityReason.None,
                0, 0, 0, 0, 0, 0, 0, detail);

        internal static FixtureExecResult SnapshotDeleteFailed(CleanupResult r, int reconciled, string detail) =>
            new(FixtureExecStatus.SnapshotDeleteFailed, FixtureMapReason.None, FixtureAuthorityReason.None,
                0, 0, 0, r.Removed, r.AlreadyGone, r.Retryable, reconciled, detail);

        internal static FixtureExecResult Ensured(EnsureResult r, int reconciled) =>
            new(FixtureExecStatus.Executed, FixtureMapReason.None, FixtureAuthorityReason.None,
                r.Created, r.AlreadyPresent, r.Failed, 0, 0, 0, reconciled, string.Empty);

        internal static FixtureExecResult Cleaned(CleanupResult r, int reconciled) =>
            new(FixtureExecStatus.Executed, FixtureMapReason.None, FixtureAuthorityReason.None,
                0, 0, 0, r.Removed, r.AlreadyGone, r.Retryable, reconciled, string.Empty);
    }

    /// <summary>
    /// Gated, crash-safe fixture lifecycle for the server role. Not thread-safe: the server
    /// responder drives it from a single main-thread pump tick (the single-slot dispatcher).
    /// </summary>
    public sealed class ServerFixtureExecutor
    {
        private readonly IServerAuthoritySource _authority;
        private readonly DeliveringPeerState _peerState;
        private readonly IFixtureWorld _world;
        private readonly Func<string, LedgerSnapshotStore> _storeFactory;
        private readonly FixtureRunContext _runContext;

        /// <summary>
        /// Construct the executor from its (engine-free) collaborators. <paramref name="storeFactory"/>
        /// maps a fixture id to its durable snapshot store, so each fixture persists to its own path.
        /// <paramref name="runContext"/> is the disposable world uid + run nonce every spawned object's
        /// durable ownership marker is stamped with and recovery is scoped to.
        /// </summary>
        public ServerFixtureExecutor(
            IServerAuthoritySource authority,
            DeliveringPeerState peerState,
            IFixtureWorld world,
            Func<string, LedgerSnapshotStore> storeFactory,
            FixtureRunContext runContext)
        {
            _authority = authority ?? throw new ArgumentNullException(nameof(authority));
            _peerState = peerState ?? throw new ArgumentNullException(nameof(peerState));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
            _runContext = runContext;
        }

        /// <summary>The last authority decision (for receipts/telemetry).</summary>
        public FixtureAuthorityDecision LastDecision { get; private set; } = FixtureAuthorityDecision.Accept;

        /// <summary>
        /// Provision the fixture named by <paramref name="verb"/>+<paramref name="args"/> under
        /// <paramref name="fixtureId"/>. Maps->recovers(markers+snapshot)->gates->ensures->persists.
        /// </summary>
        public FixtureExecResult Ensure(
            string fixtureId, string? verb, IReadOnlyDictionary<string, object?> args,
            string? deliveringPeerId, long claimedGeneration)
        {
            var map = FixtureRequestMapper.Map(fixtureId, verb, args);
            if (!map.Ok || map.Plan == null)
                return FixtureExecResult.MapRejected(map.Reason, map.Detail);

            var store = _storeFactory(fixtureId);
            if (!TryRecover(map.Plan, store, out var ledger, out int reconciled, out var recoveryDetail))
                return FixtureExecResult.RecoveryRefused(recoveryDetail);

            var decision = FixtureAuthority.Recheck(_authority, _peerState, deliveringPeerId, claimedGeneration);
            LastDecision = decision;
            if (!decision.Ok)
                return FixtureExecResult.AuthorityRejected(decision.Reason);

            EnsureResult ensured = ledger.Ensure(_world, _runContext);

            // Persist AFTER the world op. The durable ON-OBJECT marker (written as part of each spawn)
            // — not this snapshot — is the crash-safety anchor: a crash between spawn and this Save is
            // recovered next run by the bounded marker scan, which adopts the exact survivor. The
            // snapshot is a fast-path cache of that truth, so a persist failure is still fail-closed.
            try { store.Save(ledger); }
            catch (Exception ex) { return FixtureExecResult.PersistFailed(ex.GetType().Name + ": " + ex.Message, reconciled); }

            return FixtureExecResult.Ensured(ensured, reconciled);
        }

        /// <summary>
        /// Tear down the fixture under <paramref name="fixtureId"/>. Recovers (markers+snapshot),
        /// gates authority, destroys ONLY owned ids, persists; when fully cleaned deletes the durable
        /// snapshot so the world save carries no harness ledger. A snapshot delete failure is observable.
        /// </summary>
        public FixtureExecResult Cleanup(
            string fixtureId, string? verb, IReadOnlyDictionary<string, object?> args,
            string? deliveringPeerId, long claimedGeneration)
        {
            // Cleanup needs the same plan shape to reconstruct the deterministic owned ids after a
            // restart with no live ledger. It maps the same verb+args the fixture was created with.
            var map = FixtureRequestMapper.Map(fixtureId, verb, args);
            if (!map.Ok || map.Plan == null)
                return FixtureExecResult.MapRejected(map.Reason, map.Detail);

            var store = _storeFactory(fixtureId);
            if (!TryRecover(map.Plan, store, out var ledger, out int reconciled, out var recoveryDetail))
                return FixtureExecResult.RecoveryRefused(recoveryDetail);

            var decision = FixtureAuthority.Recheck(_authority, _peerState, deliveringPeerId, claimedGeneration);
            LastDecision = decision;
            if (!decision.Ok)
                return FixtureExecResult.AuthorityRejected(decision.Reason);

            CleanupResult cleaned = ledger.Cleanup(_world);

            if (cleaned.FullyCleaned)
            {
                // Nothing owned remains — remove the durable snapshot entirely (no-leak). A delete
                // failure is surfaced (not swallowed): the durable ledger persisted, so report it.
                if (!store.Delete())
                    return FixtureExecResult.SnapshotDeleteFailed(cleaned, reconciled,
                        "durable snapshot could not be deleted after full cleanup: " + store.Path);
            }
            else
            {
                // A transient destroy failure left owned entries; persist so a retry resumes.
                try { store.Save(ledger); }
                catch (Exception ex) { return FixtureExecResult.PersistFailed(ex.GetType().Name + ": " + ex.Message, reconciled); }
            }

            return FixtureExecResult.Cleaned(cleaned, reconciled);
        }

        // Recover the ledger for this plan. TWO durable sources, in fail-closed precedence:
        //   1. The durable ON-OBJECT markers (world truth) — a bounded, exact-scope scan that adopts
        //      exactly this run/world/fixture's survivors and REFUSES on any malformed/duplicate/
        //      foreign/unexpected marker. This closes the crash-before-snapshot window the snapshot
        //      file structurally cannot, and it never treats a corrupt/absent snapshot as "empty":
        //      the survivor's own marker is authoritative.
        //   2. The snapshot file — a fast-path cache of belief. A successfully-loaded snapshot for the
        //      SAME fixture seeds the ledger; a Corrupt/IoError/Absent load is NOT treated as empty —
        //      we start from the plan and let the marker scan (step 1) rediscover real survivors.
        // Returns false (fail-closed) only when the marker scan reports an integrity violation.
        private bool TryRecover(
            ValidatedFixturePlan plan, LedgerSnapshotStore store,
            out OwnedResourceLedger ledger, out int reconciled, out string recoveryDetail)
        {
            reconciled = 0;
            recoveryDetail = string.Empty;

            var load = store.Load();
            if (load.Ok && load.Snapshot != null &&
                string.Equals(load.Snapshot.FixtureId, plan.FixtureId, StringComparison.Ordinal))
            {
                ledger = OwnedResourceLedger.FromSnapshot(load.Snapshot);
            }
            else
            {
                // Absent / Corrupt / IoError / fixture-id mismatch: NEVER assume empty. Start from the
                // plan; the durable markers below are the authority on what actually survived.
                ledger = OwnedResourceLedger.ForPlan(plan);
            }

            // Durable exact-marker recovery (world truth) — fail-closed on any integrity violation.
            var status = ledger.RecoverFromMarkers(_world, _runContext, out int adopted);
            if (status != FixtureRecoveryStatus.Ok)
            {
                recoveryDetail = "marker-recovery-refused: " + status;
                return false;
            }

            // Reconcile belief vs world truth: a Created entry whose object vanished (crash between
            // create and any durable record) is downgraded so ensure re-creates it. Adoptions from the
            // marker scan are already Created-with-live-handle, so they survive reconcile.
            int downgraded = ledger.ReconcileWithWorld(_world);
            reconciled = downgraded + adopted;
            return true;
        }
    }
}
