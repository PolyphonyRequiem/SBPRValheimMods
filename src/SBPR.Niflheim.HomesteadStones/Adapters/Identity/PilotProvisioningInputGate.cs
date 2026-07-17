using System;
using System.Collections.Generic;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Identity
{
    // IAP-001 Gate 0 — the protected operator provisioning INPUT (engine-free CLEAN-side core).
    //
    // Gate 0 must identify a bounded, non-logging operator path to obtain/provision the exact HMAC-only
    // allowlist subject (AIP-FR-001, AIP-FR-019; contracts `ProvisionPilotAllowlistEntry`; research.md
    // "First-enrollment bootstrap authority"). This file proves the INPUT DISCIPLINE only — it does NOT
    // create an account, compute an allowlist HMAC, or mint any record (that is Tracer 1). It proves:
    //   * the raw subject is accepted only through protected no-echo stdin, NEVER a command-line argument
    //     and never an ordinary chat/log command;
    //   * the key/data path must be owner-only (fail closed when group/other can reach it — broader than
    //     0600 on Linux);
    //   * the utility exposes allowlist provision/revoke ONLY (no inspect/export/disable/delete/reset/
    //     gameplay), so it can never become a second account-admin API;
    //   * every echoed line of its output is redacted of the raw subject.
    //
    // net48 audit: System.String / System.Collections.Generic only. No UnityEngine / Valheim / BepInEx.

    /// <summary>Where a provisioning input claims to have come from. Only <see cref="ProtectedNoEchoStdin"/>
    /// is an admissible channel for the raw subject; every other channel is refused so a subject can never
    /// be captured by shell history, process listings, ordinary server logs, or chat.</summary>
    public enum ProvisioningInputChannel
    {
        /// <summary>Protected no-echo stdin under the server service account. The ONLY admissible channel.</summary>
        ProtectedNoEchoStdin = 0,
        /// <summary>A process command-line argument (visible in `ps`/shell history). REFUSED.</summary>
        CommandLineArgument,
        /// <summary>An in-game chat/console command that reaches ordinary logs. REFUSED.</summary>
        ChatOrConsoleCommand,
        /// <summary>An environment variable (inherited/leaked to children/logs). REFUSED.</summary>
        EnvironmentVariable
    }

    /// <summary>The bounded verb set the local bootstrap utility may perform. Allowlist provision/revoke
    /// only — never account lifecycle, read, retention, or gameplay (AIP-FR-019, contracts). Any other
    /// verb is rejected as out-of-scope so the local path cannot widen into an account-admin surface.</summary>
    public enum LocalBootstrapVerb
    {
        ProvisionAllowlistEntry = 0,
        RevokeAllowlistEntry,

        // ── Explicitly out of scope for the local utility (present only to prove they are refused) ──
        InspectAccount,
        ExportAccount,
        DisableAccount,
        DeleteAccount,
        ResetAccount,
        ChangeRetention,
        InvokeGameplayCommand
    }

    /// <summary>The result of evaluating a provisioning-input attempt. Never carries the raw subject.</summary>
    public readonly struct ProvisioningInputDecision
    {
        public ProvisioningInputDecision(bool accepted, string resultCode)
        {
            Accepted = accepted;
            ResultCode = resultCode ?? string.Empty;
        }

        public bool Accepted { get; }

        /// <summary>Stable, subject-free result/rejection code. Safe to log.</summary>
        public string ResultCode { get; }

        public static ProvisioningInputDecision Accept() => new ProvisioningInputDecision(true, "Accepted");
        public static ProvisioningInputDecision Reject(string code) => new ProvisioningInputDecision(false, code);
    }

    /// <summary>The OS-level protection state of the utility's key/data paths, supplied by the net48 host
    /// (which stats the real files). Owner-only is required; anything reachable by group/other fails
    /// closed. Modeled after a Unix mode's group/other bits so the rule is testable engine-free.</summary>
    public readonly struct PathOwnershipState
    {
        public PathOwnershipState(bool ownedByServiceAccount, bool groupReadable, bool groupWritable,
            bool otherReadable, bool otherWritable)
        {
            OwnedByServiceAccount = ownedByServiceAccount;
            GroupReadable = groupReadable;
            GroupWritable = groupWritable;
            OtherReadable = otherReadable;
            OtherWritable = otherWritable;
        }

        public bool OwnedByServiceAccount { get; }
        public bool GroupReadable { get; }
        public bool GroupWritable { get; }
        public bool OtherReadable { get; }
        public bool OtherWritable { get; }

        /// <summary>Owner-only (== 0600-or-tighter): owned by the service account and no group/other
        /// read/write bit set. Anything broader is refused.</summary>
        public bool IsOwnerOnly =>
            OwnedByServiceAccount && !GroupReadable && !GroupWritable && !OtherReadable && !OtherWritable;

        public static PathOwnershipState OwnerOnly(bool owned = true) =>
            new PathOwnershipState(owned, false, false, false, false);
    }

    /// <summary>The engine-free gate the local allowlist bootstrap utility defers to before it touches any
    /// raw subject. It enforces channel, path-ownership, and verb-scope discipline; it does NOT itself
    /// compute an HMAC or write a record. Proven by AT-AIP-PROVIDER-PROVISION-INPUT.</summary>
    public sealed class PilotProvisioningInputGate
    {
        /// <summary>Decide whether an allowlist provisioning attempt may proceed to (out-of-scope-here)
        /// HMAC computation. Rejects unless the raw subject arrives on protected no-echo stdin, the
        /// key/data path is owner-only, and the verb is within the local allowlist-only scope.</summary>
        public ProvisioningInputDecision EvaluateProvision(
            ProvisioningInputChannel channel,
            PathOwnershipState keyPath,
            LocalBootstrapVerb verb)
        {
            // Verb scope first: the local utility is allowlist-only.
            if (!IsLocalAllowlistVerb(verb))
                return ProvisioningInputDecision.Reject("VerbOutOfLocalScope");

            // Path must be owner-only or the utility fails closed (broader than 0600 on Linux).
            if (!keyPath.IsOwnerOnly)
                return ProvisioningInputDecision.Reject("KeyPathTooPermissive");

            // The raw subject may only arrive through protected no-echo stdin.
            if (channel != ProvisioningInputChannel.ProtectedNoEchoStdin)
                return ProvisioningInputDecision.Reject("SubjectChannelForbidden");

            return ProvisioningInputDecision.Accept();
        }

        /// <summary>True for the only two verbs the local bootstrap utility may perform.</summary>
        public static bool IsLocalAllowlistVerb(LocalBootstrapVerb verb) =>
            verb == LocalBootstrapVerb.ProvisionAllowlistEntry ||
            verb == LocalBootstrapVerb.RevokeAllowlistEntry;

        /// <summary>Redact any occurrence of a raw subject from a line the utility is about to echo/return,
        /// so its output carries only internal ids/receipts. Given the transient raw subject (memory only)
        /// and a candidate output line, replace it with a fixed marker. Defensive: the utility should
        /// never build the subject into output in the first place, but this guarantees it even on a bug.</summary>
        public static string RedactSubject(string outputLine, string rawSubject)
        {
            if (string.IsNullOrEmpty(outputLine)) return string.Empty;
            if (string.IsNullOrEmpty(rawSubject)) return outputLine;
            return outputLine.Replace(rawSubject, "<redacted-subject>");
        }

        /// <summary>The admissible-output allowlist for the utility: internal entry/receipt/correlation
        /// ids and stable result codes only. Used by tests to assert nothing else is emitted.</summary>
        public static IReadOnlyList<string> AdmissibleOutputFields => _admissibleOutputFields;

        private static readonly string[] _admissibleOutputFields =
        {
            "allowlistEntryId",
            "revision",
            "resultCode",
            "receiptId",
            "correlationId"
        };
    }
}
