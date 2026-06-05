using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Signs
{
    // The Pigments TYPE lives in namespace SBPR.Trailborne.Features.Pigments. From
    // inside this sibling Features.* namespace the bare name `Pigments` would
    // otherwise bind to that sibling NAMESPACE (the enclosing `Features` scope is
    // searched before a compilation-unit alias), so we alias the name to the type
    // INSIDE this namespace body to keep the readable `Pigments.InkRedName` syntax.
    using Pigments = SBPR.Trailborne.Features.Pigments.Pigments;

    /// <summary>
    /// Painted Signs (4 buildable variants, color tinted from base material).
    /// Player presses Shift+E on a placed sign to add a colored map pin matching
    /// the sign's text + color. Split out of the old M1; depends DOWN on
    /// Pigments for its ink ingredient.
    ///
    /// All gated behind ServerContext.OnSBServer.
    /// </summary>
    public static class Signs
    {
        // Sign piece prefab names
        public const string SignRedName   = "piece_sbpr_sign_red";
        public const string SignWhiteName = "piece_sbpr_sign_white";
        public const string SignBlueName  = "piece_sbpr_sign_blue";
        public const string SignBlackName = "piece_sbpr_sign_black";

        // Source clones
        private const string SourceSign     = "sign";

        // Icon file mapping
        private static readonly Dictionary<string, string> icons = new Dictionary<string, string>
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
            var clone = Assets.ClonePrefab(SourceSign, name);
            if (clone == null)
            {
                Plugin.Log.LogWarning($"[Trailborne/M1] Source sign prefab missing, skipping {name}");
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
                    // Ink isn't in ObjectDB yet at prefab-build time; this requirement
                    // is rebuilt in DoObjectDBWiring once Pigments registers the inks.
                    // Suppress the known-transient "NOT FOUND" warning for this phase.
                    BuildReq(InkLookupForSign(name), 1, warn: false),
                };
                if (icons.TryGetValue(name, out var iconFile))
                    piece.m_icon = Assets.LoadPngAsSprite(iconFile);
            }

            // Tag the sign with our colored variant so Interact handler can color-pin later
            var tag = clone.AddComponent<SignTag>();
            tag.PrefabName = name;

            // Tint the visible mesh
            if (SignColors.TryGetValue(name, out var col))
                TintMeshRenderers(clone, col);

            Assets.RegisterPrefabInZNetScene(clone);
            Plugin.Log.LogInfo($"[Trailborne/M1] Registered sign piece: {name}");
        }

        private static string InkLookupForSign(string signName)
        {
            switch (signName)
            {
                case SignRedName:   return Pigments.InkRedName;
                case SignWhiteName: return Pigments.InkWhiteName;
                case SignBlueName:  return Pigments.InkBlueName;
                case SignBlackName: return Pigments.InkBlackName;
                default: return Pigments.InkRedName;
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
                    Plugin.Log.LogWarning($"[Trailborne/M1] Tint failed on {go.name}: {e.Message}");
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
            var hammerTable = Assets.GetHammerPieceTable();
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
                if (hammerTable != null) Assets.AddPieceToTable(p, hammerTable);
            }

            Plugin.Log.LogInfo("[Trailborne/M1] Signs ObjectDB wiring complete (sign recipes + hammer pieces).");
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount, bool warn = true)
        {
            return Assets.BuildReq(resourcePrefabName, amount, "M1", warn);
        }
    }
}
