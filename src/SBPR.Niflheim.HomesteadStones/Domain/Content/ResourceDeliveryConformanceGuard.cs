using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SBPR.Niflheim.HomesteadStones.Domain.Content
{
    // RD-T001 (M0) — Resource Delivery current-truth and conformance guard.
    // ---------------------------------------------------------------------------
    // Named acceptance: AT-RD-023 (Mirrored telemetry == floored award) and
    // AT-RD-024 (conformance/docs/tests move together) for the IMPLEMENTATION
    // baseline, per docs/v2/planning/homestead-resource-delivery-{spec,plan,
    // data-model,contracts}.md (merged PR #327, main @ bacdc09).
    //
    // WHAT THIS GUARD IS FOR
    // ----------------------
    // The Resource Delivery package is a PROPOSED superseding slice. Until a later
    // behavior PR implements it, the shipped Homestead proof (20 authored = 13
    // executable + 7 unavailable, Mirrored Stone AP as receipt-compatible telemetry)
    // remains current truth. This guard makes the two mechanically distinguishable
    // BEFORE any behavior work, so an implementer cannot accidentally:
    //   * treat the proposed 21/14/7 Resource Delivery roster as if it were live;
    //   * change gameplay while claiming only to have added the guard;
    //   * ship first-slice AP without keeping Mirrored telemetry equal to the actual
    //     floored Personal/Cumulative award (AT-RD-023 / spec RD-023);
    //   * reconcile one surface (code) without the others (docs/manifest/conformance)
    //     (AT-RD-024 / spec RD-024).
    //
    // This file DELIBERATELY authors NO live gameplay. It does not extend the live
    // HomesteadProgressionCatalog roster, register a Humble node, or enable any
    // Resource Delivery outcome. The live roster stays 20/13/7 and every shipped
    // Homestead test stays green; that invariance is itself asserted here
    // (AssertShippedRosterUnchanged) so "guard-only, no behavior" is machine-checked.
    //
    // net48 audit: only System / System.Collections. Engine-free — no UnityEngine,
    // Valheim, or BepInEx surface — so it link-compiles into the net8 test project
    // exactly like HomesteadProgressionCatalog / ContentRegistryValidator.

    /// <summary>Whether a Resource Delivery content/contract shape is the shipped
    /// current truth or a proposed target that this slice does NOT yet implement.
    /// The whole point of RD-T001 is that these two are never confusable.</summary>
    public enum ContentTruthState
    {
        /// <summary>Shipped and live in the current proof build (the 20/13/7 roster,
        /// Mirrored-AP telemetry). Behavior may depend on it today.</summary>
        ShippedCurrentTruth = 0,

        /// <summary>Authored in the proposed Resource Delivery package but NOT wired
        /// into live gameplay. A later same-PR behavior slice must implement it; no
        /// runtime path may treat it as active until then.</summary>
        ProposedNotYetImplemented = 1
    }

    /// <summary>The three-number conformance target for an authored node roster:
    /// authored = executable + unavailable. Value type; equality is structural.</summary>
    public readonly struct RosterConformanceTarget : IEquatable<RosterConformanceTarget>
    {
        public RosterConformanceTarget(int authored, int executable, int unavailable)
        {
            Authored = authored;
            Executable = executable;
            Unavailable = unavailable;
        }

        public int Authored { get; }
        public int Executable { get; }
        public int Unavailable { get; }

        /// <summary>True when the three numbers are internally consistent
        /// (authored == executable + unavailable) and non-negative.</summary>
        public bool IsArithmeticallyConsistent =>
            Authored >= 0 && Executable >= 0 && Unavailable >= 0 &&
            Authored == Executable + Unavailable;

        public bool Equals(RosterConformanceTarget other) =>
            Authored == other.Authored && Executable == other.Executable && Unavailable == other.Unavailable;

        public override bool Equals(object? obj) => obj is RosterConformanceTarget o && Equals(o);
        public override int GetHashCode() => (Authored * 397 ^ Executable) * 397 ^ Unavailable;
        public override string ToString() => Authored + " = " + Executable + " executable + " + Unavailable + " unavailable";
    }

    /// <summary>One later-PR reconciliation obligation: a named surface a Resource
    /// Delivery behavior slice MUST move together with the docs (spec RD-024 /
    /// AT-RD-024). Recorded here so the boundary is enumerated, not remembered.</summary>
    public sealed class ReconciliationObligation
    {
        public ReconciliationObligation(string surface, string detail)
        {
            Surface = surface ?? throw new ArgumentNullException(nameof(surface));
            Detail = detail ?? string.Empty;
        }

        /// <summary>Stable identifier for the surface that must be reconciled
        /// (e.g. "ContentRegistry.roster", "ContentRegistryValidator.AssertRosterInvariant").</summary>
        public string Surface { get; }

        /// <summary>Human-readable description of what must change on that surface.</summary>
        public string Detail { get; }

        public override string ToString() => Surface + ": " + Detail;
    }

    /// <summary>Static current-truth + conformance guard for the Resource Delivery
    /// slice. Holds the shipped roster (projected from the live catalog constants,
    /// so it can never silently disagree with them) alongside the PROPOSED target as
    /// independent literals, and the pure invariants RD-T001 must lock.</summary>
    public static class ResourceDeliveryConformanceGuard
    {
        // ── The shipped current truth, PROJECTED from the live catalog constants ──
        // Projected (not re-typed) so this guard and the live roster read one source
        // and cannot drift — same discipline SpecCheck uses against SbprContentManifest.
        public static readonly RosterConformanceTarget ShippedRoster =
            new RosterConformanceTarget(
                HomesteadProgressionCatalog.ExpectedAuthoredNodeCount,     // 20
                HomesteadProgressionCatalog.ExpectedExecutableNodeCount,   // 13
                HomesteadProgressionCatalog.ExpectedUnavailableNodeCount); //  7

        // ── The PROPOSED Resource Delivery target, as INDEPENDENT literals ──
        // data-model.md §"Aggregate 6 — ContentRegistry extensions": first-slice roster
        // "21 = 14 executable + 7 unavailable". spec RD-010 / §Implementation Decisions:
        // appending the Foundational Humble node changes 20 = 13 + 7 -> 21 = 14 + 7.
        // These are DELIBERATELY hard literals so a fat-fingered future catalog edit is
        // diffed against the locked design numbers, exactly like SpecCheck's Portal
        // Energy manifest. They are NOT read by any live gameplay path.
        public static readonly RosterConformanceTarget ProposedResourceDeliveryRoster =
            new RosterConformanceTarget(authored: 21, executable: 14, unavailable: 7);

        /// <summary>The proposed roster is authored but not yet implemented. Any code
        /// asserting "is Resource Delivery live?" must read this and get a hard NO
        /// until the behavior slice flips it in the SAME PR that implements it.</summary>
        public const ContentTruthState ProposedRosterState = ContentTruthState.ProposedNotYetImplemented;

        /// <summary>The single first-slice node whose addition moves the roster from
        /// 20/13/7 to 21/14/7: the Foundational Humble Homesteader's Bundle. Authored
        /// identity only; no live NodeDefinition is created (spec RD-012, cost 1 BP).</summary>
        public const string HumbleNodeKey = "HumbleHomesteadersBundle";

        /// <summary>Humble's authored BP development cost (spec RD-012 / contracts
        /// §ApplyBPToNode: "Humble's authored cost is 1 BP"). Independent literal.</summary>
        public const int HumbleAuthoredBpCost = 1;

        // ── The same-PR reconciliation obligations a behavior slice MUST honor ──
        // spec RD-024 / AT-RD-024: "Implementation MUST update affected specs, content
        // manifests/conformance, code, automated tests, and joined-client evidence
        // together." Enumerated so no surface is silently skipped.
        private static readonly ReadOnlyCollection<ReconciliationObligation> _obligations =
            new ReadOnlyCollection<ReconciliationObligation>(new List<ReconciliationObligation>
            {
                new ReconciliationObligation(
                    "HomesteadProgressionCatalog.roster",
                    "Append the Foundational Humble node and bump the Expected*NodeCount consts to 21/14/7."),
                new ReconciliationObligation(
                    "ContentRegistryValidator.AssertRosterInvariant",
                    "Update the roster arithmetic (authored/executable + executable Level partition) to admit Humble."),
                new ReconciliationObligation(
                    "docs/v2/planning/homestead-resource-delivery-data-model.md",
                    "Keep §Aggregate 6 '21 = 14 executable + 7 unavailable' in sync with the code roster."),
                new ReconciliationObligation(
                    "docs/v2/planning/homestead-resource-delivery-spec.md",
                    "Keep RD-010/RD-012 and the RD-023/RD-024 acceptance rows in sync with implemented behavior."),
                new ReconciliationObligation(
                    "Mirrored Stone AP telemetry",
                    "Keep the Mirrored delta equal to the actual floored Personal/Cumulative award (RD-023); never read/debit it."),
                new ReconciliationObligation(
                    "automated tests + joined-client evidence",
                    "Move conformance tests and in-world evidence together; logs-green is not playable-proven."),
            });

        /// <summary>The enumerated set of surfaces a later Resource Delivery behavior
        /// PR must reconcile in lockstep. Read-only; identity is the surface list.</summary>
        public static IReadOnlyList<ReconciliationObligation> ReconciliationObligations => _obligations;

        // ── Pure invariants (the guard proper) ──

        /// <summary>AT-RD-024 baseline: the proposed target is a well-formed
        /// superset of the shipped truth — arithmetically consistent, adding exactly
        /// one executable node, keeping unavailable unchanged, and remaining a PROPOSED
        /// (not live) shape. Throws <see cref="InvalidOperationException"/> on any drift
        /// so a bad edit fails a test, per AGENTS.md "spec and code move together".</summary>
        public static void AssertProposedSupersessionShape()
        {
            if (!ShippedRoster.IsArithmeticallyConsistent)
                throw new InvalidOperationException(
                    "Shipped roster is not arithmetically consistent: " + ShippedRoster + ". " +
                    "The live catalog constants drifted (authored != executable + unavailable).");

            if (!ProposedResourceDeliveryRoster.IsArithmeticallyConsistent)
                throw new InvalidOperationException(
                    "Proposed Resource Delivery roster is not arithmetically consistent: " +
                    ProposedResourceDeliveryRoster + " (must satisfy authored == executable + unavailable).");

            // Appending the Foundational Humble node adds exactly one AUTHORED and one
            // EXECUTABLE node, and touches NO unavailable node (spec §Implementation
            // Decisions: "20 = 13 executable + 7 unavailable" -> "21 = 14 executable + 7 unavailable").
            int authoredDelta = ProposedResourceDeliveryRoster.Authored - ShippedRoster.Authored;
            int executableDelta = ProposedResourceDeliveryRoster.Executable - ShippedRoster.Executable;
            int unavailableDelta = ProposedResourceDeliveryRoster.Unavailable - ShippedRoster.Unavailable;

            if (authoredDelta != 1)
                throw new InvalidOperationException(
                    "Proposed roster must author exactly one more node than shipped (the Humble bundle); " +
                    "authored delta was " + authoredDelta + " (shipped " + ShippedRoster + " -> proposed " +
                    ProposedResourceDeliveryRoster + ").");
            if (executableDelta != 1)
                throw new InvalidOperationException(
                    "Proposed roster must add exactly one executable node (Humble); executable delta was " +
                    executableDelta + ".");
            if (unavailableDelta != 0)
                throw new InvalidOperationException(
                    "Proposed roster must not change the unavailable-node count; unavailable delta was " +
                    unavailableDelta + " (spec holds it at 7).");

            if (ProposedRosterState != ContentTruthState.ProposedNotYetImplemented)
                throw new InvalidOperationException(
                    "Resource Delivery roster must remain ProposedNotYetImplemented until a behavior slice " +
                    "implements it in the same PR that flips this state. No guard-only PR may mark it live.");
        }

        /// <summary>AT-RD-024 baseline: this guard changed no live gameplay. The live
        /// catalog roster MUST still be the shipped 20/13/7 proof — the proposed target
        /// is data, not behavior. Throws if the guard's presence somehow moved the live
        /// roster (e.g. someone wired the Humble node in "just to make it compile").</summary>
        public static void AssertShippedRosterUnchanged(ContentRegistryValidator validator)
        {
            if (validator == null) throw new ArgumentNullException(nameof(validator));

            // AssertRosterInvariant throws if the live roster is not exactly 20 = 13 + 7
            // with the 12/1 executable Level partition. If this ever fails while only the
            // guard was added, behavior leaked — which RD-T001 forbids.
            validator.AssertRosterInvariant();

            var live = validator.CountRoster();
            var liveTarget = new RosterConformanceTarget(live.Authored, live.Executable, live.Unavailable);
            if (!liveTarget.Equals(ShippedRoster))
                throw new InvalidOperationException(
                    "Live roster " + liveTarget + " no longer equals the shipped current truth " +
                    ShippedRoster + ". RD-T001 is a guard only; it must enable NO Resource Delivery behavior.");

            if (liveTarget.Equals(ProposedResourceDeliveryRoster))
                throw new InvalidOperationException(
                    "Live roster has already advanced to the PROPOSED Resource Delivery target " +
                    ProposedResourceDeliveryRoster + ". That is a behavior change and is not authorized by RD-T001.");
        }

        /// <summary>Compute the floored Personal AP award for an otherwise-authorized
        /// event under the Resource Delivery multiplier rule (spec RD-009 /
        /// contracts §RecordApActivity): award = floor(baseAp * participation * maturity),
        /// flooring ONCE after full multiplication. Exact integer/rational math — no
        /// floating-point accumulation is authoritative (data-model.md rule 6). The
        /// maturity multiplier is expressed as a rational (numerator/denominator) so the
        /// authored 1.0×–1.5× bands stay exact (e.g. 1.1× = 11/10).</summary>
        /// <param name="baseAp">Authored base AP for the source event (non-negative).</param>
        /// <param name="participationMultiplier">0, 1, or 2 (the 0×/1×/2× tiers).</param>
        /// <param name="maturityNumerator">Maturity band numerator (e.g. 11 for 1.1×).</param>
        /// <param name="maturityDenominator">Maturity band denominator (e.g. 10 for 1.1×).</param>
        public static int FlooredPersonalApAward(
            int baseAp, int participationMultiplier, int maturityNumerator, int maturityDenominator)
        {
            if (baseAp < 0) throw new ArgumentOutOfRangeException(nameof(baseAp));
            if (participationMultiplier < 0) throw new ArgumentOutOfRangeException(nameof(participationMultiplier));
            if (maturityNumerator < 0) throw new ArgumentOutOfRangeException(nameof(maturityNumerator));
            if (maturityDenominator <= 0) throw new ArgumentOutOfRangeException(nameof(maturityDenominator));

            // floor((baseAp * participation * maturityNum) / maturityDen). All operands
            // are non-negative integers, so C# integer division IS the floor. long guards
            // against overflow before the single floor.
            long numerator = (long)baseAp * participationMultiplier * maturityNumerator;
            return (int)(numerator / maturityDenominator);
        }

        /// <summary>AT-RD-023 baseline: the first-slice Mirrored Stone AP telemetry
        /// delta for an AP-producing operation MUST equal the actual floored
        /// Personal/Cumulative award — never a pre-floor value, a doubled value, or a
        /// separately-thresholded currency (spec RD-023, data-model §Aggregate 5:
        /// "required first-slice Mirrored telemetry delta equal to the recorded final
        /// award"). Returns true iff the recorded Mirrored delta matches the floored
        /// award for the given inputs; the telemetry mirrors the award, it does not
        /// re-derive it.</summary>
        public static bool MirroredDeltaEqualsFlooredAward(
            int recordedMirroredDelta, int baseAp, int participationMultiplier,
            int maturityNumerator, int maturityDenominator)
        {
            int flooredAward = FlooredPersonalApAward(
                baseAp, participationMultiplier, maturityNumerator, maturityDenominator);
            return recordedMirroredDelta == flooredAward;
        }
    }
}
