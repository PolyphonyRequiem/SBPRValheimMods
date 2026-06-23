using System;
using System.Collections.Generic;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Pure, engine-free decision logic for the Painted Sign's PER-CHANGED-SLOT consume
    /// cost (§A2.6, LOCKED Daniel 2026-06-21, card t_6df12ca8). No UnityEngine / Valheim
    /// refs, so tests/SignPaintDeltaTests.cs can gate the FULL two-predicate truth table
    /// headless in CI (the LensHandoffDecision / CompassNorthGate link-compile precedent —
    /// tests/SBPR.Trailborne.Tests.csproj).
    ///
    /// TWO DISTINCT PREDICATES (do NOT collapse into one — the load-bearing design):
    ///   • Changed-set — slots where new != old (ordinal), INCLUDING clears (color→"").
    ///                   Drives the commit gate + no-op detection via <see cref="HasAnyChange"/>.
    ///   • Delta cost  — 1 pigment per BILLABLE changed slot, where billable = changed AND
    ///                   new color non-empty. A cleared slot is a change that costs 0.
    ///                   Computed by <see cref="ComputeChangedCost"/>.
    ///
    /// WHY TWO: a PURE CLEAR (e.g. board Red→"") has a NON-EMPTY changed-set but an EMPTY
    /// delta cost. Gating the button on the cost count would make a clear un-committable;
    /// gating on the changed-set keeps it committable for free (Daniel-locked: clears are
    /// free AND a pure clear stays enabled, commits, reverts the slot to bare wood, consumes
    /// nothing). A no-op (nothing changed) has an EMPTY changed-set → the button silently
    /// disables (Daniel-locked: "1) disabled", no message). So the commit gate is
    /// <see cref="HasAnyChange"/>, NEVER <c>ComputeChangedCost(...).Count != 0</c>.
    ///
    /// "Old" colors are the sign's CURRENT stored ZDO colors, read LIVE at compute time by
    /// the engine-bound caller (<see cref="SignPaintBackend"/>) — this file stays pure by
    /// taking them as plain strings. Empty string ("") = that slot unset / unpainted.
    /// </summary>
    public static class SignPaintDelta
    {
        /// <summary>
        /// The CHANGED-SET predicate: true if ANY of the three slots differs (ordinal) from
        /// its current stored color, INCLUDING a clear (color→""). This — NOT the delta
        /// cost's count — is the commit gate / no-op detector. A no-op returns false (button
        /// disables silently); a pure clear returns true (committable for free).
        /// </summary>
        public static bool HasAnyChange(
            string oldText, string oldBoard, string oldBorder,
            string newText, string newBoard, string newBorder)
            => Changed(oldText, newText)
            || Changed(oldBoard, newBoard)
            || Changed(oldBorder, newBorder);

        /// <summary>
        /// The DELTA COST: a map of color-id → count of that pigment to consume, one per
        /// BILLABLE changed slot (changed AND new non-empty). Same color across M billable
        /// changed slots → M. Unchanged slots and clears contribute 0. An all-empty baseline
        /// (a fully-unpainted sign) makes every "" → color a billable change, so this equals
        /// the per-filled first-paint cost exactly (AT-4: first paint unchanged).
        /// </summary>
        public static Dictionary<string, int> ComputeChangedCost(
            string oldText, string oldBoard, string oldBorder,
            string newText, string newBoard, string newBorder)
        {
            var cost = new Dictionary<string, int>(StringComparer.Ordinal);
            AddBillable(cost, oldText, newText);
            AddBillable(cost, oldBoard, newBoard);
            AddBillable(cost, oldBorder, newBorder);
            return cost;
        }

        // A slot is CHANGED when its new color differs (ordinal) from its old color, with
        // null and "" both normalised to "unset" first — so null↔"" is NOT a change (the
        // ZDO returns "", the panel may seed null/"", CommitPaint passes color ?? "": all
        // three spellings of "unset" must compare equal). Mirrors the ordinal != idiom at
        // SignTag.cs:124/135.
        private static bool Changed(string oldColor, string newColor)
            => !string.Equals(Norm(oldColor), Norm(newColor), StringComparison.Ordinal);

        // Add one pigment for a slot ONLY when it is BILLABLE = changed AND new is non-empty.
        // A cleared slot (new == "") is changed but contributes 0 (clears stay free).
        private static void AddBillable(Dictionary<string, int> cost, string oldColor, string newColor)
        {
            string n = Norm(newColor);
            if (n.Length == 0) return;          // clear (or unchanged-empty) → not billable
            if (!Changed(oldColor, newColor)) return; // unchanged → free
            cost.TryGetValue(n, out int c);
            cost[n] = c + 1;
        }

        // Normalise an unset slot: null → "". Keeps the comparison total without NREs.
        private static string Norm(string color) => color ?? "";
    }
}
