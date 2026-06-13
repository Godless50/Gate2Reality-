using UnityEngine;
using Gate2Reality.Effects;

namespace Gate2Reality.SceneTwo
{
    public class PortalWindowEffect : TriggerableEffectBase
    {
        [SerializeField] private Material portalWindowMaterial;
        [SerializeField] private Material portalRimMaterial;
        [SerializeField] private float targetDiameter = 0.6f;
        [SerializeField] private float openDuration = 0.8f;

        private float _t;
        private static readonly int AperturePropId = Shader.PropertyToID("_Aperture");

        protected override void OnTriggered()
        {
            _t = 0f;
            SetAperture(0f);
        }

        protected override void OnEffectUpdate(float dt)
        {
            _t = Mathf.Clamp01(_t + dt / openDuration);
            // easeOutBack with ~10% overshoot
            float eased = EaseOutBack(_t);
            SetAperture(eased * targetDiameter);

            if (_t >= 1f) MarkFinished();
        }

        protected override void OnCancelled() => SetAperture(0f);

        private void SetAperture(float diameter)
        {
            if (portalWindowMaterial != null) portalWindowMaterial.SetFloat(AperturePropId, diameter);
            if (portalRimMaterial != null)    portalRimMaterial.SetFloat(AperturePropId, diameter);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
    }
}
