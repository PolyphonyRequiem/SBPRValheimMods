using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne
{
    [BepInPlugin(ModId, ModName, ModVersion)]
    public class TrailbornePlugin : BaseUnityPlugin
    {
        public const string ModId      = "net.danielgreen.sbpr.trailborne";
        public const string ModName    = "SBPR Trailborne";
        public const string ModVersion = "0.1.0";

        internal static ManualLogSource Log;
        internal static string PluginFolder;
        private  Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            PluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Log.LogInfo($"[Trailborne] Awake — {ModName} {ModVersion} booting (folder={PluginFolder}, OnSBServer={SBPRContext.OnSBServer})");

            _harmony = new Harmony(ModId);
            _harmony.PatchAll(typeof(TrailborneRegistrar));

            Log.LogInfo("[Trailborne] Harmony patches applied.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
