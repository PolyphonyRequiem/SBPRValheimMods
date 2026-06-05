using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Runtime;
using SBPR.Trailborne.Features.Cairns;

namespace SBPR.Trailborne
{
    [BepInPlugin(ModId, ModName, ModVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModId      = "net.danielgreen.sbpr.trailborne";
        public const string ModName    = "SBPR Trailborne";
        public const string ModVersion = "0.1.0";

        internal static ManualLogSource Log;
        internal static string PluginFolder;
        private  Harmony harmony;

        // ── Config ──────────────────────────────────────────────────
        // Shift+E debug-damage flag. When true, Shift+E on a pristine cairn
        // (≥75% HP) drops it to 70% so the playtester can drive the
        // repair/upgrade combo gesture without waiting for weather decay.
        // Default true for v0.1.0 — flip false (or delete in v0.2.0) once
        // real decay has been tuned.
        internal static ConfigEntry<bool> DebugCairnDamage;

        private void Awake()
        {
            Log = Logger;
            PluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Log.LogInfo($"[Trailborne] Awake — {ModName} {ModVersion} booting (folder={PluginFolder}, OnSBServer={ServerContext.OnSBServer})");

            DebugCairnDamage = Config.Bind(
                "Debug",
                "SBPR_DebugCairnDamage",
                true,
                "When true, Shift+E on a pristine cairn (≥75% HP) drops it to 70% HP so the repair/upgrade combo " +
                "gesture is exercisable without waiting for natural decay. v0.1.0 playtest aid. Flip false (or " +
                "remove this section) once decay tuning lands.");

            harmony = new Harmony(ModId);
            harmony.PatchAll(typeof(Registrar));
            harmony.PatchAll(typeof(CairnPatches));
            // Client-facing refresh layer: Player.OnSpawned recipe reload +
            // PieceTable.UpdateAvailable array repair. Makes registered content
            // actually craftable/buildable on a joined client (task
            // fix-client-registration). Server-safe: no local Player, no build
            // menu, so these hooks are inert on the dedicated server.
            harmony.PatchAll(typeof(ClientRefreshPatches));

            Log.LogInfo($"[Trailborne] Harmony patches applied (DebugCairnDamage={DebugCairnDamage.Value}).");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }
}
