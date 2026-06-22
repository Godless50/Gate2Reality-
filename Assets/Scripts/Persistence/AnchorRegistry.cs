using System.Collections.Generic;
using UnityEngine;
using Gate2Reality.Narrative;

namespace Gate2Reality.Persistence
{
    /// <summary>
    /// Живой регистр якорей текущей сессии. Эффекты Главы I регистрируют сюда
    /// свои Transform по мере активации; ProgressTracker читает регистр для
    /// сериализации и ChapterTwoDirector — для переноса якорей в Главу II.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnchorRegistry : MonoBehaviour, IAnchorRegistry
    {
        private readonly List<(int nodeIndex, NarrativeLabel label, Transform t)> _entries
            = new List<(int, NarrativeLabel, Transform)>(8);
        private readonly Dictionary<int, int> _indexByNode = new Dictionary<int, int>(8);

        public IReadOnlyList<(int nodeIndex, NarrativeLabel label, Transform t)> All => _entries;

        public void Register(int nodeIndex, NarrativeLabel label, Transform anchor)
        {
            if (anchor == null) return;
            if (_indexByNode.TryGetValue(nodeIndex, out int existing))
            {
                _entries[existing] = (nodeIndex, label, anchor);
            }
            else
            {
                _indexByNode[nodeIndex] = _entries.Count;
                _entries.Add((nodeIndex, label, anchor));
            }
        }

        public bool TryGet(int nodeIndex, out Transform anchor)
        {
            if (_indexByNode.TryGetValue(nodeIndex, out int idx))
            {
                (int _, NarrativeLabel __, Transform t) = _entries[idx];
                if (t != null) { anchor = t; return true; }
            }
            anchor = null;
            return false;
        }

        public void Clear()
        {
            _entries.Clear();
            _indexByNode.Clear();
        }
    }
}
