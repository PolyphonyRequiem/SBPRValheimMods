using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Pigments
{
    // Alias the Trailhead TYPE: from this sibling Features.* namespace the bare
    // name `Trailhead` would bind to the sibling NAMESPACE, so alias it to the
    // type to keep the readable `Trailhead.ExplorersBenchName` station lookup.
    using Trailhead = SBPR.Trailborne.Features.Trailhead.Trailhead;

    /// <summary>
    /// Pigment items (Red/White/Blue/Black inks) — the shared crafting
    /// ingredient consumed by both Signs and Cairns. Split out of the old
    /// M1 (which mixed inks + signs) so Signs and Cairns depend DOWN on a
    /// foundational content feature rather than sideways on each other.
    ///
    /// Pigment ingredients per spec:
    ///   Red   ← Raspberry          (Meadows)
    ///   White ← BoneFragments      (Meadows / BF)
    ///   Blue  ← Blueberries        (Black Forest)
    ///   Black ← Coal               (Black Forest)
    ///
    /// All gated behind ServerContext.OnSBServer.
    /// </summary>
    public static class Pigments
    {
        // Item prefab names
        public const string InkRedName   = "SBPR_InkRed";
        public const string InkWhiteName = "SBPR_InkWhite";
        public const string InkBlueName  = "SBPR_InkBlue";
        public const string InkBlackName = "SBPR_InkBlack";

        // Source clones
        private const string SourceCoinItem = "Coins"; // safe clone for tiny consumable item

        // Icon file mapping
        private static readonly Dictionary<string, string> _icons = new Dictionary<string, string>
        {
            { InkRedName,   "ink_red_v0.1.png"   },
            { InkWhiteName, "ink_white_v0.1.png" },
            { InkBlueName,  "ink_blue_v0.1.png"  },
            { InkBlackName, "ink_black_v0.1.png" },
        };

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (called from ZNetScene.Awake postfix)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            // Ink items: clone Coins (tiny consumable, simplest ItemDrop)
            RegisterInkPrefab(zns, InkRedName,   "Red Ink",   "Red Ink (raspberry).");
            RegisterInkPrefab(zns, InkWhiteName, "White Ink", "White Ink (bone).");
            RegisterInkPrefab(zns, InkBlueName,  "Blue Ink",  "Blue Ink (blueberry).");
            RegisterInkPrefab(zns, InkBlackName, "Black Ink", "Black Ink (coal).");
        }

        private static void RegisterInkPrefab(ZNetScene zns, string name, string displayName, string desc)
        {
            if (zns.GetPrefab(name) != null) return;
            var clone = Assets.ClonePrefab(SourceCoinItem, name);
            if (clone == null) return;
            var drop = clone.GetComponent<ItemDrop>();
            if (drop != null)
            {
                drop.m_itemData.m_shared.m_name        = displayName;
                drop.m_itemData.m_shared.m_description = desc;
                drop.m_itemData.m_shared.m_maxStackSize = 20; // spec PARKED v1: stack 20
                drop.m_itemData.m_shared.m_weight      = 0.1f;
                drop.m_itemData.m_shared.m_itemType    = ItemDrop.ItemData.ItemType.Material;
                if (_icons.TryGetValue(name, out var iconFile))
                {
                    var sprite = Assets.LoadPngAsSprite(iconFile);
                    if (sprite != null) drop.m_itemData.m_shared.m_icons = new[] { sprite };
                }
            }
            Assets.RegisterPrefabInZNetScene(clone);
            Plugin.Log.LogInfo($"[Trailborne/M1] Registered ink item: {name}");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — ink recipes
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            // Items into ODB
            foreach (var n in new[] { InkRedName, InkWhiteName, InkBlueName, InkBlackName })
            {
                var p = zns?.GetPrefab(n);
                if (p != null) Assets.RegisterItemInObjectDB(p);
            }

            // Recipes
            AddInkRecipe(InkRedName,   "Raspberry",     amount: 2);
            AddInkRecipe(InkWhiteName, "BoneFragments", amount: 2);
            AddInkRecipe(InkBlueName,  "Blueberries",   amount: 2);
            AddInkRecipe(InkBlackName, "Coal",          amount: 2);

            Plugin.Log.LogInfo("[Trailborne/M1] Pigments ObjectDB wiring complete (4 ink items + recipes).");
        }

        private static void AddInkRecipe(string inkName, string ingredient, int amount)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;
            // Skip if already present
            foreach (var r in odb.m_recipes)
                if (r != null && r.m_item != null && r.m_item.gameObject != null && r.m_item.gameObject.name == inkName)
                    return;

            var inkPrefab = odb.GetItemPrefab(inkName);
            if (inkPrefab == null) return;
            var ingredientItem = odb.GetItemPrefab(ingredient)?.GetComponent<ItemDrop>();
            if (ingredientItem == null)
            {
                Plugin.Log.LogWarning($"[Trailborne/M1] Recipe ingredient '{ingredient}' not in ODB; skipping ink '{inkName}'.");
                return;
            }

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name              = "Recipe_" + inkName;
            recipe.m_item            = inkPrefab.GetComponent<ItemDrop>();
            recipe.m_amount          = amount;
            recipe.m_minStationLevel = 1;
            recipe.m_craftingStation = FindStation(Trailhead.ExplorersBenchName);
            recipe.m_resources       = new[] { BuildReq(ingredient, 1) };
            odb.m_recipes.Add(recipe);
        }

        private static CraftingStation FindStation(string piecePrefabName)
        {
            var zns = ZNetScene.instance;
            var p = zns?.GetPrefab(piecePrefabName);
            var station = p?.GetComponent<CraftingStation>();
            if (station == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/M1] FindStation: '{piecePrefabName}' missing or has no CraftingStation. " +
                    "Recipe will register against null station (no bench requirement).");
            }
            return station;
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount)
        {
            return Assets.BuildReq(resourcePrefabName, amount, "M1");
        }
    }
}
