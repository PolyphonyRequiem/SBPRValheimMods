// ============================================================================
//  Niflheim 0003 — cold-reload CONFIGURATION INGRESS reachability + drift tests.
// ----------------------------------------------------------------------------
//  These tests close the reviewer-proven reachability defect: before the ingress
//  existed, HomesteadReloadCaptureObserver.{Manifest,CaptureOutputDir,Provenance,
//  SaveReceipt} had ZERO committed writers, so the observer always saw Manifest=null
//  and refused, and the controller exported variables the C# side never read.
//
//  This suite invokes the REAL configuration ingress the net48 observer calls
//  (HomesteadReloadConfigurationIngress.Bind), with a dictionary-backed environment
//  reader, and proves:
//    * a valid isolated manifest env makes the ingress READY — it binds a manifest,
//      selects a bounded absolute capture destination, and the arming gate arms
//      (the observer can become armed);
//    * every missing/malformed/forbidden input refuses fail-closed with NO armed config;
//    * the controller exports EXACTLY the variable names the C# ingress reads
//      (mutation-sensitive drift guard over the shipped controller.sh source).
//
//  It runs no Valheim client and proves ONLY configuration reachability — not
//  save/cold-reload/live success (that stays owned by t_1a1164f4).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Domain.ReloadHarness;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimColdReloadHarnessConfigurationIngressTests
    {
        private const long FixtureUid = 2413287143L;

        /// <summary>A complete, valid, isolated PRE-boot environment. Every negative test mutates ONE key of a copy.</summary>
        private static Dictionary<string, string> ValidPreEnv() => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [HomesteadReloadEnv.Phase] = "PRE",
            [HomesteadReloadEnv.CaptureDir] = "/tmp/niflheim-0003-reload/evidence",
            [HomesteadReloadEnv.WorldUid] = FixtureUid.ToString(),
            [HomesteadReloadEnv.LeaseId] = "lease-abc",
            [HomesteadReloadEnv.RollbackHash] = "rollbackHASH",
            [HomesteadReloadEnv.DisposableDbPresent] = "true",
            [HomesteadReloadEnv.DisposableFwlPresent] = "true",
            [HomesteadReloadEnv.TargetWorldName] = "astley-disposable-qa",
            [HomesteadReloadEnv.TargetPort] = "2600",
            [HomesteadReloadEnv.ReadinessWaitSeconds] = "300",
            [HomesteadReloadEnv.PhaseWaitSeconds] = "60",
            [HomesteadReloadEnv.ReadinessRetries] = "1",
            [HomesteadReloadEnv.ProvSourceHash] = "srcHASH",
            [HomesteadReloadEnv.ProvProductHash] = "prodHASH",
            [HomesteadReloadEnv.ProvHarnessHash] = "harnessHASH",
            [HomesteadReloadEnv.SavePresent] = "false",
            [HomesteadReloadEnv.SaveDbHash] = "",
            [HomesteadReloadEnv.SaveAtUtc] = "",
        };

        /// <summary>A complete, valid, isolated POST-boot environment (carries a real save receipt).</summary>
        private static Dictionary<string, string> ValidPostEnv()
        {
            var env = ValidPreEnv();
            env[HomesteadReloadEnv.Phase] = "POST";
            env[HomesteadReloadEnv.SavePresent] = "true";
            env[HomesteadReloadEnv.SaveDbHash] = "savedDbHASH";
            env[HomesteadReloadEnv.SaveAtUtc] = "2026-07-25T00:05:00Z";
            return env;
        }

        private static Func<string, string?> Reader(IReadOnlyDictionary<string, string> env) =>
            name => env.TryGetValue(name, out var value) ? value : null;

        private static HomesteadReloadConfiguration Bind(IReadOnlyDictionary<string, string> env) =>
            HomesteadReloadConfigurationIngress.Bind(Reader(env));

        // ── Reachability: the observer CAN become armed from a valid isolated manifest ──

        [Fact]
        public void Bind_arms_and_binds_every_input_from_a_valid_pre_manifest()
        {
            var config = Bind(ValidPreEnv());

            Assert.True(config.IsReady, string.Join(" | ", config.Refusals));
            Assert.True(config.Arming.IsArmed, string.Join(" | ", config.Arming.Refusals));
            Assert.NotNull(config.Manifest);
            Assert.Equal(FixtureUid, config.Manifest!.ExpectedWorldUid);
            Assert.Equal("/tmp/niflheim-0003-reload/evidence", config.CaptureOutputDir);
            Assert.Equal(HomesteadReloadPhase.Pre, config.Phase);
            Assert.Equal("srcHASH", config.Provenance.SourceHash);
            Assert.Equal("prodHASH", config.Provenance.ProductHash);
            Assert.Equal("harnessHASH", config.Provenance.HarnessHash);
            Assert.False(config.SaveReceipt.Present);
        }

        [Fact]
        public void Bind_arms_a_valid_post_manifest_with_a_save_receipt()
        {
            var config = Bind(ValidPostEnv());

            Assert.True(config.IsReady, string.Join(" | ", config.Refusals));
            Assert.Equal(HomesteadReloadPhase.Post, config.Phase);
            Assert.True(config.SaveReceipt.Present);
            Assert.Equal("savedDbHASH", config.SaveReceipt.DbFileHash);
            Assert.Equal("2026-07-25T00:05:00Z", config.SaveReceipt.SavedAtUtc);
        }

        [Fact]
        public void Bind_selects_a_bounded_capture_destination()
        {
            // Reachability of the WriteCapture destination: a ready config yields a non-empty absolute dir.
            var config = Bind(ValidPreEnv());
            Assert.True(config.IsReady);
            Assert.False(string.IsNullOrWhiteSpace(config.CaptureOutputDir));
            Assert.StartsWith("/", config.CaptureOutputDir);
        }

        // ── Fail-closed: every required key missing refuses with no armed config ──

        [Theory]
        [InlineData(HomesteadReloadEnv.Phase)]
        [InlineData(HomesteadReloadEnv.CaptureDir)]
        [InlineData(HomesteadReloadEnv.WorldUid)]
        [InlineData(HomesteadReloadEnv.LeaseId)]
        [InlineData(HomesteadReloadEnv.RollbackHash)]
        [InlineData(HomesteadReloadEnv.DisposableDbPresent)]
        [InlineData(HomesteadReloadEnv.DisposableFwlPresent)]
        [InlineData(HomesteadReloadEnv.TargetWorldName)]
        [InlineData(HomesteadReloadEnv.TargetPort)]
        [InlineData(HomesteadReloadEnv.ReadinessWaitSeconds)]
        [InlineData(HomesteadReloadEnv.PhaseWaitSeconds)]
        [InlineData(HomesteadReloadEnv.ReadinessRetries)]
        [InlineData(HomesteadReloadEnv.ProvSourceHash)]
        [InlineData(HomesteadReloadEnv.ProvProductHash)]
        [InlineData(HomesteadReloadEnv.ProvHarnessHash)]
        public void Bind_refuses_when_a_required_key_is_absent(string keyToRemove)
        {
            var env = ValidPreEnv();
            env.Remove(keyToRemove);
            var config = Bind(env);

            Assert.False(config.IsReady);
            Assert.NotEmpty(config.Refusals);
            Assert.Null(config.Manifest);
            Assert.False(config.Arming.IsArmed);
        }

        [Fact]
        public void Bind_refuses_an_unknown_phase()
        {
            var env = ValidPreEnv();
            env[HomesteadReloadEnv.Phase] = "MIDDLE";
            var config = Bind(env);
            Assert.False(config.IsReady);
            Assert.Contains(config.Refusals, r => r.Contains("PRE/POST"));
        }

        [Theory]
        [InlineData("relative/evidence")]
        [InlineData("/tmp/../etc/evidence")]
        [InlineData("")]
        public void Bind_refuses_an_unsafe_or_relative_capture_path(string path)
        {
            var env = ValidPreEnv();
            env[HomesteadReloadEnv.CaptureDir] = path;
            var config = Bind(env);
            Assert.False(config.IsReady);
        }

        [Fact]
        public void Bind_refuses_a_wrong_fixture_uid_against_the_source_fixed_uid()
        {
            // The observer used to compare the manifest UID against ITSELF (a tautology). The ingress arms
            // against the SOURCE-FIXED fixture UID, so a drifted WORLD_UID now actually refuses.
            var env = ValidPreEnv();
            env[HomesteadReloadEnv.WorldUid] = "999";
            var config = Bind(env);
            Assert.False(config.IsReady);
            Assert.Contains(config.Refusals, r => r.Contains("fixture UID"));
        }

        [Theory]
        [InlineData("Niflheim-prod")]
        [InlineData("heistan-main")]
        public void Bind_refuses_a_forbidden_production_world_name(string name)
        {
            var env = ValidPreEnv();
            env[HomesteadReloadEnv.TargetWorldName] = name;
            var config = Bind(env);
            Assert.False(config.IsReady);
            Assert.Contains(config.Refusals, r => r.Contains("production world"));
        }

        [Theory]
        [InlineData("2456")]
        [InlineData("2467")]
        public void Bind_refuses_a_forbidden_production_port(string port)
        {
            var env = ValidPreEnv();
            env[HomesteadReloadEnv.TargetPort] = port;
            var config = Bind(env);
            Assert.False(config.IsReady);
            Assert.Contains(config.Refusals, r => r.Contains("production port"));
        }

        [Theory]
        [InlineData(HomesteadReloadEnv.DisposableDbPresent)]
        [InlineData(HomesteadReloadEnv.DisposableFwlPresent)]
        public void Bind_refuses_a_non_isolated_fixture_absent_flag(string presenceKey)
        {
            var env = ValidPreEnv();
            env[presenceKey] = "false";
            var config = Bind(env);
            Assert.False(config.IsReady);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-5")]
        [InlineData("not-a-number")]
        [InlineData("100000")]
        public void Bind_refuses_an_unbounded_or_malformed_readiness_wait(string seconds)
        {
            var env = ValidPreEnv();
            env[HomesteadReloadEnv.ReadinessWaitSeconds] = seconds;
            var config = Bind(env);
            Assert.False(config.IsReady);
        }

        [Fact]
        public void Bind_refuses_more_than_one_readiness_retry()
        {
            var env = ValidPreEnv();
            env[HomesteadReloadEnv.ReadinessRetries] = "2";
            var config = Bind(env);
            Assert.False(config.IsReady);
            Assert.Contains(config.Refusals, r => r.Contains("retries"));
        }

        [Fact]
        public void Bind_refuses_a_post_boot_missing_its_save_receipt()
        {
            var env = ValidPostEnv();
            env[HomesteadReloadEnv.SavePresent] = "false";
            var config = Bind(env);
            Assert.False(config.IsReady);
            Assert.Contains(config.Refusals, r => r.Contains("save receipt"));
        }

        [Fact]
        public void Bind_refuses_a_post_boot_with_an_incomplete_save_receipt()
        {
            var env = ValidPostEnv();
            env[HomesteadReloadEnv.SaveDbHash] = "";
            var config = Bind(env);
            Assert.False(config.IsReady);
            Assert.Contains(config.Refusals, r => r.Contains("incomplete"));
        }

        [Fact]
        public void Bind_refuses_a_pre_boot_that_carries_a_save_receipt()
        {
            var env = ValidPreEnv();
            env[HomesteadReloadEnv.SavePresent] = "true";
            env[HomesteadReloadEnv.SaveDbHash] = "x";
            env[HomesteadReloadEnv.SaveAtUtc] = "y";
            var config = Bind(env);
            Assert.False(config.IsReady);
            Assert.Contains(config.Refusals, r => r.Contains("PRE boot must not carry"));
        }

        [Fact]
        public void Bind_refuses_a_malformed_boolean_presence_flag()
        {
            var env = ValidPreEnv();
            env[HomesteadReloadEnv.DisposableDbPresent] = "yes";
            var config = Bind(env);
            Assert.False(config.IsReady);
        }

        // ── Mutation-sensitive controller ⇄ C# variable-name drift guard ──────────

        [Fact]
        public void Controller_exports_every_variable_the_csharp_ingress_reads()
        {
            var controller = ReadController();
            foreach (var name in HomesteadReloadEnv.All)
            {
                Assert.True(
                    controller.Contains(name, StringComparison.Ordinal),
                    $"controller.sh must export '{name}' — the C# ingress reads it, so a missing export silently " +
                    "breaks the controller→observer handoff. Keep controller.sh and HomesteadReloadEnv in lockstep.");
            }
        }

        [Fact]
        public void Ingress_reads_no_variable_the_controller_does_not_export()
        {
            // Reverse direction: every NIFLHEIM_RELOAD_HARNESS_* the controller sets must be a known contract
            // key, so a controller-side rename cannot drift away from the C# reader undetected.
            var controller = ReadController();
            var known = new HashSet<string>(HomesteadReloadEnv.All, StringComparer.Ordinal);
            foreach (var exported in ExtractExportedHarnessVars(controller))
                Assert.True(
                    known.Contains(exported),
                    $"controller.sh sets '{exported}' but the C# HomesteadReloadEnv contract does not read it — drift.");
        }

        private static IEnumerable<string> ExtractExportedHarnessVars(string controller)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(
                controller, @"NIFLHEIM_RELOAD_HARNESS_[A-Z_]+");
            return matches.Select(m => m.Value).Distinct(StringComparer.Ordinal);
        }

        private static string ReadController()
        {
            var path = Path.Combine(
                RepoRoot(), "tools", "niflheim-homestead-reload-harness", "controller.sh");
            Assert.True(File.Exists(path), "controller.sh not found: " + path);
            return File.ReadAllText(path);
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
                "Could not locate repo root from " + AppContext.BaseDirectory);
        }
    }
}
