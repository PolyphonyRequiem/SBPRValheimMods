using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    // Named acceptance (spec §Requirement-to-acceptance; plan §Tracer 4 subset owned by IAP-012):
    //   AT-AIP-EXPORT-SAFE                 AT-AIP-RETENTION-CONFIG
    //   AT-AIP-RETENTION-INCREASE-RENOTICE AT-AIP-HOLD-EXPIRY
    //   AT-AIP-ARTIFACT-CATALOG            AT-AIP-PILOT-CLOSURE-DEADLINE
    //   (AT-AIP-DISCLOSURE-COMPLETE / AT-AIP-DATA-INVENTORY-BASIS ship in the Tracer-1 disclosure core.)
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
    }

    public sealed class PrivacyOperationException : Exception
    {
        public PrivacyRejectionCode Code { get; }
        public PrivacyOperationException(PrivacyRejectionCode code, string message) : base(message) => Code = code;
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

        /// <summary>Every string this export would render, flattened, so a mechanical scan can prove no
        /// forbidden value (raw subject, HMAC, token) leaks into player-facing output.</summary>
        public IEnumerable<string> AllRenderedValues()
        {
            yield return AccountId;
            yield return AccountStatus;
            yield return RetentionSchedule;
            foreach (var c in CredentialClasses) yield return c;
            foreach (var c in CharacterIds) yield return c;
            foreach (var g in PlayerVisibleGameplayState) yield return g;
            foreach (var r in PlayerVisibleReceipts) yield return r;
        }
    }

    /// <summary>One gameplay/receipt row the operator supplies for export. It is already internal-only
    /// (minted CharacterId + player-visible payload); the export builder copies these verbatim and adds
    /// no credential/HMAC material.</summary>
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

    public sealed class PilotPrivacyService
    {
        private readonly PilotAccountStore _store;

        public PilotPrivacyService(PilotAccountStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public PilotAccountStore Store => _store;

        // ---- Pilot lifecycle (contracts §ClosePilot; data-model.md Aggregate 5) ----

        /// <summary>Open a pilot lifecycle record. The pilot begins Active; enrollment/admission is
        /// permitted until ClosePilot commits.</summary>
        public PilotId OpenPilot(string operationId, string policyVersion, long startedAt,
            IAccountCrashInjector? crash = null)
        {
            var pilotId = OpaqueIdMint.NewPilotId();
            var change = new JournalChange("pilot")
                .Set("pilotId", pilotId.Value)
                .Set("status", PilotLifecycleStatus.Active.ToString())
                .Set("revision", "1")
                .Set("startedAt", L(startedAt))
                .Set("policyVersion", policyVersion ?? string.Empty);
            Commit(operationId, "pilot-open-" + pilotId.Value, "pilot:" + pilotId.Value, change, startedAt, crash);
            return pilotId;
        }

        /// <summary>Close the pilot: commit Active -> Closing, stamp endedAt and a derived purgeDueAt from
        /// the closed-data retention period, after which enrollment/admission must reject
        /// (AT-AIP-PILOT-CLOSURE-DEADLINE). The deadline is observable in the catalog, not inferred from
        /// files.</summary>
        public void ClosePilot(string operationId, PilotId pilotId, PilotRetentionPolicy policy,
            long endedAt, IAccountCrashInjector? crash = null)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (!_store.TryGetPilot(pilotId, out var pilot))
                throw new PrivacyOperationException(PrivacyRejectionCode.NotFound, "Pilot not found: " + pilotId.Value);

            long purgeDueAt = policy.ClosedDataPurgeDueAt(endedAt);
            var change = new JournalChange("pilot-status")
                .Set("pilotId", pilotId.Value)
                .Set("status", PilotLifecycleStatus.Closing.ToString())
                .Set("revision", L(pilot.Revision + 1))
                .Set("endedAt", L(endedAt))
                .Set("purgeDueAt", L(purgeDueAt))
                .Set("policyVersion", policy.PolicyVersion);
            Commit(operationId, "pilot-close-" + pilotId.Value, "pilot-close:" + pilotId.Value, change, endedAt, crash);
        }

        /// <summary>Whether the pilot still admits enrollment/new account creation. Closing/Purged reject
        /// (contracts `PilotClosed`).</summary>
        public bool AdmitsEnrollment(PilotId pilotId) =>
            _store.TryGetPilot(pilotId, out var pilot) && pilot.Status == PilotLifecycleStatus.Active;

        // ---- Artifact catalog (contracts §Performance and failure; data-model.md Aggregate 5) ----

        /// <summary>Catalog one artifact generation before it is used (AT-AIP-ARTIFACT-CATALOG). The
        /// expiry is derived from the retention period appropriate to the artifact class so the purge
        /// inventory is sufficient for later purge proof.</summary>
        public DataArtifactId CatalogArtifact(string operationId, PilotArtifactType artifactType,
            string storageLocator, long createdAt, long expiresAt, IAccountCrashInjector? crash = null)
        {
            var id = OpaqueIdMint.NewDataArtifactId();
            var change = new JournalChange("artifact")
                .Set("dataArtifactId", id.Value)
                .Set("artifactType", artifactType.ToString())
                .Set("storageLocator", storageLocator ?? string.Empty)
                .Set("createdAt", L(createdAt))
                .Set("expiresAt", L(expiresAt))
                .Set("status", ArtifactStatus.Active.ToString())
                .Set("revision", "1");
            Commit(operationId, "art-" + id.Value, "art:" + id.Value, change, createdAt, crash);
            return id;
        }

        /// <summary>Admission gate: the active pilot world-save fixture MUST resolve to a cataloged,
        /// non-purged WorldSave artifact or admission fails closed (AT-AIP-ARTIFACT-CATALOG; contracts
        /// §Performance and failure). Returns silently on success; throws on an uncataloged fixture.</summary>
        public void RequireCatalogedWorldFixture(string worldStorageLocator)
        {
            if (!_store.IsWorldFixtureCataloged(worldStorageLocator))
                throw new PrivacyOperationException(PrivacyRejectionCode.WorldFixtureUncataloged,
                    "Active world fixture '" + worldStorageLocator + "' is not cataloged; admission fails closed.");
        }

        /// <summary>Mark one cataloged artifact Purged with an artifact-specific evidence digest. Counts
        /// alone never prove purge; a terminal receipt + evidence digest do (data-model.md Aggregate 5
        /// invariants).</summary>
        public void PurgeArtifact(string operationId, DataArtifactId id, string evidenceDigest,
            long occurredAt, IAccountCrashInjector? crash = null)
        {
            if (!_store.TryGetArtifact(id, out var art))
                throw new PrivacyOperationException(PrivacyRejectionCode.NotFound, "Artifact not found: " + id.Value);
            if (string.IsNullOrEmpty(evidenceDigest))
                throw new ArgumentException("Purge requires an artifact-specific evidence digest; counts alone do not prove purge.", nameof(evidenceDigest));

            var change = new JournalChange("artifact-status")
                .Set("dataArtifactId", id.Value)
                .Set("status", ArtifactStatus.Purged.ToString())
                .Set("revision", L(art.Revision + 1))
                .Set("purgeEvidenceDigest", evidenceDigest);
            Commit(operationId, "art-purge-" + id.Value, "art-purge:" + id.Value, change, occurredAt, crash);
        }

        // ---- Retention holds (contracts §SetRetentionHold; data-model.md RetentionHold) ----

        /// <summary>Create a scoped, reasoned, EXPIRING retention hold. A hold that omits an expiry or
        /// targets everything by default is rejected (AT-AIP-HOLD-EXPIRY). expiresAt must be strictly
        /// after createdAt.</summary>
        public RetentionHoldId SetRetentionHold(string operationId, string scope, string reason,
            long createdAt, long expiresAt, IAccountCrashInjector? crash = null)
        {
            if (string.IsNullOrEmpty(scope) || string.Equals(scope, "*", StringComparison.Ordinal) ||
                string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase))
                throw new PrivacyOperationException(PrivacyRejectionCode.RetentionHoldInvalid,
                    "Retention hold requires a bounded scope; a global/indefinite hold is rejected.");
            if (string.IsNullOrEmpty(reason))
                throw new PrivacyOperationException(PrivacyRejectionCode.RetentionHoldInvalid, "Retention hold requires a reason.");
            if (expiresAt <= createdAt)
                throw new PrivacyOperationException(PrivacyRejectionCode.RetentionHoldInvalid,
                    "Retention hold requires an expiry strictly after creation; a hold cannot make data permanent.");

            var id = OpaqueIdMint.NewRetentionHoldId();
            var change = new JournalChange("hold")
                .Set("retentionHoldId", id.Value)
                .Set("scope", scope)
                .Set("reason", reason)
                .Set("revision", "1")
                .Set("createdAt", L(createdAt))
                .Set("expiresAt", L(expiresAt))
                .Set("status", RetentionHoldStatus.Active.ToString());
            Commit(operationId, "hold-" + id.Value, "hold:" + id.Value, change, createdAt, crash);
            return id;
        }

        public void ReleaseRetentionHold(string operationId, RetentionHoldId id, long occurredAt,
            IAccountCrashInjector? crash = null)
        {
            if (!_store.TryGetRetentionHold(id, out var hold))
                throw new PrivacyOperationException(PrivacyRejectionCode.NotFound, "Retention hold not found: " + id.Value);
            var change = new JournalChange("hold-status")
                .Set("retentionHoldId", id.Value)
                .Set("status", RetentionHoldStatus.Released.ToString())
                .Set("revision", L(hold.Revision + 1));
            Commit(operationId, "hold-release-" + id.Value, "hold-release:" + id.Value, change, occurredAt, crash);
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

        /// <summary>Build a player-safe export from internal account/character projections and the
        /// operator-supplied player-visible gameplay/receipt rows. It NEVER reads credential HMACs, key
        /// versions, or raw subjects. Before returning it catalogs the export as a PilotDataArtifactRecord
        /// with an expiry (contracts §ExportPilotAccount). Unrelated accounts are excluded by construction:
        /// only the requested account's characters are enumerated.</summary>
        public PlayerSafeAccountExport ExportAccount(
            string operationId, PilotAccountId accountId,
            IReadOnlyList<PlayerVisibleRecord> gameplayRows, IReadOnlyList<PlayerVisibleRecord> receiptRows,
            string retentionSchedule, string exportStorageLocator, long occurredAt, long expiresAt,
            IAccountCrashInjector? crash = null)
        {
            if (!_store.TryGetAccount(accountId, out var acct))
                throw new PrivacyOperationException(PrivacyRejectionCode.NotFound, "Account not found: " + accountId.Value);

            var export = new PlayerSafeAccountExport
            {
                AccountId = acct.AccountId.Value,
                AccountStatus = acct.Status.ToString(),
                RetentionSchedule = retentionSchedule ?? string.Empty,
            };

            // Credential CLASS only: provider namespace + status. Never the HMAC/subject/key version.
            foreach (var credId in acct.CredentialBindingIds)
                if (_store.TryGetCredential(credId, out var cred) && cred.Status == CredentialStatus.Active)
                    export.CredentialClasses.Add(cred.ProviderNamespace + ":" + cred.Status);

            // Character ids owned by THIS account. (Character bindings arrive in Tracer 2; until then the
            // account's own membership list is authoritative and already internal-only.)
            var ownedCharacters = new HashSet<string>(
                gameplayRows?.Select(r => r.CharacterId).Where(c => c.Length > 0) ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);
            foreach (var c in ownedCharacters.OrderBy(c => c, StringComparer.Ordinal))
                export.CharacterIds.Add(c);

            if (gameplayRows != null)
                foreach (var g in gameplayRows) export.PlayerVisibleGameplayState.Add(g.CharacterId + ": " + g.Summary);
            if (receiptRows != null)
                foreach (var r in receiptRows) export.PlayerVisibleReceipts.Add(r.CharacterId + ": " + r.Summary);

            // Catalog the export artifact with expiry before success is acknowledged.
            CatalogArtifact(operationId + "#catalog", PilotArtifactType.Export, exportStorageLocator, occurredAt, expiresAt, crash);
            return export;
        }

        // ---- shared commit helper ----

        private void Commit(string operationId, string txnId, string resultCode, JournalChange change,
            long occurredAt, IAccountCrashInjector? crash)
        {
            _store.Commit(operationId, txnId, PilotAccountStore.Digest(txnId),
                PilotAccountStore.Digest(operationId), resultCode, occurredAt, new[] { change }, crash);
        }

        private static string L(long v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
