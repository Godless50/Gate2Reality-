using UnityEngine;

namespace Gate2Reality.Effects
{
    /// <summary>
    /// Узел 3 «The Cup — The Breach» (финал Сцены 1).
    ///  - Сине-светящаяся трещина: драйвер параметра _CrackProgress
    ///    шейдера из Step 4 (анимация «расползания» по кривой).
    ///  - Виртуальные осколки (партиклы/меши). КРИТИЧНО: рендерятся с
    ///    включённой Environment Occlusion (AROcclusionManager + URP-фича,
    ///    настройка в Step 5) — физическая рука игрока корректно
    ///    перекрывает осколки. Здесь логика, там — пайплайн.
    ///  - Финал: голографическая проекция карты комнаты, поднимающаяся
    ///    над чашкой, + событие завершения для будущей Сцены 2.
    /// </summary>
    public sealed class CupBreachEffect : TriggerableEffectBase
    {
        [Header("Трещина (шейдер из Step 4)")]
        [SerializeField] private Renderer crackOverlayRenderer;
        [SerializeField] private float crackDurationSeconds = 5f;
        [Tooltip("Нелинейность расползания: рывками, как настоящий скол")]
        [SerializeField] private AnimationCurve crackCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Осколки")]
        [SerializeField] private ParticleSystem shardParticles;
        [Tooltip("Доля прогресса трещины, при которой «взрываются» осколки")]
        [Range(0f, 1f)] [SerializeField] private float shardBurstAt = 0.8f;

        [Header("Голографическая карта")]
        [SerializeField] private GameObject holographicMapRoot; // вращающаяся проекция
        [SerializeField] private float mapRiseSeconds = 2.5f;
        [SerializeField] private float mapRiseHeight = 0.4f;     // метров над чашкой
        [SerializeField] private float mapSpinDegPerSec = 15f;

        [Header("Аудио")]
        [SerializeField] private AudioSource crackAudio;   // нарастающий хруст
        [SerializeField] private AudioSource breachAudio;  // низкий гул прорыва

        private static readonly int CrackProgressId = Shader.PropertyToID("_CrackProgress");

        private MaterialPropertyBlock _mpb;
        private bool _shardsBurst;
        private bool _mapStarted;
        private float _mapTimer;
        private Vector3 _mapStartPos;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (holographicMapRoot != null) holographicMapRoot.SetActive(false);
        }

        protected override void OnTriggered()
        {
            _shardsBurst = false;
            _mapStarted = false;
            if (crackAudio != null) crackAudio.Play();
        }

        protected override void OnEffectUpdate(float dt)
        {
            // ---- Фаза 1: трещина ----
            float raw = Mathf.Clamp01(TimeSinceTriggered / crackDurationSeconds);
            float progress = crackCurve.Evaluate(raw);

            if (crackOverlayRenderer != null)
            {
                crackOverlayRenderer.GetPropertyBlock(_mpb);
                _mpb.SetFloat(CrackProgressId, progress);
                crackOverlayRenderer.SetPropertyBlock(_mpb);
            }

            if (!_shardsBurst && progress >= shardBurstAt)
            {
                _shardsBurst = true;
                if (shardParticles != null) shardParticles.Play();
                if (breachAudio != null) breachAudio.Play();
            }

            // ---- Фаза 2: голограмма после полного раскола ----
            if (raw >= 1f && !_mapStarted)
            {
                _mapStarted = true;
                _mapTimer = 0f;
                if (holographicMapRoot != null)
                {
                    _mapStartPos = Anchor.position;
                    holographicMapRoot.transform.position = _mapStartPos;
                    holographicMapRoot.SetActive(true);
                }
            }

            if (_mapStarted && holographicMapRoot != null)
            {
                _mapTimer += dt;
                float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_mapTimer / mapRiseSeconds));
                holographicMapRoot.transform.position =
                    _mapStartPos + Vector3.up * (mapRiseHeight * rise);
                holographicMapRoot.transform.Rotate(
                    0f, mapSpinDegPerSec * dt, 0f, Space.World);
                // Карта крутится бесконечно до конца сцены — MarkFinished не зовём,
                // финал сцены фиксирует NarrativeManager.OnSceneCompleted.
            }
        }

        protected override void OnCancelled()
        {
            if (shardParticles != null) shardParticles.Stop();
            if (crackAudio != null) crackAudio.Stop();
            if (breachAudio != null) breachAudio.Stop();
            if (holographicMapRoot != null) holographicMapRoot.SetActive(false);
        }
    }
}
