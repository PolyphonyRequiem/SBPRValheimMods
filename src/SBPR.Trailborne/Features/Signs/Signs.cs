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

        // FALLBACK board-bottom height (metres), used ONLY when the pole clone is
        // missing (error path). Without a pole crown to anchor to we lift the board to
        // this fixed readable height. The NORMAL path anchors the board's TOP to the
        // MEASURED pole crown (see KitbashStandingPole / BoardTopInset), so this constant
        // no longer drives the common case — it used to, and a fixed 1.2 m landed the
        // board bottom at 60 % of a 2 m post (dead centre), which is exactly the
        // "board floats mid-post" regression this card fixes (t_05bb5168).
        private const float BoardBottomHeight = 1.2f;

        // ── Seat tuning constants + crown/standoff math + the post-foot ground collider
        //    now live in Runtime/SignGeometry.cs (card t_cc093d04), shared verbatim with the
        //    four Marker Signs so they can never drift from the Painted Sign again. This file
        //    references SignGeometry.BoardTopInset / .KissEpsilon / .PostFootCollider* and
        //    calls SignGeometry.CrownAnchorLift / .LateralStandoff / .AddPostFootGroundCollider.
        //    The construction WALK (which children move, the 180° flip, plant-pole order) stays
        //    here — the sign's clone-and-move-root-group topology is not the marker's additive
        //    one, so only the topology-independent helpers are shared (spec §1).
        //
        // FALLBACK board-bottom height detail (wood_pole2 crown at root-local y=2.0, vanilla
        // sign board height 0.5 m → board lands at [1.40, 1.90] m, centre ~1.65 m, post foot
        // flush at y=0) is the SignGeometry.BoardTopInset (0.1 m) behaviour, unchanged.

        // Build cost (unpainted). Pigment is NOT a build ingredient — it is
        // consumed at paint time, one pigment per filled color slot, on the PLACED sign.
        public const int WoodCost = 2;

        // ── Thin-frame border geometry (§A2.6 Option A, ratified Daniel 2026-06-09) ──
        // The border is REAL frame geometry (a rim with a hole), built from four thin
        // bars around the board's face silhouette — NOT a scaled copy of the board mesh
        // (which rendered a second board). All three are PARAMETRIC and auto-scale to the
        // measured board; exact values are v0.2+ visual polish, tunable in playtest.

        // Visible rim band width as a fraction of the board's SMALLER face dimension
        // (height ~0.55 m → ~5.5 cm rim). Auto-scales if the board art changes.
        private const float FrameRimWidthFrac = 0.10f;

        // Absolute floor (metres) on the rim band so it stays visible as a frame at
        // readable distance even on a small board (AC5 — not a hairline).
        private const float FrameRimWidthMin = 0.03f;

        // Cap on the rim band as a fraction of the smaller face dimension. Guarantees a
        // central HOLE always remains (2·rim < smaller face) so the rim can structurally
        // NEVER become a second board (AC1) and never rings inward over the text (AC4).
        private const float FrameRimMaxHalfFrac = 0.40f;

        // Bar depth (thickness on the board's depth axis) as a multiple of the board's
        // own thickness. >1 so the rim sits PROUD of both plank faces — enough to depth-sort
        // cleanly IN FRONT of the board face in the edge band (no front/back z-fight), but
        // small enough to stay hugging the board's depth envelope and NOT reach the text
        // plane (AC4). RAISED 1.06→1.12 (card t_153ca109, Daniel playtest v0.2.10): at 1.06
        // the bars stood only ~2.7 mm proud per face on the ~0.089 m plank — too shallow to
        // depth-sort against the board face at distance/grazing angles, so the front/back
        // shimmered. 1.12 ≈ 5.3 mm proud per face: clearly proud, still well short of the
        // text plane. (The DOMINANT outer-edge fight is the coplanar OUTER SILHOUETTE, fixed
        // separately by FrameOuterInset below — depth alone never cured that.) Frame
        // visibility at distance (AC5) comes from the rim WIDTH + distinct color, not this
        // depth, so it stays subtle. Tunable v0.2+ polish.
        private const float FrameDepthFactor = 1.12f;

        // Outer-edge inset (WORLD metres): pull the frame's outer silhouette this far
        // INSIDE the board's silhouette so the four bars' OUTER faces are NOT coplanar with
        // the plank's perimeter side faces. Coplanar outer faces are the root cause of the
        // "borders z-fight on the outer edges" symptom (Daniel playtest v0.2.10, card
        // t_153ca109): two surfaces sharing a plane shimmer at distance/grazing angles, and
        // FrameDepthFactor (a depth-axis change) can NEVER separate two faces that are
        // coplanar in the FACE plane. A few mm of inset removes the shared plane outright,
        // leaving only a hair of bare-board lip — spec §A2.6: "outer edge ≈ flush; a hair is
        // fine." Being strictly INSIDE the silhouette also hardens AC1 (a hollow ring inset
        // from the edge can never read as a second board). Tunable v0.2+ polish.
        private const float FrameOuterInset = 0.004f;

        // ZDO field storing the LEGACY single applied color ("" = unpainted). Retained
        // for one-way migration only: a sign painted under the old single-color model
        // (SBPR_SignColor) is read once on spawn and folded into SBPR_SignTextColor.
        public const string ZdoColor = "SBPR_SignColor";

        // Three-tone ZDO fields. Letters / board plank / border frame — each an
        // INDEPENDENT colour (§A2.6, three-slot model Daniel 2026-06-21). "" = that slot
        // unset. Owner-write via ZNetView (mirrors CairnTag).
        //   ZdoTextColor   → TMP letters only
        //   ZdoBoardColor  → board plank mesh only        (NEW — was coupled to text/border)
        //   ZdoBorderColor → border frame bars only
        public const string ZdoTextColor   = "SBPR_SignTextColor";
        public const string ZdoBoardColor  = "SBPR_SignBoardColor";
        public const string ZdoBorderColor = "SBPR_SignBorderColor";

        // Name of the kitbashed decorative pole child. TintRenderers skips renderers
        // under this subtree so painting tints the BOARD only, not the post.
        private const string PostChildName = "SBPR_SignPost";

        // Name of the kitbashed two-tone BORDER child (§A2.6). A thin frame (rim with a
        // hole) around the board, tinted independently of the board so the sign reads
        // two-tone. The vanilla sign mesh has no separable frame renderer, so we add this
        // element — four thin bars built from the board's own mesh scaled into edge bands
        // (clean-room: no new authored geometry). See KitbashBorderElement.
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
            if (!Assets.TryClonePrefab(SourceSign, SignName, out var clone))
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

            // Kitbash a thin two-tone FRAME element around the board (§A2.6 Option A,
            // ratified Daniel 2026-06-09). The vanilla sign mesh is a single plank with no
            // separable frame, so we add one: a real frame (a rim WITH A HOLE) built from
            // four thin bars around the board's face silhouette — NOT a scaled board copy
            // (which rendered a second board). Tinted independently of the board for the
            // text/border two-tone. Baked into the prefab like the pole.
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

            // Measure the board's current foot AND top in sign-local space. The sign
            // mesh + collider live in children; the pole is NOT yet parented here (it is
            // cloned under the inactive holder below), so this sees the BOARD only.
            float boardFootY = Assets.MeasureLocalFootY(signRoot);
            float boardTopY  = Assets.MeasureLocalTopY(signRoot);

            // Clone + strip the pole FIRST so we can measure its real crown and anchor
            // the board to the TOP of the post (not a magic height). TryClonePrefab parents
            // the clone under the inactive holder, so it does NOT pollute the board
            // measurement above and is NOT yet a child of signRoot (the board-lift loop
            // below must touch board children only).
            if (!Assets.TryClonePrefab(SourcePole, PostChildName, out var pole))
            {
                // Error path: no post to anchor to. Fall back to the fixed readable
                // height so the board is still legible (sign placed without a post).
                float fallbackLift = BoardBottomHeight - boardFootY;
                if (fallbackLift > 0f)
                {
                    foreach (Transform child in rootT)
                    {
                        var lp = child.localPosition;
                        child.localPosition = new Vector3(lp.x, lp.y + fallbackLift, lp.z);
                    }
                }
                Plugin.Log.LogWarning(
                    $"[Trailborne/M1] Source pole '{SourcePole}' missing; sign placed without a post " +
                    $"(board lifted {fallbackLift:F2}m to fallback height).");
                return;
            }
            Assets.StripToDecorative(pole);
            pole.name = PostChildName;

            // Measure the pole's own foot + crown (pivot-robust: wood_pole2 is a CENTRE-
            // pivot mesh, so its foot is at local y=-1 and crown at +1, NOT base-pivot).
            // The pole is planted below at localPos.y = -poleFootY, so in the sign's root
            // space its foot lands at 0 (flush) and its crown rises to (-poleFootY +
            // poleTopY). For wood_pole2 that is -(-1) + 1 = 2.0m.
            float poleFootY = Assets.MeasureLocalFootY(pole);
            float poleTopY  = Assets.MeasureLocalTopY(pole);
            float plantedPoleCrownY = -poleFootY + poleTopY;

            // Anchor the board's TOP just under the planted crown so the board reads as
            // mounted at the top of the post. lift = where the board top should go minus
            // where it currently is. This is what fixes "board floats mid-post": the old
            // code lifted the board FOOT to a fixed 1.2m (60% up a 2m post = dead centre).
            // Math shared with the markers via SignGeometry.CrownAnchorLift (floors at 0 so
            // we never push the board below where it already sits) — behaviour identical to
            // the prior inline `targetBoardTopY - boardTopY` clamp.
            float targetBoardTopY = plantedPoleCrownY - SignGeometry.BoardTopInset;
            float lift = SignGeometry.CrownAnchorLift(boardTopY, plantedPoleCrownY);

            // ── Lateral standoff (spec t_9c4a776b §5) ───────────────────────────────
            // Slide the board GROUP sideways onto the post's SIDE face so the board
            // mounts against the post instead of sitting embedded in its centreline.
            // Everything here is DERIVED/MEASURED at runtime — no hardcoded axis, no
            // magic thickness (only the sub-mm kiss nudge KissEpsilon is a literal).
            //
            // Frames: the board children are already parented under rootT, so their
            // extents come out directly in rootT-local space. The pole is planted just
            // below with localRotation=identity, localScale=one and X/Z=0 (see the
            // poleT.localPosition assignment), so its lateral (X/Z) extent in pole-local
            // space maps 1:1 onto rootT-local space — the same frame assumption the
            // foot/crown anchoring above already relies on. Thicknesses use the 8-corner
            // transformed-bounds method (Assets.MeasureLocalExtent): the plank AND
            // wood_pole2 both reuse a 1×1×1 unit-cube Cube_Cube_Material mesh, so raw
            // sharedMesh.bounds.size ≈ (1,1,1) is useless — only the TRS-transformed
            // corners reveal the real thickness.

            // (1) Normal axis = the board's THINNEST horizontal extent (X vs Z). The
            //     plank is the only MeshFilter under the board (collider + Canvas carry
            //     none, and the pole is not parented yet), so this measures the plank.
            Assets.MeasureLocalExtent(signRoot, 0, out float boardMinX, out float boardMaxX);
            Assets.MeasureLocalExtent(signRoot, 2, out float boardMinZ, out float boardMaxZ);
            float boardExtentX = boardMaxX - boardMinX;
            float boardExtentZ = boardMaxZ - boardMinZ;
            int normalAxis = (boardExtentX <= boardExtentZ) ? 0 : 2;
            float boardMin_n = (normalAxis == 0) ? boardMinX : boardMinZ;
            float boardMax_n = (normalAxis == 0) ? boardMaxX : boardMaxZ;
            float boardThickness = boardMax_n - boardMin_n;
            float boardCenter_n  = 0.5f * (boardMin_n + boardMax_n);

            // (2) Outward direction along that axis = sign of the board face's forward
            //     (the text reads outward). Use the serialized Canvas/Text child
            //     transform — NOT Sign.m_textWidget, which is null until Sign.Awake runs
            //     (not on the prefab template at register time). Convert the world-space
            //     forward into rootT-local space and read its component on normalAxis.
            Transform? faceT = null;
            foreach (var tmp in rootT.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (tmp != null) { faceT = tmp.transform; break; }
            }
            if (faceT == null)
            {
                foreach (var cv in rootT.GetComponentsInChildren<Canvas>(true))
                {
                    if (cv != null) { faceT = cv.transform; break; }
                }
            }

            int dir = 1; // default: +normalAxis (a 180° flip is cosmetic, caught in playtest)
            if (faceT != null)
            {
                Vector3 fLocal = rootT.InverseTransformDirection(faceT.forward);
                float facing_n = fLocal[normalAxis];
                int otherAxis  = (normalAxis == 0) ? 2 : 0;
                // Cross-check (spec OPEN-2): the face SHOULD point dominantly along the
                // thinnest-extent axis. If it instead points more along the OTHER
                // horizontal axis (or is ~0 on our axis), trust the extent for the AXIS
                // but the face for the SIGN, and warn.
                if (Mathf.Abs(facing_n) < Mathf.Abs(fLocal[otherAxis]) || Mathf.Abs(facing_n) < 1e-4f)
                {
                    Plugin.Log.LogWarning(
                        $"[Trailborne/M1] {SignName}: board face normal {fLocal} not dominant on the " +
                        $"thinnest-extent axis {normalAxis}; trusting extent for axis, face for sign.");
                }
                if (facing_n < 0f) dir = -1;
            }
            else
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/M1] {SignName}: no Canvas/TMP face to derive outward direction; " +
                    $"defaulting board to +axis {normalAxis} (a 180° flip is cosmetic, caught in playtest).");
            }

            // (3) Post thickness along the SAME axis, from the planted pole geometry.
            Assets.MeasureLocalExtent(pole, normalAxis, out float postMin_n, out float postMax_n);
            float postThickness = postMax_n - postMin_n;
            float postCenter_n  = 0.5f * (postMin_n + postMax_n); // ≈0 (pole at X/Z=0), but measured

            // (4) Move the board centre out to the post's near side face + a sub-mm kiss
            //     gap so the back face and the post side face don't z-fight. Centroid
            //     difference (not a bare offset) handles a non-centred donor board/post.
            //     Magnitude shared with the markers via SignGeometry.LateralStandoff
            //     (½post + ½board + KissEpsilon) — behaviour identical to the prior inline.
            float standoff = SignGeometry.LateralStandoff(postThickness, boardThickness);
            float targetBoardCenter_n = postCenter_n + dir * standoff;
            float lateralDelta_n = targetBoardCenter_n - boardCenter_n;
            Vector3 lateralDelta = Vector3.zero;
            lateralDelta[normalAxis] = lateralDelta_n;

            // Apply the Y-lift (crown anchoring) AND the lateral standoff to the board
            // GROUP in ONE pass, so {collider, Canvas, New(plank)} move together as a
            // single group (no split) and the existing crown-height behaviour is
            // preserved (no regress of t_05bb5168). Do NOT gate the lateral offset on
            // lift>0 — it must run even when the board is already at crown height. The
            // pole is parented + planted at X/Z=0 AFTER this loop, so it is never moved.
            if (lift > 0f || lateralDelta != Vector3.zero)
            {
                foreach (Transform child in rootT)
                {
                    var lp = child.localPosition;
                    child.localPosition = lp + lateralDelta + new Vector3(0f, lift, 0f);
                }
            }

            // ── Board 180° facing flip (card t_153ca109, Daniel playtest v0.2.10) ──────
            // Daniel, in-game: "sign board is facing the wrong way (needs a 180 flip)."
            //
            // Root cause. The standoff block above only TRANSLATES the board onto the post's
            // side face — it never ROTATES it — and it DERIVES that side from faceT.forward,
            // trusting faceT.forward == the board's readable outward normal. The shipped log
            // (axis=Z dir=- → board centre 0.000→-0.251m) shows the board placed at Z=-0.251
            // with the post at Z=0. The defect is that the donor's readable normal is the
            // OPPOSITE of faceT.forward, so the text face actually reads +Z — straight INTO
            // the post — and a player at the natural front sees the BACK. (#58 verified LIVE
            // that the board kisses the side and is not embedded, so position/side is right;
            // only facing is wrong. This is the card's case (b).)
            //
            // Why (b) and not (a) "wrong side": the post is a free-standing, symmetric pole
            // (wood_pole2 at X/Z=0). "Board on the other side, still reading outward" is the
            // mirror image of the correct sign and is visually identical — it would NOT read
            // as "facing wrong way." The only visible defect is the readable face pointing
            // TOWARD the post, i.e. (b).
            //
            // Fix = rotate the board GROUP 180° about its OWN vertical centroid (NOT the post
            // axis). Both rotations flip the readable normal, but the pivot decides which side
            // the board lands on: rotating about the post axis would shuttle the board to the
            // far side where the corrected normal STILL points at the post (post occludes the
            // read); rotating about the board's own centroid keeps it on the side the standoff
            // already chose and flips the normal to point AWAY from the post — clean read,
            // post behind the board. The board's normal-axis extent is symmetric about its
            // centroid, so the post-kiss face lands in exactly the same place (#58 preserved);
            // a Y-axis rotation never touches Y, so crown height is preserved (t_05bb5168).
            // Placement yaw spins board+post rigidly together, so this register-time flip
            // holds at every placement yaw. The pole is parented at X/Z=0 AFTER this loop and
            // the border is built AFTER this method, so both are untouched here and the border
            // inherits the corrected facing automatically.
            //
            // Centroid: normalAxis component = the post-translation board centre
            // (targetBoardCenter_n); otherAxis component = the measured board midpoint on the
            // other horizontal axis (the lateral translation never moved that axis, so the
            // pre-translation extent midpoint is still valid). Rotating about this line maps
            // the board centroid onto itself → facing flips, position is preserved.
            int flipOtherAxis = (normalAxis == 0) ? 2 : 0;
            float boardMin_o = (flipOtherAxis == 0) ? boardMinX : boardMinZ;
            float boardMax_o = (flipOtherAxis == 0) ? boardMaxX : boardMaxZ;
            float boardCenter_o = 0.5f * (boardMin_o + boardMax_o);
            foreach (Transform child in rootT)
            {
                var lp = child.localPosition;
                lp[normalAxis]      = 2f * targetBoardCenter_n - lp[normalAxis];
                lp[flipOtherAxis]   = 2f * boardCenter_o       - lp[flipOtherAxis];
                child.localPosition = lp; // Y unchanged → crown height preserved
                child.localRotation = Quaternion.AngleAxis(180f, Vector3.up) * child.localRotation;
            }

            // Plant the pole under the sign root at ground level (foot → y=0).
            var poleT = pole.transform;
            poleT.SetParent(rootT, worldPositionStays: false);
            poleT.localRotation = Quaternion.identity;
            poleT.localScale    = Vector3.one;
            // Offset so the measured foot lands at the sign pivot (local y=0). Measuring
            // the pole's own foot rather than ASSUMING its pivot is at its base keeps this
            // correct for a centre-pivot pole (poleFootY=-1 → localPos.y=+1 → foot at 0)
            // OR a base-pivot pole (poleFootY=0 → localPos.y=0 → foot at 0) — pivot-robust.
            poleT.localPosition = new Vector3(0f, -poleFootY, 0f);
            pole.SetActive(true);

            // Restore a thin ground-contact collider at the planted post FOOT so the sign
            // seats flush instead of ~3/4 buried (t_4ad60d6f / parent t_1dc88742).
            // StripToDecorative removed the pole's own collider (so it never intercepts the
            // E-to-write raycast), which left the board's interact collider — lifted ~1.5m
            // to the crown — as the lowest/only collider; the placement seat drove THAT to
            // the ground and buried the 2m post. This adds the foot back as the lowest
            // collider plane. It is NEUTRALISED on the placed instance by SignTag.Awake so
            // the BOARD stays the sole interact/paint target (regression guard AT-4). Now
            // the SHARED Runtime/SignGeometry helper (card t_cc093d04), called with the
            // sign's default foot-collider child name "SBPR_SignPostFoot".
            SignGeometry.AddPostFootGroundCollider(rootT, pole);

            Plugin.Log.LogInfo(
                $"[Trailborne/M1] Kitbashed pole under {SignName}: post foot→y=0, crown→{plantedPoleCrownY:F2}m, " +
                $"board top anchored to {targetBoardTopY:F2}m (lifted {lift:F2}m). " +
                $"Lateral standoff: axis={(normalAxis == 0 ? "X" : "Z")} dir={(dir < 0 ? "-" : "+")}, " +
                $"post={postThickness:F3}m board={boardThickness:F3}m → board centre {boardCenter_n:F3}→{targetBoardCenter_n:F3}m " +
                $"(Δ{lateralDelta_n:F3}m, kiss ε={SignGeometry.KissEpsilon:F3}m).");
        }

        // AddPostFootGroundCollider moved VERBATIM to Runtime/SignGeometry.cs (card
        // t_cc093d04) so the Painted Sign and the four Marker Signs share ONE correct
        // implementation of the #68 flush-seat fix. Behaviour is unchanged; the only
        // generalisation is a foot-collider-child-name parameter (the sign keeps the
        // default "SBPR_SignPostFoot"). See SignGeometry.AddPostFootGroundCollider.

        /// <summary>
        /// Kitbash a TRUE THIN FRAME border element for the sign (§A2.6, Option A,
        /// ratified Daniel 2026-06-09). The vanilla sign mesh is a single wood plank with
        /// no separable frame renderer, so we ADD one we can tint independently of the
        /// board. The frame is REAL frame geometry — a rim WITH A HOLE — not a scaled
        /// copy of the board plank (the discarded mesh-reuse construction rendered a full
        /// SECOND BOARD poking ~5% past every edge; see the root-cause note below):
        ///
        ///   • Find the BOARD mesh — the largest <see cref="MeshFilter"/> on the sign
        ///     that is NOT part of the decorative pole (named <see cref="PostChildName"/>)
        ///     and not part of an existing border.
        ///   • Measure the plank's REAL face dimensions from its TRANSFORMED extents
        ///     (<see cref="Assets.MeasureLocalExtent(GameObject,Transform,int,out float,out float)"/>
        ///     in the sign-root frame), NOT raw <c>sharedMesh.bounds</c>. The vanilla sign
        ///     plank reuses a 1×1×1 unit-cube <c>Cube_Cube_Material</c> mesh scaled by its
        ///     transform (vprefab-verified), so raw bounds ≈ (1,1,1) carry no real size —
        ///     only the transformed corners reveal the true ~1.0 × 0.55 × 0.089 m plank.
        ///     The smallest transformed axis is the depth (thickness); the other two span
        ///     the face plane the rim rings.
        ///   • Build a <c>SBPR_SignBorder</c> root parented under the board transform (so
        ///     it inherits the board's orientation + placement automatically — no
        ///     front/back detection needed) and lay FOUR thin bars (top / bottom / left /
        ///     right) around the board's face silhouette. Each bar REUSES the board's own
        ///     unit-cube <c>sharedMesh</c> scaled into a thin edge band (clean-room — no
        ///     authored geometry, headless-safe, no collider). The bars ring the central
        ///     text area and leave it open — the board shows through the middle, so the
        ///     frame can NEVER regress to a second board.
        ///   • Each bar gets its OWN material instance copied from the board material, so
        ///     an unpainted sign reads as plain wood, and the frame is tinted
        ///     independently of the board via the <see cref="BorderChildName"/> subtree
        ///     name-match (<see cref="IsUnderBorder"/> → <see cref="TintBorder"/>).
        ///
        /// Outer silhouette is flush with the plank (no overhang); the rim is slightly
        /// proud on the depth axis so it reads as a raised frame at distance. Exact rim
        /// width / depth are v0.2+ polish (parametric, auto-scaling to the real board). If
        /// the board mesh can't be found we log and skip — the sign still works
        /// single-tone (TintBorder no-ops on the missing element).
        ///
        /// Root-cause note (why the old construction drifted): it read the depth axis from
        /// raw <c>sharedMesh.bounds</c> of the unit-cube plank (≈ (1,1,1), meaningless), so
        /// the "depth shrink" landed on an arbitrary FACE axis and the scaled board copy
        /// grew forward past the text plane → second board. The procedural rim eliminates
        /// this failure class structurally: a hollow ring has no solid center to become a
        /// second board even if axis detection slipped. Public UnityEngine API only.
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
            var boardT    = boardMf.transform;
            var boardMesh = boardMf.sharedMesh;

            // REAL board dimensions (metres) from TRANSFORMED extents in the sign-root
            // frame — NEVER raw sharedMesh.bounds. The plank reuses a 1×1×1 unit-cube
            // mesh scaled by its transform (vprefab: Cube_Cube_Material), so raw bounds
            // ≈ (1,1,1) carry no real size; only the transformed corners reveal the true
            // ~1.0 × 0.55 × 0.089 m plank. The board is axis-aligned with the sign root at
            // register time (unrotated template — the standoff feature relies on the same
            // 1:1 board-local↔root axis mapping), so a root-frame extent along axis i is
            // also the board-LOCAL length of axis i, which is what the bars (built in
            // board-local space below) need.
            Assets.MeasureLocalExtent(boardMf.gameObject, signRoot.transform, 0, out float rMinX, out float rMaxX);
            Assets.MeasureLocalExtent(boardMf.gameObject, signRoot.transform, 1, out float rMinY, out float rMaxY);
            Assets.MeasureLocalExtent(boardMf.gameObject, signRoot.transform, 2, out float rMinZ, out float rMaxZ);
            float[] worldDim = { rMaxX - rMinX, rMaxY - rMinY, rMaxZ - rMinZ };

            // Cheap sanity guard: the per-board-local-axis world length is |lossyScale|×
            // meshSize. If it disagrees with the root-frame extent the board is rotated
            // relative to the root and the axis mapping below would be off — log, don't
            // abort (the frame still renders; a 90° rim swap is a cosmetic playtest catch).
            Vector3 ls = boardT.lossyScale;
            Vector3 mSizeV = boardMesh.bounds.size;
            float[] localWorld = { Mathf.Abs(ls.x) * mSizeV.x, Mathf.Abs(ls.y) * mSizeV.y, Mathf.Abs(ls.z) * mSizeV.z };
            for (int i = 0; i < 3; i++)
            {
                float a = worldDim[i], b = localWorld[i];
                if (Mathf.Abs(a - b) > 0.05f * Mathf.Max(a, b, 1e-4f))
                {
                    Plugin.Log.LogWarning(
                        $"[Trailborne/M1] {SignName}: board axis {i} root-extent {a:F3}m vs " +
                        $"local-extent {b:F3}m disagree — board may be rotated vs root; frame rim " +
                        "may land on the wrong axis (cosmetic, caught in playtest).");
                    break;
                }
            }

            // Depth axis = the smallest real extent (the plank's thickness); the two
            // largest span the face plane the rim rings.
            int depthAxis = 0;
            for (int i = 1; i < 3; i++) if (worldDim[i] < worldDim[depthAxis]) depthAxis = i;
            int faceA = (depthAxis + 1) % 3;
            int faceB = (depthAxis + 2) % 3;

            // Board mesh LOCAL geometry (the build frame). For the unit-cube plank this is
            // center (0,0,0), size (1,1,1) → the plank occupies [-0.5, 0.5] on each axis in
            // board-local space. Read from the mesh so it generalizes if the art changes.
            Vector3 mc = boardMesh.bounds.center;
            Vector3 ms = boardMesh.bounds.size;
            float[] meshCtr  = { mc.x, mc.y, mc.z };
            float[] meshSize = { ms.x, ms.y, ms.z };

            // Local→world scale of each board-local axis = worldDim / meshSize (so a local
            // length L on axis i is L·scale[i] metres in world). Used to make the rim band
            // the SAME world width on all four sides despite the board's anisotropic scale.
            float scaleA = Mathf.Max(worldDim[faceA] / Mathf.Max(meshSize[faceA], 1e-6f), 1e-6f);
            float scaleB = Mathf.Max(worldDim[faceB] / Mathf.Max(meshSize[faceB], 1e-6f), 1e-6f);

            // Rim band width (WORLD, uniform on all four sides): ~10% of the smaller face
            // dimension so it auto-scales to the real board; floored so it stays visible at
            // distance (AC5) and capped so a central HOLE always remains (AC1/AC4) — a
            // hollow ring can structurally never become a second board.
            float smallerFace = Mathf.Min(worldDim[faceA], worldDim[faceB]);
            float rimWorld = Mathf.Clamp(
                FrameRimWidthFrac * smallerFace,
                FrameRimWidthMin,
                FrameRimMaxHalfFrac * smallerFace);

            float rimLocalA = rimWorld / scaleA; // left/right bar thickness (board-local, faceA)
            float rimLocalB = rimWorld / scaleB; // top/bottom bar thickness (board-local, faceB)

            float halfA = meshSize[faceA] * 0.5f, cA = meshCtr[faceA];
            float halfB = meshSize[faceB] * 0.5f, cB = meshCtr[faceB];
            float depthLocal = meshSize[depthAxis] * FrameDepthFactor; // thin slab, proud both faces
            float cD = meshCtr[depthAxis];

            // Frame OUTER half-extents = board half-extents pulled IN by FrameOuterInset, so
            // no bar face is coplanar with the board's perimeter side faces (the "outer
            // edges z-fight", card t_153ca109). Convert the world inset to board-local via
            // each axis' scale. Clamp the inset to a quarter of the half-extent so a
            // degenerate (tiny) board can never invert the frame — the outer silhouette
            // stays a positive majority of the board silhouette.
            float insetLocalA = Mathf.Min(FrameOuterInset / scaleA, halfA * 0.25f);
            float insetLocalB = Mathf.Min(FrameOuterInset / scaleB, halfB * 0.25f);
            float frameHalfA = halfA - insetLocalA;
            float frameHalfB = halfB - insetLocalB;

            // One border ROOT parented under the board transform → inherits the board's
            // orientation + placement automatically (no front/back detection needed). The
            // four bars live UNDER it, so the BorderChildName name-match routes TintBorder
            // to all of them and TintBoard skips them (two-tone preserved unchanged).
            var border = new GameObject(BorderChildName);
            border.transform.SetParent(boardT, worldPositionStays: false);
            border.transform.localRotation = Quaternion.identity;
            border.transform.localPosition = Vector3.zero;
            border.transform.localScale    = Vector3.one;

            int barCount = 0;

            // Local helper: a thin wood bar = the board's OWN unit-cube mesh scaled into an
            // edge band (clean-room — no authored geometry, headless-safe, and no collider
            // to strip), with its OWN material instance copied from the board (so an
            // unpainted frame reads as plain wood and tints independently of the board).
            void AddBar(string suffix, float sFaceA, float sFaceB, float pFaceA, float pFaceB)
            {
                var bar = new GameObject($"{BorderChildName}_{suffix}");
                bar.transform.SetParent(border.transform, worldPositionStays: false);
                bar.transform.localRotation = Quaternion.identity;

                var sc = new Vector3();
                sc[faceA]     = sFaceA;
                sc[faceB]     = sFaceB;
                sc[depthAxis] = depthLocal;
                bar.transform.localScale = sc;

                var po = new Vector3();
                po[faceA]     = pFaceA;
                po[faceB]     = pFaceB;
                po[depthAxis] = cD;
                bar.transform.localPosition = po;

                var bmf = bar.AddComponent<MeshFilter>();
                bmf.sharedMesh = boardMesh;
                var brend = bar.AddComponent<MeshRenderer>();
                if (boardMat != null) brend.sharedMaterial = new Material(boardMat);

                bar.SetActive(true);
                barCount++;
            }

            // Top + bottom bars span the FULL (inset) face width; their OUTER edge is pulled
            // in to cB ± frameHalfB (FrameOuterInset inside the board silhouette) so the bar
            // faces are NOT coplanar with the plank perimeter — kills the outer-edge z-fight.
            AddBar("T", 2f * frameHalfA, rimLocalB, cA, cB + frameHalfB - rimLocalB * 0.5f);
            AddBar("B", 2f * frameHalfA, rimLocalB, cA, cB - frameHalfB + rimLocalB * 0.5f);

            // Left + right bars span only the INNER height (between the inset top/bottom bars)
            // so corners butt cleanly instead of double-stacking, and their outer edge is the
            // same inset cA ± frameHalfA. innerB = 2·frameHalfB − 2·rimLocalB, floored at 0 so
            // a degenerate (tiny) board can't invert it (the rim cap already keeps a real
            // board's hole open).
            float innerB = Mathf.Max(2f * frameHalfB - 2f * rimLocalB, 0f);
            AddBar("L", rimLocalA, innerB, cA - frameHalfA + rimLocalA * 0.5f, cB);
            AddBar("R", rimLocalA, innerB, cA + frameHalfA - rimLocalA * 0.5f, cB);

            border.SetActive(true);
            Plugin.Log.LogInfo(
                $"[Trailborne/M1] Kitbashed thin-frame border '{BorderChildName}' on {SignName} " +
                $"({barCount} bars, depthAxis={depthAxis}, face={worldDim[faceA]:F3}×{worldDim[faceB]:F3}m, " +
                $"rim={rimWorld * 100f:F1}cm, depth×{FrameDepthFactor:F2}, outerInset={FrameOuterInset * 100f:F2}cm).");
        }

        /// <summary>
        /// Tint the sign BOARD (the painted plank) to <paramref name="c"/> by writing
        /// <c>_Color</c> into each board renderer's per-renderer MaterialPropertyBlock — the
        /// render-time layer vanilla itself paints these pieces through (see
        /// <see cref="TintRenderer"/> for the full mechanism + why shared-material writes were
        /// masked). No-op on a headless server (no renderers). SKIPS renderers under the
        /// decorative pole (<see cref="PostChildName"/>) AND under the two-tone border
        /// (<see cref="BorderChildName"/>) so this colours the board only. Driven by
        /// SignTag from the BORDER slot tone (Daniel 2026-06-21 re-wire — the board plank
        /// rides the border colour, NOT the text colour).
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
        /// TMP's two colour paths. NOTE: setting these here is NOT enough on its own — the
        /// vanilla <c>Sign.UpdateText</c> ~2 Hz poll reconstructs/rewrites <c>m_textWidget</c>
        /// AFTER our paint-time apply and CAN drop the letter colour (observed in-game,
        /// bug t_f8eff6d0). The <c>SignTextRetintPatch</c> postfix re-pins via
        /// <c>SignTag.ReapplyTextTint</c> on the poll's cadence to keep the letters coloured.
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
            foreach (var rend in go.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (rend == null) continue;
                if (!IsUnderBorder(rend.transform)) continue; // border subtree only
                TintRenderer(rend, c);
            }
        }

        /// <summary>
        /// Revert the BOARD (and text-tinted plank renderers) to their unpainted
        /// appearance — the "remove text color" / None affordance (§A2.6, Issue 4). Drops
        /// the per-renderer <c>_Color</c> MPB override so the board falls back to its
        /// material's own colour, visible LIVE (not only after a reload). Skips the pole +
        /// border subtrees, mirroring <see cref="TintBoard"/>'s selection.
        /// </summary>
        public static void RestoreBoard(GameObject go)
        {
            foreach (var rend in go.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (rend == null) continue;
                if (IsUnderPost(rend.transform)) continue;
                if (IsUnderBorder(rend.transform)) continue; // border reverts separately
                RestoreRenderer(rend);
            }
        }

        /// <summary>
        /// Revert ONLY the kitbashed border element to its unpainted appearance — the
        /// "remove border color" / None affordance (§A2.6, Issue 4). Drops the per-renderer
        /// <c>_Color</c> MPB override on the border bars so they revert LIVE.
        /// </summary>
        public static void RestoreBorder(GameObject go)
        {
            foreach (var rend in go.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (rend == null) continue;
                if (!IsUnderBorder(rend.transform)) continue;
                RestoreRenderer(rend);
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
            foreach (var rend in go.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                if (rend == null) continue;
                // Skip the decorative kitbash pole — board only (Daniel 2026-06-05).
                if (IsUnderPost(rend.transform)) continue;
                // Skip the border subtree unless explicitly included (it has its own tone).
                if (!includeBorder && IsUnderBorder(rend.transform)) continue;
                TintRenderer(rend, c);
            }
        }

        // Cached "_Color" shader id — the lit-shader tint slot vanilla itself paints
        // these exact pieces through (WearNTear.Highlight's support overlay), so we know
        // the material consumes it.
        private static readonly int s_ColorId = Shader.PropertyToID("_Color");

        // Reused scratch block so per-renderer tint does NOT allocate a MaterialPropertyBlock
        // per call (and per re-assert frame). GetPropertyBlock overwrites its contents with
        // the renderer's CURRENT overrides each time, so reuse is safe + read-modify-write.
        private static readonly MaterialPropertyBlock s_mpb = new MaterialPropertyBlock();

        /// <summary>
        /// Tint ONE renderer's <c>_Color</c> via a per-renderer
        /// <see cref="MaterialPropertyBlock"/> (MPB) — the SAME render-time layer vanilla
        /// uses to paint build-piece colour (bug t_f3310406 / parent t_24ad2570 diagnosis).
        ///
        /// Why MPB and NOT <c>sharedMaterials.SetColor</c> (the old mechanism): every placed
        /// sign carries a <c>WearNTear</c> (kitbashed off the vanilla <c>sign</c>), and
        /// Valheim paints piece colour through an MPB managed by <c>MaterialMan</c> /
        /// <c>WearNTear.Highlight</c>. An MPB <b>overrides</b> the material's own <c>_Color</c>
        /// at render time, so the old shared-material write landed on a layer the MPB sits in
        /// front of → the board/border never visibly changed (only the TMP text, which is a
        /// Canvas renderer outside MaterialMan, worked). Writing OUR colour into the renderer's
        /// own MPB puts it on the winning layer.
        ///
        /// Per-RENDERER (not per-<c>GameObject</c> via <c>MaterialMan.SetValue</c>) is
        /// deliberate: <c>MaterialMan</c> registers ALL child renderers of a <c>go</c> under one
        /// shared block, so a per-<c>go</c> call could not give the board and the (child-of-board)
        /// border independent tones — the two-tone requirement (§A2.6). Driving the MPB directly
        /// per renderer sidesteps that granularity and keeps board vs border disjoint.
        ///
        /// Read-modify-write (<c>GetPropertyBlock</c> first) so we PRESERVE any other property
        /// MaterialMan may have pushed onto this renderer (e.g. <c>_TakingAshlandsDamage</c>) —
        /// we only add/replace <c>_Color</c>. No-op on a headless server (no renderers).
        /// </summary>
        private static void TintRenderer(Renderer rend, Color c)
        {
            try
            {
                rend.GetPropertyBlock(s_mpb);
                s_mpb.SetColor(s_ColorId, c);
                rend.SetPropertyBlock(s_mpb);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/M1] Sign tint failed on {rend.gameObject.name}: {e.Message}");
            }
        }

        /// <summary>
        /// Revert ONE renderer to its unpainted appearance — the <c>∅ None</c> affordance
        /// (§A2.6, Issue 4). With the MPB mechanism there is no cloned material to restore;
        /// "unpainted" is simply "no <c>_Color</c> override," i.e. the renderer falling through
        /// to its material's own <c>_Color</c>. We reproduce that by writing the material's base
        /// <c>_Color</c> back into the MPB (rather than clearing the whole block, which would
        /// also drop any MaterialMan-owned property). Idempotent + headless-safe.
        /// </summary>
        private static void RestoreRenderer(Renderer rend)
        {
            try
            {
                var mat = rend.sharedMaterial;
                Color baseColor = (mat != null && mat.HasProperty(s_ColorId))
                    ? mat.GetColor(s_ColorId)
                    : Color.white;
                rend.GetPropertyBlock(s_mpb);
                s_mpb.SetColor(s_ColorId, baseColor);
                rend.SetPropertyBlock(s_mpb);
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
