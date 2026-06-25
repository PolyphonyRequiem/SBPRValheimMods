// ============================================================================
//  Trailborne v4 (Mountains) — Seer's Stone (Utility-slot wisp-lens accessory)
// ----------------------------------------------------------------------------
//  Design   : docs/design/seers-stone.md + docs/design/trailborne-vision.md (the
//             Explorer's signature item) + the #design "Seer's Stone" thread.
//  Decisions: Daniel, 2026-06-25 — Mountains tier, crystal-gated, sole v4 headline.
//             A worn lens that makes "wisps" drift over eligible forage/ore/POIs
//             (the SeersStoneWhitelist substrate, M1); look at a wisp + Alt+E to pin
//             it (M4, separate file). This file is the ITEM (M2): the equippable
//             accessory + its crystal recipe + worn-detection.
//
//  ── SIBLING OF THE CARTOGRAPHER'S KIT (extend, don't duplicate) ──────────────
//  Architecturally this IS CartographersKit with a different gate + effect:
//    • Same slot   : Utility (ItemType.Utility = 18, decomp :57646) — the dedicated
//                    m_utilityItem (:12874), shared with Megingjord/Wishbone. Coexists
//                    with any weapon/shield/Local-Map; never a hand item.
//    • Same build  : ADDITIVE (ADR-0006) via Assets.TryConstructItemShell — we do NOT
//                    clone a vanilla item. World-drop visual grafted ZNetView-free.
//    • Same detect : worn-state via PUBLIC Inventory.GetEquippedItems() + m_dropPrefab
//                    name (m_utilityItem is protected). IsWearing() mirrors IsWearingKit.
//  The DIFFERENCE is the gate (crystal, not the 40-pigment cartography gate) and the
//  effect (the wisp field reads IsWearing() each tick — M3 — instead of a fog gate).
//
//  ── The crystal gate (Daniel 2026-06-25) ─────────────────────────────────────
//  Recipe consumes Crystal (Stone Golem drop, Mountains) so the stone is a Mountains-
//  tier reward, not a turn-on-day-one convenience. Surfaced the vanilla way (a normal
//  recipe at the forge); crafting it the first time unlocks the wisp loop.
//
//  Client-only by construction (Minimap / wisps only exist on a client). All
//  registration gated behind ServerContext.OnSBServer via the Registrar.
//  logs-green ≠ playable — Daniel verifies the in-game look + feel.
// ============================================================================

using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.SeersStone
{
    public static class SeersStone
    {
        /// <summary>
        /// LOCKED prefab/wire name — a save+wire contract the moment a stone is crafted; never
        /// rename (renaming orphans every crafted instance, the Pigments/Kit precedent).
        /// </summary>
        public const string StoneName = "SBPR_SeersStone";

        // World-drop visual blueprint: read the vanilla Demister (wisplight) — no, that carries a
        // Light + particles we don't want on a dropped item. Use the same clean single-mesh leather
        // donor the Kit uses as a stand-in amulet pouch; the real worn look is a Utility item (no
        // body-attach mesh anyway). Read ONLY as a blueprint (ZNetView-free cosmetic child).
        private const string BlueprintItem = "Amber";          // a small gem-like drop mesh (single mesh, no Light/particles)
        private const string BlueprintVisualChild = "attach";

        // Recipe — crystal-gated Mountains tier (Daniel 2026-06-25). Crystal = Stone Golem drop.
        // Eyeball costs in the Sunstone/Kit spirit; final numbers are Daniel's to tune (the recipe
        // is authored here as the single source, mirrored into SpecCheck's manifest).
        public const int CrystalCost = 5;     // the gate material
        public const int SilverCost  = 2;     // Mountains-tier metal (frame/setting)
        public const int JuteCost    = 2;     // Mountains cloth (the cord) — "JuteRed" or generic; see recipe

        // Icon shipped in the modpack plugin folder (assets/icons/items/*.png via pack-modpack.sh).
        // Crash-safe: TryConstructItemShell pre-seeds a magenta fallback, so a missing PNG degrades
        // to a visible placeholder, never an IndexOutOfRange in the crafting UI. v0.1 placeholder.
        private const string IconFile = "seers_stone_v0.1.png";

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (ZNetScene.Awake postfix, via Registrar)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            if (zns.GetPrefab(StoneName) != null) return;

            // ADDITIVE (ADR-0006): build the item skeleton from scratch (no clone of a vanilla
            // item). TryConstructItemShell news the SharedData so equip/tooltip is NRE-safe.
            if (!Assets.TryConstructItemShell(StoneName, out var go))
            {
                Plugin.Log.LogWarning($"[Trailborne/SeersStone] Could not construct item shell for {StoneName}; skipping.");
                return;
            }

            var drop = go.GetComponent<ItemDrop>();
            if (drop != null)
            {
                var shared = drop.m_itemData.m_shared;
                shared.m_name        = "Seer's Stone";
                shared.m_description =
                    "A sunfinder's lens bound in silver and worn at the belt. While worn, the land " +
                    "whispers — faint wisps drift over berries, ore, and forgotten places, seen by " +
                    "you alone. Look upon a wisp and mark it, and your map remembers. Take the stone " +
                    "off and the world falls quiet again.";
                // Utility slot (ItemType.Utility = 18) — the dedicated m_utilityItem, shared with
                // Megingjord/Wishbone. Coexists with any weapon/shield; never a hand item.
                shared.m_itemType    = ItemDrop.ItemData.ItemType.Utility;
                shared.m_maxStackSize = 1;     // a worn accessory — not stackable
                shared.m_weight      = 0.5f;
                shared.m_maxQuality  = 1;      // no upgrade tiers in v0.x
                shared.m_teleportable = true;
                // Utility items have no body-attach visual (VisEquipment.SetUtilityItem only stores
                // a ZDO hash; decomp :28466), so no worn-mesh rigging is needed.

                var sprite = Assets.LoadPngAsSprite(IconFile);
                if (sprite != null)
                {
                    shared.m_icons = new[] { sprite };
                }
                else
                {
                    Plugin.Log.LogError(
                        $"[Trailborne/SeersStone] {StoneName}: icon '{IconFile}' did NOT load " +
                        "(missing from plugin folder?). The item is crash-safe (shows the magenta " +
                        "fallback placeholder) but has no real icon. Ship the PNG in assets/icons/items/.");
                }
            }

            // World-drop visual: graft the blueprint item's mesh subtree as a ZNetView-free cosmetic
            // child (additive; reads the donor, never instantiates its networked root). Cosmetic-only.
            if (!Assets.TryGraftVisualSubtree(BlueprintItem, BlueprintVisualChild, go, "SBPR_SeersStoneVisual", out _))
                Plugin.Log.LogWarning(
                    $"[Trailborne/SeersStone] {StoneName}: world-drop visual graft from '{BlueprintItem}/{BlueprintVisualChild}' " +
                    "failed; the dropped item will have no mesh this build (logs-green≠playable — Daniel " +
                    "verifies the look in-game). Functionally unaffected.");

            Assets.RegisterPrefabInZNetScene(go);
            Plugin.Log.LogInfo($"[Trailborne/SeersStone] Registered Seer's Stone item: {StoneName} (additive, Utility slot, crystal-gated).");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — register item + recipe
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            var p = zns?.GetPrefab(StoneName);
            if (p != null) Assets.RegisterItemInObjectDB(p);

            AddStoneRecipe();

            Plugin.Log.LogInfo("[Trailborne/SeersStone] Seer's Stone ObjectDB wiring complete (item + crystal recipe).");
        }

        private static void AddStoneRecipe()
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;
            if (RecipeHelpers.HasRecipe(StoneName)) return;   // idempotent across repeated ODB events

            var stonePrefab = odb.GetItemPrefab(StoneName);
            if (stonePrefab == null)
            {
                Plugin.Log.LogWarning($"[Trailborne/SeersStone] {StoneName} not in ODB at recipe time; skipping recipe (retry on next ODB event).");
                return;
            }

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name              = "Recipe_" + StoneName;
            recipe.m_item            = stonePrefab.GetComponent<ItemDrop>();
            recipe.m_amount          = 1;
            recipe.m_minStationLevel = 1;
            // Mountains-tier station: the Forge (vanilla "forge"). Crystal/silver are forge-worked.
            recipe.m_craftingStation = RecipeHelpers.FindStation("forge");
            // Crystal-gated (Daniel 2026-06-25): Crystal (Stone Golem drop) + Silver + cord. Vanilla
            // resource prefab names; BuildReq warns on a genuinely-missing resource. Numbers are
            // Daniel's to tune — authored here as the single source (SpecCheck manifest mirrors it).
            recipe.m_resources = new[]
            {
                Assets.BuildReq("Crystal", CrystalCost, "SeersStone"),
                Assets.BuildReq("Silver",  SilverCost,  "SeersStone"),
                Assets.BuildReq("JuteRed", JuteCost,    "SeersStone"),
            };
            odb.m_recipes.Add(recipe);
        }

        // ───────────────────────────────────────────────
        // WORN-STONE DETECTION (public API only — m_utilityItem is protected)
        // ───────────────────────────────────────────────

        /// <summary>
        /// True if <paramref name="player"/> currently wears the Seer's Stone in the Utility slot.
        /// Reads the PUBLIC Inventory.GetEquippedItems() (decomp :57192) and compares each equipped
        /// item's m_dropPrefab name against <see cref="StoneName"/> — the same (item → m_dropPrefab.name)
        /// pair vanilla uses to wire visuals (VisEquipment.SetUtilityItem, decomp :14158). We do NOT
        /// touch the protected m_utilityItem field. The wisp field (M3) calls this each tick to decide
        /// whether the local player sees wisps. Mirrors CartographersKit.IsWearingKit exactly.
        /// </summary>
        public static bool IsWearing(Player player)
        {
            if (player == null) return false;
            var inv = player.GetInventory();
            if (inv == null) return false;

            foreach (var item in inv.GetEquippedItems())
            {
                if (item == null) continue;
                if (item.m_shared == null || item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Utility)
                    continue;
                var drop = item.m_dropPrefab;
                if (drop == null) continue;
                if (StripCloneSuffix(drop.name) == StoneName)
                    return true;
            }
            return false;
        }

        // Mirror of vanilla ItemDrop.GetPrefabName clone-suffix strip (decomp :58940): cut at the
        // first '(' or ' ' so "SBPR_SeersStone(Clone)" matches StoneName.
        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int i = name.IndexOfAny(new[] { '(', ' ' });
            return i >= 0 ? name.Substring(0, i) : name;
        }
    }
}
