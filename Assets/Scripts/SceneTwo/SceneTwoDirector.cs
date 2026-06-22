using System;
using UnityEngine;

namespace Gate2Reality.SceneTwo
{
    using Gate2Reality.Narrative;

    /// <summary>
    /// Режиссёр Сцены 2 «Картограф». Эффекты зон (PortalWindowEffect,
    /// EchoSurfaceEffect) запускает сам граф — здесь только то, что эффекты
    /// знать не должны: цепочка шёпотов «с той стороны» и финал главы.
    ///
    /// ПРЕФЕТЧ-ЦЕПОЧКА (та же философия, что в Сцене 1, но конвейером):
    ///   размещение зон   -> заказ текста для зоны 1 (игрок ещё идёт)
    ///   активация зоны 1 -> показ текста 1 + заказ текста для зоны 2
    ///   активация зоны 2 -> показ текста 2 + заказ текста для портала
    ///   активация портала-> показ финального текста
    /// Семантика вытеснения генератора (новый запрос синхронно разрешает
    /// старый фолбэком ИЗ ЕГО пула) гарантирует: к моменту захвата текст
    /// предыдущей зоны всегда не-null — гонка невозможна по построению.
    /// КРИТИЧЕН ПОРЯДОК в HandleNodeActivated: сперва заказать следующий
    /// (вытеснение дольёт текущий слот), ПОТОМ захватить слот под показ.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneTwoDirector : MonoBehaviour
    {
        [Header("Ядро")]
        [SerializeField] private NarrativeManager narrativeManager;
#if UNITY_ANDROID && !UNITY_EDITOR
        [SerializeField] private EchoZonePlacer zonePlacer;
#endif

        [Header("Шёпоты с той стороны")]
        [SerializeField] private OnDeviceNarrativeGenerator narrativeGenerator;
        [SerializeField] private NarrativeContextCollector contextCollector;
        [SerializeField] private Gate2Reality.UI.WhisperSubtitleController subtitleController;
        [Tooltip("Пауза между активацией зоны и субтитром: рябь/окно успевают раскрыться")]
        [SerializeField] private float subtitleDelaySeconds = 1.2f;

        [Header("Узлы графа")]
        [SerializeField] private int wallEchoNodeIndex = 3;
        [SerializeField] private int surfaceEchoNodeIndex = 4;
        [SerializeField] private int portalNodeIndex = 5;
        [Tooltip("Узел «Пересечение»: Proximity ~0.5м — игрок физически входит в дверь")]
        [SerializeField] private int crossingNodeIndex = 6;

        [Header("Финал главы")]
        [SerializeField] private AudioSource chapterStinger; // финальный аккорд

        /// <summary>Глава I пройдена: подписка для меню/сейвов/Сцены 3.</summary>
        public event Action OnChapterCompleted;

        // Сюжеты шёпотов (короткие — это вход для MLLM, не текст игроку)
        private const string WallSubject = "первое окно, открывшееся в стене в зеркальную комнату";
        private const string SurfaceSubject = "светящиеся круги, расходящиеся по полу из мира-отражения";
        private const string PortalSubject = "дверь между комнатой и её отражением, распахнувшаяся до конца";

        private string _prefetchedText; // слот конвейера
        private string _textToShow;
        private bool _subtitlePending;
        private float _subtitleDueAt;

        private void OnEnable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            zonePlacer.OnZonesPlaced += HandleZonesPlaced;
#endif
            narrativeManager.OnNodeActivated += HandleNodeActivated;
            narrativeManager.OnSceneCompleted += HandleChapterCompleted;
        }

        private void OnDisable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            zonePlacer.OnZonesPlaced -= HandleZonesPlaced;
#endif
            narrativeManager.OnNodeActivated -= HandleNodeActivated;
            narrativeManager.OnSceneCompleted -= HandleChapterCompleted;
        }

        // =====================================================================
        // КОНВЕЙЕР ШЁПОТОВ
        // =====================================================================
#if UNITY_ANDROID && !UNITY_EDITOR
        private void HandleZonesPlaced(System.Collections.Generic.IReadOnlyList<EchoZonePlacer.PlacedZone> zones)
        {
            // Узел «Пересечение» делит якорь с дверью-порталом: дверь
            // открывается взглядом (узел 5), а пересекается ногами (узел 6).
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].Kind == EchoZonePlacer.ZoneKind.PortalWall)
                {
                    narrativeManager.SetNodeRuntimeTarget(crossingNodeIndex, zones[i].Anchor);
                    break;
                }
            }

            Prefetch(WallSubject); // игрок только увидел карту — время пошло
        }
#endif

        private void HandleNodeActivated(int nodeIndex, Pose pose)
        {
            if (nodeIndex == wallEchoNodeIndex)
            {
                Prefetch(SurfaceSubject);   // 1) вытеснение доливает слот зоны 1
                ScheduleSubtitle();         // 2) захват слота под показ
            }
            else if (nodeIndex == surfaceEchoNodeIndex)
            {
                Prefetch(PortalSubject);
                ScheduleSubtitle();
            }
            else if (nodeIndex == portalNodeIndex)
            {
                // Последний узел: заказывать нечего, но слот может быть пуст,
                // если генерация портального текста ещё в полёте — страховочный
                // повторный заказ (вытеснит сам себя в фолбэк мгновенно).
                if (_prefetchedText == null) Prefetch(PortalSubject);
                ScheduleSubtitle();
            }
        }

        private void Prefetch(string subject)
        {
            if (narrativeGenerator == null || contextCollector == null) return;
            narrativeGenerator.RequestMirrorWhisper(
                subject,
                contextCollector.Capture(NarrativeLabel.None),
                text => _prefetchedText = text);
        }

        private void ScheduleSubtitle()
        {
            _textToShow = _prefetchedText; // после Prefetch слот гарантированно полон
            _prefetchedText = null;        // освобождаем под следующий ответ
            if (_textToShow == null) return; // генератора нет — глава идёт без субтитров

            _subtitlePending = true;
            _subtitleDueAt = Time.unscaledTime + subtitleDelaySeconds;
        }

        private void Update()
        {
            if (!_subtitlePending || Time.unscaledTime < _subtitleDueAt) return;
            _subtitlePending = false;
            if (subtitleController != null) subtitleController.Show(_textToShow);
        }

        // =====================================================================
        // ФИНАЛ
        // =====================================================================
        private void HandleChapterCompleted()
        {
            if (chapterStinger != null) chapterStinger.Play();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[Gate2Reality] ГЛАВА I ЗАВЕРШЕНА: дверь открыта.");
#endif
            OnChapterCompleted?.Invoke();
        }
    }
}
