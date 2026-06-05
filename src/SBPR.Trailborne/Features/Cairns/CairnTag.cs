using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cairns
{
    /// <summary>
    /// Marker tag attached to each cairn clone. Carries color identity and provides
    /// ZDO-backed tier accessors. Also assembles + rescales the kitbash rock stack
    /// based on tier on Awake / tier change.
    ///
    /// The cairn is cloned from the vanilla <c>bonfire</c> prefab as a STRUCTURAL
    /// base only (WearNTear / Piece / ZNetView). The bonfire is fundamentally a
    /// FIRE prefab, so on the client we must NEUTRALIZE that fire deterministically
    /// — by component TYPE, not by guessing child names — and replace the visible
    /// mesh with a runtime stack of <c>rock_low</c> clones. A cairn is a non-burning
    /// stone marker: no flame, no glow, no smoke, no heat, no crackle SFX.
    ///
    /// This whole assembly is client-side art. The dedicated server is headless
    /// (no renderers/lights) so the visual steps are inert there; the load-bearing
    /// gameplay (comfort floor, repair/upgrade, decay, damage-immunity) lives in
    /// CairnPatches.cs + CairnInteractable.cs and is untouched by this file.
    /// </summary>
    public class CairnTag : MonoBehaviour
    {
        public string Color = null!;          // set by registration immediately after AddComponent
        private ZNetView nview = null!;        // Unity-injected in Awake via GetComponent
        private GameObject? kitbashRoot;       // genuinely null until/unless a rock pile is built (fallback path leaves it null)
        private int lastBuiltTier = -1;

        private void Awake()
        {
            nview = GetComponent<ZNetView>();
            BuildKitbashArt(); // tier from ZDO or default 1
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
        /// Kitbash: take a single `rock_low` mesh, flatten it vertically, stack N
        /// copies with rotation + offset + lateral-scale jitter where N scales with
        /// tier. Reseeded deterministically from the ZDO id so a cairn looks the
        /// same across reloads. Cheap, no asset bundles, gives us a tiered visual
        /// for v0.1.0 playtest without custom meshes.
        ///
        /// Order matters and is deliberate:
        ///   1. <see cref="NeutralizeDonorFire"/> ALWAYS runs first — even if
        ///      rock_low can't be found — so a missing prefab can never early-return
        ///      into a live, flaming bonfire (the historical bug).
        ///   2. Build the rock pile.
        ///   3. <see cref="HideDonorMeshes"/> hides the bonfire's visible meshes,
        ///      but only once we actually have a rock pile to show instead. If
        ///      rock_low is missing we leave the donor's base mesh visible as a
        ///      plain, non-burning stub rather than an invisible collider.
        /// </summary>
        public void BuildKitbashArt()
        {
            int tier = ReadTier();
            if (tier == lastBuiltTier && kitbashRoot != null) return;
            lastBuiltTier = tier;

            // STEP 1 — deterministically kill the bonfire's fire by COMPONENT TYPE.
            // Runs unconditionally and before any early-return. Idempotent: once the
            // fire components are destroyed/disabled, re-running is a cheap no-op.
            // NOTE: kitbashRoot from a prior build (if any) still exists here because
            // UnityEngine.Object.Destroy is deferred to end-of-frame, so the
            // IsKitbash guard inside still correctly skips our own rock clones.
            NeutralizeDonorFire();

            // Strip prior kitbash root (deferred destroy; re-created below).
            if (kitbashRoot != null) UnityEngine.Object.Destroy(kitbashRoot);

            var zns = ZNetScene.instance;
            if (zns == null) return;
            var rockSrc = zns.GetPrefab("rock_low");
            if (rockSrc == null)
            {
                // Fallback — no rock pile available. The fire is ALREADY neutralized
                // (step 1), so we leave the donor's base mesh visible as a plain
                // non-burning stub. Better a dull stone-ish base than a bonfire.
                Plugin.Log.LogWarning(
                    "[Trailborne/M2] rock_low prefab missing; cairn shows a non-burning bonfire-base stub " +
                    "(fire neutralized, no rock kitbash this build).");
                return;
            }

            kitbashRoot = new GameObject("SBPR_CairnKitbash");
            kitbashRoot.transform.SetParent(transform, worldPositionStays: false);

            // Pile size scales with tier: T1=4, T2=6, T3=8, T4=10, T5=12 stones.
            int stones = 2 + tier * 2;

            // Deterministic seed from ZDO so the same cairn looks the same after reload.
            int seed = 1337;
            if (nview != null && nview.GetZDO() != null)
                seed = nview.GetZDO().m_uid.GetHashCode();
            var rng = new System.Random(seed);

            // Stack from bottom up; each layer shrinks slightly.
            float baseRadius = 0.45f + 0.05f * tier;   // wider pile at higher tier
            float layerHeight = 0.16f;                 // flat layer thickness
            for (int i = 0; i < stones; i++)
            {
                // Reuse only the rock_low's visual children — clone a fresh GO with the mesh.
                var rockClone = GameObject.Instantiate(rockSrc, kitbashRoot.transform);
                // Strip behaviour components — we want art only, no terrain modifier / pickable.
                StripGameplayComponents(rockClone);

                float t01 = (float)i / Mathf.Max(1, stones - 1); // 0..1 from bottom to top
                float ringScale = Mathf.Lerp(1.0f, 0.4f, t01);   // smaller at the top

                float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float offRad = (float)(rng.NextDouble() * baseRadius * ringScale * 0.6f);
                float ox = Mathf.Cos(angle) * offRad;
                float oz = Mathf.Sin(angle) * offRad;
                float oy = layerHeight * i + 0.05f;

                // Lateral scale jitter — flatten Y, vary X/Z
                float sxz = (float)(0.55f + rng.NextDouble() * 0.45f) * (0.8f + 0.4f * ringScale);
                float sy  = (float)(0.18f + rng.NextDouble() * 0.10f);

                rockClone.transform.localPosition = new Vector3(ox, oy, oz);
                rockClone.transform.localRotation = Quaternion.Euler(
                    (float)(rng.NextDouble() * 20.0 - 10.0),
                    (float)(rng.NextDouble() * 360.0),
                    (float)(rng.NextDouble() * 20.0 - 10.0));
                rockClone.transform.localScale = new Vector3(sxz, sy, sxz);
                rockClone.SetActive(true);
            }

            // STEP 3 — now that a rock pile exists, hide the donor bonfire's visible
            // meshes so only the kitbash shows. (Skipped on the fallback path above.)
            HideDonorMeshes();
        }

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
        /// True if <paramref name="t"/> belongs to our runtime rock kitbash, so the
        /// fire-neutralization passes never touch our own stones (only the donor
        /// bonfire's components). kitbashRoot may be the PREVIOUS build's root during
        /// a tier rebuild (deferred destroy) — still correct to exclude it.
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
        /// Visible MESH renderers are handled separately in <see cref="HideDonorMeshes"/>
        /// so the fallback path can keep a plain base visible while still being fireless.
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
        /// </summary>
        private void HideDonorMeshes()
        {
            foreach (var r in GetComponentsInChildren<Renderer>(includeInactive: true))
                if (r != null && !IsKitbash(r.transform)) r.enabled = false;
        }
    }
}
