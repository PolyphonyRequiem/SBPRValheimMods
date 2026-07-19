using System;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Archer
{
    // T026 (Tracer 7, Archer node 2 of 3) — the trusted, engine-free Field Fletching I Character-Effect
    // recipe-exposure provider (spec §"Archer" line 160 "Field Fletching I exposes unchanged Wood Arrows
    // through Bushcraft while active"; contracts.md §Archer "BushcraftRecipeProvider: active Field
    // Fletching I exposes unchanged Wood Arrows through Bushcraft"; data-model.md §"Archer | 1 | Field
    // Fletching I | Character Effect | personal Offered"; research.md "Field Fletching I = unchanged Wood
    // Arrow recipe").
    //
    // Architecture decision A1 (plan.md): a derived provider translates persisted character/Stone state
    // into a typed capability. It writes no ledger; it is a PURE projection layered on the shipped T004
    // DerivedActivationView. Contrast the sibling T025 PracticeRangeProvider (a Stone-owned LOCAL Effect
    // projected through LocalEffectActivationView, gated by the Settlement Local policy AND ordinary build
    // Permission, shipping the NEW ArrowPractice content). Field Fletching I is a different node SHAPE:
    //
    //   * It is a PERSONAL Character Effect (NodeOutcomeType.CharacterEffect, personal Offered), NOT a
    //     Stone-owned Local Node. Its active/dormant status is therefore derived through the personal
    //     activation view (DerivedActivationView): the caller must hold a PURCHASE record for the node at
    //     this Stone AND an ACTIVE relationship to this Stone. Neither the Settlement Local policy nor
    //     ordinary build Permission is a conjunct — those gate Local placement capabilities, not a
    //     personal recipe effect. Change the relationship and re-derive: the same persisted purchase flips
    //     active<->dormant with zero writes (AT-NO-ACTIVE-LEDGER, carried by T004).
    //   * While active, the effect EXPOSES the UNCHANGED vanilla Wood Arrow recipe through Bushcraft
    //     (station-free craftability). "Bushcraft" here is stationless recipe eligibility: the effect makes
    //     the EXISTING vanilla Wood Arrow recipe craftable without its ordinary station requirement while
    //     active, and takes it away again when the effect goes dormant. It authors and mutates NOTHING
    //     about the recipe's inputs, yield, or authority — this provider is an EXPOSURE gate only, never a
    //     recipe author (spec: "unchanged Wood Arrows"; research.md rejects wider ammunition-registry or
    //     input/yield changes as future Field Fletching levels).
    //
    // Client claims are NEVER the source of truth: the caller supplies authenticated aggregates
    // (character purchases + the (account, Stone) authority index) composed by trusted server code; nothing
    // here trusts a payload.
    //
    // net48 audit: only System + the engine-free Domain value objects / catalog / activation view. No
    // net5+ surface, no UnityEngine/Valheim/BepInEx reference, so this link-compiles into the net8 test
    // project exactly like the sibling adapters.

    /// <summary>The exposure target of an active Field Fletching I effect: the UNCHANGED vanilla Wood
    /// Arrow recipe made station-free (Bushcraft). Pure content, engine-free. This is a marker of WHICH
    /// recipe is exposed and HOW (station-free) — it deliberately carries no authored input list or output
    /// count, because Field Fletching I never changes the recipe's ordinary inputs, yield, or authority;
    /// it only makes the existing vanilla recipe craftable without a station while active.</summary>
    public readonly struct BushcraftRecipeDefinition
    {
        public BushcraftRecipeDefinition(string outputItem, bool stationFree, bool preservesVanillaInputsYieldAuthority)
        {
            OutputItem = outputItem;
            StationFree = stationFree;
            PreservesVanillaInputsYieldAuthority = preservesVanillaInputsYieldAuthority;
        }

        /// <summary>The vanilla item this Bushcraft exposure targets — the Wood Arrow (<c>ArrowWood</c>).</summary>
        public string OutputItem { get; }

        /// <summary>The exposure makes the recipe craftable WITHOUT its ordinary crafting station
        /// (stationless "Bushcraft" eligibility) while the effect is active.</summary>
        public bool StationFree { get; }

        /// <summary>The exposure preserves the recipe's ordinary vanilla inputs, yield, and authority — it
        /// is an EXPOSURE gate, never a recipe rewrite. Always true (spec "unchanged Wood Arrows").</summary>
        public bool PreservesVanillaInputsYieldAuthority { get; }
    }

    /// <summary>The exact-item authored Field Fletching I exposure content. The Wood Arrow is a stable
    /// vanilla item id (<c>ArrowWood</c>) — Field Fletching I ships NO new content of its own; it only
    /// re-exposes the existing vanilla recipe station-free. The concrete ObjectDB recipe binding is
    /// confirmed against the running build at the joined-client evidence step.</summary>
    public static class BushcraftRecipeContent
    {
        /// <summary>The exact vanilla Wood Arrow item id whose recipe Field Fletching I exposes through
        /// Bushcraft. This is the earliest, always-present vanilla arrow (the same blueprint the T025
        /// runtime reads for its ammo family — see Features/Archer/ArcherContent).</summary>
        public const string WoodArrowItem = "ArrowWood";

        /// <summary>The Bushcraft exposure target: the unchanged vanilla Wood Arrow recipe made
        /// station-free. Inputs/yield/authority are the recipe's own vanilla values — not authored here.</summary>
        public static readonly BushcraftRecipeDefinition WoodArrowRecipe =
            new BushcraftRecipeDefinition(WoodArrowItem, stationFree: true, preservesVanillaInputsYieldAuthority: true);
    }

    /// <summary>The resolved Field Fletching I capability for one caller at one Stone. Pure projection:
    /// whether the personal Character Effect is active for them (purchase + active relationship), and — the
    /// single consequence — whether the unchanged vanilla Wood Arrow recipe is exposed through Bushcraft.</summary>
    public readonly struct BushcraftRecipeCapability
    {
        public BushcraftRecipeCapability(bool effectActive, bool woodArrowRecipeExposed)
        {
            EffectActive = effectActive;
            WoodArrowRecipeExposed = woodArrowRecipeExposed;
        }

        /// <summary>The Field Fletching I Character Effect is currently active for this caller: they hold a
        /// purchase record for the node at this Stone AND an active relationship to this Stone. Pure
        /// derivation — flip the relationship and re-derive with zero writes.</summary>
        public bool EffectActive { get; }

        /// <summary>The unchanged vanilla Wood Arrow recipe is exposed through Bushcraft (station-free) for
        /// this caller. Equal to <see cref="EffectActive"/> — exposure is the effect's sole consequence.</summary>
        public bool WoodArrowRecipeExposed { get; }

        /// <summary>The unchanged vanilla Wood Arrow recipe this capability exposes (constant content).</summary>
        public BushcraftRecipeDefinition WoodArrowRecipe => BushcraftRecipeContent.WoodArrowRecipe;

        /// <summary>An inert capability: effect dormant/unpurchased, nothing exposed.</summary>
        public static readonly BushcraftRecipeCapability None =
            new BushcraftRecipeCapability(false, false);
    }

    public sealed class BushcraftRecipeProvider
    {
        /// <summary>The stable Field Fletching I personal Character-Effect node identity in the current
        /// build (HomesteadProgressionCatalog: Archer / FieldFletchingI v1).</summary>
        public static readonly VersionedId FieldFletchingNode = new VersionedId("FieldFletchingI", 1);

        // The catalog is held for parity with the sibling providers (PracticeRangeProvider,
        // EffectiveStationLevelProvider) and to keep the door open for future catalog-driven validation;
        // the personal activation derivation itself needs only the character + authority aggregates.
        private readonly HomesteadProgressionCatalog _catalog;

        public BushcraftRecipeProvider(HomesteadProgressionCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>Resolve the Field Fletching I capability for one caller from current Stone state + the
        /// caller's character aggregate + the (account, Stone) authority index. Reuses the shipped T004
        /// DerivedActivationView so active/dormant is derived identically to every other personal Character
        /// Effect (purchase record AND active relationship, no second ledger). The Wood Arrow recipe is
        /// exposed through Bushcraft exactly while that effect is active.</summary>
        public BushcraftRecipeCapability Resolve(
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
                if (row.Node.Key == FieldFletchingNode.Key)
                {
                    effectActive = row.Active;
                    break;
                }
            }

            // Exposure is the effect's sole consequence: an active Field Fletching I exposes the unchanged
            // vanilla Wood Arrow recipe through Bushcraft; a dormant/unpurchased effect exposes nothing.
            return effectActive
                ? new BushcraftRecipeCapability(true, true)
                : BushcraftRecipeCapability.None;
        }
    }
}
