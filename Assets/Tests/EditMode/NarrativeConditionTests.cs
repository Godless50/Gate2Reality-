using NUnit.Framework;
using UnityEngine;
using Gate2Reality.Narrative;

namespace Gate2Reality.Tests
{
    public sealed class NarrativeConditionTests
    {
        private Transform _player;
        private Transform _target;

        [SetUp]
        public void SetUp()
        {
            _player = new GameObject("player").transform;
            _player.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _target = new GameObject("target").transform;
        }

        [TearDown]
        public void TearDown()
        {
            if (_player != null) Object.DestroyImmediate(_player.gameObject);
            if (_target != null) Object.DestroyImmediate(_target.gameObject);
        }

        private static NarrativeCondition Semantic() => new NarrativeCondition
        {
            type = ConditionType.SemanticDetection,
            requiredLabel = NarrativeLabel.Chair,
            minConfidence = 0.85f,
            maxBoundsRadius = 1.5f
        };

        private static DetectionEvent Det(NarrativeLabel label, float conf, float radius)
        {
            var pose = new Pose(Vector3.zero, Quaternion.identity);
            return new DetectionEvent(label, in pose, conf, radius);
        }

        [Test]
        public void Matches_WhenLabelConfidenceAndRadiusPass()
        {
            Assert.IsTrue(Semantic().MatchesDetection(Det(NarrativeLabel.Chair, 0.9f, 0.5f)));
        }

        [Test]
        public void Rejects_WrongLabel()
        {
            Assert.IsFalse(Semantic().MatchesDetection(Det(NarrativeLabel.Book, 0.9f, 0.5f)));
        }

        [Test]
        public void Rejects_LowConfidence()
        {
            Assert.IsFalse(Semantic().MatchesDetection(Det(NarrativeLabel.Chair, 0.5f, 0.5f)));
        }

        [Test]
        public void Rejects_OversizedBounds()
        {
            Assert.IsFalse(Semantic().MatchesDetection(Det(NarrativeLabel.Chair, 0.9f, 2.0f)));
        }

        [Test]
        public void Semantic_EvaluateSpatial_AlwaysFalse()
        {
            _target.position = new Vector3(0, 0, 1);
            NarrativeCondition c = Semantic();
            c.runtimeTarget = _target;
            Assert.IsFalse(c.EvaluateSpatial(_player, out _));
        }

        // ── Граничный случай: maxBoundsRadius=0 отключает проверку радиуса ──

        [Test]
        public void MatchesDetection_BoundsRadiusZero_SkipsRadiusCheck()
        {
            var cond = new NarrativeCondition
            {
                type = ConditionType.SemanticDetection,
                requiredLabel = NarrativeLabel.Chair,
                minConfidence = 0.8f,
                maxBoundsRadius = 0f
            };
            var pose = new Pose(Vector3.zero, Quaternion.identity);
            var evt = new DetectionEvent(NarrativeLabel.Chair, in pose, 0.9f, 999f);
            Assert.IsTrue(cond.MatchesDetection(in evt));
        }

        private NarrativeCondition Proximity() => new NarrativeCondition
        {
            type = ConditionType.Proximity,
            triggerRadius = 1.2f,
            runtimeTarget = _target
        };

        [Test]
        public void Proximity_FiresInsideRadius()
        {
            _target.position = new Vector3(0, 0, 1f);
            Assert.IsTrue(Proximity().EvaluateSpatial(_player, out Pose p));
            Assert.AreEqual(_target.position, p.position);
        }

        [Test]
        public void Proximity_SilentOutsideRadius()
        {
            _target.position = new Vector3(0, 0, 2f);
            Assert.IsFalse(Proximity().EvaluateSpatial(_player, out _));
        }

        private NarrativeCondition Gaze() => new NarrativeCondition
        {
            type = ConditionType.Gaze,
            maxGazeAngleDeg = 12f,
            maxGazeDistance = 6f,
            runtimeTarget = _target
        };

        [Test]
        public void Gaze_FiresWhenLookingAt()
        {
            _target.position = new Vector3(0, 0, 3f);
            Assert.IsTrue(Gaze().EvaluateSpatial(_player, out _));
        }

        [Test]
        public void Gaze_SilentWhenLookingAway()
        {
            _target.position = new Vector3(3f, 0, 0f);
            Assert.IsFalse(Gaze().EvaluateSpatial(_player, out _));
        }

        [Test]
        public void Gaze_SilentBeyondDistance()
        {
            _target.position = new Vector3(0, 0, 7f);
            Assert.IsFalse(Gaze().EvaluateSpatial(_player, out _));
        }

        private NarrativeCondition Averted() => new NarrativeCondition
        {
            type = ConditionType.AvertedGaze,
            maxGazeAngleDeg = 12f,
            maxGazeDistance = 6f,
            runtimeTarget = _target
        };

        [Test]
        public void AvertedGaze_SilentWhenObserved()
        {
            _target.position = new Vector3(0, 0, 3f);
            Assert.IsFalse(Averted().EvaluateSpatial(_player, out _));
        }

        [Test]
        public void AvertedGaze_FiresWhenLookingAway()
        {
            _target.position = new Vector3(3f, 0, 0f);
            Assert.IsTrue(Averted().EvaluateSpatial(_player, out Pose p));
            Assert.AreEqual(_target.position, p.position);
        }

        [Test]
        public void AvertedGaze_SilentBeyondDistance()
        {
            _target.position = new Vector3(20f, 0, 0f);
            Assert.IsFalse(Averted().EvaluateSpatial(_player, out _));
        }

        [Test]
        public void GazeAndAvertedGaze_AreMutuallyExclusive()
        {
            foreach (var pos in new[]
                     { new Vector3(0, 0, 3f), new Vector3(3f, 0, 0f), new Vector3(1f, 0, 2f) })
            {
                _target.position = pos;
                bool gaze    = Gaze().EvaluateSpatial(_player, out _);
                bool averted = Averted().EvaluateSpatial(_player, out _);
                Assert.AreNotEqual(gaze, averted,
                    $"Позиция {pos}: предикаты должны быть противоположны");
            }
        }
    }
}
