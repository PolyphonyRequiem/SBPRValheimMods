using System;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Portals
{
    /// <summary>
    /// Per-instance MonoBehaviour on the placed Ancient Portal. Owns the plant→grow→
    /// activate lifecycle (spec §3.6, AT-GROW): a freshly-placed seed is INERT (cannot
    /// teleport) and visibly scale-lerps from a small seed to the full ~3 m envelope over
    /// ~15 s, then activates ONCE. The plant time is ZDO-stamped with the persistent
    /// network wall-clock so the grow resumes correctly after a relog mid-grow (it's
    /// absolute world-time, not session-relative).
    ///
    /// Mirrors the cairn's <see cref="SBPR.Trailborne.Features.Cairns.CairnTag"/> owner-write
    /// ZDO discipline: every ZDO read/write guards on a live ZDO so the placement GHOST
    /// (no ZDO) is a no-op, and the first owner write claims ownership.
    ///
    /// 🔴 THE GROW GATE (spec §3.6/§3.7 — the load-bearing detail): while growing we keep
    /// the teleport DISABLED by toggling the TeleportWorldTrigger child's COLLIDER, NOT the
    /// TeleportWorld component. The decomp (assembly_valheim:123144-151) shows
    /// TeleportWorldTrigger.OnTriggerEnter calls m_teleportWorld.Teleport(player) DIRECTLY —
    /// it never checks TeleportWorld.enabled — so disabling the TeleportWorld component would
    /// NOT block a jump-through teleport. The trigger collider is the correct gate: no
    /// collider events fire while it's disabled, so a half-grown portal cannot teleport.
    ///
    /// Client + server both run this (it's a ZNetView MonoBehaviour on the placed piece).
    /// The scale-lerp is cosmetic but harmless on the headless server; the trigger-gate +
    /// the owner-write plant-stamp are the load-bearing parts and run wherever the ZDO is
    /// owned. Inert on the ghost (no ZDO).
    /// </summary>
    public class AncientPortalTag : MonoBehaviour
    {
        // 🔒 LOCKED ZDO key (save/wire contract — never rename; a rename re-grows every
        // placed portal from scratch because the old stamp orphans). Stores the plant time
        // as ZNet network-clock Ticks (a long). Spec §3.6.
        public const string ZdoPlantTime = "SBPR_PortalPlantTime";

        // Grow duration, seconds (spec §0/§3.6 — Daniel's ~15 s). The piece is inert and
        // scale-lerps over this window, then activates once.
        public const float GrowSeconds = 15f;

        // Seed (start) scale as a fraction of full (spec §3.6 — "seedScale ≈ fullScale * 0.1").
        private const float SeedScaleFraction = 0.1f;

        // Poll cadence for the grow lerp. The cairn polls at 1 Hz; the grow wants a smoother
        // scale ramp, so 0.1 s (spec §3.6 suggests ~0.25 s or an Update lerp — 0.1 s reads
        // smooth without meaningful cost, and the lerp is pure-local cosmetic math). The
        // owner-write of the plant stamp happens ONCE on first wake, not per poll.
        private const float PollSeconds = 0.1f;

        private ZNetView? nview;
        private Transform? triggerColliderHost;   // the TeleportWorldTrigger child whose collider we gate
        private Collider? triggerCollider;         // its BoxCollider (isTrigger) — toggled by grow state
        private bool activated;                    // latched true once grow completes (stops further work)
        private bool warnedNoTrigger;              // log-once guard if the trigger child is missing

        private void Awake()
        {
            nview = GetComponent<ZNetView>();

            // Resolve the overhead trigger child + its collider (built by Portals.cs). We gate
            // teleport by enabling/disabling THIS collider — see the class summary.
            var triggerTag = GetComponentInChildren<TeleportWorldTrigger>(includeInactive: true);
            if (triggerTag != null)
            {
                triggerColliderHost = triggerTag.transform;
                triggerCollider = triggerTag.GetComponent<Collider>();
            }

            // GHOST (no ZDO): leave everything inert — the placement preview must not stamp a
            // plant time or run the grow. The cairn discipline (CairnTag guards on a live ZDO).
            if (nview == null || nview.GetZDO() == null)
            {
                // Keep the trigger OFF on the ghost so a preview can never teleport.
                SetTeleportEnabled(false);
                return;
            }

            // Stamp plant time on FIRST owner wake (no stamp yet). Owner-write only; a
            // non-owner reads the stamp the owner wrote and grows in lockstep.
            long stamp = nview.GetZDO().GetLong(ZdoPlantTime, 0L);
            if (stamp == 0L && nview.IsOwner())
            {
                stamp = CurrentTicks();
                if (stamp != 0L)
                {
                    if (!nview.IsOwner()) nview.ClaimOwnership();
                    nview.GetZDO().Set(ZdoPlantTime, stamp);
                }
            }

            // Start inert: teleport gated OFF until grow completes (set seed scale immediately
            // so there's no one-frame full-size pop before the first poll).
            SetTeleportEnabled(false);
            ApplyGrowVisual(ComputeProgress());

            // Poll the grow. If we're already past the window on wake (e.g. relog after the
            // 15 s elapsed), the first tick activates immediately.
            InvokeRepeating(nameof(GrowTick), PollSeconds, PollSeconds);
        }

        /// <summary>
        /// Grow poll: lerp the visual scale toward full and, once the window elapses, enable
        /// teleport ONCE and stop polling. Fails safe — any error leaves the portal in its
        /// current (inert until proven grown) state rather than half-activating.
        /// </summary>
        private void GrowTick()
        {
            if (activated) { CancelInvoke(nameof(GrowTick)); return; }
            if (nview == null || nview.GetZDO() == null) return;   // zone-unloaded mid-grow → wait

            float t = ComputeProgress();
            ApplyGrowVisual(t);

            if (t >= 1f)
            {
                activated = true;
                ApplyGrowVisual(1f);          // snap to exact full scale
                SetTeleportEnabled(true);     // the portal goes live (trigger collider on)
                CancelInvoke(nameof(GrowTick));
            }
        }

        /// <summary>
        /// Grow progress in [0,1] from the ZDO-stamped plant time vs the current network
        /// clock. An UNSTAMPED portal reads as freshly-planted (0 → seed-scaled + inert): the
        /// owner stamps the time in its own Awake, and a non-owner momentarily sees stamp==0
        /// only until the stamp propagates, so it must wait — NOT prematurely activate. A
        /// clock that isn't up yet also returns 0 (stay seed, never activate on a bad read).
        /// Because the placer always owns a freshly-placed piece and always writes the stamp,
        /// "unstamped forever" can't happen, so failing toward 0 here never strands a portal.
        /// </summary>
        private float ComputeProgress()
        {
            if (nview == null || nview.GetZDO() == null) return 0f;
            long stamp = nview.GetZDO().GetLong(ZdoPlantTime, 0L);
            if (stamp == 0L) return 0f;       // not stamped yet (non-owner before owner write) — wait, don't activate
            long now = CurrentTicks();
            if (now == 0L) return 0f;         // clock not up — stay seed-scaled, don't activate
            double elapsedSec = (now - stamp) / (double)TimeSpan.TicksPerSecond;
            if (elapsedSec <= 0d) return 0f;
            return Mathf.Clamp01((float)(elapsedSec / GrowSeconds));
        }

        /// <summary>
        /// Apply the grow scale to the piece's VISUAL. We scale a dedicated visual root (the
        /// grafted-art parent built by Portals.cs, named <c>SBPR_AncientPortalVisual</c>) rather
        /// than the piece transform itself, so the ZNetView/collider root stays at unit scale
        /// for the placement + networking systems. If that root isn't found we fall back to
        /// scaling our own transform (degraded but functional).
        /// </summary>
        private void ApplyGrowVisual(float t)
        {
            float s = Mathf.Lerp(SeedScaleFraction, 1f, t);
            var visual = transform.Find(Portals.VisualRootName);
            if (visual != null) visual.localScale = new Vector3(s, s, s);
            else transform.localScale = new Vector3(s, s, s);
        }

        /// <summary>
        /// Gate teleport by toggling the overhead trigger collider (NOT TeleportWorld.enabled —
        /// see the class summary: OnTriggerEnter calls Teleport directly, bypassing the
        /// component-enabled flag). Disabling the collider stops trigger events, so no
        /// jump-through can fire mid-grow. We also toggle the host GameObject active state as a
        /// belt-and-braces second gate.
        /// </summary>
        private void SetTeleportEnabled(bool on)
        {
            if (triggerCollider != null)
            {
                triggerCollider.enabled = on;
            }
            else if (triggerColliderHost != null && triggerColliderHost.gameObject.activeSelf != on)
            {
                triggerColliderHost.gameObject.SetActive(on);
            }
            else if (!warnedNoTrigger && triggerColliderHost == null)
            {
                warnedNoTrigger = true;
                Plugin.Log.LogWarning(
                    "[Trailborne/Portals] AncientPortalTag: no TeleportWorldTrigger child found to gate; " +
                    "the portal cannot block teleport mid-grow (it will still grow visually). Check Portals.cs " +
                    "trigger construction.");
            }
        }

        /// <summary>Current network wall-clock in Ticks (the persistent clock vanilla uses for
        /// timed world state), or 0 when ZNet isn't up yet. Spec §3.6 — relog-durable because
        /// it's absolute world-time, not session-relative.</summary>
        private static long CurrentTicks()
        {
            if (ZNet.instance == null) return 0L;
            return ZNet.instance.GetTime().Ticks;
        }
    }
}
