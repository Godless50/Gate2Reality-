using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Gate2Reality.Effects;

namespace Gate2Reality.Tests
{
    [TestFixture]
    public sealed class EchoSurfaceEffectTests
    {
        private static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Pose ZeroPose = new Pose(Vector3.zero, Quaternion.identity);

        private GameObject _go;

        [TearDown]
        public void TearDown() { if (_go != null) Object.DestroyImmediate(_go); }

        private EchoSurfaceEffect CreateEffect()
        {
            _go = new GameObject("EchoSurface");
            _go.SetActive(false);
            var fx = _go.AddComponent<EchoSurfaceEffect>();
            _go.SetActive(true);
            return fx;
        }

        // Gemma GPU: _ripplePhase resets to 0 on Trigger. Fix: typo EchoSurfaceevice → EchoSurfaceEffect.
        [Test]
        public void EchoSurfaceEffect_OnTriggered_RipplePhaseIsZero()
        {
            var fx = CreateEffect();
            fx.Trigger(in ZeroPose);
            float phase = (float)typeof(EchoSurfaceEffect)
                .GetField("_ripplePhase", F)
                .GetValue(fx);
            Assert.AreEqual(0f, phase, 0.001f);
        }

        [Test]
        public void EchoSurfaceEffect_Trigger_NullRefs_DoesNotThrow()
        {
            var fx = CreateEffect();
            Assert.DoesNotThrow(() => fx.Trigger(in ZeroPose));
        }

        [Test]
        public void EchoSurfaceEffect_Cancel_NullRefs_DoesNotThrow()
        {
            var fx = CreateEffect();
            fx.Trigger(in ZeroPose);
            Assert.DoesNotThrow(() => fx.Cancel());
        }
    }
}
