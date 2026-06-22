using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gate2Reality.Effects
{
    /// <summary>
    /// Финальный узел графа «Пересечение»: игрок физически входит в
    /// дверь-портал (Proximity ~0.5м). Классический трюк кино-переходов:
    ///
    ///   FlashIn (0.35с) — экран заливает холодная вспышка;
    ///   Hold (0.6с)     — ПОД непрозрачной вспышкой мир подменяется:
    ///                     включается пост-профиль изнанки (Volume weight=1),
    ///                     стартует эмбиент той стороны, летит OnCrossedOver.
    ///                     Глаз не видит шов подмены — вспышка его прячет;
    ///   Reveal (2.2с)   — вспышка опадает: игрок стоит в СВОЕЙ комнате,
    ///                     но она уже не своя.
    ///
    /// ВАЖНО ПРИ НАСТРОЙКЕ: snapToAnchor в базовом компоненте = OFF
    /// (эффект экранный, к якорю двери его прибивать не нужно).
    ///
    /// О ТАЙМИНГЕ СТИНГЕРА: граф не ждёт эффектов — OnSceneCompleted (и
    /// стингер главы в SceneTwoDirector) срабатывает в момент триггера
    /// узла, то есть ПОД нарастающей вспышкой. Это задумано: аккорд главы
    /// и пик вспышки совпадают.
    /// </summary>
    public sealed class CrossingTransitionEffect : TriggerableEffectBase
    {
        [Header("Вспышка (Canvas Screen Space - Overlay, белая Image)")]
        [SerializeField] private CanvasGroup flashOverlay;
        [SerializeField] private float flashInSeconds = 0.35f;
        [SerializeField] private float holdSeconds = 0.6f;
        [SerializeField] private float revealSeconds = 2.2f;

        [Header("Та сторона")]
        [Tooltip("Global Volume с пост-профилем изнанки (холодный LUT, виньетка). Weight по умолчанию 0")]
#if !UNITY_EDITOR
        [SerializeField] private Volume invertedSideVolume;
#endif
        [SerializeField] private AudioSource crossingSwell;     // нарастающий аккорд входа
        [SerializeField] private AudioSource otherSideAmbience; // мир после перехода, loop

        /// <summary>Игрок пересёк порог (момент подмены мира, под пиком вспышки).
        /// Точка подписки для Сцены 3 / сейвов / аналитики главы.</summary>
        public static event Action OnCrossedOver;

        private enum Phase : byte { FlashIn, Hold, Reveal, Done }
        private Phase _phase;
        private float _phaseTimer;
        private bool _crossFired;

        private void Awake()
        {
            if (flashOverlay != null)
            {
                flashOverlay.alpha = 0f;
                flashOverlay.gameObject.SetActive(false);
            }
#if !UNITY_EDITOR
            if (invertedSideVolume != null) invertedSideVolume.weight = 0f;
#endif
        }

        protected override void OnTriggered()
        {
            _phase = Phase.FlashIn;
            _phaseTimer = 0f;
            _crossFired = false;

            if (flashOverlay != null)
            {
                flashOverlay.alpha = 0f;
                flashOverlay.gameObject.SetActive(true);
            }
            if (crossingSwell != null) crossingSwell.Play();
        }

        protected override void OnEffectUpdate(float dt)
        {
            _phaseTimer += dt;

            switch (_phase)
            {
                case Phase.FlashIn:
                    if (flashOverlay != null)
                        flashOverlay.alpha = Mathf.Clamp01(_phaseTimer / flashInSeconds);
                    if (_phaseTimer >= flashInSeconds) Next(Phase.Hold);
                    break;

                case Phase.Hold:
                    if (!_crossFired)
                    {
                        _crossFired = true;
                        // Подмена мира под непрозрачной вспышкой.
#if !UNITY_EDITOR
                        if (invertedSideVolume != null) invertedSideVolume.weight = 1f;
#endif
                        if (otherSideAmbience != null) otherSideAmbience.Play();
                        OnCrossedOver?.Invoke();
                    }
                    if (_phaseTimer >= holdSeconds) Next(Phase.Reveal);
                    break;

                case Phase.Reveal:
                    if (flashOverlay != null)
                        flashOverlay.alpha = 1f - Mathf.Clamp01(_phaseTimer / revealSeconds);
                    if (_phaseTimer >= revealSeconds)
                    {
                        if (flashOverlay != null) flashOverlay.gameObject.SetActive(false);
                        Next(Phase.Done);
                        MarkFinished();
                    }
                    break;
            }
        }

        private void Next(Phase p)
        {
            _phase = p;
            _phaseTimer = 0f;
        }

        protected override void OnCancelled()
        {
            if (flashOverlay != null)
            {
                flashOverlay.alpha = 0f;
                flashOverlay.gameObject.SetActive(false);
            }
#if !UNITY_EDITOR
            if (invertedSideVolume != null) invertedSideVolume.weight = 0f;
#endif
            if (crossingSwell != null) crossingSwell.Stop();
            if (otherSideAmbience != null) otherSideAmbience.Stop();
        }
    }
}
