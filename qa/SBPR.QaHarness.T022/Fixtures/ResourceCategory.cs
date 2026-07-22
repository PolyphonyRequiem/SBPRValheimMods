// ============================================================================
//  QA-M3 fixture ledger core (canonical, t_4db82cc0) — engine-free.
//  Adopted from the reviewed parallel prebuild (t_b5413567); namespace re-homed
//  into SBPR.QaHarness.T022.Core.Fixtures. Consumed under net48 by the helper and
//  link-compiled by the net8 tests-core suite. Still System.* only.
// ----------------------------------------------------------------------------
//  ResourceCategory — the CLOSED allowlist of ordinary VANILLA fixture-primitive
//  kinds a QA fixture helper may provision. This enum is the load-bearing
//  "product identity is unrepresentable" boundary: there is NO enum member for a
//  product / artifact-under-test / Bond / Attunement / AP / ownership / signature
//  / token / verdict / journal / cache. A fixture is only ever plain world scaffolding
//  (a material pile, a crafting station, a bare placement anchor) that a later
//  acceptance test stands next to — NEVER the thing being tested.
//
//  Engine-free: no UnityEngine / Valheim / BepInEx / HarmonyX. System.* only.
// ============================================================================

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    /// <summary>
    /// The only fixture-primitive kinds this helper can represent. Each is an ordinary
    /// vanilla scaffolding object. The absence of any product/artifact member is
    /// intentional and structural — the type system, not discipline, forbids a fixture
    /// helper from standing up the artifact under test or any progression/identity state.
    /// </summary>
    public enum ResourceCategory
    {
        /// <summary>A pile/stack of ordinary vanilla materials (e.g. Wood, Stone) — inert.</summary>
        Material = 1,

        /// <summary>An ordinary vanilla crafting station placed as scaffolding.</summary>
        Station = 2,

        /// <summary>A bare placement anchor / marker — a position within a fixture with no
        /// gameplay behaviour of its own.</summary>
        PlacementAnchor = 3
    }
}
