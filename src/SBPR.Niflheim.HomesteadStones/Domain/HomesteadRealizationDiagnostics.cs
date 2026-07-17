using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SBPR.Niflheim.HomesteadStones.Domain
{
    // FIX (Homestead Stone realization lifecycle) — engine-free realization gate + bounded diagnostics.
    //
    // WHY THIS EXISTS
    // The live defect (T009L2 FAIL) was a SILENT non-realization: on a headless dedicated server every
    // selected candidate was dropped before seat evaluation and NOTHING was logged, so an operator had no
    // signal to act on. Root cause (decompiled vanilla, ADR-0001 base-game RE): the engine-bound placement
    // loop gated each candidate on `ZoneSystem.IsZoneLoaded(zone)`, but a dedicated server only adds zones
    // to `ZoneSystem.m_zones` for the active area around `ZNet.GetReferencePosition()` — which never moves
    // off the far-away sentinel because there is no local player. A joined peer's location zone is realized
    // only as ghost ZDOs (via CreateGhostZones/SpawnMode.Ghost); `IsZoneLoaded` is permanently false for it.
    //
    // This type holds the two decisions that determine realization, plus the bounded-diagnostic state, as
    // PURE logic so they are unit-tested under net8 and cannot silently regress:
    //   * <see cref="ShouldRealize"/> — whether a selected candidate should realize THIS pass, and if not,
    //     the exact reason (a stable enum), so the engine-bound loop can COUNT reasons instead of dropping
    //     candidates into the void.
    //   * <see cref="RealizationPass"/> — accumulates per-pass reason counts and answers whether the pass's
    //     shape CHANGED since the last one, so the loop logs one bounded summary line only on change (never
    //     per-tick spam).
    //   * <see cref="StonelessWatch"/> — tracks how long a selected zone that the server has PLACED (its
    //     vanilla location instance exists) has gone without a realized Stone, and fires exactly one warning
    //     per zone once a bounded interval elapses — the actionable "a resident selected zone remains
    //     Stone-less" signal acceptance item 3 demands.
    //
    // net48 audit: System + collections only. Link-compiles into the net8 test project.

    /// <summary>Why a selected candidate did (or did not) realize a Homestead Stone this pass. Stable
    /// ordinal contract — persisted only as a diagnostic count, never as domain state.</summary>
    public enum RealizationGate
    {
        /// <summary>The candidate's Stone already has a resident ZDO for its host zone (idempotent skip).</summary>
        AlreadyRealized = 0,

        /// <summary>The vanilla location instance for the host zone is not placed yet, so the terrain/world
        /// generation this Stone anchors to does not exist. Deferred; revisited as the world realizes.</summary>
        ZoneNotPlaced = 1,

        /// <summary>The host zone is placed and the Stone is not yet resident — this pass will attempt to
        /// realize it (seat + persist). The one gate value that proceeds past the drop guard.</summary>
        Eligible = 2,
    }

    /// <summary>Pure realization-gate decision. Engine-bound callers supply two SERVER-OWNED booleans
    /// (does a resident Stone ZDO already exist for the host zone; is the vanilla location instance
    /// placed) and receive the stable gate reason.</summary>
    public static class HomesteadRealizationGateEvaluator
    {
        public static RealizationGate Evaluate(bool stoneAlreadyResident, bool hostLocationPlaced)
        {
            if (stoneAlreadyResident) return RealizationGate.AlreadyRealized;
            if (!hostLocationPlaced) return RealizationGate.ZoneNotPlaced;
            return RealizationGate.Eligible;
        }
    }

    /// <summary>Why an ELIGIBLE candidate did not produce a Stone this pass. Distinguishes the failure
    /// modes the R1 warning conflated ("every seat attempt is failing"), so the operator sees the ACTUAL
    /// cause. Stable ordinal — surfaced only as a diagnostic count/reason, never persisted.</summary>
    public enum SeatSkipReason
    {
        /// <summary>No world generator was available to resolve terrain (should be transient at boot).</summary>
        NoWorldGenerator = 0,

        /// <summary>Headless: the host is placed but its structure ZDOs are not persisted yet, so the
        /// footprint/surface are unknown. Creation is DEFERRED, not failed — revisited next pass.</summary>
        DeferredNoStructureEvidence = 1,

        /// <summary>All eight deterministic seats were evaluated and rejected (footprint overlap, insufficient
        /// clearance, or no local surface evidence). An honest 8-of-8 skip, not a forced seat.</summary>
        AllSeatsRejected = 2,

        /// <summary>Live path (listen server / host): the collider-aware best-of-eight found no valid seat or
        /// the local Heightmap could not resolve a height.</summary>
        LiveSeatUnavailable = 3,
    }

    /// <summary>Accumulates the gate outcomes of a single realization pass and renders a bounded, stable
    /// operator summary. The engine-bound loop compares each finished pass to the previous one via
    /// <see cref="Signature"/> and logs only when the shape changed, so a steady state is silent.</summary>
    public sealed class RealizationPass
    {
        private readonly Dictionary<RealizationGate, int> _counts = new Dictionary<RealizationGate, int>();
        private readonly Dictionary<SeatSkipReason, int> _skipReasons = new Dictionary<SeatSkipReason, int>();
        private int _realized;
        private int _seatSkipped;

        public int Selected { get; private set; }

        /// <summary>Record one selected candidate's gate outcome.</summary>
        public void Observe(RealizationGate gate)
        {
            Selected++;
            _counts.TryGetValue(gate, out var current);
            _counts[gate] = current + 1;
        }

        /// <summary>An eligible candidate that reached seat evaluation and produced a persistent Stone.</summary>
        public void Realized() => _realized++;

        /// <summary>An eligible candidate that did NOT produce a Stone this pass, with the specific reason
        /// (deferred vs honest 8-of-8 skip vs missing generator vs live-seat unavailable). Reasons are
        /// counted so the operator summary can distinguish them instead of asserting "every seat is failing".</summary>
        public void SeatSkipped(SeatSkipReason reason)
        {
            _seatSkipped++;
            _skipReasons.TryGetValue(reason, out var current);
            _skipReasons[reason] = current + 1;
        }

        public int Count(RealizationGate gate)
        {
            _counts.TryGetValue(gate, out var current);
            return current;
        }

        public int Count(SeatSkipReason reason)
        {
            _skipReasons.TryGetValue(reason, out var current);
            return current;
        }

        public int RealizedCount => _realized;
        public int SeatSkippedCount => _seatSkipped;

        /// <summary>A stable, allocation-cheap signature of this pass's shape used for change detection.
        /// Two passes with the same signature describe the same steady state and must not both log.</summary>
        public string Signature => string.Format(
            CultureInfo.InvariantCulture,
            "sel={0};already={1};notplaced={2};eligible={3};realized={4};seatskip={5};" +
            "nogen={6};defer={7};reject={8};live={9}",
            Selected,
            Count(RealizationGate.AlreadyRealized),
            Count(RealizationGate.ZoneNotPlaced),
            Count(RealizationGate.Eligible),
            _realized,
            _seatSkipped,
            Count(SeatSkipReason.NoWorldGenerator),
            Count(SeatSkipReason.DeferredNoStructureEvidence),
            Count(SeatSkipReason.AllSeatsRejected),
            Count(SeatSkipReason.LiveSeatUnavailable));

        /// <summary>One-line, PII-free operator summary. Skip reasons are broken out so a DEFERRED zone
        /// (evidence not yet persisted) is never reported as a seat failure.</summary>
        public string ToOperatorLine() => string.Format(
            CultureInfo.InvariantCulture,
            "Realization pass: selected={0} realizedThisPass={1} alreadyResident={2} zoneNotPlaced={3} " +
            "eligible={4} seatSkipped={5} (noWorldGen={6} deferredNoEvidence={7} allSeatsRejected={8} liveSeatUnavailable={9}).",
            Selected,
            _realized,
            Count(RealizationGate.AlreadyRealized),
            Count(RealizationGate.ZoneNotPlaced),
            Count(RealizationGate.Eligible),
            _seatSkipped,
            Count(SeatSkipReason.NoWorldGenerator),
            Count(SeatSkipReason.DeferredNoStructureEvidence),
            Count(SeatSkipReason.AllSeatsRejected),
            Count(SeatSkipReason.LiveSeatUnavailable));
    }

    /// <summary>Change-gated pass logger. Feed it each finished <see cref="RealizationPass"/>; it returns
    /// the summary line to log ONLY when the pass shape changed from the last logged one (and always on the
    /// very first pass), so a long-running server emits no per-tick spam.</summary>
    public sealed class RealizationPassReporter
    {
        private string? _lastSignature;

        /// <summary>Returns the operator line to log, or null to stay silent (unchanged steady state).</summary>
        public string? Consider(RealizationPass pass)
        {
            if (pass == null) throw new ArgumentNullException(nameof(pass));
            var signature = pass.Signature;
            if (string.Equals(signature, _lastSignature, StringComparison.Ordinal)) return null;
            _lastSignature = signature;
            return pass.ToOperatorLine();
        }
    }

    /// <summary>Tracks selected zones whose vanilla host location is placed but which still have no resident
    /// Stone, and fires exactly one actionable warning per zone once a bounded stone-less interval elapses.
    /// This turns "silently nothing" into the resident-selected-zone-remains-Stone-less signal acceptance
    /// item 3 requires, without per-tick spam and without leaking unbounded state (resolved zones are
    /// dropped).</summary>
    public sealed class StonelessWatch
    {
        private readonly double _warnAfterSeconds;
        private readonly Dictionary<string, double> _firstPlacedElapsed = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        public StonelessWatch(double warnAfterSeconds)
        {
            if (warnAfterSeconds <= 0.0) throw new ArgumentOutOfRangeException(nameof(warnAfterSeconds));
            _warnAfterSeconds = warnAfterSeconds;
        }

        /// <summary>Advance the watch to an absolute monotonic <paramref name="elapsedSeconds"/> clock,
        /// given the current set of zone keys that are PLACED-but-STONELESS this pass. Returns the zone keys
        /// that just crossed the warn threshold (each returned at most once for its stone-less episode).</summary>
        public IReadOnlyList<string> Advance(double elapsedSeconds, IEnumerable<string> placedStonelessZoneKeys)
        {
            if (placedStonelessZoneKeys == null) throw new ArgumentNullException(nameof(placedStonelessZoneKeys));
            var current = new HashSet<string>(placedStonelessZoneKeys, StringComparer.Ordinal);

            // Drop zones that are no longer placed-and-stoneless (realized, or host location gone): they end
            // their episode, so a later relapse starts a fresh timer and can warn again.
            foreach (var key in _firstPlacedElapsed.Keys.Where(k => !current.Contains(k)).ToList())
            {
                _firstPlacedElapsed.Remove(key);
                _warned.Remove(key);
            }

            var crossed = new List<string>();
            foreach (var key in current)
            {
                if (!_firstPlacedElapsed.TryGetValue(key, out var firstSeen))
                {
                    _firstPlacedElapsed[key] = elapsedSeconds;
                    continue;
                }
                if (_warned.Contains(key)) continue;
                if (elapsedSeconds - firstSeen >= _warnAfterSeconds)
                {
                    _warned.Add(key);
                    crossed.Add(key);
                }
            }
            crossed.Sort(StringComparer.Ordinal);
            return crossed;
        }
    }
}
