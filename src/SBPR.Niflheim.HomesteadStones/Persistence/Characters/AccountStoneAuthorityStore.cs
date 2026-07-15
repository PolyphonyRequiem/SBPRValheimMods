using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Persistence.Characters
{
    // T007 — server-owned projection sinks for the relationship lifecycle. The account–Stone authority
    // index (Aggregate 2) and the full character aggregate (Aggregate 3) are the two authoritative
    // rows a Bond/Attunement/Release mutation writes. RelationshipCommands is the sole mutation
    // authority; these sinks only STORE the post-state snapshots the durable relationship journal
    // reconciles onto them (mirroring the AP projection sinks behind OperationReceiptStore).
    //
    // A missing authority row reads as a VACANT index at the current revision-0 baseline, and a missing
    // character row reads as null so the caller can distinguish "no such character" from a zeroed one.
    //
    // net48 audit: engine-free (System.Collections.Generic + snapshot codecs). Link-compiles into net8.

    public interface IAccountStoneAuthorityStore
    {
        /// <summary>Current authority index for (account, Stone), or a fresh VACANT index at revision 0
        /// when none is stored. Never returns null so sibling-exclusivity checks have a stable baseline.</summary>
        AccountStoneAuthorityIndex GetAuthority(AccountId account, StoneId stoneId);

        /// <summary>Idempotently store the post-state authority snapshot for one operation. Set-to-state
        /// keyed by operationId, so replay after crash converges rather than double-advancing.</summary>
        void ApplyAuthorityProjection(string operationId, AccountStoneAuthorityIndex authority);
    }

    public interface ICharacterAggregateStore
    {
        /// <summary>Current full character aggregate for (account, character), or null when none is
        /// stored (the caller treats null as CharacterNotFound rather than inventing a zeroed one).</summary>
        CharacterProgressionAggregate? GetCharacter(AccountId account, CharacterId character);

        /// <summary>Seed/replace the authoritative character aggregate outside a relationship mutation
        /// (test fixtures, prior slices). Not keyed by operationId — it is a direct set.</summary>
        void PutCharacter(CharacterProgressionAggregate character);

        /// <summary>Idempotently store the post-state character snapshot for one operation, keyed by
        /// operationId so crash-replay converges.</summary>
        void ApplyCharacterProjection(string operationId, CharacterProgressionAggregate character);
    }

    /// <summary>Engine-free in-memory reference sinks used by the T007 tests and as the server-owned
    /// projection cache behind the durable relationship journal.</summary>
    public sealed class InMemoryAccountStoneAuthorityStore : IAccountStoneAuthorityStore
    {
        private readonly Dictionary<string, AccountStoneAuthorityIndex> _byKey =
            new Dictionary<string, AccountStoneAuthorityIndex>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _appliedOps =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static string Key(AccountId account, StoneId stoneId) => account.Value + "|" + stoneId.Value;

        public AccountStoneAuthorityIndex GetAuthority(AccountId account, StoneId stoneId)
        {
            if (_byKey.TryGetValue(Key(account, stoneId), out var idx)) return idx;
            return AccountStoneAuthorityIndex.Vacant(account, stoneId);
        }

        public void ApplyAuthorityProjection(string operationId, AccountStoneAuthorityIndex authority)
        {
            if (operationId == null) throw new ArgumentNullException(nameof(operationId));
            if (authority == null) throw new ArgumentNullException(nameof(authority));
            string key = Key(authority.Account, authority.StoneId);
            // Revision-guarded set-to-state (replay must not roll a NEWER projection backward): apply
            // only when the incoming snapshot is at least as new as what is stored. Journal rehydration
            // replays in ascending order so each committed op advances the projection; a late replay of
            // an OLDER committed op (e.g. bond-op1 after release-op2) is a no-op that keeps current state.
            if (_byKey.TryGetValue(key, out var current) && current.Revision > authority.Revision)
            {
                _appliedOps[operationId] = key;
                return;
            }
            _appliedOps[operationId] = key;
            _byKey[key] = authority;
        }
    }

    public sealed class InMemoryCharacterAggregateStore : ICharacterAggregateStore
    {
        private readonly Dictionary<string, CharacterProgressionAggregate> _byKey =
            new Dictionary<string, CharacterProgressionAggregate>(StringComparer.Ordinal);

        private static string Key(AccountId account, CharacterId character) =>
            account.Value + "|" + character.Value;

        public CharacterProgressionAggregate? GetCharacter(AccountId account, CharacterId character)
        {
            return _byKey.TryGetValue(Key(account, character), out var c) ? c : null;
        }

        public void PutCharacter(CharacterProgressionAggregate character)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            _byKey[Key(character.Account, character.Character)] = character;
        }

        public void ApplyCharacterProjection(string operationId, CharacterProgressionAggregate character)
        {
            if (operationId == null) throw new ArgumentNullException(nameof(operationId));
            if (character == null) throw new ArgumentNullException(nameof(character));
            string key = Key(character.Account, character.Character);
            // Revision-guarded set-to-state: replay of an OLDER committed op must not overwrite a newer
            // projection (defect: bond-op1 -> release-op2 -> retry-op1 must NOT restore pre-release state).
            if (_byKey.TryGetValue(key, out var current) && current.Revision > character.Revision)
                return;
            _byKey[key] = character;
        }
    }
}
