using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace SBPR.Niflheim.HomesteadStones
{
    /// <summary>
    /// Niflheim — Homestead Stones. The "Claim a Homestead" vertical slice
    /// (niflheim/docs/slices/claim-homestead.md). Niflheim-ONLY content — lives in the
    /// SBPR monorepo as a sibling to SBPR.Trailborne, but is NOT part of the standalone
    /// Trailborne Thunderstore mod (it only exists because the world is persistent+social;
    /// fails the "ships to a stranger's vanilla server" ownership test).
    ///
    /// STAGE (2026-07-11): first live-integration cut. Registers the additive gameplay root,
    /// accepted V12 AssetBundle visual, current Location-zone-coordinate D3 keys, and the
    /// provisional deterministic Meadows selector/seating path. Claim/account/UI and final
    /// migration/compatibility policy remain later playtest-gated slices.
    /// </summary>
    [BepInPlugin(ModId, ModName, ModVersion)]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string ModId      = "net.danielgreen.sbpr.niflheim.homesteadstones";
        public const string ModName    = "SBPR Niflheim — Homestead Stones";
        // Single source of truth = <Version> in the csproj; GenerateVersionConstant emits
        // GeneratedModVersion before compile (same pattern as SBPR.Trailborne).
        public const string ModVersion = GeneratedModVersion;

        internal static ManualLogSource Log = null!;   // set in Awake
        internal static string PluginFolder = null!;    // set in Awake
        private  Harmony harmony = null!;               // set in Awake

        private void Awake()
        {
            Log = Logger;
            PluginFolder = Path.GetDirectoryName(Info.Location) ?? BepInEx.Paths.PluginPath;
            Log.LogInfo($"[Niflheim.HomesteadStones] Awake — {ModName} {ModVersion} booting (folder={PluginFolder})");

            harmony = new Harmony(ModId);
            harmony.PatchAll(typeof(Features.HomesteadStone.HomesteadStoneRegistrar));
            harmony.PatchAll(typeof(Features.HomesteadStone.HomesteadStoneWorldPlacement));

            Log.LogInfo("[Niflheim.HomesteadStones] Harmony patches installed.");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }
}
