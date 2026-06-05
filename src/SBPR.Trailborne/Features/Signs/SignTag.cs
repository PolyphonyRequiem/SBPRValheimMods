using UnityEngine;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Tag attached to the single Painted Sign clone. Carries the sign's painted
    /// color as ZDO-backed runtime state (NOT baked into the prefab), and
    /// re-applies the corresponding mesh tint on spawn so the color persists
    /// across reloads + syncs to joined clients.
    ///
    /// Color identity is one of Signs.Colors ("red"/"white"/"blue"/"black") or
    /// the empty string for the default UNPAINTED state. Stored in ZDO field
    /// Signs.ZdoColor. Owner-only writes via ZNetView (mirrors CairnTag.WriteTier).
    /// </summary>
    public class SignTag : MonoBehaviour
    {
        private ZNetView? nview;
        private string? lastAppliedColor = null; // sentinel: nothing applied yet

        private void Awake()
        {
            nview = GetComponent<ZNetView>();
            ApplyColorFromZdo();
        }

        /// <summary>Current painted color id, or "" if unpainted.</summary>
        public string ReadColor()
        {
            if (nview == null || nview.GetZDO() == null) return "";
            return nview.GetZDO().GetString(Signs.ZdoColor, "");
        }

        /// <summary>
        /// Owner-write the painted color and re-tint immediately. Pass one of
        /// Signs.Colors. Returns false if the ZDO isn't ready.
        /// </summary>
        public bool WriteColor(string color)
        {
            if (nview == null || nview.GetZDO() == null) return false;
            if (!nview.IsOwner()) nview.ClaimOwnership();
            nview.GetZDO().Set(Signs.ZdoColor, color ?? "");
            ApplyColorFromZdo();
            return true;
        }

        /// <summary>
        /// Read the ZDO color and tint the mesh to match. Empty/unknown color
        /// leaves the sign in its vanilla (unpainted) material. Idempotent: skips
        /// re-tinting when the color hasn't changed since the last apply.
        /// </summary>
        public void ApplyColorFromZdo()
        {
            string color = ReadColor();
            if (color == lastAppliedColor) return;
            lastAppliedColor = color;

            if (!string.IsNullOrEmpty(color) && Signs.ColorValues.TryGetValue(color, out var col))
                Signs.TintRenderers(gameObject, col);
            // else: unpainted — keep the vanilla wood material as the base state.
        }
    }
}
