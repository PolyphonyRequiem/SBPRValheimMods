using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Activities;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // T009 — the engine-free live-runtime seam. This is the ONE place a real, server-observed
    // successful Valheim placement becomes a Foundational AP receipt: it constructs the trusted
    // FoundationalPlacementEvidence from a FoundationalPlacementObservation, passes it through the
    // hardened FoundationalPlacementAdapter, and (only on admission) calls the existing
    // ProgressionCommandPipeline. Before T009 the adapter+pipeline were only ever assembled in tests
    // and the Gate-A repro harness; this service is the production composition of the same shipped
    // types, so the live path is provably identical to the tested path.
    //
    // Authority chain (never client-authoritative):
    //   * identity: the observation's ActingAccountId/ActingCharacterId are the BOUND INTERNAL session
    //     principal (server-minted at admission), and are handed to the pipeline as the AuthenticatedConnection. The claim
    //     is left EMPTY on purpose — the live server has no untrusted client-payload identity to
    //     compare, so PrincipalResolver binds straight from the connection.
    //   * authorization: the runtime consults the injected IFoundationalPlacementAuthorizer, wired in
    //     production to the relationship-backed RelationshipPlacementAuthorizer. There is deliberately
    //     NO permissive/test authorizer path here.
    //   * eligibility: the adapter enforces catalog membership/exclusion/version, Stone Area, success,
    //     and physical-instance anti-repetition before any receipt is written.
    //
    // Operator visibility: every observation produces exactly one RuntimePlacementOutcome, appended to
    // a bounded in-memory ring the operator command surface reads. Durable AP state is owned by the
    // OperationReceiptStore/authority journals injected into the pipeline — this service adds no second
    // source of truth.
    //
    // net48 audit: value objects + the shipped engine-free adapter/pipeline only. No net5+ surface,
    // no UnityEngine/Valheim, so it link-compiles into the net8 test project.
    public sealed class FoundationalPlacementRuntime
    {
        private readonly FoundationalPlacementAdapter _adapter;
        private readonly ProgressionCommandPipeline _pipeline;
        private readonly RuntimePlacementLog _log;

        public FoundationalPlacementRuntime(
            FoundationalPlacementAdapter adapter,
            ProgressionCommandPipeline pipeline,
            RuntimePlacementLog? log = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _log = log ?? new RuntimePlacementLog();
        }

        public RuntimePlacementLog Log => _log;

        /// <summary>Route one server-observed placement through the shipped domain pipeline. Returns
        /// the single outcome (also appended to the operator-readable log). A failing adapter gate
        /// produces a NotEarned outcome carrying the precise admission reason and writes NO receipt; an
        /// admitted placement is handed to the pipeline and the pipeline's terminal outcome is
        /// reported (Earned / Replayed / Rejected with the pipeline result code).</summary>
        public RuntimePlacementOutcome Observe(FoundationalPlacementObservation observation)
        {
            var operationId = observation.DeriveOperationId();

            var evidence = new FoundationalPlacementEvidence(
                operationId,
                observation.StoneId,
                observation.StablePieceId,
                observation.PieceInstanceProvenance,
                observation.InsideStoneArea,
                observation.PlacementSucceeded,
                observation.FoundationalCatalogVersion);

            // The live server binds the internal session principal out-of-band at admission; there is
            // no client claim to compare, so the claim is empty and PrincipalResolver binds from the
            // bound internal account/character alone (no provider lookup — AT-AIP-NO-PROVIDER-HOTPATH).
            var connection = new AuthenticatedConnection(observation.ActingAccountId, observation.ActingCharacterId);
            var claim = default(ClaimedPrincipal);

            var admission = _adapter.Admit(evidence, connection, claim);
            if (!admission.IsAdmitted)
            {
                var notEarned = RuntimePlacementOutcome.NotEarned(
                    operationId, observation.StoneId, observation.StablePieceId, admission.Admission);
                _log.Append(notEarned);
                return notEarned;
            }

            var result = _pipeline.Handle(admission.Command);
            var outcome = RuntimePlacementOutcome.FromPipeline(
                operationId, observation.StoneId, observation.StablePieceId, admission.Admission, result);
            _log.Append(outcome);
            return outcome;
        }
    }

    /// <summary>Whether a live observation ultimately credited AP, and why not when it did not.</summary>
    public enum RuntimePlacementDisposition
    {
        /// <summary>The pipeline applied a fresh Foundational AP receipt.</summary>
        Earned,
        /// <summary>The pipeline replayed an already-committed receipt (idempotent re-observation).</summary>
        Replayed,
        /// <summary>The adapter admitted the evidence but the pipeline rejected it (unauthorized,
        /// stale revision, or operation conflict — see <see cref="RuntimePlacementOutcome.ResultCode"/>).</summary>
        PipelineRejected,
        /// <summary>An adapter gate refused the evidence (outside area, failed, excluded, unknown,
        /// stale catalog, or physical-instance repetition — see <see cref="RuntimePlacementOutcome.Admission"/>).</summary>
        NotAdmitted
    }

    /// <summary>One operator-readable outcome for a single live placement observation. Bounded and
    /// PII-free by construction: it carries stable ids and result codes, never raw player names or
    /// secrets.</summary>
    public readonly struct RuntimePlacementOutcome
    {
        private RuntimePlacementOutcome(RuntimePlacementDisposition disposition, OperationId operationId,
            StoneId stoneId, string stablePieceId, PlacementAdmission admission, string resultCode,
            int mirroredStoneApDelta)
        {
            Disposition = disposition;
            OperationId = operationId;
            StoneId = stoneId;
            StablePieceId = stablePieceId ?? string.Empty;
            Admission = admission;
            ResultCode = resultCode ?? string.Empty;
            MirroredStoneApDelta = mirroredStoneApDelta;
        }

        public RuntimePlacementDisposition Disposition { get; }
        public OperationId OperationId { get; }
        public StoneId StoneId { get; }
        public string StablePieceId { get; }
        public PlacementAdmission Admission { get; }
        public string ResultCode { get; }
        public int MirroredStoneApDelta { get; }

        public bool Credited =>
            Disposition == RuntimePlacementDisposition.Earned || Disposition == RuntimePlacementDisposition.Replayed;

        internal static RuntimePlacementOutcome NotEarned(
            OperationId operationId, StoneId stoneId, string stablePieceId, PlacementAdmission admission) =>
            new RuntimePlacementOutcome(RuntimePlacementDisposition.NotAdmitted, operationId, stoneId,
                stablePieceId, admission, admission.ToString(), 0);

        internal static RuntimePlacementOutcome FromPipeline(
            OperationId operationId, StoneId stoneId, string stablePieceId, PlacementAdmission admission,
            FoundationalPlacementResult result)
        {
            RuntimePlacementDisposition disposition;
            switch (result.Outcome)
            {
                case CommandOutcome.Applied: disposition = RuntimePlacementDisposition.Earned; break;
                case CommandOutcome.Replayed: disposition = RuntimePlacementDisposition.Replayed; break;
                default: disposition = RuntimePlacementDisposition.PipelineRejected; break;
            }
            return new RuntimePlacementOutcome(disposition, operationId, stoneId, stablePieceId, admission,
                result.ResultCode, result.MirroredStoneApDelta);
        }

        /// <summary>One-line, PII-free operator log rendering.</summary>
        public string ToOperatorLine() =>
            $"[foundational-live] stone={StoneId.Value} piece={StablePieceId} op={OperationId.Value} " +
            $"disposition={Disposition} admission={Admission} result={ResultCode} mirroredDelta={MirroredStoneApDelta}";
    }

    /// <summary>A bounded, operator-readable ring of the most recent live placement outcomes. Bounded
    /// so a long-running server cannot leak memory through the diagnostic surface; it is a diagnostic
    /// projection, never a source of AP truth (that is the durable journal behind the pipeline).</summary>
    public sealed class RuntimePlacementLog
    {
        public const int DefaultCapacity = 256;

        private readonly int _capacity;
        private readonly List<RuntimePlacementOutcome> _recent;
        private readonly object _gate = new object();
        private long _total;
        private long _credited;

        public RuntimePlacementLog(int capacity = DefaultCapacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _recent = new List<RuntimePlacementOutcome>(capacity);
        }

        public void Append(RuntimePlacementOutcome outcome)
        {
            lock (_gate)
            {
                _total++;
                if (outcome.Credited) _credited++;
                if (_recent.Count >= _capacity) _recent.RemoveAt(0);
                _recent.Add(outcome);
            }
        }

        /// <summary>Total observations processed since boot (monotonic).</summary>
        public long TotalObserved { get { lock (_gate) return _total; } }

        /// <summary>How many observations credited (fresh + replay) since boot.</summary>
        public long TotalCredited { get { lock (_gate) return _credited; } }

        /// <summary>A snapshot copy of the recent-outcome ring, oldest first.</summary>
        public IReadOnlyList<RuntimePlacementOutcome> Recent()
        {
            lock (_gate) return new List<RuntimePlacementOutcome>(_recent);
        }
    }
}
