using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Cairns
{
    /// <summary>
    /// Marker tag attached to each cairn clone. Carries color identity, ZDO-backed
    /// tier accessors, and the §A2.1b "rich visual" runtime art:
    ///
    ///   • A haphazard pile of vertically-squashed <c>rock_low</c> clones whose
    ///     COUNT equals the stone ladder (T1=9, T2=12, T3=15, T4=18, T5=21).
    ///   • Deterministic layout seeded from the ZDO id — identical on every client
    ///     and after reload. No per-frame RNG.
    ///   • Pigment tint applied to the stones from the cairn's bound color.
    ///   • A small HP-gated WEAR EMBER at the pile top, lit only at HP ≥ ~75%
    ///     (pristine) and fizzled below — toggled by a cheap health-bracket poll so
    ///     it relights on repair-UP, not only on damage-down.
    ///
    /// The cairn is cloned from the vanilla <c>bonfire</c> prefab as a STRUCTURAL
    /// base only (WearNTear / Piece / ZNetView). The bonfire is fundamentally a
    /// FIRE prefab, so on the client we NEUTRALIZE that fire deterministically — by
    /// component TYPE, not by guessing child names (the PR #23 path, kept exactly).
    /// The wear ember is a SEPARATE, hand-built element layered on top AFTER
    /// neutralization: it re-uses only a particle *material* reference, never the
    /// donor Fireplace / Light / EffectArea / AudioSource / SmokeSpawner. A cairn
    /// is a non-burning stone marker; the ember is a tiny decorative wear signal,
    /// NOT a light/heat/comfort source.
    ///
    /// This whole assembly is client-side art. The dedicated server is headless
    /// (no renderers/lights/particles) so the visual steps are inert there; the
    /// load-bearing gameplay (comfort floor, repair/upgrade, decay, damage-immunity)
    /// lives in CairnPatches.cs + CairnInteractable.cs and is untouched by this file.
    /// </summary>
    public class CairnTag : MonoBehaviour
    {
        public string Color = null!;          // set by registration immediately after AddComponent
        private ZNetView nview = null!;        // Unity-injected in Awake via GetComponent
        private WearNTear? wnt;                // Unity-injected in Awake via GetComponent (null on the rare prefab w/o WNT)
        private GameObject? kitbashRoot;       // genuinely null until/unless a rock pile is built (fallback path leaves it null)
        private GameObject? fireRoot;          // the grafted torch-tier flame (under kitbashRoot); null until BuildCosmeticFire runs
        private int lastBuiltTier = -1;
        private bool? lastFireLit;              // null until first poll, so the first tick always reconciles

        // ── §A2.1b tunables (FLAGGED for Daniel to eyeball on a joined client) ──────
        // These ranges are the "haphazard but stable" knobs. They are deterministic
        // per stone (seeded from the ZDO id) — only the *ranges* are judgement calls.
        // Sign-off note: once Daniel approves the look in-game, bake the chosen values
        // into §A2.1b. Until then they live here as the single source of truth.
        private const float PileBaseRadiusT1   = 0.42f;  // disk radius at the base, tier 1
        private const float PileBaseRadiusStep = 0.06f;  // +per tier (wider base as it grows)
        private const float PileHeightT1       = 0.34f;  // overall pile height, tier 1
        private const float PileHeightStep     = 0.12f;  // +per tier (taller as it grows)
        private const float PileBaseY          = 0.05f;  // lift the whole pile off the ground plane a touch
        private const float HeightExponent     = 1.6f;   // >1 clusters more stones toward the base (wider base, tapering up)
        private const float TopTaperFrac       = 0.78f;  // radius at the very top = base*(1-this); higher = pointier
        // Donor stones now render at NATIVE scale (Daniel 2026-06-08 — no per-stone
        // size/squash). Box02 AABB ≈ 0.32×0.19×0.65 m; half-height ≈ 0.10 m. Used only
        // to anchor the pile-top cosmetic flame now that stones aren't scaled.
        private const float StoneNativeHalfHeight = 0.12f;  // ~half the Box02 height + tilt slack
        private const float TiltDegrees        = 12f;    // ± random tilt on X/Z so stones don't sit perfectly level
        private const float PosJitterXZ        = 0.06f;  // ± extra lateral jitter on top of the disk sample
        private const float PosJitterY         = 0.04f;  // ± vertical jitter per stone
        private const float EmberHeightLift    = 0.10f;  // (legacy) kept for pile-top math compatibility

        // Cosmetic-fire Light tunables (Daniel 2026-06-07). Clearly below a vanilla
        // torch (~1.4 intensity / ~6-8 range in the current build) so the cairn reads
        // as a small marker fire, not a torch or bonfire. Eyeball in-game + adjust.
        private const float SubTorchLightIntensity = 0.8f;
        private const float SubTorchLightRange     = 4.0f;

        // Resident time-decay throttle: don't fire WearNTear.ApplyDamage (and its
        // RPC_HealthChanged fanout) until at least this much decay has accrued on the
        // shared clock. At the default 10 HP/in-game-day this is roughly one network
        // write every ~2 real minutes instead of one per 1 Hz poll — the sub-step
        // remainder stays banked on SBPR_LastWearTick and rolls into the next tick.
        private const float MinDecayHpStep = 1.0f;

        private void Awake()
        {
            nview = GetComponent<ZNetView>();
            wnt = GetComponent<WearNTear>();
            BuildKitbashArt(); // tier from ZDO or default 1
            // Health-bracket poll (1 Hz). THREE owner-authoritative responsibilities,
            // all keyed off the current HP so they fire path-independently:
            //   • RESIDENT TIME DECAY (the primary decay source) — accrue + apply wear
            //     while the cairn is loaded, so HP visibly falls in-zone in ANY weather
            //     (not just wet). This is the fix for "100% HP in a storm": vanilla wet
            //     UpdateWear is only an optional accelerant and can't even reach below
            //     50%, so the mod must drive its own time decay.
            //   • EMBER toggle — relights on repair-UP, fizzles on decay-DOWN.
            //   • AUTO-DOWNGRADE — drop a tier at <25%.
            // A single WearNTear.OnDamage postfix would miss repair-up and the resident
            // decay accrual. 1 s cadence is imperceptible for a fizzle/relight indicator
            // and an abandonment-decay tick, and costs ~nothing (a float read + compare;
            // the ApplyDamage write is throttled to ≥1 HP of accrued decay — see
            // ResidentTimeDecay — so it is NOT one RPC per second).
            InvokeRepeating(nameof(HpBracketTick), 1.0f, 1.0f);
        }

        public int ReadTier()
        {
            if (nview == null || nview.GetZDO() == null) return 1;
            int t = nview.GetZDO().GetInt(Cairns.ZdoTier, 1);
            if (t < 1) t = 1;
            if (t > Cairns.MaxTier) t = Cairns.MaxTier;
            return t;
        }

        public bool WriteTier(int newTier)
        {
            if (nview == null || nview.GetZDO() == null) return false;
            if (!nview.IsOwner()) nview.ClaimOwnership();
            if (newTier < 1) newTier = 1;
            if (newTier > Cairns.MaxTier) newTier = Cairns.MaxTier;
            nview.GetZDO().Set(Cairns.ZdoTier, newTier);
            BuildKitbashArt();
            return true;
        }

        /// <summary>
        /// Build the §A2.1b stone pile + grafted cosmetic flame for the current tier.
        ///
        /// Order matters and is deliberate:
        ///   1. There is NO donor fire to deal with — the cairn piece is built additively
        ///      from scratch (Assets.ConstructPieceShell), never cloned from bonfire, so
        ///      no Fireplace/Aoe/CinderSpawner ever existed on it (ADR-0006).
        ///   2. Build the haphazard, squashed, ZDO-seeded rock pile (count = the
        ///      stone ladder for this tier). BuildPile returns the pile-top Y.
        ///   3. Graft a small torch-tier flame (+ dim glow + crackle, NO heat/burn) at the
        ///      pile top via BuildCosmeticFire, parented under kitbashRoot so a tier
        ///      rebuild recycles it with the pile.
        ///   4. HP-gate the grafted flame via ReconcileFire (lit at pristine, out below).
        /// </summary>
        public void BuildKitbashArt()
        {
            int tier = ReadTier();
            // Rebuild whenever the tier changed OR we don't yet have a pile.
            if (tier == lastBuiltTier && kitbashRoot != null) return;
            lastBuiltTier = tier;

            // 🔴 v0.2.8 — there is NO donor fire on this piece, ever. The cairn is built
            // additively from scratch (Cairns.RegisterCairnPiecePrefab →
            // Assets.ConstructPieceShell), not cloned from bonfire — so there is no
            // Fireplace to muzzle, no Aoe burn, no CinderSpawner, no donor flame. We just
            // build our stone pile and graft a SMALL torch-tier flame on top — see
            // BuildCosmeticFire. (ADR-0006 additive construction; this whole class of
            // donor-fire-suppression work is gone because the donor is gone.)

            // Strip prior kitbash root (deferred destroy). This also drops the previous
            // build's grafted flame, since it lives under kitbashRoot.
            if (kitbashRoot != null) UnityEngine.Object.Destroy(kitbashRoot);

            var zns = ZNetScene.instance;
            if (zns == null) return;
            // Donor for the cairn pile — DELIBERATE CONSTRUCTION, not prefab cloning.
            //
            //   🔴 v0.2.7 pivot (Daniel 2026-06-07): we no longer Instantiate a vanilla
            //   prefab to build the pile. The old path cloned `Pickable_Stone` — which
            //   carries a ZNetView — onto the ACTIVE cairn at runtime, then DestroyImmediate'd
            //   that ZNetView. That nested-instantiate-inside-the-init-ZDO-window +
            //   DestroyImmediate orphaned null-ZDO entries in ZNetScene.m_instances, and
            //   vanilla ZNetScene.RemoveObjects dereferenced them every frame → client
            //   soft-lock (×21 "Double ZNetView" warnings → repeating NRE). Proven via
            //   offline prefab X-ray (`vprefab inspect Pickable_Stone`).
            //
            //   The fix: pull ONLY the bare mesh + material off the donor (no ZNetView ever
            //   instantiated), then hand-build each pile stone as a plain GameObject carrying
            //   only Transform + MeshFilter + MeshRenderer (see BuildPile). Nothing networked
            //   ever wakes, so there is nothing to orphan. Donor parts are read from a prefab
            //   that is NEVER itself instantiated — GetPrefab returns the inactive template,
            //   and reading its shared mesh/material does not fire Awake.
            var (stoneMesh, stoneMat, donorName) = ResolveStoneArt(zns);
            if (stoneMesh == null)
            {
                // Fallback — no donor stone art available. With the donor fire stripped
                // there is no flaming stub to worry about; the cairn simply shows nothing
                // visual this build (still a valid, non-burning piece). Reconcile the
                // grafted flame's lit-state in case a later rebuild succeeds.
                ReconcileFire();
                Plugin.Log.LogWarning(
                    "[Trailborne/M2] No donor stone mesh found (tried Pickable_Stone, Pickable_StoneRock); " +
                    "cairn shows no rock pile this build (donor fire already stripped — no flaming stub).");
                return;
            }

            kitbashRoot = new GameObject("SBPR_CairnKitbash");
            kitbashRoot.transform.SetParent(transform, worldPositionStays: false);

            // §A2.1b: pile count = the stone ladder. T1=9, T2=12, T3=15, T4=18, T5=21.
            int stones = Cairns.StoneCostForTier(tier);

            // BuildPile returns the local-Y of the pile top — anchor the flame there.
            float pileTopY = BuildPile(stoneMesh, stoneMat, kitbashRoot.transform, tier, stones);

            // Color identity: plant a wind-responsive BANNER at the pile, its cloth
            // material chosen by the cairn's bound color. Replaces the old stone tint
            // (Daniel 2026-06-08). Additive: we read only the banner's cloth mesh +
            // material off the vanilla donor and hand-build a SkinnedMeshRenderer + Cloth +
            // ClothWindDriver WINDSOCK that streams downwind from an elevated, hard-pinned
            // mount with a free-falling tail (card t_4a4a9706) — no ZNetView, no Piece, no
            // pole (ADR-0006). Cloth/SMR/driver are all cosmetic Unity components; the live-
            // config dimensions are baked into a per-instance mesh so the Cloth simulates
            // under a UNIFORM transform (no skew).
            BuildBanner(kitbashRoot.transform, pileTopY);

            // Graft a SMALL torch-tier cosmetic flame (+ dim light + crackle) at the pile
            // top, parented under kitbashRoot so a tier rebuild recycles it with the pile.
            BuildCosmeticFire(kitbashRoot.transform, pileTopY);

            // HP-gate the grafted flame (lit at pristine ≥75% HP, out below) — same wear
            // signal the old ember used, now driving our own flame instead of the donor's.
            ReconcileFire();
        }

        /// <summary>
        /// Lay out <paramref name="stones"/> squashed <c>rock_low</c> clones as a
        /// haphazard, deterministic pile under <paramref name="parent"/>. Returns the
        /// local-Y of the pile top (where the ember anchors).
        ///
        /// Determinism: every random draw comes from a single <see cref="System.Random"/>
        /// seeded off the ZDO id, so the pile is byte-identical on every client and
        /// after a reload. Pure function of (ZDO id, tier) — no per-frame RNG.
        ///
        /// Shape: stones are area-uniformly sampled inside a disk whose radius shrinks
        /// with height; heights are base-weighted (HeightExponent &gt; 1) so the pile is
        /// wider at the bottom and tapers up. Each stone is squashed (vertical scale =
        /// horizontal scale × a per-stone flatten ratio) so they read as flattish piled
        /// rocks, not boulders, with per-stone variation in both size and flatten.
        /// </summary>
        private float BuildPile(Mesh stoneMesh, Material? stoneMat, Transform parent, int tier, int stones)
        {
            int seed = 1337;
            if (nview != null && nview.GetZDO() != null)
                seed = nview.GetZDO().m_uid.GetHashCode();
            var rng = new System.Random(seed);

            float baseRadius = PileBaseRadiusT1 + PileBaseRadiusStep * (tier - 1);
            float pileHeight = PileHeightT1     + PileHeightStep     * (tier - 1);

            // NO TINT (Daniel 2026-06-08): cairn stones render in their NATURAL grey —
            // color identity now lives on the wind-responsive BANNER (BuildBanner), not
            // a pigment multiply on the rock. We share the donor's own material across
            // every stone (one material, draw-call-cheap, never mutated — reading a
            // shared asset is not cloning, ADR-0006).
            Material? stoneSharedMat = stoneMat;

            float maxStoneTop = PileBaseY;

            for (int i = 0; i < stones; i++)
            {
                // DELIBERATE CONSTRUCTION: a plain GameObject with ONLY Transform +
                // MeshFilter + MeshRenderer. No ZNetView, no Pickable, no collider, no
                // Instantiate of a networked prefab — so nothing registers a ZDO and
                // there is nothing for ZNetScene.RemoveObjects to choke on. This is the
                // v0.2.7 pivot away from runtime prefab-cloning.
                var stone = new GameObject("SBPR_CairnStone");
                stone.transform.SetParent(parent, worldPositionStays: false);
                var mf = stone.AddComponent<MeshFilter>();
                mf.sharedMesh = stoneMesh;
                var mr = stone.AddComponent<MeshRenderer>();
                if (stoneSharedMat != null) mr.sharedMaterial = stoneSharedMat;
                // Cosmetic-only: no shadows budget needed for pebble-scale art.
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                // Height: base-weighted so more stones sit low (wider base, tapering up).
                float k = stones <= 1 ? 0f : (float)i / (stones - 1); // 0 bottom .. 1 top
                float hf = Mathf.Pow(k, HeightExponent);
                float y = PileBaseY + pileHeight * hf + (float)(rng.NextDouble() * 2.0 - 1.0) * PosJitterY;

                // Radius at this height shrinks toward the top; area-uniform disk sample.
                float maxR = baseRadius * (1f - TopTaperFrac * k);
                float ang = (float)(rng.NextDouble() * Math.PI * 2.0);
                float r = maxR * Mathf.Sqrt((float)rng.NextDouble());
                float ox = Mathf.Cos(ang) * r + (float)(rng.NextDouble() * 2.0 - 1.0) * PosJitterXZ;
                float oz = Mathf.Sin(ang) * r + (float)(rng.NextDouble() * 2.0 - 1.0) * PosJitterXZ;

                // NO SCALING (Daniel 2026-06-08): the donor rock mesh is rendered at its
                // NATIVE proportions. The previous build scaled each stone by a per-stone
                // size (0.55–0.95×) and squashed it vertically by a flatten ratio, which
                // read as tiny flat discs. We now keep the donor `Box02` mesh at scale 1
                // and rely on haphazard POSITION + ROTATION alone for the piled look.
                stone.transform.localPosition = new Vector3(ox, y, oz);
                stone.transform.localRotation = Quaternion.Euler(
                    (float)(rng.NextDouble() * 2.0 - 1.0) * TiltDegrees,   // slight random tilt X
                    (float)(rng.NextDouble() * 360.0),                     // random yaw
                    (float)(rng.NextDouble() * 2.0 - 1.0) * TiltDegrees);  // slight random tilt Z
                stone.transform.localScale = Vector3.one;                  // native mesh size

                // Pile-top anchor (for the cosmetic flame): the stone's centre Y plus its
                // native half-height. StoneNativeHalfHeight is a FLAGGED eyeball constant —
                // the Box02 AABB is ~0.19 m tall (half ≈ 0.10 m), nudged for tilt slack.
                float stoneTop = y + StoneNativeHalfHeight;
                if (stoneTop > maxStoneTop) maxStoneTop = stoneTop;
            }

            return maxStoneTop + EmberHeightLift;
        }

        /// <summary>
        /// Resolve the donor stone ART (mesh + material) WITHOUT instantiating a networked
        /// prefab. Reads the shared mesh off the donor's MeshFilter and the shared material
        /// off its Renderer — both are asset references on the inactive prefab template, so
        /// reading them fires no Awake and creates no ZDO. Returns (mesh, material, name) or
        /// (null, null, null) if no suitable donor is registered.
        ///
        /// Donor priority: Pickable_Stone (the small grey ground pebble — mesh "Box02",
        /// ~0.32×0.19×0.65 m, verified via offline prefab X-ray), then Pickable_StoneRock.
        /// </summary>
        private static (Mesh?, Material?, string?) ResolveStoneArt(ZNetScene zns)
        {
            foreach (var candidate in new[] { "Pickable_Stone", "Pickable_StoneRock" })
            {
                var src = zns.GetPrefab(candidate);
                if (src == null) continue;
                // The renderable mesh sits on a child (e.g. "model (1)"), not the root —
                // GetComponentInChildren walks the inactive hierarchy without activating it.
                var mf = src.GetComponentInChildren<MeshFilter>(includeInactive: true);
                if (mf == null || mf.sharedMesh == null) continue;
                var mr = src.GetComponentInChildren<MeshRenderer>(includeInactive: true);
                var mat = mr != null ? mr.sharedMaterial : null;
                return (mf.sharedMesh, mat, candidate);
            }
            return (null, null, null);
        }

        // ── Banner (color identity, wind-responsive) ───────────────────────────────

        // Color → vanilla banner donor prefab. The cloth carries color identity, and its
        // motion is REAL wind-responsive cloth physics (A-prime, t_e95949c2): we graft a
        // UnityEngine.Cloth onto a SkinnedMeshRenderer built from a per-instance copy of the
        // donor cloth mesh, pin its top edge, and drive externalAcceleration/randomAcceleration
        // from the world wind via the reusable ClothWindDriver (see BuildBanner). The donor
        // cloth child itself carries zero scripts (offline X-ray) — vanilla DECORATIVE banners
        // only shader-waggle in place — so directional streaming is something we ADD, the same
        // Cloth mechanism vanilla SAILS/CAPES/tents use via GlobalWind. The bare material's
        // vertex-shader waggle still runs underneath as ambient surface ripple. Mapping LOCKED
        // with Daniel 2026-06-08. (Secondary tones are the donor's — 02 is blue/yellow, 11 is
        // black/white-inverted — flagged for in-game eyeball.)
        private static readonly Dictionary<string, string> BannerDonorFor = new Dictionary<string, string>
        {
            { "black", "piece_banner01" },   // Banner_Border_BlackWhite_mat
            { "blue",  "piece_banner02" },   // Banner_Border_BlueYellow_1_mat
            { "red",   "piece_banner04" },   // Banner_Border_RedWhite_1_mat
            { "white", "piece_banner11" },   // Banner_Border_BlackWhiteInverted_mat
        };

        // ── WINDSOCK tunables (card t_4a4a9706 — supersedes the A-prime square-drape seating) ──
        //
        // 🔴 WHY THESE ARE ALL LIVE CONFIG (not the const-only A-prime set): this banner is a
        // pure CLIENT visual. It cannot be verified headless or in CI, and it has shipped wrong
        // IN-WORLD TWICE while building 0/0 (first: shader-only waggle; second: stiff in-place
        // vibration — "zigs and zags on the spot"). Guessing un-testable metres burned two
        // playtests. So every knob below is exposed as a BepInEx `CairnBanner` config entry
        // (Plugin.Banner*). These `Default*` consts are the FALLBACK (single source of truth for
        // a no-Plugin unit context) and the STARTING eyeball; the live value wins at runtime.
        // BuildBanner reads `Plugin.Banner*?.Value ?? Default*`. Daniel converges the windsock
        // feel in ONE joined session, then we bake the chosen metres into §A2.1b.
        //
        // The donor `default` cloth is a flat Y-Z sheet (native bounds ≈ (X 0, Y 2.974, Z 1.1802),
        // vprefab-verified on piece_banner01/02/04/11 — all identical), pivot at the cloth TOP so
        // it HANGS DOWN. We measure-and-normalize Z→WIDTH, Y→DROP (X zero-thickness, inert) and
        // BAKE the result into a PER-INSTANCE mesh's VERTICES (UnityEngine.Cloth's solver SHEARS
        // under a skewed lossyScale, so the transform must stay UNIFORM — AC10 kept from A-prime).
        //
        // WINDSOCK redesign vs the prior square-drape:
        //   • Seat a SMALL mount band ELEVATED above the pile crown (BannerMountHeight), and let a
        //     LONGER, NARROWER tail hang DOWN past the cairn (Daniel: "mount above the cairn, tail
        //     free-falls below … flop in the wind like a windsock"). Was: a short square drape
        //     hugging the upper pile, seated half-a-drop above the crown.
        //   • STRONGLY ASYMMETRIC freedom: the mount band is hard-pinned (maxDistance 0); the tail
        //     freedom ramps maxDistance = FreeDistance × (depthBelowMount/span)^RampExp with a
        //     LARGE FreeDistance and RampExp > 1, so the far tail genuinely FLOPS/streams while the
        //     mount stays fixed. Was: a near-linear ramp at FreeDistance 1.0 → near-uniform low
        //     freedom → the whole sheet vibrated in place.
        //   • useGravity = true so the tail FREE-FALLS slack on build, then wind streams it.
        //   • Lower RandomFactor so the directional (downwind) term dominates the omnidirectional
        //     jitter — the "zigs and zags on the spot" is random flutter competing with streaming.
        public const float DefaultBannerWidthZ            = 0.18f;  // ribbon WIDTH, m (Z) — narrower → reads as a tail/sock, not a square flag
        public const float DefaultBannerDropY             = 1.15f;  // tail LENGTH, m (Y) — longer than the old 0.892 square drape → a streaming tail
        public const float DefaultBannerMountHeight       = 0.70f;  // mount (pinned end) height above the pile crown, m — elevates the tether so the tail free-falls past the cairn while clearing T1 ground at rest
        public const float DefaultBannerOffsetXZ          = 0.30f;  // lateral nudge off the pile centre (clears the flame)
        public const float DefaultBannerWindMult          = 1.0f;   // directional multiplier (vanilla GlobalWind default; intensity response already confirmed at 1.0)
        public const float DefaultBannerWindRandomFactor  = 0.25f;  // omnidirectional jitter (LOWER than vanilla 0.5 → directional streaming dominates)
        public const float DefaultBannerClothDamping      = 0.10f;  // Cloth.damping — low → lively/floppy tail
        public const float DefaultBannerClothFreeDistance = 3.0f;   // max tail travel (cloth units) — LARGE → the far tail flops/streams
        public const float DefaultBannerFreeRampExp       = 2.0f;   // freedom ramp exponent (mount→tail); >1 concentrates flap at the far tail
        public const float DefaultBannerPinBandFrac       = 0.04f;  // mount hard-pin band, FRACTION of Y-span (small mount cluster)
        public const bool  DefaultBannerUseGravity        = true;   // free-fall slack on build, then flop in the wind

        // Live config accessors — the runtime value, or the Default* fallback when Plugin isn't
        // bound (unit context). Centralized so BuildBanner reads one name per knob.
        private static float CfgBannerWidthZ      => Plugin.BannerWidthZ            != null ? Plugin.BannerWidthZ.Value            : DefaultBannerWidthZ;
        private static float CfgBannerDropY       => Plugin.BannerDropY             != null ? Plugin.BannerDropY.Value             : DefaultBannerDropY;
        private static float CfgBannerMountHeight => Plugin.BannerMountHeight       != null ? Plugin.BannerMountHeight.Value       : DefaultBannerMountHeight;
        private static float CfgBannerOffsetXZ    => Plugin.BannerOffsetXZ          != null ? Plugin.BannerOffsetXZ.Value          : DefaultBannerOffsetXZ;
        private static float CfgBannerWindMult    => Plugin.BannerWindMult          != null ? Plugin.BannerWindMult.Value          : DefaultBannerWindMult;
        private static float CfgBannerRandom      => Plugin.BannerWindRandomFactor  != null ? Plugin.BannerWindRandomFactor.Value  : DefaultBannerWindRandomFactor;
        private static float CfgBannerDamping     => Plugin.BannerClothDamping      != null ? Plugin.BannerClothDamping.Value      : DefaultBannerClothDamping;
        private static float CfgBannerFreeDist    => Plugin.BannerClothFreeDistance != null ? Plugin.BannerClothFreeDistance.Value : DefaultBannerClothFreeDistance;
        private static float CfgBannerRampExp     => Plugin.BannerFreeRampExp       != null ? Plugin.BannerFreeRampExp.Value       : DefaultBannerFreeRampExp;
        private static float CfgBannerPinBandFrac => Plugin.BannerPinBandFrac       != null ? Plugin.BannerPinBandFrac.Value       : DefaultBannerPinBandFrac;
        private static bool  CfgBannerUseGravity  => Plugin.BannerUseGravity        != null ? Plugin.BannerUseGravity.Value        : DefaultBannerUseGravity;

        /// <summary>
        /// Attach the wind-responsive color BANNER to the pile as a WINDSOCK (card t_4a4a9706).
        /// Reads ONLY the cloth child's mesh + material off the vanilla banner donor (the
        /// color-bearing <c>default</c> child — NOT the <c>woodbeam</c> pole) and hand-builds an
        /// additive GameObject carrying a <see cref="SkinnedMeshRenderer"/> + <see cref="Cloth"/> +
        /// <see cref="ClothWindDriver"/>. A SMALL MOUNT band at the top is hard-pinned and seated
        /// ELEVATED above the pile crown; the LONGER, NARROWER tail hangs DOWN past the cairn,
        /// free-falls slack under gravity on build, then STREAMS downwind with world wind
        /// direction + force — the windsock Daniel asked for, instead of a square drape vibrating
        /// in place. The tail freedom ramps STEEPLY (mount near-rigid, far tail fully free) so it
        /// flops rather than zig-zags. Same Cloth mechanism vanilla sails/capes/tents use via
        /// GlobalWind, re-rooted for a one-end-tethered tail.
        ///
        /// 🔴 ALL look-shaping knobs are LIVE BepInEx config (Plugin.Banner* → Cfg* accessors,
        /// Default* fallback). This banner is a pure client visual that cannot be verified
        /// headless/CI and has shipped wrong in-world TWICE while building 0/0; live config lets
        /// Daniel converge the windsock feel in ONE joined session, then we bake §A2.1b.
        ///
        /// 🔴 Sizing (AC10 kept): the measured proportions (tail length × width, both live config)
        /// are BAKED INTO A PER-INSTANCE MESH (the donor mesh's VERTICES are scaled), NOT applied
        /// as a non-uniform transform.localScale. UnityEngine.Cloth's solver shears under a
        /// skewed lossyScale, so the transform must stay UNIFORM; the rest-shape carries the
        /// dimensions instead. We never mutate the shared donor mesh — we Instantiate a copy,
        /// scale that, and a <see cref="DestroyMeshOnDestroy"/> janitor frees it on rebuild.
        ///
        /// Nothing networked is ever instantiated (ADR-0006): Cloth + SkinnedMeshRenderer +
        /// driver + the baked mesh are all cosmetic client assets, no ZNetView / Piece / pole.
        /// No-op on a headless server (no graphics device → no Cloth) and on an unknown color.
        /// </summary>
        private void BuildBanner(Transform parent, float pileTopY)
        {
            // Cloth is client-side cosmetic physics — a headless dedicated server has no
            // graphics device and the donor renderers/meshes are stripped there, so there is
            // nothing to drive. Bail before spinning up a SkinnedMeshRenderer + Cloth.
            if (IsHeadless()) return;

            var zns = ZNetScene.instance;
            if (zns == null) return;

            string color = (Color ?? "").ToLowerInvariant();
            if (!BannerDonorFor.TryGetValue(color, out var donorName))
            {
                // Unknown color → no banner this build (still a valid, non-tinted cairn).
                return;
            }

            var (donorMesh, clothMat) = ResolveBannerCloth(zns, donorName);
            if (donorMesh == null)
            {
                // Donor missing/changed — warn (we're on a client; headless already bailed).
                if (clothMat == null)
                    Plugin.Log.LogWarning(
                        $"[Trailborne/M2] Cairn banner: donor '{donorName}' cloth mesh not found; " +
                        "cairn shows no banner this build (color identity falls back to none).");
                return;
            }

            // ── Bake the windsock dimensions into a PER-INSTANCE mesh (AC10) ───────────
            // Measure-and-normalize off the DONOR's honest bounds (real metres): Z→width,
            // Y→drop, X≈0 inert. Apply to the VERTICES of a private copy so the Cloth runs
            // under a UNIFORM transform (the solver shears under a skewed lossyScale). The
            // target metres are LIVE CONFIG (CfgBanner*) so Daniel tunes the tail in-game.
            // Never touch the shared donor mesh.
            float dropY  = CfgBannerDropY;
            float widthZ = CfgBannerWidthZ;
            Vector3 native = donorMesh.bounds.size;                 // ≈ (0, 2.974, 1.1802)
            float sy = native.y > 1e-3f ? dropY  / native.y : 1f;
            float sz = native.z > 1e-3f ? widthZ / native.z : 1f;

            Mesh bakedMesh = Instantiate(donorMesh);               // private, mutable copy
            bakedMesh.name = donorMesh.name + "_SBPRbaked";
            if (!bakedMesh.isReadable)
            {
                // Can't read/rewrite verts → can't bake dims OR pin the cloth mount. Rather than
                // ship a wrong-sized blow-away banner, drop the banner this build and shout —
                // the donor's readability changed in a game patch and the spec needs re-grounding.
                Plugin.Log.LogError(
                    $"[Trailborne/M2] Cairn banner: donor cloth mesh '{donorMesh.name}' is NON-READABLE; " +
                    "cannot bake dimensions or pin the cloth mount. Skipping banner — re-ground the windsock spec " +
                    "(donor mesh readability likely changed in a game patch).");
                Destroy(bakedMesh);
                return;
            }
            Vector3[] verts = bakedMesh.vertices;                  // copy-out; readable confirmed above
            for (int i = 0; i < verts.Length; i++)
                verts[i] = new Vector3(verts[i].x, verts[i].y * sy, verts[i].z * sz);
            bakedMesh.vertices = verts;
            bakedMesh.RecalculateBounds();                         // bounds now ≈ widthZ(Z) × dropY(Y)

            // ── Build the graft GameObject ────────────────────────────────────────────
            var banner = new GameObject("SBPR_CairnBanner");
            banner.transform.SetParent(parent, worldPositionStays: false);

            // Free the baked per-instance mesh on EVERY teardown (tier rebuild destroys the
            // kitbash root, cairn removal, scene unload) — a runtime Mesh is not GC'd with its
            // GameObject, so without this we'd leak one mesh per rebuild.
            banner.AddComponent<DestroyMeshOnDestroy>().Owned = bakedMesh;

            // 🔴 WINDSOCK seating (card t_4a4a9706): the cloth pivot sits at the mesh TOP (its
            // mount/pinned end) and the ribbon hangs DOWN from it. Seat the mount ELEVATED a
            // full BannerMountHeight ABOVE the pile crown so the tail FREE-FALLS down past the
            // cairn as a proper tail (Daniel: "mount above the cairn, tail free-falls below …
            // flop in the wind like a windsock"). This replaces the prior half-drop seat that
            // hugged the upper pile as a short square drape. Nudge off-centre (deterministic
            // side per ZDO → stable across reloads) so the tail clears the cosmetic flame.
            int seed = (nview != null && nview.GetZDO() != null) ? nview.GetZDO().m_uid.GetHashCode() : 1337;
            float side = ((seed & 1) == 0) ? 1f : -1f;
            banner.transform.localPosition = new Vector3(
                CfgBannerOffsetXZ * side,
                pileTopY + CfgBannerMountHeight,
                0f);
            // UNIFORM scale only — Cloth simulates correctly under uniform lossyScale, not
            // skewed. The dimensions live in the baked mesh, not here.
            banner.transform.localScale = Vector3.one;

            // Cloth REQUIRES a SkinnedMeshRenderer (it simulates that renderer's vertices).
            // The baked mesh has no bones/skin; the SMR renders it with identity skinning and
            // Cloth then drives the vertices.
            var smr = banner.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = bakedMesh;
            if (clothMat != null) smr.sharedMaterial = clothMat;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            smr.receiveShadows = false;
            smr.updateWhenOffscreen = false;   // perf (AC4); explicit roomy bounds below avoid edge-cull pop
            // The streaming tail swings well beyond the rest-mesh AABB; give it a generous
            // mesh-local box (pre-transform, pivot at the mount) so a near-horizontal tail
            // isn't culled at the screen edge. Sized off the (live) tail length with swing slack.
            smr.localBounds = new Bounds(
                new Vector3(0f, -dropY * 0.5f, 0f),
                new Vector3(dropY * 2f, dropY * 2.5f, dropY * 2f));

            // Now the physics: add Cloth, pin only a SMALL MOUNT band at the top, and ramp the
            // tail freedom steeply so the far end genuinely FLOPS/streams (not vibrates in place).
            // Pin AFTER the bake so coefficients are computed against the BAKED vertex positions.
            var cloth = banner.AddComponent<Cloth>();
            // Gravity: free-fall slack on build, then wind streams it (Daniel's "free fall when
            // built, then flop in the wind"). Live-config so it can be A/B'd in-game.
            cloth.useGravity = CfgBannerUseGravity;
            // Damping is the flop-vs-stiff knob; low = lively tail. Live-config.
            cloth.damping = CfgBannerDamping;
            PinMountCloth(cloth, bakedMesh, CfgBannerPinBandFrac, CfgBannerFreeDist, CfgBannerRampExp);

            // Drive it from world wind (direction × force) on a ~2 s cadence (AC1/AC2/AC4).
            // RandomFactor is lowered from vanilla 0.5 so the DIRECTIONAL term dominates the
            // omnidirectional jitter — the prior "zigs and zags on the spot" was random flutter
            // drowning out the downwind stream. All live-config.
            var driver = banner.AddComponent<ClothWindDriver>();
            driver.Multiplier = CfgBannerWindMult;
            driver.RandomFactor = CfgBannerRandom;
            driver.CheckPlayerShelter = false; // cairns are open-air trail markers
        }

        /// <summary>
        /// Build the <see cref="Cloth"/> skinning coefficients for the WINDSOCK banner: hard-pin
        /// a SMALL MOUNT band at the top (maxDistance 0, anchored to the elevated mount) and ramp
        /// the freedom of every lower particle STEEPLY toward the tail so the far end genuinely
        /// FLOPS/streams while the mount stays fixed (card t_4a4a9706). The freedom of a particle
        /// at normalized depth <c>d = depthBelowMount/span ∈ [0,1]</c> is
        /// <c>maxDistance = freeDistance × d^rampExp</c> — with <paramref name="rampExp"/> &gt; 1
        /// the band near the mount stays near-rigid and the flapping concentrates at the far tail
        /// (windsock feel), and a LARGE <paramref name="freeDistance"/> lets that tail travel far
        /// instead of vibrating in place. This is the fix for the prior "zigs and zags on the spot"
        /// (a near-linear ramp at freeDistance 1.0 gave the whole sheet similar low freedom).
        ///
        /// 🔴 Mapping correctness (the trap this method exists to avoid): UnityEngine.Cloth
        /// WELDS coincident vertices, so <c>cloth.coefficients.Length</c> can be SMALLER than
        /// <c>mesh.vertexCount</c> (verified: the donor cloth is 78 mesh verts but only 71
        /// unique positions — 7 coincident pairs). Indexing coefficients 1:1 against
        /// <c>mesh.vertices</c> would mis-map every coefficient past the first weld → wrong
        /// pins → blow-away or stuck banner that COMPILES CLEAN. We therefore map against the
        /// authoritative <see cref="Cloth.vertices"/> array (always == coefficients.Length),
        /// falling back to mesh.vertices only if Unity returns a 1:1 length. If neither array
        /// matches the coefficient count we log loudly and leave the cloth unpinned rather
        /// than guess.
        ///
        /// <paramref name="mountPinBandFrac"/> is a FRACTION of the Y-span (not metres) so the
        /// SAME mount row is caught regardless of the per-instance Y-bake scale.
        /// </summary>
        private static void PinMountCloth(Cloth cloth, Mesh bakedMesh, float mountPinBandFrac, float freeDistance, float rampExp)
        {
            var coeffs = cloth.coefficients;
            if (coeffs == null || coeffs.Length == 0)
            {
                Plugin.Log.LogError(
                    "[Trailborne/M2] Cairn banner: Cloth produced 0 skinning coefficients; " +
                    "mount NOT pinned (banner may blow away). Cloth setup likely rejected the mesh.");
                return;
            }
            int n = coeffs.Length;

            // Pick the vertex source whose length matches the coefficient count. Cloth.vertices
            // is the welded particle set (authoritative); mesh.vertices only matches when Unity
            // did no welding. Mismatch on both → bail loudly (don't pin against the wrong array).
            Vector3[] clothVerts = cloth.vertices;
            Vector3[] verts;
            if (clothVerts != null && clothVerts.Length == n)
            {
                verts = clothVerts;
            }
            else
            {
                Vector3[] meshVerts;
                try { meshVerts = bakedMesh.vertices; }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError(
                        $"[Trailborne/M2] Cairn banner: cloth mesh '{bakedMesh.name}' vertices unreadable " +
                        $"({e.GetType().Name}) and Cloth.vertices length {(clothVerts?.Length ?? -1)} != " +
                        $"coefficients {n}; mount NOT pinned. Banner may not anchor correctly.");
                    return;
                }
                if (meshVerts.Length == n)
                {
                    verts = meshVerts;
                }
                else
                {
                    Plugin.Log.LogError(
                        $"[Trailborne/M2] Cairn banner: cannot map cloth coefficients — coeff count {n} " +
                        $"matches neither Cloth.vertices ({(clothVerts?.Length ?? -1)}) nor mesh.vertices " +
                        $"({meshVerts.Length}). Mount NOT pinned (banner may blow away). Donor cloth " +
                        "topology likely changed in a game patch; re-ground the windsock spec.");
                    return;
                }
            }

            float yMax = float.NegativeInfinity, yMin = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                float y = verts[i].y;
                if (y > yMax) yMax = y;
                if (y < yMin) yMin = y;
            }
            float span = yMax - yMin;
            if (span <= 1e-5f) span = 1f;
            float pinBand = mountPinBandFrac * span;   // fraction → absolute, in the baked space
            if (rampExp < 0.01f) rampExp = 0.01f;      // guard: never a zero/negative exponent

            int pinned = 0;
            for (int i = 0; i < n; i++)
            {
                float fromTop = yMax - verts[i].y;          // 0 at the mount edge, grows toward the tail
                if (fromTop <= pinBand)
                {
                    coeffs[i].maxDistance = 0f;              // hard-pinned mount cluster
                    pinned++;
                }
                else
                {
                    // Steep ramp: near-0 just below the mount, large at the far tail.
                    float depth = fromTop / span;            // 0 at mount .. 1 at tail
                    coeffs[i].maxDistance = freeDistance * Mathf.Pow(depth, rampExp);
                }
                coeffs[i].collisionSphereDistance = 0f;      // no self-collision constraint
            }
            cloth.coefficients = coeffs;

            if (pinned == 0)
                Plugin.Log.LogWarning(
                    $"[Trailborne/M2] Cairn banner: mount-pin band {mountPinBandFrac:0.###}×span matched NO " +
                    $"vertices on '{bakedMesh.name}' (yMax {yMax:0.###}); banner is unanchored and may blow " +
                    "away. Widen SBPR_BannerMountPinBandFrac.");
        }

        /// <summary>
        /// Resolve the banner CLOTH art (mesh + material) off a vanilla banner donor
        /// WITHOUT instantiating it. The renderable banner is split across two children:
        /// <c>woodbeam</c> (the pole — material <c>woodwall</c>) and <c>default</c> (the
        /// color cloth — material <c>Banner_Border_*_mat</c>). We want ONLY the cloth, so
        /// we pick the child whose material is NOT the shared <c>woodwall</c> pole
        /// material. Reading shared mesh/material on the inactive template fires no Awake.
        /// Returns (mesh, material) or (null, null) when unavailable (e.g. headless).
        /// </summary>
        private static (Mesh?, Material?) ResolveBannerCloth(ZNetScene zns, string donorName)
        {
            var src = zns.GetPrefab(donorName);
            if (src == null) return (null, null);

            MeshFilter[] filters = src.GetComponentsInChildren<MeshFilter>(includeInactive: true);
            foreach (var mf in filters)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                var mat = mr != null ? mr.sharedMaterial : null;
                // The cloth is the child whose material is NOT the wooden pole.
                string matName = mat != null ? mat.name : "";
                if (matName.IndexOf("woodwall", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (mf.sharedMesh.name.IndexOf("Cube", StringComparison.OrdinalIgnoreCase) >= 0) continue; // pole beam mesh
                return (mf.sharedMesh, mat);
            }
            return (null, null);
        }

        /// <summary>True on a headless dedicated server (no graphics device).</summary>
        private static bool IsHeadless() =>
            UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        // ── Cosmetic fire (HP-gated) ───────────────────────────────────────────────

        /// <summary>
        /// True when HP is at/above the pristine fraction (cosmetic fire lit).
        /// </summary>
        private bool FireShouldBeLit()
        {
            if (wnt == null) return true; // no WearNTear → treat as pristine (always lit)
            return wnt.GetHealthPercentage() >= Cairns.PristineHpFraction;
        }

        /// <summary>
        /// Toggle the donor's COSMETIC fire (flame particles + crackle audio + the
        /// dimmed Light) to match the current HP bracket, only when it actually flips.
        /// Cheap no-op most ticks. Lit at pristine (≥75% HP), off below — so the player
        /// reads "fizzled" as a clear visual wear state (the fire goes OUT), and a
        /// repair-to-pristine relights it.
        ///
        /// We enable/disable the donor's fire-bearing COMPONENTS directly (rather than
        /// SetActive on a wrapper GameObject) because the fire is spread across the
        /// donor's child objects, not under a single toggle root. Specifically toggled:
        /// the donor ParticleSystem emission (flame VFX), the kept Light (glow), and the
        /// kept fire AudioSource/ZSFX (crackle). The Fireplace component stays put — it's
        /// already infinite-fuel/heat-less; we just hide its visuals when fizzled.
        ///
        /// Excludes our rock kitbash (IsKitbash) so toggling the fire never touches the
        /// stone pile. No-op on a headless server (no particles/lights/audio exist).
        /// </summary>
        private void ReconcileFire()
        {
            bool lit = FireShouldBeLit();
            if (lastFireLit.HasValue && lit == lastFireLit.Value) return;
            lastFireLit = lit;
            SetFireVisible(lit);
        }

        /// <summary>
        /// Graft the small torch-tier cosmetic fire onto the pile top. Builds
        /// <see cref="fireRoot"/> via <see cref="Assets.GraftTorchFire"/> (flame VFX +
        /// dim glow + crackle, NO heat/burn/fuel — see that method for the clean-room /
        /// no-orphan rationale). Parented under kitbashRoot so a tier rebuild recycles it
        /// with the pile. No-op on a headless server (GraftTorchFire returns null — no
        /// particles/lights exist there), which is correct: the fire is pure client art.
        /// </summary>
        private void BuildCosmeticFire(Transform parent, float pileTopY)
        {
            fireRoot = Assets.GraftTorchFire(
                parent, pileTopY, SubTorchLightIntensity, SubTorchLightRange);
            // fireRoot may be null (headless / missing donor) — every fire op below
            // null-guards, so a null fireRoot simply means "no visible flame this build".
        }

        /// <summary>
        /// Show/hide the grafted cosmetic fire by toggling <see cref="fireRoot"/> active.
        /// Because the flame is now our OWN isolated subtree (built by BuildCosmeticFire,
        /// not the donor bonfire spread across the prefab), a single SetActive is the
        /// whole job — no per-component IsKitbash filtering, no fighting a live Fireplace.
        /// No-op when fireRoot is null (headless server, or donor torch missing).
        /// </summary>
        private void SetFireVisible(bool on)
        {
            if (fireRoot == null) return;
            if (fireRoot.activeSelf != on) fireRoot.SetActive(on);
        }

        /// <summary>
        /// Health-bracket poll (1 Hz via InvokeRepeating). THREE responsibilities, all
        /// keyed off the current HP so they fire path-independently (repair-UP, decay-DOWN,
        /// backfill):
        ///
        ///   0. RESIDENT TIME DECAY (owner-only) — the PRIMARY decay source. Accrue the
        ///      in-game-days elapsed since the last wear tick (shared SBPR_LastWearTick
        ///      clock, also stamped by the out-of-zone backfill) and, once ≥1 HP of decay
        ///      has built up, subtract it via vanilla WearNTear.ApplyDamage(float). That
        ///      one call sets s_health AND fires the real RPC_HealthChanged so the cached
        ///      health% refreshes (the ember/downgrade/hover all read GetHealthPercentage,
        ///      which returns the cache — a raw ZDO poke would leave it stale). At tier 1,
        ///      ApplyDamage hitting 0 HP collapses the cairn through vanilla's own
        ///      ApplyDamage→Destroy path (DropResources + fragments) — NO 5% floor here,
        ///      so live abandonment can actually reach collapse. This is the fix for the
        ///      "100% HP in a storm" bug: vanilla wet UpdateWear is only an accelerant
        ///      (and caps at >50% HP), so without our own ticker a non-wet storm — or any
        ///      resident cairn — never lost HP.
        ///   1. AUTO-DOWNGRADE (§A2.1b wear ladder / §A3.5) — when HP is below 25% and
        ///      tier &gt; 1, drop one tier and reset HP to 100% of the new tier (per
        ///      requirements.md §A3.5). Runs AFTER decay so the same tick that crosses 25%
        ///      downgrades. WriteTier rebuilds the pile at the lower stone count; the
        ///      subsequent Repair lands HP at full. At tier 1 there is no downgrade — the
        ///      decay in step 0 carries it to 0% and collapse.
        ///   2. EMBER — toggle the wear ember only when the pristine/fizzled bracket (≥75%
        ///      HP) actually flips, so it's a cheap no-op most ticks.
        ///
        /// All ZDO/HP writes are owner-gated (same IsOwner guard the downgrade used).
        /// </summary>
        private void HpBracketTick()
        {
            if (wnt == null) return;

            // 0. Resident time decay first — it mutates HP (and may collapse a tier-1
            //    cairn outright), then downgrade + ember reconcile against the new HP.
            //    Owner-only: the authoritative HP write routes through the network owner.
            if (nview != null && nview.IsOwner())
            {
                ResidentTimeDecay();
            }

            // 1. Auto-downgrade (owner-only). Reads the HP that step 0 just wrote.
            if (nview != null && nview.IsOwner())
            {
                float hp = wnt.GetHealthPercentage();
                int tier = ReadTier();
                if (hp < Cairns.DowngradeHpFraction && hp > 0f && tier > 1)
                {
                    WriteTier(tier - 1);                 // rebuilds the pile at the lower stone count
                    try { wnt.Repair(); }                // §A3.5: reset HP to 100% of the new tier
                    catch (Exception e) { Plugin.Log.LogWarning($"[Trailborne/M2] Downgrade Repair() threw: {e.Message}"); }
                    Plugin.Log.LogInfo($"[Trailborne/M2] Cairn decayed below 25% → downgraded T{tier}→T{tier - 1}, HP reset.");
                }
            }

            // 2. Cosmetic-fire reconcile (also covers no-owner clients + post-downgrade).
            ReconcileFire();
        }

        /// <summary>
        /// Apply resident (in-zone) TIME decay for the elapsed in-game time, owner-only.
        ///
        /// Reads the shared SBPR_LastWearTick clock (in in-game days — the SAME clock the
        /// out-of-zone backfill stamps, so resident + out-of-zone wear never double-count),
        /// computes the in-game-days elapsed, and subtracts <c>Cairns.DecayHpPerDay ×
        /// deltaDays</c> HP. The subtraction goes through vanilla
        /// <see cref="WearNTear.ApplyDamage(float, HitData)"/>, which:
        ///   • writes s_health and fires the registered RPC_HealthChanged (refreshing the
        ///     cached health% the ember/downgrade/hover all read — a bare ZDO Set would not), and
        ///   • at 0 HP runs vanilla's own ApplyDamage→Destroy (DropResources + fragments +
        ///     ZNetScene.Destroy), so a tier-1 cairn COLLAPSES live with no manual teardown.
        ///
        /// Throttle: we only call ApplyDamage (and advance the clock) once at least
        /// <c>MinDecayHpStep</c> (1 HP) has accrued. Below that, we leave the clock alone
        /// so the elapsed in-game time banks on SBPR_LastWearTick and rolls into the next
        /// tick — we skip the network write, never the time. So this is NOT one RPC per
        /// second; at the default 10 HP/in-game-day (1200 s/day) it's roughly one
        /// ApplyDamage every ~2 real minutes, and the billed total still equals the exact
        /// rate. There is intentionally NO 5% floor (unlike the reload-safety backfill):
        /// live abandonment must be able to reach 0%.
        ///
        /// No-ops cleanly before the world clock is up (CurrentWearDay() &lt; 0), on first
        /// sighting (seeds the clock, no decay), if decay is disabled (rate ≤ 0), or once
        /// HP is already 0 (collapse is mid-flight).
        /// </summary>
        private void ResidentTimeDecay()
        {
            if (wnt == null) return;                      // no WearNTear → nothing to decay
            if (nview == null || nview.GetZDO() == null) return;

            float rate = Cairns.DecayHpPerDay;
            if (rate <= 0f) return;                       // time decay disabled (weather-only)

            float nowDay = Cairns.CurrentWearDay();
            if (nowDay < 0f) return;                      // world clock not up yet

            var zdo = nview.GetZDO();
            float lastWearDay = zdo.GetFloat(Cairns.ZdoLastWearTick, -1f);
            if (lastWearDay < 0f)
            {
                // First sighting — seed the clock, decay nothing this tick.
                zdo.Set(Cairns.ZdoLastWearTick, nowDay);
                return;
            }

            float deltaDays = nowDay - lastWearDay;
            if (deltaDays <= 0f)
            {
                // Clock didn't advance (or went backwards on a re-sync) — re-stamp, no decay.
                if (deltaDays < 0f) zdo.Set(Cairns.ZdoLastWearTick, nowDay);
                return;
            }

            float curHp = zdo.GetFloat(ZDOVars.s_health, wnt.m_health);
            if (curHp <= 0f) return;                      // already collapsing

            float decayHp = rate * deltaDays;
            // Throttle: until at least 1 HP has accrued, DON'T advance the clock — the
            // elapsed time banks on SBPR_LastWearTick and rolls into the next tick. This
            // is what keeps us from firing one RPC per 1 Hz poll while still billing the
            // exact total rate (we only skip the network write, never the time).
            if (decayHp < MinDecayHpStep) return;

            // Enough accrued — advance the clock to now (we're billing the whole delta).
            zdo.Set(Cairns.ZdoLastWearTick, nowDay);

            // Route through vanilla ApplyDamage: refreshes the cached health% AND, at 0 HP,
            // triggers vanilla's destroy path (collapse) for a tier-1 cairn. Clamp so we
            // never over-subtract past 0 in a single call (ApplyDamage destroys at ≤0).
            float applied = Mathf.Min(decayHp, curHp);
            try
            {
                wnt.ApplyDamage(applied);
                Plugin.Log.LogInfo(
                    $"[Trailborne/M2] Cairn resident decay: {deltaDays:F3}d × {rate:F1} → -{applied:F1} HP ({curHp:F0} → {Mathf.Max(0f, curHp - applied):F0}).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/M2] Resident decay ApplyDamage threw: {e.Message}");
            }
        }

        /// <summary>
        /// Reconcile the cosmetic fire to the current HP bracket immediately. Called
        /// right after a Repair (from CairnInteractable) so the relight is instant
        /// (no up-to-1 s poll wait on the player's own action). Cheap no-op if a tier
        /// rebuild already set the state.
        /// </summary>
        public void RefreshFire() => ReconcileFire();
    }
}
