using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#endif

namespace Gate2Reality.Detection
{
    /// <summary>
    /// Проекция 2D-детекции YOLO в 3D-позу мира. Реализует Fallback-механизм из ТЗ:
    ///
    ///   Уровень 1: Raycast против ARCore Depth API (TrackableType.Depth) —
    ///              самый точный: попадает прямо в поверхность физического объекта.
    ///   Уровень 2: Raycast против обнаруженных плоскостей (объект стоит на
    ///              столе/полу — точка рядом с реальной).
    ///   Уровень 3: «Approximation marker» — точка на луче на дистанции,
    ///              оценённой из углового размера бокса. Грубо, но достаточно,
    ///              чтобы guard-маяк и партиклы указывали в правильную сторону.
    ///
    /// Возвращает также boundsRadius — оценку физического радиуса объекта
    /// (нужна NarrativeManager: правило «стул < 1.5 м»).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DepthPoseProjector : MonoBehaviour
    {
        [Header("Связи")]
        [SerializeField] private Camera arCamera;                 // камера из XR Origin
#if UNITY_ANDROID && !UNITY_EDITOR
        [SerializeField] private ARRaycastManager raycastManager;
#endif

        [Header("Fallback уровня 3")]
        [Tooltip("Типичный физический размер целевых объектов, м (медиана chair/book/cup). Используется для оценки дистанции по угловому размеру бокса.")]
        [SerializeField] private float assumedObjectSize = 0.5f;

        [Tooltip("Ограничение дистанции аппроксимации, м")]
        [SerializeField] private float maxApproxDistance = 5f;

        // Преаллоцированный список хитов — ARRaycastManager пишет в него без GC.
#if UNITY_ANDROID && !UNITY_EDITOR
        private static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>(8);
#endif

        /// <summary>
        /// viewportPoint: центр бокса в координатах viewport (0..1).
        /// bboxViewportWidth: ширина бокса в долях ширины кадра (для оценки радиуса).
        /// </summary>
        public bool TryProjectToWorld(Vector2 viewportPoint, float bboxViewportWidth,
                                      out Pose worldPose, out float boundsRadius)
        {
            Vector2 screenPoint = new Vector2(
                viewportPoint.x * Screen.width,
                viewportPoint.y * Screen.height);

#if UNITY_ANDROID && !UNITY_EDITOR
            // ---------- Уровень 1: Depth API ----------
            if (raycastManager.Raycast(screenPoint, s_Hits, TrackableType.Depth))
            {
                worldPose = s_Hits[0].pose;
                boundsRadius = EstimateRadius(bboxViewportWidth, worldPose.position);
                return true;
            }

            // ---------- Уровень 2: плоскости ----------
            if (raycastManager.Raycast(screenPoint, s_Hits,
                    TrackableType.PlaneWithinPolygon | TrackableType.PlaneEstimated))
            {
                worldPose = s_Hits[0].pose;
                boundsRadius = EstimateRadius(bboxViewportWidth, worldPose.position);
                return true;
            }
#endif

            // ---------- Уровень 3: аппроксимационный маркер ----------
            // Дистанция из углового размера: d ≈ size / (2 * tan(angularHalfWidth)).
            Ray ray = arCamera.ViewportPointToRay(viewportPoint);
            float horizontalFovRad = arCamera.fieldOfView * Mathf.Deg2Rad * arCamera.aspect;
            float angularWidth = Mathf.Max(0.01f, bboxViewportWidth * horizontalFovRad);
            float distance = Mathf.Min(maxApproxDistance,
                assumedObjectSize / (2f * Mathf.Tan(angularWidth * 0.5f)));

            Vector3 pos = ray.origin + ray.direction * distance;
            // Ориентация «лицом к игроку» — для билбордов-маркеров и маяка.
            worldPose = new Pose(pos, Quaternion.LookRotation(-ray.direction));
            boundsRadius = EstimateRadius(bboxViewportWidth, pos);
            return true; // уровень 3 не может «не сработать» — всегда даём оценку
        }

        /// <summary>
        /// Физический радиус из углового размера и известной дистанции:
        /// r ≈ d * tan(angularHalfWidth). Тригонометрия по месту, без кэшей —
        /// вызывается максимум пару десятков раз в секунду.
        /// </summary>
        private float EstimateRadius(float bboxViewportWidth, Vector3 worldPos)
        {
            float distance = Vector3.Distance(arCamera.transform.position, worldPos);
            float horizontalFovRad = arCamera.fieldOfView * Mathf.Deg2Rad * arCamera.aspect;
            float angularHalfWidth = bboxViewportWidth * horizontalFovRad * 0.5f;
            return distance * Mathf.Tan(angularHalfWidth);
        }
    }
}
