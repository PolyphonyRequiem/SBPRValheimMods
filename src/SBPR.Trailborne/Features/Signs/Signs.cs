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

        // Vanilla 2m wood pole, kitbashed in as a decorative post so the sign stands
        // free on the ground like a trail signpost instead of needing a wall to
        // attach to (Daniel playtest 2026-06-05: "kit bash in a 2m pole that the sign
        // attaches to"). Vanilla prefab name only — clean-room safe.
        private const string SourcePole = "wood_pole2";

        // Lift the sign BOARD's bottom edge to this height (metres) so the board sits
        // at readable height near the top of the ~2m pole. Measured from the placed
        // sign's pivot (ground) at register time, pivot-robust (see KitbashStandingPole).
        private const float BoardBottomHeight = 1.2f;

        // Build cost (unpainted). Ink is NOT a build ingredient anymore — it is
        // consumed at paint time, one ink per paint, on the PLACED sign.
        public const int WoodCost = 2;

        // ZDO field storing the applied color identity ("" = unpainted).
        public const string ZdoColor = "SBPR_SignColor";

        // Name of the kitbashed decorative pole child. TintRenderers skips renderers
        // under this subtree so painting tints the BOARD only, not the post.
        private const string PostChildName = "SBPR_SignPost";

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
                    "A free-standing wooden signpost on a 2m pole, placed unpainted. Apply an ink " +
                    "(red / white / blue / black) to paint it; apply a different ink to repaint. Press E to write text.";
                // SPADE menu home (design pillar: Explorer-placed pieces live on the
                // Trailblazer's Tools, not the Hammer). The spade's PieceTable declares
                // only the Misc category ('Trail' tab), so the sign MUST be Misc to
                // render there — Furniture would bucket into a tab the table doesn't have.
                piece.m_category    = Piece.PieceCategory.Misc;
                piece.m_resources   = new[] { BuildReq("Wood", WoodCost) };
                // NO station-proximity gate to PLACE the sign (Daniel 2026-06-05: "for
                // the path light and sign, no bench requirement"). The vanilla `sign`
                // clone inherits m_craftingStation = Workbench; clear it so the sign
                // places anywhere. (Build COST is unchanged — 2 Wood; this is about
                // station proximity, not making it free.)
                piece.m_craftingStation = null;
                // Keep the vanilla sign's own build icon (the unpainted wood look).
                // No per-color icon: color is no longer a prefab fork.
            }

            // Kitbash a 2m wood pole under the board so the sign stands free on the
            // ground at readable height (Daniel 2026-06-05). Runs on server + every
            // client (RegisterPrefabs is a ZNetScene.Awake postfix on both), so the
            // post is baked into the registered prefab — no ZDO, syncs by construction.
            KitbashStandingPole(clone);

            // Tag the sign so the paint receiver + pin path can identify it and so
            // its per-instance color (from ZDO) is re-applied on spawn.
            clone.AddComponent<SignTag>();

            Assets.RegisterPrefabInZNetScene(clone);
            Plugin.Log.LogInfo($"[Trailborne/M1] Registered sign piece: {SignName} (single, unpainted; paint via ink)");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — resource rebuild; sign now lives on the SPADE menu
        // (added to the spade PieceTable in Trailblazing.DoObjectDBWiring, which runs
        // after this in Registrar's fan-out), NOT the Hammer.
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            var p = zns?.GetPrefab(SignName);
            if (p == null) return;

            var piece = p.GetComponent<Piece>();
            if (piece != null)
            {
                // Unpainted build cost: Wood only (ink is applied post-placement).
                piece.m_resources = new[] { BuildReq("Wood", WoodCost) };
                // Re-assert: no station-proximity gate to place (Daniel 2026-06-05).
                // The prefab-phase clear above is authoritative, but rebuilding here
                // keeps the final placed-piece state explicit alongside the resources.
                piece.m_craftingStation = null;
            }
            // Sign goes on the SPADE build menu now, not the Hammer. The actual
            // AddPieceToTable into the spade-only PieceTable happens in
            // Trailblazing.DoObjectDBWiring (Registrar runs Trailblazing AFTER Signs,
            // and the sign prefab is already registered from the earlier
            // RegisterPrefabs pass, so the lookup there resolves).

            Plugin.Log.LogInfo("[Trailborne/M1] Signs ObjectDB wiring complete (single Painted Sign piece; paint-via-ink; placed via Spade menu).");
        }

        /// <summary>
        /// Kitbash a vanilla 2m wood pole (<c>wood_pole2</c>) under the sign as a
        /// purely decorative post, and lift the sign BOARD (and its interact collider
        /// + text canvas, which are child transforms of the sign root) up to readable
        /// height near the top of the pole. The vanilla `sign` is a small placard
        /// meant to mount on an existing wall; this makes it free-standing like a
        /// trail signpost (Daniel 2026-06-05).
        ///
        /// Clean-room: vanilla prefab + public UnityEngine transform API only. The
        /// pole clone is stripped to pure decoration (no ZNetView / Piece / WearNTear /
        /// Collider via <see cref="Assets.StripToDecorative"/>) so it carries no ZDO,
        /// is not separately destructible, and never intercepts the E-to-write raycast
        /// — the BOARD stays the sole interactable/paint target (SignTag is on the root,
        /// the Sign component + board collider are untouched).
        /// </summary>
        private static void KitbashStandingPole(GameObject signRoot)
        {
            if (signRoot == null) return;
            var rootT = signRoot.transform;

            // Measure the board's current foot so we know how far to lift it. The sign
            // mesh + collider live in children; raising every direct child by the same
            // delta keeps the board, its BoxCollider, and the text canvas aligned.
            float boardFootY = Assets.MeasureLocalFootY(signRoot);
            float lift = BoardBottomHeight - boardFootY;
            if (lift < 0f) lift = 0f; // never push the board below where it already sits

            if (lift > 0f)
            {
                foreach (Transform child in rootT)
                {
                    var lp = child.localPosition;
                    child.localPosition = new Vector3(lp.x, lp.y + lift, lp.z);
                }
            }

            // Plant the pole. Clone via ClonePrefab so it comes from ZNetScene, then
            // strip it to decoration and parent under the sign root at ground level.
            var pole = Assets.ClonePrefab(SourcePole, PostChildName);
            if (pole == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/M1] Source pole '{SourcePole}' missing; sign placed without a post (board still lifted).");
                return;
            }
            Assets.StripToDecorative(pole);
            pole.name = PostChildName;
            var poleT = pole.transform;
            poleT.SetParent(rootT, worldPositionStays: false);
            poleT.localRotation = Quaternion.identity;
            poleT.localScale    = Vector3.one;
            // Plant the pole foot on the ground (sign pivot, local y=0). Measure the
            // pole's own foot rather than ASSUMING wood_pole2's pivot is at its base —
            // pivot-robust, same technique as the board lift. Offset so foot → y=0.
            float poleFootY = Assets.MeasureLocalFootY(pole);
            poleT.localPosition = new Vector3(0f, -poleFootY, 0f);
            pole.SetActive(true);

            Plugin.Log.LogInfo($"[Trailborne/M1] Kitbashed 2m pole under {SignName}; board lifted {lift:F2}m to standing height.");
        }

        /// <summary>
        /// Tint the sign BOARD by cloning its shared materials and setting the lit
        /// shader's _Color. No-op on a headless server (no renderers). Renderers under
        /// the decorative pole (<see cref="PostChildName"/>) are SKIPPED so painting
        /// colors the board only, not the post. Shared by SignTag's per-instance recolor.
        /// </summary>
        public static void TintRenderers(GameObject go, Color c)
        {
            var prop = Shader.PropertyToID("_Color");
            foreach (var rend in go.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                // Skip the decorative kitbash pole — board only (Daniel 2026-06-05).
                if (rend == null) continue;
                if (IsUnderPost(rend.transform)) continue;
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

        /// <summary>
        /// True if <paramref name="t"/> is the decorative pole child or anything nested
        /// under it. Used by <see cref="TintRenderers"/> to keep the post un-painted.
        /// </summary>
        private static bool IsUnderPost(Transform t)
        {
            for (var cur = t; cur != null; cur = cur.parent)
                if (cur.name == PostChildName) return true;
            return false;
        }
    }
}
