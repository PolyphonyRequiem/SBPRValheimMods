// ============================================================================
//  QA-M3R real fixture adapter (t_1572d041) — engine-free responder bridge.
// ----------------------------------------------------------------------------
//  FixtureVerbExecutorBridge — adapts the crash-safe, engine-free
//  ServerFixtureExecutor to the control-plane's IServerFixtureVerbExecutor so the
//  ServerRpcResponder can drive a fixture verb after it has ADMITTED and dispatched
//  it. This is the single seam between the control plane and the fixture lifecycle.
//
//  It handles ONLY the three server fixture-create verbs plus Cleanup. A verb it does
//  not recognise is refused (Handles == false), so an action/observation/lifecycle
//  verb never reaches the fixture lifecycle.
//
//  DETERMINISTIC FIXTURE ID (crash recovery): the owned-resource ledger + its durable
//  snapshot key on a fixtureId. For a restart to reconcile the SAME owned ids, the
//  fixtureId must be reconstructable from the same request — so it is derived purely
//  from (verb, plan-defining args), never a random GUID minted at execution. Two
//  requests for the same fixture therefore address the same ledger + snapshot file, and
//  a crash mid-ensure recovers by re-deriving the id and re-driving ensure.
//
//  Engine-free: System.* only. No product state or verdict — create/cleanup of ordinary
//  vanilla scaffolding, gated by the executor's own execution-time authority recheck.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SBPR.QaHarness.T022.Core.ControlPlane;

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    /// <summary>Bridges an admitted fixture verb to the gated, crash-safe <see cref="ServerFixtureExecutor"/>.</summary>
    public sealed class FixtureVerbExecutorBridge : IServerFixtureVerbExecutor
    {
        /// <summary>The lifecycle verb that tears a fixture down (vs the three create verbs).</summary>
        public const string CleanupVerb = "Cleanup";

        private readonly ServerFixtureExecutor _executor;

        public FixtureVerbExecutorBridge(ServerFixtureExecutor executor)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        /// <summary>True for the three fixture-create verbs and Cleanup.</summary>
        public bool Handles(string? verb) =>
            verb == FixtureRequestMapper.SpawnStation ||
            verb == FixtureRequestMapper.PlaceVanillaPiece ||
            verb == FixtureRequestMapper.GrantVanillaMaterials ||
            verb == CleanupVerb;

        /// <summary>
        /// Execute the admitted fixture verb through the crash-safe lifecycle. A create verb runs
        /// Ensure; Cleanup runs Cleanup (its args must carry the SAME plan-defining fields the
        /// fixture was created with, so the deterministic owned ids can be reconstructed after a
        /// restart). Returns a descriptive outcome (executed + status token), never a verdict.
        /// </summary>
        public FixtureVerbOutcome Execute(
            string verb, IReadOnlyDictionary<string, object?> args,
            string deliveringPeerId, long claimedGeneration)
        {
            if (verb == CleanupVerb)
            {
                // Cleanup carries the create verb + args it is tearing down under a nested selector
                // so the same plan (and thus the same owned ids + snapshot path) is reconstructed.
                if (!TryResolveCleanupTarget(args, out var targetVerb, out var targetArgs))
                    return new FixtureVerbOutcome(false, "fixture-cleanup-rejected:missing-target");
                string cleanupId = DeriveFixtureId(targetVerb, targetArgs);
                var cr = _executor.Cleanup(cleanupId, targetVerb, targetArgs, deliveringPeerId, claimedGeneration);
                return Describe(cr, "fixture-cleanup");
            }

            string fixtureId = DeriveFixtureId(verb, args);
            var er = _executor.Ensure(fixtureId, verb, args, deliveringPeerId, claimedGeneration);
            return Describe(er, "fixture-ensure");
        }

        // Cleanup's typed args are just {scope} in the catalog; the runner encodes the create
        // verb + its args into the scope token as "verb|k=v;k=v" so the bridge can rebuild the
        // exact plan. A scope that does not decode to a fixture-create verb is refused.
        private static bool TryResolveCleanupTarget(
            IReadOnlyDictionary<string, object?> args, out string verb, out IReadOnlyDictionary<string, object?> targetArgs)
        {
            verb = string.Empty;
            targetArgs = EmptyArgs;
            if (!args.TryGetValue("scope", out var raw) || raw is not string scope || string.IsNullOrEmpty(scope))
                return false;

            int bar = scope.IndexOf('|');
            if (bar <= 0) return false;
            verb = scope.Substring(0, bar);
            if (verb != FixtureRequestMapper.SpawnStation &&
                verb != FixtureRequestMapper.PlaceVanillaPiece &&
                verb != FixtureRequestMapper.GrantVanillaMaterials)
                return false;

            var map = new Dictionary<string, object?>(StringComparer.Ordinal);
            string rest = scope.Substring(bar + 1);
            foreach (var pair in rest.Split(';'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                if (eq <= 0) return false;
                string k = pair.Substring(0, eq);
                string v = pair.Substring(eq + 1);
                // qty is an integer arg; posRadius a double; prefab/itemId strings. The mapper
                // re-validates, so we only need to present the right scalar type here.
                if (k == "qty" && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                    map[k] = l;
                else if (k == "posRadius" && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    map[k] = d;
                else
                    map[k] = v;
            }
            targetArgs = map;
            return true;
        }

        // Deterministic fixture id: a stable digest of (verb, sorted plan args). Same request =>
        // same id => same owned ids + snapshot file, so crash recovery reconciles the exact set.
        private static string DeriveFixtureId(string verb, IReadOnlyDictionary<string, object?> args)
        {
            var sb = new StringBuilder();
            sb.Append(verb);
            var keys = new List<string>(args.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys)
            {
                sb.Append('|').Append(k).Append('=');
                sb.Append(Convert.ToString(args[k], CultureInfo.InvariantCulture) ?? string.Empty);
            }
            // A short, filesystem-safe stable hash keeps the snapshot filename bounded while the
            // human-readable prefix keeps it debuggable.
            return "fx_" + verb + "_" + StableHash(sb.ToString());
        }

        private static string StableHash(string s)
        {
            unchecked
            {
                // FNV-1a 64-bit — deterministic across processes (no salted GetHashCode), no crypto need.
                ulong h = 14695981039346656037UL;
                foreach (char c in s)
                {
                    h ^= c;
                    h *= 1099511628211UL;
                }
                return h.ToString("x16", CultureInfo.InvariantCulture);
            }
        }

        private static FixtureVerbOutcome Describe(FixtureExecResult r, string prefix)
        {
            switch (r.Status)
            {
                case FixtureExecStatus.Executed when prefix == "fixture-ensure":
                    return new FixtureVerbOutcome(true,
                        prefix + ":created=" + r.Created + ",present=" + r.AlreadyPresent +
                        ",failed=" + r.Failed + ",reconciled=" + r.Reconciled);
                case FixtureExecStatus.Executed:
                    return new FixtureVerbOutcome(true,
                        prefix + ":removed=" + r.Removed + ",gone=" + r.AlreadyGone +
                        ",retryable=" + r.Retryable + ",reconciled=" + r.Reconciled);
                case FixtureExecStatus.MapRejected:
                    return new FixtureVerbOutcome(false, prefix + "-rejected:map=" + r.MapReason + ":" + r.Detail);
                case FixtureExecStatus.AuthorityRejected:
                    return new FixtureVerbOutcome(false, prefix + "-rejected:authority=" + r.AuthorityReason);
                case FixtureExecStatus.PersistFailed:
                    return new FixtureVerbOutcome(false, prefix + "-rejected:persist=" + r.Detail);
                case FixtureExecStatus.RecoveryRefused:
                    return new FixtureVerbOutcome(false, prefix + "-rejected:recovery=" + r.Detail);
                case FixtureExecStatus.SnapshotDeleteFailed:
                    return new FixtureVerbOutcome(false,
                        prefix + "-snapshot-delete-failed:removed=" + r.Removed + ",gone=" + r.AlreadyGone +
                        ",retryable=" + r.Retryable + ":" + r.Detail);
                default:
                    return new FixtureVerbOutcome(false, prefix + "-rejected:unknown");
            }
        }

        private static readonly Dictionary<string, object?> EmptyArgs = new(StringComparer.Ordinal);
    }
}
