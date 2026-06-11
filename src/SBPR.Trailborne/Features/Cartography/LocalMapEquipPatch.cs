// ============================================================================
//  Trailborne v2 cartography — Local Map equip discipline + torch exception (§2A.3)
// ----------------------------------------------------------------------------
//  ItemType=TwoHandedWeapon (LocalMap.cs) already gives the C3 block-clear for free:
//  vanilla Humanoid.EquipItem's TwoHandedWeapon branch (verified against
//  assembly_valheim.dll) hard-UnequipItem()s BOTH hands (never hides — so
//  GetCurrentBlocker(), which reads m_leftItem, finds nothing → no ghost block),
//  then sets m_rightItem = item. That is AT-MAP-BLOCKCLEAR by construction.
//
//  This patch adds the ONE thing the bare TwoHandedWeapon branch can't: the torch
//  exception (C12 / AT-MAP-TORCH). The bare branch evicts a left-hand Torch too, so
//  a lit map at night is impossible without help. We:
//    • PREFIX: when our SBPR_LocalMap is the item being equipped, remember whether
//      the player currently holds a Torch in the left hand (the only left item the
//      lit-map exception permits).
//    • POSTFIX: after vanilla's branch has run (map now in the right hand, left hand
//      cleared), re-seat that Torch into m_leftItem and refresh the visual — mirroring
//      vanilla's own torch-beside-one-handed special case (EquipItem :1057-1064).
//
//  Discipline (C3): a SHIELD or a left-hand WEAPON is still hard-unequipped and is
//  NOT restored — ONLY a Torch comes back. So the block-clear guarantee holds whether
//  or not a torch is up. The patch is scoped to OUR item (LocalMapItemTag on the held
//  prefab) so it never alters vanilla two-handed weapons.
//
//  Reflection: m_leftItem is PROTECTED and SetupVisEquipment is PROTECTED VIRTUAL on
//  Humanoid — both reached via cached HarmonyLib AccessTools (FieldRef + Method), the
//  same idiom PlacementMarkerRadiusPatch / CairnPlacementGatePatch use for protected
//  Player fields. Resolved once at type-load; if a future game build renames either,
//  the ref is null and the exception simply degrades to "no torch back" (logged once),
//  never throwing on every equip.
//
//  Clean-side (ADR-0001): patches the base-game Humanoid only; the torch-restore
//  mirror is adapted from the vanilla EquipItem body (the game we mod — fair to read).
// ============================================================================

using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cartography
{
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem), new[] { typeof(ItemDrop.ItemData), typeof(bool) })]
    public static class LocalMapEquipPatch
    {
        // Protected Humanoid.m_leftItem reached via cached FieldRef (fast delegate, not a
        // per-equip reflective Invoke). Resolved once; null ⇒ torch exception disabled.
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData>? LeftItemRef =
            TryFieldRef("m_leftItem");

        // Protected virtual Humanoid.SetupVisEquipment(VisEquipment, bool) + protected
        // Humanoid.m_visEquipment, used to refresh the left-hand visual after we re-seat
        // the torch. Cached MethodInfo/FieldInfo; null ⇒ we still set the slot but skip the
        // visual refresh (the next equip/CustomFixedUpdate vis pass will catch up).
        private static readonly MethodInfo? SetupVisEquipmentMI =
            AccessTools.Method(typeof(Humanoid), "SetupVisEquipment", new[] { typeof(VisEquipment), typeof(bool) });
        private static readonly FieldInfo? VisEquipmentFI =
            AccessTools.Field(typeof(Humanoid), "m_visEquipment");

        private static bool _warned;

        private static AccessTools.FieldRef<Humanoid, ItemDrop.ItemData>? TryFieldRef(string field)
        {
            try
            {
                if (AccessTools.Field(typeof(Humanoid), field) == null)
                {
                    Plugin.Log.LogWarning(
                        $"[Trailborne/Cartography] Humanoid.{field} not found via reflection; " +
                        "Local Map torch exception disabled (vanilla field renamed?).");
                    return null;
                }
                return AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>(field);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Cartography] EquipItem FieldRef '{field}' failed: {e.Message}");
                return null;
            }
        }

        /// <summary>True if this item instance is our Local Map (its dropPrefab carries the tag).</summary>
        private static bool IsLocalMap(ItemDrop.ItemData item)
        {
            if (item?.m_dropPrefab == null) return false;
            // Component check on the prefab is rename-proof + cheap. Fall back to a name
            // compare if the prefab somehow lost the tag (defensive).
            return item.m_dropPrefab.GetComponent<LocalMapItemTag>() != null
                   || item.m_dropPrefab.name == LocalMap.LocalMapName;
        }

        /// <summary>True if an item is a lit/unlit Torch (the only left item the map permits).</summary>
        private static bool IsTorch(ItemDrop.ItemData? item)
            => item != null && item.m_shared != null
               && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch;

        // PREFIX: before vanilla evicts the left hand, capture a left-hand torch so the
        // postfix can put it back. __state carries it across (Harmony's per-call channel).
        [HarmonyPrefix]
        public static void Prefix(Humanoid __instance, ItemDrop.ItemData item, out ItemDrop.ItemData? __state)
        {
            __state = null;
            if (__instance == null || item == null) return;
            if (LeftItemRef == null) return;
            if (!IsLocalMap(item)) return;

            try
            {
                var left = LeftItemRef(__instance);
                if (IsTorch(left)) __state = left; // remember THE torch to restore
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Cartography] EquipItem prefix torch-capture failed: {e.Message}");
            }
        }

        // POSTFIX: after vanilla's TwoHandedWeapon branch ran (map in right hand, left hand
        // cleared), re-seat the captured torch into the now-empty left hand and refresh the
        // visual — mirroring vanilla's torch-beside-one-handed case (EquipItem :1057-1064).
        [HarmonyPostfix]
        public static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __result, ItemDrop.ItemData? __state)
        {
            // Only act when OUR map actually equipped (vanilla returned true) and we stashed
            // a torch. __result false ⇒ equip was rejected; nothing to restore.
            if (!__result || __state == null) return;
            if (__instance == null || LeftItemRef == null) return;
            if (!IsLocalMap(item)) return;

            try
            {
                var torch = __state;

                // Defensive: only seat the torch if the player still has it (didn't get
                // consumed/dropped in the eviction) and the left hand is genuinely free.
                if (torch == null) return;
                if (__instance.GetInventory() == null || !__instance.GetInventory().ContainsItem(torch)) return;

                ref var left = ref LeftItemRef(__instance)!;
                if (left != null) return; // something already there — don't clobber

                left = torch;
                torch.m_equipped = true;

                // Refresh the left-hand visual so the torch renders + lights (vanilla calls
                // SetupVisEquipment from EquipItem). Best-effort: if reflection is unavailable
                // the slot is still set and the next vis pass picks it up.
                if (SetupVisEquipmentMI != null && VisEquipmentFI != null)
                {
                    var visEq = VisEquipmentFI.GetValue(__instance);
                    if (visEq != null)
                        SetupVisEquipmentMI.Invoke(__instance, new object[] { visEq, false });
                }
            }
            catch (Exception e)
            {
                if (!_warned)
                {
                    _warned = true;
                    Plugin.Log.LogWarning(
                        $"[Trailborne/Cartography] Local Map torch restore failed (logged once): {e.Message}. " +
                        "Map still equips + blocks-clear; only the lit-torch coexistence is affected.");
                }
            }
        }
    }
}
