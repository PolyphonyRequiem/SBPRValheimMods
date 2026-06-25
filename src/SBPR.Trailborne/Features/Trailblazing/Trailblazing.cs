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
    // And for Cairns — the v0.2.2 hotfix moves the four cairn pieces onto the spade
    // PieceTable instead of the hammer, so we need the Cairns TYPE for CairnName().
    using Cairns = SBPR.Trailborne.Features.Cairns.Cairns;
    // And for the Surveyor's Table (v2 cartography, t_2715661d) — a Spade-placed piece
    // like Signs/Cairns, so we need the type for SurveyorsTable.TableName.
    using SurveyorsTable = SBPR.Trailborne.Features.Cartography.SurveyorsTable;
    // And for the Bear Hide Tent (Trailside Camp) — a Spade-placed BF piece like the
    // Surveyor's Table, so we need the type for BearHideTent.TentName.
    using BearHideTent = SBPR.Trailborne.Features.Camp.BearHideTent;
    // And for MarkerSigns — the v2 marker pieces live on the spade 'Trail' tab too,
    // so we need the MarkerSigns TYPE for its prefab-name table.
    using MarkerSigns = SBPR.Trailborne.Features.MarkerSigns.MarkerSigns;

    /// <summary>
    /// M3 content: Trailblazer's Spade real path/replant behavior, plus the
    /// Spade ITEM itself (prefab + recipe) lifted out of the old fat Registrar
    /// so the spade and its terrain ops live in one vertical slice.
    ///
    /// ── ADDITIVE TerrainOp construction (ADR-0006; attempt #3, t_6fc9b3fa) ──
    /// The 3 path + 3 replant ops are built ADDITIVELY as modern
    /// <see cref="TerrainOp"/> pieces — `new GameObject()` + `AddComponent&lt;Piece&gt;` +
    /// `AddComponent&lt;TerrainOp&gt;`, NO ZNetView — mirroring exactly how vanilla's
    /// Hoe (`path_v2`) and Cultivator (`replant_v2`) ops are structured. We read the
    /// live `path_v2`/`replant_v2` as a BLUEPRINT (icon, place-effect, `_GhostOnly`
    /// preview marker) but never clone them. Each op sets ONLY
    /// `TerrainOp.m_settings.m_paintType` + `m_paintRadius`; level/smooth/raise stay at
    /// the Settings default `false`, so the PR #16 "no raise/level at any width" guard
    /// holds BY CONSTRUCTION. Widths scale only `m_paintRadius` (1.5/3/5m).
    ///
    /// 🔴 WHY this replaced the previous clone-a-legacy-donor design (the bug Daniel
    /// reported, 2026-06-10): Valheim ships TWO terrain-op generations sharing the same
    /// `PaintType` enum but applying paint differently —
    ///   • LEGACY `TerrainModifier` (`path`, `replant`): a PERSISTENT, ZNetView-bearing
    ///     networked piece. `OnPlaced` runs `RemoveOthers()`, a precedence battle that
    ///     only evicts SAME-paint ops — so a Reset(grass) op does NOT remove a Dirt(path)
    ///     op; the two stack on one tile and fight. THAT was the "grass fights path" bug.
    ///   • MODERN `TerrainOp` (`path_v2`, `replant_v2`): NO ZNetView; `Awake` bakes its
    ///     paint into the per-zone heightmap compiler (`TerrainComp`, which owns the
    ///     persistent terrain ZDO + networks it) then `Destroy(gameObject)`s itself —
    ///     fire-and-forget. Nothing persists to fight. This is the ENTIRE reason vanilla
    ///     Hoe-path ↔ Cultivator-grass coexist cleanly on one tile, last-applied-wins.
    /// Our spade used to clone the LEGACY donors, so BOTH our path and grass ops were
    /// persistent networked peers that battled. Migrating all six to additive `TerrainOp`
    /// gives them the vanilla fire-and-forget behavior (fixes AT-OP-1).
    ///
    /// 🔴 The ops are deliberately NOT registered in ZNetScene — vanilla's OWN
    /// `path_v2`/`replant_v2` aren't either (they exist only as PieceTable refs). They
    /// are held under the inactive PrefabHolder (see <see cref="ops"/>) and added to the
    /// spade's PieceTable BY REFERENCE, exactly as the Hoe/Cultivator hold theirs.
    /// Consequences (all three of the card's structural goals):
    ///   • No persistent op ZDO is ever written (AT-OP-2).
    ///   • A legacy orphan ZDO from the OLD persistent donors can't hang the client
    ///     (AT-OP-3): an UNregistered prefab makes `ZNetScene.CreateObject` early-return
    ///     null BEFORE it instantiates + emits "not used when creating", and the server
    ///     then drops the orphan ZDO itself. (Registering a self-destructing op was the
    ///     v0.2.14 hang — we structurally avoid it by not registering.) A belt-and-braces
    ///     server-only sweep (<see cref="LegacyTerrainOpZdoCleanup"/>) cleans them
    ///     deterministically + logs a verifiable count on top of vanilla's auto-clean.
    ///
    /// BUILD TIMING — ObjectDB phase, not ZNetScene.Awake: the `path_v2`/`replant_v2`
    /// blueprints live ONLY inside the vanilla Hoe/Cultivator build PieceTables, which
    /// are reachable once ObjectDB is populated (they are NOT in ZNetScene). So the ops
    /// are constructed in <see cref="DoObjectDBWiring"/> via
    /// <see cref="Assets.FindOpInToolPieceTable"/>, then wired into the spade table in the
    /// same pass. <see cref="RegisterPrefabs"/> (ZNetScene phase) now only registers the
    /// Spade ITEM.
    ///
    /// NB: `replant_v2` is the grass-restore op; `cultivate`/`cultivate_v2` is the
    /// soil-tiller (turns ground into farmland for crops) and is deliberately NOT used —
    /// the spade stays in the trail/exploration lane, not farming (see requirements.md
    /// "No Cultivate ability").
    ///
    /// All ops are Piece prefabs ADDED to the spade's PieceTable, so the player sees our
    /// 3 path-widths + 3 replant-widths. Spade-only piece table is built in DoObjectDBWiring.
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

        // Blueprint op names (READ as blueprints, NEVER cloned) — the vanilla MODERN
        // TerrainOp ops the Hoe/Cultivator actually use. Resolved from the tool PieceTables
        // at the ObjectDB phase (they are NOT in ZNetScene). Their settings are mirrored
        // explicitly in the additive build; the blueprint is read only for icon /
        // place-effect / _GhostOnly preview parity.
        private const string BlueprintPath    = "path_v2";
        private const string BlueprintReplant = "replant_v2";
        // The vanilla tools whose build PieceTables hold the live _v2 blueprint refs.
        private const string SourceHoeTool        = "Hoe";
        private const string SourceCultivatorTool = "Cultivator";

        private const string SourceHoe       = "Hoe";

        private const string IconFile        = "trailblazers_spade_v0.1.png";

        // Flat stamina drain per path/replant op, INDEPENDENT of radius
        // (1.5m / 3m / 5m all cost the same). See requirements.md A3.9
        // (Daniel, 2026-06-04 playtest lock). Terrain-op / build stamina is
        // driven by the WIELDING TOOL, not the op piece: Player.GetBuildStamina()
        // reads the right-hand ItemDrop's m_shared.m_attack.m_attackStamina.
        // Pieces / TerrainOp carry no stamina field, so this is the only
        // layer where the cost can be pinned — and pinning it here is
        // radius-independent by construction. (design/nomap.md §2.)
        private const float PathOpStamina    = 2f;

        // Per-op build spec. radius = m_settings.m_paintRadius override in metres
        // (1.5/3/5m UX); grassRestore = TRUE for the Cultivator-"Grass" replant ops.
        //
        // The op is built ADDITIVELY (Assets.ConstructTerrainOpPiece) — there is no
        // donor to clone. grassRestore selects:
        //   • the BLUEPRINT op to read (path_v2 vs replant_v2) for icon / place-effect /
        //     _GhostOnly preview, and which vanilla tool's PieceTable holds it;
        //   • the PaintType baked into m_settings (Dirt for path, Reset for grass —
        //     the decomp-confirmed values, identical between the legacy and modern
        //     generations: investigation §2);
        //   • Piece.m_vegetationGroundOnly (replant_v2 ships true — it reads the natural
        //     vegetation mask, which a Dirt path does NOT lower, so grass-on-path still
        //     places; path_v2 ships false).
        // NEITHER kind writes level/smooth/raise — that's the Settings default and the
        // PR #16 guard, true by construction for both (a path op's "width" is purely its
        // paint footprint here; we intentionally do NOT level/smooth terrain, matching
        // the prior slice's behavior and the spec's trail-not-earthworks intent).
        private static readonly Dictionary<string, (float radius, bool grassRestore)> variants =
            new Dictionary<string, (float, bool)>
            {
                { PathNarrowName,      (1.5f, false) },
                { PathStandardName,    (3.0f, false) },
                { PathWideName,        (5.0f, false) },
                { ReplantNarrowName,   (1.5f, true)  },
                { ReplantStandardName, (3.0f, true)  },
                { ReplantWideName,     (5.0f, true)  },
            };

        // The six additively-built op pieces, held alive under the inactive PrefabHolder
        // (Assets.ConstructTerrainOpPiece parents them there). They are deliberately NOT
        // registered in ZNetScene — exactly like vanilla path_v2/replant_v2, which live
        // only as PieceTable refs. Populated in DoObjectDBWiring (the only phase where the
        // _v2 blueprints are reachable) and added to the spade PieceTable by reference.
        private static readonly Dictionary<string, GameObject> ops =
            new Dictionary<string, GameObject>();

        // ───────────────────────────────────────────────
        // OP IDENTITY + EFFECT RADIUS (single source of truth for the placement-ripple patch)
        // ───────────────────────────────────────────────

        /// <summary>
        /// If <paramref name="prefabName"/> is one of our six registered spade terrain
        /// ops (the 3 path + 3 replant width variants), returns <c>true</c> and sets
        /// <paramref name="radius"/> to that op's effect radius in metres
        /// (1.5 / 3 / 5). Otherwise returns <c>false</c>.
        ///
        /// This is the gate AND the magnitude for the client-cosmetic placement-marker
        /// patch (<see cref="PlacementMarkerRadiusPatch"/>): the bool keeps the ripple
        /// scaling to OUR pieces (never a vanilla Hoe/Cultivator), and the radius is the
        /// value the ripple should preview.
        ///
        /// Why the radius is sourced from <see cref="variants"/> and NOT re-derived from
        /// the ghost's <c>TerrainOp</c> at runtime (which the original spike suggested via
        /// a "max of enabled op radii" mirror):
        ///   • <see cref="variants"/> is the SAME table that drives the additive build —
        ///     each op's <c>radius</c> here is written to its <c>TerrainOp.m_settings.m_paintRadius</c>
        ///     in <see cref="BuildOps"/>. So this IS the op's effect radius, read one step
        ///     closer to the source.
        ///   • A hand-mirrored "max of enabled radii" formula depends on the vanilla op
        ///     flags (m_level / m_smooth / m_paintCleared) AND on fields that differ by
        ///     game build; sourcing the intended width from the build table sidesteps all
        ///     of that and matches the acceptance test exactly (1.5 m op → ~1.5 m ripple,
        ///     5 m op → ~5 m ripple).
        ///
        /// Robust to Unity's instantiation suffix: a placement ghost GameObject is a
        /// clone, so its <c>.name</c> is e.g. "piece_sbpr_path_wide(Clone)". We strip
        /// from "(Clone" onward before matching the registered prefab name.
        /// </summary>
        public static bool TryGetSpadeOpRadius(string? prefabName, out float radius)
        {
            radius = 0f;
            if (string.IsNullOrEmpty(prefabName)) return false;
            int idx = prefabName!.IndexOf("(Clone", StringComparison.Ordinal);
            string name = (idx >= 0 ? prefabName.Substring(0, idx) : prefabName).Trim();
            if (variants.TryGetValue(name, out var v))
            {
                radius = v.radius;
                return true;
            }
            return false;
        }

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (called from ZNetScene.Awake postfix)
        // ───────────────────────────────────────────────

        /// <summary>
        /// ZNetScene-phase registration. Only the Spade ITEM is registered here — it is a
        /// ZNetView-bearing ItemDrop that belongs in ZNetScene. The 6 terrain ops are NOT
        /// built here: their `path_v2`/`replant_v2` blueprints live only in the vanilla
        /// tool PieceTables, which aren't populated until ObjectDB. The ops are built (and,
        /// like vanilla, NOT registered in ZNetScene) in <see cref="DoObjectDBWiring"/>.
        /// </summary>
        public static void RegisterPrefabs(ZNetScene zns)
        {
            RegisterSpadeItemPrefab(zns);
        }

        private static void RegisterSpadeItemPrefab(ZNetScene zns)
        {
            if (zns.GetPrefab(SpadeName) != null) return;
            if (!Assets.TryClonePrefab(SourceHoe, SpadeName, out var clone)) return;

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

        /// <summary>
        /// Build all six terrain ops ADDITIVELY (ADR-0006) during the ObjectDB phase and
        /// stash them in <see cref="ops"/>. Called from <see cref="DoObjectDBWiring"/> — NOT
        /// at ZNetScene.Awake — because the `path_v2`/`replant_v2` blueprints live only in
        /// the vanilla Hoe/Cultivator build PieceTables, reachable once ObjectDB exists.
        ///
        /// Each op is a `Piece` + `TerrainOp` with NO ZNetView, mirroring vanilla's own
        /// `path_v2`/`replant_v2`. It is held under the inactive PrefabHolder and is NOT
        /// registered in ZNetScene (vanilla's aren't either). Settings are set explicitly:
        ///   • PATH ops: PaintType.Dirt, paintRadius = width, vegetationGroundOnly=false.
        ///   • REPLANT ops: PaintType.Reset, paintRadius = width, vegetationGroundOnly=true.
        /// level/smooth/raise stay at the Settings default false for BOTH → flat ground
        /// stays flat at every width (PR #16 guard + AT-OP-4, by construction).
        ///
        /// 🔴 Behavior note vs the pre-0.2.16 clone: the old PATH op ALSO widened the
        /// legacy TerrainModifier's level/smooth radii (it leveled/smoothed terrain to
        /// width). The vanilla Hoe path op (`path_v2`) is PAINT-ONLY (m_level/m_smooth/m_raise
        /// all false) — so mirroring it makes our path op paint-only too. This is the
        /// correct vanilla-parity behavior and what AT-OP-4 asserts ("flat ground stays
        /// flat"); it's also what the spade's trail-not-earthworks intent wants. Flagged in
        /// the PR handoff for Daniel's in-game confirmation.
        ///
        /// Idempotent: re-entry (a second ObjectDB hook firing) rebuilds nothing if the
        /// dict is already populated with live objects.
        /// </summary>
        private static void BuildOps()
        {
            // Resolve the two blueprints once (read-only; never cloned). They may be null on
            // a degraded load — ConstructTerrainOpPiece copes (builds a working op with no
            // icon/ghost) and logs, so we still ship a functional tool.
            var pathBlueprint    = Assets.FindOpInToolPieceTable(SourceHoeTool, BlueprintPath);
            var replantBlueprint = Assets.FindOpInToolPieceTable(SourceCultivatorTool, BlueprintReplant);
            if (pathBlueprint == null)
                Plugin.Log.LogWarning(
                    $"[Trailborne/M3] Blueprint '{BlueprintPath}' not found in the {SourceHoeTool} PieceTable; " +
                    "path ops will build but with no icon / placement ghost.");
            if (replantBlueprint == null)
                Plugin.Log.LogWarning(
                    $"[Trailborne/M3] Blueprint '{BlueprintReplant}' not found in the {SourceCultivatorTool} PieceTable; " +
                    "replant ops will build but with no icon / placement ghost.");

            foreach (var kv in variants)
            {
                string name = kv.Key;
                float radius = kv.Value.radius;
                bool grassRestore = kv.Value.grassRestore;

                // Already built and still alive? leave it (idempotent across hook re-fires).
                if (ops.TryGetValue(name, out var existing) && existing != null)
                    continue;

                var blueprint = grassRestore ? replantBlueprint : pathBlueprint;
                var paintType = grassRestore
                    ? TerrainModifier.PaintType.Reset   // grass-restore (Cultivator "Grass")
                    : TerrainModifier.PaintType.Dirt;   // path
                string desc = grassRestore
                    ? $"Trailblazer grass-restore — regrows grass over a {radius:F1}m footprint. No terrain raise/level."
                    : $"Trailblazer path — paints a {radius:F1}m dirt path. No terrain raise/level.";

                var go = Assets.ConstructTerrainOpPiece(
                    name,
                    blueprint,
                    paintType,
                    radius,
                    vegetationGroundOnly: grassRestore,   // replant_v2=true, path_v2=false
                    NicePieceName(name),
                    desc);

                if (go == null)
                {
                    Plugin.Log.LogError(
                        $"[Trailborne/M3] FAILED to construct spade op '{name}' — it will be MISSING " +
                        "from the spade menu. This should not happen (additive build has no donor to miss).");
                    continue;
                }

                ops[name] = go;
                var opKind = grassRestore ? "grass-restore" : "path";
                Plugin.Log.LogInfo(
                    $"[Trailborne/M3] Built additive spade op: {name} ({opKind}, paint={paintType}, {radius:F1}m, no ZNetView).");
            }
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

            // Build the 6 terrain ops ADDITIVELY now — this is the only phase where the
            // path_v2/replant_v2 blueprints (inside the vanilla Hoe/Cultivator PieceTables)
            // are reachable. They are held in `ops`, NOT registered in ZNetScene.
            BuildOps();

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

            // Add the 6 ops to the spade table BY REFERENCE from our `ops` dict — NOT via
            // zns.GetPrefab (the ops are deliberately not in ZNetScene, exactly like the
            // vanilla path_v2/replant_v2 the Hoe/Cultivator hold the same way).
            foreach (var n in variants.Keys)
            {
                if (ops.TryGetValue(n, out var p) && p != null)
                    Assets.AddPieceToTable(p, table);
                else
                    Plugin.Log.LogWarning(
                        $"[Trailborne/M3] Spade table: op '{n}' was not built (see BuildOps warnings); " +
                        "it will be missing from the spade menu.");
            }

            // Explorer-placed signage + lighting + cairns belong on the Trailblazer's Spade,
            // NOT the Hammer — design pillar (design/design-pillars.md
            // lines 31-33: "Paths, signs, cairns, lamps… all live on the Spade").
            // Fixing code-vs-spec drift flagged by Daniel's 2026-06-05 playtest: the
            // Painted Sign + Path Lamp were wrongly wired onto the Hammer (already
            // corrected); Cairns slipped past that fix because they're owned by a
            // different feature module — corrected here in the v0.2.2 hotfix.
            //
            // RACE-SAFETY: Registrar runs ALL features' RegisterPrefabs (which register
            // the sign + lamp + cairn prefabs into ZNetScene) BEFORE any DoObjectDBWiring,
            // and dispatches Trailblazing AFTER Trailhead + Signs + Cairns, so all
            // prefabs resolve by name here. Every spade piece is PieceCategory.Misc so
            // it renders in this table's single 'Trail' tab — enforced by EnsureCategory
            // in AddSpadePieceByName, which screams + self-heals if any piece drifts off
            // Misc (cairns shipped as Crafting in v0.2.2 and silently vanished; fixed
            // 2026-06-07 by setting Cairn m_category = Misc + adding the guard).
            // (A separate 'Signage'/'Lights'/'Cairns' tab is a possible v1.x usability
            // tweak — flagged for Daniel; a single 'Trail' tab is acceptable for v1.)
            AddSpadePieceByName(zns, table, Signs.SignName);
            AddSpadePieceByName(zns, table, Trailhead.PathLampName);
            AddSpadePieceByName(zns, table, Cairns.CairnName("red"));
            AddSpadePieceByName(zns, table, Cairns.CairnName("white"));
            AddSpadePieceByName(zns, table, Cairns.CairnName("blue"));
            AddSpadePieceByName(zns, table, Cairns.CairnName("black"));
            // v2 cartography (t_2715661d): the Surveyor's Table is Spade-placed, never the
            // Hammer (design Pillar 1). PieceCategory.Misc like every spade piece, so it
            // renders in the single 'Trail' tab (EnsureCategory guards drift).
            AddSpadePieceByName(zns, table, SurveyorsTable.TableName);
            // Trailside Camp — the Bear Hide Tent on the same 'Trail' tab (Pillar 1: Spade,
            // never Hammer). Prefab registered in the earlier RegisterPrefabs pass.
            AddSpadePieceByName(zns, table, BearHideTent.TentName);
            // v2 Marker Signs — four additive marker pieces on the same 'Trail' tab
            // (Pillar 1: Spade, never Hammer). Prefabs were registered in the earlier
            // RegisterPrefabs pass, so they resolve by name here (same guarantee the
            // Sign/Cairn wiring above relies on).
            foreach (var mk in MarkerSigns.MarkerTypes)
                AddSpadePieceByName(zns, table, mk.PrefabName);

            drop.m_itemData.m_shared.m_buildPieces = table;
            // Derive the count + name list from the live table rather than a hardcoded
            // literal, so the log can't drift the moment a piece is added/removed/misrouted
            // (the "log string lies about runtime state" trap — valheim-mod-development skill).
            var pieceNames = new List<string>();
            foreach (var pc in table.m_pieces)
                if (pc != null) pieceNames.Add(pc.name);
            Plugin.Log.LogInfo(
                $"[Trailborne/M3] Spade-only PieceTable built with {table.m_pieces.Count} pieces: " +
                string.Join(", ", pieceNames) + ".");
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
                EnsureCategory(table, p);   // category-routing guard — see below
                Assets.AddPieceToTable(p, table);
            }
            else
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/M3] Spade table: prefab '{prefabName}' not found in ZNetScene; " +
                    "it will be missing from the spade build menu. Check feature registration order in Registrar.");
            }
        }

        /// <summary>
        /// Category-routing backstop. A from-scratch PieceTable only RENDERS the
        /// piece categories it explicitly declares in m_categories — a piece whose
        /// m_category isn't on that list is added to m_pieces yet its tab never
        /// renders, so it's INVISIBLE in the build menu (no error, no warning).
        /// This is exactly how v0.2.2's four cairns vanished: they were
        /// PieceCategory.Crafting while the spade table declared only Misc.
        ///
        /// The locked design is a SINGLE "Trail" tab, so every spade piece SHOULD
        /// be Misc and this method is normally a no-op. But rather than trust that
        /// invariant silently (the trust that already failed once), we make the
        /// drift LOUD and self-healing: if a piece arrives with an undeclared
        /// category we log an ERROR (so SpecCheck-style boot logs surface it) AND
        /// append the category + an index-aligned label, so the piece still renders
        /// instead of disappearing. A screaming, visible piece beats a silent,
        /// missing one.
        /// </summary>
        private static void EnsureCategory(PieceTable table, GameObject piecePrefab)
        {
            var piece = piecePrefab.GetComponent<Piece>();
            if (piece == null) return;
            var cat = piece.m_category;
            if (table.m_categories.Contains(cat)) return;

            Plugin.Log.LogError(
                $"[Trailborne/M3] Spade-table category drift: piece '{piecePrefab.name}' has category " +
                $"{cat} which the spade PieceTable does not declare. The locked design is a single " +
                $"'Trail' (Misc) tab — this piece should be PieceCategory.Misc. Appending the category " +
                $"as a self-heal so it still renders, but FIX THE PIECE'S m_category (see Cairns.cs " +
                $"2026-06-07 fix) rather than relying on this backstop.");

            table.m_categories.Add(cat);
            // Keep m_categoryLabels index-aligned with m_categories or the tab header
            // lookup throws / mislabels.
            while (table.m_categoryLabels.Count < table.m_categories.Count)
                table.m_categoryLabels.Add(cat.ToString());
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
