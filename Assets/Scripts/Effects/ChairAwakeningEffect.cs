using UnityEngine;

namespace Gate2Reality.Effects
{
    public class ChairAwakeningEffect : TriggerableEffectBase
    {
        [SerializeField] private Light amberLight;
        [SerializeField] private float breathingSpeed = 1.2f;
        [SerializeField] private float breathingAmplitude = 0.35f;
        [SerializeField] private float baseLightIntensity = 1.4f;
        [SerializeField] private Material distortionMaterial;
        [SerializeField] private Transform shadowQuad;
        [SerializeField] private float rampDuration = 1.5f;

        private float _rampT;
        private Vector3 _hintTarget;
        private bool _hasHintTarget;
        private static readonly int HorrorScaleId = Shader.PropertyToID("_HorrorScale");
        private static readonly int BreachProgressId = Shader.PropertyToID("_BreachProgress");

        public void SetHintTarget(Vector3 worldPosition)
        {
            _hintTarget = worldPosition;
            _hasHintTarget = true;
        }

        protected override void OnTriggered()
        {
            _rampT = 0f;
            if (amberLight != null)
            {
                amberLight.enabled = true;
                amberLight.intensity = 0f;
            }
            if (distortionMaterial != null)
            {
                distortionMaterial.SetFloat(HorrorScaleId, 0f);
                distortionMaterial.SetFloat(BreachProgressId, 0f);
            }
        }

        protected override void OnEffectUpdate(float dt)
        {
            _rampT = Mathf.Clamp01(_rampT + dt / rampDuration);

            float breath = 1f + breathingAmplitude * Mathf.Sin(TimeSinceTriggered * breathingSpeed * Mathf.PI * 2f);
            float targetIntensity = baseLightIntensity * _rampT * breath;

            if (amberLight != null)
                amberLight.intensity = targetIntensity;

            if (distortionMaterial != null)
                distortionMaterial.SetFloat(HorrorScaleId, _rampT);

            if (_hasHintTarget && shadowQuad != null)
            {
                Vector3 dir = (_hintTarget - shadowQuad.position).normalized;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                    shadowQuad.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }

        protected override void OnCancelled()
        {
            if (amberLight != null) amberLight.enabled = false;
            if (distortionMaterial != null) distortionMaterial.SetFloat(HorrorScaleId, 0f);
        }
    }
}
