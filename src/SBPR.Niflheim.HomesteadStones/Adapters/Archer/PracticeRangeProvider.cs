using System;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Archer
{
    // T025 (Tracer 7, Archer node 1 of 3) — the trusted, engine-free Practice Range Local
    // placement/recipe capability provider (spec §"Archer" line 158-159; contracts.md §Archer
    // "PracticeRangeProvider"; data-model.md §"Archer | 1 | Practice Range | Local Effect").
    // Architecture decision A1 (plan.md): a derived provider translates persisted Stone state + the
    // server-observed occupancy/governance/owner facts into a typed capability. It writes no ledger;
    // it is a PURE projection layered on the shipped T014 LocalEffectActivationView.
    //
    // The cardinal rule this file encodes (spec FR-015/FR-016; contracts.md §Archer):
    //   * Practice Range is a Stone-owned Local Effect — developed with BP, never purchased, never in a
    //     personal Offered Set, never a Tier-Access input. Its active/dormant status is RE-DERIVED here
    //     from the single Settlement Local policy + relationship/governance/level dormancy every call;
    //     there is no second mutable active-effects ledger (AT-NO-ACTIVE-LEDGER carried by T014).
    //   * A Local placement/recipe CAPABILITY is the load-bearing AND of the active Local Effect and the
    //     occupant's ORDINARY build Permission (spec FR-016 final sentence). Policy eligibility never
    //     grants the build ACL, and build Permission alone never smuggles the effect in outside the
    //     policy — both conjuncts are hard and separate, exactly as LocalEffectActivationView.
    //     CanExercisePlacement already proves for the generic Local case.
    //   * While active, the capability exposes the EXACT vanilla Archery Target placement and the
    //     authored Practice Arrow recipe (100 arrows for 8 Wood). The Practice Arrow contributes 0 ammo
    //     damage but the fired projectile RETAINS the bow's own draw damage, and a practice arrow that
    //     terminally impacts the Archery Target is DETERMINISTICALLY returned exactly once — no roll.
    //     That deterministic return is the hook a later Fletcher's Habit recovery roll (T027) must yield
    //     to (spec Edge cases: "target return wins its deterministic path and the permanent recovery
    //     roll does not run").
    //
    // Client claims are NEVER the source of truth: occupancy, governance, ownership, relationship, and
    // build Permission are all server-observed and supplied by the caller; nothing here trusts a payload.
    //
    // net48 audit: only System + the engine-free Domain value objects / catalog / activation view. No
    // net5+ surface, no UnityEngine/Valheim/BepInEx reference, so this link-compiles into the net8 test
    // project exactly like the sibling adapters.

    /// <summary>The stable identity of the surface a fired practice arrow terminally impacted, as
    /// attributed by trusted server code. Only <see cref="ArcheryTarget"/> wins the deterministic
    /// Practice Range return; every other terminal outcome yields to the ordinary path (and, for a
    /// recovery-eligible arrow, to the later Fletcher's Habit roll — T027).</summary>
    public enum TerminalImpactSurface
    {
        /// <summary>The arrow struck the vanilla Archery Target — deterministic return wins.</summary>
        ArcheryTarget = 0,

        /// <summary>The arrow struck terrain/ground.</summary>
        Ground = 1,

        /// <summary>The arrow struck water.</summary>
        Water = 2,

        /// <summary>The arrow struck a creature/character.</summary>
        Creature = 3,

        /// <summary>The arrow was lost, blocked, or expired (TTL) with no recoverable surface.</summary>
        LostOrExpired = 4
    }

    /// <summary>The authored Practice Arrow recipe (data-model.md provisional proof value: 100 arrows
    /// for 8 Wood). Pure content, engine-free. The item ids are the stable vanilla identifiers; the
    /// exact runtime prefab binding is verified at the joined-client evidence step.</summary>
    public readonly struct PracticeArrowRecipeDefinition
    {
        public PracticeArrowRecipeDefinition(string outputItem, int outputCount, string woodItem, int woodCost)
        {
            OutputItem = outputItem;
            OutputCount = outputCount;
            WoodItem = woodItem;
            WoodCost = woodCost;
        }

        /// <summary>The crafted output item (the Practice Arrow).</summary>
        public string OutputItem { get; }

        /// <summary>How many arrows one craft yields. Authored value: 100.</summary>
        public int OutputCount { get; }

        /// <summary>The single input resource (Wood).</summary>
        public string WoodItem { get; }

        /// <summary>How much Wood one craft costs. Authored value: 8.</summary>
        public int WoodCost { get; }
    }

    /// <summary>The exact-item / exact-prefab authored Practice Range content (spec line 158). These are
    /// the stable vanilla identifiers the Local Effect exposes; the concrete ZNetScene prefab binding is
    /// confirmed against the running build at the joined-client evidence step, exactly like the
    /// FoundationalPrefabMap. Provisional proof values (not final balance/compatibility locks).</summary>
    public static class PracticeRangeContent
    {
        /// <summary>The exact vanilla Archery Target build-piece prefab Practice Range unlocks. The real
        /// vanilla build-piece prefab id is <c>piece_ArcheryTarget</c> (capital A, capital T) — verified
        /// against the running build's StreamingAssets/SoftRef/manifest_extended and the decompiled
        /// <c>ArcheryTarget</c> component (localization tokens <c>$piece_archerytarget_*</c>). This is the
        /// single authored binding point (cf. FoundationalPrefabMap); the runtime registrar/gate
        /// (Features/Archer) consumes it verbatim.</summary>
        public const string ArcheryTargetPrefab = "piece_ArcheryTarget";

        /// <summary>The stable Practice Arrow output item id.</summary>
        public const string PracticeArrowItem = "ArrowPractice";

        /// <summary>The stable Wood input item id.</summary>
        public const string WoodItem = "Wood";

        /// <summary>The Practice Arrow's ammo damage contribution: 0 (spec line 159 "0 ammo damage").
        /// The fired projectile still deals the bow's own draw damage — see
        /// <see cref="PracticeRangeProvider.ResolvePracticeArrowDamage"/>.</summary>
        public const double PracticeArrowAmmoDamage = 0.0;

        /// <summary>The authored Practice Arrow recipe: 100 arrows for 8 Wood (data-model.md provisional
        /// proof value).</summary>
        public static readonly PracticeArrowRecipeDefinition PracticeArrowRecipe =
            new PracticeArrowRecipeDefinition(PracticeArrowItem, 100, WoodItem, 8);
    }

    /// <summary>The effective damage of a fired practice arrow: 0 ammo damage but the bow's own draw
    /// damage fully retained (spec line 159). Pure value object.</summary>
    public readonly struct PracticeArrowDamageProfile
    {
        public PracticeArrowDamageProfile(double bowDamage, double ammoDamage)
        {
            BowDamage = bowDamage;
            AmmoDamage = ammoDamage;
        }

        /// <summary>The bow's own draw damage for this shot (server-observed).</summary>
        public double BowDamage { get; }

        /// <summary>The Practice Arrow's ammo damage contribution — always 0.</summary>
        public double AmmoDamage { get; }

        /// <summary>The total damage the shot deals: bow damage + ammo damage. Because ammo damage is 0,
        /// this equals the bow damage — the arrow neither adds nor removes the weapon's own output.</summary>
        public double EffectiveDamage => BowDamage + AmmoDamage;
    }

    /// <summary>The deterministic Practice Range target-return decision for one terminal arrow impact.
    /// Carries no RNG — a practice arrow that struck the Archery Target is returned exactly once, and
    /// nothing else returns. <see cref="TargetReturnWon"/> is the flag a later Fletcher's Habit roll
    /// (T027) checks to suppress itself.</summary>
    public readonly struct TargetReturnDecision
    {
        public TargetReturnDecision(bool targetReturnWon, int returnedCount)
        {
            TargetReturnWon = targetReturnWon;
            ReturnedCount = returnedCount;
        }

        /// <summary>The deterministic Practice Range return path won this terminal impact (the arrow hit
        /// the Archery Target). When true, a later probabilistic recovery roll must NOT run.</summary>
        public bool TargetReturnWon { get; }

        /// <summary>How many arrow instances were returned by this deterministic path (0 or 1).</summary>
        public int ReturnedCount { get; }

        /// <summary>At least one arrow was returned.</summary>
        public bool ArrowReturned => ReturnedCount > 0;

        /// <summary>This decision was made with no RNG — identical inputs always yield identical results.</summary>
        public bool Deterministic => true;
    }

    /// <summary>The resolved Practice Range capability for one occupant at one Stone. Pure projection:
    /// whether the Local Effect is active for them, and — ANDed with ordinary build Permission — whether
    /// they may place the exact Archery Target and craft the Practice Arrow recipe.</summary>
    public readonly struct PracticeRangeCapability
    {
        public PracticeRangeCapability(bool effectActive, bool canPlaceArcheryTarget, bool canCraftPracticeArrows)
        {
            EffectActive = effectActive;
            CanPlaceArcheryTarget = canPlaceArcheryTarget;
            CanCraftPracticeArrows = canCraftPracticeArrows;
        }

        /// <summary>The Practice Range Local Effect is currently active for this occupant (developed +
        /// governance + occupancy + policy + level/commit). Independent of build Permission.</summary>
        public bool EffectActive { get; }

        /// <summary>The occupant may place the exact vanilla Archery Target: effect active AND ordinary
        /// build Permission (spec FR-016 final sentence — policy never grants the build ACL).</summary>
        public bool CanPlaceArcheryTarget { get; }

        /// <summary>The occupant may craft the Practice Arrow recipe: effect active AND ordinary build
        /// Permission. Same load-bearing AND as placement (both are Local-effect build capabilities).</summary>
        public bool CanCraftPracticeArrows { get; }

        /// <summary>The exact vanilla Archery Target prefab this capability unlocks (constant content).</summary>
        public string ArcheryTargetPrefab => PracticeRangeContent.ArcheryTargetPrefab;

        /// <summary>The authored Practice Arrow recipe this capability unlocks (constant content).</summary>
        public PracticeArrowRecipeDefinition PracticeArrowRecipe => PracticeRangeContent.PracticeArrowRecipe;

        /// <summary>An inert capability: effect dormant/ineligible, nothing unlocked.</summary>
        public static readonly PracticeRangeCapability None =
            new PracticeRangeCapability(false, false, false);
    }

    public sealed class PracticeRangeProvider
    {
        /// <summary>The stable Practice Range Local node identity in the current build
        /// (HomesteadProgressionCatalog: Archer / PracticeRange v1).</summary>
        public static readonly VersionedId PracticeRangeNode = new VersionedId("PracticeRange", 1);

        private readonly HomesteadProgressionCatalog _catalog;

        public PracticeRangeProvider(HomesteadProgressionCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>Resolve the Practice Range capability for one occupant from current Stone state + the
        /// server-observed occupancy/governance/owner/relationship facts + the occupant's ordinary build
        /// Permission. Reuses the shipped T014 LocalEffectActivationView so active/dormant/policy is
        /// derived identically to every other Local Effect (one Settlement policy, no per-effect
        /// override, no second ledger). The capability is the AND of that active status and build
        /// Permission (spec FR-016 final sentence).</summary>
        public PracticeRangeCapability Resolve(
            StoneProgressionAggregate stone,
            AccountId occupant,
            bool occupantIsOwner,
            bool occupantHasActiveRelationship,
            bool insideStoneArea,
            bool authorizedGovernorPresent,
            bool hasOrdinaryBuildPermission)
        {
            if (stone == null) throw new ArgumentNullException(nameof(stone));

            var view = LocalEffectActivationView.Derive(stone, _catalog, occupant, occupantIsOwner,
                occupantHasActiveRelationship, insideStoneArea, authorizedGovernorPresent);

            bool effectActive = view.StatusFor(PracticeRangeNode).Active;
            // Load-bearing AND: an active Local Effect never grants the build ACL by itself, and build
            // Permission never smuggles the effect in outside the policy. CanExercisePlacement already
            // encodes exactly this AND for the generic Local case.
            bool canExercise = view.CanExercisePlacement(PracticeRangeNode, hasOrdinaryBuildPermission);

            // Placement (Archery Target) and the recipe (Practice Arrow) are BOTH Local-effect build
            // capabilities governed by the same gate — a Local Effect unlocks the pair together.
            return new PracticeRangeCapability(effectActive, canExercise, canExercise);
        }

        /// <summary>The fired-shot damage profile for a Practice Arrow: 0 ammo damage, the bow's own draw
        /// damage retained (spec line 159). Pure — the arrow contributes nothing to and removes nothing
        /// from the weapon's own output.</summary>
        public static PracticeArrowDamageProfile ResolvePracticeArrowDamage(double bowDamage) =>
            new PracticeArrowDamageProfile(bowDamage, PracticeRangeContent.PracticeArrowAmmoDamage);

        /// <summary>The deterministic Practice Range target-return decision for one terminal arrow impact
        /// (spec line 158 "vanilla target return"; Edge case: target return wins deterministically and
        /// the permanent recovery roll does not run). No RNG: a practice arrow that struck the Archery
        /// Target is returned exactly once and wins; every other terminal surface returns nothing and
        /// does not win, yielding to the ordinary path (and to the later Fletcher's Habit roll).</summary>
        public static TargetReturnDecision ResolveTargetReturn(TerminalImpactSurface surface) =>
            surface == TerminalImpactSurface.ArcheryTarget
                ? new TargetReturnDecision(true, 1)
                : new TargetReturnDecision(false, 0);
    }
}
