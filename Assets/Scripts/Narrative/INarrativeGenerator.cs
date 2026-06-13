using System;
using UnityEngine;

namespace Gate2Reality.Narrative
{
    public struct NarrativeContext
    {
        public int DetectionBitmask;   // which NarrativeLabels were seen recently
        public float AmbientIntensity;
        public int RoomHeuristic;      // rough plane-count bucket: 0=sparse, 1=medium, 2=dense
    }

    public interface INarrativeGenerator
    {
        void RequestWhisper(int nodeIndex, Pose anchor);
        void SetContext(in NarrativeContext ctx);
        event Action<string> OnWhisperReady;
    }
}
