using System;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Cooking
{
    // T017 (Tracer 5, Cooking node 2 of 4) — the trusted, engine-free Field Prep Character-Effect
    // recipe-exposure policy (spec §US4 line 148-149 "Field Prep exposes unchanged Boar Jerky and
    // Queen's Jam recipes through the shared Cooking-aware Bushcraft policy"; contracts.md §Cooking
    // "CookingCraftPolicy: Field Prep eligibility plus normal Cooking skill XP, speed, and bonus-output
    // behavior for unchanged Boar Jerky/Queen's Jam recipes through Bushcraft"; data-model.md §"Cooking
    // | 1 | Field Prep | Character Effect | personal Offered"; research.md "Field Prep = Shared
    // Cooking-aware Bushcraft policy, normal Cooking XP/speed/bonus output, unchanged recipe
    // inputs/yields").
    //
    // Architecture decision A1 (plan.md): a derived provider translates persisted character/Stone state
    // into a typed capability. It writes no ledger; it is a PURE projection layered on the shipped T004
    // DerivedActivationView. It is the shared Cooking-aware Bushcraft POLICY the Cooking branch names —
    // the exact structural twin of the Archer T026 BushcraftRecipeProvider (a personal Character Effect
    // gated by purchase + relationship that exposes an UNCHANGED vanilla recipe station-free), differing
    // only in WHICH node it reads (Field Prep) and WHAT it exposes (two recipes: Boar Jerky + Queen's
    // Jam, not the Archer's single Wood Arrow).
    //
    // The cardinal rules this file encodes:
    //   * Field Prep is a PERSONAL Character Effect (NodeOutcomeType.CharacterEffect, personal Offered),
    //     NOT a Stone-owned Local Node like Savor the Hearth. Its active/dormant status is therefore
    //     derived through the personal activation view (DerivedActivationView): the caller must hold a
    //     PURCHASE record for the node at this Stone AND an ACTIVE relationship to this Stone. Neither
    //     the Settlement Local policy nor ordinary build Permission is a conjunct — those gate LOCAL
    //     effect capabilities (Savor drain / Practice Range placement), not a personal recipe effect.
    //     Change the relationship and re-derive: the same persisted purchase flips active<->dormant with
    //     zero writes (AT-NO-ACTIVE-LEDGER, carried by T004).
    //   * While active, the effect EXPOSES the UNCHANGED vanilla Boar Jerky and Queen's Jam recipes
    //     through Bushcraft (station-free craftability). "Bushcraft" here is stationless recipe
    //     eligibility: the effect makes the EXISTING vanilla recipes craftable without their ordinary
    //     Cooking-station requirement while active, and takes them away again when the effect goes
    //     dormant. It authors and mutates NOTHING about the recipes' inputs, yields, or authority — this
    //     policy is an EXPOSURE gate only, never a recipe author.
    //   * NORMAL Cooking behavior is preserved: exposing the recipe station-free does not alter the
    //     ordinary Cooking skill XP award, craft speed, or bonus-output behavior of these recipes. The
    //     policy carries no XP/speed/bonus override — it only answers WHETHER the unchanged recipe is
    //     craftable station-free right now. Every downstream Cooking mechanic runs vanilla.
    //
    // Client claims are NEVER the source of truth: the caller supplies authenticated aggregates
    // (character purchases + the (account, Stone) authority index) composed by trusted server code;
    // nothing here trusts a payload.
    //
    // net48 audit: only System + the engine-free Domain value objects / catalog / activation view. No
    // net5+ surface, no UnityEngine/Valheim/BepInEx reference, so this link-compiles into the net8 test
    // project exactly like the sibling adapters (CookingProviders, PracticeRangeProvider,
    // EffectiveStationLevelProvider).

    /// <summary>The exposure target of an active Field Prep effect: an UNCHANGED vanilla Cooking recipe
    /// made station-free (Bushcraft) while preserving the recipe's ordinary Cooking XP, speed, and
    /// bonus output. Pure content, engine-free. This is a marker of WHICH recipe is exposed and HOW
    /// (station-free, normal Cooking behavior) — it deliberately carries no authored input list or output
    /// count, because Field Prep never changes the recipe's ordinary inputs, yield, or authority; it only
    /// makes the existing vanilla recipe craftable without a station while active.</summary>
    public readonly struct BushcraftCookingRecipeDefinition
    {
        public BushcraftCookingRecipeDefinition(string outputItem, bool stationFree,
            bool preservesVanillaInputsYieldAuthority, bool preservesNormalCookingXpSpeedBonus)
        {
            OutputItem = outputItem;
            StationFree = stationFree;
            PreservesVanillaInputsYieldAuthority = preservesVanillaInputsYieldAuthority;
            PreservesNormalCookingXpSpeedBonus = preservesNormalCookingXpSpeedBonus;
        }

        /// <summary>The vanilla item this Bushcraft exposure targets (Boar Jerky <c>BoarJerky</c> or
        /// Queen's Jam <c>QueensJam</c>).</summary>
        public string OutputItem { get; }

        /// <summary>The exposure makes the recipe craftable WITHOUT its ordinary Cooking crafting station
        /// (stationless "Bushcraft" eligibility) while the effect is active.</summary>
        public bool StationFree { get; }

        /// <summary>The exposure preserves the recipe's ordinary vanilla inputs, yield, and authority — it
        /// is an EXPOSURE gate, never a recipe rewrite. Always true (spec "unchanged Boar Jerky and
        /// Queen's Jam recipes").</summary>
        public bool PreservesVanillaInputsYieldAuthority { get; }

        /// <summary>The exposure preserves the recipe's ordinary Cooking skill XP award, craft speed, and
        /// bonus-output behavior — station-free eligibility changes only WHERE the recipe can be crafted,
        /// never the normal Cooking mechanics that run when it is. Always true (contracts.md §Cooking
        /// "normal Cooking skill XP, speed, and bonus-output behavior").</summary>
        public bool PreservesNormalCookingXpSpeedBonus { get; }
    }

    /// <summary>The exact-item authored Field Prep exposure content. Boar Jerky (<c>BoarJerky</c>) and
    /// Queen's Jam (<c>QueensJam</c>) are stable vanilla item ids — Field Prep ships NO new content of
    /// its own; it only re-exposes the existing vanilla recipes station-free with normal Cooking
    /// behavior. The concrete ObjectDB recipe binding is confirmed against the running build at the
    /// joined-client evidence step.</summary>
    public static class BushcraftCookingContent
    {
        /// <summary>The exact vanilla Boar Jerky item id whose recipe Field Prep exposes through
        /// Bushcraft (verified against the running build's prefab index + StreamingAssets/SoftRef
        /// manifest_extended).</summary>
        public const string BoarJerkyItem = "BoarJerky";

        /// <summary>The exact vanilla Queen's Jam item id whose recipe Field Prep exposes through
        /// Bushcraft (verified against the running build's prefab index + StreamingAssets/SoftRef
        /// manifest_extended).</summary>
        public const string QueensJamItem = "QueensJam";

        /// <summary>The Bushcraft exposure target for Boar Jerky: the unchanged vanilla recipe made
        /// station-free with preserved Cooking XP/speed/bonus. Inputs/yield/authority are the recipe's
        /// own vanilla values — not authored here.</summary>
        public static readonly BushcraftCookingRecipeDefinition BoarJerkyRecipe =
            new BushcraftCookingRecipeDefinition(BoarJerkyItem, stationFree: true,
                preservesVanillaInputsYieldAuthority: true, preservesNormalCookingXpSpeedBonus: true);

        /// <summary>The Bushcraft exposure target for Queen's Jam: the unchanged vanilla recipe made
        /// station-free with preserved Cooking XP/speed/bonus. Inputs/yield/authority are the recipe's
        /// own vanilla values — not authored here.</summary>
        public static readonly BushcraftCookingRecipeDefinition QueensJamRecipe =
            new BushcraftCookingRecipeDefinition(QueensJamItem, stationFree: true,
                preservesVanillaInputsYieldAuthority: true, preservesNormalCookingXpSpeedBonus: true);

        /// <summary>The exact set of vanilla recipes Field Prep exposes through Bushcraft: Boar Jerky and
        /// Queen's Jam, in stable order. No other recipe is ever exposed by this node.</summary>
        public static readonly BushcraftCookingRecipeDefinition[] FieldPrepRecipes =
            new[] { BoarJerkyRecipe, QueensJamRecipe };

        /// <summary>Whether the given vanilla item id is one Field Prep exposes through Bushcraft
        /// (Boar Jerky or Queen's Jam). Ordinal exact match — never a mutable display string.</summary>
        public static bool IsFieldPrepRecipeItem(string outputItem) =>
            string.Equals(outputItem, BoarJerkyItem, StringComparison.Ordinal) ||
            string.Equals(outputItem, QueensJamItem, StringComparison.Ordinal);
    }

    /// <summary>The resolved Field Prep capability for one caller at one Stone. Pure projection: whether
    /// the personal Character Effect is active for them (purchase + active relationship), and — the
    /// single consequence — whether the unchanged vanilla Boar Jerky and Queen's Jam recipes are exposed
    /// through Bushcraft (station-free, normal Cooking behavior).</summary>
    public readonly struct CookingCraftCapability
    {
        public CookingCraftCapability(bool effectActive, bool cookingRecipesExposed)
        {
            EffectActive = effectActive;
            CookingRecipesExposed = cookingRecipesExposed;
        }

        /// <summary>The Field Prep Character Effect is currently active for this caller: they hold a
        /// purchase record for the node at this Stone AND an active relationship to this Stone. Pure
        /// derivation — flip the relationship and re-derive with zero writes.</summary>
        public bool EffectActive { get; }

        /// <summary>The unchanged vanilla Boar Jerky and Queen's Jam recipes are exposed through Bushcraft
        /// (station-free, normal Cooking XP/speed/bonus) for this caller. Equal to
        /// <see cref="EffectActive"/> — exposure is the effect's sole consequence.</summary>
        public bool CookingRecipesExposed { get; }

        /// <summary>The exact set of unchanged vanilla recipes this capability exposes (constant content:
        /// Boar Jerky + Queen's Jam).</summary>
        public BushcraftCookingRecipeDefinition[] Recipes => BushcraftCookingContent.FieldPrepRecipes;

        /// <summary>Whether this capability exposes the given vanilla item's recipe right now: it is one
        /// of Field Prep's recipes AND the effect is active. A dormant/unpurchased effect exposes
        /// nothing; an item outside Field Prep's set is never exposed by this node.</summary>
        public bool ExposesRecipeFor(string outputItem) =>
            CookingRecipesExposed && BushcraftCookingContent.IsFieldPrepRecipeItem(outputItem);

        /// <summary>An inert capability: effect dormant/unpurchased, nothing exposed.</summary>
        public static readonly CookingCraftCapability None =
            new CookingCraftCapability(false, false);
    }

    /// <summary>The shared Cooking-aware Bushcraft policy for personal Cooking Character Effects
    /// (contracts.md §Cooking "CookingCraftPolicy"). T017 lands its first consumer, Field Prep. The
    /// active/dormant decision routes through the shipped T004 <see cref="DerivedActivationView"/> so a
    /// personal Cooking effect is derived identically to every other Character Effect (purchase record
    /// AND active relationship, no second ledger); exposure of the unchanged vanilla recipes is the
    /// effect's sole consequence.</summary>
    public sealed class CookingCraftPolicy
    {
        /// <summary>The stable Field Prep personal Character-Effect node identity in the current build
        /// (HomesteadProgressionCatalog: Cooking / FieldPrep v1).</summary>
        public static readonly VersionedId FieldPrepNode = new VersionedId("FieldPrep", 1);

        // The catalog is held for parity with the sibling providers (PracticeRangeProvider,
        // BushcraftRecipeProvider, EffectiveStationLevelProvider) and to keep the door open for future
        // catalog-driven validation; the personal activation derivation itself needs only the character +
        // authority aggregates.
        private readonly HomesteadProgressionCatalog _catalog;

        public CookingCraftPolicy(HomesteadProgressionCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>Resolve the Field Prep capability for one caller from current Stone state + the
        /// caller's character aggregate + the (account, Stone) authority index. Reuses the shipped T004
        /// DerivedActivationView so active/dormant is derived identically to every other personal
        /// Character Effect (purchase record AND active relationship, no second ledger). The unchanged
        /// vanilla Boar Jerky and Queen's Jam recipes are exposed through Bushcraft exactly while that
        /// effect is active.</summary>
        public CookingCraftCapability Resolve(
            StoneProgressionAggregate stone,
            CharacterProgressionAggregate character,
            AccountStoneAuthorityIndex authority)
        {
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (authority == null) throw new ArgumentNullException(nameof(authority));

            var view = DerivedActivationView.Derive(stone, character, authority);

            bool effectActive = false;
            foreach (var row in view.Nodes)
            {
                if (row.Node.Key == FieldPrepNode.Key)
                {
                    effectActive = row.Active;
                    break;
                }
            }

            // Exposure is the effect's sole consequence: an active Field Prep exposes the unchanged
            // vanilla Boar Jerky and Queen's Jam recipes through Bushcraft with normal Cooking behavior; a
            // dormant/unpurchased effect exposes nothing.
            return effectActive
                ? new CookingCraftCapability(true, true)
                : CookingCraftCapability.None;
        }
    }
}
