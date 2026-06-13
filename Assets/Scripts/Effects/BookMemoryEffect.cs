using UnityEngine;

namespace Gate2Reality.Effects
{
    internal enum Phase : byte { NoiseRise, Whispering, CupHint, NoiseFall, Done }

    public class BookMemoryEffect : TriggerableEffectBase
    {
        [SerializeField] private ParticleSystem pageParticles;
        [SerializeField] private AudioSource whiteNoiseSource;
        [SerializeField] private AudioSource whisperSource;
        [SerializeField] private AudioSource ceramicChimeSource;
        [SerializeField] private float noiseRiseDuration = 1.0f;
        [SerializeField] private float whisperDuration = 4.0f;
        [SerializeField] private float cupHintDuration = 1.5f;
        [SerializeField] private float noiseFallDuration = 1.2f;

        private Phase _phase;
        private float _phaseTimer;

        protected override void OnTriggered()
        {
            _phase = Phase.NoiseRise;
            _phaseTimer = 0f;

            if (pageParticles != null) pageParticles.Play();
            if (whiteNoiseSource != null)
            {
                whiteNoiseSource.volume = 0f;
                whiteNoiseSource.Play();
            }
        }

        protected override void OnEffectUpdate(float dt)
        {
            _phaseTimer += dt;

            switch (_phase)
            {
                case Phase.NoiseRise:
                    if (whiteNoiseSource != null)
                        whiteNoiseSource.volume = Mathf.Clamp01(_phaseTimer / noiseRiseDuration);
                    if (_phaseTimer >= noiseRiseDuration) NextPhase(Phase.Whispering);
                    break;

                case Phase.Whispering:
                    if (_phaseTimer <= 0.05f && whisperSource != null)
                        whisperSource.Play();
                    if (_phaseTimer >= whisperDuration) NextPhase(Phase.CupHint);
                    break;

                case Phase.CupHint:
                    if (_phaseTimer <= 0.05f && ceramicChimeSource != null)
                        ceramicChimeSource.Play();
                    if (_phaseTimer >= cupHintDuration) NextPhase(Phase.NoiseFall);
                    break;

                case Phase.NoiseFall:
                    if (whiteNoiseSource != null)
                        whiteNoiseSource.volume = Mathf.Clamp01(1f - _phaseTimer / noiseFallDuration);
                    if (_phaseTimer >= noiseFallDuration) NextPhase(Phase.Done);
                    break;

                case Phase.Done:
                    MarkFinished();
                    break;
            }
        }

        internal void NextPhase(Phase next)
        {
            _phase = next;
            _phaseTimer = 0f;
        }

        protected override void OnCancelled()
        {
            if (pageParticles != null) pageParticles.Stop();
            if (whiteNoiseSource != null) { whiteNoiseSource.Stop(); whiteNoiseSource.volume = 0f; }
            if (whisperSource != null) whisperSource.Stop();
        }
    }
}
