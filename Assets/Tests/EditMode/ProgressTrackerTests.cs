using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Gate2Reality.Narrative;
using Gate2Reality.Persistence;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// EditMode tests for ProgressTracker public API.
    /// Gemma-2 wrote BeginFreshStart test; inspector added ClearProgress and BeginResume tests.
    /// </summary>
    [TestFixture]
    public sealed class ProgressTrackerTests
    {
        private static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _mgrGo;
        private GameObject _trackerGo;
        private NarrativeManager _mgr;
        private ProgressTracker _tracker;

        [SetUp]
        public void SetUp()
        {
            ProgressStore.Clear();

            _mgrGo = new GameObject("NarrativeMgr");
            _mgrGo.SetActive(false);
            _mgr = _mgrGo.AddComponent<NarrativeManager>();
            _mgrGo.SetActive(true);

            _trackerGo = new GameObject("ProgressTracker");
            _trackerGo.SetActive(false);
            _tracker = _trackerGo.AddComponent<ProgressTracker>();
            typeof(ProgressTracker).GetField("narrativeManager", F).SetValue(_tracker, _mgr);
            _trackerGo.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            ProgressStore.Clear();
            if (_mgrGo)     UnityEngine.Object.DestroyImmediate(_mgrGo);
            if (_trackerGo) UnityEngine.Object.DestroyImmediate(_trackerGo);
        }

        // Gemma-2: accepted as-is
        [Test]
        public void BeginFreshStart_ClearsProgressFile()
        {
            ProgressStore.Save(new ProgressData { nodeIndex = 5 });
            _tracker.BeginFreshStart();
            Assert.IsFalse(ProgressStore.HasSave, "BeginFreshStart should clear save file");
        }

        [Test]
        public void ClearProgress_DeletesSaveFile()
        {
            ProgressStore.Save(new ProgressData { nodeIndex = 2 });
            _tracker.ClearProgress();
            Assert.IsFalse(ProgressStore.HasSave);
        }

        [Test]
        public void TryPeekSave_WhenNoSave_ReturnsFalse()
        {
            bool found = _tracker.TryPeekSave(out _);
            Assert.IsFalse(found);
        }

        [Test]
        public void TryPeekSave_AfterSave_ReturnsData()
        {
            ProgressStore.Save(new ProgressData { nodeIndex = 3, chapter = 1 });
            bool found = _tracker.TryPeekSave(out var data);
            Assert.IsTrue(found);
            Assert.AreEqual(3, data.nodeIndex);
        }
    }
}
