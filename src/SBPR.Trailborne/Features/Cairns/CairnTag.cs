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
            // ClothWindDriver GameObject that STREAMS with world wind (A-prime, t_e95949c2)
            // — no ZNetView, no Piece, no pole (ADR-0006). Cloth/SMR/driver are all cosmetic
            // Unity components; the measured #61 dimensions are baked into a per-instance
            // mesh so the Cloth simulates under a UNIFORM transform (no skew).
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

        // Banner placement tunables (FLAGGED — eyeball on a joined client; bake the final
        // metres into requirements.md §A2.1b on sign-off). The donor `default` cloth is a
        // flat sheet in the Y-Z plane (native bounds ≈ (X 0, Y 2.974, Z 1.1802), verified
        // via vprefab on piece_banner01/02/04/11 — all four identical), with its pivot at
        // the cloth TOP so it HANGS DOWN. We measure-and-normalize each in-plane axis to a
        // target metre size — Z carries the horizontal WIDTH, Y the vertical DROP; X is
        // zero-thickness and inert. A single uniform scalar (the retired BannerScale=0.6)
        // could not fix the anisotropic ~3×-wide / ~2×-long error; per-axis normalization
        // does (card t_5756cd21).
        //
        // 🔴 A-prime (t_e95949c2): the normalization is now BAKED INTO A PER-INSTANCE MESH
        // (scale the VERTICES, not the transform) so the Cloth simulates under a UNIFORM
        // transform — UnityEngine.Cloth's constraint solver SHEARS under a skewed lossyScale,
        // so a non-uniform transform.localScale would render the wind sim wrong. The two
        // TargetBanner* constants are STILL the source of truth for the proportions; they
        // now feed the vertex bake in BuildBanner instead of transform.localScale (AC10).
        private const float TargetBannerWidthZ = 0.236f; // horizontal span, m (Z axis)
        private const float TargetBannerDropY  = 0.892f; // vertical drop,   m (Y axis)
        // Seat the cloth pivot (its top) this fraction of the drop ABOVE the pile top, so
        // the cloth — which hangs down from its pivot — clears the terrain plane and drapes
        // the pile's upper stones instead of sinking ~0.12 m underground (the retired
        // BannerLiftY=0.15 seat did exactly that with the resized ~0.892 m cloth). This
        // self-scales if the drop target is rebaked and rides up with the pile at higher tiers.
        private const float BannerSeatDropFrac = 0.5f;
        private const float BannerOffsetXZ     = 0.30f;  // nudge off the pile centre (clears the flame)

        // Cloth wind tunables (A-prime, t_e95949c2 — FLAGGED for in-game eyeball, Daniel's
        // AC9 gate). Multiplier / RandomFactor mirror vanilla GlobalWind defaults (1f / 0.5f).
        // The TOP edge is hard-pinned (maxDistance 0, anchored to the mount) and each lower
        // vertex may travel up to FreeDistance × (its normalized depth below the top), so the
        // banner streams from a fixed top instead of ballooning or blowing away (AC5).
        //
        // BannerClothTopPinBandFrac is a FRACTION OF THE Y-SPAN, not absolute metres — this is
        // load-bearing: the donor cloth's top anchor row sits at fromTop/span ≈ 0 and the next
        // row at ≈ 0.059 (X-ray: 8 verts at Y 0.048, next 8 at Y −0.127, span 2.974). An
        // ABSOLUTE-metre band would catch a DIFFERENT vertex count before vs after the Y-bake
        // (the bake shrinks span ~3.3×, moving row-1 to ~0.018 m from the top) — over-pinning
        // and stiffening the banner. A span-fraction band catches the SAME 8-vertex top row at
        // any bake scale. 0.03 sits cleanly between row-0 (0.0) and row-1 (0.059).
        private const float BannerWindMult          = 1.0f;  // ClothWindDriver.Multiplier
        private const float BannerWindRandomFactor  = 0.5f;  // ClothWindDriver.RandomFactor
        private const float BannerClothFreeDistance = 1.0f;  // max travel at the free (bottom) edge, in cloth units
        private const float BannerClothTopPinBandFrac = 0.03f; // verts within this FRACTION of the Y-span from yMax are hard-pinned

        /// <summary>
        /// Attach the wind-responsive color BANNER to the pile. Reads ONLY the cloth child's
        /// mesh + material off the vanilla banner donor (the color-bearing <c>default</c>
        /// child — NOT the <c>woodbeam</c> pole) and hand-builds an additive GameObject
        /// carrying a <see cref="SkinnedMeshRenderer"/> + <see cref="Cloth"/> +
        /// <see cref="ClothWindDriver"/> (A-prime, t_e95949c2). The Cloth's TOP edge is pinned
        /// (maxDistance 0) and its body is free, so the banner STREAMS with world wind
        /// direction + force from a fixed mount instead of waggling in place — the same
        /// mechanism vanilla sails/capes/tents use via GlobalWind.
        ///
        /// 🔴 Sizing (AC10): the #61 measured proportions (≈0.236 m wide × 0.892 m drop) are
        /// BAKED INTO A PER-INSTANCE MESH (the donor mesh's VERTICES are scaled), NOT applied
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

            // ── Bake the #61 dimensions into a PER-INSTANCE mesh (AC10) ────────────────
            // Measure-and-normalize off the DONOR's honest bounds (real metres): Z→width,
            // Y→drop, X≈0 inert. Same sy/sz v1 computed for transform.localScale — but we
            // apply them to the VERTICES of a private copy so the Cloth can run under a
            // uniform transform (no shear). Never touch the shared donor mesh.
            Vector3 native = donorMesh.bounds.size;                 // ≈ (0, 2.974, 1.1802)
            float sy = native.y > 1e-3f ? TargetBannerDropY  / native.y : 1f;
            float sz = native.z > 1e-3f ? TargetBannerWidthZ / native.z : 1f;

            Mesh bakedMesh = Instantiate(donorMesh);               // private, mutable copy
            bakedMesh.name = donorMesh.name + "_SBPRbaked";
            if (!bakedMesh.isReadable)
            {
                // Can't read/rewrite verts → can't bake dims OR pin the cloth top. Rather than
                // ship a wrong-sized blow-away banner, drop the banner this build and shout —
                // the donor's readability changed in a game patch and the spec needs re-grounding.
                Plugin.Log.LogError(
                    $"[Trailborne/M2] Cairn banner: donor cloth mesh '{donorMesh.name}' is NON-READABLE; " +
                    "cannot bake dimensions or pin the cloth top. Skipping banner — re-ground the A-prime spec " +
                    "(donor mesh readability likely changed in a game patch).");
                Destroy(bakedMesh);
                return;
            }
            Vector3[] verts = bakedMesh.vertices;                  // copy-out; readable confirmed above
            for (int i = 0; i < verts.Length; i++)
                verts[i] = new Vector3(verts[i].x, verts[i].y * sy, verts[i].z * sz);
            bakedMesh.vertices = verts;
            bakedMesh.RecalculateBounds();                         // bounds now ≈ 0.236(Z) × 0.892(Y)

            // ── Build the graft GameObject ────────────────────────────────────────────
            var banner = new GameObject("SBPR_CairnBanner");
            banner.transform.SetParent(parent, worldPositionStays: false);

            // Free the baked per-instance mesh on EVERY teardown (tier rebuild destroys the
            // kitbash root, cairn removal, scene unload) — a runtime Mesh is not GC'd with its
            // GameObject, so without this we'd leak one mesh per rebuild.
            banner.AddComponent<DestroyMeshOnDestroy>().Owned = bakedMesh;

            // Seat the cloth pivot (its top) a half-drop above the pile top so the baked
            // ribbon clears the terrain plane and drapes the upper pile; nudge off-centre so
            // it doesn't bury the flame. Deterministic side per ZDO → stable across reloads.
            int seed = (nview != null && nview.GetZDO() != null) ? nview.GetZDO().m_uid.GetHashCode() : 1337;
            float side = ((seed & 1) == 0) ? 1f : -1f;
            banner.transform.localPosition = new Vector3(
                BannerOffsetXZ * side,
                pileTopY + TargetBannerDropY * BannerSeatDropFrac,
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
            // The cloth swings beyond the rest-mesh AABB; give it a generous local box (mesh-
            // local, pre-transform) so a streaming banner isn't culled at the screen edge.
            // Sized off the baked drop/width with slack for the streamed swing.
            smr.localBounds = new Bounds(
                new Vector3(0f, -TargetBannerDropY * 0.5f, 0f),
                new Vector3(TargetBannerDropY * 1.5f, TargetBannerDropY * 2f, TargetBannerDropY * 1.5f));

            // Now the physics: add Cloth and pin its top edge so it streams from a fixed mount
            // rather than ballooning, collapsing, or blowing away (AC5). Pin AFTER the bake so
            // the coefficients are computed against the BAKED vertex positions.
            var cloth = banner.AddComponent<Cloth>();
            PinTopEdgeCloth(cloth, bakedMesh, BannerClothTopPinBandFrac, BannerClothFreeDistance);

            // Drive it from world wind (direction × force) on a ~2 s cadence (AC1/AC2/AC4).
            var driver = banner.AddComponent<ClothWindDriver>();
            driver.Multiplier = BannerWindMult;
            driver.RandomFactor = BannerWindRandomFactor;
            driver.CheckPlayerShelter = false; // cairns are open-air trail markers
        }

        /// <summary>
        /// Build the <see cref="Cloth"/> skinning coefficients for the banner: hard-pin the
        /// TOP edge row (maxDistance 0, anchored to the mount) and let each lower vertex
        /// travel up to <paramref name="freeDistance"/> × its normalized depth below the top.
        /// This is what makes the banner STREAM from a fixed top instead of blowing away (AC5).
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
        /// <paramref name="topPinBandFrac"/> is a FRACTION of the Y-span (not metres) so the
        /// SAME top row is caught regardless of the per-instance Y-bake scale.
        /// </summary>
        private static void PinTopEdgeCloth(Cloth cloth, Mesh bakedMesh, float topPinBandFrac, float freeDistance)
        {
            var coeffs = cloth.coefficients;
            if (coeffs == null || coeffs.Length == 0)
            {
                Plugin.Log.LogError(
                    "[Trailborne/M2] Cairn banner: Cloth produced 0 skinning coefficients; " +
                    "top edge NOT pinned (banner may blow away). Cloth setup likely rejected the mesh.");
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
                        $"coefficients {n}; top edge NOT pinned. Banner may not anchor correctly.");
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
                        $"({meshVerts.Length}). Top edge NOT pinned (banner may blow away). Donor cloth " +
                        "topology likely changed in a game patch; re-ground the A-prime spec.");
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
            float pinBand = topPinBandFrac * span;   // fraction → absolute, in the baked space

            int pinned = 0;
            for (int i = 0; i < n; i++)
            {
                float fromTop = yMax - verts[i].y;          // 0 at the top edge, grows downward
                if (fromTop <= pinBand)
                {
                    coeffs[i].maxDistance = 0f;              // pinned to the mount
                    pinned++;
                }
                else
                {
                    coeffs[i].maxDistance = freeDistance * (fromTop / span); // free, scaled by depth
                }
                coeffs[i].collisionSphereDistance = 0f;      // no self-collision constraint
            }
            cloth.coefficients = coeffs;

            if (pinned == 0)
                Plugin.Log.LogWarning(
                    $"[Trailborne/M2] Cairn banner: top-pin band {topPinBandFrac:0.###}×span matched NO " +
                    $"vertices on '{bakedMesh.name}' (yMax {yMax:0.###}); banner is unanchored and may blow " +
                    "away. Widen BannerClothTopPinBandFrac.");
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
