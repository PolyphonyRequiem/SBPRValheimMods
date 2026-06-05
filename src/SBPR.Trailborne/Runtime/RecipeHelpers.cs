namespace SBPR.Trailborne.Runtime
{
    /// <summary>
    /// Shared recipe/station lookup helpers. Deduped out of the per-feature
    /// registration classes (Pigments / Cairns / Trailblazing each shipped an
    /// identical private copy). These are pure ObjectDB / ZNetScene reads used by
    /// ≥2 features, so they graduate to Runtime/ as infrastructure (plan §4).
    ///
    /// Behaviour is byte-for-byte the same as the former private copies; only the
    /// per-feature log tag is unified to "[Trailborne]" (a log string, not a
    /// save/wire contract — plan R3 only protects prefab/ZDO/config literals).
    /// </summary>
    public static class RecipeHelpers
    {
        /// <summary>
        /// Resolve a CraftingStation by its piece-prefab name from the live
        /// ZNetScene. Returns null (with a warning) if the prefab is missing or
        /// carries no CraftingStation — callers then register the recipe against a
        /// null station (no bench requirement), preserving prior behaviour.
        /// </summary>
        public static CraftingStation? FindStation(string piecePrefabName)
        {
            var zns = ZNetScene.instance;
            var p = zns?.GetPrefab(piecePrefabName);
            var station = p?.GetComponent<CraftingStation>();
            if (station == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne] FindStation: '{piecePrefabName}' missing or has no CraftingStation. " +
                    "Recipe will register against null station (no bench requirement).");
            }
            return station;
        }

        /// <summary>
        /// True if ObjectDB already holds a recipe whose output item's GameObject
        /// is named <paramref name="itemPrefabName"/>. Used to make recipe
        /// registration idempotent across repeated ODB events.
        /// </summary>
        public static bool HasRecipe(string itemPrefabName)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return false;
            foreach (var r in odb.m_recipes)
                if (r != null && r.m_item != null && r.m_item.gameObject != null &&
                    r.m_item.gameObject.name == itemPrefabName)
                    return true;
            return false;
        }
    }
}
