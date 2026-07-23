// ============================================================================
//  QA-M3 fixture ledger tests (canonical, t_4db82cc0) — engine-free.
//  Adopted from the reviewed t_b5413567 prebuild; namespace re-homed.
// ----------------------------------------------------------------------------
//  Happy-path + adversarial coverage for the prebuild core, mapping to the
//  canonical M3 named acceptance tests it will feed:
//    AT-QA-FIXTURE-VANILLA-ONLY  -> vanilla-only allowlist + unrepresentable product
//    AT-QA-CLEANUP-NO-LEAK       -> every created object destroyed; nothing leaks
//
//  Adversarial cases: duplicate request, bounds overflow (distinct/count/total/radius),
//  unknown logical id, product-category unrepresentable, partial creation, cleanup
//  retry, crash snapshot recovery, unrelated object never deleted, snapshot tamper.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using SBPR.QaHarness.T022.Core.Fixtures;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public sealed class FixtureLedgerCoreTests
    {
        // ── Shared vanilla-only allowlist (the ONLY kinds a fixture may represent) ──
        private static ResourceAllowlist Allowlist() => new ResourceAllowlist(new Dictionary<string, ResourceCategory>
        {
            { "vanilla.wood.pile", ResourceCategory.Material },
            { "vanilla.stone.pile", ResourceCategory.Material },
            { "vanilla.workbench", ResourceCategory.Station },
            { "vanilla.anchor", ResourceCategory.PlacementAnchor },
        });

        private static FixturePlan Plan(string id, params (string logical, int count, double radius)[] specs) =>
            new FixturePlan(id, specs.Select(s => new ResourceSpec(s.logical, s.count, s.radius)));

        private static ValidatedFixturePlan Validated(FixturePlan plan)
        {
            var r = FixturePlanValidator.Validate(plan, Allowlist(), FixtureBounds.Default);
            Assert.True(r.Accepted, "expected plan to validate: " + r.Reason + " " + r.OffendingLogicalId);
            return r.Plan!;
        }

        // ───────────────────────── HAPPY PATH ─────────────────────────

        [Fact] // AT-QA-FIXTURE-VANILLA-ONLY (accept) + AT-QA-CLEANUP-NO-LEAK
        public void Ensure_then_cleanup_creates_and_removes_exactly_the_planned_objects()
        {
            var plan = Validated(Plan("fx1", ("vanilla.wood.pile", 3, 4.0), ("vanilla.workbench", 1, 2.0)));
            var ledger = OwnedResourceLedger.ForPlan(plan);
            var world = new FakeFixtureWorld();

            var ensure = ledger.Ensure(world, TestRun.Ctx);
            Assert.Equal(4, ensure.Created);
            Assert.Equal(0, ensure.Failed);
            Assert.True(ensure.FullySatisfied);
            Assert.Equal(4, world.LiveCount);
            Assert.Equal(4, ledger.CountInState(OwnedResourceState.Created));

            var cleanup = ledger.Cleanup(world);
            Assert.Equal(4, cleanup.Removed);
            Assert.True(cleanup.FullyCleaned);
            Assert.Equal(0, world.LiveCount); // AT-QA-CLEANUP-NO-LEAK
            Assert.Equal(4, ledger.CountInState(OwnedResourceState.Removed));
        }

        [Fact] // Idempotent ensure: re-ensure creates nothing new.
        public void Ensure_is_idempotent()
        {
            var plan = Validated(Plan("fx1", ("vanilla.stone.pile", 2, 1.0)));
            var ledger = OwnedResourceLedger.ForPlan(plan);
            var world = new FakeFixtureWorld();

            var first = ledger.Ensure(world, TestRun.Ctx);
            Assert.Equal(2, first.Created);

            var second = ledger.Ensure(world, TestRun.Ctx);
            Assert.Equal(0, second.Created);
            Assert.Equal(2, second.AlreadyPresent);
            Assert.Equal(2, world.LiveCount);
        }

        [Fact] // Deterministic ids: same plan -> same owned ids across constructions.
        public void OwnedResourceIds_are_deterministic_function_of_plan()
        {
            var a = OwnedResourceLedger.ForPlan(Validated(Plan("fx1", ("vanilla.wood.pile", 2, 1.0))));
            var b = OwnedResourceLedger.ForPlan(Validated(Plan("fx1", ("vanilla.wood.pile", 2, 1.0))));
            var idsA = a.Entries.Select(e => e.Id.Canonical).ToArray();
            var idsB = b.Entries.Select(e => e.Id.Canonical).ToArray();
            Assert.Equal(idsA, idsB);
            Assert.Contains("fx1/vanilla.wood.pile#0", idsA);
            Assert.Contains("fx1/vanilla.wood.pile#1", idsA);
        }

        [Fact] // Deterministic cleanup plan is reverse creation order, Created-only.
        public void CleanupPlan_is_deterministic_reverse_order()
        {
            var plan = Validated(Plan("fx1", ("vanilla.wood.pile", 3, 1.0)));
            var ledger = OwnedResourceLedger.ForPlan(plan);
            var world = new FakeFixtureWorld();
            ledger.Ensure(world, TestRun.Ctx);

            var cp = ledger.CleanupPlan().Select(i => i.Canonical).ToArray();
            Assert.Equal(new[] { "fx1/vanilla.wood.pile#2", "fx1/vanilla.wood.pile#1", "fx1/vanilla.wood.pile#0" }, cp);
        }

        // ───────────────────── ADVERSARIAL / VALIDATION ─────────────────────

        [Fact] // Unknown logical id -> rejected, never "assume a prefab".
        public void Unknown_logical_id_is_rejected()
        {
            var r = FixturePlanValidator.Validate(Plan("fx1", ("not.on.allowlist", 1, 1.0)), Allowlist(), FixtureBounds.Default);
            Assert.False(r.Accepted);
            Assert.Equal(PlanRejectionReason.UnknownLogicalId, r.Reason);
            Assert.Equal("not.on.allowlist", r.OffendingLogicalId);
        }

        [Fact] // Duplicate logical id within one plan -> conflict.
        public void Duplicate_logical_id_is_conflict()
        {
            var r = FixturePlanValidator.Validate(
                Plan("fx1", ("vanilla.wood.pile", 1, 1.0), ("vanilla.wood.pile", 1, 1.0)),
                Allowlist(), FixtureBounds.Default);
            Assert.False(r.Accepted);
            Assert.Equal(PlanRejectionReason.DuplicateLogicalId, r.Reason);
        }

        [Fact] // Per-resource count overflow -> rejected before any world side effect.
        public void Count_overflow_is_rejected()
        {
            var bounds = new FixtureBounds(16, 4, 256, 32.0);
            var r = FixturePlanValidator.Validate(Plan("fx1", ("vanilla.wood.pile", 5, 1.0)), Allowlist(), bounds);
            Assert.False(r.Accepted);
            Assert.Equal(PlanRejectionReason.CountOverflow, r.Reason);
        }

        [Fact] // Total-object overflow across specs -> rejected.
        public void Total_object_overflow_is_rejected()
        {
            var bounds = new FixtureBounds(16, 64, 5, 32.0);
            var r = FixturePlanValidator.Validate(
                Plan("fx1", ("vanilla.wood.pile", 3, 1.0), ("vanilla.stone.pile", 3, 1.0)),
                Allowlist(), bounds);
            Assert.False(r.Accepted);
            Assert.Equal(PlanRejectionReason.TotalObjectOverflow, r.Reason);
        }

        [Fact] // Distinct-resource overflow -> rejected.
        public void Distinct_resource_overflow_is_rejected()
        {
            var bounds = new FixtureBounds(1, 64, 256, 32.0);
            var r = FixturePlanValidator.Validate(
                Plan("fx1", ("vanilla.wood.pile", 1, 1.0), ("vanilla.stone.pile", 1, 1.0)),
                Allowlist(), bounds);
            Assert.False(r.Accepted);
            Assert.Equal(PlanRejectionReason.DistinctResourceOverflow, r.Reason);
        }

        [Fact] // Radius out of bounds -> rejected.
        public void Radius_out_of_bounds_is_rejected()
        {
            var r = FixturePlanValidator.Validate(Plan("fx1", ("vanilla.anchor", 1, 999.0)), Allowlist(), FixtureBounds.Default);
            Assert.False(r.Accepted);
            Assert.Equal(PlanRejectionReason.RadiusOutOfBounds, r.Reason);
        }

        [Fact] // A value AT the inclusive bound is accepted (boundary correctness).
        public void Values_at_the_inclusive_bound_are_accepted()
        {
            var bounds = new FixtureBounds(1, 4, 4, 10.0);
            var r = FixturePlanValidator.Validate(Plan("fx1", ("vanilla.wood.pile", 4, 10.0)), Allowlist(), bounds);
            Assert.True(r.Accepted);
            Assert.Equal(4, r.Plan!.Resources.Count);
        }

        [Fact] // Malformed spec construction is impossible (zero/negative count, non-finite radius).
        public void Malformed_specs_cannot_be_constructed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceSpec("vanilla.wood.pile", 0, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceSpec("vanilla.wood.pile", -1, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceSpec("vanilla.wood.pile", 1, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceSpec("vanilla.wood.pile", 1, double.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => new ResourceSpec("", 1, 1.0));
        }

        [Fact] // AT-QA-FIXTURE-VANILLA-ONLY: product categories are structurally unrepresentable.
        public void Only_non_product_categories_exist()
        {
            // The entire ResourceCategory enum is vanilla scaffolding — Material/Station/PlacementAnchor.
            // There is deliberately NO product/artifact/Bond/AP/ownership/signature/verdict member.
            var names = Enum.GetNames(typeof(ResourceCategory)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "Material", "PlacementAnchor", "Station" }, names);

            // And the allowlist rejects a category value outside the defined vanilla set.
            Assert.Throws<ArgumentException>(() =>
                new ResourceAllowlist(new Dictionary<string, ResourceCategory> { { "x", (ResourceCategory)999 } }));
        }

        // ───────────────────── PARTIAL FAILURE / RETRY ─────────────────────

        [Fact] // Partial creation: one spec fails; ledger records only successes as owned.
        public void Partial_creation_marks_failed_not_owned()
        {
            var plan = Validated(Plan("fx1", ("vanilla.wood.pile", 2, 1.0), ("vanilla.workbench", 1, 1.0)));
            var ledger = OwnedResourceLedger.ForPlan(plan);
            var world = new FakeFixtureWorld();
            world.FailCreateForLogicalId.Add("vanilla.workbench");

            var ensure = ledger.Ensure(world, TestRun.Ctx);
            Assert.Equal(2, ensure.Created);
            Assert.Equal(1, ensure.Failed);
            Assert.False(ensure.FullySatisfied);
            Assert.Single(ensure.FailedIds);
            Assert.Equal(1, ledger.CountInState(OwnedResourceState.Failed));
            Assert.Equal(2, world.LiveCount);

            // Cleanup only touches OWNED (Created) objects — the Failed one has nothing to remove.
            var cleanup = ledger.Cleanup(world);
            Assert.Equal(2, cleanup.Removed);
            Assert.Equal(0, world.LiveCount);
        }

        [Fact] // Ensure retries a previously-Failed resource once the world recovers.
        public void Ensure_retries_failed_resource()
        {
            var plan = Validated(Plan("fx1", ("vanilla.workbench", 1, 1.0)));
            var ledger = OwnedResourceLedger.ForPlan(plan);
            var world = new FakeFixtureWorld();
            world.FailCreateForLogicalId.Add("vanilla.workbench");

            Assert.Equal(1, ledger.Ensure(world, TestRun.Ctx).Failed);

            world.FailCreateForLogicalId.Clear(); // world recovered
            var retry = ledger.Ensure(world, TestRun.Ctx);
            Assert.Equal(1, retry.Created);
            Assert.True(retry.FullySatisfied);
            Assert.Equal(1, world.LiveCount);
        }

        [Fact] // Cleanup retry: a transient destroy failure keeps the entry Created for a later retry.
        public void Cleanup_retry_after_transient_destroy_failure()
        {
            var plan = Validated(Plan("fx1", ("vanilla.wood.pile", 2, 1.0)));
            var ledger = OwnedResourceLedger.ForPlan(plan);
            var world = new FakeFixtureWorld();
            ledger.Ensure(world, TestRun.Ctx);

            // Fail destroy of the first-cleaned object (reverse order => #1 first).
            var firstToClean = ledger.CleanupPlan()[0];
            var handle = ledger.Entries.First(e => e.Id == firstToClean).Handle;
            world.FailDestroyForHandle.Add(handle);

            var first = ledger.Cleanup(world);
            Assert.Equal(1, first.Removed);
            Assert.Equal(1, first.Retryable);
            Assert.False(first.FullyCleaned);
            Assert.Equal(1, world.LiveCount);

            world.FailDestroyForHandle.Clear(); // transient error cleared
            var second = ledger.Cleanup(world);
            Assert.Equal(1, second.Removed);
            Assert.True(second.FullyCleaned);
            Assert.Equal(0, world.LiveCount);
        }

        // ───────────────────── UNRELATED-OBJECT SAFETY ─────────────────────

        [Fact] // An object the ledger did NOT create is never touched by cleanup.
        public void Unrelated_object_is_never_deleted()
        {
            var plan = Validated(Plan("fx1", ("vanilla.wood.pile", 2, 1.0)));
            var ledger = OwnedResourceLedger.ForPlan(plan);
            var world = new FakeFixtureWorld();
            var bystander = world.SeedUnrelated("player-house");
            ledger.Ensure(world, TestRun.Ctx);

            ledger.Cleanup(world);

            Assert.Equal(0, world.LiveCount);           // owned objects gone
            Assert.True(world.UnrelatedExists(bystander)); // unrelated survives
            Assert.Equal(1, world.UnrelatedCount);
        }

        // ───────────────────── CRASH SNAPSHOT RECOVERY ─────────────────────

        [Fact] // Snapshot round-trips exactly (pure codec).
        public void Snapshot_round_trips()
        {
            var plan = Validated(Plan("fx1", ("vanilla.wood.pile", 2, 3.5), ("vanilla.workbench", 1, 0.0)));
            var ledger = OwnedResourceLedger.ForPlan(plan);
            var world = new FakeFixtureWorld();
            ledger.Ensure(world, TestRun.Ctx);

            string text = SnapshotCodec.Encode(ledger.ToSnapshot());
            var decoded = SnapshotCodec.Decode(text);
            Assert.True(decoded.Ok, decoded.Error);

            var restored = OwnedResourceLedger.FromSnapshot(decoded.Snapshot!);
            Assert.Equal(ledger.Entries.Select(e => e.Id.Canonical), restored.Entries.Select(e => e.Id.Canonical));
            Assert.Equal(ledger.Entries.Select(e => e.State), restored.Entries.Select(e => e.State));
            Assert.Equal(ledger.Entries.Select(e => e.Handle), restored.Entries.Select(e => e.Handle));
            Assert.Equal(ledger.Entries.Select(e => e.RadiusMeters), restored.Entries.Select(e => e.RadiusMeters));
        }

        [Fact] // Crash recovery: reload snapshot then cleanup removes exactly the owned objects (no leak).
        public void Crash_recovery_cleans_owned_objects_from_reloaded_snapshot()
        {
            var plan = Validated(Plan("fx1", ("vanilla.wood.pile", 3, 1.0)));
            var world = new FakeFixtureWorld();

            var before = OwnedResourceLedger.ForPlan(plan);
            before.Ensure(world, TestRun.Ctx);
            string snapshot = SnapshotCodec.Encode(before.ToSnapshot());

            // Process death: a FRESH ledger is reconstructed from the durable snapshot only.
            var recovered = OwnedResourceLedger.FromSnapshot(SnapshotCodec.Decode(snapshot).Snapshot!);
            // World objects survived the crash (durable), so nothing to reconcile.
            Assert.Equal(0, recovered.ReconcileWithWorld(world));

            var cleanup = recovered.Cleanup(world);
            Assert.Equal(3, cleanup.Removed);
            Assert.Equal(0, world.LiveCount); // AT-QA-CLEANUP-NO-LEAK across a crash boundary
        }

        [Fact] // Reconcile: a Created entry whose world object vanished is downgraded so ensure re-creates it.
        public void Reconcile_downgrades_created_entry_whose_object_vanished()
        {
            var plan = Validated(Plan("fx1", ("vanilla.wood.pile", 2, 1.0)));
            var world = new FakeFixtureWorld();

            var before = OwnedResourceLedger.ForPlan(plan);
            before.Ensure(world, TestRun.Ctx);
            string snapshot = SnapshotCodec.Encode(before.ToSnapshot());

            // Crash BETWEEN create and durable landing: created objects vanished from the world.
            world.WipeCreated();

            var recovered = OwnedResourceLedger.FromSnapshot(SnapshotCodec.Decode(snapshot).Snapshot!);
            Assert.Equal(2, recovered.ReconcileWithWorld(world));
            Assert.Equal(2, recovered.CountInState(OwnedResourceState.Planned));

            // Ensure re-creates the missing tail; no double-create, converges to exactly the plan.
            var ensure = recovered.Ensure(world, TestRun.Ctx);
            Assert.Equal(2, ensure.Created);
            Assert.Equal(2, world.LiveCount);

            var cleanup = recovered.Cleanup(world);
            Assert.Equal(2, cleanup.Removed);
            Assert.Equal(0, world.LiveCount);
        }

        [Fact] // Tampered/truncated snapshot decodes to a typed failure, never a partial ledger.
        public void Tampered_snapshot_fails_closed()
        {
            Assert.False(SnapshotCodec.Decode("garbage").Ok);
            Assert.False(SnapshotCodec.Decode("").Ok);

            var plan = Validated(Plan("fx1", ("vanilla.wood.pile", 1, 1.0)));
            var ledger = OwnedResourceLedger.ForPlan(plan);
            string good = SnapshotCodec.Encode(ledger.ToSnapshot());

            // Corrupt the declared count so it disagrees with the body.
            string bad = good.Replace("\t1\n", "\t9\n");
            var decoded = SnapshotCodec.Decode(bad);
            Assert.False(decoded.Ok);
            Assert.Null(decoded.Snapshot);
        }

        [Fact] // Snapshot framing survives odd characters in ids (escaping correctness).
        public void Snapshot_escapes_odd_id_characters()
        {
            var allow = new ResourceAllowlist(new Dictionary<string, ResourceCategory>
            {
                { "weird\tid\nwith\\chars", ResourceCategory.Material },
            });
            var r = FixturePlanValidator.Validate(
                new FixturePlan("fx\t1", new[] { new ResourceSpec("weird\tid\nwith\\chars", 1, 1.0) }),
                allow, FixtureBounds.Default);
            Assert.True(r.Accepted);

            var ledger = OwnedResourceLedger.ForPlan(r.Plan!);
            var decoded = SnapshotCodec.Decode(SnapshotCodec.Encode(ledger.ToSnapshot()));
            Assert.True(decoded.Ok, decoded.Error);
            Assert.Equal("fx\t1", decoded.Snapshot!.FixtureId);
            Assert.Equal("weird\tid\nwith\\chars", decoded.Snapshot!.Entries[0].Id.LogicalId);
        }

        [Fact] // OwnedResourceId value equality (idempotency depends on it).
        public void OwnedResourceId_equality_is_by_value()
        {
            var a = new OwnedResourceId("fx", "log", 2);
            var b = new OwnedResourceId("fx", "log", 2);
            var c = new OwnedResourceId("fx", "log", 3);
            Assert.Equal(a, b);
            Assert.True(a == b);
            Assert.NotEqual(a, c);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }
    }
}
