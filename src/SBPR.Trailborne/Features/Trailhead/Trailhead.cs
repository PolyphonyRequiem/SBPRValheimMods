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
    /// All gated behind SBPRContext.OnSBServer (the caller, Registrar, checks it).
    /// </summary>
    public static class TrailborneTrailhead
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
            var clone = TrailborneAssets.ClonePrefab(SourceWorkbench, ExplorersBenchName);
            if (clone == null) return;

            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = "Explorer's Bench";
                piece.m_description = "The explorer's writing desk. Crafts inks, signs, lamps, and trail tools.";
                piece.m_category    = Piece.PieceCategory.Crafting;
                piece.m_icon        = TrailborneAssets.LoadPngAsSprite(IconFile);
                piece.m_resources   = new[]
                {
                    BuildReq("Wood", 10),
                    BuildReq("Stone", 4),
                    BuildReq("TrophyDeer", 1),
                };
            }
            // Already a CraftingStation (workbench is one) — leave m_name unchanged so
            // existing recipes that name "piece_workbench" don't accidentally collide.
            var station = clone.GetComponent<CraftingStation>();
            if (station != null) station.m_name = "Explorer's Bench";

            TrailborneAssets.RegisterPrefabInZNetScene(clone);
            TrailbornePlugin.Log.LogInfo($"[Trailborne] Registered piece: {ExplorersBenchName}");
        }

        private static void RegisterPathLampPrefab(ZNetScene zns)
        {
            if (zns.GetPrefab(PathLampName) != null) return;
            var clone = TrailborneAssets.ClonePrefab(SourceGroundTorch, PathLampName);
            if (clone == null) return;

            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = "Path Lamp";
                piece.m_description = "A standing lamp for marking trails after dark.";
                piece.m_category    = Piece.PieceCategory.Furniture;
                piece.m_icon        = TrailborneAssets.LoadPngAsSprite(IconFile);
                piece.m_resources   = new[]
                {
                    BuildReq("Wood", 3),       // Meadows-tier
                    BuildReq("Resin", 2),
                };
            }

            TrailborneAssets.RegisterPrefabInZNetScene(clone);
            TrailbornePlugin.Log.LogInfo($"[Trailborne] Registered piece: {PathLampName}");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — rebuild bench/lamp resources + hammer pieces
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            // Pieces into Hammer build menu + REBUILD their resource lists
            // now that ODB is populated. (Pieces built at ZNetScene.Awake
            // had unresolved m_resItem for any non-vanilla prefab.)
            var hammerTable = TrailborneAssets.GetHammerPieceTable();
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
                if (hammerTable != null) TrailborneAssets.AddPieceToTable(table, hammerTable);
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
                if (hammerTable != null) TrailborneAssets.AddPieceToTable(lamp, hammerTable);
            }
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount)
        {
            return TrailborneAssets.BuildReq(resourcePrefabName, amount, "Core");
        }
    }
}
