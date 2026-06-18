// ============================================================================
//  Trailborne v2 cartography — Surveyor's Table (placed station)
// ----------------------------------------------------------------------------
//  The FOUNDATION card of the v2 cartography tier (impl spec §1, card t_2715661d):
//  a Black-Forest-tier placed piece that retains a SHARED, cumulative, windowed
//  1000 m survey in its own ZDO. Lowest-risk of the three v2 features — a re-gated
//  vanilla MapTable on public APIs. The Cartographer's Kit (t_c871efec) and the
//  Local Map viewer (t_7b616020) build ON this.
//
//  Construction is ADDITIVE (ADR-0006): Assets.TryConstructPieceShell builds the
//  networked skeleton (ZNetView + Piece + WearNTear + collider) from scratch, then
//  we graft the vanilla cartographytable's VISUAL mesh as a ZNetView-free cosmetic
//  child (Assets.TryGraftVisualSubtree) and attach our SurveyorTableTag. We NEVER
//  Instantiate piece_cartographytable (it carries a ZNetView + the vanilla MapTable
//  MonoBehaviour — cloning it is the ADR-0006 anti-pattern). We read it only as a
//  blueprint (vprefab inspect piece_cartographytable: visual under child "new",
//  material Cartographer_mat).
//
//  Placement: the Trailblazer's Spade build menu (Pillar 1 — never the Hammer);
//  added to the spade PieceTable in Trailblazing.DoObjectDBWiring like every other
//  Spade-placed SBPR piece. Recipe LOCKED (impl spec §0/§1.2): FineWood ×10, Bronze
//  ×2, DeerHide ×4, BoneFragments ×8; m_craftingStation = null (NO bench-in-range
//  to place — matches Signs/Path Lamp; the architect REVERSED the proposed lean).
//
//  All gated behind ServerContext.OnSBServer (via Registrar).
// ============================================================================

using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Cartography
{
    public static class SurveyorsTable
    {
        // LOCKED prefab name — a save/wire contract the moment a Table is placed; never
        // rename (renaming orphans every placed instance). Matches impl spec §0/§5.
        public const string TableName = "piece_sbpr_surveyors_table";

        // Vanilla cartography table, read ONLY as a visual blueprint (never instantiated).
        // Confirmed via `vprefab inspect piece_cartographytable`: Piece/ZNetView/WearNTear/
        // MapTable on the root, the build visual under child "new" (LODGroup, Cartographer_mat).
        private const string BlueprintTable = "piece_cartographytable";
        private const string BlueprintVisualChild = "new";

        // Clean stone donor for TryConstructPieceShell's effect-table reference-copy (the same
        // donor the cairns use). Wood-material would be thematically closer, but the shell
        // helper sets WearNTear.MaterialType.Stone; the Table is a sturdy field station and
        // the effect set is hit/destroy/place SFX only. (Visual polish: revisit material in
        // playtest if the destroy effect reads wrong — flagged for Daniel.)
        private const string ShellEffectDonor = "stone_floor";

        // Build cost — LOCKED (impl spec §0 row 1 / requirements §1). Black-Forest tier.
        public const int FineWoodCost      = 10;
        public const int BronzeCost        = 2;
        public const int DeerHideCost      = 4;
        public const int BoneFragmentsCost = 8;

        // Black-Forest-tier HP for the placed station. Vanilla cartography table is a
        // wood-tier piece; 800 puts the Surveyor's Table in the sturdy-furniture band so
        // it survives field weather between visits. (Tunable v0.2+ polish — flagged.)
        private const float TableHealth = 800f;

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (ZNetScene.Awake postfix, via Registrar)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            if (zns.GetPrefab(TableName) != null) return;

            // ADDITIVE: build the networked skeleton from scratch (no clone of the
            // ZNetView-bearing cartographytable). Reference-copies hit/destroy/place
            // effects off a clean stone donor.
            if (!Assets.TryConstructPieceShell(TableName, ShellEffectDonor, out var go))
            {
                Plugin.Log.LogWarning($"[Trailborne/Cartography] Could not construct piece shell for {TableName}; skipping.");
                return;
            }

            var wnt = go.GetComponent<WearNTear>();
            if (wnt != null) wnt.m_health = TableHealth;

            var piece = go.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = "Surveyor's Table";
                piece.m_description =
                    "A field survey post. Records a shared, cumulative map of its own 1000 m " +
                    "neighbourhood — anyone who surveys here adds what they've walked. Use it to " +
                    "review and tidy the shared record. Ward-protected like a cartography table.";
                // MUST be Misc: the spade's from-scratch PieceTable declares only the single
                // Misc-backed "Trail" tab (Trailblazing). A piece whose category isn't declared
                // there is added to m_pieces but its tab never renders → invisible in the menu
                // (the v0.2.2 cairn-vanish bug). EnsureCategory in Trailblazing also guards this.
                piece.m_category    = Piece.PieceCategory.Misc;
                // NO station-proximity gate to PLACE (LOCKED — impl spec §1.2 / requirements §1;
                // the architect reversed the proposed lean). Every Spade-placed SBPR piece sets
                // this null (Signs.cs:270, Trailhead.cs:186).
                piece.m_craftingStation = null;
                // Build cost — SBPR-item-free (all vanilla resources), so it resolves at the
                // ZNetScene phase too; rebuilt again in DoObjectDBWiring for the explicit
                // final state (mirrors Signs/Cairns). warn:true — a genuinely-missing vanilla
                // resource SHOULD scream.
                piece.m_resources   = BuildResources();
                piece.m_comfort      = 0;
                piece.m_comfortGroup = Piece.ComfortGroup.None;
                // Keep the vanilla cartographytable build icon if we can read it (blueprint
                // read, not clone). Falls back to no icon (the piece still builds).
                var bpPiece = zns.GetPrefab(BlueprintTable)?.GetComponent<Piece>();
                if (bpPiece != null && bpPiece.m_icon != null) piece.m_icon = bpPiece.m_icon;
            }

            // Graft the vanilla cartographytable's VISUAL mesh as a ZNetView-free cosmetic
            // child (additive; reads the donor, never instantiates its networked root).
            if (!Assets.TryGraftVisualSubtree(BlueprintTable, BlueprintVisualChild, go, "SBPR_SurveyorTableVisual", out _))
                Plugin.Log.LogWarning(
                    $"[Trailborne/Cartography] {TableName}: visual graft from '{BlueprintTable}/{BlueprintVisualChild}' " +
                    "failed; the Table will register and function but show no mesh this build (logs-green≠playable — " +
                    "Daniel verifies the look in-game).");

            // Size the root collider roughly to the cartographytable footprint (vprefab:
            // visual ~4.2×1.6×2.5 m). A box big enough to receive placement raycasts + hits;
            // exact fit is visual polish (flagged).
            var box = go.GetComponent<BoxCollider>();
            if (box != null) { box.size = new Vector3(2.0f, 1.2f, 1.4f); box.center = new Vector3(0f, 0.6f, 0f); }

            // The survey behaviour (ZDO-persisted shared survey, contribute-on-use, ward
            // gate, pin-removal backend, viewer-open). Implements Hoverable + Interactable
            // directly (the proven CairnInteractable pattern), so no separate Switch/child-
            // collider wiring is needed — see SurveyorTableTag. (Impl spec §1.1 sketches a
            // Switch; we use the repo's in-production interactable pattern instead — flagged
            // for review.)
            go.AddComponent<SurveyorTableTag>();

            Assets.RegisterPrefabInZNetScene(go);
            Plugin.Log.LogInfo($"[Trailborne/Cartography] Registered Surveyor's Table piece: {TableName} (additive, ZDO-persisted 1000m survey).");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — rebuild resources; Spade-menu add happens in Trailblazing
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            var p = zns?.GetPrefab(TableName);
            if (p == null) return;

            var piece = p.GetComponent<Piece>();
            if (piece != null)
            {
                // Re-assert the final placed-piece state (resources all vanilla, so they
                // resolved at the ZNetScene phase too; rebuilt here for the explicit final
                // state alongside the station-null, mirroring Signs.DoObjectDBWiring).
                piece.m_resources = BuildResources();
                piece.m_craftingStation = null;
            }
            // The Surveyor's Table is added to the SPADE PieceTable in
            // Trailblazing.DoObjectDBWiring (Registrar runs Trailblazing AFTER Cartography,
            // and the Table prefab is already registered from RegisterPrefabs, so the
            // lookup there resolves). NOT the Hammer (design Pillar 1).

            Plugin.Log.LogInfo("[Trailborne/Cartography] Surveyor's Table ObjectDB wiring complete (placed via Spade menu; no bench-in-range).");
        }

        /// <summary>
        /// LOCKED build cost (impl spec §0 row 1 / requirements §1): FineWood ×10,
        /// Bronze ×2, DeerHide ×4, BoneFragments ×8. All vanilla resource prefab names
        /// (verified present in the game assembly), so BuildReq resolves them at any phase.
        /// </summary>
        private static Piece.Requirement[] BuildResources()
        {
            return new[]
            {
                Assets.BuildReq("FineWood",      FineWoodCost,      "Cartography"),
                Assets.BuildReq("Bronze",        BronzeCost,        "Cartography"),
                Assets.BuildReq("DeerHide",      DeerHideCost,      "Cartography"),
                Assets.BuildReq("BoneFragments", BoneFragmentsCost, "Cartography"),
            };
        }
    }
}
