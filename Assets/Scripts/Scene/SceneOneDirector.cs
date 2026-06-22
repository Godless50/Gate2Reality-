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
#if UNITY_ANDROID && !UNITY_EDITOR
        [SerializeField] private YoloObjectDetector detector;
#endif

        [Header("Эффекты сцены")]
        [SerializeField] private ChairAwakeningEffect chairEffect;

        [Header("Guard: аудио-маяк")]
        [SerializeField] private AudioSource beaconSource; // 3D, spatialBlend = 1

        [Header("Guard: десатурация (URP Volume c Color Adjustments)")]
#if !UNITY_EDITOR
        [SerializeField] private Volume desaturationVolume; // weight 0 -> 1
#endif
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
#if UNITY_ANDROID && !UNITY_EDITOR
            detector.OnRawDetection += HandleRawDetection;
#endif
        }

        private void OnDisable()
        {
            narrativeManager.OnAudioBeaconRequested -= HandleBeacon;
            narrativeManager.OnDesaturateRequested -= HandleDesaturate;
            narrativeManager.OnSaturationRestoreRequested -= HandleRestoreSaturation;
            narrativeManager.OnGuideParticlesRequested -= HandleGuideParticles;
            narrativeManager.OnSceneCompleted -= HandleSceneCompleted;
            narrativeManager.OnNodeActivated -= HandleNodeActivated;
#if UNITY_ANDROID && !UNITY_EDITOR
            detector.OnRawDetection -= HandleRawDetection;
#endif
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
#if UNITY_ANDROID && !UNITY_EDITOR
                if (detector != null) detector.SetPersonOnlyMode(true);
#endif
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
#if UNITY_ANDROID && !UNITY_EDITOR
            if (detector != null) detector.enabled = false;
#endif
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
#if !UNITY_EDITOR
            if (_desatDirection == 0f || desaturationVolume == null) return;

            float w = desaturationVolume.weight +
                      _desatDirection * (Time.deltaTime / desaturateLerpSeconds);
            desaturationVolume.weight = Mathf.Clamp01(w);

            if (desaturationVolume.weight <= 0f || desaturationVolume.weight >= 1f)
                _desatDirection = 0f; // доехали — больше не трогаем Update-бюджет
#endif
        }
    }
}
