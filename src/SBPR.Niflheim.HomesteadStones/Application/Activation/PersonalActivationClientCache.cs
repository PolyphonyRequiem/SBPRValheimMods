using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Application.Activation
{
    // T026 remediation — the CLIENT-SIDE bounded consumer of the personal Character-Effect delivery
    // channel, mirroring the accepted LocalActivationClientCache. This is the read model a joined client
    // holds and the Field Fletching recipe gate reads to decide whether the personal effect is currently
    // active for the local player.
    //
    // It holds AT MOST one snapshot per (Stone, occupant, character) (the latest one it has applied) and
    // answers active questions purely from it. It NEVER derives activation itself — the client holds none of
    // the authoritative inputs (Stone aggregate, character purchases, authority index). It only:
    //   * applies a snapshot IFF the snapshot's sequence >= the last applied sequence (drop stale/reorder),
    //   * decides from a notification whether it must REFETCH (notification sequence ahead of what it holds,
    //     OR revisions changed), returning that decision to the transport,
    //   * fails closed: an unknown caller, or a denied snapshot, delivers nothing.
    //
    // net48 audit: engine-free (value objects + dictionaries). Link-compiles into the net8 test project.

    public sealed class PersonalActivationClientCache
    {
        private readonly Dictionary<string, PersonalActivationSnapshot> _byCaller =
            new Dictionary<string, PersonalActivationSnapshot>(StringComparer.Ordinal);

        private static string Key(StoneId stone, AccountId occupant, CharacterId character) =>
            stone.Value + "|" + occupant.Value + "|" + character.Value;

        /// <summary>The snapshot currently held for one caller, or null when none applied.</summary>
        public PersonalActivationSnapshot? Current(StoneId stone, AccountId occupant, CharacterId character) =>
            _byCaller.TryGetValue(Key(stone, occupant, character), out var s) ? s : null;

        /// <summary>Apply a fetched/pushed snapshot. Returns true when it was applied (it is at least as new
        /// as what we hold), false when it was DROPPED as stale/reordered. A snapshot with an OLDER sequence
        /// than the one we hold is never applied — delivery cannot roll backward on reorder.</summary>
        public bool Apply(PersonalActivationSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            string key = Key(snapshot.StoneId, snapshot.Occupant, snapshot.Character);
            if (_byCaller.TryGetValue(key, out var current) && current.Sequence > snapshot.Sequence)
                return false; // stale/reordered — keep the newer one we already hold.
            _byCaller[key] = snapshot;
            return true;
        }

        /// <summary>Decide, on receiving a bounded notification, whether the client must REFETCH the read
        /// model. It refetches when the notification is for a caller it does not hold, when the notification's
        /// sequence is ahead of the held snapshot, or when the notification reports different authoritative
        /// revisions than the held snapshot (a committed mutation the client hasn't seen). The notification is
        /// NEVER applied as authority — it only triggers a refetch decision.</summary>
        public bool ShouldRefetch(in PersonalActivationNotification notification)
        {
            var current = Current(notification.StoneId, notification.Occupant, notification.Character);
            if (current == null) return true;
            if (notification.Sequence > current.Sequence) return true;
            if (notification.StoneRevision != current.StoneRevision) return true;
            if (notification.CharacterRevision != current.CharacterRevision) return true;
            if (notification.AuthorityRevision != current.AuthorityRevision) return true;
            return false;
        }

        /// <summary>Whether the personal Character Effect for <paramref name="node"/> is currently active for
        /// the given caller, per the held snapshot. Fail closed: no held snapshot (or a denied one) ⇒
        /// false.</summary>
        public bool IsActive(StoneId stone, AccountId occupant, CharacterId character, VersionedId node)
        {
            var current = Current(stone, occupant, character);
            return current != null && current.IsActive(node);
        }

        /// <summary>Whether the personal Character Effect for <paramref name="node"/> is active for the LOCAL
        /// occupant at <paramref name="stone"/>, WITHOUT the caller having to name the account/character. A
        /// joined client only ever receives snapshots the server stamped for ITSELF (the transport replies
        /// with the requesting peer's own bound principal), so every snapshot the cache holds for a Stone
        /// belongs to the local player. The Field Fletching recipe gate runs client-side and knows the Stone
        /// it stands in but not its server-derived (AccountId, CharacterId) (those are HMAC/ZDO facts the
        /// server owns), so it queries by Stone alone. Fail closed: no held snapshot for the Stone, a denied
        /// snapshot, or an inactive row ⇒ false.</summary>
        public bool IsActiveForStone(StoneId stone, VersionedId node)
        {
            foreach (var kv in _byCaller)
            {
                var snap = kv.Value;
                if (!snap.StoneId.Equals(stone)) continue;
                if (snap.IsActive(node)) return true;
            }
            return false;
        }

        /// <summary>Explicitly invalidate the held snapshot for one caller — e.g. on relog, disconnect, or
        /// relationship loss before a fresh fetch arrives. After this, IsActive fails closed until a new
        /// snapshot is applied.</summary>
        public void Invalidate(StoneId stone, AccountId occupant, CharacterId character) =>
            _byCaller.Remove(Key(stone, occupant, character));

        /// <summary>Drop every held snapshot — e.g. on ZNet teardown / disconnect. After this the cache fails
        /// closed for every caller until fresh snapshots are applied.</summary>
        public void Clear() => _byCaller.Clear();
    }
}
