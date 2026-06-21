using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Gate2Reality.Narrative;
using static System.Reflection.BindingFlags;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// PlayMode тесты Guard-эскалации NarrativeManager:
    /// Dormant → BeaconFired → Desaturated → ParticlesFired.
    ///
    /// Update() приватный — вызываем через рефлексию; _idleTimer тоже.
    /// Минимальный граф: один пустой узел, чтобы StartScene → EnterNode(0) не падал.
    /// </summary>
    [TestFixture]
    public sealed class NarrativeManagerGuardTests
    {
        private GameObject _go;
        private NarrativeManager _mgr;
        private MethodInfo _update;
        private FieldInfo _idleTimer;
        private FieldInfo _guardStage;
        private FieldInfo _nextEscalationAt;
        private FieldInfo _idleThreshold;
        private FieldInfo _escalationInterval;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("MGR");
            _go.SetActive(false);
            _mgr = _go.AddComponent<NarrativeManager>();

            // Inject one minimal node so StartScene → EnterNode(0) doesn't OOB
            var node = new NarrativeNode { nodeName = "TestNode", dwellTimeSeconds = 999f };
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(NarrativeManager).GetField("nodes", F)?.SetValue(_mgr, new[] { node });

            // Кэшируем рефлексивные хэндлы
            _update             = typeof(NarrativeManager).GetMethod("Update",           F);
            _idleTimer          = typeof(NarrativeManager).GetField("_idleTimer",         F);
            _guardStage         = typeof(NarrativeManager).GetField("_guardStage",        F);
            _nextEscalationAt   = typeof(NarrativeManager).GetField("_nextEscalationAt",  F);
            _idleThreshold      = typeof(NarrativeManager).GetField("idleThresholdSeconds",   F);
            _escalationInterval = typeof(NarrativeManager).GetField("escalationIntervalSeconds", F);

            // Ускоряем тест: порог 1 с, интервал 1 с
            _idleThreshold.SetValue(_mgr, 1f);
            _escalationInterval.SetValue(_mgr, 1f);

            _go.SetActive(true); // Awake runs → BuildCache
            _mgr.StartScene();   // EnterNode(0), ResetIdleState → _idleTimer = 0
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.Destroy(_go);
        }

        [UnityTest]
        public IEnumerator InitialGuardStage_IsDormant()
        {
            byte stage = (byte)_guardStage.GetValue(_mgr);
            Assert.AreEqual(0, stage, "guard должен стартовать в Dormant");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AfterIdleThreshold_GuardFires_Beacon()
        {
            bool beaconFired = false;
            _mgr.OnAudioBeaconRequested += _ => beaconFired = true;

            // Перемотка таймера за порог + одна итерация Update
            _idleTimer.SetValue(_mgr, 1.1f);
            _update.Invoke(_mgr, null);
            yield return null;

            Assert.IsTrue(beaconFired, "OnAudioBeaconRequested должен сработать");
            Assert.AreEqual(1, (byte)_guardStage.GetValue(_mgr), "стадия BeaconFired");
        }

        [UnityTest]
        public IEnumerator AfterBeacon_IdleMore_Desaturates()
        {
            _mgr.OnDesaturateRequested += () => { };

            // Достигаем BeaconFired
            _idleTimer.SetValue(_mgr, 1.1f);
            _update.Invoke(_mgr, null);
            float nextAt = (float)_nextEscalationAt.GetValue(_mgr);

            bool desatFired = false;
            _mgr.OnDesaturateRequested += () => desatFired = true;

            // Перематываем за следующий порог
            _idleTimer.SetValue(_mgr, nextAt + 0.1f);
            _update.Invoke(_mgr, null);
            yield return null;

            Assert.IsTrue(desatFired, "OnDesaturateRequested должен сработать");
            Assert.AreEqual(2, (byte)_guardStage.GetValue(_mgr), "стадия Desaturated");
        }

        [UnityTest]
        public IEnumerator AfterDesaturation_GuideParticlesFire()
        {
            // Достигаем BeaconFired
            _idleTimer.SetValue(_mgr, 1.1f);
            _update.Invoke(_mgr, null);
            float next1 = (float)_nextEscalationAt.GetValue(_mgr);

            // Достигаем Desaturated
            _idleTimer.SetValue(_mgr, next1 + 0.1f);
            _update.Invoke(_mgr, null);
            float next2 = (float)_nextEscalationAt.GetValue(_mgr);

            bool particlesFired = false;
            _mgr.OnGuideParticlesRequested += _ => particlesFired = true;

            // Достигаем ParticlesFired
            _idleTimer.SetValue(_mgr, next2 + 0.1f);
            _update.Invoke(_mgr, null);
            yield return null;

            Assert.IsTrue(particlesFired, "OnGuideParticlesRequested должен сработать");
            Assert.AreEqual(3, (byte)_guardStage.GetValue(_mgr), "стадия ParticlesFired");
        }

        // ── Dwell accumulation (Gemma GPU) ──────────────────────────────────

        [UnityTest]
        public IEnumerator DwellAccumulator_AccumulatesWhileTargetSeen()
        {
            var go = new GameObject(); go.SetActive(false);
            var mgr = go.AddComponent<NarrativeManager>();
            var node = new NarrativeNode { dwellTimeSeconds = 10f };

            SetField(mgr, "nodes", new[] { node });
            SetField(mgr, "_sceneRunning", true);
            SetField(mgr, "_currentNodeIndex", 0);
            SetField(mgr, "_currentNode", node);

            go.SetActive(true);
            SetField(mgr, "_targetSeenThisFrame", true);
            _update.Invoke(mgr, null);

            yield return null;

            Assert.Greater(node.DwellAccumulator, 0f, "DwellAccumulator should increase when target is seen");
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator DwellAccumulator_DecaysWhenTargetLost()
        {
            var go = new GameObject(); go.SetActive(false);
            var mgr = go.AddComponent<NarrativeManager>();
            var node = new NarrativeNode { dwellTimeSeconds = 10f };

            SetField(mgr, "nodes", new[] { node });
            SetField(mgr, "_sceneRunning", true);
            SetField(mgr, "_currentNodeIndex", 0);
            SetField(mgr, "_currentNode", node);
            node.DwellAccumulator = 1.0f;

            go.SetActive(true);
            // _targetSeenThisFrame stays false → decay path
            _update.Invoke(mgr, null);

            yield return null;

            Assert.Less(node.DwellAccumulator, 1.0f, "DwellAccumulator should decay when target is lost");
            Object.Destroy(go);
        }

        // ────────────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator EnteringNewNode_ResetsGuardTimer()
        {
            // Доходим до BeaconFired
            _idleTimer.SetValue(_mgr, 1.1f);
            _update.Invoke(_mgr, null);
            Assert.AreEqual(1, (byte)_guardStage.GetValue(_mgr));

            // Добавляем второй узел и переходим в него
            var node2 = new NarrativeNode { nodeName = "Node2", dwellTimeSeconds = 999f };
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
            var nodesField = typeof(NarrativeManager).GetField("nodes", F);
            nodesField.SetValue(_mgr, new[] { (NarrativeNode)nodesField.GetValue(_mgr)?.GetType()?.GetMethod("Clone")?.Invoke(nodesField.GetValue(_mgr), null), node2 });

            // Используем StartSceneAt для перехода на узел 1
            _mgr.StartSceneAt(0);
            yield return null;

            float timer = (float)_idleTimer.GetValue(_mgr);
            byte stage  = (byte)_guardStage.GetValue(_mgr);
            Assert.AreEqual(0f, timer, 0.01f, "таймер должен сброситься при смене узла");
            Assert.AreEqual(0, stage, "guard должен вернуться в Dormant");
        }
    }
}
