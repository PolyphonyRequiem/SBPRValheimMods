// ============================================================================
//  QA-M3R real fixture adapter (t_1572d041) — engine-free request→plan mapping.
// ----------------------------------------------------------------------------
//  FixtureRequestMapper — turns an admitted server-fixture verb + its typed args
//  into an inert FixturePlan for the owned-resource ledger. It is the single
//  place a wire request becomes a fixture intent, so the "only vanilla, only
//  allowlisted, only bounded" firewall (ADR-0009 §4) is applied in one reviewed
//  spot BEFORE any world side effect:
//
//    * Only the three fixture verbs map to a plan (SpawnStation / PlaceVanillaPiece
//      / GrantVanillaMaterials). Any other verb is refused here — a mapping is
//      never invented for an action/observation/lifecycle verb.
//    * The logical id must be a STRING arg exactly as the catalog declares; a
//      product id (SBPR_ prefix / denylist) is refused with ProductId even before
//      the allowlist is consulted (defence in depth over the validator).
//    * Counts/radius come straight from the typed, already-bounds-checked args;
//      the resulting FixturePlan is then re-validated against the real
//      VanillaFixtureManifest allowlist + bounds so a mapping bug cannot bypass
//      the plan validator.
//
//  Engine-free: System.* only. No product identity/AP/ownership/signature/verdict.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    /// <summary>Why a verb+args could not be mapped to a fixture plan (None = mapped).</summary>
    public enum FixtureMapReason
    {
        None = 0,
        NotAFixtureVerb,     // the verb is not one of the server fixture verbs
        MissingArg,          // a required typed arg is absent or the wrong type
        ProductId,           // the requested logical id names product state (refused pre-allowlist)
        PlanRejected,        // the built plan failed manifest allowlist/bounds validation
    }

    /// <summary>The result of mapping a verb+args to a validated fixture plan.</summary>
    public sealed class FixtureMapResult
    {
        private FixtureMapResult(FixtureMapReason reason, ValidatedFixturePlan? plan,
            PlanRejectionReason planReason, string detail)
        {
            Reason = reason;
            Plan = plan;
            PlanReason = planReason;
            Detail = detail ?? string.Empty;
        }

        public FixtureMapReason Reason { get; }
        public bool Ok => Reason == FixtureMapReason.None && Plan != null;

        /// <summary>The validated, expanded plan when Ok.</summary>
        public ValidatedFixturePlan? Plan { get; }

        /// <summary>The underlying plan-validator reason when <see cref="Reason"/> is PlanRejected.</summary>
        public PlanRejectionReason PlanReason { get; }

        public string Detail { get; }

        public static FixtureMapResult Accept(ValidatedFixturePlan plan) =>
            new(FixtureMapReason.None, plan, PlanRejectionReason.None, string.Empty);

        public static FixtureMapResult Reject(FixtureMapReason reason, string detail) =>
            new(reason, null, PlanRejectionReason.None, detail);

        public static FixtureMapResult RejectPlan(PlanRejectionReason planReason, string offendingId) =>
            new(FixtureMapReason.PlanRejected, null, planReason, offendingId);
    }

    /// <summary>Pure mapping from an admitted fixture verb + args to a validated fixture plan.</summary>
    public static class FixtureRequestMapper
    {
        /// <summary>The catalog verbs this mapper turns into fixture plans (server-role fixtures).</summary>
        public const string SpawnStation = "SpawnStation";
        public const string PlaceVanillaPiece = "PlaceVanillaPiece";
        public const string GrantVanillaMaterials = "GrantVanillaMaterials";

        /// <summary>
        /// Map one admitted fixture verb (with its typed args) to a single-spec, validated plan.
        /// <paramref name="fixtureId"/> scopes the deterministic owned-resource ids (per run/request).
        /// Fail-closed: an unknown verb, a missing/mistyped arg, a product id, or a plan that fails
        /// manifest validation all return a typed rejection and NO plan.
        /// </summary>
        public static FixtureMapResult Map(
            string fixtureId, string? verb, IReadOnlyDictionary<string, object?> args)
        {
            if (string.IsNullOrEmpty(fixtureId)) throw new ArgumentException("fixtureId must be non-empty", nameof(fixtureId));
            if (args == null) throw new ArgumentNullException(nameof(args));

            ResourceSpec spec;
            switch (verb)
            {
                case SpawnStation:
                case PlaceVanillaPiece:
                {
                    if (!TryGetString(args, "prefab", out var prefab))
                        return FixtureMapResult.Reject(FixtureMapReason.MissingArg, "prefab");
                    if (!TryGetRadius(args, "posRadius", out var radius))
                        return FixtureMapResult.Reject(FixtureMapReason.MissingArg, "posRadius");
                    // Defence in depth: refuse a product id before the allowlist is even consulted.
                    if (VanillaFixtureManifest.IsProductId(prefab))
                        return FixtureMapResult.Reject(FixtureMapReason.ProductId, prefab);
                    spec = new ResourceSpec(prefab, 1, radius);
                    break;
                }
                case GrantVanillaMaterials:
                {
                    if (!TryGetString(args, "itemId", out var itemId))
                        return FixtureMapResult.Reject(FixtureMapReason.MissingArg, "itemId");
                    if (!TryGetCount(args, "qty", out var qty))
                        return FixtureMapResult.Reject(FixtureMapReason.MissingArg, "qty");
                    if (VanillaFixtureManifest.IsProductId(itemId))
                        return FixtureMapResult.Reject(FixtureMapReason.ProductId, itemId);
                    // Materials carry no placement radius (granted into inventory / dropped at origin).
                    spec = new ResourceSpec(itemId, qty, 0.0);
                    break;
                }
                default:
                    return FixtureMapResult.Reject(FixtureMapReason.NotAFixtureVerb, verb ?? string.Empty);
            }

            var allow = VanillaFixtureManifest.BuildAllowlist();
            var plan = new FixturePlan(fixtureId, new[] { spec });
            var validated = FixturePlanValidator.Validate(plan, allow, VanillaFixtureManifest.Bounds);
            if (!validated.Accepted || validated.Plan == null)
                return FixtureMapResult.RejectPlan(validated.Reason, validated.OffendingLogicalId);

            return FixtureMapResult.Accept(validated.Plan);
        }

        private static bool TryGetString(IReadOnlyDictionary<string, object?> args, string name, out string value)
        {
            value = string.Empty;
            if (!args.TryGetValue(name, out var raw) || raw is not string s || string.IsNullOrEmpty(s)) return false;
            value = s;
            return true;
        }

        private static bool TryGetCount(IReadOnlyDictionary<string, object?> args, string name, out int count)
        {
            count = 0;
            if (!args.TryGetValue(name, out var raw)) return false;
            long l;
            if (raw is long ll) l = ll;
            else if (raw is int ii) l = ii;
            else return false;
            if (l < 1 || l > int.MaxValue) return false;
            count = (int)l;
            return true;
        }

        private static bool TryGetRadius(IReadOnlyDictionary<string, object?> args, string name, out double radius)
        {
            radius = 0.0;
            if (!args.TryGetValue(name, out var raw)) return false;
            double d;
            if (raw is double dd) d = dd;
            else if (raw is long ll) d = ll;
            else if (raw is int ii) d = ii;
            else if (raw is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var ds)) d = ds;
            else return false;
            if (double.IsNaN(d) || double.IsInfinity(d) || d < 0) return false;
            radius = d;
            return true;
        }
    }
}
