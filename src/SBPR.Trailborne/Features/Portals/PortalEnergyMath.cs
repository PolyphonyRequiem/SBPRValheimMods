// ============================================================================
//  Trailborne v3 (Swamp) — Twisted Portal FOOD-AS-FUEL Portal Energy core (engine-free)
// ----------------------------------------------------------------------------
//  Design (authority for EVERY number): docs/design/twisted-portal-food-charge.md
//  Impl spec:                           docs/v3/planning/twisted-portal-impl-spec.md §5
//  Card: t_6e992a30 (C2 — the cost model). Seam owner: SBPR_TwistedPortal.Teleport
//        (C1, t_2b388cd5) hands (player, D) to TwistedPortalEnergy.TrySpendForJump,
//        which delegates the MATH to this file and keeps the engine I/O (read
//        GetFoods(), shorten m_time, burn berries, apply SE_Puke) in TwistedPortalEnergy.cs.
//
//  WHAT THIS FILE IS. The PURE truth table for the Portal Energy cost model:
//    • tier(food)      = round(clamp(total_stats / 30, 1, 5) × 2) / 2   (0.5 rungs, [1,5])
//    • PE(player)      = Σ over active belly slots of  rangeMinutes × tier
//                        rangeMinutes = feast ? min(realMinutes, FEAST_RANGE_CAP) : realMinutes
//    • belly_range (m) = PE × METERS_PER_PE
//    • a jump of distance D drains belly PE as food-TIME removed from slots (so a long
//      jump lands you depleted — the AT-ARRIVE-DEPLETED coupling); the shortfall past
//      belly_range is paid by ceil(shortfall / BUKE_METERS_PER_BERRY) Bukeberries.
//
//  WHY THIS IS A SEPARATE ENGINE-FREE FILE. The tier curve + the PE sum + the feast
//  range-clock + the berry-shortfall solve are the load-bearing invariants of the
//  cost model, and they are exactly the kind of pure float math the repo's test
//  suite gates headlessly (the SunstoneHaloGeometry / CompassNorthGate / BoundedMapMath
//  link-compile precedent — tests/SBPR.Trailborne.Tests.csproj). Keeping it free of
//  UnityEngine / Valheim types lets tests/PortalEnergyMathTests.cs assert AT-PE-MATH
//  under net8.0 — so a future edit that drifts the divisor, the clamp, the feast cap,
//  the 30 m/berry conversion, or the proportional debit fails CI instead of shipping.
//  TwistedPortalEnergy reads the live belly state (Player.GetFoods()) and only consumes
//  the numbers this file returns; the POLICY lives here, the engine I/O lives there.
//
//  Clean-side (ADR-0001): all SBPR-authored math; references no vanilla or third-party
//  type. Uses System.Math (Round/Ceiling/clamp) only — present under net48 AND net8.0.
// ============================================================================

using System;

namespace SBPR.Trailborne.Features.Portals
{
    /// <summary>
    /// The live-tunable cost-model knobs (BepInEx config in-game; this struct is the engine-free
    /// carrier so the math + the unit tests share ONE definition). The baselines in
    /// <see cref="Default"/> are the values Daniel locked in
    /// <c>docs/design/twisted-portal-food-charge.md</c> §6 — the architecture is fixed, these
    /// numbers are explicit playtest dials.
    /// </summary>
    public readonly struct PortalEnergyKnobs
    {
        /// <summary>Stat-points per whole tier — the slope of the tier curve (design §2 / §6.3). Baseline 30.</summary>
        public readonly float TierDivisor;
        /// <summary>Tier floor (design §2 / §6.3 — raised from an earlier 0.5 so forage isn't a 10× penalty). Baseline 1.0.</summary>
        public readonly float TierClampLo;
        /// <summary>Tier ceiling (design §2 / §6.3). Baseline 5.0.</summary>
        public readonly float TierClampHi;
        /// <summary>
        /// belly-PE → meters. The ONE constant the design doc left to playtest (impl-spec §5.3 🔴).
        /// Baseline 1.0 — derived from the locked anchors, NOT invented: at 1.0, one PE
        /// (one food-minute at tier 1) buys one meter, so a Bukeberry's 30 m reserve is worth ~30 PE
        /// of belly, the 300 m portal ceiling stays the meaningful long-jump target, and a strong
        /// full belly (PE ≈ 100–150 per slot, up to ~450 across 3 slots) earns ceiling-range single
        /// jumps while a modest belly leans on the berry reserve — exactly the "a full belly is a
        /// meaningful but not unlimited range" intent. Round and legible for Daniel to retune.
        /// </summary>
        public readonly float MetersPerPe;
        /// <summary>
        /// The feast RANGE clock cap, in minutes (design §4 / §6.2). A feast slot's range-minutes are
        /// CAPPED at this regardless of its real ~50 m buff timer, so the best feast stays just under
        /// the best personal meal for travel. Baseline 28 (≈7% under the 30 m personal ceiling).
        /// </summary>
        public readonly float FeastRangeCapMinutes;
        /// <summary>Meters of reach per burned Bukeberry (design §5 / §6.5). Baseline 30 → 10 berries = the 300 m ceiling.</summary>
        public readonly float BukeMetersPerBerry;

        public PortalEnergyKnobs(
            float tierDivisor, float tierClampLo, float tierClampHi,
            float metersPerPe, float feastRangeCapMinutes, float bukeMetersPerBerry)
        {
            TierDivisor = tierDivisor;
            TierClampLo = tierClampLo;
            TierClampHi = tierClampHi;
            MetersPerPe = metersPerPe;
            FeastRangeCapMinutes = feastRangeCapMinutes;
            BukeMetersPerBerry = bukeMetersPerBerry;
        }

        /// <summary>The locked v3 baselines (design doc §6). Single source of truth for the defaults
        /// the BepInEx config binds against and the SpecCheck-style boot assertion verifies.</summary>
        public static PortalEnergyKnobs Default => new PortalEnergyKnobs(
            tierDivisor: PortalEnergyMath.DefaultTierDivisor,
            tierClampLo: PortalEnergyMath.DefaultTierClampLo,
            tierClampHi: PortalEnergyMath.DefaultTierClampHi,
            metersPerPe: PortalEnergyMath.DefaultMetersPerPe,
            feastRangeCapMinutes: PortalEnergyMath.DefaultFeastRangeCapMinutes,
            bukeMetersPerBerry: PortalEnergyMath.DefaultBukeMetersPerBerry);
    }

    /// <summary>
    /// One active belly food slot, reduced to the three facts the PE math needs (engine-free mirror
    /// of <c>Player.Food</c>, decomp :15321). <see cref="TotalStats"/> is the food's BASE budget
    /// (<c>m_shared.m_food + m_foodStamina + m_foodEitr</c>) — NOT the decayed live stat (impl-spec
    /// §5.1 🔴: feeding the decayed value into tier() would double-count decay, since PE already
    /// scales by remaining time). <see cref="RealMinutes"/> is <c>m_time / 60</c> (remaining minutes).
    /// </summary>
    public readonly struct PeSlot
    {
        /// <summary>Base stat budget = Max Health + Max Stamina + Eitr contributions (m_food + m_foodStamina + m_foodEitr).</summary>
        public readonly float TotalStats;
        /// <summary>Remaining minutes on the slot (m_time / 60).</summary>
        public readonly float RealMinutes;
        /// <summary>Whether this slot is a feast (the <c>Feast*</c> prefab family — design §4 / impl-spec §5.6).</summary>
        public readonly bool IsFeast;

        public PeSlot(float totalStats, float realMinutes, bool isFeast)
        {
            TotalStats = totalStats;
            RealMinutes = realMinutes;
            IsFeast = isFeast;
        }
    }

    /// <summary>
    /// The resolved cost of a jump: how much belly PE/range the player has, whether the belly covers
    /// the jump, how many Bukeberries the shortfall needs, and the per-slot food-time (minutes) to
    /// remove. <see cref="MinutesRemovedPerSlot"/> is parallel to the input slot array. Note this
    /// struct does NOT decide success — whether the jump is allowed depends on the player's berry
    /// inventory, which the engine (<see cref="TwistedPortalEnergy"/>) checks against
    /// <see cref="BerriesNeeded"/>.
    /// </summary>
    public readonly struct JumpSolution
    {
        /// <summary>Σ PE across the belly (range-minutes × tier).</summary>
        public readonly float BellyPe;
        /// <summary>belly_range in meters (BellyPe × METERS_PER_PE) — the distance the belly alone can cover.</summary>
        public readonly float BellyRangeMeters;
        /// <summary>True when the belly alone covers the jump (D ≤ belly_range) → zero berries.</summary>
        public readonly bool BellyCovers;
        /// <summary>The distance past belly_range (0 when the belly covers it).</summary>
        public readonly float ShortfallMeters;
        /// <summary>ceil(shortfall / BUKE_METERS_PER_BERRY) — 0 when the belly covers the jump.</summary>
        public readonly int BerriesNeeded;
        /// <summary>REAL food-time (minutes) to remove from each slot's live <c>m_time</c>, parallel to the
        /// input slots. Belly covers the jump → a proportional partial drain (each slot loses the same
        /// fraction <c>peSpent/bellyPe</c> of its REAL remaining minutes, so the player weakens in
        /// proportion to distance — AT-ARRIVE-DEPLETED). Berries burned → every slot drained fully
        /// (all real minutes removed) so the jump lands food-empty by construction (impl-spec §5.4),
        /// reinforced at runtime by SE_Puke. NOTE the debit is in REAL minutes (what the player sees and
        /// what re-derives Max HP/Stamina/Eitr next tick); the capped FEAST range-minutes drive only the
        /// PE READ, not the debit.</summary>
        public readonly float[] MinutesRemovedPerSlot;

        public JumpSolution(float bellyPe, float bellyRangeMeters, bool bellyCovers,
            float shortfallMeters, int berriesNeeded, float[] minutesRemovedPerSlot)
        {
            BellyPe = bellyPe;
            BellyRangeMeters = bellyRangeMeters;
            BellyCovers = bellyCovers;
            ShortfallMeters = shortfallMeters;
            BerriesNeeded = berriesNeeded;
            MinutesRemovedPerSlot = minutesRemovedPerSlot;
        }
    }

    /// <summary>
    /// Pure Portal Energy math (tier curve, PE sum, feast range-clock, distance→food-time debit,
    /// Bukeberry shortfall solve). Engine-free so tests/PortalEnergyMathTests.cs gates the FULL truth
    /// table headless in CI — the load-bearing cost model of card t_6e992a30 cannot silently regress.
    /// Authority for every number: docs/design/twisted-portal-food-charge.md.
    /// </summary>
    public static class PortalEnergyMath
    {
        // ── Locked baselines (design doc §6 — single source of truth for the config defaults
        //    AND the SpecCheck-style boot assertion that screams if a default drifts) ──────────
        public const float DefaultTierDivisor         = 30f;   // ~30 stat-points per whole tier (§2)
        public const float DefaultTierClampLo         = 1f;    // tier floor (§2 — raised from 0.5)
        public const float DefaultTierClampHi         = 5f;    // tier ceiling (§2)
        public const float DefaultMetersPerPe         = 1f;    // belly-PE → meters (§5.3 — derived from anchors)
        public const float DefaultFeastRangeCapMinutes = 28f;  // feast range cap, minutes (§4 / §6.2)
        public const float DefaultBukeMetersPerBerry  = 30f;   // meters per Bukeberry (§5 / §6.5)

        // ── Design anchors the baselines must satisfy (encoded so drift screams on boot) ───────
        /// <summary>The Twisted Portal's own pairing/visible range (nomap.md §7) and the design's
        /// from-empty maximum-jump distance — the anchor that makes 30 m/berry → exactly 10 berries.</summary>
        public const float PortalCeilingMeters = 300f;
        /// <summary>The locked "300 yards for 10 Bukeperries" — a from-empty max-range jump costs exactly this (design §5).</summary>
        public const int   CeilingBerryCost   = 10;

        /// <summary>
        /// tier(food) = round( clamp(total_stats / divisor, lo, hi) × 2 ) / 2 — snapped to 0.5 rungs,
        /// range [lo, hi] (design §2, verbatim). Reads the BASE stat budget, never the decayed live stat
        /// (impl-spec §5.1 🔴). Half-step rounding is AwayFromZero; for vanilla integer stats at the
        /// /30 baseline no value ever lands exactly on a .5 boundary (stats/15 is never a half-integer
        /// for integer stats), so ToEven vs AwayFromZero never differ — AwayFromZero is the explicit,
        /// intuitive "round half up" choice and is documented so a future divisor change is unambiguous.
        /// </summary>
        public static float Tier(float totalStats, float divisor, float clampLo, float clampHi)
        {
            if (divisor <= 0f) divisor = DefaultTierDivisor;       // defensive: a 0 divisor would NaN
            double raw = totalStats / divisor;
            double clamped = Clamp(raw, clampLo, clampHi);
            double snapped = Math.Round(clamped * 2.0, MidpointRounding.AwayFromZero) / 2.0;
            return (float)snapped;
        }

        /// <summary>Convenience overload using the locked baseline divisor + clamp.</summary>
        public static float Tier(float totalStats) =>
            Tier(totalStats, DefaultTierDivisor, DefaultTierClampLo, DefaultTierClampHi);

        /// <summary>
        /// The range-minutes a slot contributes to PE: its real remaining minutes, EXCEPT a feast slot
        /// is capped at FEAST_RANGE_CAP (design §4 / impl-spec §5.6). "Capped", not "fixed": a fresh
        /// feast (50 m real) contributes the 28 m cap, but a feast depleted below the cap contributes
        /// its real remaining minutes — so the depletion coupling still applies to feasts.
        /// </summary>
        public static float SlotRangeMinutes(in PeSlot slot, float feastRangeCapMinutes)
        {
            float real = slot.RealMinutes < 0f ? 0f : slot.RealMinutes;
            if (slot.IsFeast && real > feastRangeCapMinutes) return feastRangeCapMinutes;
            return real;
        }

        /// <summary>PE(slot) = rangeMinutes × tier(baseStats) (design §3 / §5.3).</summary>
        public static float SlotPe(in PeSlot slot, in PortalEnergyKnobs k)
        {
            float rangeMin = SlotRangeMinutes(slot, k.FeastRangeCapMinutes);
            float tier = Tier(slot.TotalStats, k.TierDivisor, k.TierClampLo, k.TierClampHi);
            return rangeMin * tier;
        }

        /// <summary>PE(player) = Σ SlotPe over all active slots (design §1 / §5.3).</summary>
        public static float BellyPe(PeSlot[] slots, in PortalEnergyKnobs k)
        {
            if (slots == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < slots.Length; i++) sum += SlotPe(slots[i], k);
            return sum;
        }

        /// <summary>belly_range = PE × METERS_PER_PE (design §5.3).</summary>
        public static float BellyRangeMeters(PeSlot[] slots, in PortalEnergyKnobs k) =>
            BellyPe(slots, k) * k.MetersPerPe;

        /// <summary>ceil(shortfall / BUKE_METERS_PER_BERRY); 0 when there's no shortfall (design §5.4).</summary>
        public static int BerriesForShortfall(float shortfallMeters, float bukeMetersPerBerry)
        {
            if (shortfallMeters <= 0f) return 0;
            if (bukeMetersPerBerry <= 0f) bukeMetersPerBerry = DefaultBukeMetersPerBerry; // defensive
            return (int)Math.Ceiling(shortfallMeters / (double)bukeMetersPerBerry);
        }

        /// <summary>
        /// Resolve the full cost of a jump of distance <paramref name="distanceMeters"/> against the
        /// belly <paramref name="slots"/> (design §5.3 / §5.4):
        ///   1. belly_range = Σ(rangeMin × tier) × METERS_PER_PE.
        ///   2. D ≤ belly_range → proportional partial drain (each slot loses
        ///      peSpent × rangeMin_i / bellyPe minutes, so Σ minutes×tier removed == peSpent exactly),
        ///      0 berries.
        ///   3. D > belly_range → drain the belly fully (each slot loses ALL its range-minutes →
        ///      food-empty by construction) and pay the shortfall with ceil(shortfall / 30 m) berries.
        /// The returned <see cref="JumpSolution.MinutesRemovedPerSlot"/> is what the engine subtracts
        /// from each slot's real m_time; whether the jump SUCCEEDS is the engine's call (it checks the
        /// player actually holds <see cref="JumpSolution.BerriesNeeded"/> Bukeberries).
        /// </summary>
        public static JumpSolution SolveJump(PeSlot[] slots, float distanceMeters, in PortalEnergyKnobs k)
        {
            slots = slots ?? Array.Empty<PeSlot>();
            float d = distanceMeters < 0f ? 0f : distanceMeters;

            // Per-slot range-minutes (the CAPPED feast clock; drives the PE READ only) reused for the
            // PE sum. The DEBIT below drains REAL minutes (slots[i].RealMinutes), not these.
            var rangeMin = new float[slots.Length];
            float bellyPe = 0f;
            for (int i = 0; i < slots.Length; i++)
            {
                rangeMin[i] = SlotRangeMinutes(slots[i], k.FeastRangeCapMinutes);
                float tier = Tier(slots[i].TotalStats, k.TierDivisor, k.TierClampLo, k.TierClampHi);
                bellyPe += rangeMin[i] * tier;
            }

            float metersPerPe = k.MetersPerPe <= 0f ? DefaultMetersPerPe : k.MetersPerPe;
            float bellyRange = bellyPe * metersPerPe;
            var removed = new float[slots.Length];

            if (d <= bellyRange)
            {
                // Belly covers it. You traveled D of your possible belly_range, so you burn that
                // fraction of EACH slot's REAL remaining time → you weaken in proportion to distance
                // (AT-ARRIVE-DEPLETED). frac = D / belly_range = peSpent / bellyPe (identical), and for
                // the all-personal-food case Σ(removedReal_i × tier_i) == peSpent exactly (real==range);
                // feast slots over-drain real vs the capped range by design (feasts aren't travel-optimal).
                float frac = bellyRange > 0f ? d / bellyRange : 0f;
                if (frac < 0f) frac = 0f;
                if (frac > 1f) frac = 1f;
                for (int i = 0; i < slots.Length; i++)
                {
                    float real = slots[i].RealMinutes < 0f ? 0f : slots[i].RealMinutes;
                    removed[i] = real * frac;
                }
                return new JumpSolution(bellyPe, bellyRange, bellyCovers: true,
                    shortfallMeters: 0f, berriesNeeded: 0, minutesRemovedPerSlot: removed);
            }

            // Shortfall → drain the belly FULLY (every slot's REAL minutes removed → food-empty by
            // construction) + berries for the rest.
            for (int i = 0; i < slots.Length; i++)
                removed[i] = slots[i].RealMinutes < 0f ? 0f : slots[i].RealMinutes;
            float shortfall = d - bellyRange;
            int berries = BerriesForShortfall(shortfall, k.BukeMetersPerBerry);
            return new JumpSolution(bellyPe, bellyRange, bellyCovers: false,
                shortfallMeters: shortfall, berriesNeeded: berries, minutesRemovedPerSlot: removed);
        }

        // ── tiny engine-free clamp (System.Math.Clamp isn't in net48's surface uniformly) ──────
        private static double Clamp(double v, double lo, double hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
