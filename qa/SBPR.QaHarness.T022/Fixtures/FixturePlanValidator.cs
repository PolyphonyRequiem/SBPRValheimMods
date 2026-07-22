// ============================================================================
//  QA-M3 fixture ledger core (canonical, t_4db82cc0) — engine-free.
//  Adopted from the reviewed parallel prebuild (t_b5413567); namespace re-homed
//  into SBPR.QaHarness.T022.Core.Fixtures. Consumed under net48 by the helper and
//  link-compiled by the net8 tests-core suite. Still System.* only.
// ----------------------------------------------------------------------------
//  FixturePlanValidator — turns an inert FixturePlan into either a rejection
//  (with a typed reason) or a deterministic, expanded ValidatedFixturePlan whose
//  OwnedResourceIds are a pure function of (fixtureId, logicalId, ordinal).
//
//  Every rejection an adversarial caller can trigger is enumerated in
//  PlanRejectionReason. The validator NEVER touches the world — it is a pure
//  function of the plan + allowlist + bounds, so the same inputs always yield the
//  same expansion (the property idempotency and crash recovery both lean on).
//
//  Engine-free: System.* only.
// ============================================================================

using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    public enum PlanRejectionReason
    {
        None = 0,
        UnknownLogicalId,          // spec references an id not on the allowlist
        DuplicateLogicalId,        // same logical id listed twice in one plan (conflict)
        DistinctResourceOverflow,  // more distinct specs than bounds allow
        CountOverflow,             // a single spec count exceeds the per-resource bound
        TotalObjectOverflow,       // sum of counts exceeds the total-object bound
        RadiusOutOfBounds          // a spec radius exceeds the bound (or is non-finite)
    }

    /// <summary>The outcome of validating a plan: either Accepted (with the expanded, deterministic
    /// owned-resource id list) or a typed rejection. Immutable value.</summary>
    public sealed class PlanValidationResult
    {
        private PlanValidationResult(bool accepted, PlanRejectionReason reason, string offendingLogicalId,
            ValidatedFixturePlan? plan)
        {
            Accepted = accepted;
            Reason = reason;
            OffendingLogicalId = offendingLogicalId ?? string.Empty;
            Plan = plan;
        }

        public bool Accepted { get; }
        public PlanRejectionReason Reason { get; }
        public string OffendingLogicalId { get; }
        public ValidatedFixturePlan? Plan { get; }

        public static PlanValidationResult Accept(ValidatedFixturePlan plan) =>
            new PlanValidationResult(true, PlanRejectionReason.None, string.Empty, plan);

        public static PlanValidationResult Reject(PlanRejectionReason reason, string offendingLogicalId) =>
            new PlanValidationResult(false, reason, offendingLogicalId, null);
    }

    /// <summary>An accepted plan expanded into its deterministic, ordered owned resources. Each entry
    /// pairs an OwnedResourceId with the resolved (non-product) category and the source spec.</summary>
    public sealed class ValidatedFixturePlan
    {
        public ValidatedFixturePlan(string fixtureId, IReadOnlyList<PlannedResource> resources)
        {
            FixtureId = fixtureId;
            Resources = resources;
        }

        public string FixtureId { get; }
        public IReadOnlyList<PlannedResource> Resources { get; }
    }

    public readonly struct PlannedResource
    {
        public PlannedResource(OwnedResourceId id, ResourceCategory category, string logicalId, double radiusMeters)
        {
            Id = id;
            Category = category;
            LogicalId = logicalId;
            RadiusMeters = radiusMeters;
        }

        public OwnedResourceId Id { get; }
        public ResourceCategory Category { get; }
        public string LogicalId { get; }
        public double RadiusMeters { get; }
    }

    /// <summary>Pure plan validation. No world access, no randomness — deterministic in its inputs.</summary>
    public static class FixturePlanValidator
    {
        public static PlanValidationResult Validate(FixturePlan plan, ResourceAllowlist allowlist, FixtureBounds bounds)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (allowlist == null) throw new ArgumentNullException(nameof(allowlist));

            var specs = plan.Specs;

            if (specs.Count > bounds.MaxDistinctResources)
                return PlanValidationResult.Reject(PlanRejectionReason.DistinctResourceOverflow, string.Empty);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            long total = 0;
            var resources = new List<PlannedResource>();

            foreach (var spec in specs)
            {
                // Duplicate logical id within one plan is an intra-plan conflict.
                if (!seen.Add(spec.LogicalId))
                    return PlanValidationResult.Reject(PlanRejectionReason.DuplicateLogicalId, spec.LogicalId);

                // Unknown logical id: never assume a prefab.
                if (!allowlist.TryGetCategory(spec.LogicalId, out var category))
                    return PlanValidationResult.Reject(PlanRejectionReason.UnknownLogicalId, spec.LogicalId);

                if (spec.Count > bounds.MaxCountPerResource)
                    return PlanValidationResult.Reject(PlanRejectionReason.CountOverflow, spec.LogicalId);

                if (spec.RadiusMeters > bounds.MaxRadiusMeters)
                    return PlanValidationResult.Reject(PlanRejectionReason.RadiusOutOfBounds, spec.LogicalId);

                total += spec.Count;
                if (total > bounds.MaxTotalObjects)
                    return PlanValidationResult.Reject(PlanRejectionReason.TotalObjectOverflow, spec.LogicalId);

                for (int ordinal = 0; ordinal < spec.Count; ordinal++)
                {
                    var id = new OwnedResourceId(plan.FixtureId, spec.LogicalId, ordinal);
                    resources.Add(new PlannedResource(id, category, spec.LogicalId, spec.RadiusMeters));
                }
            }

            return PlanValidationResult.Accept(new ValidatedFixturePlan(plan.FixtureId, resources));
        }
    }
}
