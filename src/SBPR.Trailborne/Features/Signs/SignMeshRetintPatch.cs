using HarmonyLib;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Harmony postfix on vanilla <c>WearNTear.Highlight</c>: re-pin OUR sign's persisted
    /// board/border mesh tint after the hammer support-overlay clears.
    ///
    /// Why this exists (bug t_f3310406, AT-SIGN-HIGHLIGHT-REASSERT — the mesh-layer twin of
    /// <see cref="SignTextRetintPatch"/>): the board/border tint now rides a per-renderer
    /// <c>MaterialPropertyBlock</c> (MPB) <c>_Color</c> override — the same render-time layer
    /// vanilla paints pieces through, the layer the old <c>sharedMaterials</c> write was masked
    /// behind. In steady state nothing disturbs that MPB (a non-Ashlands sign is never even
    /// registered with <c>MaterialMan</c> — <c>WearNTear.Awake</c>'s <c>SetAshlandsMaterialValue(0f)</c>
    /// is a guarded no-op since <c>m_ashMaterialValue</c> already defaults to 0). The ONE thing
    /// that clobbers it is hammer-hover: <c>WearNTear.Highlight()</c> pushes the red→green
    /// support <c>_Color</c> through <c>MaterialMan</c> onto every child renderer, and ~0.2s after
    /// the last hover <c>ResetHighlight()</c> calls <c>MaterialMan.ResetValue(_Color)</c>, whose
    /// next <c>UpdateBlock</c> re-pushes a block <b>without</b> <c>_Color</c> — overwriting our
    /// paint and NOT restoring it. So a hovered painted sign would be left on plain wood until a
    /// relog.
    ///
    /// The fix re-asserts our ZDO mesh tint shortly after the overlay clears. We can't postfix the
    /// PRIVATE <c>ResetHighlight</c> by <c>nameof</c>, and even a string-name patch there would run
    /// BEFORE <c>MaterialMan</c>'s next <c>Update</c> wipe (so the re-write would itself be wiped).
    /// Instead we postfix the PUBLIC <c>Highlight</c> (fired every tick the player hovers) and let
    /// <see cref="SignTag.ScheduleMeshReassert"/> debounce a single re-assert ~0.3s out — past both
    /// the 0.2s <c>ResetHighlight</c> and <c>MaterialMan</c>'s subsequent block wipe. Re-hovering
    /// just re-arms the timer, so it fires once after the player stops.
    ///
    /// Gated to OUR signs only (those carrying a <see cref="SignTag"/>); vanilla pieces fall
    /// through untouched. Headless-safe — the underlying tint helpers no-op when there are no
    /// renderers (dedicated server). Clean-room: reads only the public <c>WearNTear.Highlight</c>
    /// seam; no decompiled body is read or copied.
    /// </summary>
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Highlight))]
    public static class SignMeshRetintPatch
    {
        [HarmonyPostfix]
        private static void Postfix(WearNTear __instance)
        {
            if (__instance == null) return;
            __instance.GetComponent<SignTag>()?.ScheduleMeshReassert();
        }
    }
}
