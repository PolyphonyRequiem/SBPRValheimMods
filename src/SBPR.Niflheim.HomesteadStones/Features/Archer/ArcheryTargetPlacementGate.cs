using System;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Archer;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Features.Progression;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Archer
{
    /// <summary>
    /// T025R — the per-attempt placement gate for the Practice Range Archery Target, ported onto the
    /// independently-reviewed authoritative Local Effect activation runtime (PR #368). It refuses a build
    /// attempt on <c>piece_ArcheryTarget</c> unless the acting occupant holds the Practice Range Local
    /// PLACEMENT capability — the load-bearing AND from spec FR-016:
    ///
    ///   * the Practice Range Local Effect is ACTIVE for the acting occupant (developed + Tree committed +
    ///     Active Stone Level + authorized Governor present + policy-eligible + inside the Stone Area — the
    ///     full dormancy/governance/policy/area derivation the <see cref="LocalActivationService"/> owns),
    ///     AND
    ///   * the occupant independently passes ORDINARY build Permission (vanilla PrivateArea/ward).
    ///
    /// Neither policy eligibility alone nor build Permission alone unlocks the target, exactly the shipped
    /// <see cref="PracticeRangeProvider"/> contract.
    ///
    /// AUTHORITY — this gate does NOT re-derive activation itself and holds NO parallel Local-effect ledger
    /// or provisional grant. It reads the authoritative projection the reviewed runtime already produces:
    ///   * On the authoritative HOST (listen-server / singleplayer host), the placing player's PlacePiece
    ///     runs server-side, so the composed <see cref="LocalProgressionObserver.Server"/> is present and
    ///     the gate FETCHES the acting occupant's read model directly from the authoritative
    ///     <see cref="LocalActivationService"/> (via the same server-owned occupant/relationship/occupancy
    ///     facts the delivery channel uses) and asks it the FR-016 AND question.
    ///   * On a pure remote CLIENT, PlacePiece runs client-side for the immediate "refused" UX. The client
    ///     never holds the authoritative inputs; it consults the bounded read model the server pushed into
    ///     <see cref="LocalProgressionObserver.ClientCache"/>. The server only ever delivers a snapshot with
    ///     the effect active when IT confirmed (server-side occupancy + committed governance/policy) the
    ///     occupant is entitled, so an active held snapshot is authoritative proof, and the absence of one
    ///     FAILS CLOSED. The server remains the source of truth for the replicated piece regardless.
    /// </summary>
    [HarmonyPatch]
    internal static class ArcheryTargetPlacementGate
    {
        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
        [HarmonyPrefix]
        private static bool BeforePlacePiece(Player __instance, Piece piece)
        {
            try
            {
                if (piece == null || piece.gameObject == null) return true;    // let vanilla proceed
                string prefabName = StripCloneSuffix(piece.gameObject.name);
                if (!string.Equals(prefabName, PracticeRangeContent.ArcheryTargetPrefab, StringComparison.Ordinal))
                    return true;                                                // not our piece — no gate

                bool permitted = IsPlacementPermitted(__instance, piece);
                if (!permitted)
                {
                    __instance.Message(MessageHud.MessageType.Center, "$msg_invalidplacement");
                    Plugin.Log.LogInfo(
                        "[Niflheim/Archer] Refused Archery Target placement: Practice Range capability absent "
                        + "(active Local Effect AND ordinary build Permission required).");
                    return false;                                              // cancel vanilla PlacePiece
                }
                return true;
            }
            catch (Exception ex)
            {
                // Never let the gate destabilize the build path; on error, defer to vanilla (which still
                // enforces its own ACL). Logged for the operator.
                Plugin.Log.LogError("[Niflheim/Archer] Archery Target placement gate threw: " + ex);
                return true;
            }
        }

        private static bool IsPlacementPermitted(Player actor, Piece piece)
        {
            Vector3 pos = piece.transform != null ? piece.transform.position
                : (actor != null ? actor.transform.position : Vector3.zero);

            // Ordinary build Permission is a HARD, SEPARATE conjunct (spec FR-016 final sentence): vanilla's
            // PrivateArea/ward ACL, evaluated at the placement point. Policy/relationship never grants it and
            // it never smuggles the effect in outside the policy.
            bool hasBuildPermission = PrivateArea.CheckAccess(pos, 0f, flash: false);
            if (!hasBuildPermission) return false;

            var server = LocalProgressionObserver.Server;
            if (server != null)
                return ResolveHostCapability(actor, pos, server, hasBuildPermission);

            // Pure remote client: consult the authoritative read model the server pushed. Fail closed when no
            // active snapshot for the Practice Range node is held.
            return LocalProgressionObserver.ClientCache.CanExercisePlacementForNode(
                PracticeRangeProvider.PracticeRangeNode, hasBuildPermission);
        }

        /// <summary>Authoritative HOST path: fetch the acting occupant's read model straight from the
        /// composed <see cref="LocalActivationService"/> using the SAME server-owned facts the delivery
        /// channel resolves (transport-authenticated bound principal, Stone Area membership at the placement
        /// point resolved server-side, and the occupant's committed relationship reservation). Then ask the
        /// authoritative snapshot the FR-016 AND question. No re-derivation, no provisional ledger — the
        /// service owns every activation input (policy/governance/level/dormancy/occupancy). Fail closed if
        /// any server-owned fact is absent.</summary>
        private static bool ResolveHostCapability(Player? actor, Vector3 pos, LocalProgressionServer server,
            bool hasBuildPermission)
        {
            var foundational = FoundationalPlacementObserver.Server;
            if (foundational == null || actor == null) return false;

            // Stone Area membership (world-owned identity, resolved from the server-owned placement point).
            if (!foundational.StoneAreas.TryResolve(pos.x, pos.z, out var stoneId))
                return false;

            // Acting bound INTERNAL principal (server-minted account/character), keyed by the same
            // player:<s_playerID> character subject admission binds under — never the payload.
            long actingPlayerId = actor.GetPlayerID();
            string peerKey = ServerCreatorIdentity.CharacterSubject(actingPlayerId);
            if (string.IsNullOrEmpty(peerKey) || !foundational.BoundSessions.TryResolve(peerKey, out var principal))
                return false;

            var occupant = principal.Account;
            var character = principal.Character;
            if (string.IsNullOrEmpty(occupant.Value)) return false;

            // The occupant's committed relationship reservation to THIS Stone (a server-owned fact). The
            // owner + Stone-wide authorized-Governor-presence facts are DERIVED inside ComposePresence from
            // committed state, never a client claim. Occupancy is true: PlacePiece fired at a point the Stone
            // Area resolver just confirmed is inside this Stone.
            bool hasRelationship = server.Authority.GetAuthority(occupant, stoneId).HasActive(character);
            var presence = server.ComposePresence(stoneId, occupant, character, hasRelationship,
                insideStoneArea: true);

            // Fetch (not Publish) the current read model — a placement check must not bump the delivery
            // sequence. This is the authoritative projection; the FR-016 AND is asked of it directly.
            var snapshot = server.Activation.Fetch(stoneId, presence);
            return snapshot.AuthorityPresent
                && snapshot.CanExercisePlacement(PracticeRangeProvider.PracticeRangeNode, hasBuildPermission);
        }

        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }
    }
}
