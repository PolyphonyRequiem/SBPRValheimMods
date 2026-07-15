using System;
using System.Collections.Generic;
using System.Globalization;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Activities
{
    // The one trusted Foundational-placement evidence adapter. Architecture decision A1 (plan.md):
    // adapters translate trusted, server-observed runtime facts into typed evidence; they never mutate
    // ledgers. This adapter's ENGINE-FREE core builds the command + evidence digest from
    // already-server-validated facts and enforces the full Foundational placement gate.
    //
    // T002 (Gate A slice) enforced the minimal gate: inside Stone Area, placement succeeded, a stable
    // piece id. T008 HARDENS this into the protected ongoing AP source (data-model.md §"Credit
    // Foundational AP"; contracts.md RecordFoundationalPlacement): the server now additionally observes
    //   * exact Foundational construction-catalog MEMBERSHIP of the placed stable piece id;
    //   * explicit EXCLUSIONS (a held-out stable id never earns, even if placeable);
    //   * current-build catalog VERSION (a stale/unknown catalog version is an out-of-build reference);
    //   * anti-REPETITION policy (the same physical piece instance is credited at most once; a fresh
    //     op that re-triggers credit for an already-credited instance is suppressed with no receipt).
    // Authenticated actor, active Attunement, and repetition of the AP amount stay the pipeline's job;
    // Tree commitment NEVER disables this baseline (there is no commit gate here — the source is
    // always available while the relationship holds, which the RelationshipPlacementAuthorizer proves).
    //
    // Client claims are NEVER the source of truth (contracts.md): the position, area result, placement
    // outcome, piece identity, and catalog version passed here are server-observed. The claim carried on
    // the command is only compared by the pipeline, never trusted.
    //
    // net48 audit: string/CultureInfo/value objects + engine-free content catalog only. No net5+ API,
    // no UnityEngine/Valheim reference in this core file, so it link-compiles into the net8 test project.

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
        MissingPieceIdentity,
        NotCatalogMember,       // stable id is not an authored current-build Foundational member
        ExcludedPiece,          // stable id is explicitly held out of the ongoing AP source
        StaleCatalogVersion,    // stamped catalog version is not the current build's
        RepetitionSuppressed    // the same physical piece instance was already credited by another op
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

    /// <summary>Server-owned anti-repetition policy for the ongoing Foundational AP source
    /// (data-model.md §"Credit Foundational AP": "anti-repetition policy"). Each PHYSICAL placed piece
    /// instance — identified by its stable pieceInstanceProvenance — may be credited at most once. A
    /// replay of the SAME operation that reserved the instance is allowed through (the receipt store is
    /// the idempotency authority and returns the one recorded result); a DIFFERENT operation that tries
    /// to re-earn AP for an already-credited instance is suppressed here with no receipt. Provisional
    /// proof policy (per-instance one-shot); event-granularity / decay tuning is deferred.</summary>
    public interface IPlacementRepetitionPolicy
    {
        /// <summary>Decide whether this (stone, provenance) placement may proceed to credit under
        /// <paramref name="operationId"/>. Returns true when the instance is unseen OR was reserved by
        /// this same operation (a replay). Returns false when a DIFFERENT operation already credited it.
        /// On a true "fresh" result the instance is recorded as reserved by this operation.</summary>
        bool TryAdmitPlacement(StoneId stoneId, string pieceInstanceProvenance, string operationId);
    }

    /// <summary>Engine-free in-memory reference anti-repetition policy. Keys a credited piece instance to
    /// the operation that reserved it, so a same-op replay is admitted (pipeline handles idempotency)
    /// while a distinct op re-crediting the same instance is suppressed. A null/empty provenance is
    /// always admitted (no instance to de-duplicate) — the catalog/area gates still apply.</summary>
    public sealed class InMemoryPlacementRepetitionPolicy : IPlacementRepetitionPolicy
    {
        private readonly Dictionary<string, string> _reservedByProvenance =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static string Key(StoneId stoneId, string provenance) => stoneId.Value + "|" + provenance;

        public bool TryAdmitPlacement(StoneId stoneId, string pieceInstanceProvenance, string operationId)
        {
            if (string.IsNullOrEmpty(pieceInstanceProvenance)) return true;
            string key = Key(stoneId, pieceInstanceProvenance);
            if (_reservedByProvenance.TryGetValue(key, out var reservedOp))
                return string.Equals(reservedOp, operationId, StringComparison.Ordinal); // same op == replay
            _reservedByProvenance[key] = operationId ?? string.Empty;
            return true;
        }
    }

    /// <summary>An always-admitting repetition policy. Used where anti-repetition is not exercised (the
    /// legacy Gate-A tests that predate T008). Never suppresses.</summary>
    public sealed class NoRepetitionPolicy : IPlacementRepetitionPolicy
    {
        public static readonly NoRepetitionPolicy Instance = new NoRepetitionPolicy();
        public bool TryAdmitPlacement(StoneId stoneId, string pieceInstanceProvenance, string operationId) => true;
    }

    public sealed class FoundationalPlacementAdapter
    {
        private readonly FoundationalPieceCatalog _catalog;
        private readonly IPlacementRepetitionPolicy _repetition;

        /// <summary>Default adapter: current-build Foundational catalog + no anti-repetition state. This
        /// keeps the T002 Gate-A construction shape working while catalog membership/exclusion/version
        /// gates are still enforced against the authored roster.</summary>
        public FoundationalPlacementAdapter()
            : this(FoundationalPieceCatalog.CurrentBuild, NoRepetitionPolicy.Instance) { }

        /// <summary>Production/ongoing adapter: an explicit current-build catalog and a stateful
        /// anti-repetition policy so the same physical piece instance is credited at most once.</summary>
        public FoundationalPlacementAdapter(FoundationalPieceCatalog catalog, IPlacementRepetitionPolicy repetition)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _repetition = repetition ?? throw new ArgumentNullException(nameof(repetition));
        }

        /// <summary>Translate server-observed placement evidence into a pipeline command. The
        /// connection/claim identity is attributed by the transport (out-of-band) and threaded
        /// through unchanged; the pipeline authenticates it. A placement is admitted ONLY when it
        /// succeeded, is inside the Stone Area, carries a stable piece id that is an authored
        /// current-build Foundational catalog member (and not an explicit exclusion), stamps the
        /// current catalog version, and is not a repeat credit of an already-credited physical
        /// instance. Any failing gate produces no command.</summary>
        public PlacementAdmissionResult Admit(
            FoundationalPlacementEvidence evidence,
            AuthenticatedConnection connection,
            ClaimedPrincipal claim,
            long? expectedStoneRevision = null,
            long? expectedCharacterRevision = null)
        {
            if (!evidence.PlacementSucceeded)
                return Rejected(PlacementAdmission.PlacementFailed);
            if (!evidence.InsideStoneArea)
                return Rejected(PlacementAdmission.OutsideStoneArea);
            if (string.IsNullOrEmpty(evidence.StablePieceId))
                return Rejected(PlacementAdmission.MissingPieceIdentity);

            // Catalog version must be the current build's: a stale/unknown catalog reference is
            // out-of-build and earns nothing (data-model.md: current-build definition required).
            if (!_catalog.IsCurrentCatalogVersion(evidence.FoundationalCatalogVersion))
                return Rejected(PlacementAdmission.StaleCatalogVersion);

            // Explicit exclusion wins over membership (checked first for precise diagnosis).
            if (_catalog.IsExcluded(evidence.StablePieceId))
                return Rejected(PlacementAdmission.ExcludedPiece);

            // Membership: only exact authored current-build catalog members are credit-eligible.
            if (!_catalog.IsCreditEligibleMember(evidence.StablePieceId))
                return Rejected(PlacementAdmission.NotCatalogMember);

            // Anti-repetition: the same physical piece instance is credited at most once. A same-op
            // replay is admitted (the receipt store is the idempotency authority); a different op
            // re-crediting an already-credited instance is suppressed with no receipt.
            if (!_repetition.TryAdmitPlacement(evidence.StoneId, evidence.PieceInstanceProvenance, evidence.OperationId.Value))
                return Rejected(PlacementAdmission.RepetitionSuppressed);

            var command = new FoundationalPlacementCommand(
                evidence.OperationId,
                evidence.StoneId,
                connection,
                claim,
                BuildEvidenceDigest(evidence),
                expectedStoneRevision,
                expectedCharacterRevision);
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
