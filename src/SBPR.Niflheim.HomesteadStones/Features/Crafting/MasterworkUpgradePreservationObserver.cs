using System;
using System.Collections.Generic;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Crafting
{
    /// <summary>
    /// T022 remediation — the net48 seam that makes <c>AT-ITEM-UPGRADE-PRESERVE</c> REAL on the genuine vanilla
    /// upgrade path. The pure codec always claimed a stamp "survives upgrade" because a legitimate upgrade does
    /// not touch <c>m_customData</c> — but that is FALSE for the actual vanilla mechanism. Decompiled
    /// <c>InventoryGui.DoCrafting</c> (upgrade branch): when <c>m_craftUpgradeItem != null</c> vanilla
    /// UN-equips and <c>Inventory.RemoveItem</c>s the exact source instance, then
    /// <c>Inventory.AddItem(prefabName, count, quality, variant, playerID, playerName, gridPos)</c> creates a
    /// FRESH prefab-backed replacement at the source's grid position — with an EMPTY custom-data map. The
    /// source's server-signed Workmanship stamp is destroyed; the replacement carries nothing. A joined-client
    /// observer re-stamping the fresh item would be REISSUANCE under a NEW provenance identity, not preservation.
    ///
    /// This seam preserves the stamp for real, byte-for-byte, without re-minting or re-signing:
    ///   * PREFIX (before vanilla runs): when this is an upgrade (<c>m_craftUpgradeItem != null</c>) whose source
    ///     carries a Workmanship stamp, CAPTURE the complete signed custom-data map + the source grid position
    ///     into <c>__state</c> — read straight off the exact instance vanilla is about to remove.
    ///   * POSTFIX (after vanilla runs): locate the fresh replacement at that same grid position and RESTORE the
    ///     captured map verbatim (<see cref="WorkmanshipCodec.RestoreStamp"/>). Quality/durability rose (vanilla
    ///     did that); <c>prov_id</c>, token, the signed property tuple, and every Workmanship field are the SAME
    ///     bytes, so the stamp re-validates identically and the provenance identity is unchanged — no reissue.
    ///     Then dirty the inventory so the restored stamp saves/replicates.
    ///
    /// Runs highest-priority so the restore lands BEFORE the sibling issuance/delivery postfixes observe the
    /// replacement — they then see an already-valid stamp and correctly no-op (no duplicate grant / reissue).
    ///
    /// Fail closed / no leakage: a NON-upgrade craft, an upgrade of a vanilla/unstamped source, or an
    /// inventory-full/error path where vanilla created no replacement all capture nothing and restore nothing —
    /// the item stays exactly as vanilla left it. An ineligible (stackable/non-durable) source never carried a
    /// stamp to begin with, so there is nothing to carry.
    ///
    /// References Valheim (InventoryGui, Player, Inventory, ItemDrop) → net48-only, NOT link-compiled into net8.
    /// The capture/restore primitives it drives are engine-free and fully unit-tested. ADR-0006 additive: reads
    /// and writes only our own domain-prefixed keys on an existing instance's existing dictionary; no prefab
    /// cloning. Clean-side (ADR-0001): base-game types only; vanilla decomp is fair game.
    /// </summary>
    [HarmonyPatch]
    internal static class MasterworkUpgradePreservationObserver
    {
        /// <summary>Carried from prefix to postfix: the exact signed stamp map lifted off the upgrade source and
        /// the grid position vanilla will place the replacement at. Null when this craft is not a stamped
        /// upgrade (nothing to preserve).</summary>
        internal sealed class CarryForward
        {
            internal Dictionary<string, string> Stamp = new Dictionary<string, string>();
            internal Vector2i GridPos;
        }

        /// <summary>PREFIX on the crafting client's own <c>InventoryGui.DoCrafting</c>. Capture the upgrade
        /// source's complete signed stamp map + grid position BEFORE vanilla unequips/removes it. Only fires for
        /// an upgrade whose source actually carries a Workmanship stamp — otherwise leaves <paramref name="__state"/>
        /// null and does nothing.</summary>
        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void DoCrafting_Prefix(InventoryGui __instance, Player player, out CarryForward? __state)
        {
            __state = null;
            try
            {
                if (__instance == null || player == null) return;

                var upgradeItem = Traverse.Create(__instance).Field("m_craftUpgradeItem").GetValue<ItemDrop.ItemData>();
                if (upgradeItem == null) return;                       // a fresh craft, not an upgrade — nothing to carry.

                var accessor = new ItemDataMetadataAccessor(upgradeItem);
                var captured = WorkmanshipCodec.CaptureStamp(accessor);
                if (!WorkmanshipCodec.HasStamp(captured)) return;      // vanilla/unstamped source — nothing to carry.

                __state = new CarryForward { Stamp = captured, GridPos = upgradeItem.m_gridPos };
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Crafting] Workmanship upgrade capture threw (ignored): " + ex.Message);
            }
        }

        /// <summary>POSTFIX on <c>InventoryGui.DoCrafting</c>. When the prefix captured a stamped upgrade source,
        /// find the fresh replacement vanilla placed at the source grid position and restore the captured signed
        /// stamp map onto it byte-for-byte, then dirty the inventory. No-op when nothing was captured, or when no
        /// replacement exists at that position (inventory-full/error path) — the item is left as vanilla made
        /// it, so no stale/duplicate metadata leaks.</summary>
        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void DoCrafting_Postfix(InventoryGui __instance, Player player, CarryForward? __state)
        {
            if (__state == null) return;
            try
            {
                if (player == null) return;
                var inv = player.GetInventory();
                if (inv == null) return;

                var replacement = inv.GetItemAt(__state.GridPos.x, __state.GridPos.y);
                if (replacement == null) return;                       // vanilla created no replacement (full/error).

                // The replacement must be a genuinely fresh instance — never overwrite an item that already
                // carries its own well-formed stamp (defensive: an unrelated stamped item shares the slot).
                var accessor = new ItemDataMetadataAccessor(replacement);
                if (WorkmanshipCodec.TryReadRaw(accessor, out _, out _) == WorkmanshipCodec.RawReadState.Present)
                    return;

                WorkmanshipCodec.RestoreStamp(accessor, __state.Stamp);
                Traverse.Create(inv).Method("Changed").GetValue();

                Plugin.Log.LogInfo(
                    "[Niflheim/Crafting] Masterwork preserved Workmanship across upgrade provId=" +
                    accessor.GetString(WorkmanshipCodec.ProvenanceIdKey, "?") + " (byte-for-byte carry-forward).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Crafting] Workmanship upgrade restore threw (ignored): " + ex.Message);
            }
        }
    }
}
