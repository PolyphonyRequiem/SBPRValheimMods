using System.Collections.Generic;
using System.Linq;

namespace SBPR.Trailborne
{
    /// <summary>
    /// Spec drift watchdog. The locked v0.1.0 recipe manifest lives here in
    /// code, not just in a markdown spec. After ObjectDB wiring, we walk
    /// every registered SBPR recipe + buildable and compare its
    /// (item -> [(resource, amount), ...], stationName) tuple against this
    /// manifest. Any mismatch is logged at ERROR level so it shows up in
    /// the server log on every boot.
    ///
    /// This is the meta-bug fix for the 2026-06-03 drift: four recipes
    /// silently diverged from the spec across milestone iterations because
    /// nobody re-read the spec on every change. Now the server screams
    /// the moment drift appears.
    ///
    /// LOCKED SOURCE: specs/2026-06-03-trailborne-v1/planning/requirements.md
    /// (lines 170-222, 318-323). Update BOTH this manifest AND the spec
    /// in the same commit when intentionally changing a recipe.
    /// </summary>
    internal static class TrailborneSpecCheck
    {
        private struct Req { public string Resource; public int Amount; }
        private static Req R(string resource, int amount) => new Req { Resource = resource, Amount = amount };

        private class RecipeSpec
        {
            public string Item;           // ItemDrop prefab name; null = build piece (no recipe)
            public string Piece;          // Piece prefab name; null = item recipe (no piece)
            public string Station;        // "piece_sbpr_explorers_bench" or null
            public int    Amount;         // recipe output amount (items only)
            public Req[]  Resources;
        }

        // The complete v0.1.0 locked manifest.
        private static readonly RecipeSpec[] Manifest = new[]
        {
            // ── Build pieces ───────────────────────────────────────────
            new RecipeSpec {
                Piece = "piece_sbpr_explorers_bench", Station = null,
                Resources = new[] { R("Wood", 10), R("Stone", 4), R("TrophyDeer", 1) }
            },
            new RecipeSpec {
                Piece = "piece_sbpr_path_lamp", Station = null,
                Resources = new[] { R("Wood", 3), R("Resin", 2) }
            },

            // ── Item recipes ───────────────────────────────────────────
            new RecipeSpec {
                Item = "SBPR_TrailblazersSpade", Station = "piece_sbpr_explorers_bench", Amount = 1,
                Resources = new[] { R("Wood", 5), R("Flint", 2), R("LeatherScraps", 2) }
            },
            new RecipeSpec {
                Item = TrailborneM1.InkRedName, Station = "piece_sbpr_explorers_bench", Amount = 2,
                Resources = new[] { R("Raspberry", 1) }
            },
            new RecipeSpec {
                Item = TrailborneM1.InkWhiteName, Station = "piece_sbpr_explorers_bench", Amount = 2,
                Resources = new[] { R("BoneFragments", 1) }
            },
            new RecipeSpec {
                Item = TrailborneM1.InkBlueName, Station = "piece_sbpr_explorers_bench", Amount = 2,
                Resources = new[] { R("Blueberries", 1) }
            },
            new RecipeSpec {
                Item = TrailborneM1.InkBlackName, Station = "piece_sbpr_explorers_bench", Amount = 2,
                Resources = new[] { R("Coal", 1) }
            },
        };

        public static void Run()
        {
            var odb = ObjectDB.instance;
            var zns = ZNetScene.instance;
            if (odb == null || zns == null)
            {
                TrailbornePlugin.Log.LogWarning("[Trailborne/SpecCheck] Skipped: ODB or ZNetScene not ready.");
                return;
            }

            int errors = 0;
            int checks = 0;

            // ── Item recipes ──
            foreach (var spec in Manifest.Where(s => s.Item != null))
            {
                checks++;
                Recipe found = null;
                foreach (var r in odb.m_recipes)
                {
                    if (r == null || r.m_item == null || r.m_item.gameObject == null) continue;
                    if (r.m_item.gameObject.name == spec.Item) { found = r; break; }
                }
                if (found == null)
                {
                    TrailbornePlugin.Log.LogError($"[Trailborne/SpecCheck] MISSING RECIPE: {spec.Item}");
                    errors++;
                    continue;
                }
                if (found.m_amount != spec.Amount)
                    LogDrift(spec.Item, "output amount", spec.Amount.ToString(), found.m_amount.ToString(), ref errors);

                var stationName = found.m_craftingStation != null ? found.m_craftingStation.name : "(null)";
                if (spec.Station != null && stationName != spec.Station)
                    LogDrift(spec.Item, "crafting station", spec.Station, stationName, ref errors);

                CompareResources(spec.Item, spec.Resources, found.m_resources, ref errors);
            }

            // ── Build pieces ──
            foreach (var spec in Manifest.Where(s => s.Piece != null))
            {
                checks++;
                var prefab = zns.GetPrefab(spec.Piece);
                var piece  = prefab?.GetComponent<Piece>();
                if (piece == null)
                {
                    TrailbornePlugin.Log.LogError($"[Trailborne/SpecCheck] MISSING PIECE: {spec.Piece}");
                    errors++;
                    continue;
                }
                CompareResources(spec.Piece, spec.Resources, piece.m_resources, ref errors);
            }

            // ── Color-variant cairn markers + cairn pieces ──
            // Generated rather than enumerated because it's 4× repetitive.
            foreach (var color in TrailborneM2.Colors)
            {
                var markerName = TrailborneM2.MarkerName(color);
                var cairnName  = TrailborneM2.CairnName(color);
                var ink        = TrailborneM2.InkNameFor(color);

                // Cairn marker recipe
                checks++;
                Recipe markerRecipe = null;
                foreach (var r in odb.m_recipes)
                {
                    if (r == null || r.m_item == null || r.m_item.gameObject == null) continue;
                    if (r.m_item.gameObject.name == markerName) { markerRecipe = r; break; }
                }
                if (markerRecipe == null)
                {
                    TrailbornePlugin.Log.LogError($"[Trailborne/SpecCheck] MISSING RECIPE: {markerName}");
                    errors++;
                }
                else
                {
                    var expected = new[] { R("LeatherScraps", 2), R("FineWood", 1), R(ink, 1) };
                    CompareResources(markerName, expected, markerRecipe.m_resources, ref errors);
                }

                // Cairn piece resources
                checks++;
                var cairnPrefab = zns.GetPrefab(cairnName);
                var cairnPiece  = cairnPrefab?.GetComponent<Piece>();
                if (cairnPiece == null)
                {
                    TrailbornePlugin.Log.LogError($"[Trailborne/SpecCheck] MISSING PIECE: {cairnName}");
                    errors++;
                }
                else
                {
                    // Tier-1 build cost (v0.1.0 cairn ladder: 9/12/15/18/21 stone per tier).
                    // Upgrade-cost validation happens at runtime in TrailborneM2.TryUpgradeCairn.
                    var expected = new[] { R("Stone", 9), R("Resin", 1), R(markerName, 1) };
                    CompareResources(cairnName, expected, cairnPiece.m_resources, ref errors);
                }
            }

            if (errors == 0)
                TrailbornePlugin.Log.LogInfo($"[Trailborne/SpecCheck] ✓ All {checks} recipes match the v0.1.0 spec manifest.");
            else
                TrailbornePlugin.Log.LogError($"[Trailborne/SpecCheck] ✗ {errors} drift(s) detected across {checks} checks. See above.");
        }

        private static void CompareResources(string itemName, Req[] expected, Piece.Requirement[] actual, ref int errors)
        {
            actual = actual ?? new Piece.Requirement[0];
            var actualNamed = actual
                .Where(r => r != null && r.m_resItem != null && r.m_resItem.gameObject != null)
                .ToDictionary(r => r.m_resItem.gameObject.name, r => r.m_amount);
            var nullCount = actual.Count(r => r != null && r.m_resItem == null);
            if (nullCount > 0)
            {
                TrailbornePlugin.Log.LogError(
                    $"[Trailborne/SpecCheck] {itemName}: {nullCount} resource requirement(s) have NULL m_resItem (prefab name not resolved). " +
                    "Check BuildReq warnings above for which prefab name failed to resolve.");
                errors++;
            }

            foreach (var exp in expected)
            {
                if (!actualNamed.TryGetValue(exp.Resource, out var amt))
                {
                    TrailbornePlugin.Log.LogError($"[Trailborne/SpecCheck] {itemName}: MISSING resource '{exp.Resource}' (spec wants {exp.Amount}).");
                    errors++;
                }
                else if (amt != exp.Amount)
                {
                    LogDrift(itemName, $"resource '{exp.Resource}' amount", exp.Amount.ToString(), amt.ToString(), ref errors);
                }
            }

            foreach (var have in actualNamed)
            {
                if (!expected.Any(e => e.Resource == have.Key))
                {
                    TrailbornePlugin.Log.LogError(
                        $"[Trailborne/SpecCheck] {itemName}: EXTRA resource '{have.Key}' x{have.Value} (not in spec).");
                    errors++;
                }
            }
        }

        private static void LogDrift(string item, string field, string expected, string actual, ref int errors)
        {
            TrailbornePlugin.Log.LogError(
                $"[Trailborne/SpecCheck] DRIFT — {item}: {field} expected '{expected}', registered '{actual}'.");
            errors++;
        }
    }
}
