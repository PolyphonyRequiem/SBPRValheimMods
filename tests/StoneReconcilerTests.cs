using System.Collections.Generic;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Domain;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    /// <summary>
    /// R5 (t_2a8a8aaa) — pre-ratification Stone reconciliation. Covers the assumption-audit items: full
    /// stable ZDOID(UserID,ID) keying (never a truncated numeric), and deterministic handling of unkeyed,
    /// stale/mismatched, unselected, and duplicate Stones.
    /// </summary>
    public sealed class StoneReconcilerTests
    {
        private const string World = "uid:-898655635";
        private const string Selector = "niflheim-homestead-playtest-v1";

        private static HomesteadAssignmentMetadata Meta(string prefab, int zx, int zz) =>
            new HomesteadAssignmentMetadata(World, Selector, prefab, zx, zz);

        // Default provenance for a static-geometry Stone: assignment + a fixed provider version + content hash.
        // Facts and expected placements built from the same (prefab,zone) match on the FULL provenance.
        private static HomesteadStoneProvenance Prov(string prefab, int zx, int zz,
            string providerVersion = "catalog-digest-A", string content = "geo-hash-A", long generation = 0,
            HomesteadSeatProvider provider = HomesteadSeatProvider.StaticGeometry,
            string selector = Selector, int schema = HomesteadProvenanceCodec.SchemaVersion) =>
            new HomesteadStoneProvenance(schema,
                new HomesteadAssignmentMetadata(World, selector, prefab, zx, zz),
                provider, providerVersion, content, generation);

        private static IReadOnlyDictionary<string, HomesteadExpectedPlacement> Selected(params (string prefab, int zx, int zz)[] items) =>
            items.ToDictionary(i => StoneReconciler.ZoneKey(i.zx, i.zz),
                i => new HomesteadExpectedPlacement(Prov(i.prefab, i.zx, i.zz)));

        private static StoneReconcileFact Fact(long userId, uint id, bool keyed, string prefab, int zx, int zz) =>
            new StoneReconcileFact(new StableZdoId(userId, id), keyed, Prov(prefab, zx, zz));

        [Fact]
        public void Keeps_a_stone_that_matches_a_selected_assignment()
        {
            var plan = StoneReconciler.Reconcile(
                new[] { Fact(10, 1, true, "WoodHouse5", -25, -30) },
                Selected(("WoodHouse5", -25, -30)));

            Assert.Single(plan.Decisions);
            Assert.Equal(StoneReconcileAction.Keep, plan.Decisions[0].Action);
            Assert.Contains(StoneReconciler.ZoneKey(-25, -30), plan.SatisfiedZoneKeys);
        }

        [Fact]
        public void Reaps_an_unkeyed_stone()
        {
            var plan = StoneReconciler.Reconcile(
                new[] { Fact(10, 1, keyed: false, "WoodHouse5", -25, -30) },
                Selected(("WoodHouse5", -25, -30)));

            Assert.Equal(StoneReconcileAction.Destroy, plan.Decisions[0].Action);
            Assert.Equal(StoneReconcileReason.Unkeyed, plan.Decisions[0].Reason);
        }

        [Fact]
        public void Reaps_a_stone_in_an_unselected_zone()
        {
            var plan = StoneReconciler.Reconcile(
                new[] { Fact(10, 1, true, "WoodHouse5", 99, 99) },
                Selected(("WoodHouse5", -25, -30)));

            Assert.Equal(StoneReconcileReason.Unselected, plan.Decisions[0].Reason);
        }

        [Fact]
        public void Reaps_a_stone_whose_metadata_drifted_for_the_same_zone()
        {
            // Same zone as a selected assignment, but the resident carries a stale selector version.
            var stale = new StoneReconcileFact(new StableZdoId(10, 1), true,
                Prov("WoodHouse5", -25, -30, selector: "niflheim-homestead-playtest-OLD"));
            var plan = StoneReconciler.Reconcile(new[] { stale }, Selected(("WoodHouse5", -25, -30)));

            Assert.Equal(StoneReconcileReason.Mismatched, plan.Decisions[0].Reason);
        }

        [Fact]
        public void Reaps_a_stone_whose_content_provenance_is_stale_and_flags_it_for_recovery()
        {
            // R7 (Blocker 1): zone + assignment still match, but the catalog digest / content hash advanced
            // (a content upgrade). The stale Stone must be reaped AND its zone flagged for ledger recovery so a
            // sticky Created entry cannot block re-creation with current provenance.
            var stale = new StoneReconcileFact(new StableZdoId(10, 1), true,
                Prov("WoodHouse5", -25, -30, providerVersion: "catalog-digest-OLD", content: "geo-hash-OLD"));
            var plan = StoneReconciler.Reconcile(new[] { stale }, Selected(("WoodHouse5", -25, -30)));

            Assert.Equal(StoneReconcileReason.ProvenanceStale, plan.Decisions[0].Reason);
            Assert.Contains(StoneReconciler.ZoneKey(-25, -30), plan.RecoveryZoneKeys);
            Assert.DoesNotContain(StoneReconciler.ZoneKey(-25, -30), plan.SatisfiedZoneKeys);
        }

        [Fact]
        public void Reaps_a_stone_whose_manifest_generation_is_stale()
        {
            // A generator Stone stamped under manifest generation 3 is stale once the expected placement is
            // generation 5 (a newer manifest). Full-provenance comparison catches the generation drift.
            var stale = new StoneReconcileFact(new StableZdoId(10, 1), true,
                Prov("WoodVillage1", 4, 4, provider: HomesteadSeatProvider.Manifest,
                     providerVersion: "op-v1", content: "doc-digest", generation: 3));
            var expected = new Dictionary<string, HomesteadExpectedPlacement>
            {
                [StoneReconciler.ZoneKey(4, 4)] = new HomesteadExpectedPlacement(
                    Prov("WoodVillage1", 4, 4, provider: HomesteadSeatProvider.Manifest,
                         providerVersion: "op-v1", content: "doc-digest", generation: 5)),
            };
            var plan = StoneReconciler.Reconcile(new[] { stale }, expected);

            Assert.Equal(StoneReconcileReason.ProvenanceStale, plan.Decisions[0].Reason);
            Assert.Contains(StoneReconciler.ZoneKey(4, 4), plan.RecoveryZoneKeys);
        }

        [Fact]
        public void A_kept_current_stone_does_not_flag_recovery_even_with_a_stale_duplicate()
        {
            // One current Stone (kept) and one stale-provenance Stone in the SAME zone. The zone is satisfied by
            // the current one, so it must NOT be flagged for recovery (a valid Stone already exists there).
            var current = new StoneReconcileFact(new StableZdoId(10, 1), true, Prov("WoodHouse5", -25, -30));
            var stale = new StoneReconcileFact(new StableZdoId(10, 2), true,
                Prov("WoodHouse5", -25, -30, content: "geo-hash-OLD"));
            var plan = StoneReconciler.Reconcile(new[] { current, stale }, Selected(("WoodHouse5", -25, -30)));

            Assert.Contains(StoneReconciler.ZoneKey(-25, -30), plan.SatisfiedZoneKeys);
            Assert.DoesNotContain(StoneReconciler.ZoneKey(-25, -30), plan.RecoveryZoneKeys);
            Assert.Equal(1, plan.Decisions.Count(d => d.Action == StoneReconcileAction.Keep));
        }

        [Fact]
        public void Keeps_exactly_one_of_duplicate_stones_and_reaps_the_rest_deterministically()
        {
            // Two Stones for the same selected zone with DIFFERENT full ZDOIDs. The lower ZDOID
            // (UserId, then Id) is kept; the other is reaped as a duplicate — stable across enumeration order.
            var a = Fact(10, 5, true, "WoodHouse5", -25, -30);
            var b = Fact(10, 2, true, "WoodHouse5", -25, -30);

            var forward = StoneReconciler.Reconcile(new[] { a, b }, Selected(("WoodHouse5", -25, -30)));
            var reverse = StoneReconciler.Reconcile(new[] { b, a }, Selected(("WoodHouse5", -25, -30)));

            // Whichever input order, the kept ZDO is (10,2) and the duplicate is (10,5).
            var keptForward = forward.Decisions.Single(d => d.Action == StoneReconcileAction.Keep);
            var keptReverse = reverse.Decisions.Single(d => d.Action == StoneReconcileAction.Keep);
            Assert.Equal(new StableZdoId(10, 2), keptForward.ZdoId);
            Assert.Equal(new StableZdoId(10, 2), keptReverse.ZdoId);
            Assert.Equal(StoneReconcileReason.Duplicate,
                forward.Decisions.Single(d => d.ZdoId.Equals(new StableZdoId(10, 5))).Reason);
        }

        [Fact]
        public void Full_zdoid_distinguishes_stones_that_share_a_numeric_id_across_userids()
        {
            // Two DISTINCT Stones for DIFFERENT selected zones that share the same numeric ID=7 but differ in
            // UserID. A truncated numeric key would collapse them; the full ZDOID must keep BOTH.
            var a = Fact(100, 7, true, "WoodHouse5", -25, -30);
            var b = Fact(200, 7, true, "WoodHouse6", 10, 11);

            var plan = StoneReconciler.Reconcile(
                new[] { a, b },
                Selected(("WoodHouse5", -25, -30), ("WoodHouse6", 10, 11)));

            Assert.Equal(2, plan.Decisions.Count(d => d.Action == StoneReconcileAction.Keep));
            Assert.Equal(2, plan.SatisfiedZoneKeys.Count);
        }

        [Fact]
        public void Stable_zdoid_value_is_the_full_userid_id_pair()
        {
            Assert.Equal("100:7", new StableZdoId(100, 7).Value);
            Assert.NotEqual(new StableZdoId(100, 7), new StableZdoId(200, 7));
            Assert.NotEqual(new StableZdoId(100, 7), new StableZdoId(100, 8));
        }
    }
}
