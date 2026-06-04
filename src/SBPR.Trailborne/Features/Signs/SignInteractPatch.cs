using System;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne
{
    /// <summary>
    /// Harmony hook on Sign.Interact: when the player holds Shift and presses E
    /// on a TrailborneSignTag-flagged sign, add a colored map pin matching the
    /// sign's text + color INSTEAD of opening the text-edit dialog. Plain E
    /// opens the dialog as vanilla.
    ///
    /// ⚠ NOTE: this patch class is currently NOT registered via Harmony
    /// (no Plugin.Awake PatchAll(typeof(...)) call references it), so the
    /// Shift+E pin behaviour is presently dead code. Wiring it is a deliberate
    /// behaviour change tracked as a separate follow-up — see PR C architecture
    /// plan §6 R1. This refactor preserves the existing (unregistered) state.
    /// </summary>
    [HarmonyPatch(typeof(Sign), nameof(Sign.Interact))]
    public static class Sign_Interact_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Sign __instance, Humanoid character, bool hold, bool alt, ref bool __result)
        {
            if (hold) return true;
            var tag = __instance.GetComponent<TrailborneSignTag>();
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

                Minimap.PinType pinType = Minimap.PinType.Icon0;
                if (TrailborneSigns.SignPinTypes.TryGetValue(tag.PrefabName, out var t)) pinType = t;

                minimap.AddPin(__instance.transform.position, pinType, text, save: true, isChecked: false, player.GetPlayerID());
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Pinned: {text}");

                __result = true;
                return false;
            }
            catch (Exception e)
            {
                TrailbornePlugin.Log.LogError($"[Trailborne/M1] Pin-on-Shift+E failed: {e}");
                return true;
            }
        }
    }
}
