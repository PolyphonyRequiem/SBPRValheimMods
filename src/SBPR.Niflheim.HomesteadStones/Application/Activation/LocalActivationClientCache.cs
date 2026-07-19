using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Application.Activation
{
    // T016 shared runtime substrate — the CLIENT-SIDE bounded consumer of the Local Effect delivery
    // channel (contracts.md §"Notification contract": "Clients that miss or reorder notifications fetch
    // the current read model"). This is the read model a joined client holds and the gameplay-family
    // consumers (Refined station-level patch, Savor timer, Practice/T.W.I.G. placement) will read to
    // decide whether an effect is currently active for the local player.
    //
    // It holds AT MOST one snapshot per Stone (the latest one it has applied) and answers
    // active/placement questions purely from it. It NEVER derives activation itself — the client holds
    // none of the authoritative inputs (Stone aggregate, governance, policy). It only:
    //   * applies a snapshot IFF the snapshot's sequence >= the last applied sequence (drop stale/reorder),
    //   * decides from a notification whether it must REFETCH (notification sequence ahead of what it holds,
    //     OR revisions changed), returning that decision to the transport,
    //   * fails closed: an unknown Stone, or a denied snapshot, delivers nothing.
    //
    // net48 audit: engine-free (value objects + dictionaries). Link-compiles into the net8 test project.

    public sealed class LocalActivationClientCache
    {
        private readonly Dictionary<string, LocalActivationSnapshot> _byStone =
            new Dictionary<string, LocalActivationSnapshot>(StringComparer.Ordinal);

        private static string Key(StoneId stone, AccountId occupant) => stone.Value + "|" + occupant.Value;

        /// <summary>The snapshot currently held for one (Stone, occupant), or null when none applied.</summary>
        public LocalActivationSnapshot? Current(StoneId stone, AccountId occupant) =>
            _byStone.TryGetValue(Key(stone, occupant), out var s) ? s : null;

        /// <summary>Apply a fetched/pushed snapshot. Returns true when it was applied (it is at least as new
        /// as what we hold), false when it was DROPPED as stale/reordered. A snapshot with an OLDER
        /// sequence than the one we hold is never applied — delivery cannot roll backward on reorder.</summary>
        public bool Apply(LocalActivationSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            string key = Key(snapshot.StoneId, snapshot.Occupant);
            if (_byStone.TryGetValue(key, out var current) && current.Sequence > snapshot.Sequence)
                return false; // stale/reordered — keep the newer one we already hold.
            _byStone[key] = snapshot;
            return true;
        }

        /// <summary>Decide, on receiving a bounded notification, whether the client must REFETCH the read
        /// model. It refetches when the notification is for a Stone/occupant it does not hold, when the
        /// notification's sequence is ahead of the held snapshot, or when the notification reports different
        /// authoritative revisions than the held snapshot (a committed mutation the client hasn't seen).
        /// The notification is NEVER applied as authority — it only triggers a refetch decision.</summary>
        public bool ShouldRefetch(in LocalActivationNotification notification)
        {
            var current = Current(notification.StoneId, notification.Occupant);
            if (current == null) return true;
            if (notification.Sequence > current.Sequence) return true;
            if (notification.StoneRevision != current.StoneRevision) return true;
            if (notification.PolicyRevision != current.PolicyRevision) return true;
            return false;
        }

        /// <summary>Whether the Local Effect for <paramref name="node"/> is currently active for the local
        /// occupant, per the held snapshot. Fail closed: no held snapshot (or a denied one) ⇒ false.</summary>
        public bool IsActive(StoneId stone, AccountId occupant, VersionedId node)
        {
            var current = Current(stone, occupant);
            return current != null && current.AuthorityPresent && current.IsActive(node);
        }

        /// <summary>Whether the Local Effect for <paramref name="node"/> is active for the LOCAL occupant at
        /// <paramref name="stone"/>, WITHOUT the caller having to name the account. A joined client only ever
        /// receives snapshots the server stamped for ITSELF (the transport replies with the requesting peer's
        /// own occupant), so every snapshot the cache holds for a Stone belongs to the local player. The
        /// gameplay-family consumers (the Refined Workshop station-level patch) run client-side and know the
        /// Stone they stand in but not their server-derived <see cref="AccountId"/> (that is an HMAC the
        /// server owns), so they query by Stone alone. Fail closed: no held snapshot for the Stone, a denied
        /// snapshot, or an inactive row ⇒ false.</summary>
        public bool IsActiveForStone(StoneId stone, VersionedId node)
        {
            foreach (var kv in _byStone)
            {
                var snap = kv.Value;
                if (!snap.StoneId.Equals(stone)) continue;
                if (snap.AuthorityPresent && snap.IsActive(node)) return true;
            }
            return false;
        }

        /// <summary>Whether the local occupant may exercise a Local PLACEMENT capability for
        /// <paramref name="node"/>: the held snapshot must have the effect active AND the caller must
        /// independently pass ordinary build Permission (spec FR-016 final sentence). Fail closed.</summary>
        public bool CanExercisePlacement(StoneId stone, AccountId occupant, VersionedId node,
            bool hasOrdinaryBuildPermission)
        {
            var current = Current(stone, occupant);
            return current != null && current.AuthorityPresent
                && current.CanExercisePlacement(node, hasOrdinaryBuildPermission);
        }

        /// <summary>Whether ANY held snapshot currently grants the local occupant a Local PLACEMENT
        /// capability for <paramref name="node"/>, ANDed with ordinary build Permission. A client only ever
        /// holds snapshots the authoritative server derived for ITS OWN occupant (the delivery channel
        /// resolves the occupant server-side and never sends another account's read model), and the server
        /// marks a Local Effect active only when it confirmed the occupant stands inside that Stone's Area.
        /// So a held snapshot with the node active is proof the server authoritatively placed this client
        /// inside an entitled Stone — the exact authoritative projection the pure-client placement gate
        /// consumes instead of self-deriving occupancy/policy (which it cannot). Fail closed: no held
        /// snapshot (or only denied ones) ⇒ false.</summary>
        public bool CanExercisePlacementForNode(VersionedId node, bool hasOrdinaryBuildPermission)
        {
            foreach (var snapshot in _byStone.Values)
                if (snapshot.AuthorityPresent && snapshot.CanExercisePlacement(node, hasOrdinaryBuildPermission))
                    return true;
            return false;
        }

        /// <summary>Explicitly invalidate the held snapshot for one (Stone, occupant) — e.g. on relog or
        /// area exit before a fresh fetch arrives. After this, IsActive fails closed until a new snapshot
        /// is applied.</summary>
        public void Invalidate(StoneId stone, AccountId occupant) =>
            _byStone.Remove(Key(stone, occupant));
    }
}
