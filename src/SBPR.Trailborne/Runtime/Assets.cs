using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Runtime
{
    /// <summary>
    /// M0 helpers: PNG → Sprite loader, prefab cloning, hammer/objectdb wiring.
    /// </summary>
    internal static class Assets
    {
        public static Sprite LoadPngAsSprite(string filename)
        {
            try
            {
                var p = Path.Combine(Plugin.PluginFolder, filename);
                if (!File.Exists(p))
                {
                    Plugin.Log.LogWarning($"[Trailborne] Icon missing on disk: {p}");
                    return null;
                }
                var bytes = File.ReadAllBytes(p);
                var tex   = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                // Use reflection to dodge a netstandard2.1 ReadOnlySpan overload
                // that the net48 compiler can't resolve.
                var loadImage = typeof(ImageConversion).GetMethod("LoadImage",
                    new[] { typeof(Texture2D), typeof(byte[]) });
                loadImage.Invoke(null, new object[] { tex, bytes });
                tex.filterMode = FilterMode.Bilinear;
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                     new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Trailborne] LoadPngAsSprite failed for {filename}: {e}");
                return null;
            }
        }

        private static GameObject _holder;
        private static GameObject GetHolder()
        {
            if (_holder == null)
            {
                _holder = new GameObject("SBPR.Trailborne.PrefabHolder");
                _holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_holder);
            }
            return _holder;
        }

        /// <summary>
        /// Clone a registered prefab from ZNetScene under a new name.
        /// Caller is responsible for adding the clone back into ZNetScene + ObjectDB.
        /// </summary>
        public static GameObject ClonePrefab(string sourceName, string newName)
        {
            var zns = ZNetScene.instance;
            if (zns == null)
            {
                Plugin.Log.LogError("[Trailborne] ClonePrefab called with no ZNetScene.");
                return null;
            }
            var src = zns.GetPrefab(sourceName);
            if (src == null)
            {
                Plugin.Log.LogError($"[Trailborne] Source prefab '{sourceName}' not in ZNetScene.");
                return null;
            }
            // Parent under inactive holder so Awake() does NOT fire on the clone
            // (otherwise ZNetView Awake registers an invalid ZDO and ZNetScene
            // spams NullReferenceException on every Update).
            var holder = GetHolder();
            var clone = UnityEngine.Object.Instantiate(src, holder.transform);
            clone.name = newName;
            return clone;
        }

        public static void RegisterPrefabInZNetScene(GameObject prefab)
        {
            var zns = ZNetScene.instance;
            if (zns == null || prefab == null) return;
            int hash = prefab.name.GetStableHashCode();
            // Use reflection on the private dict because Add* helpers don't exist publicly.
            var namedField = typeof(ZNetScene).GetField("m_namedPrefabs",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var named = (System.Collections.Generic.Dictionary<int, GameObject>)namedField.GetValue(zns);
            if (!named.ContainsKey(hash))
            {
                zns.m_prefabs.Add(prefab);
                named.Add(hash, prefab);
            }
        }

        public static void RegisterItemInObjectDB(GameObject itemPrefab)
        {
            var odb = ObjectDB.instance;
            if (odb == null || itemPrefab == null) return;
            if (odb.GetItemPrefab(itemPrefab.name.GetStableHashCode()) != null) return;
            odb.m_items.Add(itemPrefab);
            // Refresh internal indexes
            typeof(ObjectDB).GetMethod("UpdateRegisters",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(odb, null);
        }

        public static void RegisterRecipe(Recipe recipe)
        {
            var odb = ObjectDB.instance;
            if (odb == null || recipe == null) return;
            if (!odb.m_recipes.Contains(recipe))
                odb.m_recipes.Add(recipe);
        }

        public static PieceTable GetHammerPieceTable()
        {
            var odb = ObjectDB.instance;
            if (odb == null) return null;
            var hammer = odb.GetItemPrefab("Hammer");
            if (hammer == null) return null;
            var drop = hammer.GetComponent<ItemDrop>();
            return drop?.m_itemData?.m_shared?.m_buildPieces;
        }

        public static void AddPieceToTable(GameObject piecePrefab, PieceTable table)
        {
            if (piecePrefab == null || table == null) return;
            if (!table.m_pieces.Contains(piecePrefab))
                table.m_pieces.Add(piecePrefab);
        }

        /// <summary>
        /// Centralized Piece.Requirement builder. Resolves the resource prefab
        /// against ObjectDB and LOUDLY logs if it's missing — previously a
        /// missing prefab silently produced a Requirement with null m_resItem,
        /// which the game accepts and then quietly skips the cost.
        /// </summary>
        public static Piece.Requirement BuildReq(string resourcePrefabName, int amount, string tag = "Trailborne")
        {
            var odb = ObjectDB.instance;
            var item = odb?.GetItemPrefab(resourcePrefabName)?.GetComponent<ItemDrop>();
            if (item == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/{tag}] BuildReq: resource prefab '{resourcePrefabName}' NOT FOUND in ObjectDB. " +
                    "Recipe ingredient will silently fail to require this resource. Check prefab name vs decomp.");
            }
            return new Piece.Requirement
            {
                m_resItem = item,
                m_amount  = amount,
                m_recover = true,
            };
        }

        public static ItemDrop FindItemDrop(string prefabName)
        {
            var odb = ObjectDB.instance;
            var go = odb?.GetItemPrefab(prefabName);
            return go?.GetComponent<ItemDrop>();
        }
    }
}
