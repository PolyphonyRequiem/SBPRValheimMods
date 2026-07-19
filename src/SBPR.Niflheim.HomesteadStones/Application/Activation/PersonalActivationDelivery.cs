using System;
using System.Collections.Generic;
using System.Globalization;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Application.Activation
{
    // T026 remediation — the BOUNDED server→client PERSONAL Character-Effect delivery contract. This is
    // the missing channel the T026 review (PR #373) identified: the sibling Local Effect channel
    // (LocalActivationDelivery/Service/ClientCache) carries only Stone-owned LOCAL nodes projected through
    // LocalEffectActivationView, so a personal Character Effect (Field Fletching I) had no server→client
    // read model and a pure joined client always failed closed. This file defines the two engine-free wire
    // value objects the personal delivery seam carries, mirroring the accepted Local delivery shapes but
    // projecting the PERSONAL activation view (DerivedActivationView: purchase record AND active
    // relationship, per-character, no second active-effects ledger — AT-NO-ACTIVE-LEDGER):
    //
    //   * PersonalActivationSnapshot   — the per-(occupant, character) READ MODEL a client fetches. A pure
    //     projection of DerivedActivationView plus the authoritative Stone/character/authority revisions and
    //     a monotonic delivery sequence. It carries ONLY derived developed/offered/purchased/active status
    //     per personal node — never a mutable active-effects ledger, never another account's data.
    //
    //   * PersonalActivationNotification — the BOUNDED invalidation event published after a committed
    //     operation (or an observed relationship change). It carries stable IDs (Stone + occupant +
    //     character), the new revisions, a monotonic sequence, and a result code — NOT the whole read
    //     model. A client that misses or reorders notifications refetches the current snapshot.
    //
    // Fail-closed is the default: PersonalActivationSnapshot.Denied(...) is an EMPTY, all-inactive snapshot
    // returned when authority is missing/stale so the client delivers no effect rather than a stale one.
    // Clients never author activation — these types have no server-mutating surface.
    //
    // Ownership semantics preserved: unlike the Local channel, a personal Character Effect is NOT gated by
    // the Settlement Local policy, occupancy, or governor presence — its active/dormant status is purely
    // (purchase record AND active relationship to this Stone), re-derived every snapshot from the durable
    // aggregates. Relationship loss / disconnect / dormancy flip Active to false with zero writes.
    //
    // net48 audit: engine-free (System.* + snapshot codec + engine-free domain types). Link-compiles into
    // the net8 test project.

    /// <summary>One personal Character-Effect row on the wire: the node identity and the derived
    /// developed/offered/purchased/active status for THIS (occupant, character) at this Stone. A pure
    /// projection of <see cref="Domain.Activation.DerivedNodeStatus"/>; carries no mutable authority.</summary>
    public readonly struct PersonalActivationRow
    {
        public PersonalActivationRow(VersionedId node, bool developed, bool offered, bool purchased, bool active)
        {
            Node = node;
            Developed = developed;
            Offered = offered;
            Purchased = purchased;
            Active = active;
        }

        public VersionedId Node { get; }
        public bool Developed { get; }
        public bool Offered { get; }
        public bool Purchased { get; }

        /// <summary>The personal effect is currently delivered to this character: they hold a purchase record
        /// for the node at this Stone AND an active relationship to this Stone. Pure derivation.</summary>
        public bool Active { get; }
    }

    /// <summary>The per-(occupant, character) personal Character-Effect READ MODEL delivered to a client.
    /// Immutable, bounded, and a pure projection of the authoritative Stone + character + authority
    /// aggregates (via <see cref="Domain.Activation.DerivedActivationView"/>). It carries the durable
    /// revisions plus a monotonic delivery <see cref="Sequence"/> so a client can drop a stale/reordered
    /// notification and refetch. Clients never author it (no server-mutating surface).</summary>
    public sealed class PersonalActivationSnapshot
    {
        private readonly List<PersonalActivationRow> _rows;
        private readonly Dictionary<string, PersonalActivationRow> _byNodeKey;

        public PersonalActivationSnapshot(
            StoneId stoneId,
            AccountId occupant,
            CharacterId character,
            long sequence,
            long stoneRevision,
            long characterRevision,
            long authorityRevision,
            bool authorityPresent,
            IReadOnlyList<PersonalActivationRow> rows)
        {
            if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            StoneId = stoneId;
            Occupant = occupant;
            Character = character;
            Sequence = sequence;
            StoneRevision = stoneRevision;
            CharacterRevision = characterRevision;
            AuthorityRevision = authorityRevision;
            AuthorityPresent = authorityPresent;
            _rows = new List<PersonalActivationRow>(rows ?? Array.Empty<PersonalActivationRow>());
            _byNodeKey = new Dictionary<string, PersonalActivationRow>(StringComparer.Ordinal);
            foreach (var r in _rows) _byNodeKey[r.Node.Key] = r;
        }

        public StoneId StoneId { get; }
        public AccountId Occupant { get; }
        public CharacterId Character { get; }

        /// <summary>Monotonic per-(occupant, character) delivery sequence. Higher = newer. A client applies a
        /// snapshot only when its sequence is at least the last one it holds, so a reordered late fetch
        /// cannot roll delivery backward.</summary>
        public long Sequence { get; }

        public long StoneRevision { get; }
        public long CharacterRevision { get; }
        public long AuthorityRevision { get; }

        /// <summary>False when the server could not resolve authoritative state for this caller (missing/stale
        /// Stone/character/authority). A denied snapshot is empty and all-inactive — fail closed.</summary>
        public bool AuthorityPresent { get; }

        public IReadOnlyList<PersonalActivationRow> Rows => _rows;

        /// <summary>The delivered row for one personal node key, or an inactive default when the node is not
        /// a derived personal node in this snapshot. A caller that reads a non-present node gets Active=false,
        /// so an unknown node can never deliver an effect.</summary>
        public PersonalActivationRow RowFor(VersionedId node) =>
            _byNodeKey.TryGetValue(node.Key, out var r)
                ? r
                : new PersonalActivationRow(node, false, false, false, false);

        /// <summary>Whether the personal Character Effect for <paramref name="node"/> is currently delivered
        /// to this caller. Pure read of the derived snapshot — no gate is re-evaluated here. Fail closed:
        /// a denied (authority-absent) snapshot delivers nothing regardless of any row.</summary>
        public bool IsActive(VersionedId node) => AuthorityPresent && RowFor(node).Active;

        /// <summary>Whether the caller OWNS <paramref name="node"/> — a durable PERMANENT-Effect question
        /// (T027 Fletcher's Habit): the node is developed on the Stone AND the caller holds a purchase
        /// record, INDEPENDENT of the currently-active relationship. Unlike <see cref="IsActive"/>, a
        /// relationship loss does NOT revoke ownership (spec line 130 "Permanent Effects remain active"; line
        /// 260 "A released character retains Permanent Effects"). Fail closed: a denied (authority-absent)
        /// snapshot owns nothing.</summary>
        public bool IsOwned(VersionedId node)
        {
            if (!AuthorityPresent) return false;
            var row = RowFor(node);
            return row.Developed && row.Purchased;
        }

        /// <summary>A fail-closed EMPTY snapshot for a caller the server cannot authoritatively resolve
        /// (missing/stale Stone/character/authority). All effects inactive; AuthorityPresent=false. The
        /// client delivers nothing.</summary>
        public static PersonalActivationSnapshot Denied(StoneId stoneId, AccountId occupant,
            CharacterId character, long sequence) =>
            new PersonalActivationSnapshot(stoneId, occupant, character, sequence, 0, 0, 0,
                authorityPresent: false, Array.Empty<PersonalActivationRow>());

        /// <summary>Deterministic wire serialization (stable field order, ordinal). Used by the net48 RPC
        /// glue and asserted for equality by the recovery/reorder tests.</summary>
        public string Serialize()
        {
            var w = new SnapshotWriter()
                .Put("stone", StoneId.Value)
                .Put("occ", Occupant.Value)
                .Put("chr", Character.Value)
                .PutLong("seq", Sequence)
                .PutLong("srev", StoneRevision)
                .PutLong("crev", CharacterRevision)
                .PutLong("arev", AuthorityRevision)
                .PutInt("auth", AuthorityPresent ? 1 : 0)
                .PutInt("n", _rows.Count);
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                w.Put("nk" + i.ToString(CultureInfo.InvariantCulture), r.Node.Key)
                 .PutInt("nv" + i.ToString(CultureInfo.InvariantCulture), r.Node.Version)
                 .PutInt("d" + i.ToString(CultureInfo.InvariantCulture), r.Developed ? 1 : 0)
                 .PutInt("o" + i.ToString(CultureInfo.InvariantCulture), r.Offered ? 1 : 0)
                 .PutInt("p" + i.ToString(CultureInfo.InvariantCulture), r.Purchased ? 1 : 0)
                 .PutInt("ac" + i.ToString(CultureInfo.InvariantCulture), r.Active ? 1 : 0);
            }
            return w.Build();
        }

        public static PersonalActivationSnapshot Deserialize(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var r = new SnapshotReader(text);
            int count = r.GetInt("n");
            var rows = new List<PersonalActivationRow>(count);
            for (int i = 0; i < count; i++)
            {
                var node = new VersionedId(
                    r.GetString("nk" + i.ToString(CultureInfo.InvariantCulture)),
                    r.GetInt("nv" + i.ToString(CultureInfo.InvariantCulture)));
                rows.Add(new PersonalActivationRow(node,
                    r.GetInt("d" + i.ToString(CultureInfo.InvariantCulture)) == 1,
                    r.GetInt("o" + i.ToString(CultureInfo.InvariantCulture)) == 1,
                    r.GetInt("p" + i.ToString(CultureInfo.InvariantCulture)) == 1,
                    r.GetInt("ac" + i.ToString(CultureInfo.InvariantCulture)) == 1));
            }
            return new PersonalActivationSnapshot(
                new StoneId(r.GetString("stone")),
                new AccountId(r.GetString("occ")),
                new CharacterId(r.GetString("chr")),
                r.GetLong("seq"),
                r.GetLong("srev"),
                r.GetLong("crev"),
                r.GetLong("arev"),
                r.GetInt("auth") == 1,
                rows);
        }
    }

    /// <summary>The BOUNDED invalidation event published after a committed operation or an observed
    /// relationship change. It carries stable IDs (Stone + occupant + character), the new authoritative
    /// revisions, a monotonic sequence, and a result code — never the full read model. A client applies it
    /// only to decide whether to REFETCH the snapshot; it is never authority for delivery on its own.</summary>
    public readonly struct PersonalActivationNotification
    {
        public PersonalActivationNotification(StoneId stoneId, AccountId occupant, CharacterId character,
            long sequence, long stoneRevision, long characterRevision, long authorityRevision, string resultCode)
        {
            if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            StoneId = stoneId;
            Occupant = occupant;
            Character = character;
            Sequence = sequence;
            StoneRevision = stoneRevision;
            CharacterRevision = characterRevision;
            AuthorityRevision = authorityRevision;
            ResultCode = resultCode ?? string.Empty;
        }

        public StoneId StoneId { get; }
        public AccountId Occupant { get; }
        public CharacterId Character { get; }
        public long Sequence { get; }
        public long StoneRevision { get; }
        public long CharacterRevision { get; }
        public long AuthorityRevision { get; }
        public string ResultCode { get; }

        public string Serialize() => new SnapshotWriter()
            .Put("stone", StoneId.Value)
            .Put("occ", Occupant.Value)
            .Put("chr", Character.Value)
            .PutLong("seq", Sequence)
            .PutLong("srev", StoneRevision)
            .PutLong("crev", CharacterRevision)
            .PutLong("arev", AuthorityRevision)
            .Put("rc", ResultCode)
            .Build();

        public static PersonalActivationNotification Deserialize(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var r = new SnapshotReader(text);
            return new PersonalActivationNotification(
                new StoneId(r.GetString("stone")),
                new AccountId(r.GetString("occ")),
                new CharacterId(r.GetString("chr")),
                r.GetLong("seq"),
                r.GetLong("srev"),
                r.GetLong("crev"),
                r.GetLong("arev"),
                r.GetString("rc"));
        }
    }

    /// <summary>The pair a Publish returns: the fresh per-(occupant, character) read model and the bounded
    /// notification that shares its sequence. The transport delivers the notification (small) and lets the
    /// client refetch the snapshot; a listen-host consumer can also apply the snapshot directly.</summary>
    public readonly struct PersonalActivationDelivery
    {
        public PersonalActivationDelivery(PersonalActivationSnapshot snapshot,
            PersonalActivationNotification notification)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Notification = notification;
        }

        public PersonalActivationSnapshot Snapshot { get; }
        public PersonalActivationNotification Notification { get; }
    }
}
