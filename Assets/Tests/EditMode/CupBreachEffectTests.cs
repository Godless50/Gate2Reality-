using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Gate2Reality.Effects;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// Tests for CupBreachEffect: initial state after trigger, null-safety.
    /// Gemma GPU wrote structure; inspector fixed: using Gate2Reality → using UnityEngine.
    /// </summary>
    [TestFixture]
    public sealed class CupBreachEffectTests
    {
        private static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Pose ZeroPose = new Pose(Vector3.zero, Quaternion.identity);

        private GameObject _go;
        private CupBreachEffect _effect;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("CupBreach");
            _go.SetActive(false);
            _effect = _go.AddComponent<CupBreachEffect>();
            _go.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private bool GetBool(string fieldName) =>
            (bool)typeof(CupBreachEffect).GetField(fieldName, F).GetValue(_effect);

        [Test]
        public void Trigger_NullRefs_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _effect.Trigger(in ZeroPose));
        }

        [Test]
        public void AfterTrigger_ShardsBurstIsFalse()
        {
            _effect.Trigger(in ZeroPose);
            Assert.IsFalse(GetBool("_shardsBurst"));
        }

        [Test]
        public void AfterTrigger_MapStartedIsFalse()
        {
            _effect.Trigger(in ZeroPose);
            Assert.IsFalse(GetBool("_mapStarted"));
        }

        [Test]
        public void Cancel_NullRefs_DoesNotThrow()
        {
            _effect.Trigger(in ZeroPose);
            Assert.DoesNotThrow(() => _effect.Cancel());
        }
    }
}
