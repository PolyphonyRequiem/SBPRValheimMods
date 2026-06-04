using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne
{
    /// <summary>
    /// M3 content: Trailblazer's Spade real path/replant behavior.
    /// Clones vanilla `path` and `cultivate` prefabs, scales their
    /// TerrainModifier radii to our 3 widths (narrow/standard/wide =
    /// 1.5m / 3m / 5m), registers them as Piece prefabs, and ADDS them
    /// to the spade's PieceTable (which is currently the vanilla Hoe
    /// table — so the player sees our 3 path-widths + 1 wide replant
    /// alongside the vanilla raise/level/paved_road. Spade-only piece
    /// table = M3.5/v0.2.0+ cleanup.
    ///
    /// ClearVegetation (Pickable.RemoveOne batch) is genuinely
    /// out-of-scope for v0.1.0 — vanilla cultivate already restores
    /// grass which is the "clear" act in a different direction.
    /// TWEAK ME: real ClearVegetation in v0.2.0.
    /// </summary>
    public static class TrailborneM3
    {
        public const string PathNarrowName   = "piece_sbpr_path_narrow";
        public const string PathStandardName = "piece_sbpr_path_standard";
        public const string PathWideName     = "piece_sbpr_path_wide";
        public const string ReplantWideName  = "piece_sbpr_replant_wide";

        private const string SourcePath      = "path";
        private const string SourceCultivate = "cultivate";

        private static readonly Dictionary<string, (string source, float radius)> _variants =
            new Dictionary<string, (string, float)>
            {
                { PathNarrowName,   (SourcePath,      1.5f) },
                { PathStandardName, (SourcePath,      3.0f) },
                { PathWideName,     (SourcePath,      5.0f) },
                { ReplantWideName,  (SourceCultivate, 5.0f) },
            };

        public static void RegisterPrefabs(ZNetScene zns)
        {
            foreach (var kv in _variants)
                RegisterRadiusVariant(zns, kv.Key, kv.Value.source, kv.Value.radius);
        }

        private static string NicePieceName(string prefab)
        {
            switch (prefab)
            {
                case PathNarrowName:   return "Spade: Path (1.5m)";
                case PathStandardName: return "Spade: Path (3m)";
                case PathWideName:     return "Spade: Path (5m)";
                case ReplantWideName:  return "Spade: Replant (5m)";
                default:               return prefab;
            }
        }

        private static void RegisterRadiusVariant(ZNetScene zns, string name, string source, float radius)
        {
            if (zns.GetPrefab(name) != null) return;
            var clone = TrailborneAssets.ClonePrefab(source, name);
            if (clone == null)
            {
                TrailbornePlugin.Log.LogWarning($"[Trailborne/M3] Source '{source}' missing; skipping {name}");
                return;
            }
            // Scale terrain radii on the cloned TerrainModifier
            var mod = clone.GetComponentInChildren<TerrainModifier>(includeInactive: true);
            if (mod != null)
            {
                mod.m_levelRadius  = radius;
                mod.m_smoothRadius = radius;
                mod.m_paintRadius  = radius;
            }
            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = NicePieceName(name);
                piece.m_description = $"Trailblazer path/ground op — {radius:F1}m radius.";
                piece.m_category    = Piece.PieceCategory.Misc;
                // Free placement like vanilla hoe ops — no resource cost
                piece.m_resources   = Array.Empty<Piece.Requirement>();
            }
            TrailborneAssets.RegisterPrefabInZNetScene(clone);
            TrailbornePlugin.Log.LogInfo($"[Trailborne/M3] Registered spade op: {name} ({radius:F1}m)");
        }

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            // Add our spade ops to the Trailblazer Spade's PieceTable.
            // The spade currently inherits the Hoe's table (M0 simplification);
            // adding our pieces to that table makes them appear when the spade
            // is equipped AND the hoe is equipped. TWEAK ME for v0.2.0.
            var spadePrefab = zns?.GetPrefab(TrailborneRegistrar.PublicSpadeName);
            var drop = spadePrefab?.GetComponent<ItemDrop>();
            var table = drop?.m_itemData?.m_shared?.m_buildPieces;
            if (table == null)
            {
                TrailbornePlugin.Log.LogWarning("[Trailborne/M3] Spade PieceTable missing; cannot wire path ops.");
                return;
            }
            foreach (var n in _variants.Keys)
            {
                var p = zns?.GetPrefab(n);
                if (p != null) TrailborneAssets.AddPieceToTable(p, table);
            }
            TrailbornePlugin.Log.LogInfo("[Trailborne/M3] M3 wiring complete (spade path ops attached to spade table).");
        }
    }
}
