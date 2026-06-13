using UnityEngine;

namespace Gate2Reality.Effects
{
    /// <summary>
    /// «Окно в зазеркалье» как нарративный эффект графа: наследует
    /// TriggerableEffectBase, поэтому подключается к узлам Сцены 2 точно так
    /// же, как свет стула — к узлам Сцены 1. Один компонент обслуживает и
    /// малые эхо-окна (диаметр ~0.6м), и финальную дверь (~2.0м) — разница
    /// только в сериализованных параметрах.
    ///
    /// КОНВЕНЦИЯ ОРИЕНТАЦИИ (важно для авторинга префаба интерьера):
    ///   Якорь зоны от EchoZonePlacer смотрит forward'ом В КОМНАТУ
    ///   (нормаль стены к игроку). Значит:
    ///     - quad окна = licом по +Z якоря (к игроку);
    ///     - интерьер инвертированного мира уходит по ЛОКАЛЬНОМУ -Z —
    ///       префаб строится «вглубь» отрицательной оси Z.
    ///
    /// АНИМАЦИЯ АПЕРТУРЫ: easeOutBack — окно раскрывается с лёгким
    /// «перехлопом» (~9% овершут), как лопнувшая мембрана, и оседает
    /// в точный размер. Параметр _Aperture пишется через MPB — без
    /// инстансов материала, дружит с SRP Batcher.
    /// </summary>
    public sealed class PortalWindowEffect : TriggerableEffectBase
    {
        [Header("Окно")]
        [SerializeField] private Renderer windowRenderer;   // quad c PortalWindow
        [SerializeField] private float windowDiameterMeters = 0.6f; // дверь: ~2.0
        [SerializeField] private float openSeconds = 1.6f;
        [Tooltip("Сила овершута easeOutBack. 1.70158 = классические ~9%")]
        [SerializeField] private float overshoot = 1.70158f;

        [Header("Мир за стеной")]
        [Tooltip("Корень интерьера (материалы InvertedWorld). Контент уходит по локальному -Z")]
        [SerializeField] private Transform interiorRoot;

        [Header("Аудио")]
        [SerializeField] private AudioSource openAudio;     // разрыв мембраны
        [SerializeField] private AudioSource interiorAmbience; // гул зазеркалья, loop

        private static readonly int ApertureId = Shader.PropertyToID("_Aperture");

        private MaterialPropertyBlock _mpb;
        private bool _opened;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();

            // До срабатывания узла ни окна, ни мира не существует.
            if (windowRenderer != null)
            {
                SetAperture(0f);
                windowRenderer.gameObject.SetActive(false);
            }
            if (interiorRoot != null) interiorRoot.gameObject.SetActive(false);
        }

        protected override void OnTriggered()
        {
            _opened = false;

            // База уже поставила корень в позу якоря (snapToAnchor):
            // forward — в комнату. Масштабируем quad под диаметр; апертура=1
            // соответствует вписанной окружности, так что quad = диаметру.
            if (windowRenderer != null)
            {
                Transform wt = windowRenderer.transform;
                wt.localScale = new Vector3(windowDiameterMeters, windowDiameterMeters, 1f);
                windowRenderer.gameObject.SetActive(true);
                SetAperture(0f);
            }

            // Интерьер включается сразу: stencil-маска ещё нулевая, так что
            // визуально мир «вливается» строго вслед за раскрытием апертуры.
            if (interiorRoot != null) interiorRoot.gameObject.SetActive(true);

            if (openAudio != null) openAudio.Play();
            if (interiorAmbience != null) interiorAmbience.Play();
        }

        protected override void OnEffectUpdate(float dt)
        {
            if (_opened) return;

            float x = Mathf.Clamp01(TimeSinceTriggered / openSeconds);
            SetAperture(EaseOutBack(x));

            if (x >= 1f)
            {
                SetAperture(1f);
                _opened = true;
                MarkFinished(); // граф свободен; пульс обода живёт в шейдере
            }
        }

        protected override void OnCancelled()
        {
            SetAperture(0f);
            if (windowRenderer != null) windowRenderer.gameObject.SetActive(false);
            if (interiorRoot != null) interiorRoot.gameObject.SetActive(false);
            if (interiorAmbience != null) interiorAmbience.Stop();
        }

        // =====================================================================
        // ВСПОМОГАТЕЛЬНОЕ
        // =====================================================================
        private void SetAperture(float value)
        {
            if (windowRenderer == null) return;
            windowRenderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(ApertureId, Mathf.Max(0f, value));
            windowRenderer.SetPropertyBlock(_mpb);
        }

        /// <summary>easeOutBack: старт из 0, овершут ~9% на x~0.7, финиш ровно в 1.</summary>
        private float EaseOutBack(float x)
        {
            float c1 = overshoot;
            float c3 = c1 + 1f;
            float xm1 = x - 1f;
            return 1f + c3 * xm1 * xm1 * xm1 + c1 * xm1 * xm1;
        }
    }
}
