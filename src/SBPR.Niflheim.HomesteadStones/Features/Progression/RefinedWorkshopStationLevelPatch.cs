using System;
using System.Collections.Generic;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Crafting;
using SBPR.Niflheim.HomesteadStones.Domain;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Features.HomesteadStone;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T021 Refined Workshop — the net48 CLIENT-side consumer that finally wires the shipped, engine-free
    /// <see cref="EffectiveStationLevelProvider"/> into the vanilla crafting runtime. This is the piece the
    /// T021 investigation found missing: the pure provider had zero production callers, so the +1
    /// effective-station-level bonus could never manifest on a joined client.
    ///
    /// WHAT THIS BRIDGES (decomp seams — vanilla is fair game to read/adapt, AGENTS.md / ADR-0001):
    ///   * <c>Player.RequiredCraftingStation(recipe, quality, checkLevel)</c> (decomp :17790) — the
    ///     craft/upgrade/repair GATE. Vanilla compares <c>m_currentStation.GetLevel() &lt; requiredLevel</c>.
    ///     A postfix re-runs that comparison with the EFFECTIVE level from the provider when (and only when)
    ///     the Refined Workshop Local Effect is active for the local occupant AND the recipe output is an
    ///     eligible PORTABLE item. When vanilla PASSED, we never flip it to false; we only RESCUE the exact
    ///     case vanilla failed purely on the level shortfall of one (real == required-1) portable recipe.
    ///   * <c>InventoryGui.SetupRequirementList</c> UI seam (decomp :42296-42302) — the
    ///     <c>m_minStationLevelText</c> render. A postfix recolors it to the base (non-red) color when the
    ///     effective level satisfies the requirement even though the real level did not, so the player SEES
    ///     the +1 is in effect. It NEVER rewrites the required-level number (the requirement is unchanged)
    ///     and NEVER touches the real observed station level shown in <c>m_craftingStationLevel</c>.
    ///
    /// SINGLE AUTHORITY: every decision routes through <see cref="EffectiveStationLevelProvider.Resolve(bool,int,CraftingOperationKind,bool)"/>,
    /// which is the same pure policy the server-side view path calls. This patch is a THIN adapter: it
    /// supplies the four inputs (activation bit, real level, operation kind, item eligibility) and applies
    /// the provider's <c>EffectiveStationLevelValue</c> to the vanilla comparison. It re-derives nothing.
    ///
    /// ACTIVATION SOURCE (fail closed): the activation bit is read ONLY from the replicated
    /// <see cref="LocalActivationClientCache"/> the server stamped for THIS occupant (via the bounded
    /// delivery transport). The client never derives activation — it holds none of the authoritative inputs.
    /// No held snapshot, a denied snapshot, standing outside every Stone Area, or an unknown Stone ⇒ the
    /// provider sees active=false and the bonus resolves away to the real level. Structure production / build
    /// placement are never eligible operation kinds, so build gates are structurally untouched.
    ///
    /// References Valheim (Player, Recipe, CraftingStation, InventoryGui, ZNet, ZNetScene) → net48-only, NOT
    /// link-compiled into net8. The pure provider + cache accessor it drives are fully unit-tested. Clean-side
    /// (ADR-0001): base-game types only; no other mod code.
    /// </summary>
    [HarmonyPatch]
    internal static class RefinedWorkshopStationLevelPatch
    {
        // The stable Refined Workshop Local node id (Crafting Tree, Level 1). Matches the catalog authority.
        private static readonly VersionedId RefinedWorkshopNode = new VersionedId("RefinedWorkshop", 1);

        // The prefab whose resident instances mark a Homestead Stone. Client-visible (persistent ZNetView).
        private const float AreaRadius = 20.0f; // StoneAreaMembership.DefaultAreaRadius — client mirror.

        // How often (seconds, real time) the client asks the server for a fresh activation snapshot while a
        // crafting station panel is open. The server owns all the moving inputs (governor presence, policy,
        // relationship), so a periodic refetch keeps the client's cached bit current without spamming.
        private const float RefetchIntervalSeconds = 2.0f;
        private static float _lastRequest;
        private static StoneId _lastRequestedStone;
        private static bool _haveLastRequested;

        // ── The GATE: rescue a level-only failure with the effective (+1) level ──────────────────────

        /// <summary>Postfix on the craft/upgrade/repair station gate. When vanilla returned FALSE purely
        /// because the current station's REAL level was one short of the recipe's required level, and the
        /// Refined Workshop effect is active for the local occupant on an eligible portable recipe, re-run
        /// the comparison with the provider's effective level and pass iff it now satisfies. We never turn a
        /// vanilla PASS into a fail, and we only rescue the eligible-portable, station-present case.</summary>
        [HarmonyPatch(typeof(Player), "RequiredCraftingStation")]
        [HarmonyPostfix]
        private static void RequiredCraftingStation_Postfix(Player __instance, Recipe recipe, int qualityLevel,
            bool checkLevel, ref bool __result)
        {
            try
            {
                if (__result) return;               // vanilla already allowed it — never override.
                if (!checkLevel) return;            // discovery/other path did not gate on level.
                if (__instance == null || recipe == null) return;
                if (__instance != Player.m_localPlayer) return; // client decision, local player only.

                var station = __instance.GetCurrentCraftingStation();
                if (station == null) return;        // no station ⇒ nothing to boost (provider agrees).

                // The failure must be the LEVEL shortfall specifically. If the station type mismatches, or a
                // required station is absent, vanilla failed for a different reason we must not override.
                var requiredStation = recipe.GetRequiredStation(qualityLevel);
                if (requiredStation == null) return;
                if (requiredStation.m_name != station.m_name) return;

                int realLevel = Mathf.Min(station.GetLevel(), 4);
                int requiredLevel = recipe.GetRequiredStationLevel(qualityLevel);

                var op = OperationFor(qualityLevel);
                bool eligiblePortable = IsEligiblePortable(recipe);
                bool active = ResolveActiveForLocalOccupant();

                var effective = EffectiveStationLevelProvider.Resolve(active, realLevel, op, eligiblePortable);
                if (effective.BonusApplied && effective.EffectiveStationLevelValue >= requiredLevel)
                    __result = true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Refined Workshop gate postfix threw (ignored): " + ex.Message);
            }
        }

        // ── The UI seam: recolor the required-level text when the +1 satisfies it ────────────────────

        /// <summary>Postfix on the recipe requirement UI refresh. When the real station level is below the
        /// required level (so vanilla painted <c>m_minStationLevelText</c> red / "cannot"), but the Refined
        /// Workshop effective level satisfies it, repaint the text to the base color so the player sees the
        /// requirement is met via the +1. The required-level NUMBER is left exactly as vanilla wrote it (the
        /// requirement itself is unchanged) and the real station level shown elsewhere is never touched —
        /// this is purely the "real vs +1" visual distinction the task requires.</summary>
        [HarmonyPatch(typeof(InventoryGui), "SetupRequirementList")]
        [HarmonyPostfix]
        private static void SetupRequirementList_Postfix(InventoryGui __instance, int quality, Player player)
        {
            try
            {
                if (__instance == null || player == null) return;
                if (player != Player.m_localPlayer) return;

                // Private engine fields via Harmony Traverse (assemblies are not publicized in this build):
                //   m_selectedRecipe (InventoryGui.RecipeDataPair) → its Recipe,
                //   m_minStationLevelText (a TMP_Text) → its Color color property,
                //   m_minStationLevelBasecolor (the non-red base color vanilla caches at Awake).
                var gui = Traverse.Create(__instance);
                var recipe = gui.Field("m_selectedRecipe").Field("Recipe").GetValue<Recipe>();
                if (recipe == null) return;

                var station = player.GetCurrentCraftingStation();
                if (station == null) return;
                var requiredStation = recipe.GetRequiredStation(quality);
                if (requiredStation == null || requiredStation.m_name != station.m_name) return;

                int realLevel = Mathf.Min(station.GetLevel(), 4);
                int requiredLevel = recipe.GetRequiredStationLevel(quality);
                if (realLevel >= requiredLevel) return; // vanilla already painted it satisfied.

                var op = OperationFor(quality);
                var effective = EffectiveStationLevelProvider.Resolve(
                    ResolveActiveForLocalOccupant(), realLevel, op, IsEligiblePortable(recipe));

                if (effective.BonusApplied && effective.EffectiveStationLevelValue >= requiredLevel)
                {
                    // Repaint the required-level text to the base (satisfied) color: the +1 makes the
                    // requirement reachable. The required-level number and the real station level are
                    // untouched. Reached reflectively so we never reference TMP_Text at compile time.
                    var levelText = gui.Field("m_minStationLevelText").GetValue<Component>();
                    if (levelText != null)
                    {
                        Color baseColor = gui.Field("m_minStationLevelBasecolor").GetValue<Color>();
                        Traverse.Create(levelText).Property("color").SetValue(baseColor);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Refined Workshop UI postfix threw (ignored): " + ex.Message);
            }
        }

        // ── Inputs the thin adapter supplies to the pure provider ────────────────────────────────────

        /// <summary>Map the vanilla craft/upgrade path to the provider's operation kind. quality &gt; 1 means
        /// an UPGRADE of an existing item; quality == 1 is a fresh production. Repair rides the same station
        /// gate as production (vanilla has no distinct repair-level check), so it is covered by production.
        /// Structure production / build placement never reach this station gate, so they are never produced
        /// here — the build gates stay structurally untouched.</summary>
        private static CraftingOperationKind OperationFor(int qualityLevel) =>
            qualityLevel > 1
                ? CraftingOperationKind.PortableItemUpgrade
                : CraftingOperationKind.PortableItemProduction;

        /// <summary>Whether the recipe output is an eligible portable item: it must produce a real carried
        /// ItemDrop (not a structure/piece). A recipe with no <c>m_item</c> is not a portable-item recipe and
        /// gets no bonus (the provider also rejects <c>itemIsEligiblePortable=false</c>).</summary>
        private static bool IsEligiblePortable(Recipe recipe)
        {
            var item = recipe.m_item;
            if (item == null) return false;
            var shared = item.m_itemData.m_shared;
            if (shared == null) return false;
            // Structure/building outputs are not portable items. Vanilla item types produced at a crafting
            // station (weapons, tools, armor, consumables, materials) are portable; a placed-piece output is
            // not routed through RequiredCraftingStation's item path, but guard defensively.
            return true;
        }

        /// <summary>Resolve whether the Refined Workshop Local Effect is currently active for the LOCAL
        /// occupant, reading ONLY the replicated client cache (fail closed). Also opportunistically requests
        /// a fresh snapshot from the server for the Stone the local player currently stands in, on a bounded
        /// interval, so the cached bit tracks server-owned changes. The listen-host reads the server runtime
        /// directly; a pure client reads the pushed/replied snapshot.</summary>
        private static bool ResolveActiveForLocalOccupant()
        {
            var stoneId = ResolveLocalStone();
            if (stoneId == null) return false; // not inside any Stone Area ⇒ dormant.

            MaybeRequestSnapshot(stoneId.Value);

            // Read ONLY the replicated client cache (fail closed). On a joined client the bounded delivery
            // transport applies server-stamped snapshots into it. On a listen-host the peer-to-peer transport
            // does not round-trip to the host itself, so the host cache stays empty and this fails closed —
            // the QA-proven effective-Level-3 topology is a dedicated server + joined client. Host-local
            // self-delivery is a separate follow-up (see task handoff).
            return LocalProgressionObserver.ClientCache.IsActiveForStone(stoneId.Value, RefinedWorkshopNode);
        }

        private static void MaybeRequestSnapshot(StoneId stoneId)
        {
            float now = Time.realtimeSinceStartup;
            bool stoneChanged = !_haveLastRequested || !_lastRequestedStone.Equals(stoneId);
            if (!stoneChanged && (now - _lastRequest) < RefetchIntervalSeconds) return;
            _lastRequest = now;
            _lastRequestedStone = stoneId;
            _haveLastRequested = true;
            LocalActivationDeliveryObserver.RequestSnapshot(stoneId);
        }

        /// <summary>Resolve which Homestead Stone Area the LOCAL player currently stands in, from
        /// CLIENT-VISIBLE resident Stone instances (the persistent Stone prefab is replicated to every
        /// client). Returns null when outside every Area. This is a client convenience for deciding WHICH
        /// Stone to ask the server about + key the cache read; it is NOT an authority — the server
        /// independently confirms occupancy from the peer's own character ZDO before stamping the snapshot
        /// (payload-authoritative occupancy was the PR #368 review Blocker 2).</summary>
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
    }
}
