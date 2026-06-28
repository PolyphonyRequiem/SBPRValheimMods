using System.Collections.Generic;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Portals
{
    /// <summary>
    /// One look-to-aim destination candidate: a Twisted Portal's world placement + its rune (the
    /// human-readable AIM LABEL, spec §6 — NOT the pairing key under look-to-aim). The commit input
    /// (<see cref="TwistedPortalCommitInput"/>) aim-picks among these; <see cref="SBPR_TwistedPortal.CommitTravel"/>
    /// receives the chosen one as the teleport target. Under look-to-aim an UNNAMED portal is a valid
    /// destination too (any Twisted Portal is selectable by aim, AT-AIM-SELECT) — the rune is only a label.
    /// </summary>
    public readonly struct TwistedDestination
    {
        /// <summary>The portal's world position (the destination ring centre).</summary>
        public readonly Vector3 Position;

        /// <summary>The portal's world rotation (used to step the player out the front on arrival).</summary>
        public readonly Quaternion Rotation;

        /// <summary>The censored rune name (aim label), or "" when unnamed.</summary>
        public readonly string Rune;

        /// <summary>False for an unnamed portal (empty <c>sbpr_rune_name</c>) — still a valid aim target,
        /// just labelled "(unnamed)" by the overlay. Names are labels, not the pairing key (spec §1 Q3).</summary>
        public readonly bool HasRune;

        /// <summary>The portal's ZDO id — the stable identity used to exclude the origin portal (the one
        /// the player is standing on) from the destination set, and to match the aimed label for the
        /// L3 selected-highlight + food preview.</summary>
        public readonly ZDOID Id;

        public TwistedDestination(Vector3 position, Quaternion rotation, string rune, bool hasRune, ZDOID id)
        {
            Position = position;
            Rotation = rotation;
            Rune = rune;
            HasRune = hasRune;
            Id = id;
        }
    }

    /// <summary>
    /// The shared candidate-set source for look-to-aim travel (spec §4.4a / §2). BOTH the on-step
    /// overlay (<see cref="TwistedPortalOverlay"/>, the labels you aim at) AND the commit input
    /// (<see cref="TwistedPortalCommitInput"/>, the aim-pick + tap-E) gather their Twisted Portal set
    /// from here, so the label you see highlighted is provably the portal you travel to (one walk, no
    /// drift between the two surfaces).
    ///
    /// ════════════════════════════════════════════════════════════════════════════════════════════
    /// 🔴 THE L2 SWAP POINT (spec §2 — server-authoritative reach is REQUIRED on the redesign).
    ///   This is the STAGING implementation: a client-side <c>ZDOMan.GetAllZDOsWithPrefabIterative</c>
    ///   walk (decomp :65497) that sees only the ZDOs THIS PEER HOLDS — on a dedicated server, the
    ///   ~64–128 m sector window (§2), NOT a guaranteed 300 m. That is enough for L1's in-game aim/feel
    ///   accept (you aim at a nearby portal and travel), but L2 (card t_ccb454f8) MUST swap the BODY of
    ///   <see cref="Gather"/> for the owner-routed RPC candidate set (the <c>SurveyorTableTag</c>
    ///   <c>Register&lt;ZPackage&gt;</c>/<c>InvokeRPC</c> precedent) so the picker reaches destinations
    ///   past the client window. The SIGNATURE — (player position, reusable scratch, reusable output) →
    ///   a flat <see cref="TwistedDestination"/> list — is the contract L2 inherits; callers don't change.
    /// ════════════════════════════════════════════════════════════════════════════════════════════
    ///
    /// Clean-side (ADR-0001): base-game <c>ZDOMan</c>/<c>ZDO</c>/<c>CensorShittyWords</c> + our own ZDO
    /// slot only. Reads portal ZDOs, never writes them.
    /// </summary>
    public static class TwistedPortalCandidates
    {
        /// <summary>
        /// Fill <paramref name="into"/> with every Twisted Portal this peer currently holds, as flat
        /// <see cref="TwistedDestination"/> rows (position + rotation + censored rune + id). Both lists
        /// are caller-owned reusables (cleared here) so a throttled refresh allocates nothing.
        ///
        /// <paramref name="scratch"/> is the reusable <c>ZDO</c> accumulator for the paged
        /// <c>GetAllZDOsWithPrefabIterative</c> drain (it returns false until exhausted; we drain it
        /// fully — the decomp's own usage idiom, mirrored from the overlay's <c>BuildRows</c> and the
        /// core's former destination walk). Returns silently with an empty list when <c>ZDOMan</c> is
        /// not up yet (early boot / headless).
        ///
        /// 🔴 §2: on a dedicated server this is the client-held window only — the STAGING set L2 replaces.
        /// </summary>
        public static void Gather(Vector3 playerPos, List<ZDO> scratch, List<TwistedDestination> into)
        {
            into.Clear();

            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return;

            scratch.Clear();
            int index = 0;
            // Drain the paged walk fully (≤400 sectors/call; false until exhausted, then true on the
            // final outside-sector sweep) — the same idiom ResolveDestination + the overlay use.
            while (!zdoMan.GetAllZDOsWithPrefabIterative(TwistedPortal.PortalPieceName, scratch, ref index)) { }

            foreach (var z in scratch)
            {
                if (z == null) continue;
                string raw = z.GetString(SBPR_TwistedPortal.ZdoRuneName, string.Empty);
                // Censor on read (the core's ReadRuneName precedent) so a label / match can never use
                // un-filtered UGC even if a legacy ZDO stored raw bytes.
                string rune = string.IsNullOrEmpty(raw)
                    ? string.Empty
                    : CensorShittyWords.FilterUGC(raw, UGCType.Text, 0L);
                bool hasRune = !string.IsNullOrEmpty(rune);
                into.Add(new TwistedDestination(z.GetPosition(), z.GetRotation(), rune, hasRune, z.m_uid));
            }
        }

        /// <summary>
        /// Identify the ORIGIN portal — the Twisted Portal the player is standing on (Beat 1, the
        /// proximity-active state, spec §1 Q3). Returns the index into <paramref name="candidates"/> of
        /// the nearest portal within <paramref name="proximityRange"/> metres, or <c>-1</c> when the
        /// player is not on any portal (look-to-aim travel is unavailable off-portal). The origin is
        /// excluded from the destination aim-pick (you can't travel to the portal you're standing on).
        /// </summary>
        public static int FindOrigin(IReadOnlyList<TwistedDestination> candidates, Vector3 playerPos, float proximityRange)
        {
            int origin = -1;
            float bestSqr = proximityRange * proximityRange;
            for (int i = 0; i < candidates.Count; i++)
            {
                float d = (candidates[i].Position - playerPos).sqrMagnitude;
                if (d <= bestSqr)
                {
                    bestSqr = d;
                    origin = i;
                }
            }
            return origin;
        }
    }
}
