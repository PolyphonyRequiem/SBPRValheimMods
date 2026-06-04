using UnityEngine;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Marker tag attached to each colored sign clone so we can identify
    /// the variant + look up its pin color at Interact time without
    /// reading mesh tints back.
    /// </summary>
    public class TrailborneSignTag : MonoBehaviour
    {
        public string PrefabName;
    }
}
