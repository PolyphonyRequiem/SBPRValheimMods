using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.HomesteadStone
{
    /// <summary>
    /// net48 adapter: reads a host prefab's AUTHORED static collider components directly off its
    /// transform hierarchy and converts them to engine-free host-local <see cref="StaticColliderFootprint"/>s.
    ///
    /// This is the SPIKE-2 Approach-A seam. It reads each collider's SERIALIZED shape
    /// (<c>BoxCollider.size/center</c>, <c>Capsule/SphereCollider.radius/center</c>) and composes world
    /// positions with pure <c>Transform.TransformPoint</c> math — NOT <c>collider.bounds</c>, which is
    /// populated by the physics scene and is exactly what SPIKE 1 proved absent on a headless dedicated
    /// server. Reading serialized fields + transform math needs no physics scene, so it works headless on
    /// the prefab retrieved via <c>ZNetScene.GetPrefab</c> (which fires no Awake).
    ///
    /// Footprint convention mirrors the offline extractor (extract_homestead_geometry.py): host-local,
    /// axis-aligned XZ AABB per collider, de-rotated into the host root frame; per-node local rotation is
    /// folded conservatively into the AABB. Only enabled, non-trigger colliders are load-bearing.
    /// NO Physics.*, NO Heightmap.
    /// </summary>
    internal static class HomesteadHostGeometryProvider
    {
        /// <summary>Production footprint inflation, matching the offline extractor / probe pipeline.</summary>
        internal const float Inflate = 0.15f;

        /// <summary>Build host-local static footprints from a host prefab root (as returned by
        /// <c>ZNetScene.GetPrefab</c> or a live instance). Returns an empty geometry (ColliderCount == 0)
        /// when the root has no load-bearing colliders (e.g. a generator host), which the resolver treats as
        /// its explicit no-geometry branch. <paramref name="rootRotation"/> lets the caller pass the realized
        /// host yaw for a live instance; pass identity when reading an unrotated prefab template.</summary>
        internal static HomesteadHostGeometry FromHostRoot(string prefab, GameObject hostRoot, Quaternion rootRotation)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (hostRoot == null) throw new ArgumentNullException(nameof(hostRoot));

            var rootTransform = hostRoot.transform;
            var rootInverse = Quaternion.Inverse(rootRotation);
            var rootOrigin = rootTransform.position;
            var footprints = new List<StaticColliderFootprint>();

            foreach (var collider in hostRoot.GetComponentsInChildren<Collider>(includeInactive: false))
            {
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                if (!TryFootprint(collider, rootTransform, rootInverse, rootOrigin, out var footprint)) continue;
                footprints.Add(footprint);
            }

            return new HomesteadHostGeometry(prefab, footprints, HomesteadGeometryHash.Compute(footprints));
        }

        private static bool TryFootprint(
            Collider collider,
            Transform rootTransform,
            Quaternion rootInverse,
            Vector3 rootOrigin,
            out StaticColliderFootprint footprint)
        {
            footprint = default;
            var t = collider.transform;
            Vector3 localCenter;
            float sizeX, sizeZ;
            switch (collider)
            {
                case BoxCollider box:
                    localCenter = box.center;
                    sizeX = Mathf.Abs(box.size.x);
                    sizeZ = Mathf.Abs(box.size.z);
                    break;
                case CapsuleCollider capsule:
                    localCenter = capsule.center;
                    sizeX = sizeZ = capsule.radius * 2f;
                    break;
                case SphereCollider sphere:
                    localCenter = sphere.center;
                    sizeX = sizeZ = sphere.radius * 2f;
                    break;
                default:
                    return false;   // MeshCollider et al. contribute no analytic footprint here
            }

            // World center via pure transform math (no physics), then de-rotate into the host-local frame.
            var world = t.TransformPoint(localCenter);
            var local = rootInverse * (world - rootOrigin);

            // Half-extents in the collider's own lossy scale, axis-aligned (conservative), plus inflation.
            var scale = t.lossyScale;
            var halfX = (sizeX * 0.5f * Mathf.Abs(scale.x)) + Inflate;
            var halfZ = (sizeZ * 0.5f * Mathf.Abs(scale.z)) + Inflate;
            footprint = new StaticColliderFootprint(local.x, local.z, halfX, halfZ);
            return true;
        }
    }
}
