# Gate2Reality — полный исходный код (Глава I, v1.0-fieldtest)
Снапшот всех файлов проекта. Описание каждого файла — в `PROJECT_MANIFEST.md`.

---

## 1. `ITriggerable.cs`

```csharp
using UnityEngine;

namespace Gate2Reality.Narrative
{
    /// <summary>
    /// Контракт для любого сценического эффекта, который активируется узлом
    /// нарративного графа: world-space свет, шейдер дисторсии, партиклы,
    /// аудио-шёпот, голограмма карты и т.д.
    ///
    /// Дизайн-решение: эффекты НИЧЕГО не знают о детекторе (YOLO) и о графе.
    /// Они получают только Pose физического объекта-якоря. Это позволяет
    /// менять детектор (YOLO / Depth-fallback / ручной дебаг-тап) без
    /// единой правки контента.
    /// </summary>
    public interface ITriggerable
    {
        /// <summary>Уникальный ID — для логов и отладочного HUD.</summary>
        string TriggerId { get; }

        /// <summary>
        /// Запуск эффекта. worldAnchor — поза физического объекта,
        /// вычисленная детектором (центр 3D-бокса от YOLO + Depth raycast).
        /// Передаётся по 'in' — struct без копирования, ноль аллокаций.
        /// </summary>
        void Trigger(in Pose worldAnchor);

        /// <summary>Принудительная остановка (guard-сценарий / рестарт сцены).</summary>
        void Cancel();

        /// <summary>true, пока эффект проигрывается — граф может ждать завершения.</summary>
        bool IsActive { get; }
    }

    /// <summary>
    /// Семантические метки Сцены 1. byte — компактно, сравнение через
    /// enum == enum не боксит и не аллоцирует (в отличие от строк!).
    /// При переходе на YOLO: классы COCO 56(chair), 73(book), 41(cup)
    /// маппятся в эти значения внутри детектора.
    /// </summary>
    public enum NarrativeLabel : byte
    {
        None  = 0,
        Chair = 1,
        Book  = 2,
        Cup   = 3,

        // Сцена 2: «виртуальные» метки. YOLO их НИКОГДА не производит
        // (MapLabel детектора не возвращает эти значения) — они существуют
        // только как фокус-объекты для генератора шёпотов и контекста.
        EchoZone = 4,
        Portal   = 5
    }

    /// <summary>
    /// Универсальное событие детекции. Struct (readonly) — живёт на стеке,
    /// ноль давления на GC даже при 30 событиях/сек от YOLO.
    /// </summary>
    public readonly struct DetectionEvent
    {
        public readonly NarrativeLabel Label;
        public readonly Pose WorldPose;      // позиция+ориентация объекта в мире
        public readonly float Confidence;    // 0..1 (YOLO score после NMS)
        public readonly float BoundsRadius;  // приблизительный радиус объекта, м

        public DetectionEvent(NarrativeLabel label, in Pose pose, float confidence, float boundsRadius)
        {
            Label = label;
            WorldPose = pose;
            Confidence = confidence;
            BoundsRadius = boundsRadius;
        }
    }
}
```

---

## 2. `NarrativeCondition.cs`

```csharp
using UnityEngine;

namespace Gate2Reality.Narrative
{
    /// <summary>Тип условия срабатывания нарративного узла.</summary>
    public enum ConditionType : byte
    {
        /// <summary>Сцена 1: YOLO-детекция физического объекта (chair/book/cup).</summary>
        SemanticDetection = 0,
        /// <summary>Сцена 2: игрок физически подошёл к точке (одометрия XR Origin,
        /// БЕЗ геолокации — чистый ARCore-трекинг).</summary>
        Proximity = 1,
        /// <summary>Сцена 2: игрок смотрит на точку (конус взгляда камеры).</summary>
        Gaze = 2
    }

    /// <summary>
    /// Условие узла графа. Сознательно НЕ полиморфизм через [SerializeReference]:
    /// плоский класс с enum-переключателем — дружелюбнее к инспектору, к
    /// сериализации и к zero-GC оценке (никаких виртуальных вызовов в Update).
    ///
    /// Semantic-условия питаются извне через NarrativeManager.ReportDetection.
    /// Пространственные (Proximity/Gaze) оцениваются менеджером каждый кадр —
    /// это дешёвая векторная математика без рейкастов.
    ///
    /// runtimeTarget для пространственных условий обычно НЕ задаётся в редакторе:
    /// эхо-зоны Сцены 2 размещаются процедурно (EchoZonePlacer, Step 2) и
    /// привязываются через NarrativeManager.SetNodeRuntimeTarget().
    /// </summary>
    [System.Serializable]
    public sealed class NarrativeCondition
    {
        public ConditionType type = ConditionType.SemanticDetection;

        [Header("SemanticDetection")]
        [Tooltip("Какой физический объект должен найти YOLO-детектор")]
        public NarrativeLabel requiredLabel;
        [Tooltip("Минимальный confidence YOLO (ТЗ Сцены 1: > 0.85)")]
        [Range(0f, 1f)] public float minConfidence = 0.85f;
        [Tooltip("Макс. радиус объекта, м (стул: < 1.5). 0 = не проверять")]
        public float maxBoundsRadius = 1.5f;

        [Header("Proximity / Gaze")]
        [Tooltip("Якорь условия. Для процедурных зон ставится в рантайме")]
        public Transform runtimeTarget;
        [Tooltip("Proximity: радиус срабатывания, м")]
        public float triggerRadius = 1.2f;
        [Tooltip("Gaze: полуугол конуса взгляда, градусы")]
        public float maxGazeAngleDeg = 12f;
        [Tooltip("Gaze: дальше этого расстояния взгляд не считается, м")]
        public float maxGazeDistance = 6f;

        /// <summary>Проверка YOLO-детекции (только для SemanticDetection).</summary>
        public bool MatchesDetection(in DetectionEvent evt)
        {
            if (type != ConditionType.SemanticDetection) return false;
            if (evt.Label != requiredLabel) return false;
            if (evt.Confidence < minConfidence) return false;
            if (maxBoundsRadius > 0f && evt.BoundsRadius > maxBoundsRadius) return false;
            return true;
        }

        /// <summary>
        /// Покадровая оценка пространственных условий. Чистая арифметика:
        /// квадраты расстояний (без корня, где можно) и один Dot для угла.
        /// </summary>
        public bool EvaluateSpatial(Transform player, out Pose anchor)
        {
            anchor = default;
            if (runtimeTarget == null || player == null) return false;

            Vector3 targetPos = runtimeTarget.position;
            Vector3 toTarget = targetPos - player.position;

            switch (type)
            {
                case ConditionType.Proximity:
                {
                    // Сравниваем квадраты — корень не нужен.
                    if (toTarget.sqrMagnitude > triggerRadius * triggerRadius) return false;
                    anchor = new Pose(targetPos, runtimeTarget.rotation);
                    return true;
                }
                case ConditionType.Gaze:
                {
                    float sqrDist = toTarget.sqrMagnitude;
                    if (sqrDist > maxGazeDistance * maxGazeDistance) return false;
                    if (sqrDist < 1e-6f) return true; // стоим в точке — засчитано

                    // cos сравнение вместо Angle(): без acos, дешевле.
                    float cosAngle = Vector3.Dot(player.forward,
                                                 toTarget / Mathf.Sqrt(sqrDist));
                    if (cosAngle < Mathf.Cos(maxGazeAngleDeg * Mathf.Deg2Rad)) return false;

                    anchor = new Pose(targetPos, runtimeTarget.rotation);
                    return true;
                }
                default:
                    return false; // Semantic кормится через ReportDetection
            }
        }

        /// <summary>Известен ли якорь заранее (для прайминга guard-маяка).</summary>
        public bool TryGetKnownAnchor(out Pose anchor)
        {
            if (runtimeTarget != null)
            {
                anchor = new Pose(runtimeTarget.position, runtimeTarget.rotation);
                return true;
            }
            anchor = default;
            return false;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public string Describe() => type switch
        {
            ConditionType.SemanticDetection => $"YOLO:{requiredLabel} conf>{minConfidence}",
            ConditionType.Proximity => $"Proximity r={triggerRadius}m -> {(runtimeTarget ? runtimeTarget.name : "<runtime>")}",
            ConditionType.Gaze => $"Gaze {maxGazeAngleDeg}deg -> {(runtimeTarget ? runtimeTarget.name : "<runtime>")}",
            _ => "?"
        };
#endif
    }
}
```

---

## 3. `NarrativeNode.cs`

```csharp
using UnityEngine;

namespace Gate2Reality.Narrative
{
    /// <summary>
    /// Узел Trigger-Action графа. Сериализуется прямо в инспекторе
    /// NarrativeManager — для Сцены 1 этого достаточно; позже можно
    /// вынести в ScriptableObject-граф без изменения рантайм-логики.
    ///
    /// Граф, а не линейный массив: nextNodeIndices позволяет ветвление
    /// (нелинейный нарратив из ТЗ). Для Сцены 1 это просто цепочка
    /// Chair(0) -> Book(1) -> Cup(2).
    /// </summary>
    [System.Serializable]
    public sealed class NarrativeNode
    {
        [Tooltip("Имя для отладки, напр. 'The Chair — Awakening'")]
        public string nodeName;

        [Tooltip("Условие срабатывания: семантика (Сцена 1) или пространство (Сцена 2)")]
        public NarrativeCondition condition = new NarrativeCondition();

        [Tooltip("Сколько секунд подряд объект должен оставаться в детекции, " +
                 "чтобы узел сработал. Защита от одиночных ложных срабатываний YOLO.")]
        public float dwellTimeSeconds = 0.75f;

        [Tooltip("Компоненты, реализующие ITriggerable (свет, шейдер, партиклы...). " +
                 "MonoBehaviour-ссылки, т.к. Unity не сериализует интерфейсы.")]
        public MonoBehaviour[] triggerableBehaviours;

        [Tooltip("Индексы следующих узлов графа. Пусто = конец сцены.")]
        public int[] nextNodeIndices;

        // ---- Рантайм-кэш (заполняется один раз в Awake — ноль GC дальше) ----
        [System.NonSerialized] public ITriggerable[] CachedTriggerables;
        [System.NonSerialized] public float DwellAccumulator; // сброс при потере объекта
        [System.NonSerialized] public Pose LastSeenPose;      // последняя валидная поза для guard-подсказок

        /// <summary>Валидация и кэширование интерфейсов. Вызывать из Awake.</summary>
        public void BuildCache()
        {
            int count = triggerableBehaviours != null ? triggerableBehaviours.Length : 0;
            CachedTriggerables = new ITriggerable[count]; // единственная аллокация — на старте

            for (int i = 0; i < count; i++)
            {
                CachedTriggerables[i] = triggerableBehaviours[i] as ITriggerable;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (CachedTriggerables[i] == null)
                {
                    Debug.LogError($"[Gate2Reality] Узел '{nodeName}': элемент {i} не реализует ITriggerable!");
                }
#endif
            }
        }
    }
}
```

---

## 4. `NarrativeManager.cs`

```csharp
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
```

---

## 5. `YoloObjectDetector.cs`

```csharp
using System;
using Unity.Collections;
using Unity.Sentis;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Gate2Reality.Detection
{
    using Gate2Reality.Narrative;

    /// <summary>
    /// On-device YOLO-детектор для Android 15 (Pixel 9 / S26).
    ///
    /// ПАЙПЛАЙН (полностью на устройстве — требование privacy Android 15,
    /// ни один кадр не покидает девайс):
    ///   ARCameraManager (CPU image, YUV)
    ///     -> конверсия в RGBA32 сразу в 640x640 (даунскейл на этапе конверсии,
    ///        дёшево и без промежуточных текстур)
    ///     -> Texture2D -> Tensor (TextureConverter, GPU)
    ///     -> YOLOv8n int8 (Unity Sentis, backend GPUCompute / NNAPI)
    ///     -> асинхронный readback (НИКОГДА не блокируем кадр синхронным чтением!)
    ///     -> постпроцесс: фильтр классов {cup=41, chair=56, book=73} + NMS
    ///     -> DepthPoseProjector: 2D-бокс -> 3D-поза (Depth API, fallback-цепочка)
    ///     -> NarrativeManager.ReportDetection(in DetectionEvent)
    ///
    /// THERMAL/BATTERY MITIGATION:
    ///   - Инференс дросселируется до inferenceIntervalMs (по умолч. 5 Гц).
    ///   - Пока предыдущий инференс не дочитан — новый не запускается.
    ///   - Все буферы (NativeArray, Texture2D, массивы кандидатов) аллоцируются
    ///     один раз в Awake. В горячем пути — ноль управляемых аллокаций.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class YoloObjectDetector : MonoBehaviour
    {
        /// <summary>
        /// Сырой поток ВСЕХ валидных детекций (не только текущей цели графа).
        /// Нужен системам-подсказкам: тень стула наводится на книгу, как только
        /// YOLO впервые её заметил, ещё ДО того, как книга стала активным узлом.
        /// </summary>
        public event System.Action<DetectionEvent> OnRawDetection;

        /// <summary>
        /// Privacy-safeguard (Step 5): true — в кадре появился человек,
        /// false — кадр снова чист. Срабатывает только на ИЗМЕНЕНИЕ состояния.
        /// </summary>
        public event System.Action<bool> OnHumanPresenceChanged;

        private bool _humanVisible;
        private bool _personOnlyMode;

        [Tooltip("Интервал инференса в person-only режиме (privacy-вахта Сцены 2), мс")]
        [SerializeField] private int personOnlyIntervalMs = 1000;

        /// <summary>
        /// Privacy-вахта (Step 5): семантические узлы кончились на Чашке, но
        /// обещание «гасим хоррор при людях в кадре» действует ВСЮ главу.
        /// Режим оставляет только сканирование класса person на 1 Гц
        /// (~0.2 Вт против ~1 Вт полного режима); DetectionEvent'ы не эмитятся.
        /// </summary>
        public void SetPersonOnlyMode(bool enabled) => _personOnlyMode = enabled;

        /// <summary>Рантайм-настройка частоты инференса (DeviceTuningProfile):
        /// средний класс вроде Honor 90 получает 300мс вместо 200.</summary>
        public void SetInferenceInterval(int milliseconds) =>
            inferenceIntervalMs = Mathf.Max(100, milliseconds);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private float _inferDispatchedAt; // полевой замер: dispatch -> readback done
#endif

        private void OnDisable()
        {
            // Баг стыка сцен, пойманный аудитом: детектор гаснет с «человеком
            // в кадре» -> говернор навсегда застрял бы на 25% хоррора.
            // Честно сообщаем «кадр чист» при выключении.
            if (_humanVisible)
            {
                _humanVisible = false;
                OnHumanPresenceChanged?.Invoke(false);
            }
        }

        // =====================================================================
        // ИНСПЕКТОР
        // =====================================================================
        [Header("Связи")]
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private NarrativeManager narrativeManager;
        [SerializeField] private DepthPoseProjector poseProjector;

        [Header("Модель (YOLOv8n, ONNX, квантованная int8, вход 640x640)")]
        [SerializeField] private ModelAsset modelAsset;

        [Header("Производительность")]
        [Tooltip("Интервал между инференсами, мс. 200мс = 5 Гц — достаточно для нарративных триггеров и щадяще для троттлинга.")]
        [SerializeField] private int inferenceIntervalMs = 200;

        [Header("Детекция")]
        [Tooltip("Порог уверенности до NMS. Финальный порог >0.85 проверяет NarrativeManager per-node.")]
        [Range(0f, 1f)] [SerializeField] private float scoreThreshold = 0.5f;
        [Range(0f, 1f)] [SerializeField] private float nmsIouThreshold = 0.45f;

        // =====================================================================
        // КОНСТАНТЫ YOLO / COCO
        // =====================================================================
        private const int InputSize = 640;          // вход YOLOv8n
        private const int NumClasses = 80;          // COCO
        private const int MaxCandidates = 32;       // достаточно для комнатной сцены

        // Интересующие COCO-классы -> нарративные метки
        private const int CocoPerson = 0;   // privacy-safeguard (Step 5)
        private const int CocoCup = 41;
        private const int CocoChair = 56;
        private const int CocoBook = 73;

        [Tooltip("Порог для класса person: safeguard должен срабатывать раньше нарративных детекций")]
        [Range(0f, 1f)] [SerializeField] private float personScoreThreshold = 0.35f;

        // =====================================================================
        // РАНТАЙМ-СОСТОЯНИЕ (всё преаллоцировано)
        // =====================================================================
        private Worker _worker;
        private Tensor<float> _inputTensor;
        private Tensor<float> _pendingOutput;   // тензор, ожидающий async-readback
        private bool _inferenceInFlight;
        private float _nextInferenceTime;

        private Texture2D _cameraTexture;       // 640x640 RGBA32, переиспользуется
        private NativeArray<byte> _conversionBuffer;

        /// <summary>Кандидат детекции до/после NMS. Struct — на стеке/в массиве, без GC.</summary>
        private struct Candidate
        {
            public float Cx, Cy, W, H;   // в пикселях входа 640x640
            public float Score;
            public int CocoClass;
            public bool Suppressed;      // флаг NMS
        }
        private Candidate[] _candidates;
        private int _candidateCount;

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        private void Awake()
        {
            // --- Единственная зона аллокаций ---
            var model = ModelLoader.Load(modelAsset);

            // GPUCompute: на Pixel 9 / S26 (Vulkan) инференс YOLOv8n ~8-15мс.
            // При проблемах с драйвером можно переключить на BackendType.CPU.
            _worker = new Worker(model, BackendType.GPUCompute);

            _inputTensor = new Tensor<float>(new TensorShape(1, 3, InputSize, InputSize));
            _cameraTexture = new Texture2D(InputSize, InputSize, TextureFormat.RGBA32, false);
            _conversionBuffer = new NativeArray<byte>(InputSize * InputSize * 4, Allocator.Persistent);
            _candidates = new Candidate[MaxCandidates];
        }

        private void OnDestroy()
        {
            _pendingOutput = null; // принадлежит worker'у — освободит его Dispose
            _worker?.Dispose();
            _inputTensor?.Dispose();
            if (_conversionBuffer.IsCreated) _conversionBuffer.Dispose();
        }

        // =====================================================================
        // ГЛАВНЫЙ ЦИКЛ
        // =====================================================================
        private void Update()
        {
            // 1) Если есть инференс в полёте — проверяем, доехал ли readback.
            if (_inferenceInFlight)
            {
                if (_pendingOutput.IsReadbackRequestDone())
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    // Полевой замер для протокола прогона (риск №2: Sentis).
                    Debug.Log($"[Gate2Reality] YOLO inference+readback: " +
                              $"{(Time.realtimeSinceStartup - _inferDispatchedAt) * 1000f:F1} ms");
#endif
                    // Данные уже на CPU — клонирование теперь мгновенное,
                    // GPU-стойла нет. Клон наш — его и освобождаем.
                    using (Tensor<float> cpuTensor = _pendingOutput.ReadbackAndClone())
                    {
                        PostProcess(cpuTensor);
                    }
                    _pendingOutput = null; // оригиналом владеет worker — НЕ Dispose!
                    _inferenceInFlight = false;
                }
                return; // не запускаем новый, пока не разобрали старый
            }

            // 2) Троттлинг частоты инференса.
            if (Time.unscaledTime < _nextInferenceTime) return;

            // 3) Пытаемся забрать свежий CPU-кадр и запустить инференс.
            if (TryCaptureFrame())
            {
                _nextInferenceTime = Time.unscaledTime +
                    (_personOnlyMode ? personOnlyIntervalMs : inferenceIntervalMs) * 0.001f;
                RunInferenceAsync();
            }
        }

        // =====================================================================
        // ЗАХВАТ КАДРА
        // =====================================================================
        private bool TryCaptureFrame()
        {
            if (!cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
                return false;

            try
            {
                // Конверсия YUV -> RGBA32 с одновременным даунскейлом до 640x640.
                // ВНИМАНИЕ: это «squash»-ресайз (без letterbox). Для нарративных
                // триггеров искажение аспекта некритично (модель устойчива),
                // зато экономим целый проход копирования. Координаты боксов
                // разжимаются обратно в DepthPoseProjector через viewport.
                var conversion = new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(0, 0, image.width, image.height),
                    outputDimensions = new Vector2Int(InputSize, InputSize),
                    outputFormat = TextureFormat.RGBA32,
                    // Зеркалим по Y: CPU-image идёт «вверх ногами» относительно текстур Unity.
                    transformation = XRCpuImage.Transformation.MirrorY
                };

                image.Convert(conversion, _conversionBuffer);
                _cameraTexture.LoadRawTextureData(_conversionBuffer);
                _cameraTexture.Apply(false, false);
                return true;
            }
            finally
            {
                image.Dispose(); // обязательный возврат буфера ARCore
            }
        }

        private void RunInferenceAsync()
        {
            // Texture -> Tensor на GPU (нормализация 0..1 включена в конвертер).
            TextureConverter.ToTensor(_cameraTexture, _inputTensor, default);

            _worker.Schedule(_inputTensor);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _inferDispatchedAt = Time.realtimeSinceStartup;
#endif

            // PeekOutput не копирует данные и принадлежит worker'у.
            // ReadbackRequest — НЕблокирующий: GPU начнёт переливать результат
            // на CPU в фоне, готовность опрашиваем в Update. Никаких
            // ReadbackAndClone здесь — он блокирует до завершения инференса!
            _pendingOutput = _worker.PeekOutput() as Tensor<float>;
            _pendingOutput.ReadbackRequest();
            _inferenceInFlight = true;
        }

        // =====================================================================
        // ПОСТПРОЦЕСС: [1, 84, 8400] -> кандидаты -> NMS -> DetectionEvent
        // =====================================================================
        private void PostProcess(Tensor<float> output)
        {
            _candidateCount = 0;
            bool humanSeen = false;
            int anchors = output.shape[2]; // 8400 для 640x640

            // Layout YOLOv8: каналы 0-3 = cx,cy,w,h; 4..83 = score по классам.
            // Читаем ТОЛЬКО четыре нужных класса — экономим ~95% чтений тензора.
            for (int i = 0; i < anchors; i++)
            {
                // Privacy: человек в кадре? Бокс нам не нужен — только факт.
                if (!humanSeen && output[0, 4 + CocoPerson, i] > personScoreThreshold)
                    humanSeen = true;

                // Person-only вахта: нарративные классы не читаем вовсе —
                // остаётся ровно одно чтение тензора на якорь.
                if (_personOnlyMode) continue;

                float sCup   = output[0, 4 + CocoCup, i];
                float sChair = output[0, 4 + CocoChair, i];
                float sBook  = output[0, 4 + CocoBook, i];

                // Максимум из трёх без веток-аллокаций
                int cls = CocoCup; float best = sCup;
                if (sChair > best) { best = sChair; cls = CocoChair; }
                if (sBook  > best) { best = sBook;  cls = CocoBook;  }

                if (best < scoreThreshold) continue;
                if (_candidateCount >= MaxCandidates) break;

                ref Candidate c = ref _candidates[_candidateCount++];
                c.Cx = output[0, 0, i];
                c.Cy = output[0, 1, i];
                c.W  = output[0, 2, i];
                c.H  = output[0, 3, i];
                c.Score = best;
                c.CocoClass = cls;
                c.Suppressed = false;
            }

            // Privacy-safeguard: событие только на смену состояния, без спама.
            if (humanSeen != _humanVisible)
            {
                _humanVisible = humanSeen;
                OnHumanPresenceChanged?.Invoke(humanSeen);
            }

            ApplyNms();
            EmitDetections();
        }

        /// <summary>Классический greedy-NMS на преаллоцированном массиве, O(n²) при n<=32 — копейки.</summary>
        private void ApplyNms()
        {
            // Сортировка вставками по score (n мало, без Array.Sort -> без компаратора-делегата)
            for (int i = 1; i < _candidateCount; i++)
            {
                Candidate key = _candidates[i];
                int j = i - 1;
                while (j >= 0 && _candidates[j].Score < key.Score)
                {
                    _candidates[j + 1] = _candidates[j];
                    j--;
                }
                _candidates[j + 1] = key;
            }

            for (int i = 0; i < _candidateCount; i++)
            {
                if (_candidates[i].Suppressed) continue;
                for (int j = i + 1; j < _candidateCount; j++)
                {
                    if (_candidates[j].Suppressed) continue;
                    if (_candidates[i].CocoClass != _candidates[j].CocoClass) continue;
                    if (Iou(in _candidates[i], in _candidates[j]) > nmsIouThreshold)
                        _candidates[j].Suppressed = true;
                }
            }
        }

        private static float Iou(in Candidate a, in Candidate b)
        {
            float ax0 = a.Cx - a.W * 0.5f, ay0 = a.Cy - a.H * 0.5f;
            float ax1 = a.Cx + a.W * 0.5f, ay1 = a.Cy + a.H * 0.5f;
            float bx0 = b.Cx - b.W * 0.5f, by0 = b.Cy - b.H * 0.5f;
            float bx1 = b.Cx + b.W * 0.5f, by1 = b.Cy + b.H * 0.5f;

            float ix = Mathf.Max(0f, Mathf.Min(ax1, bx1) - Mathf.Max(ax0, bx0));
            float iy = Mathf.Max(0f, Mathf.Min(ay1, by1) - Mathf.Max(ay0, by0));
            float inter = ix * iy;
            float union = a.W * a.H + b.W * b.H - inter;
            return union > 0f ? inter / union : 0f;
        }

        // =====================================================================
        // ЭМИССИЯ СОБЫТИЙ: 2D -> 3D -> NarrativeManager
        // =====================================================================
        private void EmitDetections()
        {
            for (int i = 0; i < _candidateCount; i++)
            {
                ref readonly Candidate c = ref _candidates[i];
                if (c.Suppressed) continue;

                NarrativeLabel label = MapLabel(c.CocoClass);
                if (label == NarrativeLabel.None) continue;

                // Центр бокса -> viewport (0..1). Вход был squash-ресайзом всего
                // кадра, поэтому нормировка на 640 даёт корректный viewport.
                var viewportPoint = new Vector2(c.Cx / InputSize, 1f - (c.Cy / InputSize));
                float bboxViewportWidth = c.W / InputSize;

                // Fallback-цепочка проекции внутри DepthPoseProjector (ТЗ Step 2).
                if (poseProjector.TryProjectToWorld(viewportPoint, bboxViewportWidth,
                        out Pose worldPose, out float boundsRadius))
                {
                    var evt = new DetectionEvent(label, in worldPose, c.Score, boundsRadius);
                    OnRawDetection?.Invoke(evt);
                    narrativeManager.ReportDetection(in evt);
                }
            }
        }

        private static NarrativeLabel MapLabel(int cocoClass)
        {
            switch (cocoClass)
            {
                case CocoChair: return NarrativeLabel.Chair;
                case CocoBook:  return NarrativeLabel.Book;
                case CocoCup:   return NarrativeLabel.Cup;
                default:        return NarrativeLabel.None;
            }
        }
    }
}
```

---

## 6. `DepthPoseProjector.cs`

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Gate2Reality.Detection
{
    /// <summary>
    /// Проекция 2D-детекции YOLO в 3D-позу мира. Реализует Fallback-механизм из ТЗ:
    ///
    ///   Уровень 1: Raycast против ARCore Depth API (TrackableType.Depth) —
    ///              самый точный: попадает прямо в поверхность физического объекта.
    ///   Уровень 2: Raycast против обнаруженных плоскостей (объект стоит на
    ///              столе/полу — точка рядом с реальной).
    ///   Уровень 3: «Approximation marker» — точка на луче на дистанции,
    ///              оценённой из углового размера бокса. Грубо, но достаточно,
    ///              чтобы guard-маяк и партиклы указывали в правильную сторону.
    ///
    /// Возвращает также boundsRadius — оценку физического радиуса объекта
    /// (нужна NarrativeManager: правило «стул < 1.5 м»).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DepthPoseProjector : MonoBehaviour
    {
        [Header("Связи")]
        [SerializeField] private Camera arCamera;                 // камера из XR Origin
        [SerializeField] private ARRaycastManager raycastManager;

        [Header("Fallback уровня 3")]
        [Tooltip("Типичный физический размер целевых объектов, м (медиана chair/book/cup). Используется для оценки дистанции по угловому размеру бокса.")]
        [SerializeField] private float assumedObjectSize = 0.5f;

        [Tooltip("Ограничение дистанции аппроксимации, м")]
        [SerializeField] private float maxApproxDistance = 5f;

        // Преаллоцированный список хитов — ARRaycastManager пишет в него без GC.
        private static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>(8);

        /// <summary>
        /// viewportPoint: центр бокса в координатах viewport (0..1).
        /// bboxViewportWidth: ширина бокса в долях ширины кадра (для оценки радиуса).
        /// </summary>
        public bool TryProjectToWorld(Vector2 viewportPoint, float bboxViewportWidth,
                                      out Pose worldPose, out float boundsRadius)
        {
            Vector2 screenPoint = new Vector2(
                viewportPoint.x * Screen.width,
                viewportPoint.y * Screen.height);

            // ---------- Уровень 1: Depth API ----------
            if (raycastManager.Raycast(screenPoint, s_Hits, TrackableType.Depth))
            {
                worldPose = s_Hits[0].pose;
                boundsRadius = EstimateRadius(bboxViewportWidth, worldPose.position);
                return true;
            }

            // ---------- Уровень 2: плоскости ----------
            if (raycastManager.Raycast(screenPoint, s_Hits,
                    TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated))
            {
                worldPose = s_Hits[0].pose;
                boundsRadius = EstimateRadius(bboxViewportWidth, worldPose.position);
                return true;
            }

            // ---------- Уровень 3: аппроксимационный маркер ----------
            // Дистанция из углового размера: d ≈ size / (2 * tan(angularHalfWidth)).
            Ray ray = arCamera.ViewportPointToRay(viewportPoint);
            float horizontalFovRad = arCamera.fieldOfView * Mathf.Deg2Rad * arCamera.aspect;
            float angularWidth = Mathf.Max(0.01f, bboxViewportWidth * horizontalFovRad);
            float distance = Mathf.Min(maxApproxDistance,
                assumedObjectSize / (2f * Mathf.Tan(angularWidth * 0.5f)));

            Vector3 pos = ray.origin + ray.direction * distance;
            // Ориентация «лицом к игроку» — для билбордов-маркеров и маяка.
            worldPose = new Pose(pos, Quaternion.LookRotation(-ray.direction));
            boundsRadius = EstimateRadius(bboxViewportWidth, pos);
            return true; // уровень 3 не может «не сработать» — всегда даём оценку
        }

        /// <summary>
        /// Физический радиус из углового размера и известной дистанции:
        /// r ≈ d * tan(angularHalfWidth). Тригонометрия по месту, без кэшей —
        /// вызывается максимум пару десятков раз в секунду.
        /// </summary>
        private float EstimateRadius(float bboxViewportWidth, Vector3 worldPos)
        {
            float distance = Vector3.Distance(arCamera.transform.position, worldPos);
            float horizontalFovRad = arCamera.fieldOfView * Mathf.Deg2Rad * arCamera.aspect;
            float angularHalfWidth = bboxViewportWidth * horizontalFovRad * 0.5f;
            return distance * Mathf.Tan(angularHalfWidth);
        }
    }
}
```

---

## 7. `TriggerableEffectBase.cs`

```csharp
using UnityEngine;

namespace Gate2Reality.Effects
{
    using Gate2Reality.Narrative;

    /// <summary>
    /// База для всех нарративных эффектов Сцены 1. Снимает бойлерплейт:
    ///  - реализация ITriggerable (id, IsActive, Cancel);
    ///  - перемещение корня эффекта к позе физического объекта-якоря;
    ///  - защита от повторного срабатывания (нарративные узлы — one-shot).
    ///
    /// Производные классы переопределяют OnTriggered/OnCancelled и ведут
    /// свою анимацию в OnEffectUpdate (вызывается только пока активен —
    /// неактивные эффекты не тратят ни наносекунды CPU).
    /// </summary>
    public abstract class TriggerableEffectBase : MonoBehaviour, ITriggerable
    {
        [Header("ITriggerable")]
        [SerializeField] private string triggerId = "unnamed_effect";

        [Tooltip("Прилипать ли к позе физического объекта при срабатывании")]
        [SerializeField] private bool snapToAnchor = true;

        public string TriggerId => triggerId;
        public bool IsActive { get; private set; }

        protected Pose Anchor { get; private set; }
        protected float TimeSinceTriggered { get; private set; }

        public void Trigger(in Pose worldAnchor)
        {
            if (IsActive) return; // one-shot: повторные детекции игнорируем

            Anchor = worldAnchor;
            if (snapToAnchor)
            {
                transform.SetPositionAndRotation(worldAnchor.position, worldAnchor.rotation);
            }

            TimeSinceTriggered = 0f;
            IsActive = true;
            OnTriggered();
        }

        public void Cancel()
        {
            if (!IsActive) return;
            IsActive = false;
            OnCancelled();
        }

        private void Update()
        {
            if (!IsActive) return;
            TimeSinceTriggered += Time.deltaTime;
            OnEffectUpdate(Time.deltaTime);
        }

        /// <summary>Эффект сообщает, что отыграл до конца (для IsActive-ожиданий графа).</summary>
        protected void MarkFinished() => IsActive = false;

        protected abstract void OnTriggered();
        protected virtual void OnCancelled() { }
        protected virtual void OnEffectUpdate(float dt) { }
    }
}
```

---

## 8. `ChairAwakeningEffect.cs`

```csharp
using UnityEngine;

namespace Gate2Reality.Effects
{
    /// <summary>
    /// Узел 1 «The Chair — The Awakening».
    ///  - Янтарный world-space свет с медленным «вдохом» интенсивности.
    ///  - Драйвер шейдера дисторсии ножек (сам шейдер — Step 4): плавный
    ///    ramp параметра _DistortionStrength через MaterialPropertyBlock
    ///    (ноль инстансов материалов, ноль GC).
    ///  - Виртуальная тень-указатель: quad с текстурой вытянутой тени.
    ///    Пока цель (книга) неизвестна — «сканирует» комнату медленным
    ///    вращением; после вызова SetHintTarget() плавно доворачивается
    ///    на книгу. Цель скармливает SceneOneDirector из сырых YOLO-детекций.
    /// </summary>
    public sealed class ChairAwakeningEffect : TriggerableEffectBase
    {
        [Header("Янтарный свет")]
        [SerializeField] private Light amberLight;            // Point/Spot, URP
        [SerializeField] private float lightRampSeconds = 3f;
        [SerializeField] private float maxIntensity = 2.5f;
        [SerializeField] private float breathAmplitude = 0.35f;
        [SerializeField] private float breathFrequency = 0.4f; // Гц, медленное «дыхание»

        [Header("Дисторсия ножек (шейдер из Step 4)")]
        [SerializeField] private Renderer distortionOverlayRenderer; // оверлей-меш поверх области ножек
        [SerializeField] private float distortionRampSeconds = 4f;
        [SerializeField] private float maxDistortion = 1f;

        [Header("Тень-указатель")]
        [SerializeField] private Transform shadowQuad;        // quad с текстурой тени, pivot у основания
        [SerializeField] private float scanSpeedDegPerSec = 20f;
        [SerializeField] private float aimLerpSpeed = 2f;

        // Кэш ID шейдерных свойств — Shader.PropertyToID при каждом обращении
        // по строке = скрытые аллокации. Кэшируем статически один раз.
        private static readonly int DistortionStrengthId = Shader.PropertyToID("_DistortionStrength");

        private MaterialPropertyBlock _mpb;
        private Vector3 _hintTarget;
        private bool _hasHintTarget;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (amberLight != null) amberLight.enabled = false;
            if (shadowQuad != null) shadowQuad.gameObject.SetActive(false);
        }

        /// <summary>Вызывается SceneOneDirector, когда YOLO впервые увидел книгу.</summary>
        public void SetHintTarget(Vector3 worldPosition)
        {
            _hintTarget = worldPosition;
            _hasHintTarget = true;
        }

        protected override void OnTriggered()
        {
            if (amberLight != null)
            {
                amberLight.enabled = true;
                amberLight.color = new Color(1f, 0.69f, 0.25f); // янтарь
                amberLight.intensity = 0f;
            }
            if (shadowQuad != null) shadowQuad.gameObject.SetActive(true);
        }

        protected override void OnEffectUpdate(float dt)
        {
            float t = TimeSinceTriggered;

            // --- Свет: ramp -> бесконечное дыхание ---
            if (amberLight != null)
            {
                float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / lightRampSeconds));
                float breath = 1f + breathAmplitude *
                               Mathf.Sin(t * breathFrequency * 2f * Mathf.PI);
                amberLight.intensity = maxIntensity * ramp * breath;
            }

            // --- Дисторсия: плавный ramp параметра шейдера ---
            if (distortionOverlayRenderer != null)
            {
                float d = maxDistortion * Mathf.SmoothStep(0f, 1f,
                          Mathf.Clamp01(t / distortionRampSeconds));
                distortionOverlayRenderer.GetPropertyBlock(_mpb);
                _mpb.SetFloat(DistortionStrengthId, d);
                distortionOverlayRenderer.SetPropertyBlock(_mpb);
            }

            // --- Тень: скан-вращение или наведение на книгу ---
            if (shadowQuad != null)
            {
                if (_hasHintTarget)
                {
                    Vector3 dir = _hintTarget - shadowQuad.position;
                    dir.y = 0f; // тень лежит на полу — только yaw
                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        Quaternion want = Quaternion.LookRotation(dir);
                        shadowQuad.rotation = Quaternion.Slerp(
                            shadowQuad.rotation, want, aimLerpSpeed * dt);
                    }
                }
                else
                {
                    // Книга ещё не замечена: медленный тревожный «поиск».
                    shadowQuad.Rotate(0f, scanSpeedDegPerSec * dt, 0f, Space.World);
                }
            }
        }

        protected override void OnCancelled()
        {
            if (amberLight != null) amberLight.enabled = false;
            if (shadowQuad != null) shadowQuad.gameObject.SetActive(false);
        }
    }
}
```

---

## 9. `BookMemoryEffect.cs`

```csharp
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
```

---

## 10. `CupBreachEffect.cs`

```csharp
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
```

---

## 11. `SceneOneDirector.cs`

```csharp
using UnityEngine;
using UnityEngine.Rendering;

namespace Gate2Reality.Effects
{
    using Gate2Reality.Detection;
    using Gate2Reality.Narrative;

    /// <summary>
    /// «Режиссёр» Сцены 1: единственное место, где события NarrativeManager
    /// и сырые детекции YOLO превращаются в конкретные сценические реакции.
    /// Эффекты (Chair/Book/Cup) ничего не знают друг о друге — связывает их
    /// только этот класс. Добавление Сцены 2 = новый режиссёр, ноль правок ядра.
    ///
    /// Обязанности:
    ///  1. Guard-события -> аудио-маяк / десатурация (URP Volume) / партиклы.
    ///  2. Сырые YOLO-детекции книги -> наведение тени стула (SetHintTarget).
    ///  3. Финал сцены -> остановка детектора (экономия батареи: YOLO больше не нужен).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneOneDirector : MonoBehaviour
    {
        [Header("Ядро")]
        [SerializeField] private NarrativeManager narrativeManager;
        [SerializeField] private YoloObjectDetector detector;

        [Header("Эффекты сцены")]
        [SerializeField] private ChairAwakeningEffect chairEffect;

        [Header("Guard: аудио-маяк")]
        [SerializeField] private AudioSource beaconSource; // 3D, spatialBlend = 1

        [Header("Guard: десатурация (URP Volume c Color Adjustments)")]
        [SerializeField] private Volume desaturationVolume; // weight 0 -> 1
        [SerializeField] private float desaturateLerpSeconds = 1.5f;

        [Header("Guard: партиклы-проводник")]
        [SerializeField] private ParticleSystem guideParticles; // эмиссия конусом вперёд
        [SerializeField] private Transform arCameraTransform;

        [Header("Шёпот MLLM -> субтитры (префетч-пайплайн)")]
        [SerializeField] private OnDeviceNarrativeGenerator narrativeGenerator;
        [SerializeField] private NarrativeContextCollector contextCollector;
        [SerializeField] private Gate2Reality.UI.WhisperSubtitleController subtitleController;
        [Tooltip("Индексы узлов графа (см. чек-лист, раздел 4)")]
        [SerializeField] private int chairNodeIndex = 0;
        [SerializeField] private int bookNodeIndex = 1;
        [Tooltip("После активации чашки семантических узлов больше нет — YOLO выключается здесь, а не в конце графа")]
        [SerializeField] private int cupNodeIndex = 2;
        [Tooltip("Задержка субтитра после активации книги = момент старта шёпот-клипа (noiseFadeIn в BookMemoryEffect)")]
        [SerializeField] private float subtitleDelaySeconds = 2f;

        // Состояние лерпа десатурации: -1 = к нулю, +1 = к единице, 0 = покой.
        private float _desatDirection;
        private bool _bookHintGiven;

        // Префетч шёпота: текст заказывается на активации СТУЛА (книга — след.
        // цель), показывается на активации книги с задержкой под старт клипа.
        private string _whisperText;
        private bool _subtitlePending;
        private float _subtitleDueAt;

        private void OnEnable()
        {
            narrativeManager.OnAudioBeaconRequested += HandleBeacon;
            narrativeManager.OnDesaturateRequested += HandleDesaturate;
            narrativeManager.OnSaturationRestoreRequested += HandleRestoreSaturation;
            narrativeManager.OnGuideParticlesRequested += HandleGuideParticles;
            narrativeManager.OnSceneCompleted += HandleSceneCompleted;
            narrativeManager.OnNodeActivated += HandleNodeActivated;
            detector.OnRawDetection += HandleRawDetection;
        }

        private void OnDisable()
        {
            narrativeManager.OnAudioBeaconRequested -= HandleBeacon;
            narrativeManager.OnDesaturateRequested -= HandleDesaturate;
            narrativeManager.OnSaturationRestoreRequested -= HandleRestoreSaturation;
            narrativeManager.OnGuideParticlesRequested -= HandleGuideParticles;
            narrativeManager.OnSceneCompleted -= HandleSceneCompleted;
            narrativeManager.OnNodeActivated -= HandleNodeActivated;
            detector.OnRawDetection -= HandleRawDetection;
        }

        // =====================================================================
        // ШЁПОТ: префетч на стуле, показ на книге
        // =====================================================================
        private void HandleNodeActivated(int nodeIndex, Pose pose)
        {
            if (nodeIndex == chairNodeIndex)
            {
                // Игрок только что «разбудил» стул — у MLLM есть всё время,
                // пока игрок физически идёт к книге (обычно 3-15 секунд).
                _whisperText = null;
                if (narrativeGenerator != null && contextCollector != null)
                {
                    narrativeGenerator.RequestWhisper(
                        contextCollector.Capture(NarrativeLabel.Book),
                        text => _whisperText = text);
                }
            }
            else if (nodeIndex == bookNodeIndex)
            {
                // Субтитр стартует синхронно с шёпот-клипом (после фейда шума).
                _subtitlePending = true;
                _subtitleDueAt = Time.unscaledTime + subtitleDelaySeconds;

                // Страховка: префетч по какой-то причине не случился
                // (генератор подключили позже и т.п.) — заказываем сейчас;
                // таймаут генератора (3с) гарантирует текст в любом случае.
                if (_whisperText == null && narrativeGenerator != null && contextCollector != null)
                {
                    narrativeGenerator.RequestWhisper(
                        contextCollector.Capture(NarrativeLabel.Book),
                        text => _whisperText = text);
                }
            }
            else if (nodeIndex == cupNodeIndex)
            {
                // Граф уходит в пространственные узлы Сцены 2 — полный YOLO
                // больше не нужен, но privacy-вахта (класс person) остаётся
                // на всю главу: 1 Гц, ~0.2 Вт вместо ~1 Вт.
                if (detector != null) detector.SetPersonOnlyMode(true);
            }
        }

        // =====================================================================
        // СЫРЫЕ ДЕТЕКЦИИ: подсказка для тени стула
        // =====================================================================
        private void HandleRawDetection(DetectionEvent evt)
        {
            // Первое же появление книги в кадре наводит тень. Порог ниже
            // нарративного (0.85): для намёка хватает и неуверенной детекции.
            if (!_bookHintGiven && evt.Label == NarrativeLabel.Book && evt.Confidence > 0.5f)
            {
                _bookHintGiven = true;
                if (chairEffect != null) chairEffect.SetHintTarget(evt.WorldPose.position);
            }
        }

        // =====================================================================
        // GUARD-РЕАКЦИИ
        // =====================================================================
        private void HandleBeacon(Pose targetPose)
        {
            if (beaconSource == null) return;
            // Поза по умолчанию (объект ни разу не видели) -> ненаправленный
            // шёпот у игрока; иначе — 3D-пинг из позиции цели.
            bool unknown = targetPose.position == default;
            beaconSource.transform.position = unknown && arCameraTransform != null
                ? arCameraTransform.position
                : targetPose.position;
            beaconSource.spatialBlend = unknown ? 0f : 1f;
            beaconSource.Play();
        }

        private void HandleDesaturate() => _desatDirection = 1f;
        private void HandleRestoreSaturation() => _desatDirection = -1f;

        private void HandleGuideParticles(Pose targetPose)
        {
            if (guideParticles == null) return;
            // Спавним поток от камеры, направленный к цели (или вперёд,
            // если цель неизвестна — игрок хотя бы начнёт осматриваться).
            Vector3 origin = arCameraTransform != null
                ? arCameraTransform.position + arCameraTransform.forward * 0.3f
                : Vector3.zero;
            guideParticles.transform.position = origin;

            Vector3 dir = targetPose.position == default
                ? (arCameraTransform != null ? arCameraTransform.forward : Vector3.forward)
                : (targetPose.position - origin).normalized;
            guideParticles.transform.rotation = Quaternion.LookRotation(dir);
            guideParticles.Play();
        }

        private void HandleSceneCompleted()
        {
            // Сцена закрыта: гасим YOLO-инференс. На Pixel 9 это минус
            // ~0.8-1.2 Вт постоянной нагрузки — главный жест уважения
            // к термопакету Android 15.
            if (detector != null) detector.enabled = false;
        }

        // =====================================================================
        // UPDATE: лерп десатурации + тайминг субтитра. Обе ветки — пара
        // сравнений float в кадр, бюджет нулевой.
        // =====================================================================
        private void Update()
        {
            // --- Субтитр шёпота: ждём дедлайн И готовый текст ---
            if (_subtitlePending && Time.unscaledTime >= _subtitleDueAt && _whisperText != null)
            {
                _subtitlePending = false;
                if (subtitleController != null) subtitleController.Show(_whisperText);
            }

            // --- Десатурация ---
            if (_desatDirection == 0f || desaturationVolume == null) return;

            float w = desaturationVolume.weight +
                      _desatDirection * (Time.deltaTime / desaturateLerpSeconds);
            desaturationVolume.weight = Mathf.Clamp01(w);

            if (desaturationVolume.weight <= 0f || desaturationVolume.weight >= 1f)
                _desatDirection = 0f; // доехали — больше не трогаем Update-бюджет
        }
    }
}
```

---

## 12. `EchoZone.cs`

```csharp
using UnityEngine;

namespace Gate2Reality.SceneTwo
{
    /// <summary>Тип реальной поверхности, на которой живёт эхо-зона.</summary>
    public enum EchoSurface : byte { Wall = 0, Table = 1, Floor = 2 }

    /// <summary>
    /// Маркер эхо-зоны на якоре. Чистые данные: порядковый номер, поверхность,
    /// и нормаль (для стен — куда «смотрит» будущий стенсил-портал Step 3).
    /// Никакой логики — поведение зонами управляется из графа и режиссёра.
    /// </summary>
    public sealed class EchoZone : MonoBehaviour
    {
        public int Index { get; private set; }
        public EchoSurface Surface { get; private set; }
        /// <summary>Мировая нормаль поверхности (у стены — в комнату).</summary>
        public Vector3 SurfaceNormal { get; private set; }

        public void Init(int index, EchoSurface surface, Vector3 normal)
        {
            Index = index;
            Surface = surface;
            SurfaceNormal = normal;
        }
    }
}
```

---

## 13. `EchoZonePlacer.cs`

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Gate2Reality.SceneTwo
{
    using Gate2Reality.Narrative;

    /// <summary>
    /// Процедурное размещение эхо-зон Сцены 2 «Картограф» на реальных
    /// поверхностях комнаты игрока. Без геолокации: только плоскости ARCore
    /// и одометрия. Запускается активацией узла Чашки (к этому моменту
    /// ARPlaneManager успел отсканировать комнату за всю Сцену 1).
    ///
    /// АЛГОРИТМ:
    ///  1. Сбор кандидатов: трекаемые, не поглощённые плоскости с площадью
    ///     выше минимума. Стены — по alignment Vertical (работает на любом
    ///     устройстве, классификация лишь уточняет), поверхности — HorizontalUp.
    ///  2. Слоты: WallEcho (любая стена), SurfaceEcho (пол/стол), PortalWall
    ///     (САМАЯ БОЛЬШАЯ стена — финальной двери нужен размах).
    ///  3. Greedy-подбор с разносом minZoneSpacing и релаксацией порога
    ///     (1.5м -> 0.9м -> 0.4м): лучше тесные зоны, чем отказ.
    ///  4. Фолбэк-кольцо: комната без распознанных стен (бывает: зеркала,
    ///     однотонные обои) — зоны встают кольцом вокруг игрока на высоте
    ///     глаз. Та же философия, что в DepthPoseProjector: уровень 3 не
    ///     может провалиться.
    ///  5. ARAnchor на каждую зону: за минуты игры ARCore уточняет карту мира,
    ///     якорь «приклеивает» зону к реальной поверхности — без него зоны
    ///     уплывают на 10-20см и портал перестаёт лежать в стене.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EchoZonePlacer : MonoBehaviour
    {
        public enum ZoneKind : byte { WallEcho = 0, SurfaceEcho = 1, PortalWall = 2 }

        [Serializable]
        public struct ZoneSlot
        {
            public ZoneKind kind;
            [Tooltip("Индекс узла графа, который привяжется к этой зоне")]
            public int nodeIndex;
            [Tooltip("Визуал зоны (пульсирующий маркер/рамка портала). Опционален")]
            public GameObject visualPrefab;
        }

        [Header("Связи")]
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private ARAnchorManager anchorManager;
        [SerializeField] private NarrativeManager narrativeManager;
        [SerializeField] private Transform playerCamera;

        [Header("Конфигурация")]
        [Tooltip("Узел, активация которого запускает размещение (Чашка = 2)")]
        [SerializeField] private int triggerNodeIndex = 2;
        [SerializeField] private ZoneSlot[] slots =
        {
            // ПОРЯДОК = ПРИОРИТЕТ ВЫБОРА (не порядок прохождения игроком!).
            // PortalWall размещается ПЕРВЫМ и забирает самую большую стену;
            // остальные подбираются с разносом уже относительно него.
            // Прогон-симуляция поймала обратный порядок как баг: WallEcho
            // успевал украсть лучшую стену у финальной двери.
            new ZoneSlot { kind = ZoneKind.PortalWall,  nodeIndex = 5 },
            new ZoneSlot { kind = ZoneKind.WallEcho,    nodeIndex = 3 },
            new ZoneSlot { kind = ZoneKind.SurfaceEcho, nodeIndex = 4 },
        };

        [Header("Геометрия")]
        [SerializeField] private float minZoneSpacing = 1.5f;
        [SerializeField] private float minWallArea = 0.4f;     // м²
        [SerializeField] private float minSurfaceArea = 0.3f;  // м²
        [Tooltip("Полоса высоты для настенных зон (уровень глаз)")]
        [SerializeField] private float eyeMin = 1.0f, eyeMax = 1.8f;
        [SerializeField] private float fallbackRingRadius = 1.5f;

        public readonly struct PlacedZone
        {
            public readonly ZoneKind Kind;
            public readonly Transform Anchor;
            public PlacedZone(ZoneKind k, Transform a) { Kind = k; Anchor = a; }
        }

        /// <summary>Зоны размещены — подписчики: голо-карта, аудио-эмбиент.</summary>
        public event Action<IReadOnlyList<PlacedZone>> OnZonesPlaced;
        public IReadOnlyList<PlacedZone> Zones => _placed;

        private readonly List<PlacedZone> _placed = new List<PlacedZone>(3);
        private bool _done;

        private struct Candidate
        {
            public ARPlane Plane;
            public Vector3 Pos;
            public Quaternion Rot;
            public float Area;
        }

        // Переиспользуемые списки — размещение одноразовое, но привычка важнее.
        private readonly List<Candidate> _walls = new List<Candidate>(16);
        private readonly List<Candidate> _surfaces = new List<Candidate>(16);

        private void OnEnable() => narrativeManager.OnNodeActivated += HandleNodeActivated;
        private void OnDisable() => narrativeManager.OnNodeActivated -= HandleNodeActivated;

        private void HandleNodeActivated(int nodeIndex, Pose pose)
        {
            if (_done || nodeIndex != triggerNodeIndex) return;
            _done = true;
            PlaceZones();
        }

        // =====================================================================
        // РАЗМЕЩЕНИЕ
        // =====================================================================
        private void PlaceZones()
        {
            GatherCandidates();
            // Большие стены вперёд: слот PortalWall берёт _walls[0].
            _walls.Sort(static (a, b) => b.Area.CompareTo(a.Area));

            for (int i = 0; i < slots.Length; i++)
            {
                if (!TrySelect(slots[i].kind, out Candidate picked))
                {
                    picked = FallbackCandidate(i);
                }

                Transform anchor = CreateAnchor(in picked);

                // Маркер зоны (EchoZone): порядковый номер, тип поверхности и
                // нормаль — данные для эффектов, отладочного HUD и Сцены 3.
                EchoSurface surface = slots[i].kind == ZoneKind.SurfaceEcho
                    ? (picked.Pos.y > 0.35f ? EchoSurface.Table : EchoSurface.Floor)
                    : EchoSurface.Wall;
                anchor.gameObject.AddComponent<EchoZone>()
                      .Init(i, surface, picked.Rot * Vector3.forward);

                if (slots[i].visualPrefab != null)
                {
                    Instantiate(slots[i].visualPrefab, anchor, false);
                }

                narrativeManager.SetNodeRuntimeTarget(slots[i].nodeIndex, anchor);
                _placed.Add(new PlacedZone(slots[i].kind, anchor));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log($"[Gate2Reality] Зона {slots[i].kind} -> {picked.Pos} " +
                          $"(plane={(picked.Plane != null ? picked.Plane.trackableId.ToString() : "fallback")})");
#endif
            }

            OnZonesPlaced?.Invoke(_placed);

            // Плоскости отслужили: зоны заякорены (ARAnchor живут без
            // детекции), голо-карта построила контуры в обработчике события
            // выше. Гасим детекцию плоскостей до конца главы — ощутимая
            // экономия CPU/батареи на фоне и так горячего AR-трекинга.
            if (planeManager != null)
            {
                planeManager.requestedDetectionMode = PlaneDetectionMode.None;
            }
        }

        private void GatherCandidates()
        {
            _walls.Clear();
            _surfaces.Clear();
            Vector3 playerPos = playerCamera.position;

            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.trackingState != TrackingState.Tracking) continue;
                if (plane.subsumedBy != null) continue; // поглощена более крупной

                float area = plane.size.x * plane.size.y;

                if (plane.alignment == PlaneAlignment.Vertical)
                {
                    if (area < minWallArea) continue;

                    Vector3 pos = plane.center;
                    // Прижимаем к полосе глаз В ПРЕДЕЛАХ плоскости: выходить
                    // за её вертикальный размах нельзя — зона повиснет в воздухе.
                    float halfH = plane.size.y * 0.4f;
                    pos.y = Mathf.Clamp(pos.y,
                        Mathf.Max(eyeMin, plane.center.y - halfH),
                        Mathf.Min(eyeMax, plane.center.y + halfH));

                    // Нормаль стены — В КОМНАТУ (к игроку), иначе портал
                    // «смотрит» в соседскую квартиру.
                    Vector3 normal = plane.normal;
                    if (Vector3.Dot(normal, playerPos - pos) < 0f) normal = -normal;

                    _walls.Add(new Candidate
                    {
                        Plane = plane, Pos = pos,
                        Rot = Quaternion.LookRotation(normal), Area = area
                    });
                }
                else if (plane.alignment == PlaneAlignment.HorizontalUp)
                {
                    if (area < minSurfaceArea) continue;

                    Vector3 pos = plane.center;
                    Vector3 toPlayer = playerPos - pos; toPlayer.y = 0f;
                    Quaternion rot = toPlayer.sqrMagnitude > 0.001f
                        ? Quaternion.LookRotation(toPlayer.normalized)
                        : Quaternion.identity;

                    _surfaces.Add(new Candidate
                    {
                        Plane = plane, Pos = pos, Rot = rot, Area = area
                    });
                }
            }
        }

        /// <summary>Greedy с релаксацией разноса: 1.0x -> 0.6x -> 0.27x порога.</summary>
        private bool TrySelect(ZoneKind kind, out Candidate result)
        {
            List<Candidate> pool = kind == ZoneKind.SurfaceEcho ? _surfaces : _walls;
            // Поверхностей нет (пустая комната без стола, пол не пойман) —
            // SurfaceEcho деградирует до стены: лучше стена, чем фолбэк-кольцо.
            if (pool.Count == 0 && kind == ZoneKind.SurfaceEcho) pool = _walls;

            for (float spacing = minZoneSpacing; spacing >= minZoneSpacing * 0.25f; spacing *= 0.6f)
            {
                for (int i = 0; i < pool.Count; i++)
                {
                    // PortalWall обязан взять самую большую стену из допустимых —
                    // pool отсортирован по площади, первый проходной и есть лучший.
                    if (IsFarFromPlaced(pool[i].Pos, spacing))
                    {
                        result = pool[i];
                        return true;
                    }
                }
            }
            result = default;
            return false;
        }

        private bool IsFarFromPlaced(Vector3 pos, float spacing)
        {
            float sq = spacing * spacing;
            for (int i = 0; i < _placed.Count; i++)
            {
                if ((_placed[i].Anchor.position - pos).sqrMagnitude < sq) return false;
            }
            return true;
        }

        /// <summary>Уровень «не может провалиться»: кольцо вокруг игрока.</summary>
        private Candidate FallbackCandidate(int slotIndex)
        {
            float angle = slotIndex * 120f * Mathf.Deg2Rad; // 3 зоны через 120°
            Vector3 dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            dir = playerCamera.rotation * dir; dir.y = 0f; dir.Normalize();

            Vector3 pos = playerCamera.position + dir * fallbackRingRadius;
            pos.y = Mathf.Clamp(playerCamera.position.y, eyeMin, eyeMax);

            return new Candidate
            {
                Plane = null, Pos = pos,
                Rot = Quaternion.LookRotation(-dir), Area = 0f
            };
        }

        private Transform CreateAnchor(in Candidate c)
        {
            var pose = new Pose(c.Pos, c.Rot);

            // Якорь к плоскости — максимум стабильности при уточнении карты.
            if (c.Plane != null && anchorManager != null)
            {
                ARAnchor anchor = anchorManager.AttachAnchor(c.Plane, pose);
                if (anchor != null) return anchor.transform;
            }

            // Фолбэк-зона / нет менеджера якорей: обычный GameObject.
            // Он не «приклеен» к миру, но для кольца вокруг игрока это и не нужно.
            var go = new GameObject("EchoZoneAnchor");
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            return go.transform;
        }
    }
}
```

---

## 14. `HoloMapController.cs`

```csharp
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Gate2Reality.SceneTwo
{
    /// <summary>
    /// Превращает декоративную голограмму из финала Сцены 1 в игровую карту:
    /// контур комнаты (границы плоскостей ARCore) + пульсирующие метки
    /// эхо-зон + бегущая точка игрока. Вешается на holographicMapRoot
    /// из CupBreachEffect, строится по событию OnZonesPlaced.
    ///
    /// ПЕРФОРМАНС:
    ///  - Вся геометрия (LineRenderer'ы контуров, метки) строится ОДИН раз;
    ///    аллокации только в момент построения.
    ///  - В Update — позиция точки игрока + sin-пульс меток: копейки.
    ///  - Top-down проекция: Y мира схлопывается в тонкие слои карты —
    ///    читаемость важнее объёма.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HoloMapController : MonoBehaviour
    {
        [Header("Связи")]
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private EchoZonePlacer zonePlacer;
        [SerializeField] private Transform playerCamera;
        [Tooltip("Корень контента карты — дочерний объект holographicMapRoot")]
        [SerializeField] private Transform mapContentRoot;

        [Header("Вид")]
        [Tooltip("Радиус карты в метрах (комната вписывается целиком)")]
        [SerializeField] private float mapRadius = 0.22f;
        [SerializeField] private Material holoLineMaterial; // URP/Unlit, additive
        [SerializeField] private Color outlineColor = new Color(0.3f, 0.8f, 1f, 0.6f);
        [SerializeField] private Color zoneColor = new Color(0.3f, 0.9f, 1f, 1f);
        [SerializeField] private Color portalColor = new Color(0.9f, 0.4f, 1f, 1f);
        [SerializeField] private Color playerColor = new Color(1f, 0.8f, 0.3f, 1f);
        [SerializeField] private float lineWidth = 0.003f;
        [SerializeField] private float zonePulseHz = 1.1f;

        private Vector3 _roomCenter;
        private float _scale = 1f;
        private bool _built;

        private Transform _playerBlip;
        private readonly List<Transform> _zonePips = new List<Transform>(3);
        private readonly List<Vector3> _pipBaseScales = new List<Vector3>(3);

        private void OnEnable() => zonePlacer.OnZonesPlaced += Build;
        private void OnDisable() => zonePlacer.OnZonesPlaced -= Build;

        // =====================================================================
        // ПОСТРОЕНИЕ (один раз)
        // =====================================================================
        private void Build(IReadOnlyList<EchoZonePlacer.PlacedZone> zones)
        {
            if (_built) return;
            _built = true;

            ComputeRoomFit(zones);

            // --- Контуры плоскостей ---
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.trackingState != TrackingState.Tracking) continue;
                if (plane.subsumedBy != null) continue;
                DrawBoundary(plane);
            }

            // --- Метки зон ---
            for (int i = 0; i < zones.Count; i++)
            {
                bool isPortal = zones[i].Kind == EchoZonePlacer.ZoneKind.PortalWall;
                Transform pip = SpawnDot(
                    isPortal ? portalColor : zoneColor,
                    isPortal ? 0.020f : 0.012f);
                pip.localPosition = ToMap(zones[i].Anchor.position, layer: 0.012f);
                _zonePips.Add(pip);
                _pipBaseScales.Add(pip.localScale);
            }

            // --- Точка игрока ---
            _playerBlip = SpawnDot(playerColor, 0.010f);
        }

        /// <summary>Вписываем комнату в радиус карты: центр и масштаб по XZ-границам.</summary>
        private void ComputeRoomFit(IReadOnlyList<EchoZonePlacer.PlacedZone> zones)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            void Encapsulate(Vector3 p)
            {
                min = Vector2.Min(min, new Vector2(p.x, p.z));
                max = Vector2.Max(max, new Vector2(p.x, p.z));
            }

            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.trackingState != TrackingState.Tracking || plane.subsumedBy != null) continue;
                Encapsulate(plane.center - plane.transform.right * plane.size.x * 0.5f);
                Encapsulate(plane.center + plane.transform.right * plane.size.x * 0.5f);
            }
            for (int i = 0; i < zones.Count; i++) Encapsulate(zones[i].Anchor.position);
            Encapsulate(playerCamera.position);

            Vector2 c = (min + max) * 0.5f;
            _roomCenter = new Vector3(c.x, 0f, c.y);
            float extent = Mathf.Max((max - min).x, (max - min).y) * 0.5f;
            _scale = extent > 0.01f ? mapRadius / extent : 1f;
        }

        /// <summary>Мир -> локальные координаты карты (top-down, тонкие слои по Y).</summary>
        private Vector3 ToMap(Vector3 world, float layer)
        {
            Vector3 flat = (world - _roomCenter) * _scale;
            return new Vector3(flat.x, layer, flat.z);
        }

        private void DrawBoundary(ARPlane plane)
        {
            NativeArray<Vector2> boundary = plane.boundary;
            if (boundary.Length < 3) return;

            var go = new GameObject("MapOutline");
            go.transform.SetParent(mapContentRoot, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.widthMultiplier = lineWidth;
            lr.material = holoLineMaterial;
            lr.startColor = lr.endColor = outlineColor;
            lr.positionCount = boundary.Length;

            // boundary — в локальном XZ плоскости; через TransformPoint в мир,
            // затем в координаты карты. Стены при top-down проекции честно
            // схлопываются в отрезки — на карте это читается как «стена».
            for (int i = 0; i < boundary.Length; i++)
            {
                Vector3 world = plane.transform.TransformPoint(
                    new Vector3(boundary[i].x, 0f, boundary[i].y));
                lr.SetPosition(i, ToMap(world, layer: 0.006f));
            }
        }

        private Transform SpawnDot(Color color, float diameter)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>()); // физика в проекте мертва
            go.transform.SetParent(mapContentRoot, false);
            go.transform.localScale = Vector3.one * diameter;

            var r = go.GetComponent<Renderer>();
            r.material = holoLineMaterial;       // инстанс: цвет у каждого свой
            r.material.color = color;
            return go.transform;
        }

        // =====================================================================
        // АНИМАЦИЯ (только когда карта построена и видима)
        // =====================================================================
        private void Update()
        {
            if (!_built || !mapContentRoot.gameObject.activeInHierarchy) return;

            _playerBlip.localPosition = ToMap(playerCamera.position, layer: 0.018f);

            float pulse = 1f + 0.25f * Mathf.Sin(Time.time * zonePulseHz * 2f * Mathf.PI);
            for (int i = 0; i < _zonePips.Count; i++)
            {
                _zonePips[i].localScale = _pipBaseScales[i] * pulse;
            }
        }
    }
}
```

---

## 15. `SceneTwoDirector.cs`

```csharp
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
        [SerializeField] private EchoZonePlacer zonePlacer;

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
            zonePlacer.OnZonesPlaced += HandleZonesPlaced;
            narrativeManager.OnNodeActivated += HandleNodeActivated;
            narrativeManager.OnSceneCompleted += HandleChapterCompleted;
        }

        private void OnDisable()
        {
            zonePlacer.OnZonesPlaced -= HandleZonesPlaced;
            narrativeManager.OnNodeActivated -= HandleNodeActivated;
            narrativeManager.OnSceneCompleted -= HandleChapterCompleted;
        }

        // =====================================================================
        // КОНВЕЙЕР ШЁПОТОВ
        // =====================================================================
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
```

---

## 16. `PortalWindowEffect.cs`

```csharp
using UnityEngine;

namespace Gate2Reality.Effects
{
    /// <summary>
    /// «Окно в зазеркалье» как нарративный эффект графа: наследует
    /// TriggerableEffectBase, поэтому подключается к узлам Сцены 2 точно так
    /// же, как свет стула — к узлам Сцены 1. Один компонент обслуживает и
    /// малые эхо-окна (диаметр ~0.6м), и финальную дверь (~2.0м) — разница
    /// только в сериализованных параметрах.
    ///
    /// КОНВЕНЦИЯ ОРИЕНТАЦИИ (важно для авторинга префаба интерьера):
    ///   Якорь зоны от EchoZonePlacer смотрит forward'ом В КОМНАТУ
    ///   (нормаль стены к игроку). Значит:
    ///     - quad окна = licом по +Z якоря (к игроку);
    ///     - интерьер инвертированного мира уходит по ЛОКАЛЬНОМУ -Z —
    ///       префаб строится «вглубь» отрицательной оси Z.
    ///
    /// АНИМАЦИЯ АПЕРТУРЫ: easeOutBack — окно раскрывается с лёгким
    /// «перехлопом» (~9% овершут), как лопнувшая мембрана, и оседает
    /// в точный размер. Параметр _Aperture пишется через MPB — без
    /// инстансов материала, дружит с SRP Batcher.
    /// </summary>
    public sealed class PortalWindowEffect : TriggerableEffectBase
    {
        [Header("Окно")]
        [SerializeField] private Renderer windowRenderer;   // quad c PortalWindow
        [SerializeField] private float windowDiameterMeters = 0.6f; // дверь: ~2.0
        [SerializeField] private float openSeconds = 1.6f;
        [Tooltip("Сила овершута easeOutBack. 1.70158 = классические ~9%")]
        [SerializeField] private float overshoot = 1.70158f;

        [Header("Мир за стеной")]
        [Tooltip("Корень интерьера (материалы InvertedWorld). Контент уходит по локальному -Z")]
        [SerializeField] private Transform interiorRoot;

        [Header("Аудио")]
        [SerializeField] private AudioSource openAudio;     // разрыв мембраны
        [SerializeField] private AudioSource interiorAmbience; // гул зазеркалья, loop

        private static readonly int ApertureId = Shader.PropertyToID("_Aperture");

        private MaterialPropertyBlock _mpb;
        private bool _opened;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();

            // До срабатывания узла ни окна, ни мира не существует.
            if (windowRenderer != null)
            {
                SetAperture(0f);
                windowRenderer.gameObject.SetActive(false);
            }
            if (interiorRoot != null) interiorRoot.gameObject.SetActive(false);
        }

        protected override void OnTriggered()
        {
            _opened = false;

            // База уже поставила корень в позу якоря (snapToAnchor):
            // forward — в комнату. Масштабируем quad под диаметр; апертура=1
            // соответствует вписанной окружности, так что quad = диаметру.
            if (windowRenderer != null)
            {
                Transform wt = windowRenderer.transform;
                wt.localScale = new Vector3(windowDiameterMeters, windowDiameterMeters, 1f);
                windowRenderer.gameObject.SetActive(true);
                SetAperture(0f);
            }

            // Интерьер включается сразу: stencil-маска ещё нулевая, так что
            // визуально мир «вливается» строго вслед за раскрытием апертуры.
            if (interiorRoot != null) interiorRoot.gameObject.SetActive(true);

            if (openAudio != null) openAudio.Play();
            if (interiorAmbience != null) interiorAmbience.Play();
        }

        protected override void OnEffectUpdate(float dt)
        {
            if (_opened) return;

            float x = Mathf.Clamp01(TimeSinceTriggered / openSeconds);
            SetAperture(EaseOutBack(x));

            if (x >= 1f)
            {
                SetAperture(1f);
                _opened = true;
                MarkFinished(); // граф свободен; пульс обода живёт в шейдере
            }
        }

        protected override void OnCancelled()
        {
            SetAperture(0f);
            if (windowRenderer != null) windowRenderer.gameObject.SetActive(false);
            if (interiorRoot != null) interiorRoot.gameObject.SetActive(false);
            if (interiorAmbience != null) interiorAmbience.Stop();
        }

        // =====================================================================
        // ВСПОМОГАТЕЛЬНОЕ
        // =====================================================================
        private void SetAperture(float value)
        {
            if (windowRenderer == null) return;
            windowRenderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(ApertureId, Mathf.Max(0f, value));
            windowRenderer.SetPropertyBlock(_mpb);
        }

        /// <summary>easeOutBack: старт из 0, овершут ~9% на x~0.7, финиш ровно в 1.</summary>
        private float EaseOutBack(float x)
        {
            float c1 = overshoot;
            float c3 = c1 + 1f;
            float xm1 = x - 1f;
            return 1f + c3 * xm1 * xm1 * xm1 + c1 * xm1 * xm1;
        }
    }
}
```

---

## 17. `EchoSurfaceEffect.cs`

```csharp
using UnityEngine;

namespace Gate2Reality.Effects
{
    /// <summary>
    /// Эхо-зона на горизонтальной поверхности (пол/стол): из точки расходятся
    /// светящиеся круги — «зазеркалье просачивается снизу», как капли по воде,
    /// только наоборот: изнутри наружу.
    ///
    /// ПЕРЕИСПОЛЬЗОВАНИЕ: кольцо — это шейдер Gate2Reality/PortalRim на
    /// горизонтальном quad'е. Пилообразная анимация _Aperture (0 -> 1 -> сброс)
    /// даёт бесконечную рябь БЕЗ нового шейдера и без партиклов на филлрейте.
    /// Quad ориентируется в префабе (повёрнут на +90° по X относительно якоря —
    /// якорь поверхностной зоны смотрит forward'ом на игрока горизонтально).
    ///
    /// Дополнительно: столб «инвертированной пыли» (ParticleSystem, редкая
    /// эмиссия вверх) и низкий гул из-под поверхности.
    /// </summary>
    public sealed class EchoSurfaceEffect : TriggerableEffectBase
    {
        [Header("Рябь (quad c материалом PortalRim)")]
        [SerializeField] private Renderer rippleRenderer;
        [SerializeField] private float rippleDiameterMeters = 1.2f;
        [Tooltip("Период одного круга, сек")]
        [SerializeField] private float ripplePeriodSeconds = 2.2f;
        [Tooltip("Сколько кругов в интро (потом рябь продолжает жить тише)")]
        [SerializeField] private int introRippleCount = 3;
        [Tooltip("Множитель скорости ряби после интро")]
        [SerializeField] private float calmSpeedFactor = 0.5f;

        [Header("Атмосфера")]
        [SerializeField] private ParticleSystem dustColumn;
        [SerializeField] private AudioSource underworldHum; // loop, низкий гул

        private static readonly int ApertureId = Shader.PropertyToID("_Aperture");

        private MaterialPropertyBlock _mpb;
        private float _ripplePhase; // 0..1, пилообразная

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (rippleRenderer != null) rippleRenderer.gameObject.SetActive(false);
        }

        protected override void OnTriggered()
        {
            _ripplePhase = 0f;

            if (rippleRenderer != null)
            {
                Transform rt = rippleRenderer.transform;
                rt.localScale = new Vector3(rippleDiameterMeters, rippleDiameterMeters, 1f);
                rippleRenderer.gameObject.SetActive(true);
            }
            if (dustColumn != null) dustColumn.Play();
            if (underworldHum != null) underworldHum.Play();
        }

        protected override void OnEffectUpdate(float dt)
        {
            if (rippleRenderer == null) return;

            // Интро — полная скорость, дальше зона «успокаивается», но живёт
            // до конца главы (не зовём MarkFinished — стоимость копеечная).
            float introDuration = introRippleCount * ripplePeriodSeconds;
            float speed = TimeSinceTriggered < introDuration ? 1f : calmSpeedFactor;

            _ripplePhase += (dt / ripplePeriodSeconds) * speed;
            if (_ripplePhase >= 1f) _ripplePhase -= 1f; // пила: новый круг из центра

            rippleRenderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(ApertureId, _ripplePhase);
            rippleRenderer.SetPropertyBlock(_mpb);
        }

        protected override void OnCancelled()
        {
            if (rippleRenderer != null) rippleRenderer.gameObject.SetActive(false);
            if (dustColumn != null) dustColumn.Stop();
            if (underworldHum != null) underworldHum.Stop();
        }
    }
}
```

---

## 18. `CrossingTransitionEffect.cs`

```csharp
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
        [SerializeField] private Volume invertedSideVolume;
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
            if (invertedSideVolume != null) invertedSideVolume.weight = 0f;
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
                        if (invertedSideVolume != null) invertedSideVolume.weight = 1f;
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
            if (invertedSideVolume != null) invertedSideVolume.weight = 0f;
            if (crossingSwell != null) crossingSwell.Stop();
            if (otherSideAmbience != null) otherSideAmbience.Stop();
        }
    }
}
```

---

## 19. `INarrativeGenerator.cs`

```csharp
using System;

namespace Gate2Reality.Narrative
{
    /// <summary>Грубая эвристическая классификация помещения для промпта MLLM.</summary>
    public enum RoomType : byte
    {
        Unknown = 0,
        LivingRoom = 1,
        Office = 2,
        Kitchen = 3
    }

    /// <summary>
    /// Контекст для генерации нарратива. Struct — собирается на стеке перед
    /// каждым запросом, ничего не держит в куче.
    /// </summary>
    public readonly struct NarrativeContext
    {
        /// <summary>Объект, который игрок рассматривает прямо сейчас.</summary>
        public readonly NarrativeLabel FocusObject;

        /// <summary>Битовая маска всех объектов, замеченных YOLO за сессию:
        /// bit = (1 &lt;&lt; (int)NarrativeLabel). Компактнее списка, без аллокаций.</summary>
        public readonly int SeenObjectsMask;

        /// <summary>Средняя яркость сцены 0..1 (ARCore Light Estimation).
        /// Тёмная комната -> MLLM пишет более тихий, вкрадчивый текст.</summary>
        public readonly float AmbientBrightness;

        /// <summary>Цветовая температура, К (тёплый ламповый свет против
        /// холодного дневного — влияет на тон шёпота).</summary>
        public readonly float ColorTemperatureKelvin;

        /// <summary>Выведенный тип помещения.</summary>
        public readonly RoomType Room;

        public NarrativeContext(NarrativeLabel focus, int seenMask,
                                float brightness, float kelvin, RoomType room)
        {
            FocusObject = focus;
            SeenObjectsMask = seenMask;
            AmbientBrightness = brightness;
            ColorTemperatureKelvin = kelvin;
            Room = room;
        }

        public bool HasSeen(NarrativeLabel label) =>
            (SeenObjectsMask & (1 << (int)label)) != 0;
    }

    /// <summary>
    /// Абстракция генератора нарратива. Две реализации:
    ///  - OnDeviceNarrativeGenerator: локальный MLLM на устройстве
    ///    (MediaPipe LLM Inference / Gemma int4). Privacy Android 15:
    ///    ни промпт, ни ответ не покидают девайс.
    ///  - Фолбэк на заготовленные реплики — встроен в ту же реализацию
    ///    (модель не установлена / таймаут / OOM-килл сервиса).
    ///
    /// Контракт асинхронный: инференс LLM занимает 0.5-3 с, блокировать
    /// игровой поток нельзя. onResult ВСЕГДА вызывается ровно один раз
    /// и ВСЕГДА на главном потоке Unity.
    /// </summary>
    public interface INarrativeGenerator
    {
        /// <summary>true, если on-device модель реально загружена;
        /// false — работаем на заготовках (игра не ломается!).</summary>
        bool IsModelAvailable { get; }

        /// <summary>Запросить короткий шёпот (1-2 предложения) под контекст.</summary>
        void RequestWhisper(in NarrativeContext context, Action<string> onResult);
    }
}
```

---

## 20. `NarrativeContextCollector.cs`

```csharp
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Gate2Reality.Narrative
{
    using Gate2Reality.Detection;

    /// <summary>
    /// Сборщик контекста для MLLM: подписывается на ARCore Light Estimation
    /// и сырой поток YOLO-детекций, копит состояние и по запросу пакует его
    /// в NarrativeContext (struct, на стеке, без аллокаций).
    ///
    /// ТРЕБОВАНИЕ: на ARCameraManager включить Light Estimation =
    /// Ambient Intensity + Ambient Color (войдёт в чек-лист Step 5).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NarrativeContextCollector : MonoBehaviour
    {
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private YoloObjectDetector detector;

        private float _brightness = 0.5f;       // экспоненциальное сглаживание
        private float _kelvin;                  // 0 = неизвестно
        private int _seenMask;

        private void OnEnable()
        {
            cameraManager.frameReceived += OnFrame;
            detector.OnRawDetection += OnDetection;
        }

        private void OnDisable()
        {
            cameraManager.frameReceived -= OnFrame;
            detector.OnRawDetection -= OnDetection;
        }

        private void OnFrame(ARCameraFrameEventArgs args)
        {
            // Сглаживаем, чтобы шёпот не «мигал» от пролетевшей тени.
            if (args.lightEstimation.averageBrightness.HasValue)
            {
                _brightness = Mathf.Lerp(_brightness,
                    args.lightEstimation.averageBrightness.Value, 0.05f);
            }
            if (args.lightEstimation.averageColorTemperature.HasValue)
            {
                _kelvin = args.lightEstimation.averageColorTemperature.Value;
            }
        }

        private void OnDetection(DetectionEvent evt)
        {
            _seenMask |= 1 << (int)evt.Label;
        }

        /// <summary>Снимок контекста под текущий фокус-объект.</summary>
        public NarrativeContext Capture(NarrativeLabel focus)
        {
            return new NarrativeContext(focus, _seenMask, _brightness, _kelvin, InferRoom());
        }

        /// <summary>
        /// Эвристика комнаты по набору увиденных объектов. Когда YOLO-модель
        /// расширится (стол, диван, холодильник...) — эвристика станет богаче,
        /// интерфейс не изменится.
        /// </summary>
        private RoomType InferRoom()
        {
            bool chair = (_seenMask & (1 << (int)NarrativeLabel.Chair)) != 0;
            bool book = (_seenMask & (1 << (int)NarrativeLabel.Book)) != 0;
            bool cup = (_seenMask & (1 << (int)NarrativeLabel.Cup)) != 0;

            if (chair && book) return RoomType.Office;
            if (cup && !book) return RoomType.Kitchen;
            if (chair) return RoomType.LivingRoom;
            return RoomType.Unknown;
        }
    }
}
```

---

## 21. `OnDeviceNarrativeGenerator.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Gate2Reality.Narrative
{
    /// <summary>
    /// Обёртка над локальным on-device MLLM для Android 15.
    ///
    /// СТЕК: Kotlin-плагин NarrativeLlmBridge (см. NarrativeLlmBridge.kt) на
    /// MediaPipe LLM Inference API с Gemma-2B int4 (~1.2 ГБ в /data модели,
    /// инференс на GPU делегате). Полностью офлайн — требование privacy.
    ///
    /// ГАРАНТИИ:
    ///  - onResult вызывается ровно один раз, всегда на главном потоке Unity
    ///    (коллбэк из Kotlin прилетает на binder-потоке -> очередь + Update).
    ///  - Жёсткий таймаут: если модель думает дольше timeoutSeconds — отдаём
    ///    заготовку. Игрок НИКОГДА не ждёт нарратив, нарратив ждёт игрока.
    ///  - Нет модели / не Android / эксепшен — мгновенный фолбэк.
    /// </summary>
    public sealed class OnDeviceNarrativeGenerator : MonoBehaviour, INarrativeGenerator
    {
        [Header("Параметры инференса")]
        [SerializeField] private float timeoutSeconds = 3f;
        [SerializeField] private int maxTokens = 48; // шёпот короткий, экономим миллисекунды

        public bool IsModelAvailable { get; private set; }

        // ---- Мост в Kotlin ----
        private AndroidJavaObject _bridge;

        // ---- Состояние текущего запроса (одновременно живёт максимум один:
        //      нарративные узлы срабатывают последовательно) ----
        private Action<string> _pendingCallback;
        private float _deadline;
        private string[] _pendingPool = GenericLines; // фолбэк-пул текущего запроса

        // Потокобезопасная передача результата с binder-потока на главный.
        private readonly object _resultLock = new object();
        private string _threadedResult;
        private bool _hasThreadedResult;

        // =====================================================================
        // ЗАГОТОВКИ (фолбэк). Подобраны под каждый объект Сцены 1.
        // =====================================================================
        private static readonly string[] ChairLines =
        {
            "Кто-то сидел здесь. Долго. Дерево ещё помнит тяжесть.",
            "Стул не двигали годами. Спроси его — почему."
        };
        private static readonly string[] BookLines =
        {
            "Страницы листает не ветер. Здесь нет ветра.",
            "Эту книгу читали вслух. Слова всё ещё в комнате."
        };
        private static readonly string[] CupLines =
        {
            "Трещина была всегда. Ты просто научился её видеть.",
            "Чашку разбили не здесь. Но осколки упали сюда."
        };
        private static readonly string[] MirrorLines =
        {
            "С той стороны комната выглядит так же. Почти.",
            "Здесь тоже кто-то ходит. Он только что остановился.",
            "Не прижимайся к стеклу. Оно помнит лица."
        };
        private static readonly string[] GenericLines =
        {
            "Тише. Комната слушает тебя в ответ."
        };

        // =====================================================================
        // LIFECYCLE
        // =====================================================================
        private void Awake()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // Kotlin-singleton: NarrativeLlmBridge.getInstance(context)
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var bridgeClass = new AndroidJavaClass("com.gate2reality.llm.NarrativeLlmBridge");
                _bridge = bridgeClass.CallStatic<AndroidJavaObject>("getInstance", activity);
                IsModelAvailable = _bridge != null && _bridge.Call<bool>("isModelReady");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Gate2Reality] MLLM-мост недоступен, фолбэк на заготовки: {e.Message}");
                IsModelAvailable = false;
            }
#else
            IsModelAvailable = false; // редактор: всегда заготовки
#endif
        }

        private void OnDestroy() => _bridge?.Dispose();

        // =====================================================================
        // INarrativeGenerator
        // =====================================================================
        public void RequestWhisper(in NarrativeContext context, Action<string> onResult)
        {
            RequestInternal(
                BuildPrompt(LabelToRussian(context.FocusObject), in context, mirrorSide: false),
                PoolFor(context.FocusObject), onResult);
        }

        /// <summary>
        /// Сцена 2: шёпот «с той стороны» про произвольный сюжет (эхо-зоны,
        /// портал). Интерфейс INarrativeGenerator не трогаем — это расширение
        /// конкретной реализации, SceneTwoDirector держит её напрямую.
        /// </summary>
        public void RequestMirrorWhisper(string subjectRu, in NarrativeContext context, Action<string> onResult)
        {
            RequestInternal(
                BuildPrompt(subjectRu, in context, mirrorSide: true),
                MirrorLines, onResult);
        }

        private void RequestInternal(string prompt, string[] fallbackPool, Action<string> onResult)
        {
            if (onResult == null) return;

            // Запрос уже в полёте? Новый вытесняет старый: старый коллбэк
            // НЕМЕДЛЕННО (синхронно, здесь же) получает фолбэк из СВОЕГО пула —
            // гарантия «ровно один вызов» не нарушается, тексты не теряются.
            if (_pendingCallback != null) ResolveWithFallback();

            _pendingPool = fallbackPool;

            if (!IsModelAvailable)
            {
                onResult(Pick(fallbackPool));
                return;
            }

            _pendingCallback = onResult;
            _deadline = Time.unscaledTime + timeoutSeconds;
#if UNITY_ANDROID && !UNITY_EDITOR
            // generateAsync(prompt, maxTokens, callback) — неблокирующий вызов.
            _bridge.Call("generateAsync", prompt, maxTokens, new LlmCallbackProxy(this));
#endif
        }

        // =====================================================================
        // ПРОМПТ: компактный, на StringBuilder с преаллокацией. Генерация —
        // редкое событие (раз в узел), так что одна аллокация строки допустима.
        // =====================================================================
        private static readonly StringBuilder s_Prompt = new StringBuilder(512);

        private static string BuildPrompt(string subjectRu, in NarrativeContext ctx, bool mirrorSide)
        {
            s_Prompt.Clear();
            s_Prompt.Append("Ты — голос-шёпот в атмосферной AR-игре ужасов. ");
            if (mirrorSide)
            {
                s_Prompt.Append("Ты говоришь С ТОЙ СТОРОНЫ — из зеркальной, неправильной версии этой же комнаты. ");
            }
            s_Prompt.Append("Напиши ровно одну жуткую, поэтичную реплику (максимум 2 коротких предложения, без кавычек) про: ");
            s_Prompt.Append(subjectRu);
            s_Prompt.Append(". Обстановка: ");
            s_Prompt.Append(ctx.Room switch
            {
                RoomType.Office => "рабочий кабинет",
                RoomType.Kitchen => "кухня",
                RoomType.LivingRoom => "гостиная",
                _ => "комната"
            });
            s_Prompt.Append(ctx.AmbientBrightness < 0.35f
                ? ", в комнате полумрак"
                : ", комната освещена");
            if (ctx.ColorTemperatureKelvin > 0f && ctx.ColorTemperatureKelvin < 3500f)
                s_Prompt.Append(", тёплый ламповый свет");
            s_Prompt.Append(". Не упоминай игру, камеру и телефон.");
            return s_Prompt.ToString();
        }

        private static string LabelToRussian(NarrativeLabel l) => l switch
        {
            NarrativeLabel.Chair => "старый стул",
            NarrativeLabel.Book => "раскрытая книга",
            NarrativeLabel.Cup => "треснувшая чашка",
            _ => "пустая комната"
        };

        // =====================================================================
        // ПРИЁМ РЕЗУЛЬТАТА (binder-поток -> главный) + ТАЙМАУТ
        // =====================================================================
        private void Update()
        {
            if (_pendingCallback == null) return;

            // 1) Результат с потока Kotlin?
            if (_hasThreadedResult)
            {
                string text;
                lock (_resultLock)
                {
                    text = _threadedResult;
                    _hasThreadedResult = false;
                }
                Resolve(string.IsNullOrWhiteSpace(text)
                    ? Pick(_pendingPool)
                    : text.Trim());
                return;
            }

            // 2) Таймаут?
            if (Time.unscaledTime >= _deadline) ResolveWithFallback();
        }

        private void Resolve(string text)
        {
            var cb = _pendingCallback;
            _pendingCallback = null;
            cb?.Invoke(text);
        }

        private void ResolveWithFallback() => Resolve(Pick(_pendingPool));

        private static string[] PoolFor(NarrativeLabel focus) => focus switch
        {
            NarrativeLabel.Chair => ChairLines,
            NarrativeLabel.Book => BookLines,
            NarrativeLabel.Cup => CupLines,
            _ => GenericLines
        };

        private static string Pick(string[] pool)
        {
            // UnityEngine.Random — только главный поток; Pick зовётся
            // исключительно из Update/RequestInternal, так что безопасно.
            return pool[UnityEngine.Random.Range(0, pool.Length)];
        }

        /// <summary>Колбэк-прокси для Kotlin-интерфейса LlmCallback { fun onResult(text: String) }.</summary>
        private sealed class LlmCallbackProxy : AndroidJavaProxy
        {
            private readonly OnDeviceNarrativeGenerator _owner;

            public LlmCallbackProxy(OnDeviceNarrativeGenerator owner)
                : base("com.gate2reality.llm.LlmCallback") => _owner = owner;

            // Вызывается НЕ на главном потоке! Только кладём в почтовый ящик.
            public void onResult(string text)
            {
                lock (_owner._resultLock)
                {
                    _owner._threadedResult = text;
                    _owner._hasThreadedResult = true;
                }
            }
        }
    }
}
```

---

## 22. `WhisperSubtitleController.cs`

```csharp
using TMPro;
using UnityEngine;

namespace Gate2Reality.UI
{
    /// <summary>
    /// Призрачные субтитры для шёпотов MLLM. Аудио-клип шёпота — нечленораздельная
    /// «подложка» (атмосфера), смысл несёт текст: это решает проблему
    /// «MLLM отдаёт текст, а источник играет клип» без TTS (роботичный голос
    /// убил бы хоррор) и бесплатно по перформансу.
    ///
    /// Эффект печатной машинки — через TMP maxVisibleCharacters:
    /// НОЛЬ аллокаций (никаких Substring/StringBuilder в Update),
    /// строка задаётся один раз в Show().
    ///
    /// Размещение: Screen Space - Overlay, нижняя треть экрана,
    /// полупрозрачный текст с лёгким свечением (материал TMP).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WhisperSubtitleController : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TMP_Text label;

        [Header("Тайминги")]
        [SerializeField] private float fadeSeconds = 0.6f;
        [Tooltip("Скорость «печати», знаков/сек. 16-20 = темп шёпота")]
        [SerializeField] private float charsPerSecond = 18f;
        [Tooltip("Пауза после полного появления текста до растворения")]
        [SerializeField] private float holdSeconds = 2.5f;

        private enum Phase : byte { Hidden, FadeIn, Reveal, Hold, FadeOut }
        private Phase _phase = Phase.Hidden;
        private float _phaseTimer;
        private int _totalChars;

        private void Awake()
        {
            canvasGroup.alpha = 0f;
            label.text = string.Empty;
        }

        /// <summary>Показать реплику. Повторный вызов перезапускает показ новым текстом.</summary>
        public void Show(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            label.text = text;                 // единственная «строковая» операция
            label.maxVisibleCharacters = 0;
            _totalChars = text.Length;
            SetPhase(Phase.FadeIn);
        }

        private void Update()
        {
            if (_phase == Phase.Hidden) return; // спящий субтитр бесплатен
            _phaseTimer += Time.deltaTime;

            switch (_phase)
            {
                case Phase.FadeIn:
                    canvasGroup.alpha = Mathf.Clamp01(_phaseTimer / fadeSeconds);
                    if (_phaseTimer >= fadeSeconds) SetPhase(Phase.Reveal);
                    break;

                case Phase.Reveal:
                    // int-каст монотонно растёт — TMP перестраивает меш только
                    // при реальном изменении количества видимых знаков.
                    int visible = (int)(_phaseTimer * charsPerSecond);
                    label.maxVisibleCharacters = visible;
                    if (visible >= _totalChars) SetPhase(Phase.Hold);
                    break;

                case Phase.Hold:
                    if (_phaseTimer >= holdSeconds) SetPhase(Phase.FadeOut);
                    break;

                case Phase.FadeOut:
                    canvasGroup.alpha = 1f - Mathf.Clamp01(_phaseTimer / fadeSeconds);
                    if (_phaseTimer >= fadeSeconds)
                    {
                        label.text = string.Empty;
                        SetPhase(Phase.Hidden);
                    }
                    break;
            }
        }

        private void SetPhase(Phase p)
        {
            _phase = p;
            _phaseTimer = 0f;
            if (p == Phase.Reveal) canvasGroup.alpha = 1f;
        }
    }
}
```

---

## 23. `NarrativeLlmBridge.kt`

```kotlin
package com.gate2reality.llm

import android.app.Activity
import android.os.Handler
import android.os.HandlerThread
import com.google.mediapipe.tasks.genai.llminference.LlmInference
import java.io.File

/**
 * Android-сторона моста к on-device MLLM (MediaPipe LLM Inference API).
 * Кладётся в Assets/Plugins/Android вместе с зависимостью
 * com.google.mediapipe:tasks-genai в mainTemplate.gradle.
 *
 * МОДЕЛЬ: gemma-2b-it int4 (.task), доставляется через Play Asset Delivery
 * (install-time pack) в filesDir — НЕ в APK (лимит размера) и НЕ из сети
 * в рантайме (privacy-обещание «всё на устройстве» держим честно).
 *
 * ПОТОКИ: инференс на выделенном HandlerThread с пониженным приоритетом —
 * не воюем за big-ядра с рендером Unity и не провоцируем троттлинг.
 */
interface LlmCallback {
    fun onResult(text: String)
}

class NarrativeLlmBridge private constructor(activity: Activity) {

    // Захватываем application context сразу — не держим ссылку на Activity
    // (утечка при повороте экрана) и не лезем за ним из воркер-потока.
    private val appContext = activity.applicationContext

    companion object {
        @Volatile private var instance: NarrativeLlmBridge? = null

        @JvmStatic
        fun getInstance(activity: Activity): NarrativeLlmBridge =
            instance ?: synchronized(this) {
                instance ?: NarrativeLlmBridge(activity).also { instance = it }
            }
    }

    private val modelFile = File(activity.filesDir, "models/gemma-2b-it-int4.task")
    private var llm: LlmInference? = null

    private val workerThread = HandlerThread(
        "Gate2RealityLLM",
        android.os.Process.THREAD_PRIORITY_BACKGROUND
    ).apply { start() }
    private val worker = Handler(workerThread.looper)

    init {
        if (modelFile.exists()) {
            // Ленивая инициализация на воркере: первая загрузка модели ~1-2с,
            // главный поток (и Unity) этого не почувствуют.
            worker.post {
                try {
                    val options = LlmInference.LlmInferenceOptions.builder()
                        .setModelPath(modelFile.absolutePath)
                        .setMaxTokens(96)
                        .setTemperature(0.9f)   // шёпоту положено быть непредсказуемым
                        .setTopK(40)
                        .build()
                    llm = LlmInference.createFromOptions(appContext, options)
                } catch (_: Throwable) {
                    llm = null // C#-сторона уйдёт в фолбэк по isModelReady()
                }
            }
        }
    }

    /** Дёргается из C# (Awake). До конца ленивой инициализации честно вернёт false. */
    fun isModelReady(): Boolean = llm != null

    /** Неблокирующая генерация; коллбэк прилетит с воркер-потока. */
    fun generateAsync(prompt: String, maxTokens: Int, callback: LlmCallback) {
        worker.post {
            val text = try {
                llm?.generateResponse(prompt) ?: ""
            } catch (_: Throwable) {
                "" // пустая строка -> C#-сторона подставит заготовку
            }
            callback.onResult(text)
        }
    }
}
```

---

## 24. `HorrorSafetyGovernor.cs`

```csharp
using UnityEngine;
using UnityEngine.Audio;

namespace Gate2Reality.Effects
{
    using Gate2Reality.Detection;

    /// <summary>
    /// Privacy / Safety Governor (Google Play safety + здравый смысл):
    /// если в кадре появился человек (YOLO class 'person'), хоррор-элементы
    /// плавно гасятся — никаких пугающих наложений на живых людей и никакого
    /// «обвешивания» случайно попавших в кадр членов семьи эффектами.
    ///
    /// МЕХАНИКА (с гистерезисом, чтобы не мигало):
    ///  - Человек появился -> БЫСТРЫЙ спад (0.5с) до safeIntensity.
    ///  - Человек исчез -> ждём clearDelaySeconds (вдруг вернётся в кадр),
    ///    затем МЕДЛЕННОЕ восстановление (3с) до 1.0 — хоррор «вкрадывается»
    ///    обратно, это даже работает на атмосферу.
    ///
    /// КАНАЛЫ ВЛИЯНИЯ (всё — O(1), без обхода объектов сцены):
    ///  1. Shader.SetGlobalFloat(_HorrorScale) — гасит дисторсию и трещины
    ///     на всех материалах RealityDistortion разом.
    ///  2. AudioMixer-группа "Horror" (шёпот, шум, маяк) — дакинг громкости.
    ///  3. Событие OnIntensityChanged — для систем, которым нужен сам факт
    ///     (например, отложить взрыв осколков, пока в кадре человек).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HorrorSafetyGovernor : MonoBehaviour
    {
        [Header("Связи")]
        [SerializeField] private YoloObjectDetector detector;
        [SerializeField] private AudioMixer audioMixer;
        [Tooltip("Exposed-параметр громкости хоррор-группы микшера, дБ")]
        [SerializeField] private string mixerVolumeParam = "HorrorVolumeDb";

        [Header("Поведение")]
        [Tooltip("Остаточная интенсивность при человеке в кадре. Не ноль: лёгкое присутствие эффектов допустимо, исчезает только пугающее.")]
        [Range(0f, 1f)] [SerializeField] private float safeIntensity = 0.25f;
        [SerializeField] private float fadeDownSeconds = 0.5f;
        [SerializeField] private float fadeUpSeconds = 3f;
        [Tooltip("Сколько секунд кадр должен быть чист, прежде чем хоррор вернётся")]
        [SerializeField] private float clearDelaySeconds = 4f;

        /// <summary>Текущая интенсивность 0..1 — подписка для прочих систем.</summary>
        public event System.Action<float> OnIntensityChanged;
        public float CurrentIntensity { get; private set; } = 1f;

        private static readonly int HorrorScaleId = Shader.PropertyToID("_HorrorScale");

        private bool _humanVisible;
        private float _humanClearedAt;
        private float _target = 1f;

        private void Awake()
        {
            // КРИТИЧНО: глобальные шейдерные float по умолчанию 0 —
            // без этой строки все эффекты RealityDistortion невидимы.
            Shader.SetGlobalFloat(HorrorScaleId, 1f);
        }

        private void OnEnable() => detector.OnHumanPresenceChanged += HandlePresence;
        private void OnDisable() => detector.OnHumanPresenceChanged -= HandlePresence;

        private void HandlePresence(bool humanVisible)
        {
            _humanVisible = humanVisible;
            if (humanVisible)
            {
                _target = safeIntensity; // гасим сразу
            }
            else
            {
                _humanClearedAt = Time.unscaledTime; // восстановление — после паузы
            }
        }

        private void Update()
        {
            // Кадр чист достаточно долго? Разрешаем восстановление.
            if (!_humanVisible && _target < 1f &&
                Time.unscaledTime - _humanClearedAt >= clearDelaySeconds)
            {
                _target = 1f;
            }

            if (Mathf.Approximately(CurrentIntensity, _target)) return;

            // Асимметричная скорость: вниз — быстро (safety), вверх — медленно.
            float speed = _target < CurrentIntensity
                ? 1f / Mathf.Max(0.01f, fadeDownSeconds)
                : 1f / Mathf.Max(0.01f, fadeUpSeconds);

            CurrentIntensity = Mathf.MoveTowards(
                CurrentIntensity, _target, speed * Time.unscaledDeltaTime);

            Apply(CurrentIntensity);
        }

        private void Apply(float intensity)
        {
            // 1) Все шейдеры — одним вызовом.
            Shader.SetGlobalFloat(HorrorScaleId, intensity);

            // 2) Аудио: линейная интенсивность -> децибелы (лог-шкала).
            if (audioMixer != null)
            {
                float db = Mathf.Log10(Mathf.Max(intensity, 0.0001f)) * 20f;
                audioMixer.SetFloat(mixerVolumeParam, Mathf.Max(db, -40f));
            }

            // 3) Подписчики (CupBreachEffect может отложить взрыв осколков и т.п.)
            OnIntensityChanged?.Invoke(intensity);
        }
    }
}
```

---

## 25. `DeviceTuningProfile.cs`

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Gate2Reality
{
    using Gate2Reality.Detection;

    /// <summary>
    /// Рантайм-профилировщик устройства. Один раз на старте определяет тир
    /// железа и применяет настройки производительности — игра подстраивается
    /// под телефон сама, без ручных правок под каждую модель.
    ///
    /// ТИРЫ (по GPU + RAM):
    ///   Flagship — Adreno 7xx/8xx, Immortalis, Xclipse (Pixel 9, S26):
    ///              YOLO 5 Гц, Environment Depth = Best, renderScale 1.0
    ///   Mid      — Adreno 6xx, Mali-G7x (HONOR 90 / Snapdragon 7 Gen 1):
    ///              YOLO 3.3 Гц (300мс), Environment Depth = Fastest,
    ///              renderScale 0.9 — на 1.5K-экране Honor 90 неотличимо,
    ///              а это минус ~19% фрагментной нагрузки
    ///   Low      — всё остальное: YOLO 2.5 Гц, renderScale 0.8
    ///
    /// Плюс честная проверка Depth API через дескриптор подсистемы: если
    /// поддержки нет (на Honor 90 ЕСТЬ — устройство в официальном списке
    /// ARCore с пометкой Supports Depth API), окклюзия отключается и в лог
    /// уходит предупреждение о деградации (рука не перекрывает порталы,
    /// DepthPoseProjector работает с уровня 2 фолбэк-цепочки).
    ///
    /// ВАЖНО: Script Execution Order — этот компонент ПЕРВЫМ (до детектора).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeviceTuningProfile : MonoBehaviour
    {
        public enum Tier : byte { Flagship = 0, Mid = 1, Low = 2 }

        [Header("Связи")]
        [SerializeField] private AROcclusionManager occlusionManager;
        [SerializeField] private YoloObjectDetector detector;

        [Header("Частоты YOLO по тирам, мс")]
        [SerializeField] private int flagshipIntervalMs = 200;
        [SerializeField] private int midIntervalMs = 300;
        [SerializeField] private int lowIntervalMs = 400;

        [Header("Render Scale по тирам")]
        [SerializeField] private float midRenderScale = 0.9f;
        [SerializeField] private float lowRenderScale = 0.8f;

        public Tier DetectedTier { get; private set; }

        private void Awake()
        {
            // Якорная анти-троттлинг мера для ЛЮБОГО тира (чек-лист, §11).
            Application.targetFrameRate = 30;

            DetectedTier = DetectTier();
            ApplyTier(DetectedTier);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Gate2Reality] Тир устройства: {DetectedTier} " +
                      $"(GPU: {SystemInfo.graphicsDeviceName}, " +
                      $"RAM: {SystemInfo.systemMemorySize} МБ, " +
                      $"SoC-модель: {SystemInfo.deviceModel})");
#endif
        }

        private IEnumerator Start()
        {
            // Дескриптор окклюзии валиден только после старта подсистемы.
            yield return null;
            yield return new WaitForSeconds(0.5f);
            VerifyDepthSupport();
        }

        // =====================================================================
        // ОПРЕДЕЛЕНИЕ ТИРА
        // =====================================================================
        private static Tier DetectTier()
        {
            string gpu = SystemInfo.graphicsDeviceName ?? string.Empty;
            int ramMb = SystemInfo.systemMemorySize;

            // Флагманские GPU 2023+. Adreno 7xx/8xx, ARM Immortalis, Samsung Xclipse.
            if (Contains(gpu, "Adreno (TM) 7") || Contains(gpu, "Adreno (TM) 8") ||
                Contains(gpu, "Immortalis") || Contains(gpu, "Xclipse"))
            {
                return Tier.Flagship;
            }

            // Средний класс: Adreno 6xx (644 = Honor 90 / SD 7 Gen 1),
            // Mali-G7x. RAM >= 8ГБ страхует от ложного Mid на старье.
            if ((Contains(gpu, "Adreno (TM) 6") || Contains(gpu, "Mali-G7")) && ramMb >= 7000)
            {
                return Tier.Mid;
            }

            return Tier.Low;
        }

        private static bool Contains(string s, string sub) =>
            s.IndexOf(sub, System.StringComparison.OrdinalIgnoreCase) >= 0;

        // =====================================================================
        // ПРИМЕНЕНИЕ ПРОФИЛЯ
        // =====================================================================
        private void ApplyTier(Tier tier)
        {
            // 1) Частота YOLO
            if (detector != null)
            {
                detector.SetInferenceInterval(tier switch
                {
                    Tier.Flagship => flagshipIntervalMs,
                    Tier.Mid => midIntervalMs,
                    _ => lowIntervalMs
                });
            }

            // 2) Режим Environment Depth: Best на флагманах, Fastest ниже —
            //    на Adreno 644 'Best' съедал бы заметную долю кадра.
            if (occlusionManager != null)
            {
                occlusionManager.requestedEnvironmentDepthMode = tier == Tier.Flagship
                    ? EnvironmentDepthMode.Best
                    : EnvironmentDepthMode.Fastest;
            }

            // 3) Render Scale URP
            if (tier != Tier.Flagship &&
                GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = tier == Tier.Mid ? midRenderScale : lowRenderScale;
            }
        }

        // =====================================================================
        // ПРОВЕРКА DEPTH API (честная, через дескриптор подсистемы)
        // =====================================================================
        private void VerifyDepthSupport()
        {
            if (occlusionManager == null) return;

            var descriptor = occlusionManager.descriptor;
            bool supported = descriptor != null &&
                descriptor.environmentDepthImageSupported == Supported.Supported;

            if (!supported)
            {
                // Грациозная деградация: окклюзию выключаем, проекция YOLO
                // работает с уровня 2 фолбэк-цепочки (плоскости), рука не
                // перекрывает порталы. Глава ИГРАБЕЛЬНА, но беднее тактильно.
                occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
                Debug.LogWarning("[Gate2Reality] Depth API НЕ поддерживается: " +
                                 "окклюзия выключена, fallback-проекция уровня 2+.");
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            else
            {
                Debug.Log("[Gate2Reality] Depth API: поддерживается " +
                          $"(режим: {occlusionManager.requestedEnvironmentDepthMode}).");
            }
#endif
        }
    }
}
```

---

## 26. `RealityDistortion.shader`

```hlsl
// =============================================================================
// Gate2Reality / RealityDistortion
// Один шейдер на оба эффекта Сцены 1:
//   - "вибрация реальности" на ножках стула  -> _DistortionStrength (0..1)
//   - сине-светящаяся трещина на чашке       -> _CrackProgress (0..1)
// Управляется из C# через MaterialPropertyBlock (Step 3), поэтому один
// материал обслуживает оба оверлея без дублирования.
//
// ПРИНЦИП: оверлей-меш (quad/низкополигональная оболочка) рисуется поверх
// физического объекта и "переснимает" сцену позади себя из
// _CameraOpaqueTexture со смещёнными UV — классический heat-haze, но без
// GrabPass (его в URP нет и он был бы смертелен для tiler-GPU).
//
// ТРЕБОВАНИЕ ПАЙПЛАЙНА (войдёт в чек-лист Step 5):
//   URP Asset -> Opaque Texture = ON (Downsampling: 2x Bilinear достаточно).
//
// МОБИЛЬНЫЙ БЮДЖЕТ (Pixel 9 / S26, Vulkan):
//   - 2 выборки текстур на пиксель (1x noise, 1x scene color) + арифметика;
//   - всё в half: на Adreno/Mali это буквально вдвое дешевле float;
//   - ZWrite Off, прозрачная очередь, оверлеи маленькие -> overdraw ничтожен.
// =============================================================================
Shader "Gate2Reality/RealityDistortion"
{
    Properties
    {
        [NoScaleOffset] _NoiseTex ("Noise (RG: flow-вектора, B: трещины)", 2D) = "gray" {}

        [Header(Distortion ... chair legs)]
        _DistortionStrength ("Distortion Strength", Range(0, 1)) = 0
        _DistortionScale    ("Noise UV Scale",      Float)       = 3
        _DistortionSpeed    ("Scroll Speed",        Float)       = 1.5
        _DistortionAmount   ("Max UV Offset",       Range(0, 0.1)) = 0.03

        [Header(Crack ... cup breach)]
        _CrackProgress ("Crack Progress", Range(0, 1)) = 0
        [HDR] _CrackColor ("Crack Color (HDR, синий)", Color) = (0.25, 0.7, 2.5, 1)
        _CrackScale ("Crack UV Scale", Float) = 6
        _CrackWidth ("Crack Line Width", Range(0.001, 0.2)) = 0.045
        _CrackPulseHz ("Crack Pulse, Hz", Float) = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "RealityDistortion"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            // Вырезаем варианты, которые нам не нужны, — меньше компиляций,
            // быстрее загрузка на устройстве.
            #pragma skip_variants FOG_EXP FOG_EXP2 FOG_LINEAR

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            // CBUFFER -> совместимость с SRP Batcher (критично: оба оверлея
            // и любые будущие инстансы батчатся в один проход настройки).
            CBUFFER_START(UnityPerMaterial)
                half _DistortionStrength;
                half _DistortionScale;
                half _DistortionSpeed;
                half _DistortionAmount;
                half _CrackProgress;
                half4 _CrackColor;
                half _CrackScale;
                half _CrackWidth;
                half _CrackPulseHz;
            CBUFFER_END

            // ГЛОБАЛЬНЫЙ privacy-множитель (0..1). Пишется одним вызовом
            // Shader.SetGlobalFloat из HorrorSafetyGovernor — гасит хоррор
            // на ВСЕХ материалах сразу, без обхода рендереров.
            // ВНИМАНИЕ: по умолчанию глобалы = 0 — Governor ОБЯЗАН выставить
            // 1.0 в Awake (пункт чек-листа), иначе эффекты невидимы.
            half _HorrorScale;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1; // для выборки opaque-текстуры
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // ------------------------------------------------------------
                // 0. Общие величины
                // ------------------------------------------------------------
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                half t = (half)_Time.y;

                // Мягкое затухание к краям quad'а, чтобы оверлей не имел
                // видимой рамки: 1 в центре -> 0 на краях.
                half2 centered = abs(input.uv - 0.5h) * 2.0h;
                half edgeFade = saturate(1.0h - max(centered.x, centered.y));
                edgeFade *= edgeFade; // квадратичное — дешевле smoothstep

                // ------------------------------------------------------------
                // 1. ДИСТОРСИЯ: flow-вектора из RG-каналов шума
                // ------------------------------------------------------------
                float2 noiseUV = input.uv * _DistortionScale
                               + float2(0.0, t * _DistortionSpeed * 0.1);
                half3 noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV).rgb;

                half2 flow = (noise.rg * 2.0h - 1.0h);       // -1..1
                // Дрожь: высокочастотная синус-модуляция поверх плавного flow —
                // даёт нервную "вибрацию", а не ленивое марево.
                half jitter = sin(t * 23.0h + noise.r * 12.0h) * 0.5h + 0.5h;
                half2 offset = flow * jitter
                             * (_DistortionAmount * _DistortionStrength * edgeFade * _HorrorScale);

                half3 sceneColor = SampleSceneColor(screenUV + offset).rgb;

                // ------------------------------------------------------------
                // 2. ТРЕЩИНА: тонкие линии из B-канала шума + радиальное
                //    раскрытие от центра по _CrackProgress
                // ------------------------------------------------------------
                half crackNoise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                                      input.uv * _CrackScale + noise.rg * 0.05h).b;
                // Линия там, где шум пересекает 0.5: |n-0.5| < width
                half line_ = 1.0h - smoothstep(0.0h, _CrackWidth, abs(crackNoise - 0.5h));

                // Раскрытие: круг растёт из центра UV; рваный фронт за счёт
                // подмешивания шума в радиус — скол ползёт "рывками".
                half radial = length(input.uv - 0.5h) + (crackNoise - 0.5h) * 0.15h;
                half reveal = step(radial, _CrackProgress * 0.75h);

                half pulse = 0.75h + 0.25h * sin(t * _CrackPulseHz * 6.2832h);
                half crackMask = line_ * reveal * edgeFade;
                half3 emission = _CrackColor.rgb * (crackMask * pulse * _HorrorScale);

                // ------------------------------------------------------------
                // 3. Композиция
                //    Альфа = есть ли вообще что показывать (дисторсия ИЛИ
                //    трещина). При обоих параметрах в нуле пиксель полностью
                //    прозрачен — ранний выход блендинга, overdraw бесплатен.
                // ------------------------------------------------------------
                half alpha = saturate(max(_DistortionStrength * edgeFade, crackMask) * _HorrorScale);
                return half4(sceneColor + emission, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
```

---

## 27. `PortalWindow.shader`

```hlsl
// =============================================================================
// Gate2Reality / PortalWindow
// «Окно в зазеркалье» на физической стене. Quad, два прохода:
//
//   PASS 1 (StencilMask): пишет stencil ref в КРУГЛОЙ апертуре (clip по
//     радиусу), цвет не трогает (ColorMask 0). _Aperture 0..1 анимируется
//     из C# — окно «раскрывается» из точки.
//   PASS 2 (Rim): аддитивный светящийся обод по текущему радиусу апертуры —
//     живой край раны в реальности.
//
// КОНТРАКТ ОЧЕРЕДЕЙ (вся магия порталов — в порядке):
//   Geometry+10  PortalWindow (этот шейдер): записывает stencil
//   Geometry+20  InvertedWorld (оболочка мира за стеной): Comp Equal
//   Geometry+21+ реквизит инвертированного мира (очередь материала)
//
// ОККЛЮЗИЯ: ZTest LEqual + Offset -1,-1 — quad лежит НА стене, офсет
// побеждает z-fight с environment depth стены, но РУКА игрока, попавшая
// между камерой и окном, честно перекрывает и маску, и обод: окно
// «закрывается» ладонью. Дёшево и очень физично.
//
// Бюджет: ноль текстур, чистая арифметика, всё в half.
// =============================================================================
Shader "Gate2Reality/PortalWindow"
{
    Properties
    {
        _Aperture ("Aperture (0=closed, 1=open)", Range(0, 1)) = 0
        [HDR] _RimColor ("Rim Color (HDR)", Color) = (0.7, 0.35, 2.2, 1)
        _RimWidth ("Rim Width", Range(0.005, 0.3)) = 0.07
        _RimPulseHz ("Rim Pulse, Hz", Float) = 1.3
        [IntRange] _StencilRef ("Stencil Ref", Range(1, 255)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry+10"
            "RenderPipeline" = "UniversalPipeline"
        }

        // ---------------------------------------------------------------------
        // PASS 1: стенсил-маска круглой апертуры
        // ---------------------------------------------------------------------
        Pass
        {
            Name "StencilMask"
            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            Stencil
            {
                Ref [_StencilRef]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Aperture;
                half4 _RimColor;
                half _RimWidth;
                half _RimPulseHz;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // r: 0 в центре quad'а, 1 у вписанной окружности.
                half r = length(i.uv - 0.5h) * 2.0h;
                // Вне текущей апертуры — пиксель отбрасывается, stencil
                // НЕ пишется: окно ровно того размера, что задал C#.
                clip(_Aperture - r);
                return 0;
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------------
        // PASS 2 (Rim) ПЕРЕЕХАЛ в отдельный шейдер Gate2Reality/PortalRim
        // (Queue Geometry+30). Причина, пойманная прогоном: обод в очереди
        // +10 закрашивался бы InvertedWorld (+20, ZTest Always) на внутренней
        // половине кромки. Rim вешается ВТОРЫМ материалом на тот же quad.
        // ---------------------------------------------------------------------
    }
    Fallback Off
}
```

---

## 28. `InvertedWorld.shader`

```hlsl
// =============================================================================
// Gate2Reality / InvertedWorld
// Материал всего, что живёт «за стеной»: оболочка инвертированной комнаты
// и её реквизит. Рисуется ТОЛЬКО там, где PortalWindow записал stencil.
//
// КЛЮЧЕВЫЕ РЕШЕНИЯ:
//   Stencil Comp Equal  — геометрия существует лишь в апертуре окна.
//   ZTest Always        — ОБЯЗАТЕЛЬНО: environment depth ARCore уже записал
//                         в буфер глубину ФИЗИЧЕСКОЙ стены, и честный ZTest
//                         отрезал бы весь мир за ней. Мы сознательно
//                         игнорируем глубину и сортируемся очередями:
//                           Geometry+20 — оболочка комнаты (этот материал),
//                           Geometry+21..+25 — реквизит (material.renderQueue).
//                         Осознанный компромисс: рука игрока НЕ перекрывает
//                         содержимое внутри окна (только само окно — его
//                         маска ZTest LEqual). Для окон <= 2м незаметно.
//   ZWrite Off          — глубина мира за стеной никому не нужна и не должна
//                         испортить последующие прозрачные эффекты.
//
// ВИД: холодный градиент «в глубину» (туман к чёрному по дистанции от
// камеры), фреснель-контур по краям геометрии, медленное вертикальное
// мерцание — мир дышит. Ноль текстур, всё процедурно, всё в half.
// =============================================================================
Shader "Gate2Reality/InvertedWorld"
{
    Properties
    {
        _BaseColor ("Near Color", Color) = (0.16, 0.20, 0.34, 1)
        _DeepColor ("Deep Color (даль)", Color) = (0.01, 0.01, 0.03, 1)
        [HDR] _FresnelColor ("Fresnel Edge (HDR)", Color) = (0.4, 0.7, 1.6, 1)
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3.0
        _FogDistance ("Fog Distance, m", Float) = 6.0
        _ShimmerSpeed ("Shimmer Speed", Float) = 0.6
        _ShimmerAmount ("Shimmer Amount", Range(0, 0.3)) = 0.08
        [IntRange] _StencilRef ("Stencil Ref", Range(1, 255)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry+20"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "InvertedWorld"
            ZTest Always
            ZWrite Off
            Cull Back

            Stencil
            {
                Ref [_StencilRef]
                Comp Equal
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _DeepColor;
                half4 _FresnelColor;
                half _FresnelPower;
                half _FogDistance;
                half _ShimmerSpeed;
                half _ShimmerAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = (half3)TransformObjectToWorldNormal(i.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 toCam = (half3)(_WorldSpaceCameraPos - i.positionWS);
                half dist = length(toCam);
                half3 viewDir = toCam / max(dist, 0.001h);

                // Туман в глубину зазеркалья: чем дальше — тем чернее.
                half fog = saturate(dist / _FogDistance);
                half3 color = lerp(_BaseColor.rgb, _DeepColor.rgb, fog);

                // Фреснель: холодный контур по краям форм — единственное
                // «освещение» этого мира, реального света тут нет.
                half ndv = saturate(dot(normalize(i.normalWS), viewDir));
                half fresnel = pow(1.0h - ndv, _FresnelPower);
                color += _FresnelColor.rgb * fresnel * (1.0h - fog);

                // Дыхание мира: медленная вертикальная волна яркости.
                half shimmer = sin(i.positionWS.y * 5.0h
                                   + (half)_Time.y * _ShimmerSpeed * 6.2832h);
                color *= 1.0h + shimmer * _ShimmerAmount;

                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
```

---

## 29. `PortalRim.shader`

```hlsl
// =============================================================================
// Gate2Reality / PortalRim
// Светящийся обод портального окна. Отдельный шейдер с очередью Geometry+30:
// рисуется ПОСЛЕ InvertedWorld (+20), поэтому кромка лежит поверх мира,
// а не под ним (баг, пойманный прогоном контракта очередей).
//
// Применение: ВТОРОЙ материал на том же quad'е, что и PortalWindow.
// MaterialPropertyBlock рендерера задаёт _Aperture обоим материалам разом —
// C#-код (PortalWindowEffect) об этом разделении даже не знает.
// =============================================================================
Shader "Gate2Reality/PortalRim"
{
    Properties
    {
        _Aperture ("Aperture (0=closed, 1=open)", Range(0, 1)) = 0
        [HDR] _RimColor ("Rim Color (HDR)", Color) = (0.7, 0.35, 2.2, 1)
        _RimWidth ("Rim Width", Range(0.005, 0.3)) = 0.07
        _RimPulseHz ("Rim Pulse, Hz", Float) = 1.3
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Geometry+30"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Rim"
            Blend One One        // аддитив: свечение складывается со сценой
            ColorMask RGB
            ZWrite Off
            ZTest LEqual         // рука игрока перекрывает обод вместе с окном
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Aperture;
                half4 _RimColor;
                half _RimWidth;
                half _RimPulseHz;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Закрытое окно не светится вовсе (и не жжёт филлрейт зря).
                clip(_Aperture - 0.001h);

                half r = length(i.uv - 0.5h) * 2.0h;
                // Колоколообразная полоса вокруг радиуса апертуры.
                half band = 1.0h - saturate(abs(r - _Aperture) / _RimWidth);
                clip(band - 0.001h); // вне обода — отбрасываем

                band *= band; // острее к кромке
                half pulse = 0.8h + 0.2h * sin((half)_Time.y * _RimPulseHz * 6.2832h);
                return half4(_RimColor.rgb * (band * pulse), 1.0h);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
```
