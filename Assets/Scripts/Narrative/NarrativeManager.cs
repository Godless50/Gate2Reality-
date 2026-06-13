using System;
using UnityEngine;

namespace Gate2Reality.Narrative
{
    internal enum GuardStage : byte
    {
        Dormant,
        BeaconFired,
        Desaturated,
        ParticlesFired
    }

    public class NarrativeManager : MonoBehaviour
    {
        [SerializeField] private NarrativeNode[] nodes;
        [SerializeField] private float guardBeaconDelay = 45f;
        [SerializeField] private float guardDesaturateDelay = 60f;
        [SerializeField] private float guardParticlesDelay = 75f;

        public event Action<int, Pose> OnNodeActivated;
        public event Action OnSceneCompleted;
        public event Action<Pose> OnAudioBeaconRequested;
        public event Action OnDesaturateRequested;
        public event Action OnSaturationRestoreRequested;
        public event Action<Pose> OnGuideParticlesRequested;

        private int _currentNodeIndex;
        private bool _sceneActive;
        private float _inactivityTimer;
        private GuardStage _guardStage = GuardStage.Dormant;

        private void Awake()
        {
            if (nodes == null) return;
            for (int i = 0; i < nodes.Length; i++)
                nodes[i].BuildCache();
        }

        public void StartScene()
        {
            _currentNodeIndex = 0;
            _sceneActive = true;
            _inactivityTimer = 0f;
            _guardStage = GuardStage.Dormant;
            EnterNode(_currentNodeIndex);
        }

        private void Update()
        {
            if (!_sceneActive) return;
            TickGuard(Time.deltaTime);
        }

        public void ReportDetection(in DetectionEvent evt)
        {
            if (!_sceneActive || nodes == null || _currentNodeIndex >= nodes.Length) return;

            NarrativeNode node = nodes[_currentNodeIndex];

            if (!node.condition.MatchesDetection(evt)) return;

            node.DwellAccumulator += Time.deltaTime;
            node.LastSeenPose = evt.WorldPose;
            _inactivityTimer = 0f;
            ResetGuard();

            if (node.DwellAccumulator >= node.dwellTimeSeconds)
                ActivateCurrentNode();
        }

        public void SetNodeRuntimeTarget(int nodeIndex, Transform target)
        {
            if (nodes == null || nodeIndex < 0 || nodeIndex >= nodes.Length) return;
            nodes[nodeIndex].condition.runtimeTarget = target;
        }

        public void TickGuard(float dt)
        {
            _inactivityTimer += dt;

            if (_guardStage == GuardStage.Dormant && _inactivityTimer >= guardBeaconDelay)
            {
                _guardStage = GuardStage.BeaconFired;
                if (nodes[_currentNodeIndex].condition.TryGetKnownAnchor(out Pose p))
                    OnAudioBeaconRequested?.Invoke(p);
            }
            else if (_guardStage == GuardStage.BeaconFired && _inactivityTimer >= guardDesaturateDelay)
            {
                _guardStage = GuardStage.Desaturated;
                OnDesaturateRequested?.Invoke();
            }
            else if (_guardStage == GuardStage.Desaturated && _inactivityTimer >= guardParticlesDelay)
            {
                _guardStage = GuardStage.ParticlesFired;
                if (nodes[_currentNodeIndex].condition.TryGetKnownAnchor(out Pose p))
                    OnGuideParticlesRequested?.Invoke(p);
            }
        }

        private void ResetGuard()
        {
            if (_guardStage == GuardStage.Desaturated || _guardStage == GuardStage.ParticlesFired)
                OnSaturationRestoreRequested?.Invoke();
            _guardStage = GuardStage.Dormant;
        }

        private void ActivateCurrentNode()
        {
            NarrativeNode node = nodes[_currentNodeIndex];
            Pose anchor = node.LastSeenPose;

            for (int i = 0; i < node.CachedTriggerables.Length; i++)
                node.CachedTriggerables[i].Trigger(in anchor);

            OnNodeActivated?.Invoke(_currentNodeIndex, anchor);

            if (node.nextNodeIndices == null || node.nextNodeIndices.Length == 0)
            {
                _sceneActive = false;
                OnSceneCompleted?.Invoke();
                return;
            }

            _currentNodeIndex = node.nextNodeIndices[0];
            EnterNode(_currentNodeIndex);
        }

        private void EnterNode(int index)
        {
            if (index < 0 || index >= nodes.Length) return;
            nodes[index].DwellAccumulator = 0f;
            _inactivityTimer = 0f;
            _guardStage = GuardStage.Dormant;
        }
    }
}
