using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Gate2Reality.Narrative;
using Gate2Reality.Persistence;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// EditMode тесты AnchorRegistrationHook: регистрация якоря только при
    /// совпадении nodeIndex; игнорирование чужих активаций.
    ///
    /// Архитектурное ограничение: event Action нельзя вызвать снаружи объявляющего
    /// класса. Используем рефлексию для получения backing field события и прямой инвок.
    /// </summary>
    [TestFixture]
    public sealed class AnchorRegistrationHookTests
    {
        private GameObject _managerGo;
        private GameObject _hookGo;
        private NarrativeManager _manager;
        private AnchorRegistry _registry;
        private AnchorRegistrationHook _hook;

        [SetUp]
        public void SetUp()
        {
            // NarrativeManager — активный GO, будет источником события
            _managerGo = new GameObject("Manager");
            _manager   = _managerGo.AddComponent<NarrativeManager>();

            // AnchorRegistry на том же или отдельном GO
            _registry  = _managerGo.AddComponent<AnchorRegistry>();

            // Hook на отдельном GO с чётко заданным nodeIndex
            _hookGo = new GameObject("HookGO");
            _hookGo.SetActive(false);
            _hook = _hookGo.AddComponent<AnchorRegistrationHook>();

            // Инжект зависимостей через рефлексию (поля [SerializeField])
            SetPrivate(_hook, "narrativeManager", _manager);
            SetPrivate(_hook, "anchorRegistry",   _registry);
            SetPrivate(_hook, "nodeIndex",         0);
            SetPrivate(_hook, "label",             NarrativeLabel.Chair);

            _hookGo.SetActive(true); // Awake / OnEnable: подписка на OnNodeActivated

            // Ensure subscription is active regardless of Unity EditMode lifecycle timing.
            _hook.SubscribeForTest();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hookGo != null)   UnityEngine.Object.DestroyImmediate(_hookGo);
            if (_managerGo != null) UnityEngine.Object.DestroyImmediate(_managerGo);
        }

        [Test]
        public void Register_WhenActivatedIndexMatches_AddsToRegistry()
        {
            FireNodeActivated(0, new Pose(_hookGo.transform.position, Quaternion.identity));

            Assert.AreEqual(1, _registry.All.Count, "должна быть одна запись после совпадения");
            Assert.AreEqual(0, _registry.All[0].nodeIndex);
            Assert.AreEqual(NarrativeLabel.Chair, _registry.All[0].label);
        }

        [Test]
        public void Register_WhenActivatedIndexMismatches_DoesNothing()
        {
            FireNodeActivated(5, new Pose(Vector3.zero, Quaternion.identity));

            Assert.AreEqual(0, _registry.All.Count, "другой индекс не должен регистрировать");
        }

        [Test]
        public void Register_CalledTwice_SameIndex_UpdatesNotDuplicates()
        {
            FireNodeActivated(0, new Pose(Vector3.zero, Quaternion.identity));
            FireNodeActivated(0, new Pose(Vector3.one,  Quaternion.identity));

            Assert.AreEqual(1, _registry.All.Count, "дублирование не создаёт лишних записей");
        }

        [Test]
        public void AfterDisable_EventNoLongerRegisters()
        {
            _hookGo.SetActive(false); // OnDisable: отписка
            _hook.UnsubscribeForTest(); // Ensure unsubscription regardless of lifecycle timing
            FireNodeActivated(0, new Pose(Vector3.zero, Quaternion.identity));

            Assert.AreEqual(0, _registry.All.Count, "отписанный hook не должен реагировать");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private void FireNodeActivated(int index, Pose pose)
        {
            // Use the UNITY_EDITOR test helper to raise the event on the manager.
            _manager.RaiseOnNodeActivatedForTest(index, pose);
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            var fi = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(fi, $"Field '{fieldName}' not found");
            fi.SetValue(target, value);
        }
    }
}
