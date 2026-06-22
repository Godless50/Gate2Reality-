using UnityEngine;

namespace Gate2Reality.Narrative
{
    /// <summary>Тип условия срабатывания нарративного узла.</summary>
    public enum ConditionType : byte
    {
        /// <summary>Сцена 1: YOLO-детекция физического объекта (chair/book/cup).</summary>
        SemanticDetection = 0,
        /// <summary>Сцена 2: игрок физически подошёл к точке (одометрия XR Origin,
        /// БЕЗ геолокации — чистый ARCore-трекинг).</summary>
        Proximity = 1,
        /// <summary>Сцена 2: игрок смотрит на точку (конус взгляда камеры).</summary>
        Gaze = 2,
        /// <summary>Глава II «По ту сторону»: ПЕРЕВЁРНУТЫЙ взгляд — узел срабатывает,
        /// когда якорь рядом, но игрок смотрит В СТОРОНУ (объект «живёт», пока за
        /// ним не наблюдают). Тематическая инверсия Gaze: те же математика и поля,
        /// обратный предикат.</summary>
        AvertedGaze = 3,
        RitualEncirclement = 4,
        RitualStillness    = 5,
        RitualAlignment    = 6
    }

    /// <summary>
    /// Условие узла графа. Сознательно НЕ полиморфизм через [SerializeReference]:
    /// плоский класс с enum-переключателем — дружелюбнее к инспектору, к
    /// сериализации и к zero-GC оценке (никаких виртуальных вызовов в Update).
    ///
    /// Semantic-условия питаются извне через NarrativeManager.ReportDetection.
    /// Пространственные (Proximity/Gaze) оцениваются менеджером каждый кадр —
    /// это дешёвая векторная математика без рейкастов.
    ///
    /// runtimeTarget для пространственных условий обычно НЕ задаётся в редакторе:
    /// эхо-зоны Сцены 2 размещаются процедурно (EchoZonePlacer, Step 2) и
    /// привязываются через NarrativeManager.SetNodeRuntimeTarget().
    /// </summary>
    [System.Serializable]
    public sealed class NarrativeCondition
    {
        public ConditionType type = ConditionType.SemanticDetection;

        [Header("SemanticDetection")]
        [Tooltip("Какой физический объект должен найти YOLO-детектор")]
        public NarrativeLabel requiredLabel;
        [Tooltip("Минимальный confidence YOLO (ТЗ Сцены 1: > 0.85)")]
        [Range(0f, 1f)] public float minConfidence = 0.85f;
        [Tooltip("Макс. радиус объекта, м (стул: < 1.5). 0 = не проверять")]
        public float maxBoundsRadius = 1.5f;

        [Header("Proximity / Gaze")]
        [Tooltip("Якорь условия. Для процедурных зон ставится в рантайме")]
        public Transform runtimeTarget;

        [Header("Ritual")]
        [Tooltip("Degrees player must orbit the object (Encirclement)")]
        public float encirclementDegrees = 270f;
        [Tooltip("Seconds camera must stay still (Stillness)")]
        public float stillnessSeconds = 8f;
        [Tooltip("Required second group for Alignment (0=any)")]
        public ObjectGroup alignmentTargetGroup;

        [System.NonSerialized] public float RitualProgress;
        [System.NonSerialized] public bool RitualCompleted;

        public void ResetRitual()
        {
            RitualProgress = 0f;
            RitualCompleted = false;
        }
        [Tooltip("Proximity: радиус срабатывания, м")]
        public float triggerRadius = 1.2f;
        [Tooltip("Gaze: полуугол конуса взгляда, градусы")]
        public float maxGazeAngleDeg = 12f;
        [Tooltip("Gaze: дальше этого расстояния взгляд не считается, м")]
        public float maxGazeDistance = 6f;

        /// <summary>Проверка YOLO-детекции (только для SemanticDetection).</summary>
        public bool MatchesDetection(in DetectionEvent evt)
        {
            if (type != ConditionType.SemanticDetection) return false;
            if (evt.Label != requiredLabel) return false;
            if (evt.Confidence < minConfidence) return false;
            if (maxBoundsRadius > 0f && evt.BoundsRadius > maxBoundsRadius) return false;
            return true;
        }

        /// <summary>
        /// Покадровая оценка пространственных условий. Чистая арифметика:
        /// квадраты расстояний (без корня, где можно) и один Dot для угла.
        /// </summary>
        public bool EvaluateSpatial(Transform player, out Pose anchor)
        {
            anchor = default;
            if (runtimeTarget == null || player == null) return false;

            Vector3 targetPos = runtimeTarget.position;
            Vector3 toTarget = targetPos - player.position;

            switch (type)
            {
                case ConditionType.Proximity:
                {
                    // Сравниваем квадраты — корень не нужен.
                    if (toTarget.sqrMagnitude > triggerRadius * triggerRadius) return false;
                    anchor = new Pose(targetPos, runtimeTarget.rotation);
                    return true;
                }
                case ConditionType.Gaze:
                {
                    float sqrDist = toTarget.sqrMagnitude;
                    if (sqrDist > maxGazeDistance * maxGazeDistance) return false;
                    if (sqrDist < 1e-6f) return true; // стоим в точке — засчитано

                    // cos сравнение вместо Angle(): без acos, дешевле.
                    float cosAngle = Vector3.Dot(player.forward,
                                                 toTarget / Mathf.Sqrt(sqrDist));
                    if (cosAngle < Mathf.Cos(maxGazeAngleDeg * Mathf.Deg2Rad)) return false;

                    anchor = new Pose(targetPos, runtimeTarget.rotation);
                    return true;
                }
                case ConditionType.AvertedGaze:
                {
                    // Инверсия Gaze: якорь близко, но игрок НЕ смотрит на него.
                    float sqrDist = toTarget.sqrMagnitude;
                    if (sqrDist > maxGazeDistance * maxGazeDistance) return false;
                    if (sqrDist < 1e-6f) return false; // стоим на нём — наблюдаем в упор

                    float cosAngle = Vector3.Dot(player.forward,
                                                 toTarget / Mathf.Sqrt(sqrDist));
                    // Смотрит ПРЯМО на якорь -> ещё наблюдает, объект замер.
                    if (cosAngle >= Mathf.Cos(maxGazeAngleDeg * Mathf.Deg2Rad)) return false;

                    anchor = new Pose(targetPos, runtimeTarget.rotation);
                    return true;
                }
                default:
                    return false; // Semantic кормится через ReportDetection
            }
        }

        /// <summary>Известен ли якорь заранее (для прайминга guard-маяка).</summary>
        public bool TryGetKnownAnchor(out Pose anchor)
        {
            if (runtimeTarget != null)
            {
                anchor = new Pose(runtimeTarget.position, runtimeTarget.rotation);
                return true;
            }
            anchor = default;
            return false;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public string Describe() => type switch
        {
            ConditionType.SemanticDetection => $"YOLO:{requiredLabel} conf>{minConfidence}",
            ConditionType.Proximity => $"Proximity r={triggerRadius}m -> {(runtimeTarget ? runtimeTarget.name : "<runtime>")}",
            ConditionType.Gaze => $"Gaze {maxGazeAngleDeg}deg -> {(runtimeTarget ? runtimeTarget.name : "<runtime>")}",
            ConditionType.AvertedGaze => $"AvertedGaze {maxGazeAngleDeg}deg @<{maxGazeDistance}m -> {(runtimeTarget ? runtimeTarget.name : "<runtime>")}",
            _ => "?"
        };
#endif
    }
}
