using UnityEngine;

namespace Gate2Reality.Narrative
{
    public readonly struct DetectionEvent
    {
        public readonly NarrativeLabel Label;
        public readonly Pose WorldPose;
        public readonly float Confidence;
        public readonly float BoundsRadius;

        public DetectionEvent(NarrativeLabel label, Pose worldPose, float confidence, float boundsRadius)
        {
            Label = label;
            WorldPose = worldPose;
            Confidence = confidence;
            BoundsRadius = boundsRadius;
        }
    }
}
