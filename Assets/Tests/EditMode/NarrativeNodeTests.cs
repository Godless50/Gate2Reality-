using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Gate2Reality.Narrative;

namespace Gate2Reality.Tests
{
    [TestFixture]
    public sealed class NarrativeNodeTests
    {
        private class FakeTriggerable : MonoBehaviour, ITriggerable
        {
            public string TriggerId => "fake";
            public void Trigger(in Pose worldAnchor) { }
            public void Cancel() { }
            public bool IsActive => true;
        }

        [Test]
        public void BuildCache_ValidBehaviour_PopulatesCachedTriggerables()
        {
            var go   = new GameObject("FakeTriggerable");
            var fake = go.AddComponent<FakeTriggerable>();

            var node = new NarrativeNode();
            node.triggerableBehaviours = new MonoBehaviour[] { fake };

            node.BuildCache();

            Assert.AreEqual(1, node.CachedTriggerables.Length);
            Assert.IsNotNull(node.CachedTriggerables[0]);

            UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void BuildCache_NullBehaviour_ProducesNullSlotInCache()
        {
            var node = new NarrativeNode();
            node.triggerableBehaviours = new MonoBehaviour[] { null };

            // BuildCache logs an error for each null/non-ITriggerable element in EDITOR builds.
            // Declare it expected so the test runner does not count it as a failure.
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex(@"\[Gate2Reality\].*не реализует ITriggerable"));

            Assert.DoesNotThrow(() => node.BuildCache());

            Assert.AreEqual(1, node.CachedTriggerables.Length);
            Assert.IsNull(node.CachedTriggerables[0]);
        }
    }
}
