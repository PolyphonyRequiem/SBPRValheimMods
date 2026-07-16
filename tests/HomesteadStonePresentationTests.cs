// ─────────────────────────────────────────────────────────────────────────────
//  Homestead Stone V12 presentation-tune contract — xUnit structural tests.
//
//  WHY THIS MATTERS. Daniel's real-client manual-walk feedback (2026-07-15,
//  verbatim): "Needs to be about twice as big in all directions, and float about
//  a meter higher, but otherwise looks great." This suite pins the engine-free
//  HomesteadStonePresentation contract that HomesteadStoneRegistrar stamps onto
//  the live additive prefab: uniform 2× visual scale, the +1 m float raise, and
//  the refit gameplay-root collider envelope. The registrar itself is UnityEngine-
//  bound (untestable headless), but it reads every number from this pure contract,
//  so pinning the contract pins the shipped behaviour without a second copy.
// ─────────────────────────────────────────────────────────────────────────────
using SBPR.Niflheim.HomesteadStones.Domain;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class HomesteadStonePresentationTests
    {
        [Fact]
        public void Visual_scale_is_uniform_two_x()
        {
            Assert.Equal(2.0f, HomesteadStonePresentation.VisualScale);
            Assert.Equal(2.0f, HomesteadStonePresentation.VisualScale / HomesteadStonePresentation.PriorVisualScale);
        }

        [Fact]
        public void Float_height_is_raised_exactly_one_metre_above_prior()
        {
            Assert.Equal(1.0f, HomesteadStonePresentation.PriorVisualLocalY);
            Assert.Equal(2.0f, HomesteadStonePresentation.VisualLocalY);
            Assert.Equal(1.0f, HomesteadStonePresentation.FloatHeightRaiseMetres);
        }

        [Fact]
        public void Scaled_visual_height_is_double_the_authored_model()
        {
            Assert.Equal(1.8f, HomesteadStonePresentation.UnscaledVisualHeight);
            Assert.Equal(3.6f, HomesteadStonePresentation.ScaledVisualHeight, precision: 4);
        }

        [Fact]
        public void Collider_radius_doubles_to_match_the_enlarged_footprint()
        {
            // Prior root capsule radius was 0.65; 2× the visual widens the footprint the same way.
            Assert.Equal(1.3f, HomesteadStonePresentation.ColliderRadius, precision: 4);
        }

        [Fact]
        public void Collider_spans_ground_to_the_enlarged_visual_top()
        {
            // Height reaches from ground (0) up to the visual top: float gap (+2.0 m) + scaled height
            // (~3.6 m) = ~5.6 m, so the capsule is not obviously undersized or ghostly.
            Assert.Equal(5.6f, HomesteadStonePresentation.VisualTopY, precision: 4);
            Assert.Equal(5.6f, HomesteadStonePresentation.ColliderHeight, precision: 4);
            Assert.Equal(HomesteadStonePresentation.VisualTopY / 2.0f, HomesteadStonePresentation.ColliderCenterY, precision: 4);
        }

        // ── LOD / renderer-culling contract ──────────────────────────────────────────────

        [Fact]
        public void Lod_engine_reference_values_match_vanilla_valheim()
        {
            // Grounded from the shipped assembly (fair game per AGENTS.md): GameCamera.m_fov = 65,
            // GraphicsSettings.GetLodBias level 2 (the default preset) => 2f.
            Assert.Equal(65.0f, HomesteadStonePresentation.LodCameraFovVerticalDegrees);
            Assert.Equal(2.0f, HomesteadStonePresentation.LodBiasReference);
        }

        [Fact]
        public void Target_cull_distance_sits_inside_the_ninety_to_one_twenty_band()
        {
            Assert.Equal(90.0f, HomesteadStonePresentation.MinAcceptableCullDistanceMeters);
            Assert.Equal(120.0f, HomesteadStonePresentation.MaxAcceptableCullDistanceMeters);
            Assert.InRange(
                HomesteadStonePresentation.TargetCullDistanceMeters,
                HomesteadStonePresentation.MinAcceptableCullDistanceMeters,
                HomesteadStonePresentation.MaxAcceptableCullDistanceMeters);
        }

        [Fact]
        public void Cull_height_and_distance_are_exact_inverses()
        {
            // Round-trip the deterministic projection: compute the LOD transition height for a
            // representative runtime world size, then invert it back to the target distance.
            const float runtimeWorldSize = 3.6f; // ~scaled visual height (2× the 1.8 m model)
            var h = HomesteadStonePresentation.ComputeCullScreenHeight(
                runtimeWorldSize,
                HomesteadStonePresentation.TargetCullDistanceMeters,
                HomesteadStonePresentation.LodBiasReference);
            var d = HomesteadStonePresentation.ComputeCullDistance(
                runtimeWorldSize, h, HomesteadStonePresentation.LodBiasReference);
            Assert.Equal(HomesteadStonePresentation.TargetCullDistanceMeters, d, precision: 2);
            // Sanity: the seed hypothesis is 5–7% relative screen height for a stone this size.
            Assert.InRange(h, 0.02f, 0.12f);
        }
    }
}
