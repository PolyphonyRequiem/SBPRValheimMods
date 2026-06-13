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

            harmony = new Harmony(ModId);
            harmony.PatchAll(typeof(Registrar));
            harmony.PatchAll(typeof(CairnPatches));
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
            // Make the panel usable while open: block player character input, freeze
            // camera mouse-look (via PlayerController.TakeInput — the vanilla gate our
            // panel bypasses by replacing the sign text dialog), and release the mouse
            // cursor. Three nested patch classes — register each container type.
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Signs.SignPanelInputBlock.TakeInputPatch));
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Signs.SignPanelInputBlock.PlayerControllerTakeInputPatch));
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Signs.SignPanelInputBlock.MouseCapturePatch));
            // §2F (issue 7, card t_23b950ee): a Menu.Show skip-original prefix gated on
            // SignPanelInputBlock.AnyOpen, so the Escape that closes our map viewer / sign
            // panel does NOT also open the vanilla pause menu the same frame (AT-VIEWEXIT-1).
            // Fourth nested container in SignPanelInputBlock — MUST be registered here or it
            // ships dead and PatchCheck ERRORs at boot (the t_564f695a unregistered-patch
            // lesson). Self-clearing + server-safe (AnyOpen false → pass-through).
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Signs.SignPanelInputBlock.MenuOpenSuppressPatch));
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

            // §2E.3 — `sbpr_mapmode` console command: lets Daniel switch the Local Map render
            // path live between the vanilla styled "parchment" shader and the CPU composite, and
            // pick whichever looks right on his GPU. Postfixes Terminal.InitTerminal (same hook as
            // BannerDiagCommand). Registered here so the PatchCheck watchdog sees it woven.
            harmony.PatchAll(typeof(SBPR.Trailborne.Features.Cartography.MapModeCommand));

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
