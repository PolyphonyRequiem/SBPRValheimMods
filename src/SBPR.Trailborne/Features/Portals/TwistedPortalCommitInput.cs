// ============================================================================
//  Trailborne v3 (Swamp) — Twisted Portal look-to-aim COMMIT INPUT (engine host)
// ----------------------------------------------------------------------------
//  Card     : t_f4d0d5e1 (L1) — aim-pick destination + tap-E commit.
//  Impl spec: docs/v3/planning/twisted-portal-impl-spec.md §1 Q3 (Beat 2/4),
//             §4.4a (ResolveAimedDestination), §4.5 (commit input), §6 (E demote).
//  Design   : Daniel 2026-06-27 — "stand on the portal, aim the crosshair at the
//             target portal, press E to commit."
//  Math     : AimPickMath (engine-free angular pick, CI-gated AT-AIM-SELECT).
//  Source   : TwistedPortalCandidates.Gather (the shared candidate set; L2 swaps
//             its body for the server-authoritative RPC set, §2).
//
//  WHAT THIS DOES. A Player.Update POSTFIX (the SeersStone PinByLookInput / the
//  IronCompass camera-read precedent) that, while the local player stands on a
//  Twisted Portal (Beat 1 proximity-active):
//    1. gathers the candidate destination set (throttled ZDO walk, §2 staging);
//    2. EVERY FRAME picks the destination whose world-direction-from-the-camera
//       is closest in angle to the crosshair (camera forward), within the aim
//       cone (AimPickMath; the live BepInEx cone knob) — the angular pick, NOT a
//       collider raycast, so a portal behind a hill is selectable (AT-AIM-
//       THROUGHTERRAIN);
//    3. PUBLISHES the aim state (origin id, aimed destination, in-cone-ness) for
//       the L3 overlay to draw the selected-highlight + food-impact preview on;
//    4. on TAP-[Use]/E commits travel to the aimed destination (AT-COMMIT-E); on
//       HOLD-[Use]/E opens the rune rename on the portal under the crosshair (the
//       demoted rename gesture — Daniel's locked E-key fork, AT-RENAME-DEMOTE).
//
//  WHY OWN E HERE (not in SBPR_TwistedPortal.Interact). Vanilla routes a held Use
//  to Interact(hold:true) from frame 2 of ANY press (decomp Player.Update :16127),
//  so the Interact path alone cannot cleanly separate a TAP (commit) from a HOLD
//  (rename) on the same key. A press-duration timer here can: tap commits on
//  keydown (instant), hold fires rename at the threshold. SBPR_TwistedPortal.Interact
//  is a deliberate no-op so the two paths never double-fire.
//
//  Client-only by construction: Player.m_localPlayer / GameCamera.instance are
//  client concerns; the postfix early-returns on the dedicated server (no local
//  player). Reads portal ZDOs + the camera, writes only the rune ZDO via the
//  portal's owner-guarded WriteRuneName (on rename). Travel itself is the C2
//  food-as-fuel debit + Player.TeleportTo — unchanged.
//
//  Clean-side (ADR-0001): base-game Player / GameCamera / Console / Chat / Input +
//  our own classes only. No third-party code.
// ============================================================================

using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Portals
{
    /// <summary>
    /// The look-to-aim commit-input host (spec §4.5). A <c>Player.Update</c> postfix that drives the
    /// aim-pick + tap-E commit / hold-E rename. Publishes the per-frame aim state for the L3 overlay
    /// (selected-highlight + food preview). No-op off-portal and on the dedicated server.
    /// </summary>
    [HarmonyPatch(typeof(Player), "Update")]
    public static class TwistedPortalCommitInput
    {
        // ── Published aim state (the L3 read surface). Set every frame the local player stands on a
        //    Twisted Portal; cleared when off-portal. L3's overlay reads these to highlight the aimed
        //    destination's label and render the read-only food-impact preview (PreviewJump). ──
        /// <summary>True while the local player stands on a Twisted Portal (Beat 1 proximity-active).</summary>
        public static bool OnPortal { get; private set; }
        /// <summary>The ZDO id of the portal the player stands on (the origin), or default when off-portal.</summary>
        public static ZDOID OriginId { get; private set; }
        /// <summary>True when a destination is currently aimed (in cone). When false, <see cref="AimedDestination"/> is stale.</summary>
        public static bool HasAim { get; private set; }
        /// <summary>The currently aimed destination (valid only when <see cref="HasAim"/>). The L3 highlight + preview target.</summary>
        public static TwistedDestination AimedDestination { get; private set; }

        // ── Candidate-walk throttle. Portals don't move, so the ZDO walk is cached and refreshed at
        //    this cadence; the aim-pick itself runs EVERY frame against the cache for a smooth
        //    crosshair-tracked highlight (the pick is a handful of dot products — free). ──
        private const float GatherInterval = 0.33f;

        // ── Hold-vs-tap threshold (seconds). Below → a TAP (commit on release-or-keydown); at/above
        //    while held → a HOLD (rename). Comfortable, matches the vanilla ~0.33 s hold feel. ──
        private const float HoldRenameThreshold = 0.4f;

        // Reused scratch (no per-frame allocation): the ZDO accumulator, the candidate set, and the
        // engine-free direction list handed to AimPickMath.
        private static readonly List<ZDO> _zdoScratch = new List<ZDO>();
        private static readonly List<TwistedDestination> _candidates = new List<TwistedDestination>();
        private static readonly List<AimVec> _dirs = new List<AimVec>();
        // Parallel to _dirs: the index back into _candidates for each direction (origin excluded).
        private static readonly List<int> _dirCandidateIndex = new List<int>();

        private static float _nextGather;
        // Press-duration state for the tap/hold split.
        private static bool _ePressed;        // E is currently down (since some keydown)
        private static float _ePressStart;    // Time.time at the keydown
        private static bool _holdRenameFired; // the hold-rename already fired for this press (once per hold)

        [HarmonyPostfix]
        public static void Postfix(Player __instance)
        {
            try
            {
                if (__instance == null || __instance != Player.m_localPlayer) return;
                if (GameCamera.instance == null) return;

                // Don't act while a text field / menu / the rename box itself captures input.
                bool inputBlocked =
                    (Console.instance != null && Console.IsVisible()) ||
                    (Chat.instance != null && Chat.instance.HasFocus()) ||
                    (TextInput.instance != null && TextInput.instance.m_panel != null && TextInput.instance.m_panel.activeSelf);

                // Refresh the candidate set on the throttle (portals are static; cheap walk).
                if (Time.time >= _nextGather)
                {
                    _nextGather = Time.time + GatherInterval;
                    TwistedPortalCandidates.Gather(__instance.transform.position, _zdoScratch, _candidates);
                }

                // Beat 1 — are we standing on a Twisted Portal? (The proximity-active state.)
                float proximity = Plugin.TwistedOverlayProximityRange?.Value
                                  ?? TwistedPortalOverlayModel.DefaultProximityRange;
                int originIdx = TwistedPortalCandidates.FindOrigin(_candidates, __instance.transform.position, proximity);

                if (originIdx < 0)
                {
                    // Off-portal: clear published state + any in-flight press, and stop.
                    ClearAim();
                    _ePressed = false;
                    _holdRenameFired = false;
                    return;
                }

                OnPortal = true;
                OriginId = _candidates[originIdx].Id;

                // Beat 2 — aim-pick the destination (angular pick among the candidate set, origin
                // excluded). Camera position + forward = the crosshair ray (the IronCompass idiom).
                Transform camT = GameCamera.instance.transform;
                ResolveAim(__instance, camT, originIdx);

                // Beats 3/4 — input. Skip while a menu/console/rename box owns input.
                if (inputBlocked)
                {
                    _ePressed = false;
                    _holdRenameFired = false;
                    return;
                }

                HandleInput(__instance, originIdx);
            }
            catch (System.Exception e)
            {
                // A look-to-aim bug must NEVER break Player.Update (the SeersStone pin-by-look lesson).
                Plugin.Log.LogWarning($"[Trailborne/TwistedPortal] Commit-input error (ignored): {e.Message}");
            }
        }

        /// <summary>
        /// Beat 2/3: pick the aimed destination (angular pick, <see cref="AimPickMath"/>) and publish
        /// it. Builds the direction list (each candidate's direction FROM THE CAMERA, origin excluded)
        /// and the cone from the live BepInEx knob, then sets <see cref="HasAim"/> + <see cref="AimedDestination"/>.
        /// </summary>
        private static void ResolveAim(Player player, Transform camT, int originIdx)
        {
            _dirs.Clear();
            _dirCandidateIndex.Clear();

            Vector3 camPos = camT.position;
            for (int i = 0; i < _candidates.Count; i++)
            {
                if (i == originIdx) continue;                 // can't travel to the portal you're on
                Vector3 d = _candidates[i].Position - camPos; // crosshair ray origin = camera
                _dirs.Add(new AimVec(d.x, d.y, d.z));
                _dirCandidateIndex.Add(i);
            }

            float coneDeg = Plugin.TwistedAimConeDegrees?.Value ?? AimPickMath.DefaultAimConeDegrees;
            float coneCos = AimPickMath.ConeCosFromDegrees(coneDeg);
            Vector3 fwd = camT.forward;

            int pick = AimPickMath.PickByAim(_dirs, new AimVec(fwd.x, fwd.y, fwd.z), coneCos, out _);
            if (pick < 0)
            {
                HasAim = false;
                return;
            }
            HasAim = true;
            AimedDestination = _candidates[_dirCandidateIndex[pick]];
        }

        /// <summary>
        /// Beat 4: the tap/hold-E split. TAP-E (a short press) commits travel to the aimed destination;
        /// HOLD-E (held past <see cref="HoldRenameThreshold"/>) opens the rune rename on the portal the
        /// crosshair is pointing at (origin if none). Owns the press-duration state machine so tap and
        /// hold never both fire for one press (spec §4.5 / §6 — Daniel's locked E-key fork).
        /// </summary>
        private static void HandleInput(Player player, int originIdx)
        {
            // Only act on E when we're NOT pointed at some OTHER vanilla interactable (a chest, a door):
            // in that posture vanilla owns the Use key. Aiming at the open world / through-terrain (hover
            // == null) or at a Twisted Portal is ours. This keeps E un-hijacked for normal interactions.
            if (IsHoveringForeignInteractable(player))
            {
                _ePressed = false;
                _holdRenameFired = false;
                return;
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                _ePressed = true;
                _ePressStart = Time.time;
                _holdRenameFired = false;
            }

            if (_ePressed && Input.GetKey(KeyCode.E) && !_holdRenameFired)
            {
                // Held past the threshold → RENAME (the demoted gesture). Fire once per hold; this
                // pre-empts the tap-commit so a long press never also travels.
                if (Time.time - _ePressStart >= HoldRenameThreshold)
                {
                    _holdRenameFired = true;
                    OpenRenameUnderCrosshair(player, originIdx);
                }
            }

            if (Input.GetKeyUp(KeyCode.E))
            {
                bool wasShort = _ePressed && (Time.time - _ePressStart < HoldRenameThreshold);
                _ePressed = false;
                if (wasShort && !_holdRenameFired)
                {
                    // TAP → commit travel to the aimed destination (if any). No aimed destination →
                    // a tap does nothing (you're on a portal but not aiming at a target).
                    if (HasAim)
                        CommitToAim(player, originIdx);
                }
                _holdRenameFired = false;
            }
        }

        /// <summary>Commit travel: resolve the ORIGIN portal component and call its CommitTravel with the
        /// published aimed destination (the C2 food-as-fuel debit + the §5.9 cooldown pre-check live there).</summary>
        private static void CommitToAim(Player player, int originIdx)
        {
            ZDOID originId = _candidates[originIdx].Id;
            SBPR_TwistedPortal? origin = SBPR_TwistedPortal.FindByZdoId(originId);
            if (origin == null) return;   // origin scrolled out of the active set this frame — no-op
            TwistedDestination dest = AimedDestination;
            origin.CommitTravel(player, dest);
        }

        /// <summary>Open the rune rename (hold-E). Prefer the portal directly under the crosshair (so you
        /// can name a portal you're looking at); fall back to the origin (the one you stand on).</summary>
        private static void OpenRenameUnderCrosshair(Player player, int originIdx)
        {
            SBPR_TwistedPortal? target = null;

            GameObject? hover = player.GetHoverObject();
            if (hover != null) target = hover.GetComponentInParent<SBPR_TwistedPortal>();

            if (target == null)
                target = SBPR_TwistedPortal.FindByZdoId(_candidates[originIdx].Id);

            target?.RequestRenameDialog();
        }

        /// <summary>True when the crosshair is on a vanilla interactable that ISN'T a Twisted Portal —
        /// in which case vanilla's Use key owns the press and we stay out (don't hijack chests/doors).
        /// Hover == null (aiming at open world / through terrain) or a Twisted Portal ⇒ ours.</summary>
        private static bool IsHoveringForeignInteractable(Player player)
        {
            GameObject? hover = player.GetHoverObject();
            if (hover == null) return false;                                  // open world — ours
            if (hover.GetComponentInParent<SBPR_TwistedPortal>() != null) return false; // our portal — ours
            return true;                                                      // someone else's interactable
        }

        private static void ClearAim()
        {
            OnPortal = false;
            HasAim = false;
            OriginId = default;
        }
    }
}
