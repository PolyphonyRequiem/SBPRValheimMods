using System;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Archer;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Archer
{
    /// <summary>
    /// T025-RT — the per-attempt placement gate for the Practice Range Archery Target. Enforces the
    /// load-bearing AND from spec FR-016 (also encoded engine-free in
    /// <see cref="PracticeRangeProvider.Resolve"/> / <c>PracticeRangeCapability.CanPlaceArcheryTarget</c>):
    /// a build attempt on <c>piece_ArcheryTarget</c> is refused unless BOTH conjuncts hold —
    ///
    ///   * the Practice Range Local Effect is ACTIVE for the acting occupant, AND
    ///   * the occupant is inside the Stone Area with an active relationship reservation to that Stone
    ///     (the server-observed inputs the shipped provider requires: occupancy + active Attunement/Bond).
    ///
    /// Vanilla already enforces the ordinary build ACL (PrivateArea / ward) as its own gate; this gate
    /// adds the Local-Effect conjunct on top, so neither policy eligibility alone nor build Permission
    /// alone unlocks the target — exactly the provider's contract.
    ///
    /// AUTHORITY + a HONEST seam boundary (net48-only, not link-compiled into net8):
    ///   * The building player's <c>Player.PlacePiece</c> runs client-side; this prefix cancels the
    ///     placement on that client when the capability is absent, giving the immediate "refused" UX.
    ///   * The live server runtime composed by <see cref="FoundationalRuntimeBootstrap"/> exposes the
    ///     resolvable server-observed facts (Stone Area membership via <c>StoneAreas</c>, the acting bound
    ///     principal via <c>BoundSessions</c>, and the active relationship via <c>Authority</c>). Those are
    ///     the facts <see cref="ResolveServerCapability"/> reads. Where the live composition does not yet
    ///     surface a full <see cref="Domain.StoneProgression.StoneProgressionAggregate"/> at placement
    ///     time (the same live-state gap the Foundational observer documents for the dedicated path), the
    ///     gate FAILS CLOSED on the Local-Effect conjunct rather than smuggling the target in — a stricter,
    ///     never a looser, decision than the spec requires. The remaining live wiring (feeding the fully
    ///     composed aggregate into <c>PracticeRangeProvider.Resolve</c> so the effect's active/dormant
    ///     status is re-derived per attempt) is the joined-client-proof follow-up.
    /// </summary>
    [HarmonyPatch]
    internal static class ArcheryTargetPlacementGate
    {
        // Set by FoundationalRuntimeBootstrap on the authoritative server (shared with the Foundational
        // observer). Null on a pure client — the client-side gate then relies on the conservative
        // resolvable facts it can read locally (see ClientCapabilityHeuristic).
        internal static FoundationalProgressionServer? Server =>
            SBPR.Niflheim.HomesteadStones.Features.Progression.FoundationalPlacementObserver.Server;

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
            var server = Server;
            Vector3 pos = piece.transform != null ? piece.transform.position : actor.transform.position;

            if (server != null)
                return ResolveServerCapability(actor, pos, server);

            // No server runtime resolvable in this process (pure remote client). Fail closed on the
            // Local-Effect conjunct — the authoritative server is the source of truth and will not have
            // replicated a target the occupant had no capability to place. This keeps the gate strict.
            return false;
        }

        /// <summary>Resolve the placement capability from the live server's resolvable, server-observed
        /// facts: the acting bound principal, Stone Area membership at the placement point, and an active
        /// relationship reservation to that Stone. Fails closed if any is absent.</summary>
        private static bool ResolveServerCapability(Player actor, Vector3 pos, FoundationalProgressionServer server)
        {
            // Stone Area membership (world-owned identity, never a client claim).
            if (!server.StoneAreas.TryResolve(pos.x, pos.z, out var stoneId))
                return false;

            // Acting bound principal (server-minted account/character), keyed by the same peer-key form
            // admission binds under — the identical resolution the Foundational observer uses.
            long actingPlayerId = actor != null ? actor.GetPlayerID() : 0L;
            string peerKey = ServerCreatorIdentity.CharacterSubject(actingPlayerId);
            if (string.IsNullOrEmpty(peerKey) || !server.BoundSessions.TryResolve(peerKey, out var principal))
                return false;

            // Active relationship reservation (Attunement/Bond) to THIS Stone — the low-bar relationship
            // that (with occupancy) makes the Practice Range Local Effect active for the occupant. Ordinary
            // build Permission is enforced separately by vanilla's PrivateArea/ward system.
            var authoritative = principal.ToPrincipal();
            var authority = server.Authority.GetAuthority(principal.Account, stoneId);
            return authority.HasActive(authoritative.Character);
        }

        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx >= 0 ? name.Substring(0, idx) : name;
        }
    }
}
