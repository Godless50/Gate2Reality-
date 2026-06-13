using UnityEngine;
using Gate2Reality.Narrative;

namespace Gate2Reality.Effects
{
    [RequireComponent(typeof(AudioSource))]
    public abstract class TriggerableEffectBase : MonoBehaviour, ITriggerable
    {
        [SerializeField] private string triggerId;
        [SerializeField] private bool snapToAnchor = true;

        public string TriggerId => triggerId;
        public bool IsActive { get; private set; }
        public Pose Anchor { get; private set; }
        public float TimeSinceTriggered { get; private set; }

        private bool _finished;

        public void Trigger(in Pose worldAnchor)
        {
            if (IsActive) return;
            IsActive = true;
            _finished = false;
            TimeSinceTriggered = 0f;
            Anchor = worldAnchor;

            if (snapToAnchor)
            {
                transform.position = worldAnchor.position;
                transform.rotation = worldAnchor.rotation;
            }

            gameObject.SetActive(true);
            OnTriggered();
        }

        public void Cancel()
        {
            if (!IsActive) return;
            IsActive = false;
            OnCancelled();
        }

        protected void MarkFinished()
        {
            _finished = true;
            IsActive = false;
        }

        private void Update()
        {
            if (!IsActive) return;
            TimeSinceTriggered += Time.deltaTime;
            OnEffectUpdate(Time.deltaTime);
        }

        protected abstract void OnTriggered();
        protected virtual void OnCancelled() { }
        protected virtual void OnEffectUpdate(float dt) { }
    }
}
