using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Adapters.Archer;
using SBPR.Niflheim.HomesteadStones.Adapters.Cooking;
using SBPR.Niflheim.HomesteadStones.Adapters.Crafting;
using SBPR.Niflheim.HomesteadStones.Adapters.Warrior;
using SBPR.Niflheim.HomesteadStones.Application.Diagnostics;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Recovery;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    // T036 — the progression runtime conformance surface (plan.md §"Runtime conformance and
    // observability"; spec FR-027). This is the THIRD member of this repo's boot-time drift-guard
    // family and it is deliberately shaped like the other two:
    //
    //   * SBPR.Trailborne/Runtime/SpecCheck.cs   — the LOCKED recipe/piece manifest lives in CODE and
    //     is compared against what actually registered. Screams at ERROR, never bricks.
    //   * Features/Diagnostics/PatchCheck.cs     — every [HarmonyPatch] class must be woven. Reports
    //     the two failure modes distinctly. Screams at ERROR, never bricks.
    //   * THIS FILE                              — the authored PROGRESSION manifest (registry/palette
    //     version, Facets, Trees, the exact 20 stable node ids with their executable/unavailable
    //     status, the required command handlers, the required runtime providers and the required
    //     patch classes) lives in CODE and is compared against the live catalog/palette plus what the
    //     caller actually OBSERVED. Screams at ERROR, never bricks.
    //
    // ── The honesty contract, inherited verbatim from OperatorShapeReport (ADO #123) ──────────────
    // A GREEN CONFORMANCE REPORT PROVES SHAPE, NEVER PLAYABILITY. Every assertion below is about
    // identity, counts, composition and registration. NONE of it proves a joined client can develop,
    // purchase, craft, place or feel any of it. The caveat is RENDERED INTO THE REPORT TEXT — the
    // same rule OperatorShapeReport established, for the same reason — and it is asserted by test so
    // it cannot be silently dropped.
    //
    // Corollary: this surface prefers UNKNOWN over a guessed green. Nothing here reflects over the
    // runtime to infer wiring or weaving; the caller passes what it actually observed, and anything
    // it did not observe is reported NOT CHECKED (a WARNING), never PASS.
    //
    // ── Why counts are not re-tallied here ───────────────────────────────────────────────────────
    // The enumerated Tree/node counts come from OperatorShapeReport.Build, which is already the ONE
    // counting path over the catalog. Adding a second tally here would create exactly the drift this
    // family exists to catch. What THIS file adds on top is the EXPECTED manifest to compare that
    // enumeration against — SpecCheck's trick, applied to progression content.
    //
    // ── Config gating and PII ────────────────────────────────────────────────────────────────────
    // The plan requires config-gated diagnostics that identify the operation/Stone/character/rejection
    // "without logging secrets or raw PII". This surface satisfies that STRUCTURALLY, which is
    // stronger than satisfying it by discipline: its inputs are the authored content catalog, the
    // Facet palette, handler/patch NAMES, and COUNTS of ReceiptRecovery verdicts. It never accepts an
    // AccountId, CharacterId, SteamID, principal, world path, integrity key or journal payload, so
    // there is no code path by which it could emit one. Recovery is reported as four integers; the
    // per-operation ids ReceiptRecovery knows are deliberately NOT surfaced (asserted by test).
    // The verbose per-node/per-handler render is gated behind a server-owned config flag at the call
    // site (Plugin.Awake); the one-line verdict and any ERROR finding are always on, because a guard
    // you can switch off is not a guard.
    //
    // net48 audit: System / System.Collections.Generic / System.Text / System.Globalization plus the
    // shipped engine-free catalog, palette, diagnostics and recovery types. No UnityEngine / Valheim /
    // BepInEx / Harmony, so this whole file link-compiles into the net8 test project and is fully
    // unit-tested. Its net48 caller (Plugin.Awake) is a THIN CALLER that owns no logic.

    /// <summary>Severity of one conformance finding. <see cref="Error"/> means authored truth and live
    /// truth disagree; <see cref="Warning"/> means something was NOT CHECKED or needs an operator
    /// decision; <see cref="Info"/> is context.</summary>
    public enum ConformanceSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>One conformance finding. <see cref="Code"/> is a stable, greppable token so a live log
    /// line can be searched for without parsing prose.</summary>
    public readonly struct ConformanceFinding
    {
        public ConformanceFinding(ConformanceSeverity severity, string code, string message)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public ConformanceSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
    }

    /// <summary>One authored expectation about a node's stable identity. This is the progression
    /// equivalent of SpecCheck's recipe manifest row: it lives in code, and it must be edited in the
    /// same commit as the catalog and the spec.</summary>
    public readonly struct ExpectedNode
    {
        public ExpectedNode(string treeKey, string nodeKey, int nodeVersion, int treeLevel,
            NodeFirstBuildStatus status)
        {
            TreeKey = treeKey ?? string.Empty;
            NodeKey = nodeKey ?? string.Empty;
            NodeVersion = nodeVersion;
            TreeLevel = treeLevel;
            Status = status;
        }

        public string TreeKey { get; }
        public string NodeKey { get; }
        public int NodeVersion { get; }
        public int TreeLevel { get; }
        public NodeFirstBuildStatus Status { get; }
    }

    /// <summary>One authored expectation that a named runtime PROVIDER exists for an executable node
    /// and advertises the exact current-build node identity it serves.
    ///
    /// <see cref="AdvertisedNode"/> is read from the provider's own published constant wherever the
    /// provider publishes one, so a rename or version bump there breaks this manifest loudly instead
    /// of drifting quietly. Where a shipped provider publishes no constant (it takes the node id as a
    /// constructor/parameter argument instead), the id is written literally and <see cref="Note"/>
    /// says so — an honest weaker binding, not a hidden one.</summary>
    public readonly struct ExpectedProvider
    {
        public ExpectedProvider(string nodeKey, string? providerTypeName, VersionedId advertisedNode, string note)
        {
            NodeKey = nodeKey ?? string.Empty;
            ProviderTypeName = providerTypeName;
            AdvertisedNode = advertisedNode;
            Note = note ?? string.Empty;
        }

        /// <summary>The executable node this provider is expected to serve.</summary>
        public string NodeKey { get; }

        /// <summary>The provider type, or null when this build ships NO runtime provider for the node
        /// — a real, reportable gap rather than an omission from the manifest.</summary>
        public string? ProviderTypeName { get; }

        /// <summary>The node identity the provider itself advertises (its published constant where one
        /// exists). Compared against the catalog so a provider bound to a stale version is caught.</summary>
        public VersionedId AdvertisedNode { get; }

        public string Note { get; }
    }

    /// <summary>The immutable conformance verdict. <see cref="Passed"/> is false when ANY finding is an
    /// error; warnings never mask an error and an error is never downgraded to a warning.</summary>
    public sealed class ProgressionConformanceResult
    {
        internal ProgressionConformanceResult(
            IReadOnlyList<ConformanceFinding> findings,
            OperatorShapeSnapshot shape,
            int checksPerformed)
        {
            Findings = findings;
            Shape = shape;
            ChecksPerformed = checksPerformed;

            int errors = 0, warnings = 0;
            foreach (var f in findings)
            {
                if (f.Severity == ConformanceSeverity.Error) errors++;
                else if (f.Severity == ConformanceSeverity.Warning) warnings++;
            }
            ErrorCount = errors;
            WarningCount = warnings;
        }

        public IReadOnlyList<ConformanceFinding> Findings { get; }

        /// <summary>The enumerated shape snapshot this verdict was computed against. Reused from
        /// <see cref="OperatorShapeReport"/> so there is exactly one counting path.</summary>
        public OperatorShapeSnapshot Shape { get; }

        /// <summary>How many expectations were actually compared. A conformance run that checked
        /// nothing must not read as a pass, so this is rendered next to the verdict.</summary>
        public int ChecksPerformed { get; }

        public int ErrorCount { get; }
        public int WarningCount { get; }

        /// <summary>No ERROR finding. NOT a claim that anything is playable.</summary>
        public bool Passed => ErrorCount == 0;
    }

    /// <summary>The engine-free progression conformance manifest + verifier.</summary>
    public static class ProgressionConformance
    {
        /// <summary>Rendered into every report. Kept public and const so a test can assert the text
        /// carries it verbatim — the disclaimer is part of the deliverable.</summary>
        public const string ShapeNotPlayabilityCaveat =
            "A GREEN CONFORMANCE REPORT PROVES SHAPE, NEVER PLAYABILITY. Everything above is about "
            + "authored identity, counts, composition and registration. It says NOTHING about whether a "
            + "joined client can develop, purchase, craft, place or feel any of it. Verify in-world "
            + "before concluding the game works.";

        // ── The authored expectations. Spec-first: these MUST move in the same commit as the catalog,
        //    the palette and docs/v2/planning/homestead-stone-progression-data-model.md. ───────────

        /// <summary>Expected content-registry version (data-model.md §"Core definitions").</summary>
        public const int ExpectedContentRegistryVersion = 1;

        /// <summary>Expected Facet palette version.</summary>
        public const int ExpectedPaletteVersion = 1;

        /// <summary>Expected classification of this build's authored family/variant.</summary>
        public const string ExpectedFamily = "Settlement";
        public const string ExpectedVariant = "Homestead";

        /// <summary>Authored Facet expectations: exactly one Profession and one Martial Facet, with the
        /// exact candidate Tree sets (data-model.md invariants).</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, FacetCategory>> ExpectedFacets =
            new List<KeyValuePair<string, FacetCategory>>
            {
                new KeyValuePair<string, FacetCategory>(HomesteadProgressionCatalog.ProfessionFacetId, FacetCategory.Profession),
                new KeyValuePair<string, FacetCategory>(HomesteadProgressionCatalog.MartialFacetId, FacetCategory.Martial),
            };

        /// <summary>Expected candidate Trees per Facet id, in authored order.</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, string[]>> ExpectedFacetCandidates =
            new List<KeyValuePair<string, string[]>>
            {
                new KeyValuePair<string, string[]>(HomesteadProgressionCatalog.ProfessionFacetId, new[] { "Cooking", "Crafting" }),
                new KeyValuePair<string, string[]>(HomesteadProgressionCatalog.MartialFacetId, new[] { "Archer", "Warrior" }),
            };

        /// <summary>The exact 20-row authored roster: stable id, version, owning Tree, Tree level and
        /// first-build status. 20 = 13 executable + 7 unavailable (data-model.md arithmetic invariant).
        /// Ordering is irrelevant to the comparison; membership and every field are not.</summary>
        public static readonly IReadOnlyList<ExpectedNode> ExpectedNodes = new List<ExpectedNode>
        {
            // Cooking
            new ExpectedNode("Cooking",  "SavorTheHearth",   1, 1, NodeFirstBuildStatus.Executable),
            new ExpectedNode("Cooking",  "FieldPrep",        1, 1, NodeFirstBuildStatus.Executable),
            new ExpectedNode("Cooking",  "IronStomach",      1, 1, NodeFirstBuildStatus.Executable),
            new ExpectedNode("Cooking",  "SwiftPreparation", 1, 2, NodeFirstBuildStatus.Executable),
            new ExpectedNode("Cooking",  "WatchfulCook",     1, 2, NodeFirstBuildStatus.Unavailable),

            // Crafting
            new ExpectedNode("Crafting", "RefinedWorkshop",  1, 1, NodeFirstBuildStatus.Executable),
            new ExpectedNode("Crafting", "Masterwork",       1, 1, NodeFirstBuildStatus.Executable),
            new ExpectedNode("Crafting", "BuiltToLast",      1, 1, NodeFirstBuildStatus.Executable),
            new ExpectedNode("Crafting", "MeasuredCuts",     1, 1, NodeFirstBuildStatus.Unavailable),
            new ExpectedNode("Crafting", "ArtisansCounter",  1, 1, NodeFirstBuildStatus.Unavailable),

            // Archer
            new ExpectedNode("Archer",   "PracticeRange",    1, 1, NodeFirstBuildStatus.Executable),
            new ExpectedNode("Archer",   "FieldFletchingI",  1, 1, NodeFirstBuildStatus.Executable),
            new ExpectedNode("Archer",   "FletchersHabit",   1, 1, NodeFirstBuildStatus.Executable),
            new ExpectedNode("Archer",   "SteadyAim",        1, 1, NodeFirstBuildStatus.Unavailable),
            new ExpectedNode("Archer",   "BowyersLore",      1, 1, NodeFirstBuildStatus.Unavailable),

            // Warrior
            new ExpectedNode("Warrior",  "TwigTraining",     1, 1, NodeFirstBuildStatus.Executable),
            new ExpectedNode("Warrior",  "ReadyHands",       1, 1, NodeFirstBuildStatus.Executable),
            new ExpectedNode("Warrior",  "WeaponDiscipline", 1, 1, NodeFirstBuildStatus.Executable),
            new ExpectedNode("Warrior",  "ShrugItOffI",      1, 1, NodeFirstBuildStatus.Unavailable),
            new ExpectedNode("Warrior",  "HeavyHands",       1, 1, NodeFirstBuildStatus.Unavailable),
        };

        /// <summary>The command handlers a composed progression runtime must hold. Names are the ones
        /// <see cref="HomesteadHandlerWiringObserver"/> reports, so the two cannot drift apart.
        ///
        /// WeaponDisciplineCommandHandler is deliberately absent from this REQUIRED list: ADO #123
        /// already established that no composition root constructs one, and encoding a known gap as a
        /// boot ERROR would train an operator to ignore this guard. It is reported as an informational
        /// observation instead — a gap that is named, not hidden, and not yet load-bearing.</summary>
        public static readonly IReadOnlyList<string> RequiredHandlers = new List<string>
        {
            HomesteadHandlerWiringObserver.Relationship,
            HomesteadHandlerWiringObserver.Activity,
            HomesteadHandlerWiringObserver.Development,
            HomesteadHandlerWiringObserver.Facet,
            HomesteadHandlerWiringObserver.LocalPolicy,
        };

        /// <summary>The [HarmonyPatch] classes whose absence would make a SHIPPED progression outcome
        /// inert in-world. This is the specific, named counterpart to PatchCheck's general net: PatchCheck
        /// proves "every attributed class wove at least once", this proves "these exact progression seams
        /// are among them". The distinction matters because a class can be deleted or renamed out of the
        /// assembly entirely — PatchCheck then sees nothing missing (it enumerates what EXISTS), while
        /// this manifest still demands it. That is the ADO #125 failure mode from the other direction.
        ///
        /// Simple type names, compared case-sensitively against whatever the caller observed as woven.</summary>
        public static readonly IReadOnlyList<string> RequiredPatchClasses = new List<string>
        {
            // Composition + credit path.
            "FoundationalRuntimeBootstrap",
            "FoundationalPlacementObserver",
            "DedicatedPlacementIngressObserver",
            // Read-model transports without which every joined-client outcome fails closed.
            "LocalActivationDeliveryObserver",
            "PersonalActivationDeliveryObserver",
            // Per-family outcome seams (one per executable node that HAS a runtime seam).
            "SavorFoodTimerObserver",
            "FieldPrepRecipeGate",
            "IronStomachRefreshGate",
            "SwiftPreparationCraftTimer",
            "RefinedWorkshopStationLevelPatch",
            "MasterworkIssuanceObserver",
            "ArcherContentRegistrar",
            "ArcheryTargetPlacementGate",
            "FieldFletchingRecipeGate",
            "WarriorTwigPlacementObserver",
            "ReadyHandsEquipDurationPatch",
        };

        /// <summary>Expected runtime provider per EXECUTABLE node. Where the provider publishes its own
        /// node constant we read THAT constant, so a drift there fails this check instead of hiding.</summary>
        public static readonly IReadOnlyList<ExpectedProvider> ExpectedProviders = new List<ExpectedProvider>
        {
            new ExpectedProvider("SavorTheHearth", "SavorTheHearthProvider", CookingNodes.SavorTheHearth,
                "published constant CookingNodes.SavorTheHearth"),
            new ExpectedProvider("FieldPrep", "CookingCraftPolicy", CookingCraftPolicy.FieldPrepNode,
                "published constant CookingCraftPolicy.FieldPrepNode"),
            new ExpectedProvider("IronStomach", "FoodRefreshThresholdProvider", FoodRefreshThresholdProvider.IronStomachNode,
                "published constant FoodRefreshThresholdProvider.IronStomachNode"),
            new ExpectedProvider("SwiftPreparation", "MenuCraftDurationProvider", SwiftPreparationNodes.SwiftPreparation,
                "published constant SwiftPreparationNodes.SwiftPreparation"),

            new ExpectedProvider("RefinedWorkshop", "EffectiveStationLevelProvider", new VersionedId("RefinedWorkshop", 1),
                "provider publishes no node constant — it takes the node id as a parameter, so this "
                + "binding is a literal and is weaker than the constant-backed rows"),
            new ExpectedProvider("Masterwork", "WorkmanshipIssuanceProvider", WorkmanshipIssuanceProvider.MasterworkNode,
                "published constant WorkmanshipIssuanceProvider.MasterworkNode"),
            new ExpectedProvider("BuiltToLast", null, new VersionedId("BuiltToLast", 1),
                "no runtime provider ships in this build for the authored Built to Last node"),

            new ExpectedProvider("PracticeRange", "PracticeRangeProvider", PracticeRangeProvider.PracticeRangeNode,
                "published constant PracticeRangeProvider.PracticeRangeNode"),
            new ExpectedProvider("FieldFletchingI", "BushcraftRecipeProvider", BushcraftRecipeProvider.FieldFletchingNode,
                "published constant BushcraftRecipeProvider.FieldFletchingNode"),
            new ExpectedProvider("FletchersHabit", null, new VersionedId("FletchersHabit", 1),
                "no runtime provider ships in this build for the authored Fletcher's Habit node"),

            new ExpectedProvider("TwigTraining", "LocalPlacementProvider", new VersionedId("TwigTraining", 1),
                "provider publishes no node constant — its parameterless constructor binds this id, so "
                + "this binding is a literal and is weaker than the constant-backed rows"),
            new ExpectedProvider("ReadyHands", "EquipDurationProvider", WarriorNodes.ReadyHands,
                "published constant WarriorNodes.ReadyHands"),
            new ExpectedProvider("WeaponDiscipline", "SkillCapProvider", WeaponDisciplineNode.WeaponDiscipline,
                "published constant WeaponDisciplineNode.WeaponDiscipline"),
        };

        /// <summary>Run the conformance comparison.
        ///
        /// <paramref name="handlers"/> and <paramref name="wovenPatchClasses"/> are OBSERVATIONS. Pass
        /// null for either when you did not look — the result then carries a NOT CHECKED warning rather
        /// than a green. <paramref name="recovery"/> is likewise optional and reported as counts only.</summary>
        public static ProgressionConformanceResult Verify(
            HomesteadProgressionCatalog catalog,
            StoneFacetPalette palette,
            IReadOnlyList<HandlerWiring>? handlers = null,
            IReadOnlyCollection<string>? wovenPatchClasses = null,
            ReceiptRecovery? recovery = null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (palette == null) throw new ArgumentNullException(nameof(palette));

            var findings = new List<ConformanceFinding>();
            int checks = 0;

            // (1) Registry / palette version + classification identity.
            checks++;
            if (catalog.ContentRegistryVersion != ExpectedContentRegistryVersion)
                findings.Add(Error("REGISTRY-VERSION",
                    "content registry version is " + Num(catalog.ContentRegistryVersion)
                    + ", manifest expects " + Num(ExpectedContentRegistryVersion)
                    + " — bump this manifest and the data model in the same commit."));

            checks++;
            if (palette.PaletteVersion != ExpectedPaletteVersion)
                findings.Add(Error("PALETTE-VERSION",
                    "Facet palette version is " + Num(palette.PaletteVersion)
                    + ", manifest expects " + Num(ExpectedPaletteVersion) + "."));

            checks++;
            if (!string.Equals(catalog.Family, ExpectedFamily, StringComparison.Ordinal)
                || !string.Equals(catalog.Variant, ExpectedVariant, StringComparison.Ordinal))
                findings.Add(Error("CLASSIFICATION",
                    "classification is " + catalog.Family + "/" + catalog.Variant
                    + ", manifest expects " + ExpectedFamily + "/" + ExpectedVariant + "."));

            // (2) Facets — count, categories, candidate Tree sets.
            checks += VerifyFacets(palette, findings);

            // (3) Trees / nodes / executable-vs-unavailable status + stable ids.
            //     Counts come from the ONE existing counting path; the manifest below is what they are
            //     compared against.
            OperatorShapeSnapshot shape = OperatorShapeReport.Build(catalog, handlers, recovery);
            checks += VerifyRoster(catalog, shape, findings);

            // (4) Required command handlers.
            checks += VerifyHandlers(handlers, findings);

            // (5) Required patch-class registration.
            checks += VerifyPatchClasses(wovenPatchClasses, findings);

            // (6) Required runtime providers per executable node.
            checks += VerifyProviders(catalog, findings);

            // (7) Startup recovery — counts only, reusing ReceiptRecovery's own verdicts via the shape
            //     snapshot. Never classifies a journal record and never names an operation id.
            checks += VerifyRecovery(shape.Recovery, findings);

            return new ProgressionConformanceResult(findings, shape, checks);
        }

        private static int VerifyFacets(StoneFacetPalette palette, List<ConformanceFinding> findings)
        {
            int checks = 1;
            if (palette.Facets.Count != ExpectedFacets.Count)
                findings.Add(Error("FACET-COUNT",
                    "palette authors " + Num(palette.Facets.Count) + " Facet(s), manifest expects "
                    + Num(ExpectedFacets.Count) + " (exactly one Profession and one Martial)."));

            foreach (var expected in ExpectedFacets)
            {
                checks++;
                var facet = palette.TryGetFacet(expected.Key);
                if (facet == null)
                {
                    findings.Add(Error("FACET-MISSING", "authored Facet '" + expected.Key + "' is absent from the live palette."));
                    continue;
                }

                if (facet.Category != expected.Value)
                    findings.Add(Error("FACET-CATEGORY",
                        "Facet '" + expected.Key + "' has category " + facet.Category
                        + ", manifest expects " + expected.Value + "."));
            }

            foreach (var expected in ExpectedFacetCandidates)
            {
                checks++;
                var facet = palette.TryGetFacet(expected.Key);
                if (facet == null) continue; // already reported above

                var actual = new List<string>();
                foreach (var c in facet.Candidates) actual.Add(c.Key + "@v" + Num(c.Version));

                var wanted = new List<string>();
                foreach (var k in expected.Value) wanted.Add(k + "@v" + Num(ExpectedContentRegistryVersion));

                if (!SameSet(actual, wanted))
                    findings.Add(Error("FACET-CANDIDATES",
                        "Facet '" + expected.Key + "' candidates are [" + string.Join(", ", actual.ToArray())
                        + "], manifest expects [" + string.Join(", ", wanted.ToArray()) + "]."));
            }

            return checks;
        }

        private static int VerifyRoster(HomesteadProgressionCatalog catalog, OperatorShapeSnapshot shape,
            List<ConformanceFinding> findings)
        {
            int checks = 0;

            // The catalog's own declared constants vs its enumerated roster. OperatorShapeReport already
            // computes this disagreement; conformance escalates it from "reported" to "ERROR".
            checks++;
            foreach (string mismatch in shape.CatalogDeclarationMismatches)
                findings.Add(Error("CATALOG-SELF-DESCRIPTION",
                    "the catalog's declared constants disagree with its own roster — " + mismatch));

            // Enumerated counts vs THIS manifest.
            int expectedExecutable = 0, expectedUnavailable = 0;
            foreach (var e in ExpectedNodes)
            {
                if (e.Status == NodeFirstBuildStatus.Executable) expectedExecutable++;
                else expectedUnavailable++;
            }

            checks++;
            if (shape.TotalNodes != ExpectedNodes.Count)
                findings.Add(Error("NODE-COUNT",
                    "catalog enumerates " + Num(shape.TotalNodes) + " authored node(s), manifest expects "
                    + Num(ExpectedNodes.Count) + "."));

            checks++;
            if (shape.ExecutableNodes != expectedExecutable || shape.UnavailableNodes != expectedUnavailable)
                findings.Add(Error("NODE-STATUS-COUNT",
                    "catalog enumerates " + Num(shape.ExecutableNodes) + " executable / "
                    + Num(shape.UnavailableNodes) + " unavailable, manifest expects "
                    + Num(expectedExecutable) + " / " + Num(expectedUnavailable) + "."));

            // Stable ids: every expected node must resolve at its exact current-build version, in the
            // expected Tree, at the expected level, with the expected first-build status.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in ExpectedNodes)
            {
                checks++;
                seen.Add(e.NodeKey);

                var id = new VersionedId(e.NodeKey, e.NodeVersion);
                var def = catalog.TryResolveNode(id);
                if (def == null)
                {
                    findings.Add(Error("NODE-MISSING",
                        catalog.HasNodeKey(id)
                            ? "node '" + e.NodeKey + "' exists but not at expected version v" + Num(e.NodeVersion)
                              + " — a version bump must move this manifest, the data model and any provider together."
                            : "node '" + e.NodeKey + "' is absent from the current-build roster."));
                    continue;
                }

                if (!string.Equals(def.Tree.Key, e.TreeKey, StringComparison.Ordinal))
                    findings.Add(Error("NODE-TREE",
                        "node '" + e.NodeKey + "' belongs to Tree '" + def.Tree.Key + "', manifest expects '" + e.TreeKey + "'."));

                if (def.TreeLevel != e.TreeLevel)
                    findings.Add(Error("NODE-LEVEL",
                        "node '" + e.NodeKey + "' is Tree level " + Num(def.TreeLevel)
                        + ", manifest expects " + Num(e.TreeLevel) + "."));

                if (def.Status != e.Status)
                    findings.Add(Error("NODE-STATUS",
                        "node '" + e.NodeKey + "' is " + def.Status + ", manifest expects " + e.Status
                        + " — an unavailable node that became executable (or vice versa) is a content decision, "
                        + "not a drift to absorb."));
            }

            // The other direction: a node the catalog ships that this manifest never authorized.
            foreach (var def in catalog.Nodes)
            {
                checks++;
                if (!seen.Contains(def.Node.Key))
                    findings.Add(Error("NODE-UNEXPECTED",
                        "catalog ships node '" + def.Node.Key + "' (" + def.Tree.Key
                        + ") which this manifest does not authorize — add it here, to the data model and to the "
                        + "spec in the same commit."));
            }

            return checks;
        }

        private static int VerifyHandlers(IReadOnlyList<HandlerWiring>? handlers, List<ConformanceFinding> findings)
        {
            if (handlers == null)
            {
                findings.Add(Warning("HANDLERS-NOT-CHECKED",
                    "no handler observations supplied — command-handler composition was NOT CHECKED. This is "
                    + "not a pass."));
                return 1;
            }

            var byName = new Dictionary<string, WiringState>(StringComparer.Ordinal);
            foreach (var h in handlers) byName[h.HandlerName] = h.State;

            int checks = 0;
            foreach (string required in RequiredHandlers)
            {
                checks++;
                if (!byName.TryGetValue(required, out WiringState state))
                {
                    findings.Add(Warning("HANDLER-NOT-CHECKED",
                        "required handler '" + required + "' was not observed at all — NOT CHECKED, not a pass."));
                    continue;
                }

                if (state == WiringState.Composed) continue;

                if (state == WiringState.NotComposed)
                    findings.Add(Error("HANDLER-NOT-COMPOSED",
                        "required handler '" + required + "' is NOT composed into the live runtime — its command "
                        + "path is inert."));
                else
                    findings.Add(Warning("HANDLER-NOT-CHECKED",
                        "required handler '" + required + "' reported NOT CHECKED."));
            }

            // Known, named, non-required gaps: reported so they stay visible without being alarms.
            foreach (var h in handlers)
            {
                if (Contains(RequiredHandlers, h.HandlerName)) continue;
                if (h.State == WiringState.Composed) continue;
                checks++;
                findings.Add(Info("HANDLER-OPTIONAL",
                    "handler '" + h.HandlerName + "' is " + (h.State == WiringState.NotComposed ? "NOT COMPOSED" : "NOT CHECKED")
                    + " and is not required by this manifest: " + h.Note));
            }

            return checks;
        }

        private static int VerifyPatchClasses(IReadOnlyCollection<string>? woven, List<ConformanceFinding> findings)
        {
            if (woven == null)
            {
                findings.Add(Warning("PATCHES-NOT-CHECKED",
                    "no woven-patch-class observation supplied — runtime seam registration was NOT CHECKED. "
                    + "This is not a pass."));
                return 1;
            }

            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (string w in woven)
            {
                if (string.IsNullOrEmpty(w)) continue;
                set.Add(w);
                // Accept a full name too, so the caller may pass either shape.
                int dot = w.LastIndexOf('.');
                if (dot >= 0 && dot < w.Length - 1) set.Add(w.Substring(dot + 1));
            }

            int checks = 0;
            foreach (string required in RequiredPatchClasses)
            {
                checks++;
                if (!set.Contains(required))
                    findings.Add(Error("PATCH-NOT-WOVEN",
                        "required runtime seam '" + required + "' produced no woven Harmony patch. Either "
                        + "Plugin.Awake() never handed it to PatchAll, or the class no longer exists. Its "
                        + "shipped outcome is INERT in-world while every unit test stays green."));
            }

            return checks;
        }

        private static int VerifyProviders(HomesteadProgressionCatalog catalog, List<ConformanceFinding> findings)
        {
            int checks = 0;

            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in ExpectedProviders)
            {
                checks++;
                covered.Add(p.NodeKey);

                // The identity the provider advertises must resolve in the current build. This is what
                // catches a provider left bound to a stale node version after a content bump.
                if (catalog.TryResolveNode(p.AdvertisedNode) == null)
                    findings.Add(Error("PROVIDER-STALE-BINDING",
                        "provider '" + (p.ProviderTypeName ?? "(none)") + "' advertises node "
                        + p.AdvertisedNode + ", which does not resolve in the current build."));
                else if (!string.Equals(p.AdvertisedNode.Key, p.NodeKey, StringComparison.Ordinal))
                    findings.Add(Error("PROVIDER-WRONG-NODE",
                        "provider '" + (p.ProviderTypeName ?? "(none)") + "' is manifested for node '"
                        + p.NodeKey + "' but advertises '" + p.AdvertisedNode.Key + "'."));

                if (p.ProviderTypeName == null)
                    findings.Add(Warning("PROVIDER-ABSENT",
                        "executable node '" + p.NodeKey + "' has NO runtime provider in this build: " + p.Note
                        + ". The node is authored and purchasable-shaped; nothing delivers its outcome."));
            }

            // Every executable node must appear in the provider manifest, even if only to record that no
            // provider exists. Silence is what this family exists to prevent.
            foreach (var def in catalog.Nodes)
            {
                if (!def.IsExecutable) continue;
                checks++;
                if (!covered.Contains(def.Node.Key))
                    findings.Add(Error("PROVIDER-UNMANIFESTED",
                        "executable node '" + def.Node.Key + "' is not named in the provider manifest — add a row "
                        + "(with a null provider if none ships) so the gap is stated rather than silent."));
            }

            return checks;
        }

        private static int VerifyRecovery(RecoverySummary recovery, List<ConformanceFinding> findings)
        {
            if (!recovery.Inspected)
            {
                findings.Add(Warning("RECOVERY-NOT-CHECKED",
                    "startup recovery was NOT CHECKED: " + recovery.Note));
                return 1;
            }

            if (recovery.Quarantine > 0)
                findings.Add(Warning("RECOVERY-QUARANTINE",
                    Num(recovery.Quarantine) + " durable operation(s) hold partial state with no terminal result. "
                    + "Nothing was repaired and nothing was guessed — an operator must decide."));

            return 1;
        }

        /// <summary>Render the verdict. One renderer, as with OperatorShapeReport — no second formatter
        /// to drift against.
        ///
        /// <paramref name="verbose"/> is the config-gated detail: when false the report is the verdict
        /// plus every WARNING and ERROR (an operator always sees the problems); when true it also lists
        /// the enumerated shape and the informational findings.</summary>
        public static string Render(ProgressionConformanceResult result, bool verbose = false)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            var sb = new StringBuilder();
            sb.AppendLine("=== Homestead progression — runtime conformance ===");
            sb.AppendLine("  verdict:  " + (result.Passed ? "PASS (shape only)" : "FAIL")
                + "  errors=" + Num(result.ErrorCount)
                + " warnings=" + Num(result.WarningCount)
                + " checks=" + Num(result.ChecksPerformed));
            sb.AppendLine("  registry: v" + Num(result.Shape.ContentRegistryVersion)
                + "  " + result.Shape.Family + "/" + result.Shape.Variant
                + "  trees=" + Num(result.Shape.Trees.Count)
                + "  nodes=" + Num(result.Shape.TotalNodes)
                + " (" + Num(result.Shape.ExecutableNodes) + " executable, "
                + Num(result.Shape.UnavailableNodes) + " unavailable)");
            sb.AppendLine();

            bool any = false;
            foreach (var f in result.Findings)
            {
                if (!verbose && f.Severity == ConformanceSeverity.Info) continue;
                any = true;
                sb.AppendLine("  " + Label(f.Severity) + " [" + f.Code + "] " + f.Message);
            }
            if (!any) sb.AppendLine("  (no findings)");
            sb.AppendLine();

            if (verbose)
            {
                sb.AppendLine("-- enumerated trees --");
                foreach (var tree in result.Shape.Trees)
                    sb.AppendLine("  " + tree.TreeKey + " (v" + Num(tree.TreeVersion) + "): "
                        + Num(tree.TotalNodes) + " node(s), " + Num(tree.ExecutableNodes) + " executable, "
                        + Num(tree.UnavailableNodes) + " unavailable");
                sb.AppendLine();

                sb.AppendLine("-- honestly unavailable in this build --");
                if (result.Shape.UnavailableNodeLabels.Count == 0) sb.AppendLine("  (none)");
                else foreach (string label in result.Shape.UnavailableNodeLabels) sb.AppendLine("    - " + label);
                sb.AppendLine();

                sb.AppendLine("-- startup recovery (counts only; no operation ids, no principals) --");
                var r = result.Shape.Recovery;
                if (!r.Inspected) sb.AppendLine("  status: NOT CHECKED — " + r.Note);
                else sb.AppendLine("  durable operations=" + Num(r.DurableOperations)
                    + " clean=" + Num(r.Clean) + " recoverable=" + Num(r.Recoverable)
                    + " quarantine=" + Num(r.Quarantine));
                sb.AppendLine();
            }

            sb.AppendLine("-- LIMITS OF THIS REPORT --");
            sb.AppendLine("  " + ShapeNotPlayabilityCaveat);
            sb.AppendLine("  Anything reported NOT CHECKED was not inspected. It is not a pass.");
            return sb.ToString();
        }

        private static ConformanceFinding Error(string code, string message) =>
            new ConformanceFinding(ConformanceSeverity.Error, code, message);

        private static ConformanceFinding Warning(string code, string message) =>
            new ConformanceFinding(ConformanceSeverity.Warning, code, message);

        private static ConformanceFinding Info(string code, string message) =>
            new ConformanceFinding(ConformanceSeverity.Info, code, message);

        private static string Label(ConformanceSeverity severity)
        {
            switch (severity)
            {
                case ConformanceSeverity.Error: return "ERROR  ";
                case ConformanceSeverity.Warning: return "WARN   ";
                default: return "info   ";
            }
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            foreach (string s in list)
                if (string.Equals(s, value, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool SameSet(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            var set = new HashSet<string>(b, StringComparer.Ordinal);
            foreach (string s in a)
                if (!set.Remove(s)) return false;
            return set.Count == 0;
        }

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
