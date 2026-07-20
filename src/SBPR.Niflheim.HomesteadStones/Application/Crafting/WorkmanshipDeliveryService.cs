using System;
using SBPR.Niflheim.HomesteadStones.Adapters.Crafting;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;

namespace SBPR.Niflheim.HomesteadStones.Application.Crafting
{
    // ============================================================================
    // T022 remediation — the ENGINE-FREE server-side Masterwork delivery service. It is
    // the single authority the net48 dedicated-server delivery seam calls to answer a
    // joined client's issuance/validation request. It composes the two shipped, unit-
    // tested authorities — the WorkmanshipIssuanceProvider (the issue decision) and the
    // WorkmanshipCodec (mint-sign / validate) — into the two wire replies, keeping the
    // raw integrity key strictly inside the server process (the net48 seam supplies the
    // key; the CLIENT never sees it).
    //
    // The net48 seam is a THIN adapter: it authenticates the requesting peer by the
    // delivering ZRpc, resolves that peer's BOUND INTERNAL principal + Masterwork
    // activation from the composed server stores (never a client claim), mints a server-
    // owned provenance id, and hands (masterworkActive, crafterAccount, provenanceId,
    // request, key) to this service. This service performs zero I/O and touches no engine
    // type, so the whole mint/sign/validate decision runs headless in the net8 tests.
    //
    // net48 audit: engine-free (System.* + the engine-free provider/codec). Link-compiles
    // into the net8 test project.
    // ============================================================================

    public sealed class WorkmanshipDeliveryService
    {
        private readonly WorkmanshipIssuanceProvider _provider;

        public WorkmanshipDeliveryService(WorkmanshipIssuanceProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>Decide + mint + sign an issuance for one joined-client request. The caller has already
        /// resolved (server-side, from the authoritative stores keyed by the transport-authenticated principal)
        /// whether Masterwork is active for the requester and that requester's internal <paramref name="crafterAccount"/>,
        /// and minted a server-owned <paramref name="provenanceId"/>. This method runs the shipped provider's
        /// decision over the request's server-observed eligibility facts and, on Issue, signs the stamp with the
        /// server <paramref name="key"/> — returning a grant carrying the stamp FIELDS + token for the client to
        /// persist verbatim. Every refusal returns a no-write grant with the machine outcome. The key is used
        /// here and never placed on the wire.</summary>
        public WorkmanshipIssuanceGrant Issue(
            bool masterworkActive,
            string crafterAccount,
            ItemProvenanceId provenanceId,
            in WorkmanshipIssuanceRequest request,
            WorkmanshipIntegrityKey key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            var facts = new ProducedItemFacts(
                request.ItemType,
                request.NonStackable,
                request.Durable,
                request.AlreadyHasWellFormedStamp,
                provenanceId);

            var decision = _provider.Decide(masterworkActive, crafterAccount ?? string.Empty, facts);
            if (!decision.ShouldIssue)
                return WorkmanshipIssuanceGrant.Refused(request.CorrelationId, MapOutcome(decision.Outcome));

            string token = WorkmanshipCodec.Sign(decision.Stamp, key);
            return new WorkmanshipIssuanceGrant(
                request.CorrelationId, true, WorkmanshipIssuanceOutcomeCode.Issue, decision.Stamp, token);
        }

        /// <summary>Validate a client-presented stamp + token under the server key and produce the verdict.
        /// The client relayed the fields it read keylessly (WorkmanshipCodec.TryReadRaw); this recomputes the
        /// token server-side and answers Valid/Tampered. The key stays server-side.</summary>
        public WorkmanshipValidationVerdict Validate(in WorkmanshipValidationRequest request, WorkmanshipIntegrityKey key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            bool valid = WorkmanshipCodec.Validate(request.Stamp, request.Token, key) == WorkmanshipReadState.Valid;
            return new WorkmanshipValidationVerdict(request.CorrelationId, request.Stamp.ProvenanceId, request.Fingerprint, valid);
        }

        private static WorkmanshipIssuanceOutcomeCode MapOutcome(WorkmanshipIssuanceOutcome outcome)
        {
            switch (outcome)
            {
                case WorkmanshipIssuanceOutcome.Issue: return WorkmanshipIssuanceOutcomeCode.Issue;
                case WorkmanshipIssuanceOutcome.EffectNotActive: return WorkmanshipIssuanceOutcomeCode.EffectNotActive;
                case WorkmanshipIssuanceOutcome.IneligibleItem: return WorkmanshipIssuanceOutcomeCode.IneligibleItem;
                case WorkmanshipIssuanceOutcome.AlreadyStamped: return WorkmanshipIssuanceOutcomeCode.AlreadyStamped;
                default: return WorkmanshipIssuanceOutcomeCode.Unresolved;
            }
        }
    }
}
