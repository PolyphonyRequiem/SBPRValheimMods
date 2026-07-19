using System;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Cooking
{
    // T016 — Cooking effect-delivery adapter/provider boundary (contracts.md §"Effect delivery
    // contracts" → "Cooking"; spec US4 sc1; acceptance AT-SAVOR-AREA-EXIT). This is the FIRST
    // Cooking vertical slice: it establishes the shared Cooking adapter/provider surface that
    // T017–T019 (Field Prep / Iron Stomach / Swift Preparation) extend.
    //
    // Architecture (plan.md A1; data-model.md §"DerivedActivationView"): a provider is a DERIVED,
    // read-only translation of already-derived activation state into the exact vanilla-facing factor
    // the engine seam consumes. It is NOT a second ledger and it NEVER mutates an item, a stat, or a
    // food entry. The active/dormant decision is owned entirely by LocalEffectActivationView (T014);
    // this provider only READS StatusFor(Savor).Active and answers "at what factor does an active
    // food timer consume elapsed time right now?".
    //
    // Accepted contract encoded here (contracts.md SavorTheHearthProvider):
    //   * policy-eligible occupant + inside this Stone Area + active Local Node ⇒ food timers consume
    //     elapsed time at factor 0.5 (drain 50% slower).
    //   * Exit / policy loss restores factor 1 IMMEDIATELY. Because the factor is recomputed per
    //     evaluation from the current derived view, stepping outside the Area (or losing policy
    //     eligibility / governance) flips 0.5→1 on the very next evaluation with zero writes.
    //   * No item/stat mutation and NO retroactive duration: the provider only scales the elapsed
    //     slice it is asked about; time already consumed at factor 1 is never refunded, and time
    //     consumed at factor 0.5 is never clawed back on exit.
    //
    // net48 audit: value objects + double arithmetic only. No net5+ API, no UnityEngine / Valheim /
    // BepInEx reference, so this core link-compiles into the net8 test project exactly like
    // Adapters/Activities/AlignedActivityAdapter.cs while shipping under net48.

    /// <summary>The Savor the Hearth Local node identity (Cooking Tree, Level 1). Pinned here so the
    /// provider and its live engine seam agree on exactly which developed Local node drives the
    /// factor, independent of display label.</summary>
    public static class CookingNodes
    {
        public static readonly VersionedId SavorTheHearth = new VersionedId("SavorTheHearth", 1);
    }

    /// <summary>Pure derived provider for Savor the Hearth (contracts.md §"Cooking"). Translates the
    /// T014 <see cref="LocalEffectActivationView"/> active-state for the Savor Local node into the
    /// vanilla food-timer drain factor. Stateless: every answer is a pure function of the supplied
    /// derived view, so exit/policy loss flips the factor with zero writes and no retroactive
    /// adjustment.</summary>
    public sealed class SavorTheHearthProvider
    {
        /// <summary>The authored drain factor while the effect is active: active food timers consume
        /// elapsed time at half speed (spec US4 sc1: "drains active food timers 50% slower"). This is
        /// the ONLY tuning knob; final beneficiary/balance tuning is deferred (research.md).</summary>
        public const double ActiveDrainFactor = 0.5;

        /// <summary>The vanilla baseline factor: with no active Savor effect, food timers consume
        /// elapsed time at normal speed. Restored immediately on Area exit / policy loss.</summary>
        public const double InactiveDrainFactor = 1.0;

        private readonly VersionedId _node;

        public SavorTheHearthProvider() : this(CookingNodes.SavorTheHearth) { }

        /// <summary>Test/extension seam: bind the provider to an explicit Savor node identity.</summary>
        public SavorTheHearthProvider(VersionedId savorNode)
        {
            if (savorNode.IsNone) throw new ArgumentException("Savor node id must not be None.", nameof(savorNode));
            _node = savorNode;
        }

        /// <summary>The drain factor an active food timer should use for this occupant RIGHT NOW.
        /// Returns 0.5 iff the Savor Local Effect is currently active for the occupant in the supplied
        /// derived view (developed + governance + inside Area + policy-eligible), otherwise 1.0. Pure:
        /// re-derive the view after any change (Area exit, policy loss, dormancy) and call again — the
        /// factor flips with no state carried here.</summary>
        public double DrainFactor(LocalEffectActivationView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            return view.StatusFor(_node).Active ? ActiveDrainFactor : InactiveDrainFactor;
        }

        /// <summary>Whether the effect is currently delivering the slowed factor for this occupant.
        /// Equivalent to <c>DrainFactor(view) == ActiveDrainFactor</c>; exposed for a readable seam.</summary>
        public bool IsSlowing(LocalEffectActivationView view) =>
            view != null && view.StatusFor(_node).Active;

        /// <summary>Consume one elapsed real-time slice at the current derived factor. This is the
        /// exact quantity the engine seam subtracts from an active food timer for the given elapsed
        /// wall time: <c>elapsedSeconds * DrainFactor(view)</c>. It performs NO mutation and carries NO
        /// retroactive duration — it scales ONLY the slice it is handed, so time already consumed at a
        /// different factor is neither refunded nor clawed back. Non-positive elapsed consumes nothing.</summary>
        public double ConsumeElapsed(LocalEffectActivationView view, double elapsedSeconds)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (elapsedSeconds <= 0.0 || double.IsNaN(elapsedSeconds)) return 0.0;
            return elapsedSeconds * DrainFactor(view);
        }
    }
}
