using UnityEngine;

namespace Gate2Reality.Effects
{
    /// <summary>
    /// Узел 1 «The Chair — The Awakening».
    ///  - Янтарный world-space свет с медленным «вдохом» интенсивности.
    ///  - Драйвер шейдера дисторсии ножек (сам шейдер — Step 4): плавный
    ///    ramp параметра _DistortionStrength через MaterialPropertyBlock
    ///    (ноль инстансов материалов, ноль GC).
    ///  - Виртуальная тень-указатель: quad с текстурой вытянутой тени.
    ///    Пока цель (книга) неизвестна — «сканирует» комнату медленным
    ///    вращением; после вызова SetHintTarget() плавно доворачивается
    ///    на книгу. Цель скармливает SceneOneDirector из сырых YOLO-детекций.
    /// </summary>
    public sealed class ChairAwakeningEffect : TriggerableEffectBase
    {
        [Header("Янтарный свет")]
        [SerializeField] private Light amberLight;            // Point/Spot, URP
        [SerializeField] private float lightRampSeconds = 3f;
        [SerializeField] private float maxIntensity = 2.5f;
        [SerializeField] private float breathAmplitude = 0.35f;
        [SerializeField] private float breathFrequency = 0.4f; // Гц, медленное «дыхание»

        [Header("Дисторсия ножек (шейдер из Step 4)")]
        [SerializeField] private Renderer distortionOverlayRenderer; // оверлей-меш поверх области ножек
        [SerializeField] private float distortionRampSeconds = 4f;
        [SerializeField] private float maxDistortion = 1f;

        [Header("Тень-указатель")]
        [SerializeField] private Transform shadowQuad;        // quad с текстурой тени, pivot у основания
        [SerializeField] private float scanSpeedDegPerSec = 20f;
        [SerializeField] private float aimLerpSpeed = 2f;

        // Кэш ID шейдерных свойств — Shader.PropertyToID при каждом обращении
        // по строке = скрытые аллокации. Кэшируем статически один раз.
        private static readonly int DistortionStrengthId = Shader.PropertyToID("_DistortionStrength");

        private MaterialPropertyBlock _mpb;
        private Vector3 _hintTarget;
        private bool _hasHintTarget;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (amberLight != null) amberLight.enabled = false;
            if (shadowQuad != null) shadowQuad.gameObject.SetActive(false);
        }

        /// <summary>Вызывается SceneOneDirector, когда YOLO впервые увидел книгу.</summary>
        public void SetHintTarget(Vector3 worldPosition)
        {
            _hintTarget = worldPosition;
            _hasHintTarget = true;
        }

        protected override void OnTriggered()
        {
            if (amberLight != null)
            {
                amberLight.enabled = true;
                amberLight.color = new Color(1f, 0.69f, 0.25f); // янтарь
                amberLight.intensity = 0f;
            }
            if (shadowQuad != null) shadowQuad.gameObject.SetActive(true);
        }

        protected override void OnEffectUpdate(float dt)
        {
            float t = TimeSinceTriggered;

            // --- Свет: ramp -> бесконечное дыхание ---
            if (amberLight != null)
            {
                float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / lightRampSeconds));
                float breath = 1f + breathAmplitude *
                               Mathf.Sin(t * breathFrequency * 2f * Mathf.PI);
                amberLight.intensity = maxIntensity * ramp * breath;
            }

            // --- Дисторсия: плавный ramp параметра шейдера ---
            if (distortionOverlayRenderer != null)
            {
                float d = maxDistortion * Mathf.SmoothStep(0f, 1f,
                          Mathf.Clamp01(t / distortionRampSeconds));
                distortionOverlayRenderer.GetPropertyBlock(_mpb);
                _mpb.SetFloat(DistortionStrengthId, d);
                distortionOverlayRenderer.SetPropertyBlock(_mpb);
            }

            // --- Тень: скан-вращение или наведение на книгу ---
            if (shadowQuad != null)
            {
                if (_hasHintTarget)
                {
                    Vector3 dir = _hintTarget - shadowQuad.position;
                    dir.y = 0f; // тень лежит на полу — только yaw
                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        Quaternion want = Quaternion.LookRotation(dir);
                        shadowQuad.rotation = Quaternion.Slerp(
                            shadowQuad.rotation, want, aimLerpSpeed * dt);
                    }
                }
                else
                {
                    // Книга ещё не замечена: медленный тревожный «поиск».
                    shadowQuad.Rotate(0f, scanSpeedDegPerSec * dt, 0f, Space.World);
                }
            }
        }

        protected override void OnCancelled()
        {
            if (amberLight != null) amberLight.enabled = false;
            if (shadowQuad != null) shadowQuad.gameObject.SetActive(false);
        }
    }
}
