// ============================================================================
//  Trailborne v2 cartography — Local Map item (§2A) + per-instance storage
// ----------------------------------------------------------------------------
//  The SBPR_LocalMap item: a craftable, TwoHandedWeapon-slot "map you hold" that
//  is BLANK when crafted and only carries survey data once imprinted at a
//  Surveyor's Table (§2A.5). This file owns:
//    • the item prefab + recipe (DeerHide x1 + FineWood x1 @ Explorer's Bench),
//    • the ItemType=TwoHandedWeapon lock + attack suppression (AT-MAP-BLOCKCLEAR),
//    • PER-INSTANCE storage of the windowed survey snapshot in the ItemData's
//      m_customData dictionary (verified to round-trip Inventory.Save/Load AND the
//      dropped-item ZDO on this game version — see the build-time decomp check in
//      the PR handoff; no ZDO "map case" fallback needed),
//    • the imprint operation (Table snapshot → item m_customData).
//
//  The equip discipline (torch exception) lives in LocalMapEquipPatch.cs; the
//  minimap-binding-while-in-inventory + full-screen-on-equip behaviour lives in
//  LocalMapBindingPatch.cs. The forked viewer it opens lives in MapViewer.cs.
//
//  Clean-side (ADR-0001): vanilla item types/fields read+adapted from the base
//  game. The item is built by the repo's proven clone-and-reshape idiom (the
//  Trailblazer's Spade clones Hoe the same way, Trailblazing.cs:229) — a tool
//  donor gives a valid two-handed in-hand mesh; we reshape its type, strip its
//  build PieceTable, and suppress its attacks. (ADR-0006's no-clone rule is about
//  ZNetView-bearing PIECES; items follow the established clone idiom — Pigments,
//  Cairn markers, the Spade all clone item donors.)
//
//  All gated behind ServerContext.OnSBServer (via Registrar).
// ============================================================================

using System;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Cartography
{
    // Alias the Trailhead TYPE: from this sibling Features.* namespace the bare name
    // `Trailhead` binds to the sibling NAMESPACE, so alias it to the type for the
    // readable Trailhead.ExplorersBenchName station lookup (same idiom as Pigments.cs:12).
    using Trailhead = SBPR.Trailborne.Features.Trailhead.Trailhead;

    public static class LocalMap
    {
        // LOCKED prefab name — a save/wire contract the moment an item is crafted; never
        // rename (renaming orphans every crafted instance + its stored survey). Impl spec §0/§5.
        public const string LocalMapName = "SBPR_LocalMap";

        // Clone donor: the Hoe (a two-handed tool with a valid in-hand mesh) — the SAME
        // donor the Trailblazer's Spade uses (Trailblazing.cs). We reshape it to a
        // TwoHandedWeapon, null its build PieceTable, and empty its attacks below, so none
        // of the Hoe's tool behaviour survives — only its renderable held model.
        private const string SourceHeldTool = "Hoe";

        // Recipe — LOCKED (impl spec §0 row 2 / §2A.1). DeerHide x1 + FineWood x1, amount 1,
        // crafted at the Explorer's Bench (NOT the Surveyor's Table).
        public const int DeerHideCost = 1;
        public const int FineWoodCost = 1;

        // m_customData keys for the per-instance imprinted survey snapshot (§2A.5). The
        // value of MapBlobKey is Base64(Utils.Compress(SurveyData.Serialize())) — the §2C
        // windowed format, identical to what the Table persists, so one format serves
        // item + Table + viewer. BoundKey carries the bound-origin world coord (redundant
        // with the blob's OriginX/Z, kept as a fast "is this map imprinted?" probe).
        public const string MapBlobKey = "sbpr_map_blob";
        public const string BoundKey   = "sbpr_map_bound";

        private const string IconFile = "local_map_v0.1.png"; // optional; falls back to no icon

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (ZNetScene.Awake postfix, via Registrar)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            if (zns.GetPrefab(LocalMapName) != null) return;

            var clone = Assets.ClonePrefab(SourceHeldTool, LocalMapName);
            if (clone == null)
            {
                Plugin.Log.LogWarning($"[Trailborne/Cartography] Could not clone '{SourceHeldTool}' for {LocalMapName}; skipping.");
                return;
            }

            var drop = clone.GetComponent<ItemDrop>();
            if (drop != null)
            {
                var shared = drop.m_itemData.m_shared;
                shared.m_name        = "Local Map";
                shared.m_description =
                    "A blank field map. Imprint it at a Surveyor's Table to copy that table's 1000 m survey, " +
                    "then equip it (two hands) to read the bounded map. Stays bound while it's in your pack; " +
                    "loses the binding the moment it leaves your inventory.";

                // ── The decisive lock (impl spec §2A.2): ItemType = TwoHandedWeapon (=14). ──
                // Its EquipItem branch (decomp Humanoid.EquipItem, verified against
                // assembly_valheim.dll: UnequipItem(m_leftItem)+UnequipItem(m_rightItem)+
                // m_rightItem=item+clear hidden) gives the C3 true-unequip-never-hide
                // block-clear discipline for free (AT-MAP-BLOCKCLEAR).
                shared.m_itemType   = ItemDrop.ItemData.ItemType.TwoHandedWeapon;
                shared.m_animationState = ItemDrop.ItemData.AnimationState.TwoHandedAxe; // benign two-handed hold pose
                shared.m_maxStackSize = 1;     // a bound map is per-instance; never stacks
                shared.m_weight      = 0.5f;
                shared.m_maxQuality  = 1;       // no upgrade tiers
                shared.m_useDurability = false; // never "breaks"
                shared.m_canBeReparied = false;
                shared.m_centerCamera  = false;

                // ── Suppress combat (AT-MAP-BLOCKCLEAR): empty attack animations →
                //    HavePrimaryAttack()/HaveSecondaryAttack() (decomp ItemDrop :445/:450,
                //    return !IsNullOrEmpty(m_attack.m_attackAnimation)) are false → LMB/RMB
                //    do nothing, no block. The clone owns its own [Serializable]
                //    SharedData/Attack (Instantiate deep-copies), so we are NOT mutating Hoe.
                if (shared.m_attack != null)
                {
                    shared.m_attack.m_attackAnimation = string.Empty;
                    shared.m_attack.m_attackType      = Attack.AttackType.None;
                }
                if (shared.m_secondaryAttack != null)
                {
                    shared.m_secondaryAttack.m_attackAnimation = string.Empty;
                    shared.m_secondaryAttack.m_attackType      = Attack.AttackType.None;
                }

                // ── Strip the Hoe's build PieceTable so it has no scroll-to-build menu. ──
                shared.m_buildPieces = null;

                // Block power 0 (defensive; with no left-hand item it can't block anyway,
                // but a 2H weapon's own block charge should be inert on a map).
                shared.m_blockPower = 0f;
                shared.m_deflectionForce = 0f;

                var sprite = Assets.LoadPngAsSprite(IconFile);
                if (sprite != null) shared.m_icons = new[] { sprite };
            }

            // Tag the prefab so the equip/binding patches can identify our item cheaply
            // (a component check is faster + rename-proof vs a string compare every equip).
            if (clone.GetComponent<LocalMapItemTag>() == null)
                clone.AddComponent<LocalMapItemTag>();

            Assets.RegisterPrefabInZNetScene(clone);
            Plugin.Log.LogInfo($"[Trailborne/Cartography] Registered Local Map item: {LocalMapName} (TwoHandedWeapon, blank-until-imprinted).");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — item into ODB + recipe
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            var p = zns?.GetPrefab(LocalMapName);
            if (p != null) Assets.RegisterItemInObjectDB(p);

            AddRecipe();

            Plugin.Log.LogInfo("[Trailborne/Cartography] Local Map ObjectDB wiring complete (item + recipe @ Explorer's Bench).");
        }

        private static void AddRecipe()
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            // Skip if already present (re-entrant ODB hooks).
            foreach (var r in odb.m_recipes)
                if (r != null && r.m_item != null && r.m_item.gameObject != null && r.m_item.gameObject.name == LocalMapName)
                    return;

            var prefab = odb.GetItemPrefab(LocalMapName);
            if (prefab == null) return;

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name              = "Recipe_" + LocalMapName;
            recipe.m_item            = prefab.GetComponent<ItemDrop>();
            recipe.m_amount          = 1;
            recipe.m_minStationLevel = 1;
            recipe.m_craftingStation = RecipeHelpers.FindStation(Trailhead.ExplorersBenchName);
            recipe.m_resources       = new[]
            {
                Assets.BuildReq("DeerHide", DeerHideCost, "Cartography"),
                Assets.BuildReq("FineWood", FineWoodCost, "Cartography"),
            };
            odb.m_recipes.Add(recipe);
        }

        // ───────────────────────────────────────────────
        // PER-INSTANCE STORAGE (§2A.5) — imprint + read the windowed survey snapshot
        // ───────────────────────────────────────────────

        /// <summary>
        /// Imprint a blank/old Local Map with a SNAPSHOT (not a live link, §2A.5) of the
        /// given Table survey. Stores Base64(Utils.Compress(survey.Serialize())) in the
        /// item's m_customData under <see cref="MapBlobKey"/>, plus the bound-origin coord
        /// under <see cref="BoundKey"/>. Returns true on success. The blob is the §2C
        /// windowed format — identical to the Table's, so the viewer reads one format.
        /// </summary>
        public static bool Imprint(ItemDrop.ItemData item, SurveyData survey, Vector3 boundOrigin)
        {
            if (item == null || survey == null) return false;
            try
            {
                byte[] compressed = Utils.Compress(survey.Serialize());
                item.m_customData[MapBlobKey] = Convert.ToBase64String(compressed);
                item.m_customData[BoundKey]   = $"{boundOrigin.x:R};{boundOrigin.z:R}";
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Cartography] LocalMap.Imprint failed: {e.Message}");
                return false;
            }
        }

        /// <summary>True if the item carries an imprinted survey (has the blob key).</summary>
        public static bool IsImprinted(ItemDrop.ItemData item)
            => item != null && item.m_customData != null && item.m_customData.ContainsKey(MapBlobKey);

        /// <summary>
        /// Read the imprinted windowed survey snapshot off a Local Map instance, or null if
        /// the map is blank / the blob is malformed. The returned SurveyData is a fresh
        /// deserialization (a snapshot copy), never a live reference to a Table.
        /// </summary>
        public static SurveyData? ReadSurvey(ItemDrop.ItemData item)
        {
            if (!IsImprinted(item)) return null;
            try
            {
                string b64 = item.m_customData[MapBlobKey];
                if (string.IsNullOrEmpty(b64)) return null;
                byte[] raw = Utils.Decompress(Convert.FromBase64String(b64));
                return SurveyData.Deserialize(raw);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Cartography] LocalMap.ReadSurvey failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Read the bound-origin world coord (X,Z) of an imprinted map. Falls back to the
        /// blob's own OriginX/Z if the convenience key is absent. Returns false if blank.
        /// </summary>
        public static bool TryGetBoundOrigin(ItemDrop.ItemData item, out Vector3 origin)
        {
            origin = Vector3.zero;
            if (!IsImprinted(item)) return false;

            if (item.m_customData.TryGetValue(BoundKey, out var s) && !string.IsNullOrEmpty(s))
            {
                var parts = s.Split(';');
                if (parts.Length == 2 &&
                    float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                {
                    origin = new Vector3(x, 0f, z);
                    return true;
                }
            }

            // Fallback: derive from the blob itself.
            var survey = ReadSurvey(item);
            if (survey != null)
            {
                origin = new Vector3(survey.OriginX, 0f, survey.OriginZ);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Marker component baked onto the SBPR_LocalMap prefab so the equip + binding patches
    /// identify our item by a component check (rename-proof, faster than a string compare on
    /// every equip). Carries no state — its presence on the held GameObject is the signal.
    /// </summary>
    public class LocalMapItemTag : MonoBehaviour { }
}
