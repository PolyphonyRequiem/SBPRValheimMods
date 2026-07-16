using System;
using System.Collections.Generic;
using System.Linq;

namespace StoneContent.Workbench.Core.Validation
{
    /// <summary>Severity of a diagnostic. Errors block generation; warnings do not.</summary>
    public enum DiagnosticSeverity
    {
        Error,
        Warning
    }

    /// <summary>Stable diagnostic codes. Every author-facing failure maps to exactly one of these,
    /// so tests and CI assert on a code, never on prose. Compiler/test failures keep their OWN codes
    /// (COMPILE_FAILED / TEST_FAILED) and are never collapsed into a schema/semantic code.</summary>
    public static class DiagnosticCodes
    {
        public const string SchemaRequired = "SCHEMA_REQUIRED";
        public const string SchemaType = "SCHEMA_TYPE";
        public const string SchemaEnum = "SCHEMA_ENUM";
        public const string DuplicateId = "DUPLICATE_ID";
        public const string UnknownTree = "UNKNOWN_TREE";
        public const string UnknownNodeReference = "UNKNOWN_NODE_REFERENCE";
        public const string RosterArithmetic = "ROSTER_ARITHMETIC";
        public const string InvalidLevelPartition = "INVALID_LEVEL_PARTITION";
        public const string UnavailableHasPrice = "UNAVAILABLE_HAS_PRICE";
        public const string LocalHasApPrice = "LOCAL_HAS_AP_PRICE";
        public const string PersonalMissingApPrice = "PERSONAL_MISSING_AP_PRICE";
        public const string ThresholdsNotAscending = "THRESHOLDS_NOT_ASCENDING";
        public const string FoundationalMemberExcluded = "FOUNDATIONAL_MEMBER_EXCLUDED";
        public const string VersionBumpRequired = "VERSION_BUMP_REQUIRED";
        public const string VersionRegression = "VERSION_REGRESSION";
        public const string GenerationBlocked = "GENERATION_BLOCKED";
        public const string GeneratedDrift = "GENERATED_DRIFT";
        public const string CompileFailed = "COMPILE_FAILED";
        public const string TestFailed = "TEST_FAILED";
    }

    /// <summary>One author-facing diagnostic. Stable code + severity + JSON-pointer-like path + a
    /// human-readable detail. The path locates the offending field (e.g. "/nodes/4/pricing/purchaseAp").</summary>
    public sealed record ContentDiagnostic(
        string Code,
        DiagnosticSeverity Severity,
        string Path,
        string Detail)
    {
        public override string ToString() =>
            $"{Severity.ToString().ToUpperInvariant()} {Code} {Path}: {Detail}";
    }

    /// <summary>The result of validating a document (optionally against a baseline). Pure data; the
    /// validator never prints or writes. Generation is blocked whenever any Error is present.</summary>
    public sealed class ValidationReport
    {
        private readonly List<ContentDiagnostic> _diagnostics;

        public ValidationReport(IEnumerable<ContentDiagnostic> diagnostics)
        {
            _diagnostics = new List<ContentDiagnostic>(diagnostics ?? Array.Empty<ContentDiagnostic>());
        }

        public IReadOnlyList<ContentDiagnostic> Diagnostics => _diagnostics;

        public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        public bool IsClean => _diagnostics.Count == 0;

        public bool HasCode(string code) => _diagnostics.Any(d => d.Code == code);

        public IEnumerable<ContentDiagnostic> WithCode(string code) =>
            _diagnostics.Where(d => d.Code == code);
    }
}
