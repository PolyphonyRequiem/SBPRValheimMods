using System;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Crafting;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Features.Progression;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Crafting
{
    /// <summary>
    /// T023 Built to Last — the net48 runtime seam that ISSUES one frozen maximum-durability property onto a
    /// freshly crafted eligible item while the local occupant DURABLY holds the Built to Last Permanent Effect.
    /// It consumes the shipped, unit-tested <see cref="DurabilityIssuanceProvider"/> (the decision) and
    /// <see cref="DurabilityCodec"/> (the stamp) as its single authorities; this patch is a THIN adapter that
    /// supplies server-observed produced-item facts and writes the resulting stamp onto the item's real custom
    /// data.
    ///
    /// WHAT THIS BRIDGES (decomp seam — vanilla is fair game, AGENTS.md / ADR-0001):
    ///   * <c>InventoryGui.DoCrafting(Player)</c> (decomp :42523) — the craft/upgrade completion. Vanilla
    ///     appends the produced item via <c>Inventory.AddItem(...)</c> (:42576). A postfix locates that
    ///     just-produced item and stamps the durability provenance onto its <c>m_customData</c>, then
    ///     EXPLICITLY dirties persistence via the private <c>Inventory.Changed()</c> (:57540).
    ///
    /// FUTURE OUTPUTS ONLY. This patch is the ONLY write path, and it fires exactly once per production event,
    /// on the item vanilla just produced. It never enumerates or rewrites existing inventory items, so acquiring
    /// Built to Last cannot retroactively improve anything already in the world — the invariant the pure
    /// provider's read side (<see cref="DurabilityIssuanceProvider.ResolveMaxDurability"/>) then preserves by
    /// deriving an item's maximum from ONLY the stamp that instance carries.
    ///
    /// ELIGIBILITY (server-observed, never a client claim): non-stackable is <c>m_shared.m_maxStackSize &lt;= 1</c>;
    /// durable is <c>m_shared.m_useDurability</c>. Both must hold — a stack shares one ItemData so a per-instance
    /// provenance on it is meaningless, and a non-durable item has no maximum durability to improve.
    ///
    /// PERMANENT EFFECT, NOT A RELATIONSHIP-GATED ONE. Unlike the sibling Masterwork seam, entitlement here is
    /// the crafter's DURABLE purchase record alone (<see cref="DurabilityIssuanceProvider.IsBuiltToLastAcquired"/>) —
    /// no Stone aggregate, no authority index, no relationship. The Stone Area lookup below exists ONLY to resolve
    /// the acting occupant's bound internal principal (the same identity space the purchase committed under —
    /// the T022 R6 sender-binding lesson), never as an entitlement gate.
    ///
    /// AUTHORITATIVE HOST ONLY (fail closed): issuance needs the server integrity key + the composed server
    /// stores. A pure remote client has neither and issues nothing. The proven issuance topology is therefore a
    /// listen-host / dedicated-host crafter; authoritative server→client durability replication for a PURE remote
    /// crafter is the documented follow-up, exactly mirroring the accepted T021/T022 host-first-then-delivery
    /// precedent. This is stated plainly rather than implied away.
    ///
    /// References Valheim (InventoryGui, Player, Inventory, ItemDrop) → net48-only, NOT link-compiled into net8.
    /// The pure provider + codec it drives are fully unit-tested. ADR-0006 additive: only our own
    /// domain-prefixed keys on one existing instance's existing dictionary; no prefab cloning.
    /// </summary>
    [HarmonyPatch]
    internal static class BuiltToLastIssuanceObserver
    {
        private static readonly DurabilityIssuanceProvider Provider = new DurabilityIssuanceProvider();

        /// <summary>The durable server integrity key, armed by the runtime bootstrap on the authoritative host.
        /// Null on a pure client / before composition / after teardown — issuance fails closed. The SAME key the
        /// Workmanship seam uses; the two provenances are separated by their canonical domain labels, not by
        /// separate secrets (one key file, one rotation surface).</summary>
        internal static WorkmanshipIntegrityKey? Armed;

        internal static void Arm(WorkmanshipIntegrityKey key) => Armed = key;
        internal static void Disarm() => Armed = null;

        /// <summary>Postfix on craft/upgrade completion. Locate the just-produced item and, on the authoritative
        /// host with Built to Last durably acquired, stamp the frozen maximum-durability property onto an
        /// eligible non-stackable durable output that is not already stamped.</summary>
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

                bool nonStackable = shared.m_maxStackSize <= 1;
                bool durable = shared.m_useDurability;
                if (!DurabilityCodec.IsEligible(nonStackable, durable)) return;

                if (!ResolveHostBuiltToLastAcquired(server, player, out string crafterAccount)) return;

                var item = FindFreshProducedItem(player, recipe, key);
                if (item == null) return;

                string itemType = StripCloneSuffix(recipe.m_item.gameObject.name);
                var accessor = new ItemDataMetadataAccessor(item);

                // Idempotency / no-overwrite: a valid existing stamp is a no-op (the provider also refuses).
                bool alreadyValid = DurabilityCodec.Read(accessor, key).IsValid;

                var provenanceId = MintProvenanceId(crafterAccount, itemType);
                var facts = new DurableItemFacts(itemType, nonStackable, durable, alreadyValid, provenanceId);

                var decision = Provider.Decide(true, crafterAccount, facts);
                if (!decision.ShouldIssue) return;

                DurabilityCodec.Stamp(accessor, decision.Stamp, key);
                var inv = player.GetInventory();
                if (inv != null) Traverse.Create(inv).Method("Changed").GetValue();

                Plugin.Log.LogInfo(
                    "[Niflheim/Crafting] Built to Last issued maximum-durability x" +
                    decision.Stamp.Property.Factor.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                    " on '" + itemType + "' provId=" + provenanceId.Value + " for crafter=" + crafterAccount + ".");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Crafting] Built to Last issuance postfix threw (ignored): " + ex.Message);
            }
        }

        /// <summary>Authoritative HOST path: resolve the acting occupant's BOUND INTERNAL principal — the same
        /// identity space the purchase committed under (<c>BoundSessions</c>, per the T022 R6 sender-binding
        /// fix) — and ask the pure provider whether that character durably holds Built to Last. Note what is
        /// NOT here: no Stone aggregate, no authority index, no relationship check. A Permanent Effect has no
        /// such conjunct, so issuance keeps working after relationship loss and Tree revocation, which is
        /// exactly what contracts.md requires.</summary>
        private static bool ResolveHostBuiltToLastAcquired(
            Application.Activation.LocalProgressionServer server, Player player, out string crafterAccount)
        {
            crafterAccount = string.Empty;
            var foundational = FoundationalPlacementObserver.Server;
            var znet = ZNet.instance;
            if (foundational == null || znet == null) return false;

            long actingPlayerId = player.GetPlayerID();
            string peerKey = Application.Runtime.ServerCreatorIdentity.CharacterSubject(actingPlayerId);
            if (string.IsNullOrEmpty(peerKey) ||
                !foundational.BoundSessions.TryResolve(peerKey, out var principal))
                return false;

            var occupant = principal.Account;
            if (string.IsNullOrEmpty(occupant.Value)) return false;

            var characterAgg = server.Characters.GetCharacter(occupant, principal.Character);
            if (characterAgg == null) return false;

            if (!Provider.IsBuiltToLastAcquired(characterAgg)) return false;

            crafterAccount = occupant.Value;
            return true;
        }

        /// <summary>Find the just-produced item: an instance whose prefab matches the recipe output and that does
        /// NOT already carry a valid durability stamp. Preferring an unstamped instance avoids re-selecting an
        /// older already-improved copy of the same item type (issuance is one-per-instance).</summary>
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
                if (!DurabilityCodec.Read(accessor, key).IsValid)
                    return it;                            // first unstamped match — the fresh one.
            }
            return fallback;
        }

        /// <summary>Mint a server-owned exact-instance provenance id bound to the crafter account, item type, and
        /// a high-resolution craft timestamp, so two crafts of the same item get distinct ids.</summary>
        private static ItemProvenanceId MintProvenanceId(string crafterAccount, string itemType)
        {
            long stamp = DateTime.UtcNow.Ticks;
            return new ItemProvenanceId("btl:" + itemType + ":" + crafterAccount + ":" +
                stamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }
    }
}
