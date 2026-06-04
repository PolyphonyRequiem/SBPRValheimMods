namespace SBPR.Trailborne
{
    /// <summary>
    /// Server gate. M0 stub: always true. M1 wires real handshake.
    /// All registration paths must pass through this check so we can
    /// flip behaviour later without touching the registration code.
    /// </summary>
    public static class SBPRContext
    {
        public static bool OnSBServer => true;
    }
}
