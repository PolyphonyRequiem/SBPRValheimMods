using System;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Crafting
{
    // T021 — Refined Workshop: real-versus-effective station-level policy (spec §Acceptance scenario 2
    // "Crafting"; contracts.md §"Effect delivery contracts" → EffectiveStationLevelProvider; plan.md
    // Tracer 6; tasks.md T021, acceptance AT-REFINED-REAL-VS-EFFECTIVE).
    //
    // Accepted contract encoded here (verbatim scope, do NOT widen):
    //   * Refined Workshop supplies +1 for eligible portable-item production/upgrade/repair inside the
    //     active Homestead. A qualifying real Level-2 station may perform an eligible effective-Level-3
    //     operation; the same real station WITHOUT the active Local Effect cannot.
    //   * The REAL observed station level remains unchanged and visible. This provider NEVER mutates the
    //     real level — it returns the real level alongside the derived effective level so the UI can
    //     distinguish "real" from "+1".
    //   * The +1 does NOT: unlock building pieces/permissions, affect STRUCTURE production, satisfy a
    //     Stone-level place-state objective, or apply to non-portable/ineligible outputs. Structure and
    //     build-placement operations always resolve to the real level with no bonus (structure/build
    //     gates are preserved untouched — this provider returns a pure level projection and issues no
    //     capability).
    //   * Activation is derived, never stored: the Local Effect must be currently ACTIVE for this
    //     occupant (LocalEffectActivationView — developed + committed Tree + Active Stone Level + a
    //     present authorized Governor + inside the Stone Area + Settlement Local policy eligibility).
    //     Relationship release, policy change, exiting the Area, or a missing Governor re-derive the
    //     bonus away with zero writes.
    //
    // This is a PURE projection (architecture decision A1, plan.md): the crafting adapter surface T022–
    // T023 extend. It reads no ledger and writes nothing.
    //
    // net48 audit: only System + the engine-free activation view / snapshot value objects. No net5+
    // surface, no UnityEngine/Valheim/BepInEx reference, so this link-compiles into the net8 tests.

    /// <summary>The station operation being evaluated. Only the three portable-item kinds are eligible
    /// for the Refined Workshop +1; structure production and build placement never receive it (spec
    /// scenario 2: "does not unlock building pieces/permissions, affect structure production, ...").</summary>
    public enum CraftingOperationKind
    {
        /// <summary>Producing a new portable item at the station.</summary>
        PortableItemProduction,
        /// <summary>Upgrading an existing portable item at the station.</summary>
        PortableItemUpgrade,
        /// <summary>Repairing a portable item at the station.</summary>
        PortableItemRepair,
        /// <summary>Producing/raising a structure (building piece). Never eligible.</summary>
        StructureProduction,
        /// <summary>Placing/building a piece. Never eligible; gated separately by build Permission.</summary>
        BuildPlacement
    }

    /// <summary>Pure result of a station-level resolution. Carries BOTH the unchanged real observed level
    /// and the derived effective level so a caller/UI can render "real" distinctly from the "+1".</summary>
    public readonly struct EffectiveStationLevel
    {
        public EffectiveStationLevel(int realStationLevel, int effectiveStationLevel,
            bool bonusApplied, CraftingOperationKind operation)
        {
            RealStationLevel = realStationLevel;
            EffectiveStationLevelValue = effectiveStationLevel;
            BonusApplied = bonusApplied;
            Operation = operation;
        }

        /// <summary>The real, server-observed station level. NEVER mutated by Refined Workshop; always the
        /// value the station actually has and the UI must keep showing.</summary>
        public int RealStationLevel { get; }

        /// <summary>The effective level for THIS operation: real level plus the Refined Workshop +1 when
        /// (and only when) the bonus applies. Equal to <see cref="RealStationLevel"/> otherwise.</summary>
        public int EffectiveStationLevelValue { get; }

        /// <summary>Whether the Refined Workshop +1 was applied to this operation.</summary>
        public bool BonusApplied { get; }

        /// <summary>The operation this resolution was computed for.</summary>
        public CraftingOperationKind Operation { get; }
    }

    /// <summary>Refined Workshop's real-versus-effective station-level policy. A pure derivation from the
    /// occupant's current Local Effect activation plus the trusted server-observed operation facts; it
    /// stores nothing and can never become a second authority (AT-NO-ACTIVE-LEDGER carries over from the
    /// shared grammar). The crafting nodes T022–T023 extend this same adapter surface.</summary>
    public static class EffectiveStationLevelProvider
    {
        /// <summary>Whether an operation kind is one of the three portable-item kinds eligible for the +1.</summary>
        public static bool IsPortableOperation(CraftingOperationKind operation) =>
            operation == CraftingOperationKind.PortableItemProduction
            || operation == CraftingOperationKind.PortableItemUpgrade
            || operation == CraftingOperationKind.PortableItemRepair;

        /// <summary>Resolve the effective station level for one operation. The +1 is applied only when the
        /// Refined Workshop Local Effect is currently active for this occupant AND the operation is an
        /// eligible portable-item production/upgrade/repair on an eligible portable item AND a real
        /// station is present (real level ≥ 1). In every other case the effective level equals the
        /// unchanged real observed level.</summary>
        /// <param name="localEffects">The occupant's current Local Effect activation projection.</param>
        /// <param name="refinedWorkshopNode">Stable node id of the Refined Workshop Local node.</param>
        /// <param name="realStationLevel">The real, server-observed station level (unchanged, ≥ 0).</param>
        /// <param name="operation">The station operation being evaluated.</param>
        /// <param name="itemIsEligiblePortable">Whether the target output/item is an eligible portable
        /// item per the data-defined eligibility (ineligible/non-portable outputs never receive the +1).</param>
        public static EffectiveStationLevel Resolve(
            LocalEffectActivationView localEffects,
            VersionedId refinedWorkshopNode,
            int realStationLevel,
            CraftingOperationKind operation,
            bool itemIsEligiblePortable)
        {
            if (localEffects == null) throw new ArgumentNullException(nameof(localEffects));
            if (realStationLevel < 0)
                throw new ArgumentOutOfRangeException(nameof(realStationLevel),
                    "Real observed station level cannot be negative.");

            // The Refined Workshop Local Effect must be currently active for THIS occupant. Active already
            // folds in: developed Stone state + committed Crafting Tree + Active Stone Level ≥ node level +
            // an authorized Governor present + inside the Stone Area + Settlement Local policy eligibility.
            bool refinedActive = localEffects.StatusFor(refinedWorkshopNode).Active;

            // Structure production and build placement are never eligible: Refined Workshop "does not
            // unlock building pieces/permissions [or] affect structure production." A real station must
            // exist (level ≥ 1) — the +1 augments an existing station, it does not conjure one.
            bool eligible = refinedActive
                && itemIsEligiblePortable
                && IsPortableOperation(operation)
                && realStationLevel >= 1;

            int effective = eligible ? realStationLevel + 1 : realStationLevel;
            return new EffectiveStationLevel(realStationLevel, effective, eligible, operation);
        }
    }
}
