using NUnit.Framework;
using UnityEngine;
using Gate2Reality.Persistence;
using Gate2Reality.Narrative;

namespace Gate2Reality.Tests
{
    [TestFixture]
    public class AnchorRegistryTests
    {
        private AnchorRegistry _registry;
        private GameObject _container;

        [SetUp]
        public void SetUp()
        {
            _container = new GameObject();
            _registry = _container.AddComponent<AnchorRegistry>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_container);
        }

        [Test]
        public void Register_AddsEntry()
        {
            var t = new GameObject().transform;
            _registry.Register(1, NarrativeLabel.Chair, t);
            Assert.AreEqual(1, _registry.All.Count);
            UnityEngine.Object.DestroyImmediate(t.gameObject);
        }

        [Test]
        public void TryGet_ReturnsCorrectTransform()
        {
            var t = new GameObject().transform;
            _registry.Register(10, NarrativeLabel.Book, t);
            bool found = _registry.TryGet(10, out var result);
            Assert.IsTrue(found);
            Assert.AreSame(t, result);
            UnityEngine.Object.DestroyImmediate(t.gameObject);
        }

        [Test]
        public void Register_DuplicateNodeIndex_UpdatesExisting()
        {
            var t1 = new GameObject().transform;
            var t2 = new GameObject().transform;
            _registry.Register(1, NarrativeLabel.Chair, t1);
            _registry.Register(1, NarrativeLabel.Cup, t2);
            Assert.AreEqual(1, _registry.All.Count);
            Assert.AreSame(t2, _registry.All[0].t);
            UnityEngine.Object.DestroyImmediate(t1.gameObject);
            UnityEngine.Object.DestroyImmediate(t2.gameObject);
        }

        [Test]
        public void Register_NullAnchor_IsIgnored()
        {
            _registry.Register(1, NarrativeLabel.Chair, null);
            Assert.AreEqual(0, _registry.All.Count);
        }

        [Test]
        public void TryGet_UnknownIndex_ReturnsFalse()
        {
            bool found = _registry.TryGet(999, out var result);
            Assert.IsFalse(found);
            Assert.IsNull(result);
        }

        [Test]
        public void Clear_EmptiesRegistry()
        {
            var t = new GameObject().transform;
            _registry.Register(1, NarrativeLabel.Chair, t);
            _registry.Clear();
            Assert.AreEqual(0, _registry.All.Count);
            UnityEngine.Object.DestroyImmediate(t.gameObject);
        }

        // 14B: Clear also removes from internal index — TryGet must return false
        [Test]
        public void TryGet_AfterClear_ReturnsFalse()
        {
            var t = new GameObject().transform;
            _registry.Register(5, NarrativeLabel.Chair, t);
            _registry.Clear();
            Assert.IsFalse(_registry.TryGet(5, out _));
            UnityEngine.Object.DestroyImmediate(t.gameObject);
        }

        [Test]
        public void All_ReflectsRegisteredEntries()
        {
            var t1 = new GameObject().transform;
            var t2 = new GameObject().transform;
            _registry.Register(1, NarrativeLabel.Chair, t1);
            _registry.Register(2, NarrativeLabel.Book, t2);
            Assert.AreEqual(2, _registry.All.Count);
            Assert.AreEqual(1, _registry.All[0].nodeIndex);
            Assert.AreEqual(NarrativeLabel.Chair, _registry.All[0].label);
            Assert.AreEqual(2, _registry.All[1].nodeIndex);
            Assert.AreEqual(NarrativeLabel.Book, _registry.All[1].label);
            UnityEngine.Object.DestroyImmediate(t1.gameObject);
            UnityEngine.Object.DestroyImmediate(t2.gameObject);
        }
    }
}
