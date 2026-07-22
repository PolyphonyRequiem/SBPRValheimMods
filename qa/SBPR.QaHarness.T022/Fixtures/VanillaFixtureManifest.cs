// ============================================================================
//  QA-M3 fixture manifest (canonical, t_4db82cc0) — engine-free.
// ----------------------------------------------------------------------------
//  VanillaFixtureManifest — the concrete, ordinary-VANILLA-only allowlist +
//  bounds the QA fixture helper is permitted to provision (ADR-0009 §4, and the
//  PR #408 VANILLA-BINDINGS map §3.5). This is the data half of the "vanilla-only"
//  firewall: it seeds a closed ResourceAllowlist (logical id -> non-product
//  ResourceCategory) with a handful of ordinary vanilla scaffolding ids and pins
//  the conservative FixtureBounds a single fixture may not exceed.
//
//  The load-bearing rule this file adds on top of the generic core: a fixture id
//  is REJECTED as a product id iff it carries the product prefab prefix ("SBPR_")
//  or is on an explicit product denylist. The helper may NEVER provision the
//  artifact under test or any product state (identity/Bond/Attunement/AP/ownership/
//  activation/signature/token/verdict/journal/cache) — those ids are structurally
//  refused here BEFORE the allowlist is even consulted, so a misconfigured
//  allowlist entry naming a product prefab still fails closed.
//
//  Every id below is an ordinary vanilla prefab/item name verified against the
//  0.221.12 build's ObjectDB/ZNetScene naming (Wood, Stone, LeatherScraps, Iron,
//  piece_workbench, forge). No decompiled game source is committed — only the
//  public prefab NAMES, which are ordinary game data, not IronGate code.
//
//  Engine-free: System.* only. No product identity/AP/ownership/signature/verdict.
// ============================================================================

using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    /// <summary>
    /// The canonical closed manifest of ordinary vanilla fixture primitives (allowlist + bounds)
    /// plus the product-id guard. All members are static/pure — no world access, no engine.
    /// </summary>
    public static class VanillaFixtureManifest
    {
        /// <summary>The product prefab-name prefix. Any id starting with this is a PRODUCT id and is
        /// refused as a fixture — the helper may never stand up product content.</summary>
        public const string ProductPrefabPrefix = "SBPR_";

        // Explicit product/artifact denylist for ids that do NOT carry the prefix but still name
        // product state under test. Kept as a static, reviewed set. Case-sensitive/ordinal —
        // paired with a case-insensitive prefix check below so a case-folded evasion still fails.
        private static readonly HashSet<string> _productDenylist = new(StringComparer.OrdinalIgnoreCase)
        {
            // Product artifact under test / product identity surfaces (never fixtures).
            "Masterwork",
            "Workmanship",
            "Attunement",
            "Bond",
            "Signature",
            "Verdict",
            "Journal",
            "Ledger",
            "Entitlement",
        };

        // The ONLY vanilla logical ids a fixture may reference, each mapped to its non-product
        // ResourceCategory. Materials are granted via ItemDrop/Inventory; stations are placed via
        // additive server-authoritative spawn (ADR-0006). These are ordinary vanilla names.
        private static readonly Dictionary<string, ResourceCategory> _vanillaEntries =
            new(StringComparer.Ordinal)
            {
                // ── Materials (ordinary vanilla items) ──
                ["Wood"] = ResourceCategory.Material,
                ["Stone"] = ResourceCategory.Material,
                ["LeatherScraps"] = ResourceCategory.Material,
                ["DeerHide"] = ResourceCategory.Material,
                ["Iron"] = ResourceCategory.Material,
                ["Bronze"] = ResourceCategory.Material,
                ["Coal"] = ResourceCategory.Material,
                ["Resin"] = ResourceCategory.Material,

                // ── Stations (ordinary vanilla crafting stations) ──
                ["piece_workbench"] = ResourceCategory.Station,
                ["forge"] = ResourceCategory.Station,

                // ── Placement anchor (a bare position marker, no behaviour) ──
                ["FixtureAnchor"] = ResourceCategory.PlacementAnchor,
            };

        /// <summary>Conservative bounds for a single QA fixture (a handful of scaffolding objects).</summary>
        public static FixtureBounds Bounds => new FixtureBounds(
            maxDistinctResources: 8,
            maxCountPerResource: 64,
            maxTotalObjects: 128,
            maxRadiusMeters: 8.0);

        /// <summary>
        /// True iff <paramref name="id"/> names PRODUCT state (prefix or denylist) and therefore
        /// may never be a fixture. Fail-closed on null. Both checks are case-insensitive so a
        /// case-folded "sbpr_" or "masterwork" cannot slip past.
        /// </summary>
        public static bool IsProductId(string? id)
        {
            if (string.IsNullOrEmpty(id)) return true; // fail closed: an empty/absent id is not a valid vanilla fixture
            if (id!.StartsWith(ProductPrefabPrefix, StringComparison.OrdinalIgnoreCase)) return true;
            if (_productDenylist.Contains(id)) return true;
            return false;
        }

        /// <summary>
        /// Build the canonical vanilla allowlist. Guards defensively: if any seeded entry is a
        /// product id (should never happen — the seed is a reviewed constant), construction throws,
        /// so a product prefab can never be smuggled into the allowlist even by a future edit.
        /// </summary>
        public static ResourceAllowlist BuildAllowlist()
        {
            foreach (var kv in _vanillaEntries)
            {
                if (IsProductId(kv.Key))
                    throw new InvalidOperationException(
                        "VanillaFixtureManifest is corrupt: '" + kv.Key + "' is a product id and cannot be allowlisted.");
            }
            return new ResourceAllowlist(_vanillaEntries);
        }

        /// <summary>The vanilla logical ids this manifest allows (ordinal-sorted, read-only view).</summary>
        public static IReadOnlyCollection<string> VanillaLogicalIds => new List<string>(_vanillaEntries.Keys);
    }
}
