using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StoneContent.Workbench.Core;
using StoneContent.Workbench.Core.Model;
using StoneContent.Workbench.Core.Validation;

namespace StoneContent.Workbench.Web
{
    // The web adapter's service layer. Like the CLI, it owns file I/O and presentation-shaping ONLY;
    // every authoritative decision (parse / validate / classify / generate) comes from the
    // StoneContentWorkspace deep module. The browser calls these results and never reimplements
    // validation. Kept as a plain class (no ASP.NET types) so WebContractTests can exercise it
    // directly without spinning a Kestrel host.
    //
    // File-safety contract (plan Task 8):
    //   * The host grants exactly ONE asset root (the canonical file) and ONE scratch output root at
    //     startup. The browser can never supply an arbitrary server path.
    //   * The baseline file content + its SHA-256 are captured at load. Export refuses (stale-write)
    //     when the on-disk asset changed underneath the editing session.
    //   * Export writes to a temporary sibling in the scratch root, flushes, then atomically renames.
    //     A stale baseline, an invalid document, or a generator failure never overwrites anything.
    public sealed class WorkbenchService
    {
        private readonly StoneContentWorkspace _workspace = new();
        private readonly string _assetPath;
        private readonly string _scratchRoot;

        public WorkbenchService(string assetPath, string scratchRoot)
        {
            _assetPath = Path.GetFullPath(assetPath ?? throw new ArgumentNullException(nameof(assetPath)));
            _scratchRoot = Path.GetFullPath(scratchRoot ?? throw new ArgumentNullException(nameof(scratchRoot)));
        }

        public string AssetPath => _assetPath;
        public string ScratchRoot => _scratchRoot;

        // ── document ────────────────────────────────────────────────────────────────────────────
        public sealed record DocumentResult(
            bool Ok, string? Json, string? AssetId, string? BaselineHash, string ScratchRoot, string? Error);

        /// <summary>Read the canonical asset from the granted root, re-serialize it through the core so
        /// the browser always starts from canonical text, and capture the baseline hash for later
        /// stale-write detection. The hash covers the RAW on-disk bytes so any external edit is caught.</summary>
        public DocumentResult GetDocument()
        {
            if (!File.Exists(_assetPath))
                return new DocumentResult(false, null, null, null, _scratchRoot, $"Asset not found: {_assetPath}");
            var raw = File.ReadAllText(_assetPath);
            var load = _workspace.Load(raw);
            if (!load.Ok || load.Document == null)
                return new DocumentResult(false, null, null, null, _scratchRoot, load.Error ?? "Failed to load asset.");
            var canonical = _workspace.Serialize(load.Document);
            return new DocumentResult(true, canonical, load.Document.AssetId, HashText(raw), _scratchRoot, null);
        }

        // ── validate ────────────────────────────────────────────────────────────────────────────
        public sealed record DiagnosticDto(string Code, string Severity, string Path, string Detail);

        public sealed record ValidateResult(
            string Status, bool Ok, bool HasErrors, IReadOnlyList<DiagnosticDto> Diagnostics, string? CanonicalJson);

        /// <summary>Validate edited JSON against the on-disk canonical baseline so version-bump policy is
        /// enforced (a semantic edit with no explicit pin bump is rejected). Load failures are surfaced as
        /// a single SCHEMA-shaped diagnostic; the core owns every rule.</summary>
        public ValidateResult Validate(string editedJson)
        {
            var edited = _workspace.Load(editedJson);
            if (!edited.Ok || edited.Document == null)
                return new ValidateResult("load-error", false, true,
                    new[] { new DiagnosticDto("SCHEMA_REQUIRED", "Error", "/", edited.Error ?? "Malformed asset.") }, null);

            var baseline = LoadBaselineDocument();
            var report = _workspace.Validate(edited.Document, baseline);
            var canonical = _workspace.Serialize(edited.Document);
            var status = report.HasErrors ? "invalid" : "valid";
            return new ValidateResult(status, !report.HasErrors, report.HasErrors, Map(report.Diagnostics), canonical);
        }

        // ── generate preview ──────────────────────────────────────────────────────────────────────
        public sealed record GeneratedArtifactDto(string FileName, string Content);

        public sealed record GenerateResult(
            string Status, bool Ok, bool Blocked, IReadOnlyList<DiagnosticDto> Diagnostics,
            IReadOnlyList<GeneratedArtifactDto> Artifacts);

        /// <summary>Generate the four scratch C# artifacts as an in-memory PREVIEW. Validation (with the
        /// baseline for version policy) must pass first; any error blocks generation and returns the
        /// diagnostics, mirroring the CLI/core contract. No files are written by this endpoint.</summary>
        public GenerateResult GeneratePreview(string editedJson)
        {
            var edited = _workspace.Load(editedJson);
            if (!edited.Ok || edited.Document == null)
                return new GenerateResult("load-error", false, true,
                    new[] { new DiagnosticDto("SCHEMA_REQUIRED", "Error", "/", edited.Error ?? "Malformed asset.") },
                    Array.Empty<GeneratedArtifactDto>());

            var baseline = LoadBaselineDocument();
            var report = _workspace.Validate(edited.Document, baseline);
            if (report.HasErrors)
                return new GenerateResult("generation-blocked", false, true, Map(report.Diagnostics),
                    Array.Empty<GeneratedArtifactDto>());

            var gen = _workspace.Generate(edited.Document);
            var artifacts = gen.Artifacts.Select(a => new GeneratedArtifactDto(a.FileName, a.Content)).ToList();
            return new GenerateResult("generated", true, false, Array.Empty<DiagnosticDto>(), artifacts);
        }

        // ── export (atomic scratch write with stale-write detection) ─────────────────────────────
        public sealed record ExportResult(
            string Status, bool Ok, string? OutputDirectory, IReadOnlyList<string> Files,
            IReadOnlyList<DiagnosticDto> Diagnostics, string? Error);

        /// <summary>Atomically export the edited asset + freshly generated artifacts into the granted
        /// scratch root. Refuses on (a) a stale baseline (the on-disk asset changed since load),
        /// (b) any validation error, or (c) a generator failure — nothing is written in those cases.
        /// Each file is written to a temporary sibling then atomically renamed into place.</summary>
        public ExportResult Export(string editedJson, string baselineHash)
        {
            // (a) stale-write guard: compare the CURRENT on-disk asset hash to the session baseline.
            if (!File.Exists(_assetPath))
                return Fail("export-error", "Asset root disappeared; refusing to export.");
            var currentHash = HashText(File.ReadAllText(_assetPath));
            if (!string.Equals(currentHash, baselineHash, StringComparison.Ordinal))
                return Fail("stale-baseline",
                    "The asset changed on disk since it was loaded; reload before exporting to avoid clobbering an external edit.");

            // (b) validation must pass (baseline-aware, so version policy is enforced).
            var edited = _workspace.Load(editedJson);
            if (!edited.Ok || edited.Document == null)
                return new ExportResult("load-error", false, null, Array.Empty<string>(),
                    new[] { new DiagnosticDto("SCHEMA_REQUIRED", "Error", "/", edited.Error ?? "Malformed asset.") },
                    "Document did not load.");

            var baseline = LoadBaselineDocument();
            var report = _workspace.Validate(edited.Document, baseline);
            if (report.HasErrors)
                return new ExportResult("blocked", false, null, Array.Empty<string>(), Map(report.Diagnostics),
                    "Validation failed; export blocked.");

            // (c) generate, then atomically place the canonical asset + artifacts under the scratch root.
            var canonical = _workspace.Serialize(edited.Document);
            var gen = _workspace.Generate(edited.Document);

            Directory.CreateDirectory(_scratchRoot);
            var written = new List<string>();
            try
            {
                written.Add(AtomicWrite(Path.Combine(_scratchRoot, "homestead-stone.content.json"), canonical));
                foreach (var art in gen.Artifacts)
                    written.Add(AtomicWrite(Path.Combine(_scratchRoot, art.FileName), art.Content));
            }
            catch (Exception ex)
            {
                return Fail("export-error", "Atomic write failed: " + ex.Message);
            }

            return new ExportResult("exported", true, _scratchRoot,
                written.Select(Path.GetFileName).Where(f => f != null).Cast<string>().ToList(),
                Array.Empty<DiagnosticDto>(), null);
        }

        // ── helpers ─────────────────────────────────────────────────────────────────────────────
        private ExportResult Fail(string status, string error) =>
            new ExportResult(status, false, null, Array.Empty<string>(), Array.Empty<DiagnosticDto>(), error);

        private StoneContentDocument? LoadBaselineDocument()
        {
            if (!File.Exists(_assetPath)) return null;
            var load = _workspace.Load(File.ReadAllText(_assetPath));
            return load.Document;
        }

        // Write to a temp sibling in the SAME directory (so rename is atomic on the same volume),
        // flush to disk, then atomically move over the target. Returns the final path.
        private static string AtomicWrite(string finalPath, string content)
        {
            var dir = Path.GetDirectoryName(finalPath)!;
            Directory.CreateDirectory(dir);
            var tmp = Path.Combine(dir, "." + Path.GetFileName(finalPath) + ".tmp-" +
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                sw.Write(content);
                sw.Flush();
                fs.Flush(true);
            }
            File.Move(tmp, finalPath, overwrite: true);
            return finalPath;
        }

        private static IReadOnlyList<DiagnosticDto> Map(IReadOnlyList<ContentDiagnostic> diags) =>
            diags.Select(d => new DiagnosticDto(d.Code, d.Severity.ToString(), d.Path, d.Detail)).ToList();

        private static string HashText(string text)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
