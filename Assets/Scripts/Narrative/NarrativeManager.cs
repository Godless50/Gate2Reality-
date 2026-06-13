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
        [Header("Trigger-Action Graph (Сцена 1: Chair -> Book -> Cup)")]
        [SerializeField] private NarrativeNode[] nodes;

        [Tooltip("Индекс стартового узла (The Chair)")]
        [SerializeField] private int entryNodeIndex = 0;

        [Tooltip("Transform AR-камеры (XR Origin) — нужен пространственным условиям Сцены 2")]
        [SerializeField] private Transform playerCamera;

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
        private float _idleTimer;                 // секунды с последнего прогресса
        private GuardStage _guardStage;
        private float _nextEscalationAt;          // абсолютное значение _idleTimer для след. ступени
        private bool _sceneRunning;
        private bool _targetSeenThisFrame;        // выставляет ReportDetection, читает Update

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        private void Awake()
        {
            // Единственное место, где разрешены аллокации: прогрев кэшей.
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i].BuildCache();
            }
        }

        private void Start()
        {
            StartScene();
        }

        public void StartScene()
        {
            _sceneRunning = true;
            ResetIdleState();
            EnterNode(entryNodeIndex);
        }

        // =====================================================================
        // ВХОДНАЯ ТОЧКА ДЛЯ ДЕТЕКТОРА (Step 2: YOLO via Unity Sentis)
        // Вызывается на каждый результат инференса. Struct по 'in' — ноль копий.
        // =====================================================================
        public void ReportDetection(in DetectionEvent detection)
        {
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (index < 0 || index >= nodes.Length)
            {
                Debug.LogError($"[Gate2Reality] Некорректный индекс узла: {index}");
                return;
            }
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
    }
}
