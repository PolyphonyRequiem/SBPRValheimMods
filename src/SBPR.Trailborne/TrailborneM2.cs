using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne
{
    /// <summary>
    /// M2 content: 4 Cairn Marker variants (one per ink color) + 4 Cairn
    /// piece variants (one per color, requiring matching marker). Color
    /// is bound at MARKER craft time per spec A3.1 / line 320:
    ///   "pigment color binds cairn color at craft-time".
    ///
    /// Spec recipes (LOCKED, see specs/2026-06-03-trailborne-v1/planning/requirements.md):
    ///   Cairn Marker = 2 Leather Scraps + 1 Finewood + 1 Pigment (player color)
    ///   Cairn (init) = 3 Stone + 1 Resin + 1 Cairn Marker
    ///   Cairn upgrade per tier = 3 Stone + 1 Resin  [NOT WIRED — needs tier state machine, M2.5+]
    ///
    /// PARKED-2026-06-03: 5 tiers, comfort floor 3/4/5/6/7. v0.1.0 implements
    /// ONLY tier-1 (floor = 3). Tier 2-5 upgrades, decay state machine
    /// (>=75% pristine / <75% fizzled / <25% downgrade / 0% collapse),
    /// resin glow + auto-reignite — all M2.5/v0.2.0+.
    ///
    /// All gated behind SBPRContext.OnSBServer.
    /// </summary>
    public static class TrailborneM2
    {
        // Color identifiers — must match TrailborneM1 ink names
        public static readonly string[] Colors = { "red", "white", "blue", "black" };

        public static string MarkerName(string color) => "SBPR_CairnMarker_" + color;
        public static string CairnName (string color) => "piece_sbpr_cairn_" + color;
        public static string InkNameFor(string color)
        {
            switch (color)
            {
                case "red":   return TrailborneM1.InkRedName;
                case "white": return TrailborneM1.InkWhiteName;
                case "blue":  return TrailborneM1.InkBlueName;
                case "black": return TrailborneM1.InkBlackName;
                default: return TrailborneM1.InkWhiteName;
            }
        }

        // Back-compat: code outside M2 still references this name for "any marker".
        // Kept so older spec docs / external skills don't break; not used internally
        // anymore.
        public const string CairnMarkerItemName = "SBPR_CairnMarker_white";

        private const string SourceConsumable = "Coins";
        private const string SourceBonfire    = "bonfire";

        // v0.1.0 tier-1 cairn comfort floor
        private const int CairnTier1ComfortFloor = 3;
        private const float CairnComfortRadius   = 10f;

        public static void RegisterPrefabs(ZNetScene zns)
        {
            foreach (var c in Colors)
            {
                RegisterCairnMarkerPrefab(zns, c);
                RegisterCairnPiecePrefab(zns, c);
            }
        }

        private static void RegisterCairnMarkerPrefab(ZNetScene zns, string color)
        {
            var name = MarkerName(color);
            if (zns.GetPrefab(name) != null) return;
            var clone = TrailborneAssets.ClonePrefab(SourceConsumable, name);
            if (clone == null) return;
            var drop = clone.GetComponent<ItemDrop>();
            if (drop != null)
            {
                drop.m_itemData.m_shared.m_name        = "Cairn Marker (" + Capitalize(color) + ")";
                drop.m_itemData.m_shared.m_description =
                    "A wooden marker plank with a " + color + " hide pennant. Place on stones to declare a Cairn.";
                drop.m_itemData.m_shared.m_maxStackSize = 10;
                drop.m_itemData.m_shared.m_weight      = 0.5f;
                drop.m_itemData.m_shared.m_itemType    = ItemDrop.ItemData.ItemType.Material;
                var sprite = TrailborneAssets.LoadPngAsSprite("cairn_marker_v0.1.png");
                if (sprite != null) drop.m_itemData.m_shared.m_icons = new[] { sprite };
            }
            TrailborneAssets.RegisterPrefabInZNetScene(clone);
            TrailbornePlugin.Log.LogInfo($"[Trailborne/M2] Registered cairn marker item: {name}");
        }

        private static void RegisterCairnPiecePrefab(ZNetScene zns, string color)
        {
            var name = CairnName(color);
            if (zns.GetPrefab(name) != null) return;
            // Bonfire is a chunky stone-y piece; closest vanilla mesh to "stack of rocks".
            // Real cairn art is M2.5+.
            var clone = TrailborneAssets.ClonePrefab(SourceBonfire, name);
            if (clone == null)
            {
                TrailbornePlugin.Log.LogWarning($"[Trailborne/M2] Source bonfire prefab missing, skipping cairn ({color}).");
                return;
            }
            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = "Cairn (" + Capitalize(color) + ")";
                piece.m_description = "A " + color + "-marked stone cairn. Provides shelter comfort when complete.";
                piece.m_category    = Piece.PieceCategory.Crafting;
                piece.m_resources   = new[]
                {
                    BuildReq("Stone", 3),
                    BuildReq("Resin", 1),
                    BuildReq(MarkerName(color), 1),
                };
                var sprite = TrailborneAssets.LoadPngAsSprite("cairn_marker_v0.1.png");
                if (sprite != null) piece.m_icon = sprite;
                piece.m_comfort = 0;
                piece.m_comfortGroup = Piece.ComfortGroup.None;
            }
            var tag = clone.AddComponent<TrailborneCairnTag>();
            tag.Color = color;

            TrailborneAssets.RegisterPrefabInZNetScene(clone);
            TrailbornePlugin.Log.LogInfo($"[Trailborne/M2] Registered cairn piece: {name}");
        }

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            foreach (var color in Colors)
            {
                var markerName = MarkerName(color);
                var marker = zns?.GetPrefab(markerName);
                if (marker != null) TrailborneAssets.RegisterItemInObjectDB(marker);

                if (!HasRecipe(markerName))
                {
                    var markerItem = odb.GetItemPrefab(markerName);
                    if (markerItem != null)
                    {
                        var recipe = ScriptableObject.CreateInstance<Recipe>();
                        recipe.name              = "Recipe_" + markerName;
                        recipe.m_item            = markerItem.GetComponent<ItemDrop>();
                        recipe.m_amount          = 1;
                        recipe.m_minStationLevel = 1;
                        recipe.m_craftingStation = FindStation("piece_sbpr_explorers_bench");
                        recipe.m_resources       = new[]
                        {
                            BuildReq("LeatherScraps", 2),
                            BuildReq("FineWood", 1),
                            BuildReq(InkNameFor(color), 1),
                        };
                        odb.m_recipes.Add(recipe);
                    }
                }
            }

            // Cairn pieces into Hammer build menu + REBUILD their resource list
            // now that markers exist in ObjectDB. (Pieces built at ZNetScene.Awake
            // had null marker requirements because ODB wasn't populated yet.)
            var hammerTable = TrailborneAssets.GetHammerPieceTable();
            foreach (var color in Colors)
            {
                var cairnPrefab = zns?.GetPrefab(CairnName(color));
                if (cairnPrefab == null) continue;
                var piece = cairnPrefab.GetComponent<Piece>();
                if (piece != null)
                {
                    piece.m_resources = new[]
                    {
                        BuildReq("Stone", 3),
                        BuildReq("Resin", 1),
                        BuildReq(MarkerName(color), 1),
                    };
                }
                if (hammerTable != null) TrailborneAssets.AddPieceToTable(cairnPrefab, hammerTable);
            }

            TrailbornePlugin.Log.LogInfo("[Trailborne/M2] M2 ObjectDB wiring complete (4 marker variants + 4 cairn variants + recipes).");
        }

        // ───────────────────────────────────────────────
        // SE_Rested comfort patch — inject cairn comfort floor
        // ───────────────────────────────────────────────

        public static int GetCairnComfortBonus(Vector3 position)
        {
            int floor = 0;
            var hits = Physics.OverlapSphere(position, CairnComfortRadius);
            foreach (var h in hits)
            {
                if (h == null) continue;
                var tag = h.GetComponentInParent<TrailborneCairnTag>();
                if (tag != null)
                {
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
            var station = p?.GetComponent<CraftingStation>();
            if (station == null)
            {
                TrailbornePlugin.Log.LogWarning(
                    $"[Trailborne/M2] FindStation: '{piecePrefabName}' missing or has no CraftingStation. " +
                    "Recipe will register against null station (no bench requirement).");
            }
            return station;
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount)
        {
            return TrailborneAssets.BuildReq(resourcePrefabName, amount, "M2");
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }
    }

    public class TrailborneCairnTag : MonoBehaviour
    {
        // v0.1.0: presence = tier-1 cairn. Tier upgrades land in M2.5+.
        public int Tier = 1;
        public string Color = "white";
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
