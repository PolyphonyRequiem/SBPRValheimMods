using System;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Archer;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Features.HomesteadStone;
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
    /// SINGLE AUTHORITY: the active/exposed decision routes through the authoritative projection —
    /// <see cref="BushcraftRecipeProvider"/> on the host, or the server-stamped
    /// <see cref="PersonalActivationSnapshot"/> on a pure client. This patch is a THIN adapter: it identifies
    /// the Wood Arrow recipe and applies the projection's exposure verdict. It re-derives no activation and
    /// holds no parallel ledger.
    ///
    /// ACTIVATION SOURCE (fail closed, two authoritative paths — T026 remediation):
    ///   * On the authoritative HOST (listen-server / singleplayer host) the composed
    ///     <see cref="LocalProgressionObserver.Server"/> holds the character/authority/Stone stores, and the
    ///     gate resolves the acting occupant's purchase + active relationship straight from them through
    ///     <see cref="BushcraftRecipeProvider.Resolve"/>.
    ///   * On a PURE remote CLIENT the server runtime is null; the gate reads ONLY the bounded personal
    ///     read model the server pushed into <see cref="LocalProgressionObserver.PersonalClientCache"/> over
    ///     the <see cref="PersonalActivationDeliveryObserver"/> transport, and opportunistically requests a
    ///     fresh snapshot for the Stone the local player stands in on a bounded interval. The server only
    ///     ever delivers a snapshot with the effect active when IT derived (server-owned purchase + active
    ///     relationship) that the occupant is entitled, so an active held snapshot is authoritative proof;
    ///     the absence of one FAILS CLOSED (Wood Arrow keeps its vanilla station requirement). The client
    ///     never authors entitlement.
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

        // The stable Field Fletching I personal node id (Archer Tree, Level 1). Matches the catalog authority.
        private static readonly VersionedId FieldFletchingNode =
            Adapters.Archer.BushcraftRecipeProvider.FieldFletchingNode;

        // The client-visible Homestead Stone Area radius mirror (see RefinedWorkshopStationLevelPatch).
        private const float AreaRadius = 20.0f;

        // How often (seconds, real time) a pure client asks the server for a fresh personal-effect snapshot
        // while resolving exposure. The server owns the moving inputs (purchase, relationship), so a periodic
        // refetch keeps the cached bit current without spamming.
        private const float RefetchIntervalSeconds = 2.0f;
        private static float _lastRequest;
        private static StoneId _lastRequestedStone;
        private static bool _haveLastRequested;

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
        /// occupant. Two authoritative paths (fail closed on either):
        ///   * HOST: resolve the acting occupant's purchase + active relationship from the composed server
        ///     stores via the shipped <see cref="BushcraftRecipeProvider"/>.
        ///   * PURE CLIENT: read the server-stamped personal snapshot from the bounded client cache, and
        ///     opportunistically refetch for the Stone the local player stands in.
        /// No server runtime and no held snapshot ⇒ false (recipe keeps its vanilla station requirement). No
        /// client-supplied claim is ever trusted.</summary>
        private static bool ResolveExposedForLocalOccupant()
        {
            var server = LocalProgressionObserver.Server;
            if (server != null)
                return ResolveHostExposed(server);

            // Pure client: consult the authoritative read model the server pushed, keyed by the Stone the
            // local player stands in. Fail closed when no active snapshot for the Field Fletching node is held.
            var stoneId = ResolveLocalStone();
            if (stoneId == null) return false;                      // outside every Stone Area ⇒ not exposed.

            MaybeRequestSnapshot(stoneId.Value);
            return LocalProgressionObserver.PersonalClientCache.IsActiveForStone(stoneId.Value, FieldFletchingNode);
        }

        /// <summary>Authoritative HOST path: resolve the acting occupant's purchase + active relationship
        /// straight from the composed server stores (world-owned Stone Area membership + transport-bound
        /// principal), then ask the shipped pure provider the exposure question. Fail closed on any absent
        /// server-owned fact.</summary>
        private static bool ResolveHostExposed(LocalProgressionServer server)
        {
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

        private static void MaybeRequestSnapshot(StoneId stoneId)
        {
            float now = Time.realtimeSinceStartup;
            bool stoneChanged = !_haveLastRequested || !_lastRequestedStone.Equals(stoneId);
            if (!stoneChanged && (now - _lastRequest) < RefetchIntervalSeconds) return;
            _lastRequest = now;
            _lastRequestedStone = stoneId;
            _haveLastRequested = true;
            PersonalActivationDeliveryObserver.RequestSnapshot(stoneId);
        }

        /// <summary>Resolve which Homestead Stone Area the LOCAL player currently stands in, from
        /// CLIENT-VISIBLE resident Stone instances (the persistent Stone prefab is replicated to every
        /// client). Returns null when outside every Area. This is a client convenience for deciding WHICH
        /// Stone to ask the server about + key the cache read; it is NOT an authority — the server
        /// independently resolves the requesting peer's bound principal before stamping the snapshot.</summary>
        private static StoneId? ResolveLocalStone()
        {
            var player = Player.m_localPlayer;
            var znet = ZNet.instance;
            if (player == null || znet == null) return null;

            var world = new WorldId(HomesteadWorldIdentity.FromUid(znet.GetWorldUID()));
            Vector3 pp = player.transform.position;

            var stones = HomesteadStoneClientIndex.ResidentStones();
            if (stones.Count == 0) return null;

            StoneId? best = null;
            float bestSq = AreaRadius * AreaRadius;
            foreach (var s in stones)
            {
                float dx = pp.x - s.X, dz = pp.z - s.Z;
                float sq = (dx * dx) + (dz * dz);
                if (sq > AreaRadius * AreaRadius) continue;
                if (best == null || sq < bestSq)
                {
                    bestSq = sq;
                    best = StoneId.FromHostZone(world, s.ZoneX, s.ZoneZ);
                }
            }
            return best;
        }

        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }
    }
}
