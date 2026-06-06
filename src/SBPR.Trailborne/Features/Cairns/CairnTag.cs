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
        private GameObject? emberObj;          // the small wear ember, parented UNDER kitbashRoot (so neutralization + rebuild both skip/recycle it)
        private int lastBuiltTier = -1;
        private bool? lastEmberLit;            // null until first poll, so the first tick always reconciles

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
        private const float EmberHeightLift    = 0.10f;  // ember sits this far above the topmost stone

        // Pigment values — mirror the canonical SBPR ink palette (Signs.ColorValues)
        // so a cairn's stones read the same color as its marker/pennant. Kept local to
        // avoid a Cairns→Signs feature dependency.
        private static readonly Dictionary<string, Color> PigmentValues = new Dictionary<string, Color>
        {
            { "red",   new Color(0.85f, 0.18f, 0.18f, 1f) },
            { "white", new Color(0.95f, 0.94f, 0.88f, 1f) },
            { "blue",  new Color(0.20f, 0.40f, 0.85f, 1f) },
            { "black", new Color(0.10f, 0.10f, 0.12f, 1f) },
        };

        // Cached particle material harvested once from the vanilla fire_pit prefab.
        // We read only the MATERIAL reference off the prefab's particle renderer — we
        // never instantiate the donor fire, so no light/heat/SFX comes along. Static
        // so all cairns on the client share the one lookup.
        private static Material? sEmberMat;
        private static bool sEmberMatResolved;

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

            // STEP 1 — deterministically kill the bonfire's fire by COMPONENT TYPE.
            // Runs unconditionally and before any early-return. Idempotent.
            NeutralizeDonorFire();

            // Strip prior kitbash root (deferred destroy; takes the old ember with it).
            if (kitbashRoot != null) UnityEngine.Object.Destroy(kitbashRoot);
            emberObj = null;

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
                // neutralized (step 1), so we leave the donor's base mesh visible as
                // a plain non-burning stub. No pile means no ember to anchor.
                Plugin.Log.LogWarning(
                    "[Trailborne/M2] No donor stone prefab found (tried Pickable_Stone, Pickable_StoneRock, rock_low); " +
                    "cairn shows a non-burning bonfire-base stub (fire neutralized, no rock kitbash / ember this build).");
                return;
            }

            kitbashRoot = new GameObject("SBPR_CairnKitbash");
            kitbashRoot.transform.SetParent(transform, worldPositionStays: false);

            // §A2.1b: pile count = the stone ladder. T1=9, T2=12, T3=15, T4=18, T5=21.
            int stones = Cairns.StoneCostForTier(tier);

            float topY = BuildPile(rockSrc, kitbashRoot.transform, tier, stones);

            // STEP 3 — small HP-gated wear ember at the pile top.
            BuildOrUpdateEmber(zns, topY);

            // STEP 4 — hide the donor bonfire's visible meshes so only the kitbash shows.
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

        // ── Wear ember ───────────────────────────────────────────────────────────

        /// <summary>
        /// Resolve (once, cached) a particle MATERIAL off the vanilla <c>fire_pit</c>
        /// prefab so our hand-built ember renders correctly without guessing a shader
        /// name. We read the prefab's particle-renderer material REFERENCE only — the
        /// prefab is never instantiated, so none of its light/heat/SFX components come
        /// along. Returns null if fire_pit or a particle renderer can't be found
        /// (ember silently absent rather than rendering as magenta).
        /// </summary>
        private static Material? ResolveEmberMaterial(ZNetScene zns)
        {
            if (sEmberMatResolved) return sEmberMat;
            sEmberMatResolved = true;
            try
            {
                var src = zns.GetPrefab("fire_pit");
                if (src != null)
                {
                    foreach (var r in src.GetComponentsInChildren<ParticleSystemRenderer>(includeInactive: true))
                    {
                        if (r != null && r.sharedMaterial != null) { sEmberMat = r.sharedMaterial; break; }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/M2] Ember material lookup failed: {e.Message}");
            }
            if (sEmberMat == null)
                Plugin.Log.LogInfo("[Trailborne/M2] No fire_pit particle material found; cairn ember will be absent (no light/heat lost — it's decorative only).");
            return sEmberMat;
        }

        /// <summary>
        /// Build the small wear ember at <paramref name="topY"/> (local) and set its
        /// lit/fizzled state from the current HP bracket. Parented UNDER kitbashRoot so:
        ///   • <see cref="NeutralizeDonorFire"/> (which skips kitbashRoot) never strips it, and
        ///   • a tier rebuild recycles it with the pile.
        /// The ember GameObject carries ONLY Transform + ParticleSystem +
        /// ParticleSystemRenderer — by construction it has no Light, no EffectArea,
        /// no AudioSource, no SmokeSpawner, and it does NOT touch the donor Fireplace.
        /// </summary>
        private void BuildOrUpdateEmber(ZNetScene zns, float topY)
        {
            if (kitbashRoot == null) return;
            if (emberObj != null) UnityEngine.Object.Destroy(emberObj);

            emberObj = new GameObject("SBPR_CairnEmber");
            emberObj.transform.SetParent(kitbashRoot.transform, worldPositionStays: false);
            emberObj.transform.localPosition = new Vector3(0f, topY, 0f);

            var ps = emberObj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // configure before it plays

            var main = ps.main;
            main.loop = true;
            main.startLifetime = 0.7f;
            main.startSpeed = 0.35f;
            main.startSize = 0.10f;
            main.startColor = new Color(1f, 0.55f, 0.18f, 1f); // warm ember
            main.maxParticles = 14;                            // tiny — this is a wear signal, not a blaze
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.playOnAwake = true;

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 10f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.05f;

            var psr = emberObj.GetComponent<ParticleSystemRenderer>();
            var mat = ResolveEmberMaterial(zns);
            if (psr != null && mat != null) psr.sharedMaterial = mat;

            bool lit = EmberShouldBeLit();
            emberObj.SetActive(lit);
            lastEmberLit = lit;
        }

        /// <summary>True when HP is at/above the pristine fraction (ember lit).</summary>
        private bool EmberShouldBeLit()
        {
            if (wnt == null) return false;
            return wnt.GetHealthPercentage() >= Cairns.PristineHpFraction;
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

            // 1. Ember reconcile (also covers the no-owner clients and the post-downgrade state).
            ReconcileEmber();
        }

        /// <summary>
        /// Toggle the ember to match the current HP bracket, only when it actually
        /// flips. Cheap no-op most ticks. Safe on non-owner clients (read-only).
        /// </summary>
        private void ReconcileEmber()
        {
            if (emberObj == null) return;
            bool lit = EmberShouldBeLit();
            if (lastEmberLit.HasValue && lit == lastEmberLit.Value) return;
            lastEmberLit = lit;
            emberObj.SetActive(lit);
        }

        /// <summary>
        /// Reconcile the ember to the current HP bracket immediately. Called right
        /// after a Repair so the relight is instant (no up-to-1 s poll wait on the
        /// player's own action). Cheap no-op if a tier rebuild already set the state.
        /// </summary>
        public void RefreshEmber() => ReconcileEmber();

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
        /// Kill the donor bonfire's FIRE deterministically, by component TYPE, using
        /// <c>GetComponentsInChildren&lt;T&gt;(true)</c> — never by child-name string
        /// matching (the old, brittle approach that let unnamed flame/light/particle
        /// objects render straight through the rock pile).
        ///
        /// Neutralized:
        ///   • <see cref="Fireplace"/> — DESTROYED, so it stops being a lit, fuelable
        ///     fire (also removes the 'add fuel' hover and lets CairnInteractable be
        ///     the sole Hoverable/Interactable on the root). WearNTear / Piece /
        ///     ZNetView / CairnTag / CairnInteractable are left intact.
        ///   • Every <see cref="Light"/> (the warm glow) + LightFlicker / LightLod
        ///     drivers that could flicker or re-enable a light.
        ///   • Every <see cref="ParticleSystem"/> (flames, embers, smoke) — emission
        ///     off, stopped + cleared — plus <see cref="SmokeSpawner"/> which would
        ///     otherwise keep spawning fresh smoke.
        ///   • Every <see cref="EffectArea"/> (heat / burning damage area) so standing
        ///     on or near a cairn never burns the player.
        ///   • Every fire <see cref="AudioSource"/> / <see cref="ZSFX"/> (the looping
        ///     crackle).
        ///
        /// Our own kitbash (rock pile + wear ember) is excluded via <see cref="IsKitbash"/>
        /// — the ember is built AFTER this pass anyway, and on a tier rebuild it lives
        /// under kitbashRoot so this sweep skips it. Visible MESH renderers are handled
        /// separately in <see cref="HideDonorMeshes"/>.
        /// </summary>
        private void NeutralizeDonorFire()
        {
            // 1) Fireplace — remove entirely (fuel/burning behaviour + 'add fuel' hover).
            foreach (var fp in GetComponentsInChildren<Fireplace>(includeInactive: true))
            {
                if (fp == null || IsKitbash(fp.transform)) continue;
                try { UnityEngine.Object.Destroy(fp); } catch { /* swallow */ }
            }

            // 2) Lights + their drivers (flicker animates intensity; LOD can re-enable).
            foreach (var l in GetComponentsInChildren<Light>(includeInactive: true))
                if (l != null && !IsKitbash(l.transform)) l.enabled = false;
            foreach (var lf in GetComponentsInChildren<LightFlicker>(includeInactive: true))
                if (lf != null && !IsKitbash(lf.transform)) lf.enabled = false;
            foreach (var ll in GetComponentsInChildren<LightLod>(includeInactive: true))
                if (ll != null && !IsKitbash(ll.transform)) ll.enabled = false;

            // 3) Particle systems (flames/embers/smoke): stop emission, stop + clear.
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(includeInactive: true))
            {
                if (ps == null || IsKitbash(ps.transform)) continue;
                try
                {
                    var em = ps.emission; em.enabled = false;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Clear(true);
                }
                catch { /* swallow — headless/edge cases */ }
            }
            foreach (var sm in GetComponentsInChildren<SmokeSpawner>(includeInactive: true))
                if (sm != null && !IsKitbash(sm.transform)) sm.enabled = false;

            // 4) Heat / burning EffectArea — disabling unregisters it from the static
            //    burning-area lists (via its OnDisable), so it stops applying heat.
            foreach (var ea in GetComponentsInChildren<EffectArea>(includeInactive: true))
                if (ea != null && !IsKitbash(ea.transform)) ea.enabled = false;

            // 5) Looping fire audio (crackle) — both raw AudioSources and ZSFX wrappers.
            foreach (var a in GetComponentsInChildren<AudioSource>(includeInactive: true))
            {
                if (a == null || IsKitbash(a.transform)) continue;
                try { a.Stop(); a.mute = true; a.enabled = false; } catch { /* swallow */ }
            }
            foreach (var z in GetComponentsInChildren<ZSFX>(includeInactive: true))
                if (z != null && !IsKitbash(z.transform)) z.enabled = false;
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
