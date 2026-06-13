using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Gate2Reality
{
    using Gate2Reality.Detection;
    using Gate2Reality.Persistence;

    /// <summary>
    /// Рантайм-профилировщик устройства. Один раз на старте определяет тир
    /// железа и применяет настройки производительности — игра подстраивается
    /// под телефон сама, без ручных правок под каждую модель.
    ///
    /// ТИРЫ (по GPU + RAM):
    ///   Flagship — Adreno 7xx/8xx, Immortalis, Xclipse (Pixel 9, S26):
    ///              YOLO 5 Гц, Environment Depth = Best, renderScale 1.0
    ///   Mid      — Adreno 6xx, Mali-G7x (HONOR 90 / Snapdragon 7 Gen 1):
    ///              YOLO 3.3 Гц (300мс), Environment Depth = Fastest,
    ///              renderScale 0.9 — на 1.5K-экране Honor 90 неотличимо,
    ///              а это минус ~19% фрагментной нагрузки
    ///   Low      — всё остальное: YOLO 2.5 Гц, renderScale 0.8
    ///
    /// Плюс честная проверка Depth API через дескриптор подсистемы: если
    /// поддержки нет (на Honor 90 ЕСТЬ — устройство в официальном списке
    /// ARCore с пометкой Supports Depth API), окклюзия отключается и в лог
    /// уходит предупреждение о деградации (рука не перекрывает порталы,
    /// DepthPoseProjector работает с уровня 2 фолбэк-цепочки).
    ///
    /// ВАЖНО: Script Execution Order — этот компонент ПЕРВЫМ (до детектора).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeviceTuningProfile : MonoBehaviour
    {
        public enum Tier : byte { Flagship = 0, Mid = 1, Low = 2 }

        [Header("Связи")]
        [SerializeField] private AROcclusionManager occlusionManager;
        [SerializeField] private YoloObjectDetector detector;
        [SerializeField] private OfflineAnchorRelocalizer relocalizer;

        [Header("Частоты YOLO по тирам, мс")]
        [SerializeField] private int flagshipIntervalMs = 200;
        [SerializeField] private int midIntervalMs = 300;
        [SerializeField] private int lowIntervalMs = 400;

        [Header("Окно L2 по тирам, с")]
        [SerializeField] private float flagshipL2Window = 2f;
        [SerializeField] private float midL2Window = 3f;
        [SerializeField] private float lowL2Window = 4f;

        [Header("Render Scale по тирам")]
        [SerializeField] private float midRenderScale = 0.9f;
        [SerializeField] private float lowRenderScale = 0.8f;

        public Tier DetectedTier { get; private set; }

        private void Awake()
        {
            // Якорная анти-троттлинг мера для ЛЮБОГО тира (чек-лист, §11).
            Application.targetFrameRate = 30;

            DetectedTier = DetectTier();
            ApplyTier(DetectedTier);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Gate2Reality] Тир устройства: {DetectedTier} " +
                      $"(GPU: {SystemInfo.graphicsDeviceName}, " +
                      $"RAM: {SystemInfo.systemMemorySize} МБ, " +
                      $"SoC-модель: {SystemInfo.deviceModel})");
#endif
        }

        private IEnumerator Start()
        {
            // Дескриптор окклюзии валиден только после старта подсистемы.
            yield return null;
            yield return new WaitForSeconds(0.5f);
            VerifyDepthSupport();
        }

        // =====================================================================
        // ОПРЕДЕЛЕНИЕ ТИРА
        // =====================================================================
        private static Tier DetectTier()
        {
            string gpu = SystemInfo.graphicsDeviceName ?? string.Empty;
            int ramMb = SystemInfo.systemMemorySize;

            // Флагманские GPU 2023+. Adreno 7xx/8xx, ARM Immortalis, Samsung Xclipse.
            if (Contains(gpu, "Adreno (TM) 7") || Contains(gpu, "Adreno (TM) 8") ||
                Contains(gpu, "Immortalis") || Contains(gpu, "Xclipse"))
            {
                return Tier.Flagship;
            }

            // Средний класс: Adreno 6xx (644 = Honor 90 / SD 7 Gen 1),
            // Mali-G7x. RAM >= 8ГБ страхует от ложного Mid на старье.
            if ((Contains(gpu, "Adreno (TM) 6") || Contains(gpu, "Mali-G7")) && ramMb >= 7000)
            {
                return Tier.Mid;
            }

            return Tier.Low;
        }

        private static bool Contains(string s, string sub) =>
            s.IndexOf(sub, System.StringComparison.OrdinalIgnoreCase) >= 0;

        // =====================================================================
        // ПРИМЕНЕНИЕ ПРОФИЛЯ
        // =====================================================================
        private void ApplyTier(Tier tier)
        {
            // 1) Частота YOLO
            if (detector != null)
            {
                detector.SetInferenceInterval(tier switch
                {
                    Tier.Flagship => flagshipIntervalMs,
                    Tier.Mid => midIntervalMs,
                    _ => lowIntervalMs
                });
            }

            // 2) Режим Environment Depth: Best на флагманах, Fastest ниже —
            //    на Adreno 644 'Best' съедал бы заметную долю кадра.
            if (occlusionManager != null)
            {
                occlusionManager.requestedEnvironmentDepthMode = tier == Tier.Flagship
                    ? EnvironmentDepthMode.Best
                    : EnvironmentDepthMode.Fastest;
            }

            // 3) Render Scale URP
            if (tier != Tier.Flagship &&
                GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = tier == Tier.Mid ? midRenderScale : lowRenderScale;
            }

            // 4) L2 relocalization window — slower YOLO → wider detection window needed
            if (relocalizer != null)
            {
                relocalizer.SetL2Window(tier switch
                {
                    Tier.Flagship => flagshipL2Window,
                    Tier.Mid => midL2Window,
                    _ => lowL2Window
                });
            }
        }

        // =====================================================================
        // ПРОВЕРКА DEPTH API (честная, через дескриптор подсистемы)
        // =====================================================================
        private void VerifyDepthSupport()
        {
            if (occlusionManager == null) return;

            var descriptor = occlusionManager.descriptor;
            bool supported = descriptor != null &&
                descriptor.environmentDepthImageSupported == Supported.Supported;

            if (!supported)
            {
                // Грациозная деградация: окклюзию выключаем, проекция YOLO
                // работает с уровня 2 фолбэк-цепочки (плоскости), рука не
                // перекрывает порталы. Глава ИГРАБЕЛЬНА, но беднее тактильно.
                occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
                Debug.LogWarning("[Gate2Reality] Depth API НЕ поддерживается: " +
                                 "окклюзия выключена, fallback-проекция уровня 2+.");
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            else
            {
                Debug.Log("[Gate2Reality] Depth API: поддерживается " +
                          $"(режим: {occlusionManager.requestedEnvironmentDepthMode}).");
            }
#endif
        }
    }
}
