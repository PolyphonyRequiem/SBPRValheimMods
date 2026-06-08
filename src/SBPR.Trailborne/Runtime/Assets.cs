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
        /// Add a piece to a PieceTable, deduping by NAME rather than object
        /// reference. Use this for any piece added to a PERSISTENT (vanilla-shared)
        /// PieceTable like the Hammer's, where a re-join re-clones our prefab into a
        /// brand-new GameObject (a different reference, same name) and the plain
        /// reference-based AddPieceToTable would happily append a SECOND copy.
        ///
        /// That's the "two Explorer's Benches" bug: each time the player backs out
        /// to character-select and rejoins Niflheim, ZNetScene.Awake re-fires, the
        /// bench is re-cloned, and the new clone got appended next to the stale one
        /// because Contains() compared references. Deduping by name makes the add
        /// idempotent across any number of re-joins: stale same-named entries are
        /// stripped first, then the current clone is added, so the menu always shows
        /// exactly one, pointing at the freshest registration.
        ///
        /// (Tables we rebuild from scratch each join — e.g. the spade's
        /// SBPR_SpadePieceTable — don't need this; they start from a new empty list
        /// every time. It's specifically the vanilla Hammer table, which we mutate
        /// in place and which survives scene reloads, that accumulates.)
        /// </summary>
        public static void AddOrReplacePieceByName(GameObject piecePrefab, PieceTable table)
        {
            if (piecePrefab == null || table == null) return;
            string name = piecePrefab.name;
            int removed = table.m_pieces.RemoveAll(
                p => p == null || p.name == name);
            if (removed > 1)
                Plugin.Log.LogWarning(
                    $"[Trailborne] AddOrReplacePieceByName: stripped {removed} stale/duplicate " +
                    $"'{name}' entries from a PieceTable before re-adding (re-join accumulation). " +
                    "If this climbs every join, a persistent table is leaking.");
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
        /// Measure the HIGHEST point (the "crown") of every visual mesh under
        /// <paramref name="root"/>, expressed in <paramref name="root"/>'s LOCAL space.
        /// Exact mirror of <see cref="MeasureLocalFootY"/> (max instead of min Y): walks
        /// each <see cref="MeshFilter"/>'s shared-mesh AABB, transforms its 8 corners
        /// through the full TRS (so non-identity child rotation/scale is honored) into
        /// root-local space, and returns the maximum Y. Returns 0 when the root has no
        /// meshes. Kept as a standalone twin of MeasureLocalFootY rather than refactoring
        /// that validated method into a shared core — minimal blast radius on a path with
        /// proven ground-truth behavior. Public UnityEngine API only — clean-room safe.
        /// </summary>
        public static float MeasureLocalTopY(GameObject root)
        {
            if (root == null) return 0f;
            var rootT = root.transform;
            float maxY = float.MinValue;
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
                    if (local.y > maxY) maxY = local.y;
                    any = true;
                }
            }

            return any ? maxY : 0f;
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

        /// <summary>
        /// Graft a SMALL, torch-tier cosmetic fire under <paramref name="parent"/> by
        /// instantiating just the flame VFX + glow light + crackle SFX subtrees off the
        /// vanilla <c>piece_groundtorch_wood</c> donor — NOT the whole torch prefab.
        ///
        /// 🔴 v0.2.8 (Daniel: "replace it with the torch vfx and sfx (and light) … we
        /// don't need the whole prefab, just the components"). Why the torch and not the
        /// bonfire: the torch flame is already marker-scale, so we inherit "small fire"
        /// for free instead of fighting bonfire-scale particles.
        ///
        /// CLEAN-ROOM / no-orphan: the three grafted children
        /// (<c>fx_Torch_Basic</c>, <c>Point light</c>, <c>sfx_fire_loop</c>) live under the
        /// torch's INACTIVE <c>_enabled</c> node and carry NO ZNetView — only the torch
        /// ROOT does. Instantiating a ZNetView-free child subtree wakes no ZDO, so there
        /// is nothing for ZNetScene.RemoveObjects to orphan (the opposite of the PR #23
        /// trap). We read the donor via GetPrefab (never instantiate the torch itself).
        ///
        /// Two donor traps neutralized on the grafted copy:
        ///   • <c>sfx_fire_loop</c> carries <see cref="TimedDestruction"/>, which would
        ///     self-destroy our audio node a second after spawn → strip it.
        ///   • The torch's <c>FireWarmth</c> child is an <see cref="EffectArea"/> that
        ///     grants heat — we deliberately do NOT copy it, and defensively strip any
        ///     EffectArea that rides along, so the cairn grants no heat/burn.
        ///
        /// Returns the assembled fire-root GameObject (parented, at local Y =
        /// <paramref name="localY"/>), or null if the donor torch isn't registered or
        /// we're on a headless server (no renderers/particles exist there).
        /// </summary>
        public static GameObject? GraftTorchFire(
            Transform parent, float localY, float lightIntensity, float lightRange)
        {
            if (parent == null) return null;
            var zns = ZNetScene.instance;
            if (zns == null) return null;

            var torch = zns.GetPrefab("piece_groundtorch_wood")
                        ?? zns.GetPrefab("piece_groundtorch");
            if (torch == null)
            {
                Plugin.Log.LogWarning(
                    "[Trailborne/M2] GraftTorchFire: no torch donor (piece_groundtorch_wood/piece_groundtorch) " +
                    "registered; cairn shows no flame this build.");
                return null;
            }

            // The cosmetics live under the torch's inactive "_enabled" node.
            var enabled = torch.transform.Find("_enabled");
            if (enabled == null)
            {
                Plugin.Log.LogWarning(
                    "[Trailborne/M2] GraftTorchFire: torch donor has no '_enabled' child " +
                    "(vanilla structure changed?); cairn shows no flame this build.");
                return null;
            }

            var fireRoot = new GameObject("SBPR_CairnFire");
            fireRoot.transform.SetParent(parent, worldPositionStays: false);
            fireRoot.transform.localPosition = new Vector3(0f, localY, 0f);

            // Copy ONLY these three cosmetic subtrees (NOT FireWarmth / EffectArea).
            int grafted = 0;
            foreach (var childName in new[] { "fx_Torch_Basic", "Point light", "sfx_fire_loop" })
            {
                var src = enabled.Find(childName);
                if (src == null) continue;
                var copy = UnityEngine.Object.Instantiate(src.gameObject, fireRoot.transform);
                copy.name = childName;
                copy.SetActive(true);
                grafted++;
            }

            if (grafted == 0)
            {
                Plugin.Log.LogWarning(
                    "[Trailborne/M2] GraftTorchFire: none of the expected torch cosmetic children found; " +
                    "removing empty fire root.");
                UnityEngine.Object.Destroy(fireRoot);
                return null;
            }

            // Trap 1: the sfx node's TimedDestruction would self-destroy our audio.
            foreach (var td in fireRoot.GetComponentsInChildren<TimedDestruction>(true))
                if (td != null) UnityEngine.Object.DestroyImmediate(td);

            // Trap 2: defensively strip any heat/burn area that rode along (none expected
            // from the three children we copy, but belt-and-braces — a marker grants none).
            foreach (var ea in fireRoot.GetComponentsInChildren<EffectArea>(true))
                if (ea != null) UnityEngine.Object.DestroyImmediate(ea);

            // Dim the grafted light below a vanilla torch so it reads as a small marker
            // glow. Keep the FIRST light, drop any extras.
            bool keptLight = false;
            foreach (var l in fireRoot.GetComponentsInChildren<Light>(true))
            {
                if (l == null) continue;
                if (!keptLight)
                {
                    keptLight = true;
                    l.intensity = lightIntensity;
                    l.range = lightRange;
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(l);
                }
            }

            return fireRoot;
        }

        /// <summary>
        /// Construct a networked build-piece SHELL from scratch — ADR-0006 additive
        /// construction, the replacement for clone-then-strip. Returns a fresh
        /// GameObject parented under the inactive holder (so its Awake has NOT fired),
        /// carrying ONLY the skeleton every placeable/damageable/persistent SBPR piece
        /// needs: <see cref="ZNetView"/> + <see cref="Piece"/> + <see cref="WearNTear"/>
        /// + a root <see cref="BoxCollider"/>. The caller adds visuals + feature
        /// components, sets piece/wear fields, and registers it in ZNetScene.
        ///
        /// 🔴 Why this exists (ADR-0006): we no longer Instantiate a vanilla prefab and
        /// strip it. A ZNetView needs only (a) ZDOMan up and (b) a registered prefab
        /// name — it reads three PUBLIC fields (m_persistent/m_type/m_distant) and makes
        /// its own ZDO. So we AddComponent a working one; the donor never gave us
        /// anything we can't set ourselves. This structurally eliminates the
        /// runtime-clone ZDO-orphan crash class (the clone never happens).
        ///
        /// REFERENCE (not clone): the three WearNTear EffectLists (destroyed / hit /
        /// switch) and the Piece placement/removal effects are deep VALUE-copied off a
        /// clean vanilla stone donor read via GetPrefab (fires no Awake). Copying an
        /// EffectList's value is reference, not inheritance — we own the component, we
        /// just point its effect tables at the same vanilla VFX/SFX a stone piece uses.
        ///
        /// Networking fields are set to vanilla build-piece norms (verified against the
        /// decompiled WearNTear/ZNetView): persistent ZDO, Default object type, stone
        /// material, supports-bearing, non-burnable. m_health is left at the WearNTear
        /// default here; the caller overrides per-tier.
        /// </summary>
        public static GameObject? ConstructPieceShell(string name, string referenceDonorName)
        {
            var zns = ZNetScene.instance;
            if (zns == null)
            {
                Plugin.Log.LogError("[Trailborne] ConstructPieceShell called with no ZNetScene.");
                return null;
            }

            // Parent under the inactive holder BEFORE adding components, so no Awake
            // fires during construction (same discipline as ClonePrefab). The ZNetView
            // we add will wake — correctly, down the CreateNewZDO path — only once this
            // shell is instantiated into the world by the placement system.
            var holder = GetHolder();
            var go = new GameObject(name);
            go.transform.SetParent(holder.transform, worldPositionStays: false);

            // Root collider — a build piece needs one for placement raycasts + hits.
            // Caller may resize; a unit box is a safe default.
            var box = go.AddComponent<BoxCollider>();
            box.size = Vector3.one;

            // ZNetView — the networked identity. Public fields the decompiled
            // ZNetView.Awake reads to build its ZDO: persistent piece, Default type,
            // not distant (cairns are normal-range objects).
            var nview = go.AddComponent<ZNetView>();
            nview.m_persistent = true;
            nview.m_type = ZDO.ObjectType.Default;
            nview.m_distant = false;

            // Piece — placement identity. Reference-copy placement/removal effects off
            // the stone donor so it sounds/looks like a real stone build on place.
            var piece = go.AddComponent<Piece>();
            piece.m_groundPiece = false;
            piece.m_groundOnly = false;
            piece.m_cultivatedGroundOnly = false;
            piece.m_waterPiece = false;
            piece.m_noInWater = false;
            piece.m_allowedInDungeons = false;
            piece.m_canBeRemoved = true;
            piece.m_targetNonPlayerBuilt = false;

            // WearNTear — health / decay / material. Vanilla stone-build norms; the
            // cairn's damage IMMUNITY + decay are layered by CairnPatches Harmony hooks
            // (they key off CairnTag, so they apply equally to an additive piece).
            var wnt = go.AddComponent<WearNTear>();
            wnt.m_materialType = WearNTear.MaterialType.Stone;
            wnt.m_health = 100f;                 // caller overrides per-tier
            wnt.m_noRoofWear = true;             // weather decay handled by our backfill, not roof rules
            wnt.m_noSupportWear = true;          // a cairn never structurally collapses from lack of support
            wnt.m_supports = true;
            wnt.m_burnable = false;              // stone marker — never catches fire
            wnt.m_ashDamageImmune = true;
            wnt.m_triggerPrivateArea = true;
            wnt.m_autoCreateFragments = true;

            // REFERENCE-COPY the effect tables off a clean stone donor (value copy; the
            // donor is read via GetPrefab and never instantiated). If the donor is
            // missing we leave empty EffectLists — the piece still works, just silent
            // on hit/destroy (acceptable degradation, logged once).
            var donor = zns.GetPrefab(referenceDonorName);
            if (donor != null)
            {
                var dWnt = donor.GetComponent<WearNTear>();
                if (dWnt != null)
                {
                    wnt.m_destroyedEffect = dWnt.m_destroyedEffect;
                    wnt.m_hitEffect = dWnt.m_hitEffect;
                    wnt.m_switchEffect = dWnt.m_switchEffect;
                }
                var dPiece = donor.GetComponent<Piece>();
                if (dPiece != null)
                {
                    piece.m_placeEffect = dPiece.m_placeEffect;
                }
            }
            else
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne] ConstructPieceShell: reference donor '{referenceDonorName}' not found; " +
                    "piece will have no hit/destroy/place effects (still functional, just silent).");
            }

            return go;
        }
    }
}
