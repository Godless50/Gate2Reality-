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
#if UNITY_ANDROID && !UNITY_EDITOR
        [SerializeField] private YoloObjectDetector detector;
#endif
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

#if UNITY_ANDROID && !UNITY_EDITOR
        private void OnEnable() => detector.OnHumanPresenceChanged += HandlePresence;
        private void OnDisable() => detector.OnHumanPresenceChanged -= HandlePresence;
#endif

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
