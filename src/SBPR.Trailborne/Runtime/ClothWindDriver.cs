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

        // ── DIRECTIONAL ALIGNMENT (card t_1d7c0d19 — the windsock fix all prior attempts omitted) ──
        //
        // Vanilla cloth streams downwind because GlobalWind.UpdateWind() ROTATES THE WHOLE CLOTH
        // TRANSFORM to face the wind, THEN the Cloth solver adds ripple on top
        // (assembly_valheim.decompiled.cs:30348-30392 — the m_alignToWindDirection branch). Our
        // driver previously copied ONLY the cloth-force branch (externalAcceleration), so the
        // banner force-jittered (waggle ∝ intensity) but never ORIENTED — exactly Daniel's
        // "waggles in place, never streams downwind" symptom. This is the missing rotation.
        //
        // OFF by default so this stays feature-agnostic for other consumers (sails already
        // self-rotate via their own GlobalWind; future tents may not want it). The cairn banner
        // turns it on for the Option-A (Cloth windsock) color branch only.
        public bool AlignToWindDirection = false;

        // 🔴 THE PIVOT/AXIS TRAP. Vanilla LookRotation(windDir, up) aligns the transform's +Z to
        // the wind, but our banner cloth mesh is a flat Y-Z sheet: Y = drop (gravity, hangs from
        // the pinned mount), Z = width, X = the zero-thickness sheet NORMAL. A blind vanilla port
        // would point the WIDTH edge into the wind AND pitch the sheet, re-creating the "spins/
        // zigzags wrong" look. The correct windsock orientation is a YAW about world-up only, so
        // the pinned mount stays horizontal and the drop stays vertical while the sheet turns to
        // face the wind. Because two prior in-world ships were wrong, the axis is SELECTABLE so
        // Daniel can prototype it live instead of us hardcoding a third guess:
        //
        //   AlignMode 0 = StreamYaw  (DEFAULT): LookRotation(flatWindDir, up) — mesh +Z (WIDTH)
        //       points along the horizontal wind; the sheet's PLANE (Y drop × Z width) contains
        //       the wind, so the hanging tail can swing downwind IN-plane (flag/windsock stream).
        //       The normal (X) faces crosswind; the drop (Y) stays vertical. Pure yaw, no pitch.
        //   AlignMode 1 = FaceYaw: StreamYaw rotated 90° in yaw — the BROAD FACE (normal X) is
        //       presented to the wind (sail-into-wind / billow read) instead of streaming in-plane.
        //   AlignMode 2 = VanillaFull: the literal vanilla LookRotation(windDir, up) including the
        //       vertical wind component (pitches the sheet) — kept only as a faithful reference for
        //       the in-world comparison; NOT expected to read as a clean windsock on our axes.
        public int AlignMode = 0;

        private Cloth _cloth = null!;            // resolved in Start; may be null on headless / Option B
        private Player? _player;                 // only used when CheckPlayerShelter

        private void Start()
        {
            // Mirror GlobalWind.Start: bail if the world environment isn't up yet (also the
            // headless case — EnvMan exists server-side but there's no Cloth to drive there).
            if (EnvMan.instance == null)
                return;

            _cloth = GetComponent<Cloth>();
            // No cloth AND not aligning → nothing to do (e.g. headless server, where the cairn
            // banner is never even built). The alignment branch is independent of the Cloth, so
            // a future align-only consumer (a static mesh that just yaws to the wind) still ticks.
            if (_cloth == null && !AlignToWindDirection)
                return;

            if (CheckPlayerShelter)
                _player = GetComponentInParent<Player>();

            // Same cadence vanilla uses for non-smooth cloth: a randomized first fire so a
            // field of cairns doesn't update in lockstep, then every 2 s. One immediate call
            // so the banner isn't dead-limp / mis-aimed for up to ~2.5 s after it spawns. Wind
            // direction changes slowly in Valheim, so a ~2 s align poll is vanilla-faithful and
            // costs ~nothing (AC4 — no per-frame perf regression with many cairns in view).
            InvokeRepeating(nameof(UpdateWind), Random.Range(1.5f, 2.5f), 2f);
            UpdateWind();
        }

        private void UpdateWind()
        {
            if (EnvMan.instance == null)
                return;

            // Directional alignment FIRST (vanilla order): rotate the whole transform so the
            // sheet faces the wind, THEN let the cloth forces add ripple on top. Independent of
            // the Cloth — runs even when _cloth is null (align-only consumers).
            if (AlignToWindDirection)
                ApplyWindAlignment();

            if (_cloth == null)
                return;

            // direction × intensity — a single vector carrying both heading and force.
            Vector3 wind = EnvMan.instance.GetWindForce();

            if (CheckPlayerShelter && _player != null && _player.InShelter())
                wind = Vector3.zero;

            _cloth.externalAcceleration = wind * Multiplier;
            _cloth.randomAcceleration = wind * Multiplier * RandomFactor;
        }

        // Rotate the transform to face the wind per AlignMode (see the AlignMode field doc for the
        // axis reconciliation). Guards the dead-calm degeneracy: at ~zero wind GetWindDir() is
        // unstable, and a wind blowing straight up/down has no horizontal heading — in either case
        // we KEEP the last orientation rather than snap to a default, so a calm banner doesn't
        // twitch to a fixed heading. Vanilla parity: GlobalWind sets transform.rotation directly.
        private void ApplyWindAlignment()
        {
            // Intensity floor: GetWindForce() = GetWindDir() × intensity (intensity ~0.05–1.0),
            // so a near-zero magnitude means dead calm with an unstable direction — keep last.
            if (EnvMan.instance.GetWindForce().sqrMagnitude < 1e-4f)
                return;

            Vector3 dir = EnvMan.instance.GetWindDir();

            if (AlignMode == 2)
            {
                // Faithful vanilla port (includes the vertical component → pitches the sheet).
                if (dir.sqrMagnitude < 1e-6f) return;
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                return;
            }

            // Modes 0/1 are a PURE YAW: flatten the wind to the horizontal plane so the drop (Y)
            // stays vertical and only the heading turns. Degenerate when the wind is ~vertical.
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude < 1e-4f)
                return; // wind near-vertical → no stable heading; keep last.
            flat.Normalize();

            // StreamYaw (0): mesh +Z (width) along the wind → sheet plane contains the wind, tail
            // streams downwind in-plane. FaceYaw (1): rotate that 90° so the broad face (normal X)
            // is presented to the wind instead. The sheet is visually symmetric front/back, so the
            // 90° sign only mirrors the (border) design, not the windward read.
            Quaternion yaw = Quaternion.LookRotation(flat, Vector3.up);
            if (AlignMode == 1)
                yaw *= Quaternion.AngleAxis(90f, Vector3.up);

            transform.rotation = yaw;
        }
    }
}
