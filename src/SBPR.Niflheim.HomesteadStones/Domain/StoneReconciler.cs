using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SBPR.Niflheim.HomesteadStones.Domain
{
    // ============================================================================
    // R5 — engine-free Stone reconciliation policy (pre-ratification).
    //
    // Acceptance: "Reconciliation uses full stable ZDOID(UserID,ID), never truncated
    // numeric ID; handles unkeyed, stale, unselected, mismatched, and duplicate Stones
    // deterministically under pre-ratification policy."
    //
    // The net48 layer enumerates resident Stone ZDOs into StoneFact rows (each carrying the
    // FULL ZDOID as (UserID, ID), its assignment metadata, and whether it is keyed). This
    // pure policy decides, for each fact, one action: Keep (satisfies a selected assignment),
    // or Destroy (unkeyed / stale-version / mismatched / unselected / duplicate). It also
    // reports which selected zone keys are already satisfied so the caller skips creating a
    // second Stone there. Every branch is unit-tested headless.
    // ============================================================================

    /// <summary>The full stable Valheim ZDO identity: (UserID, ID). Never truncate to a single numeric —
    /// two distinct ZDOs can share an ID across different UserIDs, so a truncated key silently merges them.</summary>
    internal readonly struct StableZdoId : IEquatable<StableZdoId>
    {
        internal StableZdoId(long userId, uint id)
        {
            UserId = userId;
            Id = id;
        }

        internal long UserId { get; }
        internal uint Id { get; }

        internal string Value =>
            UserId.ToString(CultureInfo.InvariantCulture) + ":" + Id.ToString(CultureInfo.InvariantCulture);

        public bool Equals(StableZdoId other) => UserId == other.UserId && Id == other.Id;
        public override bool Equals(object? obj) => obj is StableZdoId other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return (UserId.GetHashCode() * 397) ^ (int)Id; }
        }
        public override string ToString() => Value;
    }

    /// <summary>One resident Stone ZDO as an engine-free fact: its full ZDOID, its FULL provenance (assignment
    /// PLUS provider/content/generation, read back through the shared codec), and whether it is keyed (has valid
    /// zone coordinates). A Stone with <see cref="Keyed"/> == false is unkeyed and always reaped.</summary>
    internal sealed class StoneReconcileFact
    {
        internal StoneReconcileFact(StableZdoId zdoId, bool keyed, HomesteadStoneProvenance provenance)
        {
            ZdoId = zdoId;
            Keyed = keyed;
            Provenance = provenance;
        }

        internal StableZdoId ZdoId { get; }
        internal bool Keyed { get; }
        internal HomesteadStoneProvenance Provenance { get; }
        internal HomesteadAssignmentMetadata Metadata => Provenance.Assignment;
    }

    /// <summary>The expected creation fact for a selected zone: the full provenance a freshly-resolved Stone
    /// would carry. The reconciler keeps a resident Stone only when its whole provenance equals this — so a
    /// provider/content/selector/generation UPGRADE (new catalog digest, new manifest generation, etc.) reaps
    /// the stale Stone and clears the way for a re-create, rather than silently reusing outdated geometry.</summary>
    internal sealed class HomesteadExpectedPlacement
    {
        internal HomesteadExpectedPlacement(HomesteadStoneProvenance provenance)
        {
            Provenance = provenance;
        }

        internal HomesteadStoneProvenance Provenance { get; }
        internal HomesteadAssignmentMetadata Assignment => Provenance.Assignment;
    }

    internal enum StoneReconcileAction
    {
        /// <summary>This Stone satisfies a selected assignment and is the first (deterministic) such Stone
        /// for its zone; keep it.</summary>
        Keep,

        /// <summary>Reap this Stone: unkeyed, stale/mismatched metadata, unselected zone, or a duplicate of a
        /// zone already satisfied by a kept Stone.</summary>
        Destroy,
    }

    /// <summary>Why a Stone was marked for reaping — for operator visibility and test assertions.</summary>
    internal enum StoneReconcileReason
    {
        Kept,
        Unkeyed,
        Mismatched,
        Unselected,
        Duplicate,

        /// <summary>R7 (Blocker 1) — the Stone's zone/prefab still matches a selected assignment, but its
        /// provider/content/generation provenance is STALE relative to the expected placement (a catalog digest
        /// roll, a newer manifest generation, or a provenance-schema bump). Reaped so recovery re-creates it
        /// with current provenance; an upgrade can never be blocked by an outdated Created Stone.</summary>
        ProvenanceStale,
    }

    internal readonly struct StoneReconcileDecision
    {
        internal StoneReconcileDecision(StableZdoId zdoId, StoneReconcileAction action, StoneReconcileReason reason)
        {
            ZdoId = zdoId;
            Action = action;
            Reason = reason;
        }

        internal StableZdoId ZdoId { get; }
        internal StoneReconcileAction Action { get; }
        internal StoneReconcileReason Reason { get; }
    }

    internal sealed class StoneReconcilePlan
    {
        internal StoneReconcilePlan(
            IReadOnlyList<StoneReconcileDecision> decisions,
            IReadOnlyCollection<string> satisfiedZoneKeys,
            IReadOnlyCollection<string> recoveryZoneKeys)
        {
            Decisions = decisions;
            SatisfiedZoneKeys = satisfiedZoneKeys;
            RecoveryZoneKeys = recoveryZoneKeys;
        }

        /// <summary>One decision per input fact, in input order.</summary>
        internal IReadOnlyList<StoneReconcileDecision> Decisions { get; }

        /// <summary>Zone keys ("zx:zz") that a KEPT Stone already satisfies — the caller must not create a
        /// second Stone for these.</summary>
        internal IReadOnlyCollection<string> SatisfiedZoneKeys { get; }

        /// <summary>R7 (Blocker 1) — zone keys whose only resident Stone(s) were reaped for STALE provenance (a
        /// provider/content/generation/selector upgrade). The caller must <c>ClearForRecovery</c> the ledger's
        /// Created entry for these zones so recovery re-creates the Stone with current provenance; a sticky
        /// Created must never block a legitimate upgrade.</summary>
        internal IReadOnlyCollection<string> RecoveryZoneKeys { get; }
    }

    /// <summary>The pure pre-ratification reconciliation policy.</summary>
    internal static class StoneReconciler
    {
        /// <summary>Decide Keep/Destroy for every resident Stone fact against the selected assignments, comparing
        /// the FULL provenance (assignment + provider/content/generation) so a stale-provenance Stone in a still-
        /// selected zone is reaped and flagged for recovery. Deterministic tie-break on duplicates: order facts by
        /// full ZDOID (UserId, then Id) so the SAME Stone is kept across restarts regardless of enumeration order.</summary>
        internal static StoneReconcilePlan Reconcile(
            IEnumerable<StoneReconcileFact> facts,
            IReadOnlyDictionary<string, HomesteadExpectedPlacement> expectedByZoneKey)
        {
            if (facts == null) throw new ArgumentNullException(nameof(facts));
            if (expectedByZoneKey == null) throw new ArgumentNullException(nameof(expectedByZoneKey));

            var ordered = facts
                .OrderBy(f => f.ZdoId.UserId)
                .ThenBy(f => f.ZdoId.Id)
                .ToList();

            var decisionByZdo = new Dictionary<StableZdoId, StoneReconcileDecision>();
            var satisfied = new HashSet<string>(StringComparer.Ordinal);
            var recovery = new HashSet<string>(StringComparer.Ordinal);

            foreach (var fact in ordered)
            {
                if (!fact.Keyed)
                {
                    decisionByZdo[fact.ZdoId] = Reap(fact, StoneReconcileReason.Unkeyed);
                    continue;
                }

                var key = ZoneKey(fact.Metadata.ZoneX, fact.Metadata.ZoneZ);
                if (!expectedByZoneKey.TryGetValue(key, out var expected))
                {
                    decisionByZdo[fact.ZdoId] = Reap(fact, StoneReconcileReason.Unselected);
                    continue;
                }
                if (!expected.Assignment.Matches(fact.Metadata))
                {
                    // Same zone, but world/selector/prefab drifted → stale assignment, reap it.
                    decisionByZdo[fact.ZdoId] = Reap(fact, StoneReconcileReason.Mismatched);
                    continue;
                }
                if (!expected.Provenance.Equals(fact.Provenance))
                {
                    // Zone + assignment match, but provider/content/generation provenance is stale (upgrade).
                    // Reap it AND flag the zone for ledger recovery so a re-create is not blocked by a sticky
                    // Created entry keyed to the OLD provenance.
                    decisionByZdo[fact.ZdoId] = Reap(fact, StoneReconcileReason.ProvenanceStale);
                    recovery.Add(key);
                    continue;
                }
                if (satisfied.Contains(key))
                {
                    // A lower-ZDOID Stone already satisfies this zone; this one is a duplicate.
                    decisionByZdo[fact.ZdoId] = Reap(fact, StoneReconcileReason.Duplicate);
                    continue;
                }

                satisfied.Add(key);
                decisionByZdo[fact.ZdoId] = new StoneReconcileDecision(
                    fact.ZdoId, StoneReconcileAction.Keep, StoneReconcileReason.Kept);
            }

            // A zone that ends up satisfied by a kept Stone is NOT a recovery zone (a valid current Stone exists).
            recovery.ExceptWith(satisfied);

            // Return decisions in the deterministic order they were decided.
            var decisions = ordered.Select(f => decisionByZdo[f.ZdoId]).ToList();
            return new StoneReconcilePlan(decisions, satisfied, recovery);
        }

        private static StoneReconcileDecision Reap(StoneReconcileFact fact, StoneReconcileReason reason) =>
            new StoneReconcileDecision(fact.ZdoId, StoneReconcileAction.Destroy, reason);

        internal static string ZoneKey(int zoneX, int zoneZ) =>
            zoneX.ToString(CultureInfo.InvariantCulture) + ":" + zoneZ.ToString(CultureInfo.InvariantCulture);
    }
}
