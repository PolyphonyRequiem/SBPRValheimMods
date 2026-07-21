using System;
using System.Collections.Generic;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Crafting;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Features.Progression;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Crafting
{
    /// <summary>
    /// T022 Masterwork — the net48 runtime seam that issues one exact-instance Workmanship Property onto a
    /// freshly crafted/upgraded eligible item, and validates existing stamps (transfer/tamper), while the
    /// local occupant's personal Masterwork Character Effect is active. It consumes the shipped, unit-tested
    /// <see cref="WorkmanshipIssuanceProvider"/> (the issue decision) and <see cref="WorkmanshipCodec"/> (the
    /// stamp/read/validate) as its single authorities; this patch is a THIN adapter that supplies the
    /// server-observed produced-item facts and writes the resulting stamp onto the item's real custom data.
    ///
    /// WHAT THIS BRIDGES (decomp seam — vanilla is fair game, AGENTS.md / ADR-0001):
    ///   * <c>InventoryGui.DoCrafting(Player)</c> (decomp :42523) — the craft/upgrade completion. Vanilla
    ///     appends the produced item to the crafter's inventory via <c>Inventory.AddItem(prefabName, count,
    ///     quality, variant, playerID, playerName, pos)</c> (:42576). A postfix locates that just-produced
    ///     item and, when Masterwork is active for the crafter and the item is an eligible non-stackable
    ///     durable output that is not already stamped, stamps the deterministic Workmanship Property onto its
    ///     <c>m_customData</c> and EXPLICITLY marks the item so vanilla saves/replicates it (the item rides
    ///     the crafter's own profile/character save like every other custom-data field — research.md line
    ///     137). We never mutate the recipe, cost, quality, or any shared prefab (ADR-0006 additive): only
    ///     our own domain-prefixed keys on this one instance's dictionary.
    ///
    /// ELIGIBILITY (server-observed, never a client claim): non-stackable is <c>m_shared.m_maxStackSize == 1</c>;
    /// durable is <c>m_shared.m_useDurability</c> (weapons/tools/armor wear; arrows/food/materials do not).
    /// Both must hold (<see cref="WorkmanshipCodec.IsEligible"/>) — a stack shares one ItemData so a
    /// per-instance provenance stamp on it is meaningless, and a non-durable output is out of scope.
    ///
    /// AUTHORITATIVE HOST ONLY (fail closed): issuance requires the server key + the composed server stores.
    /// It runs only on the authoritative host (listen-server / singleplayer host), where
    /// <see cref="LocalProgressionObserver.Server"/> is composed and <see cref="Armed"/> holds the durable
    /// integrity key. A PURE remote client has neither — it issues nothing (the crafted item stays a plain
    /// vanilla item there) but STILL correctly VALIDATES an already-issued stamp for display/transfer, since
    /// validation is a pure read that only needs the key... which a pure client also lacks, so on a pure
    /// client an existing stamp reads as neither confirmed nor forged and simply presents as vanilla. The
    /// QA-proven issuance topology is therefore a listen-host / dedicated-host crafter; authoritative
    /// server→client Workmanship replication for a pure remote crafter is the documented follow-up (mirrors
    /// the T021/T026 host-first-then-pure-client-delivery precedent — see the task handoff and evidence).
    ///
    /// References Valheim (InventoryGui, Player, Inventory, ItemDrop) → net48-only, NOT link-compiled into
    /// net8. The pure provider + codec it drives are fully unit-tested. Clean-side (ADR-0001): base-game
    /// types only; no other mod code.
    /// </summary>
    [HarmonyPatch]
    internal static class MasterworkIssuanceObserver
    {
        private static readonly WorkmanshipIssuanceProvider Provider =
            new WorkmanshipIssuanceProvider(new Domain.Content.HomesteadProgressionCatalog());

        /// <summary>The durable server integrity key, armed by the runtime bootstrap on the authoritative
        /// host. Null on a pure client / before composition / after teardown — issuance fails closed.</summary>
        internal static WorkmanshipIntegrityKey? Armed;

        /// <summary>Arm the issuance seam with the durable server integrity key (authoritative host only).</summary>
        internal static void Arm(WorkmanshipIntegrityKey key) => Armed = key;

        /// <summary>Disarm on teardown so a subsequent world/session cannot issue under a stale key.</summary>
        internal static void Disarm() => Armed = null;

        /// <summary>Postfix on craft/upgrade completion. Locate the just-produced item in the crafter's
        /// inventory and, on the authoritative host with Masterwork active, stamp one deterministic
        /// Workmanship Property onto an eligible non-stackable durable output.</summary>
        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        [HarmonyPostfix]
        private static void DoCrafting_Postfix(InventoryGui __instance, Player player)
        {
            try
            {
                var key = Armed;
                var server = LocalProgressionObserver.Server;
                if (key == null || server == null) return;          // pure client / not composed — fail closed.
                if (__instance == null || player == null) return;
                if (player != Player.m_localPlayer) return;

                var recipe = Traverse.Create(__instance).Field("m_craftRecipe").GetValue<Recipe>();
                if (recipe == null || recipe.m_item == null) return;

                var shared = recipe.m_item.m_itemData.m_shared;
                if (shared == null) return;

                // Server-observed eligibility: exact non-stackable durable output.
                bool nonStackable = shared.m_maxStackSize <= 1;
                bool durable = shared.m_useDurability;
                if (!WorkmanshipCodec.IsEligible(nonStackable, durable)) return;

                // Resolve the acting occupant's Masterwork activation from the composed server stores (the
                // same host resolution the sibling Field Fletching gate uses). Fail closed on any missing fact.
                if (!ResolveHostMasterworkActive(server, player, out string crafterAccount)) return;

                // Find the freshly produced item instance: the most recent matching, unstamped, eligible one
                // in the crafter's inventory. Vanilla just appended it via Inventory.AddItem.
                var item = FindFreshProducedItem(player, recipe, key);
                if (item == null) return;

                string itemType = StripCloneSuffix(recipe.m_item.gameObject.name);
                var accessor = new ItemDataMetadataAccessor(item);

                // Idempotency / no-overwrite: a valid existing stamp is a no-op (the provider also refuses).
                bool alreadyValid = WorkmanshipCodec.Read(accessor, key).State == WorkmanshipReadState.Valid;

                // Deterministic, server-minted exact-instance provenance id for this issuance.
                var provenanceId = MintProvenanceId(crafterAccount, itemType, player);
                var facts = new ProducedItemFacts(itemType, nonStackable, durable, alreadyValid, provenanceId);

                var decision = Provider.Decide(true, crafterAccount, facts);
                if (!decision.ShouldIssue) return;

                // Stamp onto the real custom data + EXPLICITLY dirty persistence. The stamp lives in the
                // item's m_customData, which vanilla serializes with the inventory on the next profile save;
                // we also invoke the private Inventory.Changed() (decomp :57540) via Traverse to fire the
                // on-changed callback vanilla raises after any inventory mutation, so save/UI signaling runs
                // now rather than only opportunistically.
                WorkmanshipCodec.Stamp(accessor, decision.Stamp, key);
                var inv = player.GetInventory();
                if (inv != null) Traverse.Create(inv).Method("Changed").GetValue();

                Plugin.Log.LogInfo(
                    "[Niflheim/Crafting] Masterwork issued Workmanship on '" + itemType + "' provId=" +
                    provenanceId.Value + " for crafter=" + crafterAccount + ".");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Crafting] Masterwork issuance postfix threw (ignored): " + ex.Message);
            }
        }

        /// <summary>Authoritative HOST path: resolve the acting occupant's Masterwork activation (purchase +
        /// active relationship) straight from the composed server stores, keyed by the Stone Area the crafter
        /// stands in and the transport-bound internal principal (never a client claim). Returns the crafter's
        /// internal account id when active; false on any absent server-owned fact.</summary>
        private static bool ResolveHostMasterworkActive(
            Application.Activation.LocalProgressionServer server, Player player, out string crafterAccount)
        {
            crafterAccount = string.Empty;
            var foundational = FoundationalPlacementObserver.Server;
            var znet = ZNet.instance;
            if (foundational == null || znet == null) return false;

            Vector3 pp = player.transform.position;
            if (!foundational.StoneAreas.TryResolve(pp.x, pp.z, out var stoneId))
                return false;                                       // outside every Stone Area ⇒ not active.

            long actingPlayerId = player.GetPlayerID();
            string peerKey = Application.Runtime.ServerCreatorIdentity.CharacterSubject(actingPlayerId);
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

            if (!Provider.IsMasterworkActive(stone, characterAgg, authority)) return false;

            crafterAccount = occupant.Value;
            return true;
        }

        /// <summary>Find the just-produced item in the crafter's inventory: an instance whose prefab matches
        /// the recipe output, that is eligible, and that does NOT already carry a valid Workmanship stamp.
        /// Preferring an unstamped instance avoids re-selecting an older already-Masterwork copy of the same
        /// item type (issuance is one-per-instance).</summary>
        private static ItemDrop.ItemData? FindFreshProducedItem(Player player, Recipe recipe, WorkmanshipIntegrityKey key)
        {
            var inv = player.GetInventory();
            if (inv == null) return null;
            string wanted = StripCloneSuffix(recipe.m_item.gameObject.name);

            ItemDrop.ItemData? fallback = null;
            var items = inv.GetAllItems();
            for (int i = items.Count - 1; i >= 0; i--)   // newest-first: AddItem appends.
            {
                var it = items[i];
                if (it == null || it.m_dropPrefab == null) continue;
                if (!string.Equals(StripCloneSuffix(it.m_dropPrefab.name), wanted, StringComparison.Ordinal)) continue;
                fallback ??= it;
                var accessor = new ItemDataMetadataAccessor(it);
                if (WorkmanshipCodec.Read(accessor, key).State != WorkmanshipReadState.Valid)
                    return it;                            // first unstamped match — the fresh one.
            }
            return fallback;
        }

        /// <summary>Mint a deterministic-per-craft, server-owned exact-instance provenance id. Bound to the
        /// crafter account, item type, and a high-resolution craft timestamp so two crafts of the same item
        /// get distinct ids while a replay of the same craft stamp is stable within the codec's idempotency.</summary>
        private static ItemProvenanceId MintProvenanceId(string crafterAccount, string itemType, Player player)
        {
            long stamp = DateTime.UtcNow.Ticks;
            return new ItemProvenanceId(itemType + ":" + crafterAccount + ":" + stamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }
    }
}
