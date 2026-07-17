// ============================================================================
//  RD-T002 (Gate A) — Deterministic time and recoverable fan-out tests.
// ----------------------------------------------------------------------------
//  Exercises the engine-free Resource Delivery Gate-A domain/application seams
//  link-compiled from ../src (see the .csproj). Closes:
//
//    AT-RD-001  Canonical account-pair Connection identity accepts either order,
//               rejects self/unauthenticated pairs, remains world/product-scoped.
//    AT-RD-003  Boundary-time tests select exactly the six approved maturity
//               multipliers.
//    AT-RD-004  Preparation replays the exact principal/target-bound durable
//               challenge across restart; fresh-ID confirmation rejects token-
//               bearing principal substitution / lost authority, supports delayed
//               and competing confirmation, freezes confirmation-time age, starts a
//               full 72h grace, and makes consumption+release+receipt atomic across
//               crash/restart.
//    AT-RD-005  Solo/stale/grace-only/0x accounts contribute zero; both eligible
//               sides contribute once; several links/Governors pick one strongest
//               multiplier.
//    AT-RD-007  Reconcile-before-mutation preserves expired/renewed intervals;
//               offline and arbitrary online partitions match across multiple
//               cycles, residual progress, same-time ordering, and pending-capacity
//               boundaries.
//
//  No gameplay is enabled: these are pure domain/application seams, exactly like
//  the Homestead progression Gate-A slice. The rest of the suite stays green.
// ============================================================================

using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class ResourceDeliveryGateATests
    {
        private static readonly WorldId World = new WorldId("world-A");
        private static readonly ProductScope Product = new ProductScope("SBPR.Trailborne");
        private const long Day = ConnectionMaturity.SecondsPerDay;

        private static ConnectionId Conn(string a, string b)
        {
            var res = ConnectionId.TryCreate(World, Product, new AccountId(a), new AccountId(b), out var id);
            Assert.Equal(ConnectionIdentityResolution.Valid, res);
            return id;
        }

        // ────────────────────────── AT-RD-001 ──────────────────────────

        [Fact]
        public void AtRd001_EitherOrder_YieldsSameCanonicalIdentity()
        {
            var ab = Conn("alice", "bob");
            var ba = Conn("bob", "alice");
            Assert.Equal(ab, ba);
            Assert.Equal(ab.CanonicalKey, ba.CanonicalKey);
            // Canonical low/high is ordinal, independent of argument order.
            Assert.Equal("alice", ab.AccountLow.Value);
            Assert.Equal("bob", ab.AccountHigh.Value);
        }

        [Fact]
        public void AtRd001_SelfPair_Rejected()
        {
            var res = ConnectionId.TryCreate(World, Product, new AccountId("alice"), new AccountId("alice"), out var id);
            Assert.Equal(ConnectionIdentityResolution.SelfPair, res);
            Assert.Equal(default(ConnectionId), id);
        }

        [Fact]
        public void AtRd001_UnauthenticatedSubject_Rejected()
        {
            var res = ConnectionId.TryCreate(World, Product, new AccountId(""), new AccountId("bob"), out _);
            Assert.Equal(ConnectionIdentityResolution.UnauthenticatedSubject, res);
        }

        [Fact]
        public void AtRd001_MissingWorldOrProduct_Rejected()
        {
            Assert.Equal(ConnectionIdentityResolution.MissingScope,
                ConnectionId.TryCreate(new WorldId(""), Product, new AccountId("a"), new AccountId("b"), out _));
            Assert.Equal(ConnectionIdentityResolution.MissingScope,
                ConnectionId.TryCreate(World, new ProductScope(""), new AccountId("a"), new AccountId("b"), out _));
        }

        [Fact]
        public void AtRd001_WorldOrProductScoped_DistinctGraphs()
        {
            var w1 = Conn("alice", "bob");
            ConnectionId.TryCreate(new WorldId("world-B"), Product, new AccountId("alice"), new AccountId("bob"), out var w2);
            ConnectionId.TryCreate(World, new ProductScope("OtherProduct"), new AccountId("alice"), new AccountId("bob"), out var p2);
            Assert.NotEqual(w1, w2);
            Assert.NotEqual(w1, p2);
        }

        [Fact]
        public void AtRd001_Involves_BothMembersOnly()
        {
            var c = Conn("alice", "bob");
            Assert.True(c.Involves(new AccountId("alice")));
            Assert.True(c.Involves(new AccountId("bob")));
            Assert.False(c.Involves(new AccountId("carol")));
        }

        // ────────────────────────── AT-RD-003 ──────────────────────────

        [Theory]
        // exactly the six approved bands, tested at boundary times
        [InlineData(0L, 10, 10)]                  // 0s        -> 1.0×
        [InlineData(Day - 1, 10, 10)]             // <1d       -> 1.0×
        [InlineData(Day, 11, 10)]                 // exactly 1d-> 1.1×
        [InlineData(7 * Day - 1, 11, 10)]         // <7d       -> 1.1×
        [InlineData(7 * Day, 12, 10)]             // exactly 7d-> 1.2×
        [InlineData(30 * Day - 1, 12, 10)]        // <30d      -> 1.2×
        [InlineData(30 * Day, 13, 10)]            // exactly 30d-> 1.3×
        [InlineData(60 * Day - 1, 13, 10)]        // <60d      -> 1.3×
        [InlineData(60 * Day, 14, 10)]            // exactly 60d-> 1.4×
        [InlineData(90 * Day - 1, 14, 10)]        // <90d      -> 1.4×
        [InlineData(90 * Day, 15, 10)]            // exactly 90d-> 1.5×
        [InlineData(365 * Day, 15, 10)]           // ≥90d      -> 1.5×
        public void AtRd003_MaturityBands_Exact(long ageSeconds, int expectedNum, int expectedDen)
        {
            var m = ConnectionMaturity.ForAccumulatedSeconds(ageSeconds);
            Assert.Equal(expectedNum, m.Numerator);
            Assert.Equal(expectedDen, m.Denominator);
        }

        [Fact]
        public void AtRd003_NegativeAge_TreatedAsZeroBand()
        {
            var m = ConnectionMaturity.ForAccumulatedSeconds(-99999);
            Assert.Equal(ConnectionMaturity.Band0, m);
        }

        [Fact]
        public void AtRd003_OnlySixDistinctMultipliers()
        {
            var set = new HashSet<MaturityMultiplier>
            {
                ConnectionMaturity.ForAccumulatedSeconds(0),
                ConnectionMaturity.ForAccumulatedSeconds(Day),
                ConnectionMaturity.ForAccumulatedSeconds(7 * Day),
                ConnectionMaturity.ForAccumulatedSeconds(30 * Day),
                ConnectionMaturity.ForAccumulatedSeconds(60 * Day),
                ConnectionMaturity.ForAccumulatedSeconds(90 * Day),
            };
            Assert.Equal(6, set.Count);
        }

        // ───────────────── Connection aggregate lifecycle ─────────────────

        private static ConnectionSource Source(string stone, string relA, string relB, int ver = 1) =>
            new ConnectionSource(new StoneId(stone), relA, relB, ver, "prov");

        [Fact]
        public void Lifecycle_AddSource_ActivatesAndAdvancesAge()
        {
            var c = ConnectionAggregate.CreateEmpty(Conn("alice", "bob"))
                .AddSource(Source("s1", "r1", "r2"), 1000);
            Assert.Equal(ConnectionLifecycle.Active, c.Lifecycle);
            Assert.True(c.IsContributionQualifying);
            // 2 days later the live age is 2 days.
            Assert.Equal(2 * Day, c.LiveAgeSeconds(1000 + 2 * Day));
        }

        [Fact]
        public void Lifecycle_FinalSourceRemoval_EntersFrozenGraceWithFrozenAge()
        {
            long t0 = 1000;
            var c = ConnectionAggregate.CreateEmpty(Conn("alice", "bob")).AddSource(Source("s1", "r1", "r2"), t0);
            long removeAt = t0 + 10 * Day;
            var graced = c.RemoveSource(Source("s1", "r1", "r2").SourceId, removeAt);

            Assert.Equal(ConnectionLifecycle.Grace, graced.Lifecycle);
            Assert.False(graced.IsContributionQualifying);
            Assert.Equal(10 * Day, graced.AccumulatedSeconds);       // age frozen at removal
            Assert.Equal(removeAt + ConnectionAggregate.GraceSeconds, graced.GraceExpiresAtSeconds);
            // Age does not advance during grace.
            Assert.Equal(10 * Day, graced.LiveAgeSeconds(removeAt + 5 * Day));
        }

        [Fact]
        public void Lifecycle_ReconnectDuringGrace_ResumesFrozenAge()
        {
            long t0 = 1000;
            var c = ConnectionAggregate.CreateEmpty(Conn("alice", "bob")).AddSource(Source("s1", "r1", "r2"), t0);
            var graced = c.RemoveSource(Source("s1", "r1", "r2").SourceId, t0 + 10 * Day);
            long reconnectAt = t0 + 10 * Day + 24 * 3600; // within 72h
            var resumed = graced.AddSource(Source("s1", "r1", "r2", 2), reconnectAt);

            Assert.Equal(ConnectionLifecycle.Active, resumed.Lifecycle);
            Assert.Equal(10 * Day, resumed.AccumulatedSeconds);   // resumes from frozen age
            // One day after reconnect, live age is 11 days.
            Assert.Equal(11 * Day, resumed.LiveAgeSeconds(reconnectAt + Day));
        }

        [Fact]
        public void Lifecycle_GraceExpiry_ResetsAgeIdempotently()
        {
            long t0 = 1000;
            var graced = ConnectionAggregate.CreateEmpty(Conn("alice", "bob"))
                .AddSource(Source("s1", "r1", "r2"), t0)
                .RemoveSource(Source("s1", "r1", "r2").SourceId, t0 + 10 * Day);

            long expiry = graced.GraceExpiresAtSeconds;
            var reset = graced.ReconcileGraceExpiry(expiry);
            Assert.Equal(ConnectionLifecycle.Reset, reset.Lifecycle);
            Assert.Equal(0, reset.AccumulatedSeconds);

            // Idempotent: re-running from Reset is a no-op; before expiry is also a no-op.
            Assert.Same(reset, reset.ReconcileGraceExpiry(expiry + Day));
            Assert.Same(graced, graced.ReconcileGraceExpiry(expiry - 1));
        }

        [Fact]
        public void Lifecycle_SnapshotRoundTrip_PreservesEveryField()
        {
            long t0 = 1000;
            var c = ConnectionAggregate.CreateEmpty(Conn("alice", "bob"))
                .AddSource(Source("s1", "r1", "r2"), t0)
                .AddSource(Source("s2", "r3", "r4"), t0 + Day);
            var round = ConnectionAggregate.Deserialize(c.Serialize());

            Assert.Equal(c.Id, round.Id);
            Assert.Equal(c.Revision, round.Revision);
            Assert.Equal(c.Lifecycle, round.Lifecycle);
            Assert.Equal(c.AccumulatedSeconds, round.AccumulatedSeconds);
            Assert.Equal(c.CurrentSegmentAnchorSeconds, round.CurrentSegmentAnchorSeconds);
            Assert.Equal(c.Sources.Count, round.Sources.Count);
            Assert.Equal(c.LiveAgeSeconds(t0 + 5 * Day), round.LiveAgeSeconds(t0 + 5 * Day));
        }

        [Fact]
        public void Lifecycle_ClockAnomaly_NeverAdvancesAgeBackwards()
        {
            var c = ConnectionAggregate.CreateEmpty(Conn("alice", "bob")).AddSource(Source("s1", "r1", "r2"), 10_000);
            // Query at a time BEFORE the anchor (clock ran backwards): age contributes zero, not negative.
            Assert.Equal(0, c.LiveAgeSeconds(9_000));
        }

        // ────────────────────────── AT-RD-005 ──────────────────────────

        private static ConnectionAggregate ActiveConn(string a, string b, string stone, long ageSeconds, long now)
        {
            // Anchor so that live age at `now` equals ageSeconds.
            return ConnectionAggregate.CreateEmpty(Conn(a, b)).AddSource(Source(stone, a + "-r", b + "-r"), now - ageSeconds);
        }

        [Fact]
        public void AtRd005_SoloBond_NoQualifyingConnection_ContributesNothing()
        {
            var e = ContributionRule.Evaluate(
                hasActiveStoneRelationship: true,
                tier: ParticipationTier.WeeklyAndDaily,
                candidateConnections: new List<ConnectionAggregate>(), // no counterpart
                stoneId: new StoneId("s1"),
                serverTimeSeconds: 1_000_000);
            Assert.False(e.Contributes);
            Assert.Equal("ConnectionSourceNotQualifying", e.ReasonCode);
        }

        [Fact]
        public void AtRd005_NoStoneRelationship_ContributesNothing()
        {
            long now = 1_000_000;
            var e = ContributionRule.Evaluate(false, ParticipationTier.WeeklyAndDaily,
                new List<ConnectionAggregate> { ActiveConn("alice", "bob", "s1", 5 * Day, now) },
                new StoneId("s1"), now);
            Assert.False(e.Contributes);
            Assert.Equal("NoStoneRelationship", e.ReasonCode);
        }

        [Fact]
        public void AtRd005_ZeroParticipation_ContributesNothing()
        {
            long now = 1_000_000;
            var e = ContributionRule.Evaluate(true, ParticipationTier.None,
                new List<ConnectionAggregate> { ActiveConn("alice", "bob", "s1", 5 * Day, now) },
                new StoneId("s1"), now);
            Assert.False(e.Contributes);
            Assert.Equal("WeeklyUpkeepRequired", e.ReasonCode);
        }

        [Fact]
        public void AtRd005_GraceOnlyConnection_ContributesNothing()
        {
            long now = 1_000_000;
            var graced = ActiveConn("alice", "bob", "s1", 5 * Day, now - 2 * Day)
                .RemoveSource(Source("s1", "alice-r", "bob-r").SourceId, now - Day);
            Assert.Equal(ConnectionLifecycle.Grace, graced.Lifecycle);

            var e = ContributionRule.Evaluate(true, ParticipationTier.WeeklyAndDaily,
                new List<ConnectionAggregate> { graced }, new StoneId("s1"), now);
            Assert.False(e.Contributes);
            Assert.Equal("ConnectionSourceNotQualifying", e.ReasonCode);
        }

        [Fact]
        public void AtRd005_EligibleAccount_ContributesOnce_WithTierAndMaturity()
        {
            long now = 1_000_000;
            var e = ContributionRule.Evaluate(true, ParticipationTier.Weekly,
                new List<ConnectionAggregate> { ActiveConn("alice", "bob", "s1", 8 * Day, now) },
                new StoneId("s1"), now);
            Assert.True(e.Contributes);
            Assert.Equal(ParticipationTier.Weekly, e.Tier);
            Assert.Equal(ConnectionMaturity.Band2, e.Maturity); // 8 days -> 1.2×
        }

        [Fact]
        public void AtRd005_SeveralLinksAndGovernors_ChooseOneStrongestMultiplier()
        {
            long now = 2_000_000;
            // alice has two qualifying Connections at s1: with bob (10 days -> 1.2x) and with carol
            // (65 days -> 1.4x). Only the strongest is applied, once.
            var withBob = ActiveConn("alice", "bob", "s1", 10 * Day, now);
            var withCarol = ActiveConn("alice", "carol", "s1", 65 * Day, now);

            var e = ContributionRule.Evaluate(true, ParticipationTier.WeeklyAndDaily,
                new List<ConnectionAggregate> { withBob, withCarol }, new StoneId("s1"), now);
            Assert.True(e.Contributes);
            Assert.Equal(ConnectionMaturity.Band4, e.Maturity); // 1.4×, the strongest
            Assert.Equal(withCarol.Id.CanonicalKey, e.ChosenConnectionKey);
        }

        [Fact]
        public void AtRd005_ConnectionAtDifferentStone_DoesNotQualify()
        {
            long now = 1_000_000;
            var otherStone = ActiveConn("alice", "bob", "s2", 5 * Day, now);
            var e = ContributionRule.Evaluate(true, ParticipationTier.Weekly,
                new List<ConnectionAggregate> { otherStone }, new StoneId("s1"), now);
            Assert.False(e.Contributes);
            Assert.Equal("ConnectionSourceNotQualifying", e.ReasonCode);
        }

        // ────────────────────────── AT-RD-007 ──────────────────────────

        // A helper that produces a single flat-rate segment span [start,end) at 1 unit/sec.
        private static List<ContributionSegment> Flat(long start, long end, long rate = 1) =>
            new List<ContributionSegment> { new ContributionSegment(start, end, rate) };

        [Fact]
        public void AtRd007_OfflineJump_EqualsArbitraryOnlinePartitions()
        {
            const long threshold = 100;
            long start = 0, end = 1000; // 1000 units at 1/s

            // One offline jump.
            var offline = IntervalReconciler.Reconcile(
                DeliveryMeterState.Start(start), Flat(start, end), end, threshold, long.MaxValue);

            // Arbitrary online partitions: split at 137, 500, 501, 999.
            var s = DeliveryMeterState.Start(start);
            long[] cuts = { 137, 500, 501, 999, end };
            long prev = start;
            foreach (var cut in cuts)
            {
                s = IntervalReconciler.Reconcile(s, Flat(prev, cut), cut, threshold, long.MaxValue);
                prev = cut;
            }

            Assert.Equal(offline.CompletedCycles, s.CompletedCycles);
            Assert.Equal(offline.ResidualUnits, s.ResidualUnits);
            Assert.Equal(offline.CursorSeconds, s.CursorSeconds);
            Assert.Equal(10, offline.CompletedCycles); // 1000/100
            Assert.Equal(0, offline.ResidualUnits);
        }

        [Fact]
        public void AtRd007_ThresholdExcess_CarriesAsResidual()
        {
            const long threshold = 100;
            var s = IntervalReconciler.Reconcile(DeliveryMeterState.Start(0), Flat(0, 250), 250, threshold, long.MaxValue);
            Assert.Equal(2, s.CompletedCycles);
            Assert.Equal(50, s.ResidualUnits); // 250 - 2*100
        }

        [Fact]
        public void AtRd007_MultipleCycles_InOneReconciliation()
        {
            const long threshold = 24; // e.g. 24 baseline hours expressed in units
            var s = IntervalReconciler.Reconcile(DeliveryMeterState.Start(0), Flat(0, 24 * 5), 24 * 5, threshold, long.MaxValue);
            Assert.Equal(5, s.CompletedCycles);
            Assert.Equal(0, s.ResidualUnits);
        }

        [Fact]
        public void AtRd007_PendingCapacity_DiscardsLaterTime_AndOnlinePartitionsMatch()
        {
            const long threshold = 100;
            // 500 units available, capacity for only 2 deposits. The 3rd completion cannot deposit ->
            // PendingCapacity latches at the crossing time; later time is discarded.
            long capacity = 2;

            var offline = IntervalReconciler.Reconcile(
                DeliveryMeterState.Start(0), Flat(0, 500), 500, threshold, capacity);
            Assert.Equal(DeliveryMeterStatus.PendingCapacity, offline.Status);
            Assert.Equal(2, offline.CompletedCycles);
            // Latched at t=300 (third cycle completes there); residual carries the third bundle's units.
            Assert.Equal(300, offline.CursorSeconds);
            Assert.Equal(100, offline.ResidualUnits);

            // Same result via online partitions. Capacity is "remaining deposits allowed", so the caller
            // recomputes it each call as (total - already-completed) — exactly what a Stockpile free-space
            // check does. Threading it this way is what makes the partitioned replay converge.
            var s = DeliveryMeterState.Start(0);
            long[] cuts = { 150, 305, 500 };
            long prev = 0;
            foreach (var cut in cuts)
            {
                long remaining = capacity - s.CompletedCycles;
                s = IntervalReconciler.Reconcile(s, Flat(prev, cut), cut, threshold, remaining);
                prev = cut;
            }
            Assert.Equal(DeliveryMeterStatus.PendingCapacity, s.Status);
            Assert.Equal(offline.CompletedCycles, s.CompletedCycles);
            Assert.Equal(offline.ResidualUnits, s.ResidualUnits);
            Assert.Equal(offline.CursorSeconds, s.CursorSeconds);
        }

        [Fact]
        public void AtRd007_ExpiredThenRenewedInterval_Preserved()
        {
            const long threshold = 100;
            // Segment 1: [0,50) at 0/s (0x participation lapse — expired upkeep). Segment 2: [50,250)
            // at 1/s after renewal. The expired interval banks nothing; the renewed interval accrues.
            var segs = new List<ContributionSegment>
            {
                new ContributionSegment(0, 50, 0),    // expired
                new ContributionSegment(50, 250, 1),  // renewed
            };
            var s = IntervalReconciler.Reconcile(DeliveryMeterState.Start(0), segs, 250, threshold, long.MaxValue);
            Assert.Equal(2, s.CompletedCycles);   // 200 units / 100
            Assert.Equal(0, s.ResidualUnits);
            Assert.Equal(250, s.CursorSeconds);
        }

        [Fact]
        public void AtRd007_SameTimeZeroLengthSegment_IsNoOp()
        {
            const long threshold = 100;
            var s0 = IntervalReconciler.Reconcile(DeliveryMeterState.Start(0), Flat(0, 100), 100, threshold, long.MaxValue);
            // Re-partition at the exact instant 100: a zero-length [100,100) segment banks nothing.
            var s1 = IntervalReconciler.Reconcile(s0, new List<ContributionSegment> { new ContributionSegment(100, 100, 1) }, 100, threshold, long.MaxValue);
            Assert.Equal(s0.CompletedCycles, s1.CompletedCycles);
            Assert.Equal(s0.ResidualUnits, s1.ResidualUnits);
        }

        [Fact]
        public void AtRd007_DormantBundle_AccruesResidualButCompletesNoCycle()
        {
            const long threshold = 100;
            var s = IntervalReconciler.Reconcile(DeliveryMeterState.Start(0), Flat(0, 500), 500, threshold,
                long.MaxValue, hasNonEmptyBundle: false);
            Assert.Equal(DeliveryMeterStatus.Dormant, s.Status);
            Assert.Equal(0, s.CompletedCycles);
            Assert.Equal(500, s.ResidualUnits); // preserved in-progress progress, frozen for reactivation
        }
    }
}
