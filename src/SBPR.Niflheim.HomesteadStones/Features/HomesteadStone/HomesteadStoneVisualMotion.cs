using SBPR.Niflheim.HomesteadStones.Domain;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.HomesteadStone
{
    internal sealed class HomesteadStoneVisualMotion : MonoBehaviour
    {
        private Vector3 basePosition;
        private Quaternion baseRotation;
        private double elapsed;

        private void Awake()
        {
            basePosition = transform.localPosition;
            baseRotation = transform.localRotation;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            var sample = HomesteadVisualMotion.Sample(elapsed);
            transform.localPosition = basePosition + (Vector3.up * (float)sample.HeightOffset);
            transform.localRotation = baseRotation * Quaternion.Euler(0f, (float)sample.YawDegrees, 0f);
        }
    }
}
