using UnityEngine;

namespace Gate2Reality.Effects
{
    /// <summary>
    /// Эхо-зона на горизонтальной поверхности (пол/стол): из точки расходятся
    /// светящиеся круги — «зазеркалье просачивается снизу», как капли по воде,
    /// только наоборот: изнутри наружу.
    ///
    /// ПЕРЕИСПОЛЬЗОВАНИЕ: кольцо — это шейдер Gate2Reality/PortalRim на
    /// горизонтальном quad'е. Пилообразная анимация _Aperture (0 -> 1 -> сброс)
    /// даёт бесконечную рябь БЕЗ нового шейдера и без партиклов на филлрейте.
    /// Quad ориентируется в префабе (повёрнут на +90° по X относительно якоря —
    /// якорь поверхностной зоны смотрит forward'ом на игрока горизонтально).
    ///
    /// Дополнительно: столб «инвертированной пыли» (ParticleSystem, редкая
    /// эмиссия вверх) и низкий гул из-под поверхности.
    /// </summary>
    public sealed class EchoSurfaceEffect : TriggerableEffectBase
    {
        [Header("Рябь (quad c материалом PortalRim)")]
        [SerializeField] private Renderer rippleRenderer;
        [SerializeField] private float rippleDiameterMeters = 1.2f;
        [Tooltip("Период одного круга, сек")]
        [SerializeField] private float ripplePeriodSeconds = 2.2f;
        [Tooltip("Сколько кругов в интро (потом рябь продолжает жить тише)")]
        [SerializeField] private int introRippleCount = 3;
        [Tooltip("Множитель скорости ряби после интро")]
        [SerializeField] private float calmSpeedFactor = 0.5f;

        [Header("Атмосфера")]
        [SerializeField] private ParticleSystem dustColumn;
        [SerializeField] private AudioSource underworldHum; // loop, низкий гул

        private static readonly int ApertureId = Shader.PropertyToID("_Aperture");

        private MaterialPropertyBlock _mpb;
        private float _ripplePhase; // 0..1, пилообразная

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (rippleRenderer != null) rippleRenderer.gameObject.SetActive(false);
        }

        protected override void OnTriggered()
        {
            _ripplePhase = 0f;

            if (rippleRenderer != null)
            {
                Transform rt = rippleRenderer.transform;
                rt.localScale = new Vector3(rippleDiameterMeters, rippleDiameterMeters, 1f);
                rippleRenderer.gameObject.SetActive(true);
            }
            if (dustColumn != null) dustColumn.Play();
            if (underworldHum != null) underworldHum.Play();
        }

        protected override void OnEffectUpdate(float dt)
        {
            if (rippleRenderer == null) return;

            // Интро — полная скорость, дальше зона «успокаивается», но живёт
            // до конца главы (не зовём MarkFinished — стоимость копеечная).
            float introDuration = introRippleCount * ripplePeriodSeconds;
            float speed = TimeSinceTriggered < introDuration ? 1f : calmSpeedFactor;

            _ripplePhase += (dt / ripplePeriodSeconds) * speed;
            if (_ripplePhase >= 1f) _ripplePhase -= 1f; // пила: новый круг из центра

            rippleRenderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(ApertureId, _ripplePhase);
            rippleRenderer.SetPropertyBlock(_mpb);
        }

        protected override void OnCancelled()
        {
            if (rippleRenderer != null) rippleRenderer.gameObject.SetActive(false);
            if (dustColumn != null) dustColumn.Stop();
            if (underworldHum != null) underworldHum.Stop();
        }
    }
}
