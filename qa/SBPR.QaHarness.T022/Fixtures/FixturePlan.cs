// ============================================================================
//  QA-M3 fixture ledger core (canonical, t_4db82cc0) — engine-free.
//  Adopted from the reviewed parallel prebuild (t_b5413567); namespace re-homed
//  into SBPR.QaHarness.T022.Core.Fixtures. Consumed under net48 by the helper and
//  link-compiled by the net8 tests-core suite. Still System.* only.
// ----------------------------------------------------------------------------
//  ResourceSpec + FixturePlan — the pure, declarative description of what a QA
//  fixture WANTS to exist. A spec carries ONLY logical id, count, and radius —
//  the allowlisted "what/how-many/how-far" triple. It carries NO product identity,
//  ownership, relationship, signature, journal, cache, or verdict, and no engine
//  handle: those are all unrepresentable here by construction.
//
//  A FixturePlan is a fixtureId plus a set of specs. It is inert data; validating
//  it against a ResourceAllowlist + FixtureBounds is what turns intent into an
//  actionable (and bounded, vanilla-only) plan. Planning never touches the world.
//
//  Engine-free: System.* only.
// ============================================================================

using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    /// <summary>One line of fixture intent: N copies of an allowlisted logical resource, placed
    /// within `radiusMeters` of the fixture origin. Value type; deliberately minimal.</summary>
    public readonly struct ResourceSpec
    {
        public ResourceSpec(string logicalId, int count, double radiusMeters)
        {
            if (string.IsNullOrEmpty(logicalId)) throw new ArgumentException("logicalId must be non-empty.", nameof(logicalId));
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be >= 1.");
            if (!(radiusMeters >= 0) || double.IsNaN(radiusMeters) || double.IsInfinity(radiusMeters))
                throw new ArgumentOutOfRangeException(nameof(radiusMeters), "radiusMeters must be a finite value >= 0.");
            LogicalId = logicalId;
            Count = count;
            RadiusMeters = radiusMeters;
        }

        /// <summary>The allowlisted logical resource id (spec key), NOT a raw prefab handle.</summary>
        public string LogicalId { get; }

        /// <summary>How many copies to ensure. Strictly positive.</summary>
        public int Count { get; }

        /// <summary>Placement radius from fixture origin. Finite, >= 0.</summary>
        public double RadiusMeters { get; }
    }

    /// <summary>An inert fixture request: a fixture id plus its resource specs. Duplicate logical ids
    /// within one plan are a conflict (caught at validation) — one plan describes each logical id once.</summary>
    public sealed class FixturePlan
    {
        private readonly List<ResourceSpec> _specs;

        public FixturePlan(string fixtureId, IEnumerable<ResourceSpec> specs)
        {
            if (string.IsNullOrEmpty(fixtureId)) throw new ArgumentException("fixtureId must be non-empty.", nameof(fixtureId));
            if (specs == null) throw new ArgumentNullException(nameof(specs));
            FixtureId = fixtureId;
            _specs = new List<ResourceSpec>(specs);
        }

        public string FixtureId { get; }

        public IReadOnlyList<ResourceSpec> Specs => _specs;
    }
}
