using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gate2Reality.UI
{
    using Gate2Reality.Narrative;
    using Gate2Reality.Persistence;

    /// <summary>
    /// Экран «Continue / New Game» поверх AR-камеры. Появляется при старте
    /// приложения; исчезает (фейд 0.4 с) по выбору игрока.
    ///
    /// ЛОГИКА БЛОКИРОВКИ АВТОСТАРТА (порядок Awake):
    ///   1. Awake читает сейв напрямую из ProgressStore.
    ///   2. Если сейв есть — зовём progressTracker.DeferToMenu() + narrativeManager.SuppressAutoStart().
    ///      Если нет — только SuppressAutoStart() (кнопка START держит паузу).
    ///   3. ProgressTracker.Start() видит _menuDecisionPending = true → не стартует сам.
    ///   4. По нажатию:
    ///        Continue  → progressTracker.BeginResume()
    ///        New Game  → progressTracker.BeginFreshStart()
    ///      Сцена стартует изнутри ProgressTracker — в точности как без меню.
    ///
    /// SCENE SETUP:
    ///   Canvas (Screen Space - Overlay) →
    ///     MenuPanel (CanvasGroup, Image-background semi-transparent black)
    ///       TitleLabel        TMP_Text  «GATE 2 REALITY»
    ///       ContinueSection   GameObject (active only when save exists)
    ///         ContinueInfoLabel TMP_Text  «Chapter I · node 2\n4h ago»
    ///         ContinueButton    Button
    ///       NewGameButton       Button    «NEW GAME» / «START»
    ///         NewGameLabel      TMP_Text
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private CanvasGroup panelGroup;

        [Header("Continue (visible only when save exists)")]
        [SerializeField] private GameObject continueSection;
        [SerializeField] private TMP_Text continueInfoLabel;
        [SerializeField] private Button continueButton;

        [Header("New Game / Start")]
        [SerializeField] private Button newGameButton;
        [SerializeField] private TMP_Text newGameLabel;

        [Header("Logic — must be assigned")]
        [SerializeField] private ProgressTracker progressTracker;
        [SerializeField] private NarrativeManager narrativeManager;

        [Header("Fade")]
        [SerializeField] private float fadeOutSeconds = 0.4f;

        private bool _fadingOut;
        private float _fadeTimer;

        private void Awake()
        {
            bool hasSave = ProgressStore.TryLoad(out ProgressData save) && save != null;

            // Always suppress autostart — menu controls the exact start moment.
            narrativeManager?.SuppressAutoStart();

            if (hasSave)
            {
                progressTracker?.DeferToMenu();

                if (continueSection != null) continueSection.SetActive(true);
                if (continueInfoLabel != null)
                    continueInfoLabel.text = BuildContinueInfo(save);
                if (newGameLabel != null)
                    newGameLabel.text = "NEW GAME";

                continueButton?.onClick.AddListener(OnContinue);
            }
            else
            {
                if (continueSection != null) continueSection.SetActive(false);
                if (newGameLabel != null)
                    newGameLabel.text = "START";
            }

            newGameButton?.onClick.AddListener(hasSave ? (UnityEngine.Events.UnityAction)OnNewGame : OnStart);

            if (panelGroup != null)
            {
                panelGroup.alpha = 1f;
                panelGroup.blocksRaycasts = true;
                panelGroup.interactable = true;
            }
        }

        // ─── Button handlers ────────────────────────────────────────────────

        private void OnContinue()
        {
            progressTracker?.BeginResume();
            BeginFadeOut();
        }

        private void OnNewGame()
        {
            progressTracker?.BeginFreshStart();
            BeginFadeOut();
        }

        // No save path: ProgressTracker has nothing to resume; call BeginFreshStart
        // which calls narrativeManager.StartScene() internally.
        private void OnStart()
        {
            if (progressTracker != null)
            {
                progressTracker.BeginFreshStart();
            }
            else
            {
                narrativeManager?.StartScene();
            }
            BeginFadeOut();
        }

        // ─── Fade out ───────────────────────────────────────────────────────

        private void BeginFadeOut()
        {
            _fadingOut = true;
            _fadeTimer = 0f;
            // Stop blocking AR touches immediately
            if (panelGroup != null)
            {
                panelGroup.blocksRaycasts = false;
                panelGroup.interactable = false;
            }
        }

        private void Update()
        {
            if (!_fadingOut) return;
            _fadeTimer += Time.deltaTime;
            if (panelGroup != null)
                panelGroup.alpha = 1f - Mathf.Clamp01(_fadeTimer / Mathf.Max(fadeOutSeconds, 0.01f));
            if (_fadeTimer >= fadeOutSeconds)
                gameObject.SetActive(false);
        }

        // ─── Save info text ─────────────────────────────────────────────────

        private static string BuildContinueInfo(ProgressData save)
        {
            string chapter = save.crossedOver
                ? "Chapter II · The Other Side"
                : $"Chapter I · {ProgressLabel(save.nodeIndex)}";

            long elapsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - save.savedAtUnixSeconds;
            return $"{chapter}\n{FormatElapsed(elapsed)}";
        }

        // Returns a friendly label for the node index rather than a raw number.
        private static string ProgressLabel(int nodeIndex)
        {
            return nodeIndex switch
            {
                0 => "the chair",
                1 => "the book",
                2 => "the cup",
                3 or 4 => "the echoes",
                5 => "the portal wall",
                6 => "the crossing",
                _ => $"node {nodeIndex}"
            };
        }

        private static string FormatElapsed(long seconds)
        {
            if (seconds < 120) return "just now";
            if (seconds < 3600) return $"{seconds / 60}m ago";
            if (seconds < 86400) return $"{seconds / 3600}h ago";
            long days = seconds / 86400;
            return days == 1 ? "yesterday" : $"{days}d ago";
        }
    }
}
