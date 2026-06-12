using HarmonyLib;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// Registers the <c>sbpr_mapmode</c> console command by postfixing <c>Terminal.InitTerminal</c>
    /// (the vanilla one-time console bootstrap — same hook as <c>BannerDiagCommand</c>). Lets Daniel
    /// switch the Local Map's cartography render path live, without a relog, between the two §2E.3
    /// modes and pick whichever looks right on his GPU:
    ///
    /// <list type="bullet">
    ///   <item><c>sbpr_mapmode shader</c> — vanilla styled "parchment" material (the target look).</item>
    ///   <item><c>sbpr_mapmode cpu</c> — the CPU biome/water/relief composite (always renders, no styling).</item>
    ///   <item><c>sbpr_mapmode toggle</c> — flip between the two.</item>
    ///   <item><c>sbpr_mapmode</c> (no arg) — print the current mode.</item>
    /// </list>
    ///
    /// On a change, if the viewer is open it forces an immediate re-render so the switch is visible
    /// without reopening the map. Client-side, read-only w.r.t. game state (no cheat); registers
    /// harmlessly anywhere a Terminal inits. Woven via <c>harmony.PatchAll(typeof(MapModeCommand))</c>
    /// in <c>Plugin.Awake</c> so PatchCheck sees it.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class MapModeCommand
    {
        private static bool _registered;

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (_registered) return;   // InitTerminal early-returns if already initialized, but guard anyway
            _registered = true;

            new Terminal.ConsoleCommand(
                "sbpr_mapmode",
                "[SBPR] Local Map render mode: 'shader' (vanilla parchment), 'cpu' (biome composite), 'toggle', or no arg to print current.",
                delegate (Terminal.ConsoleEventArgs args)
                {
                    string? arg = args.Length > 1 ? args[1].ToLowerInvariant() : null;
                    MapRenderMode before = MapRenderModeState.Current;

                    switch (arg)
                    {
                        case null:
                        case "":
                        case "status":
                            args.Context.AddString($"[SBPR] map render mode = {before.ToString().ToLowerInvariant()}");
                            return;

                        case "shader":
                        case "parchment":
                        case "vanilla":
                            MapRenderModeState.Current = MapRenderMode.Shader;
                            break;

                        case "cpu":
                        case "composite":
                        case "data":
                            MapRenderModeState.Current = MapRenderMode.Cpu;
                            break;

                        case "toggle":
                        case "t":
                            MapRenderModeState.Current = before == MapRenderMode.Shader
                                ? MapRenderMode.Cpu
                                : MapRenderMode.Shader;
                            break;

                        default:
                            args.Context.AddString($"[SBPR] sbpr_mapmode: unknown '{arg}' — use shader | cpu | toggle.");
                            return;
                    }

                    MapRenderMode after = MapRenderModeState.Current;
                    // Force a live redraw if the viewer is open so the switch is visible immediately.
                    bool refreshed = MapViewer.Instance != null && MapViewer.Instance.RefreshIfOpen();
                    args.Context.AddString(
                        $"[SBPR] map render mode: {before.ToString().ToLowerInvariant()} → {after.ToString().ToLowerInvariant()}" +
                        (refreshed ? " (redrawn)" : " (opens on next map view)"));
                },
                isCheat: false, isNetwork: false, onlyServer: false, isSecret: false);

            Plugin.Log.LogInfo("[Trailborne] Registered `sbpr_mapmode` console command.");
        }
    }
}
