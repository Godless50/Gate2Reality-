using UnityEngine;

namespace Gate2Reality.ChapterTwo
{
    using Gate2Reality.Effects;

    /// <summary>
    /// «Знакомые объекты ведут себя иначе» (Глава II). Эффект на TriggerableEffectBase:
    /// узел Главы II срабатывает (обычно по AvertedGaze — пока на объект не смотрят),
    /// и привычный предмет «оживает». В отличие от one-shot эффектов Главы I этот
    /// НЕ зовёт MarkFinished — остаётся активным и ведёт поведение в OnEffectUpdate,
    /// пока его не отменят.
    ///
    /// ПОВЕДЕНИЕ (конфигурируемое):
    ///  - watchPlayer  — медленно разворачивается лицом к игроку (предмет «следит»);
    ///  - creepToward  — подкрадывается к игроку, останавливаясь на minDistance;
    ///  - onlyWhenUnobserved — двигается ТОЛЬКО пока игрок не смотрит (классическая
    ///    инверсия наблюдения; смотрит — предмет замирает);
    ///  - distortRamp  — локально поднимает _BreachProgress материала через
    ///    MaterialPropertyBlock (без инстансинга материала — дисциплина zero-GC).
    ///
    /// Глобальный _HorrorScale (HorrorSafetyGovernor) по-прежнему гасит дисторсию
    /// при людях в кадре — обет приватности действует и в изнанке.
    /// </summary>
    public sealed class InvertedObjectEffect : TriggerableEffectBase
    {
        [Header("Игрок")]
        [SerializeField] private Transform arCameraTransform;

        [Header("Поведение")]
        [SerializeField] private bool watchPlayer = true;
        [SerializeField] private float turnSpeedDegPerSec = 35f;
        [SerializeField] private bool creepToward = true;
        [SerializeField] private float creepSpeed = 0.15f;     // м/с
        [SerializeField] private float minDistance = 0.8f;     // не подходить ближе
        [SerializeField] private bool onlyWhenUnobserved = true;
        [Tooltip("Полуугол конуса наблюдения игрока, градусы")]
        [SerializeField] private float observeAngleDeg = 22f;

        [Header("Аудио/визуал")]
        [SerializeField] private AudioSource wrongSideAudio;   // звук «не отсюда»
        [SerializeField] private Renderer distortionRenderer;  // материал RealityDistortion
        [SerializeField] private float distortRampSeconds = 4f;

        private static readonly int BreachProgressId = Shader.PropertyToID("_BreachProgress");
        private MaterialPropertyBlock _mpb;
        private float _distort;

        protected override void OnTriggered()
        {
            _distort = 0f;
            if (wrongSideAudio != null) wrongSideAudio.Play();
            if (distortionRenderer != null)
            {
                _mpb ??= new MaterialPropertyBlock();
                distortionRenderer.GetPropertyBlock(_mpb);
                _mpb.SetFloat(BreachProgressId, 0f);
                distortionRenderer.SetPropertyBlock(_mpb);
            }
            // НЕ вызываем MarkFinished: объект остаётся «живым».
        }

        protected override void OnEffectUpdate(float dt)
        {
            // Дисторсия нарастает всегда (это «порча» предмета, не движение).
            if (distortionRenderer != null && _distort < 1f)
            {
                _distort = Mathf.Clamp01(_distort + dt / Mathf.Max(0.01f, distortRampSeconds));
                _mpb ??= new MaterialPropertyBlock();
                distortionRenderer.GetPropertyBlock(_mpb);
                _mpb.SetFloat(BreachProgressId, _distort);
                distortionRenderer.SetPropertyBlock(_mpb);
            }

            if (arCameraTransform == null) return;

            // Наблюдается ли объект прямо сейчас?
            if (onlyWhenUnobserved && IsObserved()) return; // смотрят — замираем

            // --- Слежение взглядом (только рыскание, без наклона предмета) ---
            if (watchPlayer)
            {
                Vector3 toPlayer = arCameraTransform.position - transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 1e-4f)
                {
                    Quaternion want = Quaternion.LookRotation(toPlayer.normalized);
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation, want, turnSpeedDegPerSec * dt);
                }
            }

            // --- Подкрадывание ---
            if (creepToward)
            {
                Vector3 toPlayer = arCameraTransform.position - transform.position;
                toPlayer.y = 0f;
                float dist = toPlayer.magnitude;
                if (dist > minDistance)
                {
                    transform.position += toPlayer.normalized * (creepSpeed * dt);
                }
            }
        }

        /// <summary>Камера смотрит на объект в пределах конуса наблюдения.</summary>
        private bool IsObserved()
        {
            Vector3 toObj = transform.position - arCameraTransform.position;
            float sqr = toObj.sqrMagnitude;
            if (sqr < 1e-4f) return true; // в упор
            float cos = Vector3.Dot(arCameraTransform.forward, toObj / Mathf.Sqrt(sqr));
            return cos >= Mathf.Cos(observeAngleDeg * Mathf.Deg2Rad);
        }

        protected override void OnCancelled()
        {
            if (wrongSideAudio != null) wrongSideAudio.Stop();
            if (distortionRenderer != null)
            {
                _mpb ??= new MaterialPropertyBlock();
                distortionRenderer.GetPropertyBlock(_mpb);
                _mpb.SetFloat(BreachProgressId, 0f);
                distortionRenderer.SetPropertyBlock(_mpb);
            }
        }
    }
}
