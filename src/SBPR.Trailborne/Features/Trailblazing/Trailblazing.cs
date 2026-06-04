using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Trailblazing
{
    // Alias the Trailhead TYPE: from this sibling Features.* namespace the bare
    // name `Trailhead` would bind to the sibling NAMESPACE, so alias it to the
    // type to keep the readable `Trailhead.ExplorersBenchName` station lookup.
    using Trailhead = SBPR.Trailborne.Features.Trailhead.Trailhead;

    /// <summary>
    /// M3 content: Trailblazer's Spade real path/replant behavior, plus the
    /// Spade ITEM itself (prefab + recipe) lifted out of the old fat Registrar
    /// so the spade and its terrain ops live in one vertical slice.
    ///
    /// Clones vanilla `path` and `cultivate` prefabs, scales their
    /// TerrainModifier radii to our 3 widths (narrow/standard/wide =
    /// 1.5m / 3m / 5m), registers them as Piece prefabs, and ADDS them
    /// to the spade's PieceTable (which is currently the vanilla Hoe
    /// table — so the player sees our 3 path-widths + 1 wide replant
    /// alongside the vanilla raise/level/paved_road. Spade-only piece
    /// table = M3.5/v0.2.0+ cleanup.
    ///
    /// ClearVegetation (Pickable.RemoveOne batch) is genuinely
    /// out-of-scope for v0.1.0 — vanilla cultivate already restores
    /// grass which is the "clear" act in a different direction.
    /// TWEAK ME: real ClearVegetation in v0.2.0.
    /// </summary>
    public static class Trailblazing
    {
        // Spade item prefab name (lifted from Registrar — was PublicSpadeName)
        public const string SpadeName = "SBPR_TrailblazersSpade";

        public const string PathNarrowName   = "piece_sbpr_path_narrow";
        public const string PathStandardName = "piece_sbpr_path_standard";
        public const string PathWideName     = "piece_sbpr_path_wide";
        public const string ReplantWideName  = "piece_sbpr_replant_wide";

        private const string SourcePath      = "path";
        private const string SourceCultivate = "cultivate";
        private const string SourceHoe       = "Hoe";

        private const string IconFile        = "trailblazers_spade_v0.1.png";

        private static readonly Dictionary<string, (string source, float radius)> _variants =
            new Dictionary<string, (string, float)>
            {
                { PathNarrowName,   (SourcePath,      1.5f) },
                { PathStandardName, (SourcePath,      3.0f) },
                { PathWideName,     (SourcePath,      5.0f) },
                { ReplantWideName,  (SourceCultivate, 5.0f) },
            };

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (called from ZNetScene.Awake postfix)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            RegisterSpadeItemPrefab(zns);
            foreach (var kv in _variants)
                RegisterRadiusVariant(zns, kv.Key, kv.Value.source, kv.Value.radius);
        }

        private static void RegisterSpadeItemPrefab(ZNetScene zns)
        {
            if (zns.GetPrefab(SpadeName) != null) return;
            var clone = Assets.ClonePrefab(SourceHoe, SpadeName);
            if (clone == null) return;

            var drop = clone.GetComponent<ItemDrop>();
            if (drop != null)
            {
                drop.m_itemData.m_shared.m_name        = "Trailblazer's Spade";
                drop.m_itemData.m_shared.m_description = "Trailblazer's Spade — scroll to cycle path mode (dirt / paved / clear).";
                var sprite = Assets.LoadPngAsSprite(IconFile);
                if (sprite != null)
                    drop.m_itemData.m_shared.m_icons = new[] { sprite };
                // m_buildPieces inherits from Hoe — already has terrain-op pieces
                // (raise/level/paved_road) accessible via the vanilla scroll-wheel
                // cycler. For M0 we use that as the path-mode cycler instead of
                // wiring a new right-click handler. TWEAK ME: right-click rebind.
            }

            Assets.RegisterPrefabInZNetScene(clone);
            Plugin.Log.LogInfo($"[Trailborne] Registered item prefab: {SpadeName}");
        }

        private static string NicePieceName(string prefab)
        {
            switch (prefab)
            {
                case PathNarrowName:   return "Spade: Path (1.5m)";
                case PathStandardName: return "Spade: Path (3m)";
                case PathWideName:     return "Spade: Path (5m)";
                case ReplantWideName:  return "Spade: Replant (5m)";
                default:               return prefab;
            }
        }

        private static void RegisterRadiusVariant(ZNetScene zns, string name, string source, float radius)
        {
            if (zns.GetPrefab(name) != null) return;
            var clone = Assets.ClonePrefab(source, name);
            if (clone == null)
            {
                Plugin.Log.LogWarning($"[Trailborne/M3] Source '{source}' missing; skipping {name}");
                return;
            }
            // Scale terrain radii on the cloned TerrainModifier
            var mod = clone.GetComponentInChildren<TerrainModifier>(includeInactive: true);
            if (mod != null)
            {
                mod.m_levelRadius  = radius;
                mod.m_smoothRadius = radius;
                mod.m_paintRadius  = radius;
            }
            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = NicePieceName(name);
                piece.m_description = $"Trailblazer path/ground op — {radius:F1}m radius.";
                piece.m_category    = Piece.PieceCategory.Misc;
                // Free placement like vanilla hoe ops — no resource cost
                piece.m_resources   = Array.Empty<Piece.Requirement>();
            }
            Assets.RegisterPrefabInZNetScene(clone);
            Plugin.Log.LogInfo($"[Trailborne/M3] Registered spade op: {name} ({radius:F1}m)");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — spade into ODB, spade recipe, spade-only PieceTable
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            // Spade item into ObjectDB (was in Registrar.DoObjectDBWiring).
            var spade = GameObject.Find(SpadeName);
            // Try registered-prefab lookup first; Find() on inactive objects doesn't work.
            if (zns != null)
            {
                var spadePrefab = zns.GetPrefab(SpadeName);
                if (spadePrefab != null) Assets.RegisterItemInObjectDB(spadePrefab);
            }

            // Spade recipe — at orienteering table (was Registrar.AddRecipes).
            AddSpadeRecipe();

            // Spade-only PieceTable build (the original M3 wiring).
            var drop = zns?.GetPrefab(SpadeName)?.GetComponent<ItemDrop>();
            if (drop == null)
            {
                Plugin.Log.LogWarning("[Trailborne/M3] Spade prefab missing; cannot wire spade table.");
                return;
            }

            // Build a fresh, spade-only PieceTable so the player never sees vanilla
            // raise/level/paved_road when wielding the spade. Hosted on the same
            // PrefabHolder so its GameObject doesn't get collected.
            var holderGo = new GameObject("SBPR_SpadePieceTable");
            UnityEngine.Object.DontDestroyOnLoad(holderGo);
            var table = holderGo.AddComponent<PieceTable>();
            table.m_pieces = new List<GameObject>();
            table.m_categories = new List<Piece.PieceCategory> { Piece.PieceCategory.Misc };
            table.m_categoryLabels = new List<string> { "Trail" };
            table.m_canRemovePieces = true;

            foreach (var n in _variants.Keys)
            {
                var p = zns?.GetPrefab(n);
                if (p != null) Assets.AddPieceToTable(p, table);
            }

            drop.m_itemData.m_shared.m_buildPieces = table;
            Plugin.Log.LogInfo($"[Trailborne/M3] Spade-only PieceTable built with {table.m_pieces.Count} ops.");
        }

        private static void AddSpadeRecipe()
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            // Spade recipe — at orienteering table
            if (!RecipeHelpers.HasRecipe(SpadeName))
            {
                var spade = odb.GetItemPrefab(SpadeName);
                if (spade != null)
                {
                    var r = ScriptableObject.CreateInstance<Recipe>();
                    r.name             = "Recipe_" + SpadeName;
                    r.m_item           = spade.GetComponent<ItemDrop>();
                    r.m_amount         = 1;
                    r.m_minStationLevel = 1;
                    r.m_craftingStation = RecipeHelpers.FindStation(Trailhead.ExplorersBenchName);
                    r.m_resources      = new[]
                    {
                        BuildReq("Wood", 5),
                        BuildReq("Flint", 2),
                        BuildReq("LeatherScraps", 2),
                    };
                    odb.m_recipes.Add(r);
                    Plugin.Log.LogInfo("[Trailborne] Added recipe for spade.");
                }
            }
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount)
        {
            return Assets.BuildReq(resourcePrefabName, amount, "Core");
        }
    }
}
