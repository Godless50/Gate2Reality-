using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Gate2Reality.Effects;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// EditMode tests for CrossingTransitionEffect state machine and OnCrossedOver event.
    /// Gemma-2 wrote structure; inspector fixed: Phase enum is private (use byte cast),
    /// event unsubscription via stored handler variable.
    /// </summary>
    [TestFixture]
    public sealed class CrossingTransitionEffectTests
    {
        private static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly FieldInfo PhaseField =
            typeof(CrossingTransitionEffect).GetField("_phase", F);
        private static readonly FieldInfo TimerField =
            typeof(CrossingTransitionEffect).GetField("_phaseTimer", F);
        private static readonly FieldInfo CrossFiredField =
            typeof(CrossingTransitionEffect).GetField("_crossFired", F);
        private static readonly MethodInfo UpdateMethod =
            typeof(TriggerableEffectBase).GetMethod("Update", F)
            ?? typeof(CrossingTransitionEffect).GetMethod("Update", F);

        private GameObject _go;
        private CrossingTransitionEffect _effect;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Crossing");
            _go.SetActive(false);
            _effect = _go.AddComponent<CrossingTransitionEffect>();
            _go.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void CrossingTransitionEffect_OnCrossedOver_FiresDuringHoldPhase()
        {
            bool eventFired = false;
            System.Action handler = () => eventFired = true;
            CrossingTransitionEffect.OnCrossedOver += handler;

            try
            {
                var pose = new Pose(Vector3.zero, Quaternion.identity);
                _effect.Trigger(in pose);

                // Phase enum: FlashIn=0, Hold=1, Reveal=2, Done=3 — private, use byte cast
                PhaseField.SetValue(_effect, (byte)1);   // Hold
                TimerField.SetValue(_effect, 0f);
                CrossFiredField.SetValue(_effect, false);

                // Trigger one Update frame via reflection
                UpdateMethod.Invoke(_effect, null);

                Assert.IsTrue(eventFired, "OnCrossedOver should fire in Hold phase");
            }
            finally
            {
                CrossingTransitionEffect.OnCrossedOver -= handler;
            }
        }

        [Test]
        public void CrossingTransitionEffect_IsActive_TrueAfterTrigger()
        {
            var pose = new Pose(Vector3.zero, Quaternion.identity);
            _effect.Trigger(in pose);
            Assert.IsTrue(_effect.IsActive);
        }

        [Test]
        public void CrossingTransitionEffect_Cancel_SetsIsActiveFalse()
        {
            var pose = new Pose(Vector3.zero, Quaternion.identity);
            _effect.Trigger(in pose);
            _effect.Cancel();
            Assert.IsFalse(_effect.IsActive);
        }
    }
}
