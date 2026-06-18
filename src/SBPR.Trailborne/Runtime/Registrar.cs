using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Features.Trailhead;
using SBPR.Trailborne.Features.Trailblazing;
using SBPR.Trailborne.Features.Pigments;
using SBPR.Trailborne.Features.Signs;
using SBPR.Trailborne.Features.Cairns;
using SBPR.Trailborne.Features.Cartography;
using SBPR.Trailborne.Features.MarkerSigns;
using SBPR.Trailborne.Features.Portals;
using SBPR.Trailborne.Features.Sunstone;
using SBPR.Trailborne.Features.Exploration;

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
    /// Feature dispatch order is load-bearing: Pigments must register its pigment
    /// items into ObjectDB before Signs and Cairns build recipes that consume
    /// them (BuildReq resolves the pigment prefab from ODB). The chosen order also
    /// preserves the original recipe-registration order (spade, pigments, markers)
    /// and hammer build-menu order (bench, lamp, signs, cairns).
    /// </summary>
    public static class Registrar
    {
        private static bool znetSceneDone;

        // Set true once the single real in-world ObjectDB wiring pass has
        // completed (items + recipes + hammer pieces registered and SpecCheck
        // run). The client-facing refresh layer (ClientRefreshPatches, Fix A)
        // gates its Player.OnSpawned recipe refresh on this so it only re-scans
        // once our content is actually present in ObjectDB. Replaces the old
        // objectDbDone flag deleted in the boot-log-noise hygiene pass.
        private static bool contentWired;
        public static bool ContentWired => contentWired;

        // Registration mutation runs at Priority.Last so our additions land at
        // the END of each method's postfix chain — after vanilla's body and
        // after any peer mod's same-method postfix. This makes our content
        // present in a fully-settled DB regardless of modpack load order, and
        // lets SpecCheck validate the final state. (Aggravating factor #3 in
        // the gap analysis: bare postfixes left ordering undefined.)
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
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
                SurveyorsTable.RegisterPrefabs(__instance);
                LocalMap.RegisterPrefabs(__instance);
                CartographersKit.RegisterPrefabs(__instance);
                MarkerSigns.RegisterPrefabs(__instance);
                // Portals (Seed item + Ancient Portal piece). After Trailhead so the
                // Explorer's Bench exists for the recipe station; also registers the portal
                // prefab hash into Game.PortalPrefabHash (the #1 tag-pairing risk).
                Portals.RegisterPrefabs(__instance);

                // v3 Swamp — Sunstone material + Sunstone Lens trinket (card t_2fd7bc7f).
                // After Trailhead so the Explorer's Bench exists for the recipe station.
                SunstoneLens.RegisterPrefabs(__instance);

                // v3 Swamp — Sunstone loot economy (card t_0445f590): inject Sunstone into the
                // vanilla swamp-surface chest DropTable + the Draugr Elite CharacterDrop. MUST
                // run AFTER SunstoneLens.RegisterPrefabs — it references the SBPR_Sunstone
                // ZNetScene prefab that call registers. Loot wiring is a ZNetScene-phase prefab
                // edit (the DropData/Drop reference the Sunstone GameObject), so it needs no
                // ObjectDB-phase pass. Idempotent across postfix re-fires.
                SunstoneLoot.RegisterPrefabs(__instance);

                // v3 Swamp — Iron Compass trinket (card t_ee61472f). After Trailhead so the
                // Explorer's Bench exists for the recipe station; grouped after the other
                // exploration trinket (the Sunstone Lens). The compass depends on no other
                // SBPR prefab — placement here just keeps the exploration tools together.
                IronCompass.RegisterPrefabs(__instance);

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
        [HarmonyPriority(Priority.Last)]
        private static void OnObjectDBCopy()
        {
            if (!ServerContext.OnSBServer) return;
            DoObjectDBWiring();
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
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
                MarkerSigns.DoObjectDBWiring(ZNetScene.instance);
                Cairns.DoObjectDBWiring(ZNetScene.instance);
                SurveyorsTable.DoObjectDBWiring(ZNetScene.instance);
                LocalMap.DoObjectDBWiring(ZNetScene.instance);
                // Cartographer's Kit recipe consumes the four pigments — Pigments.DoObjectDBWiring
                // (above) has already registered the pigment items into ObjectDB, so BuildReq
                // resolves them here. MUST stay after Pigments.
                CartographersKit.DoObjectDBWiring(ZNetScene.instance);

                // Portals: Seed recipe (Explorer's Bench) + Ancient Portal cost rebuild +
                // Hammer-menu add. After Trailhead (the bench station + Hammer table must
                // exist); the Seed item is registered into ObjectDB inside this call before
                // the portal's one-seed build cost is rebuilt.
                Portals.DoObjectDBWiring(ZNetScene.instance);

                // v3 Swamp — Sunstone material + Lens recipes (card t_2fd7bc7f). The Lens recipe
                // consumes Sunstone; both are registered inside this one call, Sunstone first, so
                // BuildReq resolves the Sunstone ingredient. After Trailhead (the bench station).
                SunstoneLens.DoObjectDBWiring(ZNetScene.instance);

                // v3 Swamp — Iron Compass recipe (card t_ee61472f). Consumes Red Pigment
                // (SBPR_InkRed), so MUST stay AFTER Pigments.DoObjectDBWiring (above) — the
                // pigment item is in ODB by now, so BuildReq resolves it. Also after Trailhead
                // (the Explorer's Bench station must exist for FindStation).
                IronCompass.DoObjectDBWiring(ZNetScene.instance);

                Plugin.Log.LogInfo("[Trailborne] ObjectDB wiring complete (items + recipes + hammer pieces).");

                // Spec-drift watchdog — runs LAST after all wiring complete.
                SpecCheck.Run();

                // Mark content live so the client-facing refresh layer
                // (ClientRefreshPatches Fix A) knows it's worth re-scanning the
                // player's known-recipe list on the next Player.OnSpawned.
                contentWired = true;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[Trailborne] ObjectDB wiring failed: {e}");
            }
        }
    }
}
