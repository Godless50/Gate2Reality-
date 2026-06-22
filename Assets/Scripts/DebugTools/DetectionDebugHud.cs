using UnityEngine;

namespace Gate2Reality.DebugTools
{
    using Gate2Reality.Narrative;
    using Gate2Reality.Detection;
    using Gate2Reality.Effects;
    using Gate2Reality.Persistence;

    /// <summary>
    /// Экранный отладочный HUD для QA и полевых прогонов (Stage A, Pixel 9 /
    /// Honor 90): живые YOLO-детекции, состояние узла графа, таймер/ступень
    /// Guard Node, флаг присутствия человека и текущая интенсивность хоррора.
    ///
    /// Весь рабочий код — под #if UNITY_EDITOR || DEVELOPMENT_BUILD: в релизной
    /// сборке компонент пуст, ни байта в горячий путь не попадает. IMGUI
    /// (OnGUI) выбран намеренно — нулевая зависимость от Canvas/префабов, можно
    /// бросить на любой объект сцены.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DetectionDebugHud : MonoBehaviour
    {
        [Header("Связи")]
#if UNITY_ANDROID && !UNITY_EDITOR
        [SerializeField] private YoloObjectDetector detector;
#endif
        [SerializeField] private NarrativeManager narrativeManager;
        [SerializeField] private HorrorSafetyGovernor safetyGovernor;

        [Header("Отображение")]
        [Tooltip("Показывать HUD. Можно дёргать из меню разработчика.")]
        [SerializeField] private bool visible = true;
        [Tooltip("Сколько последних детекций держать в ленте")]
        [SerializeField] private int recentDetectionCapacity = 8;

        public void ToggleVisible() => visible = !visible;
        public void SetVisible(bool v) => visible = v;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private struct DetRow
        {
            public NarrativeLabel Label;
            public float Confidence;
            public float Radius;
            public float Time;
        }

        private DetRow[] _recent;
        private int _recentHead;
        private int _recentCount;

        private bool _humanPresent;
        private float _intensity = 1f;

        // Релокализация якорей (Stage A): уровень L1/L2/L3 + число восстановленных.
        private int _relocLevel;        // 0 = ещё не было resume
        private int _relocAnchorCount;
        private float _relocAtTime;

        // Сглаженный FPS
        private float _smoothedDt = 0.016f;

        // Кэш стилей/буфера (OnGUI зовётся часто — не аллоцируем каждый кадр)
        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder(256);

        private static readonly string[] GuardStageNames =
            { "Dormant", "BeaconFired", "Desaturated", "ParticlesFired" };

        private void Awake()
        {
            _recent = new DetRow[Mathf.Max(1, recentDetectionCapacity)];
        }

        private void OnEnable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (detector != null)
            {
                detector.OnRawDetection += HandleRawDetection;
                detector.OnHumanPresenceChanged += HandleHumanPresence;
            }
#endif
            if (safetyGovernor != null)
            {
                safetyGovernor.OnIntensityChanged += HandleIntensity;
                _intensity = safetyGovernor.CurrentIntensity;
            }
            OfflineAnchorRelocalizer.OnRelocalizationReported += HandleRelocalization;
        }

        private void OnDisable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (detector != null)
            {
                detector.OnRawDetection -= HandleRawDetection;
                detector.OnHumanPresenceChanged -= HandleHumanPresence;
            }
#endif
            if (safetyGovernor != null)
            {
                safetyGovernor.OnIntensityChanged -= HandleIntensity;
            }
            OfflineAnchorRelocalizer.OnRelocalizationReported -= HandleRelocalization;
        }

        private void Update()
        {
            // Экспоненциальное сглаживание dt для стабильного FPS-числа.
            _smoothedDt = Mathf.Lerp(_smoothedDt, Time.unscaledDeltaTime, 0.1f);
        }

        private void HandleRawDetection(DetectionEvent evt)
        {
            _recent[_recentHead] = new DetRow
            {
                Label = evt.Label,
                Confidence = evt.Confidence,
                Radius = evt.BoundsRadius,
                Time = Time.unscaledTime
            };
            _recentHead = (_recentHead + 1) % _recent.Length;
            if (_recentCount < _recent.Length) _recentCount++;
        }

        private void HandleHumanPresence(bool present) => _humanPresent = present;
        private void HandleIntensity(float v) => _intensity = v;

        private void HandleRelocalization(int level, int count)
        {
            _relocLevel = level;
            _relocAnchorCount = count;
            _relocAtTime = Time.unscaledTime;
        }

        private void OnGUI()
        {
            if (!visible) return;

            EnsureStyles();

            const float w = 320f;
            float h = 168f + _recentCount * 18f + (_relocLevel > 0 ? 18f : 0f);
            GUILayout.BeginArea(new Rect(10, 10, w, h), _boxStyle);

            // --- Производительность ---
            float fps = _smoothedDt > 0f ? 1f / _smoothedDt : 0f;
            Color fpsColor = fps >= 28f ? Color.green : (fps >= 24f ? Color.yellow : Color.red);
            Line($"FPS: {fps:F0}  ({_smoothedDt * 1000f:F1} ms)", fpsColor);

            // --- Состояние графа ---
            NarrativeManager.DebugSnapshot s = narrativeManager != null
                ? narrativeManager.GetDebugSnapshot()
                : default;

            if (narrativeManager != null && s.SceneRunning)
            {
                Line($"Node [{s.NodeIndex}] {s.NodeName}", Color.white);

                float dwellFrac = s.DwellTarget > 0f ? Mathf.Clamp01(s.Dwell / s.DwellTarget) : 0f;
                Line($"  dwell {s.Dwell:F2}/{s.DwellTarget:F2}s  {Bar(dwellFrac, 10)}",
                     dwellFrac >= 1f ? Color.green : Color.cyan);

                string stage = (s.GuardStage >= 0 && s.GuardStage < GuardStageNames.Length)
                    ? GuardStageNames[s.GuardStage] : "?";
                float idleFrac = s.IdleThreshold > 0f ? Mathf.Clamp01(s.IdleTimer / s.IdleThreshold) : 0f;
                Color guardColor = s.GuardStage == 0 ? Color.gray : new Color(1f, 0.6f, 0.2f);
                Line($"  guard {stage}  idle {s.IdleTimer:F0}/{s.IdleThreshold:F0}s {Bar(idleFrac, 10)}",
                     guardColor);
            }
            else
            {
                Line("Scene: not running", Color.gray);
            }

            // --- Privacy / Safety ---
            Line($"Human in frame: {(_humanPresent ? "YES" : "no")}",
                 _humanPresent ? Color.red : Color.gray);
            Line($"Horror intensity: {_intensity:F2} {Bar(_intensity, 10)}",
                 _intensity >= 0.95f ? Color.green : new Color(1f, 0.5f, 0.5f));

            // --- Релокализация якорей (Stage A) ---
            if (_relocLevel > 0)
            {
                // L1 тёплый (зелёный), L2 точный re-detect (циан), L3 фолбэк (жёлтый).
                Color rc = _relocLevel == 1 ? Color.green
                         : _relocLevel == 2 ? Color.cyan
                         : Color.yellow;
                float relocAge = Time.unscaledTime - _relocAtTime;
                Line($"Reloc: L{_relocLevel}  {_relocAnchorCount} anchor(s)  {relocAge:F0}s ago", rc);
            }

            // --- Лента детекций ---
            Line("Recent detections:", Color.white);
            for (int i = 0; i < _recentCount; i++)
            {
                // Идём от свежих к старым.
                int idx = (_recentHead - 1 - i + _recent.Length * 2) % _recent.Length;
                DetRow r = _recent[idx];
                float age = Time.unscaledTime - r.Time;
                Color c = Color.Lerp(Color.green, Color.gray, Mathf.Clamp01(age / 3f));
                Line($"  {r.Label,-6} conf {r.Confidence:F2}  r {r.Radius:F2}m  {age:F1}s ago", c);
            }

            GUILayout.EndArea();
        }

        private void Line(string text, Color color)
        {
            _labelStyle.normal.textColor = color;
            GUILayout.Label(text, _labelStyle);
        }

        private string Bar(float frac, int width)
        {
            _sb.Clear();
            int filled = Mathf.RoundToInt(Mathf.Clamp01(frac) * width);
            _sb.Append('[');
            for (int i = 0; i < width; i++) _sb.Append(i < filled ? '|' : '.');
            _sb.Append(']');
            return _sb.ToString();
        }

        private void EnsureStyles()
        {
            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    padding = new RectOffset(8, 8, 6, 6)
                };
            }
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    richText = false,
                    wordWrap = false
                };
            }
        }
#endif
    }
}
