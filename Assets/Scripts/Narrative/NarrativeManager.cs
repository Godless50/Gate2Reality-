using System;
using UnityEngine;

namespace Gate2Reality.Narrative
{
    /// <summary>
    /// Ядро Trigger-Action графа Сцены 1 "Echoes in the Silence" + Guard Node.
    ///
    /// АРХИТЕКТУРНЫЕ ПРИНЦИПЫ (Android 15 / Pixel 9 / S26):
    ///  1. Zero-GC в Update: никаких new, LINQ, string-конкатенаций, боксинга.
    ///     Все события — struct, все массивы кэшированы в Awake.
    ///  2. Детектор-агностичность: менеджер НЕ знает про YOLO. Будущий
    ///     YoloObjectDetector (Step 2, Unity Sentis) просто вызывает
    ///     ReportDetection() каждый кадр инференса. Замена детектора = 0 правок здесь.
    ///  3. Guard Node — отдельная "сторожевая" подсистема: 45с простоя ->
    ///     эскалирующая лестница подсказок (аудио-маяк -> десатурация -> партиклы).
    ///  4. Throttling-friendly: вся логика — дешёвая арифметика таймеров;
    ///     тяжёлая работа (YOLO-инференс) живёт в другом скрипте и сама
    ///     дросселируется. Этот Update стоит < 0.01 мс.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NarrativeManager : MonoBehaviour
    {
        // =====================================================================
        // ГРАФ
        // =====================================================================
        [Header("Источник графа")]
        [Tooltip("Опциональный ScriptableObject-граф. Назначен — структура берётся из него " +
                 "(инлайн-массив ниже игнорируется, эффекты доливаются из биндингов). " +
                 "Пусто — работаем на инлайн-массиве как раньше.")]
        [SerializeField] private NarrativeGraphAsset graphAsset;

        [Tooltip("Привязка сценических ITriggerable к узлам ассета по индексу. " +
                 "Нужна только при использовании graphAsset (ассет не хранит ссылки на сцену).")]
        [SerializeField] private GraphTriggerableBinding[] graphTriggerableBindings;

        [Header("Trigger-Action Graph (Сцена 1: Chair -> Book -> Cup)")]
        [SerializeField] private NarrativeNode[] nodes;

        [Tooltip("Индекс стартового узла (The Chair)")]
        [SerializeField] private int entryNodeIndex = 0;

        [Tooltip("Стартовать сцену автоматически в Start(). Сними галку, если стартом " +
                 "управляет ProgressTracker (resume) или внешний код.")]
        [SerializeField] private bool autoStartOnStart = true;

        [Tooltip("Transform AR-камеры (XR Origin) — нужен пространственным условиям Сцены 2")]
        [SerializeField] private Transform playerCamera;

        /// <summary>Привязка эффектов сцены к узлу графа-ассета по индексу.</summary>
        [System.Serializable]
        private struct GraphTriggerableBinding
        {
            [Tooltip("Индекс узла в graphAsset")]
            public int nodeIndex;
            [Tooltip("Компоненты-ITriggerable из сцены для этого узла")]
            public MonoBehaviour[] behaviours;
        }

        // =====================================================================
        // GUARD NODE
        // =====================================================================
        [Header("Guard Node — спасение застрявшего игрока")]
        [Tooltip("ТЗ: 45 секунд простоя до первой подсказки")]
        [SerializeField] private float idleThresholdSeconds = 45f;

        [Tooltip("Интервал между ступенями эскалации подсказок")]
        [SerializeField] private float escalationIntervalSeconds = 15f;

        [Tooltip("Повтор аудио-маяка после полной эскалации")]
        [SerializeField] private float beaconRepeatSeconds = 30f;

        [Tooltip("ON: активный dwell-прогресс сбрасывает guard-простой (трактовка " +
                 "«секунды с последнего прогресса»). OFF (по умолчанию): guard считает " +
                 "время в узле — поведение as-tested сборки. На коротких dwell Главы I " +
                 "разница незаметна; проявляется на длинных dwell / залипании детекции.")]
        [SerializeField] private bool resetIdleOnProgress = false;

        // =====================================================================
        // ПУБЛИЧНЫЕ СОБЫТИЯ (подписка из Step 3/4: аудио, пост-процесс, VFX)
        // C#-события вместо UnityEvent: Invoke() без аллокаций и быстрее.
        // =====================================================================
        /// <summary>Узел активирован: (индекс узла, поза физического объекта).</summary>
        public event Action<int, Pose> OnNodeActivated;

        /// <summary>Сцена завершена (узел без исходящих рёбер отработал).</summary>
        public event Action OnSceneCompleted;

        /// <summary>Guard: запустить 3D-аудио-маяк у позиции целевого объекта.</summary>
        public event Action<Pose> OnAudioBeaconRequested;

        /// <summary>Guard: десатурировать всё, кроме целевого объекта (Vol. post-FX).</summary>
        public event Action OnDesaturateRequested;

        /// <summary>Guard: вернуть нормальную сатурацию (игрок нашёл цель).</summary>
        public event Action OnSaturationRestoreRequested;

        /// <summary>Guard: партиклы-проводник от камеры к цели.</summary>
        public event Action<Pose> OnGuideParticlesRequested;

        /// <summary>
        /// Ретрансляция СЫРОГО потока детекций (до фильтрации сцены/узла).
        /// Срабатывает на каждый результат детектора, даже когда сцена не идёт —
        /// NarrativeContextCollector копит по нему _seenMask, не завися от
        /// сборки Detection (иначе Narrative↔Detection даёт циклическую ссылку).
        /// </summary>
        public event Action<DetectionEvent> OnDetectionRelayed;

        // =====================================================================
        // ВНУТРЕННЕЕ СОСТОЯНИЕ (FSM)
        // =====================================================================
        private enum GuardStage : byte
        {
            Dormant = 0,        // игрок активен, таймер тикает
            BeaconFired = 1,    // ступень 1: аудио-маяк
            Desaturated = 2,    // ступень 2: мир обесцвечен
            ParticlesFired = 3  // ступень 3: партиклы; дальше — повтор маяка
        }

        private int _currentNodeIndex = -1;
        private NarrativeNode _currentNode;       // кэш, чтобы не индексировать массив в Update
        private float _idleTimer;                 // время в узле; при resetIdleOnProgress — с последнего прогресса
        private GuardStage _guardStage;
        private float _nextEscalationAt;          // абсолютное значение _idleTimer для след. ступени
        private bool _sceneRunning;
        private bool _targetSeenThisFrame;        // выставляет ReportDetection, читает Update
        private bool _autoStartSuppressed;        // ProgressTracker берёт старт на себя (resume)

        // =====================================================================
        // ПУБЛИЧНОЕ СОСТОЯНИЕ (тривиальные геттеры — нужны сейвам в любой сборке)
        // =====================================================================
        /// <summary>Индекс текущего активного узла (-1 до старта / после финала).</summary>
        public int CurrentNodeIndex => _currentNodeIndex;

        /// <summary>Идёт ли сейчас сцена.</summary>
        public bool IsSceneRunning => _sceneRunning;

        /// <summary>Количество узлов в активном графе.</summary>
        public int NodeCount => nodes != null ? nodes.Length : 0;

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        private void Awake()
        {
            // Источник графа: ассет (приоритет) или инлайн-массив.
            if (graphAsset != null)
            {
                nodes = graphAsset.CreateRuntimeNodes();
                entryNodeIndex = graphAsset.EntryNodeIndex;
                ApplyTriggerableBindings();
            }

            if (nodes == null) nodes = System.Array.Empty<NarrativeNode>();

            // Единственное место, где разрешены аллокации: прогрев кэшей.
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i].BuildCache();
            }
        }

        /// <summary>Доливает сценические ITriggerable в клонированные узлы ассета по индексу.</summary>
        private void ApplyTriggerableBindings()
        {
            if (graphTriggerableBindings == null) return;
            for (int i = 0; i < graphTriggerableBindings.Length; i++)
            {
                int idx = graphTriggerableBindings[i].nodeIndex;
                if (idx < 0 || idx >= nodes.Length)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogError($"[Gate2Reality] Биндинг эффектов ссылается на несуществующий узел {idx}");
#endif
                    continue;
                }
                nodes[idx].triggerableBehaviours = graphTriggerableBindings[i].behaviours;
            }
        }

        private void Start()
        {
            if (autoStartOnStart && !_autoStartSuppressed) StartScene();
        }

        /// <summary>Стартовать сцену со стартового узла графа.</summary>
        public void StartScene() => StartSceneAt(entryNodeIndex);

        /// <summary>Стартовать/возобновить сцену с произвольного узла (resume из сейва).</summary>
        public void StartSceneAt(int nodeIndex)
        {
            _sceneRunning = true;
            ResetIdleState();
            EnterNode(nodeIndex);
        }

        /// <summary>ProgressTracker вызывает в своём Awake, чтобы взять старт на себя (resume).</summary>
        public void SuppressAutoStart() => _autoStartSuppressed = true;

        // =====================================================================
        // ВХОДНАЯ ТОЧКА ДЛЯ ДЕТЕКТОРА (Step 2: YOLO via Unity Sentis)
        // Вызывается на каждый результат инференса. Struct по 'in' — ноль копий.
        // =====================================================================
        public void ReportDetection(in DetectionEvent detection)
        {
            // Ретранслируем сырой поток ДО любых guard'ов: подписчики контекста
            // (NarrativeContextCollector) должны видеть детекции всегда.
            OnDetectionRelayed?.Invoke(detection);

            if (!_sceneRunning || _currentNode == null) return;

            // Вся фильтрация (метка, confidence, габариты) — в условии узла.
            if (!_currentNode.condition.MatchesDetection(in detection)) return;

            // Валидная детекция цели: запоминаем позу (нужна и guard-маяку),
            // помечаем кадр. Dwell-аккумуляция — в Update, по deltaTime.
            _currentNode.LastSeenPose = detection.WorldPose;
            _targetSeenThisFrame = true;
        }

        // =====================================================================
        // UPDATE: только таймеры. Стоимость — копейки, throttling-friendly.
        // =====================================================================
        private void Update()
        {
            if (!_sceneRunning || _currentNode == null) return;

            float dt = Time.deltaTime;

            // ---- 0. Пространственные условия (Сцена 2): оцениваем сами,
            //      каждый кадр. Дешёвая векторная математика, без рейкастов.
            //      Semantic-узлы этот блок пропускают — их кормит детектор.
            if (_currentNode.condition.type != ConditionType.SemanticDetection &&
                _currentNode.condition.EvaluateSpatial(playerCamera, out Pose spatialAnchor))
            {
                _currentNode.LastSeenPose = spatialAnchor;
                _targetSeenThisFrame = true;
            }

            // ---- 1. Dwell-логика: цель должна продержаться в кадре N секунд ----
            if (_targetSeenThisFrame)
            {
                _targetSeenThisFrame = false; // потребляем флаг
                _currentNode.DwellAccumulator += dt;

                if (_currentNode.DwellAccumulator >= _currentNode.dwellTimeSeconds)
                {
                    ActivateCurrentNode();
                    return; // узел сменился — guard-таймер уже сброшен
                }

                // Опционально: активный прогресс трактуем как «не застрял» и
                // сбрасываем guard-простой (поведение из описания _idleTimer:
                // «секунды с последнего прогресса»). По умолчанию выключено —
                // ноль изменений к as-tested сборке (guard считает время в узле).
                if (resetIdleOnProgress) ResetGuardDueToActivity();
            }
            else
            {
                // Объект потерян — мягкий распад аккумулятора (а не жёсткий сброс):
                // прощает дрожание YOLO-детекций на 1-2 кадра.
                _currentNode.DwellAccumulator = Mathf.Max(0f, _currentNode.DwellAccumulator - dt * 0.5f);
            }

            // ---- 2. Guard Node: эскалация подсказок ----
            TickGuard(dt);
        }

        // =====================================================================
        // GUARD NODE
        // =====================================================================
        private void TickGuard(float dt)
        {
            _idleTimer += dt;

            switch (_guardStage)
            {
                case GuardStage.Dormant:
                    if (_idleTimer >= idleThresholdSeconds)
                    {
                        ActivateAudioBeacon();
                        _guardStage = GuardStage.BeaconFired;
                        _nextEscalationAt = _idleTimer + escalationIntervalSeconds;
                    }
                    break;

                case GuardStage.BeaconFired:
                    if (_idleTimer >= _nextEscalationAt)
                    {
                        DesaturateNonTargetObjects();
                        _guardStage = GuardStage.Desaturated;
                        _nextEscalationAt = _idleTimer + escalationIntervalSeconds;
                    }
                    break;

                case GuardStage.Desaturated:
                    if (_idleTimer >= _nextEscalationAt)
                    {
                        SpawnGuideParticles();
                        _guardStage = GuardStage.ParticlesFired;
                        _nextEscalationAt = _idleTimer + beaconRepeatSeconds;
                    }
                    break;

                case GuardStage.ParticlesFired:
                    // Терминальная ступень: пингуем маяком, пока игрок не найдёт цель.
                    if (_idleTimer >= _nextEscalationAt)
                    {
                        ActivateAudioBeacon();
                        _nextEscalationAt = _idleTimer + beaconRepeatSeconds;
                    }
                    break;
            }
        }

        /// <summary>Ступень 1: 3D-аудио-маяк у последней известной позы цели
        /// (или у нуля сцены, если объект ещё ни разу не видели — тогда
        /// аудио-система Step 3 проиграет ненаправленный шёпот).</summary>
        private void ActivateAudioBeacon()
        {
            OnAudioBeaconRequested?.Invoke(_currentNode.LastSeenPose);
        }

        /// <summary>Ступень 2: пост-эффект десатурации всего, кроме цели.
        /// Реализация (URP Volume + слой-маска) придёт в Step 5.</summary>
        private void DesaturateNonTargetObjects()
        {
            OnDesaturateRequested?.Invoke();
        }

        /// <summary>Ступень 3: партиклы-проводник, летящие к цели.</summary>
        private void SpawnGuideParticles()
        {
            OnGuideParticlesRequested?.Invoke(_currentNode.LastSeenPose);
        }

        // =====================================================================
        // ПЕРЕХОДЫ ГРАФА
        // =====================================================================
        private void ActivateCurrentNode()
        {
            NarrativeNode node = _currentNode;

            // Запускаем все ITriggerable узла, передавая позу физического объекта.
            ITriggerable[] triggerables = node.CachedTriggerables;
            for (int i = 0; i < triggerables.Length; i++)
            {
                triggerables[i]?.Trigger(in node.LastSeenPose);
            }

            OnNodeActivated?.Invoke(_currentNodeIndex, node.LastSeenPose);

            // Если guard успел обесцветить мир — возвращаем краски.
            if (_guardStage >= GuardStage.Desaturated)
            {
                OnSaturationRestoreRequested?.Invoke();
            }

            // Переход. Сцена 1 линейна — берём первое ребро. Ветвящиеся
            // переходы (выбор ребра по условию) добавим, когда появится
            // второй сценарий — рантайм уже готов к массиву рёбер.
            int[] next = node.nextNodeIndices;
            if (next == null || next.Length == 0)
            {
                CompleteScene();
            }
            else
            {
                EnterNode(next[0]);
            }
        }

        /// <summary>
        /// Привязка процедурной цели к узлу (Сцена 2: EchoZonePlacer создаёт
        /// якоря зон в рантайме). Если узел уже активен — guard-маяк сразу
        /// узнаёт, куда указывать.
        /// </summary>
        public void SetNodeRuntimeTarget(int nodeIndex, Transform target)
        {
            if (nodeIndex < 0 || nodeIndex >= nodes.Length) return;
            nodes[nodeIndex].condition.runtimeTarget = target;

            if (nodes[nodeIndex] == _currentNode &&
                nodes[nodeIndex].condition.TryGetKnownAnchor(out Pose anchor))
            {
                nodes[nodeIndex].LastSeenPose = anchor;
            }
        }

        private void EnterNode(int index)
        {
            if (index < 0 || index >= nodes.Length)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"[Gate2Reality] Некорректный индекс узла: {index}");
#endif
                return;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Gate2Reality] -> Узел '{nodes[index].nodeName}' ({nodes[index].condition.Describe()})");
#endif
            _currentNodeIndex = index;
            _currentNode = nodes[index];
            _currentNode.DwellAccumulator = 0f;

            // Пространственные узлы знают якорь ДО первого «попадания» —
            // праймим LastSeenPose, чтобы guard-маяк с первой же секунды
            // указывал в реальную точку, а не в ноль сцены.
            if (_currentNode.condition.TryGetKnownAnchor(out Pose known))
            {
                _currentNode.LastSeenPose = known;
            }

            ResetIdleState();
        }

        private void CompleteScene()
        {
            _sceneRunning = false;
            _currentNode = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[Gate2Reality] Сцена 1 завершена: The Breach открыт.");
#endif
            OnSceneCompleted?.Invoke();
        }

        private void ResetIdleState()
        {
            _idleTimer = 0f;
            _guardStage = GuardStage.Dormant;
            _nextEscalationAt = 0f;
        }

        /// <summary>
        /// Сброс guard из-за активности игрока (прогресс dwell при resetIdleOnProgress).
        /// Если guard уже визуально обесцветил мир — возвращаем краски, как и при
        /// активации узла, иначе десатурация «зависнет» до смены узла.
        /// </summary>
        private void ResetGuardDueToActivity()
        {
            if (_guardStage >= GuardStage.Desaturated)
                OnSaturationRestoreRequested?.Invoke();
            ResetIdleState();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // =====================================================================
        // DEV-СНИМОК ДЛЯ ОТЛАДОЧНОГО HUD (DetectionDebugHud).
        // Компилируется только в Editor/Development Build — в релизе кода нет,
        // горячий путь не затронут.
        // =====================================================================
        public readonly struct DebugSnapshot
        {
            public readonly int NodeIndex;
            public readonly string NodeName;
            public readonly float Dwell;
            public readonly float DwellTarget;
            public readonly float IdleTimer;
            public readonly float IdleThreshold;
            public readonly int GuardStage;        // 0..3
            public readonly bool SceneRunning;

            public DebugSnapshot(int nodeIndex, string nodeName, float dwell, float dwellTarget,
                                 float idleTimer, float idleThreshold, int guardStage, bool sceneRunning)
            {
                NodeIndex = nodeIndex;
                NodeName = nodeName;
                Dwell = dwell;
                DwellTarget = dwellTarget;
                IdleTimer = idleTimer;
                IdleThreshold = idleThreshold;
                GuardStage = guardStage;
                SceneRunning = sceneRunning;
            }
        }

        public DebugSnapshot GetDebugSnapshot() => new DebugSnapshot(
            _currentNodeIndex,
            _currentNode != null ? _currentNode.nodeName : "<none>",
            _currentNode != null ? _currentNode.DwellAccumulator : 0f,
            _currentNode != null ? _currentNode.dwellTimeSeconds : 0f,
            _idleTimer,
            idleThresholdSeconds,
            (int)_guardStage,
            _sceneRunning);
#endif
    }
}
