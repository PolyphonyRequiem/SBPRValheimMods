// ============================================================================
//  IAP-015 Option-B — split-proof harness regression (t_0d853e78).
// ----------------------------------------------------------------------------
//  This xUnit class is the CI-gating regression for the same-account concurrent-
//  session split proof. It does NOT re-implement the admission logic (that would
//  be a source double the architect's rider forbids); instead it BUILDS the
//  compiled candidate product assembly, computes its SHA-256, and DRIVES the
//  external QaSplitSessionHarness against that exact attested binary — the same
//  shipped-binary path the live QA window runs. It asserts:
//
//    * SHIPPED-GUARD mode PASSES (exit 0): the shipped guard rejects the second
//      same-account peer with AccountAlreadyConnected before any character mint.
//    * BYPASS-GUARD mode FAILS the SAME invariant assertions (exit 1): flipping
//      the one-session guard turns the green assertions red — proving the proof is
//      NON-VACUOUS (it can fail, and only passes because the real guard fences).
//    * Attestation is fail-closed: a missing or mismatched expected SHA-256 makes
//      the harness refuse to run (exit 3), so the evidence is pinned to the exact
//      candidate binary.
//
//  It is guarded by the SBPR_RUN_SPLIT_HARNESS environment variable because it
//  needs the Valheim/BepInEx SDK to build the net48 product assembly, which CI
//  without the SDK cannot do. The engine-free admission behaviour itself is
//  already covered non-conditionally by NiflheimBoundSessionWiringTests and
//  NiflheimPilotCharacterSessionTests; THIS test is the shipped-BINARY linkage +
//  non-vacuity gate for the split-evidence rider, exercised in the QA window.
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimSplitSessionHarnessRegressionTests
    {
        // Opt-in: set SBPR_RUN_SPLIT_HARNESS=1 (the QA window / a dev box with the SDK) to run these.
        private static bool Enabled =>
            string.Equals(Environment.GetEnvironmentVariable("SBPR_RUN_SPLIT_HARNESS"), "1", StringComparison.Ordinal);

        private static string RepoRoot()
        {
            // tests/bin/Release/net8.0 → up to the repo root; also walk up until we find the harness.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "qa-split-session-harness")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "src", "SBPR.Niflheim.HomesteadStones")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("could not locate repo root from " + AppContext.BaseDirectory);
        }

        private static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            var sb = new StringBuilder();
            foreach (byte b in sha.ComputeHash(fs)) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static (int exit, string stdout, string stderr) Run(string fileName, string args, string workdir)
        {
            var psi = new ProcessStartInfo(fileName, args)
            {
                WorkingDirectory = workdir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi)!;
            string so = p.StandardOutput.ReadToEnd();
            string se = p.StandardError.ReadToEnd();
            p.WaitForExit(300_000);
            return (p.ExitCode, so, se);
        }

        [Fact]
        public void SplitProofHarness_ShippedGuardPasses_BypassFails_AttestationIsFailClosed()
        {
            if (!Enabled)
                return; // skipped on SDK-less CI; the QA window sets SBPR_RUN_SPLIT_HARNESS=1.

            string root = RepoRoot();
            string productProj = Path.Combine(root, "src", "SBPR.Niflheim.HomesteadStones", "SBPR.Niflheim.HomesteadStones.csproj");
            string harnessProj = Path.Combine(root, "qa-split-session-harness", "QaSplitSessionHarness.csproj");

            // 1) Build the candidate product assembly fresh, then pin its exact SHA-256.
            var build = Run("dotnet", $"build \"{productProj}\" -c Release", root);
            Assert.True(build.exit == 0, "product build failed:\n" + build.stdout + build.stderr);

            string dll = Path.Combine(root, "src", "SBPR.Niflheim.HomesteadStones", "bin", "Release", "SBPR.Niflheim.HomesteadStones.dll");
            Assert.True(File.Exists(dll), "candidate product DLL missing: " + dll);
            string sha = Sha256(dll);

            var hbuild = Run("dotnet", $"build \"{harnessProj}\" -c Release -p:CANDIDATE_DLL=\"{dll}\"", root);
            Assert.True(hbuild.exit == 0, "harness build failed:\n" + hbuild.stdout + hbuild.stderr);

            string runBase = $"run -c Release --no-build --project \"{harnessProj}\" -p:CANDIDATE_DLL=\"{dll}\" --";

            // 2) SHIPPED-GUARD mode: PASS (exit 0), and it must have attested the exact SHA and rejected
            //    the second peer before mint.
            var ok = Run("dotnet", runBase + $" -e {sha}", root);
            Assert.True(ok.exit == 0, "shipped-guard proof did not pass (exit " + ok.exit + "):\n" + ok.stdout + ok.stderr);
            Assert.Contains("SHA-256 attestation OK", ok.stdout);
            Assert.Contains("AccountAlreadyConnected", ok.stdout);
            Assert.Contains("before character mint", ok.stdout);
            Assert.Contains("RESULT: PASS", ok.stdout);

            // 3) NON-VACUITY: BYPASS-GUARD mode fails the SAME invariant assertions (exit 1).
            var bypass = Run("dotnet", runBase + $" -e {sha} --bypass-guard", root);
            Assert.True(bypass.exit == 1, "bypass-guard control should FAIL the invariant (exit 1) but exited " + bypass.exit + ":\n" + bypass.stdout + bypass.stderr);
            Assert.Contains("RESULT: FAIL", bypass.stdout);

            // 4) Attestation fail-closed: missing hash refuses to run (exit 3).
            var noHash = Run("dotnet", runBase, root);
            Assert.True(noHash.exit == 3, "missing-hash run should fail attestation (exit 3) but exited " + noHash.exit + ":\n" + noHash.stdout + noHash.stderr);
            Assert.Contains("ATTESTATION FAILED", noHash.stderr);

            // 5) Attestation fail-closed: a wrong hash refuses to run (exit 3).
            var wrongHash = Run("dotnet", runBase + " -e 00deadbeef", root);
            Assert.True(wrongHash.exit == 3, "wrong-hash run should fail attestation (exit 3) but exited " + wrongHash.exit + ":\n" + wrongHash.stdout + wrongHash.stderr);
            Assert.Contains("SHA-256 mismatch", wrongHash.stderr);
        }
    }
}
