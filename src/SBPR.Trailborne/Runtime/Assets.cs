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
        public static Sprite? LoadPngAsSprite(string filename)
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

        private static GameObject? holder;   // lazy static cache; genuinely null until first GetHolder()
        private static GameObject GetHolder()
        {
            if (holder == null)
            {
                holder = new GameObject("SBPR.Trailborne.PrefabHolder");
                holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(holder);
            }
            return holder;
        }

        /// <summary>
        /// Clone a registered prefab from ZNetScene under a new name.
        /// Caller is responsible for adding the clone back into ZNetScene + ObjectDB.
        /// </summary>
        public static GameObject? ClonePrefab(string sourceName, string newName)
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

        public static PieceTable? GetHammerPieceTable()
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
        ///
        /// <paramref name="warn"/> defaults true. Pass false ONLY for the
        /// prefab-phase (ZNetScene.Awake) calls that reference an SBPR item which
        /// is registered into ObjectDB LATER in DoObjectDBWiring — that null is
        /// expected and transient (the ODB phase rebuilds the requirement with the
        /// resolved item). The authoritative ODB-phase rebuild keeps warn=true, so
        /// a genuinely-missing item after wiring still screams, and SpecCheck still
        /// validates the final resource set on every boot.
        /// </summary>
        public static Piece.Requirement BuildReq(string resourcePrefabName, int amount, string tag = "Trailborne", bool warn = true)
        {
            var odb = ObjectDB.instance;
            var item = odb?.GetItemPrefab(resourcePrefabName)?.GetComponent<ItemDrop>();
            if (item == null && warn)
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

        public static ItemDrop? FindItemDrop(string prefabName)
        {
            var odb = ObjectDB.instance;
            var go = odb?.GetItemPrefab(prefabName);
            return go?.GetComponent<ItemDrop>();
        }

        /// <summary>
        /// Remove every <see cref="GuidePoint"/> component from a cloned prefab
        /// (root + children). GuidePoint is the vanilla proximity hook that makes
        /// Hugin/the raven pop a tutorial near certain structures (Workbench,
        /// Bed, Portal, etc.). When we clone such a prefab for an SBPR station the
        /// clone inherits the hook, so the raven wrongly treats our piece as the
        /// vanilla one. Strip it so no tutorial fires.
        ///
        /// Uses DestroyImmediate because the clone lives parented under an inactive
        /// holder (so Awake hasn't run) and we want it gone before the prefab is
        /// ever instantiated in the world. Returns the number of components removed.
        /// </summary>
        public static int StripGuidePoints(GameObject prefab)
        {
            if (prefab == null) return 0;
            var hooks = prefab.GetComponentsInChildren<GuidePoint>(includeInactive: true);
            int removed = 0;
            foreach (var gp in hooks)
            {
                if (gp == null) continue;
                try
                {
                    UnityEngine.Object.DestroyImmediate(gp);
                    removed++;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Trailborne] StripGuidePoints: failed to remove a GuidePoint: {e.Message}");
                }
            }
            return removed;
        }

        /// <summary>
        /// Strip a cloned vanilla prefab down to pure DECORATION — remove the
        /// gameplay/networking components (<see cref="ZNetView"/>, <see cref="Piece"/>,
        /// <see cref="WearNTear"/>, every <see cref="Collider"/>) so the object can be
        /// nested under another prefab purely as a visual kitbash child without
        /// registering its own ZDO, becoming separately destructible, or intercepting
        /// interaction raycasts. Renderers / mesh filters / LODGroup are left intact.
        ///
        /// Used by the Painted Sign kitbash to plant a vanilla wood pole under the sign
        /// board. Like <see cref="StripGuidePoints"/> it relies on the clone living
        /// parented under an INACTIVE holder (so no Awake has run and the ZNetView has
        /// no live ZDO yet), and uses DestroyImmediate so the components are gone before
        /// the prefab is ever instantiated in the world. Components are removed in
        /// dependency order (consumers before the ZNetView they reference). Public
        /// UnityEngine API only — clean-room safe (no decompiled IronGate source).
        /// </summary>
        public static void StripToDecorative(GameObject go)
        {
            if (go == null) return;

            void Kill(Component c)
            {
                if (c == null) return;
                try { UnityEngine.Object.DestroyImmediate(c); }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[Trailborne] StripToDecorative: failed to remove {c.GetType().Name}: {e.Message}");
                }
            }

            // Order matters: WearNTear / Piece reference the ZNetView, so drop them first.
            foreach (var c in go.GetComponentsInChildren<WearNTear>(true)) Kill(c);
            foreach (var c in go.GetComponentsInChildren<Piece>(true))      Kill(c);
            foreach (var c in go.GetComponentsInChildren<Collider>(true))   Kill(c);
            foreach (var c in go.GetComponentsInChildren<ZNetView>(true))   Kill(c);
        }

        /// <summary>
        /// Measure the lowest point (the "foot") of every visual mesh under
        /// <paramref name="root"/>, expressed in <paramref name="root"/>'s LOCAL
        /// space. Walks each <see cref="MeshFilter"/>'s shared-mesh AABB, transforms
        /// its 8 corners into root-local space, and returns the minimum Y.
        ///
        /// Reads <c>sharedMesh.bounds</c> (a serialized asset property) and pure
        /// transform math, so it works on a clone still parented under the inactive
        /// holder — no Awake, no live <see cref="Renderer.bounds"/> required. Returns
        /// 0 when the root has no meshes (caller treats that as "pivot is the foot").
        /// Public UnityEngine API only — clean-room safe.
        /// </summary>
        public static float MeasureLocalFootY(GameObject root)
        {
            if (root == null) return 0f;
            var rootT = root.transform;
            float minY = float.MaxValue;
            bool any = false;

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null) continue;
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                var b = mesh.bounds;
                var t = mf.transform;
                for (int xi = -1; xi <= 1; xi += 2)
                for (int yi = -1; yi <= 1; yi += 2)
                for (int zi = -1; zi <= 1; zi += 2)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3(xi, yi, zi));
                    var local  = rootT.InverseTransformPoint(t.TransformPoint(corner));
                    if (local.y < minY) minY = local.y;
                    any = true;
                }
            }

            return any ? minY : 0f;
        }

        /// <summary>
        /// Scale the VISUAL of <paramref name="root"/> by <paramref name="factor"/>
        /// on the Y axis, anchored at the foot plane so the base stays planted on the
        /// ground and the top (e.g. a torch flame) rises to the new height. Operates
        /// on each DIRECT child of the root (Valheim pieces keep their meshes / fx /
        /// light in children; the root carries Piece / ZNetView / Fireplace).
        ///
        /// Two cases per child, both anchored at the measured foot (footY):
        ///   • Child whose subtree contains a mesh (the post / pole geometry):
        ///       localScale.y    *= factor                       (geometry grows tall)
        ///       localPosition.y  = footY + (y - footY) * factor  (base stays planted)
        ///   • Child with NO mesh (the fx_Torch flame, point Light, audio):
        ///       localPosition.y  = footY + (y - footY) * factor  (rides up to the top)
        ///       localScale        UNCHANGED                       (flame stays normal size)
        /// So the post becomes 3× tall while the flame keeps its size and simply sits
        /// at the new top instead of mid-pole — not a bonfire on a stick.
        ///
        /// Foot is MEASURED from the meshes (not assumed at the pivot), so the result
        /// is correct regardless of where the prefab's pivot sits. The root transform
        /// itself is left at identity scale so the placement system (which drives the
        /// root) is unaffected, and root colliders are NOT rescaled (Daniel: "scale
        /// the visual, not necessarily the root collider") — flag for QA if the
        /// collision box should match the taller visual. Public UnityEngine API only.
        /// </summary>
        public static void ScaleVisualHeightAboutFoot(GameObject root, float factor)
        {
            if (root == null || factor <= 0f) return;
            var rootT = root.transform;

            // Warn (don't throw) if the visual lives on the root itself — then there is
            // no child to scale and the caller's intent silently no-ops.
            bool hasChildMesh = false;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf != null && mf.transform != rootT) { hasChildMesh = true; break; }
            if (!hasChildMesh)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne] ScaleVisualHeightAboutFoot: '{root.name}' has no child meshes to scale " +
                    "(visual may be on the root); height scaling skipped. Verify the prefab hierarchy.");
                return;
            }

            float footY = MeasureLocalFootY(root);
            foreach (Transform child in rootT)
            {
                var lp = child.localPosition;
                // Foot-anchored reposition applies to EVERY child so flame/light ride
                // up with the post instead of floating mid-pole.
                child.localPosition = new Vector3(lp.x, footY + (lp.y - footY) * factor, lp.z);

                // Only geometry-bearing children grow taller; pure fx/light/audio
                // children keep their original scale (normal-size flame at the top).
                if (child.GetComponentInChildren<MeshFilter>(true) != null)
                {
                    var ls = child.localScale;
                    child.localScale = new Vector3(ls.x, ls.y * factor, ls.z);
                }
            }
        }
    }
}
