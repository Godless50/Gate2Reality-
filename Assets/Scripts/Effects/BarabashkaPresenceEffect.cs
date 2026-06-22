namespace Gate2Reality.Effects
{
    using Gate2Reality.Narrative;
    using UnityEngine;

    public class BarabashkaPresenceEffect : TriggerableEffectBase
    {
        [SerializeField]
        private NarrativeManager narrativeManager;

        [SerializeField]
        private Transform shadowRoot;

        [SerializeField]
        private Renderer shadowRenderer;

        [SerializeField]
        private float shadowInertia = 0.4f;

        private Camera _cam;
        private Vector3 _shadowTargetPos;
        private Vector3 _shadowCurrentPos;
        private byte _currentLevel;

        private void Awake()
        {
            _cam = Camera.main;
        }

        protected override void OnTriggered()
        {
            if (narrativeManager.CurrentStage != NarrativeManager.NarrativeStage.Prologue)
            {
                shadowRoot.gameObject.SetActive(true);
            }
        }

        protected override void OnEffectUpdate(float dt)
        {
            byte lvl = ComputeLevel();
            if (lvl != _currentLevel)
            {
                _currentLevel = lvl;
                ApplyLevel(lvl);
            }
            if (lvl >= 2)
            {
                _shadowCurrentPos = Vector3.Lerp(_shadowCurrentPos, Anchor.position + Vector3.down * 0.05f, dt / shadowInertia);
                shadowRoot.position = _shadowCurrentPos;
            }
        }

        protected override void OnCancelled()
        {
            shadowRoot.gameObject.SetActive(false);
        }

        private byte ComputeLevel()
        {
            var s = narrativeManager.CurrentStage;
            if (s == NarrativeManager.NarrativeStage.Prologue)
                return 0;
            if (s == NarrativeManager.NarrativeStage.Rising)
                return 1;
            if (s == NarrativeManager.NarrativeStage.Escalation)
                return 2;
            return TimeSinceTriggered > 30f ? (byte)4 : (byte)3;
        }

        private void ApplyLevel(byte lvl)
        {
            shadowRoot.gameObject.SetActive(lvl > 0);
            float op = lvl switch
            {
                1 => 0.2f,
                2 => 0.5f,
                3 => 0.8f,
                _ => 1f
            };
            shadowRenderer.material.SetFloat("_Opacity", op);
            if (lvl >= 3)
            {
                shadowRenderer.material.SetFloat("_DeformAmount", lvl == 4 ? 0.8f : 0.3f);
            }
        }
    }
}
