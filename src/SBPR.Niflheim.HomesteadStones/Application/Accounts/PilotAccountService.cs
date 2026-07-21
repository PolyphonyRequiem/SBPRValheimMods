using System;
using System.Collections.Generic;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;

namespace SBPR.Niflheim.HomesteadStones.Application.Accounts
{
    // IAP-003 Tracer 1 — the account+credential admission application (engine-free CLEAN core).
    //
    // This owns the first-bind vertical slice (contracts.md "ResolveOrCreatePilotAccount",
    // "ProvisionPilotAllowlistEntry"): HMAC a transient verified subject, validate the HMAC-only
    // disclosure-aware allowlist, resolve an existing account or atomically mint a new account +
    // credential binding in ONE terminal transaction, replay idempotently, and re-key previous-key
    // records in place under the active key without changing AccountId. It performs NO name-merge and
    // creates a separate account for an unknown credential only after allowlist validation
    // (AIP-FR-008). It never persists a raw provider subject; the subject terminates here as an HMAC.
    //
    // net48 audit: System.* + LINQ only. No UnityEngine/Valheim/BepInEx.

    public enum AccountAdmissionOutcome { Created, Resolved, Replayed, Rejected }

    /// <summary>Stable rejection subset used by first-bind (contracts.md "Stable rejection vocabulary").</summary>
    public enum AccountRejectionCode
    {
        None = 0,
        NotAllowlisted,
        LookupKeyUnavailable,
        AccountDisabled,
        AccountDeletionPending,
        AccountDeleted,
        AccountQuarantined,
        OperationConflict,
        DisclosureIncomplete,
        StoreUnavailable,
        ProviderSubjectInvalid
    }

    /// <summary>Result of an admission attempt (contracts.md PilotAccountResolution).</summary>
    public sealed class PilotAccountResolution
    {
        public AccountAdmissionOutcome Outcome { get; }
        public AccountRejectionCode RejectionCode { get; }
        public PilotAccountId AccountId { get; }
        public CredentialBindingId CredentialBindingId { get; }
        public long AccountRevision { get; }
        public string ResultCode { get; }

        public PilotAccountResolution(AccountAdmissionOutcome outcome, AccountRejectionCode rejection,
            PilotAccountId accountId, CredentialBindingId credentialBindingId, long accountRevision, string resultCode)
        {
            Outcome = outcome;
            RejectionCode = rejection;
            AccountId = accountId;
            CredentialBindingId = credentialBindingId;
            AccountRevision = accountRevision;
            ResultCode = resultCode;
        }

        public bool Accepted => Outcome != AccountAdmissionOutcome.Rejected;

        internal static PilotAccountResolution Reject(AccountRejectionCode code) =>
            new PilotAccountResolution(AccountAdmissionOutcome.Rejected, code, default, default, 0, code.ToString());
    }

    /// <summary>The atomic account+credential admission service. Constructs over a boot-rehydrated store
    /// and the configured key ring. Admission-facing methods assume boot replay already completed
    /// (AT-AIP-BOOT-BEFORE-ADMISSION is enforced by the composition ordering: the store rehydrates in its
    /// constructor before this service is handed to the ingress).</summary>
    public sealed class PilotAccountService
    {
        private readonly PilotAccountStore _store;
        private readonly LookupKeyRing _keyRing;
        private readonly string _requiredNoticeVersion;
        private readonly string _retentionPolicyVersion;

        // Serializes the resolve→commit critical section so two first joins racing for one credential
        // cannot both mint an account: one wins and commits, the loser observes the now-present binding
        // and resolves it (AT-AIP-FIRST-BIND-RACE; edge case "Two first joins race for one credential").
        private readonly object _admissionGate = new object();

        public PilotAccountService(PilotAccountStore store, LookupKeyRing keyRing,
            string requiredNoticeVersion, string retentionPolicyVersion)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _keyRing = keyRing ?? throw new ArgumentNullException(nameof(keyRing));
            _requiredNoticeVersion = requiredNoticeVersion ?? string.Empty;
            _retentionPolicyVersion = retentionPolicyVersion ?? string.Empty;
        }

        public PilotAccountStore Store => _store;

        // ---- Allowlist provisioning (contracts.md ProvisionPilotAllowlistEntry) ----

        /// <summary>Provision one HMAC-only allowlist entry from a transient raw subject. The raw subject
        /// is HMAC'd immediately and never persisted (AT-AIP-ALLOWLIST-HMAC-ONLY). Requires the completed
        /// disclosure (human-approved basis) so an entry cannot be created without a valid enrollment
        /// basis. Idempotent on operationId.</summary>
        public AllowlistEntryId ProvisionAllowlistEntry(
            string operationId, string providerNamespace, string backendIssuer, string rawSubject,
            PilotDisclosure disclosure, DisclosureAcknowledgement acknowledgement, long occurredAt,
            IAccountCrashInjector? crash = null)
        {
            if (disclosure == null) throw new ArgumentNullException(nameof(disclosure));
            if (!disclosure.IsComplete())
                throw new InvalidOperationException("Disclosure incomplete: cannot provision an allowlist entry without a valid, human-approved-basis enrollment basis.");
            if (!acknowledgement.Satisfies(_requiredNoticeVersion))
                throw new InvalidOperationException("Allowlist provisioning requires acknowledgement of the required notice version.");
            if (string.IsNullOrEmpty(rawSubject))
                throw new ArgumentException("Raw subject must be non-empty.", nameof(rawSubject));

            // Idempotent replay: same op returns the recorded allowlist id.
            if (_store.TryGetCommittedOp(operationId, out _, out _, out var result) && result.StartsWith("allow:", StringComparison.Ordinal))
                return new AllowlistEntryId(result.Substring("allow:".Length));

            var hmac = _keyRing.CredentialHmacActive(providerNamespace, backendIssuer, rawSubject);
            var entryId = OpaqueIdMint.NewAllowlistEntryId();

            var change = new JournalChange("allow")
                .Set("allowlistEntryId", entryId.Value)
                .Set("providerNs", providerNamespace)
                .Set("backendIssuer", backendIssuer)
                .Set("hmac", hmac.Hex)
                .Set("keyVersion", hmac.KeyVersion.Value)
                .Set("status", AllowlistStatus.Active.ToString())
                .Set("revision", "1")
                .Set("noticeVersion", acknowledgement.NoticeVersion)
                .Set("noticeAckAt", acknowledgement.AcknowledgedAtUnixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));

            string binding = PilotAccountStore.Digest("allow|" + hmac.KeyVersion.Value + "|" + hmac.Hex);
            _store.Commit(operationId, "txn-" + entryId.Value, binding, PilotAccountStore.Digest(operationId),
                "allow:" + entryId.Value, occurredAt, new[] { change }, crash);
            return entryId;
        }

        /// <summary>Revoke one HMAC-only allowlist entry by its opaque id (contracts.md
        /// RevokePilotAllowlistEntry). Allowlist-only: this touches NO account/credential lifecycle, so it
        /// is safe for the local service-owner bootstrap path. Idempotent on operationId; a revoke of an
        /// already-revoked/absent entry is a no-op returning false. Requires no raw subject/HMAC selector.</summary>
        public bool RevokeAllowlistEntry(string operationId, AllowlistEntryId entryId, long occurredAt,
            IAccountCrashInjector? crash = null)
        {
            if (_store.TryGetCommittedOp(operationId, out _, out _, out var result) &&
                result.StartsWith("revoke:", StringComparison.Ordinal))
                return true;

            if (!_store.TryGetAllowlistEntry(entryId, out var entry)) return false;
            if (entry.Status != AllowlistStatus.Active) return false;

            var change = new JournalChange("allow-status")
                .Set("allowlistEntryId", entryId.Value)
                .Set("status", AllowlistStatus.Revoked.ToString())
                .Set("revision", (entry.Revision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            _store.Commit(operationId, "txn-revoke-" + entryId.Value,
                PilotAccountStore.Digest("revoke|" + entryId.Value),
                PilotAccountStore.Digest(operationId), "revoke:" + entryId.Value, occurredAt,
                new[] { change }, crash);
            return true;
        }

        // ---- Account resolution / first bind (contracts.md ResolveOrCreatePilotAccount) ----

        /// <summary>Resolve an existing account for the verified subject, or mint a new account +
        /// credential atomically. The raw subject is HMAC'd here and discarded; only the HMAC persists.</summary>
        public PilotAccountResolution ResolveOrCreateAccount(
            string operationId, VerifiedProviderPrincipal principal, long occurredAt,
            IAccountCrashInjector? crash = null)
        {
            if (!principal.IsResolved) return PilotAccountResolution.Reject(AccountRejectionCode.ProviderSubjectInvalid);

            lock (_admissionGate)
            {
                return ResolveOrCreateAccountLocked(operationId, principal, occurredAt, crash);
            }
        }

        private PilotAccountResolution ResolveOrCreateAccountLocked(
            string operationId, VerifiedProviderPrincipal principal, long occurredAt,
            IAccountCrashInjector? crash)
        {
            string providerNs = principal.ProviderKey.Namespace;
            string backendIssuer = principal.ProviderKey.BackendIssuer;
            string subject = principal.CanonicalSubject;

            // Idempotent replay of a committed first-bind operation.
            if (_store.TryGetCommittedOp(operationId, out var recBinding, out _, out var recResult))
            {
                if (recResult.StartsWith("account:", StringComparison.Ordinal))
                {
                    var parts = recResult.Substring("account:".Length).Split('/');
                    var acctId = new PilotAccountId(parts[0]);
                    var credId = parts.Length > 1 ? new CredentialBindingId(parts[1]) : default;
                    // Guard: a conflicting binding under the same operationId is a conflict.
                    string expectBinding = BindingFor(providerNs, backendIssuer, subject);
                    if (!string.Equals(recBinding, expectBinding, StringComparison.Ordinal))
                        return PilotAccountResolution.Reject(AccountRejectionCode.OperationConflict);
                    long rev = _store.TryGetAccount(acctId, out var acct) ? acct.Revision : 0;
                    return new PilotAccountResolution(AccountAdmissionOutcome.Replayed, AccountRejectionCode.None,
                        acctId, credId, rev, "Replayed");
                }
                // The op id was used for a DIFFERENT kind of mutation → conflict.
                return PilotAccountResolution.Reject(AccountRejectionCode.OperationConflict);
            }

            // Compute active-key HMAC; fail closed if the key ring cannot serve the active version.
            SubjectLookupHmac activeHmac;
            try { activeHmac = _keyRing.CredentialHmacActive(providerNs, backendIssuer, subject); }
            catch (LookupKeyUnavailableException) { return PilotAccountResolution.Reject(AccountRejectionCode.LookupKeyUnavailable); }

            // 1) Existing active binding under the active key resolves the account directly.
            if (_store.TryLookupCredential(activeHmac, providerNs, backendIssuer, out var existing))
                return ResolveExistingOrReject(existing);

            // 2) Existing binding under the configured previous key → resolve and lazily re-key in place.
            if (_keyRing.HasPrevious)
            {
                var prevHmac = _keyRing.CredentialHmacUnder(_keyRing.PreviousVersion, providerNs, backendIssuer, subject);
                if (_store.TryLookupCredential(prevHmac, providerNs, backendIssuer, out var prevCred))
                {
                    var reject = ResolveExistingOrReject(prevCred);
                    if (!reject.Accepted) return reject;
                    ReKeyCredentialInPlace(operationId + "#rekey", prevCred, activeHmac, occurredAt, crash);
                    long rev2 = _store.TryGetAccount(prevCred.AccountId, out var a2) ? a2.Revision : 0;
                    return new PilotAccountResolution(AccountAdmissionOutcome.Resolved, AccountRejectionCode.None,
                        prevCred.AccountId, prevCred.CredentialBindingId, rev2, "ResolvedRekeyed");
                }
            }

            // 3) No active binding → this is a first authenticated join. Normal Niflheim admission
            //    AUTO-CREATES an opaque account here: there is NO pre-join allowlist and NO fabricated
            //    per-account disclosure acknowledgement (disclosure is server policy / out-of-band notice).
            //    We NEVER auto-merge on name/resemblance — a distinct subject always mints a distinct
            //    account (AIP-FR-008). Before minting we honor the wound-down re-admission barrier: if a
            //    still-present (revoked) credential for THIS subject exists, its owning account was
            //    disabled/deleted/quarantined and must reject rather than silently recreate. Only once the
            //    account's records are physically purged does a fresh join legitimately mint anew.
            if (_store.TryLookupAnyCredentialAccountStatus(activeHmac, providerNs, backendIssuer, out var barrierStatus))
                return PilotAccountResolution.Reject(BarrierRejection(barrierStatus));

            // Mint account + credential atomically in one committed transaction. The account carries NO
            // PilotAllowlistEntry linkage (the credential's allowlistEntryId is empty) and no notice
            // acknowledgement timestamp — first bind no longer depends on a provisioned enrollment record.
            var newAccountId = OpaqueIdMint.NewAccountId();
            var newCredId = OpaqueIdMint.NewCredentialBindingId();
            var changes = new List<JournalChange>
            {
                new JournalChange("acct")
                    .Set("accountId", newAccountId.Value)
                    .Set("status", PilotAccountStatus.Active.ToString())
                    .Set("revision", "1")
                    .Set("noticeVersion", _requiredNoticeVersion)
                    .Set("noticeAckAt", "0")
                    .Set("retentionPolicyVersion", _retentionPolicyVersion),
                new JournalChange("cred")
                    .Set("credentialBindingId", newCredId.Value)
                    .Set("allowlistEntryId", string.Empty)
                    .Set("accountId", newAccountId.Value)
                    .Set("providerNs", providerNs)
                    .Set("backendIssuer", backendIssuer)
                    .Set("hmac", activeHmac.Hex)
                    .Set("keyVersion", activeHmac.KeyVersion.Value)
                    .Set("status", CredentialStatus.Active.ToString())
                    .Set("revision", "1"),
                new JournalChange("acct-add-cred")
                    .Set("accountId", newAccountId.Value)
                    .Set("credentialBindingId", newCredId.Value)
                    .Set("revision", "2"),
            };

            string bindingDigest = BindingFor(providerNs, backendIssuer, subject);
            _store.Commit(operationId, "txn-" + newAccountId.Value, bindingDigest, PilotAccountStore.Digest(operationId),
                "account:" + newAccountId.Value + "/" + newCredId.Value, occurredAt, changes, crash);

            long finalRev = _store.TryGetAccount(newAccountId, out var created) ? created.Revision : 2;
            return new PilotAccountResolution(AccountAdmissionOutcome.Created, AccountRejectionCode.None,
                newAccountId, newCredId, finalRev, "Created");
        }

        private PilotAccountResolution ResolveExistingOrReject(CredentialBindingProjection cred)
        {
            if (!_store.TryGetAccount(cred.AccountId, out var acct))
                return PilotAccountResolution.Reject(AccountRejectionCode.StoreUnavailable);
            switch (acct.Status)
            {
                case PilotAccountStatus.Disabled: return PilotAccountResolution.Reject(AccountRejectionCode.AccountDisabled);
                case PilotAccountStatus.DeletionPending: return PilotAccountResolution.Reject(AccountRejectionCode.AccountDeletionPending);
                case PilotAccountStatus.Deleted: return PilotAccountResolution.Reject(AccountRejectionCode.AccountDeleted);
                case PilotAccountStatus.Quarantined: return PilotAccountResolution.Reject(AccountRejectionCode.AccountQuarantined);
            }
            return new PilotAccountResolution(AccountAdmissionOutcome.Resolved, AccountRejectionCode.None,
                cred.AccountId, cred.CredentialBindingId, acct.Revision, "Resolved");
        }

        /// <summary>Re-key one credential binding in place under the active key: supersede the old index
        /// key, write the current HMAC/version, increment revision, RETAIN the same CredentialBindingId
        /// (AT-AIP-PREVIOUS-KEY-REKEY). No superseded credential record is created; the record is revised.</summary>
        private void ReKeyCredentialInPlace(string operationId, CredentialBindingProjection cred,
            SubjectLookupHmac activeHmac, long occurredAt, IAccountCrashInjector? crash)
        {
            if (_store.TryGetCommittedOp(operationId, out _, out _, out _)) return; // idempotent

            var changes = new List<JournalChange>
            {
                new JournalChange("cred")
                    .Set("credentialBindingId", cred.CredentialBindingId.Value)
                    .Set("allowlistEntryId", cred.AllowlistEntryId.Value)
                    .Set("accountId", cred.AccountId.Value)
                    .Set("providerNs", cred.ProviderNamespace)
                    .Set("backendIssuer", cred.BackendIssuer)
                    .Set("hmac", activeHmac.Hex)
                    .Set("keyVersion", activeHmac.KeyVersion.Value)
                    .Set("status", CredentialStatus.Active.ToString())
                    .Set("revision", (cred.Revision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            };

            // Lazy re-key also revises the linked allowlist record so no live entry lingers on the
            // retiring key version (data-model.md "Rotate/retire lookup key": lazy use re-keys
            // credential/allowlist records). If the linked allowlist is still on a non-active version,
            // supersede it and create a current-key replacement carrying the same notice provenance, and
            // relink the credential to the new entry — all in the SAME terminal transaction.
            if (_store.TryGetAllowlistEntry(cred.AllowlistEntryId, out var allow) &&
                !allow.Hmac.KeyVersion.Equals(activeHmac.KeyVersion) &&
                allow.Status == AllowlistStatus.Active)
            {
                // The allowlist HMAC over (provider,backend,subject) in the credential domain is the
                // identical value as the credential's active-key HMAC, so the allowlist re-key reuses
                // activeHmac directly — we never need the raw subject on reconnect.
                var newAllowId = OpaqueIdMint.NewAllowlistEntryId();
                changes[0].Set("allowlistEntryId", newAllowId.Value); // relink credential to the new entry
                changes.Add(new JournalChange("allow")
                    .Set("allowlistEntryId", newAllowId.Value)
                    .Set("providerNs", cred.ProviderNamespace).Set("backendIssuer", cred.BackendIssuer)
                    .Set("hmac", activeHmac.Hex).Set("keyVersion", activeHmac.KeyVersion.Value)
                    .Set("status", AllowlistStatus.Active.ToString()).Set("revision", "1")
                    .Set("noticeVersion", allow.NoticeVersion)
                    .Set("noticeAckAt", allow.NoticeAcknowledgedAt.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                changes.Add(new JournalChange("allow-status")
                    .Set("allowlistEntryId", allow.AllowlistEntryId.Value)
                    .Set("status", AllowlistStatus.Superseded.ToString())
                    .Set("revision", (allow.Revision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Set("linkedAllowlistEntryId", newAllowId.Value));
            }

            _store.Commit(operationId, "txn-rekey-" + cred.CredentialBindingId.Value,
                PilotAccountStore.Digest("rekey|" + cred.CredentialBindingId.Value + "|" + activeHmac.Hex),
                PilotAccountStore.Digest(operationId), "rekey:" + cred.CredentialBindingId.Value, occurredAt,
                changes, crash);
        }

        /// <summary>Map a wound-down owning account's status to the stable rejection code that first-bind
        /// returns instead of minting a fresh account. The account is still present but not admissible;
        /// only physical purge removes the barrier.</summary>
        private static AccountRejectionCode BarrierRejection(PilotAccountStatus status)
        {
            switch (status)
            {
                case PilotAccountStatus.Disabled: return AccountRejectionCode.AccountDisabled;
                case PilotAccountStatus.DeletionPending: return AccountRejectionCode.AccountDeletionPending;
                case PilotAccountStatus.Deleted: return AccountRejectionCode.AccountDeleted;
                case PilotAccountStatus.Quarantined: return AccountRejectionCode.AccountQuarantined;
                // An Active owning account with no ACTIVE binding for this subject is a conflicting durable
                // state (its credential should have resolved in step 1). Fail closed as a conflict rather
                // than mint a duplicate account for the same subject.
                default: return AccountRejectionCode.OperationConflict;
            }
        }

        private string BindingFor(string providerNs, string backendIssuer, string subject)
        {
            // Binding digest is over the versioned HMAC (never the raw subject), plus provider identity.
            var hmac = _keyRing.CredentialHmacActive(providerNs, backendIssuer, subject);
            return PilotAccountStore.Digest("bind|" + providerNs + "|" + backendIssuer + "|" + hmac.KeyVersion.Value + "|" + hmac.Hex);
        }
    }
}
