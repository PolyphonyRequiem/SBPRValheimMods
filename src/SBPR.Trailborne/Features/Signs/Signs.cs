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
    /// Painted Sign — ONE buildable signpost (v0.1.0 model, locked by Daniel
    /// 2026-06-04). The sign is built UNPAINTED (plain vanilla wood mesh, no
    /// color baked in). Color is runtime state applied AFTER placement by
    /// applying a pigment/ink item to the placed sign (the ItemStand pattern,
    /// via the vanilla Interactable.UseItem contract). Re-applying a different
    /// ink repaints it. The chosen color persists + syncs via the sign's ZDO
    /// (string field SBPR_SignColor; empty = unpainted).
    ///
    /// This REPLACES the earlier four-tinted-buildables design
    /// (piece_sbpr_sign_{red,white,blue,black}) — color no longer forks the
    /// prefab; it is a per-instance ZDO field. (SignInteractPatch.cs still
    /// defaults the pin/text label to "Painted Sign".)
    ///
    /// All gated behind ServerContext.OnSBServer.
    /// </summary>
    public static class Signs
    {
        // Single sign piece prefab name.
        public const string SignName = "piece_sbpr_sign";

        // Source clone (vanilla wood sign — its mesh is the unpainted base state).
        private const string SourceSign = "sign";

        // Build cost (unpainted). Ink is NOT a build ingredient anymore — it is
        // consumed at paint time, one ink per paint, on the PLACED sign.
        public const int WoodCost = 2;

        // ZDO field storing the applied color identity ("" = unpainted).
        public const string ZdoColor = "SBPR_SignColor";

        // Color identifiers — must match Pigments ink colors + Cairns.Colors.
        public static readonly string[] Colors = { "red", "white", "blue", "black" };

        // Color per identifier (used to tint the placed sign's mesh + as pin color).
        public static readonly Dictionary<string, Color> ColorValues = new Dictionary<string, Color>
        {
            { "red",   new Color(0.85f, 0.18f, 0.18f, 1f) },
            { "white", new Color(0.95f, 0.94f, 0.88f, 1f) },
            { "blue",  new Color(0.20f, 0.40f, 0.85f, 1f) },
            { "black", new Color(0.10f, 0.10f, 0.12f, 1f) },
        };

        // Pin type per color — vanilla Minimap pin sprite reuse for color clarity.
        // Consumed by the (still-unregistered) SignInteractPatch pin path.
        public static readonly Dictionary<string, Minimap.PinType> PinTypes = new Dictionary<string, Minimap.PinType>
        {
            { "red",   Minimap.PinType.Icon3 }, // red-ish vanilla pin
            { "white", Minimap.PinType.Icon0 }, // generic / white
            { "blue",  Minimap.PinType.Icon2 }, // blue-ish
            { "black", Minimap.PinType.Icon4 }, // dark / generic
        };

        /// <summary>
        /// Map an ink ITEM prefab name to its color identity, or null if the
        /// prefab is not one of our four inks. Used by SignPaintPatch to decide
        /// whether a UseItem call is a paint action.
        /// </summary>
        public static string? ColorForInk(string inkPrefabName)
        {
            if (inkPrefabName == Pigments.InkRedName)   return "red";
            if (inkPrefabName == Pigments.InkWhiteName) return "white";
            if (inkPrefabName == Pigments.InkBlueName)  return "blue";
            if (inkPrefabName == Pigments.InkBlackName) return "black";
            return null;
        }

        public static Minimap.PinType PinTypeForColor(string color)
        {
            if (color != null && PinTypes.TryGetValue(color, out var t)) return t;
            return Minimap.PinType.Icon0;
        }

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (called from ZNetScene.Awake postfix)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            // ONE sign piece — clone vanilla wood sign, no tint (placed unpainted).
            if (zns.GetPrefab(SignName) != null) return;
            var clone = Assets.ClonePrefab(SourceSign, SignName);
            if (clone == null)
            {
                Plugin.Log.LogWarning($"[Trailborne/M1] Source sign prefab missing, skipping {SignName}");
                return;
            }

            var piece = clone.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name        = "Painted Sign";
                piece.m_description =
                    "A wooden signpost, placed unpainted. Apply an ink (red / white / blue / black) " +
                    "to paint it; apply a different ink to repaint. Press E to write text.";
                piece.m_category    = Piece.PieceCategory.Furniture;
                piece.m_resources   = new[] { BuildReq("Wood", WoodCost) };
                // Keep the vanilla sign's own build icon (the unpainted wood look).
                // No per-color icon: color is no longer a prefab fork.
            }

            // Tag the sign so the paint receiver + pin path can identify it and so
            // its per-instance color (from ZDO) is re-applied on spawn.
            clone.AddComponent<SignTag>();

            Assets.RegisterPrefabInZNetScene(clone);
            Plugin.Log.LogInfo($"[Trailborne/M1] Registered sign piece: {SignName} (single, unpainted; paint via ink)");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — single sign hammer piece + resource rebuild
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            var hammerTable = Assets.GetHammerPieceTable();
            var p = zns?.GetPrefab(SignName);
            if (p == null) return;

            var piece = p.GetComponent<Piece>();
            if (piece != null)
            {
                // Unpainted build cost: Wood only (ink is applied post-placement).
                piece.m_resources = new[] { BuildReq("Wood", WoodCost) };
            }
            if (hammerTable != null) Assets.AddPieceToTable(p, hammerTable);

            Plugin.Log.LogInfo("[Trailborne/M1] Signs ObjectDB wiring complete (single Painted Sign piece; paint-via-ink).");
        }

        /// <summary>
        /// Tint every renderer on <paramref name="go"/> by cloning its shared
        /// materials and setting the lit shader's _Color. No-op on a headless
        /// server (no renderers). Shared by SignTag's per-instance recolor.
        /// </summary>
        public static void TintRenderers(GameObject go, Color c)
        {
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
                    Plugin.Log.LogWarning($"[Trailborne/M1] Sign tint failed on {go.name}: {e.Message}");
                }
            }
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount, bool warn = true)
        {
            return Assets.BuildReq(resourcePrefabName, amount, "M1", warn);
        }
    }
}
