using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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
    ///   * <c>Player.CanEat(ItemDrop.ItemData item, bool showMessages)</c> — the OUTER gate the inventory
    ///     UI and <c>Player.EatFood</c> consult before allowing a food to be (re-)eaten. Vanilla's per-food
    ///     "already have this, can I refresh it?" decision is <c>Player.Food.CanEatAgain()</c> ==
    ///     <c>m_time &lt; m_foodBurnTime / 2f</c> — refresh allowed only once the remaining fraction drops
    ///     below 0.5. A postfix RESCUES a vanilla FALSE to TRUE in the 0.5..0.75 refresh band for a
    ///     durable-Iron-Stomach local occupant (never overriding a vanilla PASS, never the three-slot cap).
    ///   * <c>Player.EatFood(ItemDrop.ItemData item)</c> — the ACTUAL refresh path. After its own CanEat
    ///     check it loops the slots and, for a matching food, re-checks the SAME <c>food2.CanEatAgain()</c>
    ///     0.5 inner guard (decomp 17486) before resetting <c>m_time/m_health/m_stamina/m_eitr</c>. The
    ///     CanEat postfix alone does NOT reach this inner guard, so in the 0.5..0.75 band EatFood silently
    ///     no-ops while <c>Humanoid.ConsumeItem</c> still debits the item (node-own live-QA defect
    ///     t_6b73a3de). A transpiler rewrites that single <c>CanEatAgain</c> call to an Iron-Stomach-aware
    ///     predicate that returns vanilla's verdict UNCHANGED and only additionally raises the in-band
    ///     refresh for a durable acquirer — so the gate and the refresh path finally agree.
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

                // Single authority: the pure provider resolves the durable Iron Stomach threshold for the
                // local occupant and answers whether this remaining fraction may refresh. __result is false
                // here (vanilla refused), so vanillaWouldRefresh is false — the raise applies ONLY while
                // Iron Stomach is durably acquired, so a non-acquired occupant is never rescued.
                if (TryResolveLocalOccupantCharacter(out var character) &&
                    Provider.ShouldRefreshOnEat(character, remainingFraction, vanillaWouldRefresh: false))
                    __result = true;                                // refresh at up to 75% remaining while acquired.
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Cooking] Iron Stomach refresh gate postfix threw (ignored): " + ex.Message);
            }
        }

        /// <summary>Transpiler on the ACTUAL refresh path. Vanilla <c>Player.EatFood</c> re-checks its inner
        /// <c>food2.CanEatAgain()</c> guard (<c>m_time &lt; m_foodBurnTime / 2f</c>, the hardcoded 0.5
        /// threshold, decomp 15335/17486) before it resets <c>m_time/m_health/m_stamina/m_eitr</c> for a
        /// matching food already in a slot. Patching only <see cref="Player.CanEat"/> raises the OUTER gate
        /// but leaves this inner guard at 0.5, so in the 0.5..0.75 band <c>EatFood</c> silently no-ops while
        /// <c>Humanoid.ConsumeItem</c> still debits the item — the node-own live-QA defect (t_6b73a3de).
        ///
        /// This transpiler replaces the SINGLE <c>Food.CanEatAgain()</c> call inside <c>EatFood</c> with a
        /// call to <see cref="ShouldRefreshOnEat"/>, which returns vanilla's own verdict UNCHANGED (never
        /// lowered) and additionally permits the in-band refresh only for a durable-Iron-Stomach local
        /// occupant. It touches nothing else in <c>EatFood</c> — the three-slot cap, the debit, and the
        /// stat/duration reset all remain vanilla — so the gate and the refresh path finally agree.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.EatFood))]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> EatFood_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo canEatAgain = AccessTools.Method(typeof(Player.Food), nameof(Player.Food.CanEatAgain));
            MethodInfo replacement = AccessTools.Method(typeof(IronStomachRefreshGate), nameof(ShouldRefreshOnEat));

            int replaced = 0;
            foreach (var ins in instructions)
            {
                if (canEatAgain != null && replacement != null &&
                    (ins.opcode == OpCodes.Callvirt || ins.opcode == OpCodes.Call) &&
                    (ins.operand as MethodInfo) == canEatAgain)
                {
                    replaced++;
                    // Same stack shape (pops the Food instance, pushes a bool) — a Call to our static helper
                    // that takes the Food. Carry any labels/exception blocks anchored on the original insn.
                    yield return new CodeInstruction(OpCodes.Call, replacement)
                    {
                        labels = ins.labels,
                        blocks = ins.blocks,
                    };
                    continue;
                }
                yield return ins;
            }

            if (replaced != 1)
                Plugin.Log.LogError(
                    "[Niflheim/Cooking] Iron Stomach EatFood transpiler expected EXACTLY 1 Food.CanEatAgain " +
                    "call to rewrite, replaced " + replaced + " — the in-world 75% refresh raise may be inert. " +
                    "Vanilla EatFood may have changed; re-verify the seam.");
        }

        /// <summary>The EatFood refresh predicate the transpiler substitutes for the vanilla inner
        /// <c>Food.CanEatAgain()</c> guard. Returns vanilla's own verdict FIRST (so it is never lowered),
        /// and — only for a durably-acquired Iron Stomach local occupant — additionally permits the refresh
        /// up to the raised threshold (0.75, boundary-inclusive). Fails closed to the exact vanilla verdict
        /// off-host / without a durable purchase / on any error. Single authority: the raise decision routes
        /// through the shipped, unit-tested pure <see cref="FoodRefreshThresholdProvider"/>.</summary>
        internal static bool ShouldRefreshOnEat(Player.Food food)
        {
            // Vanilla's own verdict, computed exactly as vanilla would — this is what we must never lower.
            bool vanillaWouldRefresh;
            try
            {
                vanillaWouldRefresh = food != null && food.CanEatAgain();
            }
            catch
            {
                return false;
            }

            try
            {
                if (vanillaWouldRefresh) return true;               // below vanilla 0.5 — always refresh.
                if (food == null || food.m_item == null || food.m_item.m_shared == null)
                    return false;

                float burn = food.m_item.m_shared.m_foodBurnTime;
                if (burn <= 0f) return false;
                float remainingFraction = Mathf.Clamp01(food.m_time / burn);

                if (!TryResolveLocalOccupantCharacter(out var character)) return false;
                return Provider.ShouldRefreshOnEat(character, remainingFraction, vanillaWouldRefresh: false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Cooking] Iron Stomach EatFood refresh predicate threw (fell back to vanilla): " + ex.Message);
                return vanillaWouldRefresh;                         // fail closed to the exact vanilla verdict.
            }
        }

        /// <summary>Resolve the durable character aggregate for the LOCAL occupant from the authoritative
        /// HOST projection (the composed <see cref="LocalProgressionObserver.Server"/> character store),
        /// keyed on the same <c>player:&lt;s_playerID&gt;</c> subject the character admission binds under —
        /// never a client-supplied claim. Fails closed (returns false) on a pure client (no server runtime),
        /// unresolvable identity, or absent character aggregate. Iron Stomach is durable and NOT Stone-scoped
        /// for delivery, so no Stone-Area membership is required — the personal Permanent Effect follows the
        /// character everywhere. Both the CanEat gate and the EatFood refresh path consult this ONE resolver
        /// so the two seams cannot disagree about who the local occupant is.</summary>
        private static bool TryResolveLocalOccupantCharacter(out CharacterProgressionAggregate character)
        {
            character = null!;

            var server = LocalProgressionObserver.Server;
            if (server == null) return false;                       // pure client — no personal-effect snapshot yet.

            var foundational = FoundationalPlacementObserver.Server;
            var player = Player.m_localPlayer;
            var znet = ZNet.instance;
            if (foundational == null || player == null || znet == null) return false;

            long actingPlayerId = player.GetPlayerID();
            string peerKey = ServerCreatorIdentity.CharacterSubject(actingPlayerId);
            if (string.IsNullOrEmpty(peerKey) ||
                !foundational.BoundSessions.TryResolve(peerKey, out var principal))
                return false;

            var occupant = principal.Account;
            var characterId = principal.Character;
            if (string.IsNullOrEmpty(occupant.Value)) return false;

            var characterAgg = server.Characters.GetCharacter(occupant, characterId);
            if (characterAgg == null) return false;

            character = characterAgg;
            return true;
        }
    }
}
