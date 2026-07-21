using System;
using System.Collections.Generic;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Cooking;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Features.Progression;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Cooking
{
    /// <summary>
    /// T018 — the net48 runtime seam that makes Iron Stomach actually raise the food refresh threshold on
    /// a joined client. Iron Stomach is a PERSONAL Permanent Effect (data-model.md §"Fixed first-build
    /// roster": Cooking | 1 | Iron Stomach | Permanent Effect | personal Offered); per data-model.md it
    /// "survive[s] relationship loss and Tree revocation", so it keys ONLY on the character's durable
    /// purchase — no relationship/policy/permission/Stone-development conjunct (spec §US4 sc1 "Iron Stomach
    /// permanently permits food refresh/replacement at 75% remaining"; contracts.md §Cooking
    /// "FoodRefreshThresholdProvider: Iron Stomach supplies threshold 0.75, highest applicable provider
    /// wins; three slots and normal food debit remain").
    ///
    /// WHAT THIS BRIDGES (decomp seam — vanilla is fair game to read/adapt, AGENTS.md / ADR-0001):
    ///   * <c>Player.CanEat(ItemDrop.ItemData item, bool showMessages)</c> — the gate <c>Player.EatFood</c>
    ///     and <c>Player.CanConsumeItem</c> consult before allowing a food to be (re-)eaten. Vanilla's
    ///     per-food "already have this, can I refresh it?" decision is <c>Player.Food.CanEatAgain()</c> ==
    ///     <c>m_time &lt; m_foodBurnTime / 2f</c> — i.e. refresh allowed only once the remaining fraction
    ///     drops below 0.5 (the vanilla baseline threshold). The CanEat postfix RESCUES a vanilla FALSE to
    ///     TRUE when (and only when) the refusal was caused solely by an ALREADY-PRESENT matching food whose
    ///     remaining fraction is at or below the Iron Stomach threshold (0.75) but above the vanilla 0.5 —
    ///     the exact "refresh at 75% remaining" band. This unlocks the CanConsumeItem entry so the eat can
    ///     proceed; above 0.75 CanEat is NOT rescued, so CanConsumeItem still denies and no item is debited.
    ///   * <c>Player.EatFood(ItemDrop.ItemData item)</c> — the INNER guard. Rescuing CanEat is NOT enough:
    ///     vanilla EatFood INDEPENDENTLY re-checks <c>Food.CanEatAgain()</c> at the same hardcoded 0.5 inside
    ///     its same-food branch (decomp: <c>if (food2.CanEatAgain()) { refresh; return true; } return false;</c>).
    ///     So at, e.g., 60% remaining, the rescued CanEat lets <c>Humanoid.ConsumeItem</c> pass and debit the
    ///     item via <c>inventory.RemoveOneItem</c>, but EatFood returns false WITHOUT refreshing — the item
    ///     is consumed with no effect (the shipped defect, live-QA proven). The EatFood PREFIX closes this:
    ///     when the durable Iron Stomach projection says the matching food is in the raised 0.5..0.75 band,
    ///     it performs EXACTLY the refresh vanilla runs below 0.5 (reset the matching slot's
    ///     m_time/health/stamina/eitr from the item, forceUpdate the food), reports success, and skips
    ///     vanilla so the single debit proceeds once. When vanilla already PASSES (below 0.5) or should DENY
    ///     (above 0.75) the prefix does nothing and vanilla runs unchanged. We never add a fourth slot, never
    ///     touch the three-slot cap or the most-depleted replacement, and mutate only the already-present
    ///     matching slot — the identical fields vanilla's below-0.5 branch writes, only the THRESHOLD moved.
    ///
    /// PRESERVED INVARIANTS (contracts.md "three slots and normal food debit remain"; research.md
    /// "preserves three slots, debit, stats, and duration"): this seam changes ONLY the refresh-threshold
    /// decision. It does not add a fourth slot (the <c>m_foods.Count &gt;= 3</c> "different food" refusal is
    /// left intact — Iron Stomach only refreshes a food ALREADY in a slot), and it mutates no
    /// <c>m_time</c>/<c>m_health</c>/<c>m_stamina</c>/<c>m_eitr</c> — the actual debit/stats/duration on
    /// eat run entirely in vanilla <c>EatFood</c>.
    ///
    /// SINGLE AUTHORITY: the acquired/threshold decision routes through the shipped, unit-tested pure
    /// <see cref="FoodRefreshThresholdProvider"/>. This patch is a THIN adapter: it re-checks the exact
    /// vanilla refresh predicate with the provider's resolved threshold and holds no parallel ledger.
    ///
    /// ACTIVATION SOURCE (fail closed, honest transport scope): Iron Stomach is a personal Permanent
    /// Effect, and the bounded server→client delivery transport that Savor / Practice Range / Refined
    /// Workshop use carries LOCAL-effect snapshots only — there is not yet a personal-effect replication
    /// channel. So this gate reads the authoritative projection where it EXISTS in-process: on the
    /// authoritative HOST (listen-server / singleplayer host) the composed
    /// <see cref="LocalProgressionObserver.Server"/> holds the character store, and the gate resolves the
    /// acting occupant's durable Iron Stomach purchase straight from it through
    /// <see cref="FoodRefreshThresholdProvider.Resolve"/>. On a PURE remote client the server runtime is
    /// null and there is no personal-effect snapshot to consume, so the gate FAILS CLOSED (foods keep the
    /// vanilla 0.5 threshold) rather than inventing an unauthenticated grant. The proven topology for T018
    /// is therefore the host occupant; a personal-effect client delivery channel is a separate follow-up,
    /// exactly as the sibling Field Prep / Field Fletching / Refined Workshop seams documented their
    /// host-only scope.
    ///
    /// References Valheim (Player, ItemDrop, ZNet) → net48-only, NOT link-compiled into net8. The pure
    /// provider it drives is fully unit-tested. Clean-side (ADR-0001): base-game types only.
    /// </summary>
    [HarmonyPatch]
    internal static class IronStomachRefreshGate
    {
        // Reuse ONE provider instance for the process; it is a pure stateless projection.
        private static readonly FoodRefreshThresholdProvider Provider = new FoodRefreshThresholdProvider();

        /// <summary>Postfix on the food-eat gate. When vanilla refused because a MATCHING food already in a
        /// slot is not yet below the vanilla 0.5 refresh threshold, and Iron Stomach is durably acquired by
        /// the local occupant, re-evaluate that food's remaining fraction against the raised threshold
        /// (0.75) and rescue the result to TRUE if it now qualifies. We never override a vanilla PASS and
        /// never touch the three-slot "different food, slots full" refusal.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.CanEat))]
        [HarmonyPostfix]
        private static void CanEat_Postfix(Player __instance, ItemDrop.ItemData item, ref bool __result)
        {
            try
            {
                if (__result) return;                               // vanilla already allowed it — never override.
                if (__instance == null || item == null || item.m_shared == null) return;
                if (__instance != Player.m_localPlayer) return;     // client decision, local player only.

                // Find the already-present matching food (same as vanilla's first loop). Only a food the
                // player ALREADY holds is subject to the refresh threshold; a brand-new food that hit the
                // three-slot cap is NOT rescued (slots preserved).
                var foods = __instance.GetFoods();
                if (foods == null) return;
                Player.Food match = null!;
                bool found = false;
                foreach (var f in foods)
                {
                    if (f != null && f.m_item != null && f.m_item.m_shared != null &&
                        f.m_item.m_shared.m_name == item.m_shared.m_name)
                    {
                        match = f;
                        found = true;
                        break;
                    }
                }
                if (!found) return;                                 // refusal was not a same-food refresh case.

                float burn = match.m_item.m_shared.m_foodBurnTime;
                if (burn <= 0f) return;
                float remainingFraction = Mathf.Clamp01(match.m_time / burn);

                // Single authority: route the rescue through the EXACT same band-aware DecideEat the inner
                // EatFood prefix uses. This is load-bearing at the vanilla baseline boundary — a NON-acquirer
                // MUST preserve vanilla's STRICT refusal at exactly 0.5 (Food.CanEatAgain is m_time < burn/2),
                // so the postfix must NOT rescue. Using the raw inclusive FoodRefreshCapability.CanRefresh here
                // would wrongly rescue a non-acquirer at exactly 0.5 (None's threshold is 0.5 and CanRefresh is
                // <=), debiting without a refresh. DecideEat returns PassThroughToVanilla for any non-acquirer
                // and RescueSameFoodRefresh ONLY for an acquired owner in the inclusive 0.5..0.75 band, so the
                // outer and inner guards agree on one boundary.
                if (ResolveRescueForLocalOccupant(remainingFraction))
                    __result = true;                                // refresh at up to 75% remaining while acquired.
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Cooking] Iron Stomach refresh gate postfix threw (ignored): " + ex.Message);
            }
        }

        /// <summary>Resolve whether the local occupant's eat attempt at the given remaining fraction must be
        /// RESCUED to a same-food refresh under a durably-acquired Iron Stomach, from the authoritative HOST
        /// projection (the character's durable purchase record, via the shipped
        /// <see cref="FoodRefreshThresholdProvider"/>). Uses the SAME band-aware
        /// <see cref="FoodRefreshThresholdProvider.DecideEat(CharacterProgressionAggregate, bool, double)"/>
        /// as the inner <see cref="EatFood_Prefix"/> so both guards agree at the exact vanilla baseline
        /// boundary: a non-acquirer never rescues (vanilla's strict 0.5 refusal stands), and an acquired owner
        /// rescues only in the inclusive 0.5..0.75 band. Fail closed: no server runtime (pure client),
        /// unresolvable identity, or absent character aggregate ⇒ false. No client-supplied claim is ever
        /// trusted.</summary>
        private static bool ResolveRescueForLocalOccupant(float remainingFraction)
        {
            var character = ResolveLocalOccupantCharacter();
            if (character == null) return false;

            // Single authority: identical band-aware decision the inner-guard prefix delegates to. A matching
            // food is present by construction (the postfix only reaches here after locating one).
            return Provider.DecideEat(character, matchingFoodPresent: true, remainingFraction)
                   == IronStomachEatDisposition.RescueSameFoodRefresh;
        }

        /// <summary>Prefix on <c>Player.EatFood</c> — the INNER-GUARD fix. The outer <see cref="CanEat_Postfix"/>
        /// only rescues <c>Player.CanEat</c>, but vanilla <c>Player.EatFood</c> INDEPENDENTLY re-checks
        /// <c>Player.Food.CanEatAgain()</c> (== remaining &lt; 0.5) inside its same-food branch and refuses
        /// the refresh above 50% remaining. With a durable Iron Stomach at, e.g., 60% remaining vanilla
        /// <c>CanEat</c> is rescued to TRUE (so <c>CanConsumeItem</c> passes and <c>Humanoid.ConsumeItem</c>
        /// debits the item), yet vanilla <c>EatFood</c> returns FALSE without refreshing — the item is lost
        /// with no effect. This prefix reproduces EXACTLY the refresh vanilla performs below 0.5 (reset the
        /// matching slot's m_time/health/stamina/eitr from the item, forceUpdate the food) when — and only
        /// when — the durable Iron Stomach projection says this attempt sits in the raised 0.5..0.75 band,
        /// then skips vanilla and reports success so the one-item debit proceeds exactly once.
        ///
        /// It NEVER touches the new-food path, the three-slot cap, or the most-depleted-replacement path
        /// (those are left entirely to vanilla when it runs), and it mutates only the ALREADY-PRESENT
        /// matching slot — the same fields, from the same item, that vanilla's below-0.5 branch writes.
        /// Fails closed (returns true → run vanilla unchanged) off-host / without a durable purchase / on
        /// any resolution gap.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.EatFood))]
        [HarmonyPrefix]
        private static bool EatFood_Prefix(Player __instance, ItemDrop.ItemData item, ref bool __result)
        {
            try
            {
                if (__instance == null || item == null || item.m_shared == null) return true;
                if (__instance != Player.m_localPlayer) return true;   // local player only, mirror the postfix.

                // Locate the already-present matching food (vanilla's same-food branch subject). Only a food
                // ALREADY in a slot is subject to the raised refresh threshold; new-food / three-slot logic
                // stays 100% vanilla.
                var foods = __instance.GetFoods();
                if (foods == null) return true;
                Player.Food match = null!;
                foreach (var f in foods)
                {
                    if (f != null && f.m_item != null && f.m_item.m_shared != null &&
                        f.m_item.m_shared.m_name == item.m_shared.m_name)
                    {
                        match = f;
                        break;
                    }
                }
                if (match == null) return true;                        // no matching food — pure vanilla path.

                float burn = match.m_item.m_shared.m_foodBurnTime;
                if (burn <= 0f) return true;
                float remainingFraction = Mathf.Clamp01(match.m_time / burn);

                var character = ResolveLocalOccupantCharacter();
                if (character == null) return true;                    // fail closed → vanilla decides.

                var disposition = Provider.DecideEat(character, matchingFoodPresent: true, remainingFraction);
                if (disposition != IronStomachEatDisposition.RescueSameFoodRefresh)
                    return true;                                       // below 0.5 vanilla refreshes; above 0.75 vanilla denies.

                // In the raised (0.5..0.75] band: perform EXACTLY vanilla's below-0.5 same-food refresh, then
                // suppress vanilla and report success so the single debit in ConsumeItem proceeds once. Same
                // fields, same source item — slots/debit/stats/duration remain vanilla; only the threshold moved.
                match.m_time = item.m_shared.m_foodBurnTime;
                match.m_health = item.m_shared.m_food;
                match.m_stamina = item.m_shared.m_foodStamina;
                match.m_eitr = item.m_shared.m_foodEitr;
                InvokeUpdateFood(__instance);
                __result = true;
                return false;                                          // skip vanilla EatFood — we handled this attempt.
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Cooking] Iron Stomach EatFood prefix threw (ignored, running vanilla): " + ex.Message);
                return true;                                           // fail closed → run vanilla unchanged.
            }
        }

        // Cached reflection handle for the private vanilla Player.UpdateFood(float, bool). Read via decomp
        // (Player.EatFood calls UpdateFood(0f, forceUpdate: true) after a refresh); resolved once.
        private static readonly System.Reflection.MethodInfo UpdateFoodMethod =
            AccessTools.Method(typeof(Player), "UpdateFood", new[] { typeof(float), typeof(bool) });

        /// <summary>Force the same post-refresh food recompute vanilla runs (<c>UpdateFood(0f, forceUpdate:
        /// true)</c>) so the refreshed slot's derived stats settle identically. Best-effort: a resolution
        /// gap is swallowed (the slot's duration/stat fields are already reset; the next tick recomputes).</summary>
        private static void InvokeUpdateFood(Player player)
        {
            try { UpdateFoodMethod?.Invoke(player, new object[] { 0f, true }); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Cooking] Iron Stomach UpdateFood invoke failed (ignored): " + ex.Message);
            }
        }

        /// <summary>Resolve the acting local occupant's durable character aggregate from the authoritative
        /// HOST projection (the composed server's character store, keyed to the bound internal principal).
        /// Returns null — the fail-closed signal — on a pure client (no server runtime), an unresolvable
        /// identity, or an absent character aggregate. No client-supplied claim is ever trusted.</summary>
        private static CharacterProgressionAggregate? ResolveLocalOccupantCharacter()
        {
            var server = LocalProgressionObserver.Server;
            if (server == null) return null;                        // pure client — no personal-effect snapshot yet.

            var foundational = FoundationalPlacementObserver.Server;
            var player = Player.m_localPlayer;
            var znet = ZNet.instance;
            if (foundational == null || player == null || znet == null) return null;

            // Acting bound INTERNAL principal (server-minted account/character), keyed by the same
            // player:<s_playerID> subject the character admission binds under — never the payload. Iron
            // Stomach is durable and NOT Stone-scoped for delivery, so no Stone-Area membership is required:
            // the personal Permanent Effect follows the character everywhere.
            long actingPlayerId = player.GetPlayerID();
            string peerKey = ServerCreatorIdentity.CharacterSubject(actingPlayerId);
            if (string.IsNullOrEmpty(peerKey) ||
                !foundational.BoundSessions.TryResolve(peerKey, out var principal))
                return null;

            var occupant = principal.Account;
            var character = principal.Character;
            if (string.IsNullOrEmpty(occupant.Value)) return null;

            return server.Characters.GetCharacter(occupant, character);
        }
    }
}
