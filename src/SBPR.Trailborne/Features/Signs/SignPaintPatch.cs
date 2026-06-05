using System;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Paint mechanism for the single Painted Sign (v0.1.0 model, Daniel
    /// 2026-06-04): the sign is BUILT unpainted; the player PAINTS it afterward
    /// by applying a pigment/ink item to the placed sign. Re-applying a
    /// different ink repaints it.
    ///
    /// Seam: vanilla `Sign.UseItem(Humanoid, ItemData)` — the public
    /// `Interactable.UseItem` contract for "apply an item to this placed object"
    /// (same surface ItemStand uses). Verified against assembly_valheim.dll
    /// public metadata (Sign : Hoverable, Interactable, TextReceiver; UseItem
    /// signature `bool UseItem(Humanoid user, ItemDrop.ItemData item)`). No
    /// decompiled IronGate source consulted — clean-room.
    ///
    /// A prefix intercepts the call: when the used item is one of our four inks
    /// AND the target carries a SignTag, we consume one ink, write the chosen
    /// color to the sign's ZDO (owner-write, persists + syncs), re-tint the
    /// mesh, and report success — skipping vanilla's body (vanilla signs don't
    /// accept items, so its UseItem returns false / "not used"). Any other item
    /// falls through to vanilla unchanged.
    /// </summary>
    [HarmonyPatch(typeof(Sign), nameof(Sign.UseItem))]
    public static class SignPaintPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Sign __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
        {
            if (item == null || user == null) return true; // nothing to apply — vanilla

            var tag = __instance.GetComponent<SignTag>();
            if (tag == null) return true; // not our Painted Sign — vanilla behavior

            // Resolve the item's prefab name → ink color, or null if not an ink.
            string? prefabName = item.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            string? color = prefabName != null ? Signs.ColorForInk(prefabName) : null;
            if (color == null) return true; // not one of our inks — let vanilla decide

            try
            {
                // Only the local player drives the paint + consumes their ink.
                if (user != Player.m_localPlayer)
                {
                    __result = false;
                    return false;
                }

                if (string.Equals(tag.ReadColor(), color, StringComparison.Ordinal))
                {
                    MessageHud.instance?.ShowMessage(
                        MessageHud.MessageType.Center,
                        $"Sign is already painted {color}.");
                    __result = false; // no ink consumed
                    return false;
                }

                if (!tag.WriteColor(color))
                {
                    // ZDO not ready (e.g. ghost/uninitialised) — don't eat the ink.
                    __result = false;
                    return false;
                }

                // Consume exactly one ink from the applied stack.
                var inv = user.GetInventory();
                inv?.RemoveItem(item, 1);

                MessageHud.instance?.ShowMessage(
                    MessageHud.MessageType.Center,
                    $"Painted sign {color}.");

                __result = true; // item was used
                return false;    // skip vanilla UseItem body
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Trailborne/M1] Sign paint failed: {e}");
                return true; // fall back to vanilla on unexpected error
            }
        }
    }
}
