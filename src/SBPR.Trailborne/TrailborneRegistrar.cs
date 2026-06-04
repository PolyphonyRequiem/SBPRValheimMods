using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne
{
    /// <summary>
    /// Registration hooks. We piggy-back on ZNetScene.Awake (prefabs available)
    /// and ObjectDB.CopyOtherDB (client receives server item list) to keep
    /// our content alive across scene loads / server-sync events.
    ///
    /// M0 strategy: clone vanilla prefabs at runtime (no asset bundles). All
    /// gated by SBPRContext.OnSBServer.
    /// </summary>
    public static class TrailborneRegistrar
    {
        private const string OrienteeringTableName = "piece_sbpr_orienteering_table";
        private const string PathLampName          = "piece_sbpr_path_lamp";
        private const string SpadeName             = "SBPR_TrailblazersSpade";
        public  const string PublicSpadeName       = SpadeName;

        private const string SourceWorkbench       = "piece_workbench";
        private const string SourceGroundTorch     = "piece_groundtorch_wood";
        private const string SourceHoe             = "Hoe";

        private const string IconFile              = "trailblazers_spade_v0.1.png";

        private static bool _znetSceneDone;
        private static bool _objectDbDone;

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPostfix]
        private static void OnZNetSceneAwake(ZNetScene __instance)
        {
            if (!SBPRContext.OnSBServer)
            {
                TrailbornePlugin.Log.LogInfo("[Trailborne] OnSBServer=false; skipping ZNetScene registration.");
                return;
            }
            try
            {
                TrailbornePlugin.Log.LogInfo("[Trailborne] ZNetScene.Awake postfix — registering content surfaces…");

                RegisterOrienteeringTablePrefab(__instance);
                RegisterPathLampPrefab(__instance);
                RegisterSpadeItemPrefab(__instance);
                TrailborneM1.RegisterPrefabs(__instance);
                TrailborneM2.RegisterPrefabs(__instance);
                TrailborneM3.RegisterPrefabs(__instance);

                _znetSceneDone = true;
                TrailbornePlugin.Log.LogInfo("[Trailborne] ZNetScene registration complete.");

                // If ObjectDB.Awake already fired before us (race on some scene loads),
                // also do the ODB wiring now.
                if (ObjectDB.instance != null && ObjectDB.instance.m_items.Count > 0)
                    DoObjectDBWiring();
            }
            catch (System.Exception e)
            {
                TrailbornePlugin.Log.LogError($"[Trailborne] ZNetScene registration failed: {e}");
            }
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        [HarmonyPostfix]
        private static void OnObjectDBCopy()
        {
            if (!SBPRContext.OnSBServer) return;
            DoObjectDBWiring();
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPostfix]
        private static void OnObjectDBAwake()
        {
            if (!SBPRContext.OnSBServer) return;
            DoObjectDBWiring();
        }

        private static void DoObjectDBWiring()
        {
            try
            {
                if (ObjectDB.instance == null) return;

                // Items
                var spade = GameObject.Find(SpadeName);
                // Try registered-prefab lookup first; Find() on inactive objects doesn't work.
                var zns = ZNetScene.instance;
                if (zns != null)
                {
                    var spadePrefab = zns.GetPrefab(SpadeName);
                    if (spadePrefab != null) TrailborneAssets.RegisterItemInObjectDB(spadePrefab);
                }

                // Recipes — only build them once items are in ODB.
                AddRecipes();

                // Pieces into Hammer build menu
                var hammerTable = TrailborneAssets.GetHammerPieceTable();
                if (hammerTable != null && zns != null)
                {
                    var table = zns.GetPrefab(OrienteeringTableName);
                    var lamp  = zns.GetPrefab(PathLampName);
                    if (table != null) TrailborneAssets.AddPieceToTable(table, hammerTable);
                    if (lamp  != null) TrailborneAssets.AddPieceToTable(lamp,  hammerTable);
                }

                _objectDbDone = true;
                TrailbornePlugin.Log.LogInfo("[Trailborne] ObjectDB wiring complete (items + recipes + hammer pieces).");

                // M1 wiring (inks + sign pieces + recipes)
                TrailborneM1.DoObjectDBWiring(zns);
                // M2 wiring (cairn marker + cairn piece + recipe)
                TrailborneM2.DoObjectDBWiring(zns);
                // M3 wiring (spade path/replant ops)
                TrailborneM3.DoObjectDBWiring(zns);
            }
            catch (System.Exception e)
            {
                TrailbornePlugin.Log.LogError($"[Trailborne] ObjectDB wiring failed: {e}");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Prefab builders
        // ─────────────────────────────────────────────────────────────

        private static void RegisterOrienteeringTablePrefab(ZNetScene zns)
        {
            if (zns.GetPrefab(OrienteeringTableName) != null) return;
            var clone = TrailborneAssets.ClonePrefab(SourceWorkbench, OrienteeringTableName);
            if (clone == null) return;

            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = "$piece_sbpr_orienteering_table";
                piece.m_description = "Plan your routes. Crafts trail tools and lamps.";
                piece.m_category    = Piece.PieceCategory.Crafting;
                piece.m_icon        = TrailborneAssets.LoadPngAsSprite(IconFile);
                piece.m_resources   = new[]
                {
                    BuildReq("Wood", 10),
                    BuildReq("Stone", 5),
                };
            }
            // Already a CraftingStation (workbench is one) — leave m_name unchanged so
            // existing recipes that name "piece_workbench" don't accidentally collide.
            var station = clone.GetComponent<CraftingStation>();
            if (station != null) station.m_name = "$piece_sbpr_orienteering_table";

            TrailborneAssets.RegisterPrefabInZNetScene(clone);
            TrailbornePlugin.Log.LogInfo($"[Trailborne] Registered piece: {OrienteeringTableName}");
        }

        private static void RegisterPathLampPrefab(ZNetScene zns)
        {
            if (zns.GetPrefab(PathLampName) != null) return;
            var clone = TrailborneAssets.ClonePrefab(SourceGroundTorch, PathLampName);
            if (clone == null) return;

            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = "$piece_sbpr_path_lamp";
                piece.m_description = "A standing lamp for marking trails after dark.";
                piece.m_category    = Piece.PieceCategory.Furniture;
                piece.m_icon        = TrailborneAssets.LoadPngAsSprite(IconFile);
                piece.m_resources   = new[]
                {
                    BuildReq("ElderBark", 3),  // "Corewood"
                    BuildReq("Resin", 2),
                };
            }

            TrailborneAssets.RegisterPrefabInZNetScene(clone);
            TrailbornePlugin.Log.LogInfo($"[Trailborne] Registered piece: {PathLampName}");
        }

        private static void RegisterSpadeItemPrefab(ZNetScene zns)
        {
            if (zns.GetPrefab(SpadeName) != null) return;
            var clone = TrailborneAssets.ClonePrefab(SourceHoe, SpadeName);
            if (clone == null) return;

            var drop = clone.GetComponent<ItemDrop>();
            if (drop != null)
            {
                drop.m_itemData.m_shared.m_name        = "$item_sbpr_spade";
                drop.m_itemData.m_shared.m_description = "Trailblazer's Spade — scroll to cycle path mode (dirt / paved / clear).";
                var sprite = TrailborneAssets.LoadPngAsSprite(IconFile);
                if (sprite != null)
                    drop.m_itemData.m_shared.m_icons = new[] { sprite };
                // m_buildPieces inherits from Hoe — already has terrain-op pieces
                // (raise/level/paved_road) accessible via the vanilla scroll-wheel
                // cycler. For M0 we use that as the path-mode cycler instead of
                // wiring a new right-click handler. TWEAK ME: right-click rebind.
            }

            TrailborneAssets.RegisterPrefabInZNetScene(clone);
            TrailbornePlugin.Log.LogInfo($"[Trailborne] Registered item prefab: {SpadeName}");
        }

        private static void AddRecipes()
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            // Spade recipe — at orienteering table
            if (!HasRecipeFor(SpadeName))
            {
                var spade = odb.GetItemPrefab(SpadeName);
                if (spade != null)
                {
                    var r = ScriptableObject.CreateInstance<Recipe>();
                    r.name             = "Recipe_" + SpadeName;
                    r.m_item           = spade.GetComponent<ItemDrop>();
                    r.m_amount         = 1;
                    r.m_minStationLevel = 1;
                    r.m_craftingStation = FindStation(OrienteeringTableName);
                    r.m_resources      = new[]
                    {
                        BuildReq("Wood", 5),
                        BuildReq("Stone", 5),
                    };
                    odb.m_recipes.Add(r);
                    TrailbornePlugin.Log.LogInfo("[Trailborne] Added recipe for spade.");
                }
            }
        }

        private static bool HasRecipeFor(string itemPrefabName)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return false;
            foreach (var r in odb.m_recipes)
                if (r != null && r.m_item != null && r.m_item.gameObject != null &&
                    r.m_item.gameObject.name == itemPrefabName)
                    return true;
            return false;
        }

        private static CraftingStation FindStation(string piecePrefabName)
        {
            var zns = ZNetScene.instance;
            var p   = zns?.GetPrefab(piecePrefabName);
            return p?.GetComponent<CraftingStation>();
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount)
        {
            var odb = ObjectDB.instance;
            var item = odb?.GetItemPrefab(resourcePrefabName)?.GetComponent<ItemDrop>();
            return new Piece.Requirement
            {
                m_resItem = item,
                m_amount  = amount,
                m_recover = true,
            };
        }
    }
}
