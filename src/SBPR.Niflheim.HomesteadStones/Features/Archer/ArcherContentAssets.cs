using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.Archer
{
    /// <summary>
    /// T025-RT — minimal, self-contained additive content helpers for the Archer / Practice Range
    /// runtime seam (item + recipe + build-piece wiring). HomesteadStones previously shipped NO item /
    /// recipe / piece registration surface at all (unlike SBPR.Trailborne, which owns a rich
    /// <c>Runtime/Assets.cs</c>); this file is the smallest correct equivalent for the one item this
    /// node needs — the Practice Arrow (<c>ArrowPractice</c>) — plus the reflection-backed ZNetScene /
    /// ObjectDB registration primitives shared by <see cref="ArcherContent"/>.
    ///
    /// ADR-0006 (additive): the Practice Arrow is built from <c>new GameObject()</c> + AddComponent of
    /// only the components a dropped/equippable Ammo item needs — never by Instantiating and stripping a
    /// vanilla arrow prefab. Reading a vanilla arrow's <em>blueprint</em> field values (mesh/material via
    /// <c>ZNetScene.GetPrefab</c>) is reference-not-clone and is permitted; we do exactly that for the
    /// visual so the practice arrow reads as an arrow in-world without cloning its ZNetView-bearing root.
    ///
    /// net48-only (UnityEngine/Valheim types) — not link-compiled into the net8 test suite. The
    /// engine-free authored values (item id, recipe 100/8, 0 ammo damage) live in the shipped
    /// <c>Adapters/Archer/PracticeRangeProvider.cs</c> and ARE unit-tested.
    /// </summary>
    internal static class ArcherContentAssets
    {
        private static GameObject? holder;

        private static GameObject GetHolder()
        {
            if (holder == null)
            {
                holder = new GameObject("SBPR.Niflheim.HomesteadStones.Archer.PrefabHolder");
                holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(holder);
            }
            return holder;
        }

        /// <summary>A fresh empty GameObject parented under the inactive holder so no Awake fires while
        /// the caller assembles components on it.</summary>
        internal static GameObject NewHolderObject(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(GetHolder().transform, worldPositionStays: false);
            return go;
        }

        private static Sprite? fallbackIcon;
        /// <summary>A guaranteed-loadable code-generated magenta placeholder sprite. A fresh SharedData
        /// defaults <c>m_icons</c> to the empty array; vanilla <c>ItemDrop.GetIcon()</c> indexes it with
        /// no bounds guard, so an additively-constructed item with an empty icon array throws in the
        /// crafting panel. Pre-seeding this makes a missing icon degrade to a visible placeholder, never a
        /// crash. Vivid magenta so a missing real icon is obvious in-world.</summary>
        internal static Sprite FallbackIcon
        {
            get
            {
                if (fallbackIcon == null)
                {
                    var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    tex.SetPixel(0, 0, Color.magenta);
                    tex.Apply();
                    fallbackIcon = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
                    fallbackIcon.name = "SBPR_Niflheim_FallbackIcon";
                }
                return fallbackIcon;
            }
        }

        /// <summary>Register a constructed prefab into the live ZNetScene named-prefab map (reflection —
        /// no public Add exists). Idempotent by stable hash.</summary>
        internal static void RegisterPrefabInZNetScene(ZNetScene zns, GameObject prefab)
        {
            if (zns == null || prefab == null) return;
            int hash = prefab.name.GetStableHashCode();
            var field = typeof(ZNetScene).GetField("m_namedPrefabs",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(zns) is Dictionary<int, GameObject> named && !named.ContainsKey(hash))
            {
                zns.m_prefabs.Add(prefab);
                named.Add(hash, prefab);
            }
        }

        /// <summary>Register an item prefab into ObjectDB and refresh its internal registers. Idempotent
        /// by stable hash.</summary>
        internal static void RegisterItemInObjectDB(GameObject itemPrefab)
        {
            var odb = ObjectDB.instance;
            if (odb == null || itemPrefab == null) return;
            if (odb.GetItemPrefab(itemPrefab.name.GetStableHashCode()) != null) return;
            odb.m_items.Add(itemPrefab);
            typeof(ObjectDB).GetMethod("UpdateRegisters",
                BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(odb, null);
        }

        /// <summary>ADDITIVELY graft a vanilla prefab's first non-empty mesh child as a cosmetic child of
        /// <paramref name="dst"/> — by READING <c>MeshFilter.sharedMesh</c> + <c>MeshRenderer.sharedMaterials</c>
        /// references (reference-not-clone, ADR-0006), never Instantiating the donor. Returns the grafted
        /// child, or null if the blueprint has no mesh.</summary>
        internal static GameObject? GraftMeshFromBlueprint(GameObject? blueprint, GameObject dst, string childName)
        {
            if (blueprint == null || dst == null) return null;
            MeshFilter? srcMf = null;
            foreach (var mf in blueprint.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf != null && mf.sharedMesh != null) { srcMf = mf; break; }
            }
            if (srcMf == null) return null;

            var child = new GameObject(childName);
            child.transform.SetParent(dst.transform, worldPositionStays: false);
            var srcT = srcMf.transform;
            var blueT = blueprint.transform;
            child.transform.localPosition = blueT.InverseTransformPoint(srcT.position);
            child.transform.localRotation = Quaternion.Inverse(blueT.rotation) * srcT.rotation;
            child.transform.localScale = srcT.lossyScale;

            var mf2 = child.AddComponent<MeshFilter>();
            mf2.sharedMesh = srcMf.sharedMesh;
            var mr2 = child.AddComponent<MeshRenderer>();
            var srcMr = srcMf.GetComponent<MeshRenderer>();
            if (srcMr != null) mr2.sharedMaterials = srcMr.sharedMaterials;
            return child;
        }
    }
}
