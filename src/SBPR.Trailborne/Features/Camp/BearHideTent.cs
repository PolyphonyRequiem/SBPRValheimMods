// ============================================================================
//  Trailborne — Bear Hide Tent (placeholder art via custom AssetBundle)
// ----------------------------------------------------------------------------
//  The first piece of the Trailside Camp triad (design: docs/design/trailside-camp.md).
//  A Black-Forest-tier placed tent — the "Bear Hide Tent" — whose PLACEHOLDER art is
//  the vanilla TraderTent (Haldor's market tent) mesh: a stitched-hide canopy, the
//  closest vanilla look to bear hide (Daniel 2026-06-24: "Make it trader tent keep
//  the legs, and keep the size. It's fine. ... It's placeholder art anyhow.").
//
//  WHY A CUSTOM ASSETBUNDLE (the one thing that makes this piece different)
//  -----------------------------------------------------------------------
//  Every other SBPR kitbash grafts its visual from a ZNetScene-registered vanilla
//  prefab by name (Surveyor's Table <- piece_cartographytable, etc.). TraderTent
//  CANNOT be reached that way: it is location decoration in a lazy SoftReference
//  bundle and is NOT in either ZNetScene serialized prefab list, so
//  ZNetScene.GetPrefab("TraderTent") returns null (verified against Jotunn's
//  prefab-list.md — every buildable donor present, TraderTent absent; and against
//  the decomp — GetPrefab is a plain dict lookup over m_prefabs/m_nonNetViewPrefabs
//  with no SoftRef fallback). So we ship the mesh ourselves in SBPR's FIRST custom
//  AssetBundle (assets/bundles/sbpr_tradertent.unity3d, built by
//  scripts/build_bear_hide_tent_bundle.py — a repack of the game's OWN Unity-6
//  bundle with the mesh renamed, so the Unity-version metadata matches by
//  construction and the bundle loads in-game; round-trip verified 2026-06-25).
//
//  MATERIAL IS BUILT AT RUNTIME (not baked in the bundle)
//  ------------------------------------------------------
//  The dedicated-server payload strips material shaders (TraderTent_cloth shader =
//  null PPtr), so the bundle ships the MESH ONLY. We build the material at runtime
//  the proven SBPR way (Assets.TryReadLeatherMaterial + new Material(leather) +
//  swap _MainTex) so Valheim's real lit shader + a hide normal grain apply, then
//  drop the extracted TraderTent diffuse on top. A bundle-baked material would
//  render magenta. Leather is the thematically-correct shader donor for a HIDE tent.
//
//  CONSTRUCTION IS ADDITIVE (ADR-0006)
//  -----------------------------------
//  Assets.TryConstructPieceShell builds the ZNetView+Piece+WearNTear+collider
//  skeleton from scratch; we attach the bundle mesh as a ZNetView-free cosmetic
//  child. We never Instantiate a networked prefab and strip it.
//
//  SHELTER NOTE (design doc §2): the tent is a genuine WALK-UNDER shelter. Its
//  collision volume is the donor's OPEN-SIDED MeshCollider (the same tent mesh reused
//  as a static concave MeshCollider, exactly like the vanilla TraderTent donor), seated
//  to coincide with the rendered canopy so the collider IS the canopy shape — solid
//  where the cloth/legs are, open underneath and on the sides. It sits on the
//  "static_solid" layer (in the vanilla Cover ray-mask) and is NOT tagged "leaky", so
//  the tent reads as underRoof=true (keeps the player dry, keeps a camp fire lit in
//  rain) — but because it is open-sided it does NOT reach the 0.8 cover threshold and is
//  therefore VISUAL-ONLY shelter, exactly as designed. The old solid root BoxCollider
//  (a wall from the ground to 4.9 m) is DEMOTED to a thin ground pad — a hit/seat target,
//  not an interior wall (card t_c96a2ea2, bear-hide-tent-collider-fit-impl-spec.md). The
//  bedroll's gated Bed.CheckExposure relax (a later Trailside Camp card) is what makes
//  sleep legal.
//
//  All gated behind ServerContext.OnSBServer (via Registrar).
// ============================================================================

using System.IO;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Camp
{
    public static class BearHideTent
    {
        // LOCKED prefab name — a save/wire contract the moment a tent is placed; never
        // rename (renaming orphans every placed instance).
        public const string TentName = "piece_sbpr_bearhide_tent";

        // The shipped AssetBundle (assets/bundles/ -> plugin folder via pack-modpack.sh)
        // and the asset name the build script stamped onto the mesh.
        private const string BundleFile = "sbpr_tradertent.unity3d";
        private const string MeshAssetName = "SBPR_TraderTentMesh";

        // Extracted vanilla TraderTent textures (assets/textures/ -> plugin folder).
        private const string DiffuseFile = "sbpr_tradertent_d.png";

        // Clean stone donor for the shell's effect-table reference-copy (same as the
        // Surveyor's Table — hit/destroy/place SFX only; a tent has no special effects).
        private const string ShellEffectDonor = "stone_floor";

        // Build cost — Black-Forest tier (design §1.3: bear Bjorn is a Black Forest
        // creature, so bear hide is a BF material → BF-tier piece, no biome conflict).
        // PROVISIONAL pending the design doc's recipe lock; mirrors the BF furniture band.
        public const int BearHideCost = 4;   // BjornHide — the namesake
        public const int FineWoodCost = 6;   // frame
        public const int LeatherScrapsCost = 4;   // lashings/ties

        // Black-Forest-tier HP — sturdy field furniture that survives weather between
        // visits (same band as the Surveyor's Table). Tunable playtest polish.
        private const float TentHealth = 600f;

        // Lazy one-time bundle load + mesh cache (the bundle stays resident; loading is
        // idempotent and cheap to keep — one tent mesh).
        private static AssetBundle? _bundle;
        private static Mesh? _tentMesh;

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (ZNetScene.Awake postfix, via Registrar)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            if (zns.GetPrefab(TentName) != null) return;

            if (!Assets.TryConstructPieceShell(TentName, ShellEffectDonor, out var go))
            {
                Plugin.Log.LogWarning($"[Trailborne/Camp] Could not construct piece shell for {TentName}; skipping.");
                return;
            }

            var wnt = go.GetComponent<WearNTear>();
            if (wnt != null) wnt.m_health = TentHealth;

            var piece = go.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name = "Bear Hide Tent";
                piece.m_description =
                    "A trailside tent of stretched hide on a wooden frame. Marks a camp where " +
                    "a traveller can shelter from the rain. (Placeholder art — the trader's tent " +
                    "stands in for the bear-hide tent for now.)";
                // MUST be Misc: the spade's from-scratch PieceTable declares only the single
                // Misc-backed 'Trail' tab; a piece whose category isn't declared there renders
                // in no tab → invisible in the menu (the v0.2.2 cairn-vanish bug).
                piece.m_category = Piece.PieceCategory.Misc;
                // NO station-proximity gate to place (Pillar 1: every Spade-placed SBPR piece
                // sets this null — field-deployable furniture).
                piece.m_craftingStation = null;
                piece.m_resources = BuildResources();
                // Comfort is owned by the Trailside Camp sleep mechanic (vanilla SE_Rested on
                // wake), NOT a per-piece comfort aura here — keep the tent comfort-neutral.
                piece.m_comfort = 0;
                piece.m_comfortGroup = Piece.ComfortGroup.None;
            }

            // Attach the bundle mesh as a ZNetView-free cosmetic child with a runtime-built
            // hide material. Failure is non-fatal: the piece still registers + builds (logs-
            // green≠playable — Daniel verifies the look in-game), it just shows no canopy.
            if (!TryAttachTentVisual(go, out var visual) || visual == null)
                Plugin.Log.LogWarning(
                    $"[Trailborne/Camp] {TentName}: tent visual attach failed; the piece will " +
                    "register and build but show no canopy this load (check the AssetBundle + textures shipped).");

            // COLLIDER FIT (spec: bear-hide-tent-collider-fit-impl-spec.md, card t_c96a2ea2).
            // The as-built used a SOLID 8×4.9×6.9 root BoxCollider — a wall of collision from
            // the ground up filling the whole footprint — AND left the canopy visual attached
            // at Vector3.zero with zero measurement, so the mesh (AABB centre ≈ (4.49,2.38,1.46),
            // foot ≈ -0.055) rendered ~4.7 m off the box. Daniel 2026-06-26: "the collision mesh
            // has no relationship to the tent mesh / I am not finding a spot where I can get
            // shelter." Fix has two halves: (1) graft the donor's OPEN-SIDED MeshCollider (the
            // SAME tent mesh, concave/static) so the collision volume IS the canopy shape — a
            // real walk-under shelter, exactly like the vanilla TraderTent donor which ships
            // MeshCollider{convex:false}; (2) SEAT the mesh+collider (same delta) so the measured
            // foot lands at root y=0 and the canopy centres on X/Z; then demote the shell box to
            // a thin ground pad (footprint × 0.2 m) so a guaranteed non-trigger hit/seat target
            // remains without walling the interior (same philosophy as the Ancient Portal walk-up
            // fix, ancient-portal-impl-spec.md §3.2b).
            if (visual != null)
                SeatTentGeometryAndCollider(go, visual);

            Assets.RegisterPrefabInZNetScene(go);
            Plugin.Log.LogInfo($"[Trailborne/Camp] Registered Bear Hide Tent piece: {TentName} (additive, AssetBundle mesh).");
        }

        // ───────────────────────────────────────────────
        // COLLIDER FIT — graft the open canopy MeshCollider + seat + ground pad
        // ───────────────────────────────────────────────

        /// <summary>
        /// Make the placed tent a genuine walk-under shelter whose collision volume coincides
        /// with the rendered canopy. Grafts the donor's open-sided <see cref="MeshCollider"/>
        /// (the SAME loaded tent mesh, concave/static — legal because a placed piece is static,
        /// and the vanilla TraderTent ships exactly this collider), seats the visual + collider
        /// (identical delta) so the measured mesh foot lands at root y=0 and the canopy centres
        /// on X/Z, and demotes the shell's solid <see cref="BoxCollider"/> to a thin ground pad
        /// under the seated footprint. Measured, not guessed — mirrors the Signs/MarkerSigns
        /// <see cref="Assets.MeasureLocalExtent(GameObject,Transform,int,out float,out float)"/>
        /// seat machinery. Clean-room safe (public UnityEngine API + base-game donor only).
        /// </summary>
        private static void SeatTentGeometryAndCollider(GameObject go, GameObject visual)
        {
            var rootT = go.transform;

            // (1) Graft the open canopy MeshCollider as a child of the PIECE ROOT (not the
            //     visual, not the shell box). Reuse the visual's already-loaded mesh so there
            //     is no second asset load. Concave + non-trigger: a real shelter surface the
            //     Cover spherecast can hit; static because a placed build piece is static.
            var mf = visual.GetComponent<MeshFilter>();
            var tentMesh = mf != null ? mf.sharedMesh : null;
            GameObject? colObj = null;
            if (tentMesh != null)
            {
                colObj = new GameObject("SBPR_BearHideTentCollider");
                colObj.transform.SetParent(rootT, worldPositionStays: false);
                var mc = colObj.AddComponent<MeshCollider>();
                mc.sharedMesh = tentMesh;
                mc.convex     = false;   // open-sided; static piece → concave is legal (donor proves it)
                mc.isTrigger  = false;   // a real shelter surface (Cover spherecast needs a solid hit)
                // Layer static_solid: in the vanilla Cover ray-mask, non-leaky → keeps
                // underRoof=true (design §2). Matches the donor TraderTent's collider layer.
                int staticSolid = LayerMask.NameToLayer("static_solid");
                if (staticSolid >= 0) colObj.layer = staticSolid;
            }
            else
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/Camp] {TentName}: no mesh on visual; shelter MeshCollider not grafted " +
                    "(the piece will still build off the ground pad, but has no walk-under canopy collider).");
            }

            // (2) Measure the canopy mesh AABB in ROOT space (measure the child mesh in the ROOT
            //     frame, not its own — a self-frame measure round-trips, the MarkerSigns caveat).
            Assets.MeasureLocalExtent(visual, rootT, 0, out float minX, out float maxX);
            Assets.MeasureLocalExtent(visual, rootT, 1, out float minY, out float maxY);
            Assets.MeasureLocalExtent(visual, rootT, 2, out float minZ, out float maxZ);
            float centreX = 0.5f * (minX + maxX);   // ≈ 4.49 m for the shipped mesh
            float centreZ = 0.5f * (minZ + maxZ);   // ≈ 1.46 m
            float footY   = minY;                    // ≈ -0.055 m

            // Re-seat the MESH (visual + collider, SAME delta) so the foot lands at root y=0 and
            // the canopy centres over the placement origin on X/Z.
            Vector3 seat = new Vector3(-centreX, -footY, -centreZ);
            visual.transform.localPosition += seat;
            if (colObj != null)
            {
                colObj.transform.localPosition = visual.transform.localPosition; // collider tracks visual
                colObj.transform.localRotation = visual.transform.localRotation;
                colObj.transform.localScale    = visual.transform.localScale;
            }

            // (3) Demote the shell's solid root BoxCollider to a thin GROUND PAD: a base-mass
            //     hit/deconstruct + placement-seat target that does NOT wall the interior, so the
            //     player can walk under the canopy. Centre it under the SEATED footprint (post-seat
            //     that is ≈ (0, 0.1, 0)). DESK-ESTIMATED pad height 0.2 m — flagged AT-WALK-UNDER,
            //     tune in-game if the player snags.
            var box = go.GetComponent<BoxCollider>();
            if (box != null)
            {
                box.size      = new Vector3(maxX - minX, 0.2f, maxZ - minZ);           // footprint × thin
                box.center    = new Vector3(centreX + seat.x, 0.1f, centreZ + seat.z); // = (0, 0.1, 0) post-seat
                box.isTrigger = false;
            }

            Plugin.Log.LogInfo(
                $"[Trailborne/Camp] {TentName}: seated canopy (foot→y0, centre X/Z), grafted open " +
                $"MeshCollider={(colObj != null)}, demoted shell box to ground pad " +
                $"(footprint {maxX - minX:0.00}×{maxZ - minZ:0.00} m).");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — rebuild resources; Spade-menu add happens in Trailblazing
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            var p = zns?.GetPrefab(TentName);
            if (p == null) return;

            var piece = p.GetComponent<Piece>();
            if (piece != null)
            {
                // Re-assert final placed-piece state now that ObjectDB exists (BjornHide etc.
                // are vanilla items, so they resolve at this phase). Mirrors Surveyor's Table.
                piece.m_resources = BuildResources();
                piece.m_craftingStation = null;
            }
            // The tent is added to the SPADE PieceTable in Trailblazing.DoObjectDBWiring
            // (Registrar runs Trailblazing after Camp; the prefab is already registered, so
            // the by-name lookup there resolves). NEVER the Hammer (design Pillar 1).

            Plugin.Log.LogInfo("[Trailborne/Camp] Bear Hide Tent ObjectDB wiring complete (placed via Spade menu; no bench-in-range).");
        }

        // ───────────────────────────────────────────────
        // Visual: load the bundle mesh + build a runtime hide material
        // ───────────────────────────────────────────────

        private static bool TryAttachTentVisual(GameObject dst, out GameObject? visual)
        {
            visual = null;
            var mesh = LoadTentMesh();
            if (mesh == null) return false;

            // Cosmetic child: MeshFilter + MeshRenderer only — no ZNetView, no collider, no
            // script (ADR-0006 additive visual; the canopy is decoration, the shelter collider
            // is grafted separately as SBPR_BearHideTentCollider, seated to the SAME TRS).
            var v = new GameObject("SBPR_BearHideTentVisual");
            v.transform.SetParent(dst.transform, worldPositionStays: false);
            v.transform.localPosition = Vector3.zero;
            v.transform.localRotation = Quaternion.identity;
            v.transform.localScale = Vector3.one;

            var mf = v.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = v.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BuildHideMaterial();
            visual = v;
            return true;
        }

        private static Mesh? LoadTentMesh()
        {
            if (_tentMesh != null) return _tentMesh;
            try
            {
                if (_bundle == null)
                {
                    var path = Path.Combine(Plugin.PluginFolder, BundleFile);
                    if (!File.Exists(path))
                    {
                        Plugin.Log.LogWarning($"[Trailborne/Camp] AssetBundle missing on disk: {path}");
                        return null;
                    }
                    _bundle = AssetBundle.LoadFromFile(path);
                    if (_bundle == null)
                    {
                        Plugin.Log.LogWarning($"[Trailborne/Camp] AssetBundle.LoadFromFile returned null for {path} " +
                            "(Unity-version mismatch? bundle should be 6000.0.61f1).");
                        return null;
                    }
                }
                // Load by name. The repacked bundle carries incidental Vendor assets too, so
                // we filter LoadAllAssets<Mesh> by our stamped name rather than trusting the
                // container manifest (the build script renamed the mesh, not its container path).
                foreach (var m in _bundle.LoadAllAssets<Mesh>())
                {
                    if (m != null && m.name == MeshAssetName) { _tentMesh = m; break; }
                }
                if (_tentMesh == null)
                    Plugin.Log.LogWarning($"[Trailborne/Camp] mesh '{MeshAssetName}' not found in {BundleFile}.");
                return _tentMesh;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"[Trailborne/Camp] LoadTentMesh failed: {e}");
                return null;
            }
        }

        /// <summary>
        /// Build the hide canopy material at runtime: instance the vanilla leather material
        /// (Valheim's real lit shader + hide normal grain) and swap its albedo to the extracted
        /// TraderTent diffuse. Instancing (new Material) — never mutate the shared leather
        /// material, which would repaint every leather item in the world. Degrades gracefully:
        /// no leather donor → default material; no diffuse PNG → plain instanced leather.
        /// </summary>
        private static Material BuildHideMaterial()
        {
            Material mat;
            if (Assets.TryReadLeatherMaterial(out var leather) && leather != null)
                mat = new Material(leather) { name = "SBPR_BearHideTentMat" };
            else
            {
                Plugin.Log.LogWarning("[Trailborne/Camp] leather donor material not found; tent uses a default material.");
                mat = new Material(Shader.Find("Standard")) { name = "SBPR_BearHideTentMat_fallback" };
            }

            var diffuse = Assets.LoadPngAsTexture(DiffuseFile, point: false);
            if (diffuse != null)
            {
                mat.mainTexture = diffuse;
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", diffuse);
                // Force base tint white so the hide albedo shows at full value instead of being
                // multiply-darkened by the leather material's own tint (the "muddy multiply" trap).
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            }
            return mat;
        }

        private static Piece.Requirement[] BuildResources()
        {
            return new[]
            {
                Assets.BuildReq("BjornHide",     BearHideCost,     "Camp"),
                Assets.BuildReq("FineWood",      FineWoodCost,     "Camp"),
                Assets.BuildReq("LeatherScraps", LeatherScrapsCost, "Camp"),
            };
        }
    }
}
