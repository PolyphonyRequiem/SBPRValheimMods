using System;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Cooking
{
    // T019 (Tracer 5, Cooking node 4 of 4 — the sole executable Tier-2 node) — the trusted, engine-free
    // Swift Preparation menu-craft duration provider (spec §US4 sc1 "Swift Preparation makes eligible
    // menu-crafted food take one-third of the vanilla skill-adjusted duration"; contracts.md §Cooking
    // "MenuCraftDurationProvider: Swift Preparation supplies factor 1/3 after vanilla Cooking-skill
    // adjustment for eligible menu-crafted food only"; data-model.md §"Cooking | 2 | Swift Preparation |
    // Character Effect | personal Offered"; research.md "Swift Preparation affects only eligible menu-craft
    // duration after vanilla skill adjustment").
    //
    // Architecture (plan.md A1; the exact structural twin of the T030 Warrior EquipDurationProvider and the
    // T016 SavorTheHearthProvider): a provider is a DERIVED, read-only translation of already-derived
    // activation state into the exact vanilla-facing factor the engine seam consumes. It is NOT a second
    // ledger and it NEVER mutates a recipe, an item, a stat, or a shared prefab. The active/dormant decision
    // for this personal Character Effect is owned entirely by the T004 DerivedActivationView (purchased AND
    // caller holds an active relationship); this provider only READS that node's Active bit and answers
    // "at what duration does THIS menu-craft of eligible food run right now?".
    //
    // WHY THE MENU-CRAFT TIMER SEAM IS SAFE (decomp — vanilla is fair game, AGENTS.md / ADR-0001):
    //   InventoryGui.UpdateRecipe (decomp assembly_valheim :42372-42375) computes the menu-craft duration
    //   as `num5 = (m_multiCrafting ? m_multiCraftDuration : m_craftDuration)` and then, when the current
    //   crafting station has a crafting skill, multiplies it by the VANILLA SKILL ADJUSTMENT
    //   `(1f - GetSkillFactor(station.m_craftingSkill) * m_craftDurationSkillMaxDecrease)`. That
    //   skill-adjusted value is the progress-bar's max (m_craftProgressBar.SetMaxValue(num5)); the craft
    //   completes only when the accumulating m_craftTimer reaches it. So the menu-craft RATE is governed
    //   entirely by that per-frame max. Swift Preparation scales that ALREADY-skill-adjusted value by 1/3
    //   — the seam hands us the post-skill number, and we return base/3 — so the factor lands strictly
    //   AFTER the vanilla Cooking-skill adjustment, never replacing or re-deriving it. The seam writes only
    //   the local progress-bar max for THIS frame's craft; it touches no recipe, no ItemDrop prefab, no
    //   station, and no other craft. Slotted world cooking (CookingStation timers) is a DISTINCT surface —
    //   it never routes through the menu m_craftTimer — so it is structurally excluded (IsMenuCraft=false).
    //
    // ELIGIBILITY (data-defined; food produced through the Cooking menu-craft path only):
    //   The factor applies ONLY to an eligible menu-crafted FOOD: the recipe output must be a food item AND
    //   the active crafting station's crafting skill must be Cooking AND the timer must be the menu-craft
    //   timer (not a slotted-cooking or non-menu path). Anything else — a non-food output, a non-Cooking
    //   station (Forge/Workbench), or a non-menu craft — keeps the full vanilla skill-adjusted duration. The
    //   eligibility is keyed on SERVER/engine-observed facts of the craft (output food flag + station skill +
    //   menu path), never a client claim of eligibility, exactly like the sibling providers.
    //
    // NO COMPLETION / NO FABRICATION (AT-NO-COOKING-COMPLETION): the factor shortens a POSITIVE duration but
    // never to zero — base/3 of any positive base is strictly positive and strictly shorter, so a craft is
    // never instant-completed and progression carries no "Tree-completion" state. A non-positive base (an
    // ill-formed/instant timer vanilla never queues) is returned UNCHANGED — the provider never conjures a
    // craft where vanilla had none.
    //
    // net48 audit: value objects + double arithmetic only. No net5+ API, no UnityEngine / Valheim / BepInEx
    // reference, so this core link-compiles into the net8 test project exactly like the sibling adapters
    // (CookingProviders, CookingCraftPolicy, FoodRefreshThresholdProvider, EquipDurationProvider).

    /// <summary>How a specific menu-craft attempt is classified for Swift Preparation eligibility. The
    /// factor applies ONLY to <see cref="EligibleMenuCraftedFood"/>; every ineligible variant keeps the full
    /// vanilla skill-adjusted duration. The distinct ineligible reasons exist so the runtime seam and tests
    /// can name exactly WHY a craft was not shortened.</summary>
    public enum MenuCraftEligibility
    {
        /// <summary>A food item crafted through the Cooking menu-craft path at a Cooking-skilled station:
        /// the one case Swift Preparation shortens to 1/3.</summary>
        EligibleMenuCraftedFood = 0,

        /// <summary>The recipe output is not a food item (e.g. a tool, weapon, or material). Never shortened —
        /// Swift Preparation is a COOKING effect.</summary>
        IneligibleNonFood = 1,

        /// <summary>The active crafting station's crafting skill is not Cooking (e.g. a Forge/Workbench
        /// recipe). Never shortened — the effect is scoped to Cooking-station menu crafts.</summary>
        IneligibleNonCookingStation = 2,

        /// <summary>The duration is not the menu-craft timer (e.g. a slotted world-cooking CookingStation
        /// timer, which is a distinct surface). Never shortened — Swift Preparation affects only the eligible
        /// menu-craft duration.</summary>
        IneligibleNotMenuCraft = 3,
    }

    /// <summary>The stable identity of the Swift Preparation personal node (Cooking Tree, Level 2). Pinned
    /// here so the provider and its live engine seam agree on exactly which purchased node drives the menu
    /// duration factor, independent of display label.</summary>
    public static class SwiftPreparationNodes
    {
        public static readonly VersionedId SwiftPreparation = new VersionedId("SwiftPreparation", 1);
    }

    /// <summary>The result of resolving one menu-craft attempt's duration under Swift Preparation. The
    /// <see cref="Duration"/> is the value the engine seam should assign to the local menu-craft progress-bar
    /// max for this frame's craft — never written back to a recipe or shared prefab.</summary>
    public readonly struct MenuCraftDurationDecision
    {
        public MenuCraftDurationDecision(double duration, bool shortened, MenuCraftEligibility eligibility)
        {
            Duration = duration;
            Shortened = shortened;
            Eligibility = eligibility;
        }

        /// <summary>The resolved menu-craft duration (seconds). Equal to the supplied vanilla skill-adjusted
        /// duration when the effect is not delivering; one-third of it when Swift Preparation is active for an
        /// eligible menu-crafted food. Always strictly positive when the base was positive.</summary>
        public double Duration { get; }

        /// <summary>True iff Swift Preparation actually shortened this craft (active + eligible menu-crafted
        /// food + positive base).</summary>
        public bool Shortened { get; }

        /// <summary>How the craft was classified for eligibility.</summary>
        public MenuCraftEligibility Eligibility { get; }
    }

    /// <summary>Pure derived provider for Swift Preparation (contracts.md §Cooking "MenuCraftDurationProvider").
    /// Translates the T004 <see cref="DerivedActivationView"/> active-state for the Swift Preparation personal
    /// node into the shortened menu-craft duration for eligible menu-crafted food. Stateless: every answer is
    /// a pure function of the supplied derived view (or resolved active bit) + the craft's eligibility + the
    /// vanilla skill-adjusted base, so relationship loss / dormancy flips the factor with zero writes and no
    /// recipe/prefab mutation.</summary>
    public sealed class MenuCraftDurationProvider
    {
        /// <summary>The authored duration factor while the effect is active for an eligible menu-crafted food:
        /// the menu craft runs at one-third of the vanilla skill-adjusted duration (spec §US4 sc1). This is
        /// the ONLY tuning knob; final balance is deferred (research.md — the display name and wider Cooking
        /// progression remain configurable).</summary>
        public const double ActiveDurationFactor = 1.0 / 3.0;

        /// <summary>The vanilla baseline factor: with no active Swift Preparation effect (or an ineligible
        /// craft), the menu craft keeps the full vanilla skill-adjusted duration.</summary>
        public const double InactiveDurationFactor = 1.0;

        /// <summary>The vanilla Cooking crafting-skill enum value (Skills.SkillType.Cooking == 105, decomp
        /// assembly_valheim :23842). A menu-craft is a Cooking craft iff the active station's
        /// <c>m_craftingSkill</c> equals this. Kept engine-free as an int so the pure core carries no
        /// UnityEngine/Valheim reference; the seam maps the real station skill onto it.</summary>
        public const int CookingSkill = 105;

        private readonly VersionedId _node;

        public MenuCraftDurationProvider() : this(SwiftPreparationNodes.SwiftPreparation) { }

        /// <summary>Test/extension seam: bind the provider to an explicit Swift Preparation node identity.</summary>
        public MenuCraftDurationProvider(VersionedId swiftNode)
        {
            if (swiftNode.IsNone)
                throw new ArgumentException("Swift Preparation node id must not be None.", nameof(swiftNode));
            _node = swiftNode;
        }

        /// <summary>The authored Cooking Swift Preparation node this provider governs.</summary>
        public VersionedId Node => _node;

        /// <summary>Classify a menu-craft attempt for Swift Preparation eligibility from the three
        /// engine-observed facts: whether the recipe output is a food item, the active station's crafting
        /// skill, and whether this is the menu-craft path. Eligible ONLY when all three hold (food, Cooking
        /// skill, menu craft). The checks are ordered so a single distinct reason is reported: non-food first
        /// (Swift Preparation is a Cooking effect), then non-Cooking station, then non-menu craft. Pure
        /// content predicate — no engine dependency; exposed so tests and the runtime seam agree on the exact
        /// membership.</summary>
        public static MenuCraftEligibility ClassifyCraft(bool outputIsFood, int stationCraftingSkill, bool isMenuCraft)
        {
            if (!outputIsFood) return MenuCraftEligibility.IneligibleNonFood;
            if (stationCraftingSkill != CookingSkill) return MenuCraftEligibility.IneligibleNonCookingStation;
            if (!isMenuCraft) return MenuCraftEligibility.IneligibleNotMenuCraft;
            return MenuCraftEligibility.EligibleMenuCraftedFood;
        }

        /// <summary>Whether the classified craft is the one eligible case.</summary>
        public static bool IsEligible(MenuCraftEligibility eligibility) =>
            eligibility == MenuCraftEligibility.EligibleMenuCraftedFood;

        /// <summary>Whether Swift Preparation is currently ACTIVE (delivering) for the caller in the supplied
        /// derived activation view: the Swift Preparation node is purchased AND the caller holds an active
        /// relationship (the T004 DerivedActivationView Active bit). Pure: re-derive the view after any
        /// relationship/level change and call again — the answer flips with zero state carried here.</summary>
        public bool IsActive(DerivedActivationView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            foreach (var n in view.Nodes)
                if (string.Equals(n.Node.Key, _node.Key, StringComparison.Ordinal))
                    return n.Active;
            return false;
        }

        /// <summary>The duration factor to apply to a menu-craft for this caller RIGHT NOW. Returns 1/3 iff
        /// Swift Preparation is active for the caller AND the craft is an eligible menu-crafted food;
        /// otherwise 1.0. Pure — flip any input and re-derive with zero writes.</summary>
        public double DurationFactor(DerivedActivationView view, MenuCraftEligibility eligibility)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            return DurationFactor(IsActive(view), eligibility);
        }

        /// <summary>Factor overload taking the already-resolved Swift Preparation active bit. The runtime seam
        /// resolves the active bit authoritatively (host derivation, or the server-stamped personal client
        /// cache) and hands it in directly, so the eligibility grammar stays the single authority without
        /// re-deriving a view on the client. Pure.</summary>
        public double DurationFactor(bool swiftPreparationActive, MenuCraftEligibility eligibility) =>
            swiftPreparationActive && IsEligible(eligibility) ? ActiveDurationFactor : InactiveDurationFactor;

        /// <summary>Resolve the menu-craft duration the engine seam should assign to this frame's local
        /// menu-craft progress-bar max. The <paramref name="skillAdjustedDurationSeconds"/> is the vanilla
        /// value AFTER the Cooking-skill adjustment (the seam computes
        /// <c>base*(1 - skillFactor*maxDecrease)</c> before calling us); this returns
        /// <c>skillAdjusted * DurationFactor</c>. It performs NO mutation and touches NO recipe/prefab — it
        /// only computes the value the caller assigns to the local craft timer's max. A non-positive base
        /// (an ill-formed/instant duration vanilla never queues) is returned UNCHANGED so the provider never
        /// fabricates a craft (AT-NO-COOKING-COMPLETION).</summary>
        public MenuCraftDurationDecision ResolveDuration(
            DerivedActivationView view, MenuCraftEligibility eligibility, double skillAdjustedDurationSeconds)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            return ResolveDuration(IsActive(view), eligibility, skillAdjustedDurationSeconds);
        }

        /// <summary>Resolve the menu-craft duration from the already-resolved Swift Preparation active bit.
        /// Same semantics as the view overload; used by the runtime seam which resolves activation
        /// authoritatively before reaching the pure grammar. Pure — no mutation, no recipe/prefab.</summary>
        public MenuCraftDurationDecision ResolveDuration(
            bool swiftPreparationActive, MenuCraftEligibility eligibility, double skillAdjustedDurationSeconds)
        {
            if (skillAdjustedDurationSeconds <= 0.0 || double.IsNaN(skillAdjustedDurationSeconds))
                return new MenuCraftDurationDecision(skillAdjustedDurationSeconds, false, eligibility);

            double factor = DurationFactor(swiftPreparationActive, eligibility);
            bool shortened = factor < InactiveDurationFactor;
            return new MenuCraftDurationDecision(skillAdjustedDurationSeconds * factor, shortened, eligibility);
        }
    }
}
