using System;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T029 remediation — the net48-ONLY live-runtime observer that gates a real, server-authoritative
    /// vanilla T.W.I.G. (TrainingDummy) placement through the shipped Warrior
    /// <see cref="WarriorLocalPlacementGate"/> (which routes it through the pure LocalPlacementProvider).
    ///
    /// Why this exists (QA t_92e47866 / PR #366 FAIL): the pure LocalPlacementProvider had zero runtime
    /// callers, so on a joined client a T.W.I.G. placement ran through vanilla Player.PlacePiece with NO
    /// SBPR gating — the FR-016 effect-active / Settlement-policy / build-Permission AND never fired
    /// in-world and the refusals could not occur. This observer is the missing seam: it recognizes the
    /// EXACT TrainingDummy prefab on a server-run placement and, when the gate refuses, UNDOES the
    /// placement (destroys the placed piece) so the ungated build never stands.
    ///
    /// This mirrors <see cref="FoundationalPlacementObserver"/> exactly:
    ///   * runs only when <c>ZNet.instance.IsServer()</c> — the authoritative host. On a listen /
    ///     singleplayer host the placing player's PlacePiece runs on the server, so the acting identity is
    ///     the authenticated local player id (server context), not a payload;
    ///   * a joined DEDICATED-server client's build never runs PlacePiece on the server — that path is the
    ///     <see cref="WarriorTwigDedicatedIngressObserver"/> (a client-side refusal-notice + server-side
    ///     ZDO revalidation), the direct analogue of the Foundational dedicated ingress;
    ///   * the placed piece's prefab, world position, and creator are all SERVER-OBSERVED facts (never a
    ///     client claim); the acting occupant's gameplay principal is the BOUND INTERNAL session published
    ///     by admission, resolved by the durable player:&lt;s_playerID&gt; peer key.
    ///
    /// References UnityEngine/Valheim (Player, Piece, ZNet, ZNetView, ZNetScene, ZDO) → net48-only, not
    /// link-compiled into net8. The engine-free gate it drives is fully unit-tested. Clean-side
    /// (ADR-0001): base-game types only; ADR-0006: no prefab cloning — it only reads a placed piece and,
    /// on refusal, destroys it via the vanilla ZNetScene.Destroy path.
    /// </summary>
    [HarmonyPatch]
    internal static class WarriorTwigPlacementObserver
    {
        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
        [HarmonyPostfix]
        private static void OnPlacePiece(Player __instance)
        {
            var server = FoundationalPlacementObserver.Server;
            if (server == null) return;                        // not the server / not wired
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            // The placed instance is the object vanilla just recorded into Player.m_placed (its world ZDO +
            // stamped creator), NOT the PlacePiece `piece` argument (the build ghost/prefab).
            var placed = PlacedPieceCapture.PlacedPiece();
            if (placed == null) return;

            try
            {
                Gate(__instance, placed, server);
            }
            catch (Exception ex)
            {
                // Never let progression gating destabilize the build path.
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Warrior T.W.I.G. placement gate threw: " + ex);
            }
        }

        private static void Gate(Player actor, Piece piece, FoundationalProgressionServer server)
        {
            var gate = server.WarriorTwigGate;

            string prefabName = piece.gameObject != null
                ? StripCloneSuffix(piece.gameObject.name)
                : string.Empty;

            // Fast decline: only the exact T.W.I.G. is ours. Any other piece is left entirely untouched.
            if (!string.Equals(prefabName, gate.TwigPrefabName, StringComparison.Ordinal))
                return;

            // Server-owned world position for Stone Area resolution.
            Vector3 pos = piece.transform != null ? piece.transform.position : Vector3.zero;

            // Acting occupant's BOUND INTERNAL session, keyed by the durable player:<s_playerID> subject —
            // the SAME server-owned peer key admission binds under and the Foundational observer resolves.
            long actingPlayerId = actor != null ? actor.GetPlayerID() : 0L;
            string peerKey = ServerCreatorIdentity.CharacterSubject(actingPlayerId);

            // The occupant's ordinary build Permission at the placement position — the vanilla ward check,
            // the SAME gate vanilla itself uses for build access. Never a client claim.
            bool hasBuildPermission = PrivateArea.CheckAccess(pos, 0f, flash: false);

            var outcome = gate.Admit(peerKey, prefabName, pos.x, pos.z, hasBuildPermission);

            if (outcome.RequiresUndo)
            {
                UndoPlacement(piece);
                Plugin.Log.LogInfo("[Niflheim/HomesteadStones] " + outcome.ToOperatorLine() + " action=undone");
                // Tell the placing player why (vanilla message token; presentation only).
                actor?.Message(MessageHud.MessageType.Center, "$msg_privatezone");
            }
            else if (outcome.IsAdmitted)
            {
                Plugin.Log.LogInfo("[Niflheim/HomesteadStones] " + outcome.ToOperatorLine() + " action=admitted");
            }
        }

        /// <summary>Undo an un-gated placement by destroying the placed piece through the vanilla
        /// ZNetScene.Destroy path. Owner-only: claim the ZDO first (server doctrine), then destroy. ADR-0006
        /// compliant — this reads and removes a live world instance, it never clones a prefab.</summary>
        private static void UndoPlacement(Piece piece)
        {
            if (piece == null || piece.gameObject == null) return;
            var nview = piece.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                if (!nview.IsOwner()) nview.ClaimOwnership();
                nview.Destroy();
                return;
            }
            // No ZNetView (should not happen for a placed piece) — fall back to a scene destroy.
            var zns = ZNetScene.instance;
            if (zns != null) zns.Destroy(piece.gameObject);
        }

        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }
    }
}
