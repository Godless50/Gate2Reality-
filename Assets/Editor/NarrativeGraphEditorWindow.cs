using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Gate2Reality.EditorTools
{
    using Gate2Reality.Narrative;

    /// <summary>
    /// Визуальный редактор Trigger-Action графа (технический долг Stage E):
    /// узлы NarrativeGraphAsset как перетаскиваемые блоки, рёбра nextNodeIndices —
    /// безье-стрелками. Правая панель редактирует поля выбранного узла. Позиции
    /// блоков хранятся в самом ассете (EditorNodePositions), так что раскладка
    /// переживает перезапуск редактора.
    ///
    /// Открыть: Window → Gate2Reality → Narrative Graph, либо двойной клик по
    /// ассету NarrativeGraph.
    /// </summary>
    public sealed class NarrativeGraphEditorWindow : EditorWindow
    {
        private const float NodeWidth = 190f;
        private const float NodeHeight = 86f;
        private const float InspectorWidth = 280f;

        private NarrativeGraphAsset _asset;
        private int _selected = -1;
        private Vector2 _inspectorScroll;
        private Rect[] _nodeRects = System.Array.Empty<Rect>();

        [MenuItem("Window/Gate2Reality/Narrative Graph")]
        public static void Open()
        {
            var w = GetWindow<NarrativeGraphEditorWindow>("Narrative Graph");
            w.minSize = new Vector2(720, 420);
            w.Show();
        }

        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int line)
        {
#pragma warning disable CS0618
            var obj = EditorUtility.InstanceIDToObject(instanceId) as NarrativeGraphAsset;
#pragma warning restore CS0618
            if (obj == null) return false;
            var w = GetWindow<NarrativeGraphEditorWindow>("Narrative Graph");
            w._asset = obj;
            w.EnsurePositions();
            w.Show();
            return true;
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_asset == null)
            {
                EditorGUILayout.HelpBox(
                    "Назначь NarrativeGraphAsset выше или дважды кликни по нему в Project.",
                    MessageType.Info);
                return;
            }

            EnsurePositions();

            Rect canvasRect = new Rect(0, 22, position.width - InspectorWidth, position.height - 22);
            Rect inspectorRect = new Rect(position.width - InspectorWidth, 22, InspectorWidth, position.height - 22);

            DrawCanvas(canvasRect);
            DrawInspector(inspectorRect);

            if (GUI.changed) EditorUtility.SetDirty(_asset);
        }

        // =====================================================================
        // TOOLBAR
        // =====================================================================
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUI.BeginChangeCheck();
            var picked = (NarrativeGraphAsset)EditorGUILayout.ObjectField(
                _asset, typeof(NarrativeGraphAsset), false, GUILayout.Width(220));
            if (EditorGUI.EndChangeCheck())
            {
                _asset = picked;
                _selected = -1;
                EnsurePositions();
            }

            using (new EditorGUI.DisabledScope(_asset == null))
            {
                if (GUILayout.Button("Add Node", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    AddNode();
                if (GUILayout.Button("Auto Layout", EditorStyles.toolbarButton, GUILayout.Width(90)))
                    AutoLayout();
            }

            GUILayout.FlexibleSpace();

            if (_asset != null)
            {
                GUILayout.Label("Entry:", GUILayout.Width(40));
                EditorGUI.BeginChangeCheck();
                int entry = EditorGUILayout.IntField(_asset.EntryNodeIndex, GUILayout.Width(40));
                if (EditorGUI.EndChangeCheck())
                {
                    _asset.EntryNodeIndex = Mathf.Clamp(entry, 0, Mathf.Max(0, _asset.NodeCount - 1));
                    EditorUtility.SetDirty(_asset);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        // =====================================================================
        // CANVAS (узлы + рёбра)
        // =====================================================================
        private void DrawCanvas(Rect canvasRect)
        {
            GUI.Box(canvasRect, GUIContent.none);

            NarrativeNode[] nodes = _asset.Nodes;
            Vector2[] pos = _asset.EditorNodePositions;

            if (_nodeRects.Length != nodes.Length)
                _nodeRects = new Rect[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
                _nodeRects[i] = new Rect(pos[i].x, pos[i].y, NodeWidth, NodeHeight);

            // 1) Рёбра под узлами (Repaint-фаза).
            if (Event.current.type == EventType.Repaint)
            {
                for (int i = 0; i < nodes.Length; i++)
                {
                    int[] next = nodes[i].nextNodeIndices;
                    if (next == null) continue;
                    for (int e = 0; e < next.Length; e++)
                    {
                        int t = next[e];
                        if (t < 0 || t >= nodes.Length) continue;
                        DrawEdge(_nodeRects[i], _nodeRects[t]);
                    }
                }
            }

            // 2) Узлы как перетаскиваемые окна.
            BeginWindows();
            for (int i = 0; i < nodes.Length; i++)
            {
                Rect r = GUI.Window(i, _nodeRects[i], DrawNodeWindow, GUIContent.none,
                    i == _selected ? SelectedNodeStyle : NodeStyle);

                if (r.position != _nodeRects[i].position)
                {
                    pos[i] = r.position;
                    _nodeRects[i] = r;
                    EditorUtility.SetDirty(_asset);
                }
            }
            EndWindows();
        }

        private void DrawNodeWindow(int id)
        {
            NarrativeNode node = _asset.Nodes[id];

            string title = string.IsNullOrEmpty(node.nodeName) ? $"Node {id}" : node.nodeName;
            bool isEntry = id == _asset.EntryNodeIndex;

            GUILayout.Label((isEntry ? "▶ " : "") + title, EditorStyles.boldLabel);
            GUILayout.Label(node.condition != null ? node.condition.Describe() : "<no condition>",
                EditorStyles.miniLabel);
            GUILayout.Label($"dwell {node.dwellTimeSeconds:0.00}s", EditorStyles.miniLabel);

            int edges = node.nextNodeIndices != null ? node.nextNodeIndices.Length : 0;
            GUILayout.Label(edges == 0 ? "→ (terminal)" : $"→ {edges} edge(s)", EditorStyles.miniLabel);

            // Клик по телу окна — выбор узла.
            if (Event.current.type == EventType.MouseDown)
                _selected = id;

            GUI.DragWindow();
        }

        private static void DrawEdge(Rect from, Rect to)
        {
            Vector3 start = new Vector3(from.xMax, from.center.y);
            Vector3 end = new Vector3(to.xMin, to.center.y);
            Vector3 startTan = start + Vector3.right * 50f;
            Vector3 endTan = end + Vector3.left * 50f;

            Handles.DrawBezier(start, end, startTan, endTan, new Color(0.4f, 0.8f, 1f), null, 3f);

            // Стрелка на конце.
            Vector3 dir = (end - endTan).normalized;
            Vector3 normal = new Vector3(-dir.y, dir.x);
            Handles.color = new Color(0.4f, 0.8f, 1f);
            Handles.DrawAAConvexPolygon(
                end,
                end - dir * 10f + normal * 5f,
                end - dir * 10f - normal * 5f);
        }

        // =====================================================================
        // INSPECTOR (поля выбранного узла)
        // =====================================================================
        private void DrawInspector(Rect rect)
        {
            GUILayout.BeginArea(rect, EditorStyles.helpBox);
            _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);

            if (_selected < 0 || _selected >= _asset.NodeCount)
            {
                EditorGUILayout.LabelField("Выбери узел на холсте.", EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            NarrativeNode node = _asset.Nodes[_selected];
            EditorGUILayout.LabelField($"Node {_selected}", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            node.nodeName = EditorGUILayout.TextField("Name", node.nodeName);
            node.dwellTimeSeconds = EditorGUILayout.FloatField("Dwell (s)", node.dwellTimeSeconds);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Condition", EditorStyles.boldLabel);
            NarrativeCondition c = node.condition ??= new NarrativeCondition();
            c.type = (ConditionType)EditorGUILayout.EnumPopup("Type", c.type);

            switch (c.type)
            {
                case ConditionType.SemanticDetection:
                    c.requiredLabel = (NarrativeLabel)EditorGUILayout.EnumPopup("Required Label", c.requiredLabel);
                    c.minConfidence = EditorGUILayout.Slider("Min Confidence", c.minConfidence, 0f, 1f);
                    c.maxBoundsRadius = EditorGUILayout.FloatField("Max Bounds Radius", c.maxBoundsRadius);
                    break;
                case ConditionType.Proximity:
                    c.triggerRadius = EditorGUILayout.FloatField("Trigger Radius (m)", c.triggerRadius);
                    break;
                case ConditionType.Gaze:
                    c.maxGazeAngleDeg = EditorGUILayout.FloatField("Max Gaze Angle (°)", c.maxGazeAngleDeg);
                    c.maxGazeDistance = EditorGUILayout.FloatField("Max Gaze Distance (m)", c.maxGazeDistance);
                    break;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Transitions (nextNodeIndices)", EditorStyles.boldLabel);
            DrawEdgeList(node);

            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(_asset);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_asset.NodeCount <= 1))
            {
                if (GUILayout.Button("Delete This Node"))
                    DeleteNode(_selected);
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawEdgeList(NarrativeNode node)
        {
            int[] next = node.nextNodeIndices ?? System.Array.Empty<int>();
            int removeAt = -1;

            for (int i = 0; i < next.Length; i++)
            {
                EditorGUILayout.BeginHorizontal();
                next[i] = EditorGUILayout.IntField($"→ [{i}]", next[i]);
                next[i] = Mathf.Clamp(next[i], 0, Mathf.Max(0, _asset.NodeCount - 1));
                if (GUILayout.Button("✕", GUILayout.Width(24))) removeAt = i;
                EditorGUILayout.EndHorizontal();
            }

            if (removeAt >= 0)
            {
                var list = new System.Collections.Generic.List<int>(next);
                list.RemoveAt(removeAt);
                node.nextNodeIndices = list.ToArray();
            }

            if (GUILayout.Button("+ Add Transition"))
            {
                var list = new System.Collections.Generic.List<int>(node.nextNodeIndices ?? System.Array.Empty<int>())
                {
                    Mathf.Min(_selected + 1, _asset.NodeCount - 1)
                };
                node.nextNodeIndices = list.ToArray();
            }
        }

        // =====================================================================
        // МУТАЦИИ ГРАФА
        // =====================================================================
        private void AddNode()
        {
            var list = new System.Collections.Generic.List<NarrativeNode>(_asset.Nodes)
            {
                new NarrativeNode
                {
                    nodeName = $"Node {_asset.NodeCount}",
                    condition = new NarrativeCondition(),
                    dwellTimeSeconds = 0.75f,
                    nextNodeIndices = System.Array.Empty<int>(),
                    triggerableBehaviours = System.Array.Empty<MonoBehaviour>()
                }
            };
            _asset.SetNodes(list.ToArray());

            var p = new System.Collections.Generic.List<Vector2>(_asset.EditorNodePositions)
            {
                new Vector2(40 + (_asset.NodeCount % 4) * (NodeWidth + 40),
                            40 + (_asset.NodeCount / 4) * (NodeHeight + 50))
            };
            _asset.EditorNodePositions = p.ToArray();

            _selected = _asset.NodeCount - 1;
            EditorUtility.SetDirty(_asset);
        }

        private void DeleteNode(int index)
        {
            var nodes = new System.Collections.Generic.List<NarrativeNode>(_asset.Nodes);
            var pos = new System.Collections.Generic.List<Vector2>(_asset.EditorNodePositions);
            nodes.RemoveAt(index);
            if (index < pos.Count) pos.RemoveAt(index);

            // Чиним рёбра: убираем ссылки на удалённый, сдвигаем большие индексы.
            for (int i = 0; i < nodes.Count; i++)
            {
                int[] next = nodes[i].nextNodeIndices;
                if (next == null) continue;
                var fixedList = new System.Collections.Generic.List<int>(next.Length);
                for (int e = 0; e < next.Length; e++)
                {
                    int t = next[e];
                    if (t == index) continue;
                    fixedList.Add(t > index ? t - 1 : t);
                }
                nodes[i].nextNodeIndices = fixedList.ToArray();
            }

            _asset.SetNodes(nodes.ToArray());
            _asset.EditorNodePositions = pos.ToArray();
            if (_asset.EntryNodeIndex >= _asset.NodeCount)
                _asset.EntryNodeIndex = Mathf.Max(0, _asset.NodeCount - 1);

            _selected = -1;
            EditorUtility.SetDirty(_asset);
        }

        private void AutoLayout()
        {
            int n = _asset.NodeCount;
            var pos = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                pos[i] = new Vector2(
                    40 + (i % 4) * (NodeWidth + 40),
                    40 + (i / 4) * (NodeHeight + 50));
            }
            _asset.EditorNodePositions = pos;
            EditorUtility.SetDirty(_asset);
        }

        /// <summary>Синхронизирует длину массива позиций с числом узлов.</summary>
        private void EnsurePositions()
        {
            if (_asset == null) return;
            int n = _asset.NodeCount;
            Vector2[] pos = _asset.EditorNodePositions;
            if (pos != null && pos.Length == n) return;

            var resized = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                if (pos != null && i < pos.Length && pos[i] != Vector2.zero)
                    resized[i] = pos[i];
                else
                    resized[i] = new Vector2(40 + (i % 4) * (NodeWidth + 40),
                                             40 + (i / 4) * (NodeHeight + 50));
            }
            _asset.EditorNodePositions = resized;
        }

        // =====================================================================
        // СТИЛИ
        // =====================================================================
        private GUIStyle _nodeStyle;
        private GUIStyle _selectedNodeStyle;

        private GUIStyle NodeStyle => _nodeStyle ??= new GUIStyle(GUI.skin.window);
        private GUIStyle SelectedNodeStyle
        {
            get
            {
                if (_selectedNodeStyle == null)
                {
                    _selectedNodeStyle = new GUIStyle(GUI.skin.window);
                    _selectedNodeStyle.normal.textColor = Color.cyan;
                    _selectedNodeStyle.onNormal.textColor = Color.cyan;
                }
                return _selectedNodeStyle;
            }
        }
    }
}
