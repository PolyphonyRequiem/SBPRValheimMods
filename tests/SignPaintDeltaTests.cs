// ============================================================================
//  SignPaintDelta — xUnit structural tests (card t_6df12ca8).
// ----------------------------------------------------------------------------
//  Tests the SHIPPED pure decision logic SignPaintDelta (link-compiled from
//  ../src, not copied — see the .csproj). This is the durable CI fence for the
//  Painted Sign's PER-CHANGED-SLOT consume cost (§A2.6, LOCKED Daniel 2026-06-21):
//  it pins the FULL two-predicate truth table — the CHANGED-SET gate (HasAnyChange,
//  incl. clears) vs the BILLABLE DELTA cost (ComputeChangedCost) — WITHOUT touching
//  any volatile UnityEngine/Harmony internals (the engine-free link-compile pattern
//  shared with LensHandoffDecision / CompassNorthGate).
//
//  WHY TWO PREDICATES (the load-bearing design). A naive "gate the button on
//  delta-cost-non-empty" REINTRODUCES a bug: a PURE CLEAR (board Red→"") has a
//  NON-EMPTY changed-set but an EMPTY delta cost, so cost-count gating would make
//  clears un-committable. So the commit gate is HasAnyChange (incl. clears), and the
//  charge is ComputeChangedCost (billable = changed AND new non-empty). These tests
//  assert both, and that they DIVERGE exactly on the pure-clear / no-op boundary.
//
//  The named acceptance tests (AT-1..AT-8 on the card) reduce to this pure logic:
//    AT-1 one-slot change · AT-2 two-slot change · AT-3 no-op (disabled) ·
//    AT-4 first-paint unchanged · AT-5 pure-clear free+committable ·
//    AT-6 same-color re-apply · AT-8 multi-paint-in-session (delta vs current).
//  The engine-bound halves (actual pigment consumption, UI button enable, the live
//  ZDO read) are wired in SignPaintBackend/SignPaintPanel and cannot be proven
//  headless ("logs green ≠ playable"); this fence proves the DECISION they consume.
// ============================================================================

using System.Collections.Generic;
using SBPR.Trailborne.Features.Signs;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class SignPaintDeltaTests
    {
        // Convenience: assert a cost map equals an expected (color→count) set exactly.
        private static void AssertCost(Dictionary<string, int> actual, params (string color, int count)[] expected)
        {
            Assert.Equal(expected.Length, actual.Count);
            foreach (var (color, count) in expected)
            {
                Assert.True(actual.ContainsKey(color), $"expected cost to contain '{color}'");
                Assert.Equal(count, actual[color]);
            }
        }

        // ── AT-1: one-slot change — change ONLY Text (Red→Blue), Board/Border unchanged ──
        //  consumes exactly 1 Blue; the unchanged Board/Border are free.

        [Fact]
        public void AT1_one_slot_change_charges_only_the_changed_slot()
        {
            var cost = SignPaintDelta.ComputeChangedCost(
                oldText: "red", oldBoard: "white", oldBorder: "white",
                newText: "blue", newBoard: "white", newBorder: "white");
            AssertCost(cost, ("blue", 1));
        }

        [Fact]
        public void AT1_one_slot_change_is_a_change()
            => Assert.True(SignPaintDelta.HasAnyChange("red", "white", "white", "blue", "white", "white"));

        // ── AT-2: two-slot change — change two slots; the third (unchanged) is free ──

        [Fact]
        public void AT2_two_slot_change_charges_exactly_those_two()
        {
            // Text red→blue, Board white→black, Border white unchanged.
            var cost = SignPaintDelta.ComputeChangedCost(
                oldText: "red", oldBoard: "white", oldBorder: "white",
                newText: "blue", newBoard: "black", newBorder: "white");
            AssertCost(cost, ("blue", 1), ("black", 1));
        }

        // ── AT-3: no-op — open a painted sign, change NOTHING ──
        //  HasAnyChange=false (button disables silently); delta cost empty.

        [Fact]
        public void AT3_no_op_has_no_change()
            => Assert.False(SignPaintDelta.HasAnyChange("red", "white", "blue", "red", "white", "blue"));

        [Fact]
        public void AT3_no_op_delta_cost_is_empty()
        {
            var cost = SignPaintDelta.ComputeChangedCost("red", "white", "blue", "red", "white", "blue");
            Assert.Empty(cost);
        }

        // ── AT-4: first paint of a fully-unpainted sign — all ""→color are changes ──
        //  → full cost, exactly the per-filled first-paint behaviour (1 Red + 2 White).

        [Fact]
        public void AT4_first_paint_charges_full_cost()
        {
            var cost = SignPaintDelta.ComputeChangedCost(
                oldText: "", oldBoard: "", oldBorder: "",
                newText: "red", newBoard: "white", newBorder: "white");
            AssertCost(cost, ("red", 1), ("white", 2));
        }

        [Fact]
        public void AT4_first_paint_is_a_change()
            => Assert.True(SignPaintDelta.HasAnyChange("", "", "", "red", "white", "white"));

        // ── AT-5: pure clear — clear ONE slot (Board Red→""), nothing else changes ──
        //  THE divergence: HasAnyChange=true (committable) BUT delta cost is EMPTY (free).
        //  This is exactly why the commit gate must be HasAnyChange, not cost.Count.

        [Fact]
        public void AT5_pure_clear_is_a_change()
            => Assert.True(SignPaintDelta.HasAnyChange("red", "red", "blue", "red", "", "blue"));

        [Fact]
        public void AT5_pure_clear_costs_nothing()
        {
            var cost = SignPaintDelta.ComputeChangedCost("red", "red", "blue", "red", "", "blue");
            Assert.Empty(cost);
        }

        [Fact]
        public void AT5_pure_clear_diverges_change_true_but_cost_empty()
        {
            // The load-bearing invariant: a pure clear is committable (HasAnyChange) yet
            // free (empty cost). Gating on cost.Count would wrongly disable the button.
            const string ot = "red", ob = "red", obr = "blue";
            const string nt = "red", nb = "", nbr = "blue";
            Assert.True(SignPaintDelta.HasAnyChange(ot, ob, obr, nt, nb, nbr));
            Assert.Empty(SignPaintDelta.ComputeChangedCost(ot, ob, obr, nt, nb, nbr));
        }

        // ── AT-6: same-color re-apply — re-select the SAME colors it already has ──
        //  no change → disabled, consumes nothing. (Same shape as AT-3 from a full sign.)

        [Fact]
        public void AT6_same_color_reapply_has_no_change()
            => Assert.False(SignPaintDelta.HasAnyChange("red", "white", "black", "red", "white", "black"));

        [Fact]
        public void AT6_same_color_reapply_costs_nothing()
            => Assert.Empty(SignPaintDelta.ComputeChangedCost("red", "white", "black", "red", "white", "black"));

        // ── AT-8: multi-paint in one session — second commit deltas vs the JUST-painted
        //  state, not vs the original. At the pure level: the "old" passed is the current
        //  (just-written) state, so re-deltaing from it charges only the new change.

        [Fact]
        public void AT8_second_paint_deltas_against_current_state()
        {
            // Session paint #1 wrote (blue, white, white). Paint #2 changes only border→black.
            // Deltaing against the just-written state charges 1 black, NOT the whole sign.
            var cost = SignPaintDelta.ComputeChangedCost(
                oldText: "blue", oldBoard: "white", oldBorder: "white",
                newText: "blue", newBoard: "white", newBorder: "black");
            AssertCost(cost, ("black", 1));
        }

        // ── Billable accounting: same color across M billable changed slots = M ──

        [Fact]
        public void Same_color_across_three_billable_changes_costs_three()
        {
            // Fully unpainted → all three painted the same color = 3 of that pigment.
            var cost = SignPaintDelta.ComputeChangedCost("", "", "", "red", "red", "red");
            AssertCost(cost, ("red", 3));
        }

        [Fact]
        public void Two_changed_slots_same_color_costs_two_third_unchanged_free()
        {
            // Text & Border change to white; Board already white (unchanged → free).
            var cost = SignPaintDelta.ComputeChangedCost(
                oldText: "red", oldBoard: "white", oldBorder: "red",
                newText: "white", newBoard: "white", newBorder: "white");
            AssertCost(cost, ("white", 2));
        }

        // ── Mixed change + clear: a clear (change, 0 cost) alongside a billable change ──

        [Fact]
        public void Clear_one_slot_and_change_another_charges_only_the_billable_change()
        {
            // Board cleared (white→"" : change, 0 cost) AND Border blue→black (billable).
            // Text unchanged. Cost = just 1 black; both slots are "changes" for the gate.
            const string ot = "red", ob = "white", obr = "blue";
            const string nt = "red", nb = "", nbr = "black";
            Assert.True(SignPaintDelta.HasAnyChange(ot, ob, obr, nt, nb, nbr));
            AssertCost(SignPaintDelta.ComputeChangedCost(ot, ob, obr, nt, nb, nbr), ("black", 1));
        }

        // ── null / "" normalization: an unset slot is "" OR null, treated identically ──
        //  (ZDO Read*Color returns ""; the panel seeds ""; CommitPaint passes color ?? "".
        //   The pure function must not treat null→"" as a change, nor ""→null, etc.)

        [Fact]
        public void Null_and_empty_are_both_unset_no_change()
        {
            Assert.False(SignPaintDelta.HasAnyChange(null!, null!, null!, "", "", ""));
            Assert.False(SignPaintDelta.HasAnyChange("", "", "", null!, null!, null!));
            Assert.Empty(SignPaintDelta.ComputeChangedCost(null!, null!, null!, "", "", ""));
        }

        [Fact]
        public void Null_old_to_color_is_a_billable_change()
        {
            // A null (unset) baseline painted to a color is a first-paint billable change.
            Assert.True(SignPaintDelta.HasAnyChange(null!, null!, null!, "red", null!, null!));
            AssertCost(SignPaintDelta.ComputeChangedCost(null!, null!, null!, "red", null!, null!), ("red", 1));
        }

        [Fact]
        public void Clearing_to_null_is_a_change_but_free()
        {
            // color→null is a clear, same as color→"" — a change with 0 cost.
            Assert.True(SignPaintDelta.HasAnyChange("red", "white", "blue", "red", "white", null!));
            Assert.Empty(SignPaintDelta.ComputeChangedCost("red", "white", "blue", "red", "white", null!));
        }

        // ── Ordinal compare (mirrors SignTag.cs:124/135 / ComputeCost's Ordinal dict) ──

        [Fact]
        public void Color_compare_is_ordinal_case_sensitive()
        {
            // "red" vs "RED" differ ordinally → a change (color ids are lowercase by
            // construction; this pins the compare semantics so a future case-fold can't
            // silently make a real change look like a no-op).
            Assert.True(SignPaintDelta.HasAnyChange("red", "white", "blue", "RED", "white", "blue"));
        }
    }
}
