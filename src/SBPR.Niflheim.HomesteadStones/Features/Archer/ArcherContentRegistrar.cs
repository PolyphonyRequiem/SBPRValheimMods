using System;
using HarmonyLib;

namespace SBPR.Niflheim.HomesteadStones.Features.Archer
{
    /// <summary>
    /// T025-RT — the net48 registration dispatcher for Archer / Practice Range content. Piggybacks on
    /// <c>ZNetScene.Awake</c> (prefabs available) and <c>ObjectDB.Awake</c> / <c>ObjectDB.CopyOtherDB</c>
    /// (item list settled, incl. after a client receives the server's DB) so the Practice Arrow item,
    /// its recipe, the deterministic target-return wiring, and the Archery Target build-piece entry stay
    /// alive across scene loads / server-sync events.
    ///
    /// Mutations run at <see cref="HarmonyLib.Priority.Last"/> so our additions land at the end of each
    /// method's postfix chain — a fully-settled DB regardless of modpack load order (the same discipline
    /// the sibling HomesteadStoneRegistrar and SBPR.Trailborne Registrar use).
    ///
    /// Idempotent: every <c>ArcherContent</c> call guards against a re-add, so repeated hook fires (menu →
    /// world → rejoin) converge on exactly one item / recipe / piece entry.
    /// </summary>
    [HarmonyPatch]
    internal static class ArcherContentRegistrar
    {
        private static bool znetSceneDone;

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void OnZNetSceneAwake(ZNetScene __instance)
        {
            try
            {
                ArcherContent.RegisterPrefabs(__instance);
                znetSceneDone = true;

                // If ObjectDB already exists (race on some scene loads), do the ODB wiring now too.
                if (ObjectDB.instance != null && ObjectDB.instance.m_items.Count > 0)
                    DoObjectDBWiring();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("[Niflheim/Archer] ZNetScene registration failed: " + e);
            }
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void OnObjectDBCopy() => DoObjectDBWiring();

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void OnObjectDBAwake() => DoObjectDBWiring();

        private static void DoObjectDBWiring()
        {
            try
            {
                // Only wire when BOTH DBs exist AND our prefab was registered into ZNetScene. At the main
                // menu ObjectDB exists (menu icons) but ZNetScene does not — skip until the world scene is
                // up so whichever hook fires last does the single real wiring pass.
                if (ObjectDB.instance == null || ZNetScene.instance == null || !znetSceneDone)
                    return;

                ArcherContent.DoObjectDBWiring(ZNetScene.instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("[Niflheim/Archer] ObjectDB wiring failed: " + e);
            }
        }
    }
}
