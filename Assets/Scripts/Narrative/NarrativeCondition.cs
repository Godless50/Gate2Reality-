using System;
using UnityEngine;

namespace Gate2Reality.Narrative
{
    public enum NarrativeLabel : byte
    {
        None,
        Chair,
        Book,
        Cup,
        EchoZone,
        Portal
    }

    public enum ConditionType : byte
    {
        SemanticDetection,
        Proximity,
        Gaze
    }

    [Serializable]
    public class NarrativeCondition
    {
        public ConditionType type;
        public NarrativeLabel requiredLabel;
        public float minConfidence = 0.5f;
        public float maxBoundsRadius = 2f;

        [NonSerialized] public Transform runtimeTarget;

        public float triggerRadius = 1.5f;
        public float maxGazeAngleDeg = 15f;
        public float maxGazeDistance = 3f;

        private Pose _lastKnownAnchor;
        private bool _hasKnownAnchor;

        public bool MatchesDetection(in DetectionEvent evt)
        {
            if (type != ConditionType.SemanticDetection) return false;
            if (evt.Label != requiredLabel) return false;
            if (evt.Confidence < minConfidence) return false;
            if (evt.BoundsRadius > maxBoundsRadius) return false;
            _lastKnownAnchor = evt.WorldPose;
            _hasKnownAnchor = true;
            return true;
        }

        public bool EvaluateSpatial(Transform cameraTransform, out Pose resultPose)
        {
            resultPose = default;
            if (runtimeTarget == null) return false;

            Vector3 toTarget = runtimeTarget.position - cameraTransform.position;

            if (type == ConditionType.Proximity)
            {
                if (toTarget.magnitude <= triggerRadius)
                {
                    resultPose = new Pose(runtimeTarget.position, runtimeTarget.rotation);
                    return true;
                }
                return false;
            }

            if (type == ConditionType.Gaze)
            {
                float dist = toTarget.magnitude;
                if (dist > maxGazeDistance) return false;
                float angle = Vector3.Angle(cameraTransform.forward, toTarget);
                if (angle <= maxGazeAngleDeg)
                {
                    resultPose = new Pose(runtimeTarget.position, runtimeTarget.rotation);
                    return true;
                }
                return false;
            }

            return false;
        }

        public bool TryGetKnownAnchor(out Pose anchor)
        {
            anchor = _lastKnownAnchor;
            return _hasKnownAnchor;
        }

        public string Describe()
        {
            return type switch
            {
                ConditionType.SemanticDetection => $"Detect {requiredLabel} (conf≥{minConfidence:F2})",
                ConditionType.Proximity => $"Proximity to {runtimeTarget?.name ?? "?"} ≤{triggerRadius}m",
                ConditionType.Gaze => $"Gaze at {runtimeTarget?.name ?? "?"} ≤{maxGazeAngleDeg}°",
                _ => "Unknown"
            };
        }
    }
}
