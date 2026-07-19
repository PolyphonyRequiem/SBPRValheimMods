using System;
using System.Collections.Generic;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Cooking;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
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
    ///     and the inventory UI consult before allowing a food to be (re-)eaten. Vanilla's per-food
    ///     "already have this, can I refresh it?" decision is <c>Player.Food.CanEatAgain()</c> ==
    ///     <c>m_time &lt; m_foodBurnTime / 2f</c> — i.e. refresh allowed only once the remaining fraction
    ///     drops below 0.5 (the vanilla baseline threshold). This postfix RESCUES a vanilla FALSE to TRUE
    ///     when (and only when) the refusal was caused solely by an ALREADY-PRESENT matching food whose
    ///     remaining fraction is at or below the Iron Stomach threshold (0.75) but above the vanilla 0.5 —
    ///     the exact "refresh at 75% remaining" band. When vanilla already PASSED we never flip it to
    ///     false; we never touch the three-slot cap or the "different food, slots full" refusal (those are
    ///     the preserved slots/debit invariants), only the remaining-duration refresh threshold for a food
    ///     the player already holds.
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
                // local occupant and answers whether this remaining fraction may refresh.
                if (ResolveCanRefreshForLocalOccupant(remainingFraction))
                    __result = true;                                // refresh at up to 75% remaining while acquired.
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Cooking] Iron Stomach refresh gate postfix threw (ignored): " + ex.Message);
            }
        }

        /// <summary>Resolve whether the local occupant may refresh a food at the given remaining fraction
        /// under a durably-acquired Iron Stomach, from the authoritative HOST projection (the character's
        /// durable purchase record, via the shipped <see cref="FoodRefreshThresholdProvider"/>). Fail
        /// closed: no server runtime (pure client), unresolvable identity, or absent character aggregate ⇒
        /// false (foods keep the vanilla 0.5 threshold). No client-supplied claim is ever trusted.</summary>
        private static bool ResolveCanRefreshForLocalOccupant(float remainingFraction)
        {
            var server = LocalProgressionObserver.Server;
            if (server == null) return false;                       // pure client — no personal-effect snapshot yet.

            var foundational = FoundationalPlacementObserver.Server;
            var player = Player.m_localPlayer;
            var znet = ZNet.instance;
            if (foundational == null || player == null || znet == null) return false;

            // Acting bound INTERNAL principal (server-minted account/character), keyed by the same
            // player:<s_playerID> subject the character admission binds under — never the payload. Iron
            // Stomach is durable and NOT Stone-scoped for delivery, so no Stone-Area membership is required:
            // the personal Permanent Effect follows the character everywhere.
            long actingPlayerId = player.GetPlayerID();
            string peerKey = ServerCreatorIdentity.CharacterSubject(actingPlayerId);
            if (string.IsNullOrEmpty(peerKey) ||
                !foundational.BoundSessions.TryResolve(peerKey, out var principal))
                return false;

            var occupant = principal.Account;
            var character = principal.Character;
            if (string.IsNullOrEmpty(occupant.Value)) return false;

            var characterAgg = server.Characters.GetCharacter(occupant, character);
            if (characterAgg == null) return false;

            // Single authority: the shipped, unit-tested pure projection keyed on the durable purchase.
            return Provider.CanRefresh(characterAgg, remainingFraction);
        }
    }
}
