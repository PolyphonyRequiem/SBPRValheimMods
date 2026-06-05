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
    // Same aliasing rationale for Signs (sibling namespace vs. the Signs type we
    // need for Signs.SignName when adding the sign to the spade build menu).
    using Signs = SBPR.Trailborne.Features.Signs.Signs;

    /// <summary>
    /// M3 content: Trailblazer's Spade real path/replant behavior, plus the
    /// Spade ITEM itself (prefab + recipe) lifted out of the old fat Registrar
    /// so the spade and its terrain ops live in one vertical slice.
    ///
    /// Clones the vanilla `path` prefab three times and scales each clone's
    /// TerrainModifier radii to our 3 widths (narrow/standard/wide =
    /// 1.5m / 3m / 5m). ALSO clones the vanilla `replant` op — the
    /// Cultivator's "Grass" mode that regrows grass on dirt — THREE times at the
    /// SAME 1.5/3/5m widths, but scaling ONLY the grass/paint footprint
    /// (m_paintRadius). The replant clones leave m_levelRadius / m_smoothRadius
    /// at the vanilla op's values, so every replant width stays a pure
    /// grass-restore brush — no terrain raise/level/smooth at ANY width. The
    /// player scrolls through Path ×3 + Replant Grass ×3 (Daniel playtest call
    /// 2026-06-05: 3 replant widths mirroring the 3 path widths).
    ///
    /// NB: `replant` is the grass-restore op; `cultivate` is the soil-tiller
    /// (turns ground into farmland for crops) and is deliberately NOT used —
    /// the spade stays in the trail/exploration lane, not farming (see
    /// requirements.md "No Cultivate ability"). Cloning `cultivate` at a forced
    /// 5m radius was the "UBER level" bug PR #16 fixed; the 3-width replant here
    /// preserves that fix by never touching the level/smooth radii (see the
    /// grassRestore branch in RegisterRadiusVariant).
    ///
    /// All ops register as Piece prefabs ADDED to the spade's PieceTable, so
    /// the player sees our 3 path-widths + 3 replant-widths. Spade-only piece
    /// table is built in DoObjectDBWiring.
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
        public const string ReplantNarrowName   = "piece_sbpr_replant_narrow";
        public const string ReplantStandardName = "piece_sbpr_replant_standard";
        public const string ReplantWideName      = "piece_sbpr_replant_wide";

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
        // OVERRIDE in metres; grassRestore = TRUE for the Cultivator-"Grass"
        // replant ops, which must scale ONLY the grass/paint footprint
        // (m_paintRadius) and leave m_levelRadius / m_smoothRadius at the vanilla
        // clone's values so they NEVER raise/level/smooth terrain at any width
        // (PR #16 regression guard). FALSE for path ops, which scale all three
        // radii together (their job IS to paint + level the path tile to width).
        //
        // Path widths and Replant widths BOTH get our 1.5/3/5m UX, but the
        // radius is applied to DIFFERENT fields depending on grassRestore — that
        // branch is the whole safety story of this slice (see RegisterRadiusVariant).
        private static readonly Dictionary<string, (string source, float radius, bool grassRestore)> variants =
            new Dictionary<string, (string, float, bool)>
            {
                { PathNarrowName,      (SourcePath,    1.5f, false) },
                { PathStandardName,    (SourcePath,    3.0f, false) },
                { PathWideName,        (SourcePath,    5.0f, false) },
                { ReplantNarrowName,   (SourceReplant, 1.5f, true)  },
                { ReplantStandardName, (SourceReplant, 3.0f, true)  },
                { ReplantWideName,     (SourceReplant, 5.0f, true)  },
            };

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (called from ZNetScene.Awake postfix)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            RegisterSpadeItemPrefab(zns);
            foreach (var kv in variants)
                RegisterRadiusVariant(zns, kv.Key, kv.Value.source, kv.Value.radius, kv.Value.grassRestore);
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
                case PathNarrowName:      return "Spade: Path (1.5m)";
                case PathStandardName:    return "Spade: Path (3m)";
                case PathWideName:        return "Spade: Path (5m)";
                case ReplantNarrowName:   return "Spade: Replant Grass (1.5m)";
                case ReplantStandardName: return "Spade: Replant Grass (3m)";
                case ReplantWideName:     return "Spade: Replant Grass (5m)";
                default:                  return prefab;
            }
        }

        private static void RegisterRadiusVariant(ZNetScene zns, string name, string source, float radius, bool grassRestore)
        {
            if (zns.GetPrefab(name) != null) return;
            var clone = Assets.ClonePrefab(source, name);
            if (clone == null)
            {
                Plugin.Log.LogWarning($"[Trailborne/M3] Source '{source}' missing; skipping {name}");
                return;
            }
            // Radius application BRANCHES on op kind — this is the load-bearing
            // safety boundary of the slice:
            //
            //  • PATH ops (grassRestore=false): scale ALL THREE radii together
            //    (level + smooth + paint). A path tile is SUPPOSED to flatten and
            //    paint to the chosen width, so widening all three is correct.
            //
            //  • REPLANT ops (grassRestore=true): scale ONLY m_paintRadius — the
            //    grass/vegetation footprint. m_levelRadius and m_smoothRadius are
            //    LEFT UNTOUCHED at the vanilla `replant` clone's values, so the op
            //    NEVER raises, levels, or smooths terrain at ANY width. This is the
            //    regression guard for PR #16 (the "UBER level" bug): widening the
            //    grass brush cannot reintroduce terrain modification, because the
            //    terrain-modifying radii are never written. Vanilla `replant`
            //    (the Cultivator's "Grass" mode) is a pure grass-restore brush —
            //    confirmed against the public Cultivator wiki — so leaving its
            //    level/smooth behavior at stock keeps all three replant widths
            //    pure grass-restore, just at our 1.5/3/5m footprints.
            var mod = clone.GetComponentInChildren<TerrainModifier>(includeInactive: true);
            if (mod != null)
            {
                mod.m_paintRadius = radius;
                if (!grassRestore)
                {
                    // Path ops only: also widen the terrain-shaping radii.
                    mod.m_levelRadius  = radius;
                    mod.m_smoothRadius = radius;
                }
            }
            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = NicePieceName(name);
                piece.m_description = grassRestore
                    ? $"Trailblazer grass-restore — regrows grass over a {radius:F1}m footprint. No terrain raise/level."
                    : $"Trailblazer path/ground op — {radius:F1}m radius.";
                piece.m_category    = Piece.PieceCategory.Misc;
                // Free placement like vanilla hoe ops — no resource cost
                piece.m_resources   = Array.Empty<Piece.Requirement>();
            }
            Assets.RegisterPrefabInZNetScene(clone);
            var opKind = grassRestore ? "grass-restore" : "path";
            Plugin.Log.LogInfo($"[Trailborne/M3] Registered spade op: {name} ({opKind}, {radius:F1}m)");
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

            // Explorer-placed signage + lighting belong on the Trailblazer's Tools
            // (the Spade), NOT the Hammer — design pillar (design/design-pillars.md
            // lines 31-33: "Paths, signs, cairns, lamps… all live on the Tools").
            // Fixing code-vs-spec drift flagged by Daniel's 2026-06-05 playtest: the
            // Painted Sign + Path Lamp were wrongly wired onto the Hammer. We add them
            // to the SPADE table here instead.
            //
            // RACE-SAFETY: Registrar runs ALL features' RegisterPrefabs (which register
            // the sign + lamp prefabs into ZNetScene) BEFORE any DoObjectDBWiring, and
            // dispatches Trailblazing AFTER Trailhead + Signs, so both prefabs resolve
            // by name here. Both pieces are PieceCategory.Misc (matching this table's
            // single 'Trail' category) so they render in the one tab. (A separate
            // 'Signage'/'Lights' tab is a possible v1.x usability tweak — flagged for
            // Daniel; a single 'Trail' tab is acceptable for v1.)
            AddSpadePieceByName(zns, table, Signs.SignName);
            AddSpadePieceByName(zns, table, Trailhead.PathLampName);

            drop.m_itemData.m_shared.m_buildPieces = table;
            Plugin.Log.LogInfo($"[Trailborne/M3] Spade-only PieceTable built with {table.m_pieces.Count} pieces (3 path widths + replant + sign + lamp).");
        }

        /// <summary>
        /// Resolve a piece prefab by name from ZNetScene and add it to the spade's
        /// PieceTable, logging a warning if the prefab is missing (a registration
        /// ordering regression would surface here rather than silently dropping the
        /// piece from the menu).
        /// </summary>
        private static void AddSpadePieceByName(ZNetScene? zns, PieceTable table, string prefabName)
        {
            var p = zns?.GetPrefab(prefabName);
            if (p != null)
            {
                Assets.AddPieceToTable(p, table);
            }
            else
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/M3] Spade table: prefab '{prefabName}' not found in ZNetScene; " +
                    "it will be missing from the spade build menu. Check feature registration order in Registrar.");
            }
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
