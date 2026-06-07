using UnityEngine;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Pure-runtime backup of a sign renderer's ORIGINAL (unpainted) materials,
    /// captured the first time <see cref="Signs.TintBoard"/> / <see cref="Signs.TintBorder"/>
    /// clones-and-tints that renderer. Lets the "remove text color" / "remove border
    /// color" None affordances (§A2.6, Issue 4) revert the element LIVE instead of only
    /// after a fresh spawn — the tint replaces <c>sharedMaterials</c> with tinted clones,
    /// so without this snapshot the original reference is lost.
    ///
    /// No ZDO, no networking: this is local view state. Each client re-tints from ZDO on
    /// spawn (SignTag.ApplyColorsFromZdo) and captures its own backup at that point, so
    /// the revert path works identically on every client.
    /// </summary>
    public class SignTintBackup : MonoBehaviour
    {
        /// <summary>The renderer's materials as they were before the first SBPR tint.</summary>
        public Material[]? Original;
    }
}
