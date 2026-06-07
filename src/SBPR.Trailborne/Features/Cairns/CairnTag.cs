using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

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
        /// Build the §A2.1b stone pile + wear ember for the current tier.
        ///
        /// Order matters and is deliberate:
        ///   1. <see cref="NeutralizeDonorFire"/> ALWAYS runs first — even if
        ///      rock_low can't be found — so a missing prefab can never early-return
        ///      into a live, flaming bonfire (the PR #23 lesson).
        ///   2. Build the haphazard, squashed, ZDO-seeded rock pile (count = the
        ///      stone ladder for this tier).
        ///   3. Build the small HP-gated wear ember at the pile top (under kitbashRoot,
        ///      so step 1's neutralization sweep — which skips kitbashRoot — never
        ///      touches it, and a tier rebuild recycles it with the pile).
        ///   4. <see cref="HideDonorMeshes"/> hides the bonfire's visible meshes, but
        ///      only once we actually have a rock pile to show instead.
        /// </summary>
        public void BuildKitbashArt()
        {
            int tier = ReadTier();
            // Rebuild whenever the tier changed OR we don't yet have a pile.
            if (tier == lastBuiltTier && kitbashRoot != null) return;
            lastBuiltTier = tier;

            // STEP 1 — turn the donor bonfire into a COSMETIC fire (flame VFX + crackle
            // + small sub-torch Light, NO heat, NO fuel). Runs unconditionally and
            // before any early-return. Idempotent. (Daniel 2026-06-07 spec reversal.)
            ConfigureCosmeticFire();

            // Strip prior kitbash root (deferred destroy).
            if (kitbashRoot != null) UnityEngine.Object.Destroy(kitbashRoot);

            var zns = ZNetScene.instance;
            if (zns == null) return;
            // Donor source for the cairn pile: a vanilla small ground-stone prefab,
            // stripped to pure decoration before stacking. Try real prefab names in
            // priority order — different Valheim builds have shipped different small-
            // stone props, and the v0.2.1 build referenced "rock_low" which doesn't
            // exist (corpus + Item_IDs.md confirm). Pickable_Stone is the literal
            // "stone on the ground" the player picks up — small, grey, irregular,
            // perfect cairn substrate. Pickable_StoneRock is a fallback variant.
            //   Daniel 2026-06-05: "this isn't a kit bashed cairn" — the bonfire
            //   teepee logs were showing through because rock_low.GetPrefab() was
            //   returning null and the fallback path leaves the donor visible.
            GameObject? rockSrc = null;
            string? rockSrcName = null;
            foreach (var candidate in new[] { "Pickable_Stone", "Pickable_StoneRock", "rock_low" })
            {
                rockSrc = zns.GetPrefab(candidate);
                if (rockSrc != null) { rockSrcName = candidate; break; }
            }
            if (rockSrc == null)
            {
                // Fallback — no donor stone prefab available. The fire is ALREADY
                // configured (step 1); reconcile its lit-state to the current HP so a
                // pristine stub still shows the flame.
                ReconcileFire();
                Plugin.Log.LogWarning(
                    "[Trailborne/M2] No donor stone prefab found (tried Pickable_Stone, Pickable_StoneRock, rock_low); " +
                    "cairn shows the cosmetic-fire bonfire-base stub (no rock kitbash this build).");
                return;
            }

            kitbashRoot = new GameObject("SBPR_CairnKitbash");
            kitbashRoot.transform.SetParent(transform, worldPositionStays: false);

            // §A2.1b: pile count = the stone ladder. T1=9, T2=12, T3=15, T4=18, T5=21.
            int stones = Cairns.StoneCostForTier(tier);

            BuildPile(rockSrc, kitbashRoot.transform, tier, stones);

            // STEP 3 — HP-gate the cosmetic fire (lit at pristine, off below).
            ReconcileFire();

            // STEP 4 — hide the donor bonfire's visible MESH logs so only the rock
            // pile + the cosmetic flame show (the flame is particles/light, not a
            // mesh, so HideDonorMeshes leaves it visible).
            HideDonorMeshes();
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
        private float BuildPile(GameObject rockSrc, Transform parent, int tier, int stones)
        {
            int seed = 1337;
            if (nview != null && nview.GetZDO() != null)
                seed = nview.GetZDO().m_uid.GetHashCode();
            var rng = new System.Random(seed);

            float baseRadius = PileBaseRadiusT1 + PileBaseRadiusStep * (tier - 1);
            float pileHeight = PileHeightT1     + PileHeightStep     * (tier - 1);
            var pigment = ColorFor(Color);

            float maxStoneTop = PileBaseY;

            for (int i = 0; i < stones; i++)
            {
                var rockClone = GameObject.Instantiate(rockSrc, parent);
                StripGameplayComponents(rockClone);

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

                rockClone.transform.localPosition = new Vector3(ox, y, oz);
                rockClone.transform.localRotation = Quaternion.Euler(
                    (float)(rng.NextDouble() * 2.0 - 1.0) * TiltDegrees,   // slight random tilt X
                    (float)(rng.NextDouble() * 360.0),                     // random yaw
                    (float)(rng.NextDouble() * 2.0 - 1.0) * TiltDegrees);  // slight random tilt Z
                rockClone.transform.localScale = new Vector3(sxz, sy, sxz);

                TintStone(rockClone, pigment);
                rockClone.SetActive(true);

                float stoneTop = y + sy * 0.5f;
                if (stoneTop > maxStoneTop) maxStoneTop = stoneTop;
            }

            return maxStoneTop + EmberHeightLift;
        }

        private static Color ColorFor(string color)
        {
            if (!string.IsNullOrEmpty(color) && PigmentValues.TryGetValue(color, out var c)) return c;
            return PigmentValues["white"];
        }

        /// <summary>
        /// Tint one rock clone's renderers to the pigment color, clean-room style:
        /// clone the shared materials and multiply the lit shader's <c>_Color</c>.
        /// (Same approach as Signs.TintRendererMaterials — texture detail survives
        /// because _Color multiplies the albedo.) No-op on a headless server.
        /// </summary>
        private static void TintStone(GameObject go, Color c)
        {
            int prop = Shader.PropertyToID("_Color");
            foreach (var rend in go.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (rend == null) continue;
                try
                {
                    var mats = rend.sharedMaterials;
                    var newMats = new Material[mats.Length];
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null) continue;
                        var m = new Material(mats[i]);
                        if (m.HasProperty(prop)) m.SetColor(prop, c);
                        newMats[i] = m;
                    }
                    rend.sharedMaterials = newMats;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Trailborne/M2] Cairn stone tint failed: {e.Message}");
                }
            }
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

        /// <summary>Enable/disable the donor's cosmetic-fire components (flame, glow, crackle).</summary>
        private void SetFireVisible(bool on)
        {
            // Flame VFX — emission on/off (keeps the system alive, just stops emitting
            // when fizzled so existing particles fade out naturally).
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(includeInactive: true))
            {
                if (ps == null || IsKitbash(ps.transform)) continue;
                try
                {
                    var em = ps.emission; em.enabled = on;
                    if (on) ps.Play(true);
                    else ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
                catch { /* swallow — headless/edge cases */ }
            }
            // Glow — the kept sub-torch Light (DimLightToSubTorch already set its level).
            bool keptOne = false;
            foreach (var l in GetComponentsInChildren<Light>(includeInactive: true))
            {
                if (l == null || IsKitbash(l.transform)) continue;
                // Only the FIRST non-kitbash light is "the cairn glow"; extras stay off.
                l.enabled = on && !keptOne;
                keptOne = true;
            }
            // Crackle — the kept fire audio.
            foreach (var a in GetComponentsInChildren<AudioSource>(includeInactive: true))
            {
                if (a == null || IsKitbash(a.transform)) continue;
                try { a.enabled = on; a.mute = !on; if (on && !a.isPlaying) a.Play(); else if (!on) a.Stop(); }
                catch { /* swallow */ }
            }
            foreach (var z in GetComponentsInChildren<ZSFX>(includeInactive: true))
                if (z != null && !IsKitbash(z.transform)) z.enabled = on;
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

        // ── Donor-fire neutralization (PR #23 path — UNCHANGED) ────────────────────

        private static void StripGameplayComponents(GameObject go)
        {
            // Remove anything that would cause weird side effects (loot drops, terrain edits, sounds).
            var bad = new List<Component>();
            foreach (var c in go.GetComponentsInChildren<Component>(includeInactive: true))
            {
                if (c == null) continue;
                if (c is Transform) continue;
                if (c is MeshFilter) continue;
                if (c is MeshRenderer) continue;
                if (c is Renderer) continue;
                bad.Add(c);
            }
            foreach (var c in bad)
            {
                try { UnityEngine.Object.DestroyImmediate(c); } catch { /* swallow — some components refuse mid-Awake destroy */ }
            }
        }

        /// <summary>
        /// True if <paramref name="t"/> belongs to our runtime rock kitbash (which now
        /// also contains the wear ember), so the fire-neutralization passes never touch
        /// our own art (only the donor bonfire's components). kitbashRoot may be the
        /// PREVIOUS build's root during a tier rebuild (deferred destroy) — still
        /// correct to exclude it.
        /// </summary>
        private bool IsKitbash(Transform t)
        {
            if (t == null || kitbashRoot == null) return false;
            var kr = kitbashRoot.transform;
            return t == kr || t.IsChildOf(kr);
        }

        /// <summary>
        /// <summary>
        /// Configure the donor bonfire into a COSMETIC fire (Daniel 2026-06-07, spec
        /// reversal — the old "non-burning marker, no light/SFX" design was wrong).
        ///
        /// The cairn at pristine SHOULD show a real flame: flame VFX + fire SFX + a
        /// SMALL Light (dimmer than a vanilla torch). It must NOT grant heat and must
        /// NOT consume fuel. So instead of destroying the donor fire wholesale (the
        /// old NeutralizeDonorFire path) and hand-rolling an invisible 14-particle
        /// "wear ember", we SELECTIVELY keep the donor's cosmetics and only neuter the
        /// gameplay-bearing parts:
        ///
        ///   KEPT (cosmetic):
        ///   • <see cref="ParticleSystem"/>s = the flame VFX (left emitting).
        ///   • Fire <see cref="AudioSource"/> / <see cref="ZSFX"/> = the crackle.
        ///   • One <see cref="Light"/> = the warm glow, but with intensity + range
        ///     dialled BELOW a vanilla torch (see <see cref="DimLightToSubTorch"/>).
        ///     LightFlicker is kept (it animates the kept light); LightLod kept too.
        ///
        ///   NEUTERED (gameplay):
        ///   • <see cref="Fireplace"/> — KEPT as a component (so the flame state reads
        ///     as "lit") but forced to <c>m_infiniteFuel = true</c> with fuel knobs
        ///     zeroed, so it never consumes fuel, never burns out, and shows no
        ///     'add fuel' interaction. CairnInteractable remains the gameplay hover.
        ///   • <see cref="EffectArea"/> — DISABLED, so standing on/near a cairn never
        ///     burns the player (no heat). Disable unregisters it via OnDisable.
        ///   • <see cref="SmokeSpawner"/> — DISABLED (a marker cairn shouldn't billow
        ///     bonfire smoke; the flame VFX alone reads as a small fire).
        ///
        /// HP-gating: the whole cosmetic fire is parented under the donor and toggled
        /// as one object by <see cref="ReconcileFire"/> — lit at &gt;= pristine HP,
        /// off below — reusing the same 1 Hz bracket poll the old ember used.
        ///
        /// Our own rock kitbash is excluded via <see cref="IsKitbash"/>. Visible donor
        /// MESH renderers are still hidden by <see cref="HideDonorMeshes"/> so only the
        /// rock pile + the flame show, not the bonfire's log teepee.
        ///
        /// All client-side: a headless server has no renderers/lights/particles, so
        /// every step here is inert server-side (the cosmetic-fire visual cannot be
        /// proven from a headless build — verified in-game only).
        /// </summary>
        private void ConfigureCosmeticFire()
        {
            // 1) Fireplace — KEEP but make it eternal + fuel-less + heat-less. Forcing
            //    m_infiniteFuel true means it never drains/needs fuel and never shows
            //    the 'add fuel' hover; zeroing the fuel knobs is belt-and-braces.
            foreach (var fp in GetComponentsInChildren<Fireplace>(includeInactive: true))
            {
                if (fp == null || IsKitbash(fp.transform)) continue;
                try
                {
                    fp.m_infiniteFuel = true;   // never consumes / never burns out
                    fp.m_maxFuel      = 1f;     // no meaningful fuel capacity
                    fp.m_secPerFuel   = float.MaxValue; // never ticks fuel down
                    fp.m_fuelItem     = null;   // no fuel item → no 'add fuel' affordance
                    fp.m_startFuel    = 1f;     // spawn already "lit"
                }
                catch { /* swallow — headless/edge cases */ }
            }

            // 2) Light — KEEP exactly one, dimmed below a torch (the warm cairn glow).
            //    Keep LightFlicker/LightLod so the kept light still animates/LODs.
            DimLightToSubTorch();

            // 3) Particle systems (flame VFX) — KEEP emitting. (We intentionally do NOT
            //    stop/clear them anymore; they ARE the cosmetic fire.) SmokeSpawner is
            //    the one particle source we silence — a small marker fire, not a pyre.
            foreach (var sm in GetComponentsInChildren<SmokeSpawner>(includeInactive: true))
                if (sm != null && !IsKitbash(sm.transform)) sm.enabled = false;

            // 4) Heat / burning EffectArea — DISABLE (no heat granted). Disabling
            //    unregisters it from the static burning-area lists via OnDisable.
            foreach (var ea in GetComponentsInChildren<EffectArea>(includeInactive: true))
                if (ea != null && !IsKitbash(ea.transform)) ea.enabled = false;

            // 5) Fire audio (crackle) — KEEP. (We intentionally do NOT stop/mute the
            //    AudioSource/ZSFX anymore; the crackle is part of the cosmetic fire.)
        }

        /// <summary>
        /// Find the donor fire's Light(s) and dial intensity + range BELOW a vanilla
        /// torch so the cairn glows but reads as a small marker fire, not a bonfire or
        /// a torch. We keep the FIRST non-kitbash Light and disable any extras (a
        /// bonfire can carry more than one); the kept one is clamped to sub-torch.
        ///
        /// Vanilla torch light sits around intensity ~1.4 / range ~6–8 in the current
        /// build; we target clearly under that. These are cosmetic tunables — eyeball
        /// in-game and adjust if the glow reads too strong/weak.
        /// </summary>
        private void DimLightToSubTorch()
        {
            bool keptOne = false;
            foreach (var l in GetComponentsInChildren<Light>(includeInactive: true))
            {
                if (l == null || IsKitbash(l.transform)) continue;
                if (!keptOne)
                {
                    keptOne = true;
                    l.enabled   = true;
                    l.intensity = SubTorchLightIntensity; // < torch
                    l.range     = SubTorchLightRange;      // < torch
                }
                else
                {
                    // Extra donor lights off — one warm glow is enough for a marker.
                    l.enabled = false;
                }
            }
        }

        /// <summary>
        /// Hide the donor bonfire's visible meshes so only the rock kitbash shows.
        /// Disables every Renderer that is NOT part of our kitbash. Called only after
        /// a rock pile has been built; the fallback path skips this so a missing
        /// rock_low leaves a plain (but non-burning) base instead of nothing.
        /// Skips ParticleSystemRenderers (our ember) — they live under kitbashRoot and
        /// IsKitbash already excludes them, but the explicit type-skip is belt-and-braces.
        /// </summary>
        private void HideDonorMeshes()
        {
            foreach (var r in GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (r == null || IsKitbash(r.transform)) continue;
                if (r is ParticleSystemRenderer) continue;
                r.enabled = false;
            }
        }
    }
}
