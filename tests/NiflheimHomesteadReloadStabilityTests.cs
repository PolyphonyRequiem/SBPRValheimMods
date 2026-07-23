// ============================================================================
//  Niflheim 0003 — StoneAreaRegistrar reconciliation stability (unit layer).
// ----------------------------------------------------------------------------
//  SCOPE HONESTY (read first): this suite is a DETERMINISTIC UNIT TEST of the
//  shipped engine-free StoneAreaRegistrar.Reconcile / StoneAreaMembership /
//  StoneId types (link-compiled from ../src). It proves those pure functions
//  behave correctly when handed a resident Stone-fact set:
//    * membership rebuilt from the same facts is count- and identity-stable;
//    * a stale assignment fact outside the current selected set is UNREGISTERED
//      (removal, not union accumulation) on reconcile;
//    * a repeated reconcile with the same facts is a stable no-op.
//
//  WHAT THIS SUITE DOES NOT PROVE (the still-open live gate on t_1a1164f4):
//    * It does NOT run the production HomesteadSelector.Select. The literal
//      114-assignment set below is a FIXED, deterministic reconciler INPUT
//      fixture — a representative Stone-fact set — NOT verified production
//      selector output. There is no committed selector input (the selector's
//      `astley-real-locations.tsv`) or committed selector output in this repo
//      from which the 285/114 identity could be reproduced, so no "selector
//      reproduces the same set on cold reload" claim is asserted here.
//    * It does NOT exercise any Unity world `.db` save/load, ZoneSystem
//      Location generation, or `ZDOMan` serialization round-trip. No persistence
//      boundary is crossed. A cold Unity save+reload identity/count proof is the
//      one mandatory 0003 acceptance gate and it REMAINS OPEN on kanban card
//      t_1a1164f4 — deterministic unit tests do not substitute for it.
//
//  History: this file previously carried a same-literal-vs-same-literal
//  "Reload_reproduces_identical_perhost_zonecoord_set_no_reroll" assertion that
//  projected one literal into two sets and compared them — a tautology that
//  invoked no selector and proved nothing about reload. That assertion was
//  removed (PR correcting the merged #346 surface); the genuinely valuable
//  reconciliation, stale-reap, and idempotence tests are retained and renamed
//  to what they actually prove.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimHomesteadReloadStabilityTests
    {
        // A fixed world identity used only to key the reconciler-input StoneIds below. It is a stable
        // literal for these deterministic tests — NOT a claim that any live Astley world was reloaded.
        private static readonly WorldId FixtureWorld = new WorldId("uid:2413287143");
        private const int FixtureAssigned = 114;

        // A FIXED, deterministic reconciler-INPUT fixture: 114 representative (prefab, zoneX, zoneZ)
        // Stone-fact triples used purely to drive StoneAreaRegistrar.Reconcile below. This is NOT verified
        // production HomesteadSelector output — no committed selector input/output exists to reproduce it,
        // and this suite makes no selector-reproduction claim. It is exercised only as a stable resident
        // Stone-fact set for the reconciliation, stale-reap, and idempotence assertions.
        private static readonly (string Prefab, int ZoneX, int ZoneZ)[] FixtureFacts = new[]
        {
            ("WoodFarm1", 21, -21),
            ("WoodHouse1", -21, -13),
            ("WoodHouse10", 35, -64),
            ("WoodHouse11", 52, -13),
            ("WoodHouse12", -38, 56),
            ("WoodHouse13", -8, 19),
            ("WoodHouse2", 48, -9),
            ("WoodHouse3", -22, -32),
            ("WoodHouse4", 61, -31),
            ("WoodHouse5", -3, -1),
            ("WoodHouse6", 1, -6),
            ("WoodHouse7", -21, -16),
            ("WoodHouse8", -49, 46),
            ("WoodHouse9", -17, 5),
            ("WoodVillage1", 30, 45),
            ("WoodFarm1", -13, -19),
            ("WoodHouse1", -31, -26),
            ("WoodHouse10", -6, 23),
            ("WoodHouse11", 1, 8),
            ("WoodHouse12", 9, 1),
            ("WoodHouse13", 5, 12),
            ("WoodHouse2", -21, -9),
            ("WoodHouse3", -16, -14),
            ("WoodHouse4", 3, 12),
            ("WoodHouse5", -3, 6),
            ("WoodHouse6", 18, -40),
            ("WoodHouse7", -14, -15),
            ("WoodHouse8", 2, 22),
            ("WoodHouse9", 58, 21),
            ("WoodVillage1", 53, -51),
            ("WoodFarm1", -17, -19),
            ("WoodHouse1", 59, -34),
            ("WoodHouse10", -7, 2),
            ("WoodHouse11", 43, 1),
            ("WoodHouse12", 21, -15),
            ("WoodHouse13", 50, 41),
            ("WoodHouse2", -42, -44),
            ("WoodHouse3", -42, 8),
            ("WoodHouse4", -6, 5),
            ("WoodHouse5", -10, 52),
            ("WoodHouse6", 49, 34),
            ("WoodHouse7", 46, 2),
            ("WoodHouse8", -26, -5),
            ("WoodHouse9", 20, -61),
            ("WoodVillage1", 66, 12),
            ("WoodFarm1", 9, -4),
            ("WoodHouse1", 3, -25),
            ("WoodHouse10", 23, -19),
            ("WoodHouse11", 8, 26),
            ("WoodHouse12", 35, 25),
            ("WoodHouse13", 6, -28),
            ("WoodHouse2", -38, 52),
            ("WoodHouse3", -26, -3),
            ("WoodHouse4", -48, 17),
            ("WoodHouse5", 19, -18),
            ("WoodHouse6", -1, 4),
            ("WoodHouse7", -17, 36),
            ("WoodHouse8", -67, 34),
            ("WoodHouse9", -43, 13),
            ("WoodVillage1", -3, -40),
            ("WoodHouse1", 14, 50),
            ("WoodHouse10", 45, -6),
            ("WoodHouse11", -4, 3),
            ("WoodHouse12", 5, 15),
            ("WoodHouse13", -32, 29),
            ("WoodHouse2", -11, -51),
            ("WoodHouse3", 49, 37),
            ("WoodHouse4", 21, -65),
            ("WoodHouse5", 49, -4),
            ("WoodHouse6", -24, -29),
            ("WoodHouse7", 37, 8),
            ("WoodHouse8", -1, 55),
            ("WoodHouse9", -6, 56),
            ("WoodVillage1", 48, -6),
            ("WoodHouse1", 56, -52),
            ("WoodHouse10", -41, 13),
            ("WoodHouse11", 37, 12),
            ("WoodHouse12", 60, 10),
            ("WoodHouse13", -28, 34),
            ("WoodHouse2", -47, 48),
            ("WoodHouse3", -26, -15),
            ("WoodHouse4", 3, 10),
            ("WoodHouse5", -22, 69),
            ("WoodHouse6", 40, 8),
            ("WoodHouse7", 6, -23),
            ("WoodHouse8", 3, 1),
            ("WoodHouse9", -6, 44),
            ("WoodVillage1", 69, 4),
            ("WoodHouse1", 31, -34),
            ("WoodHouse10", -42, 6),
            ("WoodHouse11", -7, 18),
            ("WoodHouse12", 24, 34),
            ("WoodHouse13", -70, -10),
            ("WoodHouse2", -32, -71),
            ("WoodHouse3", 7, 11),
            ("WoodHouse4", -45, 49),
            ("WoodHouse5", -25, -17),
            ("WoodHouse6", -50, 49),
            ("WoodHouse7", -3, 17),
            ("WoodHouse8", -3, -6),
            ("WoodHouse9", 2, 6),
            ("WoodHouse1", 23, -64),
            ("WoodHouse10", -3, 15),
            ("WoodHouse11", -51, 13),
            ("WoodHouse12", -8, 53),
            ("WoodHouse13", 4, -21),
            ("WoodHouse2", -28, 0),
            ("WoodHouse3", 63, 14),
            ("WoodHouse4", -22, -11),
            ("WoodHouse5", -36, -58),
            ("WoodHouse6", 63, -34),
            ("WoodHouse7", -18, -10),
            ("WoodHouse8", -27, -11),
            ("WoodHouse9", -6, 15),
        };

        // A resident membership rebuilt from a set of Stone facts — exactly what
        // HomesteadStoneWorldPlacement.ReconcileStoneAreas builds each tick from resident Stone ZDOs.
        // One Area per fact, centered deterministically on that zone.
        private static StoneAreaMembership ResidentMembershipFor(
            IEnumerable<(string Prefab, int ZoneX, int ZoneZ)> facts)
        {
            var stoneFacts = facts.Select(a => new StoneAreaRegistrar.StoneAreaFact(
                StoneId.FromHostZone(FixtureWorld, a.ZoneX, a.ZoneZ),
                a.ZoneX * 64.0,   // arbitrary but deterministic world center per zone
                a.ZoneZ * 64.0,
                StoneAreaMembership.DefaultAreaRadius));
            var membership = new StoneAreaMembership();
            StoneAreaRegistrar.Reconcile(membership, stoneFacts.ToArray());
            return membership;
        }

        private static IReadOnlyCollection<StoneAreaRegistrar.StoneAreaFact> SelectedFacts(
            IEnumerable<(string Prefab, int ZoneX, int ZoneZ)> facts) =>
            facts.Select(a => new StoneAreaRegistrar.StoneAreaFact(
                StoneId.FromHostZone(FixtureWorld, a.ZoneX, a.ZoneZ),
                a.ZoneX * 64.0, a.ZoneZ * 64.0, StoneAreaMembership.DefaultAreaRadius)).ToArray();

        // ── Fixture integrity guard ─────────────────────────────────────────────

        [Fact]
        public void Fixture_facts_are_a_distinct_set_of_expected_size()
        {
            Assert.Equal(FixtureAssigned, FixtureFacts.Length);
            // Per-host zone coords are a SET — no duplicate (prefab,zone) facts.
            var distinct = FixtureFacts
                .Select(a => a.Prefab + ":" + a.ZoneX + ":" + a.ZoneZ)
                .Distinct(StringComparer.Ordinal)
                .Count();
            Assert.Equal(FixtureAssigned, distinct);
        }

        // ── Reconcile membership is count- and identity-stable for a fixed fact set ─

        [Fact]
        public void Reconcile_membership_is_count_stable_and_identity_stable()
        {
            var first = ResidentMembershipFor(FixtureFacts);
            Assert.Equal(FixtureAssigned, first.Count);

            // Rebuilding membership from the SAME facts yields the same StoneId set — deterministic.
            var second = ResidentMembershipFor(FixtureFacts);
            Assert.Equal(FixtureAssigned, second.Count);

            var firstIds = first.RegisteredStoneIds().ToHashSet(StringComparer.Ordinal);
            var secondIds = second.RegisteredStoneIds().ToHashSet(StringComparer.Ordinal);
            Assert.True(firstIds.SetEquals(secondIds));   // identity-stable StoneId set
        }

        // ── Stale-fact reconciliation: removal, not accumulation ────────────────

        [Fact]
        public void Stale_assignment_fact_outside_selected_set_is_removed_not_accumulated()
        {
            // Resident membership including one stale assignment fact at a zone NOT in the selected set —
            // e.g. a stale assignment ZDO left by an earlier selector pass. This mirrors the v7 production
            // run reaping the stale (-25,-30) Stone.
            var staleZone = (Prefab: "WoodHouse1", ZoneX: -25, ZoneZ: -30);
            Assert.DoesNotContain(
                FixtureFacts,
                a => a.ZoneX == staleZone.ZoneX && a.ZoneZ == staleZone.ZoneZ);   // genuinely stale

            var resident = new List<(string, int, int)>(
                FixtureFacts.Select(a => (a.Prefab, a.ZoneX, a.ZoneZ)))
            {
                (staleZone.Prefab, staleZone.ZoneX, staleZone.ZoneZ),
            };
            var membership = ResidentMembershipFor(
                resident.Select(p => (p.Item1, p.Item2, p.Item3)));
            Assert.Equal(FixtureAssigned + 1, membership.Count);   // stale present pre-reconcile

            // Reconcile against the CURRENT selected set (the 114). The stale Area must be UNREGISTERED —
            // the set converges to exactly 114, never 115 (no union accumulation).
            var result = StoneAreaRegistrar.Reconcile(membership, SelectedFacts(FixtureFacts));

            Assert.Equal(1, result.Unregistered);
            Assert.Equal(0, result.Registered);   // every live one already resident
            Assert.Equal(0, result.Updated);       // centers unchanged
            Assert.Equal(FixtureAssigned, result.Total);
            Assert.Equal(FixtureAssigned, membership.Count);
            var staleId = StoneId.FromHostZone(FixtureWorld, staleZone.ZoneX, staleZone.ZoneZ);
            Assert.DoesNotContain(staleId.Value, membership.RegisteredStoneIds());
        }

        [Fact]
        public void Repeated_reconciliation_is_a_stable_noop()
        {
            // A reconcile with the same facts (no new stale, nothing dropped) must be a pure no-op —
            // stable, not oscillating. This is the idempotence guarantee the realization cadence relies on.
            var membership = ResidentMembershipFor(FixtureFacts);
            var again = StoneAreaRegistrar.Reconcile(membership, SelectedFacts(FixtureFacts));

            Assert.Equal(0, again.Registered);
            Assert.Equal(0, again.Updated);
            Assert.Equal(0, again.Unregistered);
            Assert.Equal(FixtureAssigned, again.Total);
        }
    }
}
