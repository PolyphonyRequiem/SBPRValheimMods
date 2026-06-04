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
    /// </summary>
    public class CairnTag : MonoBehaviour
    {
        public string Color;
        private ZNetView nview;
        private GameObject kitbashRoot;
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
        /// </summary>
        public void BuildKitbashArt()
        {
            int tier = ReadTier();
            if (tier == lastBuiltTier && kitbashRoot != null) return;
            lastBuiltTier = tier;

            // Strip prior kitbash root
            if (kitbashRoot != null) UnityEngine.Object.Destroy(kitbashRoot);

            var zns = ZNetScene.instance;
            if (zns == null) return;
            var rockSrc = zns.GetPrefab("rock_low");
            if (rockSrc == null)
            {
                Plugin.Log.LogWarning("[Trailborne/M2] rock_low prefab missing; cairn art will be bonfire-stub.");
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

            // Push the bonfire's vanilla mesh out of view (children named "Flame"/"Fuel"/etc).
            // We keep the WearNTear + Piece + ZNetView on the root prefab — only hide visuals.
            HideVanillaVisualChildren();
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

        private void HideVanillaVisualChildren()
        {
            // Bonfire base art lives under named children; turn them off without nuking the prefab.
            string[] hideNames = { "Flame", "fire", "Fire", "Smoke", "smoke", "model", "default", "BFX", "fuel", "Fuel" };
            foreach (var t in GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t == null || t == transform) continue;
                if (t.parent == kitbashRoot?.transform || t.IsChildOf(kitbashRoot?.transform ?? transform) && kitbashRoot != null && t != kitbashRoot.transform) continue;
                foreach (var n in hideNames)
                {
                    if (t.name.Contains(n))
                    {
                        var rs = t.GetComponentsInChildren<Renderer>(includeInactive: true);
                        foreach (var r in rs) if (r != null) r.enabled = false;
                        break;
                    }
                }
            }
        }
    }
}
