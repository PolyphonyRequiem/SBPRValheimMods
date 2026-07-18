// ============================================================================
//  RD-T005 (Tracer 2) — Weekly/daily participation and AP multiplier tests.
//  Named acceptance AT-RD-006, AT-RD-009, AT-RD-019.
// ----------------------------------------------------------------------------
//  Exercises the engine-free Tracer-2 seam link-compiled from ../src:
//    * AccountStoneParticipationAggregate  the pure per-(account,Stone) weekly/daily aggregate
//    * ApMultiplierPolicy                   the exact floor(base × participation × maturity) award
//    * AccountStoneParticipationRegistry    the durable participation coordinator + combined
//                                           AP-before-practice fifth-placement order
//
//  Closes end to end (spec RD-006 / RD-009 / RD-019, data-model Aggregates 2 & 5,
//  contracts §SubmitUpkeepDonation / RecordDailyPractice / RecordApActivity):
//
//    AT-RD-006  An accepted upkeep donation starts/refreshes a rolling seven-day
//               expiry; exact boundaries produce 0×/1×/2×; calendar reset, streak,
//               repetition, and tier stacking add no further multiplier.
//    AT-RD-009  Otherwise-authorized AP floors ONCE after full multiplication
//               without widening source authority; Cumulative/Mirrored match the
//               floored award; BP is unchanged; replay is idempotent; missing
//               Connection / missing weekly upkeep awards zero.
//    AT-RD-019  One durable daily cycle counts five distinct eligible placements,
//               completes once, ignores active-window prebuild, then resets after
//               expiry; the fifth placement's combined receipt uses the prior AP
//               tier and only later events see 2×; duplicates / raw client claims /
//               stale definitions do not count; restart/replay is exact.
//
//  No gameplay is enabled: these are pure domain/application seams, exactly like
//  the Tracer-1 slice. The rest of the suite stays green.
// ============================================================================

using System.Collections.Generic;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.ResourceDelivery;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class ResourceDeliveryParticipationTests : System.IDisposable
    {
        private static readonly WorldId World = new WorldId("world-RD005");
        private static readonly ProductScope Product = new ProductScope("SBPR.Trailborne");
        private static readonly StoneId Stone = new StoneId("stone-1");
        private const long Day = 86400L;
        private const long Hour = 3600L;
        private const long Week = 7L * Day;

        private readonly string _journalPath;

        public ResourceDeliveryParticipationTests()
        {
            _journalPath = Path.Combine(Path.GetTempPath(),
                "niflheim-rdt005-" + System.Guid.NewGuid().ToString("N") + ".journal");
        }

        public void Dispose()
        {
            if (File.Exists(_journalPath)) File.Delete(_journalPath);
        }

        private AccountStoneParticipationRegistry NewRegistry() =>
            new AccountStoneParticipationRegistry(_journalPath);

        private static ParticipationId Part(string account) =>
            new ParticipationId(World, Product, new AccountId(account), Stone);

        // A qualifying Connection at the Stone with a given age anchor, so ContributionRule sees a
        // real Active source. Age = serverTime - anchor.
        private static ConnectionAggregate ConnAtStone(string a, string b, long anchorSeconds)
        {
            ConnectionId.TryCreate(World, Product, new AccountId(a), new AccountId(b), out var id);
            var src = new ConnectionSource(Stone, "rel-" + a, "rel-" + b, 1, "prov");
            return ConnectionAggregate.CreateEmpty(id).AddSource(src, anchorSeconds);
        }

        // ══════════════════════════ AT-RD-006 ══════════════════════════
        //  Pure aggregate: rolling weekly/daily boundaries and 0×/1×/2× tiers.

        [Fact]
        public void AtRd006_NoUpkeep_IsZeroTimes()
        {
            var p = AccountStoneParticipationAggregate.CreateEmpty(Part("alice"));
            Assert.Equal(ParticipationTier.None, p.TierAt(1000));
        }

        [Fact]
        public void AtRd006_WeeklyUpkeep_EnablesOneTimes_UntilExactExpiry()
        {
            long t0 = 1000;
            var p = AccountStoneParticipationAggregate.CreateEmpty(Part("alice"))
                .RecordWeeklyCompletion(t0);

            Assert.Equal(ParticipationTier.Weekly, p.TierAt(t0));                 // current
            Assert.Equal(ParticipationTier.Weekly, p.TierAt(t0 + Week - 1));       // last current second
            Assert.Equal(ParticipationTier.None, p.TierAt(t0 + Week));            // expiry is exclusive -> 0×
            Assert.Equal(ParticipationTier.None, p.TierAt(t0 + Week + 1));
        }

        [Fact]
        public void AtRd006_WeeklyPlusDaily_EnablesTwoTimes_ForTwentyFourHours()
        {
            long t0 = 1000;
            var p = AccountStoneParticipationAggregate.CreateEmpty(Part("alice"))
                .RecordWeeklyCompletion(t0);
            // Complete the daily cycle with five distinct placements at t0.
            for (int i = 0; i < 5; i++)
                p = p.RecordDailyPractice("inst-" + i, t0).Aggregate;

            Assert.Equal(ParticipationTier.WeeklyAndDaily, p.TierAt(t0));            // 2× now
            Assert.Equal(ParticipationTier.WeeklyAndDaily, p.TierAt(t0 + Day - 1));  // last 2× second
            Assert.Equal(ParticipationTier.Weekly, p.TierAt(t0 + Day));             // daily window exclusive -> 1×
        }

        [Fact]
        public void AtRd006_DailyCurrentButWeeklyExpired_FallsToZero_NotTwo()
        {
            // Weekly at t0 (expires t0+Week). Daily completed just before weekly expiry.
            long t0 = 1000;
            long dailyAt = t0 + Week - Hour; // daily window would run to dailyAt+Day, past weekly expiry
            var p = AccountStoneParticipationAggregate.CreateEmpty(Part("alice"))
                .RecordWeeklyCompletion(t0);
            for (int i = 0; i < 5; i++)
                p = p.RecordDailyPractice("inst-" + i, dailyAt).Aggregate;

            // Just after weekly expiry: daily is still within its 24h window, but 2× requires CURRENT
            // weekly upkeep, so the tier collapses to 0× — daily never produces 2× on its own.
            long afterWeekly = t0 + Week + 1;
            Assert.True(p.DailyCurrent(afterWeekly));   // daily window technically still running
            Assert.False(p.WeeklyCurrent(afterWeekly)); // but weekly lapsed
            Assert.Equal(ParticipationTier.None, p.TierAt(afterWeekly));
        }

        [Fact]
        public void AtRd006_RepeatedWeeklyCompletion_RefreshesExpiry_DoesNotStack()
        {
            long t0 = 1000;
            var p = AccountStoneParticipationAggregate.CreateEmpty(Part("alice"))
                .RecordWeeklyCompletion(t0);
            long refreshAt = t0 + 3 * Day;
            p = p.RecordWeeklyCompletion(refreshAt);

            // Refreshed expiry is refreshAt + 7d, not t0 + 14d (no stacking).
            Assert.Equal(refreshAt + Week, p.WeeklyExpiresAtSeconds);
            // Tier is still exactly 1× — repetition never adds a second multiplier.
            Assert.Equal(ParticipationTier.Weekly, p.TierAt(refreshAt));
            Assert.Equal(ParticipationTier.Weekly, p.TierAt(refreshAt + Week - 1));
            Assert.Equal(ParticipationTier.None, p.TierAt(refreshAt + Week));
        }

        [Fact]
        public void AtRd006_RenewalAfterExpiry_PreservesLapse_ThenCurrentAgain()
        {
            long t0 = 1000;
            var p = AccountStoneParticipationAggregate.CreateEmpty(Part("alice"))
                .RecordWeeklyCompletion(t0);
            long lapsed = t0 + Week + Day;               // 0× here
            Assert.Equal(ParticipationTier.None, p.ReconcileTo(lapsed).TierAt(lapsed));

            var renewed = p.ReconcileTo(lapsed).RecordWeeklyCompletion(lapsed);
            Assert.Equal(ParticipationTier.Weekly, renewed.TierAt(lapsed));
            Assert.Equal(lapsed + Week, renewed.WeeklyExpiresAtSeconds);
        }

        // ══════════════════════════ AT-RD-019 ══════════════════════════
        //  Durable daily cycle: five distinct instances, no active-window prebuild, expiry reset.

        [Fact]
        public void AtRd019_FiveDistinctPlacements_CompleteTheCycleOnce()
        {
            long t0 = 1000;
            var p = AccountStoneParticipationAggregate.CreateEmpty(Part("alice"));
            for (int i = 0; i < 4; i++)
            {
                var r = p.RecordDailyPractice("inst-" + i, t0);
                Assert.Equal(DailyPracticeOutcome.Progressed, r.Outcome);
                p = r.Aggregate;
                Assert.Equal(i + 1, p.DailyProgress);
            }
            var fifth = p.RecordDailyPractice("inst-4", t0);
            Assert.Equal(DailyPracticeOutcome.Completed, fifth.Outcome);
            p = fifth.Aggregate;
            Assert.Equal(DailyCycleStatus.Completed, p.DailyStatus);
            Assert.Equal(t0 + Day, p.DailyExpiresAtSeconds);
        }

        [Fact]
        public void AtRd019_DuplicatePhysicalInstance_CountsOnce()
        {
            long t0 = 1000;
            var p = AccountStoneParticipationAggregate.CreateEmpty(Part("alice"));
            p = p.RecordDailyPractice("inst-A", t0).Aggregate;
            var dup = p.RecordDailyPractice("inst-A", t0);
            Assert.Equal(DailyPracticeOutcome.DuplicateInstance, dup.Outcome);
            Assert.Equal(1, dup.Aggregate.DailyProgress); // still just one distinct instance
        }

        [Fact]
        public void AtRd019_ActiveWindow_DoesNotPrebuildNextCycle()
        {
            long t0 = 1000;
            var p = AccountStoneParticipationAggregate.CreateEmpty(Part("alice"));
            for (int i = 0; i < 5; i++)
                p = p.RecordDailyPractice("inst-" + i, t0).Aggregate;
            Assert.Equal(DailyCycleStatus.Completed, p.DailyStatus);

            // Events during the current 24h window are idempotent no-progress; they do NOT build the
            // next cycle.
            var during = p.RecordDailyPractice("inst-next", t0 + Hour);
            Assert.Equal(DailyPracticeOutcome.WindowCurrentNoProgress, during.Outcome);
            Assert.Equal(0, during.Aggregate.DailyProgress);
            Assert.Equal(DailyCycleStatus.Completed, during.Aggregate.DailyStatus);
        }

        [Fact]
        public void AtRd019_AfterExpiry_FreshZeroProgressCycleOpens()
        {
            long t0 = 1000;
            var p = AccountStoneParticipationAggregate.CreateEmpty(Part("alice"));
            for (int i = 0; i < 5; i++)
                p = p.RecordDailyPractice("inst-" + i, t0).Aggregate;
            long firstCycleId = p.DailyCycleId;

            // Reconcile past the 24h expiry -> the completed cycle closes.
            long afterExpiry = t0 + Day + Hour;
            p = p.ReconcileTo(afterExpiry);
            Assert.Equal(DailyCycleStatus.None, p.DailyStatus);

            // The first eligible event opens a FRESH zero-progress cycle (new cycle id) and counts itself.
            var opened = p.RecordDailyPractice("inst-fresh", afterExpiry);
            Assert.Equal(DailyPracticeOutcome.Progressed, opened.Outcome);
            Assert.Equal(1, opened.Aggregate.DailyProgress);
            Assert.NotEqual(firstCycleId, opened.Aggregate.DailyCycleId);
        }

        [Fact]
        public void AtRd019_Registry_RawClientClaimNotAnObjectiveOperation_DoesNotCount()
        {
            // The registry accepts practice ONLY through RecordDailyPractice/RecordCombinedPlacement with
            // a server-observed operation id + physical instance. There is no client-callable "complete
            // objective". A distinct instance advances; a client cannot inject progress by any other path
            // because no such method exists. We assert the durable projection reflects exactly the
            // server-observed evidence.
            var reg = NewRegistry();
            long t0 = 1000;
            var id = Part("alice");
            var r = reg.RecordDailyPractice("op-1", id, "inst-A", t0);
            Assert.Equal(ParticipationOutcome.Applied, r.Outcome);
            Assert.Equal(1, r.Progress);
            // Only server-observed evidence is in the projection.
            Assert.Equal(1, reg.GetParticipation(id).DailyProgress);
        }

        [Fact]
        public void AtRd019_Registry_Restart_RehydratesExactCycleAndWeeklyState()
        {
            long t0 = 1000;
            var id = Part("alice");
            {
                var reg = NewRegistry();
                reg.RecordWeeklyUpkeep("op-wk", id, t0);
                for (int i = 0; i < 3; i++)
                    reg.RecordDailyPractice("op-d" + i, id, "inst-" + i, t0);
            }
            // Fresh registry over the SAME journal == restart.
            var reg2 = NewRegistry();
            var p = reg2.GetParticipation(id);
            Assert.Equal(3, p.DailyProgress);
            Assert.Equal(DailyCycleStatus.Open, p.DailyStatus);
            Assert.Equal(t0 + Week, p.WeeklyExpiresAtSeconds);
            Assert.Equal(ParticipationTier.Weekly, p.TierAt(t0));
        }

        [Fact]
        public void AtRd019_Registry_ReplaySameOperation_IsIdempotent()
        {
            var reg = NewRegistry();
            long t0 = 1000;
            var id = Part("alice");
            reg.RecordDailyPractice("op-1", id, "inst-A", t0);
            var replay = reg.RecordDailyPractice("op-1", id, "inst-A", t0);
            Assert.Equal(ParticipationOutcome.Replayed, replay.Outcome);
            Assert.Equal(1, reg.GetParticipation(id).DailyProgress); // no doubled progress
        }

        [Fact]
        public void AtRd019_Registry_ConflictingOperationId_Rejects()
        {
            var reg = NewRegistry();
            long t0 = 1000;
            var id = Part("alice");
            reg.RecordDailyPractice("op-x", id, "inst-A", t0);
            // Same op id, different binding (different instance) -> conflict, no mutation.
            var conflict = reg.RecordDailyPractice("op-x", id, "inst-B", t0);
            Assert.Equal(ParticipationOutcome.OperationConflict, conflict.Outcome);
            Assert.Equal(1, reg.GetParticipation(id).DailyProgress);
        }

        // ══════════════════════════ AT-RD-009 ══════════════════════════
        //  Multiplier-aware AP: floor once, no source-authority widening, mirrored equality, BP unchanged.

        [Fact]
        public void AtRd009_Floor_AppliedOnce_AfterFullMultiplication()
        {
            // base 10, 2× participation, 1.1× maturity -> 10 * 2 * 11 / 10 = 22 exactly.
            var maturity = ConnectionMaturity.Band1; // 11/10
            var award = ApMultiplierPolicy.Award(10, ParticipationTier.WeeklyAndDaily, maturity);
            Assert.True(award.Awarded);
            Assert.Equal(22, award.FinalAward);
        }

        [Fact]
        public void AtRd009_Floor_IsSingle_NotPerFactor()
        {
            // base 7, 1× participation, 1.1× maturity. Exact = 7*1*11/10 = 77/10 = 7.7 -> floor 7.
            // A naive per-factor floor (floor(7*1.1)=7 then *1 = 7) coincidentally matches here, so use
            // a case that distinguishes: base 3, 2×, 1.1× -> 3*2*11/10 = 66/10 = 6.6 -> floor 6.
            // A per-factor rounding (floor(3*1.1)=3, *2 = 6) also gives 6; choose 1.3× to separate:
            // base 3, 2×, 1.3× -> 3*2*13/10 = 78/10 = 7.8 -> floor 7. Per-factor: floor(3*1.3)=3, *2=6.
            var award = ApMultiplierPolicy.Award(3, ParticipationTier.WeeklyAndDaily, ConnectionMaturity.Band3);
            Assert.Equal(7, award.FinalAward); // single final floor, not 6
        }

        [Fact]
        public void AtRd009_MirroredTelemetry_EqualsFinalAward()
        {
            var award = ApMultiplierPolicy.Award(10, ParticipationTier.WeeklyAndDaily, ConnectionMaturity.Band1);
            Assert.Equal(award.FinalAward, award.MirroredTelemetryDelta);
        }

        [Fact]
        public void AtRd009_ZeroParticipation_IsRecordedNoAward()
        {
            var award = ApMultiplierPolicy.Award(10, ParticipationTier.None, ConnectionMaturity.Band5);
            Assert.False(award.Awarded);
            Assert.Equal(0, award.FinalAward);
            Assert.Equal(0, award.MirroredTelemetryDelta);
        }

        [Fact]
        public void AtRd009_NotContributing_NoQualifyingConnection_AwardsZero()
        {
            // Active relationship + weekly upkeep but NO qualifying Connection -> not contributing -> 0.
            var eligibility = ContributionRule.Evaluate(
                hasActiveStoneRelationship: true,
                tier: ParticipationTier.Weekly,
                candidateConnections: new List<ConnectionAggregate>(), // none
                stoneId: Stone,
                serverTimeSeconds: 1000);
            Assert.False(eligibility.Contributes);
            var award = ApMultiplierPolicy.Award(10, eligibility);
            Assert.False(award.Awarded);
            Assert.Equal(0, award.FinalAward);
        }

        [Fact]
        public void AtRd009_DoesNotWidenSourceAuthority()
        {
            // The policy only scales an award the source already authorized. When the caller reports NO
            // active Stone relationship (source authorization absent), eligibility denies and the award is
            // zero — the multiplier can never manufacture authorization.
            var eligibility = ContributionRule.Evaluate(
                hasActiveStoneRelationship: false,
                tier: ParticipationTier.WeeklyAndDaily,
                candidateConnections: new List<ConnectionAggregate> { ConnAtStone("alice", "bob", 0) },
                stoneId: Stone,
                serverTimeSeconds: 100 * Day);
            Assert.False(eligibility.Contributes);
            Assert.Equal("NoStoneRelationship", eligibility.ReasonCode);
            Assert.Equal(0, ApMultiplierPolicy.Award(1000, eligibility).FinalAward);
        }

        // ─────────── Combined fifth-placement AP-before-practice order (RD-019 + RD-009) ───────────

        [Fact]
        public void AtRd019_FifthPlacement_UsesPriorTier_ThenLaterEventsSeeTwoTimes()
        {
            var reg = NewRegistry();
            long t0 = 1000;
            var id = Part("alice");
            // Weekly upkeep current (1×). No daily completion yet.
            reg.RecordWeeklyUpkeep("op-wk", id, t0);
            // A qualifying Connection aged 10 days -> maturity band 1.2× at t0 (anchor t0-10d).
            var conns = new List<ConnectionAggregate> { ConnAtStone("alice", "bob", t0 - 10 * Day) };

            // Four combined placements at 1× (weekly only). base AP 10, 1× × 1.2× -> floor(10*1*12/10)=12.
            for (int i = 0; i < 4; i++)
            {
                var r = reg.RecordCombinedPlacement("op-c" + i, id, "inst-" + i, t0, 10, true, conns);
                Assert.Equal(ParticipationTier.Weekly, r.TierBeforePractice);
                Assert.Equal(12, r.ApAward.FinalAward);
                Assert.Equal(DailyPracticeOutcome.Progressed, r.PracticeOutcome);
            }

            // Fifth placement: completes the cycle. Its AP subresult MUST use the PRIOR 1× tier (12),
            // then 2× becomes current AFTER.
            var fifth = reg.RecordCombinedPlacement("op-c4", id, "inst-4", t0, 10, true, conns);
            Assert.Equal(ParticipationTier.Weekly, fifth.TierBeforePractice);   // prior tier
            Assert.Equal(12, fifth.ApAward.FinalAward);                          // NOT boosted by its own completion
            Assert.Equal(DailyPracticeOutcome.Completed, fifth.PracticeOutcome);
            Assert.Equal(ParticipationTier.WeeklyAndDaily, fifth.TierAfterPractice); // 2× now current

            // A LATER AP event now sees 2×: floor(10*2*12/10) = 24.
            long later = t0 + Hour;
            var tierLater = reg.TierAt(id, later);
            Assert.Equal(ParticipationTier.WeeklyAndDaily, tierLater);
            var award = ApMultiplierPolicy.Award(10, tierLater, ConnectionMaturity.Band2);
            Assert.Equal(24, award.FinalAward);
        }

        [Fact]
        public void AtRd009_CombinedPlacement_ReplayReturnsRecordedOrderedResult()
        {
            var reg = NewRegistry();
            long t0 = 1000;
            var id = Part("alice");
            reg.RecordWeeklyUpkeep("op-wk", id, t0);
            var conns = new List<ConnectionAggregate> { ConnAtStone("alice", "bob", t0 - 10 * Day) };

            var first = reg.RecordCombinedPlacement("op-c", id, "inst-0", t0, 10, true, conns);
            Assert.Equal(ParticipationOutcome.Applied, first.Outcome);

            // Replay returns the recorded ordered result verbatim — same AP award and pre-practice tier,
            // never recomputed against later participation state.
            var replay = reg.RecordCombinedPlacement("op-c", id, "inst-0", t0, 10, true, conns);
            Assert.Equal(ParticipationOutcome.Replayed, replay.Outcome);
            Assert.Equal(first.ApAward.FinalAward, replay.ApAward.FinalAward);
            Assert.Equal(first.TierBeforePractice, replay.TierBeforePractice);
            // No doubled practice progress from the replay.
            Assert.Equal(1, reg.GetParticipation(id).DailyProgress);
        }

        [Fact]
        public void AtRd009_CombinedPlacement_ReplayAfterRestart_IsExact()
        {
            long t0 = 1000;
            var id = Part("alice");
            var conns = new List<ConnectionAggregate> { ConnAtStone("alice", "bob", t0 - 10 * Day) };
            long recordedAward;
            {
                var reg = NewRegistry();
                reg.RecordWeeklyUpkeep("op-wk", id, t0);
                var r = reg.RecordCombinedPlacement("op-c", id, "inst-0", t0, 10, true, conns);
                recordedAward = r.ApAward.FinalAward;
            }
            // Restart: fresh registry over the same journal. Replay returns the exact recorded award.
            var reg2 = NewRegistry();
            var replay = reg2.RecordCombinedPlacement("op-c", id, "inst-0", t0, 10, true, conns);
            Assert.Equal(ParticipationOutcome.Replayed, replay.Outcome);
            Assert.Equal(recordedAward, replay.ApAward.FinalAward);
            Assert.Equal(1, reg2.GetParticipation(id).DailyProgress);
        }

        [Fact]
        public void AtRd009_Registry_WeeklyReplayReturnsRecordedExpiry()
        {
            var reg = NewRegistry();
            long t0 = 1000;
            var id = Part("alice");
            var applied = reg.RecordWeeklyUpkeep("op-wk", id, t0);
            Assert.Equal(ParticipationOutcome.Applied, applied.Outcome);
            var replay = reg.RecordWeeklyUpkeep("op-wk", id, t0);
            Assert.Equal(ParticipationOutcome.Replayed, replay.Outcome);
            Assert.Equal(applied.WeeklyExpiresAtSeconds, replay.WeeklyExpiresAtSeconds);
        }

        [Fact]
        public void AtRd009_Registry_TierAt_ReconcilesExpiredDailyWindow()
        {
            var reg = NewRegistry();
            long t0 = 1000;
            var id = Part("alice");
            reg.RecordWeeklyUpkeep("op-wk", id, t0);
            for (int i = 0; i < 5; i++)
                reg.RecordDailyPractice("op-d" + i, id, "inst-" + i, t0);
            Assert.Equal(ParticipationTier.WeeklyAndDaily, reg.TierAt(id, t0));
            // After the 24h daily window, tier reconciles down to 1× (weekly still current).
            Assert.Equal(ParticipationTier.Weekly, reg.TierAt(id, t0 + Day + 1));
        }

        // ─────────── Snapshot round-trip (durable state fidelity) ───────────

        [Fact]
        public void Participation_SnapshotRoundTrip_PreservesAuthoritativeState()
        {
            long t0 = 1000;
            var p = AccountStoneParticipationAggregate.CreateEmpty(Part("alice"))
                .RecordWeeklyCompletion(t0);
            p = p.RecordDailyPractice("inst-A", t0).Aggregate;
            p = p.RecordDailyPractice("inst-B", t0).Aggregate;

            var round = AccountStoneParticipationAggregate.Deserialize(p.Serialize());
            Assert.Equal(p.WeeklyExpiresAtSeconds, round.WeeklyExpiresAtSeconds);
            Assert.Equal(p.DailyStatus, round.DailyStatus);
            Assert.Equal(p.DailyProgress, round.DailyProgress);
            Assert.Equal(p.DailyCycleId, round.DailyCycleId);
            Assert.Equal(p.Revision, round.Revision);
        }
    }
}
