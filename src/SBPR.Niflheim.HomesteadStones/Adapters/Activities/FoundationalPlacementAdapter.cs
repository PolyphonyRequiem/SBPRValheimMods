using System;
using System.Globalization;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Activities
{
    // The one trusted Foundational-placement evidence adapter for T002 (Gate A slice). Architecture
    // decision A1 (plan.md): adapters translate trusted, server-observed runtime facts into typed
    // evidence; they never mutate ledgers. This adapter's ENGINE-FREE core builds the command +
    // evidence digest from already-server-validated facts and enforces the minimal placement gate
    // (inside Stone Area, placement succeeded, a stable piece id). The net48 Harmony/ZNetScene
    // observation that produces those facts is the thin outer shell (deferred to T008 hardening);
    // this core is what the contract tests exercise and what shipping code calls.
    //
    // Client claims are NEVER the source of truth (contracts.md): the position, area result, and
    // placement outcome passed here are server-observed. The claim carried on the command is only
    // compared by the pipeline, never trusted.
    //
    // net48 audit: string/CultureInfo/value objects only. No net5+ API, no UnityEngine/Valheim
    // reference in this core file, so it link-compiles into the net8 test project.

    /// <summary>Server-observed facts about one Foundational placement event. Every field is
    /// attributed by trusted server code (contracts.md FoundationalPlacementEvidence); no field is
    /// taken from the client payload.</summary>
    public readonly struct FoundationalPlacementEvidence
    {
        public FoundationalPlacementEvidence(
            OperationId operationId,
            StoneId stoneId,
            string stablePieceId,
            string pieceInstanceProvenance,
            bool insideStoneArea,
            bool placementSucceeded,
            string foundationalCatalogVersion)
        {
            OperationId = operationId;
            StoneId = stoneId;
            StablePieceId = stablePieceId;
            PieceInstanceProvenance = pieceInstanceProvenance;
            InsideStoneArea = insideStoneArea;
            PlacementSucceeded = placementSucceeded;
            FoundationalCatalogVersion = foundationalCatalogVersion;
        }

        public OperationId OperationId { get; }
        public StoneId StoneId { get; }
        public string StablePieceId { get; }
        public string PieceInstanceProvenance { get; }
        public bool InsideStoneArea { get; }
        public bool PlacementSucceeded { get; }
        public string FoundationalCatalogVersion { get; }
    }

    public enum PlacementAdmission
    {
        Admitted,
        OutsideStoneArea,
        PlacementFailed,
        MissingPieceIdentity
    }

    public readonly struct PlacementAdmissionResult
    {
        public PlacementAdmissionResult(PlacementAdmission admission, FoundationalPlacementCommand command)
        {
            Admission = admission;
            Command = command;
        }

        public PlacementAdmission Admission { get; }
        public bool IsAdmitted => Admission == PlacementAdmission.Admitted;

        /// <summary>Only meaningful when <see cref="IsAdmitted"/>. The command the pipeline handles.</summary>
        public FoundationalPlacementCommand Command { get; }
    }

    public sealed class FoundationalPlacementAdapter
    {
        /// <summary>Translate server-observed placement evidence into a pipeline command. The
        /// connection/claim identity is attributed by the transport (out-of-band) and threaded
        /// through unchanged; the pipeline authenticates it. A placement that is outside the Stone
        /// Area, failed, or lacks a stable piece identity is not admitted and produces no command.</summary>
        public PlacementAdmissionResult Admit(
            FoundationalPlacementEvidence evidence,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim)
        {
            if (!evidence.PlacementSucceeded)
                return Rejected(PlacementAdmission.PlacementFailed);
            if (!evidence.InsideStoneArea)
                return Rejected(PlacementAdmission.OutsideStoneArea);
            if (string.IsNullOrEmpty(evidence.StablePieceId))
                return Rejected(PlacementAdmission.MissingPieceIdentity);

            var command = new FoundationalPlacementCommand(
                evidence.OperationId,
                evidence.StoneId,
                connection,
                claim,
                BuildEvidenceDigest(evidence));
            return new PlacementAdmissionResult(PlacementAdmission.Admitted, command);
        }

        /// <summary>Stable digest of the server-observed evidence. Bound into the receipt so a
        /// replayed operationId carrying a different placement rejects as OperationConflict, and so
        /// the receipt records exact provenance (data-model.md receipt fields).</summary>
        public static string BuildEvidenceDigest(FoundationalPlacementEvidence e)
        {
            string material = string.Join("|", new[]
            {
                "foundational-placement",
                e.StoneId.Value,
                e.StablePieceId,
                e.PieceInstanceProvenance ?? string.Empty,
                e.InsideStoneArea ? "1" : "0",
                e.FoundationalCatalogVersion ?? string.Empty
            });
            return OperationReceiptStore.Digest(material);
        }

        private static PlacementAdmissionResult Rejected(PlacementAdmission admission) =>
            new PlacementAdmissionResult(admission, default);
    }
}
