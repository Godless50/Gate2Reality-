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
