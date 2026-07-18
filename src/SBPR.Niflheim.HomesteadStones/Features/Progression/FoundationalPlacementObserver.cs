using System;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T009 — the net48-ONLY live-runtime observer that turns a real, server-authoritative Valheim
    /// build placement into a <see cref="FoundationalPlacementObservation"/> and routes it through the
    /// shipped <see cref="FoundationalPlacementRuntime"/> (adapter → pipeline → durable receipt).
    ///
    /// This file references UnityEngine/Valheim (Player, Piece, ZNet, ZNetView, ZDO) and therefore does
    /// NOT link-compile into the net8 test suite — only the engine-free runtime/derivation seams it
    /// calls are unit-tested. The observer itself is deliberately thin: it does NO domain policy. Every
    /// decision (catalog membership, exclusion, version, Stone Area, success, anti-repetition,
    /// authorization) lives in the tested engine-free types; this class only ATTRIBUTES trusted
    /// server-owned facts and hands them across.
    ///
    /// Authority (never client-authoritative):
    ///   * runs only when <c>ZNet.instance.IsServer()</c> — the authoritative host. On a listen server /
    ///     singleplayer host, <c>Player.PlacePiece</c> executes on the server for the host's own builds,
    ///     so the acting identity is the authenticated local player id (server context), not a payload.
    ///   * a joined DEDICATED-server client's build is replicated as a ZDO rather than routed through a
    ///     server-run PlacePiece; wiring that observation path is the live-integration work T009L drives
    ///     against a real joined client (logs-green ≠ playable). This seam is the smallest correct one
    ///     that already carries a genuine server-authoritative placement end-to-end.
    ///
    /// Physical-instance provenance is the placed piece's durable ZDOID string, so the same physical
    /// piece is credited at most once across re-observation, retry, and restart. Stone Area membership
    /// is resolved from resident Homestead Stone ZDOs (world-owned identity), never a client claim.
    /// </summary>
    [HarmonyPatch]
    internal static class FoundationalPlacementObserver
    {
        // Wired once by Plugin.Awake on the server; null on clients (the patch no-ops when unset).
        internal static FoundationalProgressionServer? Server;

        // The current-build prefab→stable-id map and catalog version tag (immutable, engine-free).
        private static readonly FoundationalPrefabMap PrefabMap = FoundationalPrefabMap.CurrentBuild;

        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
        [HarmonyPostfix]
        private static void OnPlacePiece(Player __instance)
        {
            var server = Server;
            if (server == null) return;                       // not the server / not wired
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            // Blocker 1: the placed instance is NOT the PlacePiece `piece` argument (that is the build
            // ghost/prefab). Vanilla instantiates a NEW object and records it into the private static
            // Player.m_placed list before returning; the real placed Piece (with its world ZDO + stamped
            // creator) is captured from there. A reached PlacePiece postfix is itself the success signal
            // (vanilla only calls it from TryPlacePiece's success branch), so there is no bool result.
            var placed = PlacedPieceCapture.PlacedPiece();
            if (placed == null) return;

            try
            {
                Observe(__instance, placed, server);
            }
            catch (Exception ex)
            {
                // Never let progression observation destabilize the build path.
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Foundational placement observation threw: " + ex);
            }
        }

        private static void Observe(Player actor, Piece piece, FoundationalProgressionServer server)
        {
            // Prefab identity → stable catalog id (server-observed; empty when not a Foundational piece,
            // which the adapter rejects as MissingPieceIdentity / NotCatalogMember — never a rebind).
            string prefabName = piece.gameObject != null
                ? StripCloneSuffix(piece.gameObject.name)
                : string.Empty;
            string stablePieceId = PrefabMap.ResolveStablePieceId(prefabName) ?? string.Empty;

            // Physical-instance provenance = durable ZDOID string (stable across restart). Falls back to
            // empty when the piece has no ZDO yet (the runtime then derives a per-actor operation id).
            string provenance = string.Empty;
            var nview = piece.GetComponent<ZNetView>();
            if (nview != null)
            {
                var zdo = nview.GetZDO();
                if (zdo != null) provenance = zdo.m_uid.ToString();
            }

            // Placement world position (server-owned transform), used to resolve Stone Area membership.
            Vector3 pos = piece.transform != null ? piece.transform.position : Vector3.zero;
            var membership = server.StoneAreas;
            bool inside = membership.TryResolve(pos.x, pos.z, out var stoneId);

            // Acting identity IAP-007 Tracer 3 / IAP-007W: the gameplay principal is the acting peer's
            // BOUND INTERNAL session (server-minted AccountId/CharacterId) published by admission — NOT a
            // raw provider/platform subject and NOT the character ZDOID. We key the bound-session index by
            // the local player's durable s_playerID rendered as the stable player:<s_playerID> character
            // subject (a server-owned peer key, the SAME key form admission binds under and the dedicated
            // ingress resolves — never itself persisted as gameplay identity). If no internal session is
            // bound (admission not yet complete/activated), we FAIL CLOSED: credit nothing rather than fall
            // back to a provider identity.
            long actingPlayerId = actor != null ? actor.GetPlayerID() : 0L;
            string peerKey = ServerCreatorIdentity.CharacterSubject(actingPlayerId);
            if (string.IsNullOrEmpty(peerKey) ||
                !server.BoundSessions.TryResolve(peerKey, out var sessionPrincipal))
            {
                // No bound internal session -> no gameplay principal. Never provider-derive one.
                return;
            }
            string accountId = sessionPrincipal.Account.Value;
            string characterId = sessionPrincipal.Character.Value;

            var observation = new FoundationalPlacementObservation(
                stoneId,
                accountId,
                characterId,
                stablePieceId,
                provenance,
                insideStoneArea: inside,
                placementSucceeded: true,
                foundationalCatalogVersion: PrefabMap.CatalogVersionTag);

            var outcome = server.Runtime.Observe(observation);

            // Bounded, PII-free operator log line. Only log when this was actually a Foundational piece
            // (a mapped stable id) so unrelated vanilla builds don't spam the operator log.
            if (!string.IsNullOrEmpty(stablePieceId))
                Plugin.Log.LogInfo("[Niflheim/HomesteadStones] " + outcome.ToOperatorLine());
        }

        /// <summary>Strip Unity's "(Clone)" suffix so a live instance name matches the registered prefab
        /// name used by the engine-free prefab map.</summary>
        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }
    }
}
