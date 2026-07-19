using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression
{
    // T007 — recoverable Bond / Attunement lifecycle (contracts.md §"Relationship commands";
    // data-model.md §"Form relationship" / §"Release relationship"). These are the PURE domain
    // transitions: given the current character aggregate + the account–Stone authority index, they
    // validate the accepted policy and PRODUCE the next authoritative state. They never mutate in
    // place, never write a journal, and never invent an AP/BP refund, grant, or cooldown. The
    // durable, receipt-backed commit of the produced state lives in the application command layer
    // (Application/Commands/RelationshipCommands.cs).
    //
    // Load-bearing accepted policy encoded here (contracts.md / data-model.md):
    //   * Homestead sibling exclusivity: at most one character on an account may ACTIVELY hold either
    //     relationship to one Stone. A sibling attempt while a sibling is active is rejected with
    //     SiblingCharacterActive and ZERO mutation.
    //   * Variant-authored exception: Community Stone ATTUNEMENT permits sibling characters; Community
    //     Bond stays account-exclusive for now.
    //   * Sequential siblings are allowed: once the active sibling releases (index vacant), another
    //     sibling on the same account may bond/attune.
    //   * CreateBond consumes a Bond Slot and installs the authored owner/governor role + Responsibility
    //     Range; CreateAttunement consumes an Attunement Slot and grants NO cultivation authority.
    //   * Neither grants AP/BP by itself; balances/purchases/permanent outcomes are preserved verbatim.
    //   * ReleaseRelationship marks the record released and clears the active index. Character Effects
    //     go dormant purely by RE-DERIVATION once the index is vacant (DerivedActivationView), so no
    //     dormancy flag is stored and no refund/cooldown is created. A later valid Bond restores
    //     governance because the same preserved state re-derives Active.
    //
    // net48 audit: engine-free (System.Collections.Generic + value objects + the snapshot codec).
    // No net5+ surface, no UnityEngine/Valheim/BepInEx reference: link-compiles into the net8 tests.

    public enum RelationshipStatus
    {
        None = 0,
        Active = 1,
        Released = 2
    }

    /// <summary>One character-owned Bond or Attunement record at one Stone. Selected/provenance state:
    /// the record persists across release (Released status) so history and durable outcomes survive; it
    /// is never a derived "active effect" (data-model.md: "Character Effects may dormant, but their
    /// purchase records persist").</summary>
    public sealed class RelationshipRecord
    {
        public RelationshipRecord(string relationshipId, RelationshipKind kind, RelationshipStatus status,
            string responsibilityRange, string ownerGovernorRole,
            string activationProvenance, string releaseProvenance)
        {
            RelationshipId = relationshipId ?? string.Empty;
            Kind = kind;
            Status = status;
            ResponsibilityRange = responsibilityRange ?? string.Empty;
            OwnerGovernorRole = ownerGovernorRole ?? string.Empty;
            ActivationProvenance = activationProvenance ?? string.Empty;
            ReleaseProvenance = releaseProvenance ?? string.Empty;
        }

        public string RelationshipId { get; }
        public RelationshipKind Kind { get; }
        public RelationshipStatus Status { get; }

        /// <summary>Authored Responsibility Range this relationship carries (Bond only in this proof;
        /// Attunement carries no cultivation authority, so this is empty for Attunement).</summary>
        public string ResponsibilityRange { get; }

        /// <summary>Authored owner/governor role granted by a Bond; empty for an Attunement (which
        /// grants no cultivation authority — contracts.md CreateAttunement).</summary>
        public string OwnerGovernorRole { get; }

        public string ActivationProvenance { get; }
        public string ReleaseProvenance { get; }

        public bool IsActive => Status == RelationshipStatus.Active;

        public RelationshipRecord AsReleased(string releaseProvenance) =>
            new RelationshipRecord(RelationshipId, Kind, RelationshipStatus.Released,
                ResponsibilityRange, OwnerGovernorRole, ActivationProvenance,
                releaseProvenance ?? string.Empty);

        public string Serialize() => new SnapshotWriter()
            .Put("relId", RelationshipId)
            .PutInt("kind", (int)Kind)
            .PutInt("status", (int)Status)
            .Put("respRange", ResponsibilityRange)
            .Put("role", OwnerGovernorRole)
            .Put("actProv", ActivationProvenance)
            .Put("relProv", ReleaseProvenance)
            .Build();

        public static RelationshipRecord Deserialize(string s)
        {
            var r = new SnapshotReader(s);
            return new RelationshipRecord(
                r.GetString("relId"),
                (RelationshipKind)r.GetInt("kind"),
                (RelationshipStatus)r.GetInt("status"),
                r.GetString("respRange"),
                r.GetString("role"),
                r.GetString("actProv"),
                r.GetString("relProv"));
        }
    }

    /// <summary>Variant-authored relationship policy (contracts.md: "The exclusivity rule is
    /// variant-authored rather than universal"). Homestead: both Bond and Attunement are
    /// sibling-exclusive. Community: Bond stays account-exclusive, Attunement permits siblings.</summary>
    public readonly struct RelationshipPolicy
    {
        private RelationshipPolicy(bool bondSiblingExclusive, bool attunementSiblingExclusive)
        {
            BondSiblingExclusive = bondSiblingExclusive;
            AttunementSiblingExclusive = attunementSiblingExclusive;
        }

        public bool BondSiblingExclusive { get; }
        public bool AttunementSiblingExclusive { get; }

        public bool SiblingExclusiveFor(RelationshipKind kind) =>
            kind == RelationshipKind.Bond ? BondSiblingExclusive : AttunementSiblingExclusive;

        /// <summary>Derive the policy from the Stone's authored family/variant. A "Community" Stone
        /// relaxes Attunement sibling exclusivity only; everything else (Homestead) is fully exclusive.</summary>
        public static RelationshipPolicy For(string family, string variant)
        {
            bool community =
                string.Equals(variant, "Community", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(family, "Community", StringComparison.OrdinalIgnoreCase);
            return community
                ? new RelationshipPolicy(bondSiblingExclusive: true, attunementSiblingExclusive: false)
                : new RelationshipPolicy(bondSiblingExclusive: true, attunementSiblingExclusive: true);
        }
    }

    /// <summary>Result of a pure relationship transition. On rejection the returned aggregates are the
    /// UNCHANGED originals (contracts.md: "Validation completes before commit. Failure changes
    /// nothing."), so a caller that commits <see cref="Character"/>/<see cref="Authority"/>
    /// unconditionally still writes exactly the prior state on a rejection.</summary>
    public readonly struct RelationshipTransition
    {
        private RelationshipTransition(bool accepted, string resultCode,
            CharacterProgressionAggregate character, AccountStoneAuthorityIndex authority,
            string relationshipId)
        {
            Accepted = accepted;
            ResultCode = resultCode;
            Character = character;
            Authority = authority;
            RelationshipId = relationshipId ?? string.Empty;
        }

        public bool Accepted { get; }
        public string ResultCode { get; }
        public CharacterProgressionAggregate Character { get; }
        public AccountStoneAuthorityIndex Authority { get; }
        public string RelationshipId { get; }

        public static RelationshipTransition Reject(string code,
            CharacterProgressionAggregate character, AccountStoneAuthorityIndex authority) =>
            new RelationshipTransition(false, code, character, authority, string.Empty);

        public static RelationshipTransition Accept(string code,
            CharacterProgressionAggregate character, AccountStoneAuthorityIndex authority,
            string relationshipId) =>
            new RelationshipTransition(true, code, character, authority, relationshipId);
    }

    /// <summary>Pure Bond/Attunement/Release transitions over the character aggregate and the
    /// account–Stone authority index. Every method validates the accepted policy and returns the next
    /// state; none mutate their inputs.</summary>
    public static class Relationships
    {
        /// <summary>CreateBond (contracts.md). Consumes a Bond Slot, installs the authored owner/governor
        /// role + Responsibility Range, and occupies the account–Stone active index. Grants no AP/BP.</summary>
        public static RelationshipTransition CreateBond(
            CharacterProgressionAggregate character,
            AccountStoneAuthorityIndex authority,
            StoneId stoneId,
            RelationshipPolicy policy,
            string relationshipId,
            string responsibilityRange,
            string ownerGovernorRole,
            string activationProvenance,
            long? expectedCharacterRevision = null,
            long? expectedAuthorityRevision = null) =>
            Create(character, authority, stoneId, policy, RelationshipKind.Bond, relationshipId,
                responsibilityRange, ownerGovernorRole, activationProvenance,
                expectedCharacterRevision, expectedAuthorityRevision);

        /// <summary>CreateAttunement (contracts.md). Consumes an Attunement Slot, grants NO cultivation
        /// authority (empty role + empty Responsibility Range), and — for a Homestead — honours sibling
        /// exclusivity; a Community Stone permits sibling Attunement per the variant-authored policy.</summary>
        public static RelationshipTransition CreateAttunement(
            CharacterProgressionAggregate character,
            AccountStoneAuthorityIndex authority,
            StoneId stoneId,
            RelationshipPolicy policy,
            string relationshipId,
            string activationProvenance,
            long? expectedCharacterRevision = null,
            long? expectedAuthorityRevision = null) =>
            Create(character, authority, stoneId, policy, RelationshipKind.Attunement, relationshipId,
                responsibilityRange: string.Empty, ownerGovernorRole: string.Empty,
                activationProvenance: activationProvenance,
                expectedCharacterRevision: expectedCharacterRevision,
                expectedAuthorityRevision: expectedAuthorityRevision);

        private static RelationshipTransition Create(
            CharacterProgressionAggregate character,
            AccountStoneAuthorityIndex authority,
            StoneId stoneId,
            RelationshipPolicy policy,
            RelationshipKind kind,
            string relationshipId,
            string responsibilityRange,
            string ownerGovernorRole,
            string activationProvenance,
            long? expectedCharacterRevision,
            long? expectedAuthorityRevision)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (authority == null) throw new ArgumentNullException(nameof(authority));

            // The authority index is keyed by (AccountId, StoneId). A mismatched key is an invariant
            // failure, never silently tolerated (mirrors DerivedActivationView.Derive).
            if (!authority.Account.Equals(character.Account))
                return RelationshipTransition.Reject("Unauthorized", character, authority);
            if (!authority.StoneId.Equals(stoneId))
                return RelationshipTransition.Reject("StoneNotFound", character, authority);

            // Optimistic-concurrency (CAS): validate expected revisions BEFORE producing new state, so
            // a losing concurrent client changes nothing (contracts.md StaleCharacter/StaleAuthority).
            if (expectedCharacterRevision.HasValue && expectedCharacterRevision.Value != character.Revision)
                return RelationshipTransition.Reject("StaleCharacterRevision", character, authority);
            if (expectedAuthorityRevision.HasValue && expectedAuthorityRevision.Value != authority.Revision)
                return RelationshipTransition.Reject("StaleAuthorityRevision", character, authority);

            // Sibling exclusivity (policy-driven, multi-active reservation index). The account–Stone
            // index may hold MULTIPLE character reservations (design call 2026-07-15). If the kind is
            // sibling-exclusive for this variant and ANY DIFFERENT character on this account already
            // holds a reservation here, reject with no mutation. A character cannot evade the invariant
            // by holding Bond and Attunement via separate rows: the single index is the one gate for
            // both (data-model.md Aggregate 2 invariants). Community Attunement is NOT sibling-exclusive,
            // so multiple siblings may reserve simultaneously; Community Bond stays account-exclusive and
            // is blocked by any sibling reservation of any kind (policy.SiblingExclusiveFor(Bond)==true).
            if (policy.SiblingExclusiveFor(kind) && authority.HasSiblingOtherThan(character.Character))
                return RelationshipTransition.Reject("SiblingCharacterActive", character, authority);

            // The Community exception is only "multiple sibling Attunements". An existing sibling Bond
            // remains account-exclusive, so it also blocks a later Attunement. Check this direction
            // explicitly; the inverse (Attunement first, then Bond) is covered by the exclusive Bond path.
            if (kind == RelationshipKind.Attunement && authority.HasSiblingBondOtherThan(character.Character))
                return RelationshipTransition.Reject("SiblingCharacterActive", character, authority);

            // This character already actively holds a relationship here -> conflict, not a second grant.
            if (authority.HasActive(character.Character))
                return RelationshipTransition.Reject("RelationshipConflict", character, authority);

            var stoneRecord = FindStoneRecord(character, stoneId);

            // Slot capacity is the CHARACTER-WIDE relationship scarcity mechanism (FR-003; data-model.md
            // Aggregate 3 "Capacity"): count this character's ACTIVE relationships of this kind ACROSS
            // EVERY Stone against the aggregate's slot capacity, not just this Stone. Bonding a second
            // Stone with a single Bond Slot must be rejected.
            int activeOfKind = CountActiveCharacterWide(character, kind);
            int capacity = kind == RelationshipKind.Bond ? character.BondSlots : character.AttunementSlots;
            if (activeOfKind >= capacity)
                return RelationshipTransition.Reject("RelationshipCapacityExceeded", character, authority);

            // A still-Active record for this kind AT THIS STONE is a conflict (defensive; the index
            // check above normally catches it, but the record is the character-owned source of truth).
            if (CountActive(stoneRecord, kind) > 0)
                return RelationshipTransition.Reject("RelationshipConflict", character, authority);

            var newRecord = new RelationshipRecord(relationshipId, kind, RelationshipStatus.Active,
                responsibilityRange, ownerGovernorRole, activationProvenance, string.Empty);

            var newCharacter = WithRelationship(character, stoneId, stoneRecord, newRecord,
                appendOnly: true, character.Revision + 1);

            // Add this character's reservation to the multi-active index (does not disturb siblings).
            var newAuthority = authority.WithReservationAdded(
                new AuthorityReservation(character.Character, kind, relationshipId, activationProvenance),
                authority.Revision + 1);

            return RelationshipTransition.Accept("Applied", newCharacter, newAuthority, relationshipId);
        }

        /// <summary>ReleaseRelationship (contracts.md). Marks the record Released and clears the active
        /// index in one operation. Preserves AP/BP/purchases/Permanent Effects/Progression Keys
        /// verbatim; supplied Character Effects go dormant by re-derivation (vacant index), with no
        /// refund or cooldown. A later valid Bond restores eligible governance from preserved state.</summary>
        public static RelationshipTransition ReleaseRelationship(
            CharacterProgressionAggregate character,
            AccountStoneAuthorityIndex authority,
            StoneId stoneId,
            string relationshipId,
            string releaseProvenance,
            RelationshipStatus expectedStatus = RelationshipStatus.Active,
            long? expectedCharacterRevision = null,
            long? expectedAuthorityRevision = null)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (authority == null) throw new ArgumentNullException(nameof(authority));

            if (!authority.Account.Equals(character.Account))
                return RelationshipTransition.Reject("Unauthorized", character, authority);
            if (!authority.StoneId.Equals(stoneId))
                return RelationshipTransition.Reject("StoneNotFound", character, authority);

            if (expectedCharacterRevision.HasValue && expectedCharacterRevision.Value != character.Revision)
                return RelationshipTransition.Reject("StaleCharacterRevision", character, authority);
            if (expectedAuthorityRevision.HasValue && expectedAuthorityRevision.Value != authority.Revision)
                return RelationshipTransition.Reject("StaleAuthorityRevision", character, authority);

            var stoneRecord = FindStoneRecord(character, stoneId);
            RelationshipRecord? active = null;
            if (stoneRecord != null)
            {
                foreach (var rel in stoneRecord.Relationships)
                {
                    if (rel.IsActive && string.Equals(rel.RelationshipId, relationshipId, StringComparison.Ordinal))
                    {
                        active = rel;
                        break;
                    }
                }
            }

            // No active relationship of that id, or the caller does not currently hold a matching
            // reservation: there is nothing to release (contracts.md RelationshipRequired).
            if (active == null)
                return RelationshipTransition.Reject("RelationshipRequired", character, authority);
            if (expectedStatus != RelationshipStatus.Active)
                return RelationshipTransition.Reject("RelationshipConflict", character, authority);

            // This character must hold the exact reservation being released. Releasing another
            // character's reservation is never permitted (each release removes only its own entry).
            var reservation = authority.ReservationByRelationship(relationshipId);
            if (reservation == null || !reservation.Character.Equals(character.Character))
                return RelationshipTransition.Reject("RelationshipConflict", character, authority);

            var released = active.AsReleased(releaseProvenance);
            var newCharacter = WithRelationship(character, stoneId, stoneRecord, released,
                appendOnly: false, character.Revision + 1);

            // Remove ONLY this character's reservation from the multi-active index in the SAME logical
            // operation (data-model.md §"Release relationship": clear index after the mutation is
            // durably recoverable). Sibling reservations (e.g. a simultaneously active Community sibling)
            // are preserved untouched.
            var newAuthority = authority.WithReservationReleased(
                relationshipId, releaseProvenance ?? string.Empty, authority.Revision + 1);

            return RelationshipTransition.Accept("Applied", newCharacter, newAuthority, relationshipId);
        }

        private static CharacterStoneRecord? FindStoneRecord(CharacterProgressionAggregate character, StoneId stoneId)
        {
            foreach (var sr in character.StoneRecords)
                if (sr.StoneId.Equals(stoneId)) return sr;
            return null;
        }

        private static int CountActive(CharacterStoneRecord? stoneRecord, RelationshipKind kind)
        {
            if (stoneRecord == null) return 0;
            int n = 0;
            foreach (var rel in stoneRecord.Relationships)
                if (rel.IsActive && rel.Kind == kind) n++;
            return n;
        }

        /// <summary>Count this character's ACTIVE relationships of one kind across EVERY Stone. Bond and
        /// Attunement Slots are the character-wide relationship scarcity mechanism (FR-003), so slot
        /// capacity is measured against the whole aggregate, not a single Stone.</summary>
        private static int CountActiveCharacterWide(CharacterProgressionAggregate character, RelationshipKind kind)
        {
            int n = 0;
            foreach (var sr in character.StoneRecords)
                foreach (var rel in sr.Relationships)
                    if (rel.IsActive && rel.Kind == kind) n++;
            return n;
        }

        /// <summary>Produce a new character aggregate with <paramref name="record"/> installed on the
        /// Stone record for <paramref name="stoneId"/>. When <paramref name="appendOnly"/> the record is
        /// appended (create); otherwise the matching-id record is replaced in place (release). Every
        /// OTHER balance/purchase/credit field on every Stone record is preserved verbatim.</summary>
        private static CharacterProgressionAggregate WithRelationship(
            CharacterProgressionAggregate character, StoneId stoneId, CharacterStoneRecord? existing,
            RelationshipRecord record, bool appendOnly, long newRevision)
        {
            var newStoneRecords = new List<CharacterStoneRecord>(character.StoneRecords.Count + 1);
            bool replacedStone = false;

            foreach (var sr in character.StoneRecords)
            {
                if (!sr.StoneId.Equals(stoneId))
                {
                    newStoneRecords.Add(sr);
                    continue;
                }
                replacedStone = true;
                newStoneRecords.Add(RewriteRelationships(sr, record, appendOnly));
            }

            if (!replacedStone)
            {
                // No prior record for this Stone: create a clean zeroed one carrying only the new
                // relationship (create path). Release can never reach here (existing != null).
                newStoneRecords.Add(new CharacterStoneRecord(stoneId, 0, 0, 0,
                    facetCredits: null, purchases: null,
                    relationships: new[] { record }));
            }

            return new CharacterProgressionAggregate(
                character.Account, character.Character, character.WorldProductScope, newRevision,
                character.BondSlots, character.AttunementSlots, character.LastAppliedReceiptId,
                newStoneRecords, character.SchemaVersion);
        }

        private static CharacterStoneRecord RewriteRelationships(
            CharacterStoneRecord sr, RelationshipRecord record, bool appendOnly)
        {
            var rels = new List<RelationshipRecord>(sr.Relationships.Count + 1);
            bool replaced = false;
            foreach (var rel in sr.Relationships)
            {
                if (!appendOnly && string.Equals(rel.RelationshipId, record.RelationshipId, StringComparison.Ordinal)
                    && rel.Kind == record.Kind)
                {
                    rels.Add(record);
                    replaced = true;
                }
                else
                {
                    rels.Add(rel);
                }
            }
            if (!replaced) rels.Add(record);

            return new CharacterStoneRecord(sr.StoneId, sr.PersonalAp, sr.CumulativeAp, sr.PersonalBp,
                sr.FacetCredits, sr.Purchases, rels, sr.SkillCapChoices);
        }
    }
}
