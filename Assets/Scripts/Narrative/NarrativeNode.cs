using UnityEngine;

namespace Gate2Reality.Narrative
{
    /// <summary>
    /// Узел Trigger-Action графа. Сериализуется прямо в инспекторе
    /// NarrativeManager — для Сцены 1 этого достаточно; позже можно
    /// вынести в ScriptableObject-граф без изменения рантайм-логики.
    ///
    /// Граф, а не линейный массив: nextNodeIndices позволяет ветвление
    /// (нелинейный нарратив из ТЗ). Для Сцены 1 это просто цепочка
    /// Chair(0) -> Book(1) -> Cup(2).
    /// </summary>
    [System.Serializable]
    public sealed class NarrativeNode
    {
        [Tooltip("Имя для отладки, напр. 'The Chair — Awakening'")]
        public string nodeName;

        [Tooltip("Условие срабатывания: семантика (Сцена 1) или пространство (Сцена 2)")]
        public NarrativeCondition condition = new NarrativeCondition();

        [Tooltip("Сколько секунд подряд объект должен оставаться в детекции, " +
                 "чтобы узел сработал. Защита от одиночных ложных срабатываний YOLO.")]
        public float dwellTimeSeconds = 0.75f;

        [Tooltip("Компоненты, реализующие ITriggerable (свет, шейдер, партиклы...). " +
                 "MonoBehaviour-ссылки, т.к. Unity не сериализует интерфейсы.")]
        public MonoBehaviour[] triggerableBehaviours;

        [Tooltip("Индексы следующих узлов графа. Пусто = конец сцены.")]
        public int[] nextNodeIndices;

        // ---- Рантайм-кэш (заполняется один раз в Awake — ноль GC дальше) ----
        [System.NonSerialized] public ITriggerable[] CachedTriggerables;
        [System.NonSerialized] public float DwellAccumulator; // сброс при потере объекта
        [System.NonSerialized] public Pose LastSeenPose;      // последняя валидная поза для guard-подсказок

        /// <summary>Валидация и кэширование интерфейсов. Вызывать из Awake.</summary>
        public void BuildCache()
        {
            int count = triggerableBehaviours != null ? triggerableBehaviours.Length : 0;
            CachedTriggerables = new ITriggerable[count]; // единственная аллокация — на старте

            for (int i = 0; i < count; i++)
            {
                CachedTriggerables[i] = triggerableBehaviours[i] as ITriggerable;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (CachedTriggerables[i] == null)
                {
                    Debug.LogError($"[Gate2Reality] Узел '{nodeName}': элемент {i} не реализует ITriggerable!");
                }
#endif
            }
        }
    }
}
