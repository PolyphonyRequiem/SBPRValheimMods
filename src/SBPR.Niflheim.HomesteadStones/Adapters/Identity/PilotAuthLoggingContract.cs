using System;
using System.Collections.Generic;
using System.Globalization;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Identity
{
    // IAP-001 Gate 0 — the Niflheim auth-subsystem logging contract + upstream raw-subject inventory
    // (engine-free CLEAN-side core).
    //
    // Two obligations:
    //   1. Niflheim subsystem logs stay clean: no raw provider/profile subject, HMAC, token, secret,
    //      claim, email, Discord id, IP, or display-name-as-identity ever appears in a line this
    //      subsystem emits (AIP-FR-006; contracts "Logging contract"). PilotAuthLog builds ONLY
    //      allowed fields and mechanically scrubs a candidate line.
    //   2. Upstream base-runtime logging/world facts are INVENTORIED, disclosed, and given a bounded
    //      access + scheduled-purge path — Gate 0 does not falsely claim it can suppress base-game
    //      persistence, it enumerates it and proves each entry has a purge boundary or enrollment fails
    //      closed (AIP-FR-006, spec decision #10; AT-AIP-UPSTREAM-WORLD-FACT-INVENTORY lives in a later
    //      tracer but the inventory MODEL is proven here so AT-AIP-PROVIDER-LOG-SCRUB has real coverage).
    //
    // net48 audit: System.String / Generic / Globalization only. No UnityEngine / Valheim / BepInEx.

    /// <summary>The closed allowlist of fields a Niflheim auth line may carry (contracts "Logging
    /// contract" → allowed ordinary fields). A provider SUBJECT is never among them — only the provider
    /// CLASS. Internal AccountId/CharacterId are allowed only AFTER successful resolution.</summary>
    public enum AuthLogField
    {
        Timestamp = 0,
        ResultCode,
        ProviderClass,          // configured namespace, NOT a subject
        AccountIdAfterResolve,  // internal id, post-resolution only
        CharacterIdAfterResolve,
        OperationCorrelationId,
        ServerBuildVersion
    }

    /// <summary>A single Niflheim auth log line built exclusively from allowed fields. There is no API to
    /// attach a raw subject/HMAC/token — the type simply cannot express one. Emit() returns the composed,
    /// already-clean line; ScrubForbidden() is a belt-and-suspenders final pass.</summary>
    public sealed class PilotAuthLogLine
    {
        private readonly List<KeyValuePair<AuthLogField, string>> _fields =
            new List<KeyValuePair<AuthLogField, string>>();

        public PilotAuthLogLine With(AuthLogField field, string value)
        {
            _fields.Add(new KeyValuePair<AuthLogField, string>(field, value ?? string.Empty));
            return this;
        }

        public string Emit()
        {
            var parts = new List<string>(_fields.Count);
            foreach (var f in _fields)
                parts.Add(f.Key.ToString() + "=" + (f.Value ?? string.Empty));
            return string.Join(" ", parts.ToArray());
        }
    }

    /// <summary>Kind of upstream, base-runtime artifact that MAY carry a raw provider/profile subject
    /// outside Niflheim's control, so it must be inventoried rather than pretended away.</summary>
    public enum UpstreamArtifactKind
    {
        ValheimServerLog = 0,   // BepInEx/Valheim console log potentially printing a connecting Steam id
        VanillaWorldSaveCreatorFact,  // s_creator / s_playerID persisted in the .db world save
        BepInExDiagnosticLog
    }

    /// <summary>One inventoried upstream artifact and its disclosed treatment. To pass Gate 0 every entry
    /// must be BOTH access-restricted AND have a bounded purge path; otherwise pilot enrollment fails
    /// closed (AIP-FR-006 "If an upstream artifact cannot meet that boundary, pilot enrollment SHALL fail
    /// closed").</summary>
    public readonly struct UpstreamSubjectArtifact
    {
        public UpstreamSubjectArtifact(UpstreamArtifactKind kind, string locationHint,
            bool accessRestricted, bool hasBoundedPurgePath, int retentionDays)
        {
            Kind = kind;
            LocationHint = locationHint ?? string.Empty;
            AccessRestricted = accessRestricted;
            HasBoundedPurgePath = hasBoundedPurgePath;
            RetentionDays = retentionDays;
        }

        public UpstreamArtifactKind Kind { get; }
        public string LocationHint { get; }
        public bool AccessRestricted { get; }
        public bool HasBoundedPurgePath { get; }
        public int RetentionDays { get; }

        /// <summary>An artifact clears the Gate-0 boundary iff it is access-restricted, has a bounded
        /// purge path, and a positive bounded retention (zero/unbounded is invalid — AIP-FR-023/024).</summary>
        public bool ClearsBoundary => AccessRestricted && HasBoundedPurgePath && RetentionDays > 0;
    }

    /// <summary>The Gate-0 upstream inventory. Enrollment may open only when EVERY inventoried artifact
    /// clears the boundary; a single non-clearing artifact fails closed.</summary>
    public sealed class UpstreamSubjectInventory
    {
        private readonly List<UpstreamSubjectArtifact> _artifacts = new List<UpstreamSubjectArtifact>();

        public UpstreamSubjectInventory Add(UpstreamSubjectArtifact artifact)
        {
            _artifacts.Add(artifact);
            return this;
        }

        public IReadOnlyList<UpstreamSubjectArtifact> Artifacts => _artifacts;

        /// <summary>True only when every inventoried artifact is access-restricted, purge-bounded, and
        /// positively retained. Empty inventory is NOT a pass — Gate 0 must enumerate the known upstream
        /// facts, so an empty inventory means "not yet inventoried" and fails closed.</summary>
        public bool EnrollmentMayOpen()
        {
            if (_artifacts.Count == 0) return false;
            foreach (var a in _artifacts)
                if (!a.ClearsBoundary) return false;
            return true;
        }

        /// <summary>The subset that does not clear the boundary (drives the fail-closed reason).</summary>
        public IReadOnlyList<UpstreamSubjectArtifact> NonClearing()
        {
            var bad = new List<UpstreamSubjectArtifact>();
            foreach (var a in _artifacts)
                if (!a.ClearsBoundary) bad.Add(a);
            return bad;
        }
    }

    /// <summary>Mechanical scrub used by AT-AIP-PROVIDER-LOG-SCRUB / AT-AIP-PRINCIPAL-SCRUB style checks:
    /// given a set of forbidden raw values (subjects, HMACs, tokens) seeded into a negative fixture, prove
    /// no emitted Niflheim line contains any of them.</summary>
    public static class PilotAuthLogScrubber
    {
        /// <summary>True when <paramref name="line"/> contains any forbidden raw value. A clean line
        /// returns false. Case-sensitive ordinal (identifiers are exact byte sequences).</summary>
        public static bool ContainsForbidden(string line, IEnumerable<string> forbiddenRawValues)
        {
            if (string.IsNullOrEmpty(line) || forbiddenRawValues == null) return false;
            foreach (var v in forbiddenRawValues)
            {
                if (!string.IsNullOrEmpty(v) &&
                    line.IndexOf(v, StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>Scan a batch of emitted lines; returns the first offending (lineIndex, value) or
        /// (-1, null) when the whole batch is clean.</summary>
        public static bool TryFindForbidden(IReadOnlyList<string> lines, IReadOnlyList<string> forbiddenRawValues,
            out int lineIndex, out string offendingValue)
        {
            lineIndex = -1;
            offendingValue = string.Empty;
            if (lines == null || forbiddenRawValues == null) return false;
            for (int i = 0; i < lines.Count; i++)
            {
                foreach (var v in forbiddenRawValues)
                {
                    if (!string.IsNullOrEmpty(v) && lines[i] != null &&
                        lines[i].IndexOf(v, StringComparison.Ordinal) >= 0)
                    {
                        lineIndex = i;
                        offendingValue = v;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Compose the one admissible outcome line for a resolution/rejection: timestamp,
        /// provider CLASS, result code, and correlation id — never a subject. Convenience for the adapter
        /// so it does not hand-roll a line and accidentally include a subject.</summary>
        public static string OutcomeLine(long unixSeconds, string providerClass, string resultCode, string correlationId) =>
            new PilotAuthLogLine()
                .With(AuthLogField.Timestamp, unixSeconds.ToString(CultureInfo.InvariantCulture))
                .With(AuthLogField.ProviderClass, providerClass ?? string.Empty)
                .With(AuthLogField.ResultCode, resultCode ?? string.Empty)
                .With(AuthLogField.OperationCorrelationId, correlationId ?? string.Empty)
                .Emit();
    }
}
