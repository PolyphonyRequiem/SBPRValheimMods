using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Features.Trailhead;
using SBPR.Trailborne.Features.Trailblazing;
using SBPR.Trailborne.Features.Pigments;
using SBPR.Trailborne.Features.Signs;
using SBPR.Trailborne.Features.Cairns;

namespace SBPR.Trailborne.Runtime
{
    /// <summary>
    /// Thin registration dispatcher. We piggy-back on ZNetScene.Awake (prefabs
    /// available) and ObjectDB.CopyOtherDB / Awake (client receives server item
    /// list) to keep our content alive across scene loads / server-sync events,
    /// then fan out to each feature's RegisterPrefabs / DoObjectDBWiring.
    ///
    /// M0 strategy: clone vanilla prefabs at runtime (no asset bundles). All
    /// gated by ServerContext.OnSBServer.
    ///
    /// Feature dispatch order is load-bearing: Pigments must register its ink
    /// items into ObjectDB before Signs and Cairns build recipes that consume
    /// them (BuildReq resolves the ink prefab from ODB). The chosen order also
    /// preserves the original recipe-registration order (spade, inks, markers)
    /// and hammer build-menu order (bench, lamp, signs, cairns).
    /// </summary>
    public static class Registrar
    {
        private static bool znetSceneDone;

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPostfix]
        private static void OnZNetSceneAwake(ZNetScene __instance)
        {
            if (!ServerContext.OnSBServer)
            {
                Plugin.Log.LogInfo("[Trailborne] OnSBServer=false; skipping ZNetScene registration.");
                return;
            }
            try
            {
                Plugin.Log.LogInfo("[Trailborne] ZNetScene.Awake postfix — registering content surfaces…");

                Trailhead.RegisterPrefabs(__instance);
                Trailblazing.RegisterPrefabs(__instance);
                Pigments.RegisterPrefabs(__instance);
                Signs.RegisterPrefabs(__instance);
                Cairns.RegisterPrefabs(__instance);

                znetSceneDone = true;
                Plugin.Log.LogInfo("[Trailborne] ZNetScene registration complete.");

                // If ObjectDB.Awake already fired before us (race on some scene loads),
                // also do the ODB wiring now.
                if (ObjectDB.instance != null && ObjectDB.instance.m_items.Count > 0)
                    DoObjectDBWiring();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[Trailborne] ZNetScene registration failed: {e}");
            }
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        [HarmonyPostfix]
        private static void OnObjectDBCopy()
        {
            if (!ServerContext.OnSBServer) return;
            DoObjectDBWiring();
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPostfix]
        private static void OnObjectDBAwake()
        {
            if (!ServerContext.OnSBServer) return;
            DoObjectDBWiring();
        }

        private static void DoObjectDBWiring()
        {
            try
            {
                // GUARD: only wire when BOTH databases exist AND our prefabs have been
                // registered into ZNetScene. At the MAIN MENU, ObjectDB exists (menu
                // item icons) but ZNetScene does not and znetSceneDone is false — so the
                // ObjectDB.Awake / CopyOtherDB hooks would otherwise call this with a
                // null ZNetScene, no-op through every feature, yet still log "wiring
                // complete", "Spade prefab missing", and "SpecCheck Skipped". Skip
                // silently until the world scene is up. This preserves the 3-hook
                // self-heal: whichever hook fires LAST once all conditions hold does the
                // single real in-world wiring pass.
                if (ObjectDB.instance == null || ZNetScene.instance == null || !znetSceneDone)
                    return;

                Trailhead.DoObjectDBWiring(ZNetScene.instance);
                Trailblazing.DoObjectDBWiring(ZNetScene.instance);
                Pigments.DoObjectDBWiring(ZNetScene.instance);
                Signs.DoObjectDBWiring(ZNetScene.instance);
                Cairns.DoObjectDBWiring(ZNetScene.instance);

                Plugin.Log.LogInfo("[Trailborne] ObjectDB wiring complete (items + recipes + hammer pieces).");

                // Spec-drift watchdog — runs LAST after all wiring complete.
                SpecCheck.Run();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[Trailborne] ObjectDB wiring failed: {e}");
            }
        }
    }
}
