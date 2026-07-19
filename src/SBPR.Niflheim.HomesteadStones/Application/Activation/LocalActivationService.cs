using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;

namespace SBPR.Niflheim.HomesteadStones.Application.Activation
{
    // T016 shared runtime substrate — the SERVER-SIDE authority that turns the authoritative Stone
    // aggregate + relationship/governance + Stone Area + Settlement policy into the per-occupant
    // LocalActivationSnapshot a client fetches, and emits the bounded LocalActivationNotification a
    // client uses to decide when to refetch (contracts.md §"Notification contract"; T021 investigation).
    //
    // This is the ONE composition seam the whole US4 family (Tracers 5-8) sits on. It does NOT own a
    // second active-effects ledger: every snapshot is a fresh derivation of LocalEffectActivationView
    // from the injected authoritative stores + the server-observed occupancy/governance facts. The only
    // state it holds is a per-occupant monotonic delivery sequence (so a client can drop a stale/reordered
    // notification and refetch) — that sequence is delivery metadata, not gameplay authority: it never
    // changes what is Active, only lets a client order fetches.
    //
    // Fail-closed: a missing Stone aggregate, an authority-index mismatch, or any resolve failure returns
    // LocalActivationSnapshot.Denied (empty, all-inactive). Clients never author activation — this class
    // has no client-driven mutation surface; the ONLY inputs are the server-owned stores and the
    // server-observed facts the engine layer supplies.
    //
    // net48 audit: engine-free (value objects + engine-free stores/views). Link-compiles into the net8
    // test project, so every branch — including reorder/refetch/dormancy/hostile identity — is unit-tested
    // without a live server.

    /// <summary>Server-observed, cross-account facts about ONE occupant standing at ONE Stone that are not
    /// carried in the persisted aggregates: whether the occupant is the validated Homestead owner, whether
    /// they currently hold an active relationship to this Stone, whether they stand inside the Stone Area,
    /// and whether ANY authorized Governor is present Stone-wide. Supplied by the engine-bound layer
    /// (server truth), never by a client claim.</summary>
    public readonly struct OccupantPresence
    {
        public OccupantPresence(AccountId occupant, CharacterId character, bool isOwner,
            bool hasActiveRelationship, bool insideStoneArea, bool authorizedGovernorPresent)
        {
            Occupant = occupant;
            Character = character;
            IsOwner = isOwner;
            HasActiveRelationship = hasActiveRelationship;
            InsideStoneArea = insideStoneArea;
            AuthorizedGovernorPresent = authorizedGovernorPresent;
        }

        public AccountId Occupant { get; }
        public CharacterId Character { get; }
        public bool IsOwner { get; }
        public bool HasActiveRelationship { get; }
        public bool InsideStoneArea { get; }
        public bool AuthorizedGovernorPresent { get; }
    }

    public sealed class LocalActivationService
    {
        private readonly IStoneAggregateStore _stones;
        private readonly HomesteadProgressionCatalog _catalog;

        // Per-(Stone, occupant) monotonic delivery sequence. Delivery metadata only — NOT a gameplay
        // ledger. It exists so a client can order/deduplicate fetches; it never encodes what is Active.
        // Non-durable: a restart starts at 0 and the FIRST post-restart snapshot re-derives identical
        // active/dormant status from the durable Stone journal (the sequence resetting cannot resurrect a
        // stale effect because the derivation is authoritative, not the sequence).
        private readonly Dictionary<string, long> _sequence =
            new Dictionary<string, long>(StringComparer.Ordinal);

        public LocalActivationService(IStoneAggregateStore stones, HomesteadProgressionCatalog? catalog = null)
        {
            _stones = stones ?? throw new ArgumentNullException(nameof(stones));
            _catalog = catalog ?? new HomesteadProgressionCatalog();
        }

        private static string Key(StoneId stone, AccountId occupant) => stone.Value + "|" + occupant.Value;

        /// <summary>The current delivery sequence for one occupant (0 before any snapshot/notification).</summary>
        public long CurrentSequence(StoneId stone, AccountId occupant) =>
            _sequence.TryGetValue(Key(stone, occupant), out var s) ? s : 0;

        /// <summary>Derive the per-occupant read model WITHOUT bumping the delivery sequence. This is the
        /// client REFETCH path: a client that missed/reordered a notification asks for current truth. Fail
        /// closed on missing Stone authority. The returned snapshot carries the CURRENT sequence so the
        /// client can reconcile against the last notification it saw.</summary>
        public LocalActivationSnapshot Fetch(StoneId stone, in OccupantPresence presence)
        {
            long seq = CurrentSequence(stone, presence.Occupant);
            return Derive(stone, presence, seq);
        }

        /// <summary>Publish: re-derive the occupant's read model, BUMP the monotonic delivery sequence, and
        /// return BOTH the fresh snapshot and the bounded notification to hand to the transport. Called
        /// after a committed operation (development/facet/policy/relationship) or an observed
        /// occupancy/governance change. The snapshot and notification carry the SAME new sequence so a
        /// client that receives the notification and refetches converges. Fail closed on missing
        /// authority (still bumps the sequence + emits a denied snapshot so the client invalidates any
        /// previously delivered effect).</summary>
        public LocalActivationDelivery Publish(StoneId stone, in OccupantPresence presence, string resultCode)
        {
            string key = Key(stone, presence.Occupant);
            long next = CurrentSequence(stone, presence.Occupant) + 1;
            _sequence[key] = next;

            var snapshot = Derive(stone, presence, next);
            var notification = new LocalActivationNotification(
                stone, presence.Occupant, next, snapshot.StoneRevision, snapshot.PolicyRevision,
                resultCode ?? string.Empty);
            return new LocalActivationDelivery(snapshot, notification);
        }

        private LocalActivationSnapshot Derive(StoneId stone, in OccupantPresence presence, long sequence)
        {
            var aggregate = _stones.GetStone(stone);
            if (aggregate == null)
                return LocalActivationSnapshot.Denied(stone, presence.Occupant, sequence);

            LocalEffectActivationView view;
            try
            {
                view = LocalEffectActivationView.Derive(
                    aggregate, _catalog, presence.Occupant, presence.IsOwner,
                    presence.HasActiveRelationship, presence.InsideStoneArea,
                    presence.AuthorizedGovernorPresent);
            }
            catch (Exception)
            {
                // Any derivation invariant failure fails closed — never deliver an unverified effect.
                return LocalActivationSnapshot.Denied(stone, presence.Occupant, sequence);
            }

            var rows = new List<LocalActivationRow>(view.Effects.Count);
            foreach (var e in view.Effects)
                rows.Add(LocalActivationRow.FromStatus(e));

            return new LocalActivationSnapshot(
                stone, presence.Occupant, sequence,
                aggregate.Revision, view.PolicyRevision, view.PolicyMode,
                authorityPresent: true,
                occupantPolicyEligible: view.OccupantPolicyEligible,
                insideStoneArea: view.InsideStoneArea,
                authorizedGovernorPresent: view.AuthorizedGovernorPresent,
                rows);
        }
    }

    /// <summary>The pair a Publish returns: the fresh per-occupant read model and the bounded notification
    /// that shares its sequence. The transport delivers the notification (small) and lets the client
    /// refetch the snapshot; a listen-host consumer can also apply the snapshot directly.</summary>
    public readonly struct LocalActivationDelivery
    {
        public LocalActivationDelivery(LocalActivationSnapshot snapshot, LocalActivationNotification notification)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Notification = notification;
        }

        public LocalActivationSnapshot Snapshot { get; }
        public LocalActivationNotification Notification { get; }
    }
}
