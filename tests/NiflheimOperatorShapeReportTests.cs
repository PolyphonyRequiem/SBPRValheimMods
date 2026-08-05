// ============================================================================
//  ADO #123 — the OFFLINE operator shape report.
// ----------------------------------------------------------------------------
//  Executable evidence that the report answers the card's five questions from
//  LIVE objects, and — the actual deliverable — that it states its own limits in
//  its own rendered output.
//
//  What this suite proves:
//    * the report RENDERS, naming all five items;
//    * counts TRACK THE CATALOG: a different roster moves every number, so no
//      count here can go stale (a stale count is a bug in this card specifically);
//    * unavailable nodes are reported honestly, BY NAME, from the catalog's own
//      NodeFirstBuildStatus — this suite never invents a notion of "finished";
//    * unwired handlers are reported honestly, and an un-inspected handler renders
//      NOT CHECKED rather than a guessed green;
//    * recovery status is surfaced from the REAL ReceiptRecovery over a real
//      durable journal (including a genuine QUARANTINE from a simulated crash),
//      not reimplemented here;
//    * the shape-not-playability caveat is present in the rendered text, and a
//      NEGATIVE control proves that assertion is non-vacuous.
//
//  HONEST SCOPE: every assertion below is about the report's OWN correctness.
//  None of it proves any reported-present thing works for a player. That is the
//  exact distinction the report itself is required to state.
// ============================================================================

using System.Collections.Generic;
using System.IO;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Diagnostics;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Recovery;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimOperatorShapeReportTests : System.IDisposable
    {
        private readonly string _dir;
        private readonly WorldId _world = new WorldId("uid:shape-123");
        private readonly StoneId _stone;
        private readonly AuthoritativePrincipal _owner =
            new AuthoritativePrincipal(new AccountId("acct-shape"), new CharacterId("char-shape"));

        public NiflheimOperatorShapeReportTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "niflheim-ado123-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _stone = StoneId.FromHostZone(_world, 2, 7);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        // ── (1)(2)(3) content registry version, trees/nodes, executable vs unavailable ──

        [Fact]
        public void Report_NamesRegistryVersionTreesAndNodes_FromTheLiveCatalog()
        {
            var catalog = new HomesteadProgressionCatalog();
            string text = OperatorShapeReport.BuildAndRender(catalog);

            Assert.Contains("content_registry_version: "
                + HomesteadProgressionCatalog.CurrentContentRegistryVersion, text);

            // Derived, not asserted as literals: recompute the expectation from the live catalog so
            // this test can never pin a number the catalog has moved past.
            int total = catalog.Nodes.Count;
            int executable = catalog.Nodes.Count(n => n.IsExecutable);
            int unavailable = total - executable;
            int trees = catalog.Nodes.Select(n => n.Tree.Key).Distinct().Count();

            Assert.Contains("trees:              " + trees, text);
            Assert.Contains("authored nodes:     " + total, text);
            Assert.Contains("executable nodes:   " + executable, text);
            Assert.Contains("unavailable nodes:  " + unavailable, text);

            // Every tree gets a row, keyed by its stable content key.
            foreach (string key in catalog.Nodes.Select(n => n.Tree.Key).Distinct())
                Assert.Contains("    " + key + " (v", text);
        }

        [Fact]
        public void Report_CountsTrackTheRoster_NotHardcodedTallies()
        {
            // THE anti-drift proof. Feed a DIFFERENT roster through the SAME counting path Build() uses
            // and every number must follow it. If any count were a literal, this fails — which is the
            // point: a count that can go stale is the bug this card names.
            var shipped = OperatorShapeReport.Build(new HomesteadProgressionCatalog());

            var perturbed = new List<NodeDefinition>
            {
                Node("StubTree", "Alpha", NodeFirstBuildStatus.Executable, "Alpha"),
                Node("StubTree", "Beta", NodeFirstBuildStatus.Unavailable, "Beta"),
                Node("OtherTree", "Gamma", NodeFirstBuildStatus.Unavailable, "Gamma"),
            };
            var stub = OperatorShapeReport.BuildFromRoster(7, "Stub", "Fixture", perturbed);

            Assert.Equal(3, stub.TotalNodes);
            Assert.Equal(1, stub.ExecutableNodes);
            Assert.Equal(2, stub.UnavailableNodes);
            Assert.Equal(2, stub.Trees.Count);
            Assert.Equal(7, stub.ContentRegistryVersion);

            // And it genuinely differs from the shipped roster, so the numbers cannot be constants.
            Assert.NotEqual(shipped.TotalNodes, stub.TotalNodes);
            Assert.NotEqual(shipped.Trees.Count, stub.Trees.Count);

            string text = OperatorShapeReport.Render(stub);
            Assert.Contains("authored nodes:     3", text);
            Assert.Contains("StubTree (v1): 2 node(s), 1 executable, 1 unavailable", text);
            Assert.Contains("Beta (StubTree)", text);
            Assert.DoesNotContain("Alpha (StubTree)", text);
        }

        [Fact]
        public void Report_WhenDeclaredCountsDisagreeWithTheRoster_SaysSoRatherThanPickingOne()
        {
            // Drift control for the self-description check: a catalog that lies about its own size must
            // produce a visible drift section, and the ENUMERATED numbers must still be the reported ones.
            var roster = new List<NodeDefinition>
            {
                Node("StubTree", "Alpha", NodeFirstBuildStatus.Executable, "Alpha"),
            };
            var snapshot = OperatorShapeReport.BuildFromRoster(
                1, "Stub", "Fixture", roster, new DeclaredNodeCounts(20, 13, 7));

            Assert.NotEmpty(snapshot.CatalogDeclarationMismatches);
            Assert.Equal(1, snapshot.TotalNodes);

            string text = OperatorShapeReport.Render(snapshot);
            Assert.Contains("CATALOG SELF-DESCRIPTION DRIFT", text);
            Assert.Contains("enumerated 1 vs declared 20", text);
        }

        [Fact]
        public void Report_UnavailableNodes_AreNamedHonestlyFromTheCatalogsOwnStatus()
        {
            var catalog = new HomesteadProgressionCatalog();
            var snapshot = OperatorShapeReport.Build(catalog);
            string text = OperatorShapeReport.Render(snapshot);

            var expected = catalog.Nodes
                .Where(n => n.Status == NodeFirstBuildStatus.Unavailable)
                .Select(n => n.DisplayLabel)
                .ToList();

            Assert.NotEmpty(expected);
            Assert.Equal(expected.Count, snapshot.UnavailableNodeLabels.Count);
            foreach (string label in expected)
                Assert.Contains(label, text);

            // The report must frame them as a deliberate hold-out, not a failure.
            Assert.Contains("honestly unavailable in this build", text);
            Assert.Contains("deliberately held out", text);

            // NEGATIVE control: an EXECUTABLE node must not appear in the unavailable list.
            var executableLabel = catalog.Nodes.First(n => n.IsExecutable).DisplayLabel;
            Assert.DoesNotContain(snapshot.UnavailableNodeLabels,
                l => l.StartsWith(executableLabel + " (", System.StringComparison.Ordinal));
        }

        [Fact]
        public void Report_CatalogSelfDescriptionAgreesWithItsRoster_NoDriftLine()
        {
            // The shipped catalog's declared Expected*NodeCount constants must match its enumerated
            // roster. When they do, the drift section is absent; when they don't, the report says so.
            var snapshot = OperatorShapeReport.Build(new HomesteadProgressionCatalog());
            Assert.Empty(snapshot.CatalogDeclarationMismatches);
            Assert.DoesNotContain("CATALOG SELF-DESCRIPTION DRIFT",
                OperatorShapeReport.Render(snapshot));
        }

        // ── (4) which handlers are wired ──

        [Fact]
        public void Wiring_ComposedHandlers_AreReportedComposed_AndUncomposedOnesHonestly()
        {
            var foundational = NewFoundationalServer();
            var local = NewLocalServer(foundational);

            var wiring = HomesteadHandlerWiringObserver.Observe(foundational, local);
            string text = OperatorShapeReport.BuildAndRender(local.Catalog, wiring);

            foreach (string composed in new[]
            {
                HomesteadHandlerWiringObserver.Relationship,
                HomesteadHandlerWiringObserver.Activity,
                HomesteadHandlerWiringObserver.Development,
                HomesteadHandlerWiringObserver.Facet,
                HomesteadHandlerWiringObserver.LocalPolicy,
            })
            {
                Assert.Equal(WiringState.Composed, StateOf(wiring, composed));
            }

            // PurchaseCommandHandler is built on demand by the config-gated ingress: with no ingress in
            // hand, the honest answer is NOT COMPOSED, not a green.
            Assert.Equal(WiringState.NotComposed, StateOf(wiring, HomesteadHandlerWiringObserver.Purchase));

            // WeaponDisciplineCommandHandler ships and is unit-tested but has no composition root at all.
            // Reporting THAT is the card's point: a type that exists is not a type that is wired.
            Assert.Equal(WiringState.NotComposed,
                StateOf(wiring, HomesteadHandlerWiringObserver.WeaponDiscipline));

            Assert.Contains("NOT COMPOSED", text);
            Assert.Contains("no live runtime caller", text);
            Assert.Contains("is NOT a claim that", text);
        }

        [Fact]
        public void Wiring_PurchaseHandler_ReportsComposedOnceTheProvisioningIngressExists()
        {
            var foundational = NewFoundationalServer();
            var local = NewLocalServer(foundational);
            var ingress = local.CreateLocalProvisioningIngress();

            var wiring = HomesteadHandlerWiringObserver.Observe(foundational, local, ingress);
            Assert.Equal(WiringState.Composed, StateOf(wiring, HomesteadHandlerWiringObserver.Purchase));
        }

        [Fact]
        public void Wiring_WhatWasNotInspected_RendersNotChecked_NeverAGuessedGreen()
        {
            // Both roots absent: the report must admit it did not look, for EVERY handler it cannot see.
            var wiring = HomesteadHandlerWiringObserver.Observe(null, null);

            foreach (string name in new[]
            {
                HomesteadHandlerWiringObserver.Relationship,
                HomesteadHandlerWiringObserver.Activity,
                HomesteadHandlerWiringObserver.Development,
                HomesteadHandlerWiringObserver.Facet,
                HomesteadHandlerWiringObserver.LocalPolicy,
                HomesteadHandlerWiringObserver.Purchase,
            })
            {
                Assert.Equal(WiringState.NotChecked, StateOf(wiring, name));
            }

            string text = OperatorShapeReport.BuildAndRender(new HomesteadProgressionCatalog(), wiring);
            Assert.Contains("NOT CHECKED", text);
            Assert.Contains("Anything reported NOT CHECKED was not inspected. It is not a pass.", text);
        }

        [Fact]
        public void Report_WithNoHandlerObservationsAtAll_SaysWiringWasNotChecked()
        {
            string text = OperatorShapeReport.BuildAndRender(new HomesteadProgressionCatalog());
            Assert.Contains("no handler observations supplied", text);
        }

        // ── (5) what recovery did at startup — from the REAL ReceiptRecovery ──

        [Fact]
        public void Recovery_IsSurfacedFromTheRealReceiptRecovery_NotReimplemented()
        {
            var store = NewReceiptStore(out _);
            store.SubmitFoundationalAp(new OperationId("op-shape-1"), _stone, _owner, "evi");
            store.SubmitFoundationalAp(new OperationId("op-shape-2"), _stone, _owner, "evi");

            var recovery = new ReceiptRecovery(store);
            var snapshot = OperatorShapeReport.Build(new HomesteadProgressionCatalog(), null, recovery);

            // Cross-check against ReceiptRecovery's OWN verdicts — the report only counts them.
            var states = recovery.InspectAll();
            Assert.Equal(states.Count, snapshot.Recovery.DurableOperations);
            Assert.Equal(states.Count(s => s.Status == RecoveryStatus.Recoverable), snapshot.Recovery.Recoverable);
            Assert.Equal(states.Count(s => s.Status == RecoveryStatus.Quarantine), snapshot.Recovery.Quarantine);
            Assert.Equal(states.Count(s => s.Status == RecoveryStatus.Clean), snapshot.Recovery.Clean);

            Assert.True(snapshot.Recovery.Inspected);
            Assert.Equal(2, snapshot.Recovery.Recoverable);

            string text = OperatorShapeReport.Render(snapshot);
            Assert.Contains("RECOVERABLE: 2", text);
            Assert.Contains("startup recovery", text);
        }

        [Fact]
        public void Recovery_QuarantineFromAPartialWrite_IsSurfacedAndFlagged()
        {
            // A genuine partial durable state via the shipped crash injector — ReceiptRecovery classifies
            // it QUARANTINE, and the report must carry that forward without softening it.
            string journal = Path.Combine(_dir, "quarantine.journal");
            var store1 = new OperationReceiptStore(journal,
                new InMemoryMirroredStoneApStore(), new InMemoryCharacterApStore());
            Assert.Throws<SimulatedDeath>(() => store1.SubmitFoundationalAp(
                new OperationId("op-partial"), _stone, _owner, "evi", new CrashAfterStoneApplied()));

            var reopened = new OperationReceiptStore(journal,
                new InMemoryMirroredStoneApStore(), new InMemoryCharacterApStore());
            var snapshot = OperatorShapeReport.Build(
                new HomesteadProgressionCatalog(), null, new ReceiptRecovery(reopened));

            Assert.Equal(1, snapshot.Recovery.Quarantine);
            string text = OperatorShapeReport.Render(snapshot);
            Assert.Contains("QUARANTINE:  1", text);
            Assert.Contains("operator must decide", text);
            Assert.Contains("Nothing was repaired.", text);
        }

        [Fact]
        public void Recovery_WhenNoStoreSupplied_ReportsNotChecked_NotAReassuringZero()
        {
            var snapshot = OperatorShapeReport.Build(new HomesteadProgressionCatalog());
            Assert.False(snapshot.Recovery.Inspected);

            string text = OperatorShapeReport.Render(snapshot);
            Assert.Contains("status: NOT CHECKED", text);
            // A zero-count line would read as "recovery ran and found nothing wrong". It must not appear.
            Assert.DoesNotContain("durable operations: 0", text);
        }

        // ── The honesty requirement — the actual deliverable ──

        [Fact]
        public void Report_StatesItsOwnShapeNotPlayabilityLimit_InTheRenderedOutput()
        {
            // The all-green case is exactly the one that misleads, so assert the caveat THERE.
            var foundational = NewFoundationalServer();
            var local = NewLocalServer(foundational);
            var store = NewReceiptStore(out _);
            store.SubmitFoundationalAp(new OperationId("op-green"), _stone, _owner, "evi");

            string text = OperatorShapeReport.BuildAndRender(
                local.Catalog,
                HomesteadHandlerWiringObserver.Observe(foundational, local),
                new ReceiptRecovery(store));

            Assert.Contains(OperatorShapeReport.ShapeNotPlayabilityCaveat, text);
            Assert.Contains("PROVES SHAPE, NEVER PLAYABILITY", text);
            Assert.Contains("LIMITS OF THIS REPORT", text);
            // The header warns before the reader has scrolled anywhere.
            Assert.Contains("This report describes SHAPE ONLY", text);
        }

        [Fact]
        public void CaveatAssertion_IsNonVacuous_NegativeControl()
        {
            // Guard against the assertion above passing for a trivial reason. A string that does NOT
            // contain the caveat must fail the same check the report passes.
            const string notTheReport = "=== Homestead Stones — Operator Shape Report ===\nall good!\n";
            Assert.DoesNotContain(OperatorShapeReport.ShapeNotPlayabilityCaveat, notTheReport);
            Assert.NotEmpty(OperatorShapeReport.ShapeNotPlayabilityCaveat);
        }

        // ── helpers ─────────────────────────────────────────────────────────

        private static WiringState StateOf(IReadOnlyList<HandlerWiring> wiring, string name) =>
            wiring.First(w => w.HandlerName == name).State;

        private OperationReceiptStore NewReceiptStore(out InMemoryMirroredStoneApStore stone)
        {
            stone = new InMemoryMirroredStoneApStore();
            return new OperationReceiptStore(
                Path.Combine(_dir, "shape-ap.journal"), stone, new InMemoryCharacterApStore());
        }

        private FoundationalProgressionServer NewFoundationalServer() =>
            FoundationalProgressionServer.Create(
                Path.Combine(_dir, "foundational"),
                new FixedFamily(), new AllowBond(), new InMemoryMirroredStoneApStore(), world: _world);

        private LocalProgressionServer NewLocalServer(FoundationalProgressionServer foundational)
        {
            var stones = new InMemoryStoneAggregateStore();
            var ownerPresence = new GovernorPresenceResolver(foundational.Characters, foundational.Authority);
            return LocalProgressionServer.Create(
                Path.Combine(_dir, "local"),
                stones, foundational.Characters, foundational.Authority, foundational.Relationships,
                new FixedFamily(), new AllowGovernor(), new AllowDevelopment(),
                new CommittedGovernorOwnerAuthority(ownerPresence),
                characterApStore: foundational.CharacterApStore);
        }

        private sealed class SimulatedDeath : System.Exception { }

        private sealed class CrashAfterStoneApplied : ICrashInjector
        {
            public void AfterBoundary(ReceiptBoundary boundary)
            {
                if (boundary == ReceiptBoundary.StoneApplied) throw new SimulatedDeath();
            }
        }

        private sealed class FixedFamily : IStoneFamilyResolver
        {
            public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
            {
                family = "Settlement"; variant = "Homestead"; return true;
            }
        }

        private sealed class AllowBond : IBondAuthorityPolicy
        {
            public bool TryAuthorizeBond(StoneId stoneId, string requestedResponsibilityRange,
                out string grantedRange, out string grantedRole)
            {
                grantedRange = requestedResponsibilityRange ?? string.Empty;
                grantedRole = "Governor";
                return true;
            }
        }

        private sealed class AllowGovernor : IGovernorAuthorityPolicy
        {
            public bool CanCommit(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                string facetId, FacetCategory category) => true;
        }

        private sealed class AllowDevelopment : IGovernorDevelopmentAuthority
        {
            public bool CanDevelop(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                VersionedId tree) => true;
        }

        /// <summary>Build a synthetic authored node for the roster-perturbation tests. It exercises the
        /// SHIPPED NodeDefinition type — this is a different INPUT, not a second content source.</summary>
        private static NodeDefinition Node(string tree, string key, NodeFirstBuildStatus status, string label) =>
            new NodeDefinition(
                new VersionedId(tree, 1), new VersionedId(key, 1), 1,
                NodeOutcomeType.LocalEffect,
                status == NodeFirstBuildStatus.Executable
                    ? NodeOwnership.StoneCultivated
                    : NodeOwnership.NoneWhileUnavailable,
                status, NodePricing.None, NodeRequirements.Unavailable, label);
    }
}
