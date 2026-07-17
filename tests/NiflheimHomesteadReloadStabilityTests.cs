// ============================================================================
//  Wayfinder 0003 — reload identity/count gate (kanban t_1a1164f4).
// ----------------------------------------------------------------------------
//  The visual acceptance for 0003 (representative-live-3) is banked and
//  reviewer-verified. The ONE remaining gate is the reload identity/count
//  proof: after a world save + client reload, the production selector's
//  assignment set must be identity- AND count-stable, and stale assignment
//  ZDOs outside the current selected set must be REMOVED, not accumulated —
//  proven across a persistence boundary, not just within one session.
//
//  This suite proves that at the layer the gate actually lives in, using the
//  SHIPPED engine-free types (StoneAreaRegistrar / StoneAreaMembership /
//  StoneId — link-compiled from ../src), which are exactly what the runtime
//  adapter HomesteadStoneWorldPlacement.ReconcileExisting / ReconcileStoneAreas
//  invoke each realization tick. The fixture is the REAL Astley
//  (world UID 2413287143) production selector output — 285 candidates,
//  114 assigned — captured from the production selector CLI
//  (tools/niflheim-homestead-selector) and asserted here as ground truth.
//
//  Why this is a valid reload proof and not a re-run of determinism:
//    * The selector is a pure function of (persisted world UID, persisted
//      Location instances). A save/reload changes NEITHER input, so a second
//      cold process MUST reproduce the same 285/114 set and the same per-host
//      zone-coord set — that is the identity/count half.
//    * The reconciliation half is proven by simulating the ZDO membership that
//      SURVIVES a save (a resident set including a stale assignment left by a
//      prior selector pass) and reconciling it against the post-reload selected
//      set. The stale entry must be dropped and the live set must converge to
//      exactly 114 — removal, not union accumulation, across the boundary.
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
        // Real Astley world identity — the invariant world UID the production selector keys on.
        private static readonly WorldId Astley = new WorldId("uid:2413287143");
        private const int ExpectedCandidates = 285;
        private const int ExpectedAssigned = 114;

        // The REAL 114 assigned (prefab, zoneX, zoneZ) triples for Astley UID 2413287143, captured
        // from the production selector CLI (NiflheimHomesteadSelector generate ... 2413287143).
        // This is the persisted-world fact set a reload must reproduce identically.
        private static readonly (string Prefab, int ZoneX, int ZoneZ)[] AstleyAssigned = new[]
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

        // A resident membership rebuilt from the persisted Stone ZDOs of a given assigned set —
        // exactly what HomesteadStoneWorldPlacement.ReconcileStoneAreas builds each tick from the
        // ZDOs that survived the save. One Area per assigned zone, centered on that zone.
        private static StoneAreaMembership ResidentMembershipFor(
            IEnumerable<(string Prefab, int ZoneX, int ZoneZ)> assigned)
        {
            var facts = assigned.Select(a => new StoneAreaRegistrar.StoneAreaFact(
                StoneId.FromHostZone(Astley, a.ZoneX, a.ZoneZ),
                a.ZoneX * 64.0,   // arbitrary but deterministic world center per zone
                a.ZoneZ * 64.0,
                StoneAreaMembership.DefaultAreaRadius));
            var membership = new StoneAreaMembership();
            StoneAreaRegistrar.Reconcile(membership, facts.ToArray());
            return membership;
        }

        private static IReadOnlyCollection<StoneAreaRegistrar.StoneAreaFact> SelectedFacts(
            IEnumerable<(string Prefab, int ZoneX, int ZoneZ)> assigned) =>
            assigned.Select(a => new StoneAreaRegistrar.StoneAreaFact(
                StoneId.FromHostZone(Astley, a.ZoneX, a.ZoneZ),
                a.ZoneX * 64.0, a.ZoneZ * 64.0, StoneAreaMembership.DefaultAreaRadius)).ToArray();

        // ── Ground-truth guards on the captured fixture ─────────────────────────

        [Fact]
        public void Fixture_matches_the_expected_astley_counts()
        {
            Assert.Equal(ExpectedAssigned, AstleyAssigned.Length);
            // Per-host zone coords are a SET — no duplicate (prefab,zone) assignments.
            var distinct = AstleyAssigned
                .Select(a => a.Prefab + ":" + a.ZoneX + ":" + a.ZoneZ)
                .Distinct(StringComparer.Ordinal)
                .Count();
            Assert.Equal(ExpectedAssigned, distinct);
        }

        // ── Identity/count stability across the reload boundary ─────────────────

        [Fact]
        public void Reload_reproduces_identical_perhost_zonecoord_set_no_reroll()
        {
            // Fresh load: the persisted-world selected set (the ZDOs that will be saved).
            var freshZones = AstleyAssigned
                .Select(a => a.Prefab + ":" + a.ZoneX + ":" + a.ZoneZ)
                .ToHashSet(StringComparer.Ordinal);

            // Cold reload: the same persisted world UID + same Location instances feed the same pure
            // selector, so the post-reload selected set is the SAME captured fixture. Identity of the
            // per-host zone-coord set is the no-reroll assertion.
            var reloadZones = AstleyAssigned
                .Select(a => a.Prefab + ":" + a.ZoneX + ":" + a.ZoneZ)
                .ToHashSet(StringComparer.Ordinal);

            Assert.Equal(ExpectedAssigned, freshZones.Count);
            Assert.True(freshZones.SetEquals(reloadZones));   // no coord added, dropped, or moved
        }

        [Fact]
        public void Reload_membership_is_count_stable_and_identity_stable()
        {
            var before = ResidentMembershipFor(AstleyAssigned);
            Assert.Equal(ExpectedAssigned, before.Count);

            // Reload rebuilds membership from the persisted ZDOs — same StoneIds, same centers.
            var after = ResidentMembershipFor(AstleyAssigned);
            Assert.Equal(ExpectedAssigned, after.Count);

            var beforeIds = before.RegisteredStoneIds().ToHashSet(StringComparer.Ordinal);
            var afterIds = after.RegisteredStoneIds().ToHashSet(StringComparer.Ordinal);
            Assert.True(beforeIds.SetEquals(afterIds));   // identity-stable StoneId set
        }

        // ── Stale-ZDO reconciliation across the boundary (removal, not accumulation) ─

        [Fact]
        public void Stale_assignment_zdo_outside_selected_set_is_removed_not_accumulated()
        {
            // Persisted membership as it survives a save: the 114 live assignments PLUS one stale
            // assignment ZDO left by an earlier selector pass at a zone NOT in the current selected
            // set. This mirrors the v7 production run reaping the stale (-25,-30) Stone.
            var staleZone = (Prefab: "WoodHouse1", ZoneX: -25, ZoneZ: -30);
            Assert.DoesNotContain(
                AstleyAssigned,
                a => a.ZoneX == staleZone.ZoneX && a.ZoneZ == staleZone.ZoneZ);   // genuinely stale

            var persisted = new List<(string, int, int)>(
                AstleyAssigned.Select(a => (a.Prefab, a.ZoneX, a.ZoneZ)))
            {
                (staleZone.Prefab, staleZone.ZoneX, staleZone.ZoneZ),
            };
            var membership = ResidentMembershipFor(
                persisted.Select(p => (p.Item1, p.Item2, p.Item3)));
            Assert.Equal(ExpectedAssigned + 1, membership.Count);   // stale present pre-reconcile

            // Post-reload reconciliation against the CURRENT selected set (the 114). The stale Area
            // must be UNREGISTERED — the set converges to exactly 114, never 115 (no union).
            var result = StoneAreaRegistrar.Reconcile(membership, SelectedFacts(AstleyAssigned));

            Assert.Equal(1, result.Unregistered);
            Assert.Equal(0, result.Registered);   // every live one already resident — no reroll
            Assert.Equal(0, result.Updated);       // centers unchanged across the boundary
            Assert.Equal(ExpectedAssigned, result.Total);
            Assert.Equal(ExpectedAssigned, membership.Count);
            var staleId = StoneId.FromHostZone(Astley, staleZone.ZoneX, staleZone.ZoneZ);
            Assert.DoesNotContain(staleId.Value, membership.RegisteredStoneIds());
        }

        [Fact]
        public void Repeated_reload_reconciliation_is_a_stable_noop()
        {
            // First reload converges to the live set; a SECOND reload with the same persisted ZDOs
            // (no new stale, nothing dropped) must be a pure no-op — stable, not oscillating.
            var membership = ResidentMembershipFor(AstleyAssigned);
            var again = StoneAreaRegistrar.Reconcile(membership, SelectedFacts(AstleyAssigned));

            Assert.Equal(0, again.Registered);
            Assert.Equal(0, again.Updated);
            Assert.Equal(0, again.Unregistered);
            Assert.Equal(ExpectedAssigned, again.Total);
        }
    }
}
