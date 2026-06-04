using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne
{
    /// <summary>
    /// M1 content: pigment items (Red/White/Blue/Black inks) + Painted Signs
    /// (4 buildable variants, color tinted from base material). Player presses
    /// Shift+E on a placed sign to add a colored map pin matching the sign's
    /// text + color.
    ///
    /// Pigment ingredients per spec:
    ///   Red   ← Raspberry          (Meadows)
    ///   White ← BoneFragments      (Meadows / BF)
    ///   Blue  ← Blueberries        (Black Forest)
    ///   Black ← Coal               (Black Forest)
    ///
    /// All gated behind SBPRContext.OnSBServer.
    /// </summary>
    public static class TrailborneM1
    {
        // Item prefab names
        public const string InkRedName   = "SBPR_InkRed";
        public const string InkWhiteName = "SBPR_InkWhite";
        public const string InkBlueName  = "SBPR_InkBlue";
        public const string InkBlackName = "SBPR_InkBlack";

        // Sign piece prefab names
        public const string SignRedName   = "piece_sbpr_sign_red";
        public const string SignWhiteName = "piece_sbpr_sign_white";
        public const string SignBlueName  = "piece_sbpr_sign_blue";
        public const string SignBlackName = "piece_sbpr_sign_black";

        // Source clones
        private const string SourceSign     = "sign";
        private const string SourceCoinItem = "Coins"; // safe clone for tiny consumable item

        // Icon file mapping
        private static readonly Dictionary<string, string> _icons = new Dictionary<string, string>
        {
            { InkRedName,   "ink_red_v0.1.png"   },
            { InkWhiteName, "ink_white_v0.1.png" },
            { InkBlueName,  "ink_blue_v0.1.png"  },
            { InkBlackName, "ink_black_v0.1.png" },
            { SignRedName,   "ink_red_v0.1.png"   },
            { SignWhiteName, "ink_white_v0.1.png" },
            { SignBlueName,  "ink_blue_v0.1.png"  },
            { SignBlackName, "ink_black_v0.1.png" },
        };

        // Color per sign (used to tint the sign's mesh + as the pin color)
        public static readonly Dictionary<string, Color> SignColors = new Dictionary<string, Color>
        {
            { SignRedName,   new Color(0.85f, 0.18f, 0.18f, 1f) },
            { SignWhiteName, new Color(0.95f, 0.94f, 0.88f, 1f) },
            { SignBlueName,  new Color(0.20f, 0.40f, 0.85f, 1f) },
            { SignBlackName, new Color(0.10f, 0.10f, 0.12f, 1f) },
        };

        // Pin type per sign — vanilla Minimap pin sprite reuse for color clarity
        public static readonly Dictionary<string, Minimap.PinType> SignPinTypes = new Dictionary<string, Minimap.PinType>
        {
            { SignRedName,   Minimap.PinType.Icon3 }, // red-ish vanilla pin
            { SignWhiteName, Minimap.PinType.Icon0 }, // generic / white
            { SignBlueName,  Minimap.PinType.Icon2 }, // blue-ish
            { SignBlackName, Minimap.PinType.Icon4 }, // dark / generic
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

            // Sign pieces — clone vanilla sign + tint
            RegisterSignPrefab(zns, SignRedName,   "Painted Sign (Red)",   "Painted Sign (red).");
            RegisterSignPrefab(zns, SignWhiteName, "Painted Sign (White)", "Painted Sign (white).");
            RegisterSignPrefab(zns, SignBlueName,  "Painted Sign (Blue)",  "Painted Sign (blue).");
            RegisterSignPrefab(zns, SignBlackName, "Painted Sign (Black)", "Painted Sign (black).");
        }

        private static void RegisterInkPrefab(ZNetScene zns, string name, string displayName, string desc)
        {
            if (zns.GetPrefab(name) != null) return;
            var clone = TrailborneAssets.ClonePrefab(SourceCoinItem, name);
            if (clone == null) return;
            var drop = clone.GetComponent<ItemDrop>();
            if (drop != null)
            {
                drop.m_itemData.m_shared.m_name        = displayName;
                drop.m_itemData.m_shared.m_description = desc;
                drop.m_itemData.m_shared.m_maxStackSize = 50;
                drop.m_itemData.m_shared.m_weight      = 0.1f;
                drop.m_itemData.m_shared.m_itemType    = ItemDrop.ItemData.ItemType.Material;
                if (_icons.TryGetValue(name, out var iconFile))
                {
                    var sprite = TrailborneAssets.LoadPngAsSprite(iconFile);
                    if (sprite != null) drop.m_itemData.m_shared.m_icons = new[] { sprite };
                }
            }
            TrailborneAssets.RegisterPrefabInZNetScene(clone);
            TrailbornePlugin.Log.LogInfo($"[Trailborne/M1] Registered ink item: {name}");
        }

        private static void RegisterSignPrefab(ZNetScene zns, string name, string displayName, string desc)
        {
            if (zns.GetPrefab(name) != null) return;
            var clone = TrailborneAssets.ClonePrefab(SourceSign, name);
            if (clone == null)
            {
                TrailbornePlugin.Log.LogWarning($"[Trailborne/M1] Source sign prefab missing, skipping {name}");
                return;
            }

            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = displayName;
                piece.m_description = desc;
                piece.m_category    = Piece.PieceCategory.Furniture;
                piece.m_resources   = new[]
                {
                    BuildReq("Wood", 2),
                    BuildReq(InkLookupForSign(name), 1),
                };
                if (_icons.TryGetValue(name, out var iconFile))
                    piece.m_icon = TrailborneAssets.LoadPngAsSprite(iconFile);
            }

            // Tag the sign with our colored variant so Interact handler can color-pin later
            var tag = clone.AddComponent<TrailborneSignTag>();
            tag.PrefabName = name;

            // Tint the visible mesh
            if (SignColors.TryGetValue(name, out var col))
                TintMeshRenderers(clone, col);

            TrailborneAssets.RegisterPrefabInZNetScene(clone);
            TrailbornePlugin.Log.LogInfo($"[Trailborne/M1] Registered sign piece: {name}");
        }

        private static string InkLookupForSign(string signName)
        {
            switch (signName)
            {
                case SignRedName:   return InkRedName;
                case SignWhiteName: return InkWhiteName;
                case SignBlueName:  return InkBlueName;
                case SignBlackName: return InkBlackName;
                default: return InkRedName;
            }
        }

        private static void TintMeshRenderers(GameObject go, Color c)
        {
            // Material-instance tint via SetColor on _Color. Vanilla sign uses lit
            // material with _Color — same shader prop ID used elsewhere in decomp.
            var prop = Shader.PropertyToID("_Color");
            foreach (var rend in go.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                try
                {
                    var mats = rend.sharedMaterials;
                    var newMats = new Material[mats.Length];
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null) continue;
                        var m = new Material(mats[i]);
                        if (m.HasProperty(prop)) m.SetColor(prop, c);
                        newMats[i] = m;
                    }
                    rend.sharedMaterials = newMats;
                }
                catch (Exception e)
                {
                    TrailbornePlugin.Log.LogWarning($"[Trailborne/M1] Tint failed on {go.name}: {e.Message}");
                }
            }
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — recipes + hammer pieces
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            // Items into ODB
            foreach (var n in new[] { InkRedName, InkWhiteName, InkBlueName, InkBlackName })
            {
                var p = zns?.GetPrefab(n);
                if (p != null) TrailborneAssets.RegisterItemInObjectDB(p);
            }

            // Recipes
            AddInkRecipe(InkRedName,   "Raspberry",     amount: 2);
            AddInkRecipe(InkWhiteName, "BoneFragments", amount: 2);
            AddInkRecipe(InkBlueName,  "Blueberries",   amount: 2);
            AddInkRecipe(InkBlackName, "Coal",          amount: 2);

            // Sign pieces into Hammer build menu
            var hammerTable = TrailborneAssets.GetHammerPieceTable();
            if (hammerTable != null)
            {
                foreach (var n in new[] { SignRedName, SignWhiteName, SignBlueName, SignBlackName })
                {
                    var p = zns?.GetPrefab(n);
                    if (p != null) TrailborneAssets.AddPieceToTable(p, hammerTable);
                }
            }

            TrailbornePlugin.Log.LogInfo("[Trailborne/M1] M1 ObjectDB wiring complete (inks + sign recipes + hammer pieces).");
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
                TrailbornePlugin.Log.LogWarning($"[Trailborne/M1] Recipe ingredient '{ingredient}' not in ODB; skipping ink '{inkName}'.");
                return;
            }

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name              = "Recipe_" + inkName;
            recipe.m_item            = inkPrefab.GetComponent<ItemDrop>();
            recipe.m_amount          = amount;
            recipe.m_minStationLevel = 1;
            recipe.m_craftingStation = FindStation("piece_sbpr_explorers_bench");
            recipe.m_resources       = new[] { BuildReq(ingredient, 1) };
            odb.m_recipes.Add(recipe);
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

    /// <summary>
    /// Marker tag attached to each colored sign clone so we can identify
    /// the variant + look up its pin color at Interact time without
    /// reading mesh tints back.
    /// </summary>
    public class TrailborneSignTag : MonoBehaviour
    {
        public string PrefabName;
    }

    /// <summary>
    /// Harmony hook on Sign.Interact: when the player holds Shift and presses E
    /// on a TrailborneSignTag-flagged sign, add a colored map pin matching the
    /// sign's text + color INSTEAD of opening the text-edit dialog. Plain E
    /// opens the dialog as vanilla.
    /// </summary>
    [HarmonyPatch(typeof(Sign), nameof(Sign.Interact))]
    public static class Sign_Interact_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Sign __instance, Humanoid character, bool hold, bool alt, ref bool __result)
        {
            if (hold) return true;
            var tag = __instance.GetComponent<TrailborneSignTag>();
            if (tag == null) return true; // not ours — vanilla behavior

            // Shift+E = pin to map. Plain E = vanilla text edit.
            if (!UnityEngine.Input.GetKey(KeyCode.LeftShift) && !UnityEngine.Input.GetKey(KeyCode.RightShift))
                return true;

            try
            {
                var minimap = Minimap.instance;
                var player  = Player.m_localPlayer;
                if (minimap == null || player == null)
                {
                    __result = false;
                    return false;
                }
                var text = __instance.GetText();
                if (string.IsNullOrEmpty(text)) text = "Painted Sign";

                Minimap.PinType pinType = Minimap.PinType.Icon0;
                if (TrailborneM1.SignPinTypes.TryGetValue(tag.PrefabName, out var t)) pinType = t;

                minimap.AddPin(__instance.transform.position, pinType, text, save: true, isChecked: false, player.GetPlayerID());
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Pinned: {text}");

                __result = true;
                return false;
            }
            catch (Exception e)
            {
                TrailbornePlugin.Log.LogError($"[Trailborne/M1] Pin-on-Shift+E failed: {e}");
                return true;
            }
        }
    }
}
