// ============================================================================
//  Seer's Stone — PICKABLE CLUSTERING tests (engine-free, net8.0/xUnit, CI-gated)
// ----------------------------------------------------------------------------
//  Gates the spawn-time abundance aggregate (Daniel 2026-06-25, seers-stone.md:38):
//  "the wisp IS the spawn-time group aggregate" → ONE wisp per cluster, not one per
//  Pickable. Regression target: the original bug keyed a wisp per Pickable instance,
//  so a berry patch of N bushes spawned N wisps. These assert: one cluster per
//  contiguous same-prefab patch, per-prefab separation, aggregate bounds covering the
//  whole patch, a stable scan-order-independent key, and the singleton == legacy 2 m.
// ============================================================================

using System.Collections.Generic;
using SBPR.Trailborne.Features.SeersStone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public class SeersStonePickableClusteringTests
    {
        // Helper: a candidate at (x, z) on flat ground (y=0) with an explicit instance id.
        private static PickableCandidate C(string prefab, float x, float z, int id, string? friendly = null)
            => new PickableCandidate(prefab, new Vec3(x, 0f, z), id, friendly ?? prefab);

        // ── One contiguous same-prefab patch → exactly ONE cluster (the core bug) ─────────
        [Fact]
        public void Berry_patch_of_N_bushes_yields_one_cluster()
        {
            // Five RaspberryBush pickables packed within ~3 m — a "cluster" as Daniel reported it.
            var patch = new List<PickableCandidate>
            {
                C("RaspberryBush", 0f, 0f, 101),
                C("RaspberryBush", 1f, 0f, 102),
                C("RaspberryBush", 2f, 1f, 103),
                C("RaspberryBush", 1f, 2f, 104),
                C("RaspberryBush", 0f, 1f, 105),
            };

            var clusters = PickableClustering.Cluster(patch);

            Assert.Single(clusters);
            Assert.Equal(5, clusters[0].Count);
            Assert.Equal("RaspberryBush", clusters[0].Prefab);
        }

        // ── The aggregate bounds covers the WHOLE patch, not one bush's 2 m ──────────────
        [Fact]
        public void Cluster_bounds_radius_covers_the_whole_patch()
        {
            // A patch spanning ~10 m across (well beyond a single bush's 2 m foliage radius).
            var patch = new List<PickableCandidate>
            {
                C("BlueberryBush", -5f, 0f, 1),
                C("BlueberryBush", 0f, 0f, 2),
                C("BlueberryBush", 5f, 0f, 3),
            };

            var c = Assert.Single(PickableClustering.Cluster(patch));

            // Centroid is the mean (0,0); farthest member is 5 m out; bounds = 5 + single-bush 2 = 7.
            Assert.Equal(0f, c.Centroid.X, 3);
            Assert.Equal(0f, c.Centroid.Z, 3);
            Assert.Equal(5f + PickableClustering.SingleBushRadius, c.BoundsRadius, 3);
            // Sanity: the aggregate radius is materially bigger than a single bush — the bug's tell.
            Assert.True(c.BoundsRadius > PickableClustering.SingleBushRadius,
                "a real patch must orbit wider than a single 2 m bush");
        }

        // ── A lone bush behaves EXACTLY like the pre-cluster hardcoded 2.0 m (no regression) ─
        [Fact]
        public void Singleton_cluster_keeps_the_legacy_single_bush_radius()
        {
            var c = Assert.Single(PickableClustering.Cluster(new[] { C("Raspberry", 12f, -7f, 42) }));

            Assert.Equal(1, c.Count);
            Assert.Equal(2.0f, c.BoundsRadius, 3);                 // == the old hardcoded boundsRadius: 2.0f
            Assert.Equal(2.0f, PickableClustering.SingleBushRadius, 3);
            Assert.Equal(12f, c.Centroid.X, 3);
            Assert.Equal(-7f, c.Centroid.Z, 3);
            Assert.Equal(42, c.Key);
        }

        // ── Per-prefab grouping: two different resource types in the same area stay separate ─
        [Fact]
        public void Different_prefabs_in_the_same_area_do_not_merge()
        {
            // Raspberries and mushrooms interleaved within a couple metres — same place, different kinds.
            var mixed = new List<PickableCandidate>
            {
                C("RaspberryBush", 0f, 0f, 1),
                C("Pickable_Mushroom", 0.5f, 0.5f, 2),
                C("RaspberryBush", 1f, 0f, 3),
                C("Pickable_Mushroom", 1.5f, 0.5f, 4),
            };

            var clusters = PickableClustering.Cluster(mixed);

            Assert.Equal(2, clusters.Count);
            var rasp = Assert.Single(clusters, c => c.Prefab == "RaspberryBush");
            var mush = Assert.Single(clusters, c => c.Prefab == "Pickable_Mushroom");
            Assert.Equal(2, rasp.Count);
            Assert.Equal(2, mush.Count);
        }

        // ── Two same-prefab patches FARTHER than R apart stay two clusters ────────────────
        [Fact]
        public void Two_distant_same_prefab_patches_are_two_clusters()
        {
            // Two raspberry patches ~50 m apart — way beyond the 15 m linkage radius.
            var twoPatches = new List<PickableCandidate>
            {
                C("RaspberryBush", 0f, 0f, 1),
                C("RaspberryBush", 1f, 1f, 2),
                C("RaspberryBush", 50f, 0f, 3),
                C("RaspberryBush", 51f, 1f, 4),
            };

            var clusters = PickableClustering.Cluster(twoPatches);

            Assert.Equal(2, clusters.Count);
            Assert.All(clusters, c => Assert.Equal(2, c.Count));
        }

        // ── Single-linkage is TRANSITIVE: a chain where endpoints are > R apart is one cluster ─
        [Fact]
        public void Transitive_chain_within_R_is_one_cluster()
        {
            // Each bush is 10 m from the next (< 15 m R), so they single-link into one patch even
            // though the two ends are 30 m apart (> R). A contiguous hedge is one cluster.
            var chain = new List<PickableCandidate>
            {
                C("RaspberryBush", 0f, 0f, 1),
                C("RaspberryBush", 10f, 0f, 2),
                C("RaspberryBush", 20f, 0f, 3),
                C("RaspberryBush", 30f, 0f, 4),
            };

            var c = Assert.Single(PickableClustering.Cluster(chain));
            Assert.Equal(4, c.Count);
        }

        // ── Right at the radius boundary: <= R links, just beyond does not ───────────────
        [Fact]
        public void Linkage_is_inclusive_at_exactly_R_and_excludes_just_beyond()
        {
            // Exactly 15 m apart → one cluster (boundary inclusive).
            var atR = PickableClustering.Cluster(new[]
            {
                C("X", 0f, 0f, 1),
                C("X", 15f, 0f, 2),
            }, radius: 15f);
            Assert.Single(atR);

            // 15.01 m apart → two clusters (just beyond R).
            var beyondR = PickableClustering.Cluster(new[]
            {
                C("X", 0f, 0f, 1),
                C("X", 15.01f, 0f, 2),
            }, radius: 15f);
            Assert.Equal(2, beyondR.Count);
        }

        // ── The reconcile key is the MIN instance id and is scan-order-independent ───────
        [Fact]
        public void Cluster_key_is_min_instance_id_independent_of_order()
        {
            var a = new[] { C("Bush", 0f, 0f, 900), C("Bush", 1f, 0f, 300), C("Bush", 2f, 0f, 600) };
            var b = new[] { C("Bush", 2f, 0f, 600), C("Bush", 0f, 0f, 900), C("Bush", 1f, 0f, 300) };

            var ca = Assert.Single(PickableClustering.Cluster(a));
            var cb = Assert.Single(PickableClustering.Cluster(b));

            Assert.Equal(300, ca.Key);          // min id wins
            Assert.Equal(ca.Key, cb.Key);       // and is stable across input ordering
        }

        // ── The representative (min-id) member donates the wisp's friendly label ─────────
        [Fact]
        public void Friendly_name_comes_from_the_min_id_member()
        {
            var patch = new[]
            {
                C("RaspberryBush", 0f, 0f, 50, friendly: "Raspberries"),
                C("RaspberryBush", 1f, 0f, 20, friendly: "Raspberries"),
            };
            var c = Assert.Single(PickableClustering.Cluster(patch));
            Assert.Equal(20, c.Key);
            Assert.Equal("Raspberries", c.FriendlyName);
        }

        // ── Centroid is the arithmetic mean (carries Y too) ──────────────────────────────
        [Fact]
        public void Centroid_is_the_mean_position()
        {
            var patch = new[]
            {
                new PickableCandidate("Bush", new Vec3(0f, 2f, 0f), 1, "Bush"),
                new PickableCandidate("Bush", new Vec3(4f, 6f, 0f), 2, "Bush"),
                new PickableCandidate("Bush", new Vec3(2f, 4f, 6f), 3, "Bush"),
            };
            var c = Assert.Single(PickableClustering.Cluster(patch));
            Assert.Equal(2f, c.Centroid.X, 3);   // (0+4+2)/3
            Assert.Equal(4f, c.Centroid.Y, 3);   // (2+6+4)/3 — Y carried into the axis
            Assert.Equal(2f, c.Centroid.Z, 3);   // (0+0+6)/3
        }

        // ── Empty / null input is safe (no throw, empty result) ──────────────────────────
        [Fact]
        public void Empty_and_null_inputs_return_no_clusters()
        {
            Assert.Empty(PickableClustering.Cluster(new List<PickableCandidate>()));
            Assert.Empty(PickableClustering.Cluster(null!));
        }

        // ── Default radius is the pin merge radius (one R == abundance-radius, locked) ────
        [Fact]
        public void Default_linkage_radius_equals_the_pin_merge_radius()
        {
            // Two bushes 14 m apart (< 15 m default) merge with the DEFAULT radius — proving the
            // default IS the 15 m pin merge radius, not some other value.
            var clusters = PickableClustering.Cluster(new[]
            {
                C("Bush", 0f, 0f, 1),
                C("Bush", 14f, 0f, 2),
            });
            Assert.Single(clusters);
            Assert.Equal(15f, SeersStonePinDecision.DefaultMergeRadius, 3); // the locked "one R"
        }
    }
}
