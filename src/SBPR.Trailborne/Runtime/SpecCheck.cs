using System.Collections.Generic;
using System.Linq;
using SBPR.Trailborne.Features.Pigments;
using SBPR.Trailborne.Features.Cairns;
using SBPR.Trailborne.Features.MarkerSigns;
using SBPR.Trailborne.Features.Cartography;
using SBPR.Trailborne.Features.Portals;
using SBPR.Trailborne.Features.Sunstone;
using SBPR.Trailborne.Features.Exploration;

namespace SBPR.Trailborne.Runtime
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
    /// ASSET-RENDERABILITY (added with the v0.2.x Kit icon-crash fix, C1): the
    /// watchdog now also asserts that every SBPR item recipe's resolved
    /// <c>ItemDrop</c> has a real, loaded <c>m_icons[0]</c> — specifically that it
    /// is NOT the shared <c>Assets.FallbackIcon</c> placeholder. Additively-built
    /// items (<c>Assets.TryConstructItemShell</c>) pre-seed that magenta fallback so a
    /// missing icon PNG degrades to "ugly, never crash" instead of throwing in the
    /// crafting panel; this check is what then SCREAMS at server boot that the real
    /// PNG didn't ship — closing the "server-green recipes, client-side icon crash"
    /// blind spot that hid the Cartographer's Kit no-cost bug. It is an asset check,
    /// not a recipe row, so the recipe-manifest count is unchanged.
    ///
    /// LOCKED SOURCE: docs/v0.1.0/planning/requirements.md
    /// (lines 170-222, 318-323) for the Meadows manifest;
    /// docs/v2/planning/requirements.md §1/§3 + docs/v2/planning/cartography-impl-spec.md §0
    /// for the Black-Forest cartography rows (Surveyor's Table piece; Cartographer's Kit
    /// item recipe — the 40-pigment gate);
    /// docs/design/marker-signs-worldpin.md + docs/v2/planning/marker-signs-impl-spec.md
    /// (§0 manifest table) for the v2 marker-sign rows;
    /// docs/v2/planning/ancient-portal-impl-spec.md §0 for the v2 Portal Seed item recipe
    /// + Ancient Portal build-piece rows.
    /// Update BOTH this manifest AND the relevant spec in the same commit when
    /// intentionally changing a recipe.
    /// </summary>
    internal static class SpecCheck
    {
        private struct Req { public string Resource; public int Amount; }
        private static Req R(string resource, int amount) => new Req { Resource = resource, Amount = amount };

        private class RecipeSpec
        {
            public string? Item;           // ItemDrop prefab name; null = build piece (no recipe)
            public string? Piece;          // Piece prefab name; null = item recipe (no piece)
            public string? Station;        // "piece_sbpr_explorers_bench" or null
            public int    Amount;         // recipe output amount (items only)
            public Req[]  Resources = null!;  // always set by each manifest initializer
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
            // Single Painted Sign (§A2.6, re-lock 2026-06-05): ONE buildable piece,
            // placed UNPAINTED, then painted via the combined Paint+Text panel which
            // consumes one pigment per CHANGED color slot (delta vs the sign's current
            // ZDO color — §A2.6 per-changed, LOCKED Daniel 2026-06-21 / card t_6df12ca8;
            // unchanged slots + clears free) at paint time. Pigment is NOT a build
            // ingredient — the build recipe stays Wood x2. This replaced the four tinted
            // sign buildables (each Wood + ink). NOTE: this manifest asserts only the
            // BUILD recipe (Wood x2); the consume-cost rule is enforced by SignPaintDelta
            // + tests/SignPaintDeltaTests.cs, not by a SpecCheck runtime assert.
            new RecipeSpec {
                Piece = "piece_sbpr_sign", Station = null,
                Resources = new[] { R("Wood", 2) }
            },

            // ── v2 Black-Forest cartography (impl spec §0 row 1; card t_2715661d) ──
            // Surveyor's Table — placed station, NO bench-in-range to place
            // (m_craftingStation = null). Black-Forest tier. LOCKED per
            // docs/v2/planning/requirements.md §1 + cartography-impl-spec.md §0/§1.2.
            new RecipeSpec {
                Piece = "piece_sbpr_surveyors_table", Station = null,
                Resources = new[] { R("FineWood", 10), R("Bronze", 2), R("DeerHide", 4), R("BoneFragments", 8) }
            },

            // ── Trailside Camp — Bear Hide Tent (docs/design/trailside-camp.md) ──
            // Black-Forest tier (bear Bjorn is a BF creature → BjornHide is a BF material).
            // Placeholder art = vanilla TraderTent mesh shipped in SBPR's first custom
            // AssetBundle. NO bench-in-range to place (Pillar 1, m_craftingStation = null).
            // Recipe PROVISIONAL pending the design doc's lock; must equal BearHideTent.BuildResources().
            new RecipeSpec {
                Piece = "piece_sbpr_bearhide_tent", Station = null,
                Resources = new[] { R("BjornHide", 4), R("FineWood", 6), R("LeatherScraps", 4) }
            },

            // ── Item recipes ───────────────────────────────────────────
            new RecipeSpec {
                Item = "SBPR_TrailblazersSpade", Station = "piece_sbpr_explorers_bench", Amount = 1,
                Resources = new[] { R("Wood", 5), R("Flint", 2), R("LeatherScraps", 2) }
            },
            new RecipeSpec {
                Item = Pigments.PigmentRedName, Station = "piece_sbpr_explorers_bench", Amount = 2,
                Resources = new[] { R("Raspberry", 1) }
            },
            new RecipeSpec {
                Item = Pigments.PigmentWhiteName, Station = "piece_sbpr_explorers_bench", Amount = 2,
                Resources = new[] { R("BoneFragments", 1) }
            },
            new RecipeSpec {
                Item = Pigments.PigmentBlueName, Station = "piece_sbpr_explorers_bench", Amount = 2,
                Resources = new[] { R("Blueberries", 1) }
            },
            new RecipeSpec {
                Item = Pigments.PigmentBlackName, Station = "piece_sbpr_explorers_bench", Amount = 2,
                Resources = new[] { R("Coal", 1) }
            },

            // ── v2 Black-Forest cartography (impl spec §0 row 2; card t_cb831069) ──
            // Local Map — TwoHandedWeapon item, crafted blank at the Explorer's Bench,
            // imprinted at a Surveyor's Table. LOCKED per docs/v2/planning/requirements.md §2
            // + cartography-impl-spec.md §0/§2A.1: DeerHide ×2 + FineWood ×4, amount 1.
            // Issue 9 (Daniel, 2026-06-11 playtest): bumped from 1+1 to 2+4.
            new RecipeSpec {
                Item = "SBPR_LocalMap", Station = "piece_sbpr_explorers_bench", Amount = 1,
                Resources = new[] { R("DeerHide", 2), R("FineWood", 4) }
            },

            // ── v2 Black-Forest cartography (impl spec §0 row 3; card t_65fcfe5c) ──
            // Cartographer's Kit — Utility-slot accessory that GATES auto-mapping. The
            // 40-pigment recipe IS the gate (no discovery flag). LOCKED per
            // docs/v2/planning/requirements.md §3 + cartography-impl-spec.md §0/§3.1.
            // Pigment resource names are the SBPR_Ink* wire values via Pigments.*Name.
            new RecipeSpec {
                Item = CartographersKit.KitName, Station = "piece_sbpr_explorers_bench", Amount = 1,
                Resources = new[] {
                    R(Pigments.PigmentRedName,   10),
                    R(Pigments.PigmentWhiteName, 10),
                    R(Pigments.PigmentBlueName,  10),
                    R(Pigments.PigmentBlackName, 10),
                    R("FineWood", 4),
                }
            },
            // ── v2 Black-Forest Ancient Portal (ancient-portal-impl-spec.md §0; card t_bafc1e57) ──
            // Portal Seed — additive item recipe, crafted at the Explorer's Bench from
            // AncientSeed ×1 + GreydwarfEye ×20 + SurtlingCore ×2, amount 1, 25 kg / stack 1.
            // Item-only shape (Station set, Amount set; Piece null). LOCKED per spec §0/§2.3.
            new RecipeSpec {
                Item = Portals.SeedItemName, Station = "piece_sbpr_explorers_bench", Amount = 1,
                Resources = new[] {
                    R("AncientSeed", Portals.SeedAncientSeedCost),
                    R(MarkerSigns.EyeResource, Portals.SeedGreydwarfEyeCost),  // "GreydwarfEye" — shared const
                    R("SurtlingCore", Portals.SeedSurtlingCoreCost),
                }
            },
            // Ancient Portal — Hammer-placed build piece (m_craftingStation = null), sole
            // build cost is one Portal Seed (so break→seed is free via Piece.DropResources).
            // Piece-only shape (no Item, no Station). LOCKED per spec §0/§3.4.
            new RecipeSpec {
                Piece = Portals.PortalPieceName, Station = null,
                Resources = new[] { R(Portals.SeedItemName, 1) }
            },
            // ── v3 Swamp: Sunstone Lens (card t_2fd7bc7f) ──
            // NOTE: the Sunstone MATERIAL has NO recipe row — it is loot-sourced only (swamp
            // surface chests + rare Draugr Elite, SunstoneLoot.cs / PR #183). DropTables and
            // CharacterDrops are not modelled by SpecCheck, so the material contributes 0 manifest
            // rows. The earlier provisional Iron×1+Crystal×2 craft was a bridge until the drops
            // shipped; Daniel locked REMOVE once they did (card t_8f39b5fc → t_c27f985e, impl spec §6).
            // Sunstone Lens — additive Trinket accessory, crafted at the Explorer's Bench from
            // Sunstone ×2 + Iron ×1 + Guck ×3, amount 1. Sunstone referenced via the const so a
            // rename can't drift the recipe; it registers into ODB earlier in the same wiring pass.
            // Item-only shape (Station + Amount set; Piece null). LOCKED per docs/v3 spec §0/§6.
            new RecipeSpec {
                Item = SunstoneLens.LensName, Station = "piece_sbpr_explorers_bench", Amount = 1,
                Resources = new[] {
                    R(SunstoneLens.SunstoneName, SunstoneLens.LensSunstoneCost),
                    R("Iron", SunstoneLens.LensIronCost),
                    R("Guck", SunstoneLens.LensGuckCost),
                }
            },
            // ── v3 Swamp: Iron Compass (card t_ee61472f) ──
            // Iron Compass — additive Trinket accessory, crafted at the Explorer's Bench from
            // Iron ×4 + Ooze ×2 + Red Pigment ×1, amount 1. Daniel's Q1 LOCK (2026-06-17). Iron
            // is the Swamp tier gate; Ooze is the Swamp Blob/Oozer drop; Red Pigment is the
            // SBPR_InkRed pigment item, referenced via Pigments.PigmentRedName so a rename can't
            // drift the recipe (it registers into ODB earlier in the same wiring pass, before
            // IronCompass.DoObjectDBWiring). Item-only shape (Station + Amount set; Piece null).
            // LOCKED per docs/v3/planning/iron-compass-impl-spec.md §0/§3.2.
            new RecipeSpec {
                Item = IronCompass.CompassName, Station = "piece_sbpr_explorers_bench", Amount = 1,
                Resources = new[] {
                    R("Iron", IronCompass.IronCost),
                    R("Ooze", IronCompass.OozeCost),
                    R(Pigments.PigmentRedName, IronCompass.RedPigmentCost),
                }
            },
            // ── v3 Swamp: Twisted Portal (card t_2b388cd5) ──
            // Twisted Portal — Hammer-placed build piece (m_craftingStation = null), the endgame
            // no-restriction portal. Build cost FineWood ×20 + GreydwarfEye ×10 + SurtlingCore ×4
            // + SBPR_Sunstone ×1 (Q1 = coexist shape). Sunstone referenced via SunstoneLens.SunstoneName
            // and GreydwarfEye via MarkerSigns.EyeResource so a rename can't drift the recipe; both
            // register into ODB earlier in the same wiring pass (Sunstone via SunstoneLens, before
            // TwistedPortal.DoObjectDBWiring). Piece-only shape (no Item, no Station). Under
            // food-as-fuel there is NO key item row (the trinket was removed in the t_c15411b2
            // reconciliation) — this is the feature's only manifest row. LOCKED per
            // docs/v3/planning/twisted-portal-impl-spec.md §0.
            new RecipeSpec {
                Piece = TwistedPortal.PortalPieceName, Station = null,
                Resources = new[] {
                    R("FineWood", TwistedPortal.PortalFineWoodCost),
                    R(MarkerSigns.EyeResource, TwistedPortal.PortalGreydwarfEyeCost),  // "GreydwarfEye" — shared const
                    R("SurtlingCore", TwistedPortal.PortalSurtlingCoreCost),
                    R(SunstoneLens.SunstoneName, TwistedPortal.PortalSunstoneCost),    // "SBPR_Sunstone" — shared const
                }
            },
        };

        public static void Run()
        {
            var odb = ObjectDB.instance;
            var zns = ZNetScene.instance;
            if (odb == null || zns == null)
            {
                Plugin.Log.LogWarning("[Trailborne/SpecCheck] Skipped: ODB or ZNetScene not ready.");
                return;
            }

            int errors = 0;
            int checks = 0;
            int iconChecks = 0;   // asset-renderability assertions (C1); counted separately so the
                                  // recipe-manifest tally stays a pure recipe count.
            int attackChecks = 0; // null-m_attack boot assertions (sibling of C1); counted separately
                                  // for the same reason — keeps the recipe tally pure.

            // ── Item recipes ──
            foreach (var spec in Manifest.Where(s => s.Item != null))
            {
                checks++;
                Recipe? found = null;
                foreach (var r in odb.m_recipes)
                {
                    if (r == null || r.m_item == null || r.m_item.gameObject == null) continue;
                    if (r.m_item.gameObject.name == spec.Item) { found = r; break; }
                }
                if (found == null)
                {
                    Plugin.Log.LogError($"[Trailborne/SpecCheck] MISSING RECIPE: {spec.Item}");
                    errors++;
                    continue;
                }
                if (found.m_amount != spec.Amount)
                    LogDrift(spec.Item, "output amount", spec.Amount.ToString(), found.m_amount.ToString(), ref errors);

                var stationName = found.m_craftingStation != null ? found.m_craftingStation.name : "(null)";
                if (spec.Station != null && stationName != spec.Station)
                    LogDrift(spec.Item, "crafting station", spec.Station, stationName, ref errors);

                CompareResources(spec.Item, spec.Resources, found.m_resources, ref errors);

                // C1: assert the real icon loaded (not the shared fallback). found.m_item is the
                // resolved ItemDrop. See CheckIcon — this is the recurrence guard for the Kit
                // no-cost crash (server-green recipe, client-side empty-icon throw).
                CheckIcon(spec.Item, found.m_item, ref errors, ref iconChecks);

                // Sibling guard: assert m_attack/m_secondaryAttack are non-null. See CheckAttack —
                // the recurrence guard for the Portal Seed / Kit per-frame GetChainTooltip NRE
                // (server-green recipe, client-side null-m_attack throw on recipe select).
                CheckAttack(spec.Item, found.m_item, ref errors, ref attackChecks);
            }

            // ── Build pieces ──
            foreach (var spec in Manifest.Where(s => s.Piece != null))
            {
                checks++;
                var prefab = zns.GetPrefab(spec.Piece);
                var piece  = prefab?.GetComponent<Piece>();
                if (piece == null)
                {
                    Plugin.Log.LogError($"[Trailborne/SpecCheck] MISSING PIECE: {spec.Piece}");
                    errors++;
                    continue;
                }
                CompareResources(spec.Piece, spec.Resources, piece.m_resources, ref errors);
            }

            // ── Color-variant cairn markers + cairn pieces ──
            // Generated rather than enumerated because it's 4× repetitive.
            foreach (var color in Cairns.Colors)
            {
                var markerName = Cairns.MarkerName(color);
                var cairnName  = Cairns.CairnName(color);
                var pigment    = Cairns.PigmentNameFor(color);

                // Cairn marker recipe
                checks++;
                Recipe? markerRecipe = null;
                foreach (var r in odb.m_recipes)
                {
                    if (r == null || r.m_item == null || r.m_item.gameObject == null) continue;
                    if (r.m_item.gameObject.name == markerName) { markerRecipe = r; break; }
                }
                if (markerRecipe == null)
                {
                    Plugin.Log.LogError($"[Trailborne/SpecCheck] MISSING RECIPE: {markerName}");
                    errors++;
                }
                else
                {
                    var expected = new[] { R("LeatherScraps", 2), R("FineWood", 1), R(pigment, 1) };
                    CompareResources(markerName, expected, markerRecipe.m_resources, ref errors);
                    // C1: cairn markers are CLONE items (donor icon), so this normally passes —
                    // it catches any future additive marker that forgets its icon.
                    CheckIcon(markerName, markerRecipe.m_item, ref errors, ref iconChecks);
                    // Sibling: cairn markers are CLONE items (donor deep-copies a non-null attack),
                    // so this normally passes — it catches any future additive marker that forgets it.
                    CheckAttack(markerName, markerRecipe.m_item, ref errors, ref attackChecks);
                }

                // Cairn piece resources
                checks++;
                var cairnPrefab = zns.GetPrefab(cairnName);
                var cairnPiece  = cairnPrefab?.GetComponent<Piece>();
                if (cairnPiece == null)
                {
                    Plugin.Log.LogError($"[Trailborne/SpecCheck] MISSING PIECE: {cairnName}");
                    errors++;
                }
                else
                {
                    // Tier-1 build cost (v0.1.0 cairn ladder: 9/12/15/18/21 stone per tier).
                    // Upgrade-cost validation happens at runtime in Cairns.TryUpgradeCairn.
                    var expected = new[] { R("Stone", 9), R("Resin", 1), R(markerName, 1) };
                    CompareResources(cairnName, expected, cairnPiece.m_resources, ref errors);
                }
            }

            // ── v2 Marker Signs (4 additive build pieces, Wood x2 + Greydwarf eye x1) ──
            // Generated rather than enumerated (4× repetitive) — same DRY pattern as the
            // cairn loop above. These are build pieces (Item == null), Wood x2 + Greydwarf
            // eye x1 (Black Forest availability gate), no station (placed via the spade).
            // Source: marker-signs-impl-spec.md §0.
            foreach (var mk in MarkerSigns.MarkerTypes)
            {
                checks++;
                var prefab = zns.GetPrefab(mk.PrefabName);
                var piece  = prefab != null ? prefab.GetComponent<Piece>() : null;
                if (piece == null)
                {
                    Plugin.Log.LogError($"[Trailborne/SpecCheck] MISSING PIECE: {mk.PrefabName}");
                    errors++;
                }
                else
                {
                    var expected = new[] { R("Wood", MarkerSigns.WoodCost), R(MarkerSigns.EyeResource, MarkerSigns.EyeCost) };
                    CompareResources(mk.PrefabName, expected, piece.m_resources, ref errors);
                }
            }

            // ── v3 Twisted Portal FOOD-AS-FUEL: Portal Energy constant manifest (card t_6e992a30, C2) ──
            // Not a recipe — the cost model has no craftable — so it's its own boot assertion, the
            // SpecCheck/watchdog pattern the impl-spec §5.8 calls for: encode the design-doc §6 locked
            // baselines as INDEPENDENT literals here and diff them against PortalEnergyMath.Default* (the
            // single source of truth the config + the engine-free math + the unit tests all read). A drift
            // in EITHER — a fat-fingered Default* const or a design-doc number that moved without updating
            // the code — screams at boot, exactly like a recipe drift. Counted separately so the recipe
            // tally stays pure.
            int peChecks = 0;
            CheckPortalEnergyManifest(ref errors, ref peChecks);

            if (errors == 0)
                Plugin.Log.LogInfo($"[Trailborne/SpecCheck] ✓ All {checks} recipes match the v0.1.0 spec manifest; {iconChecks} item icon(s) loaded (no fallback placeholders); {attackChecks} item(s) have non-null m_attack (no tooltip-NRE landmine); {peChecks} Portal Energy constant(s) match the locked food-as-fuel manifest.");
            else
                Plugin.Log.LogError($"[Trailborne/SpecCheck] ✗ {errors} drift(s) detected across {checks} recipe + {iconChecks} icon + {attackChecks} attack + {peChecks} Portal-Energy checks. See above.");
        }

        private static void CompareResources(string? itemName, Req[] expected, Piece.Requirement[] actual, ref int errors)
        {
            actual = actual ?? new Piece.Requirement[0];
            var actualNamed = actual
                .Where(r => r != null && r.m_resItem != null && r.m_resItem.gameObject != null)
                .ToDictionary(r => r.m_resItem.gameObject.name, r => r.m_amount);
            var nullCount = actual.Count(r => r != null && r.m_resItem == null);
            if (nullCount > 0)
            {
                Plugin.Log.LogError(
                    $"[Trailborne/SpecCheck] {itemName}: {nullCount} resource requirement(s) have NULL m_resItem (prefab name not resolved). " +
                    "Check BuildReq warnings above for which prefab name failed to resolve.");
                errors++;
            }

            foreach (var exp in expected)
            {
                if (!actualNamed.TryGetValue(exp.Resource, out var amt))
                {
                    Plugin.Log.LogError($"[Trailborne/SpecCheck] {itemName}: MISSING resource '{exp.Resource}' (spec wants {exp.Amount}).");
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
                    Plugin.Log.LogError(
                        $"[Trailborne/SpecCheck] {itemName}: EXTRA resource '{have.Key}' x{have.Value} (not in spec).");
                    errors++;
                }
            }
        }

        private static void LogDrift(string? item, string field, string expected, string actual, ref int errors)
        {
            Plugin.Log.LogError(
                $"[Trailborne/SpecCheck] DRIFT — {item}: {field} expected '{expected}', registered '{actual}'.");
            errors++;
        }

        /// <summary>
        /// C1 asset-renderability assertion: the resolved item's real icon actually loaded.
        /// Two failure modes, both ERROR:
        ///   1. STRUCTURAL — m_icons is empty or m_icons[0] is null. Catches any future additive
        ///      path that forgets to seed the fallback (would re-open the crafting-UI crash).
        ///   2. FALLBACK — m_icons[0] is the shared Assets.FallbackIcon placeholder (reference
        ///      identity, via Unity's overloaded ==). Means the item's real PNG did NOT load and it
        ///      is wearing the magenta placeholder. The crafting UI is crash-safe (the fallback is
        ///      why) but the item has no real icon — ship the PNG.
        /// CLONE items whose own PNG failed show the DONOR's sprite (non-null, non-fallback) and so
        /// pass — that is correct and intended (a clone showing its donor icon is cosmetic, never
        /// crashed). There is deliberately no per-item "expected icon" map; the fallback-identity
        /// check is the exact resolution for the CRASH blind spot, which only additive items had.
        /// </summary>
        private static void CheckIcon(string? itemName, ItemDrop? drop, ref int errors, ref int iconChecks)
        {
            iconChecks++;

            var icons = drop?.m_itemData?.m_shared?.m_icons;
            if (icons == null || icons.Length == 0 || icons[0] == null)
            {
                Plugin.Log.LogError(
                    $"[Trailborne/SpecCheck] ICON MISSING (structural): {itemName} has an empty or null " +
                    "m_icons — the crafting UI would throw on selection. An additive item must seed " +
                    "Assets.FallbackIcon in TryConstructItemShell; a clone must inherit a donor icon.");
                errors++;
                return;
            }

            // Reference identity (Unity's overloaded ==): is the item still wearing the shared
            // fallback placeholder? If so, its real PNG never loaded.
            if (icons[0] == Assets.FallbackIcon)
            {
                Plugin.Log.LogError(
                    $"[Trailborne/SpecCheck] ICON MISSING: {itemName} is showing the fallback placeholder " +
                    "— its real icon PNG did not load (missing from the plugin folder?). The crafting UI " +
                    "is crash-safe (fallback) but the item has no real icon. Ship the PNG.");
                errors++;
            }
        }

        /// <summary>
        /// Sibling of <see cref="CheckIcon"/> — the fail-loud boot guard for the OTHER fresh-SharedData
        /// null landmine vanilla derefs without a guard: <c>m_attack</c> / <c>m_secondaryAttack</c>.
        /// A fresh <c>SharedData</c> (what <c>Assets.TryConstructItemShell</c> news) leaves both NULL
        /// (vanilla has no field initializer); vanilla <c>ItemData.GetChainTooltip</c> reads
        /// <c>m_attack.m_spawnOnHitChance</c> with no null check, and the static <c>GetTooltip</c>
        /// calls it UNCONDITIONALLY every frame from <c>InventoryGui.UpdateRecipe</c> — so a
        /// constructed item with a null <c>m_attack</c> throws a per-frame NRE the instant its recipe
        /// is selected (the "wall of red"). <c>TryConstructItemShell</c> now seeds an inert
        /// <c>new Attack()</c> for both; this assertion SCREAMS at boot if a future additive path ever
        /// forgets, exactly as the C1 icon assertion does. CLONE items deep-copy a non-null donor
        /// <c>m_attack</c> and so pass — correct and intended; there is deliberately no per-item map,
        /// the null check IS the exact resolution for the crash blind spot only constructed items had.
        /// </summary>
        private static void CheckAttack(string? itemName, ItemDrop? drop, ref int errors, ref int attackChecks)
        {
            attackChecks++;

            var shared = drop?.m_itemData?.m_shared;
            if (shared == null)
                return;   // a null SharedData is its own (different) bug; CheckIcon already flags the item.

            if (shared.m_attack == null || shared.m_secondaryAttack == null)
            {
                Plugin.Log.LogError(
                    $"[Trailborne/SpecCheck] ATTACK NULL (structural): {itemName} has a null m_attack " +
                    "or m_secondaryAttack — vanilla GetChainTooltip derefs it every frame the recipe is " +
                    "selected, throwing a per-frame NRE in the craft panel. An additive item must seed " +
                    "`new Attack()` for both in TryConstructItemShell (beside the m_icons pre-seed); a clone " +
                    "inherits the donor's non-null attack.");
                errors++;
            }
        }

        /// <summary>
        /// FOOD-AS-FUEL Portal Energy boot assertion (card t_6e992a30, C2 — impl-spec §5.8). The cost
        /// model has no recipe, so its drift backstop is this: the design-doc §6 LOCKED baselines, written
        /// here as INDEPENDENT literals, diffed against the <see cref="PortalEnergyMath"/> Default* consts
        /// the runtime + config + unit tests actually read. If a Default* const is fat-fingered, or a
        /// design number moves without updating the code, the two disagree and this ERROR-logs at boot —
        /// the same fail-loud discipline the recipe manifest uses. It ALSO verifies the two derived design
        /// ANCHORS hold (30 m/berry × 10 berries == the 300 m portal ceiling), so a berry-conversion change
        /// that silently breaks "10 berries = a max jump" is caught too.
        /// </summary>
        private static void CheckPortalEnergyManifest(ref int errors, ref int peChecks)
        {
            // The locked baselines (docs/design/twisted-portal-food-charge.md §6 / impl-spec §5.8),
            // re-typed here so this assertion is independent of the consts it checks.
            CheckConst("PE tier divisor (/30 slope, §2/§6.3)",        30f, PortalEnergyMath.DefaultTierDivisor, ref errors, ref peChecks);
            CheckConst("PE tier clamp LO (floor 1.0, §2/§6.3)",        1f, PortalEnergyMath.DefaultTierClampLo, ref errors, ref peChecks);
            CheckConst("PE tier clamp HI (ceiling 5.0, §2/§6.3)",      5f, PortalEnergyMath.DefaultTierClampHi, ref errors, ref peChecks);
            CheckConst("PE meters-per-PE (baseline 1.0, §5.3)",        1f, PortalEnergyMath.DefaultMetersPerPe, ref errors, ref peChecks);
            CheckConst("PE feast range cap (28 m, §4/§6.2)",          28f, PortalEnergyMath.DefaultFeastRangeCapMinutes, ref errors, ref peChecks);
            CheckConst("PE meters-per-Bukeberry (30 m, §5/§6.5)",     30f, PortalEnergyMath.DefaultBukeMetersPerBerry, ref errors, ref peChecks);

            // Derived anchor: a from-empty MAX jump (the 300 m portal ceiling) costs EXACTLY 10 berries.
            // ceil(300 / 30) must equal 10 — the locked "300 yards for 10 Bukeperries" (design §5). This
            // ties the berry conversion to the ceiling so a 30→X m/berry change that breaks the "10 = max"
            // contract screams even though each individual const still "looks" fine.
            peChecks++;
            int ceilingBerries = PortalEnergyMath.BerriesForShortfall(
                PortalEnergyMath.PortalCeilingMeters, PortalEnergyMath.DefaultBukeMetersPerBerry);
            if (ceilingBerries != PortalEnergyMath.CeilingBerryCost)
            {
                Plugin.Log.LogError(
                    $"[Trailborne/SpecCheck] PE ANCHOR DRIFT: a from-empty {PortalEnergyMath.PortalCeilingMeters:F0} m " +
                    $"max jump costs {ceilingBerries} Bukeberries, but the locked design anchor is " +
                    $"{PortalEnergyMath.CeilingBerryCost} (300 m ÷ 30 m/berry). The berry conversion or the portal " +
                    "ceiling drifted — re-read twisted-portal-food-charge.md §5.");
                errors++;
            }

            // Derived anchor: the tier curve snaps to 0.5 rungs and respects the [lo,hi] clamp. Spot-check
            // two design-doc §3 worked values so a rounding/clamp regression (e.g. ToEven, or a dropped
            // clamp) is caught: Marinated greens (143 stats → tier 5.0, the lone ceiling) and Raspberries
            // (27 stats → tier 1.0, the floor). round(clamp(143/30,1,5)*2)/2 = round(4.766*2)/2 = 5.0;
            // round(clamp(27/30,1,5)*2)/2 = round(max(0.9,1)*2)/2 = round(2)/2 = 1.0.
            peChecks++;
            float tierGreens = PortalEnergyMath.Tier(143f);
            float tierRasp   = PortalEnergyMath.Tier(27f);
            if (tierGreens != 5.0f || tierRasp != 1.0f)
            {
                Plugin.Log.LogError(
                    $"[Trailborne/SpecCheck] PE TIER CURVE DRIFT: design §3 anchors mis-priced — Marinated greens " +
                    $"(143 stats) → {tierGreens} (locked 5.0), Raspberries (27 stats) → {tierRasp} (locked 1.0). " +
                    "The /30 divisor, the [1,5] clamp, or the 0.5-rung snap regressed.");
                errors++;
            }
        }

        private static void CheckConst(string label, float expected, float actual, ref int errors, ref int peChecks)
        {
            peChecks++;
            // Exact equality is correct here: these are hand-authored baseline literals, not computed
            // floats, so any difference is a real drift (not FP noise).
            if (expected != actual)
            {
                Plugin.Log.LogError(
                    $"[Trailborne/SpecCheck] PE CONST DRIFT — {label}: locked baseline '{expected}', " +
                    $"PortalEnergyMath default '{actual}'. Update BOTH the const AND the design doc in one commit.");
                errors++;
            }
        }
    }
}
