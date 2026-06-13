using UnityEngine;
using Gate2Reality.Narrative;
using Gate2Reality.Detection;
using Gate2Reality.Effects;

namespace Gate2Reality.Scene
{
    public class SceneOneDirector : MonoBehaviour
    {
        [SerializeField] private NarrativeManager narrativeManager;
        [SerializeField] private YoloObjectDetector detector;
        [SerializeField] private DepthPoseProjector projector;
        [SerializeField] private ChairAwakeningEffect chairEffect;
        [SerializeField] private BookMemoryEffect bookEffect;
        [SerializeField] private CupBreachEffect cupEffect;

        [Header("Guard FX")]
        [SerializeField] private AudioSource beaconSource;
        [SerializeField] private ParticleSystem guideParticles;
        [SerializeField] private UnityEngine.Rendering.Volume desaturationVolume;

        [Header("Generative")]
        [SerializeField] private OnDeviceNarrativeGenerator narrativeGenerator;

        private void Awake()
        {
            narrativeManager.OnNodeActivated       += HandleNodeActivated;
            narrativeManager.OnSceneCompleted      += HandleSceneCompleted;
            narrativeManager.OnAudioBeaconRequested += HandleBeacon;
            narrativeManager.OnDesaturateRequested  += HandleDesaturate;
            narrativeManager.OnSaturationRestoreRequested += HandleRestoreSaturation;
            narrativeManager.OnGuideParticlesRequested    += HandleGuideParticles;

            detector.OnRawDetection        += HandleRawDetection;
            detector.OnHumanPresenceChanged += HandleHumanPresence;
        }

        private void Start() => narrativeManager.StartScene();

        private void OnDestroy()
        {
            narrativeManager.OnNodeActivated        -= HandleNodeActivated;
            narrativeManager.OnSceneCompleted       -= HandleSceneCompleted;
            narrativeManager.OnAudioBeaconRequested -= HandleBeacon;
            narrativeManager.OnDesaturateRequested  -= HandleDesaturate;
            narrativeManager.OnSaturationRestoreRequested -= HandleRestoreSaturation;
            narrativeManager.OnGuideParticlesRequested    -= HandleGuideParticles;

            detector.OnRawDetection         -= HandleRawDetection;
            detector.OnHumanPresenceChanged -= HandleHumanPresence;
        }

        public void HandleNodeActivated(int nodeIndex, Pose anchor)
        {
            // Node 2 = Cup: switch to person-only mode
            if (nodeIndex == 2)
                detector.SetPersonOnlyMode(true);

            if (narrativeGenerator != null)
                narrativeGenerator.RequestWhisper(nodeIndex, anchor);
        }

        public void HandleRawDetection(DetectionEvent evt)
        {
            if (!projector.TryProjectToWorld(
                new Vector2(evt.WorldPose.position.x, evt.WorldPose.position.y),
                640f, out Pose worldPose, out float conf)) return;

            var resolved = new DetectionEvent(evt.Label, worldPose, evt.Confidence * conf, evt.BoundsRadius);
            narrativeManager.ReportDetection(in resolved);
        }

        public void HandleBeacon(Pose anchor)
        {
            if (beaconSource == null) return;
            beaconSource.transform.position = anchor.position;
            beaconSource.Play();
        }

        public void HandleDesaturate()
        {
            if (desaturationVolume != null) desaturationVolume.weight = 1f;
        }

        public void HandleRestoreSaturation()
        {
            if (desaturationVolume != null) desaturationVolume.weight = 0f;
        }

        public void HandleGuideParticles(Pose anchor)
        {
            if (guideParticles == null) return;
            guideParticles.transform.position = anchor.position;
            guideParticles.Play();
        }

        public void HandleSceneCompleted()
        {
            detector.enabled = false;
        }

        private void HandleHumanPresence(bool present)
        {
            var governor = GetComponent<HorrorSafetyGovernor>();
            if (governor != null) governor.SetHumanPresent(present);
        }
    }
}
