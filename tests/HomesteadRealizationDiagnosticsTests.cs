using System.Collections.Generic;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Domain;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    // Regression suite for the Homestead Stone realization lifecycle FIX (live T009L2 FAIL).
    //
    // The live defect was a SILENT non-realization on a headless dedicated server: every selected candidate
    // was dropped before seat evaluation with zero diagnostics. These tests pin the engine-free decisions
    // that now drive realization so the class of bug cannot silently return:
    //   * the realization gate no longer keys off local scene state — it keys off server-owned "host location
    //     placed" + "stone already resident";
    //   * the pass reporter is change-gated (no per-tick spam) yet always speaks on a shape change;
    //   * the stone-less watch fires exactly one actionable warning per stone-less zone episode after the
    //     bounded interval, and re-arms on relapse.
    public sealed class HomesteadRealizationDiagnosticsTests
    {
        [Fact]
        public void Gate_realizes_a_placed_stoneless_zone_the_prior_IsZoneLoaded_gate_dropped()
        {
            // The exact live scenario: host location is placed (server realized the zone as ghost ZDOs) and
            // no Stone is resident yet. The candidate MUST be eligible — the old build dropped it here.
            Assert.Equal(
                RealizationGate.Eligible,
                HomesteadRealizationGateEvaluator.Evaluate(stoneAlreadyResident: false, hostLocationPlaced: true));
        }

        [Fact]
        public void Gate_defers_when_host_location_not_yet_placed_and_skips_when_already_resident()
        {
            Assert.Equal(
                RealizationGate.ZoneNotPlaced,
                HomesteadRealizationGateEvaluator.Evaluate(stoneAlreadyResident: false, hostLocationPlaced: false));
            Assert.Equal(
                RealizationGate.AlreadyRealized,
                HomesteadRealizationGateEvaluator.Evaluate(stoneAlreadyResident: true, hostLocationPlaced: true));
            // Idempotency: an already-resident Stone stays skipped even if the host reports placed — a restart
            // reuses, never duplicates.
            Assert.Equal(
                RealizationGate.AlreadyRealized,
                HomesteadRealizationGateEvaluator.Evaluate(stoneAlreadyResident: true, hostLocationPlaced: false));
        }

        [Fact]
        public void Pass_signature_and_counts_reflect_the_observed_gate_outcomes()
        {
            var pass = new RealizationPass();
            pass.Observe(RealizationGate.AlreadyRealized);
            pass.Observe(RealizationGate.ZoneNotPlaced);
            pass.Observe(RealizationGate.Eligible);
            pass.Observe(RealizationGate.Eligible);
            pass.Realized();
            pass.SeatSkipped();

            Assert.Equal(4, pass.Selected);
            Assert.Equal(1, pass.Count(RealizationGate.AlreadyRealized));
            Assert.Equal(1, pass.Count(RealizationGate.ZoneNotPlaced));
            Assert.Equal(2, pass.Count(RealizationGate.Eligible));
            Assert.Equal(1, pass.RealizedCount);
            Assert.Equal(1, pass.SeatSkippedCount);
            Assert.Equal("sel=4;already=1;notplaced=1;eligible=2;realized=1;seatskip=1", pass.Signature);
        }

        [Fact]
        public void Reporter_speaks_on_first_pass_and_on_change_but_is_silent_on_a_steady_state()
        {
            var reporter = new RealizationPassReporter();

            var first = new RealizationPass();
            first.Observe(RealizationGate.Eligible);
            first.Realized();
            Assert.NotNull(reporter.Consider(first));   // first pass always speaks

            var unchanged = new RealizationPass();
            unchanged.Observe(RealizationGate.AlreadyRealized);   // 1 selected, now all resident: steady
            var steadyLine = reporter.Consider(unchanged);
            Assert.NotNull(steadyLine);                 // shape changed from realize→resident, so it speaks once

            var same = new RealizationPass();
            same.Observe(RealizationGate.AlreadyRealized);
            Assert.Null(reporter.Consider(same));       // identical shape → silent (no per-tick spam)

            var changed = new RealizationPass();
            changed.Observe(RealizationGate.AlreadyRealized);
            changed.Observe(RealizationGate.Eligible);
            changed.SeatSkipped();
            Assert.NotNull(reporter.Consider(changed)); // shape changed again → speaks
        }

        [Fact]
        public void Stoneless_watch_warns_once_after_the_interval_and_not_before()
        {
            var watch = new StonelessWatch(warnAfterSeconds: 30.0);
            var zone = new[] { "3:2" };

            Assert.Empty(watch.Advance(0.0, zone));     // first sighting: start the timer
            Assert.Empty(watch.Advance(29.0, zone));    // still under the interval
            var warned = watch.Advance(31.0, zone);     // crossed the threshold
            Assert.Equal(new[] { "3:2" }, warned);
            Assert.Empty(watch.Advance(40.0, zone));    // already warned this episode → silent
        }

        [Fact]
        public void Stoneless_watch_forgets_a_realized_zone_and_rearms_on_relapse()
        {
            var watch = new StonelessWatch(warnAfterSeconds: 30.0);

            watch.Advance(0.0, new[] { "3:2" });
            Assert.Equal(new[] { "3:2" }, watch.Advance(31.0, new[] { "3:2" }));

            // Zone realizes → drops out of the stone-less set; episode ends.
            Assert.Empty(watch.Advance(40.0, System.Array.Empty<string>()));

            // Relapse (e.g. the Stone ZDO was culled/removed): a fresh timer starts and can warn again.
            Assert.Empty(watch.Advance(50.0, new[] { "3:2" }));
            Assert.Empty(watch.Advance(79.0, new[] { "3:2" }));
            Assert.Equal(new[] { "3:2" }, watch.Advance(81.0, new[] { "3:2" }));
        }

        [Fact]
        public void Stoneless_watch_returns_crossings_in_stable_order()
        {
            var watch = new StonelessWatch(warnAfterSeconds: 10.0);
            var zones = new List<string> { "7:4", "3:2", "5:-1" };
            watch.Advance(0.0, zones);
            var warned = watch.Advance(11.0, zones);
            Assert.Equal(new[] { "3:2", "5:-1", "7:4" }, warned);
        }
    }
}
