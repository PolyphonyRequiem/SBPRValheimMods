using System;
using System.Collections.Generic;
using UnityEngine;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Backend the Painted Sign panel drives (§A2.6). Pure economy + commit logic,
    /// no UI: compute the crafting-style pigment cost for a (text, border) color
    /// choice, check the player holds it, consume it atomically, and write both
    /// tones to the sign's ZDO via <see cref="SignTag"/>.
    ///
    /// This is the reused guts of the retired apply-ink seam (SignPaintPatch):
    /// inventory-consume + owner-write ZDO, generalised from one color to two and
    /// from "apply an item" to "the panel committed N pigments". The cost rule
    /// (§A2.6): ONE pigment per FILLED color slot — text Red + border White = 1 Red
    /// + 1 White; same color in both slots = 2 of that pigment; border optional
    /// (text-only = 1); at least one color required. Insufficient pigments → no
    /// paint at all (checked before any removal, so no partial paint / negative
    /// inventory — accept-test 9).
    /// </summary>
    public static class SignPaintBackend
    {
        public enum PaintResult
        {
            Success,
            NoColorChosen,     // neither slot filled — ≥1 required
            InsufficientItems, // player doesn't hold the required pigments
            ZdoNotReady,       // sign ZDO uninitialised — nothing consumed
            NoPlayer,          // no local player to charge
        }

        /// <summary>
        /// Crafting-style cost for a (text, border) choice: a map of COLOR id → count
        /// of that pigment required. One per filled slot; same color in both slots → 2.
        /// Empty/"" slots contribute nothing. Caller treats an empty map as "no color".
        /// </summary>
        public static Dictionary<string, int> ComputeCost(string textColor, string borderColor)
        {
            var cost = new Dictionary<string, int>(StringComparer.Ordinal);
            void Add(string c)
            {
                if (string.IsNullOrEmpty(c)) return;
                cost.TryGetValue(c, out int n);
                cost[c] = n + 1;
            }
            Add(textColor);
            Add(borderColor);
            return cost;
        }

        /// <summary>
        /// Count how many of the ink ITEM matching <paramref name="color"/> the player
        /// holds, summed across stacks. Matches by drop-prefab name (robust against
        /// localized display names). Returns 0 if no inventory / unknown color.
        /// </summary>
        public static int CountPigment(Player player, string color)
        {
            string? inkName = Signs.InkForColor(color);
            if (player == null || inkName == null) return 0;
            var inv = player.GetInventory();
            if (inv == null) return 0;

            int total = 0;
            foreach (var it in inv.GetAllItems())
            {
                if (it == null) continue;
                if (PrefabNameOf(it) == inkName) total += it.m_stack;
            }
            return total;
        }

        /// <summary>True if the player holds every pigment in <paramref name="cost"/>.</summary>
        public static bool HasPigments(Player player, Dictionary<string, int> cost)
        {
            if (cost == null) return false;
            foreach (var kv in cost)
                if (CountPigment(player, kv.Key) < kv.Value) return false;
            return true;
        }

        /// <summary>
        /// Validate, charge, and paint in one atomic step:
        ///   1. ≥1 color required (else NoColorChosen).
        ///   2. local player must exist (else NoPlayer).
        ///   3. player must hold the full cost (else InsufficientItems — nothing removed).
        ///   4. sign ZDO must be ready (else ZdoNotReady — nothing removed).
        ///   5. write both tones, THEN remove exactly the pigments.
        /// The held-check (3) happens before any removal, so an under-stocked player
        /// never loses pigments and the sign is never half-painted (accept-test 9).
        /// </summary>
        public static PaintResult CommitPaint(SignTag tag, Player player, string textColor, string borderColor)
        {
            var cost = ComputeCost(textColor, borderColor);
            if (cost.Count == 0) return PaintResult.NoColorChosen;
            if (player == null)  return PaintResult.NoPlayer;
            if (!HasPigments(player, cost)) return PaintResult.InsufficientItems;

            // Write the ZDO first; if it isn't ready we bail BEFORE consuming anything.
            if (tag == null || !tag.WriteColors(textColor ?? "", borderColor ?? ""))
                return PaintResult.ZdoNotReady;

            // Consume exactly the cost. We already proved sufficiency above.
            foreach (var kv in cost)
                RemovePigment(player, kv.Key, kv.Value);

            return PaintResult.Success;
        }

        /// <summary>
        /// Remove <paramref name="count"/> of the ink matching <paramref name="color"/>
        /// from the player, draining across stacks. Assumes sufficiency was checked.
        /// </summary>
        private static void RemovePigment(Player player, string color, int count)
        {
            string? inkName = Signs.InkForColor(color);
            if (player == null || inkName == null || count <= 0) return;
            var inv = player.GetInventory();
            if (inv == null) return;

            int remaining = count;
            // Snapshot the list — RemoveItem mutates the inventory's backing list.
            foreach (var it in new List<ItemDrop.ItemData>(inv.GetAllItems()))
            {
                if (remaining <= 0) break;
                if (it == null || PrefabNameOf(it) != inkName) continue;
                int take = Mathf.Min(remaining, it.m_stack);
                inv.RemoveItem(it, take);
                remaining -= take;
            }
        }

        /// <summary>Drop-prefab name of an item stack, or its shared name as a fallback.</summary>
        private static string PrefabNameOf(ItemDrop.ItemData it)
        {
            if (it?.m_dropPrefab != null) return it.m_dropPrefab.name;
            return it?.m_shared?.m_name ?? "";
        }
    }
}
