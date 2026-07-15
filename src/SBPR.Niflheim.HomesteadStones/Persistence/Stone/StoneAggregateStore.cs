using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Persistence.Stone
{
    // T010 — server-owned projection sink for the full Stone progression aggregate (Aggregate 1). Facet
    // commitment (FacetCommands) is the sole mutation authority; this sink only STORES the post-state
    // snapshot the durable commit journal reconciles onto it, mirroring the account–Stone authority /
    // character projection sinks behind RelationshipCommands.
    //
    // This is DISTINCT from the ZDO-backed IMirroredStoneApStore: that sink projects only the Mirrored
    // Stone AP scalar onto the world Stone ZDO. This store holds the whole engine-free aggregate
    // (levels, foundational identities, Committed Trees, node development, provenance) that the Facet
    // commit reads and rewrites. Production wiring persists the aggregate snapshot alongside the Stone
    // ZDO; the engine-free in-memory reference sink is exercised by the T010 tests.
    //
    // A missing Stone row reads as null so the caller can distinguish "no such Stone" (reject) from a
    // preconfigured one.
    //
    // net48 audit: engine-free (System.Collections.Generic + aggregate snapshot codec). Link-compiles
    // into the net8 test project.

    public interface IStoneAggregateStore
    {
        /// <summary>Current full Stone aggregate for <paramref name="stoneId"/>, or null when none is
        /// stored (the caller treats null as StoneNotFound rather than inventing a zeroed one).</summary>
        StoneProgressionAggregate? GetStone(StoneId stoneId);

        /// <summary>Seed/replace the authoritative Stone aggregate outside a commit mutation (test
        /// fixtures, prior slices). Not keyed by operationId — a direct set.</summary>
        void PutStone(StoneProgressionAggregate stone);

        /// <summary>Idempotently store the post-state Stone snapshot for one operation, keyed by
        /// operationId so crash-replay converges. Revision-guarded: a replay of an OLDER committed op
        /// never rolls a newer projection backward.</summary>
        void ApplyStoneProjection(string operationId, StoneProgressionAggregate stone);
    }

    /// <summary>Engine-free in-memory reference sink used by the T010 tests and as the server-owned
    /// projection cache behind the durable Facet commit journal.</summary>
    public sealed class InMemoryStoneAggregateStore : IStoneAggregateStore
    {
        private readonly Dictionary<string, StoneProgressionAggregate> _byKey =
            new Dictionary<string, StoneProgressionAggregate>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _appliedOps =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static string Key(StoneId stoneId) => stoneId.Value;

        public StoneProgressionAggregate? GetStone(StoneId stoneId)
        {
            return _byKey.TryGetValue(Key(stoneId), out var s) ? s : null;
        }

        public void PutStone(StoneProgressionAggregate stone)
        {
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            _byKey[Key(stone.StoneId)] = stone;
        }

        public void ApplyStoneProjection(string operationId, StoneProgressionAggregate stone)
        {
            if (operationId == null) throw new ArgumentNullException(nameof(operationId));
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            string key = Key(stone.StoneId);
            // Revision-guarded set-to-state: replay of an OLDER committed op must not overwrite a newer
            // projection. Journal rehydration replays in ascending order so each committed op advances
            // the projection; a late replay of an older op is a no-op that keeps current state.
            if (_byKey.TryGetValue(key, out var current) && current.Revision > stone.Revision)
            {
                _appliedOps[operationId] = key;
                return;
            }
            _appliedOps[operationId] = key;
            _byKey[key] = stone;
        }
    }
}
