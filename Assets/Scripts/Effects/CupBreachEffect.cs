using UnityEngine;

namespace Gate2Reality.Effects
{
    public class CupBreachEffect : TriggerableEffectBase
    {
        [SerializeField] private Animator crackAnimator;
        [SerializeField] private ParticleSystem shardsParticles;
        [SerializeField] private GameObject holoMapRoot;
        [SerializeField] private AudioSource breachAudioSource;
        [SerializeField] private float crackDuration = 2.0f;
        [SerializeField] private float shardDelay = 2.2f;
        [SerializeField] private float mapRevealDelay = 3.0f;

        private static readonly int BreachTrigger = Animator.StringToHash("Breach");
        private bool _shardsPlayed;
        private bool _mapRevealed;

        protected override void OnTriggered()
        {
            _shardsPlayed = false;
            _mapRevealed = false;

            if (crackAnimator != null) crackAnimator.SetTrigger(BreachTrigger);
            if (breachAudioSource != null) breachAudioSource.Play();
            if (holoMapRoot != null) holoMapRoot.SetActive(false);
        }

        protected override void OnEffectUpdate(float dt)
        {
            if (!_shardsPlayed && TimeSinceTriggered >= shardDelay)
            {
                _shardsPlayed = true;
                if (shardsParticles != null) shardsParticles.Play();
            }

            if (!_mapRevealed && TimeSinceTriggered >= mapRevealDelay)
            {
                _mapRevealed = true;
                if (holoMapRoot != null) holoMapRoot.SetActive(true);
                MarkFinished();
            }
        }

        protected override void OnCancelled()
        {
            if (shardsParticles != null) shardsParticles.Stop();
            if (holoMapRoot != null) holoMapRoot.SetActive(false);
        }
    }
}
