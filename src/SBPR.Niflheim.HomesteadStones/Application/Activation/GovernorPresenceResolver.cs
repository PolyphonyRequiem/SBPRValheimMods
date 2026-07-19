using System;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;

namespace SBPR.Niflheim.HomesteadStones.Application.Activation
{
    // T016 fix-forward (PR #368 review Blocker 1) — the engine-free authority that DERIVES the two
    // cross-account governance facts the Local Effect activation channel needs from COMMITTED runtime
    // state, so they are a pure function of the real relationship/authority aggregates rather than a
    // never-written flag.
    //
    // Why this exists: the first PR #368 head derived `isOwner` / `governorPresent` from an
    // `OwnerByStone` dictionary that had NO writer, so both were permanently false and every Local
    // Effect was forced dormant through the production delivery channel (the review's structural
    // rejection). This resolver removes that dead map: both facts are derived on demand from the SAME
    // committed character/authority projections the relationship journal rehydrates. There is NO parallel
    // provisional ledger — the source of truth is the committed Bond record + the account–Stone authority
    // reservation index.
    //
    // The two facts are DELIBERATELY distinct (review guard "do not conflate known owner with Stone-wide
    // authorized-Governor presence"):
    //   * AuthorizedGovernorPresent(stone): whether ANY account holds a currently-active Homestead:All
    //     Governor bond at this Stone (Stone-wide governance presence). Drives the US5 sc2 dormancy: with
    //     no authorized Governor bonded, every Local Effect stops for everyone.
    //   * IsOwner(account, stone): whether THIS specific account is the validated Homestead owner (holds
    //     that Governor bond itself). A policy-membership input, never the Stone-wide presence fact.
    //
    // A Governor bond counts only when BOTH the character's committed RelationshipRecord is Active with the
    // authored Governor role + Homestead:All range AND the account–Stone authority index still reserves it
    // for that character. Requiring both means a released bond (record marked Released and/or reservation
    // removed) immediately stops counting — dormancy is derived, never a stored flag (AT-NO-ACTIVE-LEDGER,
    // spec US5 sc2 / contracts.md §ReleaseRelationship).
    //
    // net48 audit: engine-free (value objects + the shipped stores only). No UnityEngine/Valheim/BepInEx —
    // link-compiles into the net8 test project so every branch is unit-tested without a live server.
    public sealed class GovernorPresenceResolver
    {
        // The authored provisional-proof Governor authority the Bond policy grants (mirrors
        // ServerHomesteadBondPolicy / ServerHomesteadGovernorAuthority): a Homestead:All range + Governor
        // role. A bond carrying anything else is not an authorized Governor.
        internal const string GovernorRole = "Governor";
        internal const string GovernorRange = "Homestead:All";

        private readonly ICharacterAggregateStore _characters;
        private readonly IAccountStoneAuthorityStore _authority;

        public GovernorPresenceResolver(
            ICharacterAggregateStore characters,
            IAccountStoneAuthorityStore authority)
        {
            _characters = characters ?? throw new ArgumentNullException(nameof(characters));
            _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        }

        /// <summary>Stone-wide governance presence: true when SOME account currently holds an active,
        /// authorized Homestead:All Governor bond at this Stone. When false, US5 sc2 dormancy stops every
        /// Local Effect regardless of occupancy or policy.</summary>
        public bool AuthorizedGovernorPresent(StoneId stone)
        {
            foreach (var ch in _characters.AllCharacters())
                if (HoldsActiveGovernorBond(ch, stone))
                    return true;
            return false;
        }

        /// <summary>Whether <paramref name="occupant"/> is the validated Homestead owner of this Stone —
        /// i.e. this account itself holds the active, authorized Governor bond. Never conflated with the
        /// Stone-wide presence fact above.</summary>
        public bool IsOwner(AccountId occupant, StoneId stone)
        {
            foreach (var ch in _characters.AllCharacters())
                if (ch.Account.Equals(occupant) && HoldsActiveGovernorBond(ch, stone))
                    return true;
            return false;
        }

        private bool HoldsActiveGovernorBond(CharacterProgressionAggregate ch, StoneId stone)
        {
            foreach (var sr in ch.StoneRecords)
            {
                if (!sr.StoneId.Equals(stone)) continue;
                foreach (var rel in sr.Relationships)
                {
                    if (!rel.IsActive || rel.Kind != RelationshipKind.Bond) continue;
                    if (!string.Equals(rel.OwnerGovernorRole, GovernorRole, StringComparison.Ordinal)) continue;
                    if (!string.Equals(rel.ResponsibilityRange, GovernorRange, StringComparison.Ordinal)) continue;
                    // Cross-check the account–Stone authority reservation still holds it for this character.
                    // A release clears the reservation in the same committed operation, so a stale Active
                    // record without a reservation never counts (belt-and-braces with the record status).
                    if (_authority.GetAuthority(ch.Account, stone).HasActive(ch.Character))
                        return true;
                }
            }
            return false;
        }
    }

    /// <summary>T016 fix-forward — the <see cref="IHomesteadOwnerAuthority"/> the production Local policy
    /// handler binds, derived from COMMITTED Governor-bond state via <see cref="GovernorPresenceResolver"/>.
    /// This replaces the dead-map Func the first PR #368 head injected: the validated Homestead owner is
    /// the account currently holding the authorized Homestead:All Governor bond, proven from the same
    /// committed aggregates — never a client claim, never a separately-mutated flag.</summary>
    public sealed class CommittedGovernorOwnerAuthority : IHomesteadOwnerAuthority
    {
        private readonly GovernorPresenceResolver _resolver;

        public CommittedGovernorOwnerAuthority(GovernorPresenceResolver resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public bool IsOwner(AuthoritativePrincipal principal, StoneId stoneId) =>
            _resolver.IsOwner(principal.Account, stoneId);
    }
}
