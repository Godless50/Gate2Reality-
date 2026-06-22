using UnityEngine;
using Gate2Reality.Narrative;

namespace Gate2Reality.Persistence
{
    /// <summary>
    /// Лёгкий хук: регистрирует Transform этого GameObject в AnchorRegistry
    /// при активации соответствующего узла графа. Добавьте на GameObject эффекта
    /// (ChairAwakeningEffect, BookMemoryEffect, CupBreachEffect) и заполните:
    ///   nodeIndex   — индекс узла (0=Стул, 1=Книга, 2=Чашка)
    ///   label       — семантическая метка для L2-детекции и отображения
    ///
    /// ПОРЯДОК СОБЫТИЙ (NarrativeManager): Trigger() → OnNodeActivated.
    /// К моменту вызова HandleActivated эффект уже snapToAnchor-ован —
    /// transform.position корректен.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnchorRegistrationHook : MonoBehaviour
    {
        [Header("Связи")]
        [SerializeField] private NarrativeManager narrativeManager;
        [SerializeField] private AnchorRegistry anchorRegistry;

        [Header("Конфигурация")]
        [Tooltip("Индекс узла графа, при активации которого регистрируемся")]
        [SerializeField] private int nodeIndex;
        [Tooltip("Семантическая метка объекта (Chair/Book/Cup)")]
        [SerializeField] private NarrativeLabel label;

        private void OnEnable()
        {
            if (narrativeManager != null)
                narrativeManager.OnNodeActivated += HandleActivated;
        }

        private void OnDisable()
        {
            if (narrativeManager != null)
                narrativeManager.OnNodeActivated -= HandleActivated;
        }

        private void HandleActivated(int activatedIndex, Pose pose)
        {
            if (activatedIndex != nodeIndex) return;
            anchorRegistry?.Register(nodeIndex, label, transform);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Test-only: force (re-)subscription to NarrativeManager.OnNodeActivated.
        /// Needed in EditMode tests where Unity may not call OnEnable synchronously
        /// for components added to initially-inactive GameObjects.
        /// Unsubscribes first to prevent duplicate subscriptions.
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public void SubscribeForTest()
        {
            if (narrativeManager == null) return;
            narrativeManager.OnNodeActivated -= HandleActivated; // idempotent unsub first
            narrativeManager.OnNodeActivated += HandleActivated;
        }

        /// <summary>Test-only: force unsubscription.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public void UnsubscribeForTest()
        {
            if (narrativeManager == null) return;
            narrativeManager.OnNodeActivated -= HandleActivated;
        }
#endif
    }
}
