using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;

namespace SBPR.Niflheim.HomesteadStones.Features.PilotIdentity
{
    // T022 — live-store account bootstrap GUARD (engine-free CLEAN-side core).
    //
    // This is the decision core the net48/net8 host `tools/niflheim-account-bootstrap` defers to when an
    // operator provisions exactly ONE real QA subject into the ISOLATED HomesteadT009L account store. It
    // does NOT fork or reimplement any admission/allowlist policy — provisioning is delegated verbatim to
    // the already-shipped IAP-009 `LocalAllowlistBootstrap` (which HMACs the subject inside
    // `PilotAccountService` and never persists/echoes it). This guard adds ONLY the extra fail-closed
    // boundary the live-store path needs on top of that core:
    //
    //   * Target confinement: the store/key targets must resolve UNDER the operator-configured isolated
    //     HomesteadT009L QA data root. A target that resolves outside the root, that matches a known
    //     PRODUCTION Niflheim/Heistan root, or that escapes the root via a symlink component is refused
    //     BEFORE any subject is read or any byte is written.
    //   * Quiescence: an external live-store write must NOT race the running server (which holds its own
    //     in-memory admission index). The host proves the server is stopped/quiesced; a non-quiescent
    //     server fails closed.
    //   * Store-health preflight: a subject-FREE inspection that proves target identity, key permissions,
    //     store health (torn-tail / quarantine), current notice/retention versions, and that a restart is
    //     required — WITHOUT accepting or revealing the subject.
    //   * Never truncate/reinitialize: this guard only ever APPENDS through the shipped bootstrap core; it
    //     never resets/compacts/reopens-truncating an existing store or key. A store carrying an ambiguous
    //     quarantine is escalated to the operator rather than silently written.
    //
    // The raw subject is consumed by value and never retained on this object; every result/report field is
    // subject-free and safe to print/log.
    //
    // net48 audit: System.* + the engine-free gate/service/store cores. No UnityEngine / Valheim / BepInEx.

    /// <summary>Where a resolved target path sits relative to the operator boundary. Only
    /// <see cref="UnderQaRoot"/> may proceed.</summary>
    public enum TargetConfinement
    {
        /// <summary>The resolved target is the isolated HomesteadT009L QA data root or a path under it.</summary>
        UnderQaRoot = 0,
        /// <summary>The resolved target is outside the configured QA data root.</summary>
        OutsideQaRoot,
        /// <summary>The resolved target matches a known PRODUCTION Niflheim/Heistan root (hard refusal).</summary>
        ProductionRootForbidden,
        /// <summary>A symlink component redirected the requested path to a location outside the QA root.</summary>
        SymlinkEscape
    }

    /// <summary>The host-resolved, host-stat'd view of the live store target. The host does the real
    /// realpath/lstat/stat I/O; this struct is the engine-free projection the guard reasons over. Paths are
    /// canonical (absolute, no `.`/`..`/trailing slash). No raw subject is ever present here.</summary>
    public readonly struct LiveStoreTarget
    {
        public LiveStoreTarget(
            string requestedLexicalStoreDir, string resolvedStoreDir,
            string journalPath, string keyPath,
            bool storeExists, bool keyExists, bool containedSymlink,
            PathOwnershipState keyOwnership)
        {
            RequestedLexicalStoreDir = requestedLexicalStoreDir ?? string.Empty;
            ResolvedStoreDir = resolvedStoreDir ?? string.Empty;
            JournalPath = journalPath ?? string.Empty;
            KeyPath = keyPath ?? string.Empty;
            StoreExists = storeExists;
            KeyExists = keyExists;
            ContainedSymlink = containedSymlink;
            KeyOwnership = keyOwnership;
        }

        /// <summary>The lexically-normalized requested store dir (before symlink resolution).</summary>
        public string RequestedLexicalStoreDir { get; }
        /// <summary>The realpath-resolved store dir (after symlink resolution).</summary>
        public string ResolvedStoreDir { get; }
        public string JournalPath { get; }
        public string KeyPath { get; }
        public bool StoreExists { get; }
        public bool KeyExists { get; }
        /// <summary>True when any component of the requested path was a symlink (host lstat).</summary>
        public bool ContainedSymlink { get; }
        public PathOwnershipState KeyOwnership { get; }
    }

    /// <summary>Operator-authored configuration for one isolated live-store target class. Immutable.</summary>
    public sealed class LiveStoreGuardConfig
    {
        public LiveStoreGuardConfig(
            string qaDataRootCanonical, IEnumerable<string> forbiddenProductionRoots,
            string providerNamespace, string backendIssuer,
            string noticeVersion, string retentionVersion)
        {
            QaDataRootCanonical = Canonicalize(qaDataRootCanonical);
            var forbidden = new List<string>();
            if (forbiddenProductionRoots != null)
                foreach (var r in forbiddenProductionRoots)
                {
                    var c = Canonicalize(r);
                    if (c.Length > 0) forbidden.Add(c);
                }
            ForbiddenProductionRoots = forbidden;
            ProviderNamespace = providerNamespace ?? string.Empty;
            BackendIssuer = backendIssuer ?? string.Empty;
            NoticeVersion = noticeVersion ?? string.Empty;
            RetentionVersion = retentionVersion ?? string.Empty;
        }

        public string QaDataRootCanonical { get; }
        public IReadOnlyList<string> ForbiddenProductionRoots { get; }
        public string ProviderNamespace { get; }
        public string BackendIssuer { get; }
        public string NoticeVersion { get; }
        public string RetentionVersion { get; }

        /// <summary>Lexical canonicalization used for containment comparison: trim trailing '/', collapse
        /// nothing else (the host already passes absolute realpath'd values). Empty stays empty.</summary>
        internal static string Canonicalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string p = path.Trim();
            while (p.Length > 1 && p.EndsWith("/", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 1);
            return p;
        }
    }

    /// <summary>Subject-free preflight report. Proves target identity, permissions, store health, current
    /// notice/retention versions, and restart requirement WITHOUT accepting or revealing the subject.</summary>
    public sealed class LivePreflightReport
    {
        public TargetConfinement Confinement { get; internal set; }
        public bool KeyOwnerOnly { get; internal set; }
        public bool StoreExists { get; internal set; }
        public int AccountCount { get; internal set; }
        public int ActiveAllowlistCount { get; internal set; }
        public long QuarantinedTailBytes { get; internal set; }
        public int QuarantinedIntentTransactions { get; internal set; }
        public string NoticeVersion { get; internal set; } = string.Empty;
        public string RetentionVersion { get; internal set; } = string.Empty;
        public bool ServerQuiescent { get; internal set; }
        /// <summary>An external live-store append is invisible to a running server's in-memory admission
        /// index, so a restart is always required for the new entry to admit. Always true.</summary>
        public bool RestartRequired { get; internal set; }
        /// <summary>Stable, subject-free reason a provision would be refused right now (empty when ready).</summary>
        public string BlockingResultCode { get; internal set; } = string.Empty;
        public bool Ready => BlockingResultCode.Length == 0;

        public string ToOutputLine() =>
            "confinement=" + Confinement +
            " keyOwnerOnly=" + KeyOwnerOnly +
            " storeExists=" + StoreExists +
            " accounts=" + AccountCount +
            " activeAllowlist=" + ActiveAllowlistCount +
            " quarantinedTailBytes=" + QuarantinedTailBytes +
            " quarantinedIntentTxns=" + QuarantinedIntentTransactions +
            " noticeVersion=" + NoticeVersion +
            " retentionVersion=" + RetentionVersion +
            " serverQuiescent=" + ServerQuiescent +
            " restartRequired=" + RestartRequired +
            " ready=" + Ready +
            (Ready ? string.Empty : " blockingResultCode=" + BlockingResultCode);
    }

    /// <summary>Bounded result of a live-store provision attempt. Subject-free and safe to print/log.</summary>
    public sealed class LiveProvisionOutcome
    {
        public bool Accepted { get; }
        public string ResultCode { get; }
        public string AllowlistEntryId { get; }

        private LiveProvisionOutcome(bool accepted, string resultCode, string allowlistEntryId)
        {
            Accepted = accepted;
            ResultCode = resultCode ?? string.Empty;
            AllowlistEntryId = allowlistEntryId ?? string.Empty;
        }

        internal static LiveProvisionOutcome Ok(string resultCode, string entryId) =>
            new LiveProvisionOutcome(true, resultCode, entryId);
        internal static LiveProvisionOutcome Reject(string resultCode) =>
            new LiveProvisionOutcome(false, resultCode, string.Empty);

        public string ToOutputLine() =>
            "resultCode=" + ResultCode +
            (string.IsNullOrEmpty(AllowlistEntryId) ? string.Empty : " allowlistEntryId=" + AllowlistEntryId);
    }

    public sealed class LiveStoreProvisioningGuard
    {
        private readonly LiveStoreGuardConfig _config;

        public LiveStoreProvisioningGuard(LiveStoreGuardConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>Classify where a resolved target sits relative to the operator boundary. Production-root
        /// refusal takes precedence over a generic outside-root result. A symlink component that redirects
        /// the requested path to a location outside the QA root is reported as a distinct escape.</summary>
        public TargetConfinement EvaluateTarget(LiveStoreTarget target)
        {
            string resolved = LiveStoreGuardConfig.Canonicalize(target.ResolvedStoreDir);
            string requested = LiveStoreGuardConfig.Canonicalize(target.RequestedLexicalStoreDir);

            // Hard-refuse a known production root regardless of anything else.
            foreach (var prod in _config.ForbiddenProductionRoots)
                if (IsWithin(resolved, prod) || IsWithin(requested, prod))
                    return TargetConfinement.ProductionRootForbidden;

            bool resolvedInside = IsWithin(resolved, _config.QaDataRootCanonical);

            // A symlink component that changes the resolved location AND lands outside the QA root is an
            // escape (more specific than a plain outside-root request).
            if (target.ContainedSymlink && !string.Equals(resolved, requested, StringComparison.Ordinal) && !resolvedInside)
                return TargetConfinement.SymlinkEscape;

            if (!resolvedInside) return TargetConfinement.OutsideQaRoot;

            // The resolved path is inside, but the literal requested path escaped via a symlink to get
            // there under a forbidden/foreign lexical name — treat a symlink that crosses OUT of the QA
            // root at the lexical layer as an escape too.
            if (target.ContainedSymlink && requested.Length > 0 && !IsWithin(requested, _config.QaDataRootCanonical))
                return TargetConfinement.SymlinkEscape;

            return TargetConfinement.UnderQaRoot;
        }

        /// <summary>Subject-free preflight. Proves target identity, permissions, store health, current
        /// notice/retention versions, and restart requirement. Never accepts or reveals the subject.</summary>
        public LivePreflightReport Preflight(LiveStoreTarget target, bool serverQuiescent, PilotAccountStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));

            var report = new LivePreflightReport
            {
                Confinement = EvaluateTarget(target),
                KeyOwnerOnly = target.KeyOwnership.IsOwnerOnly,
                StoreExists = target.StoreExists,
                AccountCount = store.AccountCount,
                ActiveAllowlistCount = CountActiveAllowlist(store),
                QuarantinedTailBytes = store.QuarantinedTailBytes,
                QuarantinedIntentTransactions = store.QuarantinedIntentTransactions,
                NoticeVersion = _config.NoticeVersion,
                RetentionVersion = _config.RetentionVersion,
                ServerQuiescent = serverQuiescent,
                RestartRequired = true,
            };
            report.BlockingResultCode = FirstBlockingCode(report.Confinement, serverQuiescent,
                target.KeyOwnership, store);
            return report;
        }

        /// <summary>Provision exactly one QA subject into the confined, quiesced, healthy live store by
        /// DELEGATING to the shipped <see cref="LocalAllowlistBootstrap"/> (no policy fork). Every boundary
        /// check fails closed BEFORE the subject reaches the HMAC. Never truncates/reinitializes the store.</summary>
        public LiveProvisionOutcome Provision(
            LiveStoreTarget target, bool serverQuiescent, PilotAccountStore store,
            LocalAllowlistBootstrap bootstrap, ProvisioningInputChannel channel,
            string operationId, string rawSubject,
            PilotDisclosure disclosure, DisclosureAcknowledgement acknowledgement, long occurredAt)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (bootstrap == null) throw new ArgumentNullException(nameof(bootstrap));

            // 1. Target confinement — refuse before any subject is touched.
            var confinement = EvaluateTarget(target);
            if (confinement != TargetConfinement.UnderQaRoot)
                return LiveProvisionOutcome.Reject(confinement.ToString());

            // 2. Quiescence — an external write must not race the running server.
            if (!serverQuiescent)
                return LiveProvisionOutcome.Reject("ServerNotQuiescent");

            // 3. Store health — an ambiguous durable quarantine is escalated, never silently appended to.
            if (store.QuarantinedIntentTransactions > 0)
                return LiveProvisionOutcome.Reject("StoreQuarantinedNeedsReview");

            // 4. Delegate to the shipped allowlist bootstrap core (owner-only key path, channel discipline,
            //    disclosure/acknowledgement gate, HMAC + discard of the raw subject — all reused verbatim).
            var result = bootstrap.Provision(channel, target.KeyOwnership, operationId,
                _config.ProviderNamespace, _config.BackendIssuer, rawSubject,
                disclosure, acknowledgement, occurredAt);

            return result.Accepted
                ? LiveProvisionOutcome.Ok(result.ResultCode, result.AllowlistEntryId)
                : LiveProvisionOutcome.Reject(result.ResultCode);
        }

        // ---- helpers (pure) ----

        private string FirstBlockingCode(TargetConfinement confinement, bool serverQuiescent,
            PathOwnershipState keyOwnership, PilotAccountStore store)
        {
            if (confinement != TargetConfinement.UnderQaRoot) return confinement.ToString();
            if (!serverQuiescent) return "ServerNotQuiescent";
            if (store.QuarantinedIntentTransactions > 0) return "StoreQuarantinedNeedsReview";
            if (!keyOwnership.IsOwnerOnly) return "KeyPathTooPermissive";
            return string.Empty;
        }

        private static int CountActiveAllowlist(PilotAccountStore store)
        {
            int n = 0;
            foreach (var e in store.AllowlistEntries)
                if (e.Status == AllowlistStatus.Active) n++;
            return n;
        }

        /// <summary>Lexical containment: <paramref name="candidate"/> is <paramref name="root"/> or a path
        /// strictly under it (segment-boundary aware, so `/a/bc` is NOT within `/a/b`).</summary>
        private static bool IsWithin(string candidate, string root)
        {
            if (candidate.Length == 0 || root.Length == 0) return false;
            if (string.Equals(candidate, root, StringComparison.Ordinal)) return true;
            return candidate.StartsWith(root + "/", StringComparison.Ordinal);
        }
    }
}
