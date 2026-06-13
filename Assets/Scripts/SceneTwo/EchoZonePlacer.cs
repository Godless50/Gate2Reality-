using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Gate2Reality.SceneTwo
{
    public enum ZoneKind : byte { WallEcho, SurfaceEcho, PortalWall }

    [Serializable]
    public struct ZoneSlot
    {
        public ZoneKind kind;
        public int nodeIndex;
        public GameObject visualPrefab;
    }

    public class EchoZonePlacer : MonoBehaviour
    {
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private ZoneSlot[] slots;
        [SerializeField] private float minSeparation = 1.5f;
        [SerializeField] private int fallbackRingCount = 8;
        [SerializeField] private float fallbackRingRadius = 2f;

        public event Action<IReadOnlyList<PlacedZone>> OnZonesPlaced;
        public IReadOnlyList<PlacedZone> Zones => _placedZones;

        private readonly List<PlacedZone> _placedZones = new();
        private Candidate[] _candidates;
        private int _candidateCount;

        private void Awake()
        {
            _candidates = new Candidate[64];
        }

        public void PlaceZones()
        {
            _placedZones.Clear();
            GatherCandidates();

            foreach (var slot in slots)
            {
                if (!TrySelect(slot.kind, out var candidate))
                    candidate = FallbackCandidate(_placedZones.Count);

                var anchor = CreateAnchor(in candidate);

                if (slot.visualPrefab != null)
                {
                    var vis = Instantiate(slot.visualPrefab, anchor);
                    var zone = vis.GetComponent<EchoZone>() ?? vis.AddComponent<EchoZone>();
                    var surface = slot.kind == ZoneKind.SurfaceEcho ? EchoSurface.Floor : EchoSurface.Wall;
                    zone.Init(slot.nodeIndex, surface, candidate.Rot * Vector3.forward);
                }

                _placedZones.Add(new PlacedZone(slot.kind, anchor));
            }

            OnZonesPlaced?.Invoke(_placedZones);
        }

        public void GatherCandidates()
        {
            _candidateCount = 0;
            foreach (var plane in planeManager.trackables)
            {
                if (_candidateCount >= _candidates.Length) break;
                float area = plane.size.x * plane.size.y;
                _candidates[_candidateCount++] = new Candidate(plane, plane.center, plane.transform.rotation, area);
            }
        }

        public bool TrySelect(ZoneKind kind, out Candidate result)
        {
            result = default;
            float bestArea = 0f;
            bool found = false;

            for (int i = 0; i < _candidateCount; i++)
            {
                var c = _candidates[i];
                bool isVertical = Vector3.Dot(c.Plane.normal, Vector3.up) < 0.3f;
                bool wantsWall = kind == ZoneKind.WallEcho || kind == ZoneKind.PortalWall;

                if (wantsWall != isVertical) continue;
                if (!IsFarFromPlaced(c.Pos, minSeparation)) continue;
                if (c.Area > bestArea) { bestArea = c.Area; result = c; found = true; }
            }

            return found;
        }

        public bool IsFarFromPlaced(Vector3 pos, float minDist)
        {
            float minSq = minDist * minDist;
            for (int i = 0; i < _placedZones.Count; i++)
                if ((_placedZones[i].Anchor.position - pos).sqrMagnitude < minSq) return false;
            return true;
        }

        public Candidate FallbackCandidate(int index)
        {
            float angle = index * (360f / fallbackRingCount) * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Cos(angle) * fallbackRingRadius, 0f, Mathf.Sin(angle) * fallbackRingRadius);
            pos += Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            return new Candidate(null, pos, Quaternion.identity, 0f);
        }

        public Transform CreateAnchor(in Candidate c)
        {
            var go = new GameObject($"EchoAnchor_{_placedZones.Count}");
            go.transform.SetPositionAndRotation(c.Pos, c.Rot);
            return go.transform;
        }

        internal struct Candidate
        {
            public ARPlane Plane;
            public Vector3 Pos;
            public Quaternion Rot;
            public float Area;

            public Candidate(ARPlane plane, Vector3 pos, Quaternion rot, float area)
            {
                Plane = plane; Pos = pos; Rot = rot; Area = area;
            }
        }
    }

    public readonly struct PlacedZone
    {
        public readonly ZoneKind Kind;
        public readonly Transform Anchor;

        public PlacedZone(ZoneKind kind, Transform anchor)
        {
            Kind = kind; Anchor = anchor;
        }
    }
}
