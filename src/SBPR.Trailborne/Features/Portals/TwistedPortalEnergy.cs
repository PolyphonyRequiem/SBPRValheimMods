using System;
using System.Collections.Generic;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Portals
{
    /// <summary>
    /// The food-as-fuel Portal Energy cost engine for the Twisted Portal (spec §5).
    ///
    /// 🔄 THIS IS CARD C2 (t_6e992a30) — the real food-as-fuel debit, replacing the C1
    /// loud-free-pass seam. The CONTRACT set by C1 (t_2b388cd5) is unchanged: distance
    /// <c>D</c> (meters) in → <see cref="JumpResult"/> { Ok, Reason, BurnedBerries } out.
    /// <see cref="SBPR_TwistedPortal.Teleport"/> calls this; it does NOT re-derive the teleport.
    ///
    /// ════════════════════════════════════════════════════════════════════════════════════════
    /// THE SPLIT (engine I/O here, pure math in PortalEnergyMath.cs):
    ///   • <see cref="PortalEnergyMath"/> (engine-free, CI-gated AT-PE-MATH) owns the tier curve,
    ///     the PE sum, the feast range-clock, the distance→food-time debit, and the berry-shortfall
    ///     solve — all pure float math, no UnityEngine/Valheim types.
    ///   • THIS class owns the engine I/O the math can't touch: read <c>Player.GetFoods()</c>
    ///     (decomp :17598), build the engine-free <see cref="PeSlot"/> snapshot off each slot's BASE
    ///     stat budget (m_food + m_foodStamina + m_foodEitr — NOT the decayed live stat, spec §5.1 🔴)
    ///     and remaining minutes (m_time/60), feed it to <see cref="PortalEnergyMath.SolveJump"/>,
    ///     then APPLY the verdict: shorten each slot's <c>m_time</c>, burn Bukeberries from the
    ///     inventory, and apply the vanilla Feeling Sick (<c>SE_Puke</c>) effect on a berry jump.
    ///
    /// PATCH-FREE BY CONSTRUCTION (spec §5.1 / §8): PE is read on demand at teleport time; there is
    /// NO Harmony patch anywhere in the cost model (the old key model needed three).
    ///
    /// 🔴 GROUNDING CORRECTIONS vs the C1 seam hints (verified against the decomp this pass):
    ///   • <c>Inventory.CountItems(string)</c>/<c>RemoveItem(string)</c> match on
    ///     <c>m_shared.m_name</c> (the localization TOKEN "$item_pukeberries"), NOT the prefab name.
    ///     So we count/remove Bukeberries by enumerating <c>GetAllItems()</c> and matching
    ///     <c>m_dropPrefab.name == "Pukeberries"</c> (the robust prefab-name path, the equipped-
    ///     accessory-detection idiom in the valheim-mod-development skill).
    ///   • There is NO $sbpr_* localization registration layer in this repo (the SurveyorTableTag /
    ///     SBPR_TwistedPortal center-message precedent). A custom token would leak on-screen as a
    ///     literal "[sbpr_...]" — so the block message is plain English, not "$sbpr_twisted_no_fuel".
    /// ════════════════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static class TwistedPortalEnergy
    {
        // ── Vanilla Bukeberry (Pukeberries) prefab name — the burnable emergency reserve (spec §5.4;
        //    corpus-verified internal id, ~/valheim/sbpr-corpus/wiki/fandom/Pukeberries.md). LOCK. ──
        public const string BukeberryPrefabName = "Pukeberries";

        // ── The Feast prefab family — the feast discriminator (spec §5.6). Both the biome feast and
        //    its _Material twin start with "Feast" (corpus prefab-list: FeastMeadows … FeastAshlands),
        //    so a prefix match is the robust shared-name check the spec names. LOCK. ──
        public const string FeastPrefabPrefix = "Feast";

        /// <summary>
        /// The seam's return type (spec §4.4):
        ///   • <see cref="Ok"/> — did the player have enough fuel (belly + berry reserve)? When false,
        ///     C1 blocks the teleport and shows <see cref="Reason"/>; nothing is spent.
        ///   • <see cref="Reason"/> — the block message (plain English; no $sbpr_* layer exists).
        ///     Only meaningful when <see cref="Ok"/> is false.
        ///   • <see cref="BurnedBerries"/> — how many Bukeberries the jump consumed for the shortfall
        ///     (0 = belly covered it). &gt; 0 ⇒ a Feeling-Sick-on-arrival jump (spec §5.5), already
        ///     APPLIED by this engine (C1 only logs it).
        /// </summary>
        public struct JumpResult
        {
            public bool Ok;
            public string Reason;
            public int BurnedBerries;

            public static JumpResult Blocked(string reason) => new JumpResult { Ok = false, Reason = reason, BurnedBerries = 0 };
            public static JumpResult Spent(int burnedBerries) => new JumpResult { Ok = true, Reason = string.Empty, BurnedBerries = burnedBerries };
        }

        /// <summary>
        /// 🟥 LOOK-TO-AIM (card t_f4d0d5e1 / L1): the READ-ONLY food-impact preview for the pre-commit
        /// readout (spec §5 "Look-to-aim addition" / Beat 3). Daniel's model shows the player the food
        /// cost of the AIMED jump BEFORE committing — so this is a NON-MUTATING sibling of the result:
        /// the same belly snapshot + the same non-mutating <see cref="PortalEnergyMath.SolveJump"/>, but
        /// it STOPS before any debit (no <c>m_time</c> drain, no berry burn, no <c>SE_Puke</c>).
        ///
        /// L1 SHIPS THIS SEAM (the L3 dependency line names "the PreviewJump seam"); L3 (t_d9ea1b2c)
        /// RENDERS it on the aimed destination label. The fields mirror what the overlay shows: belly
        /// range vs the jump distance, the shortfall, the berries the shortfall would need, and whether
        /// the player actually holds enough Bukeberries to make the jump (so the preview can read
        /// "reachable" vs "need N more berries"). Read-only — calling this spends nothing (AT-FOOD-PREVIEW).
        /// </summary>
        public readonly struct JumpPreview
        {
            /// <summary>The aimed jump distance (metres) this preview was computed for.</summary>
            public readonly float DistanceMeters;
            /// <summary>belly_range in metres (the distance the belly food alone can cover).</summary>
            public readonly float BellyRangeMeters;
            /// <summary>True when the belly alone covers the jump (zero berries needed).</summary>
            public readonly bool BellyCovers;
            /// <summary>Distance past belly_range (0 when the belly covers it).</summary>
            public readonly float ShortfallMeters;
            /// <summary>Bukeberries the shortfall would need (0 when the belly covers it).</summary>
            public readonly int BerriesNeeded;
            /// <summary>Bukeberries the player currently holds (so the preview can show "have N of M").</summary>
            public readonly int BerriesHeld;
            /// <summary>True when the jump is makeable right now (belly covers it, OR the player holds
            /// enough berries for the shortfall). The overlay reads green vs red off this.</summary>
            public readonly bool Reachable;

            public JumpPreview(float distanceMeters, float bellyRangeMeters, bool bellyCovers,
                float shortfallMeters, int berriesNeeded, int berriesHeld, bool reachable)
            {
                DistanceMeters = distanceMeters;
                BellyRangeMeters = bellyRangeMeters;
                BellyCovers = bellyCovers;
                ShortfallMeters = shortfallMeters;
                BerriesNeeded = berriesNeeded;
                BerriesHeld = berriesHeld;
                Reachable = reachable;
            }
        }

        /// <summary>
        /// READ-ONLY food-impact preview of a jump of <paramref name="distanceMeters"/> for
        /// <paramref name="player"/> (spec §5 Beat 3) — the non-mutating sibling of
        /// <see cref="TrySpendForJump"/>. Snapshots the belly + Bukeberry count and solves the jump
        /// with the SAME pure <see cref="PortalEnergyMath.SolveJump"/>, then returns a
        /// <see cref="JumpPreview"/> and STOPS — no debit, no berry burn, no SE_Puke. Calling this
        /// spends nothing (AT-FOOD-PREVIEW). L3 renders the result on the aimed destination label.
        /// </summary>
        public static JumpPreview PreviewJump(Player player, float distanceMeters)
        {
            if (player == null)
                return new JumpPreview(distanceMeters, 0f, false, distanceMeters, 0, 0, false);

            PortalEnergyKnobs knobs = ResolveKnobs();
            PeSlot[] slots = SnapshotBelly(player.GetFoods());
            JumpSolution sol = PortalEnergyMath.SolveJump(slots, distanceMeters, knobs);

            int berriesNeeded = sol.BerriesNeeded;
            int berriesHeld = berriesNeeded > 0 ? CountBukeberries(player) : 0;
            bool reachable = berriesNeeded == 0 || berriesHeld >= berriesNeeded;

            return new JumpPreview(
                distanceMeters,
                sol.BellyRangeMeters,
                sol.BellyCovers,
                sol.ShortfallMeters,
                berriesNeeded,
                berriesHeld,
                reachable);
        }

        /// <summary>
        /// Build the engine-free <see cref="PeSlot"/>[] snapshot off the live belly (spec §5.1). Reads
        /// each slot's BASE stat budget (<c>m_food + m_foodStamina + m_foodEitr</c> — NOT the decayed
        /// live stat, the §5.1 🔴 double-count trap) + remaining minutes (<c>m_time/60</c>) + the feast
        /// flag. Shared by the debit (<see cref="TrySpendForJump"/>) and the read-only preview
        /// (<see cref="PreviewJump"/>) so the two can never compute a different belly.
        /// </summary>
        private static PeSlot[] SnapshotBelly(List<Player.Food>? foods)
        {
            int slotCount = foods?.Count ?? 0;
            var slots = new PeSlot[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                Player.Food f = foods![i];
                ItemDrop.ItemData? item = f?.m_item;
                ItemDrop.ItemData.SharedData? shared = item?.m_shared;

                // BASE stat budget (NOT the decayed live f.m_health/m_stamina/m_eitr — spec §5.1 🔴).
                float baseStats = shared != null
                    ? shared.m_food + shared.m_foodStamina + shared.m_foodEitr
                    : 0f;
                float realMinutes = (f?.m_time ?? 0f) / 60f;
                bool isFeast = IsFeastFood(item);

                slots[i] = new PeSlot(baseStats, realMinutes, isFeast);
            }
            return slots;
        }

        /// <summary>Resolve the live cost-model knobs from BepInEx config (Plugin.*), falling back to the
        /// locked baselines (<see cref="PortalEnergyMath"/> Default* consts) in a no-Plugin unit context —
        /// the Sunstone Lens "?.Value ?? Default" idiom, single source of truth in the engine-free core.</summary>
        public static PortalEnergyKnobs ResolveKnobs()
        {
            return new PortalEnergyKnobs(
                tierDivisor:          Plugin.PeTierDivisor?.Value          ?? PortalEnergyMath.DefaultTierDivisor,
                tierClampLo:          Plugin.PeTierClampLo?.Value          ?? PortalEnergyMath.DefaultTierClampLo,
                tierClampHi:          Plugin.PeTierClampHi?.Value          ?? PortalEnergyMath.DefaultTierClampHi,
                metersPerPe:          Plugin.PeMetersPerPe?.Value          ?? PortalEnergyMath.DefaultMetersPerPe,
                feastRangeCapMinutes: Plugin.PeFeastRangeCapMinutes?.Value ?? PortalEnergyMath.DefaultFeastRangeCapMinutes,
                bukeMetersPerBerry:   Plugin.PeBukeMetersPerBerry?.Value   ?? PortalEnergyMath.DefaultBukeMetersPerBerry);
        }

        /// <summary>
        /// Gate + debit the food-as-fuel cost of a jump of <paramref name="distanceMeters"/> for
        /// <paramref name="player"/> (spec §5). The whole model in one call:
        ///   1. Snapshot the belly: <c>Player.GetFoods()</c> → <see cref="PeSlot"/>[] (base stats + real minutes + feast flag).
        ///   2. <see cref="PortalEnergyMath.SolveJump"/> → belly range, whether it covers D, berries for the shortfall, per-slot drain.
        ///   3. If a shortfall needs more Bukeberries than the player holds → BLOCK (spend nothing).
        ///   4. Otherwise DEBIT: shorten each slot's <c>m_time</c> by the solved minutes, force a food
        ///      refresh so Max HP/Stamina/Eitr drop (arrive depleted), burn the berries, apply SE_Puke
        ///      on a berry jump, and return <see cref="JumpResult.Spent"/>.
        /// </summary>
        /// <param name="player">The traveling player (never null when C1 calls — guarded upstream).</param>
        /// <param name="distanceMeters">The jump distance D, computed by C1 from the resolved destination (spec §4.4).</param>
        public static JumpResult TrySpendForJump(Player player, float distanceMeters)
        {
            if (player == null) return JumpResult.Blocked("No traveler");

            PortalEnergyKnobs knobs = ResolveKnobs();

            // ── 1) Snapshot the live belly (the PE read surface, decomp :17598). The Food list is the
            //       AUTHORITATIVE per-slot remaining-time + base-stat source; we read, never patch. ──
            List<Player.Food> foods = player.GetFoods();
            PeSlot[] slots = SnapshotBelly(foods);

            // ── 2) Solve the jump (pure math — AT-PE-MATH gates this in CI). ──
            JumpSolution sol = PortalEnergyMath.SolveJump(slots, distanceMeters, knobs);

            // ── 3) Berry gate: if the shortfall needs more Bukeberries than the player holds, BLOCK
            //       and spend NOTHING (no partial drain — the jump simply doesn't happen, spec §5.4). ──
            int berriesNeeded = sol.BerriesNeeded;
            if (berriesNeeded > 0)
            {
                int held = CountBukeberries(player);
                if (held < berriesNeeded)
                {
                    // Plain English — there is no $sbpr_* localization layer in this repo (a custom
                    // token leaks as a literal). NON-EXPLICIT about berries (spec §5.7 — the reserve is
                    // a whispered feature; the block line must not tutorialize "burn 10 Bukeberries").
                    return JumpResult.Blocked("Not enough provisions to travel that far");
                }
            }

            // ── 4) DEBIT — the point of no return. Apply the food-time drain first (always), then burn
            //       berries + apply Feeling Sick only on a berry jump. ──
            ApplyFoodTimeDrain(player, foods, sol);

            if (berriesNeeded > 0)
            {
                int actuallyBurned = RemoveBukeberries(player, berriesNeeded);
                // Apply Feeling Sick (vanilla SE_Puke) on arrival — read the effect off the berry's own
                // m_consumeStatusEffect so we reuse the exact vanilla effect, no hardcoded hash (spec §5.5).
                ApplyFeelingSick(player);
                Plugin.Log.LogInfo(
                    $"[Trailborne/TwistedPortal] Food-as-fuel jump: {distanceMeters:F0} m, belly range " +
                    $"{sol.BellyRangeMeters:F0} m (PE {sol.BellyPe:F1}); shortfall {sol.ShortfallMeters:F0} m " +
                    $"→ burned {actuallyBurned}/{berriesNeeded} Bukeberries, arrive Feeling Sick + food-empty.");
                return JumpResult.Spent(actuallyBurned);
            }

            Plugin.Log.LogInfo(
                $"[Trailborne/TwistedPortal] Food-as-fuel jump: {distanceMeters:F0} m covered by belly " +
                $"(range {sol.BellyRangeMeters:F0} m, PE {sol.BellyPe:F1}); no berries, arrive depleted by distance.");
            return JumpResult.Spent(0);
        }

        // ════════════════════════════════════════════════════════════════════════════════════
        // ENGINE I/O — the parts PortalEnergyMath can't touch (Unity/Valheim types).
        // ════════════════════════════════════════════════════════════════════════════════════

        /// <summary>True when a food slot is a vanilla feast (the <c>Feast*</c> prefab family — spec §5.6).
        /// Matches on <c>m_dropPrefab.name</c> (stripped of any "(Clone)" suffix) so a world-instantiated
        /// drop and the ODB prefab both resolve, the equipped-accessory-detection idiom.</summary>
        private static bool IsFeastFood(ItemDrop.ItemData? item)
        {
            GameObject? drop = item?.m_dropPrefab;
            if (drop == null) return false;
            string name = drop.name;
            // Vanilla never appends "(Clone)" to a Food's m_dropPrefab (it's the ODB prefab reference),
            // but strip defensively to match the GetPrefabName idiom used elsewhere in the repo.
            int paren = name.IndexOf('(');
            if (paren >= 0) name = name.Substring(0, paren).Trim();
            return name.StartsWith(FeastPrefabPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Shorten each slot's live <c>m_time</c> by the solved real-minutes drain (×60 → seconds), then
        /// force a food refresh so the next-tick stat recompute drops Max HP/Stamina/Eitr — the player
        /// visibly weakens (arrive depleted, spec §5.3). Slots driven to ≤0 are removed by vanilla's own
        /// <c>UpdateFood</c> on the forced refresh (decomp :17540-:17545), so a fully-drained belly empties.
        /// </summary>
        private static void ApplyFoodTimeDrain(Player player, List<Player.Food>? foods, in JumpSolution sol)
        {
            if (foods == null) return;
            float[] removed = sol.MinutesRemovedPerSlot;
            int n = Math.Min(foods.Count, removed?.Length ?? 0);
            for (int i = 0; i < n; i++)
            {
                Player.Food f = foods[i];
                if (f == null) continue;
                float removeSeconds = removed![i] * 60f;
                if (removeSeconds <= 0f) continue;
                f.m_time -= removeSeconds;
                if (f.m_time < 0f) f.m_time = 0f;
            }

            // Force the vanilla per-second food recompute (decomp :17526, called with forceUpdate:true at
            // :17492/:17508): re-derives Max HP/Stamina/Eitr from the shortened slots and removes any slot
            // that hit 0. UpdateFood is private — reach it via cached reflection (the repo's AccessTools /
            // GetMethod idiom: Assets.cs, SurveyorTableTag.cs). Fail-soft: if the method can't be resolved
            // the drain still persisted to m_time and the next natural 1 Hz tick applies it (≤1 s late).
            ForceFoodRefresh(player);
        }

        // Cached reflection handle for the private Player.UpdateFood(float dt, bool forceUpdate) (:17526).
        private static System.Reflection.MethodInfo? _updateFood;
        private static bool _updateFoodResolved;

        private static void ForceFoodRefresh(Player player)
        {
            try
            {
                if (!_updateFoodResolved)
                {
                    _updateFoodResolved = true;
                    _updateFood = typeof(Player).GetMethod(
                        "UpdateFood",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                        binder: null,
                        types: new[] { typeof(float), typeof(bool) },
                        modifiers: null);
                    if (_updateFood == null)
                        Plugin.Log.LogWarning(
                            "[Trailborne/TwistedPortal] Could not resolve Player.UpdateFood(float,bool) — the " +
                            "arrive-depleted stat recompute will lag one natural food tick (≤1 s). Food-time drain " +
                            "itself still applied. (Decomp drift? re-check :17526.)");
                }
                _updateFood?.Invoke(player, new object[] { 0f, true });
            }
            catch (Exception e)
            {
                // Non-fatal: the m_time drain already persisted; vanilla's own 1 Hz UpdateFood will
                // re-derive the maxes within a second. Never let a reflection hiccup brick the teleport.
                Plugin.Log.LogWarning($"[Trailborne/TwistedPortal] ForceFoodRefresh failed (non-fatal): {e.Message}");
            }
        }

        /// <summary>Count Bukeberries in the player's inventory by PREFAB name (not the localized
        /// m_shared.m_name the vanilla CountItems(string) overload matches — spec-hint correction).</summary>
        private static int CountBukeberries(Player player)
        {
            Inventory? inv = player.GetInventory();
            if (inv == null) return 0;
            int total = 0;
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
            {
                if (item == null) continue;
                if (MatchesBukeberry(item)) total += item.m_stack;
            }
            return total;
        }

        /// <summary>Remove <paramref name="amount"/> Bukeberries by PREFAB name, walking stacks. Returns the
        /// number actually removed (== amount when the gate in TrySpendForJump confirmed enough were held).
        /// Mutates stacks directly + flags the inventory Changed() so the client UI + ZDO sync update.</summary>
        private static int RemoveBukeberries(Player player, int amount)
        {
            Inventory? inv = player.GetInventory();
            if (inv == null || amount <= 0) return 0;

            int remaining = amount;
            // Snapshot the list first (we mutate stacks / remove items as we go).
            var matching = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
                if (item != null && MatchesBukeberry(item)) matching.Add(item);

            foreach (ItemDrop.ItemData item in matching)
            {
                if (remaining <= 0) break;
                int take = Math.Min(item.m_stack, remaining);
                // RemoveItem(ItemData, amount) (decomp :56922) decrements the stack + fires Changed(),
                // and drops the item when the stack hits 0 — the clean vanilla path (matches by reference,
                // so the localized-name mismatch that bites the string overload doesn't apply here).
                inv.RemoveItem(item, take);
                remaining -= take;
            }
            return amount - remaining;
        }

        /// <summary>Match an inventory item to the Bukeberry by its drop-prefab name (robust to the
        /// "(Clone)" suffix), NOT the localized m_shared.m_name.</summary>
        private static bool MatchesBukeberry(ItemDrop.ItemData item)
        {
            GameObject? drop = item.m_dropPrefab;
            if (drop == null) return false;
            string name = drop.name;
            int paren = name.IndexOf('(');
            if (paren >= 0) name = name.Substring(0, paren).Trim();
            return string.Equals(name, BukeberryPrefabName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Apply the vanilla Feeling Sick (<c>SE_Puke</c>) effect on arrival from a berry-burning jump
        /// (spec §5.5). We read the effect off the Bukeberry's OWN <c>m_shared.m_consumeStatusEffect</c>
        /// (the exact StatusEffect the berry applies on consumption, decomp :20939) and add it via
        /// <c>SEMan.AddStatusEffect(StatusEffect, resetTime:true)</c> (:24381) — reusing the vanilla SE,
        /// never reimplementing or hardcoding a hash. Falls back to the ObjectDB "Puke" lookup if the
        /// berry prefab can't be read. Fail-soft: a missing effect logs but never blocks the teleport.
        /// </summary>
        private static void ApplyFeelingSick(Player player)
        {
            try
            {
                SEMan seman = player.GetSEMan();
                if (seman == null) return;

                StatusEffect? puke = ResolvePukeEffect();
                if (puke == null)
                {
                    Plugin.Log.LogWarning(
                        "[Trailborne/TwistedPortal] Could not resolve SE_Puke (Feeling Sick) — berry jump will " +
                        "land food-empty but WITHOUT the arrival debuff. (ObjectDB missing 'Puke'? decomp drift?)");
                    return;
                }
                seman.AddStatusEffect(puke, resetTime: true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/TwistedPortal] ApplyFeelingSick failed (non-fatal): {e.Message}");
            }
        }

        // Cached SE_Puke prefab-status name hash. Vanilla's SE_Puke status-effect asset is named "Puke"
        // (the StatusEffect.m_name/asset name; the on-screen label is the localized "Feeling sick").
        private const string PukeStatusName = "Puke";

        /// <summary>Resolve the vanilla Feeling Sick StatusEffect. Primary path: read it off a live
        /// Bukeberry prefab's <c>m_consumeStatusEffect</c> (the exact effect the berry carries). Fallback:
        /// ObjectDB.GetStatusEffect("Puke".GetStableHashCode()).</summary>
        private static StatusEffect? ResolvePukeEffect()
        {
            ObjectDB odb = ObjectDB.instance;
            if (odb == null) return null;

            // Primary: the Bukeberry's own consume-effect (decomp :20939 path) — guaranteed to be the
            // exact SE_Puke the berry applies, no name guessing.
            GameObject berry = odb.GetItemPrefab(BukeberryPrefabName);
            ItemDrop? berryDrop = berry != null ? berry.GetComponent<ItemDrop>() : null;
            StatusEffect? consume = berryDrop?.m_itemData?.m_shared?.m_consumeStatusEffect;
            if (consume != null) return consume;

            // Fallback: ObjectDB status-effect registry by asset name hash.
            return odb.GetStatusEffect(PukeStatusName.GetStableHashCode());
        }
    }
}
