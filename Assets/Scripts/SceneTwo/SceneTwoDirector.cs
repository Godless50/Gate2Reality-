using System.Collections.Generic;
using UnityEngine;
using Gate2Reality.Narrative;

namespace Gate2Reality.SceneTwo
{
    public class SceneTwoDirector : MonoBehaviour
    {
        [SerializeField] private NarrativeManager narrativeManager;
        [SerializeField] private EchoZonePlacer zonePlacer;
        [SerializeField] private HoloMapController holoMap;
        [SerializeField] private OnDeviceNarrativeGenerator narrativeGenerator;
        [SerializeField] private Transform crossingAnchor;

        private void Awake()
        {
            zonePlacer.OnZonesPlaced        += HandleZonesPlaced;
            narrativeManager.OnNodeActivated += HandleNodeActivated;
            narrativeManager.OnSceneCompleted += HandleSceneCompleted;
        }

        private void Start()
        {
            // Node 6 (Crossing) uses this transform as runtime target
            narrativeManager.SetNodeRuntimeTarget(6, crossingAnchor != null ? crossingAnchor : transform);
            narrativeManager.StartScene();
            zonePlacer.PlaceZones();
        }

        private void OnDestroy()
        {
            zonePlacer.OnZonesPlaced         -= HandleZonesPlaced;
            narrativeManager.OnNodeActivated  -= HandleNodeActivated;
            narrativeManager.OnSceneCompleted -= HandleSceneCompleted;
        }

        private void HandleZonesPlaced(IReadOnlyList<PlacedZone> zones)
        {
            holoMap.Build(zones);
        }

        private void HandleNodeActivated(int nodeIndex, Pose anchor)
        {
            // Prefetch next whisper on echo nodes
            if (nodeIndex >= 3 && nodeIndex <= 5 && narrativeGenerator != null)
                narrativeGenerator.RequestWhisper(nodeIndex, anchor);
        }

        private void HandleSceneCompleted()
        {
            // CrossingTransitionEffect handles the actual transition
        }

        public void SetCrossingAnchor(Transform anchor)
        {
            crossingAnchor = anchor;
            narrativeManager.SetNodeRuntimeTarget(6, anchor);
        }
    }
}
