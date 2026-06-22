using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gate2Reality.ChapterTwo
{
    using Gate2Reality.Narrative;
    using Gate2Reality.Detection;
    using Gate2Reality.Effects;
    using Gate2Reality.Persistence;

    /// <summary>
    /// Загрузчик Главы II «По ту сторону». Точка входа — статическое событие
    /// CrossingTransitionEffect.OnCrossedOver (игрок под пиком вспышки пересёк
    /// портал). Глава II — та же физическая комната, но правила перевёрнуты;
    /// технически это переиспользование ядра: ОТДЕЛЬНЫЙ NarrativeManager со своим
    /// графом-ассетом (NarrativeGraphAsset), включаемый здесь.
    ///
    /// ПОРЯДОК ВКЛЮЧЕНИЯ (важен для пространственных узлов):
    ///   1) активируем корень Главы II (его NarrativeManager НЕ автостартует —
    ///      autoStartOnStart=false в инспекторе);
    ///   2) переносим «запомненные» якоря Главы I в узлы графа Главы II
    ///      (SetNodeRuntimeTarget) — те же объекты/зоны реальной комнаты;
    ///   3) только теперь StartScene(): узлы входят с уже привязанными целями.
    ///
    /// PRIVACY: обет «гасим хоррор при людях в кадре» действует всю игру.
    /// Детектор остаётся в person-only (1 Гц) — Глава II работает на
    /// пространственных условиях (Proximity / AvertedGaze), а не на тяжёлом YOLO.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChapterTwoDirector : MonoBehaviour
    {
        [Header("Корень Главы II (изначально выключен в сцене)")]
        [SerializeField] private GameObject chapterTwoRoot;
        [SerializeField] private NarrativeManager chapterTwoNarrative;

        [Header("База изнанки")]
        [Tooltip("Global Volume холодного пост-профиля. CrossingTransitionEffect уже " +
                 "ставит weight=1 под вспышкой; здесь подтверждаем как базовое состояние.")]
#if !UNITY_EDITOR
        [SerializeField] private Volume invertedSideVolume;
#endif
        [SerializeField] private AudioSource invertedAmbience;

        [Header("Перенос якорей Главы I -> узлы Главы II")]
        [Tooltip("Регистр живых якорей Главы I (заполняется OfflineAnchorRelocalizer / эффектами)")]
        [SerializeField] private AnchorRegistry ch1AnchorRegistry;
        [Tooltip("Ручной фолбэк/дебаг: перенесённые Transform (если регистр пуст)")]
        [SerializeField] private Transform[] carriedAnchors;
        [Tooltip("Индексы узлов для ручного фолбэка (1:1 к carriedAnchors)")]
        [SerializeField] private int[] carriedAnchorNodeIndices;

        [Header("Privacy")]
        [Tooltip("Детектор: подтверждаем person-only на всю главу (обет приватности)")]
#if UNITY_ANDROID && !UNITY_EDITOR
        [SerializeField] private YoloObjectDetector detector;
#endif

        /// <summary>Глава II началась — точка подписки для аудио/аналитики/сейвов.</summary>
        public static event Action OnChapterTwoBegan;

        private bool _began;

        private void OnEnable() => CrossingTransitionEffect.OnCrossedOver += HandleCrossedOver;
        private void OnDisable() => CrossingTransitionEffect.OnCrossedOver -= HandleCrossedOver;

        private void HandleCrossedOver()
        {
            if (_began) return; // строго одноразово
            _began = true;

            // 1) Корень Главы II. Его NarrativeManager.Awake выполнится здесь,
            //    но без автостарта (autoStartOnStart=false) — старт ниже, вручную.
            if (chapterTwoRoot != null) chapterTwoRoot.SetActive(true);

            // База изнанки как постоянное состояние.
#if !UNITY_EDITOR
            if (invertedSideVolume != null) invertedSideVolume.weight = 1f;
#endif
            if (invertedAmbience != null && !invertedAmbience.isPlaying) invertedAmbience.Play();

            // Обет приватности продолжает действовать.
#if UNITY_ANDROID && !UNITY_EDITOR
            if (detector != null) detector.SetPersonOnlyMode(true);
#endif

            // 2) Перенос якорей реальной комнаты в узлы Главы II.
            if (chapterTwoNarrative != null) ApplyCarriedAnchors();

            // 3) Старт графа Главы II с уже привязанными целями.
            if (chapterTwoNarrative != null) chapterTwoNarrative.StartScene();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[Gate2Reality] ГЛАВА II началась: комната та же, правила — нет.");
#endif
            OnChapterTwoBegan?.Invoke();
        }

        private void ApplyCarriedAnchors()
        {
            // Primary: live registry from OfflineAnchorRelocalizer / Chapter I effects
            if (ch1AnchorRegistry != null && ch1AnchorRegistry.All.Count > 0)
            {
                var all = ch1AnchorRegistry.All;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].t != null)
                        chapterTwoNarrative.SetNodeRuntimeTarget(all[i].nodeIndex, all[i].t);
                }
                return;
            }

            // Manual fallback (inspector-set, or debug override)
            if (carriedAnchors == null || carriedAnchorNodeIndices == null) return;
            int n = Mathf.Min(carriedAnchors.Length, carriedAnchorNodeIndices.Length);
            for (int i = 0; i < n; i++)
            {
                if (carriedAnchors[i] == null) continue;
                chapterTwoNarrative.SetNodeRuntimeTarget(carriedAnchorNodeIndices[i], carriedAnchors[i]);
            }
        }
    }
}
