// ============================================================================
//  QA-M3 fixture ledger core (canonical, t_4db82cc0) — engine-free.
//  Adopted from the reviewed parallel prebuild (t_b5413567); namespace re-homed
//  into SBPR.QaHarness.T022.Core.Fixtures. Consumed under net48 by the helper and
//  link-compiled by the net8 tests-core suite. Still System.* only.
// ----------------------------------------------------------------------------
//  ResourceAllowlist — the closed registry mapping an allowlisted LOGICAL id to
//  its ResourceCategory. A fixture plan may only reference a logical id that is
//  present here; an unknown logical id is rejected (never "assume a prefab"). This
//  is the second half of the "vanilla-only" boundary: ResourceCategory forbids
//  product KINDS structurally, and this allowlist forbids unknown logical NAMES.
//
//  There is intentionally NO entry that maps to a product/artifact — the allowlist
//  only carries ordinary vanilla scaffolding logical ids. The mapping is data the
//  caller supplies (so canonical M3 can seed the real manifest ids after M2),
//  but the TYPE guarantees each entry resolves to a non-product category.
//
//  Engine-free: System.* only.
// ============================================================================

using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    /// <summary>Immutable closed allowlist: logical id -> non-product ResourceCategory. Lookups are
    /// ordinal and case-sensitive; an id not present is genuinely unknown and must be rejected.</summary>
    public sealed class ResourceAllowlist
    {
        private readonly Dictionary<string, ResourceCategory> _entries;

        public ResourceAllowlist(IReadOnlyDictionary<string, ResourceCategory> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            _entries = new Dictionary<string, ResourceCategory>(StringComparer.Ordinal);
            foreach (var kv in entries)
            {
                if (string.IsNullOrEmpty(kv.Key))
                    throw new ArgumentException("Allowlist logical id must be non-empty.");
                if (!Enum.IsDefined(typeof(ResourceCategory), kv.Value))
                    throw new ArgumentException("Allowlist category '" + kv.Value + "' for '" + kv.Key + "' is not a defined ResourceCategory.");
                _entries[kv.Key] = kv.Value;
            }
        }

        public int Count => _entries.Count;

        /// <summary>True iff the logical id is on the allowlist.</summary>
        public bool Contains(string logicalId) =>
            logicalId != null && _entries.ContainsKey(logicalId);

        /// <summary>Resolve the category for an allowlisted logical id; false if unknown.</summary>
        public bool TryGetCategory(string logicalId, out ResourceCategory category)
        {
            if (logicalId != null && _entries.TryGetValue(logicalId, out category)) return true;
            category = default;
            return false;
        }

        /// <summary>The set of allowlisted logical ids (stable ordinal order).</summary>
        public IReadOnlyCollection<string> LogicalIds
        {
            get
            {
                var list = new List<string>(_entries.Keys);
                list.Sort(StringComparer.Ordinal);
                return list;
            }
        }
    }
}
