using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Runtime
{
    /// <summary>
    /// CLIENT-FACING REFRESH LAYER (task t_0387ebb3 / fix-client-registration).
    ///
    /// The Registrar lands our items/recipes/pieces into the authoritative
    /// ObjectDB + ZNetScene collections, and SpecCheck reads those same raw
    /// collections back green. That is necessary but NOT sufficient for a
    /// player on a CLIENT to actually craft/build the content: a connected
    /// client has player-facing surfaces the dedicated server never builds —
    ///   1. the local Player's KNOWN-RECIPE set (drives the crafting panel and
    ///      hammer build menu), computed at spawn, and
    ///   2. each PieceTable's runtime arrays (m_availablePieces /
    ///      m_selectedPiece / m_lastSelectedPiece), built lazily by the vanilla
    ///      PieceTable.UpdateAvailable method.
    /// Neither is re-synced after our registration, so on a joined client the
    /// content sits in the DB unreachable from the UI ("logs green ≠ playable").
    ///
    /// This file adds the two missing refreshes, OUR way (vanilla public API
    /// only; no third-party mod-loader code is referenced or copied):
    ///   • Fix A — a Player.OnSpawned postfix that calls the player's private
    ///     UpdateKnownRecipesList() so the just-spawned player re-scans
    ///     ObjectDB.m_recipes and learns our recipes.
    ///   • Fix B — a PieceTable.UpdateAvailable prefix/postfix that sizes the
    ///     jagged m_availablePieces bucket list to the category count and the
    ///     two selection arrays to match, so a freshly-constructed table (the
    ///     spade-only table in Trailblazing) — and defensively the vanilla
    ///     hammer table — render instead of coming up empty or throwing.
    ///
    /// Both hooks are dedicated-server-safe: the server has no local Player and
    /// never opens a build menu, so Fix A's guard early-outs and Fix B simply
    /// never fires there. No string literal, prefab/ZDO name, recipe number, or
    /// comfort value is touched — these are pure UI-surface repairs and the
    /// SpecCheck watchdog is unaffected.
    /// </summary>
    [HarmonyPatch]
    public static class ClientRefreshPatches
    {
        // ─────────────────────────────────────────────────────────────
        // Fix A — refresh the spawned player's known-recipe list.
        // Player.UpdateKnownRecipesList() is a private instance method, so we
        // resolve it once via AccessTools and invoke it reflectively. (Vanilla
        // recomputes this set at spawn; recipes injected into ObjectDB after
        // that point are invisible until it is re-run.)
        // ─────────────────────────────────────────────────────────────
        private static readonly MethodInfo UpdateKnownRecipesList =
            AccessTools.Method(typeof(Player), "UpdateKnownRecipesList");

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Player_OnSpawned_Postfix(Player __instance)
        {
            // Only meaningful once our content is actually wired into the DBs.
            // On a dedicated server there is no local Player, so this hook is a
            // client-only path by construction; the guard just avoids a wasted
            // rescan during the brief window before in-world wiring completes.
            if (__instance == null || !Registrar.ContentWired) return;

            try
            {
                if (UpdateKnownRecipesList != null)
                {
                    UpdateKnownRecipesList.Invoke(__instance, null);
                    Plugin.Log.LogInfo(
                        "[Trailborne] Player.OnSpawned → UpdateKnownRecipesList(): refreshed known recipes so SBPR content is craftable on this client.");
                }
                else
                {
                    Plugin.Log.LogWarning(
                        "[Trailborne] Player.UpdateKnownRecipesList not found via reflection; " +
                        "client crafting refresh skipped (vanilla method renamed?).");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Trailborne] Player.OnSpawned recipe refresh failed: {e}");
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Fix B — size the PieceTable runtime arrays the build menu reads.
        //
        // Vanilla PieceTable.UpdateAvailable populates a per-category jagged
        // list (m_availablePieces) plus two Vector2Int[] selection arrays
        // (m_selectedPiece / m_lastSelectedPiece). A table whose selection
        // arrays are shorter than m_availablePieces.Count throws or silently
        // fails to display when the player tabs categories. Our spade-only
        // table (Trailblazing) is built from scratch with only m_pieces set, so
        // those structures are empty/zero-length; the vanilla hammer table we
        // append to is generally pre-sized but we repair it defensively too.
        //
        // m_availablePieces is a PRIVATE field → reached via a cached FieldRef.
        // We mirror Piece.PieceCategory.Max buckets (the vanilla real-category
        // count; the "All" pseudo-category at 100 is not a storage bucket).
        // Prefix grows the bucket list BEFORE vanilla fills it; postfix resizes
        // the selection arrays AFTER, to whatever final length vanilla left.
        // ─────────────────────────────────────────────────────────────
        private static readonly AccessTools.FieldRef<PieceTable, List<List<Piece>>> AvailablePiecesRef =
            AccessTools.FieldRefAccess<PieceTable, List<List<Piece>>>("m_availablePieces");

        // Number of real storage categories (Piece.PieceCategory.Max). Cached
        // so a future vanilla addition is picked up via the enum, not a literal.
        private static readonly int CategoryBucketCount = (int)Piece.PieceCategory.Max;

        [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpdateAvailable))]
        [HarmonyPrefix]
        public static void PieceTable_UpdateAvailable_Prefix(PieceTable __instance)
        {
            if (__instance == null) return;
            try
            {
                var buckets = AvailablePiecesRef(__instance);
                // Contract: vanilla UpdateAvailable initializes m_availablePieces
                // to the category count itself when the list is EMPTY (the
                // from-scratch spade table's first open hits that path). So we
                // only GROW an already-initialized list that is short of the
                // current category count — e.g. a higher category came into use
                // than vanilla pre-sized for. Leaving the empty case to vanilla
                // avoids diverting its own init branch; the postfix below is what
                // guarantees the selection arrays end up correctly sized for the
                // fresh-table case.
                if (buckets == null || buckets.Count == 0) return;
                while (buckets.Count < CategoryBucketCount)
                    buckets.Add(new List<Piece>());
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne] PieceTable.UpdateAvailable prefix (bucket grow) suppressed: {e.Message}");
            }
        }

        [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpdateAvailable))]
        [HarmonyPostfix]
        public static void PieceTable_UpdateAvailable_Postfix(PieceTable __instance)
        {
            if (__instance == null) return;
            try
            {
                var buckets = AvailablePiecesRef(__instance);
                int count = buckets?.Count ?? 0;
                if (count == 0) return;

                if (__instance.m_selectedPiece == null || __instance.m_selectedPiece.Length != count)
                    Array.Resize(ref __instance.m_selectedPiece, count);
                if (__instance.m_lastSelectedPiece == null || __instance.m_lastSelectedPiece.Length != count)
                    Array.Resize(ref __instance.m_lastSelectedPiece, count);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne] PieceTable.UpdateAvailable postfix (array resize) suppressed: {e.Message}");
            }
        }
    }
}
