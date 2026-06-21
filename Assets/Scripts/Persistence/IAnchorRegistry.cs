using System.Collections.Generic;
using UnityEngine;
using Gate2Reality.Narrative;

namespace Gate2Reality.Persistence
{
    public interface IAnchorRegistry
    {
        /// <summary>Registers an anchor at the specified node index with a label and transform.</summary>
        void Register(int nodeIndex, NarrativeLabel label, Transform anchor);

        /// <summary>Attempts to retrieve the anchor transform at the given node index.</summary>
        bool TryGet(int nodeIndex, out Transform anchor);

        /// <summary>Removes all registered anchors and clears the internal index.</summary>
        void Clear();

        /// <summary>Read-only list of all registered entries (nodeIndex, label, transform).</summary>
        IReadOnlyList<(int nodeIndex, NarrativeLabel label, Transform t)> All { get; }
    }
}
