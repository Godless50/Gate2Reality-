using UnityEngine;

namespace Gate2Reality.Narrative
{
    public interface ITriggerable
    {
        string TriggerId { get; }
        bool IsActive { get; }

        void Trigger(in Pose worldAnchor);
        void Cancel();
    }
}
