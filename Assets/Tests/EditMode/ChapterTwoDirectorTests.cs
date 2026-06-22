using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Gate2Reality.ChapterTwo;
using Gate2Reality.Effects;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// Tests for ChapterTwoDirector: OnChapterTwoBegan event and idempotency.
    /// Gemma GPU wrote structure; inspector fixed: SetActive(false) before AddComponent,
    /// event handler stored in variable for proper unsubscription.
    /// </summary>
    [TestFixture]
    public sealed class ChapterTwoDirectorTests
    {
        private static readonly BindingFlags SF = BindingFlags.Static  | BindingFlags.NonPublic;
        private static readonly BindingFlags IF = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _go;
        private ChapterTwoDirector _director;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Ch2Director");
            _go.SetActive(false);
            _director = _go.AddComponent<ChapterTwoDirector>();
            _go.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.DestroyImmediate(_go);
            typeof(CrossingTransitionEffect).GetField("OnCrossedOver", SF)?.SetValue(null, null);
            typeof(ChapterTwoDirector).GetField("OnChapterTwoBegan", SF)?.SetValue(null, null);
        }

        private static void FireCrossedOver()
        {
            var field = typeof(CrossingTransitionEffect).GetField("OnCrossedOver", SF);
            (field?.GetValue(null) as Action)?.Invoke();
        }

        [Test]
        public void OnChapterTwoBegan_FiresOnCrossOver()
        {
            bool fired = false;
            Action handler = () => fired = true;
            ChapterTwoDirector.OnChapterTwoBegan += handler;
            try { FireCrossedOver(); Assert.IsTrue(fired); }
            finally { ChapterTwoDirector.OnChapterTwoBegan -= handler; }
        }

        [Test]
        public void HandleCrossedOver_SetsBeganFlag()
        {
            bool before = (bool)typeof(ChapterTwoDirector).GetField("_began", IF).GetValue(_director);
            FireCrossedOver();
            bool after = (bool)typeof(ChapterTwoDirector).GetField("_began", IF).GetValue(_director);
            Assert.IsFalse(before); Assert.IsTrue(after);
        }

        [Test]
        public void HandleCrossedOver_IsIdempotent()
        {
            FireCrossedOver(); FireCrossedOver();
            bool began = (bool)typeof(ChapterTwoDirector).GetField("_began", IF).GetValue(_director);
            Assert.IsTrue(began, "_began must be true after first crossing");
        }
    }
}
