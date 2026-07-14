using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.Content
{
    // ContentRegistryValidator (tasks.md T005). Validates the immutable current-build registry's
    // authored-roster arithmetic and rejects stale/unknown same-build content references without
    // misbinding them to a "closest" definition (AT-CONTENT-MISMATCH-REJECT).
    //
    // Rejection codes are the STABLE machine codes from contracts.md §"Rejection vocabulary":
    //   * ContentVersionMismatch — definition/catalog/Offered Set is stale or unknown.
    // Production content migration is DEFERRED — a mismatch is a clean rejection, never a silent
    // reinterpretation or a grandfathering path (data-model.md modeling rule 6).
    //
    // net48 audit: engine-free. Link-compiles into the net8 tests.

    /// <summary>Why a content reference was rejected. Distinguishes an unknown key from a version
    /// mismatch on a known key, and a wrong registry version — all three still map to the single
    /// stable contract code <c>ContentVersionMismatch</c>, but the reason aids operator diagnosis.</summary>
    public enum ContentMismatchReason
    {
        None = 0,
        UnknownNodeKey,          // no such stable node key in the current build
        NodeVersionMismatch,     // known key, but the claimed version is not the current-build version
        RegistryVersionMismatch, // the caller's content-registry version is not the current build's
        TreeMismatch             // the claimed tree does not own the resolved node
    }

    public readonly struct ContentValidationResult
    {
        private ContentValidationResult(bool ok, ContentMismatchReason reason, string rejectionCode, string detail)
        {
            IsValid = ok;
            Reason = reason;
            RejectionCode = rejectionCode;
            Detail = detail;
        }

        public bool IsValid { get; }
        public ContentMismatchReason Reason { get; }

        /// <summary>Stable contract rejection code (contracts.md). Empty when valid.</summary>
        public string RejectionCode { get; }
        public string Detail { get; }

        public static ContentValidationResult Valid() =>
            new ContentValidationResult(true, ContentMismatchReason.None, string.Empty, string.Empty);

        public static ContentValidationResult Reject(ContentMismatchReason reason, string detail) =>
            new ContentValidationResult(false, reason, "ContentVersionMismatch", detail);
    }

    public sealed class RosterArithmetic
    {
        public RosterArithmetic(int authored, int executable, int unavailable,
            int executableLevel1, int executableLevel2)
        {
            Authored = authored;
            Executable = executable;
            Unavailable = unavailable;
            ExecutableLevel1 = executableLevel1;
            ExecutableLevel2 = executableLevel2;
        }

        public int Authored { get; }
        public int Executable { get; }
        public int Unavailable { get; }
        public int ExecutableLevel1 { get; }
        public int ExecutableLevel2 { get; }
    }

    public sealed class ContentRegistryValidator
    {
        private readonly HomesteadProgressionCatalog _catalog;

        public ContentRegistryValidator(HomesteadProgressionCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>Count the authored roster by first-build status and executable Tree level.</summary>
        public RosterArithmetic CountRoster()
        {
            int exec = 0, unavail = 0, execL1 = 0, execL2 = 0;
            foreach (var n in _catalog.Nodes)
            {
                if (n.IsExecutable)
                {
                    exec++;
                    if (n.TreeLevel == 1) execL1++;
                    else if (n.TreeLevel == 2) execL2++;
                }
                else unavail++;
            }
            return new RosterArithmetic(_catalog.Nodes.Count, exec, unavail, execL1, execL2);
        }

        /// <summary>Assert the fixed-roster arithmetic invariant (data-model.md): 20 = 13 + 7, and of
        /// the 13 executable nodes 12 are Level 1 and exactly one (Swift Preparation) is Level 2.
        /// Throws <see cref="InvalidOperationException"/> if the authored roster ever drifts — this is
        /// the spec/code drift guard mandated by AGENTS.md, kept in code so a bad edit fails a test.</summary>
        public void AssertRosterInvariant()
        {
            var r = CountRoster();
            Require(r.Authored == HomesteadProgressionCatalog.ExpectedAuthoredNodeCount,
                "authored node count", HomesteadProgressionCatalog.ExpectedAuthoredNodeCount, r.Authored);
            Require(r.Executable == HomesteadProgressionCatalog.ExpectedExecutableNodeCount,
                "executable node count", HomesteadProgressionCatalog.ExpectedExecutableNodeCount, r.Executable);
            Require(r.Unavailable == HomesteadProgressionCatalog.ExpectedUnavailableNodeCount,
                "unavailable node count", HomesteadProgressionCatalog.ExpectedUnavailableNodeCount, r.Unavailable);
            Require(r.Executable == r.ExecutableLevel1 + r.ExecutableLevel2,
                "executable level partition", r.Executable, r.ExecutableLevel1 + r.ExecutableLevel2);
            Require(r.ExecutableLevel1 == 12, "executable Level-1 count", 12, r.ExecutableLevel1);
            Require(r.ExecutableLevel2 == 1, "executable Level-2 count", 1, r.ExecutableLevel2);
        }

        /// <summary>Validate one same-build content reference. Rejects a stale/unknown node WITHOUT
        /// binding it to any other definition (AT-CONTENT-MISMATCH-REJECT).</summary>
        public ContentValidationResult ValidateNodeReference(int callerRegistryVersion, VersionedId tree, VersionedId node)
        {
            if (callerRegistryVersion != _catalog.ContentRegistryVersion)
                return ContentValidationResult.Reject(ContentMismatchReason.RegistryVersionMismatch,
                    "content-registry version " + callerRegistryVersion + " != current build "
                    + _catalog.ContentRegistryVersion);

            var resolved = _catalog.TryResolveNode(node);
            if (resolved == null)
            {
                // Known key at a different version is a VERSION mismatch; absent key is UNKNOWN. Either
                // way we refuse to rebind — no "closest match" fallback.
                if (_catalog.HasNodeKey(node))
                    return ContentValidationResult.Reject(ContentMismatchReason.NodeVersionMismatch,
                        "node '" + node.Key + "' exists but version " + node.Version + " is not the current build");
                return ContentValidationResult.Reject(ContentMismatchReason.UnknownNodeKey,
                    "unknown node key '" + node.Key + "'");
            }

            // The claimed tree must own the resolved node; a right-node/wrong-tree claim is a mismatch.
            if (!resolved.Tree.Equals(tree))
                return ContentValidationResult.Reject(ContentMismatchReason.TreeMismatch,
                    "node '" + node.Key + "' belongs to tree '" + resolved.Tree.Key
                    + "', not claimed tree '" + tree.Key + "'");

            return ContentValidationResult.Valid();
        }

        private static void Require(bool condition, string what, int expected, int actual)
        {
            if (!condition)
                throw new InvalidOperationException(
                    "Content registry roster invariant violated (" + what + "): expected "
                    + expected + ", got " + actual + ". Spec and code must move together (AGENTS.md).");
        }
    }
}
