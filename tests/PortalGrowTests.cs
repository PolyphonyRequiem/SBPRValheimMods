using SBPR.Trailborne.Core.Portals;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    /// <summary>
    /// Guards <see cref="PortalGrow.Progress"/> — the engine-free grow rule extracted from
    /// AncientPortalTag.ComputeProgress (arch review P1). Every edge case the spec (§3.6, AT-GROW)
    /// named as a hazard was previously verifiable ONLY by planting a portal in-world and
    /// relogging; here they are unit tests. The rule fails toward 0 (stay seed, never activate)
    /// on any not-ready read, so a bad clock/stamp never strands or prematurely fires a portal.
    /// </summary>
    public class PortalGrowTests
    {
        private const float Grow = 15f;                       // AncientPortalTag.GrowSeconds
        private const long Tps = PortalGrow.TicksPerSecond;   // 10,000,000

        // A plausible non-zero network-clock base (ticks). Arbitrary — only deltas matter.
        private const long Base = 638_000_000_000_000_000L;

        // ── Unstamped: a non-owner before the owner's write propagates. Stay seed, DON'T fire. ──
        [Fact]
        public void Unstamped_ReturnsZero()
        {
            Assert.Equal(0f, PortalGrow.Progress(plantTicks: 0L, nowTicks: Base, growSeconds: Grow));
            Assert.False(PortalGrow.IsGrown(0L, Base, Grow));
        }

        // ── Clock not up yet (now == 0): stay seed, never activate on a bad read. ─────────────
        [Fact]
        public void ClockNotUp_ReturnsZero()
        {
            Assert.Equal(0f, PortalGrow.Progress(plantTicks: Base, nowTicks: 0L, growSeconds: Grow));
        }

        // ── Freshly planted (elapsed 0): seed-scaled. ────────────────────────────────────────
        [Fact]
        public void JustPlanted_ReturnsZero()
        {
            Assert.Equal(0f, PortalGrow.Progress(Base, Base, Grow));
        }

        // ── Mid-grow: half the window elapsed → 0.5. (The relog-durable case: absolute
        //    world-time delta, not session-relative.) ──────────────────────────────────────────
        [Fact]
        public void Halfway_ReturnsHalf()
        {
            long now = Base + (long)(7.5 * Tps);              // 7.5 s of a 15 s window
            Assert.Equal(0.5f, PortalGrow.Progress(Base, now, Grow), 3);
        }

        [Theory]
        [InlineData(3.0, 0.2)]
        [InlineData(7.5, 0.5)]
        [InlineData(12.0, 0.8)]
        [InlineData(14.999, 0.9999)]
        public void Progress_IsLinearAcrossWindow(double elapsedSec, double expected)
        {
            long now = Base + (long)(elapsedSec * Tps);
            Assert.Equal((float)expected, PortalGrow.Progress(Base, now, Grow), 3);
        }

        // ── Exactly at the window boundary → grown. ──────────────────────────────────────────
        [Fact]
        public void AtWindowEnd_IsGrown()
        {
            long now = Base + (long)(Grow * Tps);
            Assert.Equal(1f, PortalGrow.Progress(Base, now, Grow));
            Assert.True(PortalGrow.IsGrown(Base, now, Grow));
        }

        // ── Past the window (relog after 15 s elapsed): clamped to 1, activate immediately. ───
        [Fact]
        public void PastWindow_ClampsToOne_AndIsGrown()
        {
            long now = Base + (long)(1000 * Tps);            // way past
            Assert.Equal(1f, PortalGrow.Progress(Base, now, Grow));
            Assert.True(PortalGrow.IsGrown(Base, now, Grow));
        }

        // ── Clock behind the stamp (negative elapsed, e.g. clock correction): stay seed. ──────
        [Fact]
        public void ClockBehindStamp_ReturnsZero()
        {
            long now = Base - (long)(5 * Tps);
            Assert.Equal(0f, PortalGrow.Progress(Base, now, Grow));
            Assert.False(PortalGrow.IsGrown(Base, now, Grow));
        }
    }
}
