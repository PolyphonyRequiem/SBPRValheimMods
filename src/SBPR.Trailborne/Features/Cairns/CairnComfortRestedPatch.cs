using System;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cairns
{
    /// <summary>
    /// Open-air cairn comfort — grant the vanilla <c>Rested</c> buff near a cairn out in
    /// the open, WITHOUT the cairn becoming a heat source.
    ///
    /// THE BUG (root-caused in docs/investigations/2026-06-13-cairn-no-open-air-comfort-near-fire-gate.md):
    /// Our comfort-LEVEL patch (<see cref="CairnPatches.SE_Rested_CalculateComfortLevel_Postfix"/>)
    /// correctly raises the comfort level in the open, but vanilla has a SECOND gate.
    /// <c>Player.UpdateEnvStatusEffects</c> only grants the <c>Resting</c> status when a long
    /// AND-chain ends in <c>&amp;&amp; flag</c>, where <c>flag = m_nearFireTimer &lt; 0.25f</c>
    /// (near-fire). A cairn emits no heat, so in the open <c>flag</c> is false → no Resting →
    /// no Rested, no matter how high the comfort level. (Decomp: Player.UpdateEnvStatusEffects,
    /// flag13 at :2066, the Resting add/remove at :2069/:2073.)
    ///
    /// THE FIX — campfire PARITY timing (Daniel's call, 2026-06-13, overriding the architect's
    /// "grant Rested immediately" default): ride vanilla's real
    /// <c>Resting → SE_Cozy(10 s dwell) → Rested</c> pipeline so the player becomes eligible the
    /// instant they sit near a cairn (Resting appears + the "$se_resting_start" comfort message),
    /// then the <c>Rested</c> buff is granted after the normal ~10 s ramp — exactly like a campfire.
    /// SE_Cozy.m_delay = 10 s and it grants Rested once m_time &gt; m_delay (decomp :24811 / :24838).
    ///
    /// Vanilla strips <c>Resting</c> EVERY tick in the open (the only RemoveStatusEffect(Resting)
    /// call site in the whole assembly is Player.UpdateEnvStatusEffects :2073), which would reset
    /// SE_Cozy's timer and reprint the center message forever (the "thrash"). So riding the ramp
    /// needs TWO surgical, heat-free patches working together:
    ///
    ///   • <see cref="UpdateEnvStatusEffects_Postfix"/> — POSTFIX on Player.UpdateEnvStatusEffects.
    ///     When a cairn is in range and the player otherwise qualifies (read from vanilla's OWN
    ///     just-set exclusion statuses), SEED the <c>Resting</c> status if it is absent — WITHOUT
    ///     resetTime, so an already-running Resting's SE_Cozy timer keeps accumulating. Records a
    ///     short-lived "maintaining" stamp so the suppressor below knows this is our case.
    ///
    ///   • <see cref="RestingStripSuppressor"/> — PREFIX on SEMan.RemoveStatusEffect(int, bool).
    ///     Swallows ONLY the <c>Resting</c> removal, ONLY for the local player, ONLY while the
    ///     maintaining stamp is live this tick. That lets SE_Cozy.m_time climb to 10 s → vanilla
    ///     grants Rested → vanilla maintains Rested's TTL from the (cairn-raised) comfort level.
    ///
    /// NO HEAT: we never touch <c>m_nearFireTimer</c>, never call <c>OnNearFire</c>, never add an
    /// <c>EffectArea</c>. vanilla computes Cold/Freezing/CampFire with the unmodified (false)
    /// near-fire flag, so the cairn still gives no warmth, no freeze-thaw, no CampFire status
    /// (AT-CAIRN-NOT-A-FIRE holds by construction). We never set flag13, so <c>m_safeInHome</c> is
    /// untouched (AT-CAIRN-SAFEHOME-UNCHANGED).
    ///
    /// PERF: Player.UpdateEnvStatusEffects runs at ~50 Hz (FixedUpdate, local owner only). We do
    /// NOT run a Physics query here — the cairn-in-range bool is stashed every 2 s by the existing
    /// <see cref="CairnPatches.SE_Rested_CalculateComfortLevel_Postfix"/> path (see
    /// <see cref="CairnComfortStash"/>), and we only read cheap post-state status flags per tick.
    ///
    /// CLEAN-SIDE (ADR-0001): patches base-game Player / SEMan only. Client-only by construction
    /// (the dedicated server has no local Player). Registered in Plugin.cs. Fails OPEN on any
    /// error so a detection bug can never brick status effects.
    /// </summary>
    public static class CairnComfortRestedPatch
    {
        // How long, after the postfix asserts "qualifying near a cairn," the strip-suppressor
        // keeps swallowing the Resting removal. The postfix runs every FixedUpdate (~0.02 s) on
        // the same tick that vanilla tries to strip, so a short window is plenty; we keep a small
        // margin so a one-frame hiccup can't drop us. Comfortably below SE_Cozy's 10 s ramp.
        private const float SuppressWindowSeconds = 0.5f;

        // Set by the postfix when the local owner qualifies near a cairn this tick; read by the
        // strip-suppressor prefix. Time.time of the last qualifying assertion (-1 = never).
        private static float s_maintainResting = -1f;

        // ── Seed Resting near a cairn (campfire-parity ramp) ────────────────
        // Player.UpdateEnvStatusEffects is PRIVATE → patch by string name (nameof won't compile
        // against a private member from outside the class). Same pattern as the repo's existing
        // private-method patches (Player."UpdatePlacementGhost", Minimap."UpdateExplore").
        [HarmonyPatch(typeof(Player), "UpdateEnvStatusEffects")]
        [HarmonyPostfix]
        public static void UpdateEnvStatusEffects_Postfix(Player __instance)
        {
            try
            {
                if (__instance == null) return;
                // Local owner only. Vanilla already gates the method to the owner local player,
                // but guard explicitly so we never act on a remote/other player.
                if (__instance != Player.m_localPlayer) return;
                var nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsOwner()) return;

                Vector3 pos = __instance.transform.position;

                // Cairn-in-range — read the 2 s stash (NO new Physics query on this 50 Hz path).
                if (!CairnComfortStash.IsCairnInRange(pos)) return; // no cairn → do nothing; let any
                                                                    // existing Rested TTL count down.

                var seman = __instance.GetSEMan();
                if (seman == null) return;

                // Qualify predicate — read vanilla's OWN post-state exclusions (it already applied
                // Cold/Freezing/Burning/Wet THIS tick using the real no-fire flags). Reading the
                // resulting statuses is faithful by construction and drift-resistant: if IronGate
                // changes the cold model we inherit it for free, and AT-CAIRN-NO-STORM-REST holds.
                bool sensed = __instance.IsSensed();                              // flag6
                bool seatedOrSheltered = __instance.IsSitting() || __instance.InShelter(); // flag8 || flag3
                bool cold = seman.HaveStatusEffect(SEMan.s_statusEffectCold);
                bool freezing = seman.HaveStatusEffect(SEMan.s_statusEffectFreezing);
                bool burning = seman.HaveStatusEffect(SEMan.s_statusEffectBurning); // flag2
                bool wet = seman.HaveStatusEffect(SEMan.s_statusEffectWet);         // flag7
                bool warmCozy = EffectArea.IsPointInsideArea(pos, EffectArea.Type.WarmCozyArea, 1f); // flag9

                bool qualifies = !sensed && seatedOrSheltered && !cold && !freezing && !burning && (!wet || warmCozy);
                if (!qualifies)
                {
                    // Stop maintaining — let vanilla's strip run normally so Resting clears.
                    s_maintainResting = -1f;
                    return;
                }

                // We qualify near a cairn. Arm the strip-suppressor for the immediate window so
                // vanilla's per-tick RemoveStatusEffect(Resting) is swallowed and SE_Cozy's timer
                // accumulates toward the 10 s grant.
                s_maintainResting = Time.time;

                // Seed Resting ONLY if absent, and WITHOUT resetTime — re-adding with resetTime (or
                // re-running Setup on an existing one) would zero SE_Cozy.m_time and reprint the
                // "$se_resting_start" center message every tick (the thrash + message spam). A fresh
                // add (when truly absent) runs Setup once → one comfort message, then the timer runs.
                if (!seman.HaveStatusEffect(SEMan.s_statusEffectResting))
                {
                    seman.AddStatusEffect(SEMan.s_statusEffectResting); // resetTime:false (vanilla default)
                }
                // If Resting is already present, we do nothing else: the suppressor keeps it alive,
                // SE_Cozy.UpdateStatusEffect ticks m_time, and at m_time > 10 s vanilla grants Rested
                // with resetTime:true → SE_Rested.UpdateTTL sets the tier-scaled duration from the
                // cairn-raised comfort level (T1 floor 3 → 420 s … T5 floor 7 → 660 s).
            }
            catch (Exception e)
            {
                // Fail OPEN: never let a detection bug break status effects.
                s_maintainResting = -1f;
                Plugin.Log.LogWarning($"[Trailborne/M2] Cairn open-air Rested postfix error (failing open): {e.Message}");
            }
        }

        // ── Suppress ONLY the cairn-case Resting strip ──────────────────────
        // Separate [HarmonyPatch] class so the Type[] overload disambiguator and the bool return
        // semantics (skip-original) are unambiguous on the overloaded RemoveStatusEffect.
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.RemoveStatusEffect), new Type[] { typeof(int), typeof(bool) })]
        public static class RestingStripSuppressor
        {
            /// <summary>
            /// Swallow vanilla's per-tick <c>RemoveStatusEffect(Resting)</c> ONLY while the local
            /// owner is qualifying near a cairn (the postfix above armed the window THIS tick).
            /// Returning false skips the original and reports "nothing removed" (__result = false),
            /// which matches vanilla's own contract when the effect is absent — callers at
            /// Player.UpdateEnvStatusEffects :2073 ignore the return for Resting.
            ///
            /// Scoped as narrowly as possible: only the Resting hash, only when our window is live.
            /// Every other status, every other caller, and the non-cairn case are untouched, so
            /// RemoveAllStatusEffects (death/teleport — it manipulates the list directly, never
            /// routes here) and normal Resting clears when you walk away both behave normally.
            /// </summary>
            [HarmonyPrefix]
            public static bool Prefix(int nameHash, ref bool __result)
            {
                try
                {
                    if (nameHash != SEMan.s_statusEffectResting) return true;   // only Resting
                    if (s_maintainResting < 0f) return true;                    // not maintaining → let vanilla strip

                    // Window guard: only swallow within the immediate post-assertion window. The
                    // postfix re-arms every FixedUpdate while qualifying, so a live cairn keeps this
                    // fresh; once we stop qualifying (or leave), the window lapses and vanilla's
                    // strip runs again → Resting clears as normal.
                    if (Time.time - s_maintainResting > SuppressWindowSeconds) return true;

                    __result = false; // report "not removed" (effect stays); skip original
                    return false;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Trailborne/M2] Cairn Resting-strip suppressor error (failing open): {e.Message}");
                    return true; // fail OPEN → let vanilla remove normally
                }
            }
        }
    }

    /// <summary>
    /// Throttled cache of "is a cairn in comfort range of the local player." Keeps the cairn
    /// <see cref="Physics.OverlapSphere"/> query OFF the ~50 Hz <c>Player.UpdateEnvStatusEffects</c>
    /// hot path.
    ///
    /// Two feeds, one bound:
    ///   • The existing 2 s <see cref="CairnPatches.SE_Rested_CalculateComfortLevel_Postfix"/> path
    ///     already computes the cairn bonus; it calls <see cref="Stash"/> so the hot path reuses
    ///     that result with ZERO new physics (the design's preferred option).
    ///   • <see cref="IsCairnInRange"/> self-heals: if the stash is older than the throttle window
    ///     (e.g. just-spawned, before the first comfort tick), it runs ONE query and caches it.
    /// Either way a fresh OverlapSphere happens at most once per <see cref="QueryThrottleSeconds"/>.
    ///
    /// Only the local owner player drives both feeds (vanilla gates UpdateBaseValue → the comfort
    /// calc, and UpdateEnvStatusEffects, to <c>m_localPlayer</c>), so a single static cache is
    /// correct — there is exactly one local player per client. Rested TTL is 300 s+, so a ≥1–2 s
    /// staleness in the in-range bool is imperceptible.
    /// </summary>
    public static class CairnComfortStash
    {
        // Bound on how often a fresh cairn physics query may run. Matches the vanilla comfort-calc
        // cadence (UpdateBaseValue, every 2 s) so steady-state reuses that query and the hot path
        // never adds physics of its own.
        private const float QueryThrottleSeconds = 2f;

        private static float s_lastQueryTime = -999f;
        private static bool s_lastInRange;

        /// <summary>Record an externally-computed in-range result (called from the 2 s comfort postfix).</summary>
        public static void Stash(bool inRange)
        {
            s_lastQueryTime = Time.time;
            s_lastInRange = inRange;
        }

        /// <summary>
        /// True if a cairn is within comfort range. Returns the cached value while fresh; otherwise
        /// runs one throttled <see cref="Cairns.GetCairnComfortBonus"/> query and caches it.
        /// </summary>
        public static bool IsCairnInRange(Vector3 position)
        {
            if (Time.time - s_lastQueryTime < QueryThrottleSeconds)
                return s_lastInRange;

            bool inRange = Cairns.GetCairnComfortBonus(position) > 0;
            Stash(inRange);
            return inRange;
        }
    }
}
