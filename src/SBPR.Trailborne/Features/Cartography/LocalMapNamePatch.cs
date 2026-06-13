// ============================================================================
//  Trailborne v2 cartography — Local Map item-name display (§2A.6b, issue 10)
// ----------------------------------------------------------------------------
//  Surfaces an imprinted Local Map's per-instance NAME (the sbpr_map_name
//  m_customData key, stamped at the Table by LocalMap.Imprint, §2A.6a) as the
//  item's DISPLAYED TITLE — so a player carrying several bound maps tells them
//  apart in inventory (AT-TABLENAME-3).
//
//  WHY A PATCH, NOT m_shared.m_name (grounded against assembly_valheim decomp,
//  clean-side per ADR-0001):
//    • The inventory hover TITLE is ItemData.m_shared.m_name, full stop
//      (InventoryGrid.CreateItemTooltip :40890 → tooltip.Set(item.m_shared.m_name,
//      item.GetTooltip(), anchor)). There is NO per-instance display-name field.
//    • m_shared is shared BY REFERENCE across every instance of a prefab and is NOT
//      a per-instance, save-surviving field (ItemData.Clone is MemberwiseClone; only
//      m_customData is deep-copied + round-tripped through the player ZPackage). So
//      writing m_shared.m_name would rename EVERY Local Map + the prefab template and
//      not survive a reload anyway. Hard no.
//    • Therefore the only clean way to show a per-instance name is to intercept the
//      name-display seam and substitute our m_customData value FOR OUR ITEM ONLY.
//
//  SCOPE DISCIPLINE: every postfix guards on the presence of the sbpr_map_name key
//  (unique to our imprint, survives load — unlike m_dropPrefab which is [NonSerialized]).
//  For any other item the key is absent → the postfix is a PURE PASS-THROUGH and never
//  touches a vanilla title (AT-TABLENAME-7 no-orphan: a blank/pre-1.6 map has no key →
//  vanilla "Local Map" title).
//
//  Registered in Plugin.Awake via harmony.PatchAll(typeof(...)) so PatchCheck confirms
//  it wove a method at boot (the t_564f695a "unregistered patch ships dead" lesson —
//  AT-TABLENAME-8). Client-only by nature (no inventory UI / hover on the dedicated
//  server); the guard makes it inert there regardless.
//
//  Verified at build (ilspycmd over the real DLLs):
//    • InventoryGrid.CreateItemTooltip(ItemDrop.ItemData, UITooltip) — private (patched
//      by string name; nameof won't compile against a private member). assembly_valheim.
//    • UITooltip (assembly_guiutils) — public string m_topic (the TITLE field the tooltip
//      renders, :152) + public string m_text (body). Set(topic, text, anchor, fixedPos)
//      assigns m_topic = topic. We overwrite m_topic in the postfix, after the original ran.
//    • ItemDrop.GetHoverName() :58935 → returns m_itemData.m_shared.m_name (world-drop +
//      transfer hover). Postfix rewrites the returned string for our item only.
// ============================================================================

using System.Text;
using HarmonyLib;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// §2A.6b PRIMARY seam — the inventory hover title the player actually reads. Postfix
    /// on the private <c>InventoryGrid.CreateItemTooltip(ItemData, UITooltip)</c>: after the
    /// vanilla call sets <c>tooltip.m_topic = item.m_shared.m_name</c>, overwrite m_topic with
    /// the imprinted Table name formatted as <c>Local Map of "&lt;name&gt;"</c> (§2A.6c) when the
    /// item carries <c>sbpr_map_name</c>. Pure pass-through for every other item.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGrid), "CreateItemTooltip")]
    public static class LocalMapTooltipNamePatch
    {
        [HarmonyPostfix]
        private static void Postfix(ItemDrop.ItemData item, UITooltip tooltip)
        {
            if (item == null || tooltip == null) return;
            if (!LocalMap.TryGetName(item, out string name)) return; // not our imprinted map → pass-through
            tooltip.m_topic = LocalMap.FormatDisplayName(name);
        }
    }

    /// <summary>
    /// §2A.6b SECONDARY seam — the world-drop + transfer hover name (nice-to-have, so a
    /// dropped bound map names itself on the ground). Postfix on <c>ItemDrop.GetHoverName()</c>
    /// rewriting the returned title for our item only. Same sbpr_map_name guard → pure
    /// pass-through otherwise.
    /// </summary>
    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.GetHoverName))]
    public static class LocalMapHoverNamePatch
    {
        [HarmonyPostfix]
        private static void Postfix(ItemDrop __instance, ref string __result)
        {
            if (__instance == null) return;
            if (!LocalMap.TryGetName(__instance.m_itemData, out string name)) return; // not our imprinted map
            __result = LocalMap.FormatDisplayName(name);
        }
    }

    // ========================================================================
    //  §2A.7 (issue 7) — strip the weapon combat-stat rows from a Local Map's
    //  tooltip BODY. A map is not a weapon; it must show no block/parry/damage/
    //  knockback/backstab/stamina rows nor the "$item_twohanded" handed line.
    // ------------------------------------------------------------------------
    //  ROOT CAUSE (grounded against assembly_valheim decomp, clean-side ADR-0001):
    //    LocalMap sets m_itemType = TwoHandedWeapon (=14) — load-bearing for the
    //    equip/block-clear/torch discipline (§2A.2/§2A.3), so it MUST stay. But
    //    that type routes the item through the weapon `case` of
    //    ItemDrop.ItemData.GetTooltip (the body builder): AddHandedTip appends
    //    "$item_twohanded" before the switch, and the weapon case emits damage /
    //    $item_staminause / AddBlockTooltip (parrybonus/parryadrenaline/block where
    //    >1f) / $item_knockback / $item_backstab. Zeroing m_blockPower/m_deflectionForce
    //    (LocalMap.cs) only suppresses the two block rows (both gated >1f); the rest
    //    leak from the Hoe donor's un-zeroed weapon fields. Per-field zeroing is
    //    whack-a-mole — the clean fix is to suppress the whole weapon section for our
    //    item, display-side only.
    //
    //  SEAM: a Postfix on the PUBLIC STATIC overload
    //    ItemDrop.ItemData.GetTooltip(ItemData, int, bool, float, int).
    //    The instance GetTooltip(int) delegates to this static overload (decomp
    //    :618-621), and the crafting UI calls the static one directly, so ONE patch
    //    covers every surface (inventory hover + crafting hover + equip/world-drop
    //    hover). This is the BODY (item.GetTooltip()), distinct from the TITLE seam
    //    LocalMapTooltipNamePatch hooks (InventoryGrid.CreateItemTooltip → m_topic).
    //    The overload is disambiguated by an explicit Type[] in [HarmonyPatch].
    //
    //  BEHAVIOR: for OUR item, REBUILD a clean body (description + weight line) and
    //    overwrite ref __result. We rebuild rather than regex-strip because
    //    "$item_twohanded" is appended BEFORE the weight line and is hard to truncate
    //    cleanly. We do NOT transiently mutate m_shared.m_itemType around the call —
    //    m_shared is shared BY REFERENCE across every Local Map instance + the prefab
    //    template (see the name-patch rationale above), so mutating it is unsafe.
    //
    //  GUARD: item?.m_dropPrefab?.GetComponent<LocalMapItemTag>() != null. The TAG
    //    (not the sbpr_map_name key) so BOTH a blank crafted map AND an imprinted one
    //    are cleaned (the name key only exists once imprinted; a blank map would still
    //    leak weapon stats otherwise — AT-MAP-TT-3). m_dropPrefab IS reliably set on
    //    loaded-from-save items: Inventory.Load → AddItem(name,…,customData,…) →
    //    Instantiate(prefab) → ItemDrop.Awake sets m_itemData.m_dropPrefab =
    //    ObjectDB.GetItemPrefab(name) UNCONDITIONALLY (decomp :58698), before any
    //    ZNetView gating. (The "[NonSerialized] → unreliable on loaded items" note in
    //    this file's header is overcautious for THIS guard — the equip/binding/table
    //    patches all rely on the same m_dropPrefab tag check and work in-game.)
    //
    //  Pure pass-through for every other item (vanilla tooltips byte-identical —
    //  AT-MAP-TT-5). Registered in Plugin.Awake so PatchCheck confirms it wove a method
    //  (AT-MAP-TT-6). Client-only by nature: GetTooltip dereferences Player.m_localPlayer
    //  (NPEs server-side → never called there); the null-guard short-circuits regardless.
    // ========================================================================
    /// <summary>
    /// §2A.7 (issue 7) — Postfix on the public static
    /// <c>ItemDrop.ItemData.GetTooltip(ItemData, int, bool, float, int)</c> that rebuilds a
    /// clean, weapon-stat-free tooltip body for the Local Map (description + weight). Guarded
    /// on <see cref="LocalMapItemTag"/> via <c>m_dropPrefab</c> (catches blank AND imprinted
    /// maps); pure pass-through for every other item.
    /// </summary>
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
        new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int) })]
    public static class LocalMapTooltipCombatStripPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ItemDrop.ItemData item, int stackOverride, ref string __result)
        {
            // GUARD: only our Local Map (tag on the drop-prefab — blank AND imprinted).
            if (item?.m_dropPrefab == null) return;
            if (item.m_dropPrefab.GetComponent<LocalMapItemTag>() == null) return;

            // REBUILD a clean body. Mirror vanilla's leading lines (description, then the
            // weight row in the localizable "$item_weight" form) but emit NONE of the
            // weapon/combat rows and NOT the "$item_twohanded" handed line.
            var sb = new StringBuilder();
            sb.Append(item.m_shared.m_description);
            sb.AppendFormat("\n$item_weight: <color=orange>{0}</color>",
                item.GetWeight(stackOverride).ToString("0.0"));

            __result = sb.ToString();
        }
    }
}
