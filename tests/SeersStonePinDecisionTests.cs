// ============================================================================
//  Seer's Stone — PIN-BY-LOOK decision tests (engine-free, net8.0/xUnit, CI-gated)
// ----------------------------------------------------------------------------
//  Gates the pin rules (Daniel 2026-06-25): ignore-unlisted at the pin site,
//  NO count on the label ("just Blueberries"), private-by-default, and the
//  same-name-within-merge-radius dedup. If a future edit lets an unlisted hit pin,
//  re-adds a count to the label, or breaks the merge, these fail.
// ============================================================================

using System.Collections.Generic;
using SBPR.Trailborne.Features.SeersStone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public class SeersStonePinDecisionTests
    {
        private static SeersStoneEligibility Elig(params string[] names) => new SeersStoneEligibility(names);
        private static Vec3 At(float x, float z) => new Vec3(x, 0f, z);
        private static List<ExistingPin> Pins(params ExistingPin[] p) => new List<ExistingPin>(p);

        // ── Eligibility gate at the pin site (defense in depth) ──────────────────────────
        [Fact]
        public void Unlisted_hit_does_not_pin()
        {
            var plan = SeersStonePinDecision.Decide(
                "Pickable_Stone", "Stone", WispHitKind.Pickable, At(0, 0),
                Elig("RaspberryBush"), Pins());
            Assert.False(plan.ShouldPin);
            Assert.Equal("ineligible", plan.Reason);
        }

        [Fact]
        public void Listed_hit_pins()
        {
            var plan = SeersStonePinDecision.Decide(
                "RaspberryBush(Clone)", "Raspberries", WispHitKind.Pickable, At(0, 0),
                Elig("RaspberryBush"), Pins());
            Assert.True(plan.ShouldPin);
            Assert.Equal("Raspberries", plan.Label);
            Assert.True(plan.Private);                 // private by default (the lens is personal)
            Assert.Equal(WispHitKind.Pickable, plan.Kind);
        }

        // ── NO count on the label (Daniel: "just Blueberries") ───────────────────────────
        [Theory]
        [InlineData("Blueberries x12", "Blueberries")]
        [InlineData("Blueberries ×12", "Blueberries")]
        [InlineData("Blueberries (12)", "Blueberries")]
        [InlineData("Blueberries", "Blueberries")]
        public void Label_strips_any_count(string hover, string expected)
        {
            var plan = SeersStonePinDecision.Decide(
                "BlueberryBush", hover, WispHitKind.Pickable, At(0, 0),
                Elig("BlueberryBush"), Pins());
            Assert.True(plan.ShouldPin);
            Assert.Equal(expected, plan.Label);
        }

        [Fact]
        public void CleanLabel_is_idempotent_and_trims()
        {
            Assert.Equal("Blueberries", SeersStonePinDecision.CleanLabel("  Blueberries x9 "));
            Assert.Equal("Blueberries", SeersStonePinDecision.CleanLabel("Blueberries"));
            Assert.Equal("", SeersStonePinDecision.CleanLabel("   "));
            Assert.Equal("", SeersStonePinDecision.CleanLabel(null));
        }

        // ── Merge: a same-name pin within radius blocks a duplicate ──────────────────────
        [Fact]
        public void Same_name_pin_within_radius_merges_no_new_pin()
        {
            var existing = Pins(new ExistingPin("Raspberries", At(5, 0)));
            var plan = SeersStonePinDecision.Decide(
                "RaspberryBush", "Raspberries", WispHitKind.Pickable, At(10, 0),  // 5 m away, < 15 m
                Elig("RaspberryBush"), existing);
            Assert.False(plan.ShouldPin);
            Assert.Equal("merged", plan.Reason);
        }

        [Fact]
        public void Same_name_pin_beyond_radius_still_pins()
        {
            var existing = Pins(new ExistingPin("Raspberries", At(0, 0)));
            var plan = SeersStonePinDecision.Decide(
                "RaspberryBush", "Raspberries", WispHitKind.Pickable, At(30, 0),  // 30 m away, > 15 m
                Elig("RaspberryBush"), existing);
            Assert.True(plan.ShouldPin);
        }

        [Fact]
        public void Different_name_pin_within_radius_does_not_block()
        {
            var existing = Pins(new ExistingPin("Mushroom", At(2, 0)));
            var plan = SeersStonePinDecision.Decide(
                "RaspberryBush", "Raspberries", WispHitKind.Pickable, At(3, 0),
                Elig("RaspberryBush"), existing);
            Assert.True(plan.ShouldPin); // different label ⇒ not a merge candidate
        }

        [Fact]
        public void Merge_is_count_insensitive_on_existing_pin_name()
        {
            // An existing pin labelled with a stray count must still merge a new same-base pin.
            var existing = Pins(new ExistingPin("Raspberries x4", At(0, 0)));
            var plan = SeersStonePinDecision.Decide(
                "RaspberryBush", "Raspberries", WispHitKind.Pickable, At(3, 0),
                Elig("RaspberryBush"), existing);
            Assert.False(plan.ShouldPin);
            Assert.Equal("merged", plan.Reason);
        }

        // ── Location path carries its kind through ───────────────────────────────────────
        [Fact]
        public void Location_hit_pins_with_location_kind()
        {
            var plan = SeersStonePinDecision.Decide(
                "Crypt2", "Burial Chambers", WispHitKind.Location, At(0, 0),
                Elig("Crypt2"), Pins());
            Assert.True(plan.ShouldPin);
            Assert.Equal(WispHitKind.Location, plan.Kind);
            Assert.Equal("Burial Chambers", plan.Label);
        }

        // ── Empty label fails closed ─────────────────────────────────────────────────────
        [Fact]
        public void Empty_friendly_name_does_not_pin()
        {
            var plan = SeersStonePinDecision.Decide(
                "RaspberryBush", "   ", WispHitKind.Pickable, At(0, 0),
                Elig("RaspberryBush"), Pins());
            Assert.False(plan.ShouldPin);
            Assert.Equal("empty-label", plan.Reason);
        }

        // ── Null eligibility fails closed (the fail-safe) ────────────────────────────────
        [Fact]
        public void Null_eligibility_does_not_pin()
        {
            var plan = SeersStonePinDecision.Decide(
                "RaspberryBush", "Raspberries", WispHitKind.Pickable, At(0, 0),
                null!, Pins());
            Assert.False(plan.ShouldPin);
        }
    }
}
