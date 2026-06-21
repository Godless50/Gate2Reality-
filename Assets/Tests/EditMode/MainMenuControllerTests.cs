using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Gate2Reality.UI;
using Gate2Reality.Persistence;

namespace Gate2Reality.Tests
{
    /// <summary>
    /// Tests for MainMenuController static helper methods (reflection-based).
    /// Public button handlers are private; testing via FormatElapsed/ProgressLabel
    /// which are pure functions with deterministic output.
    /// </summary>
    [TestFixture]
    public sealed class MainMenuControllerTests
    {
        private static readonly BindingFlags SF = BindingFlags.Static | BindingFlags.NonPublic;

        private static string FormatElapsed(long seconds) =>
            (string)typeof(MainMenuController)
                .GetMethod("FormatElapsed", SF)
                .Invoke(null, new object[] { seconds });

        private static string ProgressLabel(int nodeIndex) =>
            (string)typeof(MainMenuController)
                .GetMethod("ProgressLabel", SF)
                .Invoke(null, new object[] { nodeIndex });

        // ── FormatElapsed ─────────────────────────────────────────────────────

        [Test]
        public void FormatElapsed_Under120Seconds_ReturnsJustNow()
        {
            Assert.AreEqual("just now", FormatElapsed(0));
            Assert.AreEqual("just now", FormatElapsed(119));
        }

        [Test]
        public void FormatElapsed_Minutes_ReturnsMinAgo()
        {
            Assert.AreEqual("2m ago", FormatElapsed(120));
            Assert.AreEqual("59m ago", FormatElapsed(3599));
        }

        [Test]
        public void FormatElapsed_Hours_ReturnsHAgo()
        {
            Assert.AreEqual("1h ago", FormatElapsed(3600));
            Assert.AreEqual("23h ago", FormatElapsed(86399));
        }

        [Test]
        public void FormatElapsed_Yesterday_ReturnsYesterday()
        {
            Assert.AreEqual("yesterday", FormatElapsed(86400));
        }

        [Test]
        public void FormatElapsed_MultipleDays_ReturnsDaysAgo()
        {
            Assert.AreEqual("3d ago", FormatElapsed(86400 * 3));
        }

        // ── ProgressLabel ─────────────────────────────────────────────────────

        [Test]
        public void ProgressLabel_Node0_ReturnsChair()
        {
            Assert.AreEqual("the chair", ProgressLabel(0));
        }

        [Test]
        public void ProgressLabel_Node1_ReturnsBook()
        {
            Assert.AreEqual("the book", ProgressLabel(1));
        }

        [Test]
        public void ProgressLabel_Node2_ReturnsCup()
        {
            Assert.AreEqual("the cup", ProgressLabel(2));
        }

        [Test]
        public void ProgressLabel_Node5_ReturnsPortalWall()
        {
            Assert.AreEqual("the portal wall", ProgressLabel(5));
        }

        [Test]
        public void ProgressLabel_UnknownNode_ReturnsNodeN()
        {
            Assert.AreEqual("node 99", ProgressLabel(99));
        }

        // ── Null-safety of Awake ─────────────────────────────────────────────

        [Test]
        public void Awake_AllNullRefs_DoesNotThrow()
        {
            var go = new GameObject("MenuController");
            go.SetActive(false);
            go.AddComponent<MainMenuController>();
            Assert.DoesNotThrow(() => go.SetActive(true));
            Object.DestroyImmediate(go);
        }
    }
}
