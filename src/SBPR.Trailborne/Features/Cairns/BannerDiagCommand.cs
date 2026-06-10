using HarmonyLib;

namespace SBPR.Trailborne.Features.Cairns
{
    /// <summary>
    /// Registers the <c>bannerdiag</c> dev console command by postfixing
    /// <c>Terminal.InitTerminal</c> (the vanilla one-time console-command bootstrap, decomp
    /// :35705). Typing <c>bannerdiag</c> fires an ON-DEMAND physics+config snapshot of every
    /// loaded cairn banner (<see cref="BannerDiagnostic.SnapshotAll"/>), logging one greppable
    /// <c>[BannerSnap]</c> block each.
    ///
    /// WHY this exists (Daniel 2026-06-10): the time-based <c>BannerDiagnostic</c> auto-probe
    /// fires at <c>Start→+4s</c> — i.e. right at WORLD LOAD, when Valheim's wind is still ramping
    /// from 0 toward its target (the premature-sampling trap Daniel identified: every prior
    /// <c>extAccel≈0.06</c> reading was dead-calm startup, not steady state). A snapshot you can
    /// fire WHENEVER — after the wind settles, or after forcing a storm with <c>wind 1 1</c> —
    /// is the only way to read the TRUE force reaching the cloth. Pair it with the vanilla
    /// <c>wind &lt;angle&gt; &lt;intensity&gt;</c> command:
    ///   <c>wind 0 1</c>   (force full wind from the north), wait ~3 s for the transition,
    ///   <c>bannerdiag</c> (snapshot), then read the <c>[BannerSnap]</c> lines in the client log.
    ///
    /// Registered through <c>harmony.PatchAll(typeof(BannerDiagCommand))</c> in Plugin.Awake so the
    /// PatchCheck self-test sees it woven. Client-only in effect (banners only exist on a client),
    /// but the command registers harmlessly anywhere a Terminal inits.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class BannerDiagCommand
    {
        private static bool _registered;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_registered) return;   // InitTerminal early-returns if already initialized, but guard anyway
            _registered = true;

            // isCheat:false so it works without devcommands — it's a read-only diagnostic, not a cheat.
            // isSecret:true keeps it out of the main help list (a dev tool, not player-facing).
            new Terminal.ConsoleCommand(
                "bannerdiag",
                "[SBPR] snapshot the physics+config state of every loaded cairn banner to the log ([BannerSnap])",
                delegate (Terminal.ConsoleEventArgs args)
                {
                    int n = BannerDiagnostic.SnapshotAll();
                    args.Context.AddString(
                        n > 0
                            ? $"[SBPR] bannerdiag: snapshotted {n} banner(s) → grep the log for [BannerSnap]."
                            : "[SBPR] bannerdiag: no cairn banners loaded right now (stand near one and retry).");
                },
                isCheat: false, isNetwork: false, onlyServer: false, isSecret: true);

            Plugin.Log.LogInfo("[Trailborne] Registered `bannerdiag` dev console command.");
        }
    }
}
