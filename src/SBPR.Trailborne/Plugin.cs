using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Runtime;
using SBPR.Trailborne.Features.Cairns;

namespace SBPR.Trailborne
{
    [BepInPlugin(ModId, ModName, ModVersion)]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string ModId      = "net.danielgreen.sbpr.trailborne";
        public const string ModName    = "SBPR Trailborne";
        // Single source of truth = <Version> in SBPR.Trailborne.csproj. The
        // GenerateVersionConstant MSBuild target emits GeneratedModVersion from it
        // before compile; this alias keeps the [BepInPlugin] attribute (which needs
        // a compile-time const) in sync with the assembly + modpack-zip version.
        // Fixes the drift where this was hand-pinned at "0.1.0" for five releases.
        public const string ModVersion = GeneratedModVersion;

        internal static ManualLogSource Log = null!;   // set in Awake (BepInEx guarantees Awake before any patch fires)
        internal static string PluginFolder = null!;    // set in Awake
        private  Harmony harmony = null!;               // set in Awake

        // ── Config ──────────────────────────────────────────────────
        // Shift+E debug-damage flag. When true, Shift+E on a pristine cairn
        // (≥75% HP) drops it to 70% so the playtester can drive the
        // repair/upgrade combo gesture without waiting for weather decay.
        // Default true for v0.1.0 — flip false (or delete in v0.2.0) once
        // real decay has been tuned.
        internal static ConfigEntry<bool> DebugCairnDamage = null!;   // set in Awake via Config.Bind

        // Cairn TIME-decay rate, HP lost per in-game day, shared by the resident ticker
        // (CairnTag) and the out-of-zone backfill (CairnPatches) via Cairns.DecayHpPerDay.
        // Default 10 HP/day vs the 100 HP cairn ⇒ a ~10-day life if never repaired. Lifted
        // out of the backfill's old local literal so v0.2.0 can tune decay without a recompile.
        internal static ConfigEntry<float> CairnDecayHpPerDay = null!;   // set in Awake via Config.Bind

        // ── Cairn BANNER windsock tunables (card t_4a4a9706) ──────────────────────────
        // The banner's wind look is a pure CLIENT visual that CANNOT be verified headless
        // or in CI — it has shipped wrong in-world twice while building 0/0. So every knob
        // that shapes the windsock feel is LIVE config: Daniel tunes them on a joined client
        // (BepInEx ConfigurationManager / edit-the-.cfg-and-reload) in ONE session, then we
        // bake the chosen metres into §A2.1b. Defaults live as CairnTag.Default* consts
        // (single source of truth) so a no-Plugin unit context still resolves a sane value.
        internal static ConfigEntry<float> BannerDropY            = null!;  // tail length, m
        internal static ConfigEntry<float> BannerWidthZ           = null!;  // ribbon width, m
        internal static ConfigEntry<float> BannerMountHeight      = null!;  // pinned mount height above the pile crown, m
        internal static ConfigEntry<float> BannerOffsetXZ         = null!;  // lateral nudge off the pile centre, m
        internal static ConfigEntry<float> BannerWindMult         = null!;  // directional wind multiplier
        internal static ConfigEntry<float> BannerWindRandomFactor = null!;  // omnidirectional jitter factor
        internal static ConfigEntry<float> BannerClothDamping     = null!;  // Cloth.damping
        internal static ConfigEntry<float> BannerStretchStiffness  = null!;  // Cloth.stretchingStiffness (card t_a2fc3073)
        internal static ConfigEntry<float> BannerBendStiffness     = null!;  // Cloth.bendingStiffness (card t_a2fc3073)
        internal static ConfigEntry<float> BannerClothFreeDistance= null!;  // max tail travel (cloth units)
        internal static ConfigEntry<float> BannerFreeRampExp      = null!;  // freedom ramp exponent (mount→tail)
        internal static ConfigEntry<float> BannerPinBandFrac      = null!;  // mount pin band, fraction of Y-span
        internal static ConfigEntry<bool>  BannerUseGravity       = null!;  // free-fall slack on build
        internal static ConfigEntry<int>   BannerSubdivisions     = null!;  // midpoint subdivisions of the cloth mesh (0=donor, 1=4×, 2=16×)
        internal static ConfigEntry<bool>  BannerRockDrape        = null!;  // sphere colliders approximating the pile so the cloth drapes on the stones
        internal static ConfigEntry<float> BannerTiltDegrees      = null!;  // tilt the mount toward horizontal (flagpole-flag mount)
        // ── A/B harness — Option A directional alignment (card t_1d7c0d19) ──
        internal static ConfigEntry<bool>  BannerAlignToWind      = null!;  // Option A: orient the windsock to the wind (the directional fix)
        internal static ConfigEntry<int>   BannerAlignMode        = null!;  // Option A: which axis maps to the wind (0=StreamYaw, 1=FaceYaw, 2=VanillaFull)
        // ── DIAGNOSTIC (card t_7de074f3 — ATTEMPT #6 Step 1) ──
        internal static ConfigEntry<bool>  BannerDiagnostic       = null!;  // attach the BannerDiagnostic runtime probe (default ON for this diagnostic build)

        // ── Cartography: NoMap enforcement (card t_2f9fc470, impl-spec §3.5.3) ──
        // The escape hatch for the mod-owned global-map disable. Default ON (enforced):
        // the mod sets GlobalKeys.NoMap server-side at world load so the cartography tier
        // (Surveyor's Table / Local Map / Cartographer's Kit) is the only map. Set false
        // ONLY to let the vanilla global map coexist (debug / non-cartography server). The
        // Mistlands tier advancement lifts NoMap independently of this flag. Read by
        // NoMapEnforcer.ShouldEnforceNoMap; the boot-log fires either way (the honesty rule).
        internal static ConfigEntry<bool>  EnforceNoMap           = null!;  // set in Awake via Config.Bind

        // ── v3 Swamp: Sunstone Lens (solar-charged detection trinket, card t_2fd7bc7f) ──
        // The charge economy + detection are LIVE config so Daniel can tune the "top up in the
        // open, spend in the dark" rhythm on a joined client without a rebuild (the design
        // note's open "charge economy" knob). Nullable + ?.Value-accessed from the feature so a
        // no-Plugin unit context falls back to SunstoneLens.Default* consts (single source of
        // truth). Bound in Awake.
        internal static ConfigEntry<float>? LensMaxCharge     = null;  // battery capacity (durability max)
        internal static ConfigEntry<float>? LensDrainPerSec   = null;  // ↓ charge/sec while worn & not charging
        internal static ConfigEntry<float>? LensChargePerSec  = null;  // ↑ charge/sec while in the sun
        internal static ConfigEntry<float>? LensDetectRadius  = null;  // metres; hostiles within are revealed
        internal static ConfigEntry<float>? LensDetectInterval = null; // seconds between detection sweeps
        // Optional clear-weather env-name allowlist (comma-separated config → this set). EMPTY
        // (default) means the recharge gate is driven purely by EnvSetup.m_isWet + IsDaylight()
        // (the env names are authored in Unity assets, not the decomp, so we don't hardcode them).
        // A server MAY pin specific clear env names to exclude a dry-but-dim weather.
        internal static readonly System.Collections.Generic.HashSet<string> LensClearWeatherNames =
            new System.Collections.Generic.HashSet<string>();
        internal static ConfigEntry<string>? LensClearWeatherNamesRaw = null;

        // ── v3 Swamp: Sunstone Lens WORLD-SPACE eidetic halo render (card t_68672b6b → t_d17d9b58) ──
        // The detection render surface is now a world-space head-halo of billboarded creature trophies
        // floating around the player (supersedes the screen-space ring). All halo geometry/feel is LIVE
        // config so Daniel converges the look on a joined client without a rebuild (the banner-windsock /
        // can't-verify-headless rule the cairn banner + compass use). Nullable + ?.Value-accessed from
        // SunstoneWorldRing / SunstoneLensHudOverlay so a no-Plugin unit context falls back to its
        // Default* consts (single source of truth). Bound in Awake. The old screen-space knobs
        // (RingRadiusPx / RingCenterOffsetY / RingIconMinPx / RingIconMaxPx) are REMOVED with the radar.
        internal static ConfigEntry<float>? LensHaloRadius        = null;  // FIXED halo radius (m) — every trophy is equidistant from the eye (no range-dependent push)
        internal static ConfigEntry<float>? LensHaloScaleMax      = null;  // trophy world-scale at FULL scale (enemy ≤10m → "1.0"); edge scale is derived (0.25×, the 10m knee)
        internal static ConfigEntry<float>? LensHaloEyeOffsetY    = null;  // lift the halo plane off the eye-point (clear the crosshair)
        internal static ConfigEntry<int>?   LensRingMaxIcons       = null;  // cap on simultaneous trophies (horde guard, pooled nearest-N)
        internal static ConfigEntry<bool>?  LensRingShowEmpty      = null;  // REPURPOSED (card t_9d7c3dfe): master on/off for the world-space sun-corona disc (was the flat ring)
        internal static ConfigEntry<bool>?  LensRingShowDepletedHint = null; // faint ring when depleted (default off)
        internal static ConfigEntry<bool>?  LensRingDebugText      = null;  // legacy text readout as a debug aid

        // ── v3 Swamp: Sunstone Lens empty-state → WORLD-SPACE pulsing sun-corona disc (card t_9d7c3dfe) ──
        // The empty-state affordance graduated from a flat screen-space ring to a glowing world-space
        // sun-corona disc that breathes on a slow alpha pulse — the substrate the trophy halo orbits.
        // Every visual knob is LIVE config so Daniel converges the look on a joined client without a
        // rebuild (the banner-windsock pattern; a client visual can't be verified headless). Nullable +
        // ?.Value-accessed from SunstoneLensHudOverlay/SunstoneCoronaDisc so a no-Plugin unit context
        // falls back to SunstoneCoronaDisc.Default* consts (single source of truth). Bound in Awake.
        // CoronaOrientation is a LIVE enum (GroundPlane "sun on the floor" default ↔ CameraFacing
        // billboard) — the LensMinimapHandoffMode live-enum idiom. The pulse ENVELOPE shape is gated by
        // the engine-free SunstoneCoronaPulse (AT-CORONA-PULSE-MATH); these knobs feed it the rate/depth.
        internal static ConfigEntry<SBPR.Trailborne.Features.Sunstone.CoronaOrientation>? LensCoronaOrientation = null;
        internal static ConfigEntry<float>? LensCoronaPulseHz      = null;  // breaths/sec (0 = steady glow)
        internal static ConfigEntry<float>? LensCoronaAlphaTrough  = null;  // alpha at the breath trough
        internal static ConfigEntry<float>? LensCoronaAlphaPeak    = null;  // alpha at the breath peak
        internal static ConfigEntry<float>? LensCoronaInnerFill    = null;  // 0 = thin hoop ↔ 1 = filled sun-disc
        internal static ConfigEntry<float>? LensCoronaThickness    = null;  // soft radiant-edge falloff width
        internal static ConfigEntry<float>? LensCoronaRadius       = null;  // world-m rim radius (default-tracks HaloRadius)
        internal static ConfigEntry<float>? LensCoronaPlaneOffsetY = null;  // vertical lift off the anchor (nudge to feet)
        // Default-ON startup dump (card t_d17d9b58 Knob #3c): at ZNetScene-ready, enumerate all Character
        // prefabs, resolve each → (own trophy | remap-sibling | none), and log the "none" set as ONE
        // reviewable block so Daniel can grow SunstoneProjection._trophyRemap over time.
        internal static ConfigEntry<bool>?  LensDumpUnmappedCreatures = null;
        // Diagnostic-logging gate (t_d5949685 HUD-render bug, the compass-shared self-deactivating-host
        // pump). When ON, emit the overlay MOUNT + visibility transitions + first-show placement so a
        // client LogOutput.log splits "mount/pump fail" from "on-screen but empty." Default ON for the
        // diagnostic cut; bake to false once Daniel confirms the ring renders in-game.
        internal static ConfigEntry<bool>?  LensRingDebugMount     = null;

        // ── v3 Swamp: Sunstone Lens → minimap handoff (card t_91e86951) ──
        // Two LIVE enums so Daniel converges the feel on a joined client without a rebuild (the
        // banner-windsock pattern). MinimapHandoffMode: where Lens threats render when a minimap is
        // present (DiscWhenBound = ring hides + minimap shows threats, default; RingOnly = escape hatch;
        // Both = supplement). MinimapBlipStyle: dots+tint (default) vs trophy art on the minimap surfaces.
        internal static ConfigEntry<SBPR.Trailborne.Features.Sunstone.MinimapHandoffMode>? LensMinimapHandoffMode = null;
        internal static ConfigEntry<SBPR.Trailborne.Features.Sunstone.BlipStyle>?          LensMinimapBlipStyle   = null;

        // ── v3 Swamp: Iron Compass (camera-yaw HUD compass overlay, card t_ee61472f) ──
        // The needle-lag feel + the overlay anchor/size/position are LIVE config so Daniel can
        // converge "a little lag is good" and place the dial on a joined client without a rebuild
        // (Q3 + Q4 — the client-visual-can't-be-verified-headless rule, same as the cairn banner).
        // Nullable + ?.Value-accessed from SBPR_CompassHud so a no-Plugin unit context falls back
        // to SBPR_CompassHud.Default* consts (single source of truth). Bound in Awake.
        internal static ConfigEntry<float>? CompassNeedleLag = null;  // LerpAngle rate (deg-equiv); higher = snappier
        internal static ConfigEntry<float>? CompassMaxTilt   = null;  // max dial-face tilt at full look up/down (deg)
        internal static ConfigEntry<SBPR.Trailborne.Features.Exploration.CompassAnchor>? CompassAnchor = null; // HUD anchor (enum, default TopCenter)
        internal static ConfigEntry<float>? CompassSize      = null;  // dial footprint, px square
        internal static ConfigEntry<float>? CompassOffsetX   = null;  // nudge from the anchor, px (+right)
        internal static ConfigEntry<float>? CompassOffsetY   = null;  // nudge from the anchor, px
        // Diagnostic-logging gate (t_61aff612 HUD-render bug): emit mount/wearing/anchor LogInfo so a
        // client LogOutput.log splits "mount/pump fail" from "off-screen." Default ON for the diagnostic
        // cut; bake to false once Daniel confirms the dial renders in-game.
        internal static ConfigEntry<bool>?  CompassDebugMount = null;

        // ── v3 Swamp: Iron Compass → map-surface north ring (card t_fb53c9e4, M1) ──
        // CompassDiscMode (the §0-① HUD-needle-vs-surface-ring policy) is a LIVE enum so Daniel can flip
        // the feel on a joined client (the LensMinimapHandoffMode / banner-windsock pattern). It mirrors the
        // Sunstone twin's enum bind exactly. The enum type lives in the engine-free CompassNorthGate.cs so
        // the bind here and the unit test share one definition. Nullable + ?.Value-accessed from
        // SBPR_CompassHud so a no-Plugin unit context falls back to the default. Bound in Awake.
        internal static ConfigEntry<SBPR.Trailborne.Features.Cartography.CompassDiscModeEnum>? CompassDiscMode = null;
        // CompassAutoNorthUp (the §6 opt-in north-up lock) — bound here for the config surface, default
        // OFF (Daniel ③). M1 leaves it INERT: nothing reads it yet (M2, CompassAutoNorthUp, wires the
        // surface north-up branch). Binding it now keeps the IronCompass config section stable across the
        // M1→M2 split so Daniel sees the full knob set at once.
        internal static ConfigEntry<bool>? CompassAutoNorthUp = null;

        // §2L.14 (ticket-cursor-lock-map-sign, card t_cad2c6f3): diagnostic toggle for the modal
        // cursor-free path. When true, §2L.18's CursorPumpPatch logs what VANILLA computes for the
        // raw IsMouseActive + per-contributor open flags every ~30 frames while a modal is open — the
        // ground-truth probe that localizes WHY the cursor re-locks on a KB+M box (this bug has
        // shipped-dead twice on seam guesses; this flag makes the next client repro decisive). Default
        // ON in this diagnostic build; flip to false (cheap) once the fix is confirmed in-game.
        internal static ConfigEntry<bool>? CursorDiag = null;

        private void Awake()
        {
            Log = Logger;
            PluginFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            Log.LogInfo($"[Trailborne] Awake — {ModName} {ModVersion} booting (folder={PluginFolder}, OnSBServer={ServerContext.OnSBServer})");

            DebugCairnDamage = Config.Bind(
                "Debug",
                "SBPR_DebugCairnDamage",
                true,
                "When true, Shift+E on a pristine cairn (≥75% HP) drops it to 70% HP so the repair/upgrade combo " +
                "gesture is exercisable without waiting for natural decay. v0.1.0 playtest aid. Flip false (or " +
                "remove this section) once decay tuning lands.");

            CursorDiag = Config.Bind(
                "Debug",
                "SBPR_CursorDiag",
                false,
                "When true, logs the modal cursor diagnostic (§2L.18, card t_94cc9713): every ~30 frames " +
                "while an SBPR map/sign modal is open, what VANILLA computes for the cursor (lockState/visible) " +
                "while the masquerade signals a GUI is open, plus the input-source flags, are written to the " +
                "BepInEx log. Default OFF (the §2L.18 masquerade fix is confirmed in-game 2026-06-23). Flip true " +
                "only to re-diagnose — it's a pure logging gate; the cursor fix runs regardless of this flag.");

            CairnDecayHpPerDay = Config.Bind(
                "Cairns",
                "SBPR_CairnDecayHpPerDay",
                Cairns.DefaultDecayHpPerDay,
                "TIME-decay rate for cairns, in HP lost per IN-GAME DAY (one in-game day ≈ 20 real minutes at the " +
                "vanilla 1200s day length). Drives both the resident 1 Hz owner ticker and the out-of-zone " +
                "WearNTear.Awake backfill — they share one timeline so total decay equals this rate regardless of " +
                "whether the cairn was loaded. Against the 100 HP cairn, the default 10 = a ~10 in-game-day life if " +
                "never repaired. Vanilla wet weather stacks on top as an accelerant (but can't push below 50% alone). " +
                "Set 0 to disable time decay entirely (weather-only).");

            // ── Banner windsock knobs (card t_4a4a9706) ──────────────────────────────
            // ALL banner-look constants are live config because the windsock visual cannot
            // be verified outside a joined client (it shipped wrong in-world twice while CI
            // was green). Range-clamped so a fat-finger in the .cfg can't blow the banner up.
            BannerDropY = Config.Bind(
                "CairnBanner", "SBPR_BannerTailLength", CairnTag.DefaultBannerDropY,
                new ConfigDescription(
                    "WINDSOCK tail length in metres (the vertical drop of the ribbon below its mount). " +
                    "Longer = more of a streaming tail; shorter = a stubby flag. Tune in-game, then we bake §A2.1b.",
                    new AcceptableValueRange<float>(0.2f, 6f)));
            BannerWidthZ = Config.Bind(
                "CairnBanner", "SBPR_BannerWidth", CairnTag.DefaultBannerWidthZ,
                new ConfigDescription(
                    "Banner ribbon WIDTH in metres. Narrower reads as a windsock/pennant tail; wider as a square drape.",
                    new AcceptableValueRange<float>(0.05f, 2f)));
            BannerMountHeight = Config.Bind(
                "CairnBanner", "SBPR_BannerMountHeight", CairnTag.DefaultBannerMountHeight,
                new ConfigDescription(
                    "Height in metres ABOVE the pile crown at which the banner's mount (pinned end) sits. The tail " +
                    "hangs DOWN from here past the cairn (Daniel: 'mount above the cairn, tail free-falls below').",
                    new AcceptableValueRange<float>(0f, 3f)));
            BannerOffsetXZ = Config.Bind(
                "CairnBanner", "SBPR_BannerOffsetXZ", CairnTag.DefaultBannerOffsetXZ,
                new ConfigDescription(
                    "Lateral nudge in metres off the pile centre so the tail clears the cosmetic flame.",
                    new AcceptableValueRange<float>(0f, 1.5f)));
            BannerWindMult = Config.Bind(
                "CairnBanner", "SBPR_BannerWindMult", CairnTag.DefaultBannerWindMult,
                new ConfigDescription(
                    "Directional wind MULTIPLIER on the Cloth's externalAcceleration. Vanilla GlobalWind uses 1.0 on " +
                    "BIG cloth; our small ribbon may need >1 to visibly STREAM downwind. Raise until the tail lifts toward horizontal in a storm.",
                    new AcceptableValueRange<float>(0.1f, 100f)));
            BannerWindRandomFactor = Config.Bind(
                "CairnBanner", "SBPR_BannerWindRandomFactor", CairnTag.DefaultBannerWindRandomFactor,
                new ConfigDescription(
                    "Omnidirectional jitter factor (randomAcceleration = wind × mult × this). Vanilla 0.5. LOWER it " +
                    "if the tail 'zigs and zags on the spot' instead of pointing downwind — random flutter competes with directional streaming.",
                    new AcceptableValueRange<float>(0f, 1f)));
            BannerClothDamping = Config.Bind(
                "CairnBanner", "SBPR_BannerClothDamping", CairnTag.DefaultBannerClothDamping,
                new ConfigDescription(
                    "Cloth.damping (0 = floppy/lively, 1 = stiff/dead). Lower = the tail responds more loosely to wind.",
                    new AcceptableValueRange<float>(0f, 1f)));
            BannerStretchStiffness = Config.Bind(
                "CairnBanner", "SBPR_BannerStretchStiffness", CairnTag.DefaultBannerStretchStiffness,
                new ConfigDescription(
                    "Cloth.stretchingStiffness (0.1 = very billowy, 1.0 = RIGID). THE windsock fix (card t_a2fc3073): a fresh " +
                    "Cloth defaults to 1.0 (rigid), so the ~1 m/s² wind force could only make it jitter in place — never stream. " +
                    "Lower it until the sheet billows. Default 0.5 mirrors the vanilla SAIL (the closest vanilla wind-streaming " +
                    "cloth; the vanilla banner has no Cloth at all). Drop toward 0.2 if it still won't lean downwind.",
                    new AcceptableValueRange<float>(0.1f, 1f)));
            BannerBendStiffness = Config.Bind(
                "CairnBanner", "SBPR_BannerBendStiffness", CairnTag.DefaultBannerBendStiffness,
                new ConfigDescription(
                    "Cloth.bendingStiffness (0.1 = curls/flutters freely, 1.0 = RIGID). Pairs with stretchingStiffness as the " +
                    "windsock fix (card t_a2fc3073) — at the default 1.0 the tail cannot curl/flutter downwind. Default 0.5 " +
                    "mirrors the vanilla SAIL. Drop toward 0.2 alongside stretch if the tail won't flutter into the wind.",
                    new AcceptableValueRange<float>(0.1f, 1f)));
            BannerClothFreeDistance = Config.Bind(
                "CairnBanner", "SBPR_BannerTailFreedom", CairnTag.DefaultBannerClothFreeDistance,
                new ConfigDescription(
                    "Max travel (in cloth units) of the FREE tail end. Larger = the tail genuinely FLOPS/streams instead " +
                    "of vibrating in place. This is the headline windsock knob — the mount end stays hard-pinned regardless.",
                    new AcceptableValueRange<float>(0.1f, 10f)));
            BannerFreeRampExp = Config.Bind(
                "CairnBanner", "SBPR_BannerFreedomRampExp", CairnTag.DefaultBannerFreeRampExp,
                new ConfigDescription(
                    "Exponent of the freedom ramp from mount (≈0 freedom) to tail (full freedom). >1 keeps the upper band " +
                    "near the mount stiff and concentrates the flapping at the far tail (more windsock-like). 1 = linear.",
                    new AcceptableValueRange<float>(0.5f, 6f)));
            BannerPinBandFrac = Config.Bind(
                "CairnBanner", "SBPR_BannerMountPinBandFrac", CairnTag.DefaultBannerPinBandFrac,
                new ConfigDescription(
                    "Fraction of the Y-span (measured from the mount end) that is HARD-pinned (a small mount cluster). " +
                    "Keep small so only the mount is fixed and the rest is free to stream.",
                    new AcceptableValueRange<float>(0.005f, 0.5f)));
            BannerUseGravity = Config.Bind(
                "CairnBanner", "SBPR_BannerUseGravity", CairnTag.DefaultBannerUseGravity,
                "When true the Cloth free-falls under gravity on build (the tail hangs slack), then wind drives it to " +
                "stream — Daniel's 'free fall when built, then flop in the wind like a windsock'. False = no gravity (wind only).");
            BannerSubdivisions = Config.Bind(
                "CairnBanner", "SBPR_BannerSubdivisions", CairnTag.DefaultBannerSubdivisions,
                new ConfigDescription(
                    "Midpoint subdivisions of the per-instance cloth mesh. The donor banner cloth is a COARSE ~78-vertex " +
                    "sheet — too few Cloth particles to drape over the rocks or flop naturally, so the tail reads as a " +
                    "stiff plank. Each level splits every triangle into 4 (~4× polys): 0 = donor (coarse), 1 = ~4× " +
                    "(default), 2 = ~16× (finest, heaviest cloth-solve). Raise for a softer, finer drape; lower if the " +
                    "cloth solve costs too much with many cairns in view. Takes effect on banner (re)build — reload the zone.",
                    new AcceptableValueRange<int>(0, 2)));
            BannerRockDrape = Config.Bind(
                "CairnBanner", "SBPR_BannerRockDrape", CairnTag.DefaultBannerRockDrape,
                "When true, a few cheap sphere colliders are placed down the stone-pile axis so the hanging banner " +
                "DRAPES against the rocks instead of clipping straight through them (Daniel's 'flap against the stones'). " +
                "Only reads well with SBPR_BannerSubdivisions > 0 — a coarse sheet has too few particles to drape. " +
                "False = no colliders (the cloth ignores the pile). Takes effect on banner (re)build — reload the zone.");
                BannerTiltDegrees = Config.Bind(
                "CairnBanner", "SBPR_BannerTiltDegrees", CairnTag.DefaultBannerTiltDegrees,
                new ConfigDescription(
                "Tilt the banner mount toward HORIZONTAL so the sheet lies out like a flagpole flag instead of " +
                "hanging straight down. 0 = vertical hang (the original — gravity fights every lift, so even forced " +
                "wind barely raises it). 90 = horizontal mount (the sheet falls out sideways; gravity now droops it " +
                "ACROSS its length instead of opposing the lift, so even weak wind streams it — this is how a real " +
                "flagpole flag works). Try 60–90 if the banner reads stiff/under-deflected at full wind. Composes " +
                "with the wind-alignment yaw (the banner still turns to face the wind). Takes effect on banner " +
                "(re)build — reload the zone.",
                new AcceptableValueRange<float>(0f, 90f)));

            // ── A/B harness — Option A directional alignment (card t_1d7c0d19) ───────
            // These two only affect Option A (the Cloth windsock — black/blue/red cairns by the
            // harness routing). Option B (white, shader-wave) ignores them entirely.
            BannerAlignToWind = Config.Bind(
                "CairnBanner", "SBPR_BannerAlignToWind", CairnTag.DefaultBannerAlignToWind,
                "OPTION A ONLY. When true the Cloth windsock ORIENTS to the wind so it STREAMS downwind (the " +
                "directional fix all 4 prior attempts omitted — vanilla cloth streams because GlobalWind rotates the " +
                "whole transform to the wind, THEN the solver ripples on top). False = the old force-only behaviour " +
                "(waggles in place, never points downwind). No effect on Option B (shader-wave) cairns.");
            BannerAlignMode = Config.Bind(
                "CairnBanner", "SBPR_BannerAlignMode", CairnTag.DefaultBannerAlignMode,
                new ConfigDescription(
                    "OPTION A ONLY — which axis maps to the wind (the pivot/axis trap our flat Y-Z cloth sheet creates: " +
                    "Y=drop, Z=width, X=normal). 0 = StreamYaw (DEFAULT): pure yaw about world-up, the sheet's PLANE " +
                    "contains the wind so the tail streams downwind in-plane while the drop stays vertical. 1 = FaceYaw: " +
                    "StreamYaw +90°, presents the broad FACE to the wind. 2 = VanillaFull: literal vanilla " +
                    "LookRotation(windDir, up) including the vertical component (pitches the sheet) — reference only, not " +
                    "expected to read as a clean windsock on our axes. Prototype 0 vs 1 in-game, then we bake the winner.",
                    new AcceptableValueRange<int>(0, 2)));

            // ── DIAGNOSTIC (card t_7de074f3 — ATTEMPT #6 Step 1) ─────────────────────
            // Attaches the BannerDiagnostic runtime probe to each cairn banner. It logs a
            // single greppable [BannerDiag] report (parent-chain lossyScale uniformity, cloth
            // enabled/particle/coefficient counts, a frame-to-frame movement test proving
            // whether the Cloth solver actually integrates, and rest-pose world orientation),
            // then self-disables after ~4 s. This is the Step-1 deliverable: prove the failure
            // mode BEFORE writing a sixth fix. Default ON for this diagnostic build; set false
            // (or strip the component) once the attempt-#6 rebuild lands.
            BannerDiagnostic = Config.Bind(
                "CairnBanner", "SBPR_BannerDiagnostic", CairnTag.DefaultBannerDiagnostic,
                "DIAGNOSTIC (attempt #6, card t_7de074f3). When true, each cairn banner logs a one-shot " +
                "[BannerDiag] report proving the Cloth failure mode: the live parent-chain lossyScale (the " +
                "prime suspect — Cloth won't simulate under non-uniform world scale), the cloth particle/" +
                "coefficient counts, a frame-to-frame MOVEMENT test (does the solver actually step, or is it " +
                "inert like the prior 5 attempts assumed it wasn't?), and whether the rest pose hangs down or " +
                "stands up. Grep the client log for '[BannerDiag]'. Set false once the failure mode is known.");

            // ── Cartography: NoMap enforcement (card t_2f9fc470, impl-spec §3.5.3) ───
            // Daniel's directive is "this mod should just disable it" → default ON, enforced.
            // ONE optional escape hatch so a future server operator / debug session can opt
            // out without a recompile. NoMapEnforcer.ShouldEnforceNoMap reads this; the loud
            // boot-log line fires either way (AT-NOMAP-BOOTLOG — the premise is never silent).
            EnforceNoMap = Config.Bind(
                "Cartography",
                "SBPR_EnforceNoMap",
                true,
                "When true (default), the mod disables the vanilla global map by setting GlobalKeys.NoMap " +
                "server-side at world load — the cartography tier's enforced precondition. Set false ONLY to " +
                "let the vanilla global map coexist (debug / non-cartography server). The Mistlands tier " +
                "advancement lifts NoMap independently of this flag.");

            // v3 Swamp — Sunstone Lens charge economy + detection (card t_2fd7bc7f). Defaults
            // mirror SunstoneLens.Default* consts (single source of truth). A full 100-unit
            // battery ≈ 5 min of detection / ≈ 1.7 min of sun to refill — the "top up in the
            // open, spend in the Swamp's dark" rhythm. Live-tunable per the charge-economy knob.
            LensMaxCharge = Config.Bind(
                "SunstoneLens", "MaxCharge", SBPR.Trailborne.Features.Sunstone.SunstoneLens.DefaultMaxCharge,
                "Battery capacity of the Sunstone Lens (its max durability = max stored sunlight).");
            LensDrainPerSec = Config.Bind(
                "SunstoneLens", "DrainPerSecond", SBPR.Trailborne.Features.Sunstone.SunstoneLens.DefaultDrainPerSec,
                "Charge drained per second while the Lens is worn and NOT recharging (constant, independent of detected count).");
            LensChargePerSec = Config.Bind(
                "SunstoneLens", "ChargePerSecond", SBPR.Trailborne.Features.Sunstone.SunstoneLens.DefaultChargePerSec,
                "Charge gained per second while in clear daylight, dry, outdoors, outside the Swamp.");
            LensDetectRadius = Config.Bind(
                "SunstoneLens", "DetectRadius", SBPR.Trailborne.Features.Sunstone.SunstoneLens.DefaultDetectRadius,
                "Radius (metres) within which the charged Lens reveals hostile creatures.");
            LensDetectInterval = Config.Bind(
                "SunstoneLens", "DetectIntervalSeconds", SBPR.Trailborne.Features.Sunstone.SunstoneLens.DefaultDetectInterval,
                "Seconds between detection sweeps (HUD-driven; lower = snappier, slightly costlier).");
            LensClearWeatherNamesRaw = Config.Bind(
                "SunstoneLens", "ClearWeatherNames", "",
                "OPTIONAL comma-separated allowlist of clear/sunny weather env names that count as 'sun' for " +
                "recharging. EMPTY (default) means any non-wet daylight weather recharges (driven by EnvSetup.m_isWet). " +
                "Pin specific names only to exclude a dry-but-dim weather.");
            LensClearWeatherNames.Clear();
            foreach (var raw in (LensClearWeatherNamesRaw.Value ?? "").Split(','))
            {
                var nm = raw.Trim();
                if (nm.Length > 0) LensClearWeatherNames.Add(nm);
            }

            // v3 Swamp — Sunstone Lens WORLD-SPACE eidetic halo render (card t_68672b6b → t_d17d9b58;
            // geometry re-locked by bug-fix t_10bacccf). A head-centric halo of billboarded creature
            // trophies floating in the 3D world at their real bearings — FIXED ring distance + scale-only
            // range cue (10m knee: full ≤10m, 0.25× at the 70m edge), vanilla star pips, yellow/orange/red
            // aggro tint. Supersedes the screen-space ring. All geometry/feel LIVE-tunable so Daniel
            // converges the look on a joined client without a rebuild (a world-space visual can't be
            // verified headless — the cairn-banner lesson). Range-clamped so a fat-finger in the .cfg
            // can't blow the halo up. Defaults mirror the SunstoneWorldRing.Default* consts (single
            // source of truth). The old HaloRadiusMin/Max → a single fixed HaloRadius; HaloScaleMin is
            // REMOVED (the edge scale is now derived as 0.25×HaloScaleMax inside SunstoneHaloGeometry).
            LensHaloRadius = Config.Bind(
                "SunstoneLens", "HaloRadius",
                SBPR.Trailborne.Features.Sunstone.SunstoneWorldRing.DefaultHaloRadius,
                new ConfigDescription(
                    "FIXED halo radius (world metres): the SINGLE distance from your eye-point at which EVERY detected "
                    + "enemy's trophy floats, regardless of how far the enemy actually is. A true fixed-distance ring — "
                    + "far enemies are NOT pushed away from your face; only their SCALE shrinks with range (see HaloScaleMax). "
                    + "Small on purpose — this is a halo around your head, not a ground ring at detection distance.",
                    new AcceptableValueRange<float>(0.5f, 8f)));
            LensHaloScaleMax = Config.Bind(
                "SunstoneLens", "HaloScaleMax",
                SBPR.Trailborne.Features.Sunstone.SunstoneWorldRing.DefaultHaloScaleMax,
                new ConfigDescription(
                    "Trophy world-scale at FULL size — the world size of a trophy quad for an enemy within 10 m (the locked "
                    + "\"1.0\"). SCALE carries all the distance information: an enemy \u2264 10 m renders at this full scale; one at "
                    + "the detection edge (DetectRadius) renders at 25% of it; linear between (the 10 m knee). The edge floor "
                    + "is derived (0.25\u00d7 this), not a separate knob. This is the eyeball tunable Daniel converges in-game.",
                    new AcceptableValueRange<float>(0.05f, 3f)));
            LensHaloEyeOffsetY = Config.Bind(
                "SunstoneLens", "HaloEyeOffsetY",
                SBPR.Trailborne.Features.Sunstone.SunstoneWorldRing.DefaultHaloEyeOffsetY,
                new ConfigDescription(
                    "Vertical lift (world metres, +up) of the halo plane off the eye-point, so trophies clear the crosshair. 0 = on the eye line.",
                    new AcceptableValueRange<float>(-1f, 2f)));
            LensRingMaxIcons = Config.Bind(
                "SunstoneLens", "RingMaxIcons", SBPR.Trailborne.Features.Sunstone.SunstoneLensHudOverlay.DefaultRingMaxIcons,
                "Max trophies drawn at once (a Swamp horde shows the nearest N; pooled + capped so it never tanks framerate).");
            LensRingShowEmpty = Config.Bind(
                "SunstoneLens", "ShowEmptyRing", SBPR.Trailborne.Features.Sunstone.SunstoneLensHudOverlay.DefaultShowEmptyRing,
                "Master on/off for the empty-state affordance. REPURPOSED (card t_9d7c3dfe): this used to "
                + "gate a flat screen-space solar ring; it now gates the WORLD-SPACE pulsing sun-corona "
                + "disc that replaced it (Daniel /bug: 'a 3d slowly pulsing sun corona disc, not a screen "
                + "space circle'). ON (default): when worn + charged but nothing's near, a glowing corona "
                + "breathes on the ground around you so you can see the lens is live. The .cfg KEY is kept "
                + "('ShowEmptyRing') to avoid churning a value Daniel may already have set. Live-tunable.");

            // v3 Swamp — Sunstone Lens empty-state → world-space pulsing sun-corona disc (card t_9d7c3dfe).
            // Every corona knob is LIVE (the banner-windsock pattern; a client visual can't be verified
            // headless — Daniel converges the look on a joined GPU client, then we bake the chosen values
            // into the SunstoneCoronaDisc.Default* consts). Range-clamped so a fat-finger in the .cfg
            // can't blow the disc up. Defaults mirror those consts (single source of truth). The pulse
            // ENVELOPE shape is gated by the engine-free SunstoneCoronaPulse (AT-CORONA-PULSE-MATH).
            LensCoronaOrientation = Config.Bind(
                "SunstoneLens", "CoronaOrientation",
                SBPR.Trailborne.Features.Sunstone.SunstoneCoronaDisc.DefaultOrientation,
                "Orientation of the world-space sun-corona disc. GroundPlane (default): a flat 'sun on the "
                + "floor' disc lying in the ground plane around your feet, the surface the detected-creature "
                + "trophies orbit. CameraFacing: an upright disc on your eye-line that yaws to face the "
                + "camera (the trophy billboard idiom). Live-flippable, no rebuild.");
            LensCoronaPulseHz = Config.Bind(
                "SunstoneLens", "CoronaPulseHz",
                SBPR.Trailborne.Features.Sunstone.SunstoneCoronaDisc.DefaultPulseHz,
                new ConfigDescription(
                    "How fast the sun-corona breathes, in breaths per second. 0.25 (default) = one slow "
                    + "breath every 4 seconds. 0 = a steady glow with no pulse. Higher = faster breathing. "
                    + "Drives the engine-free SunstoneCoronaPulse envelope on one shared clock so it never "
                    + "drifts.",
                    new AcceptableValueRange<float>(0f, 2f)));
            LensCoronaAlphaTrough = Config.Bind(
                "SunstoneLens", "CoronaAlphaTrough",
                SBPR.Trailborne.Features.Sunstone.SunstoneCoronaDisc.DefaultAlphaTrough,
                new ConfigDescription(
                    "The corona's alpha (opacity) at the FAINTEST point of its breath. Default 0.10 (very "
                    + "faint). Lower = the corona nearly vanishes at the trough; higher = it stays more "
                    + "present. If you set this above CoronaAlphaPeak the two are swapped automatically.",
                    new AcceptableValueRange<float>(0f, 1f)));
            LensCoronaAlphaPeak = Config.Bind(
                "SunstoneLens", "CoronaAlphaPeak",
                SBPR.Trailborne.Features.Sunstone.SunstoneCoronaDisc.DefaultAlphaPeak,
                new ConfigDescription(
                    "The corona's alpha (opacity) at the BRIGHTEST point of its breath. Default 0.28 (a "
                    + "soft glow around the old 0.18 static baseline). Higher = a stronger pulse peak.",
                    new AcceptableValueRange<float>(0f, 1f)));
            LensCoronaInnerFill = Config.Bind(
                "SunstoneLens", "CoronaInnerFill",
                SBPR.Trailborne.Features.Sunstone.SunstoneCoronaDisc.DefaultInnerFill,
                new ConfigDescription(
                    "Shape of the corona from centre out. 0 = a thin hoop (just the rim glows, like the old "
                    + "ring); 1 = a fully filled sun-disc bright to the centre. Default 0.35 (between a hoop "
                    + "and a filled sun — a glowing ring with a soft lit interior).",
                    new AcceptableValueRange<float>(0f, 1f)));
            LensCoronaThickness = Config.Bind(
                "SunstoneLens", "CoronaThickness",
                SBPR.Trailborne.Features.Sunstone.SunstoneCoronaDisc.DefaultThickness,
                new ConfigDescription(
                    "Width of the soft radiant edge that fades the corona out to transparent at its rim. "
                    + "Default 0.45 (a soft sun corona). Lower = a harder, crisper edge; higher = a wider, "
                    + "hazier falloff.",
                    new AcceptableValueRange<float>(0f, 1f)));
            LensCoronaRadius = Config.Bind(
                "SunstoneLens", "CoronaRadius",
                SBPR.Trailborne.Features.Sunstone.SunstoneCoronaDisc.DefaultRadius,
                new ConfigDescription(
                    "The corona's rim radius in world metres. Defaults to the trophy HaloRadius (~2.0 m) so "
                    + "the corona's edge sits where the detected-creature trophies orbit — the disc is the "
                    + "substrate they float around. Grow it for a wider floor-sun; it's independent of the "
                    + "trophy ring once you change it.",
                    new AcceptableValueRange<float>(0.5f, 8f)));
            LensCoronaPlaneOffsetY = Config.Bind(
                "SunstoneLens", "CoronaPlaneOffsetY",
                SBPR.Trailborne.Features.Sunstone.SunstoneCoronaDisc.DefaultPlaneOffsetY,
                new ConfigDescription(
                    "Vertical lift (world metres, +up) of the corona disc off its anchor. 0 = on the anchor "
                    + "(feet for GroundPlane, eye-line for CameraFacing). Nudge it to drop a ground-plane "
                    + "disc exactly to the floor or lift a camera-facing one off the crosshair.",
                    new AcceptableValueRange<float>(-2f, 2f)));
            LensRingShowDepletedHint = Config.Bind(
                "SunstoneLens", "ShowDepletedHint", SBPR.Trailborne.Features.Sunstone.SunstoneLensHudOverlay.DefaultShowDepletedHint,
                "When the lens runs out of charge, show a dim ring outline as an 'inert, recharge me' cue. Default OFF (ring fully off when depleted).");
            LensRingDebugText = Config.Bind(
                "SunstoneLens", "DebugTextReadout", SBPR.Trailborne.Features.Sunstone.SunstoneLensHudOverlay.DefaultDebugTextReadout,
                "Debug aid: also draw the legacy text line ('N hostiles · nearest Xm · charge Y%') below the ring. Default OFF.");
            LensRingDebugMount = Config.Bind(
                "SunstoneLens", "DebugMount",
                SBPR.Trailborne.Features.Sunstone.SunstoneLensHudOverlay.DefaultDebugMount,
                "Diagnostic logging for the detection-ring HUD overlay (t_d5949685 render bug — the same self-deactivating-host "
                + "Update-pump bug the Iron Compass had). When ON, the mod logs the overlay MOUNT (under Hud.m_rootObject), the "
                + "VISIBLE/hidden transitions, and the resolved placement on first show — so a fresh client LogOutput.log can tell a "
                + "mount/pump failure apart from an on-screen-but-empty ring. Leave ON while diagnosing; set false once the ring is "
                + "confirmed visible in-game.");
            LensDumpUnmappedCreatures = Config.Bind(
                "SunstoneLens", "DumpUnmappedCreatures",
                SBPR.Trailborne.Features.Sunstone.SunstoneProjection.DefaultDumpUnmappedCreatures,
                "Default ON (card t_d17d9b58). At world load (ZNetScene-ready), enumerate EVERY registered creature prefab and "
                + "resolve each to (own trophy | remapped sibling | none), logging the genuinely-unmapped set as ONE reviewable "
                + "block in LogOutput.log. Use it to grow the variant→sibling remap table over time, then set false to silence the "
                + "scan. Unmapped creatures still render the generic threat glyph — this dump is purely for review, it changes nothing.");

            // v3 Swamp — Sunstone Lens → minimap handoff (card t_91e86951). Daniel gated all 3 knobs
            // 2026-06-20: when a minimap is present (SBPR carry-disc in nomap-ON, or the vanilla corner
            // map in nomap-OFF) Lens detection moves onto it; the camera-relative ring is the no-minimap
            // fallback only. Both enums are LIVE so Daniel converges the feel on a joined client.
            LensMinimapHandoffMode = Config.Bind(
                "SunstoneLens", "MinimapHandoffMode",
                SBPR.Trailborne.Features.Sunstone.MinimapHandoffMode.DiscWhenBound,
                "When a minimap is present (the SBPR carry-disc in nomap-ON, or the vanilla corner map in "
                + "nomap-OFF), where do Lens threats render? DiscWhenBound (default): the ring hides and the "
                + "threats move onto the minimap. RingOnly: ignore every minimap, the ring always renders "
                + "(the escape hatch). Both: the ring AND the minimap both show threats. Live-tunable. (The "
                + "value name 'DiscWhenBound' predates the universal any-minimap rule — it now means 'hand "
                + "off whenever ANY minimap is present', not only the SBPR disc.)");
            LensMinimapBlipStyle = Config.Bind(
                "SunstoneLens", "MinimapBlipStyle",
                SBPR.Trailborne.Features.Sunstone.BlipStyle.Trophy,
                "How a threat draws on the minimap surfaces (the SBPR disc + the vanilla corner map). Dots "
                + "(default): a small aggro-tinted dot, legible at the disc's tight inner threat zone. Trophy: "
                + "the creature trophy sprite + aggro tint (richer, smaller-read). The screen-space RING is "
                + "unaffected — it always shows the full trophy art. Live-tunable.");

            // v3 Swamp — Iron Compass HUD overlay (card t_ee61472f). Needle-lag feel (Q3) +
            // anchor/size/position (Q4) are LIVE config: a camera-driven HUD widget can't be
            // verified headless (the cairn-banner lesson), so Daniel converges the lag and places
            // the dial on a joined client in ONE session, then we bake the chosen values into the
            // SBPR_CompassHud.Default* consts. Range-clamped so a fat-finger in the .cfg can't
            // blow the dial up. Defaults mirror those consts (single source of truth).
            CompassNeedleLag = Config.Bind(
                "IronCompass", "NeedleLag",
                SBPR.Trailborne.Features.Exploration.SBPR_CompassHud.DefaultNeedleLag,
                new ConfigDescription(
                    "Needle smoothing rate for the 'slight lag' (Q3): the needle lerps toward true heading at "
                    + "Mathf.LerpAngle(cur, target, dt * THIS). HIGHER = snappier (less lag); LOWER = laggier/dreamier. "
                    + "Daniel: 'a little lag is good, might need tuning' — converge in a joined session, then we bake it.",
                    new AcceptableValueRange<float>(0.5f, 30f)));
            CompassMaxTilt = Config.Bind(
                "IronCompass", "MaxTiltDegrees",
                SBPR.Trailborne.Features.Exploration.SBPR_CompassHud.DefaultMaxTilt,
                new ConfigDescription(
                    "Max dial-face tilt (degrees) at full look up/down — the subtle 3D-instrument feel (design §8 '~45°'). "
                    + "0 = flat dial (no tilt); 45 = the design default. Looking level always returns the face flat.",
                    new AcceptableValueRange<float>(0f, 80f)));
            CompassAnchor = Config.Bind(
                "IronCompass", "Anchor",
                SBPR.Trailborne.Features.Exploration.CompassAnchor.TopCenter,
                "Where the compass overlay anchors on the HUD (Q4). TopCenter (default) is NoMap-safe — it works "
                + "with the SB server's default no-minimap. BelowMapDisc / OnMapDiscOverlay / EyeOfOdinMinimap are "
                + "RESERVED for when their dock targets exist (the carry-state Local Map disc; the future Eye-of-Odin "
                + "global minimap) and currently fall back to TopCenter with a one-time log.");
            CompassSize = Config.Bind(
                "IronCompass", "SizePx",
                SBPR.Trailborne.Features.Exploration.SBPR_CompassHud.DefaultSize,
                new ConfigDescription(
                    "Dial footprint in pixels (square) at the reference canvas scale. The needle + labels scale with it.",
                    new AcceptableValueRange<float>(48f, 400f)));
            CompassOffsetX = Config.Bind(
                "IronCompass", "OffsetXPx",
                SBPR.Trailborne.Features.Exploration.SBPR_CompassHud.DefaultOffsetX,
                new ConfigDescription(
                    "Horizontal nudge from the anchor in pixels (+ = right). 0 keeps it centered under TopCenter.",
                    new AcceptableValueRange<float>(-960f, 960f)));
            CompassOffsetY = Config.Bind(
                "IronCompass", "OffsetYPx",
                SBPR.Trailborne.Features.Exploration.SBPR_CompassHud.DefaultOffsetY,
                new ConfigDescription(
                    "Vertical nudge from the anchor in pixels. Under TopCenter the anchor pivot is the screen's top edge, "
                    + "so a NEGATIVE value drops the dial down into view (default ≈ -94).",
                    new AcceptableValueRange<float>(-1080f, 1080f)));
            CompassDebugMount = Config.Bind(
                "IronCompass", "DebugMount",
                SBPR.Trailborne.Features.Exploration.SBPR_CompassHud.DefaultDebugMount,
                "Diagnostic logging for the HUD overlay (t_61aff612 render bug). When ON, the mod logs the overlay "
                + "MOUNT (under Hud.m_rootObject), the WEARING true/false transitions, and the resolved anchor/size on "
                + "first show — so a fresh client LogOutput.log can tell a mount/pump failure apart from an off-screen "
                + "placement. Leave ON while diagnosing; set false once the dial is confirmed visible in-game.");

            // v3 Swamp — Iron Compass → map-surface north ring (card t_fb53c9e4, M1). The HUD-needle-vs-
            // surface-ring policy is a LIVE enum so Daniel can flip the feel on a joined client (the
            // LensMinimapHandoffMode pattern). The opt-in north-up lock is bound now (default OFF) but stays
            // INERT in M1 — M2 (CompassAutoNorthUp) wires the surface north-up branch.
            CompassDiscMode = Config.Bind(
                "IronCompass", "DiscMode",
                SBPR.Trailborne.Features.Cartography.CompassDiscModeEnum.DiscWhenBound,   // ← DEFAULT (Daniel ①)
                "When the Iron Compass is worn AND an SBPR map surface (carry-disc or full-map) is showing, how "
                + "is north drawn? DiscWhenBound (default): the HUD needle hides and an iron bezel + N + ticks "
                + "north ring is drawn on the surface. HudOnly: ignore the surface, the HUD needle always renders "
                + "(today's behaviour / escape hatch). Both: the HUD needle AND the surface ring both render. "
                + "Live-tunable. (No surface showing, or compass off → the HUD needle, every mode.)");
            CompassAutoNorthUp = Config.Bind(
                "IronCompass", "AutoNorthUp",
                false,                                                                   // ← DEFAULT OFF (Daniel ③)
                "OPT-IN north-up lock (default OFF). false: the surface stays heading-up and the iron N-ring orbits "
                + "the rim (the default no-map disorientation is intact). true: worn + a surface showing → the "
                + "surface locks north-up and the player chevron rotates. NOTE: this knob is INERT in M1 — the "
                + "north-up rotation lands in M2; binding it now keeps the config section stable across the split.");

            harmony = new Harmony(ModId);
            harmony.PatchAll(typeof(Registrar));
            harmony.PatchAll(typeof(CairnPatches));
            // Open-air cairn comfort (bug-fix 2026-06-13, card t_4c5b5b2d): grant the vanilla
            // Rested buff near a cairn out in the open, WITHOUT heat. Two cooperating heat-free
            // patches — the postfix on Player.UpdateEnvStatusEffects (outer class) SEEDS Resting,
            // and the nested RestingStripSuppressor swallows vanilla's per-tick Resting strip so
            // SE_Cozy's 10 s ramp runs (campfire-parity timing, Daniel 2026-06-13). BOTH must be
            // registered or the fix ships half-dead (PatchCheck ERRORs at boot on an unregistered
            // [HarmonyPatch]). The nested container is registered separately, per the repo pattern.
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Cairns.CairnComfortRestedPatch));
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Cairns.CairnComfortRestedPatch.RestingStripSuppressor));
            // Painted Sign entrypoint (§A2.6, re-lock 2026-06-05): interacting with a
            // placed sign opens the custom combined Paint+Text uGUI panel instead of the
            // vanilla text dialog. Replaces the retired apply-ink-item paint gesture.
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Signs.SignInteractPatch));
            // v2 Marker-sign hover hint (card t_7816c0b0, impl-spec §4A): postfix on
            // Sign.GetHoverText that APPENDS a state-aware "[Shift+E] Pin/Unpin from map"
            // line on OUR markers only (MarkerSignTag gate), flipping with ReadPinned().
            // Markers fall through to Sign.GetHoverText (Sign is the first-added Hoverable,
            // §4A.1), so we augment the method that already fires. Append-not-replace keeps
            // the vanilla [Use] line; ward-gated like vanilla. MUST be registered here or it
            // ships dead and PatchCheck ERRORs at boot (the unregistered-patch lesson).
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Signs.SignHoverTextPatch));
            // Re-pin OUR sign's letter colour after the vanilla Sign.UpdateText ~2 Hz poll
            // (bug t_f8eff6d0): the poll reconstructs m_textWidget after our paint-time apply
            // and drops the letter tint, so a colour-only repaint left the letters on their
            // old colour. The postfix re-applies SignTag.ReapplyTextTint() on the poll cadence.
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Signs.SignTextRetintPatch));
            // Re-pin OUR sign's BOARD + BORDER mesh tint after the hammer support-overlay
            // clears (bug t_f3310406, AT-SIGN-HIGHLIGHT-REASSERT). The board/border tint now
            // rides a per-renderer MaterialPropertyBlock _Color override (the render-time layer
            // vanilla itself paints pieces through). WearNTear.Highlight pushes its own _Color
            // via MaterialMan while hovered and ResetHighlight wipes it ~0.2s later WITHOUT
            // restoring ours, so a hovered painted sign would drop to plain wood. This postfix
            // (gated to our SignTag) debounces a one-shot re-assert just after the wipe. MUST be
            // registered here or it ships dead and PatchCheck ERRORs at boot.
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Signs.SignMeshRetintPatch));
            // Make the surface usable while open: block player character input, freeze
            // camera mouse-look (via PlayerController.TakeInput — the vanilla gate our
            // panel bypasses by replacing the sign text dialog), and free the mouse
            // cursor. CursorPumpPatch (§2L, card t_1f82da71) re-seats the cursor-free onto
            // the LIVE GameCamera.LateUpdate seam — the old GameCamera.UpdateMouseCapture
            // target was emptied to a 1-byte ret by a vanilla Input-System update, so the
            // old postfix silently did nothing (the cursor stayed locked at the map table).
            // Four nested patch classes — register each container type so PatchCheck confirms
            // each wove a method.
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Signs.SignPanelInputBlock.TakeInputPatch));
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Signs.SignPanelInputBlock.PlayerControllerTakeInputPatch));
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Signs.SignPanelInputBlock.CursorPumpPatch));
            // §2F (issue 7, card t_23b950ee): a Menu.Show skip-original prefix gated on
            // SignPanelInputBlock.AnyOpen, so the Escape that closes our map viewer / sign
            // panel does NOT also open the vanilla pause menu the same frame (AT-VIEWEXIT-1).
            // Fourth nested container in SignPanelInputBlock — MUST be registered here or it
            // ships dead and PatchCheck ERRORs at boot (the t_564f695a unregistered-patch
            // lesson). Self-clearing + server-safe (AnyOpen false → pass-through).
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Signs.SignPanelInputBlock.MenuOpenSuppressPatch));
            // §2L.18 (card t_94cc9713, Daniel's call 2026-06-23): the MASQUERADE cursor approach —
            // supersedes the seven lockState-writing builds (§2L.7-R … §2L.17) which all failed for
            // Daniel's Linux rig (capture is below managed Cursor.lockState; proven by lockState=None +
            // still-captive in his v0.2.35 logs). Instead of writing lockState we make the game think a
            // GUI is open and let vanilla free the cursor: CursorPumpPatch (above) edge-drives the
            // vanilla mouse-capture block on GameCamera.LateUpdate, and TextInputMasqueradePatch postfixes
            // TextInput.IsVisible()→true while AnyOpen so every vanilla cursor-free gate fires for our
            // modal. §2L.13 (card t_a1cf35b0): a skip-original prefix on InventoryGui.Show(Container,int)
            // gated on AnyOpen, so the Inventory hotkey can't pop the inventory over an SBPR modal. Both
            // nested SignPanelInputBlock containers — MUST be registered here or they ship dead and
            // PatchCheck ERRORs at boot (the t_564f695a unregistered-patch lesson).
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Signs.SignPanelInputBlock.TextInputMasqueradePatch));
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Signs.SignPanelInputBlock.InventoryOpenSuppressPatch));
            // Client-facing refresh layer: Player.OnSpawned recipe reload +
            // PieceTable.UpdateAvailable array repair. Makes registered content
            // actually craftable/buildable on a joined client (task
            // fix-client-registration). Server-safe: no local Player, no build
            // menu, so these hooks are inert on the dedicated server.
            harmony.PatchAll(typeof(ClientRefreshPatches));

            // Placement-ripple magnitude (Request 1): Player.UpdatePlacementGhost
            // postfix sizes the placement marker's CircleProjector to OUR spade op's
            // effect radius (1.5/3/5m) so the aiming ripple previews the real affected
            // area instead of a fixed 5m ring. Client-cosmetic, gated on piece identity
            // (our op names only — never a vanilla Hoe/Cultivator). Inert on the
            // dedicated server (no local Player / placement marker).
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Trailblazing.PlacementMarkerRadiusPatch));

            // Cairn placement-elevation gate (§A2.1, LOCKED 2026-06-08; card t_aceacef6/PR #64).
            // SECOND postfix on the SAME vanilla Player.UpdatePlacementGhost as the radius patch
            // above — order-independent (radius sizes the preview ripple; this forces a too-low
            // cairn ghost to Invalid). Was authored + shipped in v0.2.10 but never registered here,
            // so the gate was dead on arrival (cairns still placed underwater). This line wires it.
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Cairns.CairnPlacementGatePatch));

            // Dev console command `bannerdiag` (Daniel 2026-06-10): on-demand physics+config
            // snapshot of every loaded cairn banner, so wind/cloth state can be read AFTER the
            // wind has ramped up (the auto-probe samples at world-load when wind is still ~0).
            // Read-only diagnostic; registers the command via a Terminal.InitTerminal postfix.
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Cairns.BannerDiagCommand));

            // Legacy terrain-op ZDO migration (§A2.2, card t_6fc9b3fa, AT-OP-3). Server-only,
            // one-time-per-boot postfix on ZNet.LoadWorld: destroys any persistent
            // piece_sbpr_path_*/piece_sbpr_replant_* ZDOs left by the pre-0.2.17 TerrainModifier
            // donors, before they can spam "not used when creating" warnings / hang a joining
            // client. The new additive TerrainOp ops aren't in ZNetScene, so vanilla auto-cleans
            // these anyway — this makes it deterministic + logs a verifiable count. Inert on a
            // client (LoadWorld only runs under ServerLoadWorld's m_isServer gate).
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Trailblazing.LegacyTerrainOpZdoCleanup));

            // v2 Marker Signs — WorldPin reconcile-trigger driver (card t_0c7b782d). Postfixes
            // Minimap.SetMapMode (map-open → full reconcile, the load-bearing trigger) and a
            // throttled Minimap.Update (light periodic tick) to keep the projected WorldPin set
            // consistent with the live marker-sign ZDOs (derive-by-scan, design §4.4). The
            // Shift+E pin gesture itself rides the already-registered SignInteractPatch; this
            // class only drives the projection/reconcile engine. Client-only by construction:
            // Minimap.instance is null on the dedicated server, so the reconcile early-outs there.
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.MarkerSigns.WorldPinReconcilePatches));

            // v2 Local Map (card t_cb831069) — the two-handed map item's equip discipline +
            // its bounded-viewer binding bootstrap.
            //  • LocalMapEquipPatch: prefix+postfix on Humanoid.EquipItem (overload-disambiguated
            //    by Type[]{ItemData,bool}) implementing the torch exception (C12/AT-MAP-TORCH) —
            //    ItemType=TwoHandedWeapon already gives the block-clear for free; this re-seats a
            //    left-hand Torch after the eviction so a lit map at night works.
            //  • LocalMapBootstrapPatch: postfix on Minimap.Start attaching the client-only
            //    LocalMapController (carry/equip state machine driving the bounded viewer).
            // Both client-relevant; the equip patch is server-safe (gated on our item) and the
            // bootstrap never fires server-side (no Minimap on the dedicated server).
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Cartography.LocalMapEquipPatch));
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Cartography.LocalMapBootstrapPatch));

            // v2 Local Map open-on-M (issue 3, §M-key spec — card t_f9a04fda). SBPR owns the
            // M (Map) input edge: a consume-PREFIX on Minimap.Update routes the M press into
            // LocalMapController.HandleMapKeyPressed() then ResetButtonStatus("Map"/"JoyMap") so
            // vanilla's own Update body (same frame) reads a cleared edge and never toggles its
            // Large map — no double-stack in nomap=OFF. Non-skip (void prefix): vanilla's Update
            // still runs for pins/explore/fade. Composes with the WorldPinReconcilePatches
            // Minimap.Update POSTFIX (different patch type + timing). Client+gfx only by
            // construction (early-out on a null graphics device → inert on the dedicated server).
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Cartography.MinimapMKeyOwnerPatch));

            // v2 Local Map item-name display (issue 10, §2A.6b — card t_41482aa3). Two scoped
            // postfixes substitute an imprinted map's per-instance Table name (sbpr_map_name
            // m_customData key) for the item's displayed title, so several bound maps are
            // distinguishable in inventory (AT-TABLENAME-3):
            //  • LocalMapTooltipNamePatch: Postfix on the private InventoryGrid.CreateItemTooltip
            //    — overwrites UITooltip.m_topic (the title) after the vanilla call. The seam the
            //    player actually reads.
            //  • LocalMapHoverNamePatch: Postfix on ItemDrop.GetHoverName — the world-drop hover.
            // Both guard on the presence of sbpr_map_name → pure pass-through for every other
            // item (AT-TABLENAME-7 no-orphan). Registered here so PatchCheck confirms each wove
            // a method (AT-TABLENAME-8 — the t_564f695a unregistered-patch lesson). Client-only
            // by nature (no inventory UI on the dedicated server); the guard makes it inert there.
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Cartography.LocalMapTooltipNamePatch));
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Cartography.LocalMapHoverNamePatch));

            // v2 Local Map tooltip combat-row suppression (issue 7, §2A.7 — card t_28b59e69).
            // ItemType=TwoHandedWeapon (§2A.2, load-bearing for the equip/block-clear/torch
            // discipline) routes the map through the weapon case of ItemDrop.ItemData.GetTooltip,
            // which emits weapon rows (parry bonus / knockback / backstab / stamina-use / block
            // where >1f) plus the always-on "$item_twohanded" handed line. A map is not a weapon.
            //  • LocalMapTooltipCombatStripPatch: Postfix on the PUBLIC STATIC
            //    ItemDrop.ItemData.GetTooltip(ItemData,int,bool,float,int) (overload-disambiguated
            //    by Type[]) — the BODY builder the instance GetTooltip(int) + the crafting UI both
            //    funnel through, so one seam covers inventory + crafting + equip/world-drop hover.
            //    For our item (guard: LocalMapItemTag on m_dropPrefab — catches blank AND imprinted)
            //    it rebuilds a clean body (description + weight) with no combat rows. ItemType is
            //    UNCHANGED; equip behaviour is untouched. Pure pass-through for every other item.
            // Registered here so PatchCheck confirms it wove a method (AT-MAP-TT-6). Client-only by
            // nature: GetTooltip reads Player.m_localPlayer (never called on the dedicated server);
            // the null-guard short-circuits regardless.
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Cartography.LocalMapTooltipCombatStripPatch));

            // v2 Cartographer's Kit — the auto-mapping GATE (card t_65fcfe5c, impl-spec §3.2).
            // Prefix on Minimap.UpdateExplore(float, Player): no-ops the personal walking-reveal
            // fog write unless the local player wears the Cartographer's Kit in the Utility slot.
            // Minimap.Update calls UpdateExplore unconditionally every frame (decomp :47056), so
            // fog accumulates even under v1's server-side nomap config — this gate makes that
            // accumulation Kit-dependent (AT-KIT-GATE). Client-only by construction: the dedicated
            // server has no Minimap, so UpdateExplore never runs there. The nested patch class is
            // registered explicitly so the PatchCheck watchdog sees it wove a method.
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Cartography.CartographersKit.UpdateExploreGate));

            // v2 NoMap enforcement — the cartography tier's enforced precondition (card
            // t_2f9fc470, impl-spec §3.5). Server-only Postfix on ZNet.LoadWorld (the same
            // proven post-WorldSetup seam as LegacyTerrainOpZdoCleanup, ⭐6): sets
            // GlobalKeys.NoMap server-side by default so the vanilla global map is disabled
            // and the cartography tier is the only map. Built as a LIFTABLE gate
            // (ShouldEnforceNoMap) so a future Mistlands advancement re-enables it; the
            // SBPR_EnforceNoMap config is the escape hatch. Idempotent + fail-loud-non-fatal.
            // Registered here so the PatchCheck watchdog sees it wove a method (the exact
            // dead-patch class it exists to catch).
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Cartography.NoMapEnforcer));

            // §2I.3 (issue 6, Part B) — Surveyor's Table imprint trigger. Harmony PREFIX on
            // Player.UseHotbarItem(int): while the local player is hovering a named Surveyor's
            // Table, pressing a hotbar number (1-8) imprints THAT slot's Local Map with the
            // Table's survey (SurveyorTableTag.TryImprintSlot) and consumes the press so the map
            // isn't also equipped/used. Replaces the retired auto-imprint-on-Use. Off-Table,
            // hotbar keys behave exactly as vanilla (the prefix returns true). Client-only:
            // Player.m_localPlayer is null on the dedicated server → pure pass-through. MUST be
            // registered here or it ships dead and PatchCheck ERRORs at boot (the t_564f695a
            // unregistered-patch lesson).
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Cartography.SurveyorTableHotbarImprintPatch));

            // v3 Swamp — Sunstone Lens (card t_2fd7bc7f). Two client-relevant patches:
            //  • SunstoneLens.DrainGate: Prefix on the PRIVATE Humanoid.DrainEquipedItemDurability
            //    (:13227) that takes over the per-tick durability change for OUR lens — drains at a
            //    fixed rate or RECHARGES in the sun, clamps to [0,max], and skips vanilla so the
            //    lens never breaks/unequips/destroys at zero (AC#5 inert-not-consumed). Pure
            //    pass-through for every other item / non-local player; inert on the dedicated server.
            //  • SunstoneLensHudOverlay.HudBootstrap: Postfix on Hud.Awake that builds the
            //    client-only detection HUD overlay under Hud.m_rootObject (the Iron Compass render
            //    doctrine — NoMap-safe, unlike minimap pins). Never fires on the dedicated server.
            // Both MUST be registered here or they ship dead and PatchCheck ERRORs at boot (the
            // unregistered-patch lesson). Nested patch containers are registered by their declaring type.
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Sunstone.SunstoneLens.DrainGate));
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Sunstone.SunstoneLensHudOverlay.HudBootstrap));

            // v3 Swamp — Iron Compass (card t_ee61472f). ONE client-relevant patch:
            //  • CompassHudBootstrapPatch: Postfix on Hud.Awake that mounts the client-only
            //    SBPR_CompassHud overlay under Hud.m_rootObject (the no-map orientation payoff —
            //    a camera-yaw needle with lag + pitch tilt, NoMap-safe, NEVER a north arrow on the
            //    map). The item + recipe are otherwise patch-free; only the overlay-mount needs
            //    Harmony. MUST be registered here or it ships dead and PatchCheck ERRORs at boot
            //    (the t_564f695a unregistered-patch lesson). Never fires on the dedicated server (no Hud).
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Exploration.CompassHudBootstrapPatch));

            Log.LogInfo($"[Trailborne] Harmony patches applied (DebugCairnDamage={DebugCairnDamage.Value}).");

            // Patch-registration watchdog (sibling of SpecCheck, card t_e8d24102). MUST be
            // the LAST line of Awake — after EVERY PatchAll above. Reflects over this
            // assembly for [HarmonyPatch] classes and asserts each actually wove a method
            // we own; any attributed-but-unregistered class ERROR-logs on boot. Born from
            // the dead CairnPlacementGatePatch (registered one line up): that class shipped
            // in v0.2.10 attributed-but-unwired and nobody noticed for weeks. If you add a
            // new [HarmonyPatch] class, register it above or this guard will scream.
            PatchCheck.Run();
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }
    }
}
