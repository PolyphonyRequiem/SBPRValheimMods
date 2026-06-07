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
    /// Pigment items (Red/White/Blue/Black) — the shared crafting ingredient
    /// consumed by both Signs and Cairns. Split out of the old M1 (which mixed
    /// pigments + signs) so Signs and Cairns depend DOWN on a foundational
    /// content feature rather than sideways on each other.
    ///
    /// "Pigment" is the canonical player-facing AND code-facing term (spec §A2.5,
    /// datasets/PIECES_AND_CRAFTABLES.md). The const VALUES below are the prefab
    /// names — a save/wire contract — so they keep their historical "SBPR_Ink*"
    /// spelling (renaming a prefab name would orphan every already-placed sign /
    /// cairn that stored it). Only the identifiers, display names, and helper
    /// names say "Pigment"; the on-the-wire prefab string is unchanged.
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
        // Item prefab names. The VALUES are wire/save contracts (ZDO + prefab
        // registry keyed by these strings on every placed sign/cairn) and MUST
        // NOT change; only the C# identifiers were unified to "Pigment".
        public const string PigmentRedName   = "SBPR_InkRed";
        public const string PigmentWhiteName = "SBPR_InkWhite";
        public const string PigmentBlueName  = "SBPR_InkBlue";
        public const string PigmentBlackName = "SBPR_InkBlack";

        // Source clones
        private const string SourceCoinItem = "Coins"; // safe clone for tiny consumable item

        // Icon file mapping. The PNG filenames are shipped build assets (modpack
        // zip contents) and keep their historical names; only the dictionary keys
        // are the renamed pigment-name consts.
        private static readonly Dictionary<string, string> icons = new Dictionary<string, string>
        {
            { PigmentRedName,   "ink_red_v0.1.png"   },
            { PigmentWhiteName, "ink_white_v0.1.png" },
            { PigmentBlueName,  "ink_blue_v0.1.png"  },
            { PigmentBlackName, "ink_black_v0.1.png" },
        };

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (called from ZNetScene.Awake postfix)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            // Pigment items: clone Coins (tiny consumable, simplest ItemDrop)
            RegisterPigmentPrefab(zns, PigmentRedName,   "Red Pigment",   "Red Pigment (raspberry).");
            RegisterPigmentPrefab(zns, PigmentWhiteName, "White Pigment", "White Pigment (bone).");
            RegisterPigmentPrefab(zns, PigmentBlueName,  "Blue Pigment",  "Blue Pigment (blueberry).");
            RegisterPigmentPrefab(zns, PigmentBlackName, "Black Pigment", "Black Pigment (coal).");
        }

        private static void RegisterPigmentPrefab(ZNetScene zns, string name, string displayName, string desc)
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
                if (icons.TryGetValue(name, out var iconFile))
                {
                    var sprite = Assets.LoadPngAsSprite(iconFile);
                    if (sprite != null) drop.m_itemData.m_shared.m_icons = new[] { sprite };
                }
            }
            Assets.RegisterPrefabInZNetScene(clone);
            Plugin.Log.LogInfo($"[Trailborne/M1] Registered pigment item: {name}");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — pigment recipes
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            // Items into ODB
            foreach (var n in new[] { PigmentRedName, PigmentWhiteName, PigmentBlueName, PigmentBlackName })
            {
                var p = zns?.GetPrefab(n);
                if (p != null) Assets.RegisterItemInObjectDB(p);
            }

            // Recipes
            AddPigmentRecipe(PigmentRedName,   "Raspberry",     amount: 2);
            AddPigmentRecipe(PigmentWhiteName, "BoneFragments", amount: 2);
            AddPigmentRecipe(PigmentBlueName,  "Blueberries",   amount: 2);
            AddPigmentRecipe(PigmentBlackName, "Coal",          amount: 2);

            Plugin.Log.LogInfo("[Trailborne/M1] Pigments ObjectDB wiring complete (4 pigment items + recipes).");
        }

        private static void AddPigmentRecipe(string pigmentName, string ingredient, int amount)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;
            // Skip if already present
            foreach (var r in odb.m_recipes)
                if (r != null && r.m_item != null && r.m_item.gameObject != null && r.m_item.gameObject.name == pigmentName)
                    return;

            var pigmentPrefab = odb.GetItemPrefab(pigmentName);
            if (pigmentPrefab == null) return;
            var ingredientItem = odb.GetItemPrefab(ingredient)?.GetComponent<ItemDrop>();
            if (ingredientItem == null)
            {
                Plugin.Log.LogWarning($"[Trailborne/M1] Recipe ingredient '{ingredient}' not in ODB; skipping pigment '{pigmentName}'.");
                return;
            }

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name              = "Recipe_" + pigmentName;
            recipe.m_item            = pigmentPrefab.GetComponent<ItemDrop>();
            recipe.m_amount          = amount;
            recipe.m_minStationLevel = 1;
            recipe.m_craftingStation = RecipeHelpers.FindStation(Trailhead.ExplorersBenchName);
            recipe.m_resources       = new[] { BuildReq(ingredient, 1) };
            odb.m_recipes.Add(recipe);
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount)
        {
            return Assets.BuildReq(resourcePrefabName, amount, "M1");
        }
    }
}
