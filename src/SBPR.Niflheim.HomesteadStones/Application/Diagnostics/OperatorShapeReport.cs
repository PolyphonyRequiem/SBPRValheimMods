using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Persistence.Recovery;

namespace SBPR.Niflheim.HomesteadStones.Application.Diagnostics
{
    // ADO #123 — the OFFLINE operator shape report. It answers exactly one question for whoever is
    // running the server: WHAT SHAPE IS THIS BUILD IN? Five facts, all derived from live objects:
    //
    //   1. content registry version   — HomesteadProgressionCatalog.CurrentContentRegistryVersion
    //   2. Trees and nodes            — ENUMERATED from the catalog, never a hardcoded tally
    //   3. finished vs honestly unavailable — the catalog's own NodeFirstBuildStatus, not a new notion
    //   4. which handlers are WIRED   — composed into the live runtime, as OBSERVED by the caller
    //   5. what recovery did at startup — REUSES ReceiptRecovery's RECOVERABLE/QUARANTINE/CLEAN
    //                                     classification; this file never re-derives it
    //
    // ── The load-bearing caveat (the actual deliverable) ────────────────────────────────────────
    // A GREEN REPORT PROVES SHAPE, NEVER PLAYABILITY. AGENTS.md states it as a hard rule ("logs
    // green != playable") because this map has already been burned by that distinction. The
    // disclaimer is therefore RENDERED INTO THE REPORT TEXT, not left in a code comment — a reader
    // who sees an all-present report must be told, BY THIS ARTIFACT, that it says nothing about
    // whether a joined client can actually craft/build/purchase the thing.
    //
    // Corollary, encoded in <see cref="WiringState"/>: this report prefers UNKNOWN over a guessed
    // green. Nothing here reflects over the runtime to infer wiring — the caller passes what it
    // actually observed, and anything it did not observe is reported "not checked", never "wired".
    //
    // Scope discipline: this card OBSERVES, it does not restructure. Nothing in this file changes how
    // handlers are composed, adds a runtime dependency, or touches the journal protocol.
    //
    // net48 audit: System / System.Collections.Generic / System.Text / System.Globalization plus the
    // shipped engine-free catalog + recovery types. No UnityEngine / Valheim / BepInEx / Harmony, so
    // this whole file link-compiles into the net8 test project and is fully unit-tested. Any net48
    // shell (the boot log line) is a THIN CALLER, exactly as FoundationalProgressionServer.Create
    // (engine-free, tested) is to FoundationalRuntimeBootstrap (net48, untested).

    /// <summary>What this report actually knows about one handler's composition into the live runtime.
    /// The three states are deliberately distinct: "not checked" is NEVER collapsed into "wired" or
    /// "absent", because a report that guesses a green is worse than one that admits a blind spot.</summary>
    public enum WiringState
    {
        /// <summary>The caller did not observe this handler at all. Reported as unknown, never inferred.</summary>
        NotChecked = 0,

        /// <summary>The caller observed a composed instance in the live runtime.</summary>
        Composed = 1,

        /// <summary>The caller looked and found no composed instance — the type exists, but nothing
        /// in the live runtime holds one.</summary>
        NotComposed = 2
    }

    /// <summary>One handler's observed composition, plus the note explaining WHERE it is (or is not)
    /// composed. The note is authored by the caller because only the caller knows what it inspected.</summary>
    public readonly struct HandlerWiring
    {
        public HandlerWiring(string handlerName, WiringState state, string note)
        {
            HandlerName = handlerName ?? string.Empty;
            State = state;
            Note = note ?? string.Empty;
        }

        public string HandlerName { get; }
        public WiringState State { get; }

        /// <summary>Operator-facing explanation of the observation (e.g. which composition root holds
        /// it, or why nothing does). Never a claim that the handler WORKS.</summary>
        public string Note { get; }
    }

    /// <summary>Per-Tree node tally, enumerated from the catalog.</summary>
    public readonly struct TreeShape
    {
        public TreeShape(string treeKey, int treeVersion, int totalNodes, int executableNodes, int unavailableNodes)
        {
            TreeKey = treeKey ?? string.Empty;
            TreeVersion = treeVersion;
            TotalNodes = totalNodes;
            ExecutableNodes = executableNodes;
            UnavailableNodes = unavailableNodes;
        }

        public string TreeKey { get; }
        public int TreeVersion { get; }
        public int TotalNodes { get; }
        public int ExecutableNodes { get; }
        public int UnavailableNodes { get; }
    }

    /// <summary>Startup recovery summary, derived ENTIRELY from <see cref="ReceiptRecovery"/>'s own
    /// classification. This report counts what ReceiptRecovery said; it never classifies a journal
    /// record itself.</summary>
    public readonly struct RecoverySummary
    {
        public RecoverySummary(bool inspected, int durableOperations, int clean, int recoverable, int quarantine,
            string note)
        {
            Inspected = inspected;
            DurableOperations = durableOperations;
            Clean = clean;
            Recoverable = recoverable;
            Quarantine = quarantine;
            Note = note ?? string.Empty;
        }

        /// <summary>False when no ReceiptRecovery was supplied — the report then says "not checked"
        /// rather than reporting a reassuring zero.</summary>
        public bool Inspected { get; }

        public int DurableOperations { get; }
        public int Clean { get; }
        public int Recoverable { get; }
        public int Quarantine { get; }

        /// <summary>Failure note when the durable journal could not be read at all.</summary>
        public string Note { get; }

        public static readonly RecoverySummary NotChecked =
            new RecoverySummary(false, 0, 0, 0, 0, "no recovery store supplied to this report");
    }

    /// <summary>A catalog's DECLARED expected counts, carried so the report can flag a disagreement
    /// between what the catalog says about itself and the roster it actually enumerates. Never the
    /// source of a reported number.</summary>
    public readonly struct DeclaredNodeCounts
    {
        public DeclaredNodeCounts(int authoredNodes, int executableNodes, int unavailableNodes)
        {
            AuthoredNodes = authoredNodes;
            ExecutableNodes = executableNodes;
            UnavailableNodes = unavailableNodes;
        }

        public int AuthoredNodes { get; }
        public int ExecutableNodes { get; }
        public int UnavailableNodes { get; }
    }

    /// <summary>The immutable structured snapshot. <see cref="OperatorShapeReport.Render"/> is the ONE
    /// renderer over it — there is deliberately no second formatter to drift against.</summary>
    public sealed class OperatorShapeSnapshot
    {
        internal OperatorShapeSnapshot(
            int contentRegistryVersion,
            string family,
            string variant,
            int totalNodes,
            int executableNodes,
            int unavailableNodes,
            IReadOnlyList<TreeShape> trees,
            IReadOnlyList<string> unavailableNodeLabels,
            IReadOnlyList<string> catalogDeclarationMismatches,
            IReadOnlyList<HandlerWiring> handlers,
            RecoverySummary recovery)
        {
            ContentRegistryVersion = contentRegistryVersion;
            Family = family;
            Variant = variant;
            TotalNodes = totalNodes;
            ExecutableNodes = executableNodes;
            UnavailableNodes = unavailableNodes;
            Trees = trees;
            UnavailableNodeLabels = unavailableNodeLabels;
            CatalogDeclarationMismatches = catalogDeclarationMismatches;
            Handlers = handlers;
            Recovery = recovery;
        }

        public int ContentRegistryVersion { get; }
        public string Family { get; }
        public string Variant { get; }

        /// <summary>Enumerated from the catalog's live roster, so adding/removing a node moves this.</summary>
        public int TotalNodes { get; }

        public int ExecutableNodes { get; }
        public int UnavailableNodes { get; }
        public IReadOnlyList<TreeShape> Trees { get; }

        /// <summary>Display labels of the nodes the catalog HONESTLY refuses in this build. Named, not
        /// just counted, so an operator can see exactly which capability is held out.</summary>
        public IReadOnlyList<string> UnavailableNodeLabels { get; }

        /// <summary>Any disagreement between the catalog's DECLARED Expected*NodeCount constants and the
        /// roster actually enumerated. Non-empty means the catalog's own self-description drifted; the
        /// enumerated numbers above are the ones this report trusts.</summary>
        public IReadOnlyList<string> CatalogDeclarationMismatches { get; }

        public IReadOnlyList<HandlerWiring> Handlers { get; }
        public RecoverySummary Recovery { get; }
    }

    /// <summary>Engine-free builder + renderer for the operator shape report.</summary>
    public static class OperatorShapeReport
    {
        /// <summary>The shape-not-playability limit, rendered into every report. Kept public and const so
        /// a test can assert the rendered text carries it verbatim — the disclaimer is the deliverable,
        /// so it must be impossible to silently drop.</summary>
        public const string ShapeNotPlayabilityCaveat =
            "A GREEN REPORT PROVES SHAPE, NEVER PLAYABILITY. Everything above says the pieces are "
            + "PRESENT and COMPOSED. It says NOTHING about whether a joined client can actually "
            + "develop, purchase, craft, or build any of it. Server-side registration succeeding is "
            + "not proof of a playable path. Verify in-world before concluding the game works.";

        /// <summary>Build the structured snapshot from the shipped catalog. Every count is ENUMERATED
        /// from <paramref name="catalog"/>.Nodes; the catalog's declared Expected*NodeCount constants are
        /// passed through only so the report can flag a disagreement, never as the source of a number.
        ///
        /// <paramref name="handlers"/> is what the CALLER actually observed; handlers it did not inspect
        /// must be passed as <see cref="WiringState.NotChecked"/> (or omitted, which renders the same way)
        /// rather than assumed wired. <paramref name="recovery"/> is optional: when null the report says
        /// recovery was not checked instead of reporting zeros.</summary>
        public static OperatorShapeSnapshot Build(
            HomesteadProgressionCatalog catalog,
            IReadOnlyList<HandlerWiring>? handlers = null,
            ReceiptRecovery? recovery = null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            return BuildFromRoster(
                catalog.ContentRegistryVersion, catalog.Family, catalog.Variant, catalog.Nodes,
                new DeclaredNodeCounts(
                    HomesteadProgressionCatalog.ExpectedAuthoredNodeCount,
                    HomesteadProgressionCatalog.ExpectedExecutableNodeCount,
                    HomesteadProgressionCatalog.ExpectedUnavailableNodeCount),
                handlers, recovery);
        }

        /// <summary>The single counting implementation, over ANY authored roster. <see cref="Build"/> is a
        /// thin adapter onto it, so there is one code path and no second tally to drift.
        ///
        /// This overload exists so a test can perturb the roster and prove the report's numbers FOLLOW it
        /// — an assertion that is impossible to make honestly if the only input is the one shipped
        /// singleton. It is not a second content source: production never calls it directly.</summary>
        public static OperatorShapeSnapshot BuildFromRoster(
            int contentRegistryVersion,
            string family,
            string variant,
            IReadOnlyList<NodeDefinition> nodes,
            DeclaredNodeCounts? declared = null,
            IReadOnlyList<HandlerWiring>? handlers = null,
            ReceiptRecovery? recovery = null)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));

            // ── (2)(3) Trees and nodes, ENUMERATED. Nothing below is a literal count. ──
            var treeOrder = new List<string>();
            var treeVersions = new Dictionary<string, int>(StringComparer.Ordinal);
            var treeTotal = new Dictionary<string, int>(StringComparer.Ordinal);
            var treeExecutable = new Dictionary<string, int>(StringComparer.Ordinal);
            var treeUnavailable = new Dictionary<string, int>(StringComparer.Ordinal);
            var unavailableLabels = new List<string>();

            int executable = 0;
            int unavailable = 0;

            foreach (var node in nodes)
            {
                string treeKey = node.Tree.Key;
                if (!treeTotal.ContainsKey(treeKey))
                {
                    treeOrder.Add(treeKey);
                    treeVersions[treeKey] = node.Tree.Version;
                    treeTotal[treeKey] = 0;
                    treeExecutable[treeKey] = 0;
                    treeUnavailable[treeKey] = 0;
                }

                treeTotal[treeKey] = treeTotal[treeKey] + 1;

                if (node.IsExecutable)
                {
                    executable++;
                    treeExecutable[treeKey] = treeExecutable[treeKey] + 1;
                }
                else
                {
                    unavailable++;
                    treeUnavailable[treeKey] = treeUnavailable[treeKey] + 1;
                    unavailableLabels.Add(node.DisplayLabel + " (" + treeKey + ")");
                }
            }

            var trees = new List<TreeShape>();
            foreach (string key in treeOrder)
                trees.Add(new TreeShape(key, treeVersions[key], treeTotal[key], treeExecutable[key], treeUnavailable[key]));

            int total = nodes.Count;

            // The catalog DECLARES expected counts as constants. This report trusts the ENUMERATION, and
            // reports any disagreement as drift rather than silently preferring either number.
            var mismatches = new List<string>();
            if (declared != null)
            {
                var d = declared.Value;
                if (total != d.AuthoredNodes)
                    mismatches.Add("authored nodes: enumerated " + Num(total) + " vs declared " + Num(d.AuthoredNodes));
                if (executable != d.ExecutableNodes)
                    mismatches.Add("executable nodes: enumerated " + Num(executable) + " vs declared " + Num(d.ExecutableNodes));
                if (unavailable != d.UnavailableNodes)
                    mismatches.Add("unavailable nodes: enumerated " + Num(unavailable) + " vs declared " + Num(d.UnavailableNodes));
            }

            // ── (5) Startup recovery, REUSING ReceiptRecovery's classification verbatim. ──
            RecoverySummary recoverySummary = SummarizeRecovery(recovery);

            var wiring = handlers == null
                ? (IReadOnlyList<HandlerWiring>)new List<HandlerWiring>()
                : new List<HandlerWiring>(handlers);

            return new OperatorShapeSnapshot(
                contentRegistryVersion, family ?? string.Empty, variant ?? string.Empty,
                total, executable, unavailable, trees, unavailableLabels, mismatches,
                wiring, recoverySummary);
        }

        /// <summary>Count ReceiptRecovery's OWN verdicts. This method classifies nothing: every state
        /// comes from <see cref="ReceiptRecovery.InspectAll"/>. A read failure is reported as
        /// not-inspected with the reason, never as a clean zero.</summary>
        private static RecoverySummary SummarizeRecovery(ReceiptRecovery? recovery)
        {
            if (recovery == null) return RecoverySummary.NotChecked;

            IReadOnlyList<OperationRecoveryState> states;
            try
            {
                states = recovery.InspectAll();
            }
            catch (Exception ex)
            {
                // A diagnostic that throws is worse than one that admits it could not look.
                return new RecoverySummary(false, 0, 0, 0, 0,
                    "durable journal could not be read: " + ex.GetType().Name);
            }

            int clean = 0, recoverable = 0, quarantine = 0;
            foreach (var s in states)
            {
                switch (s.Status)
                {
                    case RecoveryStatus.Clean: clean++; break;
                    case RecoveryStatus.Recoverable: recoverable++; break;
                    default: quarantine++; break;
                }
            }

            return new RecoverySummary(true, states.Count, clean, recoverable, quarantine, string.Empty);
        }

        /// <summary>Convenience: build and render in one call.</summary>
        public static string BuildAndRender(
            HomesteadProgressionCatalog catalog,
            IReadOnlyList<HandlerWiring>? handlers = null,
            ReceiptRecovery? recovery = null) =>
            Render(Build(catalog, handlers, recovery));

        /// <summary>The ONE renderer. Human-readable text; the structured snapshot above is the machine
        /// surface. There is deliberately no second formatter for the same facts.</summary>
        public static string Render(OperatorShapeSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var sb = new StringBuilder();
            sb.AppendLine("=== Homestead Stones — Operator Shape Report ===");
            sb.AppendLine("This report describes SHAPE ONLY. See the limits stated at the end.");
            sb.AppendLine();

            sb.AppendLine("-- content registry --");
            sb.AppendLine("  content_registry_version: " + Num(snapshot.ContentRegistryVersion));
            sb.AppendLine("  classification:           " + snapshot.Family + "/" + snapshot.Variant);
            sb.AppendLine();

            sb.AppendLine("-- trees and nodes (enumerated from the live catalog) --");
            sb.AppendLine("  trees:              " + Num(snapshot.Trees.Count));
            sb.AppendLine("  authored nodes:     " + Num(snapshot.TotalNodes));
            sb.AppendLine("  executable nodes:   " + Num(snapshot.ExecutableNodes));
            sb.AppendLine("  unavailable nodes:  " + Num(snapshot.UnavailableNodes));
            foreach (var tree in snapshot.Trees)
                sb.AppendLine("    " + tree.TreeKey + " (v" + Num(tree.TreeVersion) + "): "
                    + Num(tree.TotalNodes) + " node(s), " + Num(tree.ExecutableNodes) + " executable, "
                    + Num(tree.UnavailableNodes) + " unavailable");
            sb.AppendLine();

            sb.AppendLine("-- honestly unavailable in this build --");
            if (snapshot.UnavailableNodeLabels.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                sb.AppendLine("  These are authored capabilities deliberately held out of this build. They");
                sb.AppendLine("  refuse development/purchase/offering by their authored status:");
                foreach (string label in snapshot.UnavailableNodeLabels)
                    sb.AppendLine("    - " + label);
            }
            sb.AppendLine();

            if (snapshot.CatalogDeclarationMismatches.Count > 0)
            {
                sb.AppendLine("-- CATALOG SELF-DESCRIPTION DRIFT --");
                sb.AppendLine("  The catalog's declared Expected*NodeCount constants disagree with its own");
                sb.AppendLine("  roster. The ENUMERATED numbers above are what this report trusts:");
                foreach (string m in snapshot.CatalogDeclarationMismatches)
                    sb.AppendLine("    ! " + m);
                sb.AppendLine();
            }

            sb.AppendLine("-- command handlers --");
            if (snapshot.Handlers.Count == 0)
            {
                sb.AppendLine("  (no handler observations supplied — wiring NOT CHECKED)");
            }
            else
            {
                foreach (var h in snapshot.Handlers)
                {
                    sb.AppendLine("  " + Pad(h.HandlerName, 28) + Describe(h.State));
                    if (h.Note.Length > 0) sb.AppendLine("      " + h.Note);
                }
            }
            sb.AppendLine("  \"COMPOSED\" means an instance exists in the live runtime. It is NOT a claim that");
            sb.AppendLine("  the handler's command path works end-to-end for a player.");
            sb.AppendLine();

            sb.AppendLine("-- startup recovery (from the durable receipt journal) --");
            var r = snapshot.Recovery;
            if (!r.Inspected)
            {
                sb.AppendLine("  status: NOT CHECKED — " + r.Note);
            }
            else
            {
                sb.AppendLine("  durable operations: " + Num(r.DurableOperations));
                sb.AppendLine("    CLEAN:       " + Num(r.Clean) + "  (no record; operation never durably began)");
                sb.AppendLine("    RECOVERABLE: " + Num(r.Recoverable) + "  (terminal result durable; replay converges)");
                sb.AppendLine("    QUARANTINE:  " + Num(r.Quarantine)
                    + "  (partial durable state, no terminal - operator must decide, never auto-guessed)");
                if (r.Quarantine > 0)
                    sb.AppendLine("  ! At least one operation needs an operator decision. Nothing was repaired.");
            }
            sb.AppendLine();

            sb.AppendLine("-- LIMITS OF THIS REPORT --");
            sb.AppendLine("  " + ShapeNotPlayabilityCaveat);
            sb.AppendLine("  Anything reported NOT CHECKED was not inspected. It is not a pass.");
            return sb.ToString();
        }

        private static string Describe(WiringState state)
        {
            switch (state)
            {
                case WiringState.Composed: return "COMPOSED";
                case WiringState.NotComposed: return "NOT COMPOSED";
                default: return "NOT CHECKED";
            }
        }

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Pad(string s, int width)
        {
            if (s == null) s = string.Empty;
            if (s.Length >= width) return s + " ";
            return s + new string(' ', width - s.Length);
        }
    }
}
