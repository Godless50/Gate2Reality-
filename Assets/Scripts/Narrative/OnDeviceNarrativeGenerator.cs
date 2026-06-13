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
