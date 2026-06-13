using UnityEngine;
using Gate2Reality.Effects;

namespace Gate2Reality.SceneTwo
{
    public class EchoSurfaceEffect : TriggerableEffectBase
    {
        [SerializeField] private Material rimMaterial;
        [SerializeField] private ParticleSystem dustColumnParticles;
        [SerializeField] private AudioSource bassHumSource;
        [SerializeField] private float rippleDuration = 3.0f;
        [SerializeField] private int rippleCount = 5;

        private float _t;
        private static readonly int AperturePropId = Shader.PropertyToID("_Aperture");

        protected override void OnTriggered()
        {
            _t = 0f;
            if (dustColumnParticles != null) dustColumnParticles.Play();
            if (bassHumSource != null) bassHumSource.Play();
        }

        protected override void OnEffectUpdate(float dt)
        {
            _t += dt / rippleDuration;
            // Modulate aperture to create ripple pulses
            float ripple = Mathf.Abs(Mathf.Sin(_t * rippleCount * Mathf.PI));
            if (rimMaterial != null) rimMaterial.SetFloat(AperturePropId, ripple * 0.5f);

            if (_t >= 1f)
            {
                if (rimMaterial != null) rimMaterial.SetFloat(AperturePropId, 0f);
                MarkFinished();
            }
        }

        protected override void OnCancelled()
        {
            if (dustColumnParticles != null) dustColumnParticles.Stop();
            if (bassHumSource != null) bassHumSource.Stop();
            if (rimMaterial != null) rimMaterial.SetFloat(AperturePropId, 0f);
        }
    }
}
