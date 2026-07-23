// ============================================================================
//  QA-M3 fixture ledger core (canonical, t_4db82cc0) — engine-free.
//  Adopted from the reviewed parallel prebuild (t_b5413567); namespace re-homed
//  into SBPR.QaHarness.T022.Core.Fixtures. Consumed under net48 by the helper and
//  link-compiled by the net8 tests-core suite. Still System.* only.
// ----------------------------------------------------------------------------
//  OwnedResourceId — the opaque, stable identity of ONE resource the fixture
//  helper created and therefore OWNS. Ownership is the whole point of the ledger:
//  the helper may only ever clean up (delete) an object whose id it minted; an
//  unrelated pre-existing world object has no OwnedResourceId in the ledger and is
//  therefore structurally un-deletable by the cleanup planner.
//
//  The id is derived deterministically from (fixtureId, logicalId, ordinal) so the
//  same ensure request reconstructs the same id after a crash — idempotency and
//  crash recovery both depend on this being a pure function of the plan, not a
//  random GUID minted at creation time.
//
//  Engine-free: System.* only.
// ============================================================================

using System;

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    /// <summary>
    /// Deterministic, opaque owned-resource identity. Value type; equality is by value.
    /// Two ids are equal iff their (FixtureId, LogicalId, Ordinal) triples are equal, so a
    /// replayed ensure recomputes an id equal to the one already tracked (idempotency).
    /// </summary>
    public readonly struct OwnedResourceId : IEquatable<OwnedResourceId>
    {
        public OwnedResourceId(string fixtureId, string logicalId, int ordinal)
        {
            if (string.IsNullOrEmpty(fixtureId)) throw new ArgumentException("fixtureId must be non-empty.", nameof(fixtureId));
            if (string.IsNullOrEmpty(logicalId)) throw new ArgumentException("logicalId must be non-empty.", nameof(logicalId));
            if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal), "ordinal must be >= 0.");
            FixtureId = fixtureId;
            LogicalId = logicalId;
            Ordinal = ordinal;
        }

        /// <summary>Which fixture instance this resource belongs to.</summary>
        public string FixtureId { get; }

        /// <summary>The allowlisted logical resource id (spec key), NOT a raw prefab handle.</summary>
        public string LogicalId { get; }

        /// <summary>0-based index of this resource within its (fixture, logicalId) request count.</summary>
        public int Ordinal { get; }

        /// <summary>Stable, human-readable canonical form; also the durable key used by the ledger.</summary>
        public string Canonical =>
            FixtureId + "/" + LogicalId + "#" + Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public bool Equals(OwnedResourceId other) =>
            string.Equals(FixtureId, other.FixtureId, StringComparison.Ordinal)
            && string.Equals(LogicalId, other.LogicalId, StringComparison.Ordinal)
            && Ordinal == other.Ordinal;

        public override bool Equals(object? obj) => obj is OwnedResourceId o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + StringComparer.Ordinal.GetHashCode(FixtureId);
                h = h * 31 + StringComparer.Ordinal.GetHashCode(LogicalId);
                h = h * 31 + Ordinal;
                return h;
            }
        }

        public override string ToString() => Canonical;

        public static bool operator ==(OwnedResourceId a, OwnedResourceId b) => a.Equals(b);
        public static bool operator !=(OwnedResourceId a, OwnedResourceId b) => !a.Equals(b);
    }
}
