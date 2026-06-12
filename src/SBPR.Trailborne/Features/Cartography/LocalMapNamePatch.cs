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

using HarmonyLib;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// §2A.6b PRIMARY seam — the inventory hover title the player actually reads. Postfix
    /// on the private <c>InventoryGrid.CreateItemTooltip(ItemData, UITooltip)</c>: after the
    /// vanilla call sets <c>tooltip.m_topic = item.m_shared.m_name</c>, overwrite m_topic with
    /// the imprinted Table name (prefixed "Map: ") when the item carries <c>sbpr_map_name</c>.
    /// Pure pass-through for every other item.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGrid), "CreateItemTooltip")]
    public static class LocalMapTooltipNamePatch
    {
        [HarmonyPostfix]
        private static void Postfix(ItemDrop.ItemData item, UITooltip tooltip)
        {
            if (item == null || tooltip == null) return;
            if (!LocalMap.TryGetName(item, out string name)) return; // not our imprinted map → pass-through
            tooltip.m_topic = LocalMap.NameDisplayPrefix + name;
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
            __result = LocalMap.NameDisplayPrefix + name;
        }
    }
}
