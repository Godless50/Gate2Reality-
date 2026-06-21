using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Gate2Reality.Effects;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// Tests for ChairAwakeningEffect: SetHintTarget data, null-safe trigger/cancel.
    /// Gemma GPU wrote structure; inspector fixed: SetActive(false) before AddComponent,
    /// Trigger() requires in Pose argument (not parameterless).
    /// </summary>
    [TestFixture]
    public sealed class ChairAwakeningEffectTests
    {
        private static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Pose ZeroPose = new Pose(Vector3.zero, Quaternion.identity);

        private GameObject _go;
        private ChairAwakeningEffect _effect;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Chair");
            _go.SetActive(false);                           // inactive before AddComponent
            _effect = _go.AddComponent<ChairAwakeningEffect>();
            _go.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void SetHintTarget_SetsHasHintTargetTrue()
        {
            var field = typeof(ChairAwakeningEffect).GetField("_hasHintTarget", F);
            _effect.SetHintTarget(Vector3.one);
            Assert.IsTrue((bool)field.GetValue(_effect));
        }

        [Test]
        public void SetHintTarget_StoresPosition()
        {
            var field = typeof(ChairAwakeningEffect).GetField("_hintTarget", F);
            _effect.SetHintTarget(Vector3.one);
            Assert.AreEqual(Vector3.one, (Vector3)field.GetValue(_effect));
        }

        [Test]
        public void Trigger_WithNullRefs_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _effect.Trigger(in ZeroPose));
        }

        [Test]
        public void Cancel_WithNullRefs_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _effect.Cancel());
        }

        [Test]
        public void SetHintTarget_BeforeTrigger_HasHintTargetIsTrue()
        {
            _effect.SetHintTarget(new Vector3(1f, 0f, 2f));
            var field = typeof(ChairAwakeningEffect).GetField("_hasHintTarget", F);
            Assert.IsTrue((bool)field.GetValue(_effect),
                "hint target should be flagged even before Trigger");
        }
    }
}
