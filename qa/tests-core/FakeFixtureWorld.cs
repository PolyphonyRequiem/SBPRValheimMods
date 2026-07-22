// ============================================================================
//  QA-M3 fixture ledger tests (canonical, t_4db82cc0) — engine-free.
//  Adopted from the reviewed t_b5413567 prebuild; namespace re-homed.
// ----------------------------------------------------------------------------
//  FakeFixtureWorld — an in-memory IFixtureWorld for exercising the ledger without
//  any engine. It models the observable world contract the real net48 adapter must
//  fulfil: created objects exist under a handle, destroy removes them, and an
//  independently-seeded UNRELATED object exists but is NEVER created or destroyed by
//  the ledger (there is no ledger entry that could reach it). It also supports
//  injecting create/destroy failures (partial failure + cleanup-retry paths) and a
//  "world wipe" (crash: created objects vanish) for recovery tests.
// ============================================================================

using System;
using System.Collections.Generic;
using SBPR.QaHarness.T022.Core.Fixtures;

namespace SBPR.QaHarness.T022.Core.Tests
{
    internal sealed class FakeFixtureWorld : IFixtureWorld
    {
        // handle -> logical id of live objects the ledger created.
        private readonly Dictionary<string, string> _live = new Dictionary<string, string>(StringComparer.Ordinal);
        // Independently-seeded objects the ledger did NOT create (must survive cleanup).
        private readonly HashSet<string> _unrelated = new HashSet<string>(StringComparer.Ordinal);
        private int _seq;

        // Adversarial injection knobs.
        public HashSet<string> FailCreateForLogicalId { get; } = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> FailDestroyForHandle { get; } = new HashSet<string>(StringComparer.Ordinal);

        public int LiveCount => _live.Count;
        public int UnrelatedCount => _unrelated.Count;

        /// <summary>Seed an unrelated pre-existing world object the ledger has no knowledge of.</summary>
        public string SeedUnrelated(string tag)
        {
            var handle = "unrelated:" + tag;
            _unrelated.Add(handle);
            return handle;
        }

        public bool UnrelatedExists(string handle) => _unrelated.Contains(handle);

        /// <summary>Simulate process death: every ledger-created object vanishes (never yet durable).</summary>
        public void WipeCreated() => _live.Clear();

        public WorldOpResult Create(OwnedResourceId id, ResourceCategory category, string logicalId, double radiusMeters)
        {
            if (FailCreateForLogicalId.Contains(logicalId))
                return WorldOpResult.Failure("injected create failure for " + logicalId);
            var handle = "h" + (_seq++).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + id.Canonical;
            _live[handle] = logicalId;
            return WorldOpResult.Success(handle);
        }

        public WorldOpResult Destroy(OwnedResourceId id, string handle)
        {
            if (FailDestroyForHandle.Contains(handle))
                return WorldOpResult.Failure("injected destroy failure for " + handle);
            _live.Remove(handle);
            return WorldOpResult.Success(handle);
        }

        public bool Exists(string handle) => _live.ContainsKey(handle);
    }
}
