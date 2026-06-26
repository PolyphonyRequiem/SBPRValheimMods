// ============================================================================
//  Seer's Stone — wisp-eligibility CORE tests (engine-free, net8.0/xUnit, CI-gated)
// ----------------------------------------------------------------------------
//  Gates the load-bearing M1 policy: IGNORE-UNLISTED + (Clone)-normalization +
//  the empty-set fail-safe (Daniel 2026-06-25). If a future edit flips ignore-
//  unlisted into derive-unlisted, or breaks the clone-suffix strip (so live
//  "RaspberryBush(Clone)" stops matching authored "RaspberryBush"), these fail.
//  Mirrors the CompassNorthGateTests / LensHandoffDecisionTests precedent.
// ============================================================================

using System.Collections.Generic;
using SBPR.Trailborne.Features.SeersStone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public class SeersStoneEligibilityTests
    {
        private static SeersStoneEligibility Set(params string[] names)
            => new SeersStoneEligibility(names);

        // ── IGNORE-UNLISTED (the headline policy) ────────────────────────────────
        [Fact]
        public void Listed_prefab_is_eligible()
        {
            var e = Set("RaspberryBush", "Pickable_Thistle");
            Assert.True(e.IsEligible("RaspberryBush"));
            Assert.True(e.IsEligible("Pickable_Thistle"));
        }

        [Fact]
        public void Unlisted_prefab_is_NOT_eligible()
        {
            var e = Set("RaspberryBush");
            Assert.False(e.IsEligible("Pickable_Stone"));   // litter — never listed
            Assert.False(e.IsEligible("SomeModdedBerry"));  // modded — owner must opt in
        }

        [Fact]
        public void Empty_set_reveals_nothing_the_failsafe()
        {
            var e = new SeersStoneEligibility(null);
            Assert.Equal(0, e.Count);
            Assert.False(e.IsEligible("RaspberryBush"));
            Assert.False(e.IsEligible("anything"));
        }

        // ── (Clone)-suffix normalization (the runtime-name trap) ─────────────────
        [Theory]
        [InlineData("RaspberryBush(Clone)")]
        [InlineData("RaspberryBush (Clone)")]   // space-separated spelling
        [InlineData("  RaspberryBush(Clone)  ")] // leading/trailing whitespace
        public void Clone_suffix_is_stripped_so_runtime_instances_match(string runtimeName)
        {
            var e = Set("RaspberryBush");        // authored as the BASE name
            Assert.True(e.IsEligible(runtimeName));
        }

        [Fact]
        public void Authoring_a_clone_name_still_matches_the_base_one_normalization()
        {
            // Even if an owner mistakenly writes "Foo(Clone)" in the file, it normalizes to
            // the same key as the runtime "Foo(Clone)" hit — forgiving in both directions.
            var e = Set("RaspberryBush(Clone)");
            Assert.True(e.IsEligible("RaspberryBush"));
            Assert.True(e.IsEligible("RaspberryBush(Clone)"));
        }

        // ── case / whitespace insensitivity ──────────────────────────────────────
        [Fact]
        public void Matching_is_case_and_whitespace_insensitive()
        {
            var e = Set("RaspberryBush");
            Assert.True(e.IsEligible("raspberrybush"));
            Assert.True(e.IsEligible("  RASPBERRYBUSH  "));
        }

        // ── null / empty hit names ───────────────────────────────────────────────
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("(Clone)")]   // degenerates to empty after strip
        public void Null_or_empty_hit_is_not_eligible(string? hit)
        {
            var e = Set("RaspberryBush");
            Assert.False(e.IsEligible(hit));
        }

        // ── Count + dedup ────────────────────────────────────────────────────────
        [Fact]
        public void Duplicate_and_clone_variants_collapse_in_count()
        {
            var e = Set("RaspberryBush", "RaspberryBush(Clone)", "raspberrybush", "BlueberryBush");
            Assert.Equal(2, e.Count); // raspberry (3 spellings) + blueberry
        }

        // ── Normalize() is the shared contract ───────────────────────────────────
        [Theory]
        [InlineData("RaspberryBush(Clone)", "raspberrybush")]
        [InlineData("Foo (Clone)", "foo")]
        [InlineData("  Bar  ", "bar")]
        [InlineData("MineRock_Copper", "minerock_copper")]
        [InlineData(null, "")]
        [InlineData("(Clone)", "")]
        public void Normalize_matches_the_runtime_contract(string? input, string expected)
        {
            Assert.Equal(expected, SeersStoneEligibility.Normalize(input));
        }

        // ── A numeric editor-dup suffix is NOT stripped (distinct prefabs) ────────
        [Fact]
        public void Numeric_editor_duplicate_suffix_is_preserved_not_collapsed()
        {
            // " (1)" is an editor-only artifact, never a runtime name; stripping it would
            // wrongly merge distinct prefabs. We only strip (Clone).
            var e = Set("Foo");
            Assert.False(e.IsEligible("Foo (1)"));
        }
    }
}
