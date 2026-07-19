using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;

namespace SBPR.Niflheim.HomesteadStones.Application.Activation
{
    // T026 remediation — the SERVER-SIDE authority that turns the authoritative Stone + character +
    // authority aggregates into the per-(occupant, character) PersonalActivationSnapshot a client fetches,
    // and emits the bounded PersonalActivationNotification a client uses to decide when to refetch.
    //
    // This is the personal-effect analog of the accepted LocalActivationService. It does NOT own a second
    // active-effects ledger: every snapshot is a fresh derivation of DerivedActivationView from the injected
    // authoritative stores (purchase records + the (account, Stone) authority index). The only state it
    // holds is a per-(occupant, character) monotonic delivery sequence (so a client can drop a
    // stale/reordered notification and refetch) — that sequence is delivery metadata, not gameplay
    // authority: it never changes what is Active, only lets a client order fetches.
    //
    // Ownership semantics: a personal Character Effect is active for a caller iff they hold a purchase
    // record for the node at this Stone AND an active relationship (reservation) to this Stone, for THIS
    // character. It is NOT gated by occupancy, the Settlement Local policy, or governor presence — those
    // gate LOCAL placement/effects, not a personal recipe effect. Change the relationship and re-derive:
    // the same persisted purchase flips active<->dormant with zero writes (AT-NO-ACTIVE-LEDGER).
    //
    // Fail-closed: a missing character/Stone aggregate, an authority-index mismatch, or any resolve failure
    // returns PersonalActivationSnapshot.Denied (empty, all-inactive). Clients never author activation —
    // this class has no client-driven mutation surface; the ONLY inputs are the server-owned stores and the
    // transport-authenticated caller identity the engine layer supplies.
    //
    // net48 audit: engine-free (value objects + engine-free stores/views). Link-compiles into the net8 test
    // project, so every branch — including reorder/refetch/dormancy/hostile identity — is unit-tested
    // without a live server.

    public sealed class PersonalActivationService
    {
        private readonly IStoneAggregateStore _stones;
        private readonly ICharacterAggregateStore _characters;
        private readonly IAccountStoneAuthorityStore _authority;

        // Per-(Stone, occupant, character) monotonic delivery sequence. Delivery metadata only — NOT a
        // gameplay ledger. It exists so a client can order/deduplicate fetches; it never encodes what is
        // Active. Non-durable: a restart starts at 0 and the FIRST post-restart snapshot re-derives
        // identical active status from the durable aggregates (the sequence resetting cannot resurrect a
        // stale effect because the derivation is authoritative, not the sequence).
        private readonly Dictionary<string, long> _sequence =
            new Dictionary<string, long>(StringComparer.Ordinal);

        public PersonalActivationService(IStoneAggregateStore stones, ICharacterAggregateStore characters,
            IAccountStoneAuthorityStore authority)
        {
            _stones = stones ?? throw new ArgumentNullException(nameof(stones));
            _characters = characters ?? throw new ArgumentNullException(nameof(characters));
            _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        }

        private static string Key(StoneId stone, AccountId occupant, CharacterId character) =>
            stone.Value + "|" + occupant.Value + "|" + character.Value;

        /// <summary>The current delivery sequence for one caller (0 before any snapshot/notification).</summary>
        public long CurrentSequence(StoneId stone, AccountId occupant, CharacterId character) =>
            _sequence.TryGetValue(Key(stone, occupant, character), out var s) ? s : 0;

        /// <summary>Derive the per-caller read model WITHOUT bumping the delivery sequence. This is the
        /// client REFETCH path: a client that missed/reordered a notification asks for current truth. Fail
        /// closed on missing authority. The returned snapshot carries the CURRENT sequence so the client can
        /// reconcile against the last notification it saw.</summary>
        public PersonalActivationSnapshot Fetch(StoneId stone, AccountId occupant, CharacterId character)
        {
            long seq = CurrentSequence(stone, occupant, character);
            return Derive(stone, occupant, character, seq);
        }

        /// <summary>Publish: re-derive the caller's read model, BUMP the monotonic delivery sequence, and
        /// return BOTH the fresh snapshot and the bounded notification to hand to the transport. Called after
        /// a committed operation (purchase/relationship) or an observed relationship change. The snapshot and
        /// notification carry the SAME new sequence so a client that receives the notification and refetches
        /// converges. Fail closed on missing authority (still bumps the sequence + emits a denied snapshot so
        /// the client invalidates any previously delivered effect).</summary>
        public PersonalActivationDelivery Publish(StoneId stone, AccountId occupant, CharacterId character,
            string resultCode)
        {
            string key = Key(stone, occupant, character);
            long next = CurrentSequence(stone, occupant, character) + 1;
            _sequence[key] = next;

            var snapshot = Derive(stone, occupant, character, next);
            var notification = new PersonalActivationNotification(
                stone, occupant, character, next, snapshot.StoneRevision, snapshot.CharacterRevision,
                snapshot.AuthorityRevision, resultCode ?? string.Empty);
            return new PersonalActivationDelivery(snapshot, notification);
        }

        private PersonalActivationSnapshot Derive(StoneId stone, AccountId occupant, CharacterId character,
            long sequence)
        {
            var stoneAgg = _stones.GetStone(stone);
            var characterAgg = _characters.GetCharacter(occupant, character);
            if (stoneAgg == null || characterAgg == null)
                return PersonalActivationSnapshot.Denied(stone, occupant, character, sequence);

            var authorityIndex = _authority.GetAuthority(occupant, stone);
            if (authorityIndex == null)
                return PersonalActivationSnapshot.Denied(stone, occupant, character, sequence);

            DerivedActivationView view;
            try
            {
                // DerivedActivationView enforces (authority.Account == character.Account) and
                // (authority.Stone == stone.Stone); a mismatch throws. Any invariant failure fails closed —
                // never deliver an unverified effect.
                view = DerivedActivationView.Derive(stoneAgg, characterAgg, authorityIndex);
            }
            catch (Exception)
            {
                return PersonalActivationSnapshot.Denied(stone, occupant, character, sequence);
            }

            var rows = new List<PersonalActivationRow>(view.Nodes.Count);
            foreach (var n in view.Nodes)
                rows.Add(new PersonalActivationRow(n.Node, n.Developed, n.Offered, n.Purchased, n.Active));

            return new PersonalActivationSnapshot(
                stone, occupant, character, sequence,
                stoneAgg.Revision, characterAgg.Revision, authorityIndex.Revision,
                authorityPresent: true,
                rows);
        }
    }
}
