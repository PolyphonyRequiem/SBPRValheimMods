using System;
using System.Collections.Generic;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Crafting
{
    /// <summary>
    /// T023 Built to Last — the net48 seam that carries an issued maximum-durability stamp across the genuine
    /// vanilla UPGRADE path. This is the exact sibling of <see cref="MasterworkUpgradePreservationObserver"/>
    /// and exists for the same reason that one does: decompiled <c>InventoryGui.DoCrafting</c>'s upgrade branch
    /// (<c>m_craftUpgradeItem != null</c>) REMOVES the exact source instance and creates a FRESH prefab-backed
    /// replacement at the source's grid position with an EMPTY custom-data map. Without this, an upgrade would
    /// silently destroy the item's durability provenance and the improvement would vanish.
    ///
    /// It preserves, it does not reissue:
    ///   * PREFIX — when this is an upgrade whose source carries a durability stamp, CAPTURE the complete signed
    ///     map + the source grid position.
    ///   * POSTFIX — locate the fresh replacement at that position and RESTORE the captured map byte-for-byte
    ///     (<see cref="DurabilityCodec.RestoreStamp"/>). No token is recomputed and no provenance id is
    ///     re-minted, so the stamp re-validates identically and the instance keeps the factor it was ISSUED
    ///     with — a retuned current factor cannot leak in through an upgrade. Then dirty the inventory.
    ///
    /// Runs highest-priority so the restore lands BEFORE the issuance postfix observes the replacement — which
    /// then sees an already-valid stamp and correctly no-ops (no duplicate grant, no second provenance).
    ///
    /// Fail closed: a non-upgrade craft, an unstamped source, or an inventory-full/error path where vanilla
    /// created no replacement all capture nothing and restore nothing.
    ///
    /// References Valheim (InventoryGui, Player, Inventory, ItemDrop) → net48-only, NOT link-compiled into net8.
    /// The capture/restore primitives are engine-free and unit-tested. ADR-0006 additive.
    /// </summary>
    [HarmonyPatch]
    internal static class BuiltToLastUpgradePreservationObserver
    {
        internal sealed class CarryForward
        {
            internal Dictionary<string, string> Stamp = new Dictionary<string, string>();
            internal Vector2i GridPos;
        }

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
                if (upgradeItem == null) return;                        // a fresh craft — nothing to carry.

                var accessor = new ItemDataMetadataAccessor(upgradeItem);
                var captured = DurabilityCodec.CaptureStamp(accessor);
                if (!DurabilityCodec.HasStamp(captured)) return;        // unstamped source — nothing to carry.

                __state = new CarryForward { Stamp = captured, GridPos = upgradeItem.m_gridPos };
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Crafting] Built to Last upgrade capture threw (ignored): " + ex.Message);
            }
        }

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
                if (replacement == null) return;                        // vanilla created no replacement.

                // Never overwrite an item that already carries its own well-formed durability stamp.
                var accessor = new ItemDataMetadataAccessor(replacement);
                if (DurabilityCodec.TryReadRaw(accessor, out _, out _) == DurabilityCodec.RawReadState.Present)
                    return;

                DurabilityCodec.RestoreStamp(accessor, __state.Stamp);
                Traverse.Create(inv).Method("Changed").GetValue();

                Plugin.Log.LogInfo(
                    "[Niflheim/Crafting] Built to Last preserved maximum-durability across upgrade provId=" +
                    accessor.GetString(DurabilityCodec.ProvenanceIdKey, "?") + " (byte-for-byte carry-forward).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Crafting] Built to Last upgrade restore threw (ignored): " + ex.Message);
            }
        }
    }
}
