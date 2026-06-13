using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gate2Reality.Narrative
{
    public class OnDeviceNarrativeGenerator : MonoBehaviour, INarrativeGenerator
    {
        [SerializeField] private float timeoutSeconds = 3f;

        [Header("Fallback Pools (2 per node)")]
        [SerializeField] private string[] fallbackNode0;
        [SerializeField] private string[] fallbackNode1;
        [SerializeField] private string[] fallbackNode2;
        [SerializeField] private string[] fallbackNode3;
        [SerializeField] private string[] fallbackNode4;
        [SerializeField] private string[] fallbackNode5;

        public event Action<string> OnWhisperReady;

        private NarrativeContext _ctx;
        private bool _bridgeAvailable;
        private float _requestTimer;
        private bool _waitingForBridge;
        private int _pendingNodeIndex;
        private int[] _fallbackIndices;

        private void Awake()
        {
            _fallbackIndices = new int[8];
            _bridgeAvailable = TryInitBridge();
        }

        private bool TryInitBridge()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var bridge = new AndroidJavaClass("com.gate2reality.NarrativeLlmBridge");
                return bridge != null;
            }
            catch { return false; }
#else
            return false;
#endif
        }

        public void RequestWhisper(int nodeIndex, Pose anchor)
        {
            if (_bridgeAvailable)
            {
                _pendingNodeIndex = nodeIndex;
                _waitingForBridge = true;
                _requestTimer = 0f;
                SendToBridge(nodeIndex);
                return;
            }
            EmitFallback(nodeIndex);
        }

        public void SetContext(in NarrativeContext ctx) => _ctx = ctx;

        private void Update()
        {
            if (!_waitingForBridge) return;
            _requestTimer += Time.deltaTime;
            if (_requestTimer >= timeoutSeconds)
            {
                _waitingForBridge = false;
                EmitFallback(_pendingNodeIndex);
            }
        }

        // Called from Kotlin via UnitySendMessage
        public void OnBridgeResult(string whisper)
        {
            _waitingForBridge = false;
            OnWhisperReady?.Invoke(whisper);
        }

        private void EmitFallback(int nodeIndex)
        {
            string[] pool = GetPool(nodeIndex);
            if (pool == null || pool.Length == 0) return;

            int idx = _fallbackIndices[nodeIndex % _fallbackIndices.Length] % pool.Length;
            _fallbackIndices[nodeIndex % _fallbackIndices.Length] = (idx + 1) % pool.Length;
            OnWhisperReady?.Invoke(pool[idx]);
        }

        private void SendToBridge(int nodeIndex)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var bridge = new AndroidJavaClass("com.gate2reality.NarrativeLlmBridge");
                string prompt = BuildPrompt(nodeIndex);
                bridge.CallStatic("requestWhisper", prompt, gameObject.name, "OnBridgeResult");
            }
            catch { EmitFallback(nodeIndex); _waitingForBridge = false; }
#endif
        }

        private string BuildPrompt(int nodeIndex)
        {
            return $"node:{nodeIndex} labels:{_ctx.DetectionBitmask} light:{_ctx.AmbientIntensity:F2} room:{_ctx.RoomHeuristic}";
        }

        private string[] GetPool(int nodeIndex) => nodeIndex switch
        {
            0 => fallbackNode0,
            1 => fallbackNode1,
            2 => fallbackNode2,
            3 => fallbackNode3,
            4 => fallbackNode4,
            5 => fallbackNode5,
            _ => null
        };
    }
}
