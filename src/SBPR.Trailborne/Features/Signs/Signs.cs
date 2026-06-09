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

        // NORMAL-path board placement: lift the sign BOARD so its TOP edge sits this far
        // (metres) BELOW the measured crown of the kitbashed pole, so the board reads as
        // mounted at the TOP of the post (trail-signpost silhouette) instead of floating
        // mid-post. Anchored to Assets.MeasureLocalTopY(pole) — robust to a pole swap or a
        // different pivot (no magic height constant). A small reveal of post above the
        // board is intentional. Exact inset is visual polish (v0.2+); the load-bearing
        // requirement is board-at-top. With wood_pole2 (crown at root-local y=2.0) and the
        // vanilla sign board (height 0.5 m) this lands the board at [1.40, 1.90] m, centre
        // ~1.65 m (player eye height) — readable, post foot flush at y=0.
        private const float BoardTopInset = 0.1f;

        // Sub-millimetre OUTWARD nudge added to the lateral standoff so the board's back
        // face and the post's near side face don't render on the exact same plane (which
        // would z-fight). This is the ONLY permissible literal in the standoff math — the
        // axis, the direction, and the ½post+½board magnitude are all derived/measured at
        // runtime. It is VISUAL POLISH (v0.2+) per the Painted Sign build spec
        // (docs/v0.1.0/planning/requirements.md, "Painted Signs" build bullet ~:473): the
        // exact kiss tolerance is Daniel's in-game call; this default keeps a gap well
        // under a millimetre (no perceptible gap, no interpenetration).
        private const float KissEpsilon = 0.001f;

        // Vertical THICKNESS (metres) of the thin ground-contact collider kitbashed at the
        // decorative post's FOOT so the placed sign seats flush instead of ~3/4 buried
        // (t_4ad60d6f / parent t_1dc88742). This is a SHAPE parameter, NOT a placement
        // height — the collider's BOTTOM plane is DERIVED from the measured planted post
        // foot (see AddPostFootGroundCollider), so no magic Y drives where it sits; this
        // only sets how thin the pad is. Kept tiny so it never pokes visibly above the
        // foot. Analogous to KissEpsilon: the one permissible literal in this geometry.
        private const float PostFootColliderThickness = 0.05f;

        // Floor for the foot collider's horizontal footprint (metres), guarding the
        // degenerate case where the post extent measures ~0 (e.g. a donor swap that breaks
        // MeasureLocalExtent). The footprint X/Z do NOT affect the seat (which keys on the
        // collider's lowest point), so a small believable pad is sufficient.
        private const float PostFootColliderMinFootprint = 0.1f;

        // Name of the kitbashed ground-contact collider child at the post foot. SignTag
        // finds it by its SignPostFootCollider marker (not this name); the name is for
        // log/debug legibility only.
        private const string PostFootColliderName = "SBPR_SignPostFoot";

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

            // Measure the board's current foot AND top in sign-local space. The sign
            // mesh + collider live in children; the pole is NOT yet parented here (it is
            // cloned under the inactive holder below), so this sees the BOARD only.
            float boardFootY = Assets.MeasureLocalFootY(signRoot);
            float boardTopY  = Assets.MeasureLocalTopY(signRoot);

            // Clone + strip the pole FIRST so we can measure its real crown and anchor
            // the board to the TOP of the post (not a magic height). ClonePrefab parents
            // the clone under the inactive holder, so it does NOT pollute the board
            // measurement above and is NOT yet a child of signRoot (the board-lift loop
            // below must touch board children only).
            var pole = Assets.ClonePrefab(SourcePole, PostChildName);
            if (pole == null)
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
            float targetBoardTopY = plantedPoleCrownY - BoardTopInset;
            float lift = targetBoardTopY - boardTopY;
            if (lift < 0f) lift = 0f; // never push the board below where it already sits

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
            float standoff = 0.5f * postThickness + 0.5f * boardThickness + KissEpsilon;
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
            // the BOARD stays the sole interact/paint target (regression guard AT-4).
            AddPostFootGroundCollider(rootT, pole);

            Plugin.Log.LogInfo(
                $"[Trailborne/M1] Kitbashed pole under {SignName}: post foot→y=0, crown→{plantedPoleCrownY:F2}m, " +
                $"board top anchored to {targetBoardTopY:F2}m (lifted {lift:F2}m). " +
                $"Lateral standoff: axis={(normalAxis == 0 ? "X" : "Z")} dir={(dir < 0 ? "-" : "+")}, " +
                $"post={postThickness:F3}m board={boardThickness:F3}m → board centre {boardCenter_n:F3}→{targetBoardCenter_n:F3}m " +
                $"(Δ{lateralDelta_n:F3}m, kiss ε={KissEpsilon:F3}m).");
        }

        /// <summary>
        /// Add a thin, non-trigger ground-contact <see cref="BoxCollider"/> at the planted
        /// decorative post's FOOT so the placed sign seats FLUSH at ground level instead of
        /// burying the 2m post ~3/4 underground (t_4ad60d6f / parent spec t_1dc88742).
        ///
        /// WHY THIS IS NEEDED. Valheim seats a placed piece by driving the AABB of its
        /// LOWEST enabled, non-trigger collider down onto the ground (the build placement
        /// ghost computes the seat from <c>GetComponentsInChildren&lt;Collider&gt;()</c>,
        /// skipping triggers and disabled colliders). <see cref="Assets.StripToDecorative"/>
        /// removed the pole's own collider on purpose — so the post never intercepts the
        /// Sign's E-to-write raycast — which left the BOARD's interact collider, lifted
        /// ~1.5m up to the crown, as the only collider. The seat drove that lifted collider
        /// to the ground, sinking the post foot ~1.5m. Restoring a collider whose bottom is
        /// at the post foot returns the lowest-collider plane to y≈0, so the post seats
        /// flush. Robust across all three plausible seat models (lowest-collider / pivot /
        /// renderer-bounds): it can only ever LOWER the seat reference back to the foot.
        ///
        /// PLACEMENT-vs-PLACED (the load-bearing two-phase detail, verified against the
        /// vanilla build path):
        ///   • In the placement GHOST the collider must be NON-TRIGGER and on a layer the
        ///     build placement ray-mask includes (we use "piece"), or the ghost disables it
        ///     / the seat skips it and the bury persists. So we set it up exactly like a
        ///     normal piece collider here.
        ///   • The seated transform is baked in at placement, so on the PLACED instance the
        ///     collider has already done its job. <see cref="SignTag"/> DISABLES it in Awake
        ///     so it can never steal the Sign's E-to-write / paint raycast — the BOARD stays
        ///     the sole interact/paint target (regression guard AT-4). It also carries no
        ///     WearNTear, so the post is not separately destructible.
        ///
        /// Placement is DERIVED, not magic: the collider's bottom plane is the MEASURED
        /// planted-post foot in sign-root-local space (same 8-corner transformed-bounds
        /// method the pole anchoring uses), and its horizontal footprint is the measured
        /// post thickness. Public UnityEngine API only — clean-room safe.
        /// </summary>
        /// <param name="rootT">The sign-root transform (the collider parents here, so its
        /// extents share the same local frame the pole foot was measured in).</param>
        /// <param name="pole">The planted decorative post, used to measure the foot Y and
        /// the horizontal footprint.</param>
        private static void AddPostFootGroundCollider(Transform rootT, GameObject pole)
        {
            if (rootT == null || pole == null) return;

            // Measure the PLANTED post's foot in SIGN-ROOT-local space. MeasureLocalFootY
            // returns the foot in the pole's OWN local frame (≈ -1 for the centre-pivot
            // wood_pole2), so we transform that foot plane through the planted pole's actual
            // transform into root-local space. Because the pole was planted at
            // localPosition.y = -poleFootY (identity rotation, unit scale), this lands at
            // root-local y ≈ 0 — but it is DERIVED from the measured foot through the real
            // transform (robust to the pole's pivot / rotation / scale), never a magic 0.
            float poleLocalFoot = Assets.MeasureLocalFootY(pole);
            Vector3 footWorld = pole.transform.TransformPoint(new Vector3(0f, poleLocalFoot, 0f));
            float footY = rootT.InverseTransformPoint(footWorld).y;

            // Horizontal footprint = the post's thickness. The pole is planted at X/Z=0 with
            // identity rotation + unit scale, so its pole-local X/Z extents map 1:1 onto
            // root-local X/Z (the same frame assumption the lateral standoff above relies
            // on). Footprint does NOT affect the seat (which keys on the lowest Y only).
            Assets.MeasureLocalExtent(pole, 0, out float postMinX, out float postMaxX);
            Assets.MeasureLocalExtent(pole, 2, out float postMinZ, out float postMaxZ);

            float footprintX = Mathf.Max(postMaxX - postMinX, PostFootColliderMinFootprint);
            float footprintZ = Mathf.Max(postMaxZ - postMinZ, PostFootColliderMinFootprint);
            float centerX = 0.5f * (postMinX + postMaxX); // ≈0 (post planted at X/Z=0), but measured
            float centerZ = 0.5f * (postMinZ + postMaxZ);

            // A child of the sign ROOT (not the pole) so it is unaffected by the pole's own
            // transform and shares the root-local frame the foot was measured in. Identity
            // local rotation/scale so the BoxCollider size maps 1:1 to metres.
            var footObj = new GameObject(PostFootColliderName);
            var footT = footObj.transform;
            footT.SetParent(rootT, worldPositionStays: false);
            footT.localRotation = Quaternion.identity;
            footT.localScale    = Vector3.one;

            // Center the box so its BOTTOM face sits at the measured post foot: the box
            // spans [footY, footY + thickness], i.e. center y = footY + thickness/2. This
            // makes the collider's lowest point exactly the post foot — the value the
            // placement seat keys on — with no magic height.
            footT.localPosition = new Vector3(
                centerX,
                footY + 0.5f * PostFootColliderThickness,
                centerZ);

            var box = footObj.AddComponent<BoxCollider>();
            box.size      = new Vector3(footprintX, PostFootColliderThickness, footprintZ);
            box.center    = Vector3.zero;
            box.isTrigger = false; // MUST be solid: the placement seat skips trigger colliders.

            // Put it on the "piece" layer so the build placement ghost keeps it ENABLED
            // (the ghost disables colliders whose layer is outside the placement ray-mask;
            // "piece" is inside it) and the seat counts it. On the PLACED sign SignTag
            // disables this collider, so the layer no longer matters there.
            int pieceLayer = LayerMask.NameToLayer("piece");
            if (pieceLayer >= 0) footObj.layer = pieceLayer;

            // Marker so SignTag can find + disable exactly this collider on the placed sign.
            footObj.AddComponent<SignPostFootCollider>();

            footObj.SetActive(true);

            Plugin.Log.LogInfo(
                $"[Trailborne/M1] {SignName}: post-foot ground collider added at root-local " +
                $"y={footY:F3}m (box {footprintX:F2}×{PostFootColliderThickness:F2}×{footprintZ:F2}m, " +
                $"layer=piece, non-trigger) — seats the post flush; disabled on the placed sign by SignTag.");
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
