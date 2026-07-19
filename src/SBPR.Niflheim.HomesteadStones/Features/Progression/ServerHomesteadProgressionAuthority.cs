using System;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    // T016 shared runtime substrate — the SERVER-OWNED authority policies the live Local progression
    // composition injects into the accepted Facet / Development / LocalPolicy command handlers. These are
    // the provisional proof policies for the Homestead Settlement variant (mirroring the Foundational
    // runtime's ServerHomesteadBondPolicy / ServerHomesteadFamilyResolver): they authorize the authored
    // "Homestead:All" Governor range and the validated Homestead owner. They are never client-authored and
    // carry no permissive fallback — the handlers reject a request these policies do not affirm.
    //
    // A production build sources these from the Stone aggregate / content policy; kept as small seams so
    // the engine-free handlers stay pure. These types are engine-free but live in Features/Progression next
    // to the bootstrap that owns them, and are NOT link-compiled into the net8 tests (which supply their
    // own deterministic stubs).

    /// <summary>Authorizes a Governor carrying the authored "Homestead:All" range + "Governor" role to
    /// commit any authored Facet/category on the Homestead Stone. Empty range (Attunement) never
    /// authorizes.</summary>
    internal sealed class ServerHomesteadGovernorAuthority : IGovernorAuthorityPolicy
    {
        internal static readonly ServerHomesteadGovernorAuthority Instance = new ServerHomesteadGovernorAuthority();

        public bool CanCommit(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
            string facetId, Domain.StoneProgression.FacetCategory category) =>
            string.Equals(responsibilityRange, "Homestead:All", StringComparison.Ordinal)
            && string.Equals(ownerGovernorRole, "Governor", StringComparison.Ordinal)
            && category != Domain.StoneProgression.FacetCategory.None;
    }

    /// <summary>Authorizes a Governor carrying the authored "Homestead:All" range + "Governor" role to
    /// credit/spend BP on any committed Tree of the Homestead Stone.</summary>
    internal sealed class ServerHomesteadDevelopmentAuthority : IGovernorDevelopmentAuthority
    {
        internal static readonly ServerHomesteadDevelopmentAuthority Instance = new ServerHomesteadDevelopmentAuthority();

        public bool CanDevelop(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
            VersionedId tree) =>
            string.Equals(responsibilityRange, "Homestead:All", StringComparison.Ordinal)
            && string.Equals(ownerGovernorRole, "Governor", StringComparison.Ordinal)
            && !tree.IsNone;
    }

    /// <summary>Validates the Homestead owner authorized to set the single Settlement Local policy. The
    /// provisional proof policy treats the Stone's bonded Governor (Homestead:All) as the owner: only a
    /// principal that currently holds that authority may change the policy. Production sources the true
    /// owner identity from the Stone aggregate; this seam keeps the accepted handler pure. A caller that is
    /// not the authorized Governor is rejected (never a permissive default).</summary>
    internal sealed class ServerHomesteadOwnerAuthority : IHomesteadOwnerAuthority
    {
        private readonly Func<StoneId, string?> _ownerAccountForStone;

        /// <param name="ownerAccountForStone">Resolves the server-owned owner AccountId value for a Stone
        /// (the account currently holding the Homestead:All Governor bond), or null when none is known.</param>
        internal ServerHomesteadOwnerAuthority(Func<StoneId, string?> ownerAccountForStone)
        {
            _ownerAccountForStone = ownerAccountForStone ?? throw new ArgumentNullException(nameof(ownerAccountForStone));
        }

        public bool IsOwner(AuthoritativePrincipal principal, StoneId stoneId)
        {
            var owner = _ownerAccountForStone(stoneId);
            return !string.IsNullOrEmpty(owner)
                && string.Equals(owner, principal.Account.Value, StringComparison.Ordinal);
        }
    }
}
