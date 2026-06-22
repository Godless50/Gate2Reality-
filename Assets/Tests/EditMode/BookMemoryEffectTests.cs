using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Gate2Reality.Effects;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// Tests for BookMemoryEffect FSM phase state.
    /// Gemma-2 wrote structure; inspector fixed: (byte)GetValue() throws InvalidCastException
    /// for enum:byte — must use Convert.ToInt32() or Enum.GetUnderlyingType cast.
    /// </summary>
    [TestFixture]
    public sealed class BookMemoryEffectTests
    {
        private static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Pose ZeroPose = new Pose(Vector3.zero, Quaternion.identity);

        private static readonly FieldInfo PhaseField =
            typeof(BookMemoryEffect).GetField("_phase", F);
        private static readonly FieldInfo PhaseTimerField =
            typeof(BookMemoryEffect).GetField("_phaseTimer", F);

        private GameObject _go;
        private BookMemoryEffect _fx;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Book");
            _go.SetActive(false);
            _fx = _go.AddComponent<BookMemoryEffect>();
            _go.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
        }

        private int GetPhase() => Convert.ToInt32(PhaseField.GetValue(_fx));

        [Test]
        public void BookMemoryEffect_Trigger_StartsAtNoiseRisePhase()
        {
            _fx.Trigger(in ZeroPose);
            Assert.AreEqual(0, GetPhase(), "should start at NoiseRise(0)");
        }

        [Test]
        public void BookMemoryEffect_PhaseTimer_ResetOnTrigger()
        {
            _fx.Trigger(in ZeroPose);
            float timer = (float)PhaseTimerField.GetValue(_fx);
            Assert.AreEqual(0f, timer, 0.001f, "_phaseTimer should be 0 right after Trigger");
        }

        [Test]
        public void BookMemoryEffect_Trigger_NullRefs_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _fx.Trigger(in ZeroPose));
        }

        [Test]
        public void BookMemoryEffect_Cancel_NullRefs_DoesNotThrow()
        {
            _fx.Trigger(in ZeroPose);
            Assert.DoesNotThrow(() => _fx.Cancel());
        }
    }
}
