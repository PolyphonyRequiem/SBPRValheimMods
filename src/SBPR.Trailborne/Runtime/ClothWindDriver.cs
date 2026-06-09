using UnityEngine;

namespace SBPR.Trailborne.Runtime
{
    // REUSABLE — feature-agnostic wind driver.
    //
    // Drives a UnityEngine.Cloth from the world wind so a cloth GameObject streams with
    // wind DIRECTION and FORCE instead of only shader-waggling in place. Cairn-banner is
    // its FIRST consumer, but nothing here is cairn-specific — attach it next to any Cloth
    // (future tents, flags, sails) and it just works.
    //
    // Vanilla parity (fair-game, lifted directly from the base game we mod — NOT a clean-room
    // concern; clean-room is a firewall around OTHER mods' code, per AGENTS.md / ADR-0001):
    //   • GlobalWind.UpdateWind() cloth branch — assembly_valheim.decompiled.cs:30383-30392:
    //         Vector3 v = EnvMan.instance.GetWindForce();
    //         m_cloth.externalAcceleration = v * m_multiplier;
    //         m_cloth.randomAcceleration   = v * m_multiplier * m_clothRandomAccelerationFactor;
    //     (m_multiplier default 1f; m_clothRandomAccelerationFactor default 0.5f).
    //   • GlobalWind.Start() cadence — :30344:
    //         InvokeRepeating("UpdateWind", Random.Range(1.5f, 2.5f), 2f)  + one immediate call.
    //     (NOT per-frame — the wind vector changes slowly, so a ~2 s poll is plenty and costs
    //      ~nothing. AC4: no per-frame perf regression with many cairns in view. The 0.01 s
    //      per-frame branch at :30341 is vanilla's SMOOTH-cloth path; we use the 2 s one.)
    //   • EnvMan.GetWindForce() = GetWindDir() * m_wind.w  — :81333  (direction × intensity,
    //     intensity 0.05–1.0), so a single vector carries BOTH direction and force.
    //
    // GlobalWind is a plain MonoBehaviour (not a ZNetView, not networked) and so is this —
    // wind response is per-client cosmetic physics off the already-synced global wind vector;
    // it does NOT need to be ZDO-deterministic across clients (same as every vanilla cloth).
    //
    // Server/headless: a dedicated server has no Cloth (the cloth graft is client-only art),
    // so this no-ops cleanly — GetComponent<Cloth>() returns null and every tick early-returns.
    public sealed class ClothWindDriver : MonoBehaviour
    {
        // Public knobs (vanilla GlobalWind defaults). Set these BEFORE the object's Start()
        // runs (i.e. right after AddComponent, while the GameObject is still inactive) to
        // tune per-consumer; the cairn banner sets its own values in CairnTag.BuildBanner.
        public float Multiplier = 1f;            // GlobalWind.m_multiplier default
        public float RandomFactor = 0.5f;        // GlobalWind.m_clothRandomAccelerationFactor default

        // Optional: zero the wind while the cloth's owner is sheltered (vanilla capes do this
        // via m_checkPlayerShelter). Cairns are open-air trail markers, so the cairn banner
        // leaves this false — wired through for reuse by sheltered cloth (tent flaps, etc.).
        public bool CheckPlayerShelter = false;

        private Cloth _cloth = null!;            // resolved in Start; may be null on headless
        private Player? _player;                 // only used when CheckPlayerShelter

        private void Start()
        {
            // Mirror GlobalWind.Start: bail if the world environment isn't up yet (also the
            // headless case — EnvMan exists server-side but there's no Cloth to drive there).
            if (EnvMan.instance == null)
                return;

            _cloth = GetComponent<Cloth>();
            if (_cloth == null)
                return; // no cloth (e.g. headless server) — nothing to drive, stay inert.

            if (CheckPlayerShelter)
                _player = GetComponentInParent<Player>();

            // Same cadence vanilla uses for non-smooth cloth: a randomized first fire so a
            // field of cairns doesn't update in lockstep, then every 2 s. One immediate call
            // so the banner isn't dead-limp for up to ~2.5 s after it spawns.
            InvokeRepeating(nameof(UpdateWind), Random.Range(1.5f, 2.5f), 2f);
            UpdateWind();
        }

        private void UpdateWind()
        {
            if (_cloth == null || EnvMan.instance == null)
                return;

            // direction × intensity — a single vector carrying both heading and force.
            Vector3 wind = EnvMan.instance.GetWindForce();

            if (CheckPlayerShelter && _player != null && _player.InShelter())
                wind = Vector3.zero;

            _cloth.externalAcceleration = wind * Multiplier;
            _cloth.randomAcceleration = wind * Multiplier * RandomFactor;
        }
    }
}
