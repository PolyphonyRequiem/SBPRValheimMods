using System;
using System.Collections.Generic;
using System.Globalization;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;

namespace SBPR.Niflheim.HomesteadStones.Application.Accounts
{
    // IAP-009 Operator foundation — the authenticated live-admin account lifecycle service (engine-free
    // CLEAN core). This owns inspect / disable / delete-drain over the verified account store
    // (contracts "Operator commands": GetPilotAccountSummary, DisablePilotAccount, DeletePilotAccount).
    //
    // Every entry point:
    //   1. authorizes through the live-admin gate (AIP-FR-019; payload never authority);
    //   2. for a mutation, acquires the per-account fence and DRAINS any in-flight mutation before the
    //      atomic commit (contracts "acquire the per-account mutation fence, wait for any already-committing
    //      transaction"); a failed drain leaves the account untouched and recoverable;
    //   3. commits the lifecycle change in ONE terminal transaction (torn tails quarantine on replay);
    //   4. deterministically server-closes the session AFTER the durable commit, so a delayed network
    //      close cannot reopen durable authority.
    //
    // INSPECT never emits a raw subject, HMAC, secret, or token — only internal ids + coarse status
    // (contracts GetPilotAccountSummary; AT-AIP-ADMIN-INSPECT). Disable closes admission+session
    // (AT-AIP-DISABLE-CLOSES-SESSION); the fence is the mutation drain barrier (AT-AIP-MUTATION-FENCE);
    // delete additionally revokes every linked credential + allowlist entry so a stale allowlist cannot
    // immediately recreate the account (AT-AIP-DELETE-DRAIN-BARRIER, contracts DeletePilotAccount).
    //
    // net48 audit: System.* only. No UnityEngine / Valheim / BepInEx.

    /// <summary>Bounded, operator-safe projection of one account. Carries NO raw subject, HMAC, secret,
    /// token, or unrelated account (contracts "PilotAccountSummary": no raw subject/HMAC/secret).</summary>
    public sealed class PilotAccountSummary
    {
        public string AccountId { get; internal set; } = string.Empty;
        public string Status { get; internal set; } = string.Empty;
        public long Revision { get; internal set; }
        public int CredentialCount { get; internal set; }
        /// <summary>Provider CLASS (namespace) per credential — never a subject/HMAC. Deduped, ordered.</summary>
        public IReadOnlyList<string> CredentialClasses { get; internal set; } = Array.Empty<string>();
        public string NoticeVersion { get; internal set; } = string.Empty;
        public string RetentionPolicyVersion { get; internal set; } = string.Empty;
        public bool HasLiveSession { get; internal set; }
    }

    public enum OperatorOutcome { Applied, Replayed, NoOp, Rejected }

    /// <summary>Result of an operator lifecycle command. Subject-free; safe to log.</summary>
    public sealed class OperatorResult
    {
        public OperatorOutcome Outcome { get; }
        public string ResultCode { get; }
        public long CommittedRevision { get; }
        public bool SessionClosed { get; }
        public long ClosedTransportHandle { get; }
        public PilotAccountSummary? Summary { get; }

        public OperatorResult(OperatorOutcome outcome, string resultCode, long committedRevision,
            bool sessionClosed, long closedTransportHandle, PilotAccountSummary? summary)
        {
            Outcome = outcome;
            ResultCode = resultCode ?? string.Empty;
            CommittedRevision = committedRevision;
            SessionClosed = sessionClosed;
            ClosedTransportHandle = closedTransportHandle;
            Summary = summary;
        }

        public bool Accepted => Outcome != OperatorOutcome.Rejected;

        internal static OperatorResult Reject(string code) =>
            new OperatorResult(OperatorOutcome.Rejected, code, 0, false, 0, null);
    }

    public sealed class OperatorAccountService
    {
        private readonly PilotAccountStore _store;
        private readonly OperatorAdminGate _adminGate;
        private readonly AccountMutationFence _fence;
        private readonly PilotSessionRegistry _sessions;
        private readonly TimeSpan _drainTimeout;

        /// <param name="drainTimeout">Bounded wait for an in-flight mutation to finish before a lifecycle
        /// commit. On timeout the command aborts WITHOUT mutating (recoverable). Non-positive == wait
        /// indefinitely.</param>
        public OperatorAccountService(PilotAccountStore store, OperatorAdminGate adminGate,
            AccountMutationFence fence, PilotSessionRegistry sessions, TimeSpan drainTimeout)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _adminGate = adminGate ?? throw new ArgumentNullException(nameof(adminGate));
            _fence = fence ?? throw new ArgumentNullException(nameof(fence));
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _drainTimeout = drainTimeout;
        }

        // ---- Inspect (contracts GetPilotAccountSummary; AT-AIP-ADMIN-INSPECT) ----

        /// <summary>Return a bounded, subject-free summary for one internal AccountId. Admin-gated; a
        /// non-admin caller is rejected with no data leaked. Raw-subject lookup is deliberately absent.</summary>
        public OperatorResult Inspect(ServerObservedAdminContext operatorContext, PilotAccountId accountId)
        {
            if (!_adminGate.Authorize(operatorContext, out var reject))
                return OperatorResult.Reject(reject);
            if (!_store.TryGetAccount(accountId, out var acct))
                return OperatorResult.Reject("AccountNotFound");

            var summary = BuildSummary(acct);
            return new OperatorResult(OperatorOutcome.NoOp, "Inspected", acct.Revision, false, 0, summary);
        }

        private PilotAccountSummary BuildSummary(PilotAccountProjection acct)
        {
            var classes = new List<string>();
            foreach (var cid in acct.CredentialBindingIds)
            {
                if (_store.TryGetCredential(cid, out var cred) && cred.Status == CredentialStatus.Active)
                {
                    // Provider CLASS only — never the subject or HMAC.
                    if (!classes.Contains(cred.ProviderNamespace)) classes.Add(cred.ProviderNamespace);
                }
            }
            classes.Sort(StringComparer.Ordinal);
            return new PilotAccountSummary
            {
                AccountId = acct.AccountId.Value,
                Status = acct.Status.ToString(),
                Revision = acct.Revision,
                CredentialCount = acct.CredentialBindingIds.Count,
                CredentialClasses = classes,
                NoticeVersion = acct.NoticeVersion,
                RetentionPolicyVersion = acct.RetentionPolicyVersion,
                HasLiveSession = _sessions.HasSession(acct.AccountId.Value),
            };
        }

        // ---- Disable (contracts DisablePilotAccount; AT-AIP-ADMIN-DISABLE, AT-AIP-DISABLE-CLOSES-SESSION,
        //      AT-AIP-MUTATION-FENCE) ----

        /// <summary>Admin-gated Active -> Disabled. Acquires the per-account fence (draining any in-flight
        /// mutation), atomically commits the status change, THEN server-closes the session. Idempotent: a
        /// second disable of an already-disabled account replays as NoOp. A failed drain aborts with no
        /// mutation (recoverable).</summary>
        public OperatorResult Disable(ServerObservedAdminContext operatorContext, PilotAccountId accountId,
            string operationId, long occurredAt, IAccountCrashInjector? crash = null)
        {
            if (!_adminGate.Authorize(operatorContext, out var reject))
                return OperatorResult.Reject(reject);
            if (!_store.TryGetAccount(accountId, out var acct))
                return OperatorResult.Reject("AccountNotFound");

            // Idempotent replay: an already-committed disable op returns its recorded result.
            if (_store.TryGetCommittedOp(operationId, out _, out _, out var recResult) &&
                recResult.StartsWith("disable:", StringComparison.Ordinal))
            {
                long rev = _store.TryGetAccount(accountId, out var a2) ? a2.Revision : acct.Revision;
                return new OperatorResult(OperatorOutcome.Replayed, "Replayed", rev, false, 0, null);
            }

            if (acct.Status == PilotAccountStatus.Disabled)
            {
                var close0 = _sessions.CloseForAccount(accountId.Value);
                return new OperatorResult(OperatorOutcome.NoOp, "AlreadyDisabled", acct.Revision, close0.Closed, close0.TransportHandle, null);
            }
            if (acct.Status == PilotAccountStatus.Deleted || acct.Status == PilotAccountStatus.DeletionPending)
                return OperatorResult.Reject("AccountDeletionPending");

            if (!_fence.TryAcquireForLifecycle(accountId.Value, _drainTimeout, out var lease))
                return OperatorResult.Reject("DrainTimeout"); // no mutation; account stays Active (recoverable)

            long committedRevision;
            using (lease)
            {
                // Re-read under the fence so a mutation that committed during the drain is reflected.
                _store.TryGetAccount(accountId, out var fenced);
                long newRev = fenced.Revision + 1;
                var change = new JournalChange("acct-status")
                    .Set("accountId", accountId.Value)
                    .Set("status", PilotAccountStatus.Disabled.ToString())
                    .Set("revision", newRev.ToString(CultureInfo.InvariantCulture));
                _store.Commit(operationId, "txn-disable-" + accountId.Value,
                    PilotAccountStore.Digest("disable|" + accountId.Value + "|" + newRev),
                    PilotAccountStore.Digest(operationId), "disable:" + accountId.Value, occurredAt,
                    new[] { change }, crash);
                committedRevision = newRev;
            }

            // Deterministic session close AFTER the durable commit, so a delayed network close cannot
            // reopen durable authority — the account is already Disabled on disk.
            var closed = _sessions.CloseForAccount(accountId.Value);
            return new OperatorResult(OperatorOutcome.Applied, "Disabled", committedRevision, closed.Closed, closed.TransportHandle, null);
        }

        // ---- Delete + drain barrier (contracts DeletePilotAccount; AT-AIP-DELETE-DRAIN-BARRIER) ----

        /// <summary>Admin-gated deletion: acquire the per-account fence, drain any in-flight mutation, then
        /// ONE terminal transaction commits DeletionPending, revokes every linked credential AND allowlist
        /// entry (so a stale allowlist cannot immediately recreate the account), and closes future
        /// admission. Then server-close the session. A failed drain aborts with no mutation. Idempotent.</summary>
        public OperatorResult Delete(ServerObservedAdminContext operatorContext, PilotAccountId accountId,
            string operationId, long occurredAt, IAccountCrashInjector? crash = null)
        {
            if (!_adminGate.Authorize(operatorContext, out var reject))
                return OperatorResult.Reject(reject);
            if (!_store.TryGetAccount(accountId, out var acct))
                return OperatorResult.Reject("AccountNotFound");

            if (_store.TryGetCommittedOp(operationId, out _, out _, out var recResult) &&
                recResult.StartsWith("delete:", StringComparison.Ordinal))
            {
                long rev = _store.TryGetAccount(accountId, out var a2) ? a2.Revision : acct.Revision;
                return new OperatorResult(OperatorOutcome.Replayed, "Replayed", rev, false, 0, null);
            }

            if (acct.Status == PilotAccountStatus.Deleted || acct.Status == PilotAccountStatus.DeletionPending)
            {
                var close0 = _sessions.CloseForAccount(accountId.Value);
                return new OperatorResult(OperatorOutcome.NoOp, "AlreadyClosing", acct.Revision, close0.Closed, close0.TransportHandle, null);
            }

            if (!_fence.TryAcquireForLifecycle(accountId.Value, _drainTimeout, out var lease))
                return OperatorResult.Reject("DrainTimeout"); // no mutation; account intact (recoverable)

            long committedRevision;
            using (lease)
            {
                _store.TryGetAccount(accountId, out var fenced);
                long newRev = fenced.Revision + 1;
                var changes = new List<JournalChange>
                {
                    new JournalChange("acct-status")
                        .Set("accountId", accountId.Value)
                        .Set("status", PilotAccountStatus.DeletionPending.ToString())
                        .Set("revision", newRev.ToString(CultureInfo.InvariantCulture)),
                };

                // Revoke every linked credential and its allowlist entry in the SAME transaction, so the
                // revoked allowlist prevents immediate account recreation (contracts DeletePilotAccount).
                foreach (var cid in fenced.CredentialBindingIds)
                {
                    if (!_store.TryGetCredential(cid, out var cred)) continue;
                    if (cred.Status == CredentialStatus.Active)
                        changes.Add(new JournalChange("cred-status")
                            .Set("credentialBindingId", cid.Value)
                            .Set("status", CredentialStatus.Revoked.ToString())
                            .Set("revision", (cred.Revision + 1).ToString(CultureInfo.InvariantCulture)));
                    if (_store.TryGetAllowlistEntry(cred.AllowlistEntryId, out var allow) &&
                        allow.Status == AllowlistStatus.Active)
                        changes.Add(new JournalChange("allow-status")
                            .Set("allowlistEntryId", allow.AllowlistEntryId.Value)
                            .Set("status", AllowlistStatus.Revoked.ToString())
                            .Set("revision", (allow.Revision + 1).ToString(CultureInfo.InvariantCulture)));
                }

                _store.Commit(operationId, "txn-delete-" + accountId.Value,
                    PilotAccountStore.Digest("delete|" + accountId.Value + "|" + newRev),
                    PilotAccountStore.Digest(operationId), "delete:" + accountId.Value, occurredAt,
                    changes, crash);
                committedRevision = newRev;
            }

            var closed = _sessions.CloseForAccount(accountId.Value);
            return new OperatorResult(OperatorOutcome.Applied, "DeletionPending", committedRevision, closed.Closed, closed.TransportHandle, null);
        }
    }
}
