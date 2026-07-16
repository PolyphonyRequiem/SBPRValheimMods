namespace SBPR.Niflheim.HomesteadStones.Domain
{
    /// <summary>
    /// Engine-free presentation contract for the additive Homestead Stone: the uniform visual
    /// scale, the presentation child's local hover height, and the explicit gameplay-root collider
    /// envelope. Kept UnityEngine-free so it is link-compiled into the net8 test suite and pins the
    /// exact numbers the <c>HomesteadStoneRegistrar</c> stamps onto the live prefab.
    ///
    /// Provenance (Daniel real-client manual-walk feedback, 2026-07-15): "Needs to be about twice
    /// as big in all directions, and float about a meter higher, but otherwise looks great." The V12
    /// model pivot sits at its base, so the presentation child's local Y IS the visible float gap
    /// under the stone. Doubling <see cref="VisualScale"/> grows the ~1.8 m model to ~3.6 m about
    /// that base pivot (base stays put, geometry grows upward), and raising <see cref="VisualLocalY"/>
    /// from the prior 1.0 m to 2.0 m lifts the whole stone exactly +1.0 m higher.
    /// </summary>
    internal static class HomesteadStonePresentation
    {
        /// <summary>Prior (pre-tune) uniform visual scale and float height, retained for the +1 m contract.</summary>
        internal const float PriorVisualScale = 1.0f;
        internal const float PriorVisualLocalY = 1.0f;

        /// <summary>Approximate authored visual height of the unscaled V12 model, in metres.</summary>
        internal const float UnscaledVisualHeight = 1.8f;

        /// <summary>Uniform X/Y/Z scale applied to the presentation child. 2× per Daniel's tune.</summary>
        internal const float VisualScale = 2.0f;

        /// <summary>Local Y of the presentation child under the gameplay root — the visible float gap. Raised +1 m.</summary>
        internal const float VisualLocalY = 2.0f;

        /// <summary>Exact metre delta by which the tuned float height sits above the prior implementation.</summary>
        internal const float FloatHeightRaiseMetres = VisualLocalY - PriorVisualLocalY;

        /// <summary>Approximate scaled visual height (model grows upward about its base pivot).</summary>
        internal const float ScaledVisualHeight = UnscaledVisualHeight * VisualScale;

        /// <summary>World-space Y of the visual's base (the float gap) and its approximate top.</summary>
        internal const float VisualBaseY = VisualLocalY;
        internal const float VisualTopY = VisualLocalY + ScaledVisualHeight;

        // Explicit gameplay-root CapsuleCollider, refit to the enlarged, raised visual envelope.
        // The prior collider (radius 0.65, height 2.2, center 1.1) spanned ground → 2.2 m around the
        // unscaled 1.0 m-floated model. This refit doubles the radius and spans the ground up to the
        // enlarged visual top so collision/targeting is neither undersized nor ghostly against the
        // ~3.6 m stone now floating at +2.0 m.
        internal const float ColliderRadius = 1.3f;   // 2× prior 0.65
        internal const float ColliderHeight = VisualTopY;             // ground (0) → visual top (~5.6 m)
        internal const float ColliderCenterY = VisualTopY / 2.0f;     // midpoint of that span

        // ── Visual LOD / renderer-culling contract (provisional performance tune) ──────────
        //
        // Daniel/Soloredis guidance (2026-07-16): add an LODGroup to the whole presentation
        // visual so every base/ivy/emission renderer is culled together at range; target a
        // 90–120 m maximum visibility, with 5–7% relative screen height as a size-dependent
        // starting hypothesis. Because the model has no authored lower-poly mesh, the group is
        // a single visual LOD (LOD0 = all renderers) followed by a hard cull region — NOT a
        // duplicated fake lower LOD and NOT destructive geometry. Only renderers cull; the
        // additive ZNetView/identity/collider/placement/progression state stays alive.
        //
        // Unity culls a renderer when its LODGroup screen-relative height drops below the last
        // LOD's transition height H. That height maps to a camera distance deterministically:
        //
        //     screenHeight(d) = (worldSize) / (d * 2 * tan(fovVertical/2)) * lodBias
        //  ⇔  cullDistance    = (worldSize * lodBias) / (2 * H * tan(fovVertical/2))
        //
        // worldSize is the runtime LODGroup size (authored local size × the uniform VisualScale).
        // The two engine constants below are read from vanilla Valheim (fair game per AGENTS.md):
        //   • GameCamera.m_fov = 65 (vertical FOV);
        //   • GraphicsSettings.GetLodBias: quality level 2 (the default preset) → 2f.
        // The cull threshold H is calibrated in the builder from the MEASURED runtime worldSize
        // so the cull lands at TargetCullDistanceMeters; 5–7% is only the seed hypothesis
        // because FOV/bounds decide the real distance. Ratified against real-client frames.

        /// <summary>Vanilla Valheim vertical camera FOV (GameCamera.m_fov), in degrees.</summary>
        internal const float LodCameraFovVerticalDegrees = 65.0f;

        /// <summary>Vanilla Valheim default lodBias (GraphicsSettings quality level 2 → 2f).</summary>
        internal const float LodBiasReference = 2.0f;

        /// <summary>Target maximum visibility / cull distance for the ~3.6 m (2×) Stone, in metres.</summary>
        internal const float TargetCullDistanceMeters = 105.0f;

        /// <summary>Lower acceptance bound: the Stone must remain visible through ordinary approach past this.</summary>
        internal const float MinAcceptableCullDistanceMeters = 90.0f;

        /// <summary>Upper acceptance bound: the Stone must be culled by roughly this distance.</summary>
        internal const float MaxAcceptableCullDistanceMeters = 120.0f;

        /// <summary>tan(fovVertical / 2), the half-angle projection factor used by both formulas.</summary>
        internal static float LodHalfFovTangent =>
            (float)System.Math.Tan(LodCameraFovVerticalDegrees * 0.5f * System.Math.PI / 180.0);

        /// <summary>
        /// The screen-relative transition height H to stamp on the single visual LOD so the group
        /// culls at <paramref name="cullDistanceMeters"/> for a given runtime world size and lodBias.
        /// </summary>
        internal static float ComputeCullScreenHeight(float runtimeWorldSize, float cullDistanceMeters, float lodBias)
            => runtimeWorldSize * lodBias / (2.0f * cullDistanceMeters * LodHalfFovTangent);

        /// <summary>
        /// Inverse of <see cref="ComputeCullScreenHeight"/>: the camera distance at which a group of
        /// the given runtime world size and lodBias reaches the transition height <paramref name="screenHeight"/>.
        /// </summary>
        internal static float ComputeCullDistance(float runtimeWorldSize, float screenHeight, float lodBias)
            => runtimeWorldSize * lodBias / (2.0f * screenHeight * LodHalfFovTangent);
    }
}
