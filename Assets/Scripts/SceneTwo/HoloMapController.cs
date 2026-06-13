using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Gate2Reality.SceneTwo
{
    public class HoloMapController : MonoBehaviour
    {
        [SerializeField] private Transform mapContentRoot;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private GameObject zonePingPrefab;
        [SerializeField] private GameObject playerDotPrefab;
        [SerializeField] private float mapScale = 0.1f;

        private Transform _playerDot;
        private bool _built;

        public void Build(IReadOnlyList<PlacedZone> zones)
        {
            if (_built) return;
            _built = true;

            foreach (var plane in planeManager.trackables)
                DrawBoundary(plane);

            for (int i = 0; i < zones.Count; i++)
            {
                if (zonePingPrefab == null) continue;
                var ping = Instantiate(zonePingPrefab, mapContentRoot);
                ping.transform.localPosition = WorldToMap(zones[i].Anchor.position);
            }

            if (playerDotPrefab != null)
            {
                var dot = Instantiate(playerDotPrefab, mapContentRoot);
                _playerDot = dot.transform;
            }
        }

        public void DrawBoundary(ARPlane plane)
        {
            if (plane.boundary.Length < 2) return;

            var lineGo = new GameObject("PlaneBoundary");
            lineGo.transform.SetParent(mapContentRoot, false);
            var lr = lineGo.AddComponent<LineRenderer>();
            lr.positionCount = plane.boundary.Length + 1;
            lr.loop = true;
            lr.startWidth = lr.endWidth = 0.002f;

            for (int i = 0; i < plane.boundary.Length; i++)
            {
                Vector3 worldPt = plane.transform.TransformPoint(
                    new Vector3(plane.boundary[i].x, 0f, plane.boundary[i].y));
                lr.SetPosition(i, WorldToMap(worldPt));
            }
            lr.SetPosition(plane.boundary.Length, lr.GetPosition(0));
        }

        private void Update()
        {
            if (!_built || _playerDot == null) return;
            var cam = Camera.main;
            if (cam != null)
                _playerDot.localPosition = WorldToMap(cam.transform.position);
        }

        public void ComputeRoomFit(IReadOnlyList<PlacedZone> zones, out Vector3 center, out float radius)
        {
            center = Vector3.zero;
            if (zones.Count == 0) { radius = 1f; return; }

            for (int i = 0; i < zones.Count; i++) center += zones[i].Anchor.position;
            center /= zones.Count;

            radius = 0f;
            for (int i = 0; i < zones.Count; i++)
                radius = Mathf.Max(radius, Vector3.Distance(center, zones[i].Anchor.position));
            radius = Mathf.Max(radius, 0.5f);
        }

        private Vector3 WorldToMap(Vector3 worldPos)
        {
            return new Vector3(worldPos.x * mapScale, 0f, worldPos.z * mapScale);
        }
    }
}
