using UnityEngine;

namespace Gate2Reality.Effects
{
    /// <summary>
    /// Узел 2 «The Book — Memory Distortion».
    ///  - Партиклы «листающихся страниц» у позы физической книги.
    ///  - Белый шум: нарастает, держится под шёпотом, затем спадает.
    ///  - Нарративный шёпот (в Step 4 источником текста станет on-device MLLM;
    ///    пока — записанный клип).
    ///  - После шёпота — «намёк на чашку»: короткий звон фарфора + одиночный
    ///    призрачный спрайт трещины в воздухе. Игрок начинает искать чашку.
    /// Тайминги — лёгкая FSM на enum, без корутин (и их аллокаций).
    /// </summary>
    public sealed class BookMemoryEffect : TriggerableEffectBase
    {
        [Header("Партиклы страниц")]
        [SerializeField] private ParticleSystem pageParticles;

        [Header("Аудио")]
        [SerializeField] private AudioSource whiteNoiseSource;  // loop = true, clip = шум
        [SerializeField] private AudioSource whisperSource;     // одноразовый шёпот
        [SerializeField] private AudioSource cupHintSource;     // звон фарфора
        [SerializeField] private float noiseFadeInSeconds = 2f;
        [SerializeField] private float noiseTargetVolume = 0.5f;
        [SerializeField] private float noiseFadeOutSeconds = 3f;

        [Header("Намёк на чашку")]
        [SerializeField] private GameObject ghostCrackSprite;   // билборд-призрак
        [SerializeField] private float hintLifetimeSeconds = 4f;

        private enum Phase : byte { NoiseRise, Whispering, CupHint, NoiseFall, Done }
        private Phase _phase;
        private float _phaseTimer;

        private void Awake()
        {
            if (ghostCrackSprite != null) ghostCrackSprite.SetActive(false);
        }

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
                    {
                        whiteNoiseSource.volume = noiseTargetVolume *
                            Mathf.Clamp01(_phaseTimer / noiseFadeInSeconds);
                    }
                    if (_phaseTimer >= noiseFadeInSeconds)
                    {
                        if (whisperSource != null) whisperSource.Play();
                        NextPhase(Phase.Whispering);
                    }
                    break;

                case Phase.Whispering:
                    // Ждём конца клипа шёпота (isPlaying — дёшево).
                    if (whisperSource == null || !whisperSource.isPlaying)
                    {
                        if (cupHintSource != null) cupHintSource.Play();
                        if (ghostCrackSprite != null) ghostCrackSprite.SetActive(true);
                        NextPhase(Phase.CupHint);
                    }
                    break;

                case Phase.CupHint:
                    if (_phaseTimer >= hintLifetimeSeconds)
                    {
                        if (ghostCrackSprite != null) ghostCrackSprite.SetActive(false);
                        NextPhase(Phase.NoiseFall);
                    }
                    break;

                case Phase.NoiseFall:
                    if (whiteNoiseSource != null)
                    {
                        whiteNoiseSource.volume = noiseTargetVolume *
                            (1f - Mathf.Clamp01(_phaseTimer / noiseFadeOutSeconds));
                    }
                    if (_phaseTimer >= noiseFadeOutSeconds)
                    {
                        if (whiteNoiseSource != null) whiteNoiseSource.Stop();
                        if (pageParticles != null)
                            pageParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                        NextPhase(Phase.Done);
                        MarkFinished(); // граф (и guard) знают: эффект отыгран
                    }
                    break;
            }
        }

        private void NextPhase(Phase p)
        {
            _phase = p;
            _phaseTimer = 0f;
        }

        protected override void OnCancelled()
        {
            if (whiteNoiseSource != null) whiteNoiseSource.Stop();
            if (whisperSource != null) whisperSource.Stop();
            if (pageParticles != null) pageParticles.Stop();
            if (ghostCrackSprite != null) ghostCrackSprite.SetActive(false);
        }
    }
}
