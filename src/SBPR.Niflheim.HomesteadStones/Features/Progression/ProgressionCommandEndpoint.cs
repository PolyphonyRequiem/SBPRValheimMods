using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Queries;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    // T035 — the TRANSPORT-NEUTRAL progression command endpoint (spec US5 acceptance scenario 7;
    // FR-024/FR-025; contracts.md §"Contract principles": "The temporary in-world panel and future remote
    // Stones UI call the same progression commands" and "World evidence remains local and server-validated
    // even when the resulting progression selection is remote").
    //
    // WHAT THIS IS
    // One routing surface over the ALREADY-ACCEPTED, receipt-backed command handlers. A caller submits a
    // ProgressionCommandEnvelope naming a command type + payload; the endpoint maps it onto the shipped
    // handler and returns the handler's verbatim outcome. It owns NO gameplay logic: no authority check of
    // its own, no revision arithmetic, no journal, no ledger. Every authority/relationship/balance/
    // requirement/content-version/revision gate remains exactly where it already lives (FR-025 revalidation
    // is the handlers', and it happens on EVERY submission — proximate or not).
    //
    // WHY IT IS "TRANSPORT-NEUTRAL"
    // Nothing in its input names a position, a Stone Area, a nearby panel, a ZRpc, or a UI. Its only
    // identity input is the server-observed AuthenticatedConnection the transport attaches OUT of band; the
    // ClaimedPrincipal in the payload is compared and never trusted (the shipped PrincipalResolver in each
    // handler does that). So the SAME endpoint serves the temporary local panel and a future remote Stones
    // UI without semantic change — which is precisely what FR-024 requires and what makes one
    // NON-PROXIMATE selection possible (AT-REMOTE-SHAPED).
    //
    // THE SECURITY CORE (AT-LOCAL-EVIDENCE-NOT-REMOTE)
    // A remote command may EXECUTE away from the Stone, but NO remote command may FABRICATE placement,
    // presence, cooking, crafting, or combat evidence. Those facts are attributed by trusted server code in
    // the evidence adapters (Adapters/Activities/FoundationalPlacementAdapter, AlignedActivityAdapter) and
    // their pipelines/handlers. This endpoint therefore has an EXPLICIT, exhaustive routability table
    // (<see cref="ProgressionCommandRouting"/>): every command type is classified, and the evidence-bearing
    // ones are classified NON-CLIENT-CALLABLE and rejected here with `EvidenceNotRemotelyInvocable` before
    // any handler is consulted. A submission for one of them changes nothing and produces no receipt.
    // Adding a new command type without classifying it does not silently open a hole: the switch is
    // exhaustive by default (`UnknownCommand` fails closed).
    //
    // Relationship FORMATION/RELEASE is deliberately NOT routed here either. That flow is proximate and is
    // owned by a separate card (#138); this card routes progression SELECTION only. Fail closed rather than
    // widening the remote surface by accident.
    //
    // net48 audit: engine-free (value objects + the shipped engine-free handlers/queries). No UnityEngine/
    // Valheim/BepInEx reference, so it link-compiles into the net8 test project and every routing branch —
    // including every hostile one — is unit-tested. This file contains NO [HarmonyPatch] class, so there is
    // nothing for Plugin.Awake to register; the live net48 transport that will carry it is a follow-up
    // (see the handoff), and this file makes no in-world claim.

    /// <summary>Every progression command type the endpoint knows about. Stable contract identity; the
    /// numeric values are wire-stable.</summary>
    public enum ProgressionCommandType
    {
        Unknown = 0,

        // Progression SELECTION — remotely invocable (the FR-024 reusable seam).
        CommitTreeToFacet = 1,
        ApplyBPToNode = 2,
        PurchaseNode = 3,
        SetSettlementLocalPolicy = 4,
        PreviewRevocation = 5,
        RevokeTree = 6,

        // Server-observed EVIDENCE — never client-callable (AT-LOCAL-EVIDENCE-NOT-REMOTE).
        RecordFoundationalPlacement = 100,
        RecordAlignedActivity = 101,

        // Proximate relationship lifecycle — owned by card #138, not routed here.
        CreateBond = 200,
        CreateAttunement = 201,
        ReleaseRelationship = 202
    }

    /// <summary>How a command type may reach the server.</summary>
    public enum ProgressionCommandReachability
    {
        /// <summary>Unknown/unclassified — fail closed.</summary>
        Rejected = 0,

        /// <summary>Routable through this endpoint by an authenticated caller, proximate or not.</summary>
        RemotelyInvocable = 1,

        /// <summary>Server-observed evidence. The submitting authority is a trusted adapter attributing a
        /// runtime fact; a client message can never reach it.</summary>
        ServerObservedEvidenceOnly = 2,

        /// <summary>Proximate relationship lifecycle, owned by a different card. Not routed here.</summary>
        ProximateRelationshipFlow = 3
    }

    /// <summary>The EXHAUSTIVE, single-source routability table. This is the file to read (and the test to
    /// break) when asking "can a client message reach X?".</summary>
    public static class ProgressionCommandRouting
    {
        public static ProgressionCommandReachability Reachability(ProgressionCommandType type)
        {
            switch (type)
            {
                case ProgressionCommandType.CommitTreeToFacet:
                case ProgressionCommandType.ApplyBPToNode:
                case ProgressionCommandType.PurchaseNode:
                case ProgressionCommandType.SetSettlementLocalPolicy:
                case ProgressionCommandType.PreviewRevocation:
                case ProgressionCommandType.RevokeTree:
                    return ProgressionCommandReachability.RemotelyInvocable;

                case ProgressionCommandType.RecordFoundationalPlacement:
                case ProgressionCommandType.RecordAlignedActivity:
                    return ProgressionCommandReachability.ServerObservedEvidenceOnly;

                case ProgressionCommandType.CreateBond:
                case ProgressionCommandType.CreateAttunement:
                case ProgressionCommandType.ReleaseRelationship:
                    return ProgressionCommandReachability.ProximateRelationshipFlow;

                default:
                    return ProgressionCommandReachability.Rejected;   // fail closed on anything unclassified
            }
        }

        /// <summary>True only for the progression-selection commands a client message may submit.</summary>
        public static bool IsRemotelyInvocable(ProgressionCommandType type) =>
            Reachability(type) == ProgressionCommandReachability.RemotelyInvocable;

        /// <summary>Every command type, for conformance/coverage assertions.</summary>
        public static IReadOnlyList<ProgressionCommandType> AllCommandTypes { get; } =
            new[]
            {
                ProgressionCommandType.Unknown,
                ProgressionCommandType.CommitTreeToFacet,
                ProgressionCommandType.ApplyBPToNode,
                ProgressionCommandType.PurchaseNode,
                ProgressionCommandType.SetSettlementLocalPolicy,
                ProgressionCommandType.PreviewRevocation,
                ProgressionCommandType.RevokeTree,
                ProgressionCommandType.RecordFoundationalPlacement,
                ProgressionCommandType.RecordAlignedActivity,
                ProgressionCommandType.CreateBond,
                ProgressionCommandType.CreateAttunement,
                ProgressionCommandType.ReleaseRelationship
            };
    }

    /// <summary>The common command envelope (contracts.md §"Common command envelope"), transport-neutral.
    /// The transport attaches <see cref="Connection"/> out of band from the authenticated session;
    /// <see cref="Claim"/> is untrusted payload the handlers compare and never trust. There is deliberately
    /// NO position, Area, proximity, or panel field — a caller cannot assert where it is standing, and the
    /// endpoint could not read it if it did.</summary>
    public readonly struct ProgressionCommandEnvelope
    {
        public ProgressionCommandEnvelope(
            ProgressionCommandType commandType,
            OperationId operationId,
            StoneId stoneId,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            int contentRegistryVersion,
            ProgressionCommandPayload payload,
            long? expectedStoneRevision = null,
            long? expectedCharacterRevision = null,
            long? expectedAuthorityRevision = null,
            long? expectedPolicyRevision = null)
        {
            CommandType = commandType;
            OperationId = operationId;
            StoneId = stoneId;
            Connection = connection;
            Claim = claim;
            ContentRegistryVersion = contentRegistryVersion;
            Payload = payload;
            ExpectedStoneRevision = expectedStoneRevision;
            ExpectedCharacterRevision = expectedCharacterRevision;
            ExpectedAuthorityRevision = expectedAuthorityRevision;
            ExpectedPolicyRevision = expectedPolicyRevision;
        }

        public ProgressionCommandType CommandType { get; }
        public OperationId OperationId { get; }
        public StoneId StoneId { get; }

        /// <summary>Server-observed authenticated connection (bound internal account/character). The ONLY
        /// identity authority on this envelope.</summary>
        public AuthenticatedConnection Connection { get; }

        /// <summary>Untrusted client claim. Compared by the shipped handlers, never trusted.</summary>
        public ClaimedPrincipal Claim { get; }

        public int ContentRegistryVersion { get; }
        public ProgressionCommandPayload Payload { get; }

        public long? ExpectedStoneRevision { get; }
        public long? ExpectedCharacterRevision { get; }
        public long? ExpectedAuthorityRevision { get; }
        public long? ExpectedPolicyRevision { get; }
    }

    /// <summary>The union of accepted command payload fields. Flat and engine-free so one wire shape serves
    /// every routed command; each route reads only the fields its handler declares.</summary>
    public readonly struct ProgressionCommandPayload
    {
        public ProgressionCommandPayload(
            string facetId = "",
            VersionedId tree = default,
            VersionedId node = default,
            int paletteVersion = 0,
            int bpAmount = 0,
            VersionedId expectedOfferedSet = default,
            PurchasePaymentSource paymentPreference = PurchasePaymentSource.PersonalAp,
            LocalBeneficiaryMode policyMode = LocalBeneficiaryMode.Everyone,
            IReadOnlyList<string>? allowlistAccounts = null,
            string reasonCode = "")
        {
            FacetId = facetId ?? string.Empty;
            Tree = tree;
            Node = node;
            PaletteVersion = paletteVersion;
            BpAmount = bpAmount;
            ExpectedOfferedSet = expectedOfferedSet;
            PaymentPreference = paymentPreference;
            PolicyMode = policyMode;
            AllowlistAccounts = allowlistAccounts ?? Array.Empty<string>();
            ReasonCode = reasonCode ?? string.Empty;
        }

        public string FacetId { get; }
        public VersionedId Tree { get; }
        public VersionedId Node { get; }
        public int PaletteVersion { get; }
        public int BpAmount { get; }
        public VersionedId ExpectedOfferedSet { get; }
        public PurchasePaymentSource PaymentPreference { get; }
        public LocalBeneficiaryMode PolicyMode { get; }
        public IReadOnlyList<string> AllowlistAccounts { get; }
        public string ReasonCode { get; }
    }

    /// <summary>The endpoint's uniform result (contracts.md common successful/rejected result, reduced to
    /// what every routed handler can supply). <see cref="ResultCode"/> is ALWAYS the shipped handler's
    /// verbatim code on a routed command, so a caller sees the real gate that fired — never a code this
    /// endpoint invented on the handler's behalf.</summary>
    public sealed class ProgressionCommandOutcomeResult
    {
        public ProgressionCommandOutcomeResult(
            ProgressionCommandType commandType,
            CommandOutcome outcome,
            string resultCode,
            string receiptId,
            long stoneRevision,
            long characterRevision,
            ProgressionRevisionNotification? notification = null)
        {
            CommandType = commandType;
            Outcome = outcome;
            ResultCode = resultCode ?? string.Empty;
            ReceiptId = receiptId ?? string.Empty;
            StoneRevision = stoneRevision;
            CharacterRevision = characterRevision;
            Notification = notification;
        }

        public ProgressionCommandType CommandType { get; }
        public CommandOutcome Outcome { get; }
        public string ResultCode { get; }
        public string ReceiptId { get; }
        public long StoneRevision { get; }
        public long CharacterRevision { get; }

        /// <summary>The BOUNDED invalidation event to publish after a COMMITTED operation (contracts.md
        /// §"Notification contract"). Null on rejection — a rejection is not a receipt-bearing mutation and
        /// must never invalidate a client's read model.</summary>
        public ProgressionRevisionNotification? Notification { get; }

        public bool Rejected => Outcome == CommandOutcome.Rejected;

        internal static ProgressionCommandOutcomeResult Reject(ProgressionCommandType type, string code) =>
            new ProgressionCommandOutcomeResult(type, CommandOutcome.Rejected, code, string.Empty, 0, 0);
    }

    /// <summary>
    /// The transport-neutral routing surface. Handlers are injected; a route whose handler was not
    /// composed rejects <c>HandlerUnavailable</c> rather than falling back to anything permissive.
    /// </summary>
    public sealed class ProgressionCommandEndpoint
    {
        /// <summary>Rejection code for a command type that is server-observed evidence. This is the
        /// AT-LOCAL-EVIDENCE-NOT-REMOTE wall: a client message naming one of these is refused before any
        /// handler is consulted, so no evidence can be fabricated remotely.</summary>
        public const string EvidenceNotRemotelyInvocable = "EvidenceNotRemotelyInvocable";

        /// <summary>Rejection code for the proximate relationship lifecycle (card #138).</summary>
        public const string NotRemotelyInvocable = "NotRemotelyInvocable";

        public const string UnknownCommand = "UnknownCommand";
        public const string HandlerUnavailable = "HandlerUnavailable";

        private readonly FacetCommandHandler? _facets;
        private readonly DevelopmentCommandHandler? _development;
        private readonly PurchaseCommandHandler? _purchases;
        private readonly LocalPolicyCommandHandler? _localPolicy;
        private readonly RevocationCommandHandler? _revocation;
        private readonly GetRelationshipPortfolio? _portfolio;

        public ProgressionCommandEndpoint(
            FacetCommandHandler? facets = null,
            DevelopmentCommandHandler? development = null,
            PurchaseCommandHandler? purchases = null,
            LocalPolicyCommandHandler? localPolicy = null,
            RevocationCommandHandler? revocation = null,
            GetRelationshipPortfolio? portfolio = null)
        {
            _facets = facets;
            _development = development;
            _purchases = purchases;
            _localPolicy = localPolicy;
            _revocation = revocation;
            _portfolio = portfolio;
        }

        /// <summary>Compose the endpoint over a live <see cref="LocalProgressionServer"/>'s accepted
        /// handlers plus a revocation/purchase pair over its own durable journals. One composition root, so
        /// the panel and a future remote UI cannot end up on two different handler sets.</summary>
        public static ProgressionCommandEndpoint ForServer(LocalProgressionServer server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            var purchases = new PurchaseCommandHandler(
                System.IO.Path.Combine(server.DurableDirectory, LocalProgressionServer.PurchaseJournalFile),
                new PrincipalResolver(), server.Stones, server.Characters, server.Authority,
                server.Catalog, server.CharacterApStore);
            return new ProgressionCommandEndpoint(
                server.Facets, server.Development, purchases, server.LocalPolicy,
                server.CreateRevocationCommandHandler(purchases),
                new GetRelationshipPortfolio(new PrincipalResolver(), server.Characters, server.Stones,
                    server.Authority));
        }

        /// <summary>The compact, NON-PROXIMATE portfolio read (contracts.md
        /// <c>GetRelationshipPortfolio</c>). Rejects fail-closed when the query was not composed.</summary>
        public RelationshipPortfolio Portfolio(AuthenticatedConnection connection, ClaimedPrincipal claim) =>
            _portfolio == null
                ? RelationshipPortfolio.Denied(HandlerUnavailable)
                : _portfolio.Execute(connection, claim);

        /// <summary>Route ONE submitted command. Order is load-bearing:
        /// <list type="number">
        /// <item>reachability (evidence/proximate/unknown rejected here, with NO handler consulted and NO
        /// mutation);</item>
        /// <item>handler presence (fail closed);</item>
        /// <item>the shipped handler, which performs ALL authentication, claim comparison, authority,
        /// relationship, balance, requirement, content-version, revision, and idempotency validation and
        /// owns the durable journal (FR-025 revalidation).</item>
        /// </list>
        /// This method never mutates state itself and never invents a success.</summary>
        public ProgressionCommandOutcomeResult Submit(in ProgressionCommandEnvelope envelope)
        {
            var reachability = ProgressionCommandRouting.Reachability(envelope.CommandType);
            switch (reachability)
            {
                case ProgressionCommandReachability.ServerObservedEvidenceOnly:
                    // AT-LOCAL-EVIDENCE-NOT-REMOTE. Placement/presence/cooking/crafting/combat evidence is
                    // attributed by trusted server adapters only. No handler is reached, nothing is
                    // journaled, no receipt exists.
                    return ProgressionCommandOutcomeResult.Reject(envelope.CommandType, EvidenceNotRemotelyInvocable);
                case ProgressionCommandReachability.ProximateRelationshipFlow:
                    return ProgressionCommandOutcomeResult.Reject(envelope.CommandType, NotRemotelyInvocable);
                case ProgressionCommandReachability.RemotelyInvocable:
                    break;
                default:
                    return ProgressionCommandOutcomeResult.Reject(envelope.CommandType, UnknownCommand);
            }

            switch (envelope.CommandType)
            {
                case ProgressionCommandType.CommitTreeToFacet: return RouteCommit(envelope);
                case ProgressionCommandType.ApplyBPToNode: return RouteDevelopment(envelope);
                case ProgressionCommandType.PurchaseNode: return RoutePurchase(envelope);
                case ProgressionCommandType.SetSettlementLocalPolicy: return RoutePolicy(envelope);
                case ProgressionCommandType.PreviewRevocation: return RoutePreviewRevocation(envelope);
                case ProgressionCommandType.RevokeTree: return RouteRevoke(envelope);
                default:
                    return ProgressionCommandOutcomeResult.Reject(envelope.CommandType, UnknownCommand);
            }
        }

        // ── routes ──────────────────────────────────────────────────────────────────────────────────

        private ProgressionCommandOutcomeResult RouteCommit(in ProgressionCommandEnvelope e)
        {
            if (_facets == null) return Unavailable(e.CommandType);
            var result = _facets.Handle(new CommitTreeToFacetCommand(
                e.OperationId, e.StoneId, e.Connection, e.Claim,
                e.Payload.FacetId, e.Payload.Tree.Key, e.Payload.Tree.Version, e.Payload.PaletteVersion,
                e.ExpectedStoneRevision));

            var outcome = Map(result.Outcome == FacetCommandOutcome.Applied,
                result.Outcome == FacetCommandOutcome.Replayed);
            return Build(e, outcome, result.ResultCode, result.ReceiptId, result.StoneRevision, 0);
        }

        private ProgressionCommandOutcomeResult RouteDevelopment(in ProgressionCommandEnvelope e)
        {
            if (_development == null) return Unavailable(e.CommandType);
            var result = _development.Handle(new ApplyBPToNodeCommand(
                e.OperationId, e.StoneId, e.Connection, e.Claim,
                e.Payload.Tree.Key, e.Payload.Tree.Version, e.Payload.Node.Key, e.Payload.Node.Version,
                e.Payload.BpAmount, e.ExpectedStoneRevision, e.ExpectedCharacterRevision));

            var outcome = Map(result.Outcome == DevelopmentCommandOutcome.Applied,
                result.Outcome == DevelopmentCommandOutcome.Replayed);
            return Build(e, outcome, result.ResultCode, result.ReceiptId,
                result.StoneRevision, result.CharacterRevision);
        }

        private ProgressionCommandOutcomeResult RoutePurchase(in ProgressionCommandEnvelope e)
        {
            if (_purchases == null) return Unavailable(e.CommandType);
            var result = _purchases.Handle(new PurchaseNodeCommand(
                e.OperationId, e.StoneId, e.Connection, e.Claim,
                e.Payload.Tree.Key, e.Payload.Tree.Version, e.Payload.Node.Key, e.Payload.Node.Version,
                e.Payload.ExpectedOfferedSet.IsNone ? string.Empty : e.Payload.ExpectedOfferedSet.Key,
                e.Payload.ExpectedOfferedSet.IsNone ? 0 : e.Payload.ExpectedOfferedSet.Version,
                e.Payload.PaymentPreference, e.ExpectedStoneRevision, e.ExpectedCharacterRevision));

            var outcome = Map(result.Outcome == PurchaseCommandOutcome.Applied,
                result.Outcome == PurchaseCommandOutcome.Replayed);
            return Build(e, outcome, result.ResultCode, result.ReceiptId, 0, result.CharacterRevision);
        }

        private ProgressionCommandOutcomeResult RoutePolicy(in ProgressionCommandEnvelope e)
        {
            if (_localPolicy == null) return Unavailable(e.CommandType);
            var result = _localPolicy.Handle(new SetSettlementLocalPolicyCommand(
                e.OperationId, e.StoneId, e.Connection, e.Claim,
                e.Payload.PolicyMode, e.Payload.AllowlistAccounts,
                e.ExpectedStoneRevision, e.ExpectedPolicyRevision));

            var outcome = Map(result.Outcome == LocalPolicyCommandOutcome.Applied,
                result.Outcome == LocalPolicyCommandOutcome.Replayed);
            return Build(e, outcome, result.ResultCode, result.ReceiptId, result.StoneRevision, 0,
                result.PolicyRevision);
        }

        private ProgressionCommandOutcomeResult RoutePreviewRevocation(in ProgressionCommandEnvelope e)
        {
            if (_revocation == null) return Unavailable(e.CommandType);
            // Step ONE of the two-step revocation (AT-REVOKE-TWO-STEP): computes the loss and mutates
            // NOTHING. It is therefore never a committed operation and never publishes a notification.
            var preview = _revocation.PreviewRevocation(new RevokeTreeCommand(
                e.OperationId, e.StoneId, e.Connection, e.Claim,
                e.Payload.FacetId, e.Payload.Tree.Key, e.Payload.Tree.Version,
                e.Payload.ReasonCode, e.ExpectedStoneRevision));

            return new ProgressionCommandOutcomeResult(e.CommandType,
                preview.Accepted ? CommandOutcome.Applied : CommandOutcome.Rejected,
                preview.ResultCode, string.Empty, preview.StoneRevision, 0, notification: null);
        }

        private ProgressionCommandOutcomeResult RouteRevoke(in ProgressionCommandEnvelope e)
        {
            if (_revocation == null) return Unavailable(e.CommandType);
            var result = _revocation.Handle(new RevokeTreeCommand(
                e.OperationId, e.StoneId, e.Connection, e.Claim,
                e.Payload.FacetId, e.Payload.Tree.Key, e.Payload.Tree.Version,
                e.Payload.ReasonCode, e.ExpectedStoneRevision));

            var outcome = Map(result.Outcome == RevocationCommandOutcome.Applied,
                result.Outcome == RevocationCommandOutcome.Replayed);
            return Build(e, outcome, result.ResultCode, result.ReceiptId, result.StoneRevision, 0);
        }

        // ── helpers ─────────────────────────────────────────────────────────────────────────────────

        private static ProgressionCommandOutcomeResult Unavailable(ProgressionCommandType type) =>
            ProgressionCommandOutcomeResult.Reject(type, HandlerUnavailable);

        private static CommandOutcome Map(bool applied, bool replayed) =>
            applied ? CommandOutcome.Applied : replayed ? CommandOutcome.Replayed : CommandOutcome.Rejected;

        /// <summary>Attach the BOUNDED invalidation notification for a COMMITTED operation only. A rejected
        /// command produces none, so a hostile caller cannot spam a client's cache into refetching by
        /// submitting rejects.</summary>
        private static ProgressionCommandOutcomeResult Build(
            in ProgressionCommandEnvelope e, CommandOutcome outcome, string resultCode, string receiptId,
            long stoneRevision, long characterRevision, long policyRevision = 0)
        {
            ProgressionRevisionNotification? notification = null;
            if (outcome != CommandOutcome.Rejected)
            {
                notification = new ProgressionRevisionNotification(
                    e.StoneId, e.Connection.AccountId ?? string.Empty, e.CommandType,
                    stoneRevision, characterRevision, policyRevision, resultCode);
            }
            return new ProgressionCommandOutcomeResult(e.CommandType, outcome, resultCode, receiptId,
                stoneRevision, characterRevision, notification);
        }
    }
}
