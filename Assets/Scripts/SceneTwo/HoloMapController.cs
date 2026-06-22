using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#endif

namespace Gate2Reality.SceneTwo
{
    /// <summary>
    /// Превращает декоративную голограмму из финала Сцены 1 в игровую карту:
    /// контур комнаты (границы плоскостей ARCore) + пульсирующие метки
    /// эхо-зон + бегущая точка игрока. Вешается на holographicMapRoot
    /// из CupBreachEffect, строится по событию OnZonesPlaced.
    ///
    /// ПЕРФОРМАНС:
    ///  - Вся геометрия (LineRenderer'ы контуров, метки) строится ОДИН раз;
    ///    аллокации только в момент построения.
    ///  - В Update — позиция точки игрока + sin-пульс меток: копейки.
    ///  - Top-down проекция: Y мира схлопывается в тонкие слои карты —
    ///    читаемость важнее объёма.
    /// </summary>
#if UNITY_ANDROID && !UNITY_EDITOR
    [DisallowMultipleComponent]
    public sealed class HoloMapController : MonoBehaviour
    {
        [Header("Связи")]
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private EchoZonePlacer zonePlacer;
        [SerializeField] private Transform playerCamera;
        [Tooltip("Корень контента карты — дочерний объект holographicMapRoot")]
        [SerializeField] private Transform mapContentRoot;

        [Header("Вид")]
        [Tooltip("Радиус карты в метрах (комната вписывается целиком)")]
        [SerializeField] private float mapRadius = 0.22f;
        [SerializeField] private Material holoLineMaterial; // URP/Unlit, additive
        [SerializeField] private Color outlineColor = new Color(0.3f, 0.8f, 1f, 0.6f);
        [SerializeField] private Color zoneColor = new Color(0.3f, 0.9f, 1f, 1f);
        [SerializeField] private Color portalColor = new Color(0.9f, 0.4f, 1f, 1f);
        [SerializeField] private Color playerColor = new Color(1f, 0.8f, 0.3f, 1f);
        [SerializeField] private float lineWidth = 0.003f;
        [SerializeField] private float zonePulseHz = 1.1f;

        private Vector3 _roomCenter;
        private float _scale = 1f;
        private bool _built;

        private Transform _playerBlip;
        private readonly List<Transform> _zonePips = new List<Transform>(3);
        private readonly List<Vector3> _pipBaseScales = new List<Vector3>(3);

        private void OnEnable() => zonePlacer.OnZonesPlaced += Build;
        private void OnDisable() => zonePlacer.OnZonesPlaced -= Build;

        // =====================================================================
        // ПОСТРОЕНИЕ (один раз)
        // =====================================================================
        private void Build(IReadOnlyList<EchoZonePlacer.PlacedZone> zones)
        {
            if (_built) return;
            _built = true;

            ComputeRoomFit(zones);

            // --- Контуры плоскостей ---
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.trackingState != TrackingState.Tracking) continue;
                if (plane.subsumedBy != null) continue;
                DrawBoundary(plane);
            }

            // --- Метки зон ---
            for (int i = 0; i < zones.Count; i++)
            {
                bool isPortal = zones[i].Kind == EchoZonePlacer.ZoneKind.PortalWall;
                Transform pip = SpawnDot(
                    isPortal ? portalColor : zoneColor,
                    isPortal ? 0.020f : 0.012f);
                pip.localPosition = ToMap(zones[i].Anchor.position, layer: 0.012f);
                _zonePips.Add(pip);
                _pipBaseScales.Add(pip.localScale);
            }

            // --- Точка игрока ---
            _playerBlip = SpawnDot(playerColor, 0.010f);
        }

        /// <summary>Вписываем комнату в радиус карты: центр и масштаб по XZ-границам.</summary>
        private void ComputeRoomFit(IReadOnlyList<EchoZonePlacer.PlacedZone> zones)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            void Encapsulate(Vector3 p)
            {
                min = Vector2.Min(min, new Vector2(p.x, p.z));
                max = Vector2.Max(max, new Vector2(p.x, p.z));
            }

            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.trackingState != TrackingState.Tracking || plane.subsumedBy != null) continue;
                Encapsulate(plane.center - plane.transform.right * plane.size.x * 0.5f);
                Encapsulate(plane.center + plane.transform.right * plane.size.x * 0.5f);
            }
            for (int i = 0; i < zones.Count; i++) Encapsulate(zones[i].Anchor.position);
            Encapsulate(playerCamera.position);

            Vector2 c = (min + max) * 0.5f;
            _roomCenter = new Vector3(c.x, 0f, c.y);
            float extent = Mathf.Max((max - min).x, (max - min).y) * 0.5f;
            _scale = extent > 0.01f ? mapRadius / extent : 1f;
        }

        /// <summary>Мир -> локальные координаты карты (top-down, тонкие слои по Y).</summary>
        private Vector3 ToMap(Vector3 world, float layer)
        {
            Vector3 flat = (world - _roomCenter) * _scale;
            return new Vector3(flat.x, layer, flat.z);
        }

        private void DrawBoundary(ARPlane plane)
        {
            NativeArray<Vector2> boundary = plane.boundary;
            if (boundary.Length < 3) return;

            var go = new GameObject("MapOutline");
            go.transform.SetParent(mapContentRoot, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.widthMultiplier = lineWidth;
            lr.material = holoLineMaterial;
            lr.startColor = lr.endColor = outlineColor;
            lr.positionCount = boundary.Length;

            // boundary — в локальном XZ плоскости; через TransformPoint в мир,
            // затем в координаты карты. Стены при top-down проекции честно
            // схлопываются в отрезки — на карте это читается как «стена».
            for (int i = 0; i < boundary.Length; i++)
            {
                Vector3 world = plane.transform.TransformPoint(
                    new Vector3(boundary[i].x, 0f, boundary[i].y));
                lr.SetPosition(i, ToMap(world, layer: 0.006f));
            }
        }

        private Transform SpawnDot(Color color, float diameter)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>()); // физика в проекте мертва
            go.transform.SetParent(mapContentRoot, false);
            go.transform.localScale = Vector3.one * diameter;

            var r = go.GetComponent<Renderer>();
            r.material = holoLineMaterial;       // инстанс: цвет у каждого свой
            r.material.color = color;
            return go.transform;
        }

        // =====================================================================
        // АНИМАЦИЯ (только когда карта построена и видима)
        // =====================================================================
        private void Update()
        {
            if (!_built || !mapContentRoot.gameObject.activeInHierarchy) return;

            _playerBlip.localPosition = ToMap(playerCamera.position, layer: 0.018f);

            float pulse = 1f + 0.25f * Mathf.Sin(Time.time * zonePulseHz * 2f * Mathf.PI);
            for (int i = 0; i < _zonePips.Count; i++)
            {
                _zonePips[i].localScale = _pipBaseScales[i] * pulse;
            }
        }
    }
#endif
}