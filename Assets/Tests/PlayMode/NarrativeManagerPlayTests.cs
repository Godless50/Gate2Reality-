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
            float idleThreshold = 45f, float escalation = 15f, bool resetIdleOnProgress = false)
        {
            var go = new GameObject("nm");
            go.SetActive(false);
            var nm = go.AddComponent<NarrativeManager>();
            SetPrivate(nm, "nodes", nodes);
            SetPrivate(nm, "autoStartOnStart", false);
            SetPrivate(nm, "idleThresholdSeconds", idleThreshold);
            SetPrivate(nm, "escalationIntervalSeconds", escalation);
            SetPrivate(nm, "resetIdleOnProgress", resetIdleOnProgress);
            go.SetActive(true); // теперь Awake → BuildCache по готовому массиву
            return nm;
        }

        // Прокачивает кадры, скармливая валидную детекцию каждый кадр.
        private static IEnumerator FeedDetections(NarrativeManager nm, float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                var pose = new Pose(Vector3.zero, Quaternion.identity);
                var evt = new DetectionEvent(NarrativeLabel.Chair, in pose, 0.95f, 0.5f);
                nm.ReportDetection(in evt);
                t += Time.deltaTime;
                yield return null;
            }
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

        // Семантика resetIdleOnProgress (опция, закрывшая расхождение код/коммент
        // по _idleTimer). По умолчанию OFF: guard считает «время в узле» —
        // детекции его не сбрасывают, маяк срабатывает даже при активном игроке.
        [UnityTest]
        public IEnumerator Guard_DefaultOff_FiresDespiteProgress()
        {
            NarrativeManager nm = BuildManager(new[] { SemanticNode(10f) },
                idleThreshold: 0.3f, escalation: 10f, resetIdleOnProgress: false);

            bool beacon = false;
            nm.OnAudioBeaconRequested += _ => beacon = true;
            nm.StartScene();

            yield return FeedDetections(nm, 0.6f);

            Assert.IsTrue(beacon, "при OFF guard считает время в узле — маяк срабатывает");
            Object.Destroy(nm.gameObject);
        }

        // ON: активный прогресс держит guard в покое («секунды с последнего прогресса»).
        [UnityTest]
        public IEnumerator Guard_ResetOnProgress_StaysDormantWhileActive()
        {
            NarrativeManager nm = BuildManager(new[] { SemanticNode(10f) },
                idleThreshold: 0.3f, escalation: 10f, resetIdleOnProgress: true);

            bool beacon = false;
            nm.OnAudioBeaconRequested += _ => beacon = true;
            nm.StartScene();

            yield return FeedDetections(nm, 0.6f); // > порога, но прогресс каждый кадр

            Assert.IsFalse(beacon, "при ON активность игрока держит guard в покое");
            Object.Destroy(nm.gameObject);
        }
    }
}
