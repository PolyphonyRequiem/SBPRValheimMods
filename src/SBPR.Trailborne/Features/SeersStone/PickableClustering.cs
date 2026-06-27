// ============================================================================
//  Trailborne v4 (Mountains) — Seer's Stone PICKABLE CLUSTERING (engine-free)
// ----------------------------------------------------------------------------
//  Design : docs/design/seers-stone.md §2 (Locked decisions) — Daniel, 2026-06-25:
//    • Abundance: "The wisp IS the spawn-time group aggregate (no count shown), so
//      pinning the wisp pins the whole patch as one pin." (seers-stone.md:38)
//    • Radius: "One R, default 15 m (merge-radius == abundance-radius)." (:35)
//    • PARKED-2026-06-03.md:37 — "all Pickable ZDOs of the same prefab within R."
//
//  WHAT THIS FIXES. WispField.Rescan previously keyed one wisp per Pickable INSTANCE
//  (pickable.gameObject.GetInstanceID()), so a berry patch — N separate RaspberryBush
//  Pickables placed close together — spawned N wisps. The spec wants ONE wisp per
//  cluster, the spawn-time aggregate. The "spawn-time group aggregate" was never
//  implemented; this is that missing algorithm.
//
//  THE ALGORITHM (pure geometry → CI-gated headless).
//    Group eligible Pickables into clusters by (prefab-name × spatial proximity):
//    same-prefab Pickables within R of one another form one cluster — a single-linkage
//    connected component on the XZ plane (a contiguous patch). Two DIFFERENT prefabs
//    never merge (per-prefab grouping); two same-prefab patches farther than R apart
//    stay separate clusters. For each cluster we compute:
//      • Centroid  — the mean member position (the wisp's cylinder axis).
//      • BoundsRadius — max horizontal distance from centroid to any member, plus the
//        single-bush foliage radius, so the wisp orbit (BoundsRadius + margin) encloses
//        the WHOLE patch, not one bush. A singleton cluster reduces to BoundsRadius =
//        SingleBushRadius (2.0 m) — exactly the pre-cluster single-bush behaviour.
//      • Key — the MIN instance id among the cluster's members: a stable representative
//        so a re-scan that re-sees the same patch reuses its wisp instead of churning
//        it. Connected components are partition-unique (independent of scan order), so
//        the key is stable across rescans regardless of OverlapSphere ordering.
//
//  WHY R == the pin merge radius. Per spec "merge-radius == abundance-radius (one R)",
//  the cluster linkage radius defaults to SeersStonePinDecision.DefaultMergeRadius (15 m)
//  — the SAME constant, so the two stay locked together with zero duplication. Whatever
//  the patch the wisp aggregates, looking at any bush in it and pinning collapses to one
//  pin via that same 15 m merge at the pin site.
//
//  WHY ENGINE-FREE. The grouping is pure: (prefab, XZ position, instance id)[] → clusters.
//  No UnityEngine, no Physics — WispField does the OverlapSphere and feeds candidates in.
//  Keeping it free of engine types lets tests/SeersStonePickableClusteringTests.cs gate
//  the invariants headless (one wisp per patch, per-prefab separation, aggregate bounds,
//  singleton == legacy 2 m) — the WispMotion / SeersStonePinDecision link-compile precedent.
//
//  Clean-side (ADR-0001): SBPR-authored; references no vanilla or third-party type.
//  Uses the local Vec3 (from WispMotion.cs) so it compiles under net8.0 in the test
//  project AND net48 in the mod; the engine wrapper converts at the boundary.
// ============================================================================

using System.Collections.Generic;

namespace SBPR.Trailborne.Features.SeersStone
{
    /// <summary>
    /// One eligible Pickable, reduced to what clustering needs. The WispField OverlapSphere
    /// resolves each collider to a Pickable and adapts it to this (prefab name, world pos,
    /// the GameObject instance id, the friendly/hover name) — then hands the list to
    /// <see cref="PickableClustering.Cluster"/>.
    /// </summary>
    public readonly struct PickableCandidate
    {
        /// <summary>Clone-stripped prefab name — the grouping key's name axis (only same-prefab merge).</summary>
        public readonly string Prefab;
        /// <summary>World position; XZ drives proximity, Y is carried into the centroid.</summary>
        public readonly Vec3 Pos;
        /// <summary>The source Pickable GameObject's instance id — the stable representative-key source.</summary>
        public readonly int InstanceId;
        /// <summary>The source's friendly/hover name (the representative member's becomes the wisp label).</summary>
        public readonly string FriendlyName;

        public PickableCandidate(string prefab, Vec3 pos, int instanceId, string friendlyName)
        {
            Prefab = prefab;
            Pos = pos;
            InstanceId = instanceId;
            FriendlyName = friendlyName;
        }
    }

    /// <summary>
    /// One spawn-time cluster: the aggregate the wisp represents. <see cref="Key"/> is the stable
    /// reconcile key (min member instance id), <see cref="Centroid"/> the cylinder axis,
    /// <see cref="BoundsRadius"/> the aggregate radius enclosing the whole patch.
    /// </summary>
    public readonly struct PickableCluster
    {
        /// <summary>Stable reconcile key — the MIN instance id among members (scan-order-independent).</summary>
        public readonly int Key;
        /// <summary>The shared prefab name of every member (per-prefab grouping).</summary>
        public readonly string Prefab;
        /// <summary>Mean member position — the wisp's cylinder axis (centroid).</summary>
        public readonly Vec3 Centroid;
        /// <summary>Aggregate radius: max horizontal centroid→member distance + single-bush foliage radius.</summary>
        public readonly float BoundsRadius;
        /// <summary>The representative member's friendly name (the min-id member) — the wisp's label.</summary>
        public readonly string FriendlyName;
        /// <summary>Member count (diagnostics / tests; NO count is ever shown to the player — Daniel).</summary>
        public readonly int Count;

        public PickableCluster(int key, string prefab, Vec3 centroid, float boundsRadius,
                               string friendlyName, int count)
        {
            Key = key;
            Prefab = prefab;
            Centroid = centroid;
            BoundsRadius = boundsRadius;
            FriendlyName = friendlyName;
            Count = count;
        }
    }

    /// <summary>
    /// Pure spawn-time Pickable clustering: candidates → one cluster per contiguous same-prefab patch.
    /// Engine-free so the abundance-aggregate rules are CI-gated. WispField calls <see cref="Cluster"/>
    /// once per scan and spawns one wisp per returned cluster, keyed by <see cref="PickableCluster.Key"/>.
    /// </summary>
    public static class PickableClustering
    {
        /// <summary>
        /// Single-bush foliage radius (metres) — the pre-cluster hardcoded bounds. A singleton cluster
        /// keeps exactly this, so one lone bush still orbits at the render-verified 2.75 m (2.0 + 0.75 margin).
        /// </summary>
        public const float SingleBushRadius = 2.0f;

        /// <summary>
        /// Group eligible Pickables into clusters. Same-prefab candidates within <paramref name="radius"/>
        /// of one another (XZ single-linkage) form one cluster; different prefabs never merge. The default
        /// radius is the pin merge radius (one R == abundance-radius, per spec) — the SAME constant, so the
        /// two stay locked together.
        /// </summary>
        /// <param name="candidates">Eligible Pickables (caller dedups multi-collider repeats by instance id).</param>
        /// <param name="radius">Linkage radius, metres. Defaults to the pin merge radius (15 m, "one R").</param>
        /// <returns>One <see cref="PickableCluster"/> per contiguous same-prefab patch. Never null.</returns>
        public static List<PickableCluster> Cluster(
            IReadOnlyList<PickableCandidate> candidates,
            float radius = SeersStonePinDecision.DefaultMergeRadius)
        {
            var clusters = new List<PickableCluster>();
            if (candidates == null || candidates.Count == 0) return clusters;

            float r2 = radius * radius;

            // Partition the candidate indices by prefab name (only same-prefab pickables can merge).
            var byPrefab = new Dictionary<string, List<int>>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var prefab = candidates[i].Prefab;
                if (string.IsNullOrEmpty(prefab)) continue; // eligibility upstream should prevent this; skip defensively
                if (!byPrefab.TryGetValue(prefab, out var bucket))
                {
                    bucket = new List<int>();
                    byPrefab[prefab] = bucket;
                }
                bucket.Add(i);
            }

            // Within each prefab bucket, flood single-linkage connected components on the XZ plane.
            foreach (var kv in byPrefab)
            {
                var members = kv.Value;
                var assigned = new bool[members.Count];

                for (int s = 0; s < members.Count; s++)
                {
                    if (assigned[s]) continue;

                    // BFS flood from member s: pull in every still-unassigned same-prefab member whose
                    // XZ distance to ANY already-collected member is <= radius (transitive single-linkage).
                    // A List worklist with a read cursor stands in for a queue (net48 forwards Queue<> to a
                    // separate assembly the mod doesn't reference; List is always available here).
                    var component = new List<int>();   // indices into `members`; also the BFS worklist
                    assigned[s] = true;
                    component.Add(s);
                    for (int head = 0; head < component.Count; head++)
                    {
                        int cur = component[head];
                        var curPos = candidates[members[cur]].Pos;
                        for (int t = 0; t < members.Count; t++)
                        {
                            if (assigned[t]) continue;
                            var tp = candidates[members[t]].Pos;
                            float dx = curPos.X - tp.X, dz = curPos.Z - tp.Z;
                            if (dx * dx + dz * dz <= r2)
                            {
                                assigned[t] = true;
                                component.Add(t);
                            }
                        }
                    }

                    clusters.Add(BuildCluster(candidates, members, component));
                }
            }

            return clusters;
        }

        /// <summary>
        /// Reduce one connected component (indices into <paramref name="members"/>) to a cluster: mean
        /// centroid, min-instance-id key + that member's friendly name, and the aggregate bounds radius.
        /// </summary>
        private static PickableCluster BuildCluster(
            IReadOnlyList<PickableCandidate> candidates, List<int> members, List<int> component)
        {
            int n = component.Count;

            // Mean centroid (carry Y so the wisp's base height is the patch's, not one bush's).
            float sx = 0f, sy = 0f, sz = 0f;
            for (int i = 0; i < n; i++)
            {
                var p = candidates[members[component[i]]].Pos;
                sx += p.X; sy += p.Y; sz += p.Z;
            }
            var centroid = new Vec3(sx / n, sy / n, sz / n);

            // Stable key = min instance id; that member also donates the friendly name + prefab.
            int repIdx = component[0];
            int minId = candidates[members[repIdx]].InstanceId;
            for (int i = 1; i < n; i++)
            {
                int id = candidates[members[component[i]]].InstanceId;
                if (id < minId)
                {
                    minId = id;
                    repIdx = component[i];
                }
            }
            var rep = candidates[members[repIdx]];

            // Aggregate radius: farthest member from the centroid (XZ), plus the single-bush foliage radius
            // so the orbit (BoundsRadius + margin) clears the whole patch. Singleton ⇒ maxDist 0 ⇒ 2.0 m.
            float maxDist = 0f;
            for (int i = 0; i < n; i++)
            {
                var p = candidates[members[component[i]]].Pos;
                float dx = p.X - centroid.X, dz = p.Z - centroid.Z;
                float d = (float)System.Math.Sqrt(dx * dx + dz * dz);
                if (d > maxDist) maxDist = d;
            }
            float boundsRadius = maxDist + SingleBushRadius;

            return new PickableCluster(minId, rep.Prefab, centroid, boundsRadius, rep.FriendlyName, n);
        }
    }
}
