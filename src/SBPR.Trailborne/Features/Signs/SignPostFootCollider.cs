using UnityEngine;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Marker for the thin ground-contact <see cref="BoxCollider"/> kitbashed at the
    /// decorative post's FOOT (root-local y ≈ 0) by
    /// <see cref="Signs.AddPostFootGroundCollider"/>. It exists for ONE reason: to give
    /// the placed sign a non-trigger collider whose lowest point is at the post foot, so
    /// Valheim's placement seat (which drives the lowest enabled, non-trigger collider's
    /// AABB to the ground) lands the post foot flush instead of burying it.
    ///
    /// Why the post was buried (t_4ad60d6f / parent t_1dc88742): the decorative pole is
    /// stripped of its own collider (<see cref="Assets.StripToDecorative"/>) so it never
    /// intercepts the E-to-write raycast — leaving the board's interact collider, lifted
    /// ~1.5m to the crown, as the ONLY collider. The placement ghost seats by that lifted
    /// collider, driving the 2m post ~3/4 underground. Restoring a foot-level collider
    /// returns the lowest-collider plane to the post foot.
    ///
    /// Two-phase lifecycle (the load-bearing detail, confirmed against the vanilla
    /// placement path):
    ///   • PLACEMENT GHOST — the collider MUST be a non-trigger collider on a layer
    ///     inside the build placement ray-mask (we use "piece") so the ghost keeps it
    ///     ENABLED and the seat counts it. A trigger collider, or one on a layer outside
    ///     that mask, is skipped/disabled by the ghost and would NOT fix the bury.
    ///   • PLACED INSTANCE — the seated position is already baked in at placement, so the
    ///     collider has done its job. <see cref="SignTag"/> DISABLES it on the placed sign
    ///     (in Awake) so it can never steal the Sign's E-to-write / paint raycast — the
    ///     BOARD stays the sole interact/paint target (regression guard AT-4). Disabling
    ///     also keeps the post non-separately-destructible (it carries no WearNTear).
    ///
    /// Pure marker — no behaviour of its own; carries no ZDO. Public UnityEngine API only,
    /// clean-room safe.
    /// </summary>
    public class SignPostFootCollider : MonoBehaviour
    {
    }
}
