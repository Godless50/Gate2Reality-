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
