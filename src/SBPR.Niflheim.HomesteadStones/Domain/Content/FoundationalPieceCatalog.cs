using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SBPR.Niflheim.HomesteadStones.Domain.Content
{
    // T008 — the authored Foundational construction catalog (data-model.md §"Foundation":
    // "protected Foundational TreeId/version, Foundational construction CatalogId/version, exclusions";
    // spec FR-005: the protected Foundational Tree "owning one stable-ID basic-piece catalog"). This is
    // the immutable current-build source of truth for WHICH placed pieces are Foundational-catalog
    // members that may earn the deliberately low ongoing AP, and WHICH stable ids are explicitly held
    // out (exclusions) even though a client could place them.
    //
    // Cardinal rules (data-model.md modeling rules 3/6, mirrored from HomesteadProgressionCatalog):
    //   * Membership is by STABLE piece id + current-build catalog version. Display/prefab names are not
    //     identity here; the stable id is the authored key and the version pins the build so a later
    //     roster change cannot silently re-credit a stale placement.
    //   * An explicit exclusion ALWAYS wins over membership: a stable id listed in Exclusions is never a
    //     credit-eligible member, even if it also appears in the member roster. This is how the proof
    //     holds a piece out of the ongoing AP source without deleting it from the build.
    //   * Unknown same-build references reject clearly (no "closest" rebind); production catalog
    //     migration/grandfathering is DEFERRED.
    //
    // net48 audit: only System / System.Collections.Generic(.ObjectModel). No net5+ surface, no
    // UnityEngine/Valheim/BepInEx reference, so this link-compiles into the net8 test project exactly
    // like HomesteadProgressionCatalog.
    public sealed class FoundationalPieceCatalog
    {
        /// <summary>Current proof Foundational construction-catalog version. Bumping this makes any
        /// placement evidence stamped with an older version an out-of-build reference that rejects with
        /// no receipt (data-model.md §"Credit Foundational AP": current-build definition required).</summary>
        public const int CurrentCatalogVersion = 1;

        /// <summary>Stable catalog identity (data-model.md CatalogId). Version-pinned like every other
        /// authored content key. Not a display name.</summary>
        public string CatalogId { get; } = "HomesteadFoundationalConstruction";
        public int CatalogVersion => CurrentCatalogVersion;

        /// <summary>Version tag threaded on placement evidence. A placement whose stamped
        /// <c>foundationalCatalogVersion</c> is not this exact tag is an out-of-build reference.</summary>
        public string CatalogVersionTag { get; } = "v1";

        private readonly ReadOnlyCollection<string> _members;
        private readonly ReadOnlyCollection<string> _exclusions;
        private readonly HashSet<string> _memberSet;
        private readonly HashSet<string> _exclusionSet;

        // ── The authored first-build basic-piece roster (spec FR-005). Stable ids, NOT prefab names.
        // Deliberately small and legible: the protected Foundational family is the always-on, low-value
        // baseline, so the roster is the everyday basic homestead construction pieces. Provisional
        // proof content (design call 2026-07-15), explicitly configurable, not a final content lock.
        private static readonly string[] AuthoredMembers =
        {
            "foundation_wood_floor",
            "foundation_wood_wall",
            "foundation_wood_pole",
            "foundation_wood_beam",
            "foundation_wood_roof",
            "foundation_wood_stair",
            "foundation_wood_door",
            "foundation_wood_stakewall",
        };

        // Explicit exclusions: stable ids a client can physically place inside the Stone Area but which
        // the proof deliberately holds OUT of the ongoing Foundational AP source (spec User Story 4 /
        // data-model.md "exclusions"). Here: crafting/utility stations are construction, not Foundational
        // family, so they never earn the baseline AP even though they build fine.
        private static readonly string[] AuthoredExclusions =
        {
            "foundation_workbench",
            "foundation_forge",
        };

        public FoundationalPieceCatalog()
        {
            _members = new ReadOnlyCollection<string>(new List<string>(AuthoredMembers));
            _exclusions = new ReadOnlyCollection<string>(new List<string>(AuthoredExclusions));
            _memberSet = new HashSet<string>(AuthoredMembers, StringComparer.Ordinal);
            _exclusionSet = new HashSet<string>(AuthoredExclusions, StringComparer.Ordinal);
        }

        /// <summary>The immutable current-build member roster (stable ids). Ordered, read-only.</summary>
        public IReadOnlyList<string> Members => _members;

        /// <summary>The immutable current-build explicit-exclusion set (stable ids). Read-only.</summary>
        public IReadOnlyList<string> Exclusions => _exclusions;

        /// <summary>True only when the stable id is an authored member AND is not explicitly excluded.
        /// Exclusion always wins. An empty/unknown id is never a member.</summary>
        public bool IsCreditEligibleMember(string stablePieceId)
        {
            if (string.IsNullOrEmpty(stablePieceId)) return false;
            if (_exclusionSet.Contains(stablePieceId)) return false;
            return _memberSet.Contains(stablePieceId);
        }

        /// <summary>True when the stable id is explicitly excluded (distinguishes a deliberately
        /// held-out piece from a simply-unknown one for operator diagnosis).</summary>
        public bool IsExcluded(string stablePieceId) =>
            !string.IsNullOrEmpty(stablePieceId) && _exclusionSet.Contains(stablePieceId);

        /// <summary>True when the stamped catalog version tag matches the current build's tag.</summary>
        public bool IsCurrentCatalogVersion(string catalogVersionTag) =>
            string.Equals(catalogVersionTag, CatalogVersionTag, StringComparison.Ordinal);

        /// <summary>The one current-build catalog instance used by production wiring and the default
        /// adapter. Constructed once; immutable.</summary>
        public static readonly FoundationalPieceCatalog CurrentBuild = new FoundationalPieceCatalog();
    }
}
