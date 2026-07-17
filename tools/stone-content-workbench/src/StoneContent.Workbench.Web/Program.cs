using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StoneContent.Workbench.Web
{
    // Thin loopback-only host for the Stone Content Workbench UI (POC, card t_e4d16b1c).
    //
    //   stone-content-web --asset <path> [--scratch <dir>] [--port <n>]
    //
    // Binds 127.0.0.1 ONLY (never a routable interface). The host grants exactly one asset root and
    // one scratch output root at startup; the browser cannot supply arbitrary server paths. All
    // authoritative behavior lives in the Core deep module, reached through WorkbenchService.
    public static class Program
    {
        internal static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        public static int Main(string[] args)
        {
            string? assetPath = null;
            string? scratch = null;
            int port = 5177;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--asset": assetPath = Next(args, ref i); break;
                    case "--scratch": scratch = Next(args, ref i); break;
                    case "--port": port = int.Parse(Next(args, ref i)); break;
                    case "-h" or "--help":
                        Console.WriteLine("stone-content-web --asset <path> [--scratch <dir>] [--port <n>]");
                        return 0;
                    default:
                        Console.Error.WriteLine($"unknown argument '{args[i]}'");
                        return 2;
                }
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                // Default to the checked-in canonical asset relative to the repo, for convenience.
                assetPath = FindDefaultAsset();
                if (assetPath == null)
                {
                    Console.Error.WriteLine("error: --asset <path> is required (no default canonical asset found).");
                    return 2;
                }
            }
            assetPath = Path.GetFullPath(assetPath);
            if (!File.Exists(assetPath))
            {
                Console.Error.WriteLine($"error: asset not found: {assetPath}");
                return 2;
            }

            scratch ??= Path.Combine(Path.GetTempPath(), "stone-content-workbench-scratch");
            scratch = Path.GetFullPath(scratch);

            var service = new WorkbenchService(assetPath, scratch);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                // Serve wwwroot regardless of the process CWD: prefer a copy next to the assembly,
                // else fall back to the project content root's wwwroot (the `dotnet run` case).
                WebRootPath = ResolveWebRoot(),
            });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
            builder.Services.AddSingleton(service);

            var app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles();

            MapEndpoints(app, service);

            Console.WriteLine($"Stone Content Workbench — loopback host");
            Console.WriteLine($"  asset   : {assetPath}");
            Console.WriteLine($"  scratch : {scratch}");
            Console.WriteLine($"  url     : http://127.0.0.1:{port}/");
            app.Run();
            return 0;
        }

        // Exposed so integration tests can build the same endpoint graph against a TestServer.
        internal static void MapEndpoints(WebApplication app, WorkbenchService service)
        {
            app.MapGet("/api/document", () => JsonResult(service.GetDocument()));

            app.MapPost("/api/validate", async (HttpRequest req) =>
            {
                var body = await ReadBody(req);
                return JsonResult(service.Validate(body.Document));
            });

            app.MapPost("/api/generate-preview", async (HttpRequest req) =>
            {
                var body = await ReadBody(req);
                return JsonResult(service.GeneratePreview(body.Document));
            });

            app.MapPost("/api/export", async (HttpRequest req) =>
            {
                var body = await ReadBody(req);
                return JsonResult(service.Export(body.Document, body.BaselineHash ?? ""));
            });
        }

        private sealed record RequestBody(
            [property: JsonPropertyName("document")] string Document,
            [property: JsonPropertyName("baselineHash")] string? BaselineHash);

        private static async System.Threading.Tasks.Task<RequestBody> ReadBody(HttpRequest req)
        {
            using var reader = new StreamReader(req.Body);
            var text = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(text)) return new RequestBody("", null);
            try
            {
                var parsed = JsonSerializer.Deserialize<RequestBody>(text, Json);
                return parsed ?? new RequestBody("", null);
            }
            catch (JsonException)
            {
                return new RequestBody("", null);
            }
        }

        private static IResult JsonResult(object payload) =>
            Results.Text(JsonSerializer.Serialize(payload, Json), "application/json");

        private static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException($"option '{args[i]}' requires a value");
            return args[++i];
        }

        // Walk up from the base directory to find tools/stone-content-workbench/assets/...json.
        private static string? FindDefaultAsset()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName,
                    "tools", "stone-content-workbench", "assets", "homestead-stone.content.json");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        // wwwroot lives beside Program.cs in the project content root; the Web SDK does NOT copy it to
        // bin by default. Prefer a bin-adjacent copy if present (e.g. a published layout), else walk up
        // from the assembly base directory to the project's wwwroot.
        private static string ResolveWebRoot()
        {
            var adjacent = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (File.Exists(Path.Combine(adjacent, "index.html"))) return adjacent;

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var proj = Path.Combine(dir.FullName, "wwwroot", "index.html");
                if (File.Exists(proj)) return Path.Combine(dir.FullName, "wwwroot");
                // Also handle the case where we descended into bin/Release/net8.0: the project root
                // with wwwroot is a few levels up.
                var candidate = Path.Combine(dir.FullName,
                    "tools", "stone-content-workbench", "src", "StoneContent.Workbench.Web", "wwwroot");
                if (File.Exists(Path.Combine(candidate, "index.html"))) return candidate;
                dir = dir.Parent;
            }
            return adjacent;
        }
    }
}
