using System;
using System.Collections.Generic;
using StoneContent.Workbench.Core.Generation;
using StoneContent.Workbench.Core.Model;
using StoneContent.Workbench.Core.Serialization;
using StoneContent.Workbench.Core.Validation;

namespace StoneContent.Workbench.Core
{
    /// <summary>The single caller seam for the workbench core (deep module). It loads, validates,
    /// generates, and checks — all as PURE functions returning result records. It NEVER prints, writes
    /// files, reads the filesystem, or mutates repository state; CLI and web adapters own all I/O and
    /// pass file contents in as strings. Internals (serializer, validator, generator) are not exposed.</summary>
    public sealed class StoneContentWorkspace
    {
        private readonly StoneContentValidator _validator = new();
        private readonly CSharpCatalogGenerator _generator = new();

        public sealed record LoadResult(bool Ok, StoneContentDocument? Document, string? Error);

        /// <summary>Parse + strict-load an asset from JSON text. Returns a failed LoadResult (never
        /// throws) on malformed/invalid-shape JSON so adapters can surface it as a diagnostic.</summary>
        public LoadResult Load(string json)
        {
            try
            {
                return new LoadResult(true, CanonicalJson.Load(json), null);
            }
            catch (CanonicalJson.JsonLoadException ex)
            {
                return new LoadResult(false, null, ex.Message);
            }
        }

        /// <summary>Serialize a document back to canonical JSON text (deterministic).</summary>
        public string Serialize(StoneContentDocument document) => CanonicalJson.Serialize(document);

        /// <summary>Semantic + version validation. Pass a baseline to enable version-bump checks.</summary>
        public ValidationReport Validate(StoneContentDocument document, StoneContentDocument? baseline = null)
            => _validator.Validate(document, baseline);

        /// <summary>Deterministic C# generation of the four scratch data artifacts.</summary>
        public GenerationResult Generate(StoneContentDocument document) => _generator.Generate(document);

        /// <summary>The result of a check: does the on-disk generated set match a fresh generation?
        /// Reported as data (drift diagnostics); the adapter reads the directory and passes its files
        /// in, because the core does no I/O.</summary>
        public sealed record CheckResult(bool Ok, IReadOnlyList<ContentDiagnostic> Diagnostics);

        /// <summary>Compare a fresh generation of <paramref name="document"/> against the supplied
        /// already-generated files (name → content). Emits GENERATED_DRIFT for any missing, extra, or
        /// differing artifact. Purely functional: the caller reads the directory.</summary>
        public CheckResult Check(StoneContentDocument document, IReadOnlyDictionary<string, string> generatedFiles)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (generatedFiles == null) throw new ArgumentNullException(nameof(generatedFiles));

            var diags = new List<ContentDiagnostic>();

            // Validation must pass first; drift-checking a broken doc is meaningless.
            var report = _validator.Validate(document);
            if (report.HasErrors)
            {
                diags.Add(new ContentDiagnostic(DiagnosticCodes.GenerationBlocked, DiagnosticSeverity.Error,
                    "/", "Document has validation errors; generation is blocked and drift cannot be checked."));
                diags.AddRange(report.Diagnostics);
                return new CheckResult(false, diags);
            }

            var fresh = _generator.Generate(document);
            var expectedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var art in fresh.Artifacts)
            {
                expectedNames.Add(art.FileName);
                if (!generatedFiles.TryGetValue(art.FileName, out var onDisk))
                {
                    diags.Add(new ContentDiagnostic(DiagnosticCodes.GeneratedDrift, DiagnosticSeverity.Error,
                        "/" + art.FileName, $"Generated artifact '{art.FileName}' is missing on disk."));
                    continue;
                }
                if (Normalize(onDisk) != Normalize(art.Content))
                    diags.Add(new ContentDiagnostic(DiagnosticCodes.GeneratedDrift, DiagnosticSeverity.Error,
                        "/" + art.FileName, $"Generated artifact '{art.FileName}' differs from a fresh generation."));
            }
            foreach (var name in generatedFiles.Keys)
                if (!expectedNames.Contains(name))
                    diags.Add(new ContentDiagnostic(DiagnosticCodes.GeneratedDrift, DiagnosticSeverity.Error,
                        "/" + name, $"Unexpected extra generated artifact '{name}' on disk."));

            bool ok = diags.Count == 0;
            return new CheckResult(ok, diags);
        }

        // Compare on LF-normalized text so a checkout with CRLF does not read as drift.
        private static string Normalize(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");
    }
}
