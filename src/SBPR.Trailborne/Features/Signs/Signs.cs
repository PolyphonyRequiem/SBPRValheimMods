using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Runtime;
using SBPR.Trailborne.Features.Pigments;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Painted Signs (4 buildable variants, color tinted from base material).
    /// Player presses Shift+E on a placed sign to add a colored map pin matching
    /// the sign's text + color. Split out of the old M1; depends DOWN on
    /// Pigments for its ink ingredient.
    ///
    /// All gated behind SBPRContext.OnSBServer.
    /// </summary>
    public static class TrailborneSigns
    {
        // Sign piece prefab names
        public const string SignRedName   = "piece_sbpr_sign_red";
        public const string SignWhiteName = "piece_sbpr_sign_white";
        public const string SignBlueName  = "piece_sbpr_sign_blue";
        public const string SignBlackName = "piece_sbpr_sign_black";

        // Source clones
        private const string SourceSign     = "sign";

        // Icon file mapping
        private static readonly Dictionary<string, string> _icons = new Dictionary<string, string>
        {
            { SignRedName,   "ink_red_v0.1.png"   },
            { SignWhiteName, "ink_white_v0.1.png" },
            { SignBlueName,  "ink_blue_v0.1.png"  },
            { SignBlackName, "ink_black_v0.1.png" },
        };

        // Color per sign (used to tint the sign's mesh + as the pin color)
        public static readonly Dictionary<string, Color> SignColors = new Dictionary<string, Color>
        {
            { SignRedName,   new Color(0.85f, 0.18f, 0.18f, 1f) },
            { SignWhiteName, new Color(0.95f, 0.94f, 0.88f, 1f) },
            { SignBlueName,  new Color(0.20f, 0.40f, 0.85f, 1f) },
            { SignBlackName, new Color(0.10f, 0.10f, 0.12f, 1f) },
        };

        // Pin type per sign — vanilla Minimap pin sprite reuse for color clarity
        public static readonly Dictionary<string, Minimap.PinType> SignPinTypes = new Dictionary<string, Minimap.PinType>
        {
            { SignRedName,   Minimap.PinType.Icon3 }, // red-ish vanilla pin
            { SignWhiteName, Minimap.PinType.Icon0 }, // generic / white
            { SignBlueName,  Minimap.PinType.Icon2 }, // blue-ish
            { SignBlackName, Minimap.PinType.Icon4 }, // dark / generic
        };

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (called from ZNetScene.Awake postfix)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            // Sign pieces — clone vanilla sign + tint
            RegisterSignPrefab(zns, SignRedName,   "Painted Sign (Red)",   "Painted Sign (red).");
            RegisterSignPrefab(zns, SignWhiteName, "Painted Sign (White)", "Painted Sign (white).");
            RegisterSignPrefab(zns, SignBlueName,  "Painted Sign (Blue)",  "Painted Sign (blue).");
            RegisterSignPrefab(zns, SignBlackName, "Painted Sign (Black)", "Painted Sign (black).");
        }

        private static void RegisterSignPrefab(ZNetScene zns, string name, string displayName, string desc)
        {
            if (zns.GetPrefab(name) != null) return;
            var clone = TrailborneAssets.ClonePrefab(SourceSign, name);
            if (clone == null)
            {
                TrailbornePlugin.Log.LogWarning($"[Trailborne/M1] Source sign prefab missing, skipping {name}");
                return;
            }

            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = displayName;
                piece.m_description = desc;
                piece.m_category    = Piece.PieceCategory.Furniture;
                piece.m_resources   = new[]
                {
                    BuildReq("Wood", 2),
                    BuildReq(InkLookupForSign(name), 1),
                };
                if (_icons.TryGetValue(name, out var iconFile))
                    piece.m_icon = TrailborneAssets.LoadPngAsSprite(iconFile);
            }

            // Tag the sign with our colored variant so Interact handler can color-pin later
            var tag = clone.AddComponent<TrailborneSignTag>();
            tag.PrefabName = name;

            // Tint the visible mesh
            if (SignColors.TryGetValue(name, out var col))
                TintMeshRenderers(clone, col);

            TrailborneAssets.RegisterPrefabInZNetScene(clone);
            TrailbornePlugin.Log.LogInfo($"[Trailborne/M1] Registered sign piece: {name}");
        }

        private static string InkLookupForSign(string signName)
        {
            switch (signName)
            {
                case SignRedName:   return TrailbornePigments.InkRedName;
                case SignWhiteName: return TrailbornePigments.InkWhiteName;
                case SignBlueName:  return TrailbornePigments.InkBlueName;
                case SignBlackName: return TrailbornePigments.InkBlackName;
                default: return TrailbornePigments.InkRedName;
            }
        }

        private static void TintMeshRenderers(GameObject go, Color c)
        {
            // Material-instance tint via SetColor on _Color. Vanilla sign uses lit
            // material with _Color — same shader prop ID used elsewhere in decomp.
            var prop = Shader.PropertyToID("_Color");
            foreach (var rend in go.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                try
                {
                    var mats = rend.sharedMaterials;
                    var newMats = new Material[mats.Length];
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null) continue;
                        var m = new Material(mats[i]);
                        if (m.HasProperty(prop)) m.SetColor(prop, c);
                        newMats[i] = m;
                    }
                    rend.sharedMaterials = newMats;
                }
                catch (Exception e)
                {
                    TrailbornePlugin.Log.LogWarning($"[Trailborne/M1] Tint failed on {go.name}: {e.Message}");
                }
            }
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — sign hammer pieces + resource rebuild
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            // Sign pieces into Hammer build menu + REBUILD their resource lists
            // now that ink items exist in ObjectDB.
            var hammerTable = TrailborneAssets.GetHammerPieceTable();
            foreach (var n in new[] { SignRedName, SignWhiteName, SignBlueName, SignBlackName })
            {
                var p = zns?.GetPrefab(n);
                if (p == null) continue;
                var piece = p.GetComponent<Piece>();
                if (piece != null)
                {
                    piece.m_resources = new[]
                    {
                        BuildReq("Wood", 2),
                        BuildReq(InkLookupForSign(n), 1),
                    };
                }
                if (hammerTable != null) TrailborneAssets.AddPieceToTable(p, hammerTable);
            }

            TrailbornePlugin.Log.LogInfo("[Trailborne/M1] Signs ObjectDB wiring complete (sign recipes + hammer pieces).");
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount)
        {
            return TrailborneAssets.BuildReq(resourcePrefabName, amount, "M1");
        }
    }
}
