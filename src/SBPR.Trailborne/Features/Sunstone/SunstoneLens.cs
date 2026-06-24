// ============================================================================
//  Trailborne v3 (Swamp) — Sunstone Lens (solar-charged monster-detection trinket)
// ----------------------------------------------------------------------------
//  Impl spec: docs/v3/planning/sunstone-lens-impl-spec.md
//  Design   : docs/design/swamp-detection-item.md (theme/material/sourcing, PR #144)
//  Card     : t_2fd7bc7f
//
//  A v3 Swamp-tier TRINKET accessory whose DURABILITY is a solar battery:
//    • Recharges (durability ↑) only in clear weather + daylight + not wet +
//      outdoors + outside the Swamp. (The Swamp is always-wet/overcast, so it
//      can NEVER charge there — the whole "sun battery in the sunless Swamp"
//      tension. Verified: Swamp.md "always raining in the mire".)
//    • Drains (durability ↓) at a FIXED rate while worn, independent of how many
//      hostiles are detected.
//    • While worn AND charged, reveals nearby HOSTILE creatures (the rune-of-
//      detection behaviour) — reproduced from VANILLA primitives only
//      (Character.GetAllCharacters faction/tamed filter). Rendered on a HUD
//      overlay (see SunstoneLensHudOverlay) — NOT minimap pins, because the SB
//      server runs NoMap by default (NoMapEnforcer) so pins have no surface.
//    • At zero charge it goes INERT (detection off) but is NOT consumed/broken
//      and stays equipped; it works again after recharging in sunlight.
//
//  ── Slot: TRINKET (ItemType.Trinket = 24), NOT Utility ───────────────────────
//  The Trinket slot is a separate, fully-wired equip slot (Humanoid.m_trinketItem
//  :12876, EquipItem Trinket branch :13992, VisEquipment.SetTrinketItem :28478).
//  Using it sidesteps the Utility-slot contention with the Cartographer's Kit
//  (the cross-card concern three workers flagged) — the Lens coexists with the
//  Kit. (It DOES share the Trinket slot with the future Iron Compass — a
//  deliberate exploration-tool choice, flagged for Daniel, not pre-decided.)
//
//  ── Energy model: we OWN the drain + recharge ────────────────────────────────
//  m_useDurability = true so the durability bar renders + reads as a charge meter
//  (InventoryGrid draws the bar only when m_useDurability, :38853). BUT vanilla's
//  Humanoid.DrainEquipedItemDurability (:13227) drains trinkets every tick AND
//  breaks+unequips+destroys at zero (:13231-13237) — which would violate "not
//  consumed at zero". So a Harmony PREFIX on DrainEquipedItemDurability takes the
//  method over for OUR lens only: drains at our fixed rate (or recharges in sun),
//  clamps at [0, max], and returns false to SKIP vanilla's break branch. Every
//  other item (and every other player) passes straight through to vanilla.
//
//  ── Construction is ADDITIVE (ADR-0006) ──────────────────────────────────────
//  The Lens is built via Assets.TryConstructItemShell (fresh ZNetView + ItemDrop +
//  SharedData), exactly like the Cartographer's Kit. The Sunstone material item
//  clones Coins (the established tiny-Material pattern, same as Pigments).
//
//  All registration gated behind ServerContext.OnSBServer (via Registrar). The
//  drain patch is registered in Plugin.cs (PatchCheck asserts it wove).
//  logs-green ≠ playable — Daniel verifies AT-LENS-* in-game.
// ============================================================================

using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Sunstone
{
    // Alias the Trailhead TYPE: from this sibling Features.* namespace the bare name
    // `Trailhead` binds to the sibling NAMESPACE, not the class. Alias it to the type so
    // the readable `Trailhead.ExplorersBenchName` station lookup resolves (same collision
    // Pigments.cs / CartographersKit.cs document).
    using Trailhead = SBPR.Trailborne.Features.Trailhead.Trailhead;

    public static class SunstoneLens
    {
        // ── LOCKED prefab/wire names (never rename — renaming orphans every crafted
        //    instance + every recipe that references them, the Pigments SBPR_Ink* lesson).
        public const string SunstoneName = "SBPR_Sunstone";       // the standalone Material resource
        public const string LensName     = "SBPR_SunstoneLens";   // the worn Trinket accessory

        // ── Recipe costs (impl spec §0 / §6). Iron is the Swamp metal → the tier gate.
        public const int LensSunstoneCost = 2;   // Sunstone ×2 (the solar core)
        public const int LensIronCost     = 1;   // Iron ×1 (Swamp-tier frame + tier gate)
        public const int LensGuckCost     = 3;   // Guck ×3 (Swamp-surface housing/adhesive)
        // NOTE: Sunstone has NO craft recipe. Its sole acquisition path is the loot economy
        // (swamp surface chests ~15% + rare Draugr Elite ~5%, SunstoneLoot.cs, PR #183). The
        // earlier provisional Iron×1+Crystal×2 craft was a bridge until the drops shipped;
        // Daniel locked REMOVE once they did (card t_8f39b5fc → t_c27f985e, impl spec §6).

        // ── Energy / battery tuning (durability units; m_maxDurability is the capacity).
        //    Conservative v0.1 defaults; exposed as config in Plugin so Daniel can tune
        //    the charge economy without a rebuild (the design note's open "charge economy"
        //    knob). A full 100-unit battery at these rates ≈ 5 min of detection, ≈ 1.7 min
        //    of sun to refill — a real "top up in the open, spend in the dark" rhythm.
        public const float DefaultMaxCharge    = 100f;  // battery capacity (durability max)
        public const float DefaultDrainPerSec  = 0.33f; // ↓ while worn & not charging (~5 min full→empty)
        public const float DefaultChargePerSec = 1.0f;  // ↑ while in the sun (~1.7 min empty→full)
        public const float MinChargeToDetect   = 1f;    // below this the lens is inert (AC#5)

        // Charge-diagnostic default (card t_charge-diag): ON while the "won't charge in daylight" bug is
        // open so the NEXT playtest emits the recharge-gate breakdown with zero .cfg edits (the DebugMount
        // precedent). The DrainGate prefix logs one throttled line/sec from the LOCAL lens tick naming the
        // live value of every recharge sub-gate + the durability delta. Bake to false once charging is RC.
        public const bool DefaultDebugCharge = true;

        // ── Detection tuning.
        public const float DefaultDetectRadius   = 70f;  // metres; hostiles within this are revealed (Daniel in-game accept 2026-06-24, t_4b9f8889; was 50, was 30)
        public const float DefaultDetectInterval = 0.5f; // seconds between detection sweeps (HUD-driven)

        // Icon shipped in the modpack plugin folder (assets/icons/items/*.png). A real icon
        // is a HARD requirement (the crafting UI indexes m_icons[0]); TryConstructItemShell
        // pre-seeds a magenta fallback so a missing PNG degrades to "ugly, never crash", and
        // SpecCheck's C1 boot check screams if the real PNG didn't ship. v0.1 placeholders.
        private const string SunstoneIcon = "sunstone_v0.1.png";
        private const string LensIcon     = "sunstone_lens_v0.1.png";

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (ZNetScene.Awake postfix, via Registrar)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            RegisterSunstoneMaterial(zns);
            RegisterLensTrinket(zns);
        }

        // The standalone Sunstone Material — clones Coins (tiny stackable Material, the
        // established Pigments pattern; ADR-0006 carves out tiny Material items from the
        // no-clone rule the same way Pigments/cairn-marker do). Sunstone is its OWN resource
        // (Daniel's standalone-resource amendment); the Lens is its first consumer, not its
        // only use.
        private static void RegisterSunstoneMaterial(ZNetScene zns)
        {
            if (zns.GetPrefab(SunstoneName) != null) return;

            if (!Assets.TryClonePrefab("Coins", SunstoneName, out var clone))
            {
                Plugin.Log.LogWarning($"[Trailborne/Sunstone] Could not clone Coins for {SunstoneName}; skipping.");
                return;
            }

            var drop = clone.GetComponent<ItemDrop>();
            if (drop != null)
            {
                var shared = drop.m_itemData.m_shared;
                shared.m_name        = "Sunstone";
                shared.m_description =
                    "A shard of sun-finding crystal — the Viking sólarsteinn. It seems to hold a "
                    + "faint warmth of daylight even in the gloom. A Swamp-tier curiosity with more "
                    + "uses than one.";
                shared.m_itemType    = ItemDrop.ItemData.ItemType.Material;
                shared.m_maxStackSize = 20;
                shared.m_weight      = 0.3f;

                var sprite = Assets.LoadPngAsSprite(SunstoneIcon);
                if (sprite != null) shared.m_icons = new[] { sprite };
                else Plugin.Log.LogError(
                    $"[Trailborne/Sunstone] {SunstoneName}: icon '{SunstoneIcon}' did NOT load. "
                    + "Item still registers (Coins donor icon) but ship the PNG in assets/icons/items/.");
            }

            Assets.RegisterPrefabInZNetScene(clone);
            Plugin.Log.LogInfo($"[Trailborne/Sunstone] Registered Sunstone material item: {SunstoneName}");
        }

        // The Sunstone Lens — additive Trinket (TryConstructItemShell), the Cartographer's Kit
        // pattern. m_useDurability=true so the energy bar renders; WE own the drain/recharge.
        private static void RegisterLensTrinket(ZNetScene zns)
        {
            if (zns.GetPrefab(LensName) != null) return;

            if (!Assets.TryConstructItemShell(LensName, out var go))
            {
                Plugin.Log.LogWarning($"[Trailborne/Sunstone] Could not construct item shell for {LensName}; skipping.");
                return;
            }

            var drop = go.GetComponent<ItemDrop>();
            if (drop != null)
            {
                var shared = drop.m_itemData.m_shared;
                shared.m_name        = "Sunstone Lens";
                shared.m_description =
                    "A polished sunstone set in an iron-and-guck frame, worn at the belt. Hold it to "
                    + "the open sky in clear daylight and it drinks the sun, storing the light as a warm "
                    + "inner glow. Carried into the Swamp's murk, that stored light lets you sense what "
                    + "moves in the dark — and bleeds away as you spend it. It cannot drink the sun in "
                    + "the rain, at night, under a roof, or in the sunless mire.";

                // THE slot decision (impl spec §2): Trinket (ItemType.Trinket = 24, :57652) —
                // the player's dedicated m_trinketItem (:12876), separate from the Utility slot
                // the Cartographer's Kit uses. Coexists with the Kit (AT-LENS-* / cross-card).
                shared.m_itemType    = ItemDrop.ItemData.ItemType.Trinket;
                shared.m_maxStackSize = 1;     // a worn accessory — not stackable
                shared.m_weight      = 1.0f;
                shared.m_maxQuality  = 1;      // no upgrade tiers in v0.x
                shared.m_teleportable = true;

                // Energy-as-durability: the bar renders only when m_useDurability is true
                // (InventoryGrid :38853). We set it true for the meter, then OVERRIDE vanilla's
                // drain in the DrainGate prefix so the lens never breaks at zero.
                shared.m_useDurability     = true;
                shared.m_maxDurability     = Plugin.LensMaxCharge?.Value ?? DefaultMaxCharge;
                shared.m_durabilityDrain   = 0f;   // vanilla's per-tick drain coefficient — we zero it
                                                   // (defensive; the prefix skips vanilla anyway).
                shared.m_durabilityPerLevel = 0f;
                shared.m_destroyBroken     = false; // belt-and-suspenders: even if vanilla's break
                                                    // path were ever reached, never destroy the lens.
                shared.m_canBeReparied     = false; // charge is SUNLIGHT-only — never "Repair"-refillable.
                                                    // Because m_useDurability stays true (the energy bar),
                                                    // the lens is a perpetual repair candidate; without this
                                                    // it'd be free-refillable at the Explorer's Bench
                                                    // (recipe craft-station name-matches vanilla CanRepair,
                                                    // :42798), defeating the sun-charge design. This flag is
                                                    // the FIRST gate in CanRepair (:42776) so it blocks repair
                                                    // at EVERY station unconditionally. Mirrors LocalMap.cs.
                                                    // (sic: vanilla field is misspelled "Reparied".)

                // New lenses come fully charged so a freshly-crafted lens is immediately useful
                // (it still drains from there). m_durability is the live energy; persists across
                // relog (ItemData save writes m_durability, :57326).
                drop.m_itemData.m_durability = shared.m_maxDurability;

                var sprite = Assets.LoadPngAsSprite(LensIcon);
                if (sprite != null) shared.m_icons = new[] { sprite };
                else Plugin.Log.LogError(
                    $"[Trailborne/Sunstone] {LensName}: icon '{LensIcon}' did NOT load (missing PNG?). "
                    + "Crash-safe (magenta fallback) but ship the real PNG in assets/icons/items/.");
            }

            // World-drop visual: graft a small clear-crystal-ish blueprint mesh as a ZNetView-free
            // cosmetic child. Crystal is the cleanest clear-stone donor. Cosmetic-only — the item
            // is fully functional without it (logs-green≠playable — Daniel verifies the look).
            if (!Assets.TryGraftVisualSubtree("Crystal", "attach", go, "SBPR_SunstoneLensVisual", out _))
                Plugin.Log.LogWarning(
                    $"[Trailborne/Sunstone] {LensName}: world-drop visual graft from 'Crystal/attach' failed; "
                    + "dropped item will have no mesh this build. Functionally unaffected.");

            Assets.RegisterPrefabInZNetScene(go);
            Plugin.Log.LogInfo($"[Trailborne/Sunstone] Registered Sunstone Lens trinket: {LensName} (additive, Trinket slot, solar battery).");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — items + recipes
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            // Items into ODB (so recipes resolve + EquipItem can find the trinket prefab).
            foreach (var n in new[] { SunstoneName, LensName })
            {
                var p = zns?.GetPrefab(n);
                if (p != null) Assets.RegisterItemInObjectDB(p);
            }

            AddLensRecipe();   // Sunstone itself has no recipe — loot-sourced only (SunstoneLoot.cs)

            Plugin.Log.LogInfo("[Trailborne/Sunstone] Sunstone ObjectDB wiring complete (Sunstone material + Lens trinket + 1 recipe).");
        }

        private static void AddLensRecipe()
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;
            if (RecipeHelpers.HasRecipe(LensName)) return;

            var prefab = odb.GetItemPrefab(LensName);
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[Trailborne/Sunstone] {LensName} not in ODB at recipe time; skipping recipe.");
                return;
            }

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name              = "Recipe_" + LensName;
            recipe.m_item            = prefab.GetComponent<ItemDrop>();
            recipe.m_amount          = 1;
            recipe.m_minStationLevel = 1;
            recipe.m_craftingStation = RecipeHelpers.FindStation(Trailhead.ExplorersBenchName);
            // Sunstone ×2 + Iron ×1 + Guck ×3. Sunstone referenced via the const (never a
            // literal) so a rename can't drift the recipe; Sunstone registers into ODB earlier
            // in this same DoObjectDBWiring pass, so warn=true (a missing Sunstone SHOULD scream).
            recipe.m_resources = new[]
            {
                Assets.BuildReq(SunstoneName, LensSunstoneCost, "Sunstone"),
                Assets.BuildReq("Iron",       LensIronCost,     "Sunstone"),
                Assets.BuildReq("Guck",       LensGuckCost,     "Sunstone"),
            };
            odb.m_recipes.Add(recipe);
        }

        // ───────────────────────────────────────────────
        // EQUIPPED-LENS DETECTION (public API only — m_trinketItem is protected)
        // ───────────────────────────────────────────────

        /// <summary>
        /// The equipped Lens ItemData if <paramref name="player"/> currently wears one in the
        /// Trinket slot, else null. Reads the PUBLIC Inventory.GetEquippedItems() and matches
        /// each equipped item's m_dropPrefab name (clone-suffix-stripped) against
        /// <see cref="LensName"/>, gating on ItemType.Trinket — the same (item → m_dropPrefab.name)
        /// pair vanilla uses to wire trinket visuals. We do NOT touch the protected m_trinketItem
        /// field (it won't compile from outside Humanoid). Returning the ItemData (not just a bool)
        /// lets the HUD read the live m_durability charge.
        /// </summary>
        public static ItemDrop.ItemData? GetEquippedLens(Player player)
        {
            if (player == null) return null;
            var inv = player.GetInventory();
            if (inv == null) return null;

            foreach (var item in inv.GetEquippedItems())
            {
                if (item == null || item.m_shared == null) continue;
                if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Trinket) continue;
                var dropPrefab = item.m_dropPrefab;
                if (dropPrefab == null) continue;
                if (StripCloneSuffix(dropPrefab.name) == LensName)
                    return item;
            }
            return null;
        }

        /// <summary>True if the lens is worn AND has enough charge to detect (AC#5 inert-at-zero).</summary>
        public static bool IsLensActive(Player player)
        {
            var lens = GetEquippedLens(player);
            return lens != null && lens.m_durability >= MinChargeToDetect;
        }

        // Mirror of vanilla ItemDrop.GetPrefabName clone-suffix strip (:58940): cut at the first
        // '(' or ' ' so "SBPR_SunstoneLens(Clone)" matches LensName.
        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int i = name.IndexOfAny(new[] { '(', ' ' });
            return i >= 0 ? name.Substring(0, i) : name;
        }

        // ───────────────────────────────────────────────
        // RECHARGE PREDICATE (all vanilla, impl spec §3)
        // ───────────────────────────────────────────────

        /// <summary>
        /// The six independent sub-gates of <see cref="CanRecharge"/>, captured together so a diagnostic
        /// can report WHICH condition denied a recharge. The charge path is otherwise silent — a lens
        /// that "won't charge in daylight" gives no signal about which gate is false — so the DrainGate
        /// diagnostic logs this breakdown (card t_charge-diag). <see cref="All"/> is the recharge verdict.
        /// </summary>
        internal readonly struct RechargeGate
        {
            public readonly bool Daylight;      // EnvMan.IsDaylight()              (:81159)
            public readonly bool NotGlobalWet;  // !EnvMan.IsWet()                  (:81134, global s_isWet)
            public readonly bool NotEnvWet;     // !GetCurrentEnvironment().m_isWet (:80218)
            public readonly bool ClearName;     // IsClearWeatherName(env.m_name)   (config allowlist; empty ⇒ true)
            public readonly bool Outdoors;      // !Player.InShelter()              (:19375)
            public readonly bool NotSwamp;      // GetCurrentBiome() != Swamp       (:108730, Swamp=2)
            public readonly string EnvName;     // the live weather env name (diagnostic context; never null)

            public RechargeGate(bool daylight, bool notGlobalWet, bool notEnvWet, bool clearName,
                                bool outdoors, bool notSwamp, string envName)
            {
                Daylight = daylight; NotGlobalWet = notGlobalWet; NotEnvWet = notEnvWet;
                ClearName = clearName; Outdoors = outdoors; NotSwamp = notSwamp; EnvName = envName;
            }

            /// <summary>True ⇔ EVERY sub-gate passes — i.e. the lens recharges this tick.</summary>
            public bool All => Daylight && NotGlobalWet && NotEnvWet && ClearName && Outdoors && NotSwamp;
        }

        /// <summary>
        /// Read all six recharge sub-gates ONCE (the single source the verdict + the diagnostic share, so
        /// they can never drift). Null env/setup ⇒ an all-false gate (matches the old early-return-false
        /// behaviour). All base-game reads, line-cited on <see cref="RechargeGate"/>'s fields.
        /// </summary>
        internal static RechargeGate EvaluateRecharge(Player player)
        {
            var env = EnvMan.instance;
            if (player == null || env == null)
                return new RechargeGate(false, false, false, false, false, false, "<no env>");

            var setup = env.GetCurrentEnvironment();
            string envName = setup?.m_name ?? "<null env>";

            bool daylight     = EnvMan.IsDaylight();
            bool notGlobalWet = !EnvMan.IsWet();
            bool notEnvWet    = setup != null && !setup.m_isWet;
            bool clearName    = setup != null && IsClearWeatherName(setup.m_name);
            bool outdoors     = !player.InShelter();
            bool notSwamp     = player.GetCurrentBiome() != Heightmap.Biome.Swamp;

            return new RechargeGate(daylight, notGlobalWet, notEnvWet, clearName, outdoors, notSwamp, envName);
        }

        /// <summary>
        /// True when the lens should RECHARGE this tick: clear weather AND daylight AND not wet
        /// AND outdoors AND not in the Swamp (AC#2). Delegates to <see cref="EvaluateRecharge"/> so the
        /// verdict and the charge diagnostic read the SAME six gates (no drift). The Swamp is always-wet+
        /// overcast so notWet/clear already exclude it; the explicit Swamp guard is a self-documenting
        /// belt-and-suspenders invariant the card names.
        /// </summary>
        public static bool CanRecharge(Player player) => EvaluateRecharge(player).All;

        /// <summary>
        /// Whether <paramref name="envName"/> counts as "clear/sunny" weather. The env names are
        /// authored in Unity assets (not the decomp), so we do NOT hardcode a brittle allowlist:
        /// the load-bearing gate is m_isWet (checked by the caller). This adds an OPTIONAL
        /// allowlist from config — when the config list is empty (default) ANY non-wet env passes,
        /// so the predicate is driven purely by m_isWet + daylight. A server can pin specific clear
        /// env names if it wants to exclude e.g. a dry-but-dim weather.
        /// </summary>
        private static bool IsClearWeatherName(string envName)
        {
            var allow = Plugin.LensClearWeatherNames;
            if (allow == null || allow.Count == 0) return true;   // default: m_isWet is the only gate
            return allow.Contains(envName);
        }

        // ───────────────────────────────────────────────
        // DETECTION SWEEP (rune-of-detection behaviour from vanilla primitives, AC#3)
        // ───────────────────────────────────────────────

        /// <summary>
        /// Hostile characters within <paramref name="radius"/> of <paramref name="player"/>:
        /// Character.GetAllCharacters() (:10313) filtered to alive, non-player, and HOSTILE TO THE
        /// PLAYER per vanilla's own <c>BaseAI.IsEnemy(Character, Character)</c> (:4997). Using
        /// vanilla's enemy check (rather than a hand-rolled faction test) is the most faithful way
        /// to reproduce the Rune Magic "rune of detection" *behaviour* from vanilla primitives —
        /// it correctly handles tamed pets, shared groups, aggravation state, Dverger neutrality,
        /// Boss/PlayerSpawned edge cases, etc. (the faction matrix at :5034). No third-party code.
        /// Results are appended to <paramref name="results"/> (caller-owned list, reused to avoid
        /// per-sweep allocations).
        /// </summary>
        public static void GatherHostiles(Player player, float radius, List<Character> results)
        {
            results.Clear();
            if (player == null) return;
            Vector3 origin = player.transform.position;
            float r2 = radius * radius;

            var all = Character.GetAllCharacters();
            if (all == null) return;
            foreach (var c in all)
            {
                if (c == null || c.IsDead()) continue;
                if (c.IsPlayer()) continue;          // never reveal players
                // Vanilla's authoritative "is this creature an enemy of the player" check —
                // handles tamed/group/aggravation/faction-matrix correctly (AC#3).
                if (!BaseAI.IsEnemy(player, c)) continue;
                // Range — use transform.position (always valid) rather than GetCenterPoint
                // (m_collider.bounds.center, which can NRE before the collider is live).
                if ((c.transform.position - origin).sqrMagnitude > r2) continue;
                results.Add(c);
            }
        }

        // ───────────────────────────────────────────────
        // THE ENERGY GATE — Harmony Prefix on Humanoid.DrainEquipedItemDurability
        // ───────────────────────────────────────────────

        /// <summary>
        /// Takes over the per-tick durability change for OUR Sunstone Lens so it behaves as a solar
        /// battery instead of a wearing-out tool. Prefix on the PRIVATE
        /// <c>Humanoid.DrainEquipedItemDurability(ItemDrop.ItemData item, float dt)</c> (:13227),
        /// which vanilla calls from UpdateEquipment (:13198) for the trinket slot every tick.
        ///
        /// For the local player's Lens: drains at our fixed rate (or RECHARGES in the sun via
        /// <see cref="CanRecharge"/>), clamps to [0, max], and returns <c>false</c> to SKIP vanilla
        /// — so vanilla's "durability ≤ 0 → $msg_broke + Unequip + (destroy)" branch is NEVER
        /// reached (AC#5: inert, not consumed). Drain is constant, independent of detection (AC#4).
        ///
        /// Every other item, and every non-local Humanoid, returns <c>true</c> → vanilla runs
        /// unchanged. Fails OPEN (return true) on any error so a bug can't freeze a player's gear.
        /// Client-relevant only (UpdateEquipment's IsPlayer() gate); inert on the dedicated server.
        ///
        /// CLEAN-SIDE (ADR-0001): patches the base-game Humanoid only. Registered in Plugin.cs;
        /// PatchCheck asserts it wove.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), "DrainEquipedItemDurability")]
        public static class DrainGate
        {
            [HarmonyPrefix]
            public static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, float dt)
            {
                try
                {
                    if (item == null || item.m_shared == null) return true;
                    if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Trinket) return true;

                    // Only OUR lens. A loaded-from-save lens re-resolves m_dropPrefab by NAME in
                    // ItemDrop.Awake (:58696-58698) — if that name ever fails to resolve (orphaned
                    // instance, the "older spawns broke" hypothesis), this guard bails to vanilla and
                    // the lens silently never charges. The diagnostic below names exactly that case.
                    var dropPrefab = item.m_dropPrefab;
                    if (dropPrefab == null)
                    {
                        // Trinket with m_useDurability but a NULL dropPrefab — can't be matched by name.
                        // This is the prime suspect for "won't charge" on a save-loaded lens; surface it.
                        DebugLog(() => $"DrainGate: a Trinket reached the gate with m_dropPrefab=NULL "
                            + $"(name='{item.m_shared.m_name}', dur={item.m_durability:0.0}). Cannot identify as the "
                            + $"Lens by prefab name → vanilla owns it → NO charge. (orphaned/ unresolved prefab?)");
                        return true;
                    }
                    if (StripCloneSuffix(dropPrefab.name) != LensName) return true;

                    // Only the LOCAL player's lens (energy/recharge is a local-state concern; the
                    // server has no EnvMan-day/shelter context for a remote player here).
                    var player = __instance as Player;
                    if (player == null || player != Player.m_localPlayer) return true;

                    float max = item.GetMaxDurability();
                    float drainPerSec  = Plugin.LensDrainPerSec?.Value  ?? DefaultDrainPerSec;
                    float chargePerSec = Plugin.LensChargePerSec?.Value ?? DefaultChargePerSec;

                    // Read the six recharge sub-gates ONCE — the verdict (.All) drives charging; the same
                    // struct feeds the diagnostic so the log can NEVER disagree with the behaviour.
                    RechargeGate gate = EvaluateRecharge(player);

                    float before = item.m_durability;
                    float delta  = gate.All ? (chargePerSec * dt) : (-drainPerSec * dt);
                    item.m_durability = Mathf.Clamp(item.m_durability + delta, 0f, max);

                    // ── Charge diagnostic (card t_charge-diag): the charge path was previously SILENT, so a
                    //    "won't charge in daylight" report gave no way to see WHICH recharge gate denied it.
                    //    Throttled to ~1 Hz, this names every sub-gate's live value, the env name, and the
                    //    actual durability movement — so one playtest LogOutput.log pinpoints the false gate
                    //    (or proves the durability IS climbing and the bug is the BAR/persistence instead).
                    DebugLog(() =>
                        $"DrainGate[LOCAL lens]: charging={gate.All} dur {before:0.0}→{item.m_durability:0.0}/{max:0} "
                        + $"(Δ{delta:+0.000;-0.000}/tick, dt={dt:0.000}) | env='{gate.EnvName}' "
                        + $"daylight={gate.Daylight} notGlobalWet={gate.NotGlobalWet} notEnvWet={gate.NotEnvWet} "
                        + $"clearName={gate.ClearName} outdoors={gate.Outdoors} notSwamp={gate.NotSwamp}"
                        + (gate.All ? "" : $"  ← FALSE gate(s): {FalseGates(gate)}"));

                    // We fully own this item's durability — skip vanilla (no break/unequip/destroy).
                    return false;
                }
                catch (System.Exception e)
                {
                    // Fail OPEN: never let an energy bug freeze the player's equipment loop.
                    Plugin.Log.LogWarning($"[Trailborne/Sunstone] DrainGate error (failing open to vanilla): {e.Message}");
                    return true;
                }
            }

            // ── Diagnostic plumbing (card t_charge-diag) ─────────────────────────────────────────────
            // 1 Hz throttle so the per-tick prefix doesn't flood LogOutput.log. Gated by the live
            // Plugin.LensDebugCharge config (banner-windsock: flip on a joined client), else DefaultDebugCharge.
            // The message is built lazily (Func) so OFF costs nothing — no string work on the hot path.
            private static float _nextChargeLog;

            internal static bool DebugChargeEnabled =>
                Plugin.LensDebugCharge?.Value ?? DefaultDebugCharge;

            private static void DebugLog(System.Func<string> message)
            {
                if (!DebugChargeEnabled) return;
                float now = Time.realtimeSinceStartup;
                if (now < _nextChargeLog) return;       // throttle: at most ~1 line/sec
                _nextChargeLog = now + 1f;
                Plugin.Log.LogInfo($"[Trailborne/Sunstone] {message()}");
            }

            /// <summary>Comma-list of the sub-gates that are FALSE (the deny reason), for the diagnostic.</summary>
            private static string FalseGates(in RechargeGate g)
            {
                var sb = new System.Text.StringBuilder();
                void add(bool ok, string name) { if (!ok) { if (sb.Length > 0) sb.Append(", "); sb.Append(name); } }
                add(g.Daylight, "daylight");
                add(g.NotGlobalWet, "globalWet");
                add(g.NotEnvWet, "envWet");
                add(g.ClearName, "clearName");
                add(g.Outdoors, "inShelter");
                add(g.NotSwamp, "inSwamp");
                return sb.Length == 0 ? "(none)" : sb.ToString();
            }
        }
    }
}
