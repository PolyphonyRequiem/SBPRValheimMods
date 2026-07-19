using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;

namespace SBPR.Niflheim.HomesteadStones.Application.Accounts
{
    // IAP-013 Tracer 5 — the destructive privacy lifecycle (engine-free CLEAN core). This completes the
    // account-identity pilot's destruction surface over the IAP-012 privacy foundation + IAP-009 operator
    // control:
    //   * CompleteAccountDeletion — from DeletionPending, purge account-scoped artifacts, physically
    //     compact the account's records out of the journal (proving absence, not a tombstone), then commit
    //     Deleted (data-model.md "Delete/purge account"; AT-AIP-DELETE-PURGE).
    //   * RunPilotRetentionPurge — process every DUE, unheld catalog artifact (logs/exports/backups/
    //     journals/world-fixtures) to Purged with an artifact-specific evidence digest, returning
    //     per-category counts + evidence ids, NOT player/provider identifiers (AT-AIP-RETENTION-PURGE,
    //     AT-AIP-BACKUP-PURGE).
    //   * ResetScoped — explicit, named-internal-scope reset that never chooses state by file recency
    //     (AT-AIP-RESET-EXPLICIT).
    //   * FullPilotReset — whole-fixture reset fallback: destroy the old catalog/journal, emit a
    //     selector-free PilotPurgeCertificate, retire the old lookup-key epoch, and open a fresh active
    //     epoch so old bindings can never resolve again (AT-AIP-PURGE-FALLBACK-RESET,
    //     AT-AIP-FULL-RESET-ROTATES-KEY).
    //   * Quarantine — mark an account Quarantined for durable ambiguity; it admits nothing and cannot be
    //     silently repaired (AT-AIP-QUARANTINE). Deleted/Purged never travel back to Active
    //     (AT-AIP-NO-TIME-TRAVEL).
    //
    // Every durable mutation authorizes through OperatorAdminGate, replays idempotently on operationId
    // (conflict-detecting), drains the global privacy mutation fence, and commits atomically.
    //
    // net48 audit: System.* + LINQ only. No UnityEngine/Valheim/BepInEx.

    /// <summary>Per-category retention-purge report (contracts §RunPilotRetentionPurge: "Returns
    /// counts/evidence IDs by category, not player/provider identifiers"). Carries NO account/character/
    /// provider/profile selector.</summary>
    public sealed class RetentionPurgeReport
    {
        private readonly Dictionary<PilotArtifactType, int> _purgedByType = new Dictionary<PilotArtifactType, int>();
        public readonly List<string> EvidenceReceiptIds = new List<string>();
        public readonly List<string> SkippedHeldSelectors = new List<string>();

        internal void RecordPurged(PilotArtifactType type, string receiptId)
        {
            _purgedByType[type] = (_purgedByType.TryGetValue(type, out int n) ? n : 0) + 1;
            if (!string.IsNullOrEmpty(receiptId)) EvidenceReceiptIds.Add(receiptId);
        }

        public int PurgedCount(PilotArtifactType type) => _purgedByType.TryGetValue(type, out int n) ? n : 0;
        public int TotalPurged => _purgedByType.Values.Sum();
    }

    public sealed class PilotDestructionService
    {
        private readonly PilotAccountStore _store;
        private readonly OperatorAdminGate _adminGate;
        private readonly AccountMutationFence _fence;
        private readonly PilotPrivacyService _privacy;
        private readonly TimeSpan _drainTimeout;

        public PilotDestructionService(PilotAccountStore store, OperatorAdminGate adminGate,
            AccountMutationFence fence, PilotPrivacyService privacy, TimeSpan drainTimeout)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _adminGate = adminGate ?? throw new ArgumentNullException(nameof(adminGate));
            _fence = fence ?? throw new ArgumentNullException(nameof(fence));
            _privacy = privacy ?? throw new ArgumentNullException(nameof(privacy));
            _drainTimeout = drainTimeout;
        }

        // ---- shared guards ----

        private void RequireOperator(ServerObservedAdminContext op)
        {
            if (!_adminGate.Authorize(op, out var reject))
                throw new PrivacyOperationException(PrivacyRejectionCode.Unauthorized,
                    "Operator authority required for destructive privacy mutation: " + reject);
        }

        private static void RequirePositive(long ts, string what)
        {
            if (ts <= 0)
                throw new PrivacyOperationException(PrivacyRejectionCode.TimestampInvalid,
                    what + " must be a positive unix-second timestamp.");
        }

        private AccountMutationFence.FenceLease AcquireGlobalFence()
        {
            if (!_fence.TryAcquireForLifecycle(PilotPrivacyService.GlobalFenceScope, _drainTimeout, out var lease))
                throw new PrivacyOperationException(PrivacyRejectionCode.DrainTimeout,
                    "Could not drain the global mutation fence; destructive mutation aborted (recoverable).");
            return lease;
        }

        private static PrivacyOperationException Conflict(string operationId) =>
            new PrivacyOperationException(PrivacyRejectionCode.OperationConflict,
                "operationId '" + operationId + "' already committed a different mutation.");

        private static string L(long v) => v.ToString(CultureInfo.InvariantCulture);

        // ---- CompleteAccountDeletion (AT-AIP-DELETE-PURGE, AT-AIP-DELETE-REVOKES-ALLOWLIST) ----

        /// <summary>Finish a deletion that <see cref="OperatorAccountService.Delete"/> already moved to
        /// DeletionPending: at/after purgeEligibleAt, purge the account's cataloged artifacts (exports,
        /// backups) with evidence, physically compact the account's records out of the journal (proving
        /// absence — a tombstone is NOT purge), then commit the account Deleted. The revoked credential +
        /// allowlist from the Delete step already block recreation. Operator-gated, idempotent, fenced.</summary>
        public AccountDeletionResult CompleteAccountDeletion(ServerObservedAdminContext op, string operationId,
            PilotAccountId accountId, string evidenceDigest, long occurredAt)
        {
            RequireOperator(op);
            RequirePositive(occurredAt, "occurredAt");
            if (string.IsNullOrEmpty(evidenceDigest))
                throw new PrivacyOperationException(PrivacyRejectionCode.LocatorInvalid,
                    "Deletion purge requires an artifact-specific evidence digest; counts alone do not prove purge.");

            if (_store.TryGetCommittedOp(operationId, out _, out _, out var recResult))
            {
                if (recResult.StartsWith("acct-purge:", StringComparison.Ordinal))
                    return AccountDeletionResult.Replayed(accountId);
                throw Conflict(operationId);
            }
            if (!_store.TryGetAccount(accountId, out var acct))
                throw new PrivacyOperationException(PrivacyRejectionCode.NotFound, "Account not found: " + accountId.Value);
            if (acct.Status == PilotAccountStatus.Deleted)
                return AccountDeletionResult.Replayed(accountId);
            if (acct.Status != PilotAccountStatus.DeletionPending)
                throw new PrivacyOperationException(PrivacyRejectionCode.PilotClosed,
                    "CompleteAccountDeletion requires the account to be DeletionPending; current status " + acct.Status + ".");

            using (AcquireGlobalFence())
            {
                // 1) Purge every account-scoped, non-purged artifact (export/backup) with the evidence digest.
                var purgedArtifacts = new List<string>();
                foreach (var art in _store.Artifacts.Where(a =>
                             string.Equals(a.AccountId, accountId.Value, StringComparison.Ordinal) &&
                             a.Status != ArtifactStatus.Purged).ToList())
                {
                    var receiptId = OpaqueIdMint.NewReceiptId();
                    var change = new JournalChange("artifact-status")
                        .Set("dataArtifactId", art.DataArtifactId.Value)
                        .Set("status", ArtifactStatus.Purged.ToString())
                        .Set("revision", L(art.Revision + 1))
                        .Set("purgeEvidenceDigest", evidenceDigest)
                        .Set("selector", "artifact:" + art.ArtifactType + ":" + art.DataArtifactId.Value)
                        .Set("receiptId", receiptId.Value);
                    _store.Commit(operationId + "#art-" + art.DataArtifactId.Value,
                        "acct-art-purge-" + art.DataArtifactId.Value,
                        PilotAccountStore.Digest("acct-art|" + art.DataArtifactId.Value),
                        PilotAccountStore.Digest(operationId), "art-purge:" + art.DataArtifactId.Value, occurredAt,
                        new[] { change });
                    purgedArtifacts.Add(art.DataArtifactId.Value);
                }

                // 2) Physically compact the account's records out of the journal (absence proof).
                var evidence = _store.CompactRemovingAccounts(new[] { accountId.Value });

                // 3) Commit the terminal Deleted marker + a purge receipt over a FRESH (post-compaction)
                //    account record so the tombstone is minimal and the compaction is the actual purge.
                var deleteReceipt = OpaqueIdMint.NewReceiptId();
                var deleted = new JournalChange("acct")
                    .Set("accountId", accountId.Value)
                    .Set("status", PilotAccountStatus.Deleted.ToString())
                    .Set("revision", "1")
                    .Set("retentionPolicyVersion", acct.RetentionPolicyVersion);
                _store.Commit(operationId, "acct-purge-" + accountId.Value,
                    PilotAccountStore.Digest("acct-purge|" + accountId.Value + "|" + evidenceDigest),
                    PilotAccountStore.Digest(operationId), "acct-purge:" + accountId.Value, occurredAt,
                    new[] { deleted });

                return new AccountDeletionResult(accountId, false,
                    evidence.RemovedCredentialIds, evidence.RemovedCharacterIds,
                    purgedArtifacts, deleteReceipt.Value);
            }
        }

        // ---- RunPilotRetentionPurge (AT-AIP-RETENTION-PURGE, AT-AIP-BACKUP-PURGE) ----

        /// <summary>Process every DUE (now past expiry), unheld, non-purged catalog artifact to Purged with
        /// an artifact-specific evidence digest derived per-artifact, skipping any whose scope is under an
        /// active retention hold. Returns per-category counts + evidence receipt ids — never a player/
        /// provider selector. Operator-gated, fenced. Idempotent by construction (already-purged artifacts
        /// are skipped).</summary>
        public RetentionPurgeReport RunPilotRetentionPurge(ServerObservedAdminContext op, string operationId,
            long now, Func<PilotDataArtifactProjection, string> evidenceFor)
        {
            RequireOperator(op);
            RequirePositive(now, "now");
            if (evidenceFor == null) throw new ArgumentNullException(nameof(evidenceFor));

            var report = new RetentionPurgeReport();
            using (AcquireGlobalFence())
            {
                foreach (var art in _store.Artifacts.Where(a => a.Status != ArtifactStatus.Purged).ToList())
                {
                    // Shared sentinel (see PilotPrivacyService): ExpiresAt <= 0 means never-expires/valid,
                    // NOT immediately due. Durable proof-class artifacts (e.g. ResetScoped's ResetAudit) are
                    // created with expiresAt=0 and retention purge must NEVER sweep them; only an artifact
                    // with a positive deadline that has arrived is due.
                    if (art.ExpiresAt <= 0 || now < art.ExpiresAt) continue;            // never-expiring or not yet due
                    string selector = "artifact:" + art.ArtifactType + ":" + art.DataArtifactId.Value;
                    if (_privacy.IsScopeHeld(selector, now)) { report.SkippedHeldSelectors.Add(selector); continue; }

                    string evidence = evidenceFor(art);
                    if (string.IsNullOrEmpty(evidence))
                        throw new PrivacyOperationException(PrivacyRejectionCode.LocatorInvalid,
                            "Retention purge requires an artifact-specific evidence digest; counts alone do not prove purge.");

                    var receiptId = OpaqueIdMint.NewReceiptId();
                    var change = new JournalChange("artifact-status")
                        .Set("dataArtifactId", art.DataArtifactId.Value)
                        .Set("status", ArtifactStatus.Purged.ToString())
                        .Set("revision", L(art.Revision + 1))
                        .Set("purgeEvidenceDigest", evidence)
                        .Set("selector", selector)
                        .Set("receiptId", receiptId.Value);
                    _store.Commit(operationId + "#" + art.DataArtifactId.Value,
                        "ret-purge-" + art.DataArtifactId.Value,
                        PilotAccountStore.Digest("ret|" + art.DataArtifactId.Value),
                        PilotAccountStore.Digest(operationId), "ret-purge:" + art.DataArtifactId.Value, now,
                        new[] { change });
                    report.RecordPurged(art.ArtifactType, receiptId.Value);
                }
            }
            return report;
        }

        // ---- ResetScoped (AT-AIP-RESET-EXPLICIT) ----

        /// <summary>Explicit, receipted reset of NAMED internal accounts. It never chooses a source by
        /// newest timestamp and never invents ownership: only the accounts the operator explicitly names
        /// are compacted out. Requires a reason. Operator-gated, idempotent, fenced.</summary>
        public ResetResult ResetScoped(ServerObservedAdminContext op, string operationId,
            IReadOnlyList<PilotAccountId> namedAccounts, string reason, long occurredAt)
        {
            RequireOperator(op);
            RequirePositive(occurredAt, "occurredAt");
            if (namedAccounts == null || namedAccounts.Count == 0)
                throw new PrivacyOperationException(PrivacyRejectionCode.NotFound,
                    "Explicit reset requires at least one named internal account; it never infers scope.");
            if (string.IsNullOrEmpty(reason))
                throw new PrivacyOperationException(PrivacyRejectionCode.RetentionHoldInvalid,
                    "Explicit reset requires a reason.");

            if (_store.TryGetCommittedOp(operationId, out _, out _, out var recResult))
            {
                if (recResult.StartsWith("reset-scoped:", StringComparison.Ordinal))
                    return ResetResult.Replayed();
                throw Conflict(operationId);
            }

            using (AcquireGlobalFence())
            {
                var ids = namedAccounts.Select(a => a.Value).ToList();
                var evidence = _store.CompactRemovingAccounts(ids);

                // Record a minimal reset-audit artifact so the reset is provable without a selector leak
                // (the audit carries only the operation id + reason digest, not the account ids).
                var artId = OpaqueIdMint.NewDataArtifactId();
                var receiptId = OpaqueIdMint.NewReceiptId();
                var change = new JournalChange("artifact")
                    .Set("dataArtifactId", artId.Value)
                    .Set("artifactType", PilotArtifactType.ResetAudit.ToString())
                    .Set("storageLocator", "reset-audit:" + PilotAccountStore.Digest(operationId + "|" + reason))
                    .Set("createdAt", L(occurredAt))
                    .Set("expiresAt", "0")
                    .Set("status", ArtifactStatus.Active.ToString())
                    .Set("revision", "1")
                    .Set("receiptId", receiptId.Value);
                _store.Commit(operationId, "reset-scoped-" + operationId,
                    PilotAccountStore.Digest("reset-scoped|" + operationId),
                    PilotAccountStore.Digest(operationId), "reset-scoped:" + artId.Value, occurredAt,
                    new[] { change });

                return new ResetResult(false, receiptId.Value,
                    evidence.RemovedCredentialIds, evidence.RemovedCharacterIds, evidence.RemovedArtifactIds);
            }
        }

        // ---- FullPilotReset (AT-AIP-PURGE-FALLBACK-RESET, AT-AIP-FULL-RESET-ROTATES-KEY) ----

        /// <summary>Whole-fixture reset fallback: destroy the entire old catalog/journal, emit a
        /// selector-free <see cref="PilotPurgeCertificateProjection"/> preserving bounded
        /// proof, RETIRE the current lookup-key epoch, and open a fresh active epoch under
        /// <paramref name="freshKeyVersion"/> so no old binding can resolve again. After this no old
        /// account/credential/character/HMAC survives on disk. Operator-gated, fenced.</summary>
        public PilotPurgeCertificateProjection FullPilotReset(
            ServerObservedAdminContext op, string operationId, PilotId pilotId,
            string freshKeyVersion, long occurredAt)
        {
            RequireOperator(op);
            RequirePositive(occurredAt, "occurredAt");
            if (string.IsNullOrEmpty(freshKeyVersion))
                throw new PrivacyOperationException(PrivacyRejectionCode.LocatorInvalid,
                    "Full reset requires a fresh key epoch version.");

            if (_store.TryGetCommittedOp(operationId, out _, out _, out var recResult) &&
                recResult.StartsWith("pilot-reset:", StringComparison.Ordinal))
            {
                // Replay: the certificate already exists.
                var existing = _store.PurgeCertificates.FirstOrDefault();
                if (existing != null) return existing;
            }

            // Snapshot bounded proof BEFORE destruction: the ids + any recorded evidence digests of every
            // currently-cataloged artifact, and the retiring key epoch/version.
            var purgedArtifactIds = _store.Artifacts.Select(a => a.DataArtifactId.Value).ToList();
            var evidenceDigests = _store.Artifacts
                .Where(a => !string.IsNullOrEmpty(a.PurgeEvidenceDigest))
                .Select(a => a.PurgeEvidenceDigest).ToList();
            string retiredKeyVersion = _store.ActiveKeyEpochVersion();
            if (string.IsNullOrEmpty(retiredKeyVersion))
                retiredKeyVersion = "k0-pre-epoch";   // no epoch seeded yet; record the pre-epoch marker.

            if (string.Equals(freshKeyVersion, retiredKeyVersion, StringComparison.Ordinal))
                throw new PrivacyOperationException(PrivacyRejectionCode.OperationConflict,
                    "Full reset must open a NEW key epoch version distinct from the retiring one.");

            var purgeReceiptId = OpaqueIdMint.NewReceiptId();

            using (AcquireGlobalFence())
            {
                var cert = new JournalChange("purge-cert")
                    .Set("purgeReceiptId", purgeReceiptId.Value)
                    .Set("pilotId", pilotId.Value)
                    .Set("completedAt", L(occurredAt))
                    .Set("retiredKeyVersion", retiredKeyVersion)
                    .Set("freshKeyVersion", freshKeyVersion)
                    .Set("purgedArtifactIds", string.Join("~", purgedArtifactIds))
                    .Set("evidenceDigests", string.Join("~", evidenceDigests));
                var retire = new JournalChange("key-epoch")
                    .Set("keyVersion", retiredKeyVersion)
                    .Set("status", KeyEpochStatus.Retired.ToString())
                    .Set("revision", "1")
                    .Set("openedAt", "0")
                    .Set("retiredAt", L(occurredAt))
                    .Set("receiptId", purgeReceiptId.Value);
                var fresh = new JournalChange("key-epoch")
                    .Set("keyVersion", freshKeyVersion)
                    .Set("status", KeyEpochStatus.Active.ToString())
                    .Set("revision", "1")
                    .Set("openedAt", L(occurredAt))
                    .Set("retiredAt", "0")
                    .Set("receiptId", purgeReceiptId.Value);

                _store.ResetWholeFixture(cert, retire, fresh, operationId, occurredAt);
            }

            _store.TryGetPurgeCertificate(purgeReceiptId.Value, out var projected);
            return projected!;
        }

        // ---- Quarantine + no-time-travel (AT-AIP-QUARANTINE, AT-AIP-NO-TIME-TRAVEL) ----

        /// <summary>Mark an account Quarantined for durable ambiguity requiring an operator decision. A
        /// quarantined account admits nothing and cannot be silently repaired — only an explicit
        /// delete/reset resolves it. Deleted/Purged accounts NEVER quarantine back to a live state
        /// (no time travel). Operator-gated, idempotent, fenced.</summary>
        public void Quarantine(ServerObservedAdminContext op, string operationId, PilotAccountId accountId,
            string reason, long occurredAt)
        {
            RequireOperator(op);
            RequirePositive(occurredAt, "occurredAt");
            if (string.IsNullOrEmpty(reason))
                throw new PrivacyOperationException(PrivacyRejectionCode.RetentionHoldInvalid, "Quarantine requires a reason.");

            if (_store.TryGetCommittedOp(operationId, out _, out _, out var recResult))
            {
                if (recResult.StartsWith("quarantine:", StringComparison.Ordinal)) return;
                throw Conflict(operationId);
            }
            if (!_store.TryGetAccount(accountId, out var acct))
                throw new PrivacyOperationException(PrivacyRejectionCode.NotFound, "Account not found: " + accountId.Value);
            RejectTimeTravel(acct.Status, PilotAccountStatus.Quarantined);

            using (AcquireGlobalFence())
            {
                _store.TryGetAccount(accountId, out var fenced);
                var change = new JournalChange("acct-status")
                    .Set("accountId", accountId.Value)
                    .Set("status", PilotAccountStatus.Quarantined.ToString())
                    .Set("revision", L(fenced.Revision + 1));
                _store.Commit(operationId, "quarantine-" + accountId.Value,
                    PilotAccountStore.Digest("quarantine|" + accountId.Value),
                    PilotAccountStore.Digest(operationId), "quarantine:" + accountId.Value, occurredAt,
                    new[] { change });
            }
        }

        /// <summary>Whether a status transition is a forbidden "time travel" — reviving a terminal
        /// Deleted account, or reviving Quarantined/Deleted to Active. Terminal states never travel back.</summary>
        public static bool IsForbiddenRevival(PilotAccountStatus from, PilotAccountStatus to)
        {
            if (from == PilotAccountStatus.Deleted) return true;                 // Deleted is terminal
            if (to == PilotAccountStatus.Active &&
                (from == PilotAccountStatus.Quarantined || from == PilotAccountStatus.DeletionPending))
                return true;                                                     // no silent revival
            return false;
        }

        private static void RejectTimeTravel(PilotAccountStatus from, PilotAccountStatus to)
        {
            if (IsForbiddenRevival(from, to))
                throw new PrivacyOperationException(PrivacyRejectionCode.PilotClosed,
                    "Forbidden state transition (no time travel): " + from + " -> " + to + ".");
        }
    }

    /// <summary>Outcome of a completed account deletion+purge. Carries the physically-removed internal ids
    /// as artifact-specific proof — never a raw subject/HMAC.</summary>
    public sealed class AccountDeletionResult
    {
        public AccountDeletionResult(PilotAccountId accountId, bool replayed,
            IReadOnlyList<string> removedCredentialIds, IReadOnlyList<string> removedCharacterIds,
            IReadOnlyList<string> purgedArtifactIds, string deleteReceiptId)
        {
            AccountId = accountId;
            Replayed_ = replayed;
            RemovedCredentialIds = removedCredentialIds;
            RemovedCharacterIds = removedCharacterIds;
            PurgedArtifactIds = purgedArtifactIds;
            DeleteReceiptId = deleteReceiptId ?? string.Empty;
        }

        public PilotAccountId AccountId { get; }
        private bool Replayed_ { get; }
        public bool WasReplayed => Replayed_;
        public IReadOnlyList<string> RemovedCredentialIds { get; }
        public IReadOnlyList<string> RemovedCharacterIds { get; }
        public IReadOnlyList<string> PurgedArtifactIds { get; }
        public string DeleteReceiptId { get; }

        internal static AccountDeletionResult Replayed(PilotAccountId accountId) =>
            new AccountDeletionResult(accountId, true, Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>(), string.Empty);
    }

    /// <summary>Outcome of an explicit scoped reset. Evidence is the physically-removed ids by class.</summary>
    public sealed class ResetResult
    {
        public ResetResult(bool replayed, string receiptId,
            IReadOnlyList<string> removedCredentialIds, IReadOnlyList<string> removedCharacterIds,
            IReadOnlyList<string> removedArtifactIds)
        {
            WasReplayed = replayed;
            ReceiptId = receiptId ?? string.Empty;
            RemovedCredentialIds = removedCredentialIds;
            RemovedCharacterIds = removedCharacterIds;
            RemovedArtifactIds = removedArtifactIds;
        }

        public bool WasReplayed { get; }
        public string ReceiptId { get; }
        public IReadOnlyList<string> RemovedCredentialIds { get; }
        public IReadOnlyList<string> RemovedCharacterIds { get; }
        public IReadOnlyList<string> RemovedArtifactIds { get; }

        internal static ResetResult Replayed() =>
            new ResetResult(true, string.Empty, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
    }
}
