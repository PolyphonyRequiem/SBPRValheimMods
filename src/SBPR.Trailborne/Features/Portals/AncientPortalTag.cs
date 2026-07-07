using UnityEngine;
using SBPR.Trailborne.Core.Portals;
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
    /// ARCH REVIEW P1 (Model A — ZDO-component seam): this tag now derives from
    /// <see cref="ZdoComponent"/> for the ghost-guarded, owner-gated ZDO access it used to
    /// hand-roll, and its grow-progress rule is the engine-free Core <see cref="PortalGrow"/>
    /// (unit-tested: unstamped / clock-not-up / mid-grow relog / past-window). The class keeps
    /// ONLY its feature state — the trigger-collider gate + the scale-lerp visual. The
    /// <c>SBPR_PortalPlantTime</c> ZDO key is UNCHANGED (R3 wire-contract).
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
    public class AncientPortalTag : ZdoComponent
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

        private Transform? triggerColliderHost;   // the TeleportWorldTrigger child whose collider we gate
        private Collider? triggerCollider;         // its BoxCollider (isTrigger) — toggled by grow state
        private bool activated;                    // latched true once grow completes (stops further work)
        private bool warnedNoTrigger;              // log-once guard if the trigger child is missing

        // Runs AFTER ZdoComponent.Awake resolves the ZNetView (NView). Feature setup only.
        protected override void OnZdoAwake()
        {
            // Resolve the overhead trigger child + its collider (built by Portals.cs). We gate
            // teleport by enabling/disabling THIS collider — see the class summary.
            var triggerTag = GetComponentInChildren<TeleportWorldTrigger>(includeInactive: true);
            if (triggerTag != null)
            {
                triggerColliderHost = triggerTag.transform;
                triggerCollider = triggerTag.GetComponent<Collider>();
            }

            // GHOST (no ZDO): leave everything inert — the placement preview must not stamp a
            // plant time or run the grow. (ZdoComponent.HasZdo is the ghost guard.)
            if (!HasZdo)
            {
                // Keep the trigger OFF on the ghost so a preview can never teleport.
                SetTeleportEnabled(false);
                return;
            }

            // Stamp plant time on FIRST owner wake (no stamp yet). Owner-write only; a
            // non-owner reads the stamp the owner wrote and grows in lockstep. TryGetLong
            // returns false when unstamped (the sentinel-free read replacing "== 0L").
            if (!TryGetLong(ZdoPlantTime, out _) && IsOwner)
            {
                long stamp = CurrentTicks();
                if (stamp != 0L)
                {
                    // WriteLong claims ownership first if needed (ZdoComponent → ZdoAccess policy).
                    WriteLong(ZdoPlantTime, stamp);
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
            if (!HasZdo) return;   // zone-unloaded mid-grow → wait

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
        /// Grow progress in [0,1] — delegates to the engine-free Core <see cref="PortalGrow.Progress"/>,
        /// reading the ZDO-stamped plant time (ghost-guarded) and the current network clock. All the
        /// edge-case discipline (unstamped → 0, clock-not-up → 0, mid-grow relog, past-window clamp)
        /// lives in the Core rule and is unit-tested there; this shell method just supplies the two
        /// live longs.
        /// </summary>
        private float ComputeProgress()
        {
            long stamp = ReadLong(ZdoPlantTime, 0L);   // ghost-guarded; 0 when ghost or unstamped
            long now = CurrentTicks();                 // 0 when ZNet clock isn't up
            return PortalGrow.Progress(stamp, now, GrowSeconds);
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
