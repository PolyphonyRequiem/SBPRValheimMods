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
    // INSIDE this namespace body to keep the readable `Pigments.PigmentRedName` syntax.
    using Pigments = SBPR.Trailborne.Features.Pigments.Pigments;

    /// <summary>
    /// Painted Sign — ONE buildable signpost. The sign is built UNPAINTED (plain
    /// vanilla wood mesh, no color baked in). After placement, interacting with it
    /// opens the custom combined Paint+Text uGUI panel (§A2.6, re-lock 2026-06-05),
    /// which replaces the vanilla text dialog. The panel paints the sign TWO-TONE —
    /// a board/text color AND a separate border color — charging one pigment per
    /// filled color slot, and edits the text label (free). Both colors persist +
    /// sync via per-instance ZDO string fields (<see cref="ZdoTextColor"/> +
    /// <see cref="ZdoBorderColor"/>; empty = unset). The board mesh carries no
    /// separable frame, so a thin border element is kitbashed in at register time
    /// (<see cref="BorderChildName"/>) to receive the second tone.
    ///
    /// This REPLACES the earlier four-tinted-buildables design AND the interim
    /// single-color apply-ink-item model — color is no longer a prefab fork nor an
    /// item-application gesture; it is per-instance ZDO state driven by the panel.
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

        // Build cost (unpainted). Pigment is NOT a build ingredient — it is
        // consumed at paint time, one pigment per filled color slot, on the PLACED sign.
        public const int WoodCost = 2;

        // ZDO field storing the LEGACY single applied color ("" = unpainted). Retained
        // for one-way migration only: a sign painted under the old single-color model
        // (SBPR_SignColor) is read once on spawn and folded into SBPR_SignTextColor.
        public const string ZdoColor = "SBPR_SignColor";

        // Two-tone ZDO fields (§A2.6, re-lock 2026-06-05). Board/text tone + a separate
        // border tone. "" = that slot unset. Owner-write via ZNetView (mirrors CairnTag).
        public const string ZdoTextColor   = "SBPR_SignTextColor";
        public const string ZdoBorderColor = "SBPR_SignBorderColor";

        // Name of the kitbashed decorative pole child. TintRenderers skips renderers
        // under this subtree so painting tints the BOARD only, not the post.
        private const string PostChildName = "SBPR_SignPost";

        // Name of the kitbashed two-tone BORDER child (§A2.6). A thin colored matte/frame
        // around the board, tinted independently of the board so the sign reads two-tone.
        // The vanilla sign mesh has no separable frame renderer, so we add this element
        // (clean-room: reuses the board's own mesh, scaled — no new authored geometry).
        public const string BorderChildName = "SBPR_SignBorder";

        // Color identifiers — must match Pigment colors + Cairns.Colors.
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
        /// Map a pigment ITEM prefab name to its color identity, or null if the prefab
        /// is not one of our four pigments. Inverse of <see cref="PigmentForColor"/>.
        /// Retained as public API for pigment-detection (e.g. the deferred pin path);
        /// the retired apply-pigment paint seam that originally drove it is gone.
        /// </summary>
        public static string? ColorForPigment(string pigmentPrefabName)
        {
            if (pigmentPrefabName == Pigments.PigmentRedName)   return "red";
            if (pigmentPrefabName == Pigments.PigmentWhiteName) return "white";
            if (pigmentPrefabName == Pigments.PigmentBlueName)  return "blue";
            if (pigmentPrefabName == Pigments.PigmentBlackName) return "black";
            return null;
        }

        /// <summary>
        /// Map a color identity ("red"/"white"/"blue"/"black") to the matching pigment
        /// ITEM prefab name (the pigment the panel charges for that slot), or null if
        /// the color isn't one of our four. Inverse of <see cref="ColorForPigment"/>;
        /// used by the paint backend to compute + consume the crafting-style cost.
        /// </summary>
        public static string? PigmentForColor(string color)
        {
            switch (color)
            {
                case "red":   return Pigments.PigmentRedName;
                case "white": return Pigments.PigmentWhiteName;
                case "blue":  return Pigments.PigmentBlueName;
                case "black": return Pigments.PigmentBlackName;
                default:      return null;
            }
        }

        /// <summary>Human-facing pigment label for a color id, e.g. "Red Pigment".</summary>
        public static string PigmentLabel(string color)
        {
            if (string.IsNullOrEmpty(color)) return "";
            return char.ToUpperInvariant(color[0]) + color.Substring(1) + " Pigment";
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
                    "A free-standing wooden signpost on a 2m pole, placed unpainted. " +
                    "Interact to open its panel: pick a text color and an optional border " +
                    "color (one pigment each), paint it, and write the text.";
                // SPADE menu home (design pillar: Explorer-placed pieces live on the
                // Trailblazer's Spade, not the Hammer). The spade's PieceTable declares
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

            // Kitbash a thin two-tone BORDER element behind the board (§A2.6 re-lock
            // 2026-06-05). The vanilla sign mesh is a single plank with no separable
            // frame, so we add one: a copy of the board mesh, slightly larger in-plane
            // and pushed back, with its own material — tinted independently of the board
            // for the text/border two-tone. Baked into the prefab like the pole.
            KitbashBorderElement(clone);

            // Tag the sign so the paint receiver + pin path can identify it and so
            // its per-instance colors (from ZDO) are re-applied on spawn.
            clone.AddComponent<SignTag>();

            Assets.RegisterPrefabInZNetScene(clone);
            Plugin.Log.LogInfo($"[Trailborne/M1] Registered sign piece: {SignName} (single, unpainted; paint via combined Paint+Text panel)");
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
                // Unpainted build cost: Wood only (pigment is applied post-placement).
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

            Plugin.Log.LogInfo("[Trailborne/M1] Signs ObjectDB wiring complete (single Painted Sign piece; paint via combined Paint+Text panel; placed via Spade menu).");
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
        /// Kitbash a thin two-tone BORDER element for the sign (§A2.6, re-lock
        /// 2026-06-05). The vanilla sign mesh is a single wood plank with no separable
        /// frame renderer, so we ADD one we can tint independently of the board:
        ///
        ///   • Find the BOARD mesh — the largest <see cref="MeshFilter"/> on the sign
        ///     that is NOT part of the decorative pole (the pole runs first and is named
        ///     <see cref="PostChildName"/>).
        ///   • Create a child <c>SBPR_SignBorder</c> under the board's transform that
        ///     REUSES the board's own <c>sharedMesh</c> (clean-room — no authored
        ///     geometry) with its OWN material instance (a copy of the board material,
        ///     so an unpainted sign reads as plain wood).
        ///   • Scale it slightly LARGER in the board plane (the two big mesh axes) and
        ///     slightly THINNER on the depth axis (the smallest mesh axis), kept
        ///     concentric about the board mesh's bounds-center. The thinner depth keeps
        ///     the whole border INSIDE the board's thickness envelope except where it
        ///     pokes out sideways — so it reads as a recessed colored matte/frame around
        ///     the board edges and can NEVER sit in front of the text (orientation-free,
        ///     no front/back detection needed).
        ///
        /// Visual polish (exact inset, frame thickness) is a v0.2+ concern; the
        /// load-bearing requirement here is a separately-tintable border element. If the
        /// board mesh can't be found we log and skip — the sign still works single-tone
        /// (TintBorder no-ops on the missing element). Public UnityEngine API only.
        /// </summary>
        private static void KitbashBorderElement(GameObject signRoot)
        {
            if (signRoot == null) return;

            // Find the board: largest non-pole MeshFilter with a real mesh.
            MeshFilter? boardMf = null;
            float bestVol = -1f;
            foreach (var mf in signRoot.GetComponentsInChildren<MeshFilter>(includeInactive: true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                if (IsUnderPost(mf.transform)) continue;     // skip the decorative pole
                if (IsUnderBorder(mf.transform)) continue;    // never re-wrap our own border
                var s = mf.sharedMesh.bounds.size;
                float vol = Mathf.Max(s.x, 1e-4f) * Mathf.Max(s.y, 1e-4f) * Mathf.Max(s.z, 1e-4f);
                if (vol > bestVol) { bestVol = vol; boardMf = mf; }
            }

            if (boardMf == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/M1] {SignName}: no board mesh found to wrap; two-tone border skipped " +
                    "(sign will paint single-tone — board tint only).");
                return;
            }

            var boardRend = boardMf.GetComponent<MeshRenderer>();
            var boardMat  = boardRend != null ? boardRend.sharedMaterial : null;

            // Identify the depth axis = the smallest mesh extent; the other two are the
            // in-plane (face) axes that the rim grows along.
            Vector3 size = boardMf.sharedMesh.bounds.size;
            Vector3 center = boardMf.sharedMesh.bounds.center;
            int depthAxis = 0;
            if (size.y < size.x && size.y <= size.z) depthAxis = 1;
            else if (size.z < size.x && size.z < size.y) depthAxis = 2;

            const float InPlaneGrow = 1.10f; // rim pokes out ~5% on each in-plane edge
            const float DepthShrink  = 0.85f; // sit inside the board's thickness (no z-fight, text-safe)
            Vector3 scale = new Vector3(InPlaneGrow, InPlaneGrow, InPlaneGrow);
            scale[depthAxis] = DepthShrink;

            var border = new GameObject(BorderChildName);
            border.transform.SetParent(boardMf.transform, worldPositionStays: false);
            border.transform.localRotation = Quaternion.identity;
            border.transform.localScale    = scale;
            // Keep the scaled copy concentric about the board mesh's bounds-center:
            // localPos = C ⊙ (1 - scale) maps C→C while expanding/contracting around it.
            border.transform.localPosition = new Vector3(
                center.x * (1f - scale.x),
                center.y * (1f - scale.y),
                center.z * (1f - scale.z));

            var bmf = border.AddComponent<MeshFilter>();
            bmf.sharedMesh = boardMf.sharedMesh;
            var brend = border.AddComponent<MeshRenderer>();
            // Own material instance so tinting the border never bleeds into the board.
            // Copy the board material so an UNPAINTED border reads as plain wood.
            if (boardMat != null) brend.sharedMaterial = new Material(boardMat);

            border.SetActive(true);
            Plugin.Log.LogInfo(
                $"[Trailborne/M1] Kitbashed two-tone border '{BorderChildName}' on {SignName} " +
                $"(depthAxis={depthAxis}, grow={InPlaneGrow}, depth={DepthShrink}).");
        }

        /// <summary>
        /// Tint the sign BOARD (the painted plank) to <paramref name="c"/> by cloning
        /// its shared materials and setting the lit shader's _Color. No-op on a headless
        /// server (no renderers). SKIPS renderers under the decorative pole
        /// (<see cref="PostChildName"/>) AND under the two-tone border
        /// (<see cref="BorderChildName"/>) so this colours the board only. Driven by
        /// SignTag for the per-instance text/board tone.
        /// </summary>
        public static void TintBoard(GameObject go, Color c)
        {
            TintMatching(go, c, includeBorder: false);
        }

        /// <summary>
        /// Drive the sign's TMP TEXT WIDGET (<c>Sign.m_textWidget</c>) to
        /// <paramref name="c"/> — the text/board tone also colours the written letters
        /// (§A2.6 / Issue 4b). The board mesh tint alone (<see cref="TintBoard"/>) never
        /// touched the text, so painting "red" tinted the plank but left the letters
        /// their original colour. We set BOTH <c>color</c> (vertex tint) AND
        /// <c>faceColor</c> (the TMP face material colour) so the change is robust across
        /// TMP's two colour paths and survives the vanilla <c>Sign.UpdateText</c> repaint
        /// (which only ever reassigns <c>.text</c>, never the colour).
        ///
        /// No-op on a headless server (no widget) or before the Sign component's Awake
        /// has wired <c>m_textWidget</c>. Public Valheim/TMP API only — clean-room
        /// (reads the public <c>Sign.m_textWidget</c> field; no decompiled body copied).
        /// </summary>
        public static void TintText(GameObject go, Color c)
        {
            if (go == null) return;
            var sign = go.GetComponent<Sign>();
            var widget = sign != null ? sign.m_textWidget : null;
            if (widget == null) return;
            try
            {
                widget.color = c;       // vertex/tint colour
                widget.faceColor = c;   // TMP face material colour (Color32 via implicit conv)
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/M1] Sign text tint failed on {go.name}: {e.Message}");
            }
        }

        /// <summary>
        /// Tint ONLY the kitbashed border element (<see cref="BorderChildName"/>) to
        /// <paramref name="c"/> — the second (border) tone of the two-tone sign (§A2.6).
        /// No-op if the border element is absent (e.g. the board mesh couldn't be found
        /// at kitbash time) or on a headless server. Driven by SignTag.
        /// </summary>
        public static void TintBorder(GameObject go, Color c)
        {
            var prop = Shader.PropertyToID("_Color");
            foreach (var rend in go.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (rend == null) continue;
                if (!IsUnderBorder(rend.transform)) continue; // border subtree only
                TintRendererMaterials(rend, c, prop, go.name);
            }
        }

        /// <summary>
        /// Revert the BOARD (and text-tinted plank renderers) to their original
        /// unpainted materials — the "remove text color" / None affordance (§A2.6,
        /// Issue 4). Restores from the per-renderer backup captured on the first tint,
        /// so the change is visible LIVE (not only after a reload). No-op for renderers
        /// that were never tinted. Skips the pole + border subtrees, mirroring
        /// <see cref="TintBoard"/>'s selection.
        /// </summary>
        public static void RestoreBoard(GameObject go)
        {
            foreach (var rend in go.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (rend == null) continue;
                if (IsUnderPost(rend.transform)) continue;
                if (IsUnderBorder(rend.transform)) continue; // border reverts separately
                RestoreRendererMaterials(rend);
            }
        }

        /// <summary>
        /// Revert ONLY the kitbashed border element to its original (unpainted) material
        /// — the "remove border color" / None affordance (§A2.6, Issue 4). Restores from
        /// the per-renderer backup so it's visible LIVE. No-op if never tinted.
        /// </summary>
        public static void RestoreBorder(GameObject go)
        {
            foreach (var rend in go.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (rend == null) continue;
                if (!IsUnderBorder(rend.transform)) continue;
                RestoreRendererMaterials(rend);
            }
        }

        /// <summary>
        /// Read the TMP text widget's current face colour, or null if there's no widget
        /// (headless / not yet wired). Used by SignTag to snapshot the original
        /// (unpainted) text colour once, so "remove text color" can revert to it.
        /// </summary>
        public static Color? TryReadTextColor(GameObject go)
        {
            if (go == null) return null;
            var sign = go.GetComponent<Sign>();
            var widget = sign != null ? sign.m_textWidget : null;
            if (widget == null) return null;
            return widget.faceColor;
        }

        /// <summary>
        /// Shared board-tint walk. Tints every renderer that is NOT under the pole and
        /// (when <paramref name="includeBorder"/> is false) NOT under the border. The
        /// border is tinted separately by <see cref="TintBorder"/>.
        /// </summary>
        private static void TintMatching(GameObject go, Color c, bool includeBorder)
        {
            var prop = Shader.PropertyToID("_Color");
            foreach (var rend in go.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (rend == null) continue;
                // Skip the decorative kitbash pole — board only (Daniel 2026-06-05).
                if (IsUnderPost(rend.transform)) continue;
                // Skip the border subtree unless explicitly included (it has its own tone).
                if (!includeBorder && IsUnderBorder(rend.transform)) continue;
                TintRendererMaterials(rend, c, prop, go.name);
            }
        }

        private static void TintRendererMaterials(Renderer rend, Color c, int prop, string ownerName)
        {
            try
            {
                // Snapshot the original materials ONCE, before the first tint, so the
                // None affordance can revert (the tint replaces sharedMaterials with
                // clones; without a backup the original reference is lost on this
                // renderer until a fresh spawn). The backup component lives on the
                // renderer's own GameObject — pure runtime state, no ZDO.
                var backup = rend.GetComponent<SignTintBackup>();
                if (backup == null)
                {
                    backup = rend.gameObject.AddComponent<SignTintBackup>();
                    backup.Original = rend.sharedMaterials;
                }

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
                Plugin.Log.LogWarning($"[Trailborne/M1] Sign tint failed on {ownerName}: {e.Message}");
            }
        }

        private static void RestoreRendererMaterials(Renderer rend)
        {
            var backup = rend.GetComponent<SignTintBackup>();
            if (backup == null || backup.Original == null) return; // never tinted
            try
            {
                rend.sharedMaterials = backup.Original;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/M1] Sign tint restore failed on {rend.gameObject.name}: {e.Message}");
            }
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount, bool warn = true)
        {
            return Assets.BuildReq(resourcePrefabName, amount, "M1", warn);
        }

        /// <summary>
        /// True if <paramref name="t"/> is the decorative pole child or anything nested
        /// under it. Used by the tint helpers to keep the post un-painted.
        /// </summary>
        private static bool IsUnderPost(Transform t)
        {
            for (var cur = t; cur != null; cur = cur.parent)
                if (cur.name == PostChildName) return true;
            return false;
        }

        /// <summary>
        /// True if <paramref name="t"/> is the kitbashed border child or anything nested
        /// under it. Used by the tint helpers to colour the border independently.
        /// </summary>
        private static bool IsUnderBorder(Transform t)
        {
            for (var cur = t; cur != null; cur = cur.parent)
                if (cur.name == BorderChildName) return true;
            return false;
        }
    }
}
