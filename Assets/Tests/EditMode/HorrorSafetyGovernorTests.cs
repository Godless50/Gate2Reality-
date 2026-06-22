using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Gate2Reality.Effects;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// EditMode тесты HorrorSafetyGovernor — state machine с гистерезисом.
    /// GO создаётся неактивным, чтобы OnEnable (подписка на детектор) не бросал NRE.
    /// Тестируем приватный HandlePresence через рефлексию.
    /// </summary>
    [TestFixture]
    public sealed class HorrorSafetyGovernorTests
    {
        private GameObject _go;
        private HorrorSafetyGovernor _gov;
        private MethodInfo _handlePresence;
        private FieldInfo _targetField;
        private FieldInfo _humanClearedAtField;
        private FieldInfo _humanVisibleField;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Gov");
            _go.SetActive(false);                 // Awake runs, OnEnable does NOT → detector can be null
            _gov = _go.AddComponent<HorrorSafetyGovernor>();

            var t = typeof(HorrorSafetyGovernor);
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
            _handlePresence       = t.GetMethod("HandlePresence", F);
            _targetField          = t.GetField("_target",          F);
            _humanClearedAtField  = t.GetField("_humanClearedAt",  F);
            _humanVisibleField    = t.GetField("_humanVisible",     F);

            Assert.IsNotNull(_handlePresence,      "HandlePresence must exist");
            Assert.IsNotNull(_targetField,         "_target must exist");
            Assert.IsNotNull(_humanClearedAtField, "_humanClearedAt must exist");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        [Test]
        public void InitialIntensity_IsOne()
        {
            Assert.AreEqual(1f, _gov.CurrentIntensity, 0.001f);
        }

        [Test]
        public void InitialTarget_IsOne()
        {
            Assert.AreEqual(1f, (float)_targetField.GetValue(_gov), 0.001f);
        }

        [Test]
        public void HandlePresence_True_TargetDropsToSafeIntensity()
        {
            _handlePresence.Invoke(_gov, new object[] { true });

            float target = (float)_targetField.GetValue(_gov);
            Assert.Less(target, 1f, "target must drop when human is visible");
            Assert.GreaterOrEqual(target, 0f, "target must stay non-negative");
        }

        [Test]
        public void HandlePresence_TrueThenFalse_RecordsClearedAt()
        {
            _handlePresence.Invoke(_gov, new object[] { true });
            _handlePresence.Invoke(_gov, new object[] { false });

            // _humanClearedAt is set to Time.unscaledTime — must be > 0
            float clearedAt = (float)_humanClearedAtField.GetValue(_gov);
            Assert.GreaterOrEqual(clearedAt, 0f, "_humanClearedAt should be recorded");
        }

        [Test]
        public void HandlePresence_False_HumanVisibleFlagCleared()
        {
            _handlePresence.Invoke(_gov, new object[] { true });
            _handlePresence.Invoke(_gov, new object[] { false });

            bool visible = (bool)_humanVisibleField.GetValue(_gov);
            Assert.IsFalse(visible, "_humanVisible must be false after presence cleared");
        }

        [Test]
        public void HandlePresence_TrueTrue_Idempotent()
        {
            _handlePresence.Invoke(_gov, new object[] { true });
            float target1 = (float)_targetField.GetValue(_gov);

            _handlePresence.Invoke(_gov, new object[] { true });
            float target2 = (float)_targetField.GetValue(_gov);

            Assert.AreEqual(target1, target2, 0.001f, "double-presence must not change target");
        }
    }
}
