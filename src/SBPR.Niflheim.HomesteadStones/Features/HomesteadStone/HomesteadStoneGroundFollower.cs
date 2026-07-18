using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.HomesteadStone
{
    /// <summary>
    /// Keeps the Stone's presentation and targeting collider seated on the CURRENT loaded terrain without
    /// rewriting the authoritative network root/ZDO. Terrain state is already replicated into each client's
    /// Heightmap; sampling it locally avoids trusting a client to move the persistent Stone while still making
    /// hoe/pickaxe elevation edits visually and interactively correct.
    ///
    /// The root's XZ remains the stable Stone/Area identity. When no Heightmap is loaded (notably a headless
    /// dedicated server), the anchor simply retains its last/root-relative height and retries later.
    /// </summary>
    internal sealed class HomesteadStoneGroundFollower : MonoBehaviour
    {
        internal const string AnchorName = "GroundAnchor";
        private const float CheckIntervalSeconds = 0.5f;
        private const float SnapThresholdMeters = 0.02f;

        private Transform? anchor;
        private float nextCheckTime;

        private void Awake()
        {
            anchor = transform.Find(AnchorName);
        }

        private void OnEnable()
        {
            nextCheckTime = 0f;
        }

        private void LateUpdate()
        {
            if (Time.time < nextCheckTime) return;
            nextCheckTime = Time.time + CheckIntervalSeconds;

            if (anchor == null)
            {
                anchor = transform.Find(AnchorName);
                if (anchor == null) return;
            }

            var rootPosition = transform.position;
            if (!Heightmap.GetHeight(rootPosition, out var groundY)) return;

            // Convert the sampled world-space ground point back into root-local space. The authored XZ and
            // yaw stay fixed; only the presentation/collider anchor follows terrain elevation.
            var desiredLocalY = transform.InverseTransformPoint(
                new Vector3(rootPosition.x, groundY, rootPosition.z)).y;
            var current = anchor.localPosition;
            if (Mathf.Abs(current.y - desiredLocalY) < SnapThresholdMeters) return;

            current.y = desiredLocalY;
            anchor.localPosition = current;
        }
    }
}
