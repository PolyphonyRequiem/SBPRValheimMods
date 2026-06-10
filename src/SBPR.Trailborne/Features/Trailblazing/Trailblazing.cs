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

    /// <summary>
    /// M3 content: Trailblazer's Spade real path/replant behavior, plus the
    /// Spade ITEM itself (prefab + recipe) lifted out of the old fat Registrar
    /// so the spade and its terrain ops live in one vertical slice.
    ///
    /// Clones the vanilla `path` prefab three times and scales each clone's
    /// TerrainModifier radii to our 3 widths (narrow/standard/wide =
    /// 1.5m / 3m / 5m). ALSO clones the vanilla `replant_v2` op — the
    /// Cultivator's "Grass" mode that regrows grass on dirt — THREE times at the
    /// SAME 1.5/3/5m widths, but scaling ONLY the grass/paint footprint
    /// (m_settings.m_paintRadius). The replant clones leave
    /// m_settings.m_level / m_smooth / m_raise at their stock `false`, so every
    /// replant width stays a pure grass-restore brush — no terrain raise/level/
    /// smooth at ANY width. The player scrolls through Path ×3 + Replant Grass ×3
    /// (Daniel playtest call 2026-06-05: 3 replant widths mirroring the 3 path
    /// widths).
    ///
    /// DONOR SYSTEM (t_1d6fa833 fix, investigation
    /// docs/investigations/2026-06-09-replant-grass-fights-path-terrainop-vs-terrainmodifier.md):
    /// Valheim ships TWO parallel terrain-op systems that share the same
    /// `TerrainModifier.PaintType` enum but apply paint by different mechanisms:
    ///   • LEGACY `TerrainModifier` (prefabs `replant`, `path`, `cultivate`): a
    ///     persistent ZNetView-bearing networked piece. `OnPlaced` runs
    ///     `RemoveOthers()`, a precedence battle — a Reset-paint op (grass) only
    ///     evicts OTHER Reset ops, so it does NOT remove our `path` (Dirt) op;
    ///     the two stack and fight. That was the "grass fights path" bug.
    ///   • MODERN `TerrainOp` (prefabs `replant_v2`, `path_v2`): no ZNetView; on
    ///     Awake it bakes its paint straight into the heightmap compiler then
    ///     destroys its own GameObject. Nothing persists to fight.
    /// The vanilla Cultivator's grass op is `replant_v2` (a `TerrainOp`) — that's
    /// why it cleanly regrasses our path. We clone `replant_v2` to inherit the
    /// same compiler-applied, non-conflicting behavior. The PATH ops are still
    /// the legacy `path` donor (Daniel: path tool "works fine") — migrating them
    /// to `_v2` is a tracked follow-up, deliberately out of scope here.
    ///
    /// REGISTRATION TIMING — TWO PHASES (t_d84d2c17 fix):
    /// The two donor systems live in DIFFERENT places at runtime, which forces a
    /// two-phase registration:
    ///   • PATH donor `path` (a TerrainModifier) is ZNetView-bearing, so it sits in
    ///     `ZNetScene.m_prefabs` and `ZNetScene.GetPrefab("path")` resolves at our
    ///     `ZNetScene.Awake` postfix. Path variants register THERE (RegisterPrefabs).
    ///   • REPLANT donor `replant_v2` (a TerrainOp) has NO ZNetView, so it is in
    ///     NEITHER of ZNetScene's serialized lists (m_prefabs / m_nonNetViewPrefabs)
    ///     — `ZNetScene.GetPrefab("replant_v2")` is null at EVERY ZNetScene phase, on
    ///     the dedicated server AND a client. (v0.2.12 regressed exactly here: the
    ///     boot logged `replant_v2 not in ZNetScene` 3× and the spade menu dropped
    ///     12→9, the grass tool vanished — rolled back in v0.2.13.) The only live
    ///     `replant_v2` reference is baked into the vanilla Cultivator's build
    ///     PieceTable, which is reachable once ObjectDB is populated. So replant
    ///     variants register in the ObjectDB-wiring phase (DoObjectDBWiring →
    ///     RegisterReplantVariantsFromCultivator), resolving the donor via
    ///     `Assets.FindOpInToolPieceTable("Cultivator", "replant_v2")`. This mirrors
    ///     the proven deferred-after-ODB pattern Cairns uses for marker-dependent
    ///     resources. If the donor still can't be resolved, the piece FAILS LOUD at
    ///     ERROR rather than silently vanishing (AT-RV2-4).
    ///
    /// NB: `replant_v2` is the grass-restore op; `cultivate`/`cultivate_v2` is the
    /// soil-tiller (turns ground into farmland for crops) and is deliberately NOT
    /// used — the spade stays in the trail/exploration lane, not farming (see
    /// requirements.md "No Cultivate ability"). Cloning `cultivate` at a forced
    /// 5m radius was the "UBER level" bug PR #16 fixed; the 3-width replant here
    /// preserves that fix by construction — `replant_v2` ships
    /// level=0/smooth=0/raise=0 and we write ONLY m_settings.m_paintRadius (see
    /// the grassRestore branch in RegisterRadiusVariant).
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

        // PATH donor: the LEGACY `path` (a TerrainModifier, ZNetView-bearing). It IS in
        // ZNetScene.m_namedPrefabs at our ZNetScene.Awake hook, so path variants clone it
        // there (unchanged).
        private const string SourcePath      = "path";
        // REPLANT donor: the MODERN `replant_v2` (a TerrainOp, NO ZNetView) — the op the
        // vanilla Cultivator actually uses for its "Grass" mode. Cloning it makes our
        // Replant-Grass bake into the heightmap compiler (fire-and-forget, no persistent
        // networked modifier) instead of fighting the path op. See the class doc +
        // investigation doc (t_1d6fa833 / t_d84d2c17).
        //
        // CRITICAL TIMING (t_d84d2c17): unlike the legacy `replant`, `replant_v2` is in
        // NEITHER of ZNetScene's serialized prefab lists (m_prefabs / m_nonNetViewPrefabs),
        // so ZNetScene.GetPrefab("replant_v2") is null at EVERY ZNetScene phase on a
        // dedicated server (v0.2.12 boot logged it null 3×, dropping the spade menu 12→9).
        // We therefore resolve it from the vanilla Cultivator's build PieceTable during the
        // ObjectDB-wiring phase (Assets.FindOpInToolPieceTable), where the live reference
        // exists. Identical paint settings to legacy `replant` (Reset, paintR 2.2),
        // different APPLICATION system.
        private const string SourceReplant   = "replant_v2";
        // The vanilla tool whose build PieceTable holds the live `replant_v2` reference.
        private const string SourceCultivator = "Cultivator";
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

        // source = vanilla prefab to clone; radius = paint-radius OVERRIDE in
        // metres; grassRestore = TRUE for the Cultivator-"Grass" replant ops.
        //
        // The radius is applied to a DIFFERENT component+field depending on
        // grassRestore, because the two op kinds clone different donor *systems*
        // (t_1d6fa833 fix):
        //   • grassRestore=TRUE  → donor `replant_v2` is a TerrainOp. Scale ONLY
        //     `TerrainOp.m_settings.m_paintRadius` (the grass/paint footprint);
        //     m_settings.m_level / m_smooth / m_raise stay at stock false, so the
        //     op NEVER raises/levels/smooths at any width (PR #16 guard, by
        //     construction).
        //   • grassRestore=FALSE → donor `path` is a legacy TerrainModifier. Scale
        //     all three radii together (paint + level + smooth) — a path tile's job
        //     IS to paint + level to width.
        //
        // grassRestore ALSO selects the REGISTRATION PHASE (t_d84d2c17 fix), because
        // the two donor systems are resolvable at different times:
        //   • grassRestore=FALSE (path) → donor is in ZNetScene at our ZNetScene.Awake
        //     hook → registered in RegisterPrefabs (ZNetScene phase).
        //   • grassRestore=TRUE (replant) → donor `replant_v2` is NOT in ZNetScene at
        //     all (no ZNetView), only in the Cultivator's PieceTable which arrives with
        //     ObjectDB → registered in DoObjectDBWiring (ObjectDB phase).
        //
        // Path widths and Replant widths BOTH get our 1.5/3/5m UX; that branch is
        // the whole safety story of this slice (see RegisterRadiusVariant).
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
        /// the ghost's <c>TerrainModifier</c> at runtime (which the original spike
        /// suggested via a "max of enabled op radii" mirror):
        ///   • <see cref="variants"/> is the SAME table that drives registration — each
        ///     op's <c>radius</c> here is applied to its TerrainModifier's paint (and,
        ///     for paths, level/smooth) radius in <see cref="RegisterRadiusVariant"/>.
        ///     So this IS the op's effect radius, read one step closer to the source.
        ///   • A hand-mirrored "max of enabled radii" formula depends on the vanilla op
        ///     flags (m_level / m_smooth / m_paintCleared) AND on fields that differ by
        ///     game build — the metadata probe against this build's assembly_valheim.dll
        ///     confirmed m_raise / m_raiseRadius (listed in the spike) do NOT exist here,
        ///     and for a narrow replant op the vanilla level/smooth radii (~2 m) would
        ///     overshoot our 1.5 m paint footprint. Sourcing the intended width from the
        ///     registration table sidesteps all of that and matches the acceptance test
        ///     exactly (1.5 m op → ~1.5 m ripple, 5 m op → ~5 m ripple).
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

        public static void RegisterPrefabs(ZNetScene zns)
        {
            RegisterSpadeItemPrefab(zns);
            // PHASE 1 (ZNetScene): only the PATH variants. Their donor `path` is a
            // ZNetView-bearing TerrainModifier present in ZNetScene at this hook.
            // The REPLANT variants are deferred to the ObjectDB phase — their donor
            // `replant_v2` (a TerrainOp, no ZNetView) is not in ZNetScene at all;
            // see RegisterReplantVariantsFromCultivator + the class doc timing note.
            foreach (var kv in variants)
            {
                if (kv.Value.grassRestore) continue;   // replant → ObjectDB phase
                RegisterRadiusVariant(zns, kv.Key, kv.Value.source, kv.Value.radius, kv.Value.grassRestore);
            }
        }

        // ───────────────────────────────────────────────
        // REPLANT REGISTRATION (PHASE 2 — called from DoObjectDBWiring)
        //
        // The modern `replant_v2` TerrainOp donor is NOT in ZNetScene's prefab lists
        // on any platform (it has no ZNetView), so we resolve it from the vanilla
        // Cultivator's build PieceTable — available once ObjectDB is populated — and
        // register the three grass-restore width variants here. This is the t_d84d2c17
        // fix for the v0.2.12 dedicated-server regression where ZNetScene.GetPrefab
        // returned null and all three replant pieces silently vanished.
        // ───────────────────────────────────────────────

        public static void RegisterReplantVariantsFromCultivator(ZNetScene zns)
        {
            // Resolve the donor ONCE from the Cultivator's PieceTable.
            var donor = Assets.FindOpInToolPieceTable(SourceCultivator, SourceReplant);
            if (donor == null)
            {
                // FAIL LOUD (AT-RV2-4): a shipped feature that silently doesn't register
                // is a bug. The previous regression was diagnosable only because the
                // ClonePrefab path happened to log; make this path scream unmistakably.
                Plugin.Log.LogError(
                    $"[Trailborne/M3] Replant-Grass donor '{SourceReplant}' NOT FOUND in the " +
                    $"vanilla '{SourceCultivator}' build PieceTable (ObjectDB phase). All three " +
                    "grass-restore widths will be ABSENT from the spade menu. This is the " +
                    "t_d84d2c17 failure mode — the modern TerrainOp donor could not be resolved. " +
                    "Check: is the Cultivator item in ObjectDB, and does its m_buildPieces table " +
                    "still contain 'replant_v2' on this game build?");
                return;
            }

            foreach (var kv in variants)
            {
                if (!kv.Value.grassRestore) continue;   // path handled in ZNetScene phase
                RegisterReplantVariant(zns, kv.Key, donor, kv.Value.radius);
            }
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

        // PATH variant registration (PHASE 1, ZNetScene). Path ops clone the legacy
        // `path` TerrainModifier and scale ALL THREE radii (level + smooth + paint)
        // together — a path tile is SUPPOSED to flatten + paint to the chosen width.
        // grassRestore is always FALSE here (replant variants go through
        // RegisterReplantVariant in the ObjectDB phase); it's kept in the signature so
        // the variants-table tuple maps 1:1 and a future caller can't silently route a
        // grass op down the path branch.
        private static void RegisterRadiusVariant(ZNetScene zns, string name, string source, float radius, bool grassRestore)
        {
            if (zns.GetPrefab(name) != null) return;
            if (grassRestore)
            {
                // Defensive: replant ops MUST use RegisterReplantVariant (different donor
                // system + resolution path). Reaching here means a caller mis-routed one.
                Plugin.Log.LogError(
                    $"[Trailborne/M3] RegisterRadiusVariant called for grass-restore op '{name}' — " +
                    "replant variants must register via RegisterReplantVariantsFromCultivator " +
                    "(ObjectDB phase). Skipping to avoid cloning the wrong donor system.");
                return;
            }
            var clone = Assets.ClonePrefab(source, name);
            if (clone == null)
            {
                Plugin.Log.LogWarning($"[Trailborne/M3] Source '{source}' missing; skipping {name}");
                return;
            }
            // PATH ops: scale all three TerrainModifier radii together (level + smooth +
            // paint). A path tile is SUPPOSED to flatten and paint to the chosen width.
            var mod = clone.GetComponentInChildren<TerrainModifier>(includeInactive: true);
            if (mod != null)
            {
                mod.m_paintRadius  = radius;
                mod.m_levelRadius  = radius;
                mod.m_smoothRadius = radius;
            }
            else
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/M3] Path op '{name}': cloned donor '{source}' has no " +
                    "TerrainModifier; radii NOT scaled. Check the donor prefab.");
            }
            FinalizePieceAndRegister(clone, name, radius, grassRestore: false);
        }

        // REPLANT variant registration (PHASE 2, ObjectDB). The donor is the modern
        // `replant_v2` TerrainOp template resolved from the Cultivator's PieceTable (it
        // is NOT in ZNetScene — see RegisterReplantVariantsFromCultivator). We clone the
        // template (NOT a ZNetScene name) under the inactive holder so the TerrainOp's
        // self-destructing Awake never fires, then scale ONLY the paint footprint.
        private static void RegisterReplantVariant(ZNetScene zns, string name, GameObject donorTemplate, float radius)
        {
            if (zns.GetPrefab(name) != null) return;
            var clone = Assets.ClonePrefabTemplate(donorTemplate, name);
            if (clone == null)
            {
                Plugin.Log.LogError($"[Trailborne/M3] Replant op '{name}': failed to clone donor template; skipping.");
                return;
            }
            // REPLANT ops: scale ONLY TerrainOp.m_settings.m_paintRadius — the grass/
            // vegetation footprint. m_settings.m_level / m_smooth / m_raise ship `false`
            // on `replant_v2` and are LEFT UNTOUCHED, so the op NEVER raises, levels, or
            // smooths terrain at ANY width. This is the PR #16 ("UBER level" bug)
            // regression guard, satisfied BY CONSTRUCTION (AT-RV2-3): the terrain-shaping
            // flags are never enabled, so widening the paint footprint cannot reintroduce
            // terrain modification.
            var op = clone.GetComponentInChildren<TerrainOp>(includeInactive: true);
            if (op != null && op.m_settings != null)
            {
                op.m_settings.m_paintRadius = radius;
            }
            else
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/M3] Replant op '{name}': cloned donor '{SourceReplant}' has no " +
                    "TerrainOp (or null m_settings); paint radius NOT scaled. The grass op will use " +
                    "the donor's stock footprint. Check the donor is the modern '_v2' TerrainOp.");
            }
            FinalizePieceAndRegister(clone, name, radius, grassRestore: true);
        }

        // Shared piece-field finalization + ZNetScene registration for both op kinds.
        // Keeps the Piece name/description/category/cost identical regardless of which
        // donor system produced the clone.
        private static void FinalizePieceAndRegister(GameObject clone, string name, float radius, bool grassRestore)
        {
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

            // PHASE 2 (ObjectDB): register the three REPLANT grass-restore variants NOW,
            // before the spade PieceTable is assembled below. Their donor `replant_v2`
            // (modern TerrainOp, no ZNetView) is not in ZNetScene at any phase — it lives
            // only in the vanilla Cultivator's build PieceTable, which is reachable now
            // that ObjectDB is populated. This is the t_d84d2c17 fix: doing this here
            // (instead of at ZNetScene.Awake, where GetPrefab("replant_v2") returns null
            // on a dedicated server) is what makes all three widths actually register, so
            // the loop below finds 6 ops, not 3. Idempotent across re-joins (each variant
            // early-outs if already in ZNetScene). zns is non-null here (we just resolved
            // the spade off it).
            RegisterReplantVariantsFromCultivator(zns!);

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
