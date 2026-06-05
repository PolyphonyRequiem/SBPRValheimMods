using System;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Harmony hook on Sign.Interact: when the player holds Shift and presses E
    /// on a SignTag-flagged sign, add a map pin matching the sign's text + its
    /// painted color INSTEAD of opening the text-edit dialog. Plain E opens the
    /// dialog as vanilla.
    ///
    /// Pin color now reads the sign's per-instance ZDO color (Signs.ZdoColor)
    /// via SignTag.ReadColor() — there is a single Painted Sign prefab, so the
    /// old per-prefab pin-type map is gone. An unpainted sign pins with the
    /// generic Icon0.
    ///
    /// ⚠ NOTE: this patch class is currently NOT registered via Harmony
    /// (no Plugin.Awake PatchAll(typeof(...)) call references it), so the
    /// Shift+E pin behaviour is presently dead code. Wiring it is a deliberate
    /// behaviour change tracked as a separate follow-up — see PR C architecture
    /// plan §6 R1. This refactor preserves the existing (unregistered) state.
    /// </summary>
    [HarmonyPatch(typeof(Sign), nameof(Sign.Interact))]
    public static class SignInteractPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Sign __instance, Humanoid character, bool hold, bool alt, ref bool __result)
        {
            if (hold) return true;
            var tag = __instance.GetComponent<SignTag>();
            if (tag == null) return true; // not ours — vanilla behavior

            // Shift+E = pin to map. Plain E = vanilla text edit.
            if (!UnityEngine.Input.GetKey(KeyCode.LeftShift) && !UnityEngine.Input.GetKey(KeyCode.RightShift))
                return true;

            try
            {
                var minimap = Minimap.instance;
                var player  = Player.m_localPlayer;
                if (minimap == null || player == null)
                {
                    __result = false;
                    return false;
                }
                var text = __instance.GetText();
                if (string.IsNullOrEmpty(text)) text = "Painted Sign";

                Minimap.PinType pinType = Signs.PinTypeForColor(tag.ReadColor());

                minimap.AddPin(__instance.transform.position, pinType, text, save: true, isChecked: false, player.GetPlayerID());
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Pinned: {text}");

                __result = true;
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Trailborne/M1] Pin-on-Shift+E failed: {e}");
                return true;
            }
        }
    }
}
