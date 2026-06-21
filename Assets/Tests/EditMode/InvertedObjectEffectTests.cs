using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Gate2Reality.ChapterTwo;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// Tests for InvertedObjectEffect: null-safe behavior, default field values.
    /// Gemma GPU wrote structure; inspector fixed: Pose.identity → ZeroPose,
    /// Trigger() → Trigger(in ZeroPose) — Trigger requires in Pose parameter.
    /// </summary>
    [TestFixture]
    public sealed class InvertedObjectEffectTests
    {
        private static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Pose ZeroPose = new Pose(Vector3.zero, Quaternion.identity);

        private GameObject _go;
        private InvertedObjectEffect _effect;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("InvObj");
            _go.SetActive(false);
            _effect = _go.AddComponent<InvertedObjectEffect>();
            _go.SetActive(true);
        }

        [TearDown]
        public void TearDown() { if (_go != null) Object.DestroyImmediate(_go); }

        [Test]
        public void Trigger_NullCamera_DoesNotThrow()
        {
            typeof(InvertedObjectEffect).GetField("arCameraTransform", F)?.SetValue(_effect, null);
            Assert.DoesNotThrow(() => _effect.Trigger(in ZeroPose));
        }

        [Test]
        public void Cancel_NullCamera_DoesNotThrow()
        {
            typeof(InvertedObjectEffect).GetField("arCameraTransform", F)?.SetValue(_effect, null);
            _effect.Trigger(in ZeroPose);
            Assert.DoesNotThrow(() => _effect.Cancel());
        }

        [Test]
        public void WatchPlayer_DefaultIsTrue()
        {
            bool val = (bool)typeof(InvertedObjectEffect).GetField("watchPlayer", F).GetValue(_effect);
            Assert.IsTrue(val);
        }

        [Test]
        public void OnlyWhenUnobserved_DefaultIsTrue()
        {
            bool val = (bool)typeof(InvertedObjectEffect).GetField("onlyWhenUnobserved", F).GetValue(_effect);
            Assert.IsTrue(val);
        }
    }
}
