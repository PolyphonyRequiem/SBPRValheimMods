using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;

namespace SBPR.Niflheim.HomesteadStones.Features.Crafting
{
    /// <summary>
    /// T022 — the net48 adapter that binds the engine-free <see cref="WorkmanshipCodec"/> abstract item
    /// metadata surface (<see cref="IItemMetadataWriter"/> / <see cref="IItemMetadataReader"/>) to the real
    /// vanilla <c>ItemDrop.ItemData.m_customData</c> string dictionary. This is the ONLY place the codec
    /// touches the engine: the codec itself is pure and unit-tested; this thin wrapper lets the SAME
    /// stamp/read/validate code run against a live item at runtime.
    ///
    /// WHAT THIS BRIDGES (decomp seam — vanilla is fair game, AGENTS.md / ADR-0001): <c>ItemData.m_customData</c>
    /// is a <c>Dictionary&lt;string,string&gt;</c> that vanilla serializes with the item across clone /
    /// inventory move / drop→pickup / container transfer / world save (research.md line 137: "Exact
    /// ItemData.m_customData survives clone/inventory/drop/container transfer"). Writing our provenance keys
    /// there means the Workmanship stamp rides every legitimate transfer for free, and a legitimate upgrade
    /// (which raises quality/durability but never touches m_customData) preserves it — exactly the
    /// AT-ITEM-UPGRADE-PRESERVE / AT-ITEM-TRANSFER behaviours the pure codec tests prove.
    ///
    /// ADDITIVE (ADR-0006): we only add/read our own domain-prefixed keys on an EXISTING item's existing
    /// dictionary. No prefab cloning, no component surgery. References only <c>ItemDrop.ItemData</c> →
    /// net48-only, NOT link-compiled into net8.
    /// </summary>
    internal sealed class ItemDataMetadataAccessor : IItemMetadataWriter, IItemMetadataReader
    {
        private readonly Dictionary<string, string> _customData;

        internal ItemDataMetadataAccessor(ItemDrop.ItemData item)
        {
            // Vanilla lazily allocates m_customData; ensure it exists before we write.
            _customData = item.m_customData ?? (item.m_customData = new Dictionary<string, string>());
        }

        public void SetString(string key, string value) => _customData[key] = value;
        public void Remove(string key) => _customData.Remove(key);
        public string GetString(string key, string missing) =>
            _customData.TryGetValue(key, out var v) ? v : missing;
        public bool Contains(string key) => _customData.ContainsKey(key);
    }
}
