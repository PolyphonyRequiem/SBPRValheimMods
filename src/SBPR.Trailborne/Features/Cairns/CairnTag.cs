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
        private const float StoneSizeMin       = 0.55f;  // overall stone scale (horizontal footprint) lower bound
        private const float StoneSizeMax       = 0.95f;  // overall stone scale upper bound
        private const float FlattenRatioMin    = 0.16f;  // vertical/horizontal ratio lower bound (smaller = flatter/squashier)
        private const float FlattenRatioMax    = 0.30f;  // vertical/horizontal ratio upper bound
        private const float TiltDegrees        = 12f;    // ± random tilt on X/Z so stones don't sit perfectly level
        private const float PosJitterXZ        = 0.06f;  // ± extra lateral jitter on top of the disk sample
        private const float PosJitterY         = 0.04f;  // ± vertical jitter per stone
        private const float EmberHeightLift    = 0.10f;  // (legacy) kept for pile-top math compatibility

        // Cosmetic-fire Light tunables (Daniel 2026-06-07). Clearly below a vanilla
        // torch (~1.4 intensity / ~6-8 range in the current build) so the cairn reads
        // as a small marker fire, not a torch or bonfire. Eyeball in-game + adjust.
        private const float SubTorchLightIntensity = 0.8f;
        private const float SubTorchLightRange     = 4.0f;

        // Pigment values — mirror the canonical SBPR pigment palette (Signs.ColorValues)
        // so a cairn's stones read the same color as its marker/pennant. Kept local to
        // avoid a Cairns→Signs feature dependency.
        private static readonly Dictionary<string, Color> PigmentValues = new Dictionary<string, Color>
        {
            { "red",   new Color(0.85f, 0.18f, 0.18f, 1f) },
            { "white", new Color(0.95f, 0.94f, 0.88f, 1f) },
            { "blue",  new Color(0.20f, 0.40f, 0.85f, 1f) },
            { "black", new Color(0.10f, 0.10f, 0.12f, 1f) },
        };

        private void Awake()
        {
            nview = GetComponent<ZNetView>();
            wnt = GetComponent<WearNTear>();
            BuildKitbashArt(); // tier from ZDO or default 1
            // Health-bracket poll: path-independent ember toggle + auto-downgrade.
            // Catches repair-UP (50%→100%), debug-damage-down, out-of-zone backfill,
            // and natural weather decay alike — a single WearNTear.OnDamage postfix
            // would miss repair-up. 1 s cadence is imperceptible for a fizzle/relight
            // wear indicator and an abandonment-decay downgrade, and costs ~nothing
            // (one float read + a bracket compare).
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
            var pigment = ColorFor(Color);

            // Build ONE pigment-tinted material for the whole pile (clone the donor's
            // shared material so we never mutate the vanilla asset; multiply _Color).
            // All stones share it — one material, not one-per-stone — so the pile is a
            // handful of draw-call-cheap MeshRenderers with no networked components.
            Material? tinted = MakeTintedStoneMaterial(stoneMat, pigment);

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
                if (tinted != null) mr.sharedMaterial = tinted;
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

                // Squash: horizontal footprint = overall size; vertical = size × flatten.
                float size    = Mathf.Lerp(StoneSizeMin, StoneSizeMax, (float)rng.NextDouble());
                float flatten = Mathf.Lerp(FlattenRatioMin, FlattenRatioMax, (float)rng.NextDouble());
                float sxz = size;
                float sy  = size * flatten;

                stone.transform.localPosition = new Vector3(ox, y, oz);
                stone.transform.localRotation = Quaternion.Euler(
                    (float)(rng.NextDouble() * 2.0 - 1.0) * TiltDegrees,   // slight random tilt X
                    (float)(rng.NextDouble() * 360.0),                     // random yaw
                    (float)(rng.NextDouble() * 2.0 - 1.0) * TiltDegrees);  // slight random tilt Z
                stone.transform.localScale = new Vector3(sxz, sy, sxz);

                float stoneTop = y + sy * 0.5f;
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

        /// <summary>
        /// Clone the donor stone material and multiply its lit-shader <c>_Color</c> by the
        /// cairn pigment (clean-room tint, same approach as Signs.TintRendererMaterials —
        /// texture detail survives because _Color multiplies the albedo). Returns null on a
        /// headless server (no material) so the construction loop simply skips assigning one.
        /// </summary>
        private static Material? MakeTintedStoneMaterial(Material? donorMat, Color c)
        {
            if (donorMat == null) return null;
            try
            {
                var m = new Material(donorMat);
                int prop = Shader.PropertyToID("_Color");
                if (m.HasProperty(prop)) m.SetColor(prop, c);
                return m;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/M2] Cairn stone material tint failed: {e.Message}");
                return null;
            }
        }

        private static Color ColorFor(string color)
        {
            if (!string.IsNullOrEmpty(color) && PigmentValues.TryGetValue(color, out var c)) return c;
            return PigmentValues["white"];
        }

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
        /// Health-bracket poll (1 Hz via InvokeRepeating). Two responsibilities, both
        /// keyed off the current HP fraction so they fire path-independently (repair-UP,
        /// damage-DOWN, backfill, decay):
        ///
        ///   1. EMBER — toggle the wear ember only when the pristine/fizzled bracket
        ///      (≥75% HP) actually flips, so it's a cheap no-op most ticks.
        ///   2. AUTO-DOWNGRADE (§A2.1b wear ladder / §A3.5) — when HP falls below 25%
        ///      and tier &gt; 1, drop one tier and reset HP to 100% of the new tier
        ///      (per requirements.md §A3.5: "reduces comfort floor by 1, resets health
        ///      to 100% of new tier"). WriteTier rebuilds the pile at the lower stone
        ///      count; the subsequent Repair lands HP at full so the ember relights and
        ///      the piece doesn't immediately collapse. Owner-only (ZDO writes + Repair
        ///      route through the network owner). At tier 1, no downgrade — the cairn is
        ///      left to fall to 0% and collapse via the vanilla WearNTear destroy path.
        /// </summary>
        private void HpBracketTick()
        {
            if (wnt == null) return;

            // 2. Auto-downgrade first (it mutates HP, then the ember reconciles below).
            //    Only the ZDO owner performs the authoritative tier+HP write.
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

            // 1. Cosmetic-fire reconcile (also covers no-owner clients + post-downgrade).
            ReconcileFire();
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
