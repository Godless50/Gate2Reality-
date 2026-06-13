using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Gate2Reality.Detection;

namespace Gate2Reality.Narrative
{
    [RequireComponent(typeof(OnDeviceNarrativeGenerator))]
    public class NarrativeContextCollector : MonoBehaviour
    {
        [SerializeField] private ARCameraManager cameraManager;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private YoloObjectDetector detector;
        [SerializeField] private float updateInterval = 2f;

        private OnDeviceNarrativeGenerator _generator;
        private float _timer;
        private int _detectionBitmask;

        private void Awake()
        {
            _generator = GetComponent<OnDeviceNarrativeGenerator>();
            detector.OnRawDetection += AccumulateDetection;
        }

        private void OnDestroy() => detector.OnRawDetection -= AccumulateDetection;

        private void AccumulateDetection(DetectionEvent evt)
        {
            _detectionBitmask |= 1 << (int)evt.Label;
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < updateInterval) return;
            _timer = 0f;

            float ambientIntensity = 1f;
            if (cameraManager != null &&
                cameraManager.TryGetIntrinsics(out var _) &&
                cameraManager.currentLightEstimation.averageBrightness.HasValue)
            {
                ambientIntensity = cameraManager.currentLightEstimation.averageBrightness.Value;
            }

            int planeCount = 0;
            foreach (var _ in planeManager.trackables) planeCount++;
            int roomHeuristic = planeCount < 3 ? 0 : planeCount < 8 ? 1 : 2;

            var ctx = new NarrativeContext
            {
                DetectionBitmask = _detectionBitmask,
                AmbientIntensity = ambientIntensity,
                RoomHeuristic = roomHeuristic
            };

            _generator.SetContext(in ctx);
            _detectionBitmask = 0;
        }
    }
}
