using System;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Cooking;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Features.Progression;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Cooking
{
    /// <summary>
    /// T017 — the net48 runtime seam that makes Field Prep actually craft on a joined client. Field Prep
    /// is a PERSONAL Character Effect (not a Stone-owned Local Node like Savor the Hearth): while active
    /// for the acting occupant it EXPOSES the UNCHANGED vanilla Boar Jerky and Queen's Jam recipes through
    /// Bushcraft — i.e. makes the existing <c>BoarJerky</c> / <c>QueensJam</c> recipes craftable WITHOUT
    /// their ordinary Cooking crafting station (spec §US4 line 148-149; contracts.md §Cooking
    /// "CookingCraftPolicy: Field Prep eligibility plus normal Cooking skill XP, speed, and bonus-output
    /// behavior for unchanged Boar Jerky/Queen's Jam recipes through Bushcraft"). It authors and mutates
    /// NOTHING about the recipes' inputs, yield, authority, or the ordinary Cooking mechanics (XP, craft
    /// speed, bonus output) that run when they are crafted — this is an EXPOSURE gate only, consuming the
    /// shipped, unit-tested <see cref="CookingCraftPolicy"/> as its single authority.
    ///
    /// WHAT THIS BRIDGES (decomp seam — vanilla is fair game to read/adapt, AGENTS.md / ADR-0001):
    ///   * <c>Player.RequiredCraftingStation(recipe, qualityLevel, checkLevel)</c> — the station GATE that
    ///     <c>Player.GetAvailableRecipes</c> and <c>Player.HaveRequirements</c> consult. Vanilla returns
    ///     FALSE when a recipe declares a required station and the player is not at it. A postfix RESCUES
    ///     exactly the vanilla Boar Jerky / Queen's Jam recipes (output items <c>BoarJerky</c>/
    ///     <c>QueensJam</c>) to TRUE — making them station-free (Bushcraft) — when (and only when) Field
    ///     Prep is active for the local occupant. When vanilla already PASSED we never flip it to false; we
    ///     only rescue those two recipes, only on the station requirement, only while the effect is active.
    ///     Because we only touch the station gate, the ordinary Cooking XP/speed/bonus mechanics that run on
    ///     craft are entirely untouched (spec "normal Cooking skill XP, speed, and bonus-output behavior").
    ///
    /// SINGLE AUTHORITY: the active/exposed decision routes through <see cref="CookingCraftPolicy"/> (via
    /// the authoritative host projection below). This patch is a THIN adapter: it identifies the two
    /// Field Prep recipes and applies the policy's exposure verdict. It re-derives no activation and holds
    /// no parallel ledger.
    ///
    /// ACTIVATION SOURCE (fail closed, honest transport scope): Field Prep is a personal Character Effect,
    /// and the bounded server→client delivery transport that Savor / Practice Range / Refined Workshop use
    /// carries LOCAL-effect snapshots only — there is not yet a personal Character-Effect replication
    /// channel. So this gate reads the authoritative projection where it EXISTS in-process: on the
    /// authoritative HOST (listen-server / singleplayer host) the composed
    /// <see cref="LocalProgressionObserver.Server"/> holds the character/authority/Stone stores, and the
    /// gate resolves the acting occupant's purchase + active relationship straight from them through
    /// <see cref="CookingCraftPolicy.Resolve"/>. On a PURE remote client the server runtime is null and
    /// there is no personal-effect snapshot to consume, so the gate FAILS CLOSED (the recipes keep their
    /// vanilla station requirement) rather than inventing an unauthenticated grant. The proven Bushcraft
    /// topology for T017 is therefore the host occupant; a personal-effect client delivery channel is a
    /// separate follow-up (see task handoff), exactly as the sibling Field Fletching / Refined Workshop
    /// seams documented their host-only scope.
    ///
    /// References Valheim (Player, Recipe, ItemDrop, ZNet) → net48-only, NOT link-compiled into net8. The
    /// pure policy it drives is fully unit-tested. Clean-side (ADR-0001): base-game types only.
    /// </summary>
    [HarmonyPatch]
    internal static class FieldPrepRecipeGate
    {
        // Reuse ONE catalog + policy instance for the process; the policy is a pure stateless projection.
        private static readonly CookingCraftPolicy Policy =
            new CookingCraftPolicy(new Domain.Content.HomesteadProgressionCatalog());

        /// <summary>Postfix on the recipe station gate. When vanilla refused a recipe because it requires a
        /// crafting station the player is not at, and that recipe is Boar Jerky or Queen's Jam, and Field
        /// Prep is active for the local occupant, rescue the result to TRUE (station-free Bushcraft
        /// exposure). We never turn a vanilla PASS into a fail and never touch any other recipe.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.RequiredCraftingStation))]
        [HarmonyPostfix]
        private static void RequiredCraftingStation_Postfix(Player __instance, Recipe recipe, ref bool __result)
        {
            try
            {
                if (__result) return;                               // vanilla already allowed it — never override.
                if (__instance == null || recipe == null) return;
                if (__instance != Player.m_localPlayer) return;     // client decision, local player only.

                string outputItem = RecipeOutputItem(recipe);
                if (!BushcraftCookingContent.IsFieldPrepRecipeItem(outputItem)) return; // only the two Field Prep recipes.

                if (ResolveExposedForLocalOccupant())
                    __result = true;                                // Bushcraft: station-free while active.
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Cooking] Field Prep recipe gate postfix threw (ignored): " + ex.Message);
            }
        }

        /// <summary>The output item id of a recipe, matched by the recipe's output ItemDrop prefab name
        /// (clone-suffix stripped), never by a mutable display string. Returns empty when unresolvable.</summary>
        private static string RecipeOutputItem(Recipe recipe)
        {
            var item = recipe.m_item;
            if (item == null || item.gameObject == null) return string.Empty;
            return StripCloneSuffix(item.gameObject.name);
        }

        /// <summary>Resolve whether Field Prep currently EXPOSES its recipes for the LOCAL occupant, from
        /// the authoritative HOST projection (purchase record + active relationship, via the shipped
        /// <see cref="CookingCraftPolicy"/>). Fail closed: no server runtime (pure client), unresolvable
        /// identity / Stone, or any absent server-owned fact ⇒ false (recipes keep their vanilla station
        /// requirement). No client-supplied claim is ever trusted.</summary>
        private static bool ResolveExposedForLocalOccupant()
        {
            var server = LocalProgressionObserver.Server;
            if (server == null) return false;                       // pure client — no personal-effect snapshot yet.

            var foundational = FoundationalPlacementObserver.Server;
            var player = Player.m_localPlayer;
            var znet = ZNet.instance;
            if (foundational == null || player == null || znet == null) return false;

            // Which Homestead Stone Area is the local occupant standing in? Resolve from the server-owned
            // Stone Area membership at the player's position (world-owned identity, never a client claim).
            Vector3 pp = player.transform.position;
            if (!foundational.StoneAreas.TryResolve(pp.x, pp.z, out var stoneId))
                return false;                                        // outside every Stone Area ⇒ not exposed.

            // Acting bound INTERNAL principal (server-minted account/character), keyed by the same
            // player:<s_playerID> subject the character admission binds under — never the payload.
            long actingPlayerId = player.GetPlayerID();
            string peerKey = ServerCreatorIdentity.CharacterSubject(actingPlayerId);
            if (string.IsNullOrEmpty(peerKey) ||
                !foundational.BoundSessions.TryResolve(peerKey, out var principal))
                return false;

            var occupant = principal.Account;
            var character = principal.Character;
            if (string.IsNullOrEmpty(occupant.Value)) return false;

            // Pull the three authoritative aggregates the pure policy needs, straight from the composed
            // server stores. Any missing aggregate ⇒ fail closed.
            var stone = server.Stones.GetStone(stoneId);
            var characterAgg = server.Characters.GetCharacter(occupant, character);
            var authority = server.Authority.GetAuthority(occupant, stoneId);
            if (stone == null || characterAgg == null || authority == null) return false;

            // Single authority: the shipped, unit-tested pure projection. Exposure == active Field Prep
            // (purchase record AND active relationship). No re-derivation here.
            return Policy.Resolve(stone, characterAgg, authority).CookingRecipesExposed;
        }

        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }
    }
}
