using NUnit.Framework;
using UnityEngine;
using Gate2Reality.Narrative;

namespace Gate2Reality.Tests
{
    [TestFixture]
    public sealed class DetectionEventTests
    {
        [Test]
        public void DetectionEvent_Constructor_SetsAllFields()
        {
            var pose        = new Pose(Vector3.forward, Quaternion.identity);
            var confidence  = 0.95f;
            var boundsRadius = 0.3f;
            var label       = NarrativeLabel.Cup;

            var evt = new DetectionEvent(label, pose, confidence, boundsRadius);

            Assert.That(evt.Label,                  Is.EqualTo(label));
            Assert.That(evt.Confidence,             Is.EqualTo(confidence).Within(0.001f));
            Assert.That(evt.BoundsRadius,           Is.EqualTo(boundsRadius).Within(0.001f));
            Assert.That(evt.WorldPose.position,     Is.EqualTo(Vector3.forward));
        }
    }
}
