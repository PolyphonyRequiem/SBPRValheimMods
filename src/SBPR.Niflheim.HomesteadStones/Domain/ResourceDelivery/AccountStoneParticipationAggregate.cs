using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery
{
    // RD-T005 (Tracer 2) — the pure AccountStoneParticipationAggregate (data-model Aggregate 2). One
    // server-owned per-(account, Stone) upkeep/practice status aggregate. Named acceptance: AT-RD-006
    // (rolling weekly/daily boundaries and 0×/1×/2× tiers) and AT-RD-019 (durable five-distinct daily
    // cycle, active-window no-prebuild, expiry reset).
    //
    // WHAT THIS OWNS (spec RD-006 / RD-019, data-model Aggregate 2)
    //   * Weekly upkeep: an accepted donation starts/refreshes a rolling SEVEN-DAY expiry. Repeated
    //     completion refreshes the latest timestamp; it never stacks another multiplier.
    //   * Daily practice: one durable cycle per account/Stone. While no completion is current, five
    //     DISTINCT physical eligible Foundational placement instances complete it; each instance counts
    //     at most once. Completion closes the cycle and starts a rolling 24-HOUR window. Events during
    //     that window do NOT pre-build the next cycle. After expiry is reconciled, the next eligible
    //     event opens a fresh zero-progress cycle.
    //   * Derived tier: 0× when weekly upkeep is expired/missing; 1× while weekly is current; 2× while
    //     weekly is current AND daily practice is within its rolling 24-hour window.
    //
    // PURITY / REPLAY
    //   Every transition is pure: it validates accepted policy and returns a NEW aggregate (never
    //   mutates in place, reads Unity state, or persists). Because weekly/daily expiry are wall-time
    //   thresholds against durable timestamps, replaying the same ordered transitions at the same
    //   server times reconstructs the exact tier/cycle state after a restart. Same-server-time
    //   mutations are ordered by durable receipt sequence upstream; this aggregate treats zero elapsed
    //   time between them as legal.
    //
    // net48 audit: System.Collections.Generic + value objects + the snapshot codec. Engine-free — no
    // UnityEngine/Valheim/BepInEx — so it link-compiles into the net8 test project.

    /// <summary>Canonical per-account, per-Stone participation identity =
    /// <c>(WorldId, ProductScope, AccountId, StoneId)</c> (data-model §Stable identities). One account's
    /// upkeep/practice state at one Stone. Compared ordinally on all four components.</summary>
    public readonly struct ParticipationId : IEquatable<ParticipationId>
    {
        public ParticipationId(WorldId world, ProductScope product, AccountId account, StoneId stone)
        {
            World = world;
            Product = product;
            Account = account;
            Stone = stone;
        }

        public WorldId World { get; }
        public ProductScope Product { get; }
        public AccountId Account { get; }
        public StoneId Stone { get; }

        /// <summary>Stable string key derived only from the four identity components; used as a
        /// dictionary/journal key and stable across process/replay.</summary>
        public string CanonicalKey =>
            World.Value + "\u0001" + Product.Value + "\u0001" + Account.Value + "\u0001" + Stone.Value;

        public bool Equals(ParticipationId other) =>
            World.Equals(other.World) && Product.Equals(other.Product) &&
            Account.Equals(other.Account) && Stone.Equals(other.Stone);
        public override bool Equals(object? obj) => obj is ParticipationId other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + World.GetHashCode();
                h = h * 31 + Product.GetHashCode();
                h = h * 31 + Account.GetHashCode();
                h = h * 31 + Stone.GetHashCode();
                return h;
            }
        }
        public override string ToString() => "Participation(" + CanonicalKey + ")";
    }

    /// <summary>Status of the single durable daily-practice cycle (data-model Aggregate 2 §Daily
    /// practice).</summary>
    public enum DailyCycleStatus
    {
        /// <summary>No cycle is open and none is current. The next eligible event opens a fresh
        /// zero-progress cycle.</summary>
        None = 0,
        /// <summary>A cycle is accumulating distinct placements toward the five-instance threshold.</summary>
        Open = 1,
        /// <summary>The cycle completed at five distinct instances; a rolling 24-hour window is running.
        /// While current it produces 2× (with weekly upkeep) and no further evidence pre-builds the next
        /// cycle.</summary>
        Completed = 2
    }

    /// <summary>The outcome of a <see cref="AccountStoneParticipationAggregate.RecordDailyPractice"/>
    /// call, so the caller can build a receipt and know whether the cycle closed on this instance.</summary>
    public enum DailyPracticeOutcome
    {
        /// <summary>A new distinct instance was recorded toward an open cycle (below five).</summary>
        Progressed = 0,
        /// <summary>This instance was the fifth distinct one; the cycle closed and the 24-hour window
        /// started at the event time.</summary>
        Completed = 1,
        /// <summary>A completed window is still current: this evidence is an idempotent no-progress
        /// result and does NOT pre-build the next cycle.</summary>
        WindowCurrentNoProgress = 2,
        /// <summary>The instance id already counted in the current open cycle: a no-op.</summary>
        DuplicateInstance = 3
    }

    /// <summary>The pure per-(account, Stone) participation aggregate (data-model Aggregate 2). Immutable
    /// value; every transition returns a new instance.</summary>
    public sealed class AccountStoneParticipationAggregate
    {
        /// <summary>Rolling weekly upkeep window: seven days in whole seconds (spec RD-006).</summary>
        public const long WeeklySeconds = 7L * 86400L;

        /// <summary>Rolling daily-practice window: 24 hours in whole seconds (spec RD-006 / RD-019).</summary>
        public const long DailySeconds = 24L * 3600L;

        /// <summary>Distinct physical eligible Foundational placements that complete the first daily
        /// fixture (spec RD-019).</summary>
        public const int DailyThreshold = 5;

        public AccountStoneParticipationAggregate(
            ParticipationId id,
            long revision,
            long cursorSeconds,
            long weeklyCompletedAtSeconds,
            long weeklyExpiresAtSeconds,
            long dailyCycleId,
            DailyCycleStatus dailyStatus,
            IReadOnlyList<string>? dailyInstanceIds,
            long dailyCompletedAtSeconds,
            long dailyExpiresAtSeconds,
            int schemaVersion = 1)
        {
            Id = id;
            Revision = revision;
            CursorSeconds = cursorSeconds;
            WeeklyCompletedAtSeconds = weeklyCompletedAtSeconds;
            WeeklyExpiresAtSeconds = weeklyExpiresAtSeconds;
            DailyCycleId = dailyCycleId;
            DailyStatus = dailyStatus;
            DailyInstanceIds = dailyInstanceIds ?? Array.Empty<string>();
            DailyCompletedAtSeconds = dailyCompletedAtSeconds;
            DailyExpiresAtSeconds = dailyExpiresAtSeconds;
            SchemaVersion = schemaVersion;
        }

        public ParticipationId Id { get; }
        public long Revision { get; }

        /// <summary>Last reconciled server time (whole seconds). Advances forward only.</summary>
        public long CursorSeconds { get; }

        public long WeeklyCompletedAtSeconds { get; }

        /// <summary>Server time at which weekly upkeep expires. Weekly is current iff
        /// serverTime &lt; this. Zero means no upkeep has ever been recorded.</summary>
        public long WeeklyExpiresAtSeconds { get; }

        /// <summary>Monotonic daily cycle identity; incremented each time a fresh cycle opens.</summary>
        public long DailyCycleId { get; }

        public DailyCycleStatus DailyStatus { get; }

        /// <summary>Distinct physical-instance ids counted in the CURRENT open cycle. Empty when no cycle
        /// is open. Progress = <c>DailyInstanceIds.Count</c>.</summary>
        public IReadOnlyList<string> DailyInstanceIds { get; }

        public long DailyCompletedAtSeconds { get; }

        /// <summary>Server time at which the completed daily window expires. Daily practice is current iff
        /// status is Completed AND serverTime &lt; this.</summary>
        public long DailyExpiresAtSeconds { get; }

        public int SchemaVersion { get; }

        /// <summary>Distinct-instance progress in the current open cycle (0 when none open).</summary>
        public int DailyProgress => DailyStatus == DailyCycleStatus.Open ? DailyInstanceIds.Count : 0;

        /// <summary>A fresh participation aggregate for the given identity: no upkeep, no cycle.</summary>
        public static AccountStoneParticipationAggregate CreateEmpty(ParticipationId id) =>
            new AccountStoneParticipationAggregate(id, 0, 0, 0, 0, 0,
                DailyCycleStatus.None, null, 0, 0);

        // ---- Derived participation tier (data-model Aggregate 2 §Derived participation multiplier) ----

        /// <summary>Whether weekly upkeep is current at <paramref name="serverTimeSeconds"/>.</summary>
        public bool WeeklyCurrent(long serverTimeSeconds) =>
            WeeklyExpiresAtSeconds > 0 && serverTimeSeconds < WeeklyExpiresAtSeconds;

        /// <summary>Whether the daily-practice window is current at <paramref name="serverTimeSeconds"/>.
        /// True only while a completed cycle's rolling 24-hour window has not expired.</summary>
        public bool DailyCurrent(long serverTimeSeconds) =>
            DailyStatus == DailyCycleStatus.Completed && serverTimeSeconds < DailyExpiresAtSeconds;

        /// <summary>The exact participation tier at <paramref name="serverTimeSeconds"/> (spec RD-006).
        /// 0× without current weekly upkeep, 1× with weekly only, 2× with weekly + current daily.
        /// Daily practice never produces 2× on its own — it requires current weekly upkeep.</summary>
        public ParticipationTier TierAt(long serverTimeSeconds)
        {
            if (!WeeklyCurrent(serverTimeSeconds)) return ParticipationTier.None;
            return DailyCurrent(serverTimeSeconds)
                ? ParticipationTier.WeeklyAndDaily
                : ParticipationTier.Weekly;
        }

        // ---- Transitions (pure; each returns a new aggregate) ----

        /// <summary>Advance the reconciliation cursor to <paramref name="serverTimeSeconds"/> under the
        /// PRIOR completion/expiry state (contracts §RecordDailyPractice / RecordApActivity: "reconcile
        /// [lastCursor, receivedServerTime) under the old state"). An expired completed daily window is
        /// closed here so the next eligible event opens a fresh zero-progress cycle rather than
        /// pre-building against a stale window. Negative elapsed time (clock ran backwards) is a no-op:
        /// the cursor never moves backward. Idempotent when nothing changes.</summary>
        public AccountStoneParticipationAggregate ReconcileTo(long serverTimeSeconds)
        {
            if (serverTimeSeconds <= CursorSeconds)
                return this; // no forward time / clock anomaly: nothing to reconcile

            bool dailyExpired = DailyStatus == DailyCycleStatus.Completed
                && serverTimeSeconds >= DailyExpiresAtSeconds;

            if (!dailyExpired)
            {
                // Only the cursor advances; weekly/daily windows keep ticking against their expiries.
                return new AccountStoneParticipationAggregate(
                    Id, Revision + 1, serverTimeSeconds,
                    WeeklyCompletedAtSeconds, WeeklyExpiresAtSeconds,
                    DailyCycleId, DailyStatus, DailyInstanceIds,
                    DailyCompletedAtSeconds, DailyExpiresAtSeconds, SchemaVersion);
            }

            // The completed daily window has lapsed: close the cycle. Tier falls from 2× to 1× (if weekly
            // still current). The next eligible placement opens a fresh zero-progress cycle.
            return new AccountStoneParticipationAggregate(
                Id, Revision + 1, serverTimeSeconds,
                WeeklyCompletedAtSeconds, WeeklyExpiresAtSeconds,
                DailyCycleId, DailyCycleStatus.None, Array.Empty<string>(),
                DailyCompletedAtSeconds, DailyExpiresAtSeconds, SchemaVersion);
        }

        /// <summary>Record an accepted weekly upkeep completion at <paramref name="serverTimeSeconds"/>.
        /// Starts/refreshes the rolling seven-day expiry (spec RD-006). Repeated completion refreshes the
        /// latest timestamp; it never stacks another multiplier. The caller reconciles the elapsed span
        /// BEFORE calling this so a lapse/renewal interval stays recoverable.</summary>
        public AccountStoneParticipationAggregate RecordWeeklyCompletion(long serverTimeSeconds)
        {
            return new AccountStoneParticipationAggregate(
                Id, Revision + 1, CursorSeconds,
                weeklyCompletedAtSeconds: serverTimeSeconds,
                weeklyExpiresAtSeconds: serverTimeSeconds + WeeklySeconds,
                DailyCycleId, DailyStatus, DailyInstanceIds,
                DailyCompletedAtSeconds, DailyExpiresAtSeconds, SchemaVersion);
        }

        /// <summary>Result of a daily-practice mutation: the new aggregate plus the classified outcome.</summary>
        public readonly struct DailyPracticeResult
        {
            public DailyPracticeResult(AccountStoneParticipationAggregate aggregate, DailyPracticeOutcome outcome)
            {
                Aggregate = aggregate;
                Outcome = outcome;
            }

            public AccountStoneParticipationAggregate Aggregate { get; }
            public DailyPracticeOutcome Outcome { get; }
        }

        /// <summary>Record one distinct physical eligible Foundational placement toward the daily cycle
        /// (spec RD-019). The caller MUST reconcile the elapsed span first (see <see cref="ReconcileTo"/>)
        /// so an expired window is already closed. Behavior:
        /// <list type="bullet">
        /// <item>Completed window still current -> idempotent no-progress; the next cycle is NOT
        ///   pre-built.</item>
        /// <item>No cycle open -> open a fresh zero-progress cycle and count this instance.</item>
        /// <item>Cycle open, new distinct instance -> record progress; the fifth distinct instance closes
        ///   the cycle and starts the rolling 24-hour window at the event time.</item>
        /// <item>Cycle open, duplicate instance -> no-op.</item>
        /// </list>
        /// This mutates ONLY practice progress; the combined AP-before-practice receipt order is enforced
        /// by the application layer, which snapshots the pre-practice tier before calling this.</summary>
        public DailyPracticeResult RecordDailyPractice(string physicalInstanceId, long serverTimeSeconds)
        {
            if (string.IsNullOrEmpty(physicalInstanceId))
                throw new ArgumentException("physicalInstanceId required", nameof(physicalInstanceId));

            // While a completed window is still current, do not pre-build the next cycle (spec RD-019).
            if (DailyCurrent(serverTimeSeconds))
                return new DailyPracticeResult(this, DailyPracticeOutcome.WindowCurrentNoProgress);

            // No open cycle (fresh, or an expired window that reconciliation closed): open a fresh
            // zero-progress cycle and count this instance as its first.
            if (DailyStatus != DailyCycleStatus.Open)
            {
                var opened = new List<string> { physicalInstanceId };
                return CloseOrProgress(opened, DailyCycleId + 1, serverTimeSeconds);
            }

            // An open cycle: duplicate physical instances count at most once.
            foreach (var existing in DailyInstanceIds)
                if (string.Equals(existing, physicalInstanceId, StringComparison.Ordinal))
                    return new DailyPracticeResult(this, DailyPracticeOutcome.DuplicateInstance);

            var next = new List<string>(DailyInstanceIds.Count + 1);
            next.AddRange(DailyInstanceIds);
            next.Add(physicalInstanceId);
            return CloseOrProgress(next, DailyCycleId, serverTimeSeconds);
        }

        private DailyPracticeResult CloseOrProgress(List<string> instances, long cycleId, long serverTimeSeconds)
        {
            if (instances.Count >= DailyThreshold)
            {
                // Fifth distinct instance: close the cycle and start the rolling 24-hour window.
                var completed = new AccountStoneParticipationAggregate(
                    Id, Revision + 1, CursorSeconds,
                    WeeklyCompletedAtSeconds, WeeklyExpiresAtSeconds,
                    cycleId, DailyCycleStatus.Completed, Array.Empty<string>(),
                    dailyCompletedAtSeconds: serverTimeSeconds,
                    dailyExpiresAtSeconds: serverTimeSeconds + DailySeconds,
                    SchemaVersion);
                return new DailyPracticeResult(completed, DailyPracticeOutcome.Completed);
            }

            var progressed = new AccountStoneParticipationAggregate(
                Id, Revision + 1, CursorSeconds,
                WeeklyCompletedAtSeconds, WeeklyExpiresAtSeconds,
                cycleId, DailyCycleStatus.Open, instances,
                DailyCompletedAtSeconds, DailyExpiresAtSeconds, SchemaVersion);
            return new DailyPracticeResult(progressed, DailyPracticeOutcome.Progressed);
        }

        // ---- Snapshot codec (round-trips every authoritative field) ----

        public string Serialize() => new SnapshotWriter()
            .PutInt("schema", SchemaVersion)
            .Put("world", Id.World.Value)
            .Put("product", Id.Product.Value)
            .Put("account", Id.Account.Value)
            .Put("stone", Id.Stone.Value)
            .PutLong("rev", Revision)
            .PutLong("cursor", CursorSeconds)
            .PutLong("wkDone", WeeklyCompletedAtSeconds)
            .PutLong("wkExp", WeeklyExpiresAtSeconds)
            .PutLong("cycId", DailyCycleId)
            .PutInt("cycStatus", (int)DailyStatus)
            .PutList("cycInst", (IReadOnlyList<string>)DailyInstanceIds, s => s)
            .PutLong("dyDone", DailyCompletedAtSeconds)
            .PutLong("dyExp", DailyExpiresAtSeconds)
            .Build();

        public static AccountStoneParticipationAggregate Deserialize(string snapshot)
        {
            var r = new SnapshotReader(snapshot);
            var id = new ParticipationId(
                new WorldId(r.GetString("world")),
                new ProductScope(r.GetString("product")),
                new AccountId(r.GetString("account")),
                new StoneId(r.GetString("stone")));
            var instances = r.GetList("cycInst", enc => enc);
            return new AccountStoneParticipationAggregate(
                id,
                r.GetLong("rev"),
                r.GetLong("cursor"),
                r.GetLong("wkDone"),
                r.GetLong("wkExp"),
                r.GetLong("cycId"),
                (DailyCycleStatus)r.GetInt("cycStatus"),
                instances,
                r.GetLong("dyDone"),
                r.GetLong("dyExp"),
                r.GetInt("schema"));
        }
    }
}
