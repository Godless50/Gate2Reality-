using UnityEngine;
using Gate2Reality.Detection;

namespace Gate2Reality.Safety
{
    public enum DeviceTier { Flagship, Mid, Low }

    public class DeviceTuningProfile : MonoBehaviour
    {
        [SerializeField] private YoloObjectDetector detector;
        [SerializeField] private Camera arCamera;

        [Header("Flagship (≥12 TOPS)")]
        [SerializeField] private int flagshipInferenceHz = 5;
        [SerializeField] private float flagshipRenderScale = 1.0f;

        [Header("Mid (HONOR 90 / Snapdragon 7 Gen 1)")]
        [SerializeField] private int midInferenceIntervalMs = 300;
        [SerializeField] private float midRenderScale = 0.9f;

        [Header("Low")]
        [SerializeField] private int lowInferenceIntervalMs = 500;
        [SerializeField] private float lowRenderScale = 0.75f;

        private void Awake()
        {
            // Script Execution Order: -100 ensures this runs before other scripts
            ApplyTier(DetectTier());
        }

        private DeviceTier DetectTier()
        {
            int processorFreq = SystemInfo.processorFrequency;
            int gpuMemory = SystemInfo.graphicsMemorySize;

            // HONOR 90 (Snapdragon 7 Gen 1) heuristic
            if (processorFreq < 3000 && gpuMemory < 4096) return DeviceTier.Mid;
            if (processorFreq < 2000 || gpuMemory < 2048) return DeviceTier.Low;
            return DeviceTier.Flagship;
        }

        private void ApplyTier(DeviceTier tier)
        {
            var urpAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;

            switch (tier)
            {
                case DeviceTier.Flagship:
                    detector?.SetInferenceInterval(flagshipInferenceHz);
                    if (urpAsset != null) urpAsset.renderScale = flagshipRenderScale;
                    break;

                case DeviceTier.Mid:
                    detector?.SetInferenceInterval(Mathf.RoundToInt(1000f / midInferenceIntervalMs));
                    if (urpAsset != null) urpAsset.renderScale = midRenderScale;
                    break;

                case DeviceTier.Low:
                    detector?.SetInferenceInterval(Mathf.RoundToInt(1000f / lowInferenceIntervalMs));
                    if (urpAsset != null) urpAsset.renderScale = lowRenderScale;
                    break;
            }

            Application.targetFrameRate = 30;
        }
    }
}
