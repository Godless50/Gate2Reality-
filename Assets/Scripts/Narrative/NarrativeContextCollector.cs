using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.XR.ARFoundation;
#endif

namespace Gate2Reality.Narrative
{
    /// <summary>
    /// Сборщик контекста для MLLM: подписывается на ARCore Light Estimation
    /// и сырой поток YOLO-детекций, копит состояние и по запросу пакует его
    /// в NarrativeContext (struct, на стеке, без аллокаций).
    ///
    /// Сырые детекции берём через NarrativeManager.OnDetectionRelayed, а не
    /// напрямую из YoloObjectDetector — иначе Narrative ссылалась бы на сборку
    /// Detection, которая уже ссылается на Narrative (циклическая зависимость).
    ///
    /// ТРЕБОВАНИЕ: на ARCameraManager включить Light Estimation =
    /// Ambient Intensity + Ambient Color (войдёт в чек-лист Step 5).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NarrativeContextCollector : MonoBehaviour
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        [SerializeField] private ARCameraManager cameraManager;
#endif
        [SerializeField] private NarrativeManager narrativeManager;

        private float _brightness = 0.5f;       // экспоненциальное сглаживание
        private float _kelvin;                  // 0 = неизвестно
        private int _seenMask;
        private readonly NarrativeLabel[] _chain = new NarrativeLabel[3];
        private int _chainCount;

        private void OnEnable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            cameraManager.frameReceived += OnFrame;
#endif
            narrativeManager.OnDetectionRelayed += OnDetection;
        }

        private void OnDisable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            cameraManager.frameReceived -= OnFrame;
#endif
            narrativeManager.OnDetectionRelayed -= OnDetection;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
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
#endif

        private void OnDetection(DetectionEvent evt)
        {
            _seenMask |= 1 << (int)evt.Label;
            _chain[_chainCount % 3] = evt.Label;
            _chainCount++;
        }

        /// <summary>Снимок контекста под текущий фокус-объект.</summary>
        public NarrativeContext Capture(NarrativeLabel focus)
        {
            return new NarrativeContext(focus, _seenMask, _brightness, _kelvin, InferRoom());
        }

        public string BuildChainString()
        {
            if (_chainCount == 0) return string.Empty;
            int count = System.Math.Min(_chainCount, 3);
            var sb = new System.Text.StringBuilder(64);
            int start = _chainCount > 3 ? _chainCount % 3 : 0;
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(LabelToRussian(_chain[(start + i) % 3]));
            }
            return sb.ToString();
        }

        private static string LabelToRussian(NarrativeLabel l) => l switch
        {
            NarrativeLabel.Chair     => "стул",
            NarrativeLabel.Book      => "книга",
            NarrativeLabel.Cup       => "чашка",
            NarrativeLabel.Table     => "стол",
            NarrativeLabel.Tv        => "телевизор",
            NarrativeLabel.Laptop    => "ноутбук",
            NarrativeLabel.Phone     => "телефон",
            NarrativeLabel.Bottle    => "бутылка",
            NarrativeLabel.Bowl      => "миска",
            NarrativeLabel.Fork      => "вилка",
            NarrativeLabel.Knife     => "нож",
            NarrativeLabel.Scissors  => "ножницы",
            NarrativeLabel.TeddyBear => "мишка",
            NarrativeLabel.Backpack  => "рюкзак",
            NarrativeLabel.Couch     => "диван",
            NarrativeLabel.Bed       => "кровать",
            NarrativeLabel.Bicycle   => "велосипед",
            _                        => "предмет"
        };

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