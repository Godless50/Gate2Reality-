using NUnit.Framework;
using UnityEngine;
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

            Object.DestroyImmediate(go);
        }
    }
}
