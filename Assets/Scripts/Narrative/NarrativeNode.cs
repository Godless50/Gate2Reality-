using System;
using UnityEngine;

namespace Gate2Reality.Narrative
{
    [Serializable]
    public class NarrativeNode
    {
        public string nodeName;
        public NarrativeCondition condition;
        public float dwellTimeSeconds = 0f;

        [SerializeField] public MonoBehaviour[] triggerableBehaviours;
        public int[] nextNodeIndices = Array.Empty<int>();

        [NonSerialized] public ITriggerable[] CachedTriggerables;
        [NonSerialized] public float DwellAccumulator;
        [NonSerialized] public Pose LastSeenPose;

        public void BuildCache()
        {
            if (triggerableBehaviours == null || triggerableBehaviours.Length == 0)
            {
                CachedTriggerables = Array.Empty<ITriggerable>();
                return;
            }

            int count = 0;
            for (int i = 0; i < triggerableBehaviours.Length; i++)
                if (triggerableBehaviours[i] is ITriggerable) count++;

            CachedTriggerables = new ITriggerable[count];
            int idx = 0;
            for (int i = 0; i < triggerableBehaviours.Length; i++)
                if (triggerableBehaviours[i] is ITriggerable t) CachedTriggerables[idx++] = t;
        }
    }
}
