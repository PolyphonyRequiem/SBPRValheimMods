// ============================================================================
//  Trailborne v3 (Swamp) — Iron Compass (the earned no-map orientation payoff)
// ----------------------------------------------------------------------------
//  Impl spec: docs/v3/planning/iron-compass-impl-spec.md
//  Design   : docs/design/nomap.md §8 "Iron Compass (Swamps)"
//  Card     : t_ee61472f  (parent spec card t_d35405e3 / PR #170)
//
//  A v3 Swamp-tier TRINKET accessory whose HUD overlay finally grants the
//  cardinal orientation the no-map pillar deliberately withholds — WITHOUT ever
//  touching the local map. v1/v2 ship the map with NO north indicator by design
//  (requirements.md:646; cartography re-lock §2H.1); the Iron Compass is the
//  separate EARNED tool that reads true north on a dial at the edge of sight.
//  Putting a north arrow back on the map would delete this item's reason to
//  exist — so the compass lives entirely on its own HUD overlay.
//
//  ── Slot: TRINKET (ItemType.Trinket = 24) ────────────────────────────────────
//  The Trinket slot is a separate, fully-wired equip slot (Humanoid.m_trinketItem
//  :12876, EquipItem Trinket branch :13992, VisEquipment.SetTrinketItem :28478,
//  enum Trinket = 24 :57652). The compass shares this slot with the Sunstone Lens
//  (wear threat-sense OR orientation, not both — a deliberate exploration-tool
//  opportunity cost, Daniel-acknowledged).
//
//  ── Daniel's Q1–Q4 answers (2026-06-17 — supersede the spec's architect defaults) ──
//   • Q1 recipe LOCKED: Iron ×4 + Ooze ×2 + Red Pigment ×1 @ Explorer's Bench.
//     Iron is the Swamp tier gate; Ooze is the Swamp Blob/Oozer drop (the wet
//     swamp resin that beds the needle); Red Pigment (SBPR_InkRed) paints the
//     north tip. "Every material has a sentence."
//   • Q2 mesh: the HUD is a 2D UGUI sprite (the native tool) — see SBPR_CompassHud.
//     The held-trinket WORLD mesh is DEFERRED to v0.2+ (requirements.md:696); a
//     placeholder item visual is fine for v1 (a Trinket's in-world mesh is rarely
//     seen — the overlay sprite is the art that matters).
//   • Q3 lag: lerp-toward-target needle with a Config.Bind smoothing constant.
//   • Q4 anchor: a Config.Bind ENUM (CompassAnchor), default TopCenter, NoMap-safe.
//     Scaffolded from day one to extend to the carry-state Local Map disc and the
//     future Eye-of-Odin global minimap (see SBPR_CompassHud.CompassAnchor).
//
//  ── Construction is ADDITIVE (ADR-0006) ──────────────────────────────────────
//  Built via Assets.ConstructItemShell (fresh ZNetView + ItemDrop + SharedData),
//  exactly like the Cartographer's Kit and the Sunstone Lens — NEVER by cloning a
//  vanilla item prefab and stripping it.
//
//  ── No game-state patches ─────────────────────────────────────────────────────
//  The item is just an ItemDrop + recipe (patch-free). The ONLY Harmony patch in
//  the feature is the Hud.Awake postfix that mounts the overlay (CompassHudBootstrapPatch);
//  the needle reads GameCamera.instance.transform every frame, client-side, writing
//  nothing. It cannot desync, cannot corrupt a save, and is invisible to vanilla /
//  other-modded clients (it does nothing unless SBPR_IronCompass is equipped).
//
//  All registration gated behind ServerContext.OnSBServer (via Registrar). The
//  Hud.Awake postfix carries its own OnSBServer guard. logs-green ≠ playable —
//  Daniel verifies the AT-COMPASS-* acceptance tests in-game.
//
//  Clean-side (ADR-0001): every decomp line cited is the base game
//  (assembly_valheim), which is fair game to read and adapt. No third-party mod
//  code is read or copied.
// ============================================================================

using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Exploration
{
    // Alias the Trailhead TYPE: from this sibling Features.* namespace the bare name
    // `Trailhead` binds to the sibling NAMESPACE, not the class. Alias it to the type so
    // the readable `Trailhead.ExplorersBenchName` station lookup resolves (the same
    // collision Pigments.cs / CartographersKit.cs / SunstoneLens.cs document).
    using Trailhead = SBPR.Trailborne.Features.Trailhead.Trailhead;
    using Pigments = SBPR.Trailborne.Features.Pigments.Pigments;

    /// <summary>
    /// The Iron Compass item: an additive Trinket ItemDrop + its Explorer's Bench recipe.
    /// The HUD behaviour lives in <see cref="SBPR_CompassHud"/>; the overlay mount lives in
    /// <see cref="CompassHudBootstrapPatch"/>. This class is patch-free (item + recipe only).
    /// </summary>
    public static class IronCompass
    {
        // ── LOCKED prefab/wire name (never rename — renaming orphans every crafted
        //    instance + every recipe that references it, the Pigments SBPR_Ink* lesson).
        public const string CompassName = "SBPR_IronCompass";   // the worn Trinket accessory

        // ── Recipe costs (Q1 LOCKED 2026-06-17). Iron is the Swamp metal → the tier gate.
        //    Ooze is the Swamp Blob/Oozer drop; Red Pigment is the SBPR_InkRed pigment item.
        public const int IronCost      = 4;   // Iron ×4    — the Swamp-tier frame + gate
        public const int OozeCost       = 2;  // Ooze ×2    — swamp resin bedding the needle
        public const int RedPigmentCost = 1;  // Red Pigment ×1 — paints the north tip

        // Icon shipped in the modpack plugin folder (assets/icons/items/*.png copied by
        // scripts/pack-modpack.sh). A real icon is a HARD requirement (the crafting UI
        // indexes m_icons[0]); ConstructItemShell pre-seeds a magenta fallback so a missing
        // PNG degrades to "ugly, never crash", and SpecCheck's C1 boot check screams if the
        // real PNG didn't ship. v0.1 placeholder per the icon-asset doctrine.
        private const string IconFile = "iron_compass_v0.1.png";

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (ZNetScene.Awake postfix, via Registrar)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            if (zns.GetPrefab(CompassName) != null) return;

            // ADDITIVE (ADR-0006): build the item skeleton from scratch (no clone of a vanilla
            // item). ConstructItemShell news the SharedData (+ seeds m_icons[0]=FallbackIcon and
            // an inert m_attack/m_secondaryAttack) so the equip/tooltip path is NRE-safe.
            var go = Assets.ConstructItemShell(CompassName);
            if (go == null)
            {
                Plugin.Log.LogWarning($"[Trailborne/Exploration] Could not construct item shell for {CompassName}; skipping.");
                return;
            }

            var drop = go.GetComponent<ItemDrop>();
            if (drop != null)
            {
                var shared = drop.m_itemData.m_shared;
                shared.m_name        = "Iron Compass";
                shared.m_description =
                    "A worn iron compass on a leather thong, kept at the belt. Wear it and a dial "
                    + "settles at the edge of your sight, its red-tipped needle holding true north — "
                    + "the swamp can no longer hide the cardinal directions from you. Take it off and "
                    + "the bearing is yours to keep in your head again.";

                // THE slot decision (impl spec §2/§3.1): Trinket (ItemType.Trinket = 24, decomp
                // :57652) — the player's dedicated m_trinketItem (:12876), separate from the
                // Utility slot the Cartographer's Kit uses. Shares the Trinket slot with the
                // Sunstone Lens (orientation OR threat-sense — the exploration-tool opportunity cost).
                shared.m_itemType    = ItemDrop.ItemData.ItemType.Trinket;
                shared.m_maxStackSize = 1;     // a worn accessory — not stackable
                shared.m_weight      = 0.5f;
                shared.m_maxQuality  = 1;      // no upgrade tiers in v0.x
                shared.m_teleportable = true;
                shared.m_equipDuration = 0.5f;
                shared.m_useDurability = false; // the compass does not wear (unlike the Twisted Key / Sunstone Lens battery)

                var sprite = Assets.LoadPngAsSprite(IconFile);
                if (sprite != null)
                {
                    shared.m_icons = new[] { sprite };
                }
                else
                {
                    // ConstructItemShell already pre-seeded a magenta fallback into m_icons, so a
                    // missing icon degrades to a visible placeholder and the crafting UI does NOT
                    // crash. Keep this ERROR as the loud human signal (paired with SpecCheck's C1).
                    Plugin.Log.LogError(
                        $"[Trailborne/Exploration] {CompassName}: icon '{IconFile}' did NOT load (missing PNG?). "
                        + "Crash-safe (magenta fallback) but ship the real PNG in assets/icons/items/.");
                }
            }

            Assets.RegisterPrefabInZNetScene(go);
            Plugin.Log.LogInfo($"[Trailborne/Exploration] Registered Iron Compass trinket: {CompassName} (additive, Trinket slot, HUD-overlay orientation).");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — item + recipe
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            // Item into ODB (so the recipe resolves + EquipItem can find the trinket prefab).
            var prefab = zns?.GetPrefab(CompassName);
            if (prefab != null) Assets.RegisterItemInObjectDB(prefab);

            AddCompassRecipe();

            Plugin.Log.LogInfo("[Trailborne/Exploration] Iron Compass ObjectDB wiring complete (item + recipe).");
        }

        private static void AddCompassRecipe()
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;
            if (RecipeHelpers.HasRecipe(CompassName)) return;

            var prefab = odb.GetItemPrefab(CompassName);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[Trailborne/Exploration] {CompassName} not in ODB at recipe time; skipping recipe.");
                return;
            }

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name              = "Recipe_" + CompassName;
            recipe.m_item            = prefab.GetComponent<ItemDrop>();
            recipe.m_amount          = 1;
            recipe.m_minStationLevel = 1;
            recipe.m_craftingStation = RecipeHelpers.FindStation(Trailhead.ExplorersBenchName);
            // Q1 LOCKED (2026-06-17): Iron ×4 + Ooze ×2 + Red Pigment ×1. Iron + Ooze are vanilla
            // Swamp resources (warn=true: a missing one SHOULD scream). Red Pigment is the SBPR_InkRed
            // pigment item, referenced via the Pigments.PigmentRedName const (never a literal) so a
            // rename can't drift the recipe; it registers into ODB earlier in the Registrar dispatch
            // order (Pigments.DoObjectDBWiring runs before Exploration's), so warn=true there too.
            recipe.m_resources = new[]
            {
                Assets.BuildReq("Iron",                   IronCost,      "Exploration"),
                Assets.BuildReq("Ooze",                   OozeCost,       "Exploration"),
                Assets.BuildReq(Pigments.PigmentRedName,  RedPigmentCost, "Exploration"),
            };
            odb.m_recipes.Add(recipe);
        }
    }
}
