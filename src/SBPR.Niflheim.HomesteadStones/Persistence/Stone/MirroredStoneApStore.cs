using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Persistence.Stone
{
    // World-scoped Stone aggregate projection sink for Mirrored Stone AP (T002, Gate A slice).
    //
    // Mirrored Stone AP is a receipt-derived projection: it "equals the sum of accepted mirrored
    // deltas after receipt reconciliation; it is not inferred from current Personal AP balances and
    // is never debited or applied to a threshold/Facet in this proof" (data-model.md StoneProgression
    // invariants). The receipt store rebuilds the per-operation total from the durable journal and
    // hands it here to be applied idempotently, keyed by operationId, so replay converges.
    //
    // net48 audit: only System.Collections.Generic / value objects. No net5+ API, no UnityEngine /
    // Valheim reference, so this file link-compiles into the net8 test project. The ZDO-backed
    // production sink lives in ZdoStoneProgressionStore.cs (net48-only) and implements this interface.

    public interface IMirroredStoneApStore
    {
        /// <summary>Idempotently record that <paramref name="operationId"/> contributed
        /// <paramref name="mirroredApTotal"/> Mirrored Stone AP to this Stone. Applying the same
        /// operationId again is a no-op (set-to-total per operation, never blind increment), so
        /// crash-replay cannot double-count. Accumulate-only: this store exposes no debit path.</summary>
        void ApplyMirroredApProjection(StoneId stoneId, string operationId, int mirroredApTotal);

        /// <summary>Current Mirrored Stone AP total = sum of distinct accepted per-operation deltas.</summary>
        int GetMirroredStoneAp(StoneId stoneId);
    }

    /// <summary>Engine-free in-memory reference sink used by the contract/recovery tests and as the
    /// server-owned projection cache behind the ZDO store. Accumulate-only by construction.</summary>
    public sealed class InMemoryMirroredStoneApStore : IMirroredStoneApStore
    {
        // stoneId -> (operationId -> that operation's mirrored total). Storing per-operation totals
        // (not a running sum) is what makes replay idempotent.
        private readonly Dictionary<string, Dictionary<string, int>> _byStone =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        public void ApplyMirroredApProjection(StoneId stoneId, string operationId, int mirroredApTotal)
        {
            if (operationId == null) throw new ArgumentNullException(nameof(operationId));
            if (mirroredApTotal < 0) throw new ArgumentOutOfRangeException(nameof(mirroredApTotal));
            if (!_byStone.TryGetValue(stoneId.Value, out var ops))
            {
                ops = new Dictionary<string, int>(StringComparer.Ordinal);
                _byStone[stoneId.Value] = ops;
            }
            ops[operationId] = mirroredApTotal; // set-to-total: idempotent under replay
        }

        public int GetMirroredStoneAp(StoneId stoneId)
        {
            if (!_byStone.TryGetValue(stoneId.Value, out var ops)) return 0;
            int sum = 0;
            foreach (var v in ops.Values) sum += v;
            return sum;
        }
    }
}
