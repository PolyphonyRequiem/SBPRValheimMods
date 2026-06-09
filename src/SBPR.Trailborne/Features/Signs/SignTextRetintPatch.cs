using HarmonyLib;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Harmony postfix on the vanilla <c>Sign.UpdateText</c> poll: re-pin OUR sign's
    /// persisted letter tint right after the poll touches the TMP widget.
    ///
    /// Why this exists (bug t_f8eff6d0 / fix t_2af1c9c9): <see cref="SignTag.ApplyColorsFromZdo"/>
    /// only fires on spawn (Awake) and on paint (WriteColors). The vanilla
    /// <c>Sign.UpdateText</c> runs on its own ~2 Hz cadence and reconstructs/rewrites
    /// <c>m_textWidget</c> AFTER our paint-time apply, dropping the letter colour — so a
    /// colour-only repaint left the written letters on their previous colour (red→blue:
    /// plank turned blue but the letters stayed red). Re-applying the text tint on the
    /// SAME cadence the poll runs keeps the letters pinned with no perceptible flicker.
    ///
    /// Gated to OUR signs only (those carrying a <see cref="SignTag"/>); vanilla signs
    /// fall through untouched. Cheap: <see cref="SignTag.ReapplyTextTint"/> only re-sets
    /// the TMP widget (TMP's faceColor set early-outs on an unchanged colour) and does NOT
    /// re-walk the board/border renderers. Headless-safe — the underlying tint helpers
    /// no-op when there is no text widget (dedicated server).
    ///
    /// <c>Sign.UpdateText</c> is a PRIVATE method, so it is patched by string name (the
    /// <c>nameof</c> form will not compile against a private member). HarmonyX resolves the
    /// non-public target from the string. Clean-room: reads only the public
    /// <c>Sign.m_textWidget</c> seam via the tint helpers; no decompiled body is read or copied.
    /// </summary>
    [HarmonyPatch(typeof(Sign), "UpdateText")]
    public static class SignTextRetintPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Sign __instance)
        {
            if (__instance == null) return;
            __instance.GetComponent<SignTag>()?.ReapplyTextTint();
        }
    }
}
