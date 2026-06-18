// ============================================================================
//  Trailborne v3 (Swamp) — Sunstone Loot Economy (dual-source: chests + elite)
// ----------------------------------------------------------------------------
//  Impl spec: docs/v3/planning/sunstone-loot-economy-impl-spec.md
//  Design   : docs/design/swamp-detection-item.md §Sourcing (PR #144)
//  Card     : t_0445f590  (parent loot-economy card t_1fcbb780)
//
//  Wires the Sunstone material (SBPR_Sunstone, shipped by card t_2fd7bc7f) into a
//  real loot economy so it is FOUND by exploring the Swamp, not only crafted:
//
//    • PRIMARY  — swamp SURFACE loot chests. Inject a Sunstone DropData into the
//      vanilla TreasureChest_swamp Container.m_defaultItems DropTable at a weight
//      solved to ~15% PER-CHEST (Daniel's lock, 2026-06-18). EXCLUDE the Sunken
//      Crypt chest (TreasureChest_sunkencrypt) — the gated-dungeon loop we steer
//      away from. There is only ONE swamp-surface chest prefab: the named POIs
//      (Draugr Village, Ruined Tower, Abandoned House/Village, Viking Graveyard,
//      swamp Shipwreck) all REUSE TreasureChest_swamp (RE t_5e9a4d49, verified vs
//      the real client asset), so one injection covers every surface chest.
//
//    • SECONDARY — rare Draugr Elite combat drop. Inject a Sunstone Drop into the
//      vanilla Draugr_Elite CharacterDrop at m_chance = 0.05 (5%, Daniel's lock).
//      Secondary because the canonical elite farm is Body Piles inside crypts;
//      keeping chests primary preserves the surface-exploration intent.
//
//  ── WHY THE CHEST NUMBER IS A WEIGHT, NOT A PERCENT ──────────────────────────
//  vanilla DropTable is WEIGHT-based, sampled WITHOUT replacement (m_oneOfEach),
//  drawing m_dropMin..m_dropMax items (DropTable.GetDropListItems, decomp :56456).
//  The live TreasureChest_swamp table (read from the real client asset, RE
//  t_5e9a4d49 + this card's re-probe) is:
//      m_dropChance = 1.0, m_dropMin = 2, m_dropMax = 3, m_oneOfEach = true
//      10 entries, total weight 9.5: 9 items at weight 1.0 + WitheredBone at 0.5.
//  So "15%" cannot be a raw weight or a slot fraction — it is the probability that
//  a freshly-populated chest CONTAINS at least one Sunstone. Solving that against
//  the real sampling algorithm (exact recursion + a faithful Monte-Carlo of
//  GetDropListItems, both agree) gives weight ≈ 0.584 → 15.00% per chest. That
//  lands Sunstone at a ~5.8% slot fraction — right at the WitheredBone rare-tail
//  (5.3%) the design doc named as precedent. The solver is documented in the impl
//  spec; ChestSunstoneWeight is the const it produced.
//
//  ── ADDITIVE, NOT CLONING (ADR-0006) ─────────────────────────────────────────
//  We do NOT clone or rebuild the vanilla chest / Draugr prefabs. We read each as
//  a live blueprint via ZNetScene.GetPrefab (fires no Awake) and APPEND one entry
//  to a list it already owns (Container.m_defaultItems.m_drops / CharacterDrop
//  .m_drops). Appending to a vanilla List<T> is a field edit, not a subtractive
//  clone — the exact surface the parent RE cards scoped. Idempotent: every inject
//  checks for an existing Sunstone entry first, so the ZNetScene.Awake postfix
//  re-firing across scene loads never double-adds.
//
//  ── TIMING ───────────────────────────────────────────────────────────────────
//  Runs at the ZNetScene phase (RegisterPrefabs), AFTER SunstoneLens.RegisterPrefabs
//  so SBPR_Sunstone is already a registered ZNetScene prefab. The DropData/Drop
//  reference the Sunstone GAMEOBJECT (the ZNetScene prefab), not the ODB ItemData —
//  vanilla's AddItemToList does data.m_item.GetComponent<ItemDrop>() at populate
//  time, and the Sunstone GameObject carries an ItemDrop (cloned from Coins). No
//  ObjectDB phase work is needed for loot wiring.
//
//  ── KNOWN, VANILLA-CONSISTENT LIMITATION (for QA / Daniel) ────────────────────
//  Container.Awake populates a chest ONCE, owner-side, gated by the ZDO flag
//  s_addedDefaultItems (decomp :101784). Chests already generated+populated in an
//  existing world keep their old contents; Sunstone appears in chests populated
//  AFTER this build loads. QA should sample freshly-discovered swamp chests (or a
//  fresh world). This matches how every vanilla loot-table change behaves.
//
//  All gated behind ServerContext.OnSBServer via the Registrar. Clean-side
//  (ADR-0001): reads/edits base-game prefabs only; no third-party mod code.
//  logs-green ≠ playable — Daniel verifies AT-SUNSTONE-LOOT-* in-game.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Sunstone
{
    /// <summary>
    /// Injects the Sunstone material into the vanilla swamp-surface loot economy:
    /// the TreasureChest_swamp DropTable (primary) and the Draugr_Elite CharacterDrop
    /// (secondary). See the file header for the weight-solve rationale and ADR notes.
    /// </summary>
    public static class SunstoneLoot
    {
        // ── LOCKED vanilla prefab names (verified vs the real client asset, RE
        //    t_5e9a4d49 / t_eb405e48). Do NOT guess-case these — Draugr_Elite is
        //    capital E (Draugr_elite lowercase is a bare mesh node, not the creature).
        public const string SwampChestPrefab = "TreasureChest_swamp";   // the ONE swamp-surface chest
        public const string DraugrElitePrefab = "Draugr_Elite";          // the elite creature root
        // The Sunken Crypt chest we EXCLUDE (named for self-documentation only; we
        // never touch it — listed so a future reader sees the exclusion is deliberate).
        public const string ExcludedCryptChestPrefab = "TreasureChest_sunkencrypt";

        // ── Drop tuning (Daniel's lock, 2026-06-18: 15% chest / 5% elite). ──
        // Chest: weight solved so per-chest P(≥1 Sunstone) = 0.15 against the live
        // TreasureChest_swamp table (9×1.0 + WitheredBone 0.5, total 9.5; 2-3 draws,
        // one-of-each). See impl spec §3 for the solver; w=0.584 → 15.00%.
        public const float ChestSunstoneWeight = 0.584f;
        public const int ChestSunstoneStackMin = 1;
        public const int ChestSunstoneStackMax = 1;

        // Elite: a flat 5% per-kill roll, independent of star level (m_levelMultiplier
        // = false), mirroring how vanilla authors the Draugr_Elite trophy (chance 0.10,
        // lvlMult 0). m_chance is 0..1 (CharacterDrop.Drop, decomp :11321).
        public const float EliteSunstoneChance = 0.05f;
        public const int EliteSunstoneAmountMin = 1;
        public const int EliteSunstoneAmountMax = 1;

        // ───────────────────────────────────────────────
        // ENTRY POINT (ZNetScene.Awake postfix, via Registrar — after SunstoneLens)
        // ───────────────────────────────────────────────

        /// <summary>
        /// Inject Sunstone into the swamp chest DropTable + the Draugr Elite CharacterDrop.
        /// Idempotent and null-safe: skips silently if a surface is missing or already wired.
        /// </summary>
        public static void RegisterPrefabs(ZNetScene zns)
        {
            if (zns == null) return;

            var sunstone = zns.GetPrefab(SunstoneLens.SunstoneName);
            if (sunstone == null)
            {
                // SunstoneLens.RegisterPrefabs runs before us in the Registrar order, so this
                // should never fire; if it does, the material isn't registered yet and we'd
                // inject a null prefab into vanilla loot (a populate-time NRE). Skip loudly.
                Plugin.Log.LogWarning(
                    $"[Trailborne/SunstoneLoot] {SunstoneLens.SunstoneName} not in ZNetScene yet; "
                    + "skipping loot wiring this pass (ordering bug if persistent).");
                return;
            }
            if (sunstone.GetComponent<ItemDrop>() == null)
            {
                // vanilla AddItemToList does m_item.GetComponent<ItemDrop>() at populate time;
                // a Sunstone without an ItemDrop would NRE every chest open. Guard it.
                Plugin.Log.LogError(
                    $"[Trailborne/SunstoneLoot] {SunstoneLens.SunstoneName} has no ItemDrop component; "
                    + "refusing to add it to loot tables (would NRE at populate time).");
                return;
            }

            InjectChestDrop(zns, sunstone);
            InjectEliteDrop(zns, sunstone);
        }

        // ── PRIMARY: swamp surface chest DropTable ───────────────────────────────
        private static void InjectChestDrop(ZNetScene zns, GameObject sunstone)
        {
            var chest = zns.GetPrefab(SwampChestPrefab);
            if (chest == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/SunstoneLoot] {SwampChestPrefab} not in ZNetScene; skipping chest drop.");
                return;
            }

            var container = chest.GetComponent<Container>();
            if (container == null || container.m_defaultItems == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/SunstoneLoot] {SwampChestPrefab} has no Container/m_defaultItems; skipping chest drop.");
                return;
            }

            var drops = container.m_defaultItems.m_drops;
            if (drops == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/SunstoneLoot] {SwampChestPrefab} DropTable.m_drops is null; skipping chest drop.");
                return;
            }

            // Idempotent: bail if Sunstone is already in this table (postfix re-fire).
            if (ContainsItem(drops, sunstone))
            {
                Plugin.Log.LogInfo(
                    $"[Trailborne/SunstoneLoot] {SwampChestPrefab} already carries {SunstoneLens.SunstoneName}; chest drop is idempotent, skipping.");
                return;
            }

            drops.Add(new DropTable.DropData
            {
                m_item = sunstone,
                m_stackMin = ChestSunstoneStackMin,
                m_stackMax = ChestSunstoneStackMax,
                m_weight = ChestSunstoneWeight,
                // A single sun-finding crystal — do NOT scale by Game.m_resourceRate (a
                // higher-loot server should not multiply a treasure into a stack). Vanilla
                // gems/sledge in this table are dontScale=false, but they are commodity loot;
                // Sunstone is a rare single-shard treasure, so we pin its count.
                m_dontScale = true,
            });

            Plugin.Log.LogInfo(
                $"[Trailborne/SunstoneLoot] Added {SunstoneLens.SunstoneName} to {SwampChestPrefab} DropTable "
                + $"(weight {ChestSunstoneWeight} → ~15% per chest). Sunken Crypt table untouched.");
        }

        // ── SECONDARY: Draugr Elite CharacterDrop ────────────────────────────────
        private static void InjectEliteDrop(ZNetScene zns, GameObject sunstone)
        {
            var elite = zns.GetPrefab(DraugrElitePrefab);
            if (elite == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/SunstoneLoot] {DraugrElitePrefab} not in ZNetScene; skipping elite drop.");
                return;
            }

            var charDrop = elite.GetComponent<CharacterDrop>();
            if (charDrop == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/SunstoneLoot] {DraugrElitePrefab} has no CharacterDrop; skipping elite drop.");
                return;
            }
            if (charDrop.m_drops == null)
                charDrop.m_drops = new List<CharacterDrop.Drop>();

            // Idempotent: bail if Sunstone is already a drop (postfix re-fire).
            if (ContainsEliteDrop(charDrop.m_drops, sunstone))
            {
                Plugin.Log.LogInfo(
                    $"[Trailborne/SunstoneLoot] {DraugrElitePrefab} already drops {SunstoneLens.SunstoneName}; elite drop is idempotent, skipping.");
                return;
            }

            charDrop.m_drops.Add(new CharacterDrop.Drop
            {
                m_prefab = sunstone,
                m_amountMin = EliteSunstoneAmountMin,
                m_amountMax = EliteSunstoneAmountMax,
                m_chance = EliteSunstoneChance,
                // Flat 5% regardless of star (RE t_eb405e48 recommendation, Daniel-confirmed
                // 15%/5% lock): a 2★ elite is not a better Sunstone source than a 0★.
                m_levelMultiplier = false,
                m_onePerPlayer = false,
                m_dontScale = true,   // one crystal per roll; do not resource-rate scale.
            });

            Plugin.Log.LogInfo(
                $"[Trailborne/SunstoneLoot] Added {SunstoneLens.SunstoneName} to {DraugrElitePrefab} CharacterDrop "
                + $"(chance {EliteSunstoneChance} = 5%, flat across stars).");
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        /// <summary>True if <paramref name="drops"/> already contains a DropData for the
        /// given prefab (reference identity — the same registered GameObject).</summary>
        private static bool ContainsItem(List<DropTable.DropData> drops, GameObject prefab)
        {
            foreach (var d in drops)
                if (d.m_item == prefab) return true;
            return false;
        }

        /// <summary>True if <paramref name="drops"/> already contains a Drop for the given prefab.</summary>
        private static bool ContainsEliteDrop(List<CharacterDrop.Drop> drops, GameObject prefab)
        {
            foreach (var d in drops)
                if (d.m_prefab == prefab) return true;
            return false;
        }
    }
}
