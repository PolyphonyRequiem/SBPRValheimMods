using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T009R3 (Blocker 1) — captures the ACTUAL placed <see cref="Piece"/> after a vanilla
    /// <c>Player.PlacePiece</c> runs, instead of the prefab/ghost that is passed AS the method argument.
    ///
    /// The T009R/T009R2 observers patched <c>Player.PlacePiece(Piece piece, ...)</c> and treated
    /// <paramref name="piece"/> as the instantiated placed object — it is NOT. Vanilla
    /// <c>PlacePiece</c> (assembly_valheim: <c>Player.PlacePiece</c>) does:
    /// <code>
    ///   GameObject gameObject = Object.Instantiate(piece.gameObject, pos, rot);   // the real instance
    ///   gameObject.GetComponent&lt;Piece&gt;()?.SetCreator(GetPlayerID());          // creator stamped here
    ///   ...
    ///   m_placed.Clear();
    ///   gameObject.GetComponents(m_placed);   // private static List&lt;IPlaced&gt; := the instance's IPlaced
    /// </code>
    /// So the argument <c>piece</c> is the build-ghost prefab (no world ZDO / no creator); the placed
    /// instance's <c>Piece</c> is the first <c>Piece</c> in the private static <c>Player.m_placed</c> list,
    /// populated from the instantiated object right before <c>PlacePiece</c> returns. Reading the ZDOID
    /// and creator off THAT instance is the only correct seam.
    ///
    /// Also note <c>PlacePiece</c> returns <c>void</c> — the prior observers declared a <c>bool __result</c>
    /// injection, which Harmony cannot satisfy for a void method (invalid patch shape). A reached
    /// <c>PlacePiece</c> is itself the success signal: vanilla only calls it from the default (success)
    /// branch of <c>TryPlacePiece</c> after every <c>PlacementStatus</c> failure has early-returned. So a
    /// postfix firing == a materialized successful placement.
    ///
    /// This references UnityEngine/Valheim + HarmonyLib and is net48-only (not link-compiled into net8).
    /// </summary>
    internal static class PlacedPieceCapture
    {
        // Player.m_placed is `private static List<IPlaced> m_placed`. AccessTools resolves it once; the
        // vanilla field name is stable (verify against assembly_valheim metadata if a Valheim update
        // renames it — a null accessor degrades to "no capture", never a crash).
        private static readonly AccessTools.FieldRef<List<IPlaced>>? PlacedRef = ResolvePlacedRef();

        private static AccessTools.FieldRef<List<IPlaced>>? ResolvePlacedRef()
        {
            var field = AccessTools.Field(typeof(Player), "m_placed");
            if (field == null) return null;
            return AccessTools.StaticFieldRefAccess<List<IPlaced>>(field);
        }

        /// <summary>Return the instantiated placed <see cref="Piece"/> vanilla just populated into
        /// <c>Player.m_placed</c>, or null when none is available (field missing / list empty / no Piece
        /// among the placed components). This is the world instance carrying the durable ZDO + creator,
        /// NOT the prefab argument to PlacePiece.</summary>
        internal static Piece? PlacedPiece()
        {
            var accessor = PlacedRef;
            if (accessor == null) return null;

            List<IPlaced> placed;
            try { placed = accessor(); }
            catch { return null; }
            if (placed == null) return null;

            // The instantiated object's components are appended by gameObject.GetComponents(m_placed);
            // the placed Piece is the first Piece among them.
            for (int i = 0; i < placed.Count; i++)
            {
                if (placed[i] is Piece piece && piece != null) return piece;
            }
            return null;
        }
    }
}
