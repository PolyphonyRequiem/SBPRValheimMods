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
    /// This is the reused guts of the retired apply-pigment seam (SignPaintPatch):
    /// inventory-consume + owner-write ZDO, generalised from one color to two and
    /// from "apply an item" to "the panel committed N pigments". The cost rule
    /// (§A2.6, PER-CHANGED-SLOT — LOCKED Daniel 2026-06-21, card t_6df12ca8): ONE
    /// pigment per slot whose color actually CHANGED vs the sign's current stored ZDO
    /// color — text Red→Blue on an otherwise-unchanged sign = 1 Blue; unchanged slots
    /// are free; re-applying the same colors costs nothing. A changed slot is BILLABLE
    /// only when its new color is non-empty, so clearing a slot (color→∅) is a change
    /// that costs 0. A fully-unpainted sign is all ""→color = all changes → full cost,
    /// exactly the prior per-filled behaviour. The two-predicate split (changed-set gate
    /// vs billable delta cost) is the pure <see cref="SignPaintDelta"/>; this backend
    /// supplies the LIVE "old" colors from <see cref="SignTag"/>. Insufficient pigments →
    /// no paint at all (checked before any removal, so no partial paint / negative
    /// inventory — accept-test 9).
    /// </summary>
    public static class SignPaintBackend
    {
        public enum PaintResult
        {
            Success,
            NothingChanged,    // no slot changed vs current ZDO — silent no-op (button is disabled)
            NoColorChosen,     // neither slot filled — ≥1 required (defensive; the changed-set gate
                               // now precedes this, so CommitPaint no longer returns it — retained so
                               // the genuinely-blank message branch stays defined, never fires on no-op)
            InsufficientItems, // player doesn't hold the required pigments
            ZdoNotReady,       // sign ZDO uninitialised — nothing consumed
            NoPlayer,          // no local player to charge
        }

        /// <summary>
        /// Crafting-style PER-FILLED cost for a (text, board, border) choice: a map of COLOR
        /// id → count of that pigment, one per filled slot; the same color in N slots → N.
        /// Empty/"" slots contribute nothing.
        ///
        /// NOTE (card t_6df12ca8): this is the underlying per-FILLED primitive. The live
        /// consume/display path now charges PER-CHANGED-SLOT (§A2.6) via
        /// <see cref="ComputeChangedCost"/> + <see cref="SignPaintDelta"/> — a first paint of
        /// a fully-unset sign reduces to this exact per-filled cost (all ""→color are
        /// changes), so this is kept as the shared filled-slot accounting primitive and the
        /// "first paint" baseline. It has no direct caller after the per-changed rewire;
        /// retained intentionally (architect's design note) as the documented primitive — do
        /// NOT route the consume/gate/display through it (that reintroduces the per-filled
        /// over-charge bug). Prefer <see cref="ComputeChangedCost"/> for anything user-facing.
        /// </summary>
        public static Dictionary<string, int> ComputeCost(string textColor, string boardColor, string borderColor)
        {
            var cost = new Dictionary<string, int>(StringComparer.Ordinal);
            void Add(string c)
            {
                if (string.IsNullOrEmpty(c)) return;
                cost.TryGetValue(c, out int n);
                cost[c] = n + 1;
            }
            Add(textColor);
            Add(boardColor);
            Add(borderColor);
            return cost;
        }

        /// <summary>
        /// PER-CHANGED-SLOT cost (§A2.6, card t_6df12ca8): the prospective delta — a map of
        /// COLOR id → count — for repainting <paramref name="tag"/> to the (text, board,
        /// border) selection, charging ONE pigment per BILLABLE changed slot (changed vs the
        /// tag's CURRENT stored color AND new color non-empty). Unchanged slots and clears
        /// cost 0. The "old" colors are read LIVE from the tag here (not snapshotted at
        /// panel-open) so a second paint in the same session deltas against the just-written
        /// ZDO. Pure decision delegated to <see cref="SignPaintDelta.ComputeChangedCost"/>.
        /// A null tag (headless / no sign) falls back to a first-paint cost (old = all unset).
        /// </summary>
        public static Dictionary<string, int> ComputeChangedCost(SignTag? tag, string textColor, string boardColor, string borderColor)
        {
            string oldText = tag != null ? tag.ReadTextColor() : "";
            string oldBoard = tag != null ? tag.ReadBoardColor() : "";
            string oldBorder = tag != null ? tag.ReadBorderColor() : "";
            return SignPaintDelta.ComputeChangedCost(
                oldText, oldBoard, oldBorder,
                textColor ?? "", boardColor ?? "", borderColor ?? "");
        }

        /// <summary>
        /// CHANGED-SET predicate (§A2.6, card t_6df12ca8): true if ANY slot's new color
        /// differs (ordinal) from the tag's CURRENT stored color — INCLUDING a clear
        /// (color→∅). This is the commit gate / no-op detector the panel enables the Paint
        /// button on — NOT <c>ComputeChangedCost(...).Count != 0</c>, because a pure clear is
        /// a change with an EMPTY delta cost and must stay committable (Daniel-locked free
        /// clear). A no-op (nothing changed) returns false → the button silently disables.
        /// Delegated to <see cref="SignPaintDelta.HasAnyChange"/>; null tag = first paint.
        /// </summary>
        public static bool HasAnyChange(SignTag? tag, string textColor, string boardColor, string borderColor)
        {
            string oldText = tag != null ? tag.ReadTextColor() : "";
            string oldBoard = tag != null ? tag.ReadBoardColor() : "";
            string oldBorder = tag != null ? tag.ReadBorderColor() : "";
            return SignPaintDelta.HasAnyChange(
                oldText, oldBoard, oldBorder,
                textColor ?? "", boardColor ?? "", borderColor ?? "");
        }

        /// <summary>
        /// Count how many of the pigment ITEM matching <paramref name="color"/> the
        /// player holds, summed across stacks. Matches by drop-prefab name (robust
        /// against localized display names). Returns 0 if no inventory / unknown color.
        /// </summary>
        public static int CountPigment(Player player, string color)
        {
            string? pigmentName = Signs.PigmentForColor(color);
            if (player == null || pigmentName == null) return 0;
            var inv = player.GetInventory();
            if (inv == null) return 0;

            int total = 0;
            foreach (var it in inv.GetAllItems())
            {
                if (it == null) continue;
                if (PrefabNameOf(it) == pigmentName) total += it.m_stack;
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
        /// Is this pigment color "discovered" for the local player? (§A2.6 / Issue 4 —
        /// only discovered pigments render as swatches; reserved future-pigment
        /// placeholders no longer draw dead, unclickable boxes.)
        ///
        /// RULE (Daniel-confirmed 2026-06-07): a pigment is discovered if the player has
        /// EVER discovered the material (vanilla <c>Player.IsKnownMaterial</c> — persistent;
        /// set once you pick the pigment up and never cleared, so the swatch does NOT
        /// flicker away when you spend your last unit), OR its recipe is known
        /// (<c>IsRecipeKnown</c>), OR they currently own at least one. The persistent
        /// material-discovery clause is the primary signal; recipe-known and owned are
        /// belt-and-braces so a swatch can never be MISSING for a pigment the player can
        /// clearly use.
        /// </summary>
        public static bool IsPigmentDiscovered(Player player, string color)
        {
            if (player == null || string.IsNullOrEmpty(color)) return false;
            var name = PigmentDisplayName(color);
            // Primary: persistent material discovery — survives spending your last unit.
            if (!string.IsNullOrEmpty(name) && player.IsKnownMaterial(name)) return true;
            // Fallbacks: recipe known, or currently holding at least one.
            if (!string.IsNullOrEmpty(name) && player.IsRecipeKnown(name)) return true; // known recipe
            if (CountPigment(player, color) > 0) return true; // owned right now
            return false;
        }

        /// <summary>
        /// The pigment ITEM's localized display name for <paramref name="color"/>
        /// (e.g. "Red Pigment"), read from its registered ItemDrop. Falls back to
        /// <see cref="Signs.PigmentLabel"/> if the prefab isn't resolvable yet.
        /// </summary>
        public static string PigmentDisplayName(string color)
        {
            string? prefab = Signs.PigmentForColor(color);
            if (prefab != null)
            {
                var odb = ObjectDB.instance;
                var drop = odb != null ? odb.GetItemPrefab(prefab)?.GetComponent<ItemDrop>() : null;
                var n = drop?.m_itemData?.m_shared?.m_name;
                if (!string.IsNullOrEmpty(n)) return n!;
            }
            return Signs.PigmentLabel(color);
        }

        /// <summary>
        /// The pigment ITEM's inventory icon sprite for <paramref name="color"/>, or
        /// null if not resolvable (headless / not yet registered). Used by the panel's
        /// crafting-style cost rows.
        /// </summary>
        public static Sprite? PigmentSprite(string color)
        {
            string? prefab = Signs.PigmentForColor(color);
            if (prefab == null) return null;
            var odb = ObjectDB.instance;
            var drop = odb != null ? odb.GetItemPrefab(prefab)?.GetComponent<ItemDrop>() : null;
            var icons = drop?.m_itemData?.m_shared?.m_icons;
            return (icons != null && icons.Length > 0) ? icons[0] : null;
        }

        /// <summary>
        /// Validate, charge, and paint in one atomic step, on the PER-CHANGED-SLOT basis
        /// (§A2.6, card t_6df12ca8):
        ///   1. ≥1 slot must CHANGE vs the sign's current ZDO color (else NothingChanged —
        ///      a SILENT no-op; the panel keeps the button disabled so this is unreachable
        ///      via the UI, but defended here too). This is the changed-set gate, NOT a
        ///      cost-count check — a pure clear (color→∅) is a change with an EMPTY delta
        ///      and stays committable for free.
        ///   2. local player must exist (else NoPlayer).
        ///   3. player must hold the full DELTA cost (else InsufficientItems — nothing removed).
        ///   4. sign ZDO must be ready (else ZdoNotReady — nothing removed).
        ///   5. write all three tones, THEN remove exactly the delta pigments.
        /// The delta is computed from the sign's CURRENT colors read LIVE BEFORE the write
        /// (step 5 overwrites them). <paramref name="consumed"/> returns the map actually
        /// consumed so the caller renders the confirmation message from it WITHOUT a
        /// post-write re-read (which would delta to 0 — the :397 post-write trap). On every
        /// non-Success path <paramref name="consumed"/> is an empty map (nothing consumed).
        /// The held-check (3) happens before any removal, so an under-stocked player never
        /// loses pigments and the sign is never half-painted (accept-test 9).
        /// </summary>
        public static PaintResult CommitPaint(SignTag tag, Player player, string textColor, string boardColor, string borderColor, out Dictionary<string, int> consumed)
        {
            consumed = new Dictionary<string, int>(StringComparer.Ordinal);

            // Two-predicate gate: the changed-set (incl. clears) gates the commit; the
            // billable delta is the charge. Both read the CURRENT colors LIVE, pre-write.
            if (!HasAnyChange(tag, textColor, boardColor, borderColor))
                return PaintResult.NothingChanged; // silent no-op (button is disabled)

            var cost = ComputeChangedCost(tag, textColor, boardColor, borderColor);
            if (player == null)  return PaintResult.NoPlayer;
            if (!HasPigments(player, cost)) return PaintResult.InsufficientItems;

            // Write the ZDO first; if it isn't ready we bail BEFORE consuming anything.
            if (tag == null || !tag.WriteColors(textColor ?? "", boardColor ?? "", borderColor ?? ""))
                return PaintResult.ZdoNotReady;

            // Consume exactly the delta. We already proved sufficiency above. A pure clear
            // has an empty delta → this loop is a no-op (clears stay free).
            foreach (var kv in cost)
                RemovePigment(player, kv.Key, kv.Value);

            // Hand back what was actually consumed so the message renders the real delta.
            consumed = cost;
            return PaintResult.Success;
        }

        /// <summary>
        /// Remove <paramref name="count"/> of the pigment matching <paramref name="color"/>
        /// from the player, draining across stacks. Assumes sufficiency was checked.
        /// </summary>
        private static void RemovePigment(Player player, string color, int count)
        {
            string? pigmentName = Signs.PigmentForColor(color);
            if (player == null || pigmentName == null || count <= 0) return;
            var inv = player.GetInventory();
            if (inv == null) return;

            int remaining = count;
            // Snapshot the list — RemoveItem mutates the inventory's backing list.
            foreach (var it in new List<ItemDrop.ItemData>(inv.GetAllItems()))
            {
                if (remaining <= 0) break;
                if (it == null || PrefabNameOf(it) != pigmentName) continue;
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
