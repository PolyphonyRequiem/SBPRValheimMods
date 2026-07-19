using System;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Archer;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Features.Progression;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Archer
{
    /// <summary>
    /// T026 — the net48 runtime seam that makes Field Fletching I actually craft on a joined client. Field
    /// Fletching I is a PERSONAL Character Effect (not a Stone-owned Local Node like Practice Range): while
    /// active for the acting occupant it EXPOSES the UNCHANGED vanilla Wood Arrow recipe through Bushcraft —
    /// i.e. makes the existing <c>ArrowWood</c> recipe craftable WITHOUT its ordinary crafting station
    /// (spec line 160; contracts.md §Archer "BushcraftRecipeProvider: active Field Fletching I exposes
    /// unchanged Wood Arrows through Bushcraft"). It authors and mutates NOTHING about the recipe's inputs,
    /// yield, or authority — this is an EXPOSURE gate only, consuming the shipped, unit-tested
    /// <see cref="BushcraftRecipeProvider"/> as its single authority.
    ///
    /// WHAT THIS BRIDGES (decomp seam — vanilla is fair game to read/adapt, AGENTS.md / ADR-0001):
    ///   * <c>Player.RequiredCraftingStation(recipe, qualityLevel, checkLevel)</c> (decomp :17790) — the
    ///     station GATE that <c>Player.GetAvailableRecipes</c> (:20443) and <c>Player.HaveRequirements</c>
    ///     consult. Vanilla returns FALSE when a recipe declares a required station and the player is not at
    ///     it. A postfix RESCUES exactly the vanilla Wood Arrow recipe (output item <c>ArrowWood</c>) to
    ///     TRUE — making it station-free (Bushcraft) — when (and only when) Field Fletching I is active for
    ///     the local occupant. When vanilla already PASSED we never flip it to false; we only rescue the
    ///     one recipe, only on the station requirement, only while the effect is active.
    ///
    /// SINGLE AUTHORITY: the active/exposed decision routes through
    /// <see cref="BushcraftRecipeProvider"/> (via the authoritative host projection below). This patch is a
    /// THIN adapter: it identifies the Wood Arrow recipe and applies the provider's exposure verdict. It
    /// re-derives no activation and holds no parallel ledger.
    ///
    /// ACTIVATION SOURCE (fail closed, honest transport scope): Field Fletching I is the FIRST personal
    /// Character Effect to reach runtime, and the bounded server→client delivery transport that Practice
    /// Range / Refined Workshop use carries LOCAL-effect snapshots only — there is not yet a personal
    /// Character-Effect replication channel. So this gate reads the authoritative projection where it EXISTS
    /// in-process: on the authoritative HOST (listen-server / singleplayer host) the composed
    /// <see cref="LocalProgressionObserver.Server"/> holds the character/authority/Stone stores, and the
    /// gate resolves the acting occupant's purchase + active relationship straight from them through
    /// <see cref="BushcraftRecipeProvider.Resolve"/>. On a PURE remote client the server runtime is null and
    /// there is no personal-effect snapshot to consume, so the gate FAILS CLOSED (Wood Arrow keeps its
    /// vanilla station requirement) rather than inventing an unauthenticated grant. The proven Bushcraft
    /// topology for T026 is therefore the host occupant; a personal-effect client delivery channel is a
    /// separate follow-up (see task handoff), exactly as the sibling Refined Workshop patch documented its
    /// listen-host self-delivery gap.
    ///
    /// References Valheim (Player, Recipe, ItemDrop, ZNet) → net48-only, NOT link-compiled into net8. The
    /// pure provider it drives is fully unit-tested. Clean-side (ADR-0001): base-game types only.
    /// </summary>
    [HarmonyPatch]
    internal static class FieldFletchingRecipeGate
    {
        // Reuse ONE catalog + provider instance for the process; the provider is a pure stateless projection.
        private static readonly Adapters.Archer.BushcraftRecipeProvider Provider =
            new Adapters.Archer.BushcraftRecipeProvider(new Domain.Content.HomesteadProgressionCatalog());

        /// <summary>Postfix on the recipe station gate. When vanilla refused a recipe because it requires a
        /// crafting station the player is not at, and that recipe is the vanilla Wood Arrow, and Field
        /// Fletching I is active for the local occupant, rescue the result to TRUE (station-free Bushcraft
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
                if (!IsWoodArrowRecipe(recipe)) return;             // only the exact vanilla Wood Arrow recipe.

                if (ResolveExposedForLocalOccupant())
                    __result = true;                                // Bushcraft: station-free while active.
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Archer] Field Fletching recipe gate postfix threw (ignored): " + ex.Message);
            }
        }

        /// <summary>The recipe whose output is the exact vanilla Wood Arrow item (<c>ArrowWood</c>). Matched
        /// by the recipe's output ItemDrop prefab name (clone-suffix stripped), never by a mutable display
        /// string. Field Fletching I exposes ONLY this recipe.</summary>
        private static bool IsWoodArrowRecipe(Recipe recipe)
        {
            var item = recipe.m_item;
            if (item == null || item.gameObject == null) return false;
            string name = StripCloneSuffix(item.gameObject.name);
            return string.Equals(name, BushcraftRecipeContent.WoodArrowItem, StringComparison.Ordinal);
        }

        /// <summary>Resolve whether Field Fletching I currently EXPOSES the Wood Arrow recipe for the LOCAL
        /// occupant, from the authoritative HOST projection (purchase record + active relationship, via the
        /// shipped <see cref="BushcraftRecipeProvider"/>). Fail closed: no server runtime (pure client),
        /// unresolvable identity / Stone, or any absent server-owned fact ⇒ false (recipe keeps its vanilla
        /// station requirement). No client-supplied claim is ever trusted.</summary>
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

            // Pull the three authoritative aggregates the pure provider needs, straight from the composed
            // server stores. Any missing aggregate ⇒ fail closed.
            var stone = server.Stones.GetStone(stoneId);
            var characterAgg = server.Characters.GetCharacter(occupant, character);
            var authority = server.Authority.GetAuthority(occupant, stoneId);
            if (stone == null || characterAgg == null || authority == null) return false;

            // Single authority: the shipped, unit-tested pure projection. Exposure == active Field
            // Fletching I (purchase record AND active relationship). No re-derivation here.
            return Provider.Resolve(stone, characterAgg, authority).WoodArrowRecipeExposed;
        }

        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }
    }
}
