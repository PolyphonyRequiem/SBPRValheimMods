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
    /// Clones the vanilla `path` prefab three times and scales each clone's
    /// TerrainModifier radii to our 3 widths (narrow/standard/wide =
    /// 1.5m / 3m / 5m). ALSO clones the vanilla `replant` op — the
    /// Cultivator's "Grass" mode that regrows grass on dirt — at its STOCK
    /// vanilla radius with NO override, so the spade's replant mirrors the
    /// Cultivator's replant exactly: a small grass-restore brush, not a wide
    /// terrain modifier.
    ///
    /// NB: `replant` is the grass-restore op; `cultivate` is the soil-tiller
    /// (turns ground into farmland for crops) and is deliberately NOT used —
    /// the spade stays in the trail/exploration lane, not farming (see
    /// requirements.md "No Cultivate ability"). Cloning `cultivate` at a forced
    /// 5m radius was the "UBER level" bug this slice fixes.
    ///
    /// All ops register as Piece prefabs ADDED to the spade's PieceTable, so
    /// the player sees our 3 path-widths + 1 replant. Spade-only piece table
    /// is built in DoObjectDBWiring.
    ///
    /// ClearVegetation (Pickable.RemoveOne batch) is genuinely out-of-scope
    /// for v0.1.0. TWEAK ME: real ClearVegetation in v0.2.0.
    /// </summary>
    public static class Trailblazing
    {
        // Spade item prefab name (lifted from Registrar — was PublicSpadeName)
        public const string SpadeName = "SBPR_TrailblazersSpade";

        public const string PathNarrowName   = "piece_sbpr_path_narrow";
        public const string PathStandardName = "piece_sbpr_path_standard";
        public const string PathWideName     = "piece_sbpr_path_wide";
        public const string ReplantName      = "piece_sbpr_replant";

        private const string SourcePath      = "path";
        private const string SourceReplant   = "replant";
        private const string SourceHoe       = "Hoe";

        private const string IconFile        = "trailblazers_spade_v0.1.png";

        // Flat stamina drain per path/replant op, INDEPENDENT of radius
        // (1.5m / 3m / 5m all cost the same). See requirements.md A3.9
        // (Daniel, 2026-06-04 playtest lock). Terrain-op / build stamina is
        // driven by the WIELDING TOOL, not the op piece: Player.GetBuildStamina()
        // reads the right-hand ItemDrop's m_shared.m_attack.m_attackStamina.
        // Pieces / TerrainModifier carry no stamina field, so this is the only
        // layer where the cost can be pinned — and pinning it here is
        // radius-independent by construction. (design/nomap.md §2.)
        private const float PathOpStamina    = 2f;

        // source = vanilla prefab to clone; radius = TerrainModifier radius
        // OVERRIDE in metres, or null to keep the vanilla op's stock radius.
        // Path widths get our 1.5/3/5m overrides; Replant keeps vanilla
        // (mirrors the Cultivator's grass-replant exactly — NOT a wide op).
        private static readonly Dictionary<string, (string source, float? radius)> variants =
            new Dictionary<string, (string, float?)>
            {
                { PathNarrowName,   (SourcePath,    1.5f) },
                { PathStandardName, (SourcePath,    3.0f) },
                { PathWideName,     (SourcePath,    5.0f) },
                { ReplantName,      (SourceReplant, null) },
            };

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (called from ZNetScene.Awake postfix)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            RegisterSpadeItemPrefab(zns);
            foreach (var kv in variants)
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

                // Flat path/replant stamina: pin the tool's build-stamina to 2 so
                // every width (1.5m / 3m / 5m) costs the same. Player.GetBuildStamina()
                // returns the wielded item's m_shared.m_attack.m_attackStamina, so the
                // spade — not the cloned op pieces — is the correct layer. This clone
                // owns its own [Serializable] SharedData/Attack (deep-copied by
                // Instantiate), so we are NOT mutating the vanilla Hoe.
                var shared = drop.m_itemData.m_shared;
                if (shared.m_attack != null)
                    shared.m_attack.m_attackStamina = PathOpStamina;
                if (shared.m_secondaryAttack != null)
                    shared.m_secondaryAttack.m_attackStamina = PathOpStamina;

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
                case ReplantName:      return "Spade: Replant Grass";
                default:               return prefab;
            }
        }

        private static void RegisterRadiusVariant(ZNetScene zns, string name, string source, float? radius)
        {
            if (zns.GetPrefab(name) != null) return;
            var clone = Assets.ClonePrefab(source, name);
            if (clone == null)
            {
                Plugin.Log.LogWarning($"[Trailborne/M3] Source '{source}' missing; skipping {name}");
                return;
            }
            // Scale terrain radii ONLY for the path widths. Replant passes
            // radius=null so it keeps the vanilla op's stock radius untouched
            // — matching the Cultivator's grass-replant exactly.
            if (radius.HasValue)
            {
                var mod = clone.GetComponentInChildren<TerrainModifier>(includeInactive: true);
                if (mod != null)
                {
                    mod.m_levelRadius  = radius.Value;
                    mod.m_smoothRadius = radius.Value;
                    mod.m_paintRadius  = radius.Value;
                }
            }
            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = NicePieceName(name);
                piece.m_description = radius.HasValue
                    ? $"Trailblazer path/ground op — {radius.Value:F1}m radius."
                    : "Trailblazer grass-replant — restores grass at the vanilla Cultivator radius.";
                piece.m_category    = Piece.PieceCategory.Misc;
                // Free placement like vanilla hoe ops — no resource cost
                piece.m_resources   = Array.Empty<Piece.Requirement>();
            }
            Assets.RegisterPrefabInZNetScene(clone);
            var radiusLabel = radius.HasValue ? $"{radius.Value:F1}m" : "vanilla radius";
            Plugin.Log.LogInfo($"[Trailborne/M3] Registered spade op: {name} ({radiusLabel})");
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

            foreach (var n in variants.Keys)
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
