using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Gate2Reality.Narrative;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// Интеграционные play-mode тесты FSM графа: dwell → активация → завершение
    /// и эскалация Guard Node по таймеру простоя. Update MonoBehaviour крутится
    /// только в play-mode, поэтому тут, а не в edit-mode. Узлы инжектятся
    /// рефлексией (приватный сериализованный массив) — без сцены и ассетов.
    /// </summary>
    public sealed class NarrativeManagerPlayTests
    {
        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo fi = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fi, $"поле {field} не найдено");
            fi.SetValue(target, value);
        }

        private static NarrativeNode SemanticNode(float dwell)
        {
            return new NarrativeNode
            {
                nodeName = "Chair",
                dwellTimeSeconds = dwell,
                condition = new NarrativeCondition
                {
                    type = ConditionType.SemanticDetection,
                    requiredLabel = NarrativeLabel.Chair,
                    minConfidence = 0.85f,
                    maxBoundsRadius = 1.5f
                },
                nextNodeIndices = System.Array.Empty<int>(),
                triggerableBehaviours = System.Array.Empty<MonoBehaviour>()
            };
        }

        // GameObject создаётся ВЫКЛЮЧЕННЫМ: иначе Awake (BuildCache по nodes)
        // выполнится до инжекта узлов и упадёт на null-массиве.
        private static NarrativeManager BuildManager(NarrativeNode[] nodes,
            float idleThreshold = 45f, float escalation = 15f)
        {
            var go = new GameObject("nm");
            go.SetActive(false);
            var nm = go.AddComponent<NarrativeManager>();
            SetPrivate(nm, "nodes", nodes);
            SetPrivate(nm, "autoStartOnStart", false);
            SetPrivate(nm, "idleThresholdSeconds", idleThreshold);
            SetPrivate(nm, "escalationIntervalSeconds", escalation);
            go.SetActive(true); // теперь Awake → BuildCache по готовому массиву
            return nm;
        }

        [UnityTest]
        public IEnumerator Dwell_ActivatesNode_ThenCompletesTerminalScene()
        {
            NarrativeManager nm = BuildManager(new[] { SemanticNode(0.05f) });

            int activated = -1;
            bool completed = false;
            nm.OnNodeActivated += (i, p) => activated = i;
            nm.OnSceneCompleted += () => completed = true;

            nm.StartScene();

            float t = 0f;
            while (!completed && t < 3f)
            {
                var pose = new Pose(Vector3.zero, Quaternion.identity);
                var evt = new DetectionEvent(NarrativeLabel.Chair, in pose, 0.95f, 0.5f);
                nm.ReportDetection(in evt);
                t += Time.deltaTime;
                yield return null;
            }

            Assert.AreEqual(0, activated, "должен активироваться узел 0");
            Assert.IsTrue(completed, "терминальный узел должен завершить сцену");
            Assert.IsFalse(nm.IsSceneRunning);

            Object.Destroy(nm.gameObject);
        }

        [UnityTest]
        public IEnumerator NoDetection_DoesNotActivate()
        {
            NarrativeManager nm = BuildManager(new[] { SemanticNode(0.05f) });

            bool activated = false;
            nm.OnNodeActivated += (i, p) => activated = true;
            nm.StartScene();

            float t = 0f;
            while (t < 0.5f) { t += Time.deltaTime; yield return null; }

            Assert.IsFalse(activated, "без детекций узел не должен срабатывать");
            Object.Destroy(nm.gameObject);
        }

        [UnityTest]
        public IEnumerator Guard_FiresBeacon_AfterIdleThreshold()
        {
            // dwell заведомо большой — узел не успеет сработать, копится простой.
            NarrativeManager nm = BuildManager(new[] { SemanticNode(10f) },
                idleThreshold: 0.15f, escalation: 10f);

            bool beacon = false;
            nm.OnAudioBeaconRequested += _ => beacon = true;
            nm.StartScene();

            float t = 0f;
            while (!beacon && t < 2f) { t += Time.deltaTime; yield return null; }

            Assert.IsTrue(beacon, "guard-маяк должен сработать после порога простоя");
            Object.Destroy(nm.gameObject);
        }

        // ПРИМЕЧАНИЕ (расхождение код/комментарий, найдено тестами):
        // поле _idleTimer прокомментировано как «секунды с последнего прогресса»,
        // но реализация сбрасывает его только при входе в узел (ResetIdleState),
        // а TickGuard инкрементит каждый кадр НЕЗАВИСИМО от детекций. Поэтому
        // guard срабатывает по «времени в узле», а не по «времени без прогресса».
        // Тест на «прогресс сбрасывает простой» сознательно НЕ добавлен — он
        // кодировал бы предполагаемое, а не фактическое поведение. Если задумка
        // была иной — это однострочный фикс в ReportDetection/Update (сброс
        // _idleTimer при _targetSeenThisFrame), но менять дизайн-поведение ядра
        // без решения автора не стал.
    }
}
