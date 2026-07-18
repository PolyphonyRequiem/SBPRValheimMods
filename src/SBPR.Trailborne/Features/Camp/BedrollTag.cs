using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Camp
{
    /// <summary>
    /// Identity + interaction spine for the special bedroll (piece_sbpr_bedroll).
    ///
    /// WHY THIS OWNS THE INTERACTION (not the vanilla <see cref="Bed"/> component)
    /// -------------------------------------------------------------------------
    /// Verified against the decomp (assembly_valheim Bed.Interact :99592-99655): the
    /// vanilla bed's SLEEP branch (the AttachStart(isBed:true) at :99643) is only ever
    /// reached when the bed is ALREADY the player's current spawn point
    /// (owner==playerID AND IsCurrent()). Every path that gets you there first calls
    /// <c>PlayerProfile.SetCustomSpawnPoint</c> (:99613 on first-claim, :99651 on the
    /// "not current spawn point" re-claim). So there is NO vanilla code path that skips
    /// the night via a Bed WITHOUT also overwriting your respawn point.
    ///
    /// Daniel's lock (card t_439f2351, design §1.4): a trail nap must NOT overwrite your
    /// home respawn. Therefore the bedroll cannot route through Bed.Interact for sleep.
    /// Instead THIS MonoBehaviour is the <see cref="Interactable"/> on the piece: it
    /// reimplements the vanilla 5-gate sleep chain (time / enemies / exposure-relaxed /
    /// fire / wet) and drives <c>Player.AttachStart(..., isBed:true, "attach_bed")</c>
    /// directly. Setting the <c>s_inBed</c> ZDO flag (AttachStart :21263) is all
    /// <c>Game.EverybodyIsTryingToSleep</c> (:84716) needs to run SkipToMorning — the
    /// spawn point is never touched.
    ///
    /// Unity resolves the hover interactable via <c>GetComponentInParent&lt;Interactable&gt;()</c>
    /// (Player.FindHoverObject :19276). We therefore keep the vanilla <see cref="Bed"/>
    /// component present (so the piece is recognized as a bed structurally, and so a
    /// future migration could reuse it) but this tag is the FIRST-added Interactable and
    /// owns the E-press. Both live on the same GameObject; GetComponentInParent returns
    /// the first, which registration guarantees is this tag (added before the Bed's
    /// Interactable contract matters — see Bedroll.RegisterPrefabs ordering).
    ///
    /// Comfort: free vanilla <c>SE_Rested</c> rides the wake — Game.SleepStop
    /// (:84742) → Player.SetSleeping(false) (:21456) → AddStatusEffect(s_statusEffectRested)
    /// (:21464). No extra code. (Inspired is deferred per spec Q7 — not built yet.)
    ///
    /// Fail-open discipline: any null/edge in the gate chain refuses the nap (returns
    /// false) rather than sleeping in an unsafe state. Server-gated at registration.
    /// </summary>
    public class BedrollTag : MonoBehaviour, Hoverable, Interactable
    {
        private ZNetView nview = null!;   // Unity-injected via GetComponent in Awake
        private Piece piece = null!;      // Unity-injected via GetComponent in Awake
        private Bed bed = null!;          // the co-located vanilla Bed (spawn anchor only)

        private void Awake()
        {
            nview = GetComponent<ZNetView>();
            piece = GetComponent<Piece>();
            bed = GetComponent<Bed>();
        }

        public string GetHoverName()
        {
            return piece != null ? piece.m_name : "Bedroll";
        }

        public string GetHoverText()
        {
            string raw = $"{GetHoverName()}\n[<color=yellow><b>$KEY_Use</b></color>] $piece_bed_sleep";
            return Localization.instance != null ? Localization.instance.Localize(raw) : raw;
        }

        /// <summary>
        /// Bedroll nap: reimplements the vanilla Bed sleep-gate chain (Bed.Interact
        /// :99619-99643) EXCEPT the exposure cover-clause (relaxed by
        /// BedrollCheckExposurePatch) and the spawn-point set (deliberately omitted).
        /// On all gates passing, drives AttachStart(isBed:true) — the sleep vote then
        /// skips to morning via Game.EverybodyIsTryingToSleep. Never sets spawn.
        /// </summary>
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            var human = user as Player;
            if (human == null) return false;
            if (nview == null || !nview.IsValid()) return false;

            // Gate 1 — time (vanilla EnvMan.CanSleep :81139). Same $msg_cantsleep.
            if (!EnvMan.CanSleep())
            {
                human.Message(MessageHud.MessageType.Center, "$msg_cantsleep");
                return false;
            }

            // Gate 2 — no enemies (vanilla Bed.CheckEnemies :99667).
            if (human.IsSensed())
            {
                human.Message(MessageHud.MessageType.Center, "$msg_bedenemiesnearby");
                return false;
            }

            // Gate 3 — exposure. We call the SAME cover test the vanilla bed uses, but
            // apply the RELAXED rule: underRoof still required (Q6 — no open-sky sleep),
            // the 0.8 cover clause dropped (the tent canopy gives ~0.47). This mirrors
            // BedrollCheckExposurePatch's rule so the bedroll behaves identically whether
            // sleep is driven here or (defensively) through any Bed path.
            Vector3 spawnPoint = bed != null && bed.m_spawnPoint != null
                ? bed.m_spawnPoint.position
                : transform.position;
            Cover.GetCoverForPoint(spawnPoint, out float coverPercentage, out bool underRoof);
            if (!underRoof)
            {
                human.Message(MessageHud.MessageType.Center, "$msg_bedneedroof");
                return false;
            }
            // NOTE: intentionally NOT refusing on coverPercentage < 0.8 — the relax.
            _ = coverPercentage;

            // Gate 4 — near a (burning) fire. Vanilla Bed.CheckFire :99694 tests the
            // piece origin against a Heat EffectArea; the covered camp fire supplies it.
            if (!EffectArea.IsPointInsideArea(transform.position, EffectArea.Type.Heat))
            {
                human.Message(MessageHud.MessageType.Center, "$msg_bednofire");
                return false;
            }

            // Gate 5 — not wet. Vanilla Bed.CheckWet :99657. Under the canopy the player
            // is dry; step off into the rain and this refuses (AT-BEDROLL-WET).
            if (human.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet))
            {
                human.Message(MessageHud.MessageType.Center, "$msg_bedwet");
                return false;
            }

            // All gates pass. Drive the in-bed attach WITHOUT setting spawn. Mirror the
            // vanilla attach args (Bed.Interact :99643): hide weapons, isBed:true so the
            // s_inBed ZDO flag is set and the all-asleep vote (Game.EverybodyIsTryingToSleep
            // :84716) can fire SkipToMorning. detachOffset matches vanilla's (0, 0.5, 0).
            Transform attach = bed != null && bed.m_spawnPoint != null ? bed.m_spawnPoint : transform;
            human.AttachStart(attach, gameObject, hideWeapons: true, isBed: true,
                onShip: false, "attach_bed", new Vector3(0f, 0.5f, 0f));
            return false;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
