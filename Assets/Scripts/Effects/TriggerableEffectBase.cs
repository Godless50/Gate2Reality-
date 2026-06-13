using UnityEngine;

namespace Gate2Reality.Effects
{
    using Gate2Reality.Narrative;

    /// <summary>
    /// База для всех нарративных эффектов Сцены 1. Снимает бойлерплейт:
    ///  - реализация ITriggerable (id, IsActive, Cancel);
    ///  - перемещение корня эффекта к позе физического объекта-якоря;
    ///  - защита от повторного срабатывания (нарративные узлы — one-shot).
    ///
    /// Производные классы переопределяют OnTriggered/OnCancelled и ведут
    /// свою анимацию в OnEffectUpdate (вызывается только пока активен —
    /// неактивные эффекты не тратят ни наносекунды CPU).
    /// </summary>
    public abstract class TriggerableEffectBase : MonoBehaviour, ITriggerable
    {
        [Header("ITriggerable")]
        [SerializeField] private string triggerId = "unnamed_effect";

        [Tooltip("Прилипать ли к позе физического объекта при срабатывании")]
        [SerializeField] private bool snapToAnchor = true;

        public string TriggerId => triggerId;
        public bool IsActive { get; private set; }

        protected Pose Anchor { get; private set; }
        protected float TimeSinceTriggered { get; private set; }

        public void Trigger(in Pose worldAnchor)
        {
            if (IsActive) return; // one-shot: повторные детекции игнорируем

            Anchor = worldAnchor;
            if (snapToAnchor)
            {
                transform.SetPositionAndRotation(worldAnchor.position, worldAnchor.rotation);
            }

            TimeSinceTriggered = 0f;
            IsActive = true;
            OnTriggered();
        }

        public void Cancel()
        {
            if (!IsActive) return;
            IsActive = false;
            OnCancelled();
        }

        private void Update()
        {
            if (!IsActive) return;
            TimeSinceTriggered += Time.deltaTime;
            OnEffectUpdate(Time.deltaTime);
        }

        /// <summary>Эффект сообщает, что отыграл до конца (для IsActive-ожиданий графа).</summary>
        protected void MarkFinished() => IsActive = false;

        protected abstract void OnTriggered();
        protected virtual void OnCancelled() { }
        protected virtual void OnEffectUpdate(float dt) { }
    }
}
