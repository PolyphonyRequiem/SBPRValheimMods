using System;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;

namespace SBPR.Niflheim.HomesteadStones.Features.PilotIdentity
{
    // IAP-009 Operator foundation — the OS-scoped local allowlist bootstrap utility CORE (engine-free
    // CLEAN side). This is the decision core the net48 host CLI (tools/niflheim-account-bootstrap) defers
    // to; the CLI does I/O (stat the key path, read no-echo stdin, print redacted output), this core owns
    // the authorization boundary and the allowlist-only effect.
    //
    // BOUNDARY (AIP-FR-019; contracts ProvisionPilotAllowlistEntry local path):
    //   * Authority is OS ownership of the key/data path, NOT any Valheim admin identity and NOT any
    //     network/gameplay payload — this is a SERVER-HOST-LOCAL utility. The `PathOwnershipState` the
    //     host stats must be owner-only (broader than 0600 fails closed).
    //   * The raw subject arrives ONLY through protected no-echo stdin (never argv/env/chat). The gate
    //     refuses every other channel BEFORE the subject is ever HMAC'd.
    //   * The utility can perform allowlist provision/revoke ONLY. Any account inspect/export/disable/
    //     delete/reset/retention/gameplay verb is rejected as out-of-local-scope, so this path can NEVER
    //     become a second account-admin API or a remote/admin backdoor.
    //   * Output is redacted of the raw subject; only internal ids/result codes are returned.
    //
    // The subject is consumed by value and never stored on this object; the caller is responsible for not
    // retaining it. This core computes the HMAC via PilotAccountService (which discards the subject).
    //
    // net48 audit: System.* + the engine-free gate/service. No UnityEngine / Valheim / BepInEx.

    /// <summary>The bounded result of a local bootstrap operation. Subject-free and safe to print/log.</summary>
    public sealed class LocalBootstrapResult
    {
        public bool Accepted { get; }
        public string ResultCode { get; }
        /// <summary>The provisioned/affected allowlist entry id, when applicable. Opaque internal id only.</summary>
        public string AllowlistEntryId { get; }

        private LocalBootstrapResult(bool accepted, string resultCode, string allowlistEntryId)
        {
            Accepted = accepted;
            ResultCode = resultCode ?? string.Empty;
            AllowlistEntryId = allowlistEntryId ?? string.Empty;
        }

        internal static LocalBootstrapResult Ok(string resultCode, string entryId) =>
            new LocalBootstrapResult(true, resultCode, entryId);
        internal static LocalBootstrapResult Reject(string resultCode) =>
            new LocalBootstrapResult(false, resultCode, string.Empty);

        /// <summary>The single admissible output line for this result — internal ids + result code only,
        /// never a subject. The host prints exactly this (optionally passed through RedactSubject as a
        /// belt-and-suspenders pass against the transient subject).</summary>
        public string ToOutputLine() =>
            "resultCode=" + ResultCode +
            (string.IsNullOrEmpty(AllowlistEntryId) ? string.Empty : " allowlistEntryId=" + AllowlistEntryId);
    }

    public sealed class LocalAllowlistBootstrap
    {
        private readonly PilotProvisioningInputGate _inputGate;
        private readonly PilotAccountService _accounts;

        public LocalAllowlistBootstrap(PilotProvisioningInputGate inputGate, PilotAccountService accounts)
        {
            _inputGate = inputGate ?? throw new ArgumentNullException(nameof(inputGate));
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        }

        /// <summary>Provision one allowlist entry from a transient raw subject read on protected no-echo
        /// stdin. Fails closed unless the channel is protected stdin, the key path is owner-only, and the
        /// verb is allowlist-scoped. The subject is HMAC'd inside the service and never persisted/echoed.</summary>
        public LocalBootstrapResult Provision(
            ProvisioningInputChannel channel, PathOwnershipState keyPath,
            string operationId, string providerNamespace, string backendIssuer, string rawSubject,
            PilotDisclosure disclosure, DisclosureAcknowledgement acknowledgement, long occurredAt,
            IAccountCrashInjector? crash = null)
        {
            var decision = _inputGate.EvaluateProvision(channel, keyPath, LocalBootstrapVerb.ProvisionAllowlistEntry);
            if (!decision.Accepted) return LocalBootstrapResult.Reject(decision.ResultCode);

            try
            {
                var entryId = _accounts.ProvisionAllowlistEntry(operationId, providerNamespace, backendIssuer,
                    rawSubject, disclosure, acknowledgement, occurredAt, crash);
                return LocalBootstrapResult.Ok("Provisioned", entryId.Value);
            }
            catch (InvalidOperationException)
            {
                // Disclosure/acknowledgement gate rejected — surface a stable, subject-free code.
                return LocalBootstrapResult.Reject("DisclosureIncomplete");
            }
            catch (ArgumentException)
            {
                return LocalBootstrapResult.Reject("ProviderSubjectInvalid");
            }
        }

        /// <summary>Revoke one allowlist entry by its opaque id. No raw subject needed — the local path
        /// operates on internal ids only for revoke. Same OS-ownership + verb-scope boundary as provision.</summary>
        public LocalBootstrapResult Revoke(
            PathOwnershipState keyPath, string operationId, AllowlistEntryId entryId, long occurredAt,
            IAccountCrashInjector? crash = null)
        {
            // Revoke carries no raw subject, so the channel is not subject-bearing; still enforce path
            // ownership + verb scope through the gate (channel is the non-subject internal-id path).
            var decision = _inputGate.EvaluateProvision(
                ProvisioningInputChannel.ProtectedNoEchoStdin, keyPath, LocalBootstrapVerb.RevokeAllowlistEntry);
            if (!decision.Accepted) return LocalBootstrapResult.Reject(decision.ResultCode);

            bool ok = _accounts.RevokeAllowlistEntry(operationId, entryId, occurredAt, crash);
            return ok
                ? LocalBootstrapResult.Ok("Revoked", entryId.Value)
                : LocalBootstrapResult.Reject("AllowlistEntryNotActive");
        }

        /// <summary>Prove the local utility can never perform an account-lifecycle verb: any non-allowlist
        /// verb is rejected by the input gate as out-of-local-scope BEFORE any effect. Exposed so the host
        /// and tests can assert the utility is not a second admin API (AT-AIP-LOCAL-BOOTSTRAP-SCOPE).</summary>
        public LocalBootstrapResult RejectOutOfScope(LocalBootstrapVerb verb, PathOwnershipState keyPath)
        {
            var decision = _inputGate.EvaluateProvision(ProvisioningInputChannel.ProtectedNoEchoStdin, keyPath, verb);
            return decision.Accepted
                ? LocalBootstrapResult.Reject("UnexpectedlyAccepted") // never for a non-allowlist verb
                : LocalBootstrapResult.Reject(decision.ResultCode);
        }
    }
}
