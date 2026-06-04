using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne
{
    /// <summary>
    /// M2 content: Cairn Marker (consumable single-use item that gates Cairn
    /// construction) + Cairn piece (5 stones, bonfire-derived, with custom
    /// comfort level injected via SE_Rested.CalculateComfortLevel postfix —
    /// vanilla ComfortGroup enum does not have Cairn, so we patch the result
    /// directly with Mathf.Max).
    ///
    /// PARKED-2026-06-03: Cairn comfort floor = 3, ceiling = 7 (5 tiers).
    /// v0.1.0 implements ONLY tier-1 cairn (floor = 3 comfort). Tier 2-5
    /// upgrades + repair material costs + downgrade-at-25% + collapse-at-0%
    /// state machine = M2.5/v0.2.0+.
    ///
    /// All gated behind SBPRContext.OnSBServer.
    /// </summary>
    public static class TrailborneM2
    {
        public const string CairnMarkerItemName = "SBPR_CairnMarker";
        public const string CairnPieceName      = "piece_sbpr_cairn";

        private const string SourceConsumable = "Coins";
        private const string SourceBonfire    = "bonfire";

        // v0.1.0 tier-1 cairn comfort floor
        private const int CairnTier1ComfortFloor = 3;
        private const float CairnComfortRadius   = 10f;

        public static void RegisterPrefabs(ZNetScene zns)
        {
            RegisterCairnMarkerPrefab(zns);
            RegisterCairnPiecePrefab(zns);
        }

        private static void RegisterCairnMarkerPrefab(ZNetScene zns)
        {
            if (zns.GetPrefab(CairnMarkerItemName) != null) return;
            var clone = TrailborneAssets.ClonePrefab(SourceConsumable, CairnMarkerItemName);
            if (clone == null) return;
            var drop = clone.GetComponent<ItemDrop>();
            if (drop != null)
            {
                drop.m_itemData.m_shared.m_name        = "Cairn Marker";
                drop.m_itemData.m_shared.m_description = "A wooden marker plank with a blank hide pennant. Place on stones to declare a Cairn.";
                drop.m_itemData.m_shared.m_maxStackSize = 10;
                drop.m_itemData.m_shared.m_weight      = 0.5f;
                drop.m_itemData.m_shared.m_itemType    = ItemDrop.ItemData.ItemType.Material;
                var sprite = TrailborneAssets.LoadPngAsSprite("cairn_marker_v0.1.png");
                if (sprite != null) drop.m_itemData.m_shared.m_icons = new[] { sprite };
            }
            TrailborneAssets.RegisterPrefabInZNetScene(clone);
            TrailbornePlugin.Log.LogInfo($"[Trailborne/M2] Registered cairn marker item: {CairnMarkerItemName}");
        }

        private static void RegisterCairnPiecePrefab(ZNetScene zns)
        {
            if (zns.GetPrefab(CairnPieceName) != null) return;
            // Bonfire is a chunky stone-y piece; closest vanilla mesh to "stack of rocks".
            // Real cairn art is M2.5+.
            var clone = TrailborneAssets.ClonePrefab(SourceBonfire, CairnPieceName);
            if (clone == null)
            {
                TrailbornePlugin.Log.LogWarning($"[Trailborne/M2] Source bonfire prefab missing, skipping cairn.");
                return;
            }
            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = "Cairn";
                piece.m_description = "A stacked-stone cairn. Provides shelter comfort when complete.";
                piece.m_category    = Piece.PieceCategory.Crafting;
                piece.m_resources   = new[]
                {
                    BuildReq("Stone", 9), // tier-1 per "9 stones" canon
                    BuildReq(CairnMarkerItemName, 1),
                };
                var sprite = TrailborneAssets.LoadPngAsSprite("cairn_marker_v0.1.png");
                if (sprite != null) piece.m_icon = sprite;
                // Tag this as a comfort piece for the SE_Rested patch
                piece.m_comfort = 0; // vanilla comfort stays 0; our patch enforces the floor
                piece.m_comfortGroup = Piece.ComfortGroup.None;
            }
            // Add our tag so the SE_Rested patch can identify nearby cairns
            clone.AddComponent<TrailborneCairnTag>();

            TrailborneAssets.RegisterPrefabInZNetScene(clone);
            TrailbornePlugin.Log.LogInfo($"[Trailborne/M2] Registered cairn piece: {CairnPieceName}");
        }

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            // Cairn Marker into ODB
            var marker = zns?.GetPrefab(CairnMarkerItemName);
            if (marker != null) TrailborneAssets.RegisterItemInObjectDB(marker);

            // Cairn Marker recipe — at Orienteering Table (uses 2 wood + 1 white ink)
            if (!HasRecipe(CairnMarkerItemName))
            {
                var markerItem = odb.GetItemPrefab(CairnMarkerItemName);
                if (markerItem != null)
                {
                    var recipe = ScriptableObject.CreateInstance<Recipe>();
                    recipe.name              = "Recipe_" + CairnMarkerItemName;
                    recipe.m_item            = markerItem.GetComponent<ItemDrop>();
                    recipe.m_amount          = 1;
                    recipe.m_minStationLevel = 1;
                    recipe.m_craftingStation = FindStation("piece_sbpr_explorers_bench");
                    recipe.m_resources       = new[]
                    {
                        BuildReq("Wood", 2),
                        BuildReq(TrailborneM1.InkWhiteName, 1),
                    };
                    odb.m_recipes.Add(recipe);
                }
            }

            // Cairn piece into Hammer build menu
            var hammerTable = TrailborneAssets.GetHammerPieceTable();
            if (hammerTable != null)
            {
                var cairn = zns?.GetPrefab(CairnPieceName);
                if (cairn != null) TrailborneAssets.AddPieceToTable(cairn, hammerTable);
            }

            TrailbornePlugin.Log.LogInfo("[Trailborne/M2] M2 ObjectDB wiring complete (cairn marker + cairn piece + recipe).");
        }

        // ───────────────────────────────────────────────
        // SE_Rested comfort patch — inject cairn comfort floor
        // ───────────────────────────────────────────────

        public static int GetCairnComfortBonus(Vector3 position)
        {
            // Find nearby cairn pieces with our tag
            int floor = 0;
            var hits = Physics.OverlapSphere(position, CairnComfortRadius);
            foreach (var h in hits)
            {
                if (h == null) continue;
                var tag = h.GetComponentInParent<TrailborneCairnTag>();
                if (tag != null)
                {
                    // v0.1.0: any registered cairn contributes the tier-1 floor.
                    // Tier upgrades live on tag.Tier in M2.5+.
                    if (CairnTier1ComfortFloor > floor) floor = CairnTier1ComfortFloor;
                }
            }
            return floor;
        }

        private static bool HasRecipe(string itemPrefabName)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return false;
            foreach (var r in odb.m_recipes)
                if (r != null && r.m_item != null && r.m_item.gameObject != null && r.m_item.gameObject.name == itemPrefabName)
                    return true;
            return false;
        }

        private static CraftingStation FindStation(string piecePrefabName)
        {
            var zns = ZNetScene.instance;
            var p = zns?.GetPrefab(piecePrefabName);
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

    public class TrailborneCairnTag : MonoBehaviour
    {
        // v0.1.0: presence = tier-1 cairn. Tier upgrades land in M2.5+.
        public int Tier = 1;
    }

    /// <summary>
    /// SE_Rested.CalculateComfortLevel(bool inShelter, Vector3 pos) postfix —
    /// raise the returned comfort to the cairn floor if a registered cairn is
    /// within radius AND the player is in shelter (matches vanilla rule that
    /// comfort only counts when sheltered). Uses Mathf.Max so we don't stomp
    /// vanilla — we ESTABLISH a floor, not a ceiling.
    /// </summary>
    [HarmonyPatch(typeof(SE_Rested), nameof(SE_Rested.CalculateComfortLevel), new System.Type[] { typeof(bool), typeof(Vector3) })]
    public static class SE_Rested_CalculateComfortLevel_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(bool inShelter, Vector3 position, ref int __result)
        {
            if (!inShelter) return;
            int cairnBonus = TrailborneM2.GetCairnComfortBonus(position);
            if (cairnBonus > __result) __result = cairnBonus;
        }
    }
}
