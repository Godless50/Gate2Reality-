using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.ARFoundation;
using TMPro;

namespace Gate2Reality.EditorTools
{
    using Gate2Reality.Narrative;
    using Gate2Reality.Detection;
    using Gate2Reality.Effects;
    using Gate2Reality.Persistence;
    using Gate2Reality.UI;

    /// <summary>
    /// Автосборщик «нервной системы» Сцены 1. Создаёт все игровые менеджеры,
    /// добавляет недостающие AR-менеджеры, поднимает три эффекта (Chair/Book/Cup)
    /// с хуками регистрации якорей, минимальный субтитр-канвас, и связывает все
    /// межкомпонентные ссылки + ссылку на сгенерированный граф Главы I.
    ///
    /// НЕ настраивает вручную: ModelAsset (YOLO .onnx), AudioMixer, Volume,
    /// свет/партиклы/рендереры эффектов, главное меню — это контент-слой,
    /// он остаётся на ручную доводку (см. UNITY_SETUP_CHECKLIST.md).
    ///
    /// Связывание дефенсивное: если поле переименуют, будет Warning, а не краш.
    /// Вся операция оборачивается в Undo — откат по Ctrl+Z.
    ///
    /// Tools → Gate2Reality → Build Scene 1 (auto-wire).
    /// </summary>
    public static class SceneOneBuilder
    {
        [MenuItem("Tools/Gate2Reality/Build Scene 1 (auto-wire)")]
        public static void Build()
        {
            // --- 1. AR-каркас должен уже стоять (XR Origin + AR Session) ---
            var origin = Object.FindFirstObjectByType<XROrigin>();
            if (origin == null)
            {
                EditorUtility.DisplayDialog("Gate2Reality",
                    "В сцене нет XR Origin.\n\nСначала: GameObject → XR → AR Session, " +
                    "затем GameObject → XR → XR Origin (Mobile AR).",
                    "Понятно");
                return;
            }

            Camera arCamera = origin.Camera;
            if (arCamera == null)
            {
                EditorUtility.DisplayDialog("Gate2Reality",
                    "У XR Origin не назначена Camera. Пересоздай XR Origin (Mobile AR).",
                    "Понятно");
                return;
            }

            var arCameraManager = arCamera.GetComponent<ARCameraManager>();

            // --- 2. Доукомплектовать AR-менеджеры ---
            var raycastManager = GetOrAdd<ARRaycastManager>(origin.gameObject);
            GetOrAdd<ARPlaneManager>(origin.gameObject);
            var occlusionManager = GetOrAdd<AROcclusionManager>(arCamera.gameObject);

            // --- 3. Найти сгенерированный граф Главы I ---
            var graphAsset = FindGraphAsset();
            if (graphAsset == null)
            {
                EditorUtility.DisplayDialog("Gate2Reality",
                    "Не найден NarrativeGraphAsset.\n\nСначала: Tools → Gate2Reality → " +
                    "Generate Chapter I Graph. Потом запусти автосборку снова.",
                    "Понятно");
                return;
            }

            // --- 4. Создать корневой объект менеджеров ---
            var core = new GameObject("[Gate2Reality Core]");
            Undo.RegisterCreatedObjectUndo(core, "Build Scene 1");

            // Порядок не важен для AddComponent, но держим логику читаемой.
            var narrativeManager = core.AddComponent<NarrativeManager>();
            var poseProjector    = core.AddComponent<DepthPoseProjector>();
            var detector         = core.AddComponent<YoloObjectDetector>();
            var contextCollector = core.AddComponent<NarrativeContextCollector>();
            var generator        = core.AddComponent<OnDeviceNarrativeGenerator>();
            var governor         = core.AddComponent<HorrorSafetyGovernor>();
            var anchorRegistry   = core.AddComponent<AnchorRegistry>();
            var relocalizer      = core.AddComponent<OfflineAnchorRelocalizer>();
            var progressTracker  = core.AddComponent<ProgressTracker>();
            var tuning           = core.AddComponent<DeviceTuningProfile>();
            var director         = core.AddComponent<SceneOneDirector>();

            // --- 5. Эффекты Chair/Book/Cup (голые: визуал доливается вручную) ---
            var chairEffect = CreateEffect<ChairAwakeningEffect>(core.transform, "Effect - Chair", "chair_awakening",
                                                                 narrativeManager, anchorRegistry, 0, NarrativeLabel.Chair);
            var bookEffect = CreateEffect<BookMemoryEffect>(core.transform, "Effect - Book", "book_memory",
                                                            narrativeManager, anchorRegistry, 1, NarrativeLabel.Book);
            var cupEffect = CreateEffect<CupBreachEffect>(core.transform, "Effect - Cup", "cup_breach",
                                                          narrativeManager, anchorRegistry, 2, NarrativeLabel.Cup);
            Component[] effects = { chairEffect, bookEffect, cupEffect };

            // --- 6. Минимальный субтитр-канвас ---
            var subtitleController = CreateSubtitleCanvas();

            // --- 7. СВЯЗЫВАНИЕ ---
            // YoloObjectDetector
            Wire(detector, "cameraManager", arCameraManager);
            Wire(detector, "narrativeManager", narrativeManager);
            Wire(detector, "poseProjector", poseProjector);

            // DepthPoseProjector
            Wire(poseProjector, "arCamera", arCamera);
            Wire(poseProjector, "raycastManager", raycastManager);

            // NarrativeContextCollector
            Wire(contextCollector, "cameraManager", arCameraManager);
            Wire(contextCollector, "narrativeManager", narrativeManager);

            // HorrorSafetyGovernor
            Wire(governor, "detector", detector);

            // DeviceTuningProfile
            Wire(tuning, "occlusionManager", occlusionManager);
            Wire(tuning, "detector", detector);
            Wire(tuning, "relocalizer", relocalizer);

            // OfflineAnchorRelocalizer
            Wire(relocalizer, "anchorRegistry", anchorRegistry);
            Wire(relocalizer, "detector", detector);
            Wire(relocalizer, "arCamera", arCamera);

            // ProgressTracker
            Wire(progressTracker, "narrativeManager", narrativeManager);
            Wire(progressTracker, "anchorRegistry", anchorRegistry);
            Wire(progressTracker, "relocalizer", relocalizer);

            // SceneOneDirector
            Wire(director, "narrativeManager", narrativeManager);
            Wire(director, "detector", detector);
            Wire(director, "chairEffect", chairEffect);
            Wire(director, "narrativeGenerator", generator);
            Wire(director, "contextCollector", contextCollector);
            Wire(director, "subtitleController", subtitleController);
            Wire(director, "arCameraTransform", arCamera.transform);

            // NarrativeManager: граф + камера + привязка эффектов к узлам 0/1/2
            Wire(narrativeManager, "graphAsset", graphAsset);
            Wire(narrativeManager, "playerCamera", arCamera.transform);
            WireGraphBindings(narrativeManager, effects);

            // --- 8. Финал ---
            Selection.activeGameObject = core;
            EditorGUIUtility.PingObject(core);
            EditorSceneManager.MarkSceneDirty(core.scene);

            Debug.Log("[Gate2Reality] Сцена 1 собрана: 11 менеджеров + 3 эффекта + субтитры, " +
                      "граф подключён, ссылки связаны.\n" +
                      "ОСТАЛОСЬ ВРУЧНУЮ: YoloObjectDetector.Model Asset (.onnx), " +
                      "AudioMixer у HorrorSafetyGovernor, визуал эффектов (свет/партиклы/рендереры), " +
                      "главное меню. Сохрани сцену (Ctrl+S).");
        }

        // =====================================================================
        // ХЕЛПЕРЫ
        // =====================================================================
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static NarrativeGraphAsset FindGraphAsset()
        {
            // Предпочитаем граф Главы I, иначе любой NarrativeGraphAsset.
            string[] guids = AssetDatabase.FindAssets("t:NarrativeGraphAsset");
            NarrativeGraphAsset fallback = null;
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var asset = AssetDatabase.LoadAssetAtPath<NarrativeGraphAsset>(path);
                if (asset == null) continue;
                if (path.Contains("ChapterI") && !path.Contains("ChapterII")) return asset;
                fallback ??= asset;
            }
            return fallback;
        }

        private static T CreateEffect<T>(Transform parent, string objName, string triggerId,
            NarrativeManager nm, AnchorRegistry registry, int nodeIndex, NarrativeLabel label)
            where T : TriggerableEffectBase
        {
            var go = new GameObject(objName);
            go.transform.SetParent(parent, false);
            var effect = go.AddComponent<T>();
            Wire(effect, "triggerId", triggerId);

            // Хук авто-регистрации якоря при активации узла.
            var hook = go.AddComponent<AnchorRegistrationHook>();
            Wire(hook, "narrativeManager", nm);
            Wire(hook, "anchorRegistry", registry);
            WireInt(hook, "nodeIndex", nodeIndex);
            WireInt(hook, "label", (int)label);
            return effect;
        }

        private static WhisperSubtitleController CreateSubtitleCanvas()
        {
            var canvasGO = new GameObject("Subtitle Canvas",
                typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasGO, "Build Scene 1");
            canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var textGO = new GameObject("Whisper Text", typeof(TextMeshProUGUI), typeof(CanvasGroup));
            textGO.transform.SetParent(canvasGO.transform, false);

            var tmp = textGO.GetComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Bottom;
            tmp.fontSize = 32f;

            var rt = tmp.rectTransform;
            rt.anchorMin = new Vector2(0.1f, 0.08f);
            rt.anchorMax = new Vector2(0.9f, 0.28f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var controller = textGO.AddComponent<WhisperSubtitleController>();
            Wire(controller, "canvasGroup", textGO.GetComponent<CanvasGroup>());
            Wire(controller, "label", tmp);
            return controller;
        }

        private static void WireGraphBindings(NarrativeManager nm, Component[] effects)
        {
            var so = new SerializedObject(nm);
            var arr = so.FindProperty("graphTriggerableBindings");
            if (arr == null)
            {
                Debug.LogWarning("[Gate2Reality] Поле 'graphTriggerableBindings' не найдено — эффекты к узлам не привязаны.");
                return;
            }
            arr.arraySize = effects.Length;
            for (int i = 0; i < effects.Length; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("nodeIndex").intValue = i;
                var beh = el.FindPropertyRelative("behaviours");
                beh.arraySize = 1;
                beh.GetArrayElementAtIndex(0).objectReferenceValue = effects[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- дефенсивная установка одного поля ----
        private static void Wire(Component target, string prop, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(prop);
            if (p == null)
            {
                Debug.LogWarning($"[Gate2Reality] {target.GetType().Name}: поле '{prop}' не найдено — пропуск.");
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Wire(Component target, string prop, string value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning($"[Gate2Reality] {target.GetType().Name}: поле '{prop}' не найдено."); return; }
            p.stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireInt(Component target, string prop, int value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning($"[Gate2Reality] {target.GetType().Name}: поле '{prop}' не найдено."); return; }
            p.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
