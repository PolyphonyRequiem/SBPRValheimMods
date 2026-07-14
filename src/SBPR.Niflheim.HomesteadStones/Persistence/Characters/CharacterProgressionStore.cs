using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Persistence.Characters
{
    // Server-owned character aggregate projection sink for Personal AP + Cumulative AP Earned
    // (T002, Gate A slice). Per (AccountId, CharacterId, StoneId).
    //
    // Per data-model.md CharacterProgression invariants: "Personal AP and Cumulative AP Earned are
    // never negative; Cumulative AP never decreases in this proof." Both are receipt-derived
    // projections rebuilt from the durable journal and applied here idempotently, keyed by
    // operationId, so crash-replay converges to exactly one credit per operation.
    //
    // net48 audit: only System.Collections.Generic / value objects. No net5+ API, no UnityEngine /
    // Valheim reference, so this file link-compiles into the net8 test project.

    public interface ICharacterApStore
    {
        /// <summary>Idempotently record that <paramref name="operationId"/> credited this character
        /// <paramref name="personalApTotal"/> Personal AP and <paramref name="cumulativeApTotal"/>
        /// Cumulative AP at this Stone. Same operationId again is a no-op (set-to-total per
        /// operation), so crash-replay cannot double-count.</summary>
        void ApplyApProjection(AccountId account, CharacterId character, StoneId stoneId, string operationId,
            int personalApTotal, int cumulativeApTotal);

        int GetPersonalAp(AccountId account, CharacterId character, StoneId stoneId);
        int GetCumulativeAp(AccountId account, CharacterId character, StoneId stoneId);
    }

    /// <summary>Engine-free in-memory reference sink used by the contract/recovery tests and as the
    /// server-owned projection cache behind the durable character store.</summary>
    public sealed class InMemoryCharacterApStore : ICharacterApStore
    {
        private readonly Dictionary<string, Dictionary<string, ApDelta>> _byCharacterStone =
            new Dictionary<string, Dictionary<string, ApDelta>>(StringComparer.Ordinal);

        private readonly struct ApDelta
        {
            public ApDelta(int personal, int cumulative) { Personal = personal; Cumulative = cumulative; }
            public int Personal { get; }
            public int Cumulative { get; }
        }

        private static string Key(AccountId account, CharacterId character, StoneId stoneId) =>
            account.Value + "|" + character.Value + "|" + stoneId.Value;

        public void ApplyApProjection(AccountId account, CharacterId character, StoneId stoneId, string operationId,
            int personalApTotal, int cumulativeApTotal)
        {
            if (operationId == null) throw new ArgumentNullException(nameof(operationId));
            if (personalApTotal < 0) throw new ArgumentOutOfRangeException(nameof(personalApTotal));
            if (cumulativeApTotal < 0) throw new ArgumentOutOfRangeException(nameof(cumulativeApTotal));
            var key = Key(account, character, stoneId);
            if (!_byCharacterStone.TryGetValue(key, out var ops))
            {
                ops = new Dictionary<string, ApDelta>(StringComparer.Ordinal);
                _byCharacterStone[key] = ops;
            }
            ops[operationId] = new ApDelta(personalApTotal, cumulativeApTotal); // set-to-total: idempotent
        }

        public int GetPersonalAp(AccountId account, CharacterId character, StoneId stoneId)
        {
            if (!_byCharacterStone.TryGetValue(Key(account, character, stoneId), out var ops)) return 0;
            int sum = 0;
            foreach (var d in ops.Values) sum += d.Personal;
            return sum;
        }

        public int GetCumulativeAp(AccountId account, CharacterId character, StoneId stoneId)
        {
            if (!_byCharacterStone.TryGetValue(Key(account, character, stoneId), out var ops)) return 0;
            int sum = 0;
            foreach (var d in ops.Values) sum += d.Cumulative;
            return sum;
        }
    }
}
