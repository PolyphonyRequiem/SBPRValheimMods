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
    /// from here, so the label you see highlighted is provably the portal you travel to (one source, no
    /// drift between the two surfaces).
    ///
    /// ════════════════════════════════════════════════════════════════════════════════════════════
    /// 🟢 L2 LANDED — SERVER-AUTHORITATIVE long-range candidate set (card t_ccb454f8, spec §2 REQUIRED).
    ///   <see cref="Gather"/> now PREFERS the server-authoritative directory (<see cref="TwistedPortalDirectory"/>):
    ///   the stepping client asks the SERVER (over a routed RPC) for the within-range slice of the FULL
    ///   Twisted-Portal set, the server walks its complete ZDO set (it holds every ZDO in the world —
    ///   ZDOMan.Load :64701-64713) and replies, and Gather reads that cache. So the picker now reaches
    ///   destinations PAST the client's ~64-128 m sync window (AT-PICK-LONGRANGE) — the §2 requirement.
    ///
    ///   The L1 client-side <c>GetAllZDOsWithPrefabIterative</c> walk (decomp :65497) is KEPT as the
    ///   FALLBACK (<see cref="GatherLocalWindow"/>): it serves the first ~1 s before the first server
    ///   response lands (instant on-placement feel), and is the whole answer in singleplayer / on a host
    ///   (where the local peer IS the server and already holds the full set) and if the directory RPC is
    ///   ever unavailable (graceful degradation — a stale/absent cache falls back to the window, never to
    ///   nothing). The SIGNATURE is unchanged from L1 — (player position, reusable scratch, reusable
    ///   output) → a flat <see cref="TwistedDestination"/> list — so the commit input + overlay callers
    ///   are untouched; L2 swapped only the BODY, exactly as L1 structured the seam to allow.
    /// ════════════════════════════════════════════════════════════════════════════════════════════
    ///
    /// Clean-side (ADR-0001): base-game <c>ZDOMan</c>/<c>ZDO</c>/<c>CensorShittyWords</c> + our own ZDO
    /// slot + our own routed-RPC directory only. Reads portal ZDOs, never writes them.
    /// </summary>
    public static class TwistedPortalCandidates
    {
        // ── Directory request throttle. The read path (Gather) is called every frame-throttle by BOTH
        //    consumers; we collectively fire at most one server REQUEST per this interval (shared static
        //    timer), and only when at least one Twisted Portal is in the local window (i.e. portals are
        //    RELEVANT here) — so a player nowhere near any portal generates zero directory traffic. ──
        private const float RequestInterval = 1.0f;
        private static float _nextRequest;

        // ── Cache staleness tolerance. Use the server cache's FAR portals while it's been refreshed
        //    within this window; if the server stops answering (disconnect mid-session) the cache goes
        //    stale and we coast on the local window alone. Generous vs the 1 s request cadence so a couple
        //    of dropped round-trips don't flap the source. ──
        private const float CacheStaleSeconds = 5.0f;

        // Reusable scratch for the cache copy (no per-refresh allocation). Gather is main-thread-only and
        // non-re-entrant across its two consumers, so a single static buffer is safe.
        private static readonly List<TwistedDestination> _cacheScratch = new List<TwistedDestination>();

        /// <summary>
        /// Fill <paramref name="into"/> with the look-to-aim candidate set as flat
        /// <see cref="TwistedDestination"/> rows (position + rotation + censored rune + id). Both lists
        /// are caller-owned reusables (cleared here) so a throttled refresh allocates nothing.
        ///
        /// 🟢 L2 (spec §2): the set is the UNION of (a) the always-current LOCAL WINDOW (the client's own
        /// ~64-128 m sync set — authoritative for the immediate vicinity, and the guarantee that the ORIGIN
        /// portal the player stands on is always present so <see cref="FindOrigin"/> never goes blind) and
        /// (b) the FAR portals from the server-authoritative directory cache that the window can't see
        /// (reaches past the client window — AT-PICK-LONGRANGE). Dedup is by ZDOID so a portal in both
        /// appears once (no duplicated origin masquerading as a 0 m destination).
        ///
        /// Union (not replace) is deliberate: the cache slice is centered on the player's position at the
        /// last throttled REQUEST, so right after a long teleport it is briefly centered on the OLD region
        /// and would not contain the new origin. Anchoring on the live local window keeps the origin +
        /// nearby set correct every frame; the cache only ever ADDS far destinations. A stale/absent cache
        /// (singleplayer before the self-dispatch, a dropped round-trip, the directory RPC unavailable)
        /// degrades cleanly to the local window alone.
        ///
        /// <paramref name="scratch"/> is the reusable <c>ZDO</c> accumulator for the local-window
        /// <c>GetAllZDOsWithPrefabIterative</c> drain. Returns silently with an empty list when
        /// <c>ZDOMan</c> is not up yet (early boot / headless).
        /// </summary>
        public static void Gather(Vector3 playerPos, List<ZDO> scratch, List<TwistedDestination> into)
        {
            into.Clear();

            // (1) Always compute the local window — the always-current nearby set, the origin guarantee,
            //     AND the cheap "are portals relevant here?" probe that gates directory traffic. (Existing
            //     L1 cost; unchanged.)
            GatherLocalWindow(playerPos, scratch, into);

            // (2) If any Twisted Portal is within the local window, keep the server directory warm: fire
            //     a throttled request for the within-range slice around the player. into.Count == 0 ⇒
            //     nothing relevant nearby ⇒ no request (a player far from any portal makes zero traffic).
            if (into.Count > 0 && Time.time >= _nextRequest)
            {
                _nextRequest = Time.time + RequestInterval;
                float requestRadius = Plugin.TwistedOverlayRadius?.Value ?? TwistedPortalOverlayModel.DefaultOverlayRadius;
                TwistedPortalDirectory.RequestSlice(playerPos, requestRadius);
            }

            // (3) UNION the FAR portals from the fresh server cache (those not already in the local
            //     window, by ZDOID) so the picker reaches destinations past the client window. A stale /
            //     absent cache leaves the local-window set from step (1) in place (the staging fallback).
            if (TwistedPortalDirectory.HasFreshCache(CacheStaleSeconds)
                && TwistedPortalDirectory.CopyCache(_cacheScratch))
            {
                for (int i = 0; i < _cacheScratch.Count; i++)
                {
                    TwistedDestination far = _cacheScratch[i];
                    if (!ContainsId(into, far.Id))
                        into.Add(far);
                }
            }
        }

        /// <summary>Linear ZDOID membership test over the (small) candidate set — used to dedup the
        /// server cache against the local window. The set is a handful of in-range portals, so linear is
        /// free and avoids a per-frame HashSet allocation.</summary>
        private static bool ContainsId(List<TwistedDestination> set, ZDOID id)
        {
            for (int i = 0; i < set.Count; i++)
                if (set[i].Id == id) return true;
            return false;
        }

        /// <summary>
        /// The L1 STAGING walk, retained as the L2 fallback: fill <paramref name="into"/> with every
        /// Twisted Portal THIS PEER currently holds (<c>GetAllZDOsWithPrefabIterative</c>, decomp :65497).
        /// 🔴 On a dedicated-server CLIENT this is only the ~64-128 m sync window (spec §2) — which is why
        /// <see cref="Gather"/> prefers the server directory cache. On a HOST / in singleplayer the local
        /// peer IS the server, so this walk already returns the FULL set and the directory round-trip just
        /// self-dispatches to the same data. Censors the rune on read (the core's ReadRuneName precedent).
        /// </summary>
        private static void GatherLocalWindow(Vector3 playerPos, List<ZDO> scratch, List<TwistedDestination> into)
        {
            into.Clear();

            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return;

            scratch.Clear();
            int index = 0;
            // Drain the paged walk fully (≤400 sectors/call; false until exhausted, then true on the
            // final outside-sector sweep) — the same idiom the server-side directory walk uses.
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
