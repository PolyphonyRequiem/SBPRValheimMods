// ============================================================================
//  §2L.13 (card t_a1cf35b0) — InventoryOpenSuppressPatch REGISTRATION guard.
// ----------------------------------------------------------------------------
//  Deterministic source-conformance regression closing REVIEW t_88a76210's
//  CHANGES_REQUESTED finding: deleting the Plugin.Awake() registration
//    harmony.PatchAll(typeof(
//        SBPR.Trailborne.Features.Signs.SignPanelInputBlock.InventoryOpenSuppressPatch));
//  left ALL 1505 tests green. HarmonyX PatchAll(Type) patches ONLY the given
//  type (no assembly-wide auto-scan), so without that exact line the
//  InventoryGui.Show(Container,int) skip-original prefix ships as dead code and
//  the Inventory hotkey can pop the vanilla inventory over an SBPR sign modal.
//  Boot-time PatchCheck alone is NOT deterministic CI coverage.
//
//  WHY A SOURCE-CONFORMANCE GUARD (not a link-compiled execution test): this
//  test project references NO UnityEngine / HarmonyLib / Valheim assemblies
//  (see SBPR.Trailborne.Tests.csproj) so it runs headless in SDK-less CI. The
//  net48-only Plugin.cs and the sign seams cannot be link-compiled here, and
//  Harmony's weave only happens against assembly_valheim at runtime on a live
//  host. So the strongest regression the clean-room build permits is to assert
//  the SHIPPED Plugin.cs source contains the exact per-type PatchAll
//  registration. It reads the shipped source directly (drift-proof), fails RED
//  when that line is removed/commented/renamed, and is GREEN only when present.
//
//  This mirrors the established OperatorSurfaceRegistrationGuardTests idiom.
// ============================================================================

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class InventoryOpenSuppressRegistrationGuardTests
    {
        private const string PluginSource = "src/SBPR.Trailborne/Plugin.cs";

        [Fact]
        public void InventoryOpenSuppressPatch_isRegisteredInPluginAwake()
        {
            var full = Path.Combine(RepoRoot(), PluginSource);
            Assert.True(File.Exists(full), "shipped Plugin.cs not found: " + full);
            var src = StripLineComments(File.ReadAllText(full));

            // Matches:
            //   harmony.PatchAll(typeof(
            //       SBPR.Trailborne.Features.Signs.SignPanelInputBlock.InventoryOpenSuppressPatch));
            // Whitespace/newlines tolerated; the fully-qualified nested-type path is required so a
            // like-named type elsewhere cannot satisfy the guard.
            var registration = new Regex(
                @"PatchAll\s*\(\s*typeof\s*\(\s*" +
                @"SBPR\.Trailborne\.Features\.Signs\.SignPanelInputBlock\.InventoryOpenSuppressPatch" +
                @"\s*\)\s*\)",
                RegexOptions.Compiled);

            Assert.True(
                registration.IsMatch(src),
                "Plugin.Awake() must register the sign-modal inventory suppressor via " +
                "harmony.PatchAll(typeof(SBPR.Trailborne.Features.Signs.SignPanelInputBlock." +
                "InventoryOpenSuppressPatch)). Without it, HarmonyX never patches it and the " +
                "InventoryGui.Show(Container,int) skip-original prefix (§2L.13, card t_a1cf35b0) " +
                "ships as dead code — the Inventory hotkey pops the vanilla inventory over an " +
                "SBPR sign modal (REVIEW t_88a76210 CHANGES_REQUESTED finding).");
        }

        // Remove // line comments so a commented-out registration does NOT satisfy the guard.
        // (Block comments are not used around these calls; a conservative // strip is sufficient
        // and avoids false positives from a neutered `// harmony.PatchAll(...)` line.)
        private static string StripLineComments(string src)
        {
            var sb = new StringBuilder(src.Length);
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
                if (File.Exists(Path.Combine(dir.FullName, "src", "SBPR.Trailborne", "Plugin.cs")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate repo root (src/SBPR.Trailborne/Plugin.cs) from " +
                AppContext.BaseDirectory);
        }
    }
}
