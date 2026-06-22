using UnityEngine;

namespace Gate2Reality.Narrative
{
    /// <summary>
    /// ScriptableObject-обёртка над Trigger-Action графом. Снимает технический
    /// долг «граф зашит инлайном в NarrativeManager»: структура (узлы, условия,
    /// dwell, рёбра) живёт в ассете и редактируется визуальным редактором
    /// (Window → Gate2Reality → Narrative Graph), а сценозависимые ссылки на
    /// ITriggerable-эффекты остаются в сцене — ассет не может держать ссылки на
    /// объекты сцены.
    ///
    /// РАНТАЙМ: NarrativeManager в Awake клонирует узлы ассета (CreateRuntimeNodes),
    /// чтобы покадровые мутации (DwellAccumulator, LastSeenPose) НИКОГДА не пачкали
    /// сам ассет на диске, и доливает в них эффекты по индексу из биндингов сцены.
    /// Если ассет не назначен — менеджер работает на инлайн-массиве как раньше
    /// (полная обратная совместимость).
    /// </summary>
    [CreateAssetMenu(fileName = "NarrativeGraph", menuName = "Gate2Reality/Narrative Graph", order = 0)]
    public sealed class NarrativeGraphAsset : ScriptableObject
    {
        [SerializeField] private NarrativeNode[] nodes = System.Array.Empty<NarrativeNode>();
        [SerializeField] private int entryNodeIndex = 0;

        [Tooltip("Позиции узлов в окне редактора графа. Только для редактора, рантайм не читает.")]
        [SerializeField] private Vector2[] editorNodePositions = System.Array.Empty<Vector2>();

        public NarrativeNode[] Nodes => nodes;
        public int EntryNodeIndex { get => entryNodeIndex; set => entryNodeIndex = value; }
        public int NodeCount => nodes != null ? nodes.Length : 0;

        public Vector2[] EditorNodePositions
        {
            get => editorNodePositions;
            set => editorNodePositions = value;
        }

        /// <summary>Заменить массив узлов (используется редактором).</summary>
        public void SetNodes(NarrativeNode[] newNodes) => nodes = newNodes ?? System.Array.Empty<NarrativeNode>();

        /// <summary>
        /// Глубокая копия узлов под рантайм. Клонируется структура; ссылки на
        /// эффекты остаются пустыми (их доливает NarrativeManager из биндингов
        /// сцены по индексу узла). Рантайм-поля ([NonSerialized]) рождаются
        /// чистыми у новых экземпляров.
        /// </summary>
        public NarrativeNode[] CreateRuntimeNodes()
        {
            int n = nodes != null ? nodes.Length : 0;
            var copy = new NarrativeNode[n];
            for (int i = 0; i < n; i++)
            {
                NarrativeNode s = nodes[i];
                copy[i] = new NarrativeNode
                {
                    nodeName = s.nodeName,
                    dwellTimeSeconds = s.dwellTimeSeconds,
                    nextNodeIndices = s.nextNodeIndices != null
                        ? (int[])s.nextNodeIndices.Clone()
                        : System.Array.Empty<int>(),
                    triggerableBehaviours = System.Array.Empty<MonoBehaviour>(),
                    condition = CloneCondition(s.condition)
                };
            }
            return copy;
        }

        private static NarrativeCondition CloneCondition(NarrativeCondition c)
        {
            if (c == null) return new NarrativeCondition();
            return new NarrativeCondition
            {
                type = c.type,
                requiredLabel = c.requiredLabel,
                minConfidence = c.minConfidence,
                maxBoundsRadius = c.maxBoundsRadius,
                // runtimeTarget намеренно не копируем — это рантайм-якорь сцены.
                triggerRadius = c.triggerRadius,
                maxGazeAngleDeg = c.maxGazeAngleDeg,
                maxGazeDistance = c.maxGazeDistance
            };
        }
    }
}
