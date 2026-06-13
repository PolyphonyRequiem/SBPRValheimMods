// ============================================================================
//  Trailborne v2 cartography — Surveyor's Table imprint trigger (§2I.3, issue 6)
// ----------------------------------------------------------------------------
//  PART B of issue 6: replace the old "Use the Table → imprint ALL carried Local
//  Maps" trigger with an explicit, disambiguated gesture:
//
//    While LOOKING AT a named Surveyor's Table, press the hotbar number key (1–8)
//    of the Local Map slot you want to imprint. That ONE map — and only that one —
//    is imprinted with the Table's current survey.
//
//  The seam (decomp-verified — clean-side, ADR-0001): vanilla `Player.Update` reads
//  `ZInput.GetButtonDown("Hotbar1".."Hotbar8")` and calls `Player.UseHotbarItem(n)`,
//  whose body is `m_inventory.GetItemAt(index - 1, 0)` → `UseItem(...)` (decomp
//  assembly_valheim Player.UseHotbarItem :17781). The whole hotbar block runs only
//  while `TakeInput()` is true (false during any vanilla modal/menu/chat), so a
//  Harmony PREFIX on `Player.UseHotbarItem(int)` is the exact, collision-free capture
//  point — one method covers all 8 keys + the gamepad radial's hotbar use, already
//  inside vanilla's input gate, and returning false lets us CONSUME the press so the
//  map isn't also "used"/equipped by vanilla the same frame.
//
//  Gate (the imprint behaviour fires ONLY in this exact context — AT-IMPRINT-HOTBAR-6):
//    • local player only (Player.m_localPlayer) — never a remote/NPC; this also makes
//      the patch inert on the dedicated server (no local Player there → pass-through);
//    • the player must be HOVERING a SurveyorTableTag — GetHoverObject() →
//      GetComponentInParent<SurveyorTableTag>(). Standing near it is not enough, the
//      same "look at the piece" gate vanilla Use uses. Not looking at a Table → return
//      true → vanilla hotbar use is completely unchanged.
//
//  When hovering the Table we resolve the SAME slot vanilla would (GetItemAt(index-1,0),
//  row 0 = hotbar) and hand it to SurveyorTableTag.TryImprintSlot, which owns all the
//  refusal/feedback/name-gate logic (§2I.4). TryImprintSlot returns true whenever it
//  HANDLED the gesture (success OR a refusal-with-Center-message) — i.e. whenever the
//  player was clearly trying to imprint at this Table — so we return !handled to consume
//  the press and stop vanilla from equipping/using the slot item (AT-IMPRINT-HOTBAR-2:
//  a wrong/empty slot is refused AND not consumed/used/equipped).
//
//  Clean-side (ADR-0001): patches the base-game Player only; ZInput / UseHotbarItem /
//  GetHoverObject / Inventory.GetItemAt are all vanilla. No third-party mod code.
// ============================================================================

using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cartography
{
    // Type[] overload-disambiguator supplied per the repo's overload-safety discipline
    // (the SE_Rested / LocalMapEquipPatch lesson): UseHotbarItem has a single (int)
    // overload today, but pinning the signature means a future vanilla overload can't
    // silently re-bind this patch to the wrong target.
    [HarmonyPatch(typeof(Player), nameof(Player.UseHotbarItem), new[] { typeof(int) })]
    public static class SurveyorTableHotbarImprintPatch
    {
        /// <summary>
        /// Prefix on <c>Player.UseHotbarItem(int)</c>. Returns <c>false</c> (skip vanilla
        /// use/equip) when the local player is hovering a Surveyor's Table and the gesture
        /// was handled as an imprint attempt (success or a refusal-with-feedback); returns
        /// <c>true</c> (run vanilla) in every other case so off-Table hotbar use is untouched.
        /// </summary>
        [HarmonyPrefix]
        public static bool Prefix(Player __instance, int index)
        {
            // Local player only. On a dedicated server Player.m_localPlayer is null, so this
            // is pure pass-through there (AT-IMPRINT-HOTBAR-6 / server-safe).
            if (__instance == null || __instance != Player.m_localPlayer)
                return true;

            // Must be LOOKING AT a Surveyor's Table — not merely near it. GetComponentInParent
            // catches a hover on any child collider of the Table piece.
            var table = HoveredSurveyorTable(__instance);
            if (table == null)
                return true; // not looking at a Table → vanilla hotbar use, completely unchanged

            // Resolve the SAME slot vanilla's UseHotbarItem would: row 0 is the hotbar, so
            // "press n" maps to GetItemAt(n-1, 0). GetItemAt returns null for an empty slot
            // (no exception), which TryImprintSlot refuses with feedback.
            var inv = __instance.GetInventory();
            ItemDrop.ItemData? item = inv != null ? inv.GetItemAt(index - 1, 0) : null;

            // TryImprintSlot owns the §2I.4 refusal/feedback/name-gate logic and returns true
            // when it HANDLED the gesture (imprinted, or refused with a Center message). We
            // consume the press in that case so the map isn't also equipped/used by vanilla.
            bool handled = table.TryImprintSlot(item);
            return !handled;
        }

        /// <summary>
        /// The Surveyor's Table the local player is currently hovering, or null. Reuses the
        /// §2G hover idiom: <c>GetHoverObject()</c> (returns <c>m_hovering</c>) →
        /// <c>GetComponentInParent&lt;SurveyorTableTag&gt;()</c> so a hover on any child collider
        /// of the Table piece resolves to the tag.
        /// </summary>
        private static SurveyorTableTag? HoveredSurveyorTable(Player player)
        {
            GameObject? hover = player.GetHoverObject();
            if (hover == null) return null;
            return hover.GetComponentInParent<SurveyorTableTag>();
        }
    }
}
