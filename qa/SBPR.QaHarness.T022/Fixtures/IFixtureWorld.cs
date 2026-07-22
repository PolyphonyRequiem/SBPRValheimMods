// ============================================================================
//  QA-M3 fixture ledger core (canonical, t_4db82cc0) — engine-free.
//  Adopted from the reviewed parallel prebuild (t_b5413567); namespace re-homed
//  into SBPR.QaHarness.T022.Core.Fixtures. Consumed under net48 by the helper and
//  link-compiled by the net8 tests-core suite. Still System.* only.
// ----------------------------------------------------------------------------
//  IFixtureWorld — the engine-free PORT the ledger drives to actually create and
//  destroy fixture objects. The prebuild depends only on this interface; canonical
//  M3 supplies the net48 adapter that fulfils it over the real ZDO/object system
//  (additive construction per ADR-0006). The ledger never references Unity/Valheim.
//
//  The contract is intentionally narrow and OWNERSHIP-KEYED:
//    * Create returns a WorldHandle string the adapter uses to find the object again.
//    * Destroy is given the handle of an object THIS ledger created — the adapter
//      must never destroy anything the ledger did not hand it.
//    * Exists lets recovery reconcile the ledger's belief against world truth.
//
//  A create may FAIL (returns a failed WorldOpResult) — that is how partial-failure
//  is modelled without exceptions; the ledger records only successes as owned.
//
//  Engine-free: System.* only.
// ============================================================================

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    /// <summary>The result of a world create attempt. On success carries the adapter's opaque
    /// handle to the created object; on failure carries a reason and no handle.</summary>
    public readonly struct WorldOpResult
    {
        private WorldOpResult(bool ok, string handle, string failureReason)
        {
            Ok = ok;
            Handle = handle ?? string.Empty;
            FailureReason = failureReason ?? string.Empty;
        }

        public bool Ok { get; }
        public string Handle { get; }
        public string FailureReason { get; }

        public static WorldOpResult Success(string handle) => new WorldOpResult(true, handle, string.Empty);
        public static WorldOpResult Failure(string reason) => new WorldOpResult(false, string.Empty, reason);
    }

    /// <summary>
    /// Engine-free seam for standing up / tearing down vanilla fixture scaffolding. The ledger owns
    /// the WHAT (deterministic OwnedResourceId + non-product category); the adapter owns the HOW
    /// (additive prefab construction). No product identity/AP/ownership crosses this boundary.
    /// </summary>
    public interface IFixtureWorld
    {
        /// <summary>Additively create one vanilla scaffolding object for the given owned resource.
        /// Returns a handle on success or a typed failure (partial-failure path).</summary>
        WorldOpResult Create(OwnedResourceId id, ResourceCategory category, string logicalId, double radiusMeters);

        /// <summary>Destroy the object previously created under `handle`. Returns Ok even if the object
        /// is already gone (cleanup is idempotent); returns Failure only on a genuine transient error.</summary>
        WorldOpResult Destroy(OwnedResourceId id, string handle);

        /// <summary>Does the object under `handle` still exist in the world? Used by crash recovery to
        /// reconcile the ledger's belief with world truth.</summary>
        bool Exists(string handle);
    }
}
