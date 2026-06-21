using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Gate2Reality.ChapterTwo;
using Gate2Reality.Narrative;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// Tests for InvertedGuardDirector: adversarial lure placement.
    /// Gemma-2 wrote this test; accepted with minor namespace additions.
    /// </summary>
    [TestFixture]
    public sealed class InvertedGuardDirectorTests
    {
        private static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _directorGo;
        private GameObject _cameraGo;
        private GameObject _lureGo;

        [TearDown]
        public void TearDown()
        {
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_cameraGo   != null) Object.DestroyImmediate(_cameraGo);
            if (_lureGo     != null) Object.DestroyImmediate(_lureGo);
        }

        [Test]
        public void HandleFalseLure_PlacesLureBehindPlayer()
        {
            // Arrange
            _directorGo = new GameObject("InvGuard");
            _directorGo.SetActive(false);  // OnEnable null-safe (chapterTwoNarrative==null)
            var director = _directorGo.AddComponent<InvertedGuardDirector>();

            _cameraGo = new GameObject("Cam");
            _cameraGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            _lureGo = new GameObject("Lure");
            var lureSource = _lureGo.AddComponent<AudioSource>();

            typeof(InvertedGuardDirector).GetField("arCameraTransform", F)
                .SetValue(director, _cameraGo.transform);
            typeof(InvertedGuardDirector).GetField("lureSource", F)
                .SetValue(director, lureSource);

            _directorGo.SetActive(true);

            // Camera at (0,0,0), target at (0,0,5) → away = -Z → lure at (0,0,-3)
            var targetPose = new Pose(new Vector3(0f, 0f, 5f), Quaternion.identity);

            typeof(InvertedGuardDirector)
                .GetMethod("HandleFalseLure", F)
                .Invoke(director, new object[] { targetPose });

            // Assert: lure must be behind player (negative Z)
            Assert.Less(lureSource.transform.position.z, 0f,
                "lure should be placed behind player, away from target");
        }

        [Test]
        public void HandleWorldClosing_SetsCloseDirectionPositive()
        {
            _directorGo = new GameObject("InvGuard");
            _directorGo.SetActive(false);
            var director = _directorGo.AddComponent<InvertedGuardDirector>();
            _directorGo.SetActive(true);

            typeof(InvertedGuardDirector)
                .GetMethod("HandleWorldClosing", F)
                .Invoke(director, null);

            float dir = (float)typeof(InvertedGuardDirector)
                .GetField("_closeDirection", F)
                .GetValue(director);

            Assert.AreEqual(1f, dir, 0.001f, "closing should set direction to +1");
        }

        [Test]
        public void HandleWorldRelease_SetsCloseDirectionNegative()
        {
            _directorGo = new GameObject("InvGuard");
            _directorGo.SetActive(false);
            var director = _directorGo.AddComponent<InvertedGuardDirector>();
            _directorGo.SetActive(true);

            typeof(InvertedGuardDirector)
                .GetMethod("HandleWorldRelease", F)
                .Invoke(director, null);

            float dir = (float)typeof(InvertedGuardDirector)
                .GetField("_closeDirection", F)
                .GetValue(director);

            Assert.AreEqual(-1f, dir, 0.001f, "release should set direction to -1");
        }
    }
}
