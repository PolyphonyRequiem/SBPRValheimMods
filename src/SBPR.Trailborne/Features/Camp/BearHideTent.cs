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
//  SHELTER NOTE (design doc §2): the grafted canopy collider sits on the
//  "static_solid" layer (in the vanilla Cover ray-mask) and is NOT tagged "leaky",
//  so the tent reads as underRoof=true (keeps the player dry, keeps a camp fire lit
//  in rain) — but it is open-sided, so it does NOT reach the 0.8 cover threshold and
//  is therefore VISUAL-ONLY shelter, exactly as designed. The bedroll's gated
//  Bed.CheckExposure relax (a later Trailside Camp card) is what makes sleep legal.
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
            if (!TryAttachTentVisual(go))
                Plugin.Log.LogWarning(
                    $"[Trailborne/Camp] {TentName}: tent visual attach failed; the piece will " +
                    "register and build but show no canopy this load (check the AssetBundle + textures shipped).");

            // Size the root collider to the TraderTent footprint (vprefab: 8.0 × 4.9 × 6.9 m).
            // A box big enough to receive placement raycasts + hits; exact fit is polish.
            var box = go.GetComponent<BoxCollider>();
            if (box != null) { box.size = new Vector3(8.0f, 4.9f, 6.9f); box.center = new Vector3(0f, 2.45f, 0f); }

            Assets.RegisterPrefabInZNetScene(go);
            Plugin.Log.LogInfo($"[Trailborne/Camp] Registered Bear Hide Tent piece: {TentName} (additive, AssetBundle mesh).");
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

        private static bool TryAttachTentVisual(GameObject dst)
        {
            var mesh = LoadTentMesh();
            if (mesh == null) return false;

            // Cosmetic child: MeshFilter + MeshRenderer only — no ZNetView, no collider, no
            // script (ADR-0006 additive visual; the canopy is decoration, the shell owns the
            // networked collider).
            var visual = new GameObject("SBPR_BearHideTentVisual");
            visual.transform.SetParent(dst.transform, worldPositionStays: false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            var mf = visual.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = visual.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BuildHideMaterial();
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
