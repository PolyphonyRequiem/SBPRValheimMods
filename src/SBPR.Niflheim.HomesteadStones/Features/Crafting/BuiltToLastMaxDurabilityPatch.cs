using System;
using System.Collections.Generic;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Crafting;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;

namespace SBPR.Niflheim.HomesteadStones.Features.Crafting
{
    /// <summary>
    /// T023 Built to Last — the net48 seam that makes an issued maximum-durability property actually APPLY
    /// in-world, and the one place the no-retroactive-mutation invariant is enforced at read time.
    ///
    /// WHAT THIS BRIDGES (decomp seam — vanilla is fair game, AGENTS.md / ADR-0001):
    ///   * <c>ItemDrop.ItemData.GetMaxDurability(int quality)</c> (decomp :58135) —
    ///     <c>m_shared.m_maxDurability + Mathf.Max(0, quality - 1) * m_shared.m_durabilityPerLevel</c>. This is
    ///     the SINGLE vanilla authority for an instance's maximum durability: the parameterless
    ///     <c>GetMaxDurability()</c> (:58130) delegates to it, and <c>GetDurabilityPercentage()</c> (:58120)
    ///     divides by it. Patching this one overload therefore covers the durability bar, the wear maths, and
    ///     every consumer, with no second copy of the policy.
    ///
    /// A postfix scales the vanilla result by the factor FROZEN INTO THE STAMP THIS EXACT INSTANCE CARRIES,
    /// delegating to the pure, unit-tested <see cref="DurabilityIssuanceProvider.ResolveMaxDurability"/>. That
    /// is the whole invariant:
    ///   * an item with no stamp is untouched — vanilla maximum, forever, so acquiring the effect never reaches
    ///     back and improves anything already in the world;
    ///   * a tampered / unknown-schema / foreign-key stamp degrades to vanilla — never a trusted forgery;
    ///   * a retune of the configured factor changes only what FUTURE issuances freeze, never an existing item;
    ///   * quality/upgrade scaling stays vanilla's, because we scale vanilla's answer rather than replacing it.
    ///
    /// SHARED PREFAB IS NEVER MUTATED (the T030 Ready Hands discipline): we return a scaled VALUE from the
    /// getter. <c>m_shared.m_maxDurability</c> / <c>m_durabilityPerLevel</c> are untouched, so other items
    /// sharing the prefab — and this item if its stamp is ever removed — read the unchanged vanilla numbers.
    ///
    /// HOT-PATH COST. <c>GetMaxDurability</c> is called from UI and wear paths, so a naive HMAC per call would
    /// be unacceptable. Two guards: (1) an unstamped item is rejected by a single dictionary <c>ContainsKey</c>
    /// before any crypto — that is the overwhelming majority of items; (2) a stamped item's verdict is memoized
    /// against the COMPLETE signed-stamp fingerprint (<see cref="DurabilityCodec.Fingerprint"/>), so any change
    /// to any signed byte misses the memo and is re-validated — the same fingerprint-keyed discipline the T022
    /// remediation used to close the stale-verdict hole. The memo is bounded and cleared on teardown.
    ///
    /// Fail closed: with no armed server key (a pure client / before composition / after teardown) the postfix
    /// returns vanilla unchanged.
    ///
    /// References Valheim (ItemDrop) → net48-only, NOT link-compiled into net8. ADR-0006 additive: reads only
    /// our own domain-prefixed keys on an existing instance's existing dictionary.
    /// </summary>
    [HarmonyPatch]
    internal static class BuiltToLastMaxDurabilityPatch
    {
        /// <summary>Bound on memoized fingerprint verdicts (spam guard). An evicted entry simply re-validates.</summary>
        private const int MemoCapacity = 512;

        private static readonly Dictionary<string, double> Memo = new Dictionary<string, double>(StringComparer.Ordinal);
        private static readonly List<string> MemoOrder = new List<string>();

        /// <summary>Drop every memoized verdict — on ZNet teardown / disarm, so a subsequent world under a
        /// different key cannot reuse a stale factor.</summary>
        internal static void ClearMemo()
        {
            Memo.Clear();
            MemoOrder.Clear();
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), "GetMaxDurability", new[] { typeof(int) })]
        [HarmonyPostfix]
        private static void GetMaxDurability_Postfix(ItemDrop.ItemData __instance, ref float __result)
        {
            try
            {
                var key = BuiltToLastIssuanceObserver.Armed;
                if (key == null) return;                                   // no server key — vanilla.
                if (__instance == null) return;

                var custom = __instance.m_customData;
                // Cheap rejection BEFORE any crypto: an unstamped item is vanilla and by far the common case.
                if (custom == null || !custom.ContainsKey(DurabilityCodec.ProvenanceIdKey)) return;

                var accessor = new ItemDataMetadataAccessor(__instance);
                string fingerprint = DurabilityCodec.Fingerprint(accessor);

                if (!Memo.TryGetValue(fingerprint, out double factor))
                {
                    var read = DurabilityCodec.Read(accessor, key);
                    factor = read.IsValid ? read.Stamp.Property.Factor : DurabilityProperty.NeutralFactor;
                    Remember(fingerprint, factor);
                }

                __result = (float)DurabilityIssuanceProvider.ApplyFactor(__result, factor);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Crafting] Built to Last max-durability postfix threw (ignored): " + ex.Message);
            }
        }

        private static void Remember(string fingerprint, double factor)
        {
            if (!Memo.ContainsKey(fingerprint))
            {
                MemoOrder.Add(fingerprint);
                if (MemoOrder.Count > MemoCapacity)
                {
                    string evict = MemoOrder[0];
                    MemoOrder.RemoveAt(0);
                    Memo.Remove(evict);
                }
            }
            Memo[fingerprint] = factor;
        }
    }
}
