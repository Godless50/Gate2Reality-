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
    /// ПОРЯДОК СТАРТА (важно): в Awake (до любого Start) читаем сейв и зовём
    /// NarrativeManager.SuppressAutoStart(), чтобы менеджер не стартовал сам с
    /// нуля. В своём Start уже зовём StartSceneAt(savedNode). Awake всегда
    /// предшествует Start — гонки нет.
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

        private ProgressData _data;
        private bool _shouldResume;
        private int _resumeNodeIndex;

        private void Awake()
        {
            _data = new ProgressData { chapter = chapterId };

            if (resumeOnStart && ProgressStore.TryLoad(out ProgressData saved) && saved.chapter == chapterId)
            {
                _data = saved;
                _shouldResume = true;
                _resumeNodeIndex = Mathf.Max(0, saved.nodeIndex);
                // Забираем автозапуск у менеджера ДО его Start().
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
            if (_shouldResume && narrativeManager != null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[Gate2Reality] Resume: глава {chapterId}, узел {_resumeNodeIndex}.");
#endif
                narrativeManager.StartSceneAt(_resumeNodeIndex);
            }
        }

        // =====================================================================
        // ЗАПИСЬ ПРОГРЕССА
        // =====================================================================
        private void HandleNodeActivated(int nodeIndex, Pose pose)
        {
            // Сохраняем СЛЕДУЮЩУЮ цель: при возобновлении игрок продолжает с
            // узла, который ещё не пройден. Если узел был терминальным — граф
            // сам уйдёт в OnSceneCompleted, тут просто фиксируем достигнутое.
            _data.chapter = chapterId;
            _data.nodeIndex = narrativeManager != null ? narrativeManager.CurrentNodeIndex : nodeIndex;
            ProgressStore.Save(_data);
        }

        private void HandleCrossedOver()
        {
            _data.crossedOver = true;
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
                // Фиксируем «глава пройдена»: узел за пределами графа = маркер финала.
                _data.nodeIndex = narrativeManager != null ? narrativeManager.NodeCount : _data.nodeIndex;
                ProgressStore.Save(_data);
            }
        }

        // =====================================================================
        // ПУБЛИЧНОЕ API (меню/настройки)
        // =====================================================================
        /// <summary>Полный сброс прогресса (кнопка «Новая игра»).</summary>
        public void ClearProgress()
        {
            ProgressStore.Clear();
            _data = new ProgressData { chapter = chapterId };
        }

        /// <summary>Текущий снимок (для меню «Продолжить»: показать главу/прогресс).</summary>
        public bool TryPeekSave(out ProgressData data) => ProgressStore.TryLoad(out data);
    }
}
