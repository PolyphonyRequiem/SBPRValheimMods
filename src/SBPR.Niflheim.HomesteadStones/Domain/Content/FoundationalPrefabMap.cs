using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SBPR.Niflheim.HomesteadStones.Domain.Content
{
    // T009 — the engine-free bridge from a live Valheim build-piece PREFAB name (what the server
    // actually observes on a placed piece's ZDO) to the stable Foundational-catalog piece id the
    // FoundationalPieceCatalog authorizes. The catalog is deliberately authored in stable ids
    // (`foundation_wood_floor`), NOT prefab names, so a roster change or a prefab rename can never
    // silently re-credit; this map is the one place that binding lives, and it is version-pinned to the
    // same current-build catalog version so the two move together.
    //
    // Unknown prefabs resolve to null (the observer then submits an empty stable id, which the adapter
    // rejects as MissingPieceIdentity / NotCatalogMember — never a "closest" rebind). Exclusions are
    // NOT filtered here: a mapped-but-excluded prefab still resolves to its stable id so the adapter can
    // report ExcludedPiece precisely rather than the map hiding it.
    //
    // net48 audit: System + collections only. Link-compiles into the net8 test project.
    public sealed class FoundationalPrefabMap
    {
        private readonly ReadOnlyDictionary<string, string> _byPrefab;

        // Provisional proof mapping (design call 2026-07-15), aligned 1:1 with FoundationalPieceCatalog
        // members + exclusions. Vanilla basic wood build pieces map onto the authored stable ids; the
        // two crafting stations map onto the authored exclusions so a placement of them is reported as
        // ExcludedPiece, not silently unknown. Keys are Valheim ZNetScene prefab names.
        private static readonly KeyValuePair<string, string>[] AuthoredMap =
        {
            new KeyValuePair<string, string>("wood_floor", "foundation_wood_floor"),
            new KeyValuePair<string, string>("wood_wall", "foundation_wood_wall"),
            new KeyValuePair<string, string>("wood_pole", "foundation_wood_pole"),
            new KeyValuePair<string, string>("wood_beam", "foundation_wood_beam"),
            new KeyValuePair<string, string>("wood_roof", "foundation_wood_roof"),
            new KeyValuePair<string, string>("wood_stair", "foundation_wood_stair"),
            new KeyValuePair<string, string>("wood_door", "foundation_wood_door"),
            new KeyValuePair<string, string>("piece_workbench", "foundation_workbench"),
            new KeyValuePair<string, string>("forge", "foundation_forge"),
            // Vanilla names the sharpened-stake wall "piece_dvergr_stakewall" / "sharpstakes";
            // the everyday early stakewall prefab is "piece_stakewall".
            new KeyValuePair<string, string>("piece_stakewall", "foundation_wood_stakewall"),
        };

        public FoundationalPrefabMap()
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in AuthoredMap) dict[kv.Key] = kv.Value;
            _byPrefab = new ReadOnlyDictionary<string, string>(dict);
        }

        /// <summary>Version tag this map is pinned to — must match the catalog's current tag. If a
        /// future build bumps the catalog, this map must move in the same change.</summary>
        public string CatalogVersionTag => FoundationalPieceCatalog.CurrentBuild.CatalogVersionTag;

        /// <summary>The whole authored binding (prefab → stable id), read-only.</summary>
        public IReadOnlyDictionary<string, string> Bindings => _byPrefab;

        /// <summary>Resolve a live prefab name to its stable Foundational-catalog id, or null when the
        /// prefab is not an authored Foundational build piece. A null/empty prefab resolves to null.</summary>
        public string? ResolveStablePieceId(string? prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            return _byPrefab.TryGetValue(prefabName!, out var id) ? id : null;
        }

        /// <summary>The one current-build prefab map used by production wiring. Immutable.</summary>
        public static readonly FoundationalPrefabMap CurrentBuild = new FoundationalPrefabMap();
    }
}
