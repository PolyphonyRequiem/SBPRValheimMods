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
                piece.m_description = "A standing lamp for marking trails after dark.";
                piece.m_category    = Piece.PieceCategory.Furniture;
                piece.m_icon        = Assets.LoadPngAsSprite(IconFile);
                piece.m_resources   = new[]
                {
                    BuildReq("Wood", 3),       // Meadows-tier
                    BuildReq("Resin", 2),
                };
            }

            Assets.RegisterPrefabInZNetScene(clone);
            Plugin.Log.LogInfo($"[Trailborne] Registered piece: {PathLampName}");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — rebuild bench/lamp resources + hammer pieces
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            // Pieces into Hammer build menu + REBUILD their resource lists
            // now that ODB is populated. (Pieces built at ZNetScene.Awake
            // had unresolved m_resItem for any non-vanilla prefab.)
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
                    lampPiece.m_resources = new[]
                    {
                        BuildReq("Wood", 3),
                        BuildReq("Resin", 2),
                    };
                if (hammerTable != null) Assets.AddPieceToTable(lamp, hammerTable);
            }
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount)
        {
            return Assets.BuildReq(resourcePrefabName, amount, "Core");
        }
    }
}
