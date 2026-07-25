// ============================================================================
//  Niflheim 0003 — cold-reload capture harness REGISTRATION conformance guard.
// ----------------------------------------------------------------------------
//  Mirrors OperatorSurfaceRegistrationGuardTests. The net48 capture observer
//  (HomesteadReloadCaptureObserver) is compiled into the shipping DLL, but a
//  compiled class is NOT registered until Plugin.Awake() hands it to
//  harmony.PatchAll(typeof(Features.ReloadHarness.HomesteadReloadCaptureObserver)).
//  HarmonyX PatchAll(Type) patches ONLY that type — there is no assembly auto-scan —
//  so a forgotten registration would ship the whole capture path as dead code with
//  NO build error and NO boot signal (the exact 04efd544-class defect).
//
//  This test project references no UnityEngine/HarmonyLib/Valheim, so the observer
//  and Plugin.cs cannot be link-compiled or Harmony-woven headless. The strongest
//  regression the clean-room build permits is a SOURCE-conformance guard: assert the
//  shipped Plugin.cs contains the exact per-type PatchAll registration AND the
//  fail-closed conformance diagnostic call. It is mutation-sensitive — deleting
//  either line turns the corresponding case RED.
// ============================================================================

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimColdReloadHarnessRegistrationGuardTests
    {
        private const string PluginSource =
            "src/SBPR.Niflheim.HomesteadStones/Plugin.cs";

        [Fact]
        public void PluginAwake_registers_the_cold_reload_capture_observer()
        {
            var src = StripLineComments(ReadPluginSource());
            var registration = new Regex(
                @"PatchAll\s*\(\s*typeof\s*\(\s*Features\.ReloadHarness\.HomesteadReloadCaptureObserver\s*\)\s*\)",
                RegexOptions.Compiled);

            Assert.True(
                registration.IsMatch(src),
                "Plugin.Awake() must register the capture observer via harmony.PatchAll(typeof(" +
                "Features.ReloadHarness.HomesteadReloadCaptureObserver)). Without it, HarmonyX never " +
                "patches it and the QA-only cold-reload capture path ships as dead code.");
        }

        [Fact]
        public void PluginAwake_runs_the_capture_harness_conformance_diagnostic()
        {
            var src = StripLineComments(ReadPluginSource());
            var conformanceCall = new Regex(
                @"HomesteadReloadHarnessConformance\s*\.\s*Verify\s*\(",
                RegexOptions.Compiled);

            Assert.True(
                conformanceCall.IsMatch(src),
                "Plugin.Awake() must call HomesteadReloadHarnessConformance.Verify(ModId) after the harness " +
                "PatchAll so a forgotten registration is caught loudly at boot.");
        }

        [Fact]
        public void PluginAwake_binds_the_qa_only_enablement_flag_defaulting_false()
        {
            var src = StripLineComments(ReadPluginSource());
            // The enablement must be a Config.Bind with default `false` — the harness is inert unless a
            // server operator explicitly turns it on for a QA window.
            var bindFalse = new Regex(
                @"EnableColdReloadCaptureHarness""\s*,\s*false",
                RegexOptions.Compiled);

            Assert.True(
                bindFalse.IsMatch(src),
                "The cold-reload capture harness must be gated by a Config.Bind flag defaulting to false so it " +
                "is inert in normal product use.");
        }

        private static string ReadPluginSource()
        {
            var full = Path.Combine(RepoRoot(), PluginSource);
            Assert.True(File.Exists(full), "shipped Plugin.cs not found: " + full);
            return File.ReadAllText(full);
        }

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
