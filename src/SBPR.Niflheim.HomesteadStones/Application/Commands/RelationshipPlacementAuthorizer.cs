using System;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;

namespace SBPR.Niflheim.HomesteadStones.Application.Commands
{
    // T008 — the production authorizer that makes real T007 relationships the gate for the ongoing
    // protected Foundational AP source, replacing the T002 PreconfiguredTestAuthorizer allow-list.
    //
    // Contract (contracts.md RecordFoundationalPlacement "Validates: active Attunement, authenticated
    // actor..."; data-model.md §"Credit Foundational AP" step 2 "Validate active Attunement..."): a
    // Foundational placement earns AP only when the acting character holds an ACTIVE relationship
    // (Bond or Attunement) to this Stone in the authoritative account–Stone authority index (Aggregate
    // 2). Attunement is the low-bar relationship that unlocks the baseline earning; a bonded Governor
    // also holds a reservation and therefore also earns.
    //
    // The load-bearing T008 property — "Tree commitment MUST NOT disable this baseline source" — falls
    // out structurally: this authorizer reads ONLY the relationship authority index. It never consults
    // Facet occupancy, Committed Trees, Tree Level, or any development state, so committing/developing a
    // Tree cannot revoke the ongoing Foundational earning. As long as the character's Attunement stays
    // active, the source stays active for the entire Homestead life; a release removes the reservation
    // and the source correctly stops (AT-FOUNDATIONAL-ONGOING / AT-FOUNDATIONAL-EXCLUDED).
    //
    // net48 audit: engine-free — value objects + the authority projection store interface only. No net5+
    // surface, no UnityEngine/Valheim/BepInEx reference, so it link-compiles into the net8 test project.
    public sealed class RelationshipPlacementAuthorizer : IFoundationalPlacementAuthorizer
    {
        private readonly IAccountStoneAuthorityStore _authority;

        public RelationshipPlacementAuthorizer(IAccountStoneAuthorityStore authority)
        {
            _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        }

        /// <summary>Authorized only when the acting character currently holds an active reservation
        /// (Bond or Attunement) to this Stone in the authoritative index. A vacant index, a released
        /// relationship, or a sibling-only reservation on the same account all deny — the reservation
        /// must belong to THIS character. Tree/Facet/development state is deliberately never read, so a
        /// committed Tree can never disable this baseline.</summary>
        public bool IsAuthorized(AuthoritativePrincipal principal, StoneId stoneId)
        {
            var index = _authority.GetAuthority(principal.Account, stoneId);
            return index.HasActive(principal.Character);
        }
    }
}
