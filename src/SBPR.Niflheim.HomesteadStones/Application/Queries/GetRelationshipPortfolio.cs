using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;

namespace SBPR.Niflheim.HomesteadStones.Application.Queries
{
    // T035 — the COMPACT read contract `GetRelationshipPortfolio` (contracts.md §"Read contracts"):
    //
    //   "Future Stones-UI-shaped query returning all Stones related to the authenticated character plus
    //    compact revisions/status and links/keys for full GetStoneProgressionView queries. This proof
    //    needs only enough shape to demonstrate that the current Homestead commands are not bound to a
    //    nearby panel."
    //
    // Why it exists: the temporary in-world panel asks ONE Stone it is standing next to. A remote Stones
    // UI has no Stone in front of it — it must first ask "which Stones am I related to?" and then follow a
    // LINK (StoneId + the revisions it was observed at) into the full per-Stone read model. This query is
    // that first hop, and it is what makes the seam non-proximate: nothing in its input names a position,
    // an Area, or a nearby panel.
    //
    // Load-bearing boundaries:
    //   * CALLER-SCOPED. The projection is built from the AUTHENTICATED principal's own character
    //     aggregate only. A hostile claim naming another account/character is rejected by the shared
    //     PrincipalResolver (PrincipalMismatch), and even a bound caller can never see a sibling's or
    //     another account's rows — the query never enumerates the character store.
    //   * COMPACT. It carries stable identity + status + revisions ONLY: no balances (AP/BP), no node
    //     catalog, no node statuses, no purchases, no policy allowlist. Those live behind
    //     GetStoneProgressionView, which the caller fetches per Stone with the link keys returned here.
    //     This is the "do not broadcast entire character ledgers" rule applied to the READ side.
    //   * PURE. It derives from the authoritative aggregates on every call and stores nothing. A released
    //     relationship re-derives as Released with zero writes.
    //
    // net48 audit: engine-free (value objects + engine-free stores). Link-compiles into the net8 tests.

    /// <summary>One Stone the authenticated character is related to, in COMPACT form: stable identity,
    /// relationship status, and the revisions/version a follow-up
    /// <see cref="GetStoneProgressionView"/> should be issued against. Deliberately carries no balances,
    /// node rows, purchases, or policy detail — it is a LINK, not a ledger.</summary>
    public sealed class RelationshipPortfolioEntry
    {
        public RelationshipPortfolioEntry(
            StoneId stoneId,
            string family,
            string variant,
            RelationshipKind kind,
            RelationshipStatus status,
            string relationshipId,
            string responsibilityRange,
            bool authorityIndexActive,
            bool stoneResolved,
            long stoneRevision,
            long characterRevision,
            long authorityRevision,
            int contentRegistryVersion)
        {
            StoneId = stoneId;
            Family = family ?? string.Empty;
            Variant = variant ?? string.Empty;
            Kind = kind;
            Status = status;
            RelationshipId = relationshipId ?? string.Empty;
            ResponsibilityRange = responsibilityRange ?? string.Empty;
            AuthorityIndexActive = authorityIndexActive;
            StoneResolved = stoneResolved;
            StoneRevision = stoneRevision;
            CharacterRevision = characterRevision;
            AuthorityRevision = authorityRevision;
            ContentRegistryVersion = contentRegistryVersion;
        }

        /// <summary>The world-scoped Stone identity — the LINK KEY a full read-model query is issued with.</summary>
        public StoneId StoneId { get; }

        /// <summary>Stone family/variant, empty when the Stone aggregate is not resolvable on this server
        /// (see <see cref="StoneResolved"/>). Never a display name used as contract identity.</summary>
        public string Family { get; }
        public string Variant { get; }

        public RelationshipKind Kind { get; }
        public RelationshipStatus Status { get; }
        public string RelationshipId { get; }
        public string ResponsibilityRange { get; }

        /// <summary>Whether the account–Stone authority index currently reserves this character here. The
        /// character-owned record and the index are two aggregates; a portfolio row reports BOTH so a UI
        /// can render an honest "released/recovering" state rather than guessing from one of them.</summary>
        public bool AuthorityIndexActive { get; }

        /// <summary>False when this server holds no Stone aggregate for <see cref="StoneId"/> (the
        /// character's record outlives an unloaded/reset Stone). Revisions are then 0 and the caller must
        /// treat the link as unresolvable rather than inventing a zeroed Stone.</summary>
        public bool StoneResolved { get; }

        // Compact revisions/version: exactly what a follow-up command/query must pass as its expected
        // values (contracts.md common envelope).
        public long StoneRevision { get; }
        public long CharacterRevision { get; }
        public long AuthorityRevision { get; }
        public int ContentRegistryVersion { get; }
    }

    /// <summary>The authenticated character's Stone portfolio. Empty (and <see cref="Bound"/> false) when
    /// the caller is unauthenticated or the payload claim mismatched the authenticated principal.</summary>
    public sealed class RelationshipPortfolio
    {
        public RelationshipPortfolio(bool bound, string resultCode, AccountId account, CharacterId character,
            IReadOnlyList<RelationshipPortfolioEntry> entries)
        {
            Bound = bound;
            ResultCode = resultCode ?? string.Empty;
            Account = account;
            Character = character;
            Entries = entries ?? Array.Empty<RelationshipPortfolioEntry>();
        }

        /// <summary>True when an authenticated principal was resolved and the claim (if any) matched.</summary>
        public bool Bound { get; }

        /// <summary>"Applied" on success, otherwise the stable rejection code
        /// (<c>Unauthenticated</c>, <c>PrincipalMismatch</c>, <c>CharacterNotFound</c>).</summary>
        public string ResultCode { get; }

        public AccountId Account { get; }
        public CharacterId Character { get; }

        public IReadOnlyList<RelationshipPortfolioEntry> Entries { get; }

        /// <summary>Fail-closed empty portfolio. A rejected caller sees no Stones at all — never a partial
        /// or another principal's list.</summary>
        public static RelationshipPortfolio Denied(string resultCode) =>
            new RelationshipPortfolio(false, resultCode, default, default,
                Array.Empty<RelationshipPortfolioEntry>());
    }

    public sealed class GetRelationshipPortfolio
    {
        private readonly PrincipalResolver _resolver;
        private readonly ICharacterAggregateStore _characters;
        private readonly IStoneAggregateStore _stones;
        private readonly IAccountStoneAuthorityStore _authority;

        public GetRelationshipPortfolio(
            PrincipalResolver resolver,
            ICharacterAggregateStore characters,
            IStoneAggregateStore stones,
            IAccountStoneAuthorityStore authority)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _characters = characters ?? throw new ArgumentNullException(nameof(characters));
            _stones = stones ?? throw new ArgumentNullException(nameof(stones));
            _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        }

        /// <summary>Build the authenticated caller's compact Stone portfolio. The transport supplies
        /// <paramref name="connection"/> (server-observed); <paramref name="claim"/> is untrusted payload
        /// that is COMPARED and never trusted. No position, Area, or panel context is accepted — this
        /// query is structurally non-proximate.</summary>
        public RelationshipPortfolio Execute(AuthenticatedConnection connection, ClaimedPrincipal claim)
        {
            var resolution = _resolver.Resolve(connection, claim, out var principal);
            if (resolution == PrincipalResolution.UnauthenticatedPeer)
                return RelationshipPortfolio.Denied("Unauthenticated");
            if (resolution == PrincipalResolution.PrincipalMismatch)
                return RelationshipPortfolio.Denied("PrincipalMismatch");

            var character = _characters.GetCharacter(principal.Account, principal.Character);
            if (character == null)
                return new RelationshipPortfolio(true, "CharacterNotFound",
                    principal.Account, principal.Character, Array.Empty<RelationshipPortfolioEntry>());

            var entries = new List<RelationshipPortfolioEntry>();
            foreach (var record in character.StoneRecords)
            {
                foreach (var relationship in record.Relationships)
                {
                    // Every relationship the caller's OWN aggregate carries — active and released alike.
                    // Released rows are reported (with Status=Released) rather than hidden, because a
                    // returning player's UI must be able to show what they can recover.
                    var stone = _stones.GetStone(record.StoneId);
                    var authority = _authority.GetAuthority(principal.Account, record.StoneId);
                    var reservation = authority.ReservationByRelationship(relationship.RelationshipId);

                    entries.Add(new RelationshipPortfolioEntry(
                        record.StoneId,
                        stone != null ? stone.Family : string.Empty,
                        stone != null ? stone.Variant : string.Empty,
                        relationship.Kind,
                        relationship.Status,
                        relationship.RelationshipId,
                        relationship.ResponsibilityRange,
                        authorityIndexActive: reservation != null,
                        stoneResolved: stone != null,
                        stoneRevision: stone != null ? stone.Revision : 0L,
                        characterRevision: character.Revision,
                        authorityRevision: authority.Revision,
                        contentRegistryVersion: stone != null ? stone.ContentRegistryVersion : 0));
                }
            }

            return new RelationshipPortfolio(true, "Applied", principal.Account, principal.Character,
                new ReadOnlyCollection<RelationshipPortfolioEntry>(entries));
        }
    }
}
