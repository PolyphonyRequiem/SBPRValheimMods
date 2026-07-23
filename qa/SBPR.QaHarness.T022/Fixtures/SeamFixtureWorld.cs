// ============================================================================
//  QA-M3 seam adapter (canonical, t_4db82cc0) — engine-free.
// ----------------------------------------------------------------------------
//  SeamFixtureWorld — the engine-free bridge from the owned-resource ledger's
//  IFixtureWorld port to the game-binding IVanillaFixtureSeam (declared in
//  ControlPlane/GameBindingAdapters.cs, PR #408 §3.5). The ledger owns the WHAT
//  (deterministic OwnedResourceId + non-product category); this adapter maps a
//  planned resource to the correct additive spawn/grant call on the seam and
//  hands the seam's spawned-instance id back as the ledger's world handle.
//
//  Why this exists as its own class: the ledger must never reference the game
//  seam directly (it multi-targets net8 for tests), and the seam must never know
//  about deterministic ids. This adapter is the ONLY place the two meet, so the
//  additive-only + product-rejection + allowlist invariants are enforced in ONE
//  reviewed spot:
//
//    • ADDITIVE ONLY (ADR-0006). The adapter can ONLY call the seam's additive
//      Spawn/Grant/Despawn methods — there is no clone-and-strip path reachable
//      from here, because IVanillaFixtureSeam exposes none. A non-additive base
//      (a cloned ZNetView-bearing prefab) can never be produced through this seam.
//    • PRODUCT REJECTION. Every Create re-checks VanillaFixtureManifest.IsProductId
//      on the logical id and fails closed on a product id, even though the plan
//      validator already rejected it — defence in depth at the world boundary.
//    • ALLOWLIST + PREFAB EXISTENCE. Create fails closed unless the seam reports
//      the prefab/item exists (a live-ObjectDB/ZNetScene drift check per §3.5).
//
//  Category routing: Station/PlacementAnchor -> SpawnPrefab (an additively placed
//  object); Material -> GrantItem (an allowlisted vanilla item grant). Destroy and
//  Exists map straight to the seam's Despawn/PrefabExists-tracked instance.
//
//  Engine-free: System.* only. Depends on the seam INTERFACE, never on Unity.
// ============================================================================

using System;
using System.Collections.Generic;
using SBPR.QaHarness.T022.Core.ControlPlane;

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    /// <summary>
    /// Bridges the ledger's <see cref="IFixtureWorld"/> port to the additive game seam
    /// <see cref="IVanillaFixtureSeam"/>. Enforces additive-only, product-rejection, and
    /// prefab-existence at the single world boundary. Bounded per-material grant quantity is
    /// applied here: one owned resource == one unit (the plan's Count already expanded to N ids).
    /// </summary>
    public sealed class SeamFixtureWorld : IFixtureWorld
    {
        private readonly IVanillaFixtureSeam _seam;

        public SeamFixtureWorld(IVanillaFixtureSeam seam)
        {
            _seam = seam ?? throw new ArgumentNullException(nameof(seam));
        }

        public WorldOpResult Create(OwnedResourceId id, ResourceCategory category, string logicalId,
            double radiusMeters, FixtureOwnershipMarker marker)
        {
            // Defence in depth: a product id must never reach the world seam, even though the
            // validator already refused it. Fail closed.
            if (VanillaFixtureManifest.IsProductId(logicalId))
                return WorldOpResult.Failure("product-id-refused: '" + logicalId + "' names product state, not a vanilla fixture");

            // Live drift guard (§3.5): the prefab/item must actually exist in the game.
            if (!_seam.PrefabExists(logicalId))
                return WorldOpResult.Failure("unknown-prefab: '" + logicalId + "' not present in the live ObjectDB/ZNetScene");

            string markerPayload = marker.Encode();

            try
            {
                string handle;
                switch (category)
                {
                    case ResourceCategory.Material:
                        // One owned resource == one granted unit (the plan expanded Count into N ids).
                        handle = _seam.GrantItem(logicalId, 1, markerPayload);
                        break;

                    case ResourceCategory.Station:
                    case ResourceCategory.PlacementAnchor:
                        // Additive server-authoritative construction at a bounded offset (ADR-0006).
                        handle = _seam.SpawnPrefab(logicalId, category, radiusMeters, markerPayload);
                        break;

                    default:
                        return WorldOpResult.Failure("unsupported-category: " + category);
                }

                // An empty handle means the seam could not durably stand up AND mark the object
                // (including a marker-write failure): treat as a partial-failure, never a leak.
                if (string.IsNullOrEmpty(handle))
                    return WorldOpResult.Failure("seam-returned-empty-handle for '" + logicalId + "'");

                return WorldOpResult.Success(handle);
            }
            catch (Exception ex)
            {
                // Any seam fault is a partial-failure, not a crash — the ledger records Failed.
                return WorldOpResult.Failure("seam-fault: " + ex.Message);
            }
        }

        public WorldOpResult Destroy(OwnedResourceId id, string handle)
        {
            if (string.IsNullOrEmpty(handle))
                return WorldOpResult.Success(string.Empty); // nothing to destroy; idempotent

            try
            {
                // Despawn returns false when the instance was already gone — cleanup is idempotent,
                // so that is a success, not a retryable failure.
                _seam.Despawn(handle);
                return WorldOpResult.Success(string.Empty);
            }
            catch (Exception ex)
            {
                // A genuine transient fault leaves the entry Created for a later cleanup retry.
                return WorldOpResult.Failure("seam-despawn-fault: " + ex.Message);
            }
        }

        public bool Exists(string handle)
        {
            if (string.IsNullOrEmpty(handle)) return false;
            // The seam tracks live spawned-instance ids; existence == the seam still holds it.
            // We conservatively treat a seam that no longer reports the instance as gone.
            return _seam.IsLiveInstance(handle);
        }

        public WorldDiscoveryResult DiscoverMarked(FixtureWorldScope scope)
        {
            // Translate the engine-free bounded scope into the seam's scope and consume its typed result.
            var seamScope = new FixtureSeamScope(scope.AllowedPrefabNames, scope.MaxRadiusMeters, scope.MaxCandidates);
            SeamDiscoveryResult raw;
            try
            {
                raw = _seam.DiscoverMarked(seamScope);
            }
            catch (Exception ex)
            {
                // A seam fault is a refusal, never an empty complete set (fail-closed).
                return WorldDiscoveryResult.Refused("seam-discovery-fault: " + ex.Message);
            }

            if (raw == null || raw.Outcome != SeamDiscoveryOutcome.Complete)
                return WorldDiscoveryResult.Refused("seam-discovery-refused: " + (raw?.Detail ?? "null-result"));

            var list = new List<MarkedInstance>(raw.Marked?.Count ?? 0);
            if (raw.Marked != null)
                foreach (var m in raw.Marked)
                    list.Add(new MarkedInstance(m.MarkerPayload, m.SpawnedInstanceId));
            return WorldDiscoveryResult.Complete(list);
        }
    }
}
