using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StoneContent.Workbench.Core;
using StoneContent.Workbench.Core.Generation;
using StoneContent.Workbench.Core.Validation;

namespace StoneContent.Workbench.Cli
{
    // Thin CLI adapter over the StoneContentWorkspace deep module (POC, card t_e2de37e4). This layer
    // owns ALL file I/O and console presentation; every decision comes from the core.
    //
    //   stone-content validate <asset> [--json]
    //   stone-content generate <asset> --output <scratch-dir> [--json]
    //   stone-content check    <asset> --generated <dir> [--json]
    //   stone-content serve    ...   -> RESERVED for the UI child card (t_e4d16b1c); not implemented.
    //
    // Exit 0 only on a clean result; nonzero on validation / drift / compile / usage failure.
    // `generate` REFUSES to write into a production catalog path during the POC — it requires an
    // explicit scratch --output directory that is not under src/.
    internal static class Program
    {
        private const int ExitOk = 0;
        private const int ExitFailure = 1;
        private const int ExitUsage = 2;

        private static int Main(string[] args)
        {
            if (args.Length == 0)
                return Usage("no command given");

            var command = args[0];
            var rest = args.Skip(1).ToArray();
            try
            {
                return command switch
                {
                    "validate" => Validate(rest),
                    "generate" => Generate(rest),
                    "check" => Check(rest),
                    "serve" => ServeReserved(),
                    "-h" or "--help" or "help" => Usage(null),
                    _ => Usage($"unknown command '{command}'"),
                };
            }
            catch (CliError ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                return ExitUsage;
            }
        }

        private sealed class CliError : Exception
        {
            public CliError(string message) : base(message) { }
        }

        private static int Validate(string[] args)
        {
            var opts = ParseOptions(args, requirePositional: true);
            var (doc, loadErr) = LoadDocument(opts.Positional!);
            if (doc == null)
            {
                return EmitLoadError(loadErr!, opts.Json);
            }

            var ws = new StoneContentWorkspace();
            var report = ws.Validate(doc);
            if (opts.Json)
                Console.WriteLine(JsonDiagnostics(report.Diagnostics, report.HasErrors ? "invalid" : "valid"));
            else
            {
                if (report.IsClean) Console.WriteLine("valid: no diagnostics.");
                else foreach (var d in report.Diagnostics) Console.WriteLine(d.ToString());
            }
            return report.HasErrors ? ExitFailure : ExitOk;
        }

        private static int Generate(string[] args)
        {
            var opts = ParseOptions(args, requirePositional: true);
            var output = opts.Get("--output") ?? throw new CliError("generate requires --output <scratch-dir>");
            GuardScratchPath(output);

            var (doc, loadErr) = LoadDocument(opts.Positional!);
            if (doc == null) return EmitLoadError(loadErr!, opts.Json);

            var ws = new StoneContentWorkspace();
            var report = ws.Validate(doc);
            if (report.HasErrors)
            {
                if (opts.Json) Console.WriteLine(JsonDiagnostics(report.Diagnostics, "generation-blocked"));
                else
                {
                    Console.Error.WriteLine("generation blocked: validation errors:");
                    foreach (var d in report.Diagnostics) Console.Error.WriteLine("  " + d);
                }
                return ExitFailure;
            }

            Directory.CreateDirectory(output);
            var result = ws.Generate(doc);
            foreach (var art in result.Artifacts)
                File.WriteAllText(Path.Combine(output, art.FileName), art.Content);

            if (opts.Json)
                Console.WriteLine("{\"status\":\"generated\",\"output\":" + JsonString(output) +
                    ",\"artifacts\":[" + string.Join(",", result.Artifacts.Select(a => JsonString(a.FileName))) + "]}");
            else
            {
                Console.WriteLine($"generated {result.Artifacts.Count} artifact(s) into {output}:");
                foreach (var art in result.Artifacts) Console.WriteLine("  " + art.FileName);
            }
            return ExitOk;
        }

        private static int Check(string[] args)
        {
            var opts = ParseOptions(args, requirePositional: true);
            var genDir = opts.Get("--generated") ?? throw new CliError("check requires --generated <dir>");

            var (doc, loadErr) = LoadDocument(opts.Positional!);
            if (doc == null) return EmitLoadError(loadErr!, opts.Json);

            var onDisk = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Directory.Exists(genDir))
                foreach (var file in Directory.GetFiles(genDir, "*.g.cs"))
                    onDisk[Path.GetFileName(file)] = File.ReadAllText(file);

            var ws = new StoneContentWorkspace();
            var result = ws.Check(doc, onDisk);
            if (opts.Json)
                Console.WriteLine(JsonDiagnostics(result.Diagnostics, result.Ok ? "clean" : "drift"));
            else
            {
                if (result.Ok) Console.WriteLine("check: clean — generated output matches the asset.");
                else foreach (var d in result.Diagnostics) Console.WriteLine(d.ToString());
            }
            return result.Ok ? ExitOk : ExitFailure;
        }

        private static int ServeReserved()
        {
            Console.Error.WriteLine("serve: RESERVED for the Stone Content Workbench UI child card " +
                "(t_e4d16b1c). Not implemented in the core POC.");
            return ExitUsage;
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────
        private static (Core.Model.StoneContentDocument?, string?) LoadDocument(string path)
        {
            if (!File.Exists(path)) throw new CliError($"asset file not found: {path}");
            var ws = new StoneContentWorkspace();
            var load = ws.Load(File.ReadAllText(path));
            return (load.Document, load.Error);
        }

        private static int EmitLoadError(string error, bool json)
        {
            if (json)
                Console.WriteLine("{\"status\":\"load-error\",\"error\":" + JsonString(error) + "}");
            else
                Console.Error.WriteLine("load error: " + error);
            return ExitFailure;
        }

        // The POC never generates into production source. Reject an --output that lands under a
        // repo src/ directory (or literally equals one).
        private static void GuardScratchPath(string output)
        {
            var full = Path.GetFullPath(output).Replace('\\', '/');
            var segments = full.Split('/');
            if (segments.Any(s => string.Equals(s, "src", StringComparison.Ordinal)))
                throw new CliError(
                    "generate refuses to write under a 'src/' path during the POC; " +
                    "choose an explicit scratch directory outside production source.");
        }

        private sealed class Options
        {
            public string? Positional;
            public bool Json;
            private readonly Dictionary<string, string> _named = new(StringComparer.Ordinal);
            public void Set(string key, string value) => _named[key] = value;
            public string? Get(string key) => _named.TryGetValue(key, out var v) ? v : null;
        }

        private static Options ParseOptions(string[] args, bool requirePositional)
        {
            var opts = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a == "--json") { opts.Json = true; }
                else if (a.StartsWith("--", StringComparison.Ordinal))
                {
                    if (i + 1 >= args.Length) throw new CliError($"option '{a}' requires a value");
                    opts.Set(a, args[++i]);
                }
                else
                {
                    if (opts.Positional != null) throw new CliError($"unexpected extra argument '{a}'");
                    opts.Positional = a;
                }
            }
            if (requirePositional && opts.Positional == null)
                throw new CliError("missing <asset> path");
            return opts;
        }

        private static string JsonString(string s)
        {
            var sb = new System.Text.StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string JsonDiagnostics(IReadOnlyList<ContentDiagnostic> diags, string status)
        {
            var items = diags.Select(d =>
                "{\"code\":" + JsonString(d.Code) +
                ",\"severity\":" + JsonString(d.Severity.ToString()) +
                ",\"path\":" + JsonString(d.Path) +
                ",\"detail\":" + JsonString(d.Detail) + "}");
            return "{\"status\":" + JsonString(status) + ",\"diagnostics\":[" + string.Join(",", items) + "]}";
        }

        private static int Usage(string? error)
        {
            if (error != null) Console.Error.WriteLine("error: " + error);
            Console.Error.WriteLine(@"stone-content — Stone Content Authoring Workbench CLI (POC)

Usage:
  stone-content validate <asset> [--json]
  stone-content generate <asset> --output <scratch-dir> [--json]
  stone-content check    <asset> --generated <dir> [--json]
  stone-content serve    (reserved for the UI child card — not implemented)

Exit codes: 0 = clean, 1 = validation/drift failure, 2 = usage error.");
            return error == null ? ExitOk : ExitUsage;
        }
    }
}
