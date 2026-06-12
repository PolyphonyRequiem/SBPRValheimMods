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

        private static Sprite? _fallbackIcon;   // lazy static cache; one shared instance (see FallbackIcon)
        /// <summary>
        /// A guaranteed-loadable, code-generated placeholder sprite (no disk dependency, so the
        /// guarantee holds even if the modpack ships zero PNGs). Deliberately a vivid magenta so a
        /// fallback is VISUALLY OBVIOUS in-game ("this item's real icon didn't load") and trivially
        /// identity-checkable by SpecCheck. Lazily created; one shared instance so reference (==)
        /// identity comparison works for the SpecCheck icon-load assertion (C1).
        ///
        /// Why this exists: <see cref="ConstructItemShell"/> news a fresh SharedData whose
        /// <c>m_icons</c> defaults to the vanilla empty array. Vanilla
        /// <c>ItemDrop.ItemData.GetIcon()</c> indexes <c>m_icons[m_variant]</c> with no bounds
        /// guard, so an additively-constructed item with an empty <c>m_icons</c> throws
        /// IndexOutOfRangeException in the crafting panel on selection. Pre-seeding this fallback
        /// makes a missing icon degrade to a visible placeholder, never a crash.
        /// </summary>
        public static Sprite FallbackIcon
        {
            get
            {
                if (_fallbackIcon == null)
                {
                    var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    tex.SetPixel(0, 0, Color.magenta);
                    tex.Apply();
                    _fallbackIcon = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
                    _fallbackIcon.name = "SBPR_FallbackIcon";
                }
                return _fallbackIcon;
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
        /// Create a fresh empty GameObject parented under the inactive prefab holder, so
        /// no <c>Awake</c> fires while a feature module assembles its components on it
        /// (the same discipline <see cref="ClonePrefab"/> + <see cref="ConstructPieceShell"/>
        /// use). Returns null only if the holder can't be created (never, in practice).
        /// ADR-0006: the additive entry point for a feature that wants full control over
        /// which components it adds, rather than the fixed skeleton ConstructPieceShell bakes.
        /// </summary>
        public static GameObject NewHolderObject(string name)
        {
            var h = GetHolder();
            var go = new GameObject(name);
            go.transform.SetParent(h.transform, worldPositionStays: false);
            return go;
        }

        /// <summary>
        /// ADDITIVELY graft a vanilla prefab's VISUAL mesh as a fresh child of
        /// <paramref name="dst"/> — by READING the blueprint's <c>MeshFilter.sharedMesh</c>
        /// + <c>MeshRenderer.sharedMaterials</c> references and attaching them to a NEW
        /// GameObject (with the donor child's local TRS), NOT by Instantiating the
        /// blueprint. Reading a shared mesh/material reference off a vanilla prefab is
        /// explicitly permitted by ADR-0006 ("Copying ... a shared mesh onto our own
        /// constructed GameObject is reference, not inheritance"); we never Instantiate the
        /// ZNetView-bearing donor, so there is no init-ZDO window to orphan.
        ///
        /// Finds the first non-empty MeshFilter under <paramref name="blueprint"/> whose
        /// GameObject name contains <paramref name="meshChildHint"/> (case-insensitive), or
        /// the first mesh anywhere if the hint is null/empty. Returns the grafted child, or
        /// null if the blueprint has no matching mesh (logged once). The grafted child
        /// carries ONLY MeshFilter + MeshRenderer — no collider, no ZNetView, no script.
        /// </summary>
        public static GameObject? GraftMeshFromBlueprint(
            GameObject? blueprint, GameObject dst, string childName, string? meshChildHint = null)
        {
            if (blueprint == null || dst == null) return null;

            MeshFilter? srcMf = null;
            foreach (var mf in blueprint.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                if (string.IsNullOrEmpty(meshChildHint) ||
                    mf.gameObject.name.IndexOf(meshChildHint, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    srcMf = mf;
                    break;
                }
            }
            // Fall back to the first mesh of any name if the hint matched nothing.
            if (srcMf == null)
            {
                foreach (var mf in blueprint.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf != null && mf.sharedMesh != null) { srcMf = mf; break; }
                }
            }
            if (srcMf == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne] GraftMeshFromBlueprint: blueprint '{blueprint.name}' has no mesh to graft " +
                    $"(child '{childName}' will be empty). Visual will be bare — still functional.");
                return null;
            }

            var child = new GameObject(childName);
            child.transform.SetParent(dst.transform, worldPositionStays: false);
            // Preserve the donor mesh-child's local transform relative to the donor ROOT,
            // so the plank/post lands where it does on the vanilla prefab.
            var srcT = srcMf.transform;
            var blueT = blueprint.transform;
            child.transform.localPosition = blueT.InverseTransformPoint(srcT.position);
            child.transform.localRotation = Quaternion.Inverse(blueT.rotation) * srcT.rotation;
            child.transform.localScale    = srcT.lossyScale;

            var mf2 = child.AddComponent<MeshFilter>();
            mf2.sharedMesh = srcMf.sharedMesh;     // reference, not a copy — clean-room safe
            var mr2 = child.AddComponent<MeshRenderer>();
            var srcMr = srcMf.GetComponent<MeshRenderer>();
            if (srcMr != null) mr2.sharedMaterials = srcMr.sharedMaterials;  // reference array
            return child;
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

        /// <summary>
        /// Resolve a vanilla op prefab by NAME from a vanilla tool's build
        /// <see cref="PieceTable"/> (the tool's <c>m_itemData.m_shared.m_buildPieces</c>),
        /// instead of via <c>ZNetScene.GetPrefab</c>. Returns the live GameObject to be
        /// read as a BLUEPRINT (mesh/material/EffectList/field values) — NOT cloned.
        ///
        /// WHY THIS EXISTS: modern <see cref="TerrainOp"/> ops (<c>replant_v2</c>,
        /// <c>path_v2</c>, …) carry NO <see cref="ZNetView"/> — only <see cref="Piece"/> +
        /// <see cref="TerrainOp"/>. <c>ZNetScene.Awake</c> builds its <c>m_namedPrefabs</c>
        /// lookup ONLY from <c>m_prefabs</c> (ZNetView-bearing) and <c>m_nonNetViewPrefabs</c>,
        /// and the <c>_v2</c> ops are in NEITHER serialized list, so
        /// <c>ZNetScene.GetPrefab("replant_v2")</c> returns <c>null</c> at EVERY ZNetScene
        /// phase on BOTH the dedicated server and a client (the niflheim v0.2.12 boot log
        /// logged it null three times — that regression vanished the grass tool). The only
        /// place a live <c>_v2</c> reference exists is as a baked asset reference inside the
        /// vanilla tool's build PieceTable. Reaching it there is exactly how the vanilla
        /// tool itself uses it — and how WE must reach it to read its real field values.
        ///
        /// Call this when <see cref="ObjectDB"/> is populated (the ObjectDB-wiring phase),
        /// because the tool item prefab + its serialized <c>m_shared.m_buildPieces</c> are
        /// read from ObjectDB. Returns <c>null</c> if the tool, its PieceTable, or the named
        /// op can't be found — the caller decides how loud to be.
        ///
        /// Reads only public UnityEngine / Valheim asset fields — clean-room safe (we read
        /// the base game's own assets to mod the base game; permitted, ADR-0001).
        /// </summary>
        public static GameObject? FindOpInToolPieceTable(string toolItemName, string opPrefabName)
        {
            var odb = ObjectDB.instance;
            // ObjectDB is the canonical home for item prefabs + their SharedData; fall back
            // to ZNetScene only if the tool somehow isn't in ODB (it normally is).
            GameObject? tool = odb?.GetItemPrefab(toolItemName);
            if (tool == null) tool = ZNetScene.instance?.GetPrefab(toolItemName);
            if (tool == null) return null;

            var table = tool.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces;
            if (table?.m_pieces == null) return null;

            foreach (var go in table.m_pieces)
            {
                if (go != null && go.name == opPrefabName)
                    return go;
            }
            return null;
        }

        /// <summary>
        /// ADDITIVELY construct a terrain-op build piece (ADR-0006) that mirrors a vanilla
        /// modern <see cref="TerrainOp"/> op (<c>path_v2</c> / <c>replant_v2</c>) WITHOUT
        /// cloning it. Returns a fresh GameObject parented under the inactive holder
        /// (so no <c>Awake</c> fires during construction), carrying exactly
        /// <see cref="Piece"/> + <see cref="TerrainOp"/> — the same two components the
        /// vanilla op has, and (deliberately) NO <see cref="ZNetView"/>.
        ///
        /// 🔴 WHY ADDITIVE + NO ZNetView + NOT REGISTERED IN ZNetScene (the whole fix —
        /// attempt #3, t_6fc9b3fa): a vanilla <c>TerrainOp</c> is fire-and-forget. When
        /// <see cref="Player.PlacePiece"/> instantiates it, <c>TerrainOp.Awake</c> bakes its
        /// paint straight into the per-zone heightmap compiler (<c>TerrainComp</c>, which
        /// owns the persistent terrain ZDO and RPCs the change to clients) and then
        /// <c>Destroy(gameObject)</c>s itself. So the op piece:
        ///   • needs NO ZNetView (the TerrainComp, not the op, owns persistence/networking);
        ///   • must NOT be registered in ZNetScene — vanilla's OWN <c>path_v2</c>/<c>replant_v2</c>
        ///     aren't either (they live only as PieceTable refs). Registering a
        ///     self-destructing op was the v0.2.14 client-hang: on world load
        ///     <c>ZNetScene.CreateObject</c> would find it, Instantiate it (firing the
        ///     self-destruct) and leave the init-ZDO unconsumed → "not used when creating".
        ///     UNregistered, <c>CreateObject</c> early-returns null BEFORE instantiating, so
        ///     any legacy orphan ZDO is dropped cleanly by vanilla — no warning possible.
        /// This structurally removes BOTH the precedence fight (no persistent op peer) AND
        /// the orphan-ZDO hang (nothing to orphan), which is exactly why the vanilla
        /// Hoe/Cultivator path↔grass coexist on one tile.
        ///
        /// REFERENCE (not clone) the BLUEPRINT op: pass the live <c>path_v2</c>/<c>replant_v2</c>
        /// resolved via <see cref="FindOpInToolPieceTable"/> as <paramref name="blueprint"/>.
        /// We read (value-copy) its <c>Piece.m_icon</c> + <c>Piece.m_placeEffect</c> and
        /// REPARENT a fresh instance of its <c>_GhostOnly</c> preview child (a ZNetView-free
        /// cosmetic subtree — same safe graft pattern as <see cref="GraftTorchFire"/>), so
        /// the placement ghost looks identical to vanilla's. Reading an asset / copying an
        /// EffectList value is reference, not inheritance — clean-room safe.
        ///
        /// Op settings are set EXPLICITLY from known decomp values (the caller passes
        /// <paramref name="paintType"/> + <paramref name="paintRadius"/>); level/smooth/raise
        /// are LEFT at the Settings default <c>false</c> so no width can ever raise/level/
        /// smooth terrain — the PR #16 guard holds BY CONSTRUCTION, not by remembering to
        /// avoid writing a field on a clone.
        /// </summary>
        public static GameObject? ConstructTerrainOpPiece(
            string name,
            GameObject? blueprint,
            TerrainModifier.PaintType paintType,
            float paintRadius,
            bool vegetationGroundOnly,
            string niceName,
            string description)
        {
            var holder = GetHolder();
            var go = new GameObject(name);
            go.transform.SetParent(holder.transform, worldPositionStays: false);

            // ── TerrainOp: the fire-and-forget paint applier (no ZNetView) ──
            var op = go.AddComponent<TerrainOp>();
            // m_settings is a [Serializable] field already newed by the class initializer,
            // but a fresh AddComponent gives us a fresh Settings — set the paint fields we
            // intend and leave level/smooth/raise at their stock false (PR #16 guard).
            op.m_settings = new TerrainOp.Settings
            {
                m_paintCleared = true,            // matches vanilla path_v2/replant_v2 (paint IS the op)
                m_paintType    = paintType,
                m_paintRadius  = paintRadius,
                // m_level / m_smooth / m_raise default false; m_*Radius defaults are inert
                // while their bool is false. Explicit for the reader:
                m_level  = false,
                m_smooth = false,
                m_raise  = false,
            };

            // ── Piece: placement identity (mirrors the vanilla op's Piece flags) ──
            var piece = go.AddComponent<Piece>();
            piece.m_name        = niceName;
            piece.m_description = description;
            piece.m_category    = Piece.PieceCategory.Misc;   // single 'Trail' tab (spade table)
            piece.m_groundPiece = true;                        // vanilla path_v2/replant_v2 = groundPiece
            piece.m_targetNonPlayerBuilt = true;               // vanilla op default
            piece.m_canBeRemoved = false;                      // a terrain op isn't a removable structure
            piece.m_vegetationGroundOnly = vegetationGroundOnly; // replant_v2=true (regrass needs ground), path_v2=false
            piece.m_resources   = Array.Empty<Piece.Requirement>(); // free placement like vanilla hoe ops

            // Reference-copy the icon + place-effect off the blueprint so the build menu
            // entry + placement sound match vanilla. Graft the _GhostOnly preview child so
            // the placement ghost shows the same marker. All ZNetView-free, all read-only
            // on the blueprint (we never instantiate the blueprint root).
            if (blueprint != null)
            {
                var bp = blueprint.GetComponent<Piece>();
                if (bp != null)
                {
                    if (bp.m_icon != null) piece.m_icon = bp.m_icon;
                    piece.m_placeEffect = bp.m_placeEffect;   // EffectList value-copy (reference, not inheritance)
                }
                GraftGhostOnly(blueprint, go);
            }
            else
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne] ConstructTerrainOpPiece('{name}'): no blueprint op resolved; " +
                    "piece will build correctly but with no icon / placement ghost / place-effect " +
                    "(degraded cosmetics, still functional).");
            }

            return go;
        }

        /// <summary>
        /// Reparent a fresh instance of <paramref name="blueprint"/>'s <c>_GhostOnly</c>
        /// child (the vanilla terrain-op's placement-preview marker — a ParticleSystem,
        /// sometimes a Quad mesh, both ZNetView-free) under <paramref name="dst"/>, named
        /// and positioned exactly as vanilla so <c>Player.SetupPlacementGhost</c>'s
        /// <c>transform.Find("_GhostOnly")</c> + <c>SetActive(true)</c> lights it up during
        /// aiming. Starts inactive (vanilla keeps it inactive until placement preview).
        ///
        /// Grafting just the ZNetView-free child subtree wakes no ZDO (the op blueprint's
        /// only networked-ness would be a ZNetView it does NOT have), so there is nothing to
        /// orphan — the same safe pattern <see cref="GraftTorchFire"/> uses for the cairn
        /// flame. Public UnityEngine API only — clean-room safe.
        /// </summary>
        private static void GraftGhostOnly(GameObject blueprint, GameObject dst)
        {
            var ghost = blueprint.transform.Find("_GhostOnly");
            if (ghost == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne] GraftGhostOnly: blueprint '{blueprint.name}' has no '_GhostOnly' child " +
                    "(vanilla op structure changed?); the spade op will have no placement-preview marker this build.");
                return;
            }
            var copy = UnityEngine.Object.Instantiate(ghost.gameObject, dst.transform);
            copy.name = "_GhostOnly";                       // vanilla looks up this exact name
            copy.transform.localPosition = ghost.localPosition;
            copy.transform.localRotation = ghost.localRotation;
            copy.transform.localScale    = ghost.localScale;
            copy.SetActive(false);                          // vanilla activates it during placement preview
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
        /// Measure the extent of every visual mesh under <paramref name="root"/> along
        /// ONE axis (0 = X, 1 = Y, 2 = Z), expressed in <paramref name="root"/>'s LOCAL
        /// space, returning the min and max coordinate on that axis via
        /// <paramref name="min"/> / <paramref name="max"/>. A generalization of
        /// <see cref="MeasureLocalFootY"/> / <see cref="MeasureLocalTopY"/> (which are the
        /// axis = 1 min / max special cases) to an arbitrary axis, so callers can measure
        /// a board's or post's THICKNESS along its facing normal, not just height.
        ///
        /// Walks each <see cref="MeshFilter"/>'s shared-mesh AABB and transforms its 8
        /// corners through the full TRS into root-local space — so a child mesh that
        /// reuses a unit-cube mesh scaled by its transform (e.g. the vanilla sign plank /
        /// <c>wood_pole2</c>, both a 1×1×1 <c>Cube_Cube_Material</c>) reports its REAL
        /// transformed size, where raw <c>sharedMesh.bounds.size</c> ≈ (1,1,1) would lie.
        /// Reads only the serialized <c>sharedMesh.bounds</c> + transform math, so it works
        /// on a clone still parented under an inactive holder (no Awake, no live
        /// <see cref="Renderer.bounds"/> required). Returns min = max = 0 when the root has
        /// no meshes. Public UnityEngine API only — clean-room safe.
        /// </summary>
        public static void MeasureLocalExtent(GameObject root, int axis, out float min, out float max)
        {
            // Measure root's own mesh subtree expressed in root's OWN local space (the
            // common case). Delegates to the reference-frame overload with frame = root.
            MeasureLocalExtent(root, root != null ? root.transform : null, axis, out min, out max);
        }

        /// <summary>
        /// Reference-frame variant of <see cref="MeasureLocalExtent(GameObject,int,out float,out float)"/>:
        /// measure the extent of every visual mesh under <paramref name="meshRoot"/> along
        /// <paramref name="axis"/>, but expressed in <paramref name="referenceFrame"/>'s
        /// LOCAL space rather than <paramref name="meshRoot"/>'s. This decouples WHICH meshes
        /// are measured (the <paramref name="meshRoot"/> subtree) from the FRAME the result
        /// is reported in.
        ///
        /// Needed when <paramref name="meshRoot"/> IS the scaled mesh holder: a child that
        /// reuses a 1×1×1 unit-cube mesh scaled by its own transform (the vanilla sign plank /
        /// <c>wood_pole2</c>) reports raw <c>sharedMesh.bounds.size</c> ≈ (1,1,1) when measured
        /// in its own frame (an identity round-trip), carrying no real dimensions. Passing a
        /// referenceFrame ABOVE the mesh's own transform (e.g. the sign root) bakes the mesh
        /// transform's scale into the corners, revealing the REAL transformed size — while
        /// still measuring ONLY the meshRoot subtree, so a sibling pole/board parented under
        /// that same root does not pollute the result. (This is the standoff feature's
        /// signRoot-space measurement generalized to a chosen mesh subtree.) Reads only
        /// serialized <c>sharedMesh.bounds</c> + transform math, so it works on a clone
        /// parented under an inactive holder. Public UnityEngine API only — clean-room safe.
        /// </summary>
        public static void MeasureLocalExtent(GameObject meshRoot, Transform? referenceFrame, int axis, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            if (meshRoot == null || referenceFrame == null) return;
            if (axis < 0 || axis > 2) return;

            float lo = float.MaxValue;
            float hi = float.MinValue;
            bool any = false;

            foreach (var mf in meshRoot.GetComponentsInChildren<MeshFilter>(true))
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
                    var local  = referenceFrame.InverseTransformPoint(t.TransformPoint(corner));
                    float v = local[axis];
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                    any = true;
                }
            }

            if (any)
            {
                min = lo;
                max = hi;
            }
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
        /// Graft a vanilla prefab's VISUAL mesh subtree onto <paramref name="dst"/> as a
        /// purely-cosmetic child, ADDITIVE / clean-room safe (ADR-0001 + ADR-0006). Reads
        /// the donor via <see cref="ZNetScene.GetPrefab"/> (fires no Awake), instantiates a
        /// COPY of the named visual child (e.g. the cartographytable's <c>new</c> LODGroup
        /// subtree), strips any gameplay/networking components off the copy
        /// (<see cref="StripToDecorative"/> removes ZNetView/Piece/WearNTear/Collider), and
        /// parents it under <paramref name="dst"/>. Instantiating a ZNetView-free subtree
        /// wakes no ZDO, so there is nothing to orphan — the same safe graft pattern
        /// <see cref="GraftTorchFire"/> / <see cref="GraftGhostOnly"/> use. We read the base
        /// game's own asset to build our piece's look; reading an asset is reference, not
        /// cloning — never the subtractive Instantiate-the-whole-networked-prefab anti-pattern.
        ///
        /// Returns the grafted visual GameObject (parented at the local position/rotation/
        /// scale the donor child had), or null if the donor or the named child is missing
        /// (caller decides how loud to be; the piece still works, just without that visual).
        /// </summary>
        public static GameObject? GraftVisualSubtree(string donorPrefabName, string visualChildName,
                                                     GameObject dst, string graftName)
        {
            if (dst == null) return null;
            var zns = ZNetScene.instance;
            if (zns == null)
            {
                Plugin.Log.LogWarning($"[Trailborne] GraftVisualSubtree('{donorPrefabName}'): no ZNetScene.");
                return null;
            }
            var donor = zns.GetPrefab(donorPrefabName);
            if (donor == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne] GraftVisualSubtree: donor prefab '{donorPrefabName}' not found; " +
                    $"'{dst.name}' will have no grafted visual.");
                return null;
            }
            var src = donor.transform.Find(visualChildName);
            if (src == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne] GraftVisualSubtree: donor '{donorPrefabName}' has no '{visualChildName}' child " +
                    $"(vanilla structure changed?); '{dst.name}' will have no grafted visual.");
                return null;
            }

            var copy = UnityEngine.Object.Instantiate(src.gameObject, dst.transform);
            copy.name = graftName;
            copy.transform.localPosition = src.localPosition;
            copy.transform.localRotation = src.localRotation;
            copy.transform.localScale    = src.localScale;
            // Belt-and-braces: the visual subtree should carry no networking/gameplay, but
            // a donor could nest one (a Switch, a collider). Strip anything that would make
            // the cosmetic copy interactive / networked / separately destructible.
            StripToDecorative(copy);
            copy.SetActive(true);
            return copy;
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

        /// <summary>
        /// Construct a networked ITEM-DROP SHELL from scratch — ADR-0006 additive
        /// construction, the item analogue of <see cref="ConstructPieceShell"/>. Returns a
        /// fresh GameObject parented under the inactive holder (so its Awake has NOT fired),
        /// carrying ONLY the skeleton a dropped/equippable vanilla item needs:
        /// <see cref="ZNetView"/> + <see cref="ZSyncTransform"/> + <see cref="Rigidbody"/> +
        /// a <see cref="BoxCollider"/> on the "item" physics layer + an <see cref="ItemDrop"/>
        /// whose <c>m_itemData.m_shared</c> is a FRESH <see cref="ItemDrop.ItemData.SharedData"/>
        /// (name/description/type/icon set by the caller). The caller adds the visual mesh
        /// child + any feature MonoBehaviour and registers the prefab in ZNetScene/ObjectDB.
        ///
        /// 🔴 Why this exists (ADR-0006): the pre-ADR item features (Pigments, cairn markers)
        /// clone a vanilla consumable (Coins) and overwrite its SharedData fields. That is the
        /// subtractive clone-then-strip pattern ADR-0006 retires — the donor drags whatever
        /// components/SharedData IronGate ships on Coins, and we inherit landmines we don't
        /// control. Here we AddComponent only what we intend, exactly like the Surveyor's
        /// Table piece. The one item-specific subtlety the decomp forces: <c>ItemDrop.Awake</c>
        /// only auto-populates <c>m_itemData.m_shared</c> from the prefab when
        /// <c>Application.isEditor</c> (assembly_valheim ItemDrop.Awake); at runtime the live
        /// instance shares the PREFAB's SharedData by reference. So an additive item MUST set
        /// <c>m_shared</c> on the prefab itself — a null SharedData would NRE the moment the
        /// item is inspected (tooltip/craft/equip). We new one here so the caller never trips
        /// that. SharedData's own field initializers give every EffectList / list a non-null
        /// default, so the equip path (which fires <c>m_equipEffect</c>) is NRE-safe too.
        ///
        /// REFERENCE (not clone): nothing is instantiated. The vanilla item layer index is
        /// read via <c>LayerMask.NameToLayer("item")</c> (the layer every vanilla ItemDrop
        /// lives on; confirmed in assembly_valheim — autopickup/interact masks key on "item").
        /// Networking fields match vanilla item norms (non-persistent ZDO is wrong for a
        /// dropped item — vanilla items ARE persistent so a dropped Kit survives relog; we set
        /// persistent + Default type + syncs).
        ///
        /// Returns null only if ZNetScene isn't up (logged) — the caller skips registration.
        /// </summary>
        public static GameObject? ConstructItemShell(string name)
        {
            var zns = ZNetScene.instance;
            if (zns == null)
            {
                Plugin.Log.LogError("[Trailborne] ConstructItemShell called with no ZNetScene.");
                return null;
            }

            // Parent under the inactive holder BEFORE adding components so no Awake fires
            // during construction (ItemDrop.Awake touches ObjectDB.GetItemPrefab + RPC
            // registration — must run only once the prefab is instantiated as a real drop).
            var holder = GetHolder();
            var go = new GameObject(name);
            go.transform.SetParent(holder.transform, worldPositionStays: false);

            // The vanilla "item" physics layer — autopickup, interact and item masks all key
            // on it (assembly_valheim: m_autoPickupMask/m_itemMask = LayerMask.GetMask("item")).
            // A dropped item on the Default layer would never be auto-picked-up. NameToLayer
            // returns -1 if the project has no such layer; guard so we don't set a bad layer.
            int itemLayer = LayerMask.NameToLayer("item");
            if (itemLayer >= 0) go.layer = itemLayer;

            // ZNetView — the networked identity. Vanilla items are PERSISTENT (a dropped item
            // survives a relog) with a Default object type; not distant.
            var nview = go.AddComponent<ZNetView>();
            nview.m_persistent = true;
            nview.m_type = ZDO.ObjectType.Default;
            nview.m_distant = false;

            // ZSyncTransform — vanilla items carry it (Wishbone/Coins blueprint: ZNetView +
            // ZSyncTransform + ItemDrop + Rigidbody). Position sync only; items don't sync scale.
            var zsync = go.AddComponent<ZSyncTransform>();
            zsync.m_syncPosition = true;
            zsync.m_syncRotation = true;
            zsync.m_syncScale = false;

            // Rigidbody — a dropped item is a physics body. Vanilla ItemDrop.Awake sets
            // maxDepenetrationVelocity = 1f on it; mirror that. Mass/drag left at Unity
            // defaults (cosmetic — the item only tumbles briefly on drop).
            var body = go.AddComponent<Rigidbody>();
            body.maxDepenetrationVelocity = 1f;

            // Collider — needed for the autopickup OverlapSphere + interact raycast. A small
            // unit box; the caller may resize to the visual footprint.
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(0.5f, 0.5f, 0.5f);

            // ItemDrop + a FRESH SharedData (the ADR-0006 + decomp-forced step explained above).
            var drop = go.AddComponent<ItemDrop>();
            drop.m_itemData = new ItemDrop.ItemData
            {
                m_stack = 1,
                m_quality = 1,
                m_shared = new ItemDrop.ItemData.SharedData(),
            };

            // Crash-safe by construction: a fresh SharedData defaults m_icons to the vanilla empty
            // array, and vanilla ItemDrop.GetIcon() indexes m_icons[m_variant] with NO bounds guard
            // (assembly_valheim ItemDrop.ItemData.GetIcon), so selecting an empty-icon item in the
            // crafting panel throws IndexOutOfRangeException and aborts the cost repaint. Pre-seed a
            // shared magenta fallback so EVERY additively-constructed item degrades to a visible
            // placeholder, never a crash. A real icon (if it loads) overwrites this later; if it
            // doesn't, SpecCheck's icon-load assertion (C1) screams at boot because m_icons[0] is
            // still this shared FallbackIcon instance.
            drop.m_itemData.m_shared.m_icons = new[] { FallbackIcon };

            return go;
        }
    }
}
