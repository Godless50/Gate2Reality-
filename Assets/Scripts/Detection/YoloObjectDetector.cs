using System;
using Unity.Collections;
using Unity.InferenceEngine;
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
    ///     -> YOLOv8n int8 (Unity Inference Engine, backend GPUCompute / NNAPI)
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
