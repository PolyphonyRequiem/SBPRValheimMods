using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Trailhead
{
    /// <summary>
    /// Trailhead content: the Explorer's Bench (the crafting station that every
    /// SBPR recipe attaches to) and the Path Lamp. Lifted out of the old fat
    /// Registrar so the station — foundational content that all other features
    /// reference by name — lives in its own vertical slice.
    ///
    /// All gated behind ServerContext.OnSBServer (the caller, Registrar, checks it).
    /// </summary>
    public static class Trailhead
    {
        // Piece prefab names
        public const string ExplorersBenchName = "piece_sbpr_explorers_bench";
        public const string PathLampName       = "piece_sbpr_path_lamp";

        // Source clones
        private const string SourceWorkbench   = "piece_workbench";
        private const string SourceGroundTorch = "piece_groundtorch_wood";

        private const string IconFile          = "trailblazers_spade_v0.1.png";

        // Path Lamp visual height multiplier (Daniel 2026-06-05 playtest: "scale the
        // prefab 3x vertically and move the center point up to match"). The cloned
        // ground-torch is scaled 3× tall about its foot so the base stays planted and
        // the flame rides to the new top — see Assets.ScaleVisualHeightAboutFoot.
        private const float LampHeightScale = 3f;

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (called from ZNetScene.Awake postfix)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            RegisterExplorersBenchPrefab(zns);
            RegisterPathLampPrefab(zns);
        }

        private static void RegisterExplorersBenchPrefab(ZNetScene zns)
        {
            if (zns.GetPrefab(ExplorersBenchName) != null) return;
            var clone = Assets.ClonePrefab(SourceWorkbench, ExplorersBenchName);
            if (clone == null) return;

            // The vanilla Workbench prefab carries a GuidePoint component — the
            // proximity hook that makes Hugin/the raven pop the "you built a
            // workbench" tutorial. Our clone inherits it, so Hugin wrongly
            // greets the Explorer's Bench as if it were a Workbench. The bench is
            // its own station; strip the inherited tutorial hook so no raven fires
            // on first placement. (Path Lamp's source, piece_groundtorch_wood,
            // carries no GuidePoint, so only the bench needs this.)
            int removed = Assets.StripGuidePoints(clone);
            if (removed > 0)
                Plugin.Log.LogInfo($"[Trailborne] Stripped {removed} inherited GuidePoint(s) (Hugin tutorial) from {ExplorersBenchName}.");

            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = "Explorer's Bench";
                piece.m_description = "The explorer's writing desk. Crafts inks, signs, lamps, and trail tools.";
                piece.m_category    = Piece.PieceCategory.Crafting;
                piece.m_icon        = Assets.LoadPngAsSprite(IconFile);
                piece.m_resources   = new[]
                {
                    BuildReq("Wood", 10),
                    BuildReq("Stone", 4),
                    BuildReq("TrophyDeer", 1),
                };
            }
            // Give the clone its OWN CraftingStation identity (spec: requirements.md
            // ~line 119 + ADR-0003 — "its own CraftingStation, NOT the vanilla Workbench").
            // Two independent things are required for that, and the bench needs BOTH:
            //
            //   1. A distinct m_name. Recipes keyed to a station are matched by station
            //      name, so a name that differs from the Workbench's keeps Workbench-keyed
            //      recipes off the bench. (This was already in place — it was never the
            //      cause of the vanilla-craftables leak below.)
            //
            //   2. m_showBasicRecipies = false. The vanilla Workbench is the ONLY station
            //      that surfaces the stationless "basic" recipes you can otherwise craft by
            //      hand (Club, Torch, Stone Axe, Hammer, Hoe, rag armor, …). A raw clone
            //      inherits that flag = true, so the Explorer's Bench wrongly offered all of
            //      them (playtest, Daniel 2026-06-04, card t_30f97042). Every other vanilla
            //      station (forge, stonecutter, cauldron, …) ships this false; we match them
            //      so ONLY Trailborne recipes appear here.
            var station = clone.GetComponent<CraftingStation>();
            if (station != null)
            {
                station.m_name              = "Explorer's Bench";
                station.m_showBasicRecipies = false;
            }

            Assets.RegisterPrefabInZNetScene(clone);
            Plugin.Log.LogInfo($"[Trailborne] Registered piece: {ExplorersBenchName}");
        }

        private static void RegisterPathLampPrefab(ZNetScene zns)
        {
            if (zns.GetPrefab(PathLampName) != null) return;
            var clone = Assets.ClonePrefab(SourceGroundTorch, PathLampName);
            if (clone == null) return;

            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = "Path Lamp";
                piece.m_description = "A tall standing lamp for marking trails after dark.";
                // SPADE menu home (design pillar: Explorer-placed pieces live on the
                // Trailblazer's Spade, not the Hammer). The spade's PieceTable declares
                // only the Misc category ('Trail' tab), so the lamp MUST be Misc to
                // render there — Furniture would bucket into a tab the table doesn't have.
                piece.m_category    = Piece.PieceCategory.Misc;
                piece.m_icon        = Assets.LoadPngAsSprite(IconFile);
                piece.m_resources   = new[]
                {
                    BuildReq("Wood", 3),       // Meadows-tier
                    BuildReq("Resin", 2),
                };
                // NO station-proximity gate to PLACE the lamp (Daniel 2026-06-05: "for
                // the path light and sign, no bench requirement"). The vanilla
                // ground-torch clone inherits m_craftingStation = Workbench; clear it
                // so the lamp places anywhere. (Build COST unchanged — 3 Wood + 2 Resin.)
                piece.m_craftingStation = null;
            }

            // Scale the lamp 3× vertically with the base kept planted on the ground and
            // the flame/light riding up to the new top (Daniel 2026-06-05). Runs on
            // server + every client (ZNetScene.Awake postfix), so the taller shape is
            // baked into the registered prefab and syncs by construction.
            Assets.ScaleVisualHeightAboutFoot(clone, LampHeightScale);

            Assets.RegisterPrefabInZNetScene(clone);
            Plugin.Log.LogInfo($"[Trailborne] Registered piece: {PathLampName} (3× tall; spade menu)");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — rebuild bench/lamp resources; bench → Hammer menu,
        // lamp → SPADE menu (added in Trailblazing.DoObjectDBWiring).
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            // REBUILD resource lists now that ODB is populated (pieces built at
            // ZNetScene.Awake had unresolved m_resItem for any non-vanilla prefab).
            // The Explorer's Bench is a crafting STATION the player builds with the
            // Hammer (a settler's-tool action, NOT an Explorer-placed trail piece), so
            // it stays on the Hammer table. The Path Lamp is an Explorer-placed trail
            // piece and moves to the Spade menu per the design pillar.
            var hammerTable = Assets.GetHammerPieceTable();
            if (zns == null) return;

            var table = zns.GetPrefab(ExplorersBenchName);
            var lamp  = zns.GetPrefab(PathLampName);
            if (table != null)
            {
                var tablePiece = table.GetComponent<Piece>();
                if (tablePiece != null)
                    tablePiece.m_resources = new[]
                    {
                        BuildReq("Wood", 10),
                        BuildReq("Stone", 4),
                        BuildReq("TrophyDeer", 1),
                    };
                if (hammerTable != null) Assets.AddPieceToTable(table, hammerTable);
            }
            if (lamp != null)
            {
                var lampPiece = lamp.GetComponent<Piece>();
                if (lampPiece != null)
                {
                    lampPiece.m_resources = new[]
                    {
                        BuildReq("Wood", 3),
                        BuildReq("Resin", 2),
                    };
                    // Re-assert no station-proximity gate to place (Daniel 2026-06-05).
                    lampPiece.m_craftingStation = null;
                }
                // Path Lamp goes on the SPADE build menu now, not the Hammer. The
                // AddPieceToTable into the spade-only PieceTable happens in
                // Trailblazing.DoObjectDBWiring (Registrar runs Trailblazing AFTER
                // Trailhead, and the lamp prefab is already registered from the earlier
                // RegisterPrefabs pass, so the lookup there resolves).
            }
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount)
        {
            return Assets.BuildReq(resourcePrefabName, amount, "Core");
        }
    }
}
