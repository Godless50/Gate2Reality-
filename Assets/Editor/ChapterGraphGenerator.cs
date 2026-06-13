using UnityEditor;
using UnityEngine;

namespace Gate2Reality.EditorTools
{
    using Gate2Reality.Narrative;

    /// <summary>
    /// Генераторы канонических графов как настоящих ассетов (NarrativeGraphAsset).
    /// Закрывают разрыв «система графа есть, а контента нет»: дизайнер получает
    /// готовый граф из ТЗ и правит его в визуальном редакторе, а не собирает узлы
    /// руками. Эффекты сцены к узлам привязываются на NarrativeManager
    /// (graphTriggerableBindings) — ассет сценозависимых ссылок не хранит.
    ///
    /// Tools → Gate2Reality → Generate Chapter I / II Graph.
    /// </summary>
    public static class ChapterGraphGenerator
    {
        private const float Dx = 230f, Dy = 0f, Y0 = 60f, X0 = 40f;

        [MenuItem("Tools/Gate2Reality/Generate Chapter I Graph")]
        public static void GenerateChapterOne()
        {
            // Каноничный граф Главы I + Сцены 2 (чек-лист §4 dwell + §13 узлы 3–6).
            var nodes = new[]
            {
                Semantic("0 · The Chair — Awakening", NarrativeLabel.Chair, 0.85f, 1.5f, 0.75f, 1),
                Semantic("1 · The Book — Memory",     NarrativeLabel.Book,  0.85f, 1.5f, 0.75f, 2),
                Semantic("2 · The Cup — Breach",      NarrativeLabel.Cup,   0.85f, 1.5f, 1.00f, 3),
                Proximity("3 · WallEcho",    1.2f, 0.5f, 4),
                Proximity("4 · SurfaceEcho", 1.2f, 0.5f, 5),
                Gaze("5 · PortalWall", 12f, 6f, 1.0f, 6),
                Proximity("6 · Crossing", 0.5f, 0.4f /* terminal */),
            };
            Create("Gate2Reality_ChapterI", nodes);
        }

        [MenuItem("Tools/Gate2Reality/Generate Chapter II Graph")]
        public static void GenerateChapterTwo()
        {
            // Глава II «По ту сторону»: перевёрнутая арка. Знакомые объекты
            // оживают, пока на них не смотрят (AvertedGaze); финал — уход глубже.
            var nodes = new[]
            {
                Averted("0 · The Watching Chair", 18f, 4f, 1.0f, 1),
                Averted("1 · The Unreadable Book", 18f, 4f, 1.0f, 2),
                Proximity("2 · The Whole Cup", 1.0f, 0.5f, 3),
                Proximity("3 · Deeper In", 0.5f, 0.4f /* terminal */),
            };
            Create("Gate2Reality_ChapterII", nodes);
        }

        // =====================================================================
        // ФАБРИКИ УЗЛОВ
        // =====================================================================
        private static NarrativeNode Semantic(string name, NarrativeLabel label,
            float minConf, float maxBounds, float dwell, params int[] next) =>
            new NarrativeNode
            {
                nodeName = name,
                dwellTimeSeconds = dwell,
                condition = new NarrativeCondition
                {
                    type = ConditionType.SemanticDetection,
                    requiredLabel = label,
                    minConfidence = minConf,
                    maxBoundsRadius = maxBounds
                },
                nextNodeIndices = next,
                triggerableBehaviours = System.Array.Empty<MonoBehaviour>()
            };

        private static NarrativeNode Proximity(string name, float radius, float dwell, params int[] next) =>
            new NarrativeNode
            {
                nodeName = name,
                dwellTimeSeconds = dwell,
                condition = new NarrativeCondition
                {
                    type = ConditionType.Proximity,
                    triggerRadius = radius
                },
                nextNodeIndices = next,
                triggerableBehaviours = System.Array.Empty<MonoBehaviour>()
            };

        private static NarrativeNode Gaze(string name, float angleDeg, float dist, float dwell, params int[] next) =>
            new NarrativeNode
            {
                nodeName = name,
                dwellTimeSeconds = dwell,
                condition = new NarrativeCondition
                {
                    type = ConditionType.Gaze,
                    maxGazeAngleDeg = angleDeg,
                    maxGazeDistance = dist
                },
                nextNodeIndices = next,
                triggerableBehaviours = System.Array.Empty<MonoBehaviour>()
            };

        private static NarrativeNode Averted(string name, float angleDeg, float dist, float dwell, params int[] next) =>
            new NarrativeNode
            {
                nodeName = name,
                dwellTimeSeconds = dwell,
                condition = new NarrativeCondition
                {
                    type = ConditionType.AvertedGaze,
                    maxGazeAngleDeg = angleDeg,
                    maxGazeDistance = dist
                },
                nextNodeIndices = next,
                triggerableBehaviours = System.Array.Empty<MonoBehaviour>()
            };

        // =====================================================================
        // СОЗДАНИЕ АССЕТА
        // =====================================================================
        private static void Create(string assetName, NarrativeNode[] nodes)
        {
            const string folder = "Assets/Narrative";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", "Narrative");

            var asset = ScriptableObject.CreateInstance<NarrativeGraphAsset>();
            asset.SetNodes(nodes);
            asset.EntryNodeIndex = 0;

            // Горизонтальная раскладка под визуальный редактор графа.
            var positions = new Vector2[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
                positions[i] = new Vector2(X0 + i * Dx, Y0 + i * Dy);
            asset.EditorNodePositions = positions;

            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{assetName}.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.SetDirty(asset);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            Debug.Log($"[Gate2Reality] Создан граф: {path} ({nodes.Length} узлов). " +
                      "Открой в Window → Gate2Reality → Narrative Graph.");
        }
    }
}
