using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Features.Progression;

namespace SBPR.Niflheim.HomesteadStones.Application.Queries
{
    // T035 — BOUNDED revision / invalidation notifications for the progression READ MODEL
    // (contracts.md §"Notification contract"):
    //
    //   "After a committed operation, publish a bounded invalidation/event containing stable entity IDs,
    //    new revisions, and result code. Do not broadcast entire character ledgers or trust notification
    //    order as authority. Clients that miss or reorder notifications fetch the current read model."
    //
    // The Local-EFFECT delivery channel already has its own bounded pair (LocalActivationNotification +
    // LocalActivationSnapshot) for "is this effect currently active for this occupant". This file is the
    // sibling for the PROGRESSION READ MODEL a Stones UI renders — "the Stone/character/policy you are
    // looking at moved; refetch it". They are deliberately separate: an effect-activation change and a
    // read-model change have different consumers and different fetch targets.
    //
    // Boundaries, all load-bearing:
    //   * BOUNDED. A notification carries stable IDs (Stone + subscriber account), the command type that
    //     moved it, the new revisions, and a result code. It NEVER carries balances, purchases,
    //     relationships, node rows, or any other account's data. Its serialized form is fixed-size in the
    //     number of fields — there is no list to grow.
    //   * NOT AUTHORITY. A client may only use it to DECIDE WHETHER TO REFETCH. The endpoint's read
    //     queries remain the only source of truth. Reorder-safe: a per-subscriber monotonic Sequence lets
    //     a client drop a late/duplicate event, and revision movement alone also triggers a refetch, so a
    //     DROPPED notification cannot strand a client on stale data (the next one it sees has moved
    //     revisions and forces the fetch).
    //   * PER-SUBSCRIBER. Publication is explicitly scoped to accounts that have subscribed for a Stone.
    //     There is no broadcast-to-everyone path, so a Stone mutation never leaks the fact of its own
    //     revisions to an unrelated account.
    //   * COMMITTED-ONLY. The endpoint attaches a notification only to a non-rejected outcome, so a
    //     hostile caller cannot drive other clients into refetch storms with rejected submissions.
    //
    // net48 audit: engine-free (value objects + the shipped snapshot codec + dictionaries). Link-compiles
    // into the net8 test project.

    /// <summary>The BOUNDED invalidation event published after a COMMITTED progression operation. Stable
    /// ids + new revisions + result code, and nothing else.</summary>
    public readonly struct ProgressionRevisionNotification
    {
        public ProgressionRevisionNotification(
            StoneId stoneId,
            string subscriberAccountId,
            ProgressionCommandType commandType,
            long stoneRevision,
            long characterRevision,
            long policyRevision,
            string resultCode,
            long sequence = 0)
        {
            if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            StoneId = stoneId;
            SubscriberAccountId = subscriberAccountId ?? string.Empty;
            CommandType = commandType;
            StoneRevision = stoneRevision;
            CharacterRevision = characterRevision;
            PolicyRevision = policyRevision;
            ResultCode = resultCode ?? string.Empty;
            Sequence = sequence;
        }

        public StoneId StoneId { get; }

        /// <summary>The account this copy of the event is addressed to. Set at publication time by
        /// <see cref="ProgressionNotificationHub"/>; the endpoint mints the event with the ACTING account
        /// and the hub re-stamps one copy per subscriber.</summary>
        public string SubscriberAccountId { get; }

        public ProgressionCommandType CommandType { get; }

        public long StoneRevision { get; }
        public long CharacterRevision { get; }
        public long PolicyRevision { get; }
        public string ResultCode { get; }

        /// <summary>Monotonic per-(Stone, subscriber) delivery sequence stamped at publication. Delivery
        /// metadata only — it never encodes what changed, it only lets a client order/deduplicate.</summary>
        public long Sequence { get; }

        internal ProgressionRevisionNotification ForSubscriber(string account, long sequence) =>
            new ProgressionRevisionNotification(StoneId, account, CommandType, StoneRevision,
                CharacterRevision, PolicyRevision, ResultCode, sequence);

        public string Serialize() => new SnapshotWriter()
            .Put("stone", StoneId.Value)
            .Put("sub", SubscriberAccountId)
            .PutInt("cmd", (int)CommandType)
            .PutLong("srev", StoneRevision)
            .PutLong("crev", CharacterRevision)
            .PutLong("prev", PolicyRevision)
            .Put("rc", ResultCode)
            .PutLong("seq", Sequence)
            .Build();

        public static ProgressionRevisionNotification Deserialize(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var r = new SnapshotReader(text);
            return new ProgressionRevisionNotification(
                new StoneId(r.GetString("stone")),
                r.GetString("sub"),
                (ProgressionCommandType)r.GetInt("cmd"),
                r.GetLong("srev"),
                r.GetLong("crev"),
                r.GetLong("prev"),
                r.GetString("rc"),
                r.GetLong("seq"));
        }
    }

    /// <summary>The server-side publication authority: tracks which accounts are watching which Stone and
    /// stamps each published event with that subscriber's monotonic sequence. It holds NO gameplay state —
    /// only subscriptions and delivery counters — so it can never become a second source of truth.</summary>
    public sealed class ProgressionNotificationHub
    {
        private readonly Dictionary<string, HashSet<string>> _subscribers =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _sequence =
            new Dictionary<string, long>(StringComparer.Ordinal);

        private static string Key(StoneId stone, string account) => stone.Value + "|" + account;

        /// <summary>Register an account as watching one Stone's read model. Idempotent.</summary>
        public void Subscribe(StoneId stone, AccountId account)
        {
            if (string.IsNullOrEmpty(account.Value)) return;
            if (!_subscribers.TryGetValue(stone.Value, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                _subscribers[stone.Value] = set;
            }
            set.Add(account.Value);
        }

        /// <summary>Stop watching (relog, panel close, disconnect). After this, the account receives no
        /// further events for that Stone.</summary>
        public void Unsubscribe(StoneId stone, AccountId account)
        {
            if (_subscribers.TryGetValue(stone.Value, out var set))
                set.Remove(account.Value);
        }

        public IReadOnlyCollection<string> Subscribers(StoneId stone) =>
            _subscribers.TryGetValue(stone.Value, out var set)
                ? (IReadOnlyCollection<string>)new List<string>(set)
                : Array.Empty<string>();

        public long CurrentSequence(StoneId stone, AccountId account) =>
            _sequence.TryGetValue(Key(stone, account.Value), out var s) ? s : 0;

        /// <summary>Publish ONE committed operation's bounded event to every current subscriber of that
        /// Stone, each stamped with its own next monotonic sequence. Returns the per-subscriber copies for
        /// the transport to deliver. Publishing to zero subscribers is a no-op — there is no broadcast.</summary>
        public IReadOnlyList<ProgressionRevisionNotification> Publish(in ProgressionRevisionNotification notification)
        {
            var stone = notification.StoneId;
            if (!_subscribers.TryGetValue(stone.Value, out var set) || set.Count == 0)
                return Array.Empty<ProgressionRevisionNotification>();

            var delivered = new List<ProgressionRevisionNotification>(set.Count);
            foreach (var account in set)
            {
                string key = Key(stone, account);
                long next = (_sequence.TryGetValue(key, out var current) ? current : 0) + 1;
                _sequence[key] = next;
                delivered.Add(notification.ForSubscriber(account, next));
            }
            return delivered;
        }
    }

    /// <summary>The CLIENT-side bounded consumer: holds the revisions of the read model it last fetched
    /// for one Stone and decides, per notification, whether it must REFETCH. It never applies a
    /// notification as data — a notification carries no data to apply.</summary>
    public sealed class ProgressionRevisionCache
    {
        private readonly Dictionary<string, Held> _byStone = new Dictionary<string, Held>(StringComparer.Ordinal);

        private readonly struct Held
        {
            public Held(long sequence, long stoneRevision, long characterRevision, long policyRevision)
            {
                Sequence = sequence;
                StoneRevision = stoneRevision;
                CharacterRevision = characterRevision;
                PolicyRevision = policyRevision;
            }

            public long Sequence { get; }
            public long StoneRevision { get; }
            public long CharacterRevision { get; }
            public long PolicyRevision { get; }
        }

        /// <summary>Record the revisions of a read model the client just FETCHED. This is the only way the
        /// cache learns authoritative revisions — never from a notification.</summary>
        public void RecordFetched(StoneId stone, long stoneRevision, long characterRevision,
            long policyRevision, long sequence = 0)
        {
            _byStone[stone.Value] = new Held(sequence, stoneRevision, characterRevision, policyRevision);
        }

        /// <summary>Decide whether the client must refetch. It refetches when it holds nothing for the
        /// Stone, when the notification's sequence is ahead of what it holds, or when ANY reported revision
        /// differs from what it fetched. A stale/duplicate/reordered event whose sequence is not ahead and
        /// whose revisions match is dropped.</summary>
        public bool ShouldRefetch(in ProgressionRevisionNotification notification)
        {
            if (!_byStone.TryGetValue(notification.StoneId.Value, out var held)) return true;
            if (notification.Sequence > held.Sequence) return true;
            if (notification.StoneRevision != held.StoneRevision) return true;
            if (notification.CharacterRevision != held.CharacterRevision) return true;
            if (notification.PolicyRevision != held.PolicyRevision) return true;
            return false;
        }

        /// <summary>Forget the held revisions for a Stone (relog / panel close). The next notification then
        /// forces a refetch — fail toward fetching, never toward stale.</summary>
        public void Invalidate(StoneId stone) => _byStone.Remove(stone.Value);
    }
}
