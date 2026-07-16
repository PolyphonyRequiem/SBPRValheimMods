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
    }
}
