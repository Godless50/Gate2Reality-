using System.Collections.Generic;
using UnityEngine;
using Gate2Reality.Narrative;

namespace Gate2Reality.Persistence
{
    public interface IAnchorRegistry
    {
        void Register(int nodeIndex, NarrativeLabel label, Transform anchor);
        bool TryGet(int nodeIndex, out Transform anchor);
        IReadOnlyList<(int nodeIndex, NarrativeLabel label, Transform t)> All { get; }
    }
}
