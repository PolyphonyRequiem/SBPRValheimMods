// ============================================================================
//  Trailborne v2 cartography — Cartographer's Kit (Utility-slot gating accessory)
// ----------------------------------------------------------------------------
//  Impl spec §3 (cartography-impl-spec.md), requirements §3, card t_65fcfe5c.
//  A Black-Forest-tier equippable accessory in the UTILITY slot whose presence
//  GATES the personal auto-map's passive fog reveal:
//
//    • Kit worn   → walking reveals fog (vanilla Minimap.UpdateExplore runs).
//    • Kit absent → ZERO passive fog reveal (our Prefix no-ops UpdateExplore).
//
//  The 40-pigment recipe IS the gate (requirements §3, C10/C11) — there is NO
//  discovery-flag system. The Kit is surfaced as a normal recipe the vanilla way
//  (IsKnownMaterial); crafting it the first time gates the whole cartography loop.
//
//  ── Construction is ADDITIVE (ADR-0006) ──────────────────────────────────────
//  Assets.ConstructItemShell builds the networked item skeleton (ZNetView +
//  ZSyncTransform + Rigidbody + collider + ItemDrop with a FRESH SharedData) from
//  scratch. We do NOT clone a vanilla item (the pre-ADR Pigments/cairn-marker
//  pattern). The world-drop visual is grafted as a ZNetView-free cosmetic child
//  off a vanilla blueprint (Assets.GraftVisualSubtree) — reading a mesh is
//  reference, not cloning. We never Instantiate a ZNetView-bearing prefab.
//
//  ── The gate (the whole point) ───────────────────────────────────────────────
//  Harmony Prefix on Minimap.UpdateExplore(float dt, Player player) (decomp
//  :48005). Minimap.Update calls UpdateExplore UNCONDITIONALLY every frame
//  (decomp :47056) — BEFORE any nomap/map-mode check — so personal fog (m_explored)
//  accumulates even under the v1 server-side nomap config (Game.m_noMap only gates
//  the *visible* map, not the explore write). That is exactly the behaviour the
//  spike found and exactly what this gate targets: skip the fog write for the tick
//  unless the local player wears the Kit. We gate UpdateExplore (the single
//  personal-walking-reveal entry point), NOT Explore(Vector3,float) — Explore is
//  also reached from ExploreOthers / shared-data merges, which must keep working
//  without the Kit (reading a Surveyor's Table's shared fog is not gated).
//
//  Client-only by construction: Minimap.instance / UpdateExplore only exist on a
//  client; the dedicated server never runs them. m_utilityItem is PROTECTED, so we
//  detect the Kit via the PUBLIC Inventory.GetEquippedItems() + ItemData.m_dropPrefab
//  name (the exact pair vanilla itself uses at VisEquipment wiring, decomp :14158).
//
//  All registration gated behind ServerContext.OnSBServer (via Registrar). The
//  gate Prefix is registered in Plugin.cs (PatchCheck asserts it wove).
//  logs-green ≠ playable — Daniel verifies AT-KIT-* in-game.
// ============================================================================

using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Cartography
{
    // Alias the Trailhead + Pigments TYPES: from this sibling Features.* namespace the bare
    // names `Trailhead` / `Pigments` bind to the sibling NAMESPACES, not the classes — the
    // same collision Pigments.cs documents for Trailhead. Alias both to the types so the
    // readable `Trailhead.ExplorersBenchName` / `Pigments.PigmentRedName` lookups resolve.
    using Trailhead = SBPR.Trailborne.Features.Trailhead.Trailhead;
    using Pigments = SBPR.Trailborne.Features.Pigments.Pigments;

    public static class CartographersKit
    {
        // LOCKED prefab/wire name (impl spec §0/§5) — a save+wire contract the moment a
        // Kit is crafted; never rename (renaming orphans every crafted instance, the same
        // reason Pigments kept SBPR_Ink*).
        public const string KitName = "SBPR_CartographersKit";

        // World-drop visual blueprint: read the vanilla LeatherScraps mesh as a thematic
        // stand-in (a leather kit/satchel reads as a cartographer's kit). Chosen as the
        // CLEANEST single-mesh/single-material item donor (vprefab: one mesh under the
        // "attach" child, no ping-trail particles or stray Light like Wishbone carries).
        // Read ONLY as a blueprint (GraftVisualSubtree → ZNetView-free cosmetic child);
        // never instantiated. If the child doesn't resolve, the item still works with no
        // world mesh (logs-green≠playable — Daniel verifies the look). Visual polish flagged.
        private const string BlueprintItem = "LeatherScraps";
        private const string BlueprintVisualChild = "attach";

        // Recipe — LOCKED (impl spec §0 row 3 / requirements §3, C11): 10×(R/W/B/K) pigment
        // + 4 FineWood, output 1, at the Explorer's Bench. Pigments referenced via the
        // Pigments.*Name consts (values are the historical SBPR_Ink* wire names — NEVER a
        // literal), so a pigment rename can't silently drift this recipe.
        public const int PigmentEachCost = 10;
        public const int FineWoodCost    = 4;

        // Icon shipped in the modpack plugin folder (assets/icons/items/*.png copied by
        // scripts/pack-modpack.sh). The real icon is a HARD requirement, not cosmetic — but it
        // is no longer a crash risk: ConstructItemShell pre-seeds a shared magenta fallback into
        // m_icons so a missing PNG degrades to a visible placeholder, never an IndexOutOfRange in
        // the crafting UI (vanilla GetIcon indexes m_icons[0] with no bounds guard). If this PNG
        // fails to load, the ERROR below + SpecCheck's C1 boot check are the loud signals that the
        // real icon didn't ship. v0.1 placeholder per the icon-asset doctrine.
        private const string IconFile = "cartographers_kit_v0.1.png";

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (ZNetScene.Awake postfix, via Registrar)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            if (zns.GetPrefab(KitName) != null) return;

            // ADDITIVE (ADR-0006): build the item skeleton from scratch (no clone of a
            // vanilla item). ConstructItemShell news the SharedData so the equip/tooltip
            // path is NRE-safe.
            var go = Assets.ConstructItemShell(KitName);
            if (go == null)
            {
                Plugin.Log.LogWarning($"[Trailborne/Cartography] Could not construct item shell for {KitName}; skipping.");
                return;
            }

            var drop = go.GetComponent<ItemDrop>();
            if (drop != null)
            {
                var shared = drop.m_itemData.m_shared;
                shared.m_name        = "Cartographer's Kit";
                shared.m_description =
                    "A surveyor's kit of inks and tools, worn at the belt. While worn, the land " +
                    "you walk imprints onto your personal map. Take it off and you stop committing " +
                    "the world to memory.";
                // THE decisive lock (impl spec §3.1 / requirements §3): Utility slot
                // (ItemType.Utility = 18, decomp :57646) — the player's dedicated
                // m_utilityItem (:12874), the SAME slot as Megingjord/Wishbone. Coexists with
                // any weapon/shield/Local-Map; never a hand item (AT-KIT-COEXIST).
                shared.m_itemType    = ItemDrop.ItemData.ItemType.Utility;
                shared.m_maxStackSize = 1;   // a worn accessory — not stackable
                shared.m_weight      = 1.0f;
                shared.m_maxQuality  = 1;    // no upgrade tiers in v0.x
                shared.m_teleportable = true;
                // Utility items have no body-attach visual (VisEquipment.SetUtilityItem only
                // stores a ZDO hash; decomp :28466), so no worn-mesh rigging is needed.

                var sprite = Assets.LoadPngAsSprite(IconFile);
                if (sprite != null)
                {
                    shared.m_icons = new[] { sprite };
                }
                else
                {
                    // ConstructItemShell already pre-seeded a magenta fallback into m_icons, so a
                    // missing icon degrades to a visible placeholder and the crafting UI does NOT
                    // crash (it no longer leaves m_icons empty). Keep this ERROR as the loud human
                    // signal that the real PNG didn't ship; SpecCheck's C1 boot check is the
                    // server-side backstop for the same condition. The item still registers and is
                    // craftable — it just wears the placeholder until the PNG is restored.
                    Plugin.Log.LogError(
                        $"[Trailborne/Cartography] {KitName}: icon '{IconFile}' did NOT load " +
                        "(missing from plugin folder?). The item is crash-safe (shows the magenta " +
                        "fallback placeholder) but has no real icon. Ship the PNG in assets/icons/items/.");
                }
            }

            // World-drop visual: graft the blueprint item's mesh subtree as a ZNetView-free
            // cosmetic child (additive; reads the donor, never instantiates its networked
            // root). Cosmetic-only — the item is fully functional without it.
            var visual = Assets.GraftVisualSubtree(BlueprintItem, BlueprintVisualChild, go, "SBPR_CartographersKitVisual");
            if (visual == null)
                Plugin.Log.LogWarning(
                    $"[Trailborne/Cartography] {KitName}: world-drop visual graft from '{BlueprintItem}/{BlueprintVisualChild}' " +
                    "failed; the dropped item will have no mesh this build (logs-green≠playable — Daniel " +
                    "verifies the look in-game). Functionally unaffected.");

            Assets.RegisterPrefabInZNetScene(go);
            Plugin.Log.LogInfo($"[Trailborne/Cartography] Registered Cartographer's Kit item: {KitName} (additive, Utility slot, auto-map gate).");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — register item + recipe
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            // Item into ODB (so EquipItem's VisEquipment.SetUtilityItem can resolve the
            // m_dropPrefab name, and so the recipe's m_item resolves).
            var p = zns?.GetPrefab(KitName);
            if (p != null) Assets.RegisterItemInObjectDB(p);

            AddKitRecipe();

            Plugin.Log.LogInfo("[Trailborne/Cartography] Cartographer's Kit ObjectDB wiring complete (item + 40-pigment recipe).");
        }

        private static void AddKitRecipe()
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;
            if (RecipeHelpers.HasRecipe(KitName)) return;   // idempotent across repeated ODB events

            var kitPrefab = odb.GetItemPrefab(KitName);
            if (kitPrefab == null)
            {
                Plugin.Log.LogWarning($"[Trailborne/Cartography] {KitName} not in ODB at recipe time; skipping recipe (will retry on next ODB event).");
                return;
            }

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name              = "Recipe_" + KitName;
            recipe.m_item            = kitPrefab.GetComponent<ItemDrop>();
            recipe.m_amount          = 1;
            recipe.m_minStationLevel = 1;
            recipe.m_craftingStation = RecipeHelpers.FindStation(Trailhead.ExplorersBenchName);
            // LOCKED recipe (impl spec §0 row 3 / requirements §3): 10× each of the four
            // pigments + 4 FineWood. Pigment names via Pigments.*Name (SBPR_Ink* values) —
            // never a literal. Pigments register into ODB earlier in the Registrar dispatch
            // order (Pigments.DoObjectDBWiring runs before CartographersKit's), so warn=true:
            // a genuinely-missing pigment SHOULD scream here.
            recipe.m_resources = new[]
            {
                Assets.BuildReq(Pigments.PigmentRedName,   PigmentEachCost, "Cartography"),
                Assets.BuildReq(Pigments.PigmentWhiteName, PigmentEachCost, "Cartography"),
                Assets.BuildReq(Pigments.PigmentBlueName,  PigmentEachCost, "Cartography"),
                Assets.BuildReq(Pigments.PigmentBlackName, PigmentEachCost, "Cartography"),
                Assets.BuildReq("FineWood",                FineWoodCost,    "Cartography"),
            };
            odb.m_recipes.Add(recipe);
        }

        // ───────────────────────────────────────────────
        // EQUIPPED-KIT DETECTION (public API only — m_utilityItem is protected)
        // ───────────────────────────────────────────────

        /// <summary>
        /// True if <paramref name="player"/> currently wears the Cartographer's Kit in the
        /// Utility slot. Reads the PUBLIC Inventory.GetEquippedItems() (decomp :57192) and
        /// compares each equipped item's m_dropPrefab name against <see cref="KitName"/> —
        /// the same (item → m_dropPrefab.name) pair vanilla uses to wire visuals
        /// (VisEquipment.SetUtilityItem, decomp :14158). We do NOT touch the protected
        /// m_utilityItem field directly (it won't compile from outside Humanoid, and reaching
        /// it via reflection would be brittle). Matching on the prefab name (stripped of any
        /// "(Clone)"/space suffix) is robust whether the equipped instance is the ODB prefab
        /// or a world-instantiated drop.
        /// </summary>
        public static bool IsWearingKit(Player player)
        {
            if (player == null) return false;
            var inv = player.GetInventory();
            if (inv == null) return false;

            foreach (var item in inv.GetEquippedItems())
            {
                if (item == null) continue;
                // Only the Utility slot counts (the Kit is the only Utility item we ship, but
                // gate on the slot anyway so a future Utility item can't accidentally satisfy
                // the check). m_shared is non-null on any real item.
                if (item.m_shared == null || item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Utility)
                    continue;
                var drop = item.m_dropPrefab;
                if (drop == null) continue;
                if (StripCloneSuffix(drop.name) == KitName)
                    return true;
            }
            return false;
        }

        // Mirror of the vanilla ItemDrop.GetPrefabName clone-suffix strip (decomp :58940):
        // cut at the first '(' or ' ' so "SBPR_CartographersKit(Clone)" matches KitName.
        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int i = name.IndexOfAny(new[] { '(', ' ' });
            return i >= 0 ? name.Substring(0, i) : name;
        }

        // ───────────────────────────────────────────────
        // THE GATE — Harmony Prefix on Minimap.UpdateExplore
        // ───────────────────────────────────────────────

        /// <summary>
        /// Gates the personal auto-map's passive walking-reveal behind wearing the Kit.
        /// Prefix on <c>Minimap.UpdateExplore(float dt, Player player)</c> (decomp :48005):
        /// returning <c>false</c> SKIPS the original method for that tick, so
        /// <c>Explore(player.position, m_exploreRadius)</c> never runs and no cell of
        /// <c>m_explored</c> is written — zero passive fog reveal (AT-KIT-GATE). When the Kit
        /// IS worn we return <c>true</c> and vanilla runs unchanged.
        ///
        /// Scope discipline (impl spec §3.2): we gate ONLY <c>UpdateExplore</c> — the single
        /// personal-walking-reveal entry point — never <c>Explore</c> directly, which is also
        /// reached from <c>ExploreOthers</c>/shared-data merges that must keep working without
        /// the Kit (reading a Surveyor's Table's shared fog is NOT gated).
        ///
        /// We only gate the LOCAL player's reveal: the <c>player</c> arg is always
        /// <c>Player.m_localPlayer</c> here (Minimap.Update passes it; decomp :47056), but we
        /// guard explicitly so a future caller can't gate someone else. Any unexpected error
        /// fails OPEN (return true → vanilla reveal) so a bug in our detection can never
        /// silently brick a player's map.
        ///
        /// CLEAN-SIDE (ADR-0001): patches the base-game Minimap only. Registered in Plugin.cs.
        /// </summary>
        [HarmonyPatch(typeof(Minimap), "UpdateExplore")]
        public static class UpdateExploreGate
        {
            [HarmonyPrefix]
            public static bool Prefix(Player player)
            {
                try
                {
                    // Only gate the local player's personal reveal.
                    if (player == null || player != Player.m_localPlayer)
                        return true;   // not our concern — let vanilla run

                    // Kit worn → reveal as normal. Kit absent → SKIP the fog write this tick.
                    return IsWearingKit(player);
                }
                catch (System.Exception e)
                {
                    // Fail OPEN: never let a detection bug brick the map. Log once-ish at warn.
                    Plugin.Log.LogWarning($"[Trailborne/Cartography] UpdateExplore gate error (failing open): {e.Message}");
                    return true;
                }
            }
        }
    }
}
