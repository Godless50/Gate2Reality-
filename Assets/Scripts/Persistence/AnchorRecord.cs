using System;
using UnityEngine;

namespace Gate2Reality.Persistence
{
    [Serializable]
    public sealed class AnchorRecord
    {
        public int label;        // (int)NarrativeLabel — Chair/Book/Cup/EchoZone…
        public int nodeIndex;    // graph node this anchor belongs to
        public Vector3 localPos; // position relative to reference anchor's coordinate frame
        public Quaternion localRot;
        public float confidence;
    }

    [Serializable]
    public sealed class RoomFingerprint
    {
        // Upper-triangle of pairwise distance matrix (i < j, row-major).
        // Index for pair (i,j): i*n - i*(i+1)/2 + (j-i-1)
        public float[] pairwiseDistances;
        public int anchorCount;
    }
}
