// ============================================================================
//  IAP-015 — LIVE operator command surface REGISTRATION conformance guard.
// ----------------------------------------------------------------------------
//  RED-FIRST regression for the runtime-proven PR #416 patch-registration defect
//  (live smoke t_48797ca3 at exact candidate 04efd544).
//
//  THE DEFECT: the three net48-only operator patch classes —
//    • OperatorCommandIngressObserver (SERVER per-peer ZRpc request handler),
//    • OperatorCommandConsole         (CLIENT sbpr_pilotop console command),
//    • OperatorCommandReplyClient     (CLIENT server-reply handler)
//  compile and ship in the DLL, but Plugin.Awake() never handed them to
//  harmony.PatchAll(typeof(X)). HarmonyX PatchAll(Type) patches ONLY that type
//  (there is no assembly-wide auto-scan), so all three were dead code: sbpr_pilotop
//  was absent from Terminal.commands and none of the three wove. The entire client
//  console -> server direct-RPC -> client reply adapter did nothing, with NO build
//  error and NO boot signal.
//
//  WHY A SOURCE-CONFORMANCE GUARD (not a link-compiled execution test): this test
//  project deliberately references NO UnityEngine / HarmonyLib / Valheim assemblies
//  (see SBPR.Trailborne.Tests.csproj) so it runs headless in CI with no Valheim SDK.
//  The net48-only Plugin.cs and the operator seams cannot be link-compiled here, and
//  Harmony's weave only happens against assembly_valheim at runtime on a live host.
//  So the strongest regression the clean-room build permits is to assert the SHIPPED
//  Plugin.cs source contains the exact per-type PatchAll registration for each of the
//  three classes. It reads the shipped source directly (drift-proof), fails RED on
//  04efd544 (no registrations), and turns GREEN only when ALL THREE are present.
//
//  It catches removal of ANY ONE line/type: each class is asserted individually via a
//  [Theory], so deleting a single PatchAll line fails that class's case — it is not a
//  mere "the classes compile" check.
// ============================================================================

using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class OperatorSurfaceRegistrationGuardTests
    {
        private const string PluginSource =
            "src/SBPR.Niflheim.HomesteadStones/Plugin.cs";

        // The three exact patch classes that MUST each be registered via a per-type
        // harmony.PatchAll(typeof(Features.PilotIdentity.<Class>)) call in Plugin.Awake().
        public static readonly string[] RequiredPatchClasses =
        {
            "OperatorCommandIngressObserver",
            "OperatorCommandConsole",
            "OperatorCommandReplyClient",
        };

        public static System.Collections.Generic.IEnumerable<object[]> PatchClassCases()
        {
            foreach (var c in RequiredPatchClasses) yield return new object[] { c };
        }

        [Theory]
        [MemberData(nameof(PatchClassCases))]
        public void OperatorPatchClass_isRegisteredInPluginAwake(string patchClass)
        {
            var full = Path.Combine(RepoRoot(), PluginSource);
            Assert.True(File.Exists(full), "shipped Plugin.cs not found: " + full);
            var src = StripLineComments(File.ReadAllText(full));

            // Matches: harmony.PatchAll(typeof(Features.PilotIdentity.<Class>));
            // Whitespace tolerated; the Features.PilotIdentity namespace prefix is required so a
            // like-named type elsewhere cannot satisfy the guard.
            var registration = new Regex(
                @"PatchAll\s*\(\s*typeof\s*\(\s*Features\.PilotIdentity\." +
                Regex.Escape(patchClass) + @"\s*\)\s*\)",
                RegexOptions.Compiled);

            Assert.True(
                registration.IsMatch(src),
                "Plugin.Awake() must register the operator patch class via " +
                "harmony.PatchAll(typeof(Features.PilotIdentity." + patchClass + ")). " +
                "Without it, HarmonyX never patches it and the live operator command surface " +
                "(sbpr_pilotop) ships as dead code — the runtime-proven 04efd544 defect.");
        }

        [Fact]
        public void PluginAwake_runsOperatorSurfaceConformanceDiagnostic()
        {
            // The fail-closed startup diagnostic must be invoked so the next live smoke gets an
            // unmistakable per-role signal (console / server request / client reply) — and a LOUD
            // error if any registration is ever dropped again.
            var full = Path.Combine(RepoRoot(), PluginSource);
            var src = StripLineComments(File.ReadAllText(full));

            var conformanceCall = new Regex(
                @"OperatorSurfaceConformance\s*\.\s*Verify\s*\(",
                RegexOptions.Compiled);

            Assert.True(
                conformanceCall.IsMatch(src),
                "Plugin.Awake() must call OperatorSurfaceConformance.Verify(ModId) after the operator " +
                "PatchAll calls so a forgotten registration is caught loudly at boot.");
        }

        // Remove // line comments so a commented-out registration does NOT satisfy the guard.
        // (Block comments are not used around these calls; a conservative // strip is sufficient and
        // avoids false positives from a neutered `// harmony.PatchAll(...)` line.)
        private static string StripLineComments(string src)
        {
            var sb = new System.Text.StringBuilder(src.Length);
            foreach (var line in src.Split('\n'))
            {
                int idx = line.IndexOf("//", StringComparison.Ordinal);
                sb.Append(idx >= 0 ? line.Substring(0, idx) : line).Append('\n');
            }
            return sb.ToString();
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName,
                        "src", "SBPR.Niflheim.HomesteadStones", "Plugin.cs")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate repo root (src/SBPR.Niflheim.HomesteadStones/Plugin.cs) from " +
                AppContext.BaseDirectory);
        }
    }
}
