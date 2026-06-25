using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Portals
{
    /// <summary>
    /// The food-as-fuel Portal Energy cost engine for the Twisted Portal (spec §5).
    ///
    /// 🔧 THIS IS THE C1 SEAM, NOT THE C2 IMPLEMENTATION. Card C1 (t_2b388cd5, the portal
    /// MECHANISM) defines and calls this seam from <see cref="SBPR_TwistedPortal.Teleport"/>;
    /// card C2 (t_6e992a30, food-as-fuel) replaces <see cref="TrySpendForJump"/>'s BODY with the
    /// real Portal Energy math. The CONTRACT is fixed here and must not change without coordinating
    /// both cards: distance <c>D</c> (meters) in → <see cref="JumpResult"/> { Ok, Reason,
    /// BurnedBerries } out.
    ///
    /// ════════════════════════════════════════════════════════════════════════════════════════
    /// WHAT C2 BUILDS HERE (spec §5 — authority is docs/design/twisted-portal-food-charge.md):
    ///   • <c>tier(food) = round(clamp(total_stats/30, 1, 5) × 2) / 2</c> off the BASE budget
    ///     (m_food + m_foodStamina + m_foodEitr) — NOT the decayed live stat (spec §5.1 🔴).
    ///   • <c>PE(player) = Σ (slot.m_time/60 × tier(slot.m_item))</c> over Player.GetFoods()
    ///     (decomp :17598), with feasts on the normalized FEAST_RANGE_CAP clock (spec §5.6).
    ///   • <c>belly_range = PE × METERS_PER_PE</c>; drain belly food-time for the covered distance
    ///     (arrive depleted — the coupling, spec §5.3).
    ///   • Shortfall past belly_range → burn ceil(shortfall / 30 m) Bukeberries (vanilla
    ///     Pukeberries; Inventory.RemoveItem/CountItems), 10 berries = the 300 m ceiling (§5.4).
    ///   • A berry-burning jump applies vanilla Feeling Sick (SE_Puke) on arrival and reports it via
    ///     <see cref="JumpResult.BurnedBerries"/> &gt; 0 (spec §5.5).
    ///   • All numbers are BepInEx config knobs; METERS_PER_PE is the one under-specified constant —
    ///     C2 derives a defensible baseline from the locked anchors or BLOCKS for Daniel (spec §5.3).
    ///   • PATCH-FREE: PE is read on demand from GetFoods(); no Harmony patches (spec §5.1 / §8).
    /// ════════════════════════════════════════════════════════════════════════════════════════
    ///
    /// 🚧 UNTIL C2 LANDS — DELIBERATE LOUD FREE-PASS (not a silent no-op, not a hard throw):
    ///   The current body returns <c>Ok = true</c> and burns nothing, so the C1 portal MECHANISM
    ///   acceptance tests (AT-NOPORTALS-BYPASS, AT-NAME-PAIR, AT-JUMP-ACTIVATE, AT-RUNE-NAME) are
    ///   verifiable in-game on their own PR — a hard <c>NotImplementedException</c> or a hard block
    ///   would brick C1's own ATs before C2 exists, and a SILENT free-pass would hide that travel is
    ///   currently free. So instead it screams a WARNING on every jump that the food-as-fuel cost
    ///   model is NOT YET WIRED. C2 deletes this warning when it implements the real debit. This is
    ///   the "build what you need, tripwire what you don't" doctrine (valheim-mod-development skill).
    /// </summary>
    public static class TwistedPortalEnergy
    {
        /// <summary>
        /// The seam's return type (spec §4.4). C2 owns the meaning of every field:
        ///   • <see cref="Ok"/> — did the player have enough fuel (belly + berry reserve) to jump?
        ///     When false, C1 blocks the teleport and shows <see cref="Reason"/>; nothing is spent.
        ///   • <see cref="Reason"/> — the block message shown to the player. Plain English (the repo
        ///     has NO $sbpr_* localization registration layer, so a custom token leaks as a literal —
        ///     the SurveyorTableTag center-message precedent; vanilla tokens like $msg_noteleport are
        ///     fine). Only meaningful when <see cref="Ok"/> is false; may be empty (C1 falls back to a
        ///     generic plain-English line).
        ///   • <see cref="BurnedBerries"/> — how many Bukeberries the jump consumed for the shortfall
        ///     (0 = belly covered it). &gt; 0 signals a "Feeling Sick on arrival" jump (spec §5.5).
        /// </summary>
        public struct JumpResult
        {
            public bool Ok;
            public string Reason;
            public int BurnedBerries;

            public static JumpResult Blocked(string reason) => new JumpResult { Ok = false, Reason = reason, BurnedBerries = 0 };
            public static JumpResult Spent(int burnedBerries) => new JumpResult { Ok = true, Reason = string.Empty, BurnedBerries = burnedBerries };
        }

        // One-time loud notice so the warning doesn't spam every single jump forever, but still fires
        // (the first time per session) so it can never be silently forgotten. Reset is process-scoped.
        private static bool _warnedUnwired;

        /// <summary>
        /// Gate + debit the food-as-fuel cost of a jump of <paramref name="distanceMeters"/> for
        /// <paramref name="player"/>. SEAM — C2 (t_6e992a30) replaces this body with the real Portal
        /// Energy math (spec §5). Until then: a loud free-pass (see the class summary).
        /// </summary>
        /// <param name="player">The traveling player (never null when C1 calls — guarded upstream).</param>
        /// <param name="distanceMeters">The jump distance D, computed by C1 from the resolved
        /// destination (spec §4.4). The whole reason the seam takes D: cost scales with distance.</param>
        public static JumpResult TrySpendForJump(Player player, float distanceMeters)
        {
            // ── C2 IMPLEMENTS FROM HERE (delete the free-pass below) ───────────────────────────
            // PE = Σ over GetFoods() of (m_time/60 × tier(base stats)); belly_range = PE × METERS_PER_PE;
            // if D ≤ belly_range → drain food-time, return Spent(0);
            // else burn ceil((D − belly_range)/30) Bukeberries if available (return Spent(n) + SE_Puke),
            // else return Blocked("$sbpr_twisted_no_fuel").
            // ───────────────────────────────────────────────────────────────────────────────────

            if (!_warnedUnwired)
            {
                _warnedUnwired = true;
                Plugin.Log.LogWarning(
                    "[Trailborne/TwistedPortal] FOOD-AS-FUEL COST MODEL NOT YET WIRED (card C2 t_6e992a30). " +
                    "TwistedPortalEnergy.TrySpendForJump is a LOUD FREE-PASS: Twisted Portal travel is currently " +
                    "FREE (no Portal Energy debit, no Bukeberry shortfall, no Feeling Sick). This is intentional " +
                    "so the C1 portal-mechanism acceptance tests can run before C2 lands — it is a tripwire, NOT " +
                    "shippable. C2 replaces this body with the real food-as-fuel debit (spec §5) and deletes this warning.");
            }

            // Free-pass: let the mechanism work so C1's ATs are verifiable. Spends nothing, burns no berries.
            return JumpResult.Spent(0);
        }
    }
}
