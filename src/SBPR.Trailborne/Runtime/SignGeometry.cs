using UnityEngine;

namespace SBPR.Trailborne.Runtime
{
    /// <summary>
    /// Shared sign/marker geometry — the topology-INDEPENDENT pieces of the
    /// "free-standing signpost" kitbash, extracted so the Painted Sign
    /// (<c>Features.Signs.Signs</c>, a clone-and-move-root-group construction) and the
    /// Marker Signs (<c>Features.MarkerSigns.MarkerSigns</c>, an additive named-children
    /// construction) can NEVER drift apart on the seat tuning, the crown/standoff math,
    /// or the subtle two-phase post-foot ground collider again (card t_cc093d04, spec
    /// docs/v2/planning/marker-signs-geometry-fix-impl-spec.md §1).
    ///
    /// What lives here (and ONLY here — AT-MARKER-GEO-9 DRY guard):
    ///   • the four seat tuning constants (board crown inset, kiss epsilon, foot-pad
    ///     thickness + min footprint),
    ///   • the pure scalar crown-anchor + lateral-standoff formulas,
    ///   • the post-foot ground collider builder (the #68 flush-seat fix), and
    ///   • the placed-instance foot-collider neutralizer.
    ///
    /// What deliberately does NOT live here: the per-feature CONSTRUCTION WALK (which
    /// children to move, in what order, whether the interact collider is a root box or an
    /// inherited child). The two features have incompatible topologies, so each keeps its
    /// own walk and calls into these helpers (spec §1, §1.3). Runtime is the correct home:
    /// Features already depend on Runtime, so there is no new back-dependency.
    ///
    /// Public UnityEngine + serialized-asset API only — clean-room safe (ADR-0001/0006),
    /// headless-safe (no Awake required: all measurement is transform math over
    /// serialized <c>sharedMesh.bounds</c>).
    /// </summary>
    internal static class SignGeometry
    {
        // ── Shared seat tuning constants (single source of truth) ───────────────────
        // Moved out of Signs.cs (was Signs.cs:66/76/85/91) so the Painted Sign and the
        // four marker pieces reference the SAME values and can never diverge.

        /// <summary>
        /// NORMAL-path board placement: lift the sign/marker BOARD so its TOP edge sits
        /// this far (metres) BELOW the measured crown of the kitbashed pole, so the board
        /// reads as mounted at the TOP of the post (trail-signpost silhouette) instead of
        /// floating mid-post. Anchored to the MEASURED pole crown (no magic height); a
        /// small reveal of post above the board is intentional. Visual polish (v0.2+).
        /// (Was <c>Signs.BoardTopInset</c>.)
        /// </summary>
        public const float BoardTopInset = 0.1f;

        /// <summary>
        /// Sub-millimetre OUTWARD nudge added to the lateral standoff so the board's back
        /// face and the post's near side face don't render on the exact same plane (which
        /// would z-fight). The ONLY permissible literal in the standoff math — the axis,
        /// direction, and ½post+½board magnitude are all derived/measured at runtime.
        /// (Was <c>Signs.KissEpsilon</c>.)
        /// </summary>
        public const float KissEpsilon = 0.001f;

        /// <summary>
        /// Vertical THICKNESS (metres) of the thin ground-contact collider kitbashed at
        /// the decorative post's FOOT so the placed piece seats flush instead of buried.
        /// A SHAPE parameter, NOT a placement height — the collider's BOTTOM plane is
        /// DERIVED from the measured planted-post foot (see
        /// <see cref="AddPostFootGroundCollider"/>), so no magic Y drives where it sits;
        /// this only sets how thin the pad is. (Was <c>Signs.PostFootColliderThickness</c>.)
        /// </summary>
        public const float PostFootColliderThickness = 0.05f;

        /// <summary>
        /// Floor for the foot collider's horizontal footprint (metres), guarding the
        /// degenerate case where the post extent measures ~0. The footprint X/Z do NOT
        /// affect the seat (which keys on the collider's lowest point), so a small
        /// believable pad is sufficient. (Was <c>Signs.PostFootColliderMinFootprint</c>.)
        /// </summary>
        public const float PostFootColliderMinFootprint = 0.1f;

        // ── Pure scalar geometry math (formulas only — trivially auditable) ─────────

        /// <summary>
        /// Lift required to put the board TOP just under the planted pole crown. Floored
        /// at 0 so we never push the board below where it already sits. (Extracted from
        /// the inline computation at Signs.cs:396-398 — behaviour identical.)
        /// </summary>
        /// <param name="boardTopY">The board mesh top in the construction root's frame.</param>
        /// <param name="plantedPoleCrownY">The planted pole crown in the same frame.</param>
        public static float CrownAnchorLift(float boardTopY, float plantedPoleCrownY)
            => Mathf.Max((plantedPoleCrownY - BoardTopInset) - boardTopY, 0f);

        /// <summary>
        /// Lateral distance from the post centre to the board centre so the board's back
        /// face kisses the post's near side face plus a sub-mm anti-z-fight gap. (Extracted
        /// from the inline computation at Signs.cs:481 — behaviour identical.) Caller
        /// multiplies by the outward direction (±1) and adds to the post centre.
        /// </summary>
        public static float LateralStandoff(float postThickness, float boardThickness)
            => 0.5f * postThickness + 0.5f * boardThickness + KissEpsilon;

        // ── Post-foot ground collider (the #68 flush-seat fix) ──────────────────────

        /// <summary>
        /// Add a thin, non-trigger ground-contact <see cref="BoxCollider"/> at the planted
        /// decorative post's FOOT so the placed piece seats FLUSH at ground level instead
        /// of burying the post (t_4ad60d6f / parent spec t_1dc88742). Moved VERBATIM from
        /// <c>Signs.AddPostFootGroundCollider</c> (Signs.cs:621-687); the ONLY
        /// generalisation is the <paramref name="footColliderName"/> parameter (default
        /// keeps the sign's <c>SBPR_SignPostFoot</c>; the marker passes
        /// <c>SBPR_MarkerPostFoot</c>). Body is otherwise unchanged — same measured foot
        /// plane, same "piece"-layer non-trigger box, same <see cref="PostFootColliderTag"/>
        /// marker component.
        ///
        /// WHY THIS IS NEEDED. Valheim seats a placed piece by driving the AABB of its
        /// LOWEST enabled, non-trigger collider down onto the ground. A decorative pole
        /// stripped/grafted without its own collider leaves the board's interact collider,
        /// lifted ~1.5m to the crown, as the only collider — and the seat drives THAT to
        /// the ground, sinking the post foot ~1.5m. Restoring a collider whose bottom is at
        /// the post foot returns the lowest-collider plane to y≈0, so the post seats flush.
        ///
        /// PLACEMENT-vs-PLACED (the load-bearing two-phase detail): in the placement GHOST
        /// the collider must be NON-TRIGGER and on a layer the build placement ray-mask
        /// includes ("piece"), or the ghost disables it / the seat skips it and the bury
        /// persists. On the PLACED instance the seated transform is already baked in, so
        /// the collider has done its job — the owning tag DISABLES it in Awake (via
        /// <see cref="NeutralizeFootColliderIfPlaced"/>) so it can never steal the
        /// E-to-write / paint raycast — the BOARD stays the sole interact/paint target.
        ///
        /// Placement is DERIVED, not magic: the collider's bottom plane is the MEASURED
        /// planted-post foot in root-local space (8-corner transformed-bounds method), and
        /// its horizontal footprint is the measured post thickness. Public UnityEngine API
        /// only — clean-room safe.
        /// </summary>
        /// <param name="rootT">The construction-root transform (the collider parents here,
        /// so its extents share the same local frame the pole foot was measured in).</param>
        /// <param name="pole">The planted decorative post, used to measure the foot Y and
        /// the horizontal footprint. Its <c>transform</c> must carry the mesh-to-world
        /// mapping (true for both the sign's pole clone and the marker's grafted post).</param>
        /// <param name="footColliderName">Name of the kitbashed foot-collider child (for
        /// log/debug legibility; the owning tag finds it by its
        /// <see cref="PostFootColliderTag"/> marker, not by name).</param>
        public static void AddPostFootGroundCollider(
            Transform rootT, GameObject pole, string footColliderName = "SBPR_SignPostFoot")
        {
            if (rootT == null || pole == null) return;

            // Measure the PLANTED post's foot in ROOT-local space. MeasureLocalFootY
            // returns the foot in the pole's OWN local frame, so we transform that foot
            // plane through the planted pole's actual transform into root-local space.
            // Because the pole was planted with its foot at root-local y ≈ 0, this lands at
            // y ≈ 0 — but it is DERIVED from the measured foot through the real transform
            // (robust to the pole's pivot / rotation / scale), never a magic 0.
            float poleLocalFoot = Assets.MeasureLocalFootY(pole);
            Vector3 footWorld = pole.transform.TransformPoint(new Vector3(0f, poleLocalFoot, 0f));
            float footY = rootT.InverseTransformPoint(footWorld).y;

            // Horizontal footprint = the post's thickness. Footprint does NOT affect the
            // seat (which keys on the lowest Y only), so the min-footprint floor guards the
            // degenerate measurement case.
            Assets.MeasureLocalExtent(pole, 0, out float postMinX, out float postMaxX);
            Assets.MeasureLocalExtent(pole, 2, out float postMinZ, out float postMaxZ);

            float footprintX = Mathf.Max(postMaxX - postMinX, PostFootColliderMinFootprint);
            float footprintZ = Mathf.Max(postMaxZ - postMinZ, PostFootColliderMinFootprint);
            float centerX = 0.5f * (postMinX + postMaxX); // ≈0 (post planted at X/Z=0), but measured
            float centerZ = 0.5f * (postMinZ + postMaxZ);

            // A child of the construction ROOT (not the pole) so it is unaffected by the
            // pole's own transform and shares the root-local frame the foot was measured in.
            // Identity local rotation/scale so the BoxCollider size maps 1:1 to metres.
            var footObj = new GameObject(footColliderName);
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
            // "piece" is inside it) and the seat counts it. On the PLACED piece the owning
            // tag disables this collider, so the layer no longer matters there.
            int pieceLayer = LayerMask.NameToLayer("piece");
            if (pieceLayer >= 0) footObj.layer = pieceLayer;

            // Marker so the owning tag can find + disable exactly this collider on the
            // placed instance.
            footObj.AddComponent<PostFootColliderTag>();

            footObj.SetActive(true);

            Plugin.Log.LogInfo(
                $"[Trailborne] {footColliderName}: post-foot ground collider added at root-local " +
                $"y={footY:F3}m (box {footprintX:F2}×{PostFootColliderThickness:F2}×{footprintZ:F2}m, " +
                $"layer=piece, non-trigger) — seats the post flush; disabled on the placed piece.");
        }

        // ── Placed-instance neutralize (extracted from SignTag.cs:62-76) ────────────

        /// <summary>
        /// On a PLACED instance (live ZDO) disable every collider under a
        /// <see cref="PostFootColliderTag"/> child so it can't steal the interact/paint
        /// raycast. NO-OP on the placement GHOST (no ZDO) so the seat still works, and a
        /// NO-OP without a <see cref="ZNetView"/> (headless-safe). Both
        /// <c>SignTag.Awake</c> and <c>MarkerSignTag.Awake</c> call this.
        ///
        /// CRITICAL — placed-only gate. The ghost has no ZDO (vanilla sets
        /// <c>ZNetView.m_forceDisableInit</c> while instantiating it) and still needs the
        /// foot collider ENABLED to compute the flush seat; the ghost fails the live-ZDO
        /// check and keeps its collider, so seating is preserved. Idempotent.
        /// </summary>
        /// <param name="owner">The placed piece component (the tag) whose child subtree is
        /// searched for foot-collider markers.</param>
        /// <param name="nview">The piece's ZNetView; a live <c>GetZDO()</c> distinguishes a
        /// placed instance from the ghost.</param>
        public static void NeutralizeFootColliderIfPlaced(Component owner, ZNetView? nview)
        {
            if (owner == null) return;
            // Ghost (no ZDO) → leave the collider enabled so the post seats flush. Only a
            // truly placed instance reaches the disable path.
            if (nview == null || nview.GetZDO() == null) return;

            foreach (var marker in owner.GetComponentsInChildren<PostFootColliderTag>(includeInactive: true))
            {
                if (marker == null) continue;
                foreach (var col in marker.GetComponents<Collider>())
                {
                    if (col != null) col.enabled = false;
                }
            }
        }
    }
}
