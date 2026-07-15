using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.Identity
{
    // Aggregate 2 — AccountStoneAuthorityIndex (data-model.md §"Aggregate 2"). One server-owned
    // authority record per (AccountId, StoneId). This index owns NO gameplay balance or outcome — it
    // is pure authority: which character(s) on this account actively hold a relationship to this
    // Stone, and the provenance of each activation/release.
    //
    // T007 design call (2026-07-15, "multi-active Community index"): the index is a single
    // authoritative account–Stone active-character RESERVATION index that can hold MULTIPLE character
    // entries, governed by variant-authored cardinality policy:
    //   * Homestead: at most one sibling character may hold either active Bond or active Attunement.
    //   * Community Attunement: multiple sibling characters on the same account may be simultaneously
    //     active; each remains represented in this authoritative index and in derived activation.
    //   * Community Bond: remains account-exclusive / single-active.
    // Community activity is derived ONLY from this index — never through a second authority path.
    // Each reservation identifies the character + relationship kind/id + activation provenance needed
    // for release/recovery; release removes ONLY that character's reservation.
    //
    // net48 audit: engine-free value objects + snapshot codec. Link-compiles into net8 tests.

    public enum RelationshipKind
    {
        None = 0,
        Bond = 1,
        Attunement = 2
    }

    /// <summary>One active reservation held by exactly one character at this (account, Stone). Carries
    /// the identity + provenance needed to release/recover exactly this character's reservation.</summary>
    public sealed class AuthorityReservation
    {
        public AuthorityReservation(CharacterId character, RelationshipKind kind,
            string relationshipId, string activationReceiptId)
        {
            Character = character;
            Kind = kind;
            RelationshipId = relationshipId ?? string.Empty;
            ActivationReceiptId = activationReceiptId ?? string.Empty;
        }

        public CharacterId Character { get; }
        public RelationshipKind Kind { get; }
        public string RelationshipId { get; }
        public string ActivationReceiptId { get; }

        public string Serialize() => new SnapshotWriter()
            .Put("char", Character.Value)
            .PutInt("kind", (int)Kind)
            .Put("relId", RelationshipId)
            .Put("actReceipt", ActivationReceiptId)
            .Build();

        public static AuthorityReservation Deserialize(string s)
        {
            var r = new SnapshotReader(s);
            return new AuthorityReservation(
                new CharacterId(r.GetString("char")),
                (RelationshipKind)r.GetInt("kind"),
                r.GetString("relId"),
                r.GetString("actReceipt"));
        }
    }

    public sealed class AccountStoneAuthorityIndex
    {
        public const int CurrentSchemaVersion = 2;

        private readonly List<AuthorityReservation> _reservations;

        public AccountStoneAuthorityIndex(
            AccountId account,
            StoneId stoneId,
            long revision,
            IReadOnlyList<AuthorityReservation>? reservations,
            string lastReleaseReceiptId,
            int schemaVersion = CurrentSchemaVersion)
        {
            Account = account;
            StoneId = stoneId;
            Revision = revision;
            _reservations = reservations != null
                ? new List<AuthorityReservation>(reservations)
                : new List<AuthorityReservation>();
            LastReleaseReceiptId = lastReleaseReceiptId ?? string.Empty;
            SchemaVersion = schemaVersion;
        }

        public int SchemaVersion { get; }
        public AccountId Account { get; }
        public StoneId StoneId { get; }
        public long Revision { get; }

        /// <summary>All active reservations at this (account, Stone). A released relationship removes
        /// only that character's reservation; retained purchases/Permanent Effects do not appear here.</summary>
        public IReadOnlyList<AuthorityReservation> Reservations => _reservations;

        /// <summary>Receipt provenance of the most recent release applied to this index.</summary>
        public string LastReleaseReceiptId { get; }

        /// <summary>Vacant when no character holds an active reservation.</summary>
        public bool IsVacant => _reservations.Count == 0;

        /// <summary>The active reservation this character holds here, or null.</summary>
        public AuthorityReservation? ReservationFor(CharacterId character)
        {
            foreach (var res in _reservations)
                if (res.Character.Equals(character)) return res;
            return null;
        }

        /// <summary>The reservation matching this relationship id, or null.</summary>
        public AuthorityReservation? ReservationByRelationship(string relationshipId)
        {
            foreach (var res in _reservations)
                if (string.Equals(res.RelationshipId, relationshipId, StringComparison.Ordinal)) return res;
            return null;
        }

        /// <summary>True when this character currently holds any active reservation here.</summary>
        public bool HasActive(CharacterId character) => ReservationFor(character) != null;

        /// <summary>True when a DIFFERENT character on this account holds any active reservation here.</summary>
        public bool HasSiblingOtherThan(CharacterId character)
        {
            foreach (var res in _reservations)
                if (!res.Character.Equals(character)) return true;
            return false;
        }

        /// <summary>True when a DIFFERENT character holds an active Bond here (Community Bond exclusivity).</summary>
        public bool HasSiblingBondOtherThan(CharacterId character)
        {
            foreach (var res in _reservations)
                if (!res.Character.Equals(character) && res.Kind == RelationshipKind.Bond) return true;
            return false;
        }

        /// <summary>Produce a new index with <paramref name="reservation"/> added, revision incremented.</summary>
        public AccountStoneAuthorityIndex WithReservationAdded(AuthorityReservation reservation, long newRevision)
        {
            var next = new List<AuthorityReservation>(_reservations.Count + 1);
            next.AddRange(_reservations);
            next.Add(reservation);
            return new AccountStoneAuthorityIndex(Account, StoneId, newRevision, next, LastReleaseReceiptId, SchemaVersion);
        }

        /// <summary>Produce a new index with the reservation matching <paramref name="relationshipId"/>
        /// removed, stamping the release receipt, revision incremented. Removes ONLY that reservation.</summary>
        public AccountStoneAuthorityIndex WithReservationReleased(string relationshipId, string releaseReceiptId, long newRevision)
        {
            var next = new List<AuthorityReservation>(_reservations.Count);
            foreach (var res in _reservations)
                if (!string.Equals(res.RelationshipId, relationshipId, StringComparison.Ordinal))
                    next.Add(res);
            return new AccountStoneAuthorityIndex(Account, StoneId, newRevision, next,
                releaseReceiptId ?? string.Empty, SchemaVersion);
        }

        public string Serialize() => new SnapshotWriter()
            .PutInt("schema", SchemaVersion)
            .Put("account", Account.Value)
            .Put("stoneId", StoneId.Value)
            .PutLong("revision", Revision)
            .PutList("reservations", _reservations, x => x.Serialize())
            .Put("lastRelease", LastReleaseReceiptId)
            .Build();

        public static AccountStoneAuthorityIndex Deserialize(string s)
        {
            var r = new SnapshotReader(s);
            return new AccountStoneAuthorityIndex(
                new AccountId(r.GetString("account")),
                new StoneId(r.GetString("stoneId")),
                r.GetLong("revision"),
                r.GetList("reservations", AuthorityReservation.Deserialize),
                r.HasKey("lastRelease") ? r.GetString("lastRelease") : string.Empty,
                r.GetInt("schema"));
        }

        /// <summary>A fresh vacant index at revision 0 for (account, Stone). Used as the baseline when no
        /// authority row is stored, so sibling-exclusivity checks always have a stable starting point.</summary>
        public static AccountStoneAuthorityIndex Vacant(AccountId account, StoneId stoneId) =>
            new AccountStoneAuthorityIndex(account, stoneId, 0, null, string.Empty);

        public bool StructurallyEquals(AccountStoneAuthorityIndex o)
        {
            if (o == null) return false;
            if (!(SchemaVersion == o.SchemaVersion
                  && Account.Equals(o.Account)
                  && StoneId.Equals(o.StoneId)
                  && Revision == o.Revision
                  && string.Equals(LastReleaseReceiptId, o.LastReleaseReceiptId, StringComparison.Ordinal)
                  && _reservations.Count == o._reservations.Count))
                return false;
            for (int i = 0; i < _reservations.Count; i++)
                if (!string.Equals(_reservations[i].Serialize(), o._reservations[i].Serialize(), StringComparison.Ordinal))
                    return false;
            return true;
        }
    }
}
