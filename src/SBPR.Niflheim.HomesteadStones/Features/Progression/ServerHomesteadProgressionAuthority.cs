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
}
