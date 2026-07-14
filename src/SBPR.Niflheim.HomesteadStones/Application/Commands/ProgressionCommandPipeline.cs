using System;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Application.Commands
{
    // The one command pipeline for the Foundational AP receipt slice (T002, Gate A). Architecture
    // decision A2 (plan.md): every mutation authenticates the connection principal, compares the
    // claim, then commits/journals recoverably. There is no direct adapter->wallet or UI->ZDO write;
    // world adapters submit validated evidence and the pipeline is the sole mutation authority.
    //
    // This slice implements only RecordFoundationalPlacement -> ApActivityReceipt (contracts.md
    // evidence-and-credit contracts). Relationships (T007) are not yet present, so authorization is
    // gated by an explicit preconfigured-test authorizer — NOT a production relationship bypass.
    //
    // net48 audit: value objects + interfaces only; no net5+ API, no UnityEngine/Valheim reference,
    // so this file link-compiles into the net8 test project.

    /// <summary>Preconfigured-test authorization surface. Until T007 lands real Bond/Attunement
    /// relationships, the pipeline consults this to decide whether a principal may earn Foundational
    /// AP at a Stone. A production build wires this to the real relationship authority; the test
    /// fixture wires an explicit allow-list. It is deliberately NOT a blanket bypass.</summary>
    public interface IFoundationalPlacementAuthorizer
    {
        bool IsAuthorized(AuthoritativePrincipal principal, StoneId stoneId);
    }

    /// <summary>The common progression command envelope (contracts.md). The transport attaches the
    /// server-observed <see cref="Connection"/> outside the payload; <see cref="Claim"/> is untrusted
    /// client payload the handler compares but never trusts.</summary>
    public readonly struct FoundationalPlacementCommand
    {
        public FoundationalPlacementCommand(
            OperationId operationId,
            StoneId stoneId,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            string evidenceDigest,
            long? expectedStoneRevision = null,
            long? expectedCharacterRevision = null)
        {
            OperationId = operationId;
            StoneId = stoneId;
            Connection = connection;
            Claim = claim;
            EvidenceDigest = evidenceDigest;
            ExpectedStoneRevision = expectedStoneRevision;
            ExpectedCharacterRevision = expectedCharacterRevision;
        }

        public OperationId OperationId { get; }
        public StoneId StoneId { get; }
        public AuthenticatedConnection Connection { get; }
        public ClaimedPrincipal Claim { get; }
        public string EvidenceDigest { get; }

        /// <summary>Optimistic-concurrency token: the Stone aggregate revision the caller observed.
        /// The handler commits only if it still matches current, else rejects StaleStoneRevision with
        /// no mutation (contracts.md common envelope: expectedStoneRevision required for Stone
        /// mutations). Null means the caller supplied no expectation (compare-any) — the transport
        /// layer supplies it for real client commands.</summary>
        public long? ExpectedStoneRevision { get; }

        /// <summary>Optimistic-concurrency token for the character-at-Stone aggregate. Rejects
        /// StaleCharacterRevision on mismatch with no mutation.</summary>
        public long? ExpectedCharacterRevision { get; }
    }

    public enum CommandOutcome
    {
        Applied,
        Replayed,
        Rejected
    }

    /// <summary>Result of a Foundational placement command. On rejection the balances are zero and
    /// nothing was journaled (contracts.md: a rejection is not a receipt-bearing mutation).</summary>
    public readonly struct FoundationalPlacementResult
    {
        public FoundationalPlacementResult(CommandOutcome outcome, string resultCode,
            int personalApDelta, int cumulativeApDelta, int mirroredStoneApDelta, string receiptId,
            long stoneRevision = 0, long characterRevision = 0)
        {
            Outcome = outcome;
            ResultCode = resultCode;
            PersonalApDelta = personalApDelta;
            CumulativeApDelta = cumulativeApDelta;
            MirroredStoneApDelta = mirroredStoneApDelta;
            ReceiptId = receiptId;
            StoneRevision = stoneRevision;
            CharacterRevision = characterRevision;
        }

        public CommandOutcome Outcome { get; }
        public string ResultCode { get; }
        public int PersonalApDelta { get; }
        public int CumulativeApDelta { get; }
        public int MirroredStoneApDelta { get; }
        public string ReceiptId { get; }

        /// <summary>Stone aggregate revision after this operation (contracts.md result.stoneRevision).
        /// On a stale-revision rejection this carries the current revision the caller must refetch.</summary>
        public long StoneRevision { get; }

        /// <summary>Character-at-Stone aggregate revision after this operation
        /// (contracts.md result.characterRevision).</summary>
        public long CharacterRevision { get; }
    }

    public sealed class ProgressionCommandPipeline
    {
        private readonly PrincipalResolver _resolver;
        private readonly OperationReceiptStore _receipts;
        private readonly IFoundationalPlacementAuthorizer _authorizer;

        public ProgressionCommandPipeline(
            PrincipalResolver resolver,
            OperationReceiptStore receipts,
            IFoundationalPlacementAuthorizer authorizer)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _receipts = receipts ?? throw new ArgumentNullException(nameof(receipts));
            _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        }

        public FoundationalPlacementResult Handle(FoundationalPlacementCommand command, ICrashInjector? crash = null)
        {
            // 1-2. Authenticate connection principal; compare (never trust) the claim.
            var resolution = _resolver.Resolve(command.Connection, command.Claim, out var principal);
            if (resolution == PrincipalResolution.UnauthenticatedPeer)
                return Rejected("Unauthenticated");
            if (resolution == PrincipalResolution.PrincipalMismatch)
                return Rejected("PrincipalMismatch");

            // 3-4. Authority validation. Rejection changes nothing; no journal is written.
            if (!_authorizer.IsAuthorized(principal, command.StoneId))
                return Rejected("RelationshipRequired");

            // 5-8. Reserve/find operation result, commit/journal deltas recoverably, acknowledge.
            // Revisioned command: the caller's expected Stone/character revisions are validated (CAS)
            // before any durable write, so a stale command from a losing concurrent client changes
            // nothing (contracts.md common envelope + StaleStoneRevision/StaleCharacterRevision).
            var receipt = _receipts.SubmitFoundationalAp(
                command.OperationId, command.StoneId, principal, command.EvidenceDigest, crash,
                command.ExpectedStoneRevision, command.ExpectedCharacterRevision);

            switch (receipt.Outcome)
            {
                case ReceiptOutcome.Applied:
                    return new FoundationalPlacementResult(CommandOutcome.Applied, "Applied",
                        receipt.PersonalAp, receipt.CumulativeAp, receipt.MirroredStoneAp, receipt.ReceiptId,
                        receipt.StoneRevision, receipt.CharacterRevision);
                case ReceiptOutcome.Replayed:
                    return new FoundationalPlacementResult(CommandOutcome.Replayed, "Replayed",
                        receipt.PersonalAp, receipt.CumulativeAp, receipt.MirroredStoneAp, receipt.ReceiptId,
                        receipt.StoneRevision, receipt.CharacterRevision);
                case ReceiptOutcome.StaleStoneRevision:
                    return RejectedWithRevisions("StaleStoneRevision", receipt.StoneRevision, receipt.CharacterRevision);
                case ReceiptOutcome.StaleCharacterRevision:
                    return RejectedWithRevisions("StaleCharacterRevision", receipt.StoneRevision, receipt.CharacterRevision);
                default:
                    return Rejected("OperationConflict");
            }
        }

        private static FoundationalPlacementResult Rejected(string code) =>
            new FoundationalPlacementResult(CommandOutcome.Rejected, code, 0, 0, 0, string.Empty);

        private static FoundationalPlacementResult RejectedWithRevisions(string code, long stoneRevision, long characterRevision) =>
            new FoundationalPlacementResult(CommandOutcome.Rejected, code, 0, 0, 0, string.Empty,
                stoneRevision, characterRevision);
    }

    /// <summary>Explicit preconfigured-test authorizer: an allow-list of (account, Stone) pairs. This
    /// is the T002-scope stand-in for the T007 relationship authority. It authorizes only what is
    /// explicitly configured; it is not a production bypass and grants nothing by default.</summary>
    public sealed class PreconfiguredTestAuthorizer : IFoundationalPlacementAuthorizer
    {
        private readonly System.Collections.Generic.HashSet<string> _allowed =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        public PreconfiguredTestAuthorizer Allow(AccountId account, StoneId stoneId)
        {
            _allowed.Add(account.Value + "|" + stoneId.Value);
            return this;
        }

        public bool IsAuthorized(AuthoritativePrincipal principal, StoneId stoneId) =>
            _allowed.Contains(principal.Account.Value + "|" + stoneId.Value);
    }
}
