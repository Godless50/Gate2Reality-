using UnityEngine;

namespace Gate2Reality.Persistence
{
    using Gate2Reality.Narrative;
    using Gate2Reality.Effects;

    /// <summary>
    /// Связывает граф со хранилищем прогресса: пишет сейв на каждой активации
    /// узла и на пересечении портала, а на старте — при наличии сейва — берёт
    /// автозапуск сцены на себя и возобновляет с сохранённого узла.
    ///
    /// ПОРЯДОК СТАРТА: в Awake читаем сейв и зовём SuppressAutoStart(); в Start —
    /// если меню не взяло управление (DeferToMenu) — запускаем релокализацию и
    /// StartSceneAt. Если меню есть — ждём явного BeginResume/BeginFreshStart.
    /// Awake всегда предшествует Start — гонки нет.
    ///
    /// ЗАПИСЬ ЯКОРЕЙ: на каждом OnNodeActivated вызываем AnchorSerializer.Capture,
    /// если AnchorRegistry заполнен. Сейв включает относительные позы объектов
    /// комнаты — релокализатор использует их при следующем запуске.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProgressTracker : MonoBehaviour
    {
        [Header("Связи")]
        [SerializeField] private NarrativeManager narrativeManager;

        [Header("Конфигурация")]
        [Tooltip("Идентификатор главы — сейв привязан к нему")]
        [SerializeField] private int chapterId = 1;
        [Tooltip("Возобновлять с сохранённого узла при наличии сейва этой главы")]
        [SerializeField] private bool resumeOnStart = true;
        [Tooltip("Чистить сейв при завершении сцены (глава пройдена с нуля в след. раз)")]
        [SerializeField] private bool clearOnSceneCompleted = false;

        [Header("Якоря (v2)")]
        [Tooltip("Регистр живых якорей — заполняется эффектами/плейсерами во время игры")]
        [SerializeField] private AnchorRegistry anchorRegistry;
        [Tooltip("Метка опорного якоря для системы отсчёта комнаты")]
        [SerializeField] private NarrativeLabel referenceAnchorLabel = NarrativeLabel.Chair;
        [Tooltip("Релокализатор L1→L2→L3. Если не задан — resume идёт без восстановления якорей.")]
        [SerializeField] private OfflineAnchorRelocalizer relocalizer;

        private ProgressData _data;
        private bool _shouldResume;
        private int _resumeNodeIndex;

        // Set by MainMenuController.Awake() when it wants to own the start moment.
        // Checked in Start() — all Awakes run before any Start, so no race condition.
        private bool _menuDecisionPending;
        private bool _resumeStarted; // guard against double-invoke

        private void Awake()
        {
            _data = new ProgressData { chapter = chapterId };

            if (resumeOnStart && ProgressStore.TryLoad(out ProgressData saved) &&
                saved.chapter == chapterId)
            {
                _data = saved;
                _shouldResume = true;
                _resumeNodeIndex = Mathf.Max(0, saved.nodeIndex);
                if (narrativeManager != null) narrativeManager.SuppressAutoStart();
            }
        }

        private void OnEnable()
        {
            if (narrativeManager != null)
            {
                narrativeManager.OnNodeActivated += HandleNodeActivated;
                narrativeManager.OnSceneCompleted += HandleSceneCompleted;
            }
            CrossingTransitionEffect.OnCrossedOver += HandleCrossedOver;
        }

        private void OnDisable()
        {
            if (narrativeManager != null)
            {
                narrativeManager.OnNodeActivated -= HandleNodeActivated;
                narrativeManager.OnSceneCompleted -= HandleSceneCompleted;
            }
            CrossingTransitionEffect.OnCrossedOver -= HandleCrossedOver;
        }

        private void Start()
        {
            if (_menuDecisionPending) return; // MainMenuController drives the start
            if (!_shouldResume || narrativeManager == null) return;
            RunResumeLogic();
        }

        // =====================================================================
        // ПУБЛИЧНОЕ API — меню / continue-screen
        // =====================================================================

        /// <summary>
        /// Вызывается MainMenuController.Awake() чтобы предотвратить авто-resume
        /// из ProgressTracker.Start(). Меню само вызовет BeginResume или BeginFreshStart.
        /// </summary>
        public void DeferToMenu() => _menuDecisionPending = true;

        /// <summary>
        /// Вызывается MainMenuController по нажатию «Продолжить».
        /// Запускает релокализацию + StartSceneAt.
        /// </summary>
        public void BeginResume()
        {
            if (_resumeStarted) return;
            _resumeStarted = true;
            _menuDecisionPending = false;

            if (_shouldResume && narrativeManager != null)
                RunResumeLogic();
            else if (narrativeManager != null)
                narrativeManager.StartScene(); // no save but menu asked to continue — just start
        }

        /// <summary>
        /// Вызывается MainMenuController по нажатию «Новая игра».
        /// Сбрасывает сейв и стартует сцену с нуля.
        /// </summary>
        public void BeginFreshStart()
        {
            if (_resumeStarted) return;
            _resumeStarted = true;
            _menuDecisionPending = false;

            ClearProgress();
            if (narrativeManager != null) narrativeManager.StartScene();
        }

        // =====================================================================
        // ЗАПИСЬ ПРОГРЕССА
        // =====================================================================
        private void HandleNodeActivated(int nodeIndex, Pose pose)
        {
            _data.chapter = chapterId;
            _data.nodeIndex = narrativeManager != null
                ? narrativeManager.CurrentNodeIndex
                : nodeIndex;

            if (anchorRegistry != null && anchorRegistry.All.Count > 0)
                AnchorSerializer.Capture(anchorRegistry, referenceAnchorLabel, _data);

            ProgressStore.Save(_data);
        }

        private void HandleCrossedOver()
        {
            _data.crossedOver = true;

            if (anchorRegistry != null && anchorRegistry.All.Count > 0)
                AnchorSerializer.Capture(anchorRegistry, referenceAnchorLabel, _data);

            ProgressStore.Save(_data);
        }

        private void HandleSceneCompleted()
        {
            if (clearOnSceneCompleted)
            {
                ProgressStore.Clear();
            }
            else
            {
                _data.nodeIndex = narrativeManager != null
                    ? narrativeManager.NodeCount
                    : _data.nodeIndex;
                ProgressStore.Save(_data);
            }
        }

        // =====================================================================
        // ВНУТРЕННЯЯ ЛОГИКА
        // =====================================================================
        private void RunResumeLogic()
        {
            if (relocalizer != null)
            {
                relocalizer.Relocalize(_data, result =>
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"[Gate2Reality] Resume L{result.Level}: глава {chapterId}, узел {_resumeNodeIndex}.");
#endif
                    if (result.NodeAnchors != null)
                    {
                        foreach (var kvp in result.NodeAnchors)
                            narrativeManager.SetNodeRuntimeTarget(kvp.Key, kvp.Value);
                    }
                    narrativeManager.StartSceneAt(_resumeNodeIndex);
                });
            }
            else
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[Gate2Reality] Resume (без релокализации): глава {chapterId}, узел {_resumeNodeIndex}.");
#endif
                narrativeManager.StartSceneAt(_resumeNodeIndex);
            }
        }

        // =====================================================================
        // ПУБЛИЧНОЕ API — утилиты
        // =====================================================================
        public void ClearProgress()
        {
            ProgressStore.Clear();
            _data = new ProgressData { chapter = chapterId };
            _shouldResume = false;
            anchorRegistry?.Clear();
        }

        public bool TryPeekSave(out ProgressData data) => ProgressStore.TryLoad(out data);
    }
}
