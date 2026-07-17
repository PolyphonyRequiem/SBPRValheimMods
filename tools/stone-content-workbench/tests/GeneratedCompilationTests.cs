using System;
using System.Diagnostics;
using System.IO;
using StoneContent.Workbench.Core;
using StoneContent.Workbench.Core.Serialization;
using Xunit;

namespace StoneContent.Workbench.Tests
{
    // Task 5 — the generated scratch output must COMPILE under net8. We generate the four artifacts to
    // a fresh temp directory, drop a minimal net8 library csproj beside them, and run `dotnet build`.
    // This is the "byte-equal is not sufficient" guard: the generated code has to be real C#.
    public sealed class GeneratedCompilationTests
    {
        [Fact]
        public void Generated_artifacts_compile_under_net8()
        {
            var ws = new StoneContentWorkspace();
            var doc = CanonicalJson.Load(TestAssets.AssetJson);
            var result = ws.Generate(doc);

            var dir = Path.Combine(Path.GetTempPath(), "scw-harness-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                foreach (var art in result.Artifacts)
                    File.WriteAllText(Path.Combine(dir, art.FileName), art.Content);

                File.WriteAllText(Path.Combine(dir, "GeneratedHarness.csproj"), @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableDefaultCompileItems>true</EnableDefaultCompileItems>
  </PropertyGroup>
</Project>
");
                // Shield the temp project from any ambient Directory.Build.props above tmp.
                File.WriteAllText(Path.Combine(dir, "Directory.Build.props"), "<Project></Project>\n");

                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/dotnet",
                    Arguments = "build -c Release --nologo",
                    WorkingDirectory = dir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                psi.Environment.Remove("DOTNET_ROOT");
                using var proc = Process.Start(psi)!;
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                Assert.True(proc.ExitCode == 0,
                    $"Generated harness failed to compile.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
            }
        }
    }
}
