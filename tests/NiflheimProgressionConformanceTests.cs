// ============================================================================
//  T036 — the progression runtime conformance surface.
// ----------------------------------------------------------------------------
//  Executable evidence for the THIRD member of this repo's boot-time drift-guard
//  family (after SBPR.Trailborne/Runtime/SpecCheck.cs and
//  Features/Diagnostics/PatchCheck.cs).
//
//  What this suite proves:
//    * the SHIPPED catalog and Facet palette match the authored manifest exactly
//      — registry/palette version, classification, Facets + candidate Trees, and
//      all 20 stable node ids with their Tree, level and first-build status;
//    * the manifest's own arithmetic is the data model's (20 = 13 + 7) and its
//      provider rows cover every executable node;
//    * each check is NON-VACUOUS: a deliberately perturbed observation produces
//      the specific finding code, so a green run is not green-by-accident;
//    * NOT CHECKED is never collapsed into a pass — a missing observation is a
//      WARNING, and an un-run check never reads as PASS;
//    * a required patch class that did not weave is an ERROR (the ADO #125 /
//      IAP-015 defect family), including the case where the class was deleted
//      outright, which PatchCheck's enumerate-what-exists net cannot see;
//    * the report emits NO secrets and NO raw PII: recovery is counts only and
//      no operation id, account, character or path appears in the rendered text;
//    * the shape-not-playability caveat is present in the rendered output, with
//      a NEGATIVE control proving that assertion is non-vacuous.
//
//  HONEST SCOPE: every assertion below is about the conformance surface's OWN
//  correctness. None of it proves any reported-conformant thing works for a
//  player. That is exactly the distinction the report itself must state.
// ============================================================================

using System.Collections.Generic;
using System.IO;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Application.Diagnostics;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Features.Progression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Recovery;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimProgressionConformanceTests : System.IDisposable
    {
        private readonly string _dir;
        private readonly WorldId _world = new WorldId("uid:conformance-t036");
        private readonly StoneId _stone;
        private readonly AuthoritativePrincipal _owner =
            new AuthoritativePrincipal(new AccountId("acct-t036"), new CharacterId("char-t036"));

        public NiflheimProgressionConformanceTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "niflheim-t036-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _stone = StoneId.FromHostZone(_world, 3, 11);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        }

        // ── The shipped build conforms ───────────────────────────────────────

        [Fact]
        public void ShippedCatalogAndPalette_ConformToTheAuthoredManifest()
        {
            var result = Verify(AllComposed(), AllWoven());

            Assert.DoesNotContain(result.Findings, f => f.Severity == ConformanceSeverity.Error);
            Assert.True(result.Passed);
            Assert.True(result.ChecksPerformed > 40,
                "a conformance run that checked almost nothing must not read as a pass");
        }

        [Fact]
        public void Manifest_CarriesTheDataModelArithmetic_20Is13Plus7()
        {
            int executable = ProgressionConformance.ExpectedNodes.Count(n => n.Status == NodeFirstBuildStatus.Executable);
            int unavailable = ProgressionConformance.ExpectedNodes.Count(n => n.Status == NodeFirstBuildStatus.Unavailable);

            Assert.Equal(20, ProgressionConformance.ExpectedNodes.Count);
            Assert.Equal(13, executable);
            Assert.Equal(7, unavailable);

            // And the shipped catalog agrees, via the ONE counting path.
            var result = Verify(AllComposed(), AllWoven());
            Assert.Equal(20, result.Shape.TotalNodes);
            Assert.Equal(13, result.Shape.ExecutableNodes);
            Assert.Equal(7, result.Shape.UnavailableNodes);
        }

        [Fact]
        public void ManifestNodeIds_AreUnique_AndEveryOneResolvesInTheShippedCatalog()
        {
            var catalog = new HomesteadProgressionCatalog();
            var seen = new HashSet<string>();

            foreach (var expected in ProgressionConformance.ExpectedNodes)
            {
                Assert.True(seen.Add(expected.NodeKey), "duplicate manifest row for " + expected.NodeKey);

                var def = catalog.TryResolveNode(new VersionedId(expected.NodeKey, expected.NodeVersion));
                Assert.NotNull(def);
                Assert.Equal(expected.TreeKey, def!.Tree.Key);
                Assert.Equal(expected.TreeLevel, def.TreeLevel);
                Assert.Equal(expected.Status, def.Status);
            }
        }

        [Fact]
        public void ProviderManifest_NamesEveryExecutableNode_IncludingTheOnesWithNoProvider()
        {
            var catalog = new HomesteadProgressionCatalog();
            var manifested = new HashSet<string>(ProgressionConformance.ExpectedProviders.Select(p => p.NodeKey));

            foreach (var def in catalog.Nodes.Where(n => n.IsExecutable))
                Assert.Contains(def.Node.Key, manifested);

            // The gaps are STATED, not omitted: this build ships no provider for two authored nodes.
            var absent = ProgressionConformance.ExpectedProviders.Where(p => p.ProviderTypeName == null)
                .Select(p => p.NodeKey).OrderBy(k => k).ToArray();
            Assert.Equal(new[] { "BuiltToLast", "FletchersHabit" }, absent);

            // ...and each gap surfaces as a WARNING on a fully-observed run, never silence.
            var result = Verify(AllComposed(), AllWoven());
            Assert.Equal(2, result.Findings.Count(f => f.Code == "PROVIDER-ABSENT"));
            Assert.All(result.Findings.Where(f => f.Code == "PROVIDER-ABSENT"),
                f => Assert.Equal(ConformanceSeverity.Warning, f.Severity));
        }

        [Fact]
        public void EveryProviderRow_AdvertisesTheNodeItIsManifestedFor_AtACurrentBuildVersion()
        {
            var catalog = new HomesteadProgressionCatalog();
            foreach (var p in ProgressionConformance.ExpectedProviders)
            {
                Assert.Equal(p.NodeKey, p.AdvertisedNode.Key);
                Assert.NotNull(catalog.TryResolveNode(p.AdvertisedNode));
            }
        }

        // ── NOT CHECKED is never a pass ──────────────────────────────────────

        [Fact]
        public void NoHandlerObservation_IsWarnedAsNotChecked_NeverAssumedComposed()
        {
            var result = Verify(handlers: null, woven: AllWoven());

            Assert.Contains(result.Findings, f => f.Code == "HANDLERS-NOT-CHECKED");
            Assert.Equal(ConformanceSeverity.Warning,
                result.Findings.First(f => f.Code == "HANDLERS-NOT-CHECKED").Severity);

            string text = ProgressionConformance.Render(result);
            Assert.Contains("NOT CHECKED", text);
            Assert.Contains("Anything reported NOT CHECKED was not inspected. It is not a pass.", text);
        }

        [Fact]
        public void NoPatchObservation_IsWarnedAsNotChecked_NeverAssumedWoven()
        {
            var result = Verify(AllComposed(), woven: null);
            Assert.Contains(result.Findings, f => f.Code == "PATCHES-NOT-CHECKED");
            Assert.DoesNotContain(result.Findings, f => f.Code == "PATCH-NOT-WOVEN");
        }

        [Fact]
        public void NoRecoveryStore_IsWarnedAsNotChecked_NeverReportedAsACleanZero()
        {
            var result = Verify(AllComposed(), AllWoven(), recovery: null);
            Assert.Contains(result.Findings, f => f.Code == "RECOVERY-NOT-CHECKED");
        }

        // ── Non-vacuous: each check actually fires ───────────────────────────

        [Fact]
        public void ARequiredHandlerThatIsNotComposed_IsAnError()
        {
            var handlers = AllComposed()
                .Select(h => h.HandlerName == HomesteadHandlerWiringObserver.Development
                    ? new HandlerWiring(h.HandlerName, WiringState.NotComposed, "perturbed for this test")
                    : h)
                .ToList();

            var result = Verify(handlers, AllWoven());

            Assert.False(result.Passed);
            var finding = result.Findings.Single(f => f.Code == "HANDLER-NOT-COMPOSED");
            Assert.Equal(ConformanceSeverity.Error, finding.Severity);
            Assert.Contains("DevelopmentCommandHandler", finding.Message);
        }

        [Fact]
        public void ARequiredHandlerReportedNotChecked_IsAWarning_NotAnError()
        {
            var handlers = AllComposed()
                .Select(h => h.HandlerName == HomesteadHandlerWiringObserver.Facet
                    ? new HandlerWiring(h.HandlerName, WiringState.NotChecked, "perturbed for this test")
                    : h)
                .ToList();

            var result = Verify(handlers, AllWoven());

            Assert.True(result.Passed, "an admitted blind spot is not a drift error");
            Assert.Contains(result.Findings,
                f => f.Code == "HANDLER-NOT-CHECKED" && f.Severity == ConformanceSeverity.Warning);
        }

        [Fact]
        public void ARequiredPatchClassThatDidNotWeave_IsAnError_NamingTheClass()
        {
            var woven = AllWoven().Where(n => n != "ReadyHandsEquipDurationPatch").ToList();

            var result = Verify(AllComposed(), woven);

            Assert.False(result.Passed);
            var finding = result.Findings.Single(f => f.Code == "PATCH-NOT-WOVEN");
            Assert.Contains("ReadyHandsEquipDurationPatch", finding.Message);
            Assert.Contains("INERT in-world", finding.Message);
        }

        [Fact]
        public void EveryRequiredPatchClass_IsIndividuallyLoadBearing()
        {
            // Drop each required seam in turn: every one must produce its own error. This is what makes
            // the required list a manifest rather than a comment.
            foreach (string required in ProgressionConformance.RequiredPatchClasses)
            {
                var woven = AllWoven().Where(n => n != required).ToList();
                var result = Verify(AllComposed(), woven);
                Assert.False(result.Passed, required + " was dropped but conformance still passed");
                Assert.Contains(result.Findings,
                    f => f.Code == "PATCH-NOT-WOVEN" && f.Message.Contains(required));
            }
        }

        [Fact]
        public void AFullyQualifiedWovenName_SatisfiesTheRequirement()
        {
            // The caller may report either shape; neither may be silently rejected into a false alarm.
            var woven = ProgressionConformance.RequiredPatchClasses
                .Select(n => "SBPR.Niflheim.HomesteadStones.Features.Progression." + n)
                .ToList();

            var result = Verify(AllComposed(), woven);
            Assert.DoesNotContain(result.Findings, f => f.Code == "PATCH-NOT-WOVEN");
        }

        [Fact]
        public void AnEmptyWovenSet_ReportsEveryRequiredSeamMissing_NotSilence()
        {
            var result = Verify(AllComposed(), new List<string>());
            Assert.Equal(ProgressionConformance.RequiredPatchClasses.Count,
                result.Findings.Count(f => f.Code == "PATCH-NOT-WOVEN"));
        }

        // ── Recovery: counts only, no PII, quarantine never softened ─────────

        [Fact]
        public void Recovery_IsCountedFromTheRealReceiptRecovery_AndNeverNamesAnOperationId()
        {
            var store = NewReceiptStore();
            store.SubmitFoundationalAp(new OperationId("op-t036-secret-id"), _stone, _owner, "evi");

            var recovery = new ReceiptRecovery(store);
            var result = Verify(AllComposed(), AllWoven(), recovery);

            Assert.True(result.Shape.Recovery.Inspected);
            Assert.Equal(recovery.InspectAll().Count, result.Shape.Recovery.DurableOperations);

            // The rendered text — in BOTH gating modes — carries counts, never identity.
            foreach (string text in new[]
            {
                ProgressionConformance.Render(result),
                ProgressionConformance.Render(result, verbose: true),
            })
            {
                Assert.DoesNotContain("op-t036-secret-id", text);
                Assert.DoesNotContain("acct-t036", text);
                Assert.DoesNotContain("char-t036", text);
                Assert.DoesNotContain(_dir, text);
            }
        }

        [Fact]
        public void RecoveryQuarantine_IsWarned_AndSaysNothingWasRepaired()
        {
            string journal = Path.Combine(_dir, "quarantine.journal");
            var store1 = new OperationReceiptStore(journal,
                new InMemoryMirroredStoneApStore(), new InMemoryCharacterApStore());
            Assert.Throws<SimulatedDeath>(() => store1.SubmitFoundationalAp(
                new OperationId("op-t036-quarantine"), _stone, _owner, "evi", new CrashAfterStoneApplied()));

            var reopened = new OperationReceiptStore(journal,
                new InMemoryMirroredStoneApStore(), new InMemoryCharacterApStore());
            var result = Verify(AllComposed(), AllWoven(), new ReceiptRecovery(reopened));

            var finding = result.Findings.Single(f => f.Code == "RECOVERY-QUARANTINE");
            Assert.Equal(ConformanceSeverity.Warning, finding.Severity);
            Assert.Contains("Nothing was repaired", finding.Message);
            Assert.True(result.Passed, "an operator decision is not a content drift");
        }

        // ── The rendered report and its config gate ──────────────────────────

        [Fact]
        public void Render_CarriesTheShapeNotPlayabilityCaveatVerbatim()
        {
            string text = ProgressionConformance.Render(Verify(AllComposed(), AllWoven()));
            Assert.Contains(ProgressionConformance.ShapeNotPlayabilityCaveat, text);
            Assert.Contains("PROVES SHAPE, NEVER PLAYABILITY", text);
        }

        [Fact]
        public void Render_NegativeControl_TheCaveatAssertionIsNotVacuous()
        {
            string text = ProgressionConformance.Render(Verify(AllComposed(), AllWoven()));
            Assert.DoesNotContain("this build is playable", text);
            Assert.DoesNotContain("verified in-world", text);
        }

        [Fact]
        public void Render_AlwaysShowsTheVerdictAndProblems_EvenWhenVerboseDetailIsGatedOff()
        {
            // The config flag may hide DETAIL. It may never hide a finding an operator must act on.
            var woven = AllWoven().Where(n => n != "MasterworkIssuanceObserver").ToList();
            var result = Verify(AllComposed(), woven);

            string terse = ProgressionConformance.Render(result, verbose: false);
            Assert.Contains("verdict:  FAIL", terse);
            Assert.Contains("MasterworkIssuanceObserver", terse);
            Assert.Contains("PROVIDER-ABSENT", terse);          // warnings survive the gate too
            Assert.DoesNotContain("enumerated trees", terse);    // detail does not

            string verbose = ProgressionConformance.Render(result, verbose: true);
            Assert.Contains("enumerated trees", verbose);
            Assert.Contains("honestly unavailable in this build", verbose);
        }

        [Fact]
        public void Render_ReportsHowManyChecksActuallyRan_SoAnEmptyRunCannotReadAsAPass()
        {
            string text = ProgressionConformance.Render(Verify(AllComposed(), AllWoven()));
            Assert.Contains("checks=", text);
            Assert.Contains("verdict:  PASS (shape only)", text);
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static ProgressionConformanceResult Verify(
            IReadOnlyList<HandlerWiring>? handlers,
            IReadOnlyCollection<string>? woven,
            ReceiptRecovery? recovery = null) =>
            ProgressionConformance.Verify(
                new HomesteadProgressionCatalog(), StoneFacetPalette.Current, handlers, woven, recovery);

        /// <summary>Every REQUIRED handler observed as composed (plus the two known non-required ones
        /// reported exactly as ADO #123 reports them).</summary>
        private static List<HandlerWiring> AllComposed()
        {
            var list = ProgressionConformance.RequiredHandlers
                .Select(n => new HandlerWiring(n, WiringState.Composed, "observed composed for this test"))
                .ToList();
            list.Add(new HandlerWiring(HomesteadHandlerWiringObserver.Purchase, WiringState.NotComposed,
                "built on demand by the config-gated admin seam"));
            list.Add(new HandlerWiring(HomesteadHandlerWiringObserver.WeaponDiscipline, WiringState.NotComposed,
                "type ships and is unit-tested, but no composition root constructs one"));
            return list;
        }

        private static List<string> AllWoven() =>
            ProgressionConformance.RequiredPatchClasses.ToList();

        private OperationReceiptStore NewReceiptStore() =>
            new OperationReceiptStore(
                Path.Combine(_dir, "conformance-ap.journal"),
                new InMemoryMirroredStoneApStore(), new InMemoryCharacterApStore());

        private sealed class SimulatedDeath : System.Exception { }

        private sealed class CrashAfterStoneApplied : ICrashInjector
        {
            public void AfterBoundary(ReceiptBoundary boundary)
            {
                if (boundary == ReceiptBoundary.StoneApplied) throw new SimulatedDeath();
            }
        }
    }
}
