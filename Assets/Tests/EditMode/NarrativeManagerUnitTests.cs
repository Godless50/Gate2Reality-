using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Gate2Reality.Narrative;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// EditMode unit tests for NarrativeManager public API (no Update loop needed).
    /// </summary>
    [TestFixture]
    public sealed class NarrativeManagerUnitTests
    {
        private static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _go;
        private NarrativeManager _mgr;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("MGR");
            _go.SetActive(false);
            _mgr = _go.AddComponent<NarrativeManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private void SetField(string name, object value) =>
            typeof(NarrativeManager).GetField(name, F).SetValue(_mgr, value);

        // ── SetNodeRuntimeTarget (Gemma GPU, accepted as-is) ─────────────────

        [Test]
        public void SetNodeRuntimeTarget_UpdatesConditionRuntimeTarget()
        {
            var node = new NarrativeNode { condition = new NarrativeCondition() };
            SetField("nodes", new[] { node });
            _go.SetActive(true);

            var anchorGo = new GameObject();
            _mgr.SetNodeRuntimeTarget(0, anchorGo.transform);

            Assert.AreSame(anchorGo.transform, node.condition.runtimeTarget);
            Object.DestroyImmediate(anchorGo);
        }

        [Test]
        public void SetNodeRuntimeTarget_OutOfRange_DoesNotThrow()
        {
            SetField("nodes", new NarrativeNode[] { });
            _go.SetActive(true);

            Assert.DoesNotThrow(() => _mgr.SetNodeRuntimeTarget(99, null));
        }

        // ── NarrativeContext (Gemma-2, Library→Unknown fix by inspector) ─────

        // ── Scene lifecycle (Gemma GPU) ───────────────────────────────────────

        [Test]
        public void IsSceneRunning_FalseAfterCompleteScene()
        {
            var go = new GameObject(); go.SetActive(false);
            var mgr = go.AddComponent<NarrativeManager>();

            typeof(NarrativeManager)
                .GetField("nodes", F)
                .SetValue(mgr, new NarrativeNode[] { new NarrativeNode { dwellTimeSeconds = 0f } });

            go.SetActive(true);
            mgr.StartScene();
            Assert.IsTrue(mgr.IsSceneRunning);

            typeof(NarrativeManager)
                .GetMethod("CompleteScene", F)
                .Invoke(mgr, null);

            Assert.IsFalse(mgr.IsSceneRunning);
            Object.DestroyImmediate(go);
        }

        // ── NarrativeContext (Gemma-2, Library→Unknown fix by inspector) ─────

        [Test]
        public void NarrativeContext_FocusObject_MatchesConstructorArg()
        {
            var ctx = new NarrativeContext(NarrativeLabel.Book, 0, 1.0f, 3000f, RoomType.Unknown);
            Assert.AreEqual(NarrativeLabel.Book, ctx.FocusObject);
        }
    }
}
