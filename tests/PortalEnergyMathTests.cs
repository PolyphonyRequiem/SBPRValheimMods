// ============================================================================
//  PortalEnergyMath — xUnit structural tests (card t_6e992a30, C2). AT-PE-MATH.
// ----------------------------------------------------------------------------
//  Tests the SHIPPED pure FOOD-AS-FUEL Portal Energy math PortalEnergyMath
//  (link-compiled from ../src, not copied — see the .csproj). This is the durable
//  CI fence for the Twisted Portal cost model: it pins the tier curve, the PE sum,
//  the feast range-clock, the distance→food-time debit, and the Bukeberry shortfall
//  solve, WITHOUT touching any volatile UnityEngine/Valheim internals (the engine-free
//  link-compile pattern shared with SunstoneHaloGeometry / CompassNorthGate /
//  BoundedMapMath).
//
//  WHY THIS MATTERS. The cost model is the load-bearing economy of the Twisted
//  Portal, and every NUMBER is a design-locked baseline (twisted-portal-food-charge.md
//  §2-§6). Failure classes this fence catches before they ship:
//    • tier curve drift — a /30 divisor change, a dropped [1,5] clamp, or a
//      round-to-even regression that mis-prices a food's travel tier (AT-PE-MATH).
//    • feast-clock drift — a feast contributing its real ~50 m timer instead of the
//      normalised FEAST_RANGE_CAP, which would let "just eat feast to travel" win
//      (AT-FEAST-CLOCK).
//    • debit drift — a long jump that doesn't drain the belly proportionally (so the
//      player wouldn't arrive depleted, AT-ARRIVE-DEPLETED) or a berry jump that
//      doesn't empty the belly (AT-PE-DEBIT / AT-BUKE-RESERVE).
//    • the locked anchor — 10 berries == a from-empty 300 m max jump (AT-BUKE-RESERVE).
//
//  Every asserted number is derived from twisted-portal-food-charge.md §3 (the worked
//  60-food PE table) and §5 (the berry math); none are magic. The design doc is the
//  authority — these tests encode its worked examples so the SHIPPED math can't drift
//  from them silently.
// ============================================================================

using SBPR.Trailborne.Features.Portals;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class PortalEnergyMathTests
    {
        // The locked baseline knobs (PortalEnergyMath.Default* — the same values the runtime config
        // binds against and SpecCheck asserts at boot). Tests run against these unless they explicitly
        // vary a knob, so a Default* drift is caught here AND at boot.
        private static PortalEnergyKnobs Baseline => PortalEnergyKnobs.Default;

        // ── TIER CURVE (design §2 / §3) — tier = round(clamp(stats/30, 1, 5) × 2) / 2 ────────────────

        [Theory]
        // (totalStats, expectedTier) — every value worked from the design §3 table.
        [InlineData(143f, 5.0f)]   // Marinated greens — the lone 5.0 ceiling
        [InlineData(140f, 4.5f)]   // Piquant pie
        [InlineData(134f, 4.5f)]   // Roasted crust pie / Mashed meat
        [InlineData(127f, 4.0f)]   // Seeker aspic / Fiery svinstew
        [InlineData(120f, 4.0f)]   // Yggdrasil porridge / Cooked bonemaw
        [InlineData(113f, 4.0f)]   // Misthare supreme / Mushroom omelette
        [InlineData(106f, 3.5f)]   // Serpent stew / Meat platter / Salad
        [InlineData(100f, 3.5f)]   // Blood pudding
        [InlineData(93f, 3.0f)]    // Cooked serpent meat / Bread / Fish wraps
        [InlineData(86f, 3.0f)]    // Wolf skewer / Eyescream / Frosted sweetbread
        [InlineData(80f, 2.5f)]    // Onion soup / Cooked chicken
        [InlineData(75f, 2.5f)]    // Magecap (75/30 = 2.5 exactly → tier 2.5)
        [InlineData(73f, 2.5f)]    // Sausages / Turnip stew
        [InlineData(66f, 2.0f)]    // Wolf jerky / Muckshake
        [InlineData(60f, 2.0f)]    // Deer stew / Carrot soup / Cooked fish
        [InlineData(53f, 2.0f)]    // Minced meat sauce / Onion / Cloudberries
        [InlineData(47f, 1.5f)]    // Cooked deer meat
        [InlineData(46f, 1.5f)]    // Boar jerky
        [InlineData(43f, 1.5f)]    // Honey
        [InlineData(33f, 1.0f)]    // Blueberries (33/30 = 1.1 → clamp stays 1.1 → ×2=2.2 → round 2 → 1.0)
        [InlineData(30f, 1.0f)]    // Mushroom / Royal jelly
        [InlineData(27f, 1.0f)]    // Raspberries — the floor
        public void Tier_matches_design_section_3_worked_values(float totalStats, float expectedTier)
        {
            Assert.Equal(expectedTier, PortalEnergyMath.Tier(totalStats));
        }

        [Fact]
        public void Tier_clamps_to_the_floor_and_ceiling()
        {
            // Below the floor: 0 stats → clamp lifts to 1.0 (the §2 floor, raised from 0.5).
            Assert.Equal(1.0f, PortalEnergyMath.Tier(0f));
            Assert.Equal(1.0f, PortalEnergyMath.Tier(15f));   // 15/30 = 0.5 → clamp → 1.0
            // Above the ceiling: huge stats → clamp caps at 5.0.
            Assert.Equal(5.0f, PortalEnergyMath.Tier(150f));  // 150/30 = 5.0 exactly
            Assert.Equal(5.0f, PortalEnergyMath.Tier(300f));  // 300/30 = 10 → clamp → 5.0
        }

        [Fact]
        public void Tier_snaps_to_half_rungs_only()
        {
            // Every tier the curve produces is a multiple of 0.5 in [1.0, 5.0]. Sweep a wide stat range.
            for (int stats = 0; stats <= 400; stats++)
            {
                float t = PortalEnergyMath.Tier(stats);
                Assert.True(t >= 1.0f && t <= 5.0f, $"tier {t} out of [1,5] at stats={stats}");
                float doubled = t * 2f;
                Assert.Equal(doubled, System.MathF.Round(doubled));   // t*2 is an integer ⇒ t is a 0.5 rung
            }
        }

        // ── PE PER SLOT + SUM (design §3) — PE(slot) = rangeMinutes × tier ───────────────────────────

        [Theory]
        // (totalStats, realMinutes, expectedPe) — personal foods (isFeast=false), full remaining duration.
        [InlineData(143f, 30f, 150.0f)]  // Marinated greens — PE 150, the personal ceiling
        [InlineData(140f, 30f, 135.0f)]  // Piquant pie
        [InlineData(127f, 30f, 120.0f)]  // Seeker aspic
        [InlineData(134f, 25f, 112.5f)]  // Mashed meat
        [InlineData(106f, 30f, 105.0f)]  // Serpent stew
        [InlineData(120f, 25f, 100.0f)]  // Yggdrasil porridge
        [InlineData(73f, 25f, 62.5f)]    // Sausages
        [InlineData(66f, 30f, 60.0f)]    // Wolf jerky
        [InlineData(75f, 15f, 37.5f)]    // Magecap
        [InlineData(27f, 10f, 10.0f)]    // Raspberries — PE 10, the floor
        [InlineData(33f, 10f, 10.0f)]    // Blueberries
        public void SlotPe_matches_design_section_3_personal_food_table(float stats, float minutes, float expectedPe)
        {
            var slot = new PeSlot(stats, minutes, isFeast: false);
            Assert.Equal(expectedPe, PortalEnergyMath.SlotPe(slot, Baseline), precision: 3);
        }

        [Fact]
        public void BellyPe_sums_all_active_slots()
        {
            var slots = new[]
            {
                new PeSlot(143f, 30f, false),   // PE 150
                new PeSlot(73f, 25f, false),    // PE 62.5
                new PeSlot(27f, 10f, false),    // PE 10
            };
            Assert.Equal(222.5f, PortalEnergyMath.BellyPe(slots, Baseline), precision: 3);
        }

        [Fact]
        public void Empty_belly_is_zero_pe_and_zero_range()
        {
            var empty = new PeSlot[0];
            Assert.Equal(0f, PortalEnergyMath.BellyPe(empty, Baseline));
            Assert.Equal(0f, PortalEnergyMath.BellyRangeMeters(empty, Baseline));
        }

        // ── FEAST RANGE CLOCK (design §4 / impl-spec §5.6) — capped at FEAST_RANGE_CAP, not the 50 m timer ──

        [Theory]
        // Design §4 feast table: PE = FEAST_RANGE_CAP(28) × tier. Feast real timer is ~50 m but irrelevant.
        [InlineData(70f, 2.5f, 70.0f)]    // Whole roasted Meadow boar — tier 2.5, PE 70
        [InlineData(90f, 3.0f, 84.0f)]    // Sailor's bounty — tier 3.0, PE 84
        [InlineData(110f, 3.5f, 98.0f)]   // Plains pie picnic — tier 3.5, PE 98
        [InlineData(163f, 5.0f, 140.0f)]  // Mushrooms galore — tier 5.0, PE 140
        [InlineData(188f, 5.0f, 140.0f)]  // Ashlands gourmet bowl — tier 5.0, PE 140 (the best feast)
        public void Feast_uses_the_normalised_range_cap_not_its_real_timer(float stats, float expectedTier, float expectedPe)
        {
            // A fresh feast: 50 real minutes, but the range clock is capped at 28.
            var feast = new PeSlot(stats, realMinutes: 50f, isFeast: true);
            Assert.Equal(expectedTier, PortalEnergyMath.Tier(stats));
            Assert.Equal(expectedPe, PortalEnergyMath.SlotPe(feast, Baseline), precision: 3);
        }

        [Fact]
        public void Best_feast_stays_under_the_best_personal_meal()
        {
            // The load-bearing §4 invariant: "the travel-optimal pick is always a personal meal."
            var bestFeast = new PeSlot(188f, 50f, isFeast: true);    // Ashlands gourmet bowl → PE 140
            var bestPersonal = new PeSlot(143f, 30f, isFeast: false); // Marinated greens → PE 150
            float feastPe = PortalEnergyMath.SlotPe(bestFeast, Baseline);
            float personalPe = PortalEnergyMath.SlotPe(bestPersonal, Baseline);
            Assert.True(feastPe < personalPe, $"best feast PE {feastPe} must stay under best personal PE {personalPe}");
        }

        [Fact]
        public void Feast_below_the_cap_uses_its_real_remaining_minutes()
        {
            // "Capped", not "fixed": a feast depleted below FEAST_RANGE_CAP contributes its REAL minutes,
            // so the depletion coupling still applies to feasts.
            var depletedFeast = new PeSlot(188f, realMinutes: 10f, isFeast: true);   // 10 < 28 → real wins
            Assert.Equal(10f * 5.0f, PortalEnergyMath.SlotPe(depletedFeast, Baseline), precision: 3);  // PE 50
        }

        // ── JUMP SOLVE: belly covers it → proportional drain, 0 berries (AT-PE-DEBIT / AT-ARRIVE-DEPLETED) ──

        [Fact]
        public void Short_jump_within_belly_range_drains_proportionally_and_burns_no_berries()
        {
            // One Marinated greens slot: PE 150 → belly_range 150 m (METERS_PER_PE = 1.0).
            var slots = new[] { new PeSlot(143f, 30f, false) };
            var sol = PortalEnergyMath.SolveJump(slots, distanceMeters: 100f, Baseline);

            Assert.True(sol.BellyCovers);
            Assert.Equal(0, sol.BerriesNeeded);
            Assert.Equal(0f, sol.ShortfallMeters);
            Assert.Equal(150f, sol.BellyRangeMeters, precision: 3);
            // Drained 100/150 = 2/3 of the slot's 30 real minutes → 20 minutes removed (slot 30 → 10).
            Assert.Single(sol.MinutesRemovedPerSlot);
            Assert.Equal(20f, sol.MinutesRemovedPerSlot[0], precision: 3);
        }

        [Fact]
        public void A_longer_jump_drains_more_than_a_shorter_one_same_belly()
        {
            // AT-ARRIVE-DEPLETED: the distance↔depletion coupling is monotonic.
            var make = new System.Func<PeSlot[]>(() => new[] { new PeSlot(143f, 30f, false) });
            var shortHop = PortalEnergyMath.SolveJump(make(), 30f, Baseline);
            var longHaul = PortalEnergyMath.SolveJump(make(), 120f, Baseline);
            Assert.True(longHaul.MinutesRemovedPerSlot[0] > shortHop.MinutesRemovedPerSlot[0]);
            // 30 m → 6 min removed (30/150 × 30); 120 m → 24 min removed (120/150 × 30).
            Assert.Equal(6f, shortHop.MinutesRemovedPerSlot[0], precision: 3);
            Assert.Equal(24f, longHaul.MinutesRemovedPerSlot[0], precision: 3);
        }

        [Fact]
        public void Exact_belly_range_jump_drains_the_whole_belly_no_berries()
        {
            var slots = new[] { new PeSlot(143f, 30f, false) };   // range 150 m
            var sol = PortalEnergyMath.SolveJump(slots, distanceMeters: 150f, Baseline);
            Assert.True(sol.BellyCovers);                          // D == range is still "covered"
            Assert.Equal(0, sol.BerriesNeeded);
            Assert.Equal(30f, sol.MinutesRemovedPerSlot[0], precision: 3);   // full slot drained
        }

        [Fact]
        public void Multi_slot_belly_drains_every_slot_by_the_same_fraction()
        {
            // Two slots, total PE 150 + 62.5 = 212.5 → range 212.5 m. Jump 106.25 m = half the range →
            // every slot loses half its REAL minutes.
            var slots = new[]
            {
                new PeSlot(143f, 30f, false),   // 30 real min
                new PeSlot(73f, 25f, false),    // 25 real min
            };
            var sol = PortalEnergyMath.SolveJump(slots, distanceMeters: 106.25f, Baseline);
            Assert.True(sol.BellyCovers);
            Assert.Equal(15f, sol.MinutesRemovedPerSlot[0], precision: 3);    // half of 30
            Assert.Equal(12.5f, sol.MinutesRemovedPerSlot[1], precision: 3);  // half of 25
        }

        // ── JUMP SOLVE: shortfall → empty the belly + ceil(shortfall/30) berries (AT-BUKE-RESERVE) ──────

        [Fact]
        public void From_empty_belly_a_300m_max_jump_costs_exactly_10_berries()
        {
            // The locked anchor (design §5): "300 yards for 10 Bukeperries." Empty belly → 300 m shortfall.
            var empty = new PeSlot[0];
            var sol = PortalEnergyMath.SolveJump(empty, distanceMeters: 300f, Baseline);
            Assert.False(sol.BellyCovers);
            Assert.Equal(0f, sol.BellyRangeMeters);
            Assert.Equal(300f, sol.ShortfallMeters, precision: 3);
            Assert.Equal(10, sol.BerriesNeeded);
        }

        [Fact]
        public void Belly_then_berries_only_the_shortfall_is_paid_in_berries()
        {
            // One Wolf jerky slot: PE 60 → range 60 m. Jump 150 m → shortfall 90 m → ceil(90/30) = 3 berries,
            // and the belly is drained FULLY (food-empty by construction).
            var slots = new[] { new PeSlot(66f, 30f, false) };
            var sol = PortalEnergyMath.SolveJump(slots, distanceMeters: 150f, Baseline);
            Assert.False(sol.BellyCovers);
            Assert.Equal(60f, sol.BellyRangeMeters, precision: 3);
            Assert.Equal(90f, sol.ShortfallMeters, precision: 3);
            Assert.Equal(3, sol.BerriesNeeded);
            Assert.Equal(30f, sol.MinutesRemovedPerSlot[0], precision: 3);   // whole slot drained → food-empty
        }

        [Fact]
        public void Shortfall_berries_round_up()
        {
            // Empty belly, 31 m jump → ceil(31/30) = 2 berries (not 1 — the shortfall rounds UP).
            var empty = new PeSlot[0];
            var sol = PortalEnergyMath.SolveJump(empty, distanceMeters: 31f, Baseline);
            Assert.Equal(2, sol.BerriesNeeded);
            // Exactly 30 m → 1 berry (no rounding up at the boundary).
            Assert.Equal(1, PortalEnergyMath.SolveJump(empty, 30f, Baseline).BerriesNeeded);
            // 1 m → 1 berry.
            Assert.Equal(1, PortalEnergyMath.SolveJump(empty, 1f, Baseline).BerriesNeeded);
        }

        [Fact]
        public void Zero_distance_jump_costs_nothing()
        {
            var slots = new[] { new PeSlot(143f, 30f, false) };
            var sol = PortalEnergyMath.SolveJump(slots, distanceMeters: 0f, Baseline);
            Assert.True(sol.BellyCovers);
            Assert.Equal(0, sol.BerriesNeeded);
            Assert.Equal(0f, sol.MinutesRemovedPerSlot[0], precision: 3);   // nothing removed
        }

        [Fact]
        public void BerriesForShortfall_is_zero_when_there_is_no_shortfall()
        {
            Assert.Equal(0, PortalEnergyMath.BerriesForShortfall(0f, PortalEnergyMath.DefaultBukeMetersPerBerry));
            Assert.Equal(0, PortalEnergyMath.BerriesForShortfall(-5f, PortalEnergyMath.DefaultBukeMetersPerBerry));
        }

        // ── KNOB TUNABILITY — the architecture is fixed, the numbers are dials (design §6) ──────────────

        [Fact]
        public void MetersPerPe_scales_belly_range_linearly()
        {
            var slots = new[] { new PeSlot(143f, 30f, false) };   // PE 150
            var doubled = new PortalEnergyKnobs(30f, 1f, 5f, metersPerPe: 2f, 28f, 30f);
            Assert.Equal(300f, PortalEnergyMath.BellyRangeMeters(slots, doubled), precision: 3);   // 150 PE × 2 = 300 m
        }

        [Fact]
        public void Lowering_buke_meters_per_berry_makes_a_max_jump_cost_more_berries()
        {
            // At 15 m/berry instead of 30, a from-empty 300 m jump costs 20 berries, not 10.
            var empty = new PeSlot[0];
            var costlier = new PortalEnergyKnobs(30f, 1f, 5f, 1f, 28f, bukeMetersPerBerry: 15f);
            Assert.Equal(20, PortalEnergyMath.SolveJump(empty, 300f, costlier).BerriesNeeded);
        }

        [Fact]
        public void A_lower_feast_cap_lowers_feast_range()
        {
            var feast = new PeSlot(188f, 50f, isFeast: true);     // tier 5.0
            var tighter = new PortalEnergyKnobs(30f, 1f, 5f, 1f, feastRangeCapMinutes: 24f, 30f);
            Assert.Equal(24f * 5.0f, PortalEnergyMath.SlotPe(feast, tighter), precision: 3);   // PE 120, down from 140
        }

        // ── DEFENSIVE — bad knobs never NaN/crash the solve (a fat-fingered .cfg can't brick travel) ─────

        [Fact]
        public void A_zero_divisor_falls_back_to_the_baseline_not_NaN()
        {
            float t = PortalEnergyMath.Tier(90f, divisor: 0f, clampLo: 1f, clampHi: 5f);
            Assert.False(float.IsNaN(t));
            Assert.Equal(3.0f, t);   // falls back to /30 → 90/30 = 3.0
        }

        [Fact]
        public void Null_slots_solve_to_a_clean_empty_belly()
        {
            var sol = PortalEnergyMath.SolveJump(null!, distanceMeters: 60f, Baseline);
            Assert.False(sol.BellyCovers);
            Assert.Equal(0f, sol.BellyRangeMeters);
            Assert.Equal(2, sol.BerriesNeeded);   // ceil(60/30)
            Assert.Empty(sol.MinutesRemovedPerSlot);
        }
    }
}
