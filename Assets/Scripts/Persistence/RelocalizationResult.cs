using System.Collections.Generic;
using UnityEngine;

namespace Gate2Reality.Persistence
{
    public readonly struct RelocalizationResult
    {
        /// <summary>Achieved level: 1 = warm return, 2 = YOLO re-detect, 3 = relative fallback.</summary>
        public readonly int Level;
        /// <summary>Map of graph nodeIndex → restored Transform.</summary>
        public readonly IReadOnlyDictionary<int, Transform> NodeAnchors;

        public RelocalizationResult(int level, Dictionary<int, Transform> anchors)
        {
            Level = level;
            NodeAnchors = anchors;
        }
    }

    public interface IAnchorRelocalizer
    {
        /// <summary>
        /// Restores anchors from a saved ProgressData snapshot via L1→L2→L3 fallback.
        /// onComplete is called exactly once, on the main thread.
        /// </summary>
        void Relocalize(ProgressData save, System.Action<RelocalizationResult> onComplete);
    }
}
