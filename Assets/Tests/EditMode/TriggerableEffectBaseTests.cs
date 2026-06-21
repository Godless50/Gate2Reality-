using NUnit.Framework;
using UnityEngine;
using Gate2Reality.Effects;
using Gate2Reality.Narrative;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// EditMode tests for TriggerableEffectBase: one-shot protection, IsActive lifecycle.
    /// Gemma GPU wrote structure; inspector fixed Pose.identity (no such Unity property).
    /// </summary>
    [TestFixture]
    public sealed class TriggerableEffectBaseTests
    {
        private class FakeEffect : TriggerableEffectBase
        {
            public int TriggerCount;
            protected override void OnTriggered() => TriggerCount++;
        }

        private static readonly Pose ZeroPose = new Pose(Vector3.zero, Quaternion.identity);

        private GameObject _go;
        private FakeEffect _fx;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("FakeEffect");
            _go.SetActive(false);
            _fx = _go.AddComponent<FakeEffect>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void Trigger_SetsIsActiveTrue()
        {
            _fx.Trigger(in ZeroPose);
            Assert.IsTrue(_fx.IsActive);
        }

        [Test]
        public void Cancel_SetsIsActiveFalse()
        {
            _fx.Trigger(in ZeroPose);
            _fx.Cancel();
            Assert.IsFalse(_fx.IsActive);
        }

        [Test]
        public void DoubleTrigger_OnlyTriggersOnce()
        {
            _fx.Trigger(in ZeroPose);
            _fx.Trigger(in ZeroPose);
            Assert.AreEqual(1, _fx.TriggerCount);
        }

        [Test]
        public void Cancel_WithoutTrigger_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _fx.Cancel());
        }

        [Test]
        public void TriggerThenCancel_ThenTriggerAgain_IsNotOneShot()
        {
            // After Cancel(), a new Trigger() should be accepted (reusable effect)
            // TriggerableEffectBase.Trigger() only blocks if IsActive==true.
            // After Cancel() IsActive=false, so next Trigger IS accepted.
            _fx.Trigger(in ZeroPose);
            _fx.Cancel();
            _fx.Trigger(in ZeroPose);
            Assert.AreEqual(2, _fx.TriggerCount);
        }
    }
}
