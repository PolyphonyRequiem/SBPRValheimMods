namespace SBPR.Trailborne.Core.Portals
{
    /// <summary>
    /// The engine-free grow-progress rule for the Ancient Portal (spec §3.6, AT-GROW). A
    /// freshly-planted portal is INERT and scale-lerps from seed to full over a fixed window,
    /// then activates once. Progress is a pure function of the ZDO-stamped plant time, the
    /// current network clock, and the grow duration — so it unit-tests with no engine
    /// (the edge cases that used to be verifiable only in-world: unstamped, clock-not-up,
    /// mid-grow relog, past-window).
    ///
    /// <para>Extracted verbatim from <c>AncientPortalTag.ComputeProgress</c>. The only change
    /// is <c>Mathf.Clamp01</c> → an inline clamp (identical result, no UnityEngine ref), so the
    /// shell's grow visual + activation gate are byte-for-byte unchanged.</para>
    /// </summary>
    public static class PortalGrow
    {
        /// <summary>Ticks per second (System.TimeSpan.TicksPerSecond), inlined so the Core carries
        /// no BCL-version assumption. The plant stamp + clock are ZNet network-clock Ticks.</summary>
        public const long TicksPerSecond = 10_000_000L;

        /// <summary>
        /// Grow progress in [0,1] from the stamped plant time vs. the current network clock.
        /// Fails toward 0 (stay seed, never activate) on every not-ready read, matching the tag:
        /// <list type="bullet">
        ///   <item><paramref name="plantTicks"/> == 0 → unstamped (non-owner before the owner's
        ///     write propagates): 0, wait — do NOT prematurely activate.</item>
        ///   <item><paramref name="nowTicks"/> == 0 → clock not up yet: 0, stay seed.</item>
        ///   <item>elapsed ≤ 0 (clock behind the stamp): 0.</item>
        ///   <item>otherwise clamp(elapsedSeconds / <paramref name="growSeconds"/>, 0, 1).</item>
        /// </list>
        /// Because the placer always owns a freshly-placed piece and always writes the stamp,
        /// "unstamped forever" can't happen, so failing toward 0 never strands a portal.
        /// </summary>
        public static float Progress(long plantTicks, long nowTicks, float growSeconds)
        {
            if (plantTicks == 0L) return 0f;   // not stamped yet — wait, don't activate
            if (nowTicks == 0L) return 0f;     // clock not up — stay seed-scaled
            if (growSeconds <= 0f) return 1f;  // degenerate window → already grown (guard div-by-zero)

            double elapsedSec = (nowTicks - plantTicks) / (double)TicksPerSecond;
            if (elapsedSec <= 0d) return 0f;

            double t = elapsedSec / growSeconds;
            if (t <= 0d) return 0f;
            if (t >= 1d) return 1f;
            return (float)t;
        }

        /// <summary>True once the grow window has fully elapsed (progress == 1) — the activation
        /// latch condition. Convenience over <see cref="Progress"/> for the shell's tick.</summary>
        public static bool IsGrown(long plantTicks, long nowTicks, float growSeconds)
            => Progress(plantTicks, nowTicks, growSeconds) >= 1f;
    }
}
