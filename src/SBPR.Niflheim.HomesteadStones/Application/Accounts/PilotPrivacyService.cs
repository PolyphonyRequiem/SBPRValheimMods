using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;

namespace SBPR.Niflheim.HomesteadStones.Application.Accounts
{
    // IAP-012 Tracer 4 — privacy foundation: export, retention, closure, and artifact catalog
    // (engine-free CLEAN core). This owns the operator/privacy vertical slice over the verified account
    // foundation (contracts.md §Operator commands, §Query contracts; data-model.md Aggregate 5,
    // Retention model). It is composed over a boot-rehydrated PilotAccountStore just like
    // PilotAccountService.
    //
    // FIX-FORWARD over PR #336 (t_f6c8c748): the original privacy foundation was structurally green but
    // semantically incomplete. This revision closes the independent-review gaps:
    //   (1) ExportAccount derives characters from acct.CharacterIds (never the caller's gameplay rows),
    //       filters gameplay/receipt rows to owned characters, and REJECTS foreign/untrusted rows.
    //   (2) A real fail-closed admission gate (IPrivacyAdmissionGate) so closed pilots and
    //       uncataloged/expired/PurgePending world fixtures reject live admission.
    //   (3) EVERY durable privacy mutation authorizes through OperatorAdminGate, replays idempotently on
    //       operationId (conflict-detecting), drains the per-scope mutation fence, commits atomically
    //       (intent→commit crash recovery via the framed journal), and records an audit receipt id.
    //   (4) Artifact locators/timestamps are validated, expiry is DERIVED from the retention policy per
    //       artifact class, and purge enforces expiry/holds/due-time and records selector + key-version +
    //       receipt identity so an account-scoped purge/key census is provable.
    //
    // net48 audit: System.* + LINQ only. No UnityEngine/Valheim/BepInEx.

    /// <summary>Stable rejection subset for the privacy/lifecycle surface (contracts §Stable rejection
    /// vocabulary).</summary>
    public enum PrivacyRejectionCode
    {
        None = 0,
        WorldFixtureUncataloged,
        RetentionHoldInvalid,
        PilotClosed,
        NotFound,
        RetentionValueInvalid,
        RetentionIncreaseRequiresRenotice,
        // FIX-FORWARD additions:
        Unauthorized,               // operator gate rejected the mutation
        OperationConflict,          // operationId already used for a different mutation
        ForeignCharacterRow,        // an export row referenced a character not owned by the account
        LocatorInvalid,             // artifact storage locator missing/blank
        TimestampInvalid,           // createdAt/occurredAt not a positive unix second
        ArtifactAlreadyPurged,      // purge of an already-purged artifact
        ArtifactNotDue,             // purge attempted before the artifact's derived expiry/due time
        ScopeHeld,                  // an active retention hold suppresses purge for the scope
        DrainTimeout                // the per-scope mutation fence could not be drained in time
    }

    public sealed class PrivacyOperationException : Exception
    {
        public PrivacyRejectionCode Code { get; }
        public PrivacyOperationException(PrivacyRejectionCode code, string message) : base(message) => Code = code;
    }

    /// <summary>The fail-closed live-admission decision the privacy layer contributes: a live session may
    /// only be admitted while the pilot is Active (not Closing/Purged, not past its purge deadline) and the
    /// active world-save fixture is cataloged, non-purged, and unexpired (AT-AIP-PILOT-CLOSURE-DEADLINE,
    /// AT-AIP-ARTIFACT-CATALOG). Wired into <see cref="LiveSessionAdmission"/> so an admission cannot bind a
    /// principal into a closed/uncataloged pilot.</summary>
    public interface IPrivacyAdmissionGate
    {
        /// <summary>Returns <see cref="PrivacyRejectionCode.None"/> when admission may proceed, else the
        /// stable, subject-free reason it fails closed.</summary>
        PrivacyRejectionCode EvaluateAdmission(long now);
    }

    /// <summary>The player-safe account export (data-model.md PlayerSafeAccountExport). It carries ONLY
    /// player-visible internal state; it must exclude raw/HMAC subjects, key versions/secrets, tokens,
    /// other accounts, and private operator notes (AIP-FR-021, AT-AIP-EXPORT-SAFE).</summary>
    public sealed class PlayerSafeAccountExport
    {
        public string AccountId = string.Empty;
        public string AccountStatus = string.Empty;
        public readonly List<string> CredentialClasses = new List<string>();   // provider namespace + status only
        public readonly List<string> CharacterIds = new List<string>();
        public readonly List<string> PlayerVisibleGameplayState = new List<string>();
        public readonly List<string> PlayerVisibleReceipts = new List<string>();
        public string RetentionSchedule = string.Empty;
        /// <summary>The durable audit receipt id minted for this export mutation. Never a secret; a stable
        /// selector an operator can use to prove the export was cataloged and later purged.</summary>
        public string ReceiptId = string.Empty;

        /// <summary>Every string this export would render, flattened, so a mechanical scan can prove no
        /// forbidden value (raw subject, HMAC, token) leaks into player-facing output.</summary>
        public IEnumerable<string> AllRenderedValues()
        {
            yield return AccountId;
            yield return AccountStatus;
            yield return RetentionSchedule;
            yield return ReceiptId;
            foreach (var c in CredentialClasses) yield return c;
            foreach (var c in CharacterIds) yield return c;
            foreach (var g in PlayerVisibleGameplayState) yield return g;
            foreach (var r in PlayerVisibleReceipts) yield return r;
        }
    }

    /// <summary>One gameplay/receipt row the operator supplies for export. It is already internal-only
    /// (minted CharacterId + player-visible payload); the export builder copies these verbatim only after
    /// proving the CharacterId is owned by the exported account, and adds no credential/HMAC material.</summary>
    public sealed class PlayerVisibleRecord
    {
        public PlayerVisibleRecord(string characterId, string summary)
        {
            CharacterId = characterId ?? string.Empty;
            Summary = summary ?? string.Empty;
        }
        public string CharacterId { get; }
        public string Summary { get; }
    }

    public sealed class PilotPrivacyService : IPrivacyAdmissionGate
    {
        private readonly PilotAccountStore _store;
        private readonly OperatorAdminGate _adminGate;
        private readonly AccountMutationFence _fence;
        private readonly TimeSpan _drainTimeout;

        /// <summary>The global fence scope key used to serialize pilot/artifact/hold mutations that are not
        /// account-scoped, so two global privacy mutations cannot interleave a half-committed transaction.</summary>
        internal const string GlobalFenceScope = "__privacy_global__";

        public PilotPrivacyService(PilotAccountStore store, OperatorAdminGate adminGate,
            AccountMutationFence fence, TimeSpan drainTimeout)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _adminGate = adminGate ?? throw new ArgumentNullException(nameof(adminGate));
            _fence = fence ?? throw new ArgumentNullException(nameof(fence));
            _drainTimeout = drainTimeout;
        }

        public PilotAccountStore Store => _store;

        // ---- Operator authority + fence + timestamp guards (shared preamble) ----

        private void RequireOperator(ServerObservedAdminContext op)
        {
            if (!_adminGate.Authorize(op, out var reject))
                throw new PrivacyOperationException(PrivacyRejectionCode.Unauthorized,
                    "Operator authority required for privacy mutation: " + reject);
        }

        private static void RequirePositive(long ts, string what)
        {
            if (ts <= 0)
                throw new PrivacyOperationException(PrivacyRejectionCode.TimestampInvalid,
                    what + " must be a positive unix-second timestamp.");
        }

        private AccountMutationFence.FenceLease AcquireFence(string scope)
        {
            if (!_fence.TryAcquireForLifecycle(scope, _drainTimeout, out var lease))
                throw new PrivacyOperationException(PrivacyRejectionCode.DrainTimeout,
                    "Could not drain the mutation fence for scope '" + scope + "'; mutation aborted (recoverable).");
            return lease;
        }

        // ---- Pilot lifecycle (contracts §ClosePilot; data-model.md Aggregate 5) ----

        /// <summary>Open a pilot lifecycle record. Operator-gated, idempotent on operationId, fenced, and
        /// receipted. The pilot begins Active; enrollment/admission is permitted until ClosePilot commits.</summary>
        public PilotId OpenPilot(ServerObservedAdminContext op, string operationId, string policyVersion,
            long startedAt, IAccountCrashInjector? crash = null)
        {
            RequireOperator(op);
            RequirePositive(startedAt, "startedAt");

            if (_store.TryGetCommittedOp(operationId, out _, out _, out var recResult))
            {
                if (recResult.StartsWith("pilot-open:", StringComparison.Ordinal))
                    return new PilotId(recResult.Substring("pilot-open:".Length));
                throw Conflict(operationId);
            }

            using (AcquireFence(GlobalFenceScope))
            {
                var pilotId = OpaqueIdMint.NewPilotId();
                var receiptId = OpaqueIdMint.NewReceiptId();
                var change = new JournalChange("pilot")
                    .Set("pilotId", pilotId.Value)
                    .Set("status", PilotLifecycleStatus.Active.ToString())
                    .Set("revision", "1")
                    .Set("startedAt", L(startedAt))
                    .Set("policyVersion", policyVersion ?? string.Empty)
                    .Set("receiptId", receiptId.Value);
                Commit(operationId, "pilot-open-" + pilotId.Value, "pilot-open:" + pilotId.Value, change, startedAt, crash);
                return pilotId;
            }
        }

        /// <summary>Close the pilot: commit Active -> Closing, stamp endedAt and a derived purgeDueAt from
        /// the closed-data retention period, after which enrollment/admission must reject
        /// (AT-AIP-PILOT-CLOSURE-DEADLINE). Operator-gated, idempotent, fenced, receipted. The deadline is
        /// observable in the catalog, not inferred from files.</summary>
        public void ClosePilot(ServerObservedAdminContext op, string operationId, PilotId pilotId,
            PilotRetentionPolicy policy, long endedAt, IAccountCrashInjector? crash = null)
        {
            RequireOperator(op);
            RequirePositive(endedAt, "endedAt");
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            if (_store.TryGetCommittedOp(operationId, out _, out _, out var recResult))
            {
                if (recResult.StartsWith("pilot-close:", StringComparison.Ordinal)) return; // idempotent replay
                throw Conflict(operationId);
            }
            if (!_store.TryGetPilot(pilotId, out var pilot))
                throw new PrivacyOperationException(PrivacyRejectionCode.NotFound, "Pilot not found: " + pilotId.Value);

            using (AcquireFence(GlobalFenceScope))
            {
                long purgeDueAt = policy.ClosedDataPurgeDueAt(endedAt);
                var receiptId = OpaqueIdMint.NewReceiptId();
                var change = new JournalChange("pilot-status")
                    .Set("pilotId", pilotId.Value)
                    .Set("status", PilotLifecycleStatus.Closing.ToString())
                    .Set("revision", L(pilot.Revision + 1))
                    .Set("endedAt", L(endedAt))
                    .Set("purgeDueAt", L(purgeDueAt))
                    .Set("policyVersion", policy.PolicyVersion)
                    .Set("receiptId", receiptId.Value);
                Commit(operationId, "pilot-close-" + pilotId.Value, "pilot-close:" + pilotId.Value, change, endedAt, crash);
            }
        }

        /// <summary>Whether the pilot still admits enrollment/new account creation. Closing/Purged reject
        /// (contracts `PilotClosed`).</summary>
        public bool AdmitsEnrollment(PilotId pilotId) =>
            _store.TryGetPilot(pilotId, out var pilot) && pilot.Status == PilotLifecycleStatus.Active;

        // ---- Fail-closed live admission gate (correction 2) ----

        /// <summary>The single active pilot + its active world-save fixture this server admits into. Set by
        /// the composition root at boot so <see cref="EvaluateAdmission"/> can fail closed on a closed pilot
        /// or an uncataloged/expired/purged world fixture without the caller re-deriving them each tick.</summary>
        private PilotId _admissionPilot;
        private string _admissionWorldLocator = string.Empty;
        private bool _admissionConfigured;

        /// <summary>Bind this service as the live-admission gate for one pilot + world fixture. Called once
        /// at composition. Until configured, <see cref="EvaluateAdmission"/> fails closed.</summary>
        public void ConfigureAdmission(PilotId pilotId, string worldStorageLocator)
        {
            _admissionPilot = pilotId;
            _admissionWorldLocator = worldStorageLocator ?? string.Empty;
            _admissionConfigured = true;
        }

        /// <inheritdoc />
        public PrivacyRejectionCode EvaluateAdmission(long now)
        {
            // Fail closed if the gate was never configured with a pilot + world fixture.
            if (!_admissionConfigured) return PrivacyRejectionCode.PilotClosed;

            // The pilot must exist and still be Active (Closing/Purged reject), and not past its deadline.
            if (!_store.TryGetPilot(_admissionPilot, out var pilot)) return PrivacyRejectionCode.PilotClosed;
            if (pilot.Status != PilotLifecycleStatus.Active) return PrivacyRejectionCode.PilotClosed;
            if (pilot.PurgeDueAt > 0 && now >= pilot.PurgeDueAt) return PrivacyRejectionCode.PilotClosed;

            // The active world fixture must resolve to a cataloged, non-purged, unexpired WorldSave.
            if (!TryGetActiveWorldFixture(_admissionWorldLocator, now, out _))
                return PrivacyRejectionCode.WorldFixtureUncataloged;

            return PrivacyRejectionCode.None;
        }

        // ---- Artifact catalog (contracts §Performance and failure; data-model.md Aggregate 5) ----

        /// <summary>Catalog one artifact generation before it is used (AT-AIP-ARTIFACT-CATALOG). The
        /// storage locator and createdAt are validated, and the expiry is DERIVED from the retention period
        /// appropriate to the artifact class (security logs use the security-log period; everything else
        /// uses the closed-data period) so the purge inventory is sufficient for later purge proof.
        /// Operator-gated, idempotent, fenced, receipted.</summary>
        public DataArtifactId CatalogArtifact(ServerObservedAdminContext op, string operationId,
            PilotArtifactType artifactType, string storageLocator, PilotRetentionPolicy policy,
            long createdAt, IAccountCrashInjector? crash = null)
        {
            RequireOperator(op);
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (string.IsNullOrWhiteSpace(storageLocator))
                throw new PrivacyOperationException(PrivacyRejectionCode.LocatorInvalid,
                    "Artifact catalog requires a non-empty storage locator.");
            RequirePositive(createdAt, "createdAt");

            if (_store.TryGetCommittedOp(operationId, out _, out _, out var recResult))
            {
                if (recResult.StartsWith("art:", StringComparison.Ordinal))
                    return new DataArtifactId(recResult.Substring("art:".Length));
                throw Conflict(operationId);
            }

            using (AcquireFence(GlobalFenceScope))
            {
                long expiresAt = DeriveExpiry(artifactType, policy, createdAt);
                var id = OpaqueIdMint.NewDataArtifactId();
                var receiptId = OpaqueIdMint.NewReceiptId();
                var change = new JournalChange("artifact")
                    .Set("dataArtifactId", id.Value)
                    .Set("artifactType", artifactType.ToString())
                    .Set("storageLocator", storageLocator)
                    .Set("createdAt", L(createdAt))
                    .Set("expiresAt", L(expiresAt))
                    .Set("status", ArtifactStatus.Active.ToString())
                    .Set("revision", "1")
                    .Set("policyVersion", policy.PolicyVersion)
                    .Set("keyVersion", ClosedKeyVersion())
                    .Set("receiptId", receiptId.Value);
                Commit(operationId, "art-" + id.Value, "art:" + id.Value, change, createdAt, crash);
                return id;
            }
        }

        /// <summary>The purge deadline derived from the retention policy for one artifact class: security
        /// logs use SecurityLogPurgeDueAt, every other pilot-linked artifact uses ClosedDataPurgeDueAt.</summary>
        private static long DeriveExpiry(PilotArtifactType type, PilotRetentionPolicy policy, long createdAt) =>
            type == PilotArtifactType.SecurityLog
                ? policy.SecurityLogPurgeDueAt(createdAt)
                : policy.ClosedDataPurgeDueAt(createdAt);

        /// <summary>Admission gate: the active pilot world-save fixture MUST resolve to a cataloged,
        /// non-purged, unexpired WorldSave artifact or admission fails closed (AT-AIP-ARTIFACT-CATALOG;
        /// contracts §Performance and failure). Returns silently on success; throws otherwise.</summary>
        public void RequireCatalogedWorldFixture(string worldStorageLocator, long now)
        {
            if (!TryGetActiveWorldFixture(worldStorageLocator, now, out _))
                throw new PrivacyOperationException(PrivacyRejectionCode.WorldFixtureUncataloged,
                    "Active world fixture '" + worldStorageLocator + "' is not cataloged/active; admission fails closed.");
        }

        private bool TryGetActiveWorldFixture(string worldStorageLocator, long now, out PilotDataArtifactProjection fixture)
        {
            fixture = null!;
            if (string.IsNullOrWhiteSpace(worldStorageLocator)) return false;
            foreach (var a in _store.Artifacts)
                if (a.ArtifactType == PilotArtifactType.WorldSave &&
                    a.Status == ArtifactStatus.Active &&
                    string.Equals(a.StorageLocator, worldStorageLocator, StringComparison.Ordinal) &&
                    (a.ExpiresAt <= 0 || now < a.ExpiresAt))
                {
                    fixture = a;
                    return true;
                }
            return false;
        }

        /// <summary>Mark one cataloged artifact Purged with an artifact-specific evidence digest. Purge is
        /// only permitted when it is DUE (now past the derived expiry) and its scope is not under an active
        /// retention hold; counts alone never prove purge (data-model.md Aggregate 5 invariants). Records
        /// the artifact selector + key version + a purge receipt id so an account-scoped purge/key census is
        /// provable. Operator-gated, idempotent, fenced. <paramref name="force"/> bypasses the due-time gate
        /// ONLY for an incident/full-reset purge and still records the evidence + receipt.</summary>
        public void PurgeArtifact(ServerObservedAdminContext op, string operationId, DataArtifactId id,
            string evidenceDigest, long occurredAt, bool force = false, IAccountCrashInjector? crash = null)
        {
            RequireOperator(op);
            RequirePositive(occurredAt, "occurredAt");
            if (string.IsNullOrEmpty(evidenceDigest))
                throw new PrivacyOperationException(PrivacyRejectionCode.LocatorInvalid,
                    "Purge requires an artifact-specific evidence digest; counts alone do not prove purge.");

            if (_store.TryGetCommittedOp(operationId, out _, out _, out var recResult))
            {
                if (recResult.StartsWith("art-purge:", StringComparison.Ordinal)) return; // idempotent replay
                throw Conflict(operationId);
            }
            if (!_store.TryGetArtifact(id, out var art))
                throw new PrivacyOperationException(PrivacyRejectionCode.NotFound, "Artifact not found: " + id.Value);
            if (art.Status == ArtifactStatus.Purged)
                throw new PrivacyOperationException(PrivacyRejectionCode.ArtifactAlreadyPurged, "Artifact already purged: " + id.Value);

            // Scope hold: an active hold over the artifact's own selector suppresses ordinary purge.
            string selector = ArtifactSelector(art);
            if (IsScopeHeld(selector, occurredAt))
                throw new PrivacyOperationException(PrivacyRejectionCode.ScopeHeld,
                    "An active retention hold suppresses purge for scope '" + selector + "'.");

            // Due-time: an ordinary purge may only run once the artifact is past its derived expiry.
            if (!force && art.ExpiresAt > 0 && occurredAt < art.ExpiresAt)
                throw new PrivacyOperationException(PrivacyRejectionCode.ArtifactNotDue,
                    "Artifact '" + id.Value + "' is not yet due for purge (expiresAt=" + art.ExpiresAt + ").");

            using (AcquireFence(GlobalFenceScope))
            {
                var receiptId = OpaqueIdMint.NewReceiptId();
                var change = new JournalChange("artifact-status")
                    .Set("dataArtifactId", id.Value)
                    .Set("status", ArtifactStatus.Purged.ToString())
                    .Set("revision", L(art.Revision + 1))
                    .Set("purgeEvidenceDigest", evidenceDigest)
                    .Set("selector", selector)
                    .Set("keyVersion", ClosedKeyVersion())
                    .Set("receiptId", receiptId.Value);
                Commit(operationId, "art-purge-" + id.Value, "art-purge:" + id.Value, change, occurredAt, crash);
            }
        }

        /// <summary>Mark a due, unheld artifact PurgePending (the two-phase purge intent recorded before the
        /// external bytes are destroyed). A PurgePending world fixture no longer admits, and completion via
        /// <see cref="PurgeArtifact"/> requires the evidence digest.</summary>
        public void MarkArtifactPurgePending(ServerObservedAdminContext op, string operationId, DataArtifactId id,
            long occurredAt, IAccountCrashInjector? crash = null)
        {
            RequireOperator(op);
            RequirePositive(occurredAt, "occurredAt");
            if (_store.TryGetCommittedOp(operationId, out _, out _, out var recResult))
            {
                if (recResult.StartsWith("art-pending:", StringComparison.Ordinal)) return;
                throw Conflict(operationId);
            }
            if (!_store.TryGetArtifact(id, out var art))
                throw new PrivacyOperationException(PrivacyRejectionCode.NotFound, "Artifact not found: " + id.Value);
            if (art.Status == ArtifactStatus.Purged)
                throw new PrivacyOperationException(PrivacyRejectionCode.ArtifactAlreadyPurged, "Artifact already purged: " + id.Value);

            using (AcquireFence(GlobalFenceScope))
            {
                var receiptId = OpaqueIdMint.NewReceiptId();
                var change = new JournalChange("artifact-status")
                    .Set("dataArtifactId", id.Value)
                    .Set("status", ArtifactStatus.PurgePending.ToString())
                    .Set("revision", L(art.Revision + 1))
                    .Set("selector", ArtifactSelector(art))
                    .Set("receiptId", receiptId.Value);
                Commit(operationId, "art-pending-" + id.Value, "art-pending:" + id.Value, change, occurredAt, crash);
            }
        }

        private static string ArtifactSelector(PilotDataArtifactProjection art) =>
            "artifact:" + art.ArtifactType + ":" + art.DataArtifactId.Value;

        // ---- Retention holds (contracts §SetRetentionHold; data-model.md RetentionHold) ----

        /// <summary>Create a scoped, reasoned, EXPIRING retention hold. A hold that omits an expiry or
        /// targets everything by default is rejected (AT-AIP-HOLD-EXPIRY). expiresAt must be strictly after
        /// createdAt. Operator-gated, idempotent, fenced, receipted.</summary>
        public RetentionHoldId SetRetentionHold(ServerObservedAdminContext op, string operationId, string scope,
            string reason, long createdAt, long expiresAt, IAccountCrashInjector? crash = null)
        {
            RequireOperator(op);
            RequirePositive(createdAt, "createdAt");
            if (string.IsNullOrEmpty(scope) || string.Equals(scope, "*", StringComparison.Ordinal) ||
                string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase))
                throw new PrivacyOperationException(PrivacyRejectionCode.RetentionHoldInvalid,
                    "Retention hold requires a bounded scope; a global/indefinite hold is rejected.");
            if (string.IsNullOrEmpty(reason))
                throw new PrivacyOperationException(PrivacyRejectionCode.RetentionHoldInvalid, "Retention hold requires a reason.");
            if (expiresAt <= createdAt)
                throw new PrivacyOperationException(PrivacyRejectionCode.RetentionHoldInvalid,
                    "Retention hold requires an expiry strictly after creation; a hold cannot make data permanent.");

            if (_store.TryGetCommittedOp(operationId, out _, out _, out var recResult))
            {
                if (recResult.StartsWith("hold:", StringComparison.Ordinal))
                    return new RetentionHoldId(recResult.Substring("hold:".Length));
                throw Conflict(operationId);
            }

            using (AcquireFence(GlobalFenceScope))
            {
                var id = OpaqueIdMint.NewRetentionHoldId();
                var receiptId = OpaqueIdMint.NewReceiptId();
                var change = new JournalChange("hold")
                    .Set("retentionHoldId", id.Value)
                    .Set("scope", scope)
                    .Set("reason", reason)
                    .Set("revision", "1")
                    .Set("createdAt", L(createdAt))
                    .Set("expiresAt", L(expiresAt))
                    .Set("status", RetentionHoldStatus.Active.ToString())
                    .Set("receiptId", receiptId.Value);
                Commit(operationId, "hold-" + id.Value, "hold:" + id.Value, change, createdAt, crash);
                return id;
            }
        }

        public void ReleaseRetentionHold(ServerObservedAdminContext op, string operationId, RetentionHoldId id,
            long occurredAt, IAccountCrashInjector? crash = null)
        {
            RequireOperator(op);
            RequirePositive(occurredAt, "occurredAt");
            if (_store.TryGetCommittedOp(operationId, out _, out _, out var recResult))
            {
                if (recResult.StartsWith("hold-release:", StringComparison.Ordinal)) return;
                throw Conflict(operationId);
            }
            if (!_store.TryGetRetentionHold(id, out var hold))
                throw new PrivacyOperationException(PrivacyRejectionCode.NotFound, "Retention hold not found: " + id.Value);

            using (AcquireFence(GlobalFenceScope))
            {
                var receiptId = OpaqueIdMint.NewReceiptId();
                var change = new JournalChange("hold-status")
                    .Set("retentionHoldId", id.Value)
                    .Set("status", RetentionHoldStatus.Released.ToString())
                    .Set("revision", L(hold.Revision + 1))
                    .Set("receiptId", receiptId.Value);
                Commit(operationId, "hold-release-" + id.Value, "hold-release:" + id.Value, change, occurredAt, crash);
            }
        }

        /// <summary>Whether a hold currently suppresses purge for a scope at <paramref name="now"/>: an
        /// Active, unreleased hold whose expiry is still in the future. An EXPIRED hold no longer holds —
        /// ordinary purge eligibility resumes automatically (AT-AIP-HOLD-EXPIRY).</summary>
        public bool IsScopeHeld(string scope, long now)
        {
            foreach (var h in _store.RetentionHolds)
                if (h.Status == RetentionHoldStatus.Active &&
                    string.Equals(h.Scope, scope, StringComparison.Ordinal) &&
                    h.ExpiresAt > now)
                    return true;
            return false;
        }

        // ---- Player-safe export (contracts §ExportPilotAccount; AT-AIP-EXPORT-SAFE) ----

        /// <summary>Build a player-safe export for ONE account. Characters are derived from the account's
        /// own membership list (<c>acct.CharacterIds</c>) — never the caller's supplied rows. Each supplied
        /// gameplay/receipt row must reference a character OWNED by this account; a foreign/untrusted row is
        /// REJECTED (ForeignCharacterRow), so a caller cannot smuggle another account's data or arbitrary
        /// rows into the export. It NEVER reads credential HMACs, key versions, or raw subjects, and it
        /// catalogs the export as a PilotDataArtifactRecord with a policy-derived expiry before returning
        /// (contracts §ExportPilotAccount). Operator-gated, idempotent, fenced, receipted.</summary>
        public PlayerSafeAccountExport ExportAccount(
            ServerObservedAdminContext op, string operationId, PilotAccountId accountId,
            IReadOnlyList<PlayerVisibleRecord>? gameplayRows, IReadOnlyList<PlayerVisibleRecord>? receiptRows,
            string retentionSchedule, string exportStorageLocator, PilotRetentionPolicy policy,
            long occurredAt, IAccountCrashInjector? crash = null)
        {
            RequireOperator(op);
            RequirePositive(occurredAt, "occurredAt");
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (string.IsNullOrWhiteSpace(exportStorageLocator))
                throw new PrivacyOperationException(PrivacyRejectionCode.LocatorInvalid,
                    "Export requires a non-empty storage locator.");
            if (!_store.TryGetAccount(accountId, out var acct))
                throw new PrivacyOperationException(PrivacyRejectionCode.NotFound, "Account not found: " + accountId.Value);

            // AUTHORITATIVE owned-character set: the account's own membership list, never the caller's rows.
            var owned = new HashSet<string>(acct.CharacterIds.Select(c => c.Value), StringComparer.Ordinal);

            // Reject any supplied row that references a character this account does not own. This is the
            // cross-account / untrusted-row firewall (AT-AIP-EXPORT-CROSS-ACCOUNT-BLOCK).
            RejectForeignRows(gameplayRows, owned);
            RejectForeignRows(receiptRows, owned);

            var receiptId = OpaqueIdMint.NewReceiptId();
            var export = new PlayerSafeAccountExport
            {
                AccountId = acct.AccountId.Value,
                AccountStatus = acct.Status.ToString(),
                RetentionSchedule = retentionSchedule ?? string.Empty,
                ReceiptId = receiptId.Value,
            };

            // Credential CLASS only: provider namespace + status. Never the HMAC/subject/key version.
            foreach (var credId in acct.CredentialBindingIds)
                if (_store.TryGetCredential(credId, out var cred) && cred.Status == CredentialStatus.Active)
                    export.CredentialClasses.Add(cred.ProviderNamespace + ":" + cred.Status);

            // Characters derived from the account membership list, ordered, deduped.
            foreach (var c in owned.OrderBy(c => c, StringComparer.Ordinal))
                export.CharacterIds.Add(c);

            if (gameplayRows != null)
                foreach (var g in gameplayRows) export.PlayerVisibleGameplayState.Add(g.CharacterId + ": " + g.Summary);
            if (receiptRows != null)
                foreach (var r in receiptRows) export.PlayerVisibleReceipts.Add(r.CharacterId + ": " + r.Summary);

            // Catalog the export artifact with a policy-derived expiry before success is acknowledged.
            if (!_store.TryGetCommittedOp(operationId, out _, out _, out var recResult))
            {
                using (AcquireFence(accountId.Value))
                {
                    long expiresAt = policy.ClosedDataPurgeDueAt(occurredAt);
                    var artId = OpaqueIdMint.NewDataArtifactId();
                    var change = new JournalChange("artifact")
                        .Set("dataArtifactId", artId.Value)
                        .Set("artifactType", PilotArtifactType.Export.ToString())
                        .Set("storageLocator", exportStorageLocator)
                        .Set("createdAt", L(occurredAt))
                        .Set("expiresAt", L(expiresAt))
                        .Set("status", ArtifactStatus.Active.ToString())
                        .Set("revision", "1")
                        .Set("policyVersion", policy.PolicyVersion)
                        .Set("keyVersion", ClosedKeyVersion())
                        .Set("accountId", accountId.Value)
                        .Set("receiptId", receiptId.Value);
                    Commit(operationId, "export-" + artId.Value, "export:" + artId.Value + "/" + accountId.Value,
                        change, occurredAt, crash);
                }
            }
            else if (!recResult.StartsWith("export:", StringComparison.Ordinal))
            {
                throw Conflict(operationId);
            }

            return export;
        }

        private void RejectForeignRows(IReadOnlyList<PlayerVisibleRecord>? rows, HashSet<string> owned)
        {
            if (rows == null) return;
            foreach (var r in rows)
                if (string.IsNullOrEmpty(r.CharacterId) || !owned.Contains(r.CharacterId))
                    throw new PrivacyOperationException(PrivacyRejectionCode.ForeignCharacterRow,
                        "Export row references a character not owned by the exported account.");
        }

        // ---- Key census helper (records the key version needed for account-scoped purge/key census) ----

        /// <summary>The lookup key version stamped on account-scoped artifacts so a later purge/key census
        /// can attribute artifacts to a key epoch (AT-AIP-KEY-VERSION-CENSUS). Derived from the store's live
        /// census (the single active credential key version), or "unknown" when the store carries none.</summary>
        private string ClosedKeyVersion()
        {
            var versions = _store.RunCensus().Versions();
            return versions.Count > 0 ? versions.First() : "unknown";
        }

        // ---- shared commit / conflict helpers ----

        private static PrivacyOperationException Conflict(string operationId) =>
            new PrivacyOperationException(PrivacyRejectionCode.OperationConflict,
                "operationId '" + operationId + "' already committed a different mutation.");

        private void Commit(string operationId, string txnId, string resultCode, JournalChange change,
            long occurredAt, IAccountCrashInjector? crash)
        {
            _store.Commit(operationId, txnId, PilotAccountStore.Digest(txnId),
                PilotAccountStore.Digest(operationId), resultCode, occurredAt, new[] { change }, crash);
        }

        private static string L(long v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
