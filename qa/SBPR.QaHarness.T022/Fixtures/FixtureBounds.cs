// ============================================================================
//  QA-M3 fixture ledger core (canonical, t_4db82cc0) — engine-free.
//  Adopted from the reviewed parallel prebuild (t_b5413567); namespace re-homed
//  into SBPR.QaHarness.T022.Core.Fixtures. Consumed under net48 by the helper and
//  link-compiled by the net8 tests-core suite. Still System.* only.
// ----------------------------------------------------------------------------
//  FixtureBounds — the bounded validation limits every fixture plan must satisfy.
//  Bounds exist so a malformed or adversarial request (overflow counts, absurd
//  radius, too many distinct resources) is rejected BEFORE any world side effect,
//  and so a fixture can never fan out unboundedly. All limits are inclusive
//  maxima; a value at the limit is accepted, one past it is rejected.
//
//  Engine-free: System.* only.
// ============================================================================

using System;

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    /// <summary>Immutable inclusive limits for a single fixture plan. Defaults are deliberately
    /// small — a QA fixture is a handful of scaffolding objects, not a world.</summary>
    public readonly struct FixtureBounds
    {
        public FixtureBounds(int maxDistinctResources, int maxCountPerResource, int maxTotalObjects, double maxRadiusMeters)
        {
            if (maxDistinctResources <= 0) throw new ArgumentOutOfRangeException(nameof(maxDistinctResources));
            if (maxCountPerResource <= 0) throw new ArgumentOutOfRangeException(nameof(maxCountPerResource));
            if (maxTotalObjects <= 0) throw new ArgumentOutOfRangeException(nameof(maxTotalObjects));
            if (!(maxRadiusMeters > 0) || double.IsNaN(maxRadiusMeters) || double.IsInfinity(maxRadiusMeters))
                throw new ArgumentOutOfRangeException(nameof(maxRadiusMeters));
            MaxDistinctResources = maxDistinctResources;
            MaxCountPerResource = maxCountPerResource;
            MaxTotalObjects = maxTotalObjects;
            MaxRadiusMeters = maxRadiusMeters;
        }

        /// <summary>Max number of distinct (logicalId) specs one plan may list.</summary>
        public int MaxDistinctResources { get; }

        /// <summary>Max count a single spec may request.</summary>
        public int MaxCountPerResource { get; }

        /// <summary>Max total objects (sum of all counts) across the whole plan.</summary>
        public int MaxTotalObjects { get; }

        /// <summary>Max placement radius (meters) a spec may request from the fixture origin.</summary>
        public double MaxRadiusMeters { get; }

        /// <summary>Conservative default limits for an ordinary QA fixture.</summary>
        public static FixtureBounds Default => new FixtureBounds(
            maxDistinctResources: 16,
            maxCountPerResource: 64,
            maxTotalObjects: 256,
            maxRadiusMeters: 32.0);
    }
}
