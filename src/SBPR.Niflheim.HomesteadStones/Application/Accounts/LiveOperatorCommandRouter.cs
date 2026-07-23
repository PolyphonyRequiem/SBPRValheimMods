using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;

namespace SBPR.Niflheim.HomesteadStones.Application.Accounts
{
    // IAP-015 — the engine-free LIVE operator command router (CLEAN core). This is the brain the net48
    // direct per-peer ZRpc ingress delegates to: it turns ONE bounded, authenticated wire request into a
    // shipped operator lifecycle mutation over the EXACT live services (OperatorAccountService,
    // PilotPrivacyService, PilotDestructionService) and the SAME durable PilotAccountStore /
    // PilotSessionRegistry / AccountMutationFence the live admission path uses. It exposes only the bounded
    // verbs the accepted IAP-015 journey + runbook require, and it fails closed on non-admin,
    // unauthenticated, stale-session, malformed, oversized, replay-conflict, and unknown-verb requests
    // WITHOUT any mutation or data leakage.
    //
    // Authority model (AIP-FR-019): the ONLY thing that can authorize a command is the
    // ServerObservedAdminContext the net48 layer derived from the ACTUAL delivering, transport-authenticated
    // ZNetPeer against the server-owned adminlist.txt (via OperatorAdminGate). The client wire payload is
    // NEVER authority — it carries only a verb, an opaque internal selector, an operation id, and a
    // correlation id. There is no code path here that reads an admin claim out of the payload.
    //
    // Output scrub (contracts §Operator commands): every response carries ONLY opaque internal ids, coarse
    // statuses, result codes, receipt/correlation ids, and safe counts. A raw provider subject, HMAC,
    // token, secret, unrestricted path, or another account's data can never appear in a response.
    //
    // Explicitly NOT exposed by this live client surface (task point 4): whole-fixture reset
    // (FullPilotReset), arbitrary scoped reset (ResetScoped), raw provider-subject lookup/input, journal
    // editing, quarantine, and any non-QA destructive shortcut. Those remain operator-console/host-only.
    //
    // net48 audit: System.* + engine-free account cores only. No UnityEngine/Valheim/BepInEx, so it
    // link-compiles into the net8 test project and every branch is unit-tested against fakes.
    public sealed class LiveOperatorCommandRouter
    {
        // Wire bounds (fail-closed): a request longer than this, with more args, or a too-long arg, is
        // rejected as MalformedRequest before any service is touched.
        internal const int MaxWireLength = 512;
        internal const int MaxArgs = 6;
        internal const int MaxArgLength = 128;
        internal const string WireVersion = "v1";

        private readonly LiveOperatorServices _services;
        private readonly IServerPeerCloser _peerCloser;

        public LiveOperatorCommandRouter(LiveOperatorServices services, IServerPeerCloser peerCloser)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _peerCloser = peerCloser ?? throw new ArgumentNullException(nameof(peerCloser));
        }

        /// <summary>Handle ONE authenticated operator request. <paramref name="operatorContext"/> is the
        /// server-observed authority derived from the delivering peer (NEVER the payload).
        /// <paramref name="wire"/> is the bounded client request string. All authority, replay, and scrub
        /// decisions happen here; a rejection performs no mutation and leaks no data.</summary>
        public OperatorWireResponse Handle(ServerObservedAdminContext operatorContext, string? wire, long occurredAt)
        {
            // 0. Parse + bound the request (fail closed on malformed/oversized). Recover the correlation id
            //    when possible so the caller can still match the rejection to its request.
            if (!OperatorWireRequest.TryParse(wire, out var req, out string correlationId))
                return OperatorWireResponse.Reject(correlationId, "unknown", "MalformedRequest");

            // 1. Single authority gate up front: a non-admin / unauthenticated caller is rejected with a
            //    stable code and NOTHING is inspected, mutated, or leaked (AT-AIP-NONADMIN-REJECT). Every
            //    service also re-authorizes, but gating here guarantees no verb-specific work runs first.
            if (!_services.AdminGate.Authorize(operatorContext, out var authReject))
                return OperatorWireResponse.Reject(req.CorrelationId, req.VerbToken, authReject);

            try
            {
                switch (req.Verb)
                {
                    case OperatorVerb.OpenPilot: return OpenPilot(operatorContext, req, occurredAt);
                    case OperatorVerb.Inspect: return Inspect(operatorContext, req);
                    case OperatorVerb.Export: return Export(operatorContext, req, occurredAt);
                    case OperatorVerb.Disable: return DisableOrDelete(operatorContext, req, occurredAt, delete: false);
                    case OperatorVerb.Delete: return DisableOrDelete(operatorContext, req, occurredAt, delete: true);
                    case OperatorVerb.Purge: return Purge(operatorContext, req, occurredAt);
                    case OperatorVerb.RetentionPurge: return RetentionPurge(operatorContext, req, occurredAt);
                    case OperatorVerb.ClosePilot: return ClosePilot(operatorContext, req, occurredAt);
                    default: return OperatorWireResponse.Reject(req.CorrelationId, req.VerbToken, "UnknownVerb");
                }
            }
            catch (PrivacyOperationException pex)
            {
                // Stable, subject-free rejection code from the privacy/destruction core; no data leaks.
                return OperatorWireResponse.Reject(req.CorrelationId, req.VerbToken, pex.Code.ToString());
            }
            catch (Exception)
            {
                // Never surface an exception message (could carry a path/detail). Fail closed, no mutation
                // beyond whatever the atomic service already committed.
                return OperatorWireResponse.Reject(req.CorrelationId, req.VerbToken, "InternalError");
            }
        }

        // ---- open/configure pilot for the current cataloged world fixture (runbook §pilot open) ----

        private OperatorWireResponse OpenPilot(ServerObservedAdminContext op, OperatorWireRequest req, long occurredAt)
        {
            if (string.IsNullOrEmpty(_services.WorldFixtureLocator))
                return OperatorWireResponse.Reject(req.CorrelationId, req.VerbToken, "WorldFixtureUncataloged");

            var policy = _services.RetentionPolicy;
            var pilotId = _services.Privacy.OpenPilot(op, req.OperationId + "#pilot", policy.PolicyVersion, occurredAt);
            // Catalog the current world-save fixture so the fail-closed admission gate can enforce it, then
            // bind the admission gate to (pilot, fixture). The artifact catalog is idempotent on op id.
            _services.Privacy.CatalogArtifact(op, req.OperationId + "#world", PilotArtifactType.WorldSave,
                _services.WorldFixtureLocator, policy, occurredAt);
            _services.Privacy.ConfigureAdmission(pilotId, _services.WorldFixtureLocator);

            var resp = OperatorWireResponse.Ok(req.CorrelationId, req.VerbToken, "PilotOpen");
            resp.Add("pilot", pilotId.Value);
            resp.Add("admits", _services.Privacy.EvaluateAdmission(occurredAt) == PrivacyRejectionCode.None ? "true" : "false");
            return resp;
        }

        // ---- inspect (safe projection) ----

        private OperatorWireResponse Inspect(ServerObservedAdminContext op, OperatorWireRequest req)
        {
            if (!req.TryGetAccountId(out var accountId))
                return OperatorWireResponse.Reject(req.CorrelationId, req.VerbToken, "MalformedRequest");

            var result = _services.Accounts.Inspect(op, accountId);
            if (!result.Accepted || result.Summary == null)
                return OperatorWireResponse.Reject(req.CorrelationId, req.VerbToken, result.ResultCode);

            var s = result.Summary;
            var resp = OperatorWireResponse.Ok(req.CorrelationId, req.VerbToken, "Inspected");
            resp.Add("account", s.AccountId);
            resp.Add("status", s.Status);
            resp.Add("rev", s.Revision.ToString(CultureInfo.InvariantCulture));
            resp.Add("creds", s.CredentialCount.ToString(CultureInfo.InvariantCulture));
            resp.Add("classes", string.Join("+", s.CredentialClasses)); // provider CLASS only, never subject
            resp.Add("live", s.HasLiveSession ? "true" : "false");
            return resp;
        }

        // ---- player-safe export ----

        private OperatorWireResponse Export(ServerObservedAdminContext op, OperatorWireRequest req, long occurredAt)
        {
            if (!req.TryGetAccountId(out var accountId))
                return OperatorWireResponse.Reject(req.CorrelationId, req.VerbToken, "MalformedRequest");

            // Locator is server-derived (never a client-supplied path): a stable, opaque per-account slot.
            string locator = "export:" + PilotAccountStore.Digest("export|" + accountId.Value + "|" + req.OperationId);
            var export = _services.Privacy.ExportAccount(op, req.OperationId, accountId,
                gameplayRows: null, receiptRows: null,
                retentionSchedule: _services.RetentionPolicy.PolicyVersion,
                exportStorageLocator: locator, policy: _services.RetentionPolicy, occurredAt: occurredAt);

            var resp = OperatorWireResponse.Ok(req.CorrelationId, req.VerbToken, "Exported");
            resp.Add("account", export.AccountId);
            resp.Add("astatus", export.AccountStatus);
            resp.Add("chars", export.CharacterIds.Count.ToString(CultureInfo.InvariantCulture));
            resp.Add("classes", export.CredentialClasses.Count.ToString(CultureInfo.InvariantCulture));
            resp.Add("receipt", export.ReceiptId);
            return resp;
        }

        // ---- disable / delete (durable status + ACTUAL server-side peer/session close) ----

        private OperatorWireResponse DisableOrDelete(ServerObservedAdminContext op, OperatorWireRequest req,
            long occurredAt, bool delete)
        {
            if (!req.TryGetAccountId(out var accountId))
                return OperatorWireResponse.Reject(req.CorrelationId, req.VerbToken, "MalformedRequest");

            var result = delete
                ? _services.Accounts.Delete(op, accountId, req.OperationId, occurredAt)
                : _services.Accounts.Disable(op, accountId, req.OperationId, occurredAt);

            if (!result.Accepted)
                return OperatorWireResponse.Reject(req.CorrelationId, req.VerbToken, result.ResultCode);

            // The service already committed the durable status and removed the process-local registry entry,
            // returning the exact transport handle. NOW trigger the REAL server-side socket close + unbind so
            // the disabled/deleted player is actually dropped (not merely marked). A delayed/absent socket is
            // a no-op — the durable status is already on disk, so authority cannot reopen.
            bool socketClosed = false;
            if (result.SessionClosed && result.ClosedTransportHandle != 0L)
                socketClosed = _peerCloser.CloseTransport(result.ClosedTransportHandle);

            var resp = OperatorWireResponse.Ok(req.CorrelationId, req.VerbToken, result.ResultCode);
            resp.Add("outcome", result.Outcome.ToString());
            resp.Add("rev", result.CommittedRevision.ToString(CultureInfo.InvariantCulture));
            resp.Add("sessionClosed", result.SessionClosed ? "true" : "false");
            resp.Add("socketClosed", socketClosed ? "true" : "false");
            return resp;
        }

        // ---- account-scoped complete-deletion / purge evidence ----

        private OperatorWireResponse Purge(ServerObservedAdminContext op, OperatorWireRequest req, long occurredAt)
        {
            if (!req.TryGetAccountId(out var accountId))
                return OperatorWireResponse.Reject(req.CorrelationId, req.VerbToken, "MalformedRequest");

            // Evidence digest is server-derived (artifact-specific), never a client-supplied raw locator.
            string evidence = PilotAccountStore.Digest("purge-evidence|" + accountId.Value + "|" + req.OperationId);
            var result = _services.Destruction.CompleteAccountDeletion(op, req.OperationId, accountId, evidence, occurredAt);

            var resp = OperatorWireResponse.Ok(req.CorrelationId, req.VerbToken, result.WasReplayed ? "Replayed" : "Purged");
            resp.Add("account", result.AccountId.Value);
            resp.Add("removedCreds", result.RemovedCredentialIds.Count.ToString(CultureInfo.InvariantCulture));
            resp.Add("removedChars", result.RemovedCharacterIds.Count.ToString(CultureInfo.InvariantCulture));
            resp.Add("purgedArtifacts", result.PurgedArtifactIds.Count.ToString(CultureInfo.InvariantCulture));
            resp.Add("receipt", result.DeleteReceiptId);
            return resp;
        }

        // ---- retention purge (counts/evidence ids by category, never a player/provider selector) ----

        private OperatorWireResponse RetentionPurge(ServerObservedAdminContext op, OperatorWireRequest req, long occurredAt)
        {
            var report = _services.Destruction.RunPilotRetentionPurge(op, req.OperationId, occurredAt,
                art => PilotAccountStore.Digest("ret-evidence|" + art.DataArtifactId.Value + "|" + req.OperationId));

            var resp = OperatorWireResponse.Ok(req.CorrelationId, req.VerbToken, "RetentionPurged");
            resp.Add("total", report.TotalPurged.ToString(CultureInfo.InvariantCulture));
            resp.Add("exports", report.PurgedCount(PilotArtifactType.Export).ToString(CultureInfo.InvariantCulture));
            resp.Add("backups", report.PurgedCount(PilotArtifactType.Backup).ToString(CultureInfo.InvariantCulture));
            resp.Add("logs", report.PurgedCount(PilotArtifactType.SecurityLog).ToString(CultureInfo.InvariantCulture));
            resp.Add("held", report.SkippedHeldSelectors.Count.ToString(CultureInfo.InvariantCulture));
            resp.Add("evidence", report.EvidenceReceiptIds.Count.ToString(CultureInfo.InvariantCulture));
            return resp;
        }

        // ---- pilot closure (retention close only; deadline recorded in the catalog) ----

        private OperatorWireResponse ClosePilot(ServerObservedAdminContext op, OperatorWireRequest req, long occurredAt)
        {
            if (req.ArgCount < 1 || !req.Arg(0).StartsWith("pilot-", StringComparison.Ordinal))
                return OperatorWireResponse.Reject(req.CorrelationId, req.VerbToken, "MalformedRequest");
            var pilotId = new PilotId(req.Arg(0));
            _services.Privacy.ClosePilot(op, req.OperationId, pilotId, _services.RetentionPolicy, occurredAt);

            var resp = OperatorWireResponse.Ok(req.CorrelationId, req.VerbToken, "PilotClosing");
            resp.Add("pilot", pilotId.Value);
            resp.Add("admits", _services.Privacy.EvaluateAdmission(occurredAt) == PrivacyRejectionCode.None ? "true" : "false");
            return resp;
        }
    }

    /// <summary>Server-owned port that performs the REAL transport close of a live session identified by its
    /// opaque transport handle (the net48 layer maps it to the delivering ZNetPeer and calls
    /// <c>ZNet.Disconnect</c> + unbinds the bound-session principal). Returns true iff a live socket was
    /// actually closed; a stale/absent handle is a clean no-op (the durable status already committed).</summary>
    public interface IServerPeerCloser
    {
        bool CloseTransport(long transportHandle);
    }

    /// <summary>A no-op closer for composition paths (and tests) where no live transport exists. The durable
    /// lifecycle commit is unaffected; only the socket-close side effect is skipped.</summary>
    public sealed class NullServerPeerCloser : IServerPeerCloser
    {
        public bool CloseTransport(long transportHandle) => false;
    }
}
