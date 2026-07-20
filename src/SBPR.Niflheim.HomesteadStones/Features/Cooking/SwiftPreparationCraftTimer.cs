using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Cooking;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Features.Progression;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Cooking
{
    /// <summary>
    /// T019 — the net48 runtime seam that makes Swift Preparation actually shorten an eligible
    /// menu-crafted food's craft on a joined client. Swift Preparation is the sole executable Tier-2 Cooking
    /// node, a PERSONAL Character Effect (data-model.md §"Cooking | 2 | Swift Preparation | Character Effect
    /// | personal Offered"): while active for the acting occupant it multiplies the vanilla Cooking-skill-
    /// ADJUSTED menu-craft duration of an eligible menu-crafted food by 1/3 (spec §US4 sc1; contracts.md
    /// §Cooking "MenuCraftDurationProvider: Swift Preparation supplies factor 1/3 after vanilla Cooking-skill
    /// adjustment for eligible menu-crafted food only").
    ///
    /// WHAT THIS BRIDGES (decomp seam — vanilla is fair game to read/adapt, AGENTS.md / ADR-0001):
    ///   * <c>InventoryGui.UpdateRecipe(Player, float)</c> (decomp assembly_valheim :42372-42386) computes the
    ///     menu-craft duration into a LOCAL: <c>num5 = m_multiCrafting ? m_multiCraftDuration : m_craftDuration</c>,
    ///     then applies the VANILLA SKILL ADJUSTMENT <c>num5 *= 1 - GetSkillFactor(station.m_craftingSkill) *
    ///     m_craftDurationSkillMaxDecrease</c>, feeds it to <c>m_craftProgressBar.SetMaxValue(num5)</c>, and
    ///     completes the craft when the accumulating <c>m_craftTimer</c> reaches that same <c>num5</c>. Because
    ///     both the progress-bar max AND the completion comparison read the SAME local, a transpiler that scales
    ///     <c>num5</c> in place — inserted at the <c>SetMaxValue(num5)</c> call site, i.e. AFTER the skill
    ///     adjustment already ran — shortens the whole menu craft by exactly the provider's factor, strictly
    ///     after vanilla Cooking-skill adjustment, and touches NO recipe, ItemDrop prefab, station, or other
    ///     craft. It only rescales this frame's local craft-timer max.
    ///
    /// WHY A TRANSPILER (not a postfix): <c>num5</c> is a stack-local with no returned value to postfix, and the
    /// completion check reads it directly, so the ONLY faithful "shorten the whole menu craft" seam is to scale
    /// the local before it is consumed. The transpiler is a minimal, anchored insertion (find the single
    /// <c>callvirt GuiBar.SetMaxValue</c> and inject a scale of the local it pushes); it changes no control flow.
    ///
    /// ELIGIBILITY + NO COMPLETION: the single gameplay decision routes through the shipped, unit-tested pure
    /// <see cref="MenuCraftDurationProvider"/>. The seam classifies the craft from engine-observed facts (recipe
    /// output is a food item + the current station's crafting skill is Cooking + this is the menu-craft path,
    /// always true here) and asks the provider for the scaled duration. Ineligible crafts (non-food, non-Cooking
    /// station) get factor 1 and run at full vanilla duration; a non-positive duration is returned unchanged, so
    /// the seam never instant-completes or fabricates a craft (AT-NO-COOKING-COMPLETION). This is a THIN adapter:
    /// it re-derives no activation and holds no parallel ledger.
    ///
    /// ACTIVATION SOURCE (fail closed, honest transport scope): Swift Preparation is a personal Character
    /// Effect, and the bounded server→client delivery transport that Savor / Practice Range / Refined Workshop
    /// use carries LOCAL-effect snapshots only — there is not yet a personal Character-Effect replication
    /// channel. So this seam reads the authoritative projection where it EXISTS in-process: on the authoritative
    /// HOST (listen-server / singleplayer host) the composed <see cref="LocalProgressionObserver.Server"/> holds
    /// the character/authority/Stone stores, and the seam resolves the acting occupant's Swift Preparation
    /// purchase + active relationship straight from them through the shipped T004
    /// <see cref="DerivedActivationView"/>. On a PURE remote client the server runtime is null and there is no
    /// personal-effect snapshot to consume, so the seam FAILS CLOSED (the craft keeps its full vanilla
    /// skill-adjusted duration) rather than inventing an unauthenticated grant. The proven topology for T019 is
    /// therefore the host occupant; a personal-effect client delivery channel is a separate follow-up, exactly
    /// as the sibling Field Prep / Iron Stomach / Field Fletching / Refined Workshop seams documented their
    /// host-only scope.
    ///
    /// References Valheim (InventoryGui, Player, Recipe, CraftingStation, ItemDrop, ZNet) → net48-only, NOT
    /// link-compiled into net8. The pure provider it drives is fully unit-tested. Clean-side (ADR-0001):
    /// base-game types only.
    /// </summary>
    [HarmonyPatch]
    internal static class SwiftPreparationCraftTimer
    {
        // Reuse ONE provider instance for the process; it is a pure stateless projection.
        private static readonly MenuCraftDurationProvider Provider = new MenuCraftDurationProvider();

        /// <summary>Transpiler on <c>InventoryGui.UpdateRecipe</c>. Finds the single
        /// <c>m_craftProgressBar.SetMaxValue(num5)</c> call site and, immediately before the <c>ldloc num5</c>
        /// that pushes the (already skill-adjusted) menu-craft duration onto the stack for that call, injects a
        /// scale of the local: <c>num5 = ScaleMenuCraftDuration(this, num5)</c>. Because both the progress-bar
        /// max and the completion comparison read the same local, the whole craft is shortened by the provider's
        /// factor for an eligible active occupant, and left untouched otherwise. If the anchor is not found the
        /// original IL is returned unchanged (fail closed: vanilla duration).</summary>
        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> UpdateRecipe_Transpiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var code = new List<CodeInstruction>(instructions);

            // Locate the SetMaxValue call: ... ldloc <num5>; callvirt GuiBar.SetMaxValue(float). GuiBar lives
            // in a Valheim assembly this project does not reference at compile time, so match by method NAME +
            // declaring-type name (never a compile-time GuiBar handle).
            int callIdx = -1;
            for (int i = 0; i < code.Count; i++)
            {
                if ((code[i].opcode == OpCodes.Callvirt || code[i].opcode == OpCodes.Call) &&
                    code[i].operand is MethodInfo mi &&
                    mi.Name == "SetMaxValue" && mi.DeclaringType != null && mi.DeclaringType.Name == "GuiBar")
                {
                    callIdx = i;
                    break;
                }
            }
            if (callIdx < 0) return code; // anchor gone → run vanilla unchanged.

            // The instruction pushing num5 is the ldloc immediately preceding the call (the SetMaxValue arg).
            int ldIdx = callIdx - 1;
            if (ldIdx < 0 || !IsLdloc(code[ldIdx])) return code;

            var numLocal = ResolveLocal(code[ldIdx], il);
            if (numLocal == null) return code;

            // Inject BEFORE the ldloc num5: num5 = ScaleMenuCraftDuration(this, num5).
            var inject = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),                    // this (InventoryGui)
                new CodeInstruction(OpCodes.Ldloc, numLocal),           // num5 (skill-adjusted duration)
                new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(SwiftPreparationCraftTimer), nameof(ScaleMenuCraftDuration))),
                new CodeInstruction(OpCodes.Stloc, numLocal),           // num5 = scaled
            };
            code.InsertRange(ldIdx, inject);
            return code;
        }

        private static bool IsLdloc(CodeInstruction ci) =>
            ci.opcode == OpCodes.Ldloc || ci.opcode == OpCodes.Ldloc_S ||
            ci.opcode == OpCodes.Ldloc_0 || ci.opcode == OpCodes.Ldloc_1 ||
            ci.opcode == OpCodes.Ldloc_2 || ci.opcode == OpCodes.Ldloc_3;

        /// <summary>Resolve the LocalBuilder the ldloc pushes. For the short/indexed forms we cannot rebuild a
        /// LocalBuilder handle (Harmony gives us only the numeric index), so we require the explicit
        /// <c>ldloc</c>/<c>ldloc.s</c> forms whose operand IS the local — the C# compiler emits <c>ldloc.s</c>
        /// for a local of num5's index in this method, so the anchor is stable. Returns null on the numbered
        /// short forms (fail closed: vanilla duration).</summary>
        private static LocalBuilder? ResolveLocal(CodeInstruction ci, ILGenerator il)
        {
            if ((ci.opcode == OpCodes.Ldloc || ci.opcode == OpCodes.Ldloc_S) && ci.operand is LocalBuilder lb)
                return lb;
            return null;
        }

        /// <summary>The scale helper the transpiler injects. Given the InventoryGui and the vanilla
        /// skill-adjusted menu-craft duration, classify the currently selected recipe's craft and ask the
        /// shipped pure provider for the shortened duration when Swift Preparation is active for the local
        /// occupant on an eligible menu-crafted food. Returns the input UNCHANGED on any resolution gap /
        /// ineligible craft / dormant effect (fail closed). Never throws into the vanilla path.</summary>
        public static float ScaleMenuCraftDuration(InventoryGui gui, float skillAdjustedDuration)
        {
            try
            {
                if (gui == null || skillAdjustedDuration <= 0f) return skillAdjustedDuration;

                var player = Player.m_localPlayer;
                if (player == null) return skillAdjustedDuration;

                // The selected recipe + current station are engine-observed facts of THIS menu craft.
                // InventoryGui.m_selectedRecipe is an InventoryGui.RecipeDataPair whose Recipe member is a C#
                // AUTO-PROPERTY (compiler backing field <Recipe>k__BackingField), NOT a plain field — so
                // Harmony Traverse must resolve it as .Property("Recipe"); .Field("Recipe") returns null and the
                // 1/3 effect silently never fires. Mirrors the RecipeDataPair.Recipe access in
                // RefinedWorkshopStationLevelPatch.
                var recipe = Traverse.Create(gui).Field("m_selectedRecipe").Property("Recipe").GetValue<Recipe>();
                if (recipe == null || recipe.m_item == null) return skillAdjustedDuration;

                bool outputIsFood = RecipeOutputIsFood(recipe);
                int stationSkill = CurrentStationCookingSkill(player);

                // This IS the menu-craft path (InventoryGui.UpdateRecipe), so isMenuCraft is true by construction.
                var eligibility = MenuCraftDurationProvider.ClassifyCraft(outputIsFood, stationSkill, isMenuCraft: true);
                if (!MenuCraftDurationProvider.IsEligible(eligibility)) return skillAdjustedDuration;

                bool active = ResolveActiveForLocalOccupant();
                var decision = Provider.ResolveDuration(active, eligibility, skillAdjustedDuration);
                return (float)decision.Duration;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Cooking] Swift Preparation craft-timer scale threw (ignored, running vanilla): " + ex.Message);
                return skillAdjustedDuration;
            }
        }

        /// <summary>Whether the recipe's output item is a FOOD (its shared data grants food value). Cooked-food
        /// outputs carry <c>m_food &gt; 0</c> (or food stamina/eitr); this is the engine-observed food flag,
        /// never a client claim.</summary>
        private static bool RecipeOutputIsFood(Recipe recipe)
        {
            var shared = recipe.m_item.m_itemData.m_shared;
            if (shared == null) return false;
            return shared.m_food > 0f || shared.m_foodStamina > 0f || shared.m_foodEitr > 0f;
        }

        /// <summary>The current crafting station's crafting-skill enum value as an int, or 0 (None) when there is
        /// no station or it has no crafting skill. Compared by the provider against
        /// <see cref="MenuCraftDurationProvider.CookingSkill"/> (Skills.SkillType.Cooking == 105).</summary>
        private static int CurrentStationCookingSkill(Player player)
        {
            var station = player.GetCurrentCraftingStation();
            if (station == null) return 0;
            return (int)station.m_craftingSkill;
        }

        /// <summary>Resolve whether Swift Preparation currently DELIVERS for the LOCAL occupant, from the
        /// authoritative HOST projection (purchase record + active relationship, via the shipped T004
        /// <see cref="DerivedActivationView"/> and the pure provider). Fail closed: no server runtime (pure
        /// client), unresolvable identity / Stone, or any absent server-owned fact ⇒ false (full vanilla
        /// duration). No client-supplied claim is ever trusted. Mirrors the FieldPrepRecipeGate host resolver.</summary>
        private static bool ResolveActiveForLocalOccupant()
        {
            var server = LocalProgressionObserver.Server;
            if (server == null) return false;                       // pure client — no personal-effect snapshot yet.

            var foundational = FoundationalPlacementObserver.Server;
            var player = Player.m_localPlayer;
            var znet = ZNet.instance;
            if (foundational == null || player == null || znet == null) return false;

            // Swift Preparation is a personal Character Effect activated by purchase + active relationship. Its
            // activation is NOT Stone-Area-scoped for delivery (a Character Effect follows the character), but we
            // still need the Stone whose Offered node was purchased to derive the view. Resolve the Stone the
            // occupant currently stands in (world-owned membership), matching the Field Prep host resolver.
            Vector3 pp = player.transform.position;
            if (!foundational.StoneAreas.TryResolve(pp.x, pp.z, out var stoneId))
                return false;

            long actingPlayerId = player.GetPlayerID();
            string peerKey = ServerCreatorIdentity.CharacterSubject(actingPlayerId);
            if (string.IsNullOrEmpty(peerKey) ||
                !foundational.BoundSessions.TryResolve(peerKey, out var principal))
                return false;

            var occupant = principal.Account;
            var character = principal.Character;
            if (string.IsNullOrEmpty(occupant.Value)) return false;

            var stone = server.Stones.GetStone(stoneId);
            var characterAgg = server.Characters.GetCharacter(occupant, character);
            var authority = server.Authority.GetAuthority(occupant, stoneId);
            if (stone == null || characterAgg == null || authority == null) return false;

            var view = DerivedActivationView.Derive(stone, characterAgg, authority);
            return Provider.IsActive(view);
        }
    }
}
